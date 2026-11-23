using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Reconciliation (RFC-0004 §5.4, §15.2). Native hooks enqueue events with location evidence captured at the time.
    /// A scheduled sweep checks a bounded slice of records. Both apply changes outside native callbacks and never depend
    /// on the player opening a UI. Phase 2 turns demonstrated death and destruction into terminal outcomes and routes
    /// lost representation through the species preservation policy.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        internal const int SweepInterval = 250;
        internal const int SweepBudget = 512;

        private enum PendingKind { Projection, Aperture, MapTeardown }

        private sealed record PendingEvent(PendingKind Kind, Thing Projection, Hediff_Aperture Aperture, GuUnresolvedCause Cause,
            GuTerminalEvidence Evidence, GuLocation Location, string Detail, MapTeardownCapture Teardown = null);

        /// <summary>Locations of every Gu on a map, captured before native teardown discards its contents.</summary>
        private sealed class MapTeardownCapture
        {
            internal Map Map;
            internal readonly Dictionary<GuRecord, GuLocation> Captured = new();
        }

        private readonly Queue<PendingEvent> pending = new();
        private int sweepCursor;
        private MapTeardownCapture activeTeardown;
        // Transient: Things being destroyed only because their holder is being torn down, with the holder's location.
        private readonly Dictionary<Thing, GuLocation> holderTeardown = new();
        // Transient: where a corpse stood when its destruction began, keyed by the inner Pawn.
        private readonly Dictionary<Pawn, GuLocation> corpseLocations = new();

        internal int PendingEventCount => pending.Count;

        // ---- Native event intake (called from native callbacks; enqueue only) ----------------------------------

        internal void EnqueueProjectionEvent(Thing projection, GuTerminalEvidence evidence)
        {
            if (projection == null) return;
            if (activeTeardown != null && byProjection.TryGetValue(projection, out var captured) && activeTeardown.Captured.ContainsKey(captured))
                return;
            var location = projection.GuTag()?.TakeEndingLocation() ?? LocationOf(projection);
            if (holderTeardown.TryGetValue(projection, out var holder))
            {
                // The holder's teardown, not the Gu itself, is ending the native object. That is not destruction.
                location = holder;
                evidence = GuTerminalEvidence.Discarded;
            }
            pending.Enqueue(new(PendingKind.Projection, projection, null, GuUnresolvedCause.ProjectionEnded, evidence, location,
                $"Native object {projection.GetUniqueLoadID()} reported {evidence}."));
        }

        internal void EnqueueApertureEvent(Hediff_Aperture aperture, GuUnresolvedCause cause, string detail)
        {
            if (aperture?.pawn is not { } host) return;
            if (activeTeardown != null && apertureOf.Any(binding => binding.Value == aperture && byId.TryGetValue(binding.Key, out var record)
                    && activeTeardown.Captured.ContainsKey(record)))
                return;
            var location = corpseLocations.TryGetValue(host, out var corpse) ? corpse
                : GuLocation.Capture(host.Dead ? host.Corpse?.MapHeld : host.MapHeld,
                    host.Dead ? host.Corpse?.PositionHeld ?? IntVec3.Invalid : host.PositionHeld, host.GetUniqueLoadID(), Find.TickManager.TicksGame);
            pending.Enqueue(new(PendingKind.Aperture, null, aperture, cause, GuTerminalEvidence.None, location, detail));
        }

        internal void BeginMapTeardown(Map map)
        {
            if (!ready || map == null) return;
            if (activeTeardown != null) EndMapTeardown(activeTeardown.Map);
            int tick = Find.TickManager.TicksGame;
            var capture = new MapTeardownCapture { Map = map };
            foreach (var binding in projectionOf)
                if (byId.TryGetValue(binding.Key, out var record) && record.IsLiving && binding.Value.MapHeld == map)
                    capture.Captured[record] = GuLocation.Capture(map, binding.Value.PositionHeld, HolderIdOf(binding.Value), tick);
            foreach (var binding in apertureOf)
            {
                var host = binding.Value.pawn;
                var hostMap = host.Dead ? host.Corpse?.MapHeld : host.MapHeld;
                if (hostMap == map && byId.TryGetValue(binding.Key, out var record) && record.IsLiving)
                    capture.Captured[record] = GuLocation.Capture(map, host.Dead ? host.Corpse.PositionHeld : host.PositionHeld,
                        host.GetUniqueLoadID(), tick);
            }
            activeTeardown = capture.Captured.Count == 0 ? null : capture;
        }

        internal void EndMapTeardown(Map map)
        {
            if (activeTeardown == null || activeTeardown.Map != map) return;
            pending.Enqueue(new(PendingKind.MapTeardown, null, null, GuUnresolvedCause.MapRemoved, GuTerminalEvidence.MapRemoved,
                null, $"Map {map.GetUniqueLoadID()} was removed.", activeTeardown));
            activeTeardown = null;
        }

        public override void GameComponentTick()
        {
            if (!ready || CustodyTransitionInProgress) return;
            if (pending.Count > 0) ApplyPendingEvents();
            Current.Game?.GetComponent<GuWorldComponent>()?.ApplyNativeCultivatorObservations();
            int tick = Find.TickManager.TicksGame;
            if (tick % SweepInterval == 0) Sweep(SweepBudget);
            if (tick % CompactionInterval == 0) CompactRecords(tick, CompactionBudget);
        }

        // ---- Application (outside native callbacks) ---------------------------------------------------------------

        /// <summary>Applies queued native events. Exposed for fixtures that need a deterministic reconciliation point.</summary>
        internal void ApplyPendingEvents()
        {
            if (!ready || CustodyTransitionInProgress || pending.Count == 0) return;
            int tick = Find.TickManager.TicksGame;
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                switch (item.Kind)
                {
                    case PendingKind.Projection:
                        // Only the authorized manifestation affects the record. Retired or rogue objects are ignored.
                        if (byProjection.TryGetValue(item.Projection, out var record) && record.IsLiving && !record.IsQuarantined)
                            ApplyProjectionEnded(record, item.Projection, item.Evidence, item.Location, item.Detail, tick);
                        break;
                    case PendingKind.Aperture:
                        foreach (var occupant in apertureOf.Where(binding => binding.Value == item.Aperture)
                                     .Select(binding => byId[binding.Key]).ToArray())
                            ApplyApertureEvent(occupant, item.Aperture, item.Cause, item.Location, item.Detail, tick);
                        break;
                    case PendingKind.MapTeardown:
                        ResolveTeardown(item.Teardown, tick);
                        break;
                }
            }
            holderTeardown.Clear();
        }

        private void ApplyProjectionEnded(GuRecord record, Thing thing, GuTerminalEvidence evidence, GuLocation location, string detail, int tick)
        {
            switch (evidence)
            {
                case GuTerminalEvidence.Killed:
                case GuTerminalEvidence.Destroyed:
                    // Demonstrated biological death or physical destruction is terminal (owner decisions §1).
                    UnbindAll(record);
                    // Killed means biological death for a Pawn. Any other destruction, including a vanished Pawn, is Destroyed.
                    var outcome = thing is Pawn && evidence == GuTerminalEvidence.Killed ? GuTerminalOutcome.Died : GuTerminalOutcome.Destroyed;
                    record.EndLife(outcome, null, null, location, detail, tick);
                    ApplyTerminalEffect(record, outcome, location);
                    break;
                default:
                    Abandon(record, thing, location, evidence == GuTerminalEvidence.MapRemoved ? GuUnresolvedCause.MapRemoved
                        : GuUnresolvedCause.ProjectionEnded, evidence, evidence.ToString(), detail, tick);
                    break;
            }
        }

        private void ApplyApertureEvent(GuRecord record, Hediff_Aperture aperture, GuUnresolvedCause cause, GuLocation location,
            string detail, int tick)
        {
            if (!record.IsLiving || record.IsQuarantined) return;
            switch (cause)
            {
                case GuUnresolvedCause.HostDead:
                    // Deceased-host custody: the binding stays and the Gu remain recoverable from the body.
                    record.NoteLocation(location);
                    record.AddHistory(new(tick, GuLifecycleEventKind.HostDied, "HostDied", null, aperture.pawn.GetUniqueLoadID(), location, detail));
                    break;
                case GuUnresolvedCause.HostCorpseDestroyed when !aperture.pawn.Dead:
                    // A living host is discarded (for example by native world-pawn GC). Its body is gone, not dead.
                    UnbindAll(record);
                    lastHost[record.Id] = aperture.pawn;
                    record.MarkUnresolved(GuUnresolvedCause.HostUnavailable, GuTerminalEvidence.Discarded,
                        "The living aperture host was discarded.", tick, location);
                    break;
                case GuUnresolvedCause.HostCorpseDestroyed:
                    // Owner decision D2.7: when the body itself is destroyed, the Gu slip out active and masterless.
                    lastHost[record.Id] = aperture.pawn;
                    EscapeFromDeceasedHost(record, location, detail, tick);
                    break;
                default:
                    UnbindAll(record);
                    lastHost[record.Id] = aperture.pawn;
                    record.MarkUnresolved(cause, GuTerminalEvidence.None, detail, tick, location);
                    break;
            }
        }

        private void ResolveTeardown(MapTeardownCapture capture, int tick)
        {
            if (capture == null) return;
            foreach (var (record, location) in capture.Captured)
            {
                if (!record.IsLiving || record.IsQuarantined) continue;
                if (record.Placement == GuPlacementKind.Manifested)
                {
                    var thing = ProjectionOf(record);
                    if (thing != null && MatchesManifestation(record, thing))
                    {
                        // A Gu that followed a living holder to the world keeps its manifestation. A wild Gu Pawn passed to
                        // the world by itself has left native simulation.
                        // ReturnToPopulation has no population target yet, so the world Pawn stays the manifestation (as in the sweep).
                        if (thing is Pawn pawn && IsAbandonedWorldPawn(pawn) && PreservationFor(record) != GuPreservationPolicy.ReturnToPopulation)
                            Abandon(record, pawn, location, GuUnresolvedCause.MapRemoved, GuTerminalEvidence.MapRemoved, "MapRemoved",
                                "The map was removed and the wild Gu passed to the world.", tick);
                        continue;
                    }
                    Abandon(record, thing, location, GuUnresolvedCause.MapRemoved, GuTerminalEvidence.MapRemoved, "MapRemoved",
                        "The map was removed with the Gu on it.", tick);
                    continue;
                }
                if (record.ContainerKind == GuContainerKind.MortalAperture
                    && apertureOf.TryGetValue(record.Id, out var aperture) && IsBoundAperture(aperture, record)) continue;
                // The host's remains left with the map, so its aperture Gu slipped out there (D2.7). The map is gone, so the
                // escaped Gu is placed by its preservation policy at that location.
                if (record.ContainerKind == GuContainerKind.MortalAperture) MarkEscaped(record, location, "The host's remains were removed with the map.", tick);
                Abandon(record, null, location, GuUnresolvedCause.MapRemoved, GuTerminalEvidence.MapRemoved, "MapRemoved",
                    "The map was removed with the aperture host's remains on it.", tick);
            }
        }

        // ---- Abandonment and preservation policy ----------------------------------------------------------------

        /// <summary>Policy for a Gu that lost its native representation without dying (owner decisions §4).</summary>
        internal static GuPreservationPolicy PreservationFor(GuRecord record)
        {
            var species = record.Species;
            if (species == null || !string.IsNullOrEmpty(record.UniquenessKey) || record.Refinement?.CultivatorId != null
                || record.Rank >= species.individualPreservationMinRank) return GuPreservationPolicy.Individual;
            return species.abandonmentPolicy;
        }

        /// <summary>
        /// Applies the preservation policy. Individuals keep their identity at an abstract world location when the location
        /// is known. Declared retirement ends individual simulation with accounting intact. Without a safe outcome the record
        /// stays unresolved with its evidence. Nothing here establishes death.
        /// </summary>
        private void Abandon(GuRecord record, Thing nativeObject, GuLocation location, GuUnresolvedCause fallback,
            GuTerminalEvidence evidence, string cause, string detail, int tick)
        {
            UnbindAll(record);
            if (nativeObject != null) lastProjection[record.Id] = nativeObject;
            var policy = PreservationFor(record);
            if (policy == GuPreservationPolicy.Individual && location?.HasTile == true)
            {
                record.CommitWorldSite(location, cause, detail, tick);
                RetireNativeObject(nativeObject, record);
            }
            else if (policy == GuPreservationPolicy.RetireFromSimulation)
            {
                record.Retire(cause, location, detail, tick);
                RetireNativeObject(nativeObject, record);
            }
            else
            {
                string note = policy == GuPreservationPolicy.ReturnToPopulation
                    ? " No regional population can receive it yet." : " The location could not be established.";
                record.MarkUnresolved(fallback, evidence, detail + note, tick, location);
            }
        }

        /// <summary>Intentional retirement of a native object that no longer represents the record. Never counted as destruction.</summary>
        private void RetireNativeObject(Thing thing, GuRecord record)
        {
            if (thing == null || thing.Discarded || (thing.Destroyed && thing is not Pawn)) return;
            try
            {
                if (thing is Pawn pawn)
                {
                    if (pawn.Spawned) pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                    else if (pawn.holdingOwner == null && !pawn.Discarded) pawn.Discard(true);
                }
                else thing.Destroy(DestroyMode.Vanish);
            }
            catch (System.Exception exception)
            {
                Log.Warning($"[Project GU] Could not retire the former native object of Gu {record.Id}. {exception}");
            }
            if (!thing.Discarded && !thing.Destroyed) MarkRogue(thing.GuTag(), "The record left native simulation but its former object could not be retired.");
        }

        private static bool IsAbandonedWorldPawn(Pawn pawn) => !pawn.Dead && !pawn.Spawned && pawn.IsWorldPawn()
            && pawn.GetCaravan() == null && pawn.holdingOwner == null;

        private static string HolderIdOf(Thing thing) => thing.ParentHolder switch
        {
            Pawn_InventoryTracker inventory => inventory.pawn.GetUniqueLoadID(),
            Pawn_CarryTracker carry => carry.pawn.GetUniqueLoadID(),
            Thing holder => holder.GetUniqueLoadID(),
            _ => null
        };

        /// <summary>Best location evidence for a native object: its live map, its root world tile, or its last spawn.</summary>
        internal static GuLocation LocationOf(Thing thing)
        {
            int tick = Find.TickManager.TicksGame;
            if (thing.MapHeld is { } map) return GuLocation.Capture(map, thing.PositionHeld, HolderIdOf(thing), tick);
            // The last spawn is evidence only for an object outside every holder (a world Pawn). A held Thing has moved since.
            if (thing.ParentHolder == null && thing.GuTag() is { HasLastLocation: true } tag) return tag.LastLocation(tick);
            var tile = thing.Tile;
            return tile.Valid ? GuLocation.FromTile(tile.tileId, tile.Layer?.LayerID ?? -1, IntVec3.Invalid, null, HolderIdOf(thing), tick) : null;
        }

        // ---- Scheduled sweep --------------------------------------------------------------------------------------

        /// <summary>Checks up to <paramref name="budget"/> living records and repairs indexes or recovers unresolved ones.</summary>
        internal void Sweep(int budget)
        {
            if (!ready || CustodyTransitionInProgress || records.Count == 0) return;
            int tick = Find.TickManager.TicksGame;
            int count = System.Math.Min(budget, records.Count);
            for (int step = 0; step < count; step++)
            {
                if (sweepCursor >= records.Count) sweepCursor = 0;
                var record = records[sweepCursor++];
                if (record == null || record.IsQuarantined || !record.IsLiving || record.Id == null) continue;
                if (record.Placement == GuPlacementKind.Manifested) SweepManifested(record, tick);
                else SweepContained(record, tick);
            }
        }

        private void SweepManifested(GuRecord record, int tick)
        {
            if (!record.IsUnresolved && projectionOf.TryGetValue(record.Id, out var thing))
            {
                if (MatchesManifestation(record, thing))
                {
                    // A wild Gu Pawn that left every map by itself is abstracted or retired by policy. ReturnToPopulation has no
                    // target yet: the world Pawn stays until native GC discards it, and the record then keeps its evidence.
                    if (thing is Pawn pawn && IsAbandonedWorldPawn(pawn) && PreservationFor(record) != GuPreservationPolicy.ReturnToPopulation)
                        Abandon(record, pawn, LocationOf(pawn), GuUnresolvedCause.ProjectionEnded, GuTerminalEvidence.None, "WorldPawn",
                            "The wild Gu left every map and became a world Pawn.", tick);
                    return;
                }
                // A missed native event. The native object's state is the evidence.
                if (thing is Pawn { Dead: true } || (thing.Destroyed && !thing.Discarded))
                {
                    UnbindAll(record);
                    var outcome = thing is Pawn { Dead: true } ? GuTerminalOutcome.Died : GuTerminalOutcome.Destroyed;
                    record.EndLife(outcome, null, null, LocationOf(thing), "Detected by the scheduled sweep.", tick);
                    ApplyTerminalEffect(record, outcome, record.LastKnown);
                }
                else if (thing.Discarded)
                    Abandon(record, thing, LocationOf(thing), GuUnresolvedCause.ProjectionEnded, GuTerminalEvidence.Discarded, "Discarded",
                        "The native object was discarded.", tick);
                else
                {
                    UnbindAll(record);
                    record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                        $"Native object {record.ProjectionId} is no longer a valid manifestation.", tick, LocationOf(thing));
                }
                return;
            }
            // Recovery is limited to the same native object that was last authorized.
            if (record.IsUnresolved && record.TerminalEvidence == GuTerminalEvidence.None
                && record.Unresolved == GuUnresolvedCause.ProjectionMissing
                && lastProjection.TryGetValue(record.Id, out var last) && MatchesManifestation(record, last))
            {
                record.ClearUnresolved();
                BindProjection(record, last);
            }
        }

        private void SweepContained(GuRecord record, int tick)
        {
            if (record.ContainerKind == GuContainerKind.WorldSite) return;
            if (!record.IsUnresolved)
            {
                if (apertureOf.TryGetValue(record.Id, out var aperture) && IsBoundAperture(aperture, record)) return;
                var host = aperture?.pawn ?? (lastHost.TryGetValue(record.Id, out var known) ? known : null);
                if (host != null) ApplyApertureState(record, host, tick);
                else
                {
                    UnbindAll(record);
                    record.MarkUnresolved(GuUnresolvedCause.HostUnavailable, GuTerminalEvidence.None,
                        $"Aperture host {record.AnchorHint} is not available.", tick);
                }
                return;
            }
            // A returning host or a restored aperture recovers the same containment. Host death no longer unbinds, so Phase 1
            // HostDead records recover here as deceased-host custody.
            if (record.Unresolved is GuUnresolvedCause.HostDead or GuUnresolvedCause.HostUnavailable or GuUnresolvedCause.ApertureMissing
                && lastHost.TryGetValue(record.Id, out var lastKnown) && !lastKnown.Discarded
                && FindAperture(lastKnown, record.ContainerId) is { } restored && IsBoundAperture(restored, record))
            {
                record.ClearUnresolved();
                record.AddHistory(new(tick, GuLifecycleEventKind.Recovered, "ApertureRestored", null, lastKnown.GetUniqueLoadID(), null,
                    "The same aperture became available again."));
                BindAperture(record, restored);
            }
        }
    }
}
