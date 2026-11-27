using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Alpha 6 owner requirement: Gu populations coexist with ordinary RimWorld wildlife without consuming its population
/// budget. Native positive and negative controls on the same home map with an equivalent ordinary wildlife population,
/// with and without manifested, registered wild Gu: occupancy weight, desired weight and density, the ecosystem-full gate,
/// a matched-seed replenishment tick, and the inherited eligibility facts recorded for the Slice 1A gate (RFC-0007).
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Slice0Wildlife => GenCommandLine.CommandLineArgPassed("guslice0wildlife");
    private static string WildlifeStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-slice0-wildlife.json");
    private record WildlifeState(string MapId, List<string> GuIds, float Desired, int Ordinary);

    private static readonly PropertyInfo CurrentWeightProperty = AccessTools.Property(typeof(WildAnimalSpawner), "CurrentTotalAnimalWeight");
    private static readonly PropertyInfo DesiredWeightProperty = AccessTools.Property(typeof(WildAnimalSpawner), "DesiredTotalAnimalWeight");
    private static readonly PropertyInfo DesiredDensityProperty = AccessTools.Property(typeof(WildAnimalSpawner), "DesiredAnimalDensity");

    private static float CurrentWeight(Map map) => (float)CurrentWeightProperty.GetValue(map.wildAnimalSpawner);
    private static float DesiredWeight(Map map) => (float)DesiredWeightProperty.GetValue(map.wildAnimalSpawner);
    private static float DesiredDensity(Map map) => (float)DesiredDensityProperty.GetValue(map.wildAnimalSpawner);

    private static List<Pawn> OrdinaryWild(Map map) => map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.Faction == null && pawn is not Pawn_GuWildProjection).ToList();
    private static List<Pawn_GuWildProjection> SpawnedGu(Map map) => map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>().ToList();

    // The engine's own sum with the Gu terms left out, in the same order: exact when every Gu term is zero.
    private static float OrdinaryWeight(Map map)
    {
        float total = 0f;
        foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            if (pawn.Faction == null && pawn is not Pawn_GuWildProjection) total += pawn.kindDef.ecoSystemWeight;
        return total;
    }

    private record Occupancy(float Current, float Desired, float Density, bool Full, int Ordinary, int Gu);

    private static Occupancy Measure(Map map) => new(CurrentWeight(map), DesiredWeight(map), DesiredDensity(map),
        map.wildAnimalSpawner.AnimalEcosystemFull, OrdinaryWild(map).Count, SpawnedGu(map).Count);

    private static void ExerciseSlice0Wildlife(Pawn disciple)
    {
        var registry = IndividualRegistry;
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        Prefs.DevMode = true;
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = home;
        var guKinds = DefDatabase<PawnKindDef>.AllDefs.Where(Pawn_GuWildProjection.IsGuKind).ToList();
        var species = DefDatabase<GuSpeciesDef>.AllDefs.Where(GuIndividualRegistry.IsDevelopmentSpawnable).OrderBy(def => def.defName).ToList();
        Check(guKinds.Count >= 4 && species.Count >= 4, $"Loaded {guKinds.Count} Gu projection kinds and {species.Count} spawnable species");
        Check(guKinds.All(kind => kind.ecoSystemWeight == 0f && Pawn_GuWildProjection.HasNativeGenerationExclusions(kind)),
            "Every Gu projection kind declares zero ecosystem weight and the native generation exclusions");
        Check(!DefDatabase<BiomeDef>.AllDefs.Any(biome => guKinds.Any(kind => biome.CommonalityOfAnimal(kind) > 0f
                || biome.CommonalityOfPollutionAnimal(kind) > 0f || biome.CommonalityOfCoastalAnimal(kind) > 0f)),
            "No loaded biome lists a Gu kind in its ordinary, pollution or coastal wildlife pools");
        Report($"Wildlife audit: {home.Biome.defName} home map {home.Size.x}x{home.Size.z}, biomes {string.Join(", ", home.Biomes.Select(biome => biome.defName))}; "
            + $"{OrdinaryWild(home).Count} ordinary factionless animals at start, weight {CurrentWeight(home):0.###} of desired {DesiredWeight(home):0.###}.");

        IntVec3 center = home.Center;
        IntVec3 FreeCell(IntVec3 near) => GenRadial.RadialCellsAround(near, 40f, true).First(cell => cell.InBounds(home) && cell.Standable(home)
            && !cell.Fogged(home) && cell.GetFirstPawn(home) == null && cell.GetThingList(home).Count == 0);
        List<Pawn_GuWildProjection> SpawnGu(Map map, IntVec3 near, int count)
        {
            var spawned = new List<Pawn_GuWildProjection>();
            for (int i = 0; i < count; i++)
            {
                var def = species[i % species.Count];
                IntVec3 cell = GenRadial.RadialCellsAround(near, 30f, true).First(candidate => candidate.InBounds(map) && candidate.Standable(map)
                    && !candidate.Fogged(map) && candidate.GetFirstPawn(map) == null && candidate.GetThingList(map).All(GuIndividualRegistry.SharesCellWithGu));
                Check(registry.TrySpawnDevelopmentGu(def, map, cell, configuredSpecies: true, out var pawn), $"Spawned registered wild {def.defName}");
                Check(pawn.Faction == null && pawn.RaceProps.Animal && registry.Resolve(pawn) != null, $"{def.defName} is a factionless, registered, native animal Pawn");
                spawned.Add(pawn);
            }
            return spawned;
        }
        var ordinaryKind = home.Biome.AllWildAnimals.Where(kind => kind.ecoSystemWeight > 0f && home.mapTemperature.SeasonAcceptableFor(kind.race)
            && !Pawn_GuWildProjection.IsGuKind(kind)).OrderBy(kind => kind.ecoSystemWeight).ThenBy(kind => kind.defName).First();
        Pawn SpawnOrdinary()
        {
            var pawn = PawnGenerator.GeneratePawn(ordinaryKind);
            return (Pawn)GenSpawn.Spawn(pawn, FreeCell(center), home);
        }

        // Equivalent ordinary population just below the desired weight: clear the factionless population, then refill with one kind.
        foreach (var pawn in OrdinaryWild(home)) pawn.Destroy();
        float desired = DesiredWeight(home);
        Check(desired > ordinaryKind.ecoSystemWeight * 2, $"The map wants more than two {ordinaryKind.defName} ({desired:0.###})");
        while (CurrentWeight(home) + ordinaryKind.ecoSystemWeight < desired) SpawnOrdinary();
        var baseline = Measure(home);
        var baselineAnimals = OrdinaryWild(home).ToHashSet();
        Check(!baseline.Full && baseline.Gu == 0 && baseline.Current == OrdinaryWeight(home) && desired - baseline.Current <= ordinaryKind.ecoSystemWeight,
            $"Baseline: {baseline.Ordinary} {ordinaryKind.defName}, weight {baseline.Current:0.###} of {baseline.Desired:0.###}; replenishment eligible");

        // Negative control: manifested registered Gu change nothing in the native occupancy calculation.
        var gu = SpawnGu(home, center, 16);
        var withGu = Measure(home);
        Check(withGu.Gu == 16 && withGu.Current == baseline.Current && withGu.Desired == baseline.Desired && withGu.Density == baseline.Density
            && withGu.Full == baseline.Full && withGu.Ordinary == baseline.Ordinary && withGu.Current == OrdinaryWeight(home),
            $"Sixteen manifested Gu left occupancy weight, desired weight, density and the full gate unchanged ({withGu.Current:0.###}, full {withGu.Full})");

        // Positive control: the same calculation counts an ordinary animal, and one more fills the ecosystem.
        var extra = SpawnOrdinary();
        var filled = Measure(home);
        Check(filled.Current == baseline.Current + ordinaryKind.ecoSystemWeight && filled.Full,
            $"Positive control: one more {ordinaryKind.defName} raised the weight by {ordinaryKind.ecoSystemWeight} and closed replenishment");
        extra.Destroy();
        Check(Measure(home) == withGu, "Removing the extra animal restored the measured state exactly");

        // Matched-seed replenishment through the engine's own spawner tick, with Gu present and then absent.
        TickUntil(() => Find.TickManager.TicksGame % 1213 == 0, 1300, "Reached a wild animal spawner interval");
        foreach (var pawn in OrdinaryWild(home).Where(pawn => !baselineAnimals.Contains(pawn))) pawn.Destroy();
        (int Seed, string Kind, int Count) Attempt(int seed)
        {
            var before = OrdinaryWild(home).ToHashSet();
            Rand.PushState(seed);
            try { home.wildAnimalSpawner.WildAnimalSpawnerTick(); }
            finally { Rand.PopState(); }
            var added = OrdinaryWild(home).Where(pawn => !before.Contains(pawn)).ToList();
            var result = (seed, added.FirstOrDefault()?.kindDef.defName, added.Count);
            foreach (var pawn in added) pawn.Destroy();
            return result;
        }
        var withGuSpawn = Enumerable.Range(1, 20000).Select(Attempt).FirstOrDefault(result => result.Count > 0);
        Check(withGuSpawn.Count > 0, "With Gu present, an eligible spawner interval replenished ordinary wildlife");
        foreach (var pawn in gu) pawn.Destroy();
        Check(SpawnedGu(home).Count == 0 && Measure(home) == baseline, "Removing every Gu returned the occupancy calculation to the baseline exactly");
        var withoutGuSpawn = Attempt(withGuSpawn.Seed);
        Check(withoutGuSpawn.Kind == withGuSpawn.Kind && withoutGuSpawn.Count == withGuSpawn.Count,
            $"The same seed replenished the same wildlife with and without Gu ({withGuSpawn.Count} {withGuSpawn.Kind}, seed {withGuSpawn.Seed})");

        // The underground cavern where Gu actually manifest: same result.
        float caveBefore = CurrentWeight(cave);
        var caveGu = SpawnGu(cave, cave.Center, 4);
        Check(CurrentWeight(cave) == caveBefore && CurrentWeight(cave) == OrdinaryWeight(cave), $"Gu manifested in the spring cavern left its occupancy weight unchanged ({caveBefore:0.###})");
        foreach (var pawn in caveGu) pawn.Destroy();

        // Inherited animal eligibility, recorded for the Slice 1A gate. Population accounting paths are asserted; the
        // custody-sensitive inherited behaviors are reported as facts (pre-existing, unchanged by Slice 0).
        var sample = SpawnGu(home, center, 1).Single();
        var predator = DefDatabase<PawnKindDef>.AllDefs.Where(kind => kind.RaceProps.predator && kind.RaceProps.maxPreyBodySize >= sample.BodySize
            && !Pawn_GuWildProjection.IsGuKind(kind)).OrderByDescending(kind => kind.combatPower).First();
        var hunter = (Pawn)GenSpawn.Spawn(PawnGenerator.GeneratePawn(predator), FreeCell(center), home);
        Check(!FoodUtility.IsAcceptablePreyFor(hunter, sample) && !sample.RaceProps.CanDoHerdMigration && !sample.kindDef.canArriveManhunter
            && !sample.RaceProps.herdMigrationAllowed, $"A wild Gu is never predator prey ({predator.defName}), never migrates in herds and never arrives manhunter");
        hunter.Destroy();
        bool huntable;
        try { huntable = new Designator_Hunt().CanDesignateThing(sample).Accepted; }
        catch (Exception) { huntable = sample.AnimalOrWildMan() && !sample.IsPrisonerInPrisonCell() && (sample.Faction == null || !sample.Faction.def.humanlikeFaction); }
        bool tameable = TameUtility.CanTame(sample);
        bool insanity = IncidentWorker_AnimalInsanityMass.AnimalUsable(sample);
        bool selfTame = sample.IsAnimal && sample.Faction == null && !sample.InMentalState && !sample.Downed && sample.GetStatValue(StatDefOf.Wildness) > 0f;
        Report($"INHERITED ELIGIBILITY (not population accounting; Slice 1A gate): hunt designation {huntable}, tame designation {tameable}, "
            + $"animal insanity target {insanity}, self-tame incident candidate {selfTame} (wildness {sample.GetStatValue(StatDefOf.Wildness):0.##}).");

        // Saved with Gu present for the fresh-process check.
        var kept = SpawnGu(home, center, 3).Append(sample).ToList();
        File.WriteAllText(WildlifeStatePath, JsonSerializer.Serialize(new WildlifeState(home.GetUniqueLoadID(), kept.Select(pawn => pawn.GuId).ToList(),
            DesiredWeight(home), OrdinaryWild(home).Count)));
        Check(CurrentWeight(home) == OrdinaryWeight(home) && !home.wildAnimalSpawner.AnimalEcosystemFull, "Saved with Gu present and replenishment still eligible");
        AssertNoErrors("slice0-wildlife");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Wildlife independence: 16 manifested Gu changed nothing in occupancy, desired weight, density or the full gate; an ordinary {ordinaryKind.defName} did; "
            + "matched-seed replenishment was identical with and without Gu.");
    }

    private static void VerifySlice0WildlifeReload()
    {
        var registry = IndividualRegistry;
        var state = JsonSerializer.Deserialize<WildlifeState>(File.ReadAllText(WildlifeStatePath));
        Map home = Find.Maps.Single(map => map.GetUniqueLoadID() == state.MapId);
        var gu = SpawnedGu(home);
        Check(gu.Count == state.GuIds.Count && gu.All(pawn => pawn.Faction == null && registry.Resolve(pawn) != null && state.GuIds.Contains(pawn.GuId)),
            "The fresh process restored the same registered, factionless wild Gu");
        Check(CurrentWeight(home) == OrdinaryWeight(home) && DesiredWeight(home) == state.Desired && !home.wildAnimalSpawner.AnimalEcosystemFull,
            $"After the reload, Gu still contribute nothing to occupancy and replenishment stays eligible ({CurrentWeight(home):0.###} of {DesiredWeight(home):0.###})");
        AssertNoErrors("slice0-wildlife-reload");
        Report("Fresh process: manifested Gu still contribute nothing to native wildlife occupancy.");
    }
}
