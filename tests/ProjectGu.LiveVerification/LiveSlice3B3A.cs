using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B3A => GenCommandLine.CommandLineArgPassed("guslice3b3a");
    private static string InventoryFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-inventory-fixture.json");
    private static Thing_GuDormantProjection inventorySubject;
    private static ThingOwner observedInventory;
    private static Action afterGuInventoryAdd;
    private static Action afterGuInventoryDespawn;
    private static Action afterGuInventorySpawn;

    private record InventoryFixture(string CaveId, GuRecordSnapshot Held, GuRecordSnapshot Ground,
        GuRecordSnapshot Recovery, string RecoveryReason, GuRecordSnapshot Missing, GuRecordSnapshot WrongHolder, string ObservedHolderId);

    private static void ExerciseSlice3B3A()
    {
        Map home = Find.CurrentMap;
        Pawn traveler = home.mapPawns.FreeColonistsSpawned.First();
        Pawn other = home.mapPawns.FreeColonistsSpawned.First(pawn => pawn != traveler);
        var site = OpenIndependentSite(traveler, home);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var cave = site.Levels.Single().LiveMap;
        EnsureInventoryFixtureSpace(traveler);
        EnsureInventoryFixtureSpace(other);
        var registry = IndividualRegistry;
        var inventory = traveler.inventory.GetDirectlyHeldThings();
        var maxStacks = AccessTools.Field(typeof(ThingOwner), "maxStacks");
        int normalLimit = (int)maxStacks.GetValue(inventory);
        var thing = SpawnGroundGu(traveler);
        var record = registry.Resolve(thing);
        var ground = Snapshot(record);
        IntVec3 origin = thing.Position;

        Prefs.DevMode = false;
        bool rejectedOutsideDev = !registry.TryTakeDormant(thing, ground.Revision, traveler, out _);
        Prefs.DevMode = true;
        Check(rejectedOutsideDev && !registry.TryTakeDormant(thing, ground.Revision, null, out _)
            && !registry.TryTakeDormant(thing, ground.Revision - 1, traveler, out _)
            && Snapshot(registry.Resolve(thing)) == ground, "Pickup rejects disabled Development Mode, invalid holders and stale revisions");

        var ballast = ThingMaker.MakeThing(ThingDefOf.Steel);
        ballast.stackCount = ballast.def.stackLimit;
        Check(inventory.TryAdd(ballast, false) && MassUtility.CountToPickUpUntilOverEncumbered(traveler, thing) == 0
            && !registry.TryTakeDormant(thing, ground.Revision, traveler, out _),
            "Pickup respects native carrying mass");
        ballast.Destroy();
        maxStacks.SetValue(inventory, inventory.Count);
        Check(!inventory.CanAcceptAnyOf(thing, false) && !registry.TryTakeDormant(thing, ground.Revision, traveler, out _)
            && thing.Position == origin && Snapshot(registry.Resolve(thing)) == ground, "A native full inventory leaves ground custody intact");
        maxStacks.SetValue(inventory, normalLimit);

        var observer = new Harmony("projectgu.verification.slice3b3a.inventory");
        GuRecordSnapshot recoverySnapshot;
        string recoveryReason;
        try
        {
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingOwner<Thing>), nameof(ThingOwner.TryAdd),
                new[] { typeof(Thing), typeof(bool) }), postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterGuInventoryAdd)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.DeSpawn)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterGuInventoryDespawn)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterGuInventorySpawn)));
            inventorySubject = thing;
            observedInventory = inventory;

            const string reentrySave = "gu-inventory-reentry";
            bool afterNativeAdd = false;
            bool reentryBlocked = false;
            afterGuInventoryAdd = () =>
            {
                afterNativeAdd = thing.holdingOwner == inventory && inventory.Any(item => item == thing);
                reentryBlocked = registry.CustodyTransitionInProgress && registry.Resolve(thing) == null
                    && !registry.TryDropDormant(thing, ground.Revision, traveler, origin, out _);
                GameDataSaveLoader.SaveGame(reentrySave);
                throw new InvalidOperationException("Injected fault after native Gu inventory add");
            };
            Check(!registry.TryTakeDormant(thing, ground.Revision, traveler, out _) && afterNativeAdd && reentryBlocked
                && !File.Exists(GenFilePaths.FilePathForSavedGame(reentrySave))
                && thing.Position == origin && thing.holdingOwner == home.GetDirectlyHeldThings()
                && !inventory.Any(item => item == thing) && Snapshot(registry.Resolve(thing)) == ground,
                "A fault after native add rolls back the exact Thing and blocks save and action reentry");
            afterGuInventoryAdd = null;

            Check(registry.TryTakeDormant(thing, ground.Revision, traveler, out string reason), $"Native pickup succeeded. {reason}");
            var held = Snapshot(record);
            // V2: a physical move of the same authorized Thing is not a custody transition. Both revisions stay unchanged.
            Check(held == ground with { Custody = new(GuCustodyKind.DormantInventory,
                ground.Custody.NativeOwnerId, traveler.GetUniqueLoadID()) } && thing.GuRevision == held.Revision
                && registry.Resolve(thing) == record && !thing.Spawned && thing.holdingOwner == inventory
                && inventory.Count(item => item == thing) == 1, "Pickup moves the original Thing into inventory without changing revisions");
            Check(!registry.TryTakeDormant(thing, held.Revision, traveler, out _)
                && !registry.TryDropDormant(thing, held.Revision - 1, traveler, origin, out _)
                && !registry.TryDropDormant(thing, held.Revision, other, OpenInventoryCell(other), out _)
                && Snapshot(registry.Resolve(thing)) == held, "Wrong custody, stale revision and an unrelated inventory holder are rejected");

            Thing blocker = GenSpawn.Spawn(MakeStack(1), origin, home);
            Check(!registry.TryDropDormant(thing, held.Revision, traveler, origin, out _)
                && blocker.Spawned && thing.holdingOwner == inventory && Snapshot(registry.Resolve(thing)) == held,
                "An occupied destination leaves the holder and destination occupant intact");
            blocker.Destroy();
            bool afterNativeSpawn = false;
            afterGuInventorySpawn = () =>
            {
                afterNativeSpawn = thing.Spawned && thing.Position == origin && registry.Resolve(thing) == null;
                throw new InvalidOperationException("Injected fault after native Gu drop placement");
            };
            Check(!registry.TryDropDormant(thing, held.Revision, traveler, origin, out _) && afterNativeSpawn
                && !thing.Spawned && thing.holdingOwner == inventory && inventory.Count(item => item == thing) == 1
                && Snapshot(registry.Resolve(thing)) == held, "A fault after native drop returns the exact Thing to the original inventory");
            afterGuInventorySpawn = null;

            Check(registry.TryDropDormant(thing, held.Revision, traveler, origin, out reason), $"Native drop succeeded. {reason}");
            var returned = Snapshot(record);
            Check(returned == ground && thing.GuRevision == returned.Revision
                && thing.holdingOwner == home.GetDirectlyHeldThings() && thing.Position == origin
                && !registry.TryTakeDormant(thing, ground.Revision - 1, traveler, out _)
                && !registry.TryDropDormant(thing, held.Revision, traveler, origin, out _)
                && Snapshot(registry.Resolve(thing)) == returned,
                "A complete round trip keeps both revisions, rejects stale revisions and rejects a drop from the wrong endpoint");

            inventorySubject = SpawnGroundGu(other);
            var recovery = registry.Resolve(inventorySubject);
            recoverySnapshot = Snapshot(recovery);
            IntVec3 recoveryOrigin = inventorySubject.Position;
            Thing lateBlocker = null;
            afterGuInventoryDespawn = () =>
            {
                lateBlocker = GenSpawn.Spawn(MakeStack(1), recoveryOrigin, home);
                throw new InvalidOperationException("Injected fault with blocked Gu rollback cell");
            };
            // V1 quarantined this claim. In V2 the same Thing stays the authorized manifestation wherever recovery placed it.
            Check(!registry.TryTakeDormant(inventorySubject, recovery.Revision, other, out _)
                && lateBlocker?.Spawned == true && inventorySubject.Spawned && inventorySubject.Position != recoveryOrigin
                && inventorySubject.holdingOwner == home.GetDirectlyHeldThings() && inventorySubject.GuRevision == recovery.Revision
                && Snapshot(recovery) == recoverySnapshot && recovery.IsActionable && registry.Resolve(inventorySubject) == recovery,
                "Blocked original placement keeps the same authorized Gu in a nearby cell with an unchanged record");
            lateBlocker.Destroy();
            recoveryReason = recovery.QuarantineReason;
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            inventorySubject = null;
            observedInventory = null;
            afterGuInventoryAdd = afterGuInventoryDespawn = afterGuInventorySpawn = null;
        }

        Check(registry.TryTakeDormant(thing, thing.GuRevision, traveler, out _), "A new request can pick up the same Gu after the round trip");
        var missing = SpawnGroundGu(traveler);
        Check(registry.TryTakeDormant(missing, missing.GuRevision, traveler, out _), "Two distinct Gu can bind to one exact inventory");
        var missingSnapshot = Snapshot(registry.Resolve(missing));
        missing.Destroy(DestroyMode.Vanish);

        var wrong = SpawnGroundGu(traveler);
        Check(registry.TryTakeDormant(wrong, wrong.GuRevision, traveler, out _), "The wrong-holder fixture began in authorized inventory");
        var wrongBefore = Snapshot(registry.Resolve(wrong));
        var otherInventory = other.inventory.GetDirectlyHeldThings();
        Check(inventory.TryTransferToContainer(wrong, otherInventory, 1, false) == 1 && wrong.holdingOwner == otherInventory,
            "An external native move transferred the Gu to another holder");
        var wrongSnapshot = Snapshot(registry.Resolve(wrong));
        Check(wrongSnapshot == wrongBefore with { Custody = wrongBefore.Custody with { HolderPawnId = other.GetUniqueLoadID() } },
            "The external move changed only the observed holder. Identity, revisions and authorization are unchanged");

        var leftOnGround = SpawnGroundGu(traveler);
        Check(JobDriver_GuPocketProbeTravel.TryTravel(traveler, entrance, out string travelReason) && traveler.Map == cave
            && thing.MapHeld == cave && thing.holdingOwner == inventory && registry.Resolve(thing) == record,
            $"The same Gu and native inventory crossed the existing passage. {travelReason}");
        Check(!registry.TryTakeDormant(leftOnGround, leftOnGround.GuRevision, traveler, out _)
            && leftOnGround.Map == home && registry.Resolve(leftOnGround) != null, "Pickup cannot move a ground Gu across maps");
        var fixture = new InventoryFixture(cave.GetUniqueLoadID(), Snapshot(record), Snapshot(registry.Resolve(leftOnGround)),
            recoverySnapshot, recoveryReason, missingSnapshot, wrongSnapshot, other.GetUniqueLoadID());
        File.WriteAllText(InventoryFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Native capacity, mass, holder checks, blocked cell, stale requests, interrupted pickup and drop, guarded save reentry and nearby recovery without quarantine passed. Physical moves kept both revisions. One original Gu crossed to the cavern in its native inventory.");
    }

    private static void VerifySlice3B3AReload()
    {
        var fixture = JsonSerializer.Deserialize<InventoryFixture>(File.ReadAllText(InventoryFixturePath));
        var registry = IndividualRegistry;
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        AssertNativeAddress(cave);
        Pawn holder = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.Held.Custody.HolderPawnId);
        Pawn observedHolder = home.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.ObservedHolderId);
        var inventory = holder.inventory.GetDirectlyHeldThings();
        var otherInventory = observedHolder.inventory.GetDirectlyHeldThings();
        var physical = GroundDormantGu().Concat(inventory.OfType<Thing_GuDormantProjection>())
            .Concat(otherInventory.OfType<Thing_GuDormantProjection>()).ToArray();
        Check(registry.Individuals.Count == 5 && physical.Length == 4
            && physical.Select(thing => thing.GuId).Distinct().Count() == 4, "Fresh reload conserved the records and all four surviving Things");

        Thing_GuDormantProjection Verify(GuRecordSnapshot expected, ThingOwner owner)
        {
            var record = registry.Individuals.Single(entry => entry.Id == expected.Id);
            var thing = physical.SingleOrDefault(entry => entry.GuId == expected.Id);
            Check(Snapshot(record) == expected && record.IsActionable && registry.Resolve(record.Id) == record,
                "Reload retained the original canonical claim and its authorization");
            Check(thing != null && thing.GetUniqueLoadID() == expected.Custody.NativeOwnerId
                && thing.GuRevision == expected.Revision && thing.stackCount == 1 && thing.holdingOwner == owner
                && registry.Resolve(thing) == record, "Reload resolved the exact projection in its observed native holder without repair or duplication");
            return thing;
        }

        var held = Verify(fixture.Held, inventory);
        Verify(fixture.Ground, home.GetDirectlyHeldThings());
        Verify(fixture.Recovery, home.GetDirectlyHeldThings());
        // The wrong-holder move from V1 is now ordinary native movement. The Gu stays authorized in the other inventory.
        Verify(fixture.WrongHolder, otherInventory);
        // Phase 2 (owner decisions §1): demonstrated physical destruction is a terminal outcome. Phase 1 kept it unresolved.
        var missing = registry.Individuals.Single(entry => entry.Id == fixture.Missing.Id);
        Check(!missing.IsQuarantined && missing.Existence == GuExistence.Terminal && missing.TerminalOutcome == GuTerminalOutcome.Destroyed
            && !missing.IsUnresolved && missing.CustodyRevision == fixture.Missing.Revision && missing.Id == fixture.Missing.Id
            && missing.SpeciesDefName == fixture.Missing.Species && missing.LastKnown?.HolderId == fixture.Missing.Custody.HolderPawnId
            && registry.Resolve(missing.Id) == null, "The destroyed Gu ended as Destroyed with its identity, provenance and last holder retained");

        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(holder, exit, out string reason) && holder.Map == home
            && held.holdingOwner == inventory && held.MapHeld == home && Snapshot(registry.Resolve(held)) == fixture.Held,
            $"The reloaded holder returned to the surface with the same Gu. {reason}");
        EnsureInventoryFixtureSpace(holder);
        IntVec3 cell = OpenInventoryCell(holder);
        Check(registry.TryDropDormant(held, fixture.Held.Revision, holder, cell, out reason) && held.Position == cell
            && held.holdingOwner == home.GetDirectlyHeldThings() && !inventory.Any(item => item == held)
            && Snapshot(registry.Resolve(held)) == fixture.Held with {
                Custody = new(GuCustodyKind.DormantGround, fixture.Held.Custody.NativeOwnerId)
            }, $"After reload and travel a drop placed the original Thing on the ground without changing revisions. {reason}");
        Report("Fresh-process reload rebound the ground, inventory, recovered and externally moved Gu, and kept the destroyed Gu as a Destroyed tombstone. The original holder returned through the saved passage and dropped the same Gu.");
    }

    private static bool IsEmptyInventoryCell(Map map, IntVec3 cell) =>
        cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map) && cell.GetThingList(map).Count == 0;

    private static IntVec3 OpenInventoryCell(Pawn pawn) => GenAdj.CardinalDirections.Select(offset => pawn.Position + offset)
        .First(cell => IsEmptyInventoryCell(pawn.Map, cell));

    private static void EnsureInventoryFixtureSpace(Pawn pawn)
    {
        Map map = pawn.Map;
        bool HasOpenSide(IntVec3 cell) => GenAdj.CardinalDirections.Any(offset => IsEmptyInventoryCell(map, cell + offset));
        if (HasOpenSide(pawn.Position)) return;
        IntVec3 position = GenRadial.RadialCellsAround(pawn.Position, 14f, true)
            .First(cell => IsEmptyInventoryCell(map, cell) && HasOpenSide(cell));
        TransferToFixture(pawn, map, position);
    }

    private static Thing_GuDormantProjection SpawnGroundGu(Pawn neighbor)
    {
        var registry = IndividualRegistry;
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        // Earlier drops can fill every side of the neighbor on some generated maps. This moves the neighbor only in that case.
        EnsureInventoryFixtureSpace(neighbor);
        var cell = OpenInventoryCell(neighbor);
        Check(registry.TrySpawnDevelopmentGu(species, neighbor.Map, cell, out var wild), "Created the actual development Gu beside the colonist");
        // Native Pawn generation can age the fixture into a condition that correctly blocks retirement.
        foreach (Hediff condition in wild.health.hediffSet.hediffs.ToArray())
            wild.health.RemoveHediff(condition);
        Check(registry.TryMakeDormant(wild, wild.GuRevision, cell, out var thing, out string reason), $"Created the original dormant Thing. {reason}");
        return thing;
    }

    private static void AfterGuInventoryAdd(ThingOwner<Thing> __instance, Thing __0, bool __result)
    {
        if (__result && __0 == inventorySubject && __instance == observedInventory) afterGuInventoryAdd?.Invoke();
    }

    private static void AfterGuInventoryDespawn(ThingWithComps __instance)
    {
        if (__instance == inventorySubject) afterGuInventoryDespawn?.Invoke();
    }

    private static void AfterGuInventorySpawn(ThingWithComps __instance)
    {
        if (__instance == inventorySubject) afterGuInventorySpawn?.Invoke();
    }
}
