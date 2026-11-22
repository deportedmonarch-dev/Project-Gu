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
        Check(original.Revision == 2 && original.Custody.Kind == GuCustodyKind.DormantInventory,
            "The original projection is authorized in native inventory at custody revision two (V1: three, pickup no longer commits)");

        Prefs.DevMode = false;
        // The V1 Gear guard is removed (RFC-0004 §15.3). The vanilla Gear handler now moves the Gu with no Project GU
        // transaction, and the same Thing stays authorized wherever the engine places it.
        var drop = AccessTools.DeclaredMethod(typeof(ITab_Pawn_Gear), "InterfaceDrop");
        Check(Harmony.GetPatchInfo(drop)?.Owners.Any(owner => owner.StartsWith("projectgu.livingcultivation", StringComparison.Ordinal)) != true,
            "Project GU no longer patches the Gear drop handler");
        var observer = new Harmony("projectgu.verification.ordinaryinventory");
        try
        {
            gearFaultObserved = false;
            gearFaultThing = thing;
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveVanillaGearPlacement)));
            GearDrop(pawn, thing);
            Check(gearFaultObserved && thing.Spawned && Snapshot(IndividualRegistry.Resolve(thing)) == original with {
                    Custody = new(GuCustodyKind.DormantGround, original.Custody.NativeOwnerId) } && thing.GuRevision == 2,
                "The vanilla Gear drop placed the same authorized Thing on the map with no Project GU transaction and unchanged revisions");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            gearFaultThing = null;
        }
        thing.DeSpawn();
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(thing, false) && Snapshot(IndividualRegistry.Resolve(thing)) == original,
            "A raw native return to inventory keeps the same authorization and revisions");

        var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
        Check(pawn.inventory.GetDirectlyHeldThings().TryAdd(steel, false), "Added a vanilla Gear control item");
        GearDrop(pawn, steel);
        Check(steel.Spawned && steel.Map == map && Snapshot(IndividualRegistry.Resolve(thing)) == original,
            "The Gear handler still drops ordinary items through native behavior");
        steel.Destroy();

        GearDrop(pawn, thing);
        var dropped = AssertOrdinaryGround(thing, original);
        // The Gear tab lists only held items, so a second drop cannot be requested. Native ownership and revisions are unchanged.
        Check(!pawn.inventory.GetDirectlyHeldThings().Contains(thing) && Snapshot(IndividualRegistry.Resolve(thing)) == dropped,
            "A ground binding is no longer offered by the Gear tab and keeps its revisions");
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
            && Snapshot(record) == original && thing.GuRevision == original.Revision,
            "Ordinary pickup used native walking and moved the same Thing into inventory without changing revisions");

        GearDrop(pawn, thing);
        var nextGround = AssertOrdinaryGround(thing, original);
        // V1 rejected this order because the round trip changed the revision. In V2 nothing about the authorization
        // changed, so the earlier option revalidates current conditions and may act. Loss of authorization is checked below.
        // Stand away first: an adjacent pickup completes inside StartJob and leaves no job to observe.
        TransferToFixture(pawn, map, EmptyCargoCell(map, thing.Position, pawn, 7));
        staleOption.action();
        Check(pawn.CurJob?.def.defName == "GU_PickupDormant" && Snapshot(IndividualRegistry.Resolve(thing)) == nextGround,
            "An earlier menu order revalidates at execution and acts only because the same Thing is still authorized");
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        // Fixture setup only: development spawn needs Development Mode. The ordinary action below runs with it off.
        Prefs.DevMode = true;
        EnsureInventoryFixtureSpace(pawn);
        var revoked = SpawnGroundGu(pawn);
        Prefs.DevMode = false;
        var revokedOption = OrdinaryPickupOption(pawn, revoked);
        Check(revokedOption != null, "A second ground Gu offers an ordinary pickup option");
        revoked.Destroy(DestroyMode.Vanish);
        IndividualRegistry.ApplyPendingEvents();
        revokedOption.action();
        Check(pawn.CurJob?.def.defName != "GU_PickupDormant", "A menu order whose Gu lost authorization issues no job");
        TransferToFixture(pawn, map, EmptyCargoCell(map, thing.Position, pawn, 7));
        OrdinaryPickupOption(pawn, thing).action();
        Check(pawn.CurJob?.def.defName == "GU_PickupDormant" && thing.Spawned, "Saved at a stable ground binding while the native pickup job was walking");
        File.WriteAllText(VanillaInventoryFixturePath, JsonSerializer.Serialize(new VanillaInventoryFixture(nextGround, pawn.GetUniqueLoadID())));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Outside Development Mode, the native Gear handler and right-click pickup preserved one Thing at a single custody revision through every physical move. Gear staging fault, overweight pickup, a cancelled job, and stale controls preserved custody.");
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
            Custody = new(GuCustodyKind.DormantInventory, fixture.Record.Custody.NativeOwnerId, fixture.PawnId)
        } && thing.GuRevision == fixture.Record.Revision, "The saved job moved the same Thing into the exact inventory without changing revisions");
        GearDrop(pawn, thing);
        AssertOrdinaryGround(thing, fixture.Record);
        Check(IndividualRegistry.Individuals.Count(entry => entry.IsActionable) == 1 && ActuallyProjectedGu().Single() == thing,
            "Exactly one record and one physical projection survived the repeated pickup and drop");
        Report("Fresh-process reload resumed the saved pickup. The actual Gear handler returned the same Thing to its map. Every physical move kept the custody revision.");
    }

    private static GuRecordSnapshot AssertOrdinaryGround(Thing_GuDormantProjection thing, GuRecordSnapshot prior)
    {
        var record = IndividualRegistry.Resolve(thing);
        var expected = prior with { Custody = new(GuCustodyKind.DormantGround, prior.Custody.NativeOwnerId) };
        Check(record != null && thing.Spawned && thing.holdingOwner == thing.Map.GetDirectlyHeldThings()
            && thing.GuRevision == prior.Revision && Snapshot(record) == expected,
            "The Gear handler placed the same authorized Thing on its map without changing revisions");
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

    private static void ObserveVanillaGearPlacement(ThingWithComps __instance)
    {
        if (__instance == gearFaultThing)
            gearFaultObserved = __instance.Spawned && !IndividualRegistry.CustodyTransitionInProgress;
    }

    private static void GearDrop(Pawn pawn, Thing thing)
    {
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn);
        AccessTools.DeclaredMethod(typeof(ITab_Pawn_Gear), "InterfaceDrop").Invoke(new ITab_Pawn_Gear(), new object[] { thing });
    }
}
