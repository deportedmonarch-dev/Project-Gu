using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private const int SecondCargoCount = 29;
    private const string AssignHaulerLabel = "Assign to underground hauling";

    private static void ExerciseAutomaticHauling()
    {
        Map home = Find.CurrentMap;
        Pawn pawn = home.mapPawns.FreeColonistsSpawned.First(person => !person.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
        Pawn blocker = home.mapPawns.FreeColonistsSpawned.First(person => person != pawn);
        var site = OpenIndependentSite(pawn, home);
        var surface = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        Map cave = exit.Map;
        IntVec3 cell = EmptyCargoCell(home, surface.Position, pawn, 7);
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        stockpile.AddCell(cell);
        var filter = stockpile.GetStoreSettings().filter;
        filter.SetDisallowAll();

        Thing first = GenSpawn.Spawn(MakeStack(CargoPrototypeCount), EmptyCargoCell(cave, exit.Position, null, 7), cave);
        Thing second = GenSpawn.Spawn(MakeStack(SecondCargoCount), EmptyCargoCell(cave, exit.Position, null, 7), cave);
        second.SetForbidden(true, false);
        SetOnlyHaulingWork(pawn);
        blocker.drafter.Drafted = true;
        Prefs.DevMode = false;
        surface.GetFloatMenuOptions(pawn).Single(option => option.Label == AssignHaulerLabel).action();
        Check(site.Hauling.Worker == pawn && site.Hauling.Enabled, "Player assigned one Hauling colonist without Dev Mode");
        AssertNoHaulJob(site, pawn, "Filtered surface stockpile blocks departure");

        filter.SetAllow(StoneDef, true);
        Thing full = GenSpawn.Spawn(MakeStack(StoneDef.stackLimit), cell, home);
        AssertNoHaulJob(site, pawn, "Full surface stockpile blocks departure");
        full.Destroy();
        var blockingJob = JobMaker.MakeJob(JobDefOf.Wait, 500);
        blocker.jobs.StartJob(blockingJob, JobCondition.InterruptForced);
        Check(blocker.Reserve(cell, blockingJob), "Fixture reserved surface storage for another pawn");
        AssertNoHaulJob(site, pawn, "Reserved surface stockpile blocks departure");
        blocker.jobs.EndCurrentJob(JobCondition.InterruptForced, false);

        TransferToFixture(blocker, cave, exit.Position);
        AssertNoHaulJob(site, pawn, "Occupied landing blocks departure");
        TransferToFixture(blocker, home, cell + East);
        first.SetForbidden(true, false);
        AssertNoHaulJob(site, pawn, "Forbidden underground sources block departure");
        first.SetForbidden(false, false);
        var ballast = ThingMaker.MakeThing(ThingDefOf.Steel);
        ballast.stackCount = ballast.def.stackLimit;
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(ballast, false)
            && MassUtility.CountToPickUpUntilOverEncumbered(pawn, first) == 0, "Fixture filled native carrying mass with unrelated inventory");
        AssertNoHaulJob(site, pawn, "The hauler waits when their existing inventory fills carrying mass");
        ballast.Destroy();
        site.Hauling.NextCheckTick = 0;
        Check(GuPassageHauling.Plan(pawn)?.def == GuPassageHaulDefOf.GU_PassageHaulTravel,
            "The available stack and storage qualify for a trip");
        site.Hauling.NextCheckTick = 0;

        TickUntil(() => pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulPack, 1800, "Native work issued an underground pickup");
        Check(pawn.Map == cave && pawn.Position != first.Position && site.Hauling.Cargo == null
            && cave.reservationManager.ReservedBy(first, pawn, pawn.CurJob), "Pickup walks toward a reserved source stack");
        ToggleHauling(exit, false);
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        Check(first.Spawned && first.stackCount == CargoPrototypeCount && !cave.reservationManager.IsReserved(first),
            "Cancelling pickup kept the original ground stack and released its reservation");
        Check(GuPassageHauling.Plan(pawn) == null, "Pausing the route suppresses new work");

        Thing removedTarget = GenSpawn.Spawn(MakeStack(4), EmptyCargoCell(cave, exit.Position, null, 9), cave);
        first.SetForbidden(true, false);
        ToggleHauling(exit, true);
        TickUntil(() => pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulPack, 1800, "Hauling picked a replacement target");
        Check(pawn.CurJob.targetA.Thing == removedTarget, "The replacement stack is reserved for pickup");
        removedTarget.Destroy();
        first.SetForbidden(false, false);
        TickUntil(() => site.Hauling.Cargo != null, 1800, "Re-enabled Hauling packed the stack after moving to it");
        Check(site.Hauling.Cargo == first && pawn.Map == cave && first.stackCount == CargoPrototypeCount,
            "Automatic pickup packed the original physical stack into the worker inventory");
        TransferToFixture(blocker, home, surface.Position);
        blocker.drafter.Drafted = true;
        TickUntil(() => false, 350);
        Check(blocker.Position == surface.Position && pawn.Map == cave && site.Hauling.Cargo == first
            && PhysicalStoneCount() == CargoPrototypeCount + SecondCargoCount,
            "Blocked return holds cargo once in inventory after further work opportunities");
        Check(Find.CurrentMap == home, "Automatic travel did not change the viewed map");

        GameDataSaveLoader.SaveGame(SaveName);
        Report("Automatic hauling ran without Dev Mode. Filters, full storage, reservations, load limits, a blocked landing, forbidden sources, cancelled pickup and a removed target preserved all 66 shipment stones. Saved the original 37-stone cargo in inventory underground.");
    }

    private static void VerifyAutomaticHaulingReload()
    {
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        var surface = home.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.endpoint).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(surface, out var site, out _), "Saved route resolved");
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        Map cave = exit.Map;
        Pawn pawn = site.Hauling.Worker;
        Pawn blocker = home.mapPawns.FreeColonistsSpawned.Single(person => person.Position == surface.Position);
        Thing cargo = site.Hauling.Cargo;
        Check(site.Hauling.Enabled && pawn.Map == cave && cargo?.stackCount == CargoPrototypeCount
            && cargo.holdingOwner == pawn.inventory.GetDirectlyHeldThings(), "Fresh process restored worker, enabled route and original native inventory custody");
        Check(PhysicalStoneCount() == CargoPrototypeCount + SecondCargoCount, "Reload conserved the full shipment and the second ground stack");

        var stockpile = home.zoneManager.AllZones.OfType<Zone_Stockpile>().Single();
        IntVec3 cell = stockpile.Cells.Single();
        var filter = stockpile.GetStoreSettings().filter;
        TransferToFixture(blocker, home, cell + East);
        Prefs.DevMode = false;
        TickUntil(() => pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulStore, 2000, "Cargo travelled to the surface and native hauling work reserved storage");
        Check(pawn.Map == home && pawn.Position != cell && home.reservationManager.ReservedBy(cell, pawn, pawn.CurJob),
            "Surface delivery walks toward a reserved native storage cell");
        filter.SetAllow(StoneDef, false);
        TickUntil(() => pawn.CurJobDef != GuPassageHaulDefOf.GU_PassageHaulStore, 900, "Filter changed during delivery");
        Check(site.Hauling.Cargo == cargo && cargo.stackCount == CargoPrototypeCount && !home.reservationManager.IsReserved(cell),
            "Changed filter prevented placement and retained packed cargo");

        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, surface.Position), JobCondition.InterruptForced);
        TickUntil(() => pawn.Position == surface.Position, 1000, "Worker walked away from the rejected storage cell");
        filter.SetAllow(StoneDef, true);
        site.Hauling.NextCheckTick = 0;
        TickUntil(() => pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulStore, 2000, "Delivery retried after reopening storage");
        ToggleHauling(surface, false);
        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 180), JobCondition.InterruptForced);
        Check(site.Hauling.Cargo == cargo && !home.reservationManager.IsReserved(cell), "Forced interruption released storage and left the cargo held");

        const int partialRoom = 13;
        int filler = StoneDef.stackLimit - partialRoom;
        Thing stored = GenSpawn.Spawn(MakeStack(filler), cell, home);
        ToggleHauling(surface, true);
        TickUntil(() => stored.stackCount == StoneDef.stackLimit, 2200, "Native placement filled the partial surface stack");
        Check(site.Hauling.Cargo == cargo && cargo.stackCount == CargoPrototypeCount - partialRoom
            && PhysicalStoneCount() == CargoPrototypeCount + SecondCargoCount + filler,
            "Partial room limited delivery and left the remainder in the same inventory stack");
        IntVec3 extra = EmptyCargoCell(home, surface.Position, pawn, 6);
        stockpile.AddCell(extra);
        cave.listerThings.ThingsOfDef(StoneDef).Single().SetForbidden(false, false);
        TickUntil(() => home.listerThings.ThingsOfDef(StoneDef).Sum(item => item.stackCount)
            == CargoPrototypeCount + SecondCargoCount + filler, 4500, "The rest of the first stack and the second shipment reached native storage automatically");
        Check(pawn.Map == home && site.Hauling.Cargo == null && cave.listerThings.ThingsOfDef(StoneDef).Count == 0
            && home.listerThings.ThingsOfDef(StoneDef).All(item => stockpile.Cells.Contains(item.Position))
            && PhysicalStoneCount() == CargoPrototypeCount + SecondCargoCount + filler,
            "Successive automatic cycles conserved every stone and placed all cargo in the stockpile");

        ToggleHauling(surface, false);
        Prefs.DevMode = true;
        surface.GetFloatMenuOptions(pawn).Single(option => option.Label == "Enter underground level").action();
        TickUntil(() => pawn.Map == cave, 1000, "Existing ordered Pawn travel still enters the cavern");
        exit.GetFloatMenuOptions(pawn).Single(option => option.Label == "Use underground passage").action();
        TickUntil(() => pawn.Map == home, 1000, "Existing ordered Pawn travel still returns to the surface");
        Thing stranded = GenSpawn.Spawn(MakeStack(3), exit.Position + East, cave);
        exit.DeSpawn();
        ToggleHauling(surface, true);
        AssertNoHaulJob(site, pawn, "Missing passage suppresses jobs and leaves the stranded stack untouched");
        Check(stranded.Spawned && stranded.stackCount == 3 && pawn.Map == home, "A closed route neither generates another map nor removes physical cargo");
        Report("Fresh-process reload resumed cargo. Delivery cancellation, filtering after dispatch, partial capacity, successive stacks, ordinary ordered travel and a missing passage all preserved native custody.");
    }

    private static void SetOnlyHaulingWork(Pawn pawn)
    {
        pawn.workSettings.EnableAndInitialize();
        foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            if (!pawn.WorkTypeIsDisabled(work)) pawn.workSettings.SetPriority(work, work == WorkTypeDefOf.Hauling ? 1 : 0);
        pawn.jobs.StopAll();
    }

    private static void AssertNoHaulJob(GuIndependentSite site, Pawn pawn, string message)
    {
        site.Hauling.NextCheckTick = 0;
        Check(GuPassageHauling.Plan(pawn) == null, message);
    }

    private static void ToggleHauling(Building_GuPocketProbeEndpoint endpoint, bool enabled)
    {
        var toggle = endpoint.GetGizmos().OfType<Command_Toggle>().Single(command => command.defaultLabel == "Underground hauling");
        if (toggle.isActive() != enabled) toggle.toggleAction();
    }

    private static IntVec3 EmptyCargoCell(Map map, IntVec3 origin, Pawn pawn, int minimumDistance) =>
        GenRadial.RadialCellsAround(origin, 14f, false).First(cell => cell.DistanceToSquared(origin) >= minimumDistance * minimumDistance
            && cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map) && cell.GetThingList(map).Count == 0
            && map.zoneManager.ZoneAt(cell) == null && (pawn == null
                ? map.reachability.CanReach(origin, cell, PathEndMode.OnCell, TraverseMode.PassDoors)
                : pawn.CanReach(cell, PathEndMode.OnCell, Danger.None)));

    private static int PhysicalStoneCount() => Find.Maps.Sum(map => map.listerThings.ThingsOfDef(StoneDef).Sum(item => item.stackCount)
        + map.mapPawns.AllPawns.Sum(pawn => (pawn.inventory?.GetDirectlyHeldThings().Where(item => item.def == StoneDef)
            .Sum(item => item.stackCount) ?? 0) + (pawn.carryTracker?.CarriedThing is { } cargo && cargo.def == StoneDef ? cargo.stackCount : 0)));

    private static void TickUntil(Func<bool> condition, int maxTicks, string message = null)
    {
        for (int tick = 0; tick < maxTicks && !condition(); tick++) Find.TickManager.DoSingleTick();
        if (message != null) Check(condition(), message);
    }
}
