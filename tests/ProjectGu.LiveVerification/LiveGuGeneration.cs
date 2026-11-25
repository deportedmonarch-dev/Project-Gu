using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

[StaticConstructorOnStartup]
internal static class GuStartingPetProbe
{
    internal static PawnKindDef[] Pool { get; private set; }
    internal static PawnKindDef[] ScenarioPool { get; private set; }

    static GuStartingPetProbe()
    {
        if (!GenCommandLine.CommandLineArgPassed("gugeneration")) return;
        new Harmony("projectgu.verification.startingpets").Patch(
            AccessTools.Method(typeof(ScenPart_StartingAnimal), nameof(ScenPart_StartingAnimal.PlayerStartingThings)),
            prefix: new HarmonyMethod(typeof(GuStartingPetProbe), nameof(ReadStartingPetPool)));
    }

    private static void ReadStartingPetPool(ScenPart_StartingAnimal __instance)
    {
        if (!ReferenceEquals(Find.Scenario, ClanQuickStart.Def.scenario)
            || Find.GameInitData?.startingAndOptionalPawns is not { Count: > 0 }) return;
        Pool = ((IEnumerable<PawnKindDef>)AccessTools.Method(typeof(ScenPart_StartingAnimal), "RandomPets")
            .Invoke(__instance, null)).ToArray();
        ScenarioPool = ((IEnumerable<PawnKindDef>)AccessTools.Method(typeof(ScenPart_StartingAnimal), "PossibleAnimals")
            .Invoke(__instance, new object[] { false })).ToArray();
    }
}

public sealed partial class LiveLifecycleChecks
{
    private const string GenerationRefusal = "Wild Gu Pawns require Project GU registry generation.";
    private static bool GuGeneration => GenCommandLine.CommandLineArgPassed("gugeneration");
    private static string GuGenerationStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-generation-state.txt");

    private static void ExerciseGuGeneration(Pawn disciple, CompGuSpiritSpring spring)
    {
        var kinds = DefDatabase<PawnKindDef>.AllDefs.Where(Pawn_GuWildProjection.IsGuKind).ToHashSet();
        var species = DefDatabase<GuSpeciesDef>.AllDefs.Where(def => def.wildProjectionKind != null).ToArray();
        Check(kinds.Count >= 4 && species.All(def => kinds.Contains(def.wildProjectionKind))
            && species.Any(def => def.maxRank >= 6),
            "The native audit includes every loaded Gu kind and the high-rank developer fixture");
        var pool = GuStartingPetProbe.Pool;
        Check(pool is { Length: > 0 } && !pool.Any(kinds.Contains)
            && !ActuallyProjectedGu().Any(), "Native random pets were ordinary animals and the colony started without a Gu Pawn");
        Check(GuStartingPetProbe.ScenarioPool is { Length: > 0 } scenarioPool && !scenarioPool.Any(kinds.Contains),
            "The native named-animal list used by the scenario randomizer and editor excludes every Gu kind");
        Check(!DefDatabase<BiomeDef>.AllDefs.SelectMany(biome => biome.AllWildAnimals).Any(kinds.Contains),
            "No Gu kind occurs in any loaded native biome wildlife pool");

        var animalStock = DefDatabase<TraderKindDef>.AllDefs.SelectMany(trader => trader.stockGenerators)
            .OfType<StockGenerator_Animals>().ToArray();
        var canStock = AccessTools.Method(typeof(StockGenerator_Animals), "PawnKindAllowed");
        var canArrive = AccessTools.Method(typeof(AggressiveAnimalIncidentUtility), "CanArriveManhunter");
        Check(animalStock.Length > 0, "Loaded animal trader stock generators");
        foreach (var kind in kinds)
        {
            Check(Pawn_GuWildProjection.HasNativeGenerationExclusions(kind) && kind.RaceProps.neverIncludeInQuests
                && !(bool)canArrive.Invoke(null, new object[] { kind }),
                $"{kind.defName} passes the resolved policy and is excluded from quest and manhunter selection");
            Check(animalStock.All(stock => !(bool)canStock.Invoke(stock, new object[] { kind, Home.Tile })
                && stock.TradeabilityFor(kind.race) == Tradeability.None), $"{kind.defName} cannot enter sampled-tile animal stock or trade");
        }

        var questNode = new QuestNode_GetAnimalKindByPoints();
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var slate = new Slate();
            slate.Set("points", attempt % 2 == 0 ? 2f : 500f);
            if (questNode.TestRun(slate))
                Check(!kinds.Contains(slate.Get<PawnKindDef>("animalKindDef")), "A native quest animal draw never returned a Gu");
        }
        AssertNoErrors("Gu generation ordinary pools");
        foreach (var kind in kinds)
        {
            CheckGenerationBlocked(() => PawnGenerator.GeneratePawn(kind, Faction.OfPlayer));
            CheckGenerationBlocked(() => ThingMaker.MakeThing(kind.race));
        }

