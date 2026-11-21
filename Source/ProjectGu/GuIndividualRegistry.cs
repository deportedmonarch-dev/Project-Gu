using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    public sealed class GuIndividualRegistry : GameComponent
    {
        private List<GuIndividual> individuals = new();
        private readonly Dictionary<string, Pawn_GuWildProjection> byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Pawn_GuWildProjection, GuIndividual> byPawn = new();
        private bool ready;

        public GuIndividualRegistry(Game game) { }

        internal IReadOnlyList<GuIndividual> Individuals => individuals;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars) ready = false;
            Scribe_Collections.Look(ref individuals, "guIndividualRecordsV1", LookMode.Deep);
            individuals ??= new();
        }

        public override void StartedNewGame() => RebuildBindings();
        public override void LoadedGame() => RebuildBindings();

        private void RebuildBindings()
        {
            byId.Clear();
            byPawn.Clear();
            if (individuals.Count > 0)
            {
                var records = individuals.Where(record => record != null).ToArray();
                var recordsById = records.ToLookup(record => record.Id, StringComparer.OrdinalIgnoreCase);
                var recordsByOwner = records.ToLookup(record => record.Custody.NativeOwnerId, StringComparer.Ordinal);
                var pawns = PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().Distinct().ToArray();
                var pawnsById = pawns.ToLookup(pawn => pawn.GuId, StringComparer.OrdinalIgnoreCase);
                var pawnsByOwner = pawns.ToLookup(pawn => pawn.GetUniqueLoadID(), StringComparer.Ordinal);

                foreach (GuIndividual record in records)
                {
                    if (record.IsQuarantined) continue;
                    var endpoints = pawnsByOwner[record.Custody.NativeOwnerId];
                    string error = RecordError(record);
                    if (error == null && (recordsById[record.Id].Count() != 1 || recordsByOwner[record.Custody.NativeOwnerId].Count() != 1))
                        error = "Multiple records claim this Gu ID or native owner.";
                    if (error == null && (pawnsById[record.Id].Count() > 1 || endpoints.Count() > 1))
                        error = "Multiple Pawn projections claim this Gu ID or native owner.";
                    if (error == null && !MatchesProjection(record, endpoints.SingleOrDefault()))
                        error = "The exact live map-owned Pawn and revision could not be resolved.";

                    if (error != null) record.Quarantine(error);
                    else Bind(record, endpoints.Single());
                }
            }
            ready = true;
        }

        private static string RecordError(GuIndividual record)
        {
            if (record.Version != GuIndividual.CurrentVersion) return "Unsupported Gu record version.";
            if (!Guid.TryParseExact(record.Id, "N", out _) || record.Revision < 1
                || string.IsNullOrWhiteSpace(record.Origin) || string.IsNullOrWhiteSpace(record.OriginId) || record.CreatedTick < 0)
                return "Invalid Gu identity, revision or provenance.";
            if (record.Species is not { } species) return "The saved Gu species is unavailable.";
            if (record.Rank < species.minRank || record.Rank > species.maxRank) return "The saved rank is outside the species range.";
            if (record.Custody.Kind != GuCustodyKind.WildPawn || record.BiologicalState != GuBiologicalState.Active
                || string.IsNullOrWhiteSpace(record.Custody.NativeOwnerId)) return "Unsupported or invalid Gu custody.";
            return null;
        }

        private static bool MatchesProjection(GuIndividual record, Pawn_GuWildProjection pawn) =>
            pawn?.Spawned == true && !pawn.Dead && !pawn.Destroyed
            && pawn.Map is { Disposed: false } map && Find.Maps.Contains(map)
            && pawn.holdingOwner == map.GetDirectlyHeldThings() && pawn.ParentHolder == map
            && pawn.GetUniqueLoadID() == record.Custody.NativeOwnerId
            && pawn.GuId == record.Id && pawn.GuRevision == record.Revision
            && pawn.kindDef == record.Species?.wildProjectionKind;

        private void Bind(GuIndividual record, Pawn_GuWildProjection pawn)
        {
            byId.Add(record.Id, pawn);
            byPawn.Add(pawn, record);
        }

        internal GuIndividual Resolve(string id) => id != null && byId.TryGetValue(id, out var pawn) ? Resolve(pawn) : null;

        internal GuIndividual Resolve(Pawn_GuWildProjection pawn)
        {
            if (!ready || pawn == null || !byPawn.TryGetValue(pawn, out var record)) return null;
            if (MatchesProjection(record, pawn)) return record;

            record.Quarantine("The bound wild Pawn is no longer live in its expected native custody.");
            byId.Remove(record.Id);
            byPawn.Remove(pawn);
            return null;
        }

        internal bool TrySpawnDevelopmentGu(GuSpeciesDef species, Map map, IntVec3 cell, out Pawn_GuWildProjection pawn)
        {
            pawn = null;
            if (!Prefs.DevMode || !ready || LongEventHandler.AnyEventNowOrWaiting
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
