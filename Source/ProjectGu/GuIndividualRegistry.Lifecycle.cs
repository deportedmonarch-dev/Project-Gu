using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Phase 2 lifecycle (owner decisions §1–§5): explicit terminal operations with idempotent event keys, deceased-host
    /// extraction, recovery from abstract world locations, physical Gu that survive their holder, world-pawn retention for
    /// Gu custody, and compaction of ended records.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        /// <summary>One in-game year (owner decisions §5).</summary>
        internal const int RetentionTicks = GenDate.TicksPerYear;
        internal const int CompactionInterval = GenDate.TicksPerDay;
        internal const int CompactionBudget = 512;

        private readonly Dictionary<string, GuRecord> byEndEvent = new(StringComparer.Ordinal);
        private readonly Dictionary<Pawn, (Map Map, IntVec3 Cell)> corpseDrops = new();
        private readonly HashSet<Pawn> criticalPawns = new();
        private int criticalCacheTick = -1;
        private int compactionCursor;

        private void RebuildEndEventBindings(IEnumerable<GuRecord> source)
        {
            foreach (var claims in source.Where(record => !string.IsNullOrWhiteSpace(record.EndEventKey))
                .GroupBy(record => record.EndEventKey, StringComparer.Ordinal))
            {
                var claimants = claims.ToArray();
                byEndEvent[claims.Key] = claimants.Length == 1 ? claimants[0] : null;
                if (claimants.Length > 1)
                    foreach (var record in claimants) record.Quarantine("Multiple individuals claim this terminal event.");
            }
        }

        private void ApplyTerminalEffect(GuRecord record, GuTerminalOutcome outcome, GuLocation location)
        {
            if (record.Species?.terminalEffectWorker is not { } type) return;
            try
            {
                Map map = location?.MapId == null ? null : Find.Maps.FirstOrDefault(candidate => candidate.GetUniqueLoadID() == location.MapId);
                ((GuTerminalEffectWorker)Activator.CreateInstance(type)).Apply(record, outcome, location, map);
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The terminal effect of Gu {record.Id} failed. The outcome stands. {exception}");
            }
        }

        // ---- Explicit terminal operations ---------------------------------------------------------------------

        internal static bool IsExplicitOutcome(GuTerminalOutcome outcome) =>
            outcome is GuTerminalOutcome.DeliberatelyDestroyed or GuTerminalOutcome.Consumed or GuTerminalOutcome.Sacrificed;

        /// <summary>
        /// Ends an individual deliberately: destruction by a cultivator, consumption or sacrifice. The event key is
        /// idempotent: replaying it returns the original result and never ends a second individual. The record is ended
        /// and unbound before its native object is retired, so the retirement is not reported as physical destruction.
        /// </summary>
        internal bool TryEndLife(string id, int expectedRevision, GuTerminalOutcome outcome, string eventKey, Pawn actor, string cause,
            out string reason)
        {
            reason = "An explicit lifecycle operation needs an event key and a deliberate outcome.";
            if (string.IsNullOrWhiteSpace(eventKey) || !IsExplicitOutcome(outcome)) return false;
            if (byEndEvent.TryGetValue(eventKey, out var previous))
            {
                reason = "This event key is ambiguous or already ended a different individual or outcome.";
                if (previous == null || !string.Equals(previous.Id, id, StringComparison.OrdinalIgnoreCase) || previous.TerminalOutcome != outcome)
                    return false;
                reason = $"This event already ended Gu {previous.Id}.";
                return true;
            }

            reason = "The Gu is not an authorized living individual, or the request is stale.";
            if (!CanUseCustody || Resolve(id) is not { } record || record.CustodyRevision != expectedRevision
                || expectedRevision == int.MaxValue) return false;
            reason = outcome == GuTerminalOutcome.DeliberatelyDestroyed
                ? "Only the cultivator holding the Gu, or the living host of its aperture, can destroy it."
                : "The actor cannot reach this Gu.";
            if (!ActorCanReach(record, actor, outcome == GuTerminalOutcome.DeliberatelyDestroyed)) return false;

            int tick = Find.TickManager.TicksGame;
            Thing thing = record.Placement == GuPlacementKind.Manifested ? ProjectionOf(record) : null;
            GuLocation location = thing != null ? LocationOf(thing) : ContainerLocation(record, tick);
            CustodyTransitionInProgress = true;
            try
            {
                UnbindAll(record);
                record.EndLife(outcome, eventKey, actor?.GetUniqueLoadID(), location, cause, tick);
                byEndEvent[eventKey] = record;
            }
            finally { CustodyTransitionInProgress = false; }
            RetireNativeObject(thing, record);
            ApplyTerminalEffect(record, outcome, location);
            reason = null;
            return true;
        }

        private bool ActorCanReach(GuRecord record, Pawn actor, bool deliberate)
        {
            if (actor == null) return !deliberate;
            if (actor.Dead || actor.Destroyed) return false;
            if (record.Placement == GuPlacementKind.Contained)
                return record.ContainerKind == GuContainerKind.MortalAperture && apertureOf.TryGetValue(record.Id, out var aperture)
                    && IsLiveAperture(aperture, record) && aperture.pawn == actor;
            var thing = ProjectionOf(record);
            if (thing == null) return false;
            if (InventoryOf(actor) is { } inventory && IsInInventory(thing, inventory)) return true;
            if (actor.carryTracker?.CarriedThing == thing) return true;
            return IsMapOwned(actor) && thing.Spawned && thing.Map == actor.Map && !actor.Downed
                && actor.CanReachImmediate(thing, Verse.AI.PathEndMode.Touch);
        }

        private GuLocation ContainerLocation(GuRecord record, int tick)
        {
            if (record.ContainerKind == GuContainerKind.WorldSite) return record.LastKnown;
            if (!apertureOf.TryGetValue(record.Id, out var aperture)) return null;
            var host = aperture.pawn;
            return host.Dead ? GuLocation.Capture(host.Corpse?.MapHeld, host.Corpse?.PositionHeld ?? IntVec3.Invalid, host.GetUniqueLoadID(), tick)
                : GuLocation.Capture(host.MapHeld, host.PositionHeld, host.GetUniqueLoadID(), tick);
        }

        // ---- Deceased-host custody ----------------------------------------------------------------------------

        /// <summary>
        /// Development fixture: takes a dormant Gu out of a slain cultivator's aperture into an adjacent free colonist's
        /// inventory. Final extraction gameplay is deferred. A later resurrection finds the aperture without this Gu.
        /// </summary>
        internal bool TryExtractFromDeceased(string id, int expectedRevision, Pawn recipient, out Thing_GuDormantProjection extracted,
            out string reason)
        {
            extracted = null;
            reason = "The Gu is not held in the aperture of a dead host, or the request is stale.";
            if (!CanUseDevelopmentCustody || Resolve(id) is not { } record || record.Placement != GuPlacementKind.Contained
                || record.ContainerKind != GuContainerKind.MortalAperture || record.CustodyRevision != expectedRevision
                || expectedRevision == int.MaxValue || !apertureOf.TryGetValue(record.Id, out var aperture)
                || !IsBoundAperture(aperture, record) || !aperture.pawn.Dead) return false;
            var host = aperture.pawn;
            reason = "The recipient must be a free colonist next to the host's corpse.";
            if (recipient == null || !recipient.IsFreeColonist || recipient.Downed || InventoryOf(recipient) is not { } inventory
                || !IsMapOwned(recipient) || host.Corpse is not { Spawned: true } corpse || corpse.Map != recipient.Map
                || !recipient.CanReachImmediate(corpse, Verse.AI.PathEndMode.Touch)) return false;
            var def = record.Species?.dormantProjectionDef;
            reason = "This species has no dormant projection, or the Gu is not dormant.";
            if (!Thing_GuDormantProjection.IsConfigured(def) || record.Condition != GuCondition.Dormant) return false;

            int tick = Find.TickManager.TicksGame;
            Thing_GuDormantProjection staged = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    staged = (Thing_GuDormantProjection)ThingMaker.MakeThing(def);
                    staged.InitializeBinding(record);
                    reason = "The recipient's inventory has no room for the Gu.";
                    if (!CanPack(recipient, inventory, staged) || !inventory.TryAdd(staged, false) || !IsInInventory(staged, inventory)
                        || !IsBoundAperture(aperture, record) || staged.GuRevision != expectedRevision) return false;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted extraction.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }
                record.CommitManifestation(GuManifestationKind.Thing, staged.GetUniqueLoadID());
                staged.InitializeBinding(record);
                BindProjection(record, staged);
                committed = true;
                record.AddHistory(new(tick, GuLifecycleEventKind.Extracted, "DeceasedHost", null, recipient.GetUniqueLoadID(),
                    GuLocation.Capture(corpse.Map, corpse.Position, host.GetUniqueLoadID(), tick), $"Taken from the aperture of {host.GetUniqueLoadID()}."));
                extracted = staged;
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (!committed && staged != null && !DestroyApertureTransferThing(staged))
                        MarkRogue(staged.GuTag(), "A failed extraction could not retire its provisional Thing.");
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        // ---- Escape from a destroyed host body (owner decision D2.7) -------------------------------------------

        /// <summary>The Gu left its host: masterless, active, and refinable by anyone.</summary>
        private static void MarkEscaped(GuRecord record, GuLocation location, string detail, int tick)
        {
            record.ReleaseRefinement();
            record.ChangeCondition(GuCondition.Active);
            record.NoteEncounterInteraction(tick, historical: true);
            record.AddHistory(new(tick, GuLifecycleEventKind.Escaped, "HostBodyDestroyed", null, null, location, detail));
        }

        /// <summary>
        /// When a dead cultivator's body is destroyed, the Gu still in its aperture slip out as active, masterless wild Gu
        /// where the body was. Without a live map at that place the escaped Gu follows its preservation policy.
        /// </summary>
        private void EscapeFromDeceasedHost(GuRecord record, GuLocation location, string detail, int tick)
        {
            UnbindAll(record);
            MarkEscaped(record, location, detail, tick);
            if (TryManifestEscapedGu(record, location))
            {
                if (record.IsActionable) record.RecordEscapePlacement();
                return;
            }
            Abandon(record, null, location, GuUnresolvedCause.HostCorpseDestroyed, GuTerminalEvidence.HostCorpseDestroyed, "HostCorpseDestroyed",
                "The Gu slipped out of the destroyed body where no live map could receive it.", tick);
            if (record.IsActionable) record.RecordEscapePlacement();
        }

        private bool TryManifestEscapedGu(GuRecord record, GuLocation location)
        {
            var species = record.Species;
            var map = location?.MapId == null ? null : Find.Maps.FirstOrDefault(candidate => candidate.GetUniqueLoadID() == location.MapId);
            if (map == null || map.Disposed || !location.Cell.IsValid || FirstAdultProjectionStage(species?.wildProjectionKind) is not { } adult)
                return false;
            IntVec3 cell = GenRadial.RadialCellsAround(location.Cell, 6f, true).FirstOrDefault(candidate => CanPlaceDormant(map, candidate, null), IntVec3.Invalid);
            if (!cell.IsValid) return false;

            Pawn_GuWildProjection prepared = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    prepared = PrepareWildPawn(species.wildProjectionKind, adult, map);
                    prepared.InitializeBinding(record);
                    if (!TryPlaceStagedWildPawn(prepared, record, species.wildProjectionKind, map, cell)) return false;
                    record.CommitManifestation(GuManifestationKind.Pawn, prepared.GetUniqueLoadID());
                    prepared.InitializeBinding(record);
                    BindProjection(record, prepared);
                    committed = true;
                    prepared.SetReleaseSuppressed(false);
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] A native callback interrupted the escape of Gu {record.Id}. {exception}");
                }
                return committed;
            }
            finally
            {
                try
                {
                    if (!committed && prepared != null && !DiscardProvisionalWildPawn(prepared))
                        MarkRogue(prepared.GuTag(), "A failed escape could not discard its provisional Pawn.");
                    if (committed && !MatchesManifestation(record, prepared))
                    {
                        UnbindAll(record);
                        record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                            "The escaped Gu's native Pawn binding failed after commit.", Find.TickManager.TicksGame, location);
                    }
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        // ---- Abstract world locations -------------------------------------------------------------------------

        internal IEnumerable<GuRecord> WorldSiteRecordsAt(Map map) => records.Where(record => record != null && record.IsActionable
            && record.Placement == GuPlacementKind.Contained && record.ContainerKind == GuContainerKind.WorldSite
            && record.LastKnown?.IsAt(map) == true);

        /// <summary>
        /// Development fixture for future expeditions: manifests a Gu at its recorded native locality.
        /// Dormant Gu return as Things and active Gu as wild Pawns.
        /// </summary>
        internal bool TryRecoverFromWorldSite(string id, int expectedRevision, Map map, IntVec3 cell, out Thing manifestation, out string reason)
            => TryRecoverWorldSite(id, expectedRevision, map, cell, null, out manifestation, out reason);

        internal bool TryRecoverSurfaceResident(GuRecord resident, Map map, IntVec3 cell, Func<bool> surfaceReady, out string reason)
            => TryRecoverWorldSite(resident.Id, resident.CustodyRevision, map, cell, surfaceReady, out _, out reason);

        private bool TryRecoverWorldSite(string id, int expectedRevision, Map map, IntVec3 cell, Func<bool> surfaceReady,
            out Thing manifestation, out string reason)
        {
            manifestation = null;
            reason = "The Gu is not held at an abstract world location, or the request is stale.";
            if (!(surfaceReady == null ? CanUseDevelopmentCustody : CanUseCustody) || Resolve(id) is not { } record
                || surfaceReady != null && (record.SurfaceResidence == null || record.Condition != GuCondition.Active || !surfaceReady())
                || record.Placement != GuPlacementKind.Contained
                || record.ContainerKind != GuContainerKind.WorldSite || record.CustodyRevision != expectedRevision
                || expectedRevision == int.MaxValue) return false;
            reason = "Choose an open cell in the Gu's recorded native locality.";
            if (record.LastKnown?.IsAt(map) != true || !CanPlaceDormant(map, cell, null)) return false;
            var species = record.Species;
            bool dormant = record.Condition == GuCondition.Dormant;
            var adult = dormant ? null : FirstAdultProjectionStage(species?.wildProjectionKind);
            reason = "This species has no projection for the Gu's condition.";
            if (dormant ? !Thing_GuDormantProjection.IsConfigured(species?.dormantProjectionDef) : adult == null) return false;

            int tick = Find.TickManager.TicksGame;
            Thing staged = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    reason = "Native placement did not establish the expected manifestation.";
                    if (dormant)
                    {
                        var thing = (Thing_GuDormantProjection)ThingMaker.MakeThing(species.dormantProjectionDef);
                        staged = thing;
                        thing.InitializeBinding(record);
                        if (!GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct, out Thing placed) || placed != thing
                            || !IsOnCell(thing, map, cell)) return false;
                    }
                    else
                    {
                        var pawn = PrepareWildPawn(species.wildProjectionKind, adult, map);
                        staged = pawn;
                        pawn.InitializeBinding(record);
                        if (!TryPlaceStagedWildPawn(pawn, record, species.wildProjectionKind, map, cell)) return false;
                    }
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted world-location recovery.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }
                if (!record.IsActionable || record.CustodyRevision != expectedRevision || record.LastKnown?.IsAt(map) != true
                    || !IsOnCell(staged, map, cell) || MapTeardownInProgress || pending.Any(item => item.Teardown?.Map == map)) return false;
                if (surfaceReady != null && (!surfaceReady() || map.reservationManager.IsReserved(cell)
                    || !CanPlaceDormant(map, cell, staged as Pawn))) return false;
                record.CommitManifestation(dormant ? GuManifestationKind.Thing : GuManifestationKind.Pawn, staged.GetUniqueLoadID());
                staged.GuTag()?.Bind(record);
                BindProjection(record, staged);
                committed = true;
                if (staged is Pawn_GuWildProjection released) released.SetReleaseSuppressed(false);
                record.AddHistory(new(tick, GuLifecycleEventKind.Recovered, "WorldSite", null, null,
                    GuLocation.Capture(map, cell, null, tick), "Recovered from an abstract world location."));
                manifestation = staged;
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (!committed && staged != null)
                    {
                        bool cleared = staged is Pawn_GuWildProjection pawn ? DiscardProvisionalWildPawn(pawn)
                            : DestroyApertureTransferThing((Thing_GuDormantProjection)staged);
                        if (!cleared) MarkRogue(staged.GuTag(), "A failed world-location recovery could not retire its provisional object.");
                    }
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        // ---- Physical Gu survive their holder (owner decisions §2) -----------------------------------------------

        private bool HasGuCustody(Pawn pawn) => pawn?.inventory?.GetDirectlyHeldThings().Any(thing => byProjection.ContainsKey(thing)) == true
            || apertureOf.Values.Any(aperture => aperture.pawn == pawn);

        /// <summary>Corpse.Destroy prefix: remembers where the body stood before native teardown clears it.</summary>
        internal void NoteCorpseTeardown(Corpse corpse)
        {
            if (!ready || corpse?.InnerPawn is not { } pawn || !HasGuCustody(pawn)) return;
            int tick = Find.TickManager.TicksGame;
            var map = corpse.MapHeld;
            var tile = corpse.Tile;
            corpseLocations[pawn] = map != null ? GuLocation.Capture(map, corpse.PositionHeld, pawn.GetUniqueLoadID(), tick)
                : GuLocation.FromTile(tile.tileId, tile.Valid ? tile.Layer?.LayerID ?? -1 : -1, IntVec3.Invalid, null, pawn.GetUniqueLoadID(), tick);
            if (map != null) corpseDrops[pawn] = (map, corpse.PositionHeld);
        }

        internal void ForgetCorpseTeardown(Pawn pawn)
        {
            if (pawn == null) return;
            corpseLocations.Remove(pawn);
            corpseDrops.Remove(pawn);
        }

        /// <summary>
        /// Corpse.PostCorpseDestroy prefix. Native teardown destroys a dead or discarded Pawn's inventory. Authorized Gu are
        /// placed where the body stood when that is a live map. Otherwise their native end is recorded as the holder's
        /// teardown, which the preservation policy handles, never as the Gu's destruction.
        /// </summary>
        internal void BeforeHolderTeardown(Pawn pawn, bool discarded)
        {
            var spot = !discarded && pawn != null && corpseDrops.TryGetValue(pawn, out var corpse) ? corpse : default;
            PreserveHeldGu(pawn, spot.Map, spot.Cell);
        }

        /// <summary>Pawn.Destroy prefix for a non-lethal destruction, which natively destroys the whole inventory.</summary>
        internal void BeforeHolderVanish(Pawn pawn, DestroyMode mode)
        {
            if (mode == DestroyMode.KillFinalize || pawn == null) return;
            PreserveHeldGu(pawn, pawn.Spawned ? pawn.Map : null, pawn.Spawned ? pawn.Position : IntVec3.Invalid);
        }

        private void PreserveHeldGu(Pawn pawn, Map dropMap, IntVec3 dropCell)
        {
            if (!ready || pawn?.inventory?.GetDirectlyHeldThings() is not { } inventory) return;
            var held = inventory.Where(thing => byProjection.TryGetValue(thing, out var record) && record.IsLiving
                && MatchesManifestation(record, thing)).ToArray();
            if (held.Length == 0) return;
            if (dropMap is { Disposed: false } map && dropCell.IsValid && Find.Maps.Contains(map) && !mapTeardowns.ContainsKey(map))
            {
                foreach (var thing in held)
                {
                    try { inventory.TryDrop(thing, dropCell, map, ThingPlaceMode.Near, out _); }
                    catch (Exception exception) { Log.Warning($"[Project GU] Could not place a Gu from a destroyed corpse. {exception}"); }
                }
            }
            var location = corpseLocations.TryGetValue(pawn, out var known) ? known : LocationOf(pawn);
            foreach (var thing in held.Where(thing => thing.holdingOwner == inventory)) holderTeardown[thing] = location;
        }

        /// <summary>World-pawn GC retention for Pawns that hold or host Gu that must keep their identity (owner decisions §4).</summary>
        internal bool IsCustodyCritical(Pawn pawn)
        {
            if (!ready || pawn == null) return false;
            int tick = Find.TickManager.TicksGame;
            if (tick != criticalCacheTick)
            {
                criticalCacheTick = tick;
                criticalPawns.Clear();
                // Holders carry canonical custody even while off-map. For unheld bodies, retain only species with explicit
                // individual policy and exact surface residents that reconciliation cannot safely move into record-only custody.
                foreach (var (thing, record) in byProjection)
                {
                    if (!record.IsLiving) continue;
                    var policy = PreservationFor(record);
                    if (thing is Pawn projection)
                    {
                        if (record.SurfaceResidence != null || policy != GuPreservationPolicy.ReturnToPopulation)
                            criticalPawns.Add(projection);
                    }
                    else if (policy != GuPreservationPolicy.Individual && record.Species?.HasSurfacePopulation != true) continue;
                    else if (thing.ParentHolder is Pawn_InventoryTracker inventory) criticalPawns.Add(inventory.pawn);
                    else if (thing.ParentHolder is Pawn_CarryTracker carry) criticalPawns.Add(carry.pawn);
                }
                foreach (var (id, aperture) in apertureOf)
                    if (byId.TryGetValue(id, out var record) && record.IsLiving
                        && (PreservationFor(record) == GuPreservationPolicy.Individual || record.Species?.HasSurfacePopulation == true))
                        criticalPawns.Add(aperture.pawn);
            }
            return criticalPawns.Contains(pawn);
        }

        // ---- Retention and compaction (owner decisions §5) -------------------------------------------------------

        /// <summary>
        /// Compacts ended records older than the retention period and summarizes old narrative history on living records.
        /// Records are never deleted: their creation keys, terminal keys and cohort debits keep replay protection and
        /// conservation exact. Returns the number of records compacted.
        /// </summary>
        internal int CompactRecords(int now, int budget)
        {
            if (!ready || CustodyTransitionInProgress || records.Count == 0) return 0;
            int compactedCount = 0;
            int count = Math.Min(budget, records.Count);
            for (int step = 0; step < count; step++)
            {
                if (compactionCursor >= records.Count) compactionCursor = 0;
                var record = records[compactionCursor++];
                if (record == null) continue;
                if (record.IsLiving) record.SummarizeHistoryBefore(now - RetentionTicks);
                else if (!record.IsCompacted && record.EndedTick >= 0 && now - record.EndedTick >= RetentionTicks
                    && (record.Existence != GuExistence.Aggregated || CanCompactPopulationReturn(record)) && record.Compact()) compactedCount++;
            }
            return compactedCount;
        }

        private bool CanCompactPopulationReturn(GuRecord record) => record.PopulationReturn is { } receipt && receipt.Matches(record)
            && GuSurfaceEcology.Instance?.CohortAt(receipt.Site, record.SpeciesDefName) is { } cohort && SurfaceEvidenceIntact(receipt.Site, cohort);
    }
}
