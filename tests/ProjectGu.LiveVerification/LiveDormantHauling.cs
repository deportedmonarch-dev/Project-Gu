using System.Text.Json;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Ordinary hauling of dormant Gu (owner decisions §6). Native hauling moves the same authorized Thing to storage without
/// changing either revision, condition or allegiance, respects forbidden items and storage filters, and survives a save
/// taken mid-haul.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool DormantHauling => GenCommandLine.CommandLineArgPassed("gudormanthauling");
    private static string HaulingFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-dormant-hauling.json");
    private record HaulingSnapshot(string Id, string NativeId, int Custody, int State, GuCondition Condition, string Refiner);
    private record HaulingFixture(HaulingSnapshot Stored, HaulingSnapshot Moving, string HaulerId);

    private static HaulingSnapshot HaulSnapshot(GuIndividual record, Thing thing) => new(record.Id, thing.GetUniqueLoadID(),
        record.CustodyRevision, record.StateRevision, record.Condition, record.Refinement?.CultivatorId);

    private static void ExerciseDormantHauling()
    {
        var registry = IndividualRegistry;
        Map home = Find.CurrentMap;
        var colonists = home.mapPawns.FreeColonistsSpawned.ToArray();
        Pawn hauler = colonists.First(person => !person.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
        foreach (var other in colonists.Where(person => person != hauler)) other.drafter.Drafted = true;
        EnsureInventoryFixtureSpace(hauler);

        var gu = SpawnGroundGu(hauler);
        var record = registry.Resolve(gu);
        var original = HaulSnapshot(record, gu);
        Check(gu.def.EverHaulable && gu.def.stackLimit == 1 && !gu.CanStackWith(gu), "The dormant Gu Def is haulable and unstackable");

        IntVec3 storeCell = EmptyCargoCell(home, hauler.Position, hauler, 8);
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        foreach (var cell in GenAdj.CellsOccupiedBy(storeCell, Rot4.North, new IntVec2(3, 3))
                     .Where(cell => cell.InBounds(home) && cell.Standable(home) && cell.GetThingList(home).Count == 0))
            stockpile.AddCell(cell);
        var filter = stockpile.GetStoreSettings().filter;
        filter.SetDisallowAll();
        filter.SetAllow(gu.def, true);
        SetOnlyHaulingWork(hauler);
        Prefs.DevMode = false;

        gu.SetForbidden(true, false);
        IntVec3 start = gu.Position;
        TickUntil(() => !gu.Spawned || gu.Position != start, 900);
        Check(gu.Spawned && gu.Position == start && registry.Resolve(gu) == record, "A forbidden dormant Gu was not hauled");

        filter.SetAllow(gu.def, false);
        gu.SetForbidden(false, false);
        TickUntil(() => !gu.Spawned || gu.Position != start, 900);
        Check(gu.Spawned && gu.Position == start, "A storage filter that disallows the Gu prevents hauling");

        filter.SetAllow(gu.def, true);
        TickUntil(() => gu.ParentHolder is Pawn_CarryTracker, 2500, "The hauler picked up the dormant Gu");
        Check(registry.Resolve(gu) == record && record.Custody.Kind == GuCustodyKind.DormantInventory
            && record.Custody.HolderPawnId == hauler.GetUniqueLoadID() && HaulSnapshot(record, gu) == original,
            "While carried, the same authorized Thing is observed with its hauler and unchanged revisions");
        TickUntil(() => gu.Spawned && stockpile.ContainsCell(gu.Position), 2500, "The hauler stored the dormant Gu");
        Check(registry.Resolve(gu) == record && record.Custody.Kind == GuCustodyKind.DormantGround && HaulSnapshot(record, gu) == original
            && record.Condition == GuCondition.Dormant && record.Refinement == null && gu.stackCount == 1,
            "Storage kept the same Thing, both revisions, dormancy and no allegiance");
        Check(OrdinaryPickupOption(hauler, gu) != null, "Ordinary right-click pickup remains available for a stored Gu");

        // A second Gu is saved while the hauler carries it.
        Prefs.DevMode = true;
        // The hauler now stands beside the stockpile. Spawn the second Gu well outside storage so it needs hauling.
        TransferToFixture(hauler, home, EmptyCargoCell(home, storeCell, null, 8));
        EnsureInventoryFixtureSpace(hauler);
        var moving = SpawnGroundGu(hauler);
        Check(!stockpile.ContainsCell(moving.Position), "The second Gu starts outside storage");
        var movingRecord = registry.Resolve(moving);
        var movingSnapshot = HaulSnapshot(movingRecord, moving);
        Prefs.DevMode = false;
        TickUntil(() => moving.ParentHolder is Pawn_CarryTracker, 2500, "The hauler picked up the second Gu");
        Check(!registry.CustodyTransitionInProgress && HaulSnapshot(movingRecord, moving) == movingSnapshot, "Saved mid-haul with no transition open");
        File.WriteAllText(HaulingFixturePath, JsonSerializer.Serialize(new HaulingFixture(original, movingSnapshot, hauler.GetUniqueLoadID())));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Native hauling respected forbidden items and storage filters and stored the same dormant Gu with unchanged revisions, condition and allegiance. Saved while a second Gu was carried.");
    }

    private static void VerifyDormantHaulingReload()
    {
        var registry = IndividualRegistry;
        var fixture = JsonSerializer.Deserialize<HaulingFixture>(File.ReadAllText(HaulingFixturePath));
        Prefs.DevMode = false;
        var stored = registry.FindRecord(fixture.Stored.Id);
        var storedThing = registry.ProjectionOf(stored);
        Check(storedThing != null && registry.Resolve(storedThing) == stored && HaulSnapshot(stored, storedThing) == fixture.Stored
            && stored.Custody.Kind == GuCustodyKind.DormantGround, "The stored Gu reloaded in storage with unchanged revisions");
        var moving = registry.FindRecord(fixture.Moving.Id);
        var movingThing = registry.ProjectionOf(moving);
        Check(movingThing != null && registry.Resolve(movingThing) == moving && HaulSnapshot(moving, movingThing) == fixture.Moving,
            "The carried Gu reloaded as the same authorized Thing");
        var stockpile = movingThing.MapHeld.zoneManager.AllZones.OfType<Zone_Stockpile>().Single();
        TickUntil(() => movingThing.Spawned && stockpile.ContainsCell(movingThing.Position), 2500, "The reloaded haul completed");
        Check(registry.Resolve(movingThing) == moving && HaulSnapshot(moving, movingThing) == fixture.Moving
            && movingThing.Position != storedThing.Position, "The completed haul kept both revisions and stored the Gu in its own cell");
        Check(registry.Individuals.Count(record => record.IsActionable) == 2 && ActuallyProjectedGu().Count() == 2,
            "Exactly two records and two physical Gu exist after reload and hauling");
        Report("Fresh-process reload resumed the carried haul. Both Gu were stored as the same Things with unchanged revisions.");
    }
}
