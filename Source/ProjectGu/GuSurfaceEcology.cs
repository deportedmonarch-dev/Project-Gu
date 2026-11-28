using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    public enum GuSurfaceHabitat { None, Forest, Rocky }

    /// <summary>Surface cohorts are owned by the game, with one cohort per species at each locality.</summary>
    public sealed partial class GuSurfaceEcology : GameComponent, IGuPopulationSourceDirectory
    {
        internal const string SourceKind = "surface";
        internal const string AmbientTrigger = "surface-ambient";
        private const int SampleTicks = 250;
        private const int ManifestationTicks = 2500;
        private const int HabitatSamples = 128;
        private const int Capacity = 4;
        private const int Threshold = 1;
        internal const int SpeciesManifestationLimit = 2;
        internal const int DefaultManifestationBudget = 6;
        internal const int MaximumManifestationBudget = 32;
        internal const int TicksPerDay = 60000;
        private Dictionary<string, Site> sites = new();
        private Dictionary<string, Locality> legacyLocalities;
        private static IEnumerable<GuSpeciesDef> SurfaceSpecies =>
            DefDatabase<GuSpeciesDef>.AllDefsListForReading.Where(species => species.HasSurfacePopulation);

        public GuSurfaceEcology(Game game) { }

        internal static GuSurfaceEcology Instance => Current.Game?.GetComponent<GuSurfaceEcology>();
        private static GuIndividualRegistry Registry => Current.Game?.GetComponent<GuIndividualRegistry>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref sites, "guSurfaceSites", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref regions, "guSurfaceRegions", LookMode.Value, LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.Saving)
                Scribe_Collections.Look(ref legacyLocalities, "guSurfaceLocalities", LookMode.Value, LookMode.Deep);
            sites ??= new();
            regions ??= new();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) scheduleReady = false;
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

        internal static bool IsForest(BiomeDef biome) => biome?.defName is "TemperateForest" or "BorealForest" or "TropicalRainforest";

        internal static int Suitability(GuSurfaceHabitat habitat, BiomeDef biome, Hilliness hilliness, bool hasExposedRock) => habitat switch
        {
            GuSurfaceHabitat.Forest => Suitability(biome),
            GuSurfaceHabitat.Rocky when hilliness is Hilliness.LargeHills or Hilliness.Mountainous or Hilliness.Impassable => 3,
            GuSurfaceHabitat.Rocky when hilliness == Hilliness.SmallHills => 2,
            GuSurfaceHabitat.Rocky when hasExposedRock && IsForest(biome) => 1,
            _ => 0
        };

        internal static int Suitability(Map map, GuSpeciesDef species) => Suitability(map, species, out _);

        private static int Suitability(Map map, GuSpeciesDef species, out bool exposedRock)
        {
            var hilliness = Find.WorldGrid[map.Tile].hilliness;
            bool needsRock = species.surfaceProfile?.NeedsRockObservation(map.Biome, hilliness)
                ?? (species.surfaceHabitat == GuSurfaceHabitat.Rocky && hilliness == Hilliness.Flat && IsForest(map.Biome));
            exposedRock = needsRock && HasExposedRock(map);
            return Weight(species, map.Biome, hilliness, exposedRock);
        }

        private static int Weight(GuSpeciesDef species, BiomeDef biome, Hilliness hilliness, bool exposedRock) =>
            species.surfaceProfile?.Weight(biome, hilliness, exposedRock) ?? Suitability(species.surfaceHabitat, biome, hilliness, exposedRock);

        private static bool HasExposedRock(Map map) => Find.World.NaturalRockTypesIn(map.Tile).Any(rock =>
            map.listerThings.ThingsOfDef(rock).Any(formation => GenAdj.CardinalDirections.Any(offset =>
                OpenNaturalGround(map, formation.Position + offset))));

        private static string Key(Map map) => Key(map.Tile);
        private static string Key(PlanetTile tile) => $"{tile.Layer.LayerID}/{tile.tileId}/0";

        private Site GetSite(string key)
        {
            if (!sites.TryGetValue(key, out var site))
                sites.Add(key, site = new Site());
            return site;
        }

        private Locality GetOrInitialize(string key, GuSpeciesDef species, int suitability)
        {
            var populations = GetSite(key).Populations;
            if (populations.TryGetValue(species.defName, out var locality)) return locality;
            int seed = GenText.StableStringHash($"{Find.World.info.seedString}/{key}/{species.defName}/1");
            int initial = 0;
            for (int i = 0; i < 3; i++)
                if (Rand.ValueSeeded(Gen.HashCombineInt(seed, i)) < suitability * 0.25f) initial++;
            var cohort = new GuPopulationRecord(species.defName, initial, 0, -1);
            if (!Registry.SurfaceEvidenceIntact(key, cohort)) return null;
            locality = new Locality { Cohort = cohort, Seed = seed, Established = initial > 0, FoundingCount = initial };
            populations.Add(species.defName, locality);
            return locality;
        }

        private static GuRecord[] Residents(string key, string species) => Registry.SurfaceResidents(key, species, Capacity + 1);

        private static bool Countable(GuRecord record, PlanetTile tile, Map map)
        {
            if (!record.IsActionable || record.Condition != GuCondition.Active || record.Refinement?.CultivatorId != null) return false;
            if (IsWorldResident(record)) return IsRecordedLocality();
            if (Registry.ProjectionOf(record) is not Pawn_GuWildProjection { Dead: false, Faction: null } pawn) return false;
            var observed = Registry.Observe(record).Form;
            return pawn.Spawned ? map != null && pawn.Map == map && observed == GuObservedForm.ActivePawn
                : observed == GuObservedForm.WorldPawn && pawn.GetCaravan() == null && pawn.holdingOwner == null && IsRecordedLocality();

            // A resident still occupies one habitat unit while its exact native parent is unavailable for recovery.
            bool IsRecordedLocality() => record.LastKnown is { MapParentId: not null } location
                && location.Tile == tile.tileId && location.Layer == tile.Layer.LayerID;
        }

        private static bool IsWorldResident(GuRecord record) =>
            record.Placement == GuPlacementKind.Contained && record.ContainerKind == GuContainerKind.WorldSite;

        internal Locality LocalityAt(Map map, string species) =>
            map?.Tile.Valid == true ? LocalityAt(map.Tile, species) : null;

        internal Locality LocalityAt(PlanetTile tile, string species) =>
            tile.Valid && sites.TryGetValue(Key(tile), out var site) ? site.Populations.GetValueOrDefault(species) : null;

        internal GuPopulationRecord CohortAt(string locality, string species) =>
            sites.TryGetValue(locality, out var site) ? site.Populations.GetValueOrDefault(species)?.Cohort : null;

        internal Locality RegionalPopulation(string region, string species) =>
            region != null && regions.TryGetValue(region, out var entry) ? entry.Populations.GetValueOrDefault(species) : null;

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % SampleTicks != 0 || Registry == null || Registry.CustodyTransitionInProgress || Registry.MapTeardownInProgress) return;
            EnsureSchedule(now);
            var speciesList = SurfaceSpecies.ToArray();
            bool manifestationTurn = now % ManifestationTicks == 0;
            int first = speciesList.Length == 0 ? 0 : now / ManifestationTicks % speciesList.Length;
            foreach (var map in Find.Maps)
            {
                if (!GuPocketProbeComponent.IsSurfaceSettlement(map)) continue;
                string key = Key(map);
                if (!UniqueMap(map, key)) continue;
                for (int offset = 0; offset < speciesList.Length; offset++)
                {
                    var species = speciesList[(first + offset) % speciesList.Length];
                    int suitability = Suitability(map, species, out bool exposedRock);
                    var locality = GetOrInitialize(key, species, suitability);
                    if (locality?.Cohort == null) continue;
                    var residents = Residents(key, species.defName);
                    ObserveLoaded(map, key, locality, species, suitability, exposedRock, residents, now);
                    foreach (var candidate in residents)
                        Registry.TryReturnSurfaceResident(candidate.Id, candidate.CustodyRevision, map, out _);
                    if (!manifestationTurn || suitability == 0 || residents.Length > Capacity
                        || residents.Any(record => !Countable(record, map.Tile, map))) continue;
                    var resident = residents.FirstOrDefault(record => IsWorldResident(record) && record.LastKnown?.IsAt(map) == true);
                    bool hasRoom = HasManifestationRoom(map, species.defName);
                    if (resident != null ? hasRoom || HasRetention(resident, now)
                        : hasRoom && locality.Cohort.MatureCount > 0 && now >= locality.NextManifestationTick)
                        TryManifest(map, key, sites[key], locality, species, resident, now);
                }
            }
            ServiceSchedule(now);
        }

        private static bool HasRetention(GuRecord record, int now) => record.EncounterProtected(now,
            Math.Clamp(ProjectGuMod.Settings?.encounterQuietDays ?? 1, 1, 600) * TicksPerDay,
            Math.Clamp(ProjectGuMod.Settings?.encounterHistoryDays ?? 60, 1, 600) * TicksPerDay);

        private static bool HasManifestationRoom(Map map, string species)
        {
            int budget = Math.Clamp(ProjectGuMod.Settings?.localityManifestationBudget ?? DefaultManifestationBudget,
                1, MaximumManifestationBudget);
            int total = 0, sameSpecies = 0;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn is not Pawn_GuWildProjection wild || wild.ReleaseSuppressed
                    || Registry.FindRecord(wild.GuId) is not { Species: { HasSurfacePopulation: true } } record
                    || Registry.ProjectionOf(record) != wild || !GuIndividualRegistry.IsAuthorized(record, wild)) continue;
                if (++total >= budget || record.SpeciesDefName == species && ++sameSpecies >= SpeciesManifestationLimit) return false;
            }
            return true;
        }

        private static bool UniqueMap(Map map, string key) =>
            !Find.Maps.Any(other => other != map && other.Tile.Valid && Key(other) == key);

        internal static bool SearchGround(Map map, IntVec3 cell) => GuPocketProbeComponent.IsSurfaceSettlement(map)
            && OpenNaturalGround(map, cell) && !cell.Fogged(map) && SurfaceSpecies.Any(species => Suitability(map, species) > 0
                && (species.surfaceHabitat == GuSurfaceHabitat.Rocky || ForestGround(map, cell)));

        private static bool OpenNaturalGround(Map map, IntVec3 cell) => cell.InBounds(map) && cell.Standable(map)
            && !cell.Roofed(map) && cell.GetTerrain(map) is { natural: true, dangerous: false, IsWater: false };

        private static bool ForestGround(Map map, IntVec3 cell) => cell.GetTerrain(map).fertility > 0f && Suitability(map.BiomeAt(cell)) > 0;

        internal static bool AmbientHabitat(Map map, IntVec3 cell, GuSurfaceHabitat habitat = GuSurfaceHabitat.Forest) =>
            OpenNaturalGround(map, cell) && cell.GetRoom(map)?.PsychologicallyOutdoors == true && !map.reservationManager.IsReserved(cell)
            && (habitat == GuSurfaceHabitat.Forest ? ForestGround(map, cell) : habitat == GuSurfaceHabitat.Rocky && NearExposedRock(map, cell));

        private static bool NearExposedRock(Map map, IntVec3 cell)
        {
            foreach (var nearby in GenRadial.RadialCellsAround(cell, 5f, false))
                if (nearby.InBounds(map) && nearby.GetEdifice(map)?.def.building is { isNaturalRock: true, isResourceRock: false }
                    && GenSight.LineOfSight(cell, nearby, map, true)) return true;
            return false;
        }

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

        private static IntVec3 FindHabitat(Map map, GuSurfaceHabitat habitat, int seed)
        {
            Rand.PushState(seed);
            try
            {
                for (int i = 0; i < HabitatSamples; i++)
                {
                    var cell = CellFinder.RandomCell(map);
                    if (AmbientHabitat(map, cell, habitat) && GuIndividualRegistry.CanPlaceSurfaceWild(map, cell, null))
                        return cell;
                }
                return IntVec3.Invalid;
            }
            finally { Rand.PopState(); }
        }

        private static void TryManifest(Map map, string key, Site site, Locality locality, GuSpeciesDef species, GuRecord returning, int now)
        {
            if (site.ActiveManifestation != null) return;
            var cell = FindHabitat(map, species.surfaceHabitat, Gen.HashCombineInt(locality.Seed, now));
            if (!cell.IsValid) return;

            site.ActiveManifestation = new Manifestation(map, cell, locality, species);
            try
            {
                var source = new Source(key, site);
                if (returning != null)
                {
                    Registry.TryRecoverSurfaceResident(returning, map, cell,
                        () => source.PlacementReady(map, cell, AmbientTrigger) && Countable(returning, map.Tile, map)
                            && (HasManifestationRoom(map, species.defName) || HasRetention(returning, now)), out _);
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

        private sealed record Manifestation(Map Map, IntVec3 Cell, Locality Locality, GuSpeciesDef Species);

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

        private sealed class Source(string key, Site site) : IGuPopulationSource
        {
            public string OwnerKind => SourceKind;
            public string SourceId => key;
            public object Binding => site;
            private Manifestation Active => site.ActiveManifestation;
            public string CurrentCredential => Active?.Map.GetUniqueLoadID();
            public bool HasUniqueIdentity => Active is { } active && GuPocketProbeComponent.IsSurfaceSettlement(active.Map)
                && Suitability(active.Map, active.Species) > 0 && Key(active.Map) == key && UniqueMap(active.Map, key);
            public bool RequiresDevelopmentMode(string trigger) => false;
            public GuPopulationRecord CohortFor(string species) => site.Populations.GetValueOrDefault(species)?.Cohort;
            public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, IGuSourceAsset asset)
            {
                const string refusal = "No qualifying surface manifestation can issue this Gu.";
                if (species?.HasSurfacePopulation != true || Active?.Species != species
                    || Active.Locality != site.Populations.GetValueOrDefault(species.defName) || rank != 1
                    || stage != GuCohortStage.Mature || asset != null || trigger != AmbientTrigger
                    || !HasUniqueIdentity || !HasManifestationRoom(Active.Map, species.defName)) return refusal;
                var residents = Residents(key, species.defName);
                return Active.Locality.Cohort.TotalCount + residents.Length <= Capacity
                    && residents.All(resident => Countable(resident, Active.Map.Tile, Active.Map)) ? null : refusal;
            }
            public string PlacementRefusal => "The Gu needs open, suitable natural habitat.";
            public bool PlacementReady(Map map, IntVec3 cell, string trigger) => trigger == AmbientTrigger
                && Active is { } active && active.Map == map && active.Cell == cell && HasUniqueIdentity
                && AmbientHabitat(map, cell, active.Species.surfaceHabitat);
        }
    }
}
