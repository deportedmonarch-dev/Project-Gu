using HarmonyLib;
using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

[StaticConstructorOnStartup]
internal static class HeadlessRenderer
{
    static HeadlessRenderer()
    {
        if (!GenCommandLine.CommandLineArgPassed("headless")) return;
        // This test adapter lets the stock engine dispose maps without GPU-created sections.
        new Harmony("projectgu.priority0test.headless").Patch(AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.Dispose)),
            prefix: new HarmonyMethod(typeof(HeadlessRenderer), nameof(HasSections)));
    }

    private static bool HasSections(Section[,] ___sections) => ___sections != null;
}

public sealed partial class LiveLifecycleChecks : GameComponent
{
    private const string SaveName = "autostart";
    private static readonly IntVec3 East = new(1, 0, 0);
    private static GuPocketProbeComponent Registry => Current.Game.GetComponent<GuPocketProbeComponent>();
    private static ThingDef StoneDef => DefDatabase<ThingDef>.GetNamed("GU_PrimevalStone");
    private static bool EmptyCavernReset => GenCommandLine.CommandLineArgPassed("guemptycavernreset");
    private static bool AwakeningSafety => GenCommandLine.CommandLineArgPassed("guawakeningsafety");
    private static bool Botanical => GenCommandLine.CommandLineArgPassed("gubotanical");
    private static bool CargoPrototype => GenCommandLine.CommandLineArgPassed("gucargoprototype");
    private static bool AutomaticHauling => GenCommandLine.CommandLineArgPassed("guautomatichauling");
    private Action NewCheck => ClanStart ? ExerciseClanStart : CultivatorLifecycle ? ExerciseCultivatorLifecycle : ManifestationProbe ? ExerciseManifestationProbe : Phase2Lifecycle ? ExercisePhase2Lifecycle : DormantHauling ? ExerciseDormantHauling : V2Engine ? ExerciseV2EngineProbe : VanillaGuInventory ? ExerciseVanillaGuInventory
        : Slice3B5BPerf ? PrepareGuRegistryBenchmark : Slice3B5A || Slice3B5B ? ExerciseSlice3B5A
        : Slice3B4 ? ExerciseSlice3B4 : Slice3B3C ? ExerciseSlice3B3C : Slice3B3B ? ExerciseSlice3B3B : Slice3B3A ? ExerciseSlice3B3A : Slice3B2 ? ExerciseSlice3B2
        : Slice3B1 ? ExerciseSlice3B1 : Slice3A ? ExerciseSlice3A : EmptyCavernReset ? ExerciseEmptyCavernReset
        : AwakeningSafety ? ExerciseAwakening : Botanical ? ExerciseBotanical
        : CargoPrototype ? ExerciseCargoPrototype : AutomaticHauling ? ExerciseAutomaticHauling : GenerateAndExercise;
    private Action ReloadCheck => ClanStart ? VerifyClanStartReload : CultivatorLifecycle ? VerifyCultivatorLifecycleReload : ManifestationProbe ? VerifyManifestationProbeReload : Phase2Lifecycle ? VerifyPhase2LifecycleReload : DormantHauling ? VerifyDormantHaulingReload : V1Migration ? VerifyV1Migration : V2Engine ? VerifyV2EngineProbeReload : VanillaGuInventory ? VerifyVanillaGuInventoryReload : Slice3B5A || Slice3B5B ? VerifySlice3B5AReload
        : Slice3B4 ? ReleaseAfterReload : Slice3B3C ? VerifySlice3B3CReload : Slice3B3B ? VerifySlice3B3BReload : Slice3B3A ? VerifySlice3B3AReload : Slice3B2 ? VerifySlice3B2Reload
        : Slice3B1 ? VerifySlice3B1Reload : Slice3A ? VerifySlice3AReload
        : GenCommandLine.CommandLineArgPassed("gulegacy31") ? () => VerifyHistoricalSpring(31)
        : GenCommandLine.CommandLineArgPassed("gulegacy39") ? () => VerifyHistoricalSpring(39)
        : EmptyCavernReset ? VerifyEmptyCavernResetReload
        : AwakeningSafety ? VerifyAwakeningReload : Botanical ? VerifyBotanicalReload
        : CargoPrototype ? VerifyCargoPrototypeReload : AutomaticHauling ? VerifyAutomaticHaulingReload : VerifyReload;
    private Action pending;

    public LiveLifecycleChecks(Game game) { }

