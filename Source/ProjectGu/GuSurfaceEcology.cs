using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    /// <summary>Surface forest cohorts are owned by the game, with one cohort per species at each locality.</summary>
    public sealed class GuSurfaceEcology : GameComponent, IGuPopulationSourceDirectory
    {
        internal const string SourceKind = "surface";
        internal const string AmbientTrigger = "surface-ambient";
        private const int SampleTicks = 250;
        private const int ManifestationTicks = 2500;
        private const int HabitatSamples = 128;
        private const int Capacity = 4;
        private const int Threshold = 1;
        internal const int TicksPerDay = 60000;
        private Dictionary<string, Site> sites = new();
        private Dictionary<string, Locality> legacyLocalities;
        private static IEnumerable<GuSpeciesDef> SurfaceSpecies =>
            DefDatabase<GuSpeciesDef>.AllDefsListForReading.Where(IsSurfaceSpecies);

        private static bool IsSurfaceSpecies(GuSpeciesDef species) =>
            species is { surfaceForestPopulation: true, wildPopulationEnabled: true };

        public GuSurfaceEcology(Game game) { }

        internal static GuSurfaceEcology Instance => Current.Game?.GetComponent<GuSurfaceEcology>();
        private static GuIndividualRegistry Registry => Current.Game?.GetComponent<GuIndividualRegistry>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sites, "guSurfaceSites", LookMode.Value, LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.Saving)
                Scribe_Collections.Look(ref legacyLocalities, "guSurfaceLocalities", LookMode.Value, LookMode.Deep);
            sites ??= new();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && legacyLocalities != null)
            {
                foreach (var (key, locality) in legacyLocalities)
                    if (locality?.Cohort is { } cohort)
                        GetSite(key).Populations.TryAdd(cohort.SpeciesDefName, locality);
                legacyLocalities = null;
            }
        }

        internal static int Suitability(BiomeDef biome) => biome?.defName switch
        {
            "TemperateForest" or "BorealForest" => 3,
            "TropicalRainforest" => 2,
            "TemperateSwamp" or "TropicalSwamp" or "ColdBog" or "AridShrubland" => 1,
            _ => 0
        };

        private static string Key(Map map) => $"{map.Tile.Layer.LayerID}/{map.Tile.tileId}/0";

        private static bool Eligible(Map map) => GuPocketProbeComponent.IsSurfaceSettlement(map) && Suitability(map.Biome) > 0;

        private Site GetSite(string key)
        {
            if (!sites.TryGetValue(key, out var site))
                sites.Add(key, site = new Site());
            return site;
        }

        private Locality GetOrInitialize(Map map, string key, GuSpeciesDef species)
        {
            var populations = GetSite(key).Populations;
            if (populations.TryGetValue(species.defName, out var locality)) return locality;
            int seed = GenText.StableStringHash($"{Find.World.info.seedString}/{key}/{species.defName}/1");
            int initial = 0;
            for (int i = 0; i < 3; i++)
                if (Rand.ValueSeeded(Gen.HashCombineInt(seed, i)) < Suitability(map.Biome) * 0.25f) initial++;
            var cohort = new GuPopulationRecord(species.defName, initial, 0, -1);
            if (!Registry.SurfaceEvidenceIntact(key, cohort)) return null;
            locality = new Locality { Cohort = cohort, Seed = seed };
            populations.Add(species.defName, locality);
            return locality;
        }

        private static bool ResidentOf(GuRecord record, string key) => record is { IsLiving: true, SurfaceResidencyEnded: false }
            && record.CreationEvidence is { OwnerKind: SourceKind, SourceId: var source } && source == key;

        private static GuRecord[] Residents(string key, string species) => Registry.Records
            .Where(record => record.SpeciesDefName == species && ResidentOf(record, key)).Take(2).ToArray();

        private static bool Countable(GuRecord record, Map map) => record.IsActionable && record.Condition == GuCondition.Active
            && record.Refinement?.CultivatorId == null && (record.ContainerKind == GuContainerKind.WorldSite
                ? record.LastKnown?.IsAt(map) == true
                : Registry.ProjectionOf(record) is Pawn_GuWildProjection { Dead: false, Spawned: true, Faction: null } pawn
                    && pawn.Map == map && Registry.Observe(record).Form == GuObservedForm.ActivePawn);

        internal Locality LocalityAt(Map map, string species) =>
            map?.Tile.Valid == true && sites.TryGetValue(Key(map), out var site)
                ? site.Populations.GetValueOrDefault(species) : null;

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % SampleTicks != 0 || Registry == null || Registry.CustodyTransitionInProgress) return;
            foreach (var map in Find.Maps)
            {
                if (!Eligible(map)) continue;
                string key = Key(map);
                if (!UniqueMap(map, key)) continue;
                foreach (var species in SurfaceSpecies)
                {
                    var locality = GetOrInitialize(map, key, species);
                    if (locality?.Cohort == null) continue;
                    var residents = Residents(key, species.defName);
                    if (residents.Length > 1 || residents.Any(record => !Countable(record, map))) continue;
                    locality.Advance(now, map, key, residents.Length);
                    if (now % ManifestationTicks != 0) continue;
                    var resident = residents.SingleOrDefault();
                    if (resident != null ? resident.ContainerKind == GuContainerKind.WorldSite : locality.Cohort.MatureCount > 0)
                        TryManifest(map, key, sites[key], locality, resident, now);
                }
            }
        }

        private static bool UniqueMap(Map map, string key) =>
            !Find.Maps.Any(other => other != map && other.Tile.Valid && Key(other) == key);

        internal static bool SearchGround(Map map, IntVec3 cell) => Eligible(map) && HabitatCell(map, cell);

        private static bool HabitatCell(Map map, IntVec3 cell) => cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map)
            && !cell.Roofed(map) && cell.GetTerrain(map) is { natural: true, fertility: > 0f, dangerous: false }
            && Suitability(map.BiomeAt(cell)) > 0;

        internal static bool AmbientHabitat(Map map, IntVec3 cell) => HabitatCell(map, cell)
            && cell.GetRoom(map)?.PsychologicallyOutdoors == true && !map.reservationManager.IsReserved(cell);

        // Detection only reads live projections. Callers decide when to search and how to present the result.
        internal static Pawn_GuWildProjection FindWildGu(Pawn actor, IntVec3 center, float radius, GuSpeciesDef target = null)
        {
            if (actor is not { Spawned: true, Downed: false } || Registry is not { } registry || !center.InBounds(actor.Map))
                return null;
            var map = actor.Map;
            foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map) || cell.Fogged(map)) continue;
                foreach (var thing in cell.GetThingList(map))
                {
                    if (thing is Pawn_GuWildProjection { Spawned: true, Dead: false, Faction: null } wild
                        && registry.Resolve(wild) is { IsActionable: true, IsLiving: true, Condition: GuCondition.Active } record
                        && record.Refinement?.CultivatorId == null && (target == null || record.Species == target)
                        && actor.CanReach(wild, PathEndMode.Touch, actor.NormalMaxDanger()))
                        return wild;
                }
            }
            return null;
        }

        private static IntVec3 FindHabitat(Map map, int seed)
        {
            Rand.PushState(seed);
            try
            {
                for (int i = 0; i < HabitatSamples; i++)
                {
                    var cell = CellFinder.RandomCell(map);
                    if (AmbientHabitat(map, cell) && GuIndividualRegistry.CanPlaceDormant(map, cell, null))
                        return cell;
                }
                return IntVec3.Invalid;
            }
            finally { Rand.PopState(); }
        }

        private static void TryManifest(Map map, string key, Site site, Locality locality, GuRecord returning, int now)
        {
            if (site.ActiveManifestation != null) return;
            var cell = FindHabitat(map, Gen.HashCombineInt(locality.Seed, now));
            if (!cell.IsValid) return;

            site.ActiveManifestation = new Manifestation(map, cell, locality);
            try
            {
                var source = new Source(key, site);
                if (returning != null)
                {
                    Registry.TryRecoverSurfaceResident(returning, map, cell,
                        () => source.PlacementReady(map, cell, AmbientTrigger) && Countable(returning, map), out _);
                    return;
                }

                var cohort = locality.Cohort;
                string eventKey = $"{SourceKind}/{key}/{cohort.SpeciesDefName}/{cohort.Revision}";
                if (GuIndividualRegistry.TryPrepareSourceRequest(source, cohort.SpeciesDefName, AmbientTrigger, null, eventKey, out var request))
                    Registry.TryMaterializeFromSource(source, request, map, cell, out _, out _);
            }
            finally
            {
                site.ActiveManifestation = null;
            }
        }

        void IGuPopulationSourceDirectory.CollectSources(string ownerKind, string sourceId, List<IGuPopulationSource> into)
        {
            if (ownerKind == SourceKind && sites.TryGetValue(sourceId, out var site))
                into.Add(new Source(sourceId, site));
        }

        private sealed record Manifestation(Map Map, IntVec3 Cell, Locality Locality);

        private sealed class Site : IExposable
        {
            public Site() { }

            public Dictionary<string, Locality> Populations = new();
            public Manifestation ActiveManifestation;

            public void ExposeData()
            {
                Scribe_Collections.Look(ref Populations, "populations", LookMode.Value, LookMode.Deep);
                Populations ??= new();
            }
        }

        internal sealed class Locality : IExposable
        {
            public GuPopulationRecord Cohort;
            public int Seed;
            public int ReplacementOrdinal;
            // -1 means the threshold has not latched. A running clock advances only on eligible, loaded maps.
            public int ReplacementTicks = -1;
            private int lastMapId = -1;
            private int lastSample = -1;

            internal void Advance(int now, Map map, string key, int residents)
            {
                int elapsed = lastMapId == map.uniqueID && lastSample == now - SampleTicks ? SampleTicks : 0;
                lastMapId = map.uniqueID;
                lastSample = now;
                long occupied = Cohort.TotalCount + residents;
                if (occupied >= Capacity)
                {
                    ReplacementTicks = -1;
                    return;
                }
                if (ReplacementTicks < 0)
                {
                    if (occupied <= Threshold) ScheduleReplacement();
                    return;
                }
                ReplacementTicks = Math.Max(0, ReplacementTicks - elapsed);
                if (ReplacementTicks != 0 || !Registry.SurfaceEvidenceIntact(key, Cohort) || !Cohort.TryRecruitMature(Capacity, residents)) return;
                if (occupied + 1 >= Capacity) ReplacementTicks = -1;
                else ScheduleReplacement();
            }

            private void ScheduleReplacement()
            {
                int roll = Gen.HashCombineInt(Seed, ReplacementOrdinal++);
                ReplacementTicks = Rand.RangeInclusiveSeeded(3 * TicksPerDay, 9 * TicksPerDay, roll);
            }

            public void ExposeData()
            {
                Scribe_Deep.Look(ref Cohort, "cohort");
                Scribe_Values.Look(ref Seed, "seed");
                Scribe_Values.Look(ref ReplacementOrdinal, "replacementOrdinal");
                Scribe_Values.Look(ref ReplacementTicks, "replacementTicks", -1);
            }
        }

        private sealed class Source(string key, Site site) : IGuPopulationSource
        {
            public string OwnerKind => SourceKind;
            public string SourceId => key;
            public object Binding => site;
            private Manifestation Active => site.ActiveManifestation;
            public string CurrentCredential => Active?.Map.GetUniqueLoadID();
            public bool HasUniqueIdentity => Active?.Map is { } map && Eligible(map) && Key(map) == key && UniqueMap(map, key);
            public bool RequiresDevelopmentMode(string trigger) => false;
            public GuPopulationRecord CohortFor(string species) => site.Populations.GetValueOrDefault(species)?.Cohort;
            public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, IGuSourceAsset asset) =>
                IsSurfaceSpecies(species) && Active?.Locality == site.Populations.GetValueOrDefault(species.defName)
                && rank == 1 && stage == GuCohortStage.Mature && asset == null && trigger == AmbientTrigger
                && HasUniqueIdentity && Residents(key, species.defName).Length == 0
                    ? null : "No qualifying surface manifestation can issue this Gu.";
            public string PlacementRefusal => "The Gu needs open, suitable natural habitat.";
            public bool PlacementReady(Map map, IntVec3 cell, string trigger) => trigger == AmbientTrigger
                && Active is { } active && active.Map == map && active.Cell == cell && HasUniqueIdentity && AmbientHabitat(map, cell);
        }
    }
}
