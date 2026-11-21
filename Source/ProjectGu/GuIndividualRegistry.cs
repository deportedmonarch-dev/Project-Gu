using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry : GameComponent
    {
        private List<GuIndividual> individuals = new();
        private readonly Dictionary<string, Thing> byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Thing, GuIndividual> byProjection = new();
        private bool ready;

        public GuIndividualRegistry(Game game) { }

        internal IReadOnlyList<GuIndividual> Individuals => individuals;
        internal bool CustodyTransitionInProgress { get; private set; }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars) ready = false;
            if (Scribe.mode == LoadSaveMode.Saving && CustodyTransitionInProgress)
                throw new InvalidOperationException("Wait for the Gu custody transition to finish before saving.");
            Scribe_Collections.Look(ref individuals, "guIndividualRecordsV1", LookMode.Deep);
            individuals ??= new();
        }

        public override void StartedNewGame() => RebuildBindings();
        public override void LoadedGame() => RebuildBindings();

        private void RebuildBindings()
        {
            byId.Clear();
            byProjection.Clear();
            if (individuals.Count > 0)
            {
                var records = individuals.Where(record => record != null).ToArray();
                var recordsById = records.ToLookup(record => record.Id, StringComparer.OrdinalIgnoreCase);
                var recordsByOwner = records.ToLookup(record => record.Custody.NativeOwnerId, StringComparer.Ordinal);
                var projections = FindProjections().Distinct().ToArray();
                var projectionsById = projections.ToLookup(thing => ((IGuProjection)thing).GuId, StringComparer.OrdinalIgnoreCase);
                var projectionsByOwner = projections.ToLookup(thing => thing.GetUniqueLoadID(), StringComparer.Ordinal);

                foreach (GuIndividual record in records)
                {
                    if (record.IsQuarantined) continue;
                    var endpoints = projectionsByOwner[record.Custody.NativeOwnerId];
                    string error = RecordError(record);
                    if (error == null && (recordsById[record.Id].Count() != 1 || recordsByOwner[record.Custody.NativeOwnerId].Count() != 1))
                        error = "Multiple records claim this Gu ID or native owner.";
                    if (error == null && (projectionsById[record.Id].Count() > 1 || endpoints.Count() > 1))
                        error = "Multiple Gu projections claim this Gu ID or native owner.";
                    if (error == null && !MatchesProjection(record, endpoints.SingleOrDefault()))
                        error = "The exact live projection, native holder and revision could not be resolved.";

                    if (error != null) record.Quarantine(error);
                    else Bind(record, endpoints.Single());
                }
            }
            ready = true;
        }

        private static bool HasConflictingProjection(GuIndividual record, Thing projection) =>
            FindProjections().Any(thing => thing != projection && (thing.GetUniqueLoadID() == record.Custody.NativeOwnerId
                || string.Equals(((IGuProjection)thing).GuId, record.Id, StringComparison.OrdinalIgnoreCase)));

        private static IEnumerable<Thing> FindProjections()
        {
            var pawns = PawnsFinder.All_AliveOrDead;
            foreach (var pawn in pawns.OfType<Pawn_GuWildProjection>()) yield return pawn;
            var dormantDefs = DefDatabase<GuSpeciesDef>.AllDefsListForReading.Select(species => species.dormantProjectionDef)
                .Where(def => def != null).Distinct().ToArray();
            var found = new List<Thing_GuDormantProjection>();
            foreach (Map map in Find.Maps)
                foreach (ThingDef def in dormantDefs)
                {
                    ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(def), found, false, null, true);
                    foreach (var thing in found) yield return thing;
                }
            foreach (Pawn pawn in pawns.Where(pawn => !pawn.Spawned && pawn.inventory != null))
                foreach (var thing in pawn.inventory.GetDirectlyHeldThings().OfType<Thing_GuDormantProjection>()) yield return thing;
        }

        private static string RecordError(GuIndividual record)
        {
            if (record.Version != GuIndividual.CurrentVersion) return "Unsupported Gu record version.";
            if (!Guid.TryParseExact(record.Id, "N", out _) || record.Revision < 1
                || string.IsNullOrWhiteSpace(record.Origin) || string.IsNullOrWhiteSpace(record.OriginId) || record.CreatedTick < 0)
                return "Invalid Gu identity, revision or provenance.";
            if (record.Species is not { } species) return "The saved Gu species is unavailable.";
            if (record.Rank < species.minRank || record.Rank > species.maxRank) return "The saved rank is outside the species range.";
            if (string.IsNullOrWhiteSpace(record.Custody.NativeOwnerId)) return "Invalid Gu native owner.";
            return record.Custody.Kind switch
            {
                GuCustodyKind.WildPawn when record.BiologicalState == GuBiologicalState.Active
                    && record.Custody.HolderPawnId == null => null,
                GuCustodyKind.DormantGround when record.BiologicalState == GuBiologicalState.Dormant
                    && record.Custody.HolderPawnId == null => null,
                GuCustodyKind.DormantInventory when record.BiologicalState == GuBiologicalState.Dormant
                    && !string.IsNullOrWhiteSpace(record.Custody.HolderPawnId) => null,
                _ => "Unsupported or invalid Gu custody."
            };
        }

        private static bool IsMapOwned(Thing thing) => thing?.Spawned == true && !thing.Destroyed
            && thing.Map is { Disposed: false } map && Find.Maps.Contains(map)
            && thing.holdingOwner == map.GetDirectlyHeldThings() && thing.ParentHolder == map;

        private static bool MatchesIdentity(GuIndividual record, Thing thing) =>
            thing is IGuProjection tag && !thing.Destroyed && thing.GetUniqueLoadID() == record.Custody.NativeOwnerId
            && tag.GuId == record.Id && tag.GuRevision == record.Revision;

        private static bool MatchesDormantThing(GuIndividual record, Thing_GuDormantProjection thing) =>
            MatchesIdentity(record, thing) && thing.stackCount == 1 && thing.def == record.Species?.dormantProjectionDef
            && Thing_GuDormantProjection.IsConfigured(thing.def);

        private static bool MatchesProjection(GuIndividual record, Thing thing) =>
            (record.Custody.Kind, thing) switch
            {
                (GuCustodyKind.WildPawn, Pawn_GuWildProjection pawn) => MatchesIdentity(record, pawn) && IsMapOwned(pawn)
                    && !pawn.Dead && pawn.kindDef == record.Species?.wildProjectionKind,
                (GuCustodyKind.DormantGround, Thing_GuDormantProjection dormant) => MatchesDormantThing(record, dormant) && IsMapOwned(dormant),
                (GuCustodyKind.DormantInventory, Thing_GuDormantProjection dormant) => MatchesDormantThing(record, dormant)
                    && dormant.ParentHolder is Pawn_InventoryTracker tracker && tracker.ParentHolder is Pawn holder
                    && holder.GetUniqueLoadID() == record.Custody.HolderPawnId && IsInInventory(dormant, InventoryOf(holder)),
                _ => false
            };

        private void Bind(GuIndividual record, Thing projection)
        {
            byId.Add(record.Id, projection);
            byProjection.Add(projection, record);
        }

        private void Unbind(GuIndividual record, Thing projection)
        {
            byId.Remove(record.Id);
            byProjection.Remove(projection);
        }

        internal GuIndividual Resolve(string id) => id != null && byId.TryGetValue(id, out var thing) ? Resolve(thing) : null;

        internal GuIndividual Resolve(Thing thing)
        {
            if (!ready || thing == null || !byProjection.TryGetValue(thing, out var record) || CustodyTransitionInProgress) return null;
            if (!record.IsQuarantined && MatchesProjection(record, thing)) return record;

            record.Quarantine("The bound Gu projection is no longer live in its expected native custody.");
            Unbind(record, thing);
            return null;
        }

        internal static string InspectProjection(Thing thing, string native)
        {
            if (!Prefs.DevMode || thing is not IGuProjection tag) return native;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var record = registry?.Resolve(thing);
            string custody = registry?.CustodyTransitionInProgress == true ? "transition in progress" : "quarantined, no authorized binding";
            string binding = record?.DebugDescription ?? $"Gu {tag.GuId ?? "(missing ID)"} | revision {tag.GuRevision}\nCustody: {custody}";
            return string.IsNullOrEmpty(native) ? binding : native + "\n" + binding;
        }

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
            if (individuals.Any(record => string.Equals(record?.Id, id, StringComparison.OrdinalIgnoreCase))) return false;

            GuIndividual individual = null;
            try
            {
                var projection = (Pawn_GuWildProjection)PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, tile: map.Tile,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true,
                    developmentalStages: DevelopmentalStage.Adult));
                individual = new GuIndividual(id, species, "development", map.GetUniqueLoadID(),
                    Find.TickManager.TicksGame, projection.GetUniqueLoadID());
                projection.InitializeBinding(individual);
                individuals.Add(individual);
                GenSpawn.Spawn(projection, cell, map);
                if (RecordError(individual) != null || !MatchesProjection(individual, projection))
                {
                    individual.Quarantine("Development placement did not establish the expected wild Pawn binding.");
                    return false;
                }
                Bind(individual, projection);
                pawn = projection;
                return true;
            }
            catch (Exception exception)
            {
                individual?.Quarantine("A native callback interrupted development placement.");
                Log.Error($"[Project GU] Failed to place a development Gu. {exception}");
                return false;
            }
        }
    }
}