    public override void StartedNewGame()
    {
        if (ClanStart) NoteClanStartedNewGame();
        pending = StartedNewGameCheck;
    }

    private void StartedNewGameCheck()
    {
        if (ClanSession) StartClanSessionGame();
        else if (GenCommandLine.CommandLineArgPassed("gupriority0bootstrap"))
            Run(() => GameDataSaveLoader.SaveGame(SaveName), "bootstrap");
        else Run(NewCheck, "new");
    }

    public override void LoadedGame()
    {
        if (ClanStart) NoteClanLoadedGame();
        pending = LoadedGameCheck;
    }

    private void LoadedGameCheck()
    {
        if (GenCommandLine.CommandLineArgPassed("gupriority0fixture")) Run(NewCheck, "new");
        else if (C3Stage is { } c3Stage) Run(VerifyCultivatorMigration, c3Stage);
        else if (CultivatorLifecycle && GenCommandLine.CommandLineArgPassed("gucultivatorreload2")) Run(VerifyCultivatorLifecycleSecondReload, "reload2");
        else if (CultivatorLifecycle && GenCommandLine.CommandLineArgPassed("gucultivatorinvalid")) Run(VerifyCultivatorInvalidSave, "invalid");
        else if (GuRegistryLoadTiming.Stage is { } benchmarkStage) Run(MeasureGuRegistryBenchmark, benchmarkStage);
        else if (V1Resave) Run(VerifyV1MigrationResave, "resave");
        else if (GenCommandLine.CommandLineArgPassed("guslice3b5baperture")) Run(ReleaseIntegratedCohort, "aperture");
        else if (GenCommandLine.CommandLineArgPassed("guslice3b5breleased")) Run(VerifyIntegratedCohortRelease, "released");
        else if (GenCommandLine.CommandLineArgPassed("guclanprefounding")) Run(VerifyClanPrefounding, "prefounding");
        else if (GenCommandLine.CommandLineArgPassed("guslice3b4released")) Run(VerifyReleasedReload, "released");
        else Run(ReloadCheck, "reload");
    }

    public override void GameComponentUpdate()
    {
        if (pending == null || LongEventHandler.AnyEventNowOrWaiting) return;
        Action run = pending;
        pending = null;
        StageTiming.Record("playing");
        run();
    }

    private static void Run(Action run, string stage)
    {
        if (!GenCommandLine.CommandLineArgPassed("headless") && !GenCommandLine.CommandLineArgPassed("gupriority0test")) return;
        StageTiming.Record("test.start");
        try
        {
            Find.TickManager.Pause();
            Prefs.DevMode = true;
            Check(GuUndergroundLifecycleGuards.Installed, "Harmony protections installed");
            run();
            StageTiming.Record("test.end");
            Report($"PASS {stage}");
        }
        catch (Exception error)
        {
            StageTiming.Record("test.end");
            Report($"FAIL {stage} {error}");
        }
        StageTiming.Record("exitRequested");
        StageTiming.Flush();
        UnityEngine.Application.Quit();
    }

    private const int ResetMarkerCount = 137;

    private static void ExerciseEmptyCavernReset()
    {
        Check(ModsConfig.IdeologyActive && !ModsConfig.OdysseyActive, "Run the empty cavern reset with Ideology alone");
        Map home = Find.CurrentMap;
        var colonists = home.mapPawns.FreeColonistsSpawned.ToArray();
        Pawn pawn = colonists.First();
        var site = OpenIndependentSite(pawn, home);
        var level = site.Levels.Single();
        Map cave = level.LiveMap;
        var exit = site.Endpoints.Single(end => !end.IsSurface);
        Thing marker = GenSpawn.Spawn(MakeStack(ResetMarkerCount), exit.Cell + East, cave);
        Check(!cave.IsPlayerHome && !cave.mapPawns.AllPawns.Any(person =>
                person.Faction == Faction.OfPlayer || person.IsPrisonerOfColony)
            && !Registry.HasLegacyDependents && !Find.World.pocketMaps.Any(),
            "Cavern is unoccupied by the colony, with no legacy dependency");
        var destination = Find.WorldGrid.Surface.Tiles.Select(tile => tile.tile).First(tile =>
            !Find.WorldObjects.AnyWorldObjectAt(tile) && Find.WorldPathGrid.Passable(tile));
        var oldParent = home.Parent;

        var newSettlement = MoveColonyUtility.MoveColonyAndReset(destination, colonists, Faction.OfPlayer);

        Map newHome = newSettlement.Map;
        Check(newSettlement.Spawned && newSettlement.Tile == destination && newHome?.IsPlayerHome == true,
            "Reset created a native player settlement at the selected tile");
        Check(colonists.All(person => person.MapHeld == newHome && !Find.WorldPawns.Contains(person)),
            "The new colony owns all selected colonists, including arrivals still in native drop pods");
        Check(!GuPocketProbeComponent.IsLive(home) && !oldParent.Spawned,
            "The original colony map and parent were removed");
        Check(level.LiveMap == cave && marker.Spawned && marker.Map == cave && marker.stackCount == ResetMarkerCount,
            "Reset retained the same cavern and physical stack");
        AssertSealedResetCavern(cave);
        Current.Game.CurrentMap = newHome;
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Ideology reset completed and sealed the independent cavern with its original 137-stone stack.");
    }

