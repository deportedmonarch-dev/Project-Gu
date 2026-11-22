using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Schema V2 canonical registry (RFC-0004). It owns identity, manifestation authority and record-only containment.
    /// The native engine owns the physical location of an authorized manifestation. Read methods never mutate records.
    /// </summary>
    public sealed partial class GuIndividualRegistry : GameComponent
    {
        private List<GuRecord> records = new();
        private List<GuIndividualV1> legacyV1;

        private readonly Dictionary<string, GuRecord> byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Thing, GuRecord> byProjection = new();
        private readonly Dictionary<string, Thing> projectionOf = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Hediff_Aperture> apertureOf = new(StringComparer.OrdinalIgnoreCase);
        // Transient recovery hints for unresolved records. Rebuilt on load.
        private readonly Dictionary<string, Pawn> lastHost = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Thing> lastProjection = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<CompGuManifestation> rogues = new();
        private bool ready;

        public GuIndividualRegistry(Game game) { }

        internal IReadOnlyList<GuRecord> Records => records;
        internal IReadOnlyCollection<CompGuManifestation> RogueProjections => rogues;
        internal string LastMigrationSummary { get; private set; }
        internal bool CustodyTransitionInProgress { get; private set; }
        private bool CanUseDevelopmentCustody => Prefs.DevMode && CanUseCustody;
        private bool CanUseCustody => ready && GuCustodySaveGuard.Installed && !CustodyTransitionInProgress
            && !LongEventHandler.AnyEventNowOrWaiting && Scribe.mode == LoadSaveMode.Inactive;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                ready = false;
                CompGuManifestation.LoadBuffer.Clear();
            }
            if (Scribe.mode == LoadSaveMode.Saving && CustodyTransitionInProgress)
                throw new InvalidOperationException("Wait for the Gu custody transition to finish before saving.");
            Scribe_Collections.Look(ref records, "guIndividualRecordsV2", LookMode.Deep);
            // Schema V1 is read once for migration and never written again.
            if (Scribe.mode != LoadSaveMode.Saving) Scribe_Collections.Look(ref legacyV1, "guIndividualRecordsV1", LookMode.Deep);
            records ??= new();
        }

        public override void StartedNewGame()
        {
            CompGuManifestation.LoadBuffer.Clear();
            legacyV1 = null;
            RebuildBindings(Array.Empty<CompGuManifestation>());
        }

        public override void LoadedGame()
        {
            var loaded = CompGuManifestation.LoadBuffer.Where(tag => tag?.parent != null).Distinct().ToArray();
            CompGuManifestation.LoadBuffer.Clear();
            MigrateLegacyRecords(loaded);
            RebuildBindings(loaded);
        }

        // ---- Pure validation ----------------------------------------------------------------------------------

        /// <summary>
        /// True when the native object is the record's authorized manifestation. Physical location and holder are not
        /// checked: the engine is authoritative for them. Actions revalidate location separately.
        /// </summary>
        internal static bool MatchesManifestation(GuRecord record, Thing thing)
        {
            if (record?.Placement != GuPlacementKind.Manifested || thing == null || thing.Destroyed || thing.Discarded) return false;
            if (thing.GuTag() is not { } tag || !string.Equals(tag.GuId, record.Id, StringComparison.OrdinalIgnoreCase)
                || tag.GuRevision != record.CustodyRevision || thing.GetUniqueLoadID() != record.ProjectionId) return false;
            if (record.Species is not { } species) return false;
            return record.Manifestation switch
            {
                GuManifestationKind.Pawn => thing is Pawn_GuWildProjection pawn && !pawn.Dead && !pawn.ReleaseSuppressed
                    && pawn.kindDef == species.wildProjectionKind && record.Condition == GuCondition.Active,
                GuManifestationKind.Thing => thing is Thing_GuDormantProjection dormant && dormant.stackCount == 1
                    && dormant.def == species.dormantProjectionDef && record.Condition == GuCondition.Dormant,
                _ => false
            };
        }

        internal static bool IsAuthorized(GuRecord record, Thing thing) => record?.IsActionable == true && MatchesManifestation(record, thing);

        private static bool IsLiveAperture(Hediff_Aperture aperture, GuRecord record) =>
            aperture?.pawn is { Dead: false, Destroyed: false, Discarded: false } host && aperture.def == GuWorldComponent.ApertureDef
            && record?.Placement == GuPlacementKind.Contained && record.ContainerKind == GuContainerKind.MortalAperture
            && aperture.ApertureId != null && aperture.ApertureId == record.ContainerId
            && host.GetUniqueLoadID() == record.AnchorHint && host.health?.hediffSet?.hediffs.Contains(aperture) == true;

        private static Hediff_Aperture FindAperture(Pawn host, string apertureId)
        {
            if (host?.health?.hediffSet == null || string.IsNullOrEmpty(apertureId)) return null;
            Hediff_Aperture match = null;
            foreach (var hediff in host.health.hediffSet.hediffs)
            {
                if (hediff is not Hediff_Aperture aperture || aperture.ApertureId != apertureId) continue;
                if (match != null) return null;
                match = aperture;
            }
            return match;
        }

        // ---- Pure queries -------------------------------------------------------------------------------------

        internal GuRecord Resolve(Thing thing)
        {
            if (!ready || CustodyTransitionInProgress || thing == null || !byProjection.TryGetValue(thing, out var record)) return null;
            return IsAuthorized(record, thing) ? record : null;
        }

        internal GuRecord Resolve(string id)
        {
            if (!ready || CustodyTransitionInProgress || id == null || !byId.TryGetValue(id, out var record) || !record.IsActionable)
                return null;
            if (record.Placement == GuPlacementKind.Manifested)
                return projectionOf.TryGetValue(record.Id, out var thing) && MatchesManifestation(record, thing) ? record : null;
            return apertureOf.TryGetValue(record.Id, out var aperture) && IsLiveAperture(aperture, record) ? record : null;
        }

        /// <summary>Looks up a record by ID whatever its state, for inspection and tests.</summary>
        internal GuRecord FindRecord(string id) => id != null && byId.TryGetValue(id, out var record) ? record : null;

        internal Thing ProjectionOf(GuRecord record) =>
            record != null && projectionOf.TryGetValue(record.Id, out var thing) ? thing : null;

        internal IEnumerable<GuRecord> ApertureOccupants(Hediff_Aperture aperture) =>
            !ready || CustodyTransitionInProgress || aperture?.ApertureId == null ? Enumerable.Empty<GuRecord>()
                : apertureOf.Where(binding => binding.Value == aperture && byId.TryGetValue(binding.Key, out var record)
                    && record.IsActionable && IsLiveAperture(aperture, record)).Select(binding => byId[binding.Key]);

        internal GuRecord ResolveApertureOccupant(Hediff_Aperture aperture)
        {
            var occupants = ApertureOccupants(aperture).Take(2).ToArray();
            return occupants.Length == 1 ? occupants[0] : null;
        }

        /// <summary>Observed form and holder, derived from the native graph without side effects.</summary>
        internal (GuObservedForm Form, string NativeId, string HolderId, Map Map) Observe(GuRecord record)
        {
            if (record == null || record.Existence != GuExistence.Living) return (GuObservedForm.Unavailable, null, null, null);
            if (record.Placement == GuPlacementKind.Contained)
            {
                var host = apertureOf.TryGetValue(record.Id, out var aperture) ? aperture.pawn : null;
                return (record.IsUnresolved ? GuObservedForm.Unavailable : GuObservedForm.Contained, record.ContainerId,
                    record.AnchorHint, host?.MapHeld);
            }
            if (!projectionOf.TryGetValue(record.Id, out var thing) || !MatchesManifestation(record, thing))
                return (GuObservedForm.Unavailable, record.ProjectionId, null, null);
            if (thing is Pawn pawn)
                return (pawn.Spawned ? GuObservedForm.ActivePawn : pawn.IsWorldPawn() ? GuObservedForm.WorldPawn : GuObservedForm.OtherHolder,
                    record.ProjectionId, null, pawn.MapHeld);
            Pawn holder = thing.ParentHolder switch
            {
                Pawn_InventoryTracker inventory => inventory.pawn,
                Pawn_CarryTracker carry => carry.pawn,
                _ => null
            };
            var form = thing.Spawned ? GuObservedForm.GroundThing : holder != null ? GuObservedForm.HeldThing : GuObservedForm.OtherHolder;
            return (form, record.ProjectionId, holder?.GetUniqueLoadID(), thing.MapHeld);
        }

        internal GuView View(GuRecord record)
        {
            var observed = Observe(record);
            return new(record.Id, record.SpeciesDefName, record.Rank, record.Existence, record.Condition, record.Placement,
                observed.Form, observed.NativeId, observed.HolderId, observed.Map?.GetUniqueLoadID(), record.CustodyRevision,
                record.StateRevision, record.Unresolved, record.TerminalEvidence, record.IsQuarantined, false);
        }

        internal bool TryGetView(Thing thing, out GuView view)
        {
            view = default;
            if (thing?.GuTag() is not { } tag) return false;
            if (Resolve(thing) is { } record)
            {
                view = View(record);
                return true;
            }
            var known = FindRecord(tag.GuId);
            view = new(tag.GuId, known?.SpeciesDefName, known?.Rank ?? 0, known?.Existence ?? GuExistence.Living,
                known?.Condition ?? GuCondition.Active, known?.Placement ?? GuPlacementKind.Manifested, GuObservedForm.Unavailable,
                thing.GetUniqueLoadID(), null, thing.MapHeld?.GetUniqueLoadID(), tag.GuRevision, known?.StateRevision ?? 0,
                known?.Unresolved ?? GuUnresolvedCause.None, known?.TerminalEvidence ?? GuTerminalEvidence.None,
                known?.IsQuarantined ?? false, true);
            return true;
        }

        internal static string InspectProjection(Thing thing, string native)
        {
            if (!Prefs.DevMode || thing.GuTag() is not { } tag) return native;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            string binding;
            if (registry?.Resolve(thing) is { } record)
            {
                var observed = registry.Observe(record);
                binding = record.DebugDescription + $"\nObserved: {observed.Form}" + (observed.HolderId == null ? "" : $" held by {observed.HolderId}");
            }
            else
            {
                string state = registry == null ? "registry unavailable"
                    : registry.CustodyTransitionInProgress ? "transition in progress"
                    : tag.Rogue ? "rogue projection, not authorized"
                    : registry.FindRecord(tag.GuId) is { } known ? $"not authorized ({(known.IsQuarantined ? "quarantined" : known.IsUnresolved ? $"unresolved: {known.Unresolved}" : "bound elsewhere")})"
                    : "no canonical record";
                binding = $"Gu {tag.GuId ?? "(missing ID)"} | tag revision {tag.GuRevision}\nCustody: {state}";
            }
            return string.IsNullOrEmpty(native) ? binding : native + "\n" + binding;
        }

        // ---- Index maintenance (mutating; coordinator, load and reconciliation only) ----------------------------

        private void BindProjection(GuRecord record, Thing projection)
        {
            UnbindAll(record);
            byProjection[projection] = record;
            projectionOf[record.Id] = projection;
            lastProjection[record.Id] = projection;
            if (projection.GuTag() is { } tag) rogues.Remove(tag);
        }

        private void BindAperture(GuRecord record, Hediff_Aperture aperture)
        {
            UnbindAll(record);
            apertureOf[record.Id] = aperture;
            lastHost[record.Id] = aperture.pawn;
        }

        private void UnbindAll(GuRecord record)
        {
            if (projectionOf.TryGetValue(record.Id, out var thing))
            {
                projectionOf.Remove(record.Id);
                if (byProjection.TryGetValue(thing, out var bound) && bound == record) byProjection.Remove(thing);
            }
            apertureOf.Remove(record.Id);
        }

        private void MarkRogue(CompGuManifestation tag, string reason)
        {
            if (tag == null || !rogues.Add(tag)) return;
            tag.Rogue = true;
            Log.Warning($"[Project GU] Rogue Gu projection {tag.parent?.GetUniqueLoadID()} claims Gu {tag.GuId} at revision {tag.GuRevision}. {reason} It is not authorized.");
        }

        private static Dictionary<string, Pawn> PawnsByLoadId() => PawnsFinder.All_AliveOrDead.Where(pawn => pawn != null).Distinct()
            .GroupBy(pawn => pawn.GetUniqueLoadID(), StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        internal bool TrySpawnDevelopmentGu(GuSpeciesDef species, Map map, IntVec3 cell, out Pawn_GuWildProjection pawn)
        {
            pawn = null;
            if (!Prefs.DevMode || !ready || CustodyTransitionInProgress || LongEventHandler.AnyEventNowOrWaiting
                || species?.wildProjectionKind is not { race.thingClass: { } pawnClass } kind
                || !typeof(Pawn_GuWildProjection).IsAssignableFrom(pawnClass)
                || map?.Disposed != false || !Find.Maps.Contains(map) || !cell.InBounds(map)
                || cell.Fogged(map) || !cell.Standable(map) || cell.GetFirstPawn(map) != null)
                return false;

            string id = Guid.NewGuid().ToString("N");
            if (byId.ContainsKey(id)) return false;

            GuRecord record = null;
            try
            {
                var projection = (Pawn_GuWildProjection)PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, tile: map.Tile,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true,
                    developmentalStages: DevelopmentalStage.Adult));
                record = GuRecord.CreateManifested(id, species, "development", map.GetUniqueLoadID(), Find.TickManager.TicksGame,
                    GuCondition.Active, GuManifestationKind.Pawn, projection.GetUniqueLoadID());
                projection.InitializeBinding(record);
                records.Add(record);
                byId[record.Id] = record;
                GenSpawn.Spawn(projection, cell, map);
                BindProjection(record, projection);
                if (!MatchesManifestation(record, projection) || !projection.Spawned)
                {
                    record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                        "Development placement did not establish the expected wild Pawn.", Find.TickManager.TicksGame);
                    UnbindAll(record);
                    return false;
                }
                pawn = projection;
                return true;
            }
            catch (Exception exception)
            {
                if (record != null)
                {
                    record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                        "A native callback interrupted development placement.", Find.TickManager.TicksGame);
                    UnbindAll(record);
                }
                Log.Error($"[Project GU] Failed to place a development Gu. {exception}");
                return false;
            }
        }
    }
}
