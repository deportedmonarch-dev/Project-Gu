using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static bool VanillaGuInventory => GenCommandLine.CommandLineArgPassed("guvanillainventory");
    private static string VanillaInventoryFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-vanilla-inventory.json");
    private static Thing_GuDormantProjection gearFaultThing;
    private static bool gearFaultObserved;
    private record VanillaInventoryFixture(GuRecordSnapshot Record, string PawnId);

    private static void ExerciseVanillaGuInventory()
    {
        var map = Find.CurrentMap;
        var pawn = map.mapPawns.FreeColonistsSpawned.First();
        EnsureInventoryFixtureSpace(pawn);
        var thing = PackTestGu(pawn);
        var record = IndividualRegistry.Resolve(thing);
        var original = Snapshot(record);
        Check(GuGearInventoryGuard.Installed && original.Revision == 3 && original.Custody.Kind == GuCustodyKind.DormantInventory,
            "The original projection is authorized in native inventory at revision three");

        Prefs.DevMode = false;
        var observer = new Harmony("projectgu.verification.ordinaryinventory");
        try
        {
            gearFaultObserved = false;
            gearFaultThing = thing;
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(InterruptGearPlacement)));
            GearDrop(pawn, thing);
            Check(gearFaultObserved && !thing.Spawned && thing.holdingOwner == pawn.inventory.GetDirectlyHeldThings()
                && Snapshot(IndividualRegistry.Resolve(thing)) == original && thing.GuRevision == 3,
                "Native Gear placement interrupted after spawn rolled back the exact Thing and preserved revision three");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            gearFaultThing = null;
        }

        var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(steel, false), "Added a vanilla Gear control item");
        GearDrop(pawn, steel);
        Check(steel.Spawned && steel.Map == map && Snapshot(IndividualRegistry.Resolve(thing)) == original,
            "The Gear handler still drops ordinary items through native behavior");
        steel.Destroy();

        GearDrop(pawn, thing);
        var dropped = AssertOrdinaryGround(thing, original, 4);
        GearDrop(pawn, thing);
        Check(Snapshot(IndividualRegistry.Resolve(thing)) == dropped, "A repeated Gear drop cannot advance a ground binding");
        TransferToFixture(pawn, map, EmptyCargoCell(map, thing.Position, pawn, 7));
        IntVec3 start = pawn.Position;
        var ballast = ThingMaker.MakeThing(ThingDefOf.Steel);
        ballast.stackCount = ballast.def.stackLimit;
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(ballast, false)
            && OrdinaryPickupOption(pawn, thing) == null && Snapshot(IndividualRegistry.Resolve(thing)) == dropped,
            "The native context menu respects carrying mass and leaves the ground Gu intact");
        ballast.Destroy();

        var staleOption = OrdinaryPickupOption(pawn, thing);
        Check(staleOption != null, "The native FromThing provider exposes a normal pickup option outside Development Mode");
        staleOption.action();
        Check(pawn.CurJob?.def.defName == "GU_PickupDormant" && map.reservationManager.IsReserved(thing),
            "The player order reserved the exact Thing before movement");
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        Check(!map.reservationManager.IsReserved(thing) && Snapshot(IndividualRegistry.Resolve(thing)) == dropped,
            "Cancellation releases the reservation and preserves the original ground claim");
        OrdinaryPickupOption(pawn, thing).action();
        TickUntil(() => thing.holdingOwner == pawn.inventory.GetDirectlyHeldThings(), 1000, "The walking pickup reached the thing");
        Check(pawn.Position != start && IndividualRegistry.Resolve(thing) == record
            && Snapshot(record) == original with { Revision = 5 } && thing.GuRevision == 5,
            "Ordinary pickup used native walking and committed inventory exactly once");

        GearDrop(pawn, thing);
        var nextGround = AssertOrdinaryGround(thing, original, 6);
        staleOption.action();
        Check(pawn.CurJob?.def.defName != "GU_PickupDormant" && Snapshot(IndividualRegistry.Resolve(thing)) == nextGround,
            "The earlier menu order cannot take the same Thing after a revision-changing round trip");
        TransferToFixture(pawn, map, EmptyCargoCell(map, thing.Position, pawn, 7));
        OrdinaryPickupOption(pawn, thing).action();
        Check(pawn.CurJob?.def.defName == "GU_PickupDormant" && thing.Spawned, "Saved at a stable ground binding while the native pickup job was walking");
        File.WriteAllText(VanillaInventoryFixturePath, JsonSerializer.Serialize(new VanillaInventoryFixture(nextGround, pawn.GetUniqueLoadID())));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Outside Development Mode, the native Gear handler and right-click pickup preserved one Thing through revisions 3–6. Gear staging fault, overweight pickup, a cancelled job, and stale controls preserved custody.");
    }

    private static void VerifyVanillaGuInventoryReload()
    {
        Prefs.DevMode = false;
        var fixture = JsonSerializer.Deserialize<VanillaInventoryFixture>(File.ReadAllText(VanillaInventoryFixturePath));
        var thing = GroundDormantGu().Single();
        var pawn = thing.Map.mapPawns.FreeColonistsSpawned.Single(person => person.GetUniqueLoadID() == fixture.PawnId);
        var record = IndividualRegistry.Resolve(thing);
        Check(record != null && Snapshot(record) == fixture.Record && pawn.CurJob?.def.defName == "GU_PickupDormant",
            "Fresh-process reload restored the original ground claim and the saved walking job");
        TickUntil(() => thing.holdingOwner == pawn.inventory.GetDirectlyHeldThings(), 1000, "The saved pickup job completed");
        Check(IndividualRegistry.Resolve(thing) == record && Snapshot(record) == fixture.Record with {
            Revision = 7, Custody = new(GuCustodyKind.DormantInventory, fixture.Record.Custody.NativeOwnerId, fixture.PawnId)
        } && thing.GuRevision == 7, "The saved job committed exact inventory custody at revision seven");
        GearDrop(pawn, thing);
        AssertOrdinaryGround(thing, fixture.Record, 8);
        Check(IndividualRegistry.Individuals.Single() == record && ActuallyProjectedGu().Single() == thing,
            "Exactly one record and one physical projection survived the repeated pickup and drop");
        Report("Fresh-process reload resumed the saved pickup to revision seven. The actual Gear handler returned the same Thing to its map at revision eight.");
    }

    private static GuRecordSnapshot AssertOrdinaryGround(Thing_GuDormantProjection thing, GuRecordSnapshot prior, int revision)
    {
        var record = IndividualRegistry.Resolve(thing);
        var expected = prior with { Revision = revision, Custody = new(GuCustodyKind.DormantGround, prior.Custody.NativeOwnerId) };
        Check(record != null && thing.Spawned && thing.holdingOwner == thing.Map.GetDirectlyHeldThings()
            && thing.GuRevision == revision && Snapshot(record) == expected,
            $"The Gear handler committed ground custody of the same Thing at revision {revision}");
        return expected;
    }

    private static FloatMenuOption OrdinaryPickupOption(Pawn pawn, Thing thing)
    {
        var context = new FloatMenuContext(new List<Pawn> { pawn }, thing.Position.ToVector3Shifted(), thing.Map);
        var provider = new FloatMenuOptionProvider_FromThing();
        var method = AccessTools.DeclaredMethod(typeof(FloatMenuOptionProvider_FromThing), "GetOptionsFor");
        var options = (IEnumerable<FloatMenuOption>)method.Invoke(provider, new object[] { thing, context });
        return options.SingleOrDefault(option => option.Label == $"Pick up {thing.Label}");
    }

    private static void InterruptGearPlacement(ThingWithComps __instance)
    {
        if (__instance != gearFaultThing) return;
        gearFaultObserved = __instance.Spawned && IndividualRegistry.CustodyTransitionInProgress && IndividualRegistry.Resolve(__instance) == null;
        throw new InvalidOperationException("Injected interruption after native Gear placement");
    }

    private static void GearDrop(Pawn pawn, Thing thing)
    {
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn);
        AccessTools.DeclaredMethod(typeof(ITab_Pawn_Gear), "InterfaceDrop").Invoke(new ITab_Pawn_Gear(), new object[] { thing });
    }
}