    private static void VerifyEmptyCavernResetReload()
    {
        Map cave = Find.WorldObjects.MapParents.OfType<GuUndergroundLevelParent>().Single().Map;
        AssertSealedResetCavern(cave);
        Map newHome = Find.Maps.Single(map => map.IsPlayerHome);
        Check(newHome.mapPawns.AllPawns.Any(person => person.IsFreeColonist && person.MapHeld == newHome),
            "New colony and its colonists survived a fresh-process reload");
    }

    private static void AssertSealedResetCavern(Map cave)
    {
        AssertNativeAddress(cave);
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef)
            .OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(exit, out var site, out var endpoint)
            && site.Levels.Single().LiveMap == cave
            && site.DestinationFor(endpoint.EndpointId) is { IsSurface: true, Surface: null, Anchor: null },
            "The original site and exit resolve with a closed surface binding");
        Check(cave.listerThings.ThingsOfDef(StoneDef).Single().stackCount == ResetMarkerCount,
            "One physical stack remains underground with the exact original count");
    }

    private static void GenerateAndExercise()
    {
        Map home = Find.CurrentMap;
        Check(GuPocketSiteDef.SpiritSpring.independentOwner && !GuPocketSiteDef.Probe.independentOwner,
            "New spirit springs use the independent provider and generic probe remains a legacy control");
        Pawn pawn = home.mapPawns.FreeColonistsSpawned.First();
        Thing packed = MakeStack(7);
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(packed, false), "Add native inventory fixture");
        var equipment = pawn.equipment.AllEquipmentListForReading.ToArray();

        Map firstSurface = ExtraSettlement();
        Map secondSurface = ExtraSettlement();
        Map legacySurface = ExtraSettlement();
        Map unrelated = ExtraSettlement();
        GuIndependentSite first = OpenIndependentSite(pawn, firstSurface, exerciseRejection: true);
        GuIndependentSite second = OpenIndependentSite(pawn, secondSurface);
        Map firstCave = first.Levels[0].LiveMap;
        Map secondCave = second.Levels[0].LiveMap;
        var layer = GuUndergroundAddressLayer.Resolve(Find.WorldGrid, false);
        Check(firstCave.Tile != secondCave.Tile && firstCave.Tile != first.Geography && layer.Owns(secondCave.Tile)
            && layer.TilesCount == 32 && !layer.Visible && !layer.CanReachLayer && layer.Connections.Count == 0,
            "Two levels share one hidden, disconnected address layer");
        AssertNativeAddress(firstCave);
        AssertNativeAddress(secondCave);
        Check(!Find.WorldReachability.CanReach(home.Tile, firstCave.Tile), "Native world pathing cannot reach hidden addresses");

        Find.WorldGrid.RemovePlanetLayer(layer);
        Check(layer.Owns(firstCave.Tile) && GuPocketProbeComponent.IsLive(firstCave), "Layer teardown refused");
        firstCave.Parent.Destroy();
        Check(first.Levels[0].LiveMap == firstCave, "Direct parent destruction refused");
        Current.Game.DeinitAndRemoveMap(firstCave, false);
        Check(first.Levels[0].LiveMap == firstCave, "Direct persistent level teardown refused");

        var firstEnd = first.Endpoints.Single(end => !end.IsSurface);
        TransferToFixture(pawn, firstCave, firstEnd.Cell + East);
        firstSurface.Parent.Abandon(false);
        Check(firstSurface.Parent.Spawned && pawn.Map == firstCave, "Occupied site's last surface removal blocked");
        var firstOutside = first.Endpoints.Single(end => end.IsSurface);
        firstOutside.Anchor.DeSpawn();
        Check(!JobDriver_GuPocketProbeTravel.TryTravel(pawn, firstEnd.Anchor, out _), "Destroyed route closes");
        Thing carried = MakeStack(5);
        Check(pawn.carryTracker.GetDirectlyHeldThings().TryAdd(carried, false), "Native carried Thing added");
        bool rescued = Registry.TryRecoverPawn(pawn, out string reason);
        Check(rescued && pawn.Map == firstSurface && pawn.inventory.GetDirectlyHeldThings().Contains(carried)
            && carried.stackCount == 5 && pawn.inventory.GetDirectlyHeldThings().Contains(packed)
            && pawn.equipment.AllEquipmentListForReading.SequenceEqual(equipment), $"Emergency return preserves custody. {reason}");

        bool moved = JobDriver_GuPocketProbeTravel.TryMove(pawn, secondCave, second.Endpoints.Single(end => !end.IsSurface).Cell,
            () => false, out _);
        Check(!moved && pawn.Map == firstSurface && pawn.inventory.GetDirectlyHeldThings().Contains(packed),
            "Rejected transfer restores original pawn and inventory");

        TransferToFixture(pawn, home, home.Center);
        var marker = MakeStack(23);
        GenSpawn.Spawn(marker, firstEnd.Cell + East, firstCave);
        GameDataSaveLoader.SaveGame("before-source-transition");
        if (ModsConfig.OdysseyActive) GravshipUtility.AbandonMap(firstSurface);
        else firstSurface.Parent.Abandon(false);
        Check(!GuPocketProbeComponent.IsLive(firstSurface) && first.Levels[0].LiveMap == firstCave
            && firstOutside.Surface == null && marker.Map == firstCave && marker.stackCount == 23,
            "Ordinary source abandonment retains empty independent level and physical contents");
        var retainedSpring = firstCave.listerThings.AllThings.Select(thing => thing.TryGetComp<CompGuSpiritSpring>())
            .Single(comp => comp != null);
        int reserve = retainedSpring.Reserve;
        retainedSpring.CompGetGizmosExtra().OfType<Command_Action>()
            .Single(command => command.defaultLabel == "GU: Produce stone now").action();
        Check(retainedSpring.Reserve == reserve - 1 && firstCave.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount) == 24,
            "Retained ecology and local production remain usable after source abandonment");
        TransferToFixture(pawn, firstCave, firstEnd.Cell);
        rescued = Registry.TryRecoverPawn(pawn, out reason);
        Caravan caravan = pawn.GetCaravan();
        Check(rescued && caravan?.Spawned == true && caravan.Tile == first.Geography && caravan.ContainsPawn(pawn)
            && pawn.inventory.GetDirectlyHeldThings().Contains(packed), $"Emergency caravan preserves custody after source loss. {reason}");
        CaravanEnterMapUtility.Enter(caravan, home, CaravanEnterMode.Center, CaravanDropInventoryMode.DoNotDrop);
        Check(pawn.Map == home && packed.stackCount == 7, "Native caravan re-entry conserved inventory");
        unrelated.Parent.Abandon(false);
        Check(!GuPocketProbeComponent.IsLive(unrelated), "Unrelated settlement can abandon normally");

        var legacyEntrance = SpawnEntrance(legacySurface);
        Registry.AddSite(GuPocketSiteDef.SpiritSpring, legacyEntrance);
        Check(GuPocketProbeComponent.TryGetSite(legacyEntrance, out var legacy), "Legacy site registered");
        Check(GuPocketProbeComponent.TryGenerate(legacy, out reason), $"Native pocket generation. {reason}");
        int pocketCount = Find.World.pocketMaps.Count;
        var rejectedLegacyEntrance = (Building_GuPocketProbeEndpoint)GenSpawn.Spawn(GuPocketSiteDef.SpiritSpring.endpoint,
            legacySurface.Center - East * 3, legacySurface);
        Registry.AddSite(GuPocketSiteDef.SpiritSpring, rejectedLegacyEntrance);
        Check(GuPocketProbeComponent.TryGetSite(rejectedLegacyEntrance, out var rejectedLegacy), "Additional legacy fixture");
        RejectNext(rejectedLegacyEntrance);
        Check(!GuPocketProbeComponent.TryGenerate(rejectedLegacy, out reason) && !rejectedLegacy.GenerationStarted
            && Find.World.pocketMaps.Count == pocketCount, $"Legacy rejected generation cleans up only its new map. {reason}");
        legacySurface.Parent.Abandon(false);
        var legacyParent = legacySurface.Parent;
        legacyParent.SetFaction(null);
        Check(legacyParent.ShouldRemoveMapNow(out _), "Vanilla would automatically remove the legacy source");
        legacyParent.CheckRemoveMapNow();
        Check(legacyParent.Spawned && GuPocketProbeComponent.IsLive(legacySurface), "Automatic removal kept the native dependency");
        legacyParent.SetFaction(Faction.OfPlayer);
        legacySurface.Parent.CheckRemoveMapNow();
        Find.WorldObjects.Remove(legacySurface.Parent);
        Current.Game.DeinitAndRemoveMap(legacySurface, false);
        Check(GuPocketProbeComponent.HasValidRoute(legacy) && legacySurface.Parent.Spawned, "Legacy source removal refused");
        PocketMapUtility.DestroyPocketMap(legacy.Cavern);
        Check(GuPocketProbeComponent.HasValidRoute(legacy), "Legacy direct pocket destruction refused");
        if (ModsConfig.OdysseyActive)
        {
            var engine = (Building_GravEngine)GenSpawn.Spawn(ThingDefOf.GravEngine, legacySurface.Center - 5 * East, legacySurface);
            Map selected = Find.CurrentMap;
            Find.GravshipController.InitiateTakeoff(engine, home.Tile);
            Check(Find.CurrentMap == selected && engine.Spawned && GuPocketProbeComponent.HasValidRoute(legacy),
                "Odyssey launch commit refused before gravship capture");
            GravshipUtility.AbandonMap(legacySurface);
            Check(GuPocketProbeComponent.HasValidRoute(legacy), "Odyssey abandonment blocked before source mutation");
        }
        if (ModsConfig.IdeologyActive)
        {
            try
            {
                MoveColonyUtility.MoveColonyAndReset(home.Tile, new[] { pawn }, Faction.OfPlayer);
                throw new InvalidOperationException("Expected guarded Ideology reset");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("Project GU blocked colony reset"))
            {
                Check(pawn.Map == home && pawn.inventory.GetDirectlyHeldThings().Contains(packed)
                    && GuPocketProbeComponent.HasValidRoute(legacy), "Ideology preflight conserves pawn, items and maps");
            }
        }

        var secondOutside = second.Endpoints.Single(end => end.IsSurface);
        TransferToFixture(pawn, secondSurface, secondOutside.Cell + East);
        Check(JobDriver_GuPocketProbeTravel.TryTravel(pawn, secondOutside.Anchor, out reason) && pawn.Map == secondCave,
            $"Save fixture places its traveler on the independent level. {reason}");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Native conservation fixture saved with {Find.Maps.Count} loaded maps");
    }

    private static void VerifyReload()
    {
        var levels = Find.WorldObjects.MapParents.OfType<GuUndergroundLevelParent>().ToArray();
        Check(levels.Length == 2 && levels.Select(level => level.Tile).Distinct().Count() == 2, "Independent parents restored exactly once");
        foreach (var level in levels) AssertNativeAddress(level.Map);
        var first = levels.Single(level => level.Map.listerThings.ThingsOfDef(StoneDef).Any(thing => thing.stackCount == 23));
        var second = levels.Single(level => level != first);
        Check(Registry.TryGetIndependentArrival(first.Map, out _) && Registry.TryGetIndependentArrival(second.Map, out _),
            "Two original levels retained and bound");
        var levelMarker = second.Map.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef)
            .OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(levelMarker, out var tethered, out _), "Saved route restored");
        var sourceEntrance = tethered.Endpoints.Single(end => end.IsSurface);
        Pawn pawn = Find.Maps.SelectMany(map => map.mapPawns.FreeColonistsSpawned)
            .Single(person => person.inventory.GetDirectlyHeldThings().Any(thing => thing.def == StoneDef && thing.stackCount == 7));
        Check(pawn.inventory.GetDirectlyHeldThings().Any(thing => thing.def == StoneDef && thing.stackCount == 5),
            "Carried and inventory resources survived the full save");
        Check(pawn.Map == second.Map && JobDriver_GuPocketProbeTravel.TryTravel(pawn, levelMarker, out _)
            && pawn.Map == sourceEntrance.Surface, "The same saved pawn returned from an occupied level");
        bool entered = JobDriver_GuPocketProbeTravel.TryTravel(pawn, sourceEntrance.Anchor, out string reason);
        Check(entered && pawn.Map == second.Map && Find.WorldObjects.MapParents.OfType<GuUndergroundLevelParent>().Count() == 2,
            $"Old level entered with no duplicate generation. {reason}");
        var legacy = Find.World.pocketMaps.Single();
        Check(legacy.sourceMap?.Parent.Spawned == true && legacy.Map != null, "Existing pocket still native after reload");
        legacy.sourceMap.Parent.Abandon(false);
        Check(legacy.sourceMap.Parent.Spawned && legacy.Map != null, "Legacy source guard restored after reload");
    }

    private static GuIndependentSite OpenIndependentSite(Pawn pawn, Map source, bool exerciseRejection = false)
    {
        var entrance = SpawnEntrance(source);
        Registry.AddIndependentSite(GuPocketSiteDef.SpiritSpring, entrance);
        TransferToFixture(pawn, source, entrance.Position + East);
        if (exerciseRejection)
        {
            int mapsBefore = Find.Maps.Count;
            RejectNext(entrance);
            bool rejected = JobDriver_GuPocketProbeTravel.TryTravel(pawn, entrance, out string failure);
            Check(!rejected && pawn.Map == source && Find.Maps.Count == mapsBefore,
                $"Rejected independent generation preserves pawn and map count. {failure}");
        }
        bool entered = JobDriver_GuPocketProbeTravel.TryTravel(pawn, entrance, out string reason);
        Check(entered, $"New independent site entered. {reason}");
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(entrance, out var site, out _), "New site resolved");
        var level = site.Levels.Single();
        Check(pawn.Map == level.LiveMap && GuPocketSiteDef.IsSpringCavern(pawn.Map), "Existing spring profile preserved");
        var spring = pawn.Map.listerThings.AllThings.Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp != null);
        Check(spring.HopeGuAvailable == 3 && spring.HabitatSupport == 7, "Native flora and Hope Gu preserved by the new provider");
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        bool returned = JobDriver_GuPocketProbeTravel.TryTravel(pawn, exit, out reason);
        Check(returned && pawn.Map == source, $"Return route works. {reason}");
        return site;
    }

    private static void RejectNext(Building_GuPocketProbeEndpoint entrance) => entrance.GetGizmos().OfType<Command_Toggle>()
        .Single(gizmo => gizmo.defaultLabel == "Reject next generation").toggleAction();

    private static Building_GuPocketProbeEndpoint SpawnEntrance(Map map)
    {
        IntVec3 cell = GenRadial.RadialCellsAround(map.Center + East * 3, 12f, true).First(candidate =>
            JobDriver_GuPocketProbeTravel.CanLand(map, candidate, null)
            && JobDriver_GuPocketProbeTravel.CanLand(map, candidate + East, null)
            && !candidate.GetTerrain(map).IsWater && candidate.GetThingList(map).Count == 0);
        return (Building_GuPocketProbeEndpoint)GenSpawn.Spawn(GuPocketSiteDef.SpiritSpring.endpoint, cell, map);
    }

    private static void TransferToFixture(Pawn pawn, Map map, IntVec3 cell)
    {
        pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
        GenSpawn.Spawn(pawn, cell, map);
    }

    private static Map ExtraSettlement()
    {
        PlanetTile tile = Find.WorldGrid.Surface.Tiles.Select(tile => tile.tile).First(tile => !Find.WorldObjects.AnyWorldObjectAt(tile)
            && Find.WorldPathGrid.Passable(tile));
        var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        parent.Tile = tile;
        parent.SetFaction(Faction.OfPlayer);
        parent.Name = "Priority 0 control";
        Find.WorldObjects.Add(parent);
        return MapGenerator.GenerateMap(new IntVec3(31, 1, 31), parent, GuPocketSiteDef.Probe.generator, null, null, false, false);
    }

    private static void AssertNativeAddress(Map map) => Check(map != null && Current.Game.FindMap(map.Tile) == map
        && Find.WorldObjects.MapParentAt(map.Tile) == map.Parent && map.TileInfo.PrimaryBiome.defName == "Underground",
        "Native tile and owner resolve the same underground level");

    private static Thing MakeStack(int count)
    {
        Thing item = ThingMaker.MakeThing(StoneDef);
        item.stackCount = count;
        return item;
    }

    private static void Report(string line) =>
        File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "priority0-result.txt"), line + Environment.NewLine);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
