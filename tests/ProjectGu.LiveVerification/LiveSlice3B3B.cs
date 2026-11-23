using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B3B => GenCommandLine.CommandLineArgPassed("guslice3b3b");
    private static string ApertureFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-aperture-fixture.json");
    private static ThingOwner watchedApertureInventory;
    private static Action<Thing_GuDormantProjection> afterApertureInventoryRemove;
    private static Action<Thing_GuDormantProjection> afterApertureInventoryAdd;
    private static Action<Thing_GuDormantProjection> beforeApertureThingDestroy;

    /// <summary>A lost aperture claim and the unresolved cause V2 must report. V1 quarantined all of these.</summary>
    private record ApertureClaim(GuRecordSnapshot Record, GuUnresolvedCause Cause);
    private record ApertureFixture(string CaveId, GuRecordSnapshot[] Stored, GuRecordSnapshot Packed, ApertureClaim[] Lost);

    private static void ExerciseSlice3B3B()
    {
        Map home = Find.CurrentMap;
        var colonists = home.mapPawns.FreeColonistsSpawned.Take(3).ToArray();
        Pawn host = colonists[0], replacementHost = colonists[1], dyingHost = colonists[2];
        var site = OpenIndependentSite(host, home);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        Map cave = site.Levels.Single().LiveMap;
        foreach (Pawn colonist in colonists) EnsureInventoryFixtureSpace(colonist);

        var registry = IndividualRegistry;
        var aperture = AddProbeAperture(host);
        var otherAperture = AddProbeAperture(replacementHost);
        var inventory = host.inventory.GetDirectlyHeldThings();
        var thing = PackTestGu(host);
        var record = registry.Resolve(thing);
        var carried = Snapshot(record);
        Prefs.DevMode = false;
        bool storedOutsideDevelopment = registry.TryStoreDormant(thing, carried.Revision, host, aperture, out string ordinaryStoreReason);
        var ordinaryStored = Snapshot(record);
        Thing_GuDormantProjection ordinaryWithdrawal = null;
        bool withdrawnOutsideDevelopment = storedOutsideDevelopment
            && registry.TryWithdrawDormant(record.Id, ordinaryStored.Revision, host, aperture, out ordinaryWithdrawal, out _);
        if (withdrawnOutsideDevelopment) thing = ordinaryWithdrawal;
        carried = Snapshot(record);
        Prefs.DevMode = true;
        Check(storedOutsideDevelopment && withdrawnOutsideDevelopment && thing.GuId == record.Id
            && record.Custody.Kind == GuCustodyKind.DormantInventory && carried.Revision == ordinaryStored.Revision + 1,
            $"Ordinary player custody stores and withdraws the same Gu with Development Mode disabled. {ordinaryStoreReason}");
        Check(!registry.TryStoreDormant(thing, carried.Revision - 1, host, aperture, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, host, null, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, host, otherAperture, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, replacementHost, otherAperture, out _)
            && Snapshot(registry.Resolve(thing)) == carried, "Storage rejects stale revisions, missing apertures and wrong hosts");

        var fault = new Harmony("projectgu.verification.slice3b3b.aperture");
        GuRecordSnapshot stored, storedSecond;
        ApertureClaim interrupted;
        try
        {
            fault.Patch(AccessTools.DeclaredMethod(typeof(ThingOwner<Thing>), nameof(ThingOwner.Remove)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterApertureInventoryRemove)));
            fault.Patch(AccessTools.DeclaredMethod(typeof(ThingOwner<Thing>), nameof(ThingOwner.TryAdd),
                new[] { typeof(Thing), typeof(bool) }), postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterApertureInventoryAdd)));
            fault.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.Destroy)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(BeforeApertureThingDestroy)));
            watchedApertureInventory = inventory;
            bool guardedDetach = false;
            const string saveReentry = "gu-aperture-reentry";
            afterApertureInventoryRemove = item =>
            {
                if (item != thing) return;
                // Mid-transaction the Thing is detached, so its observed form is no holder. Check the stored claim instead.
                guardedDetach = item.holdingOwner == null && record.Revision == carried.Revision && record.StateRevision == carried.StateRevision
                    && record.Placement == GuPlacementKind.Manifested && registry.CustodyTransitionInProgress
                    && registry.Resolve(record.Id) == null && registry.ResolveApertureOccupant(aperture) == null
                    && !registry.TryStoreDormant(thing, carried.Revision, host, aperture, out _);
                GameDataSaveLoader.SaveGame(saveReentry);
                throw new InvalidOperationException("Injected fault after native aperture deposit detached its source");
            };
            Check(!registry.TryStoreDormant(thing, carried.Revision, host, aperture, out _) && guardedDetach
                && !File.Exists(GenFilePaths.FilePathForSavedGame(saveReentry)) && thing.holdingOwner == inventory
                && inventory.Count(item => item == thing) == 1 && Snapshot(registry.Resolve(thing)) == carried,
                "Interrupted deposit restores the original Thing and revision and blocks action and save reentry");
            afterApertureInventoryRemove = null;

            bool commitBeforeDestroy = false;
            beforeApertureThingDestroy = item =>
            {
                if (item == thing)
                    commitBeforeDestroy = item.holdingOwner == null && record.Custody.Kind == GuCustodyKind.Aperture
                        && record.Revision == carried.Revision + 1 && registry.CustodyTransitionInProgress;
            };
            Check(registry.TryStoreDormant(thing, carried.Revision, host, aperture, out string reason), $"Native aperture deposit succeeded. {reason}");
            stored = Snapshot(record);
            Check(commitBeforeDestroy && thing.Destroyed && !inventory.Any(item => item == thing)
                && registry.Resolve(thing) == null && registry.Resolve(record.Id) == record && registry.ResolveApertureOccupant(aperture) == record
                && stored == carried with { Revision = carried.Revision + 1,
                    Custody = new(GuCustodyKind.Aperture, aperture.ApertureId, host.GetUniqueLoadID()) },
                "Deposit commits canonical custody before native destruction and leaves zero projections");
            beforeApertureThingDestroy = null;

            Check(!registry.TryWithdrawDormant(record.Id, carried.Revision, host, aperture, out _, out _)
                && !registry.TryWithdrawDormant(record.Id, stored.Revision, replacementHost, otherAperture, out _, out _)
                && !registry.TryWithdrawDormant(record.Id, stored.Revision, replacementHost, aperture, out _, out _)
                && Snapshot(registry.Resolve(record.Id)) == stored,
                "Withdrawal rejects a stale revision, a different aperture and a different host");

            var secondThing = PackTestGu(host);
            var secondRecord = registry.Resolve(secondThing);
            var secondCarried = Snapshot(secondRecord);
            Check(registry.TryStoreDormant(secondThing, secondCarried.Revision, host, aperture, out string secondReason),
                $"A second distinct Gu entered the same mortal aperture. {secondReason}");
            storedSecond = Snapshot(secondRecord);
            Check(secondThing.Destroyed && secondRecord.Id != record.Id && storedSecond.Id != stored.Id
                && registry.ApertureClaimCount(aperture) == 2 && registry.ApertureOccupants(aperture).Count() == 2
                && registry.ResolveApertureOccupant(aperture) == null,
                "Two distinct authoritative identities share the aperture without an arbitrary single-occupant result");
            var maxStacks = AccessTools.Field(typeof(ThingOwner), "maxStacks");
            int normalLimit = (int)maxStacks.GetValue(inventory);
            maxStacks.SetValue(inventory, inventory.Count);
            Check(!registry.TryWithdrawDormant(record.Id, stored.Revision, host, aperture, out _, out _)
                && Snapshot(registry.Resolve(record.Id)) == stored, "A full native inventory leaves the aperture record unchanged");
            maxStacks.SetValue(inventory, normalLimit);
            var ballast = ThingMaker.MakeThing(ThingDefOf.Steel);
            ballast.stackCount = ballast.def.stackLimit;
            Check(inventory.TryAdd(ballast, false) && !registry.TryWithdrawDormant(record.Id, stored.Revision, host, aperture, out _, out _)
                && Snapshot(registry.Resolve(record.Id)) == stored, "Withdrawal respects native mass capacity");
            ballast.Destroy();

            Thing_GuDormantProjection failed = null;
            bool guardedAdd = false;
            afterApertureInventoryAdd = item =>
            {
                failed = item;
                guardedAdd = item.holdingOwner == inventory && Snapshot(record) == stored && registry.Resolve(item) == null
                    && !registry.TryWithdrawDormant(record.Id, stored.Revision, host, aperture, out _, out _);
                throw new InvalidOperationException("Injected fault after native aperture withdrawal bound a provisional Thing");
            };
            Check(!registry.TryWithdrawDormant(record.Id, stored.Revision, host, aperture, out _, out _) && guardedAdd
                && failed is { Destroyed: true, holdingOwner: null } && !inventory.Any(item => item == failed)
                && Snapshot(registry.Resolve(record.Id)) == stored, "Interrupted withdrawal destroys the provisional Thing and preserves exact aperture custody");
            afterApertureInventoryAdd = null;

            var interruptedRecord = StoreFixtureGu(replacementHost, otherAperture);
            var interruptedSnapshot = Snapshot(interruptedRecord);
            watchedApertureInventory = replacementHost.inventory.GetDirectlyHeldThings();
            failed = null;
            afterApertureInventoryAdd = item =>
            {
                failed = item;
                replacementHost.health.RemoveHediff(otherAperture);
            };
            Check(!registry.TryWithdrawDormant(interruptedRecord.Id, interruptedRecord.Revision, replacementHost, otherAperture, out _, out _)
                && failed is { Destroyed: true, holdingOwner: null } && !watchedApertureInventory.Any(item => item == failed)
                && !interruptedRecord.IsQuarantined && interruptedRecord.Unresolved == GuUnresolvedCause.ApertureMissing
                && SameClaim(interruptedRecord, interruptedSnapshot),
                "Aperture loss during withdrawal leaves the unchanged claim unresolved and removes the provisional Thing");
            interrupted = new(Snapshot(interruptedRecord), GuUnresolvedCause.ApertureMissing);
        }
        finally
        {
            fault.UnpatchAll(fault.Id);
            watchedApertureInventory = null;
            afterApertureInventoryRemove = afterApertureInventoryAdd = beforeApertureThingDestroy = null;
        }

        var packed = PackTestGu(host);
        var packedSnapshot = Snapshot(registry.Resolve(packed));
        Check(!registry.TryStoreDormant(packed, packed.GuRevision, host, aperture, out _)
            && Snapshot(registry.Resolve(packed)) == packedSnapshot && Snapshot(registry.Resolve(record.Id)) == stored
            && Snapshot(registry.Resolve(storedSecond.Id)) == storedSecond && registry.ApertureClaimCount(aperture) == 2,
            "A third Gu cannot exceed mortal aperture capacity two");

        var removed = AddProbeAperture(replacementHost);
        var replaced = StoreFixtureGu(replacementHost, removed);
        var replacedSnapshot = Snapshot(replaced);
        replacementHost.health.RemoveHediff(removed);
        var replacement = AddProbeAperture(replacementHost);
        // Native callbacks only enqueue. Apply them at an explicit reconciliation point instead of waiting for a tick.
        registry.ApplyPendingEvents();
        Check(replacement.GetUniqueLoadID() != removed.GetUniqueLoadID() && replacement.ApertureId == null
            && registry.ResolveApertureOccupant(replacement) == null
            && replaced.Unresolved == GuUnresolvedCause.ApertureMissing && !replaced.IsQuarantined && SameClaim(replaced, replacedSnapshot)
            && !registry.TryWithdrawDormant(replaced.Id, replaced.Revision, replacementHost, replacement, out _, out _),
            "Native removal leaves the original claim unresolved and a same-Def replacement inherits nothing");

        var missing = StoreFixtureGu(replacementHost, replacement);
        // Simulate loss of a saved reference without invoking the normal native notification.
        Check(replacementHost.health.hediffSet.hediffs.Remove(replacement) && !missing.IsQuarantined,
            "Prepared an unresolved record for fresh-process binding validation");
        var dyingAperture = AddProbeAperture(dyingHost);
        var death = StoreFixtureGu(dyingHost, dyingAperture);
        var deathSnapshot = Snapshot(death);
        dyingHost.Kill(null);
        registry.ApplyPendingEvents();
        // Phase 2 (owner decisions §2): host death keeps the Gu contained and recoverable from the body. Phase 1 left it unresolved.
        Check(dyingHost.Dead && registry.Resolve(death.Id) == death && !death.IsUnresolved && !death.IsQuarantined
            && SameClaim(death, deathSnapshot) && death.Custody.Kind == GuCustodyKind.DeceasedAperture
            && death.History.Any(entry => entry.Kind == GuLifecycleEventKind.HostDied)
            && !registry.TryWithdrawDormant(death.Id, death.Revision, dyingHost, dyingAperture, out _, out _),
            "Native host death keeps deceased-host custody with its revision and containment. A dead host cannot withdraw the Gu");

        var destroyedHost = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            tile: home.Tile, forceGenerateNewPawn: true));
        GenSpawn.Spawn(destroyedHost, EmptyCargoCell(home, entrance.Position, null, 2), home);
        EnsureInventoryFixtureSpace(destroyedHost);
        var destroyedAperture = AddProbeAperture(destroyedHost);
        var destroyed = StoreFixtureGu(destroyedHost, destroyedAperture);
        var destroyedSnapshot = Snapshot(destroyed);
        destroyedHost.Destroy(DestroyMode.Vanish);
        Check(destroyedHost.Destroyed && registry.Resolve(destroyed.Id) == null, "A destroyed host cannot authorize its aperture occupant");
        // No native callback reports this. The scheduled sweep detects it without any UI read.
        registry.Sweep(int.MaxValue);
        Check(destroyed.Unresolved == GuUnresolvedCause.HostUnavailable && !destroyed.IsQuarantined && SameClaim(destroyed, destroyedSnapshot),
            "The reconciliation sweep marks the destroyed host unavailable without changing the claim");

        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out string travelReason) && host.Map == cave
            && packed.holdingOwner == inventory && packed.MapHeld == cave && Snapshot(registry.Resolve(packed)) == packedSnapshot
            && registry.ApertureOccupants(aperture).Select(Snapshot).OrderBy(item => item.Id, StringComparer.Ordinal)
                .SequenceEqual(new[] { stored, storedSecond }.OrderBy(item => item.Id, StringComparer.Ordinal)),
            $"The original host and exact aperture moved underground with two stored and one packed Gu. {travelReason}");
        Check(ActuallyProjectedGu().Single() == packed, "All aperture Gu are records only");
        var fixture = new ApertureFixture(cave.GetUniqueLoadID(), new[] { stored, storedSecond }.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), packedSnapshot, new[] {
            interrupted, new(Snapshot(replaced), GuUnresolvedCause.ApertureMissing), new(Snapshot(missing), GuUnresolvedCause.ApertureMissing),
            new(Snapshot(death), GuUnresolvedCause.None), new(Snapshot(destroyed), GuUnresolvedCause.HostUnavailable)
        });
        File.WriteAllText(ApertureFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame(SaveName);
        var saved = XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName));
        Check(saved.Descendants("guIndividualRecordsV2").Single().Elements("li").Count() == fixture.Lost.Length + 3
            && saved.Descendants("guIndividualId").Single().Value == packed.GuId, "The native save contains one record per Gu and only the packed projection");
        Prefs.DevMode = false;
        Check(host.GetGizmos().OfType<Command_Action>().Any(command => command.defaultLabel == "Manage aperture Gu"),
            "An awakened player-controlled colonist exposes aperture inspection and custody controls in ordinary play");
        Prefs.DevMode = true;
        Report("Aperture storage by durable aperture ID, two-Gu capacity, stale requests, native callbacks, cleanup, death, destruction and replacement passed without quarantine. The same host traveled with two stored Gu and one packed Gu.");
    }

    private static void VerifySlice3B3BReload()
    {
        var fixture = JsonSerializer.Deserialize<ApertureFixture>(File.ReadAllText(ApertureFixturePath));
        var registry = IndividualRegistry;
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        AssertNativeAddress(cave);
        Pawn host = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.Stored[0].Custody.HolderPawnId);
        var aperture = host.health.hediffSet.hediffs.OfType<Hediff_Aperture>()
            .Single(hediff => hediff.ApertureId == fixture.Stored[0].Custody.NativeOwnerId);
        var storedRecords = registry.ApertureOccupants(aperture).OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
        var stored = storedRecords[0];
        var packed = ActuallyProjectedGu().Single();
        var inventory = host.inventory.GetDirectlyHeldThings();
        Check(storedRecords.Length == 2 && storedRecords.Select(Snapshot).SequenceEqual(fixture.Stored)
            && storedRecords.All(record => registry.Resolve(record.Id) == record) && registry.ResolveApertureOccupant(aperture) == null
            && packed.holdingOwner == inventory && Snapshot(registry.Resolve(packed)) == fixture.Packed
            && registry.Individuals.Count == fixture.Lost.Length + 3, "Fresh reload rebuilt two exact aperture bindings and one inventory binding");
        foreach (var expected in fixture.Lost)
        {
            var lost = registry.Individuals.Single(entry => entry.Id == expected.Record.Id);
            if (expected.Cause == GuUnresolvedCause.None)
            {
                Check(lost.IsActionable && Snapshot(lost) == expected.Record && registry.Resolve(lost.Id) == lost
                    && lost.Custody.Kind == GuCustodyKind.DeceasedAperture, "Reload rebound the Gu held in the deceased host's aperture");
                continue;
            }
            Check(!lost.IsQuarantined && lost.Unresolved == expected.Cause && lost.TerminalEvidence == GuTerminalEvidence.None
                && Snapshot(lost) == expected.Record && registry.Resolve(lost.Id) == null,
                $"Reload keeps the lost claim unresolved ({expected.Cause}) with its data and rejects the missing native pair");
        }
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out string reason) && host.Map.IsPlayerHome
            && registry.ApertureOccupants(aperture).Select(Snapshot).OrderBy(item => item.Id, StringComparer.Ordinal).SequenceEqual(fixture.Stored)
            && packed.holdingOwner == inventory && packed.MapHeld == host.Map
            && Snapshot(registry.Resolve(packed)) == fixture.Packed,
            $"Both custody forms returned through the original passage without changing revision. {reason}");
        Check(registry.TryWithdrawDormant(stored.Id, stored.Revision, host, aperture, out var withdrawn, out reason), $"Withdrawal after reload succeeded. {reason}");
        Check(Snapshot(registry.Resolve(withdrawn)) == fixture.Stored[0] with {
                Revision = fixture.Stored[0].Revision + 1,
                Custody = new(GuCustodyKind.DormantInventory, withdrawn.GetUniqueLoadID(), host.GetUniqueLoadID()) }
            && withdrawn.GuId == fixture.Stored[0].Id && withdrawn.GuRevision == stored.Revision && withdrawn.stackCount == 1
            && withdrawn.holdingOwner == inventory && registry.ApertureOccupants(aperture).Count() == 1
            && registry.ResolveApertureOccupant(aperture) != null
            && !registry.TryWithdrawDormant(stored.Id, fixture.Stored[0].Revision, host, aperture, out _, out _)
            && ActuallyProjectedGu().Count() == 2, "Withdrawal creates exactly one native Thing with the same identity while preserving the other aperture Gu and rejecting replay");
        Check(registry.TryStoreDormant(withdrawn, withdrawn.GuRevision, host, aperture, out reason)
            && stored.Revision == fixture.Stored[0].Revision + 2 && withdrawn.Destroyed && ActuallyProjectedGu().Single() == packed
            && registry.ApertureOccupants(aperture).Select(record => record.Id).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(fixture.Stored.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal)),
            $"The same individual can return to the aperture after withdrawal while preserving both identities. {reason}");
        Report("Fresh-process reload preserved two exact aperture bindings and one packed binding, kept five lost claims unresolved with their causes, and preserved both identities through withdrawal and re-storage.");
    }

    /// <summary>The stored claim is unchanged: custody and state revisions, container ID and anchor host.</summary>
    private static bool SameClaim(GuIndividual record, GuRecordSnapshot snapshot) => record.Revision == snapshot.Revision
        && record.StateRevision == snapshot.StateRevision && record.ContainerId == snapshot.Custody.NativeOwnerId
        && record.AnchorHint == snapshot.Custody.HolderPawnId && record.Placement == GuPlacementKind.Contained;

    private static Thing_GuDormantProjection PackTestGu(Pawn host)
    {
        var gu = SpawnGroundGu(host);
        Check(IndividualRegistry.TryTakeDormant(gu, gu.GuRevision, host, out string reason), $"Packed the original test Gu. {reason}");
        return gu;
    }

    private static GuIndividual StoreFixtureGu(Pawn host, Hediff_Aperture aperture)
    {
        var gu = PackTestGu(host);
        var record = IndividualRegistry.Resolve(gu);
        Check(IndividualRegistry.TryStoreDormant(gu, gu.GuRevision, host, aperture, out string reason) && gu.Destroyed,
            $"Created record-only aperture custody. {reason}");
        return record;
    }

    private static IEnumerable<Thing> ActuallyProjectedGu() => GroundDormantGu().Cast<Thing>()
        .Concat(PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>())
        .Concat(PawnsFinder.All_AliveOrDead.Where(pawn => pawn.inventory != null)
            .SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings().OfType<Thing_GuDormantProjection>()));

    private static void AfterApertureInventoryRemove(ThingOwner<Thing> __instance, Thing __0, bool __result)
    {
        if (__result && __instance == watchedApertureInventory && __0 is Thing_GuDormantProjection thing)
            afterApertureInventoryRemove?.Invoke(thing);
    }

    private static void AfterApertureInventoryAdd(ThingOwner<Thing> __instance, Thing __0, bool __result)
    {
        if (__result && __instance == watchedApertureInventory && __0 is Thing_GuDormantProjection thing)
            afterApertureInventoryAdd?.Invoke(thing);
    }

    private static void BeforeApertureThingDestroy(ThingWithComps __instance)
    {
        if (__instance is Thing_GuDormantProjection thing) beforeApertureThingDestroy?.Invoke(thing);
    }
}
