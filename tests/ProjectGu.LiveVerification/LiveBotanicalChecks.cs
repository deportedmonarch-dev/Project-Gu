using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static TerrainDef QiSoil => DefDatabase<TerrainDef>.GetNamed("GU_PrimevalQiRichSoil");
    private static ThingDef NativeGrass => DefDatabase<ThingDef>.GetNamed("GU_SpiritGrass");
    private static ThingDef NativeOrchid => DefDatabase<ThingDef>.GetNamed("GU_MoonOrchid");

    private static void ExerciseBotanical()
    {
        Map home = Find.CurrentMap;
        Pawn pawn = home.mapPawns.FreeColonistsSpawned.First();
        var site = OpenIndependentSite(pawn, home, exerciseRejection: true);
        Map cave = site.Levels.Single().LiveMap;
        var spring = BotanicalSpring(cave);
        AssertBotanicalHabitat(cave, spring);
        int reserve = spring.Reserve;
        int juvenile = spring.HopeGuJuvenile;
        var grass = cave.listerThings.ThingsOfDef(NativeGrass).Cast<Plant>().ToArray();
        Check(grass.All(plant => plant.Growth >= 0.84f && plant.GrowthRateFactor_Light == 1f),
            "Seeded grass has mature coverage and can grow on the roofed bank");

        var step = spring.CompGetGizmosExtra().OfType<Command_Action>()
            .Single(action => action.defaultLabel == "GU: Simulate flora growth step");
        var harmed = grass[0];
        harmed.HitPoints -= 12;
        int damaged = harmed.HitPoints;
        step.action();
        Check(harmed.HitPoints > damaged && harmed.HitPoints <= harmed.MaxHitPoints, "A native grass patch partly recovers");
        harmed.Destroy();
        step.action();
        Plant replacement = cave.listerThings.ThingsOfDef(NativeGrass).Cast<Plant>().Single(plant => !grass.Contains(plant));
        Check(replacement.Growth < 0.84f && spring.Reserve == reserve && spring.HopeGuAvailable == 3
            && spring.HopeGuJuvenile == juvenile, "One juvenile grass patch replaces one vacancy with unchanged cohorts and reserve");
        AssertBotanicalHabitat(cave, spring);

        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        TransferToFixture(pawn, cave, exit.Position + East);
        var garden = CellRect.CenteredOn(exit.Position + new IntVec3(4, 0, 0), 1);
        IntVec3 cell = garden.Cells
            .First(candidate => candidate.GetTerrain(cave) == QiSoil
            && PlantUtility.CanNowPlantAt(NativeOrchid, candidate, cave, false)
            && !candidate.GetThingList(cave).Any(thing => thing is Plant)
            && pawn.CanReach(candidate, PathEndMode.OnCell, Danger.None));
        var zone = new Zone_Growing(cave.zoneManager);
        cave.zoneManager.RegisterZone(zone);
        zone.AddCell(cell);
        zone.SetPlantDefToGrow(NativeOrchid);
        var grower = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("GrowerSow").Worker;
        // Alpha 5B: moon orchids are seed-gated. With one seed at hand the native work giver issues the seed-carrying sow job.
        var seed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("GU_MoonOrchidSeeds"));
        Check(pawn.inventory.innerContainer.TryAdd(seed, false), "The grower carries one moon orchid seed");
        Job job = grower.JobOnCell(pawn, cell);
        Check(job?.def?.defName == "GU_SowWithMaterial", $"The native growing-zone work giver assigns a seed-gated sowing job ({job?.def?.defName})");
        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        for (int tick = 0; tick < 1800 && cell.GetPlant(cave) is not { Growth: > 0f }; tick++)
            Find.TickManager.DoSingleTick();
        var sown = cell.GetPlant(cave);
        Check(sown?.def == NativeOrchid && sown.Growth > 0f && sown.DeliberatelyCultivated()
            && sown.GrowthRateFactor_Light == 1f, "The native sowing job completed and left a cultivated seedling on Qi soil");
        Check(pawn.inventory.innerContainer.All(thing => thing.def.defName != "GU_MoonOrchidSeeds"), "The sowing spent the seed");
        VerifyNativeGrowth(replacement, sown);

        IntVec3 surfaceCell = CellRect.CenteredOn(home.Center, 12).Cells.First(candidate => candidate.Standable(home)
            && candidate.GetThingList(home).Count == 0);
        home.terrainGrid.SetTerrain(surfaceCell, TerrainDefOf.Soil);
        Check(!PlantUtility.CanNowPlantAt(NativeOrchid, surfaceCell, home, false),
            "The same empty surface cell rejects Moon Orchid on standard soil");
        home.terrainGrid.SetTerrain(surfaceCell, QiSoil);
        Check(PlantUtility.CanNowPlantAt(NativeOrchid, surfaceCell, home, false),
            "The same empty surface cell accepts Moon Orchid on Qi soil without a spring");
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(exit, out _, out _), "Cave route remains registered");
        VerifySouthernProduction(spring, pawn);
        Current.Game.CurrentMap = cave;
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Native flora, garden sowing, ecosystem lighting, wet outputs, dry fallback and blocked-output conservation passed.");
    }

    private static void VerifyNativeGrowth(Plant grass, Plant orchid)
    {
        var clock = Find.TickManager;
        int originalTick = clock.TicksGame;
        int noon = originalTick + (12 - GenLocalDate.HourOfDay(grass)) * GenDate.TicksPerHour;
        try
        {
            clock.DebugSetTicksGame(noon);
            float grassGrowth = grass.Growth;
            float flowerGrowth = orchid.Growth;
            grass.TickLong();
            orchid.TickLong();
            Check(grass.Growth > grassGrowth && orchid.Growth > flowerGrowth,
                $"Both species gain native growth on the roofed bank. Grass {grassGrowth:F3} to {grass.Growth:F3}, " +
                $"orchid {flowerGrowth:F3} to {orchid.Growth:F3}, hour {GenLocalDate.HourOfDay(grass)}, " +
                $"temperatures {grass.Position.GetTemperature(grass.Map):F1} and {orchid.Position.GetTemperature(orchid.Map):F1}.");
        }
        finally
        {
            clock.DebugSetTicksGame(originalTick);
        }
    }

    private static void VerifyBotanicalReload()
    {
        Map cave = Find.WorldObjects.MapParents.OfType<GuUndergroundLevelParent>().Single().Map;
        AssertNativeAddress(cave);
        var spring = BotanicalSpring(cave);
        AssertBotanicalHabitat(cave, spring, expectedOrchids: 6);
        Check(spring.HopeGuAvailable == 3 && cave.listerThings.ThingsOfDef(NativeOrchid).Cast<Plant>()
            .Count(plant => plant.DeliberatelyCultivated()) == 1, "Cohort and native sown plant survived reload");
        var outlets = spring.CavernOutputs().ToArray();
        Check(outlets[0].Cell.GetTerrain(cave).IsWater && cave.listerThings.ThingsOfDef(StoneDef)
            .Any(thing => thing.Position == outlets[0].Cell && thing.stackCount == 200)
            && cave.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount) == 202,
            "The original wet and shoreline stacks survived reload");
        var pawn = cave.mapPawns.FreeColonistsSpawned.First();
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef)
            .OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(pawn, exit, out string reason) && pawn.Map.IsPlayerHome,
            $"Travel away from the saved botanical cavern works. {reason}");
    }

    private static CompGuSpiritSpring BotanicalSpring(Map cave) => cave.listerThings.AllThings
        .Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp != null);

    private static void AssertBotanicalHabitat(Map cave, CompGuSpiritSpring spring, int expectedOrchids = 5)
    {
        var grass = cave.listerThings.ThingsOfDef(NativeGrass);
        var orchids = cave.listerThings.ThingsOfDef(NativeOrchid);
        Check(grass.Count == 110 && grass.All(plant => plant is Plant) && NativeGrass.plant.maxMeshCount == 9
            && NativeGrass.pathCost == 0, "The new cavern contains a dense native grass field with bounded meshes");
        Check(orchids.Count == expectedOrchids && orchids.All(plant => plant is Plant && plant.Position.GetTerrain(cave) == QiSoil),
            "The native orchids are rooted in Qi-rich soil");
        Check(spring.HabitatSupport == 7, "The botanical field preserves the seven-support baseline");
        Check(spring.UsesCavernComposition && cave.AllCells.All(cell => cell.GetTerrain(cave) == QiSoil
            || cell.GetTerrain(cave).IsWater || cell.GetTerrain(cave).IsRock),
            "The fresh cavern floor contains Qi soil, actual water, and stone");
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).Single();
        var approach = GuSpringCavernLayout.MakeDryApproach(exit.Position, spring.parent.OccupiedRect());
        Check(approach.All(cell => cell.GetTerrain(cave) == QiSoil) && grass.All(plant => !approach.Contains(plant.Position)),
            "Plantable Qi soil keeps the approach free of wild Spirit Grass");
        cave.glowGrid.GlowGridUpdate_First();
        var core = cave.glowGrid.VisualGlowAt(spring.parent.Position);
        var reflection = cave.glowGrid.VisualGlowAt(exit.Position + new IntVec3(4, 0, 0));
        Check(core.b > core.r && core.b > 100, "The ecosystem alone lights the spring focal point");
        Check(reflection.g > 40 && reflection.b < core.b, "The open garden has soft reflected light below the spring focal point");
    }

    private static void VerifySouthernProduction(CompGuSpiritSpring spring, Pawn pawn)
    {
        Map cave = spring.parent.Map;
        var outlets = spring.CavernOutputs().ToArray();
        var produce = spring.CompGetGizmosExtra().OfType<Command_Action>()
            .Single(action => action.defaultLabel == "GU: Produce stone now");
        int reserve = spring.Reserve;

        produce.action();
        Thing first = cave.listerThings.ThingsOfDef(StoneDef).Single();
        Check(first.Position == outlets[0].Cell && first.Position.GetTerrain(cave) == TerrainDefOf.WaterShallow
            && pawn.CanReach(first, PathEndMode.ClosestTouch, Danger.None)
            && pawn.CanReserve(first) && first.def.selectable && first.def.EverHaulable,
            "First emergence is a selectable, reachable, haulable stack in actual shallow water");
        first.stackCount = 199;
        produce.action();
        Check(first.stackCount == 200, "Wet output merges to the native stack limit");
        produce.action();
        Thing second = cave.listerThings.ThingsOfDef(StoneDef).Single(thing => thing != first);
        Check(second.Position == outlets[1].Cell && second.Position.GetTerrain(cave).IsWater,
            "Available second water outlet precedes the dry shore");
        second.stackCount = 200;
        produce.action();
        Thing fallback = cave.listerThings.ThingsOfDef(StoneDef).Single(thing => thing != first && thing != second);
        Check(fallback.Position == outlets[2].Cell && fallback.Position.GetTerrain(cave) == QiSoil,
            "Two full water stacks send production to the adjacent dry shore");
        fallback.stackCount = 200;
        Thing blocker = GenSpawn.Spawn(ThingDefOf.WoodLog, outlets[3].Cell, cave);
        int count = cave.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount);
        produce.action();
        Check(spring.Reserve == reserve - 4 && cave.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount) == count,
            "Four actual products debit four reserve and blocked outputs debit zero");
        blocker.Destroy();
        second.stackCount = 1;
        fallback.stackCount = 1;
    }
}
