using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B2 => GenCommandLine.CommandLineArgPassed("guslice3b2");
    private static string DormancyFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-dormancy-fixture.json");
    private static Pawn_GuWildProjection dormancySource;
    private static Action afterDormancyDespawn;
    private static Action<Thing_GuDormantProjection> afterDormantSpawn;
    private static Action beforeDormancyDiscard;
    private static Action afterDormancyCleanup;
    private static int healthCleanupCalls;

    private record DormancyRecord(GuRecordSnapshot Record, string FormerPawnId, string QuarantineReason);
    private record DormancyFixture(string MapId, string TravelerId, DormancyRecord Healthy, DormancyRecord Quarantined);

    private static void ExerciseSlice3B2()
    {
        Check(GuDormancySaveGuard.Installed, "The native save entry is guarded");
        Map home = Find.CurrentMap;
        Pawn traveler = home.mapPawns.FreeColonistsSpawned.First();
        var site = OpenIndependentSite(traveler, home);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        Map cave = exit.Map;
        Check(JobDriver_GuPocketProbeTravel.TryTravel(traveler, entrance, out string reason) && traveler.Map == cave,
            $"The same traveler enters the existing spring passage. {reason}");
        var registry = IndividualRegistry;
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        Check(Thing_GuDormantProjection.IsConfigured(species.dormantProjectionDef), "The native dormant Def is inert and unstackable");

        Pawn_GuWildProjection Spawn()
        {
            IntVec3 cell = EmptyCargoCell(cave, exit.Position, null, 2);
            Check(registry.TrySpawnDevelopmentGu(species, cave, cell, out var pawn), "The production command created the actual Gu Pawn");
            return pawn;
        }

        var source = Spawn();
        var record = registry.Resolve(source);
        var initial = Snapshot(record);
        IntVec3 origin = source.Position;
        string originalPawnId = source.GetUniqueLoadID();
        IntVec3 target = EmptyCargoCell(cave, exit.Position, null, 3);
        var blocker = GenSpawn.Spawn(MakeStack(1), target, cave);
        Check(!registry.TryMakeDormant(source, initial.Revision, target, out _, out _)
            && source.Spawned && source.Position == origin && blocker.Spawned && Snapshot(registry.Resolve(source)) == initial,
            "Blocked placement leaves both the original Pawn binding and the destination occupant intact");
        blocker.Destroy();
        var inventory = source.inventory.GetDirectlyHeldThings();
        Thing held = MakeStack(1);
        Check(inventory.TryAdd(held, false) && !registry.TryMakeDormant(source, initial.Revision, target, out _, out _)
            && inventory.Contains(held) && Snapshot(registry.Resolve(source)) == initial, "The retirement contract protects native inventory");
        held.Destroy();
        Check(!registry.TryMakeDormant(source, initial.Revision + 1, target, out _, out _)
            && Snapshot(registry.Resolve(source)) == initial, "The checked operation rejects a stale expected revision");

        var observer = new Harmony("projectgu.verification.slice3b2.dormancy");
        DormancyRecord healthy;
        DormancyRecord quarantined;
        try
        {
            InstallRetirementObservers(observer);
            observer.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.DeSpawn)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterDormancyDespawn)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterDormantSpawn)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(WorldPawns), nameof(WorldPawns.PassToWorld)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(BeforeDormancyDiscard)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), "Notify_PawnCorpseDestroyed"),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveDormancyHealthCleanup)));
            observer.Patch(AccessTools.DeclaredMethod(typeof(Corpse), "PostCorpseDestroy"),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterDormancyCleanup)));
            dormancySource = source;

            const string reentrySave = "gu-dormancy-reentry";
            Thing lateBlocker = null;
            afterDormancyDespawn = () =>
            {
                Check(!source.Spawned && registry.DormancyInProgress && registry.Resolve(record.Id) == null
                    && !registry.TryMakeDormant(source, initial.Revision, target, out _, out _), "Reentry cannot authorize either projection during staging");
                GameDataSaveLoader.SaveGame(reentrySave);
                Check(!File.Exists(GenFilePaths.FilePathForSavedGame(reentrySave)), "The native save entry did not serialize staging");
                lateBlocker = GenSpawn.Spawn(MakeStack(1), target, cave);
            };
            Check(!registry.TryMakeDormant(source, initial.Revision, target, out _, out _) && lateBlocker?.Spawned == true,
                "A native callback blocked placement after reversible despawn");
            afterDormancyDespawn = null;
            lateBlocker.Destroy();
            CheckSameWildProjection(source, origin, originalPawnId, initial);

            Thing_GuDormantProjection abandoned = null;
            afterDormantSpawn = thing =>
            {
                abandoned = thing;
                Check(thing.Spawned && registry.Resolve(thing) == null && record.Custody.Kind == GuCustodyKind.WildPawn,
                    "The map-owned staged Thing is inert and unauthorized before commit");
                throw new InvalidOperationException("Injected post-placement dormancy failure");
            };
            Check(!registry.TryMakeDormant(source, initial.Revision, target, out _, out _) && abandoned?.Destroyed == true,
                "An exception after native placement removes the provisional Thing");
            afterDormantSpawn = null;
            CheckSameWildProjection(source, origin, originalPawnId, initial);

            Job job = JobMaker.MakeJob(JobDefOf.Goto, target);
            source.jobs.StartJob(job, JobCondition.InterruptForced);
            Check(source.CurJob == job && source.Reserve(target, job), "The actual Gu Pawn began with a job and reservation");
            observedRetirement = source;
            killCalls = makeCorpseCalls = discardCleanupCalls = corpseDestructionCalls = healthCleanupCalls = 0;
            int discardAttempts = 0;
            beforeDormancyDiscard = () =>
            {
                discardAttempts++;
                Check(record.Custody.Kind == GuCustodyKind.DormantGround && record.BiologicalState == GuBiologicalState.Dormant
                    && !source.Spawned && source.CurJob == null && !cave.reservationManager.IsReserved(target),
                    "Canonical custody committed and reservations cleared before native discard");
                Check(!registry.TryMakeDormant(source, initial.Revision, target, out _, out _), "Retirement callback cannot reenter the transition");
            };
            Check(registry.TryMakeDormant(source, initial.Revision, target, out var dormant, out reason),
                $"The development Gu successfully became dormant. {reason}");
            Check(discardAttempts == 1 && source.Discarded && !source.Dead && killCalls == 0 && makeCorpseCalls == 0
                && corpseDestructionCalls == 0 && discardCleanupCalls == 1 && healthCleanupCalls == 1
                && !Find.TaleManager.AnyTaleConcerns(source), "The actual Gu retirement invoked native discard and health cleanup once, without death or a corpse");
            CheckOldPawnAbsent(originalPawnId);
            Check(registry.Resolve(dormant) == record && registry.Resolve(source) == null && dormant.Position == target
                && Snapshot(record) == initial with { Biology = GuBiologicalState.Dormant, Custody = new(GuCustodyKind.DormantGround, dormant.GetUniqueLoadID()) },
                "Dormancy preserves the same record, ID, provenance, species, rank and revision");
            Check(!registry.TryMakeDormant(source, initial.Revision, target, out _, out _)
                && discardAttempts == 1 && GroundDormantGu().Single() == dormant, "A repeated action creates no second projection or discard");
            healthy = new(Snapshot(record), originalPawnId, null);

            var failingPawn = Spawn();
            var failingRecord = registry.Resolve(failingPawn);
            string failingPawnId = failingPawn.GetUniqueLoadID();
            dormancySource = failingPawn;
            beforeDormancyDiscard = null;
            bool cleanupEntered = false;
            afterDormancyCleanup = () =>
            {
                cleanupEntered = true;
                throw new InvalidOperationException("Injected native discard cleanup failure");
            };
            Check(!registry.TryMakeDormant(failingPawn, failingRecord.Revision, failingPawn.Position, out _, out _)
                && cleanupEntered && failingPawn.Discarded && failingRecord.IsQuarantined
                && failingRecord.Custody.Kind == GuCustodyKind.DormantGround
                && registry.Resolve(failingRecord.Id) == null && failingPawn.holdingOwner == null,
                "A native cleanup callback failure keeps the committed dormant Thing and quarantines its authorization");
            CheckOldPawnAbsent(failingPawnId);
            var quarantinedThing = GroundDormantGu().Single(thing => thing.GuId == failingRecord.Id);
            Check(registry.Resolve(quarantinedThing) == null && !dormant.CanStackWith(quarantinedThing)
                && quarantinedThing.stackCount == 1 && dormant.stackCount == 1, "Both Things are separate units and only the healthy binding is authorized");
            quarantined = new(Snapshot(failingRecord), failingPawnId, failingRecord.QuarantineReason);
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            dormancySource = null;
            observedRetirement = null;
            afterDormancyDespawn = null;
            afterDormantSpawn = null;
            beforeDormancyDiscard = null;
            afterDormancyCleanup = null;
        }

        var fixture = new DormancyFixture(cave.GetUniqueLoadID(), traveler.GetUniqueLoadID(), healthy, quarantined);
        File.WriteAllText(DormancyFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Actual Gu Pawn retirement passed with one native discard and health cleanup, zero kills and corpses. Blocked placement, rollback after placement, save reentry, repeated actions and discard quarantine passed.");
    }

    private static void CheckSameWildProjection(Pawn_GuWildProjection source, IntVec3 origin, string pawnId, GuRecordSnapshot initial)
    {
        var restored = IndividualRegistry.Resolve(source);
        Check(restored != null && Snapshot(restored) == initial && source.Spawned && source.Position == origin
            && source.GetUniqueLoadID() == pawnId && !source.Destroyed && !GroundDormantGu().Any(),
            "Rollback respawned the exact Pawn in its original cell and removed the provisional Thing");
    }

    private static IEnumerable<Thing_GuDormantProjection> GroundDormantGu() =>
        Find.Maps.SelectMany(map => map.listerThings.AllThings.OfType<Thing_GuDormantProjection>());

    private static void CheckOldPawnAbsent(string pawnId)
    {
        Check(!PawnsFinder.All_AliveOrDead.Any(pawn => pawn.GetUniqueLoadID() == pawnId)
            && !Find.Maps.SelectMany(map => map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>())
                .Any(corpse => corpse.InnerPawn.GetUniqueLoadID() == pawnId), "No retired Gu Pawn or its corpse remains in the native world");
    }

    private static void AfterDormancyDespawn(Pawn __instance)
    {
        if (__instance == dormancySource) afterDormancyDespawn?.Invoke();
    }

    private static void AfterDormantSpawn(ThingWithComps __instance)
    {
        if (__instance is Thing_GuDormantProjection thing && thing.GuId == dormancySource?.GuId) afterDormantSpawn?.Invoke(thing);
    }

    private static void BeforeDormancyDiscard(Pawn __0, PawnDiscardDecideMode __1)
    {
        if (__0 == dormancySource && __1 == PawnDiscardDecideMode.Discard) beforeDormancyDiscard?.Invoke();
    }

    private static void ObserveDormancyHealthCleanup(Pawn_HealthTracker __instance)
    {
        if (__instance == observedRetirement?.health) healthCleanupCalls++;
    }

    private static void AfterDormancyCleanup(Pawn __0, bool __1)
    {
        if (__0 == dormancySource && __1) afterDormancyCleanup?.Invoke();
    }

    private static void VerifySlice3B2Reload()
    {
        var fixture = JsonSerializer.Deserialize<DormancyFixture>(File.ReadAllText(DormancyFixturePath));
        var registry = IndividualRegistry;
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.MapId);
        AssertNativeAddress(cave);
        var things = GroundDormantGu().ToArray();
        Check(things.Length == 2 && registry.Individuals.Count == 2 && things.All(thing => thing.Map == cave),
            "Reload retained exactly the two dormant Things and their records on the original underground map");
        foreach (var expected in new[] { fixture.Healthy, fixture.Quarantined })
        {
            var record = registry.Individuals.Single(entry => entry.Id == expected.Record.Id);
            var thing = things.Single(entry => entry.GetUniqueLoadID() == expected.Record.Custody.NativeOwnerId);
            Check(Snapshot(record) == expected.Record && record.QuarantineReason == expected.QuarantineReason
                && thing.GuId == record.Id && thing.GuRevision == record.Revision && thing.stackCount == 1
                && thing.holdingOwner == cave.GetDirectlyHeldThings(), "Reload preserved the exact record and native dormant Thing custody");
            Check(registry.Resolve(thing) == (record.IsQuarantined ? null : record)
                && registry.Resolve(record.Id) == (record.IsQuarantined ? null : record), "Only the healthy dormant Thing rebinds after reload");
            CheckOldPawnAbsent(expected.FormerPawnId);
        }

        var traveler = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.TravelerId);
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(traveler, exit, out string reason) && traveler.Map.IsPlayerHome
            && things.All(thing => thing.Map == cave), $"Normal return travel preserves both underground dormant Things. {reason}");
        Report("Fresh-process reload kept the original healthy and quarantined dormant bindings, with no old Pawn or corpse. The original colonist returned through the saved passage.");
    }
}
