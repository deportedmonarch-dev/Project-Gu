using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private const int CargoPrototypeCount = 37;

    private static void ExerciseCargoPrototype()
    {
        Map home = Find.CurrentMap;
        Pawn pawn = home.mapPawns.FreeColonistsSpawned.First();
        var site = OpenIndependentSite(pawn, home);
        Map cave = site.Levels.Single().LiveMap;
        var surface = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;

        IntVec3 storageCell = GenRadial.RadialCellsAround(surface.Position, 8f, false).First(cell =>
            cell.DistanceToSquared(surface.Position) >= 16 && cell.Standable(home)
            && !cell.Fogged(home) && cell.GetThingList(home).Count == 0
            && home.zoneManager.ZoneAt(cell) == null && pawn.CanReach(cell, PathEndMode.OnCell, Danger.None));
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        stockpile.AddCell(storageCell);
        stockpile.GetStoreSettings().filter.SetDisallowAll();
        stockpile.GetStoreSettings().filter.SetAllow(StoneDef, true);

        Check(JobDriver_GuPocketProbeTravel.TryTravel(pawn, surface, out string reason) && pawn.Map == cave,
            $"The source passage returns the collector to the original cavern. {reason}");
        Thing marker = GenSpawn.Spawn(MakeStack(CargoPrototypeCount), exit.Position + East, cave);
        Check(MassUtility.CountToPickUpUntilOverEncumbered(pawn, marker) >= CargoPrototypeCount
            && pawn.carryTracker.AvailableStackSpace(StoneDef) >= CargoPrototypeCount,
            "The selected stack fits both native load limits");
        Check(pawn.carryTracker.TryStartCarry(marker, CargoPrototypeCount, false) == CargoPrototypeCount
            && pawn.carryTracker.CarriedThing == marker, "Native pickup puts the original stack in carry custody");
        Check(!JobDriver_GuPocketProbeTravel.TryTravel(pawn, exit, out _) && pawn.Map == cave
            && pawn.carryTracker.CarriedThing == marker, "Ordinary passage refuses the unpacked carry tracker");

        var hands = pawn.carryTracker.GetDirectlyHeldThings();
        var inventory = pawn.inventory.GetDirectlyHeldThings();
        Check(hands.TryTransferToContainer(marker, inventory, CargoPrototypeCount, false) == CargoPrototypeCount
            && marker.holdingOwner == inventory, "Native ThingOwner moves the original stack to Pawn inventory");
        Check(JobDriver_GuPocketProbeTravel.TryTravel(pawn, exit, out reason) && pawn.Map == home
            && marker.holdingOwner == inventory && marker.stackCount == CargoPrototypeCount,
            $"Passage transfer retains the same stack in native Pawn inventory. {reason}");
        Check(Find.Maps.Sum(map => map.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount)) == 0,
            "The packed stack has no duplicate on either map");

        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Cargo prototype saved one original {CargoPrototypeCount}-stone stack in Pawn inventory after native passage travel.");
    }

    private static void VerifyCargoPrototypeReload()
    {
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        Pawn pawn = home.mapPawns.FreeColonistsSpawned.Single(person =>
            person.inventory.GetDirectlyHeldThings().Any(thing => thing.def == StoneDef));
        var inventory = pawn.inventory.GetDirectlyHeldThings();
        Thing marker = inventory.Single(thing => thing.def == StoneDef);
        Check(marker.stackCount == CargoPrototypeCount && marker.holdingOwner == inventory,
            "Fresh-process reload restored the packed stack once and retained its Pawn owner");
        Check(inventory.TryDrop(marker, pawn.Position, home, ThingPlaceMode.Near, out Thing placed)
            && placed == marker && marker.Map == home, "Native drop places the transported stack on the surface");
        var stockpile = home.zoneManager.AllZones.OfType<Zone_Stockpile>().Single();
        Job haul = HaulAIUtility.HaulToStorageJob(pawn, placed, false);
        Check(haul != null && haul.targetB.Cell == stockpile.Cells.Single(),
            "Vanilla hauling selects the filtered surface stockpile");
        pawn.jobs.StartJob(haul, JobCondition.InterruptForced);
        IntVec3 destination = stockpile.Cells.Single();
        for (int tick = 0; tick < 750 && marker.Position != destination; tick++)
            Find.TickManager.DoSingleTick();
        Check(marker.Spawned && marker.Map == home && marker.Position == destination
            && marker.stackCount == CargoPrototypeCount && !inventory.Contains(marker),
            "The original stack reached storage in an actual native hauling job");
        Check(Find.Maps.Sum(map => map.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount)) == CargoPrototypeCount,
            "Exactly the prototype quantity exists as physical stacks on both maps");
        Report($"Cargo prototype delivered {CargoPrototypeCount} stones to the surface stockpile through vanilla hauling after reload.");
    }
}