        var registry = IndividualRegistry;
        var dev = DefDatabase<GuSpeciesDef>.GetNamed(CompGuSpiritSpring.TestSpecies);
        var moon = DefDatabase<GuSpeciesDef>.GetNamed("GU_MoonlightGu");
        Map cave = disciple.Map;
        IntVec3 cell = EmptyCargoCell(cave, disciple.Position, disciple, 5);
        Check(!registry.TrySpawnDevelopmentGu(dev, cave, cell, out _), "Normal play cannot create a developer Gu");
        Prefs.DevMode = true;
        CheckGenerationBlocked(() => PawnGenerator.GeneratePawn(dev.wildProjectionKind));
        Check(!registry.TrySpawnDevelopmentGu(moon, cave, cell, out _), "The test spawner rejects ordinary species even in Development Mode");
        Check(registry.TrySpawnDevelopmentGu(dev, cave, cell, out var debug), "The explicit Project GU test spawner remains available");
        CheckGenerationBlocked(() => PawnGenerator.GeneratePawn(dev.wildProjectionKind));
        var record = registry.Records.Single();
        Check(record == registry.Resolve(debug) && record.Origin == GuRecord.DevelopmentOrigin && !record.CohortDebitCommitted
            && debug.GetInspectString().Contains("Development test Gu"), "The developer specimen has one distinct, visibly marked debug origin");

        // Serialize the native form left by the old starting-animal bug, with no Gu ID or canonical claim.
        var oldPet = Pawn_GuWildProjection.GenerateForRegistry(new PawnGenerationRequest(moon.wildProjectionKind,
            tile: Home.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoGear: true));
        GenSpawn.Spawn(oldPet, EmptyCargoCell(Home, Home.Center, null, 6), Home);
        oldPet.SetFaction(Faction.OfPlayer);
        Prefs.DevMode = false;
        Check(registry.Resolve(oldPet) == null && !registry.CanOrderWildCapture(oldPet, disciple)
            && !registry.TryCaptureWild(oldPet, oldPet.GuRevision, disciple, oldPet.Position, out _, out _)
            && spring.MatureCountFor(moon.defName) == 2 && registry.Records.Single() == record,
            "An unbound legacy pet cannot be captured, registered or charged to a cohort");
        File.WriteAllLines(GuGenerationStatePath, new[] { debug.ThingID, record.Id, oldPet.ThingID, spring.HabitatId });
        var forcedErrors = Log.Messages.Where(message => message.type == LogMessageType.Error).Select(message => message.text).ToArray();
        Check(forcedErrors.Length > 0 && forcedErrors.All(error => error.StartsWith(
            $"Error while generating pawn. Rethrowing. Exception: System.InvalidOperationException: {GenerationRefusal}", StringComparison.Ordinal)),
            $"Only the deliberately forced native generation attempts may log errors. Errors: {string.Join("\n", forcedErrors)}");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Native pools excluded {kinds.Count} Gu kinds from {pool.Length} random-pet candidates, all loaded biomes, "
            + $"and {animalStock.Length} animal stock generators. Quest and manhunter exclusions and direct factory refusals passed.");
    }

    private static void CheckGenerationBlocked(Func<Thing> generate)
    {
        try { generate(); }
        catch (InvalidOperationException ex) when (ex.Message == GenerationRefusal) { return; }
        throw new InvalidOperationException("The native factory did not reject a direct Gu construction attempt.");
    }

    private static void VerifyGuGenerationReload()
    {
        Prefs.DevMode = false;
        var state = File.ReadAllLines(GuGenerationStatePath);
        var registry = IndividualRegistry;
        var pawns = PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().ToArray();
        var debug = pawns.Single(pawn => pawn.ThingID == state[0]);
        var oldPet = pawns.Single(pawn => pawn.ThingID == state[2]);
        var record = registry.Records.Single();
        Check(record.Id == state[1] && registry.Resolve(debug) == record && record.Origin == GuRecord.DevelopmentOrigin,
            "Scribe restored the original authorized developer specimen");
        Check(oldPet.Faction == Faction.OfPlayer && registry.RogueProjections.Contains(oldPet.GuTag())
            && registry.Resolve(oldPet) == null && oldPet.GetInspectString().Contains("Unbound Gu projection"),
            "The legacy pet loaded as an explicitly flagged rogue without canonical Gu ownership");
        var spring = Find.Maps.SelectMany(map => map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring")))
            .Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp.HabitatId == state[3]);
        Check(spring.MatureCountFor("GU_MoonlightGu") == 2, "The old pet and the test specimen never debited the Moonlight Gu cohort");
        AssertNoErrors("Gu generation reload");
        Report("Fresh-process load preserved the marked test identity and identified the legacy unbound starting pet.");
    }
}
