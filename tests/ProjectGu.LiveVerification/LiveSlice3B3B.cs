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

    private record ApertureClaim(GuRecordSnapshot Record, string QuarantineReason);
    private record ApertureFixture(string CaveId, GuRecordSnapshot Stored, GuRecordSnapshot Packed, ApertureClaim[] Lost);

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
        bool rejectedOutsideDevelopment = !registry.TryStoreDormant(thing, carried.Revision, host, aperture, out _);
        Prefs.DevMode = true;
        Check(rejectedOutsideDevelopment && !registry.TryStoreDormant(thing, carried.Revision - 1, host, aperture, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, host, null, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, host, otherAperture, out _)
            && !registry.TryStoreDormant(thing, carried.Revision, replacementHost, otherAperture, out _)
            && Snapshot(registry.Resolve(thing)) == carried, "Storage rejects stale revisions, missing apertures, wrong hosts and disabled Development Mode");

        var fault = new Harmony("projectgu.verification.slice3b3b.aperture");
        GuRecordSnapshot stored;
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
                guardedDetach = item.holdingOwner == null && Snapshot(record) == carried && registry.CustodyTransitionInProgress
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
                    Custody = new(GuCustodyKind.Aperture, aperture.GetUniqueLoadID(), host.GetUniqueLoadID()) },
                "Deposit commits canonical custody before native destruction and leaves zero projections");
            beforeApertureThingDestroy = null;

            Check(!registry.TryWithdrawDormant(record.Id, carried.Revision, host, aperture, out _, out _)
                && !registry.TryWithdrawDormant(record.Id, stored.Revision, replacementHost, otherAperture, out _, out _)
                && !registry.TryWithdrawDormant(record.Id, stored.Revision, replacementHost, aperture, out _, out _)
                && Snapshot(registry.Resolve(record.Id)) == stored,
                "Withdrawal rejects a stale revision, a different aperture and a different host");
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
                && interruptedRecord.IsQuarantined && Snapshot(interruptedRecord) == interruptedSnapshot,
                "Aperture loss during withdrawal quarantines the exact claim and removes the provisional Thing");
            interrupted = new(interruptedSnapshot, interruptedRecord.QuarantineReason);
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
            && Snapshot(registry.Resolve(packed)) == packedSnapshot && Snapshot(registry.Resolve(record.Id)) == stored,
            "A second Gu cannot take the occupied slot");

        var removed = AddProbeAperture(replacementHost);
        var replaced = StoreFixtureGu(replacementHost, removed);
        var replacedSnapshot = Snapshot(replaced);
        replacementHost.health.RemoveHediff(removed);
        var replacement = AddProbeAperture(replacementHost);
        Check(replacement.GetUniqueLoadID() != removed.GetUniqueLoadID() && registry.ResolveApertureOccupant(replacement) == null
            && replaced.QuarantineReason?.Contains("removed") == true && Snapshot(replaced) == replacedSnapshot
            && !registry.TryWithdrawDormant(replaced.Id, replaced.Revision, replacementHost, replacement, out _, out _),
            "Native removal quarantines the original exact claim and a same-Def replacement inherits nothing");

        var missing = StoreFixtureGu(replacementHost, replacement);
        // Simulate loss of a saved reference without invoking the normal native notification.
        Check(replacementHost.health.hediffSet.hediffs.Remove(replacement) && !missing.IsQuarantined,
            "Prepared an unresolved record for fresh-process binding validation");
        var dyingAperture = AddProbeAperture(dyingHost);
        var death = StoreFixtureGu(dyingHost, dyingAperture);
        var deathSnapshot = Snapshot(death);
        dyingHost.Kill(null);
        Check(dyingHost.Dead && death.QuarantineReason?.Contains("died") == true && Snapshot(death) == deathSnapshot
            && registry.Resolve(death.Id) == null, "Native host death immediately quarantines the record and preserves its revision");

        var destroyedHost = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            tile: home.Tile, forceGenerateNewPawn: true));
        GenSpawn.Spawn(destroyedHost, EmptyCargoCell(home, entrance.Position, null, 2), home);
        EnsureInventoryFixtureSpace(destroyedHost);
        var destroyedAperture = AddProbeAperture(destroyedHost);
        var destroyed = StoreFixtureGu(destroyedHost, destroyedAperture);
        var destroyedSnapshot = Snapshot(destroyed);
        destroyedHost.Destroy(DestroyMode.Vanish);
        Check(destroyedHost.Destroyed && registry.Resolve(destroyed.Id) == null && destroyed.IsQuarantined
            && Snapshot(destroyed) == destroyedSnapshot, "Destroying the host also invalidates exact aperture custody");

        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out string travelReason) && host.Map == cave
            && packed.holdingOwner == inventory && packed.MapHeld == cave && Snapshot(registry.Resolve(packed)) == packedSnapshot
            && Snapshot(registry.ResolveApertureOccupant(aperture)) == stored,
            $"The original host and exact aperture moved underground with one stored and one packed Gu. {travelReason}");
        Check(ActuallyProjectedGu().Single() == packed, "All aperture Gu are records only");
        var fixture = new ApertureFixture(cave.GetUniqueLoadID(), stored, packedSnapshot, new[] {
            interrupted, new(replacedSnapshot, replaced.QuarantineReason), new(Snapshot(missing), null),
            new(deathSnapshot, death.QuarantineReason), new(destroyedSnapshot, destroyed.QuarantineReason)
        });
        File.WriteAllText(ApertureFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame(SaveName);
        var saved = XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName));
        Check(saved.Descendants("guIndividualRecordsV1").Single().Elements("li").Count() == fixture.Lost.Length + 2
            && saved.Descendants("guIndividualId").Single().Value == packed.GuId, "The native save contains one record per Gu and only the packed projection");
        Report("Aperture storage, exact host and Hediff, capacity, stale requests, native callbacks, cleanup, death and replacement passed. The same host traveled with a stored Gu and a packed Gu.");
    }

    private static void VerifySlice3B3BReload()
    {
        var fixture = JsonSerializer.Deserialize<ApertureFixture>(File.ReadAllText(ApertureFixturePath));
        var registry = IndividualRegistry;
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        AssertNativeAddress(cave);
        Pawn host = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.Stored.Custody.HolderPawnId);
        var aperture = host.health.hediffSet.hediffs.OfType<Hediff_Aperture>()
            .Single(hediff => hediff.GetUniqueLoadID() == fixture.Stored.Custody.NativeOwnerId);
        var stored = registry.ResolveApertureOccupant(aperture);
        var packed = ActuallyProjectedGu().Single();
        var inventory = host.inventory.GetDirectlyHeldThings();
        Check(stored != null && Snapshot(stored) == fixture.Stored && registry.Resolve(stored.Id) == stored
            && packed.holdingOwner == inventory && Snapshot(registry.Resolve(packed)) == fixture.Packed
            && registry.Individuals.Count == fixture.Lost.Length + 2, "Fresh reload rebuilt one exact aperture binding and one inventory binding");
        foreach (var expected in fixture.Lost)
        {
            var quarantined = registry.Individuals.Single(entry => entry.Id == expected.Record.Id);
            Check(quarantined.IsQuarantined && Snapshot(quarantined) == expected.Record && registry.Resolve(quarantined.Id) == null
                && (expected.QuarantineReason == null ? quarantined.QuarantineReason.Contains("could not be resolved")
                    : expected.QuarantineReason == quarantined.QuarantineReason), "Reload retains quarantined claims and rejects the unresolved native pair");
        }
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out string reason) && host.Map.IsPlayerHome
            && registry.ResolveApertureOccupant(aperture) == stored && packed.holdingOwner == inventory && packed.MapHeld == host.Map
            && Snapshot(stored) == fixture.Stored && Snapshot(registry.Resolve(packed)) == fixture.Packed,
            $"Both custody forms returned through the original passage without changing revision. {reason}");
        Check(registry.TryWithdrawDormant(stored.Id, stored.Revision, host, aperture, out var withdrawn, out reason), $"Withdrawal after reload succeeded. {reason}");
        Check(Snapshot(registry.Resolve(withdrawn)) == fixture.Stored with {
                Revision = fixture.Stored.Revision + 1,
                Custody = new(GuCustodyKind.DormantInventory, withdrawn.GetUniqueLoadID(), host.GetUniqueLoadID()) }
            && withdrawn.GuId == fixture.Stored.Id && withdrawn.GuRevision == stored.Revision && withdrawn.stackCount == 1
            && withdrawn.holdingOwner == inventory && registry.ResolveApertureOccupant(aperture) == null
            && !registry.TryWithdrawDormant(stored.Id, fixture.Stored.Revision, host, aperture, out _, out _)
            && ActuallyProjectedGu().Count() == 2, "Withdrawal creates exactly one native Thing with the same identity and rejects replay");
        Check(registry.TryStoreDormant(withdrawn, withdrawn.GuRevision, host, aperture, out reason)
            && stored.Revision == fixture.Stored.Revision + 2 && withdrawn.Destroyed && ActuallyProjectedGu().Single() == packed,
            $"The same individual can occupy the slot again after a committed withdrawal. {reason}");
        Report("Fresh-process reload preserved the exact two live bindings and five lost claims. The host returned by passage, withdrew the same individual into inventory, and stored it again.");
    }

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
