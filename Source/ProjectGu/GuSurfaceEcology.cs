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
        private const string Proximity = "surface-proximity";
        private const string Search = "surface-search";
        private const int SampleTicks = 250;
        private const int EncounterTicks = 2500;
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
                    if (now % EncounterTicks != 0 || residents.Any(record => record.ContainerKind != GuContainerKind.WorldSite)) continue;
                    var colonists = map.mapPawns.FreeColonistsSpawned;
                    if (colonists.Count == 0) continue;
                    // Rotate the witness so a colonist exploring away from the base can also make the discovery.
                    var actor = colonists[(now / EncounterTicks + map.uniqueID) % colonists.Count];
                    TryDiscover(actor, actor.Position, deliberate: false, out _, species);
                }
            }
        }

        private static bool UniqueMap(Map map, string key) =>
            !Find.Maps.Any(other => other != map && other.Tile.Valid && Key(other) == key);

        private static bool Witness(Pawn actor, Map map) => actor is { IsColonistPlayerControlled: true, Spawned: true, Downed: false }
            && actor.Map == map;

        internal static bool SearchGround(Map map, IntVec3 cell) => Eligible(map) && HabitatCell(map, cell);

        private static bool HabitatCell(Map map, IntVec3 cell) => cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map)
            && !cell.Roofed(map) && cell.GetTerrain(map).fertility > 0f && Suitability(map.BiomeAt(cell)) > 0;

        internal bool TryDiscover(Pawn actor, IntVec3 center, bool deliberate, out string reason, GuSpeciesDef target = null)
        {
            reason = "There is no suitable wild Gu habitat here.";
            Map map = actor?.Map;
            if (!Eligible(map) || !Witness(actor, map) || !center.InBounds(map)
                || deliberate && (!HabitatCell(map, center) || actor.Position.DistanceToSquared(center) > 2)) return false;
            string key = Key(map);
            if (!UniqueMap(map, key)) return false;
            if (target != null)
                return IsSurfaceSpecies(target) && TryDiscoverSpecies(actor, center, deliberate, key, target, out reason);

            reason = "You found no signs of wild Gu here.";
            foreach (var species in SurfaceSpecies)
                if (TryDiscoverSpecies(actor, center, deliberate, key, species, out _)) return true;
            return false;
        }

        private bool TryDiscoverSpecies(Pawn actor, IntVec3 center, bool deliberate, string key, GuSpeciesDef species, out string reason)
        {
            reason = "There is no available encounter for this Gu.";
            Map map = actor.Map;
            var locality = GetOrInitialize(map, key, species);
            if (locality?.Cohort == null || sites[key].ActiveEncounter != null) return false;
            var residents = Residents(key, species.defName);
            var returning = residents.Length == 1 && residents[0].ContainerKind == GuContainerKind.WorldSite ? residents[0] : null;
            reason = $"There is already a wild {species.label} in this locality.";
            if (residents.Length != 0 && (returning == null || !Countable(returning, map))) return false;
            reason = $"You found no signs of {species.label} here.";
            if (returning == null && locality.Cohort.MatureCount < 1) return false;
            string trigger = deliberate ? Search : Proximity;
            int radius = deliberate ? 6 : 19;
            sites[key].ActiveEncounter = locality;
            locality.Witness = actor;
            locality.Origin = center;
            locality.Trigger = trigger;
            try
            {
                var source = new Source(key, sites[key]);
                // An ambient appearance starts away from its witness. A deliberate search may reveal one much closer.
                var cells = GenRadial.RadialCellsAround(center, radius, false);
                foreach (var cell in deliberate ? cells : cells.Reverse())
                {
                    if (!source.PlacementReady(map, cell, trigger) || !GuIndividualRegistry.CanPlaceDormant(map, cell, null)
                        || map.reservationManager.IsReserved(cell)) continue;
                    if (returning != null)
                    {
                        if (!Registry.TryRecoverSurfaceResident(returning, map, cell,
                            () => source.PlacementReady(map, cell, trigger) && Countable(returning, map), out reason)) return false;
                    }
                    else
                    {
                        var request = new GuSourceRequest($"{SourceKind}/{key}/{species.defName}/{locality.Cohort.Revision}", SourceKind,
                            key, source.CurrentCredential, species.defName, 1, GuCohortStage.Mature, trigger, null, locality.Cohort.Revision);
                        if (Registry.TryMaterializeFromSource(source, request, map, cell, out _, out reason) != GuSourceOutcome.Committed)
                            return false;
                    }
                    Messages.Message($"{actor.LabelShort} discovered a wild {species.label}.", new TargetInfo(cell, map),
                        MessageTypeDefOf.NeutralEvent, false);
                    reason = null;
                    return true;
                }
                return false;
            }
            finally
            {
                locality.Witness = null;
                sites[key].ActiveEncounter = null;
            }
        }

        void IGuPopulationSourceDirectory.CollectSources(string ownerKind, string sourceId, List<IGuPopulationSource> into)
        {
            if (ownerKind == SourceKind && sites.TryGetValue(sourceId, out var site))
                into.Add(new Source(sourceId, site));
        }

        private sealed class Site : IExposable
        {
            public Site() { }

            public Dictionary<string, Locality> Populations = new();
            public Locality ActiveEncounter;

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

            internal Pawn Witness;
            internal IntVec3 Origin;
            internal string Trigger;

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
            private Locality Encounter => site.ActiveEncounter;
            public string CurrentCredential => Encounter?.Witness?.Map?.GetUniqueLoadID();
            public bool HasUniqueIdentity => Encounter?.Witness?.Map is { } map && UniqueMap(map, key);
            public bool RequiresDevelopmentMode(string trigger) => false;
            public GuPopulationRecord CohortFor(string species) => site.Populations.GetValueOrDefault(species)?.Cohort;
            public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, IGuSourceAsset asset) =>
                IsSurfaceSpecies(species) && species.defName == Encounter?.Cohort?.SpeciesDefName && rank == 1 && stage == GuCohortStage.Mature && asset == null
                && trigger is Search or Proximity && trigger == Encounter.Trigger && HasUniqueIdentity
                && Eligible(Encounter.Witness.Map) && Key(Encounter.Witness.Map) == key && Residents(key, species.defName).Length == 0
                    ? null : "No qualifying surface encounter can issue this Gu.";
            public string PlacementRefusal => "The Gu needs reachable, visible natural ground near the discoverer.";
            public bool PlacementReady(Map map, IntVec3 cell, string trigger) => Encounter != null && Eligible(map) && Key(map) == key
                && trigger == Encounter.Trigger && Witness(Encounter.Witness, map) && HabitatCell(map, cell)
                && (trigger == Search ? Encounter.Origin.DistanceToSquared(cell) <= 36
                    && Encounter.Witness.Position.DistanceToSquared(Encounter.Origin) <= 2
                    : Encounter.Witness.Position.DistanceToSquared(cell) is >= 49 and <= 361)
                && Encounter.Witness.CanReach(cell, PathEndMode.OnCell, Encounter.Witness.NormalMaxDanger());
        }
    }
}
