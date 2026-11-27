using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using Verse;
using Verse.AI;

/// <summary>
/// A caller-supplied adapter that claims a registered source's identity. With its own binding it is fabricated; with the real
/// binding its own answers must be ignored in favor of the registered owner's.
/// </summary>
internal sealed class GuSlice0Fabricated : IGuPopulationSource
{
    internal GuSlice0Source registered;
    internal object binding;
    internal GuPopulationRecord row;
    public string OwnerKind => registered.OwnerKind;
    public string SourceId => registered.SourceId;
    public object Binding => binding;
    public string CurrentCredential => registered.CurrentCredential;
    public bool HasUniqueIdentity => true;
    public bool RequiresDevelopmentMode(string trigger) => true;
    public GuPopulationRecord CohortFor(string speciesDefName) => row;
    public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, IGuSourceAsset asset) => null;
    public bool PlacementReady(Map map, IntVec3 cell, string trigger) => true;
    public string PlacementRefusal => "Fabricated placement.";
}

/// <summary>
/// Alpha 6 Slice 0 corrections after the independent Codex review (docs/evidence/ALPHA-6-SLICE-0-CODEX-REVIEW.md). Each
/// section asserts the corrected outcome of a scenario that docs/evidence/alpha6-slice0-review-repro demonstrated on a4d40a5.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static string Slice0CorrectionsPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-slice0-corrections.json");
    private static Func<Thing, Exception> slice0SplitFault;
    private static Func<Thing, Exception> slice0DestroyFault;
    private static bool slice0HoldJobEnd;

    private record Slice0Frozen(string SourceId, string Species, int Mature, long Revision, List<string> Records);
    private record Slice0Corrections(List<Slice0Frozen> Frozen, string UnresolvedId, string UnresolvedKey, GuSourceRequest UnresolvedRequest,
        string QuarantinedId, GuSourceRequest QuarantinedRequest, string InterruptedKey, string InterruptedActor, string InterruptedBait,
        string PendingKey, string PendingActor, int PendingBeer, string SpringId, string AnchorId, string FrozenWarningSource);

    private static void Slice0SplitPrefix(Thing __instance)
    {
        if (slice0SplitFault?.Invoke(__instance) is { } fault) throw fault;
    }

    private static void Slice0DestroyPrefix(Thing __instance)
    {
        if (slice0DestroyFault?.Invoke(__instance) is { } fault) throw fault;
    }

    private static bool Slice0EndJobPrefix() => !slice0HoldJobEnd;

    private static readonly List<string> slice0Messages = new();

    private static void Slice0MessagePrefix(Message msg) { if (msg?.text != null) slice0Messages.Add(msg.text); }

    private static string LastMessages() => string.Join(" | ", slice0Messages.Skip(Math.Max(0, slice0Messages.Count - 3)));

    private static List<GuRecord> Slice0RecordsFor(string key) => IndividualRegistry.Records.Where(record => record.CreationEventKey == key).ToList();

    private static int HeldBeer(Pawn pawn) => pawn.inventory.innerContainer.Where(thing => thing.def == ThingDefOf.Beer).Sum(thing => thing.stackCount)
        + (pawn.carryTracker?.CarriedThing is { } carried && carried.def == ThingDefOf.Beer ? carried.stackCount : 0);

    private static void ClearBeer(Pawn pawn)
    {
        foreach (var held in pawn.inventory.innerContainer.Where(thing => thing.def == ThingDefOf.Beer).ToList()) held.Destroy();
    }

    private static Thing GiveBeer(Pawn pawn, int count)
    {
        var beer = ThingMaker.MakeThing(ThingDefOf.Beer);
        beer.stackCount = count;
        Check(pawn.inventory.innerContainer.TryAdd(beer, false), $"Gave {pawn.LabelShort} {count} beer");
        return beer;
    }

    private static Job OrderLure(Pawn pawn, Thing springThing)
    {
        bool dev = Prefs.DevMode;
        Prefs.DevMode = false;
        Order(pawn, springThing, "Lure Liquor Worm with beer");
        Prefs.DevMode = dev;
        Check(pawn.CurJobDef?.defName == "GU_LureWildGu", $"{pawn.LabelShort} started the lure job");
        return pawn.CurJob;
    }

    // Jobs are pooled and reused: a job is still running only while the pawn holds the same object with the same load ID.
    private static Func<bool> Running(Pawn pawn, Job job)
    {
        string id = job.GetUniqueLoadID();
        return () => pawn.CurJob == job && job.GetUniqueLoadID() == id;
    }

    private static void WaitLure(Pawn pawn, Job job)
    {
        var running = Running(pawn, job);
        for (int tick = 0; tick < 4000 && running(); tick++) Find.TickManager.DoSingleTick();
        var driver = pawn.jobs.curDriver;
        Check(!running(), $"{pawn.LabelShort}'s lure job ended. Still on toil {driver?.CurToilIndex} ({driver?.CurToilString}), "
            + $"at {pawn.Position} near {job.targetA.Thing?.Position} on {pawn.Map}, drafted {pawn.Drafted}, downed {pawn.Downed}, mental {pawn.MentalStateDef}, "
            + $"ticks left {driver?.ticksLeftThisToil}, held beer {HeldBeer(pawn)}. Messages: {LastMessages()}");
    }

    private static Toil FinalLureToil(Pawn pawn) =>
        ((List<Toil>)AccessTools.Field(typeof(JobDriver), "toils").GetValue(pawn.jobs.curDriver)).Last();

    // ---- Production lure transaction (review High 3) ------------------------------------------------------------------

    private static (GuSourceRequest Request, GuRecord Gu) ExerciseSlice0Lure(Pawn disciple, CompGuSpiritSpring spring, List<Slice0Opening> openings)
    {
        var registry = IndividualRegistry;
        Map cave = disciple.Map;
        Thing springThing = spring.parent;
        var liquor = DefDatabase<GuSpeciesDef>.GetNamed("GU_LiquorWorm");
        var row = GuIndividualRegistry.ForSpring(spring).CohortFor(liquor.defName);
        // Exactly enough stock for the matrix: two juveniles mature legitimately after the competing lures, for the failed-destruction case and the pending lure saved at the end of the stage.
        Check(row.EvidenceVersion == 0, "The Liquor Worm row has no receipt evidence yet");
        AccessTools.Field(typeof(GuPopulationRecord), "matureCount").SetValue(row, 4);
        AccessTools.Field(typeof(GuPopulationRecord), "juvenileCount").SetValue(row, 2);
        var opening = openings.Single(entry => entry.OwnerKind == "spring" && entry.Species == liquor.defName);
        openings[openings.IndexOf(opening)] = opening with { Total = 6 };
        int Liquors() => registry.Records.Count(record => record.SpeciesDefName == liquor.defName && record.CohortDebitCommitted);
        var competitor = PawnsFinder.AllMaps_FreeColonistsSpawned.First(pawn => pawn != disciple && pawn.Map != cave);
        competitor.DeSpawn();
        GenSpawn.Spawn(competitor, GenRadial.RadialCellsAround(springThing.Position, 4f, false).First(cell => cell.Standable(cave)
            && cell.GetFirstPawn(cave) == null && cell.GetThingList(cave).Count == 0), cave);

        var harmony = new Harmony("projectgu.verification.slice0.lure");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.SplitOff)), prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0SplitPrefix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.Destroy)), prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0DestroyPrefix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(JobDriver), nameof(JobDriver.EndJobWith)), prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0EndJobPrefix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup)), postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseSpawn)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0MessagePrefix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(GuTransactionProbe), nameof(GuTransactionProbe.Reach)), prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0ProbePrefix)));
        GuSourceRequest springLure;
        GuRecord ordinary;
        Slice0Corrections partial;
        try
        {
            // 1. An ordinary lure through the real job: one Gu, one beer, a settled receipt keyed to the actor and the job.
            ClearBeer(disciple);
            var bait = GiveBeer(disciple, 1);
            string baitId = bait.GetUniqueLoadID();
            int gu = Liquors(), mature = row.MatureCount;
            var job = OrderLure(disciple, springThing);
            // Jobs are pooled: a finished Job object is cleared and reused, so every identity is read while the job is live.
            string key = spring.LureEventKey(liquor, disciple, job);
            string expectedKey = $"player-lure:{spring.HabitatId}:{liquor.defName}:{disciple.GetUniqueLoadID()}:{job.GetUniqueLoadID()}";
            WaitLure(disciple, job);
            ordinary = Slice0RecordsFor(key).SingleOrDefault();
            Check(ordinary is { CohortDebitCommitted: true } && Liquors() == gu + 1 && row.MatureCount == mature - 1 && HeldBeer(disciple) == 0
                && bait.Destroyed && ordinary.CreationEvidence is { Trigger: GuSourceTriggers.SpringLure, AssetScope: "Beer", Settlement: GuAssetSettlement.Settled }
                && ordinary.CreationEvidence.AssetId == baitId && ordinary.Rank == ordinary.CreationEvidence.Rank
                && key == expectedKey,
                $"An ordinary lure drew exactly one Liquor Worm, spent exactly its held beer once and keyed the receipt to the actor and job ({key}). "
                + $"Record {ordinary?.Id} debited {ordinary?.CohortDebitCommitted}, Liquor Worms {gu}->{Liquors()}, mature {mature}->{row.MatureCount}, beer {HeldBeer(disciple)}, "
                + $"bait destroyed {bait.Destroyed}, receipt {ordinary?.CreationEvidence?.Describe()}, records {string.Join(",", registry.Records.Where(r => r.SpeciesDefName == liquor.defName).Select(r => r.CreationEventKey))}. Bait {baitId} vs {ordinary?.CreationEvidence?.AssetId}; rank {ordinary?.Rank}/{ordinary?.CreationEvidence?.Rank}. Messages: {LastMessages()}");
            springLure = new GuSourceRequest(key, "spring", spring.HabitatId, spring.parent.GetUniqueLoadID(), liquor.defName, liquor.minRank,
                GuCohortStage.Mature, GuSourceTriggers.SpringLure, "Beer", ordinary.CreationEvidence.RevisionBefore);
            var springSource = GuIndividualRegistry.ForSpring(spring);
            Check(registry.TryMaterializeFromSource(springSource, springLure, cave, IntVec3.Invalid, out var replay, out _) == GuSourceOutcome.Replayed && replay == ordinary
                && registry.TryMaterializeFromSource(springSource, springLure with { AssetScope = "Wine" }, cave, IntVec3.Invalid, out _, out _) == GuSourceOutcome.Conflict
                && registry.TryMaterializeFromSource(springSource, springLure with { Trigger = GuSourceTriggers.SpringSearch, AssetScope = null }, cave, IntVec3.Invalid, out _, out _) == GuSourceOutcome.Conflict,
                "The lure replays, and a different bait or trigger under its key is a conflict");

            // 2. Interrupted before commitment: drafting during the wait keeps the beer and the cohort.
            GiveBeer(disciple, 1);
            gu = Liquors(); mature = row.MatureCount;
            job = OrderLure(disciple, springThing);
            var interruptedRun = Running(disciple, job);
            TickUntil(() => disciple.jobs.curDriver?.CurToilIndex == ((List<Toil>)AccessTools.Field(typeof(JobDriver), "toils").GetValue(disciple.jobs.curDriver)).Count - 2,
                2000, "The lure reached its wait");
            TickUntil(() => false, 60);
            disciple.drafter.Drafted = true;
            TickUntil(() => false, 10);
            disciple.drafter.Drafted = false;
            Check(!interruptedRun() && HeldBeer(disciple) == 1 && Liquors() == gu && row.MatureCount == mature,
                "A lure interrupted before commitment kept its beer and drew nothing");

            // 3. A transaction fault before staging inside the real job rolls back: beer kept, nothing drawn.
            slice0Probe = (point, eventKey) =>
            {
                if (point == GuTransactionPoint.BeforeStaging && eventKey.StartsWith("player-lure:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Injected fault before lure staging");
            };
            job = OrderLure(disciple, springThing);
            key = spring.LureEventKey(liquor, disciple, job);
            WaitLure(disciple, job);
            slice0Probe = null;
            Check(HeldBeer(disciple) == 1 && Liquors() == gu && row.MatureCount == mature && !Slice0RecordsFor(key).Any(),
                "A lure whose transaction faulted before staging rolled back with its beer kept");

            // 4. The final toil executed twice in one job (as after a toil restart) replays: one Gu, one beer. A save attempted
            //    while the bait is being spent is refused by the existing save barrier.
            ClearBeer(disciple);
            GiveBeer(disciple, 2);
            gu = Liquors(); mature = row.MatureCount;
            const string settlementSave = "gu-slice0-settlement-save";
            bool settlementSaveAttempted = false;
            slice0SplitFault = thing =>
            {
                if (thing.def != ThingDefOf.Beer || settlementSaveAttempted) return null;
                settlementSaveAttempted = true;
                GameDataSaveLoader.SaveGame(settlementSave);
                return null;
            };
            job = OrderLure(disciple, springThing);
            key = spring.LureEventKey(liquor, disciple, job);
            var toil = FinalLureToil(disciple);
            slice0HoldJobEnd = true;
            toil.initAction();
            toil.initAction();
            slice0HoldJobEnd = false;
            slice0SplitFault = null;
            disciple.jobs.EndCurrentJob(JobCondition.InterruptForced);
            var repeated = Slice0RecordsFor(key);
            Check(repeated.Count == 1 && Liquors() == gu + 1 && row.MatureCount == mature - 1 && HeldBeer(disciple) == 1
                && repeated[0].CreationEvidence.Settlement == GuAssetSettlement.Settled,
                "Executing one job's final toil twice drew exactly one Liquor Worm and spent exactly one beer");
            Check(settlementSaveAttempted && !File.Exists(GenFilePaths.FilePathForSavedGame(settlementSave)),
                "A save requested while the bait was being spent was refused by the custody save barrier");

            // 5. Bait splitting fails before its stack decrement: the Gu stands, the receipt durably records the interrupted
            //    settlement, and the same intent's replay spends the same bait exactly once.
            ClearBeer(disciple);
            var stack = GiveBeer(disciple, 2);
            gu = Liquors(); mature = row.MatureCount;
            bool splitFailed = false;
            slice0SplitFault = thing => thing == stack && !splitFailed && (splitFailed = true) ? new InvalidOperationException("Injected SplitOff failure before the decrement") : null;
            job = OrderLure(disciple, springThing);
            key = spring.LureEventKey(liquor, disciple, job);
            toil = FinalLureToil(disciple);
            slice0HoldJobEnd = true;
            toil.initAction();
            var interrupted = Slice0RecordsFor(key).SingleOrDefault();
            Check(splitFailed && interrupted is { CohortDebitCommitted: true } && interrupted.CreationEvidence.Settlement == GuAssetSettlement.Interrupted
                && Liquors() == gu + 1 && row.MatureCount == mature - 1 && HeldBeer(disciple) == 2,
                "A failed bait split left one counted Liquor Worm with an explicit interrupted settlement and the beer unspent");
            toil.initAction();
            slice0HoldJobEnd = false;
            slice0SplitFault = null;
            disciple.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Check(interrupted.CreationEvidence.Settlement == GuAssetSettlement.Settled && Slice0RecordsFor(key).Count == 1 && Liquors() == gu + 1
                && HeldBeer(disciple) == 1 && stack.stackCount == 1,
                "Replaying the same lure intent spent the same bait exactly once and drew nothing more");

            // 6. The bait moves to another holder during staging, then the quantity changes during staging: both roll back.
            ClearBeer(disciple);
            ClearBeer(competitor);
            bait = GiveBeer(disciple, 1);
            gu = Liquors(); mature = row.MatureCount;
            bool moved = false;
            afterReleaseSpawn = _ =>
            {
                if (moved || bait.ParentHolder is not Pawn_InventoryTracker) return;
                moved = disciple.inventory.innerContainer.TryTransferToContainer(bait, competitor.inventory.innerContainer, false);
            };
            job = OrderLure(disciple, springThing);
            key = spring.LureEventKey(liquor, disciple, job);
            WaitLure(disciple, job);
            afterReleaseSpawn = null;
            Check(moved && Liquors() == gu && row.MatureCount == mature && HeldBeer(competitor) == 1 && HeldBeer(disciple) == 0
                && !Slice0RecordsFor(key).Any(),
                "Bait moved to another holder during staging failed revalidation: nothing drawn, the moved beer not spent");
            ClearBeer(competitor);
            bait = GiveBeer(disciple, 2);
            bool split = false;
            afterReleaseSpawn = _ =>
            {
                if (split) return;
                split = true;
                competitor.inventory.innerContainer.TryAdd(bait.SplitOff(1), false);
            };
            job = OrderLure(disciple, springThing);
            WaitLure(disciple, job);
            afterReleaseSpawn = null;
            Check(split && Liquors() == gu && row.MatureCount == mature && HeldBeer(disciple) == 1 && HeldBeer(competitor) == 1,
                "Bait whose quantity changed during staging failed revalidation: nothing drawn or spent");

            // 7. Two colonists lure the last mature Liquor Worm at once: exactly one commits and spends one beer.
            Check(row.MatureCount == 1, $"One mature Liquor Worm remains for the competing lures ({row.MatureCount})");
            ClearBeer(disciple);
            GiveBeer(disciple, 1);
            ClearBeer(competitor);
            GiveBeer(competitor, 1);
            gu = Liquors();
            int beerBefore = HeldBeer(disciple) + HeldBeer(competitor);
            var jobA = OrderLure(disciple, springThing);
            var jobB = OrderLure(competitor, springThing);
            string keyA = spring.LureEventKey(liquor, disciple, jobA), keyB = spring.LureEventKey(liquor, competitor, jobB);
            var runA = Running(disciple, jobA);
            var runB = Running(competitor, jobB);
            TickUntil(() => !runA() && !runB(), 4000, "Both competing lures ended");
            int winners = Slice0RecordsFor(keyA).Count + Slice0RecordsFor(keyB).Count;
            Check(winners == 1 && Liquors() == gu + 1 && row.MatureCount == 0 && HeldBeer(disciple) + HeldBeer(competitor) == beerBefore - 1,
                "Two competing lures for the last mature Liquor Worm committed once and spent exactly one beer");

            // A legitimate intervening change: both juveniles mature, for the failed-destruction case and the pending lure.
            row.AdvanceDaily(10, false, 0);
            Check(row.MatureCount == 2 && row.JuvenileCount == 0 && row.EvidenceIntact, "Two juveniles matured with the row's debit evidence intact");

            // 8. Destroying the last unit fails: an interrupted settlement persisted for the fresh-process replay.
            ClearBeer(competitor);
            var lastUnit = GiveBeer(competitor, 1);
            gu = Liquors(); mature = row.MatureCount;
            bool destroyFailed = false;
            slice0DestroyFault = thing => thing == lastUnit && !destroyFailed && (destroyFailed = true) ? new InvalidOperationException("Injected bait destruction failure") : null;
            job = OrderLure(competitor, springThing);
            string interruptedKey = spring.LureEventKey(liquor, competitor, job);
            WaitLure(competitor, job);
            slice0DestroyFault = null;
            var unspent = Slice0RecordsFor(interruptedKey).SingleOrDefault();
            Check(destroyFailed && unspent is { CohortDebitCommitted: true } && unspent.CreationEvidence.Settlement == GuAssetSettlement.Interrupted
                && !lastUnit.Destroyed && HeldBeer(competitor) == 1 && Liquors() == gu + 1 && row.MatureCount == mature - 1,
                "A failed bait destruction kept the Gu with a durable interrupted settlement and the beer unspent");
            partial = new Slice0Corrections(new(), null, null, default, null, default, interruptedKey, competitor.GetUniqueLoadID(),
                lastUnit.GetUniqueLoadID(), null, null, 0, spring.HabitatId, spring.parent.GetUniqueLoadID(), null);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            slice0Probe = null;
            slice0SplitFault = slice0DestroyFault = null;
            slice0HoldJobEnd = false;
            afterReleaseSpawn = null;
        }
        File.WriteAllText(Slice0CorrectionsPath, JsonSerializer.Serialize(partial));
        Report($"Production lure: ordinary, interrupted, faulted, repeated, failed split, moved and resized bait, failed destruction and competing lures each committed at most one Liquor Worm and spent at most one beer.");
        return (springLure, ordinary);
    }

    /// <summary>A lure saved mid-wait: the fresh process completes the same job under the same durable intent.</summary>
    private static void StartSlice0PendingLure(Pawn disciple, CompGuSpiritSpring spring)
    {
        var state = JsonSerializer.Deserialize<Slice0Corrections>(File.ReadAllText(Slice0CorrectionsPath));
        var liquor = DefDatabase<GuSpeciesDef>.GetNamed("GU_LiquorWorm");
        ClearBeer(disciple);
        GiveBeer(disciple, 1);
        var job = OrderLure(disciple, spring.parent);
        TickUntil(() => disciple.jobs.curDriver?.CurToilIndex == ((List<Toil>)AccessTools.Field(typeof(JobDriver), "toils").GetValue(disciple.jobs.curDriver)).Count - 2,
            2000, "The pending lure reached its wait");
        TickUntil(() => false, 60);
        Check(disciple.CurJob == job && !Slice0RecordsFor(spring.LureEventKey(liquor, disciple, job)).Any(), "The pending lure is waiting with nothing drawn yet");
        File.WriteAllText(Slice0CorrectionsPath, JsonSerializer.Serialize(state with
        {
            PendingKey = spring.LureEventKey(liquor, disciple, job), PendingActor = disciple.GetUniqueLoadID(), PendingBeer = HeldBeer(disciple)
        }));
    }

    // ---- Registry transaction corrections (review High 1, 2, 4, 5, Medium 2, Low) -------------------------------------

    private static void ExerciseSlice0Corrections(GuIndividualRegistry registry, Map cave, IntVec3 anchor, List<Slice0Opening> openings)
    {
        var state = JsonSerializer.Deserialize<Slice0Corrections>(File.ReadAllText(Slice0CorrectionsPath));
        GuSlice0Source New(string tag, params (string Species, int Mature, int Juvenile)[] rows)
        {
            var created = new GuSlice0Source { id = $"slice0-{tag}-{Guid.NewGuid():N}", mapId = cave.GetUniqueLoadID(), anchor = anchor };
            foreach (var (species, mature, juvenile) in rows) created.cohorts.Add(new GuPopulationRecord(species, mature, juvenile, -1));
            GuSlice0Sources.Instance.sources.Add(created);
            return created;
        }
        void Open(GuSlice0Source created)
        {
            foreach (var row in created.cohorts) openings.Add(new(GuSlice0Source.Kind, created.id, row.SpeciesDefName, row.TotalCount));
        }
        GuSourceRequest Prepare(IGuPopulationSource from, string key, string species = DevSpecies, string trigger = GuSlice0Source.ProbeTrigger, string scope = null)
        {
            Check(GuIndividualRegistry.TryPrepareSourceRequest(from, species, trigger, scope, key, out var prepared), $"Prepared {key}");
            return prepared;
        }
        IntVec3 Cell(IntVec3 except = default) => GenRadial.RadialCellsAround(anchor, 11.5f, false).First(candidate => candidate != except
            && IsEmptyInventoryCell(cave, candidate) && !cave.reservationManager.IsReserved(candidate) && candidate.DistanceToSquared(anchor) <= 132);
        GuSourceOutcome Run(IGuPopulationSource from, GuSourceRequest request, out GuRecord individual, out string why, IntVec3 at = default, IGuSourceAsset asset = null) =>
            registry.TryMaterializeFromSource(from, request, cave, at.IsValid && at != default ? at : Cell(), out individual, out why, asset);
        int Mature(GuSlice0Source from, string species = DevSpecies) => from.CohortFor(species).MatureCount;
        string reason;

        // ---- High 1: re-entry before staging, during credential resolution and during SpawnSetup -------------------------
        void ReentryCase(string label, Action<Action> arm, Action disarm)
        {
            var a = New($"reentry-{label}-a", (DevSpecies, 2, 0));
            var b = New($"reentry-{label}-b", (DevSpecies, 2, 0));
            Open(a);
            Open(b);
            string key = $"slice0-reentry-{label}";
            var outer = Prepare(a, key);
            var nestedRequest = Prepare(b, key);
            IntVec3 outerCell = Cell();
            GuSourceOutcome nested = (GuSourceOutcome)(-1);
            arm(() => nested = registry.TryMaterializeFromSource(b, nestedRequest, cave, Cell(outerCell), out _, out _));
            var outcome = registry.TryMaterializeFromSource(a, outer, cave, outerCell, out var record, out reason);
            disarm();
            var claims = Slice0RecordsFor(key);
            Check(nested == GuSourceOutcome.Refused && outcome == GuSourceOutcome.Committed && claims.Count == 1 && claims[0] == record
                && Mature(a) == 1 && Mature(b) == 2 && registry.Resolve(record.Id) == record,
                $"Re-entry {label} with the same event key against a second stocked source was refused: one event, one debit, one individual ({nested}, {outcome}). {reason}");
            Check(registry.TryMaterializeFromSource(b, nestedRequest, cave, Cell(), out _, out _) == GuSourceOutcome.Conflict && Mature(b) == 2,
                $"Afterwards the same key on the second source is a payload conflict, never a second creation ({label})");
        }
        bool fired = false;
        ReentryCase("credential", nest => { fired = false; GuSlice0Source.onCredentialRead = _ => { if (fired) return; fired = true; nest(); }; },
            () => GuSlice0Source.onCredentialRead = null);
        Check(fired, "The credential-read re-entry fired inside the transaction");
        ReentryCase("preflight", nest => slice0Probe = (point, _) => { if (point == GuTransactionPoint.Preflight) { slice0Probe = null; nest(); } },
            () => slice0Probe = null);
        ReentryCase("before-staging", nest => slice0Probe = (point, _) => { if (point == GuTransactionPoint.BeforeStaging) { slice0Probe = null; nest(); } },
            () => slice0Probe = null);
        ReentryCase("spawnsetup", nest => afterReleaseSpawn = _ => { afterReleaseSpawn = null; nest(); }, () => afterReleaseSpawn = null);
        Report("Re-entry before staging, during credential resolution and during SpawnSetup, each with the same key against a second stocked source: one event, one debit, one individual.");

        // ---- High 2: independent source authority -----------------------------------------------------------------------
        var registered = New("authority", (DevSpecies, 2, 0), (HopeSpecies, 2, 0));
        Open(registered);
        var detached = new GuSlice0Source { id = registered.id, mapId = registered.mapId, anchor = anchor };
        detached.cohorts.Add(new GuPopulationRecord(DevSpecies, 3, 0, -1));
        Check(Run(detached, Prepare(detached, "slice0-detached"), out _, out reason) == GuSourceOutcome.Refused && reason.Contains("registered owner")
            && detached.CohortFor(DevSpecies).MatureCount == 3 && Mature(registered) == 2 && !Slice0RecordsFor("slice0-detached").Any(),
            $"A detached instance with the registered identity and its own unsaved stocked row is refused. {reason}");
        var fabricated = new GuSlice0Fabricated { registered = registered, binding = new object(), row = registered.CohortFor(HopeSpecies) };
        Check(Run(fabricated, Prepare(registered, "slice0-fabricated"), out _, out reason) == GuSourceOutcome.Refused && Mature(registered, HopeSpecies) == 2,
            $"A fabricated adapter claiming the registered identity is refused. {reason}");
        var impostor = new GuSlice0Fabricated { registered = registered, binding = registered, row = registered.CohortFor(HopeSpecies) };
        Check(Run(impostor, Prepare(registered, "slice0-impostor"), out var trusted, out reason) == GuSourceOutcome.Committed
            && trusted.SpeciesDefName == DevSpecies && Mature(registered) == 1 && Mature(registered, HopeSpecies) == 2,
            $"An adapter presenting the real binding is answered by the registered owner: its own wrong-species row and permissive policy are ignored. {reason}");
        registered.mislabelAs = HopeSpecies;
        Check(Run(registered, Prepare(registered, "slice0-mislabeled") with { SpeciesDefName = DevSpecies }, out _, out reason) == GuSourceOutcome.Refused
            && Mature(registered, HopeSpecies) == 2, $"A registered source returning another species' row is refused. {reason}");
        registered.mislabelAs = null;
        afterReleaseSpawn = _ => registered.revoked = true;
        Check(Run(registered, Prepare(registered, "slice0-revoked"), out _, out reason) == GuSourceOutcome.RolledBack && Mature(registered) == 1,
            $"Issuance revoked during SpawnSetup fails post-callback authorization with no debit. {reason}");
        registered.revoked = false;
        afterReleaseSpawn = _ => GuSlice0Sources.Instance.sources.Remove(registered);
        Check(Run(registered, Prepare(registered, "slice0-unregistered"), out _, out reason) == GuSourceOutcome.RolledBack && Mature(registered) == 1,
            $"A source removed from its owning component during SpawnSetup can no longer authorize the debit. {reason}");
        GuSlice0Sources.Instance.sources.Add(registered);
        var token = new GuSlice0Token();
        afterReleaseSpawn = _ => token.refusal = "The test asset was withdrawn.";
        Check(Run(registered, Prepare(registered, "slice0-asset-withdrawn", trigger: GuSlice0Source.BaitTrigger, scope: GuSlice0Source.BaitScope),
                out _, out reason, asset: token) == GuSourceOutcome.RolledBack && token.settled == 0 && Mature(registered) == 1,
            $"An asset made ineligible during SpawnSetup fails post-callback revalidation and is not spent. {reason}");
        afterReleaseSpawn = null;
        Report("Authority: detached, fabricated, mislabeled, revoked, unregistered and asset-withdrawn requests issued nothing; an adapter presenting the real binding was answered by the owner.");

        // ---- Medium 2: rank contract --------------------------------------------------------------------------------------
        var ranked = New("rank", ("GU_TestRankRangeGu", 2, 0));
        ranked.anyRank = true;
        Open(ranked);
        var rankRequest = Prepare(ranked, "slice0-rank", "GU_TestRankRangeGu");
        Check(Run(ranked, rankRequest with { Rank = 2 }, out _, out reason) == GuSourceOutcome.Refused && Mature(ranked, "GU_TestRankRangeGu") == 2,
            $"Slice 0 issues only the species' wild rank, even when a source would authorize rank two. {reason}");
        Check(Run(ranked, rankRequest, out var rankRecord, out reason) == GuSourceOutcome.Committed && rankRecord.Rank == 1 && rankRecord.CreationEvidence.Rank == 1,
            $"The authorized rank, the canonical record's rank and the receipt rank agree. {reason}");

        // ---- High 4: owner-side debit evidence ----------------------------------------------------------------------------
        var frozen = new List<Slice0Frozen>();
        var revision = AccessTools.Field(typeof(GuPopulationRecord), "revision");
        foreach (var (label, offset) in new[] { ("equal", 0L), ("higher", 5L) })
        {
            var scope = New($"evidence-{label}", (DevSpecies, 3, 0));
            Check(Run(scope, Prepare(scope, $"slice0-evidence-{label}-1"), out var first, out reason) == GuSourceOutcome.Committed, $"Evidence {label} setup. {reason}");
            var substitute = new GuPopulationRecord(DevSpecies, 3, 0, -1);
            revision.SetValue(substitute, first.CreationEvidence.RevisionAfter + offset);
            scope.cohorts[0] = substitute;
            Check(Run(scope, Prepare(scope, $"slice0-evidence-{label}-2"), out _, out reason) == GuSourceOutcome.Refused && reason.Contains("frozen")
                && substitute.MatureCount == 3 && !Slice0RecordsFor($"slice0-evidence-{label}-2").Any() && registry.FindRecord(first.Id) == first,
                $"A substituted pre-debit row at the {label} revision is frozen: nothing issued, credited or deleted. {reason}");
            frozen.Add(new(scope.id, DevSpecies, 3, substitute.Revision, new() { first.Id }));
        }
        var edited = New("evidence-edit", (DevSpecies, 3, 0));
        Open(edited);
        Check(Run(edited, Prepare(edited, "slice0-evidence-edit-1"), out _, out reason) == GuSourceOutcome.Committed, $"Edit setup. {reason}");
        var matureField = AccessTools.Field(typeof(GuPopulationRecord), "matureCount");
        matureField.SetValue(edited.CohortFor(DevSpecies), 4);
        Check(Run(edited, Prepare(edited, "slice0-evidence-edit-2"), out _, out reason) == GuSourceOutcome.Refused && reason.Contains("outside its owner"),
            $"An inflated count edited outside the owner breaks the row's seal and freezes the scope. {reason}");
        matureField.SetValue(edited.CohortFor(DevSpecies), 2);
        Check(Run(edited, Prepare(edited, "slice0-evidence-edit-3"), out _, out reason) == GuSourceOutcome.Committed,
            $"Once the edit is repaired the scope issues again. {reason}");
        var duplicated = New("evidence-duplicate", (DevSpecies, 3, 0));
        var duplicateRequest = Prepare(duplicated, "slice0-evidence-dup-1");
        Check(Run(duplicated, duplicateRequest, out var claimed, out reason) == GuSourceOutcome.Committed, $"Duplicate setup. {reason}");
        var forged = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), DefDatabase<GuSpeciesDef>.GetNamed(DevSpecies),
            duplicateRequest with { EventKey = "slice0-evidence-dup-2" }, Find.TickManager.TicksGame, "Pawn_slice0_forged");
        forged.CommitSourceDebit(claimed.CreationEvidence.RevisionAfter);
        ((List<GuRecord>)AccessTools.Field(typeof(GuIndividualRegistry), "records").GetValue(registry)).Add(forged);
        ((Dictionary<string, GuRecord>)AccessTools.Field(typeof(GuIndividualRegistry), "byId").GetValue(registry))[forged.Id] = forged;
        ((Dictionary<string, GuRecord>)AccessTools.Field(typeof(GuIndividualRegistry), "byCreationEvent").GetValue(registry))[forged.CreationEventKey] = forged;
        Check(Run(duplicated, Prepare(duplicated, "slice0-evidence-dup-3"), out _, out reason) == GuSourceOutcome.Refused && reason.Contains("same debit")
            && Mature(duplicated) == 2 && !claimed.IsQuarantined && registry.FindRecord(forged.Id) == forged,
            $"Two receipts claiming one debit revision freeze the scope without deleting or adjusting either claim. {reason}");
        frozen.Add(new(duplicated.id, DevSpecies, 2, duplicated.CohortFor(DevSpecies).Revision, new() { claimed.Id, forged.Id }));
        var living = New("evidence-intervening", (DevSpecies, 2, 2));
        Check(Run(living, Prepare(living, "slice0-evidence-live-1"), out _, out reason) == GuSourceOutcome.Committed, $"Intervening setup. {reason}");
        living.CohortFor(DevSpecies).AdvanceDaily(6, true, 1);
        // Opening 4 units; one materialized; maturation conserves; one declared birth: 4 + 1 = cohort units + counted individuals.
        openings.Add(new(GuSlice0Source.Kind, living.id, DevSpecies, living.CohortFor(DevSpecies).TotalCount + 1));
        Check(living.CohortFor(DevSpecies).TotalCount == 4, "Maturation and one birth changed the intervening scope legitimately");
        Check(Run(living, Prepare(living, "slice0-evidence-live-2"), out _, out reason) == GuSourceOutcome.Committed,
            $"Legitimate intervening source changes (maturation and a birth) keep the evidence valid. {reason}");
        Report($"Evidence: equal- and higher-revision substituted rows, an edited count and a duplicated debit claim froze their scopes; a repaired edit and legitimate intervening changes kept issuing.");

        // ---- High 5: recovery under combined native failures ----------------------------------------------------------------
        var combined = New("recovery-debited", (DevSpecies, 3, 0));
        Open(combined);
        var combinedRequest = Prepare(combined, "slice0-recovery-debited");
        slice0Probe = (point, _) =>
        {
            if (point is GuTransactionPoint.AfterDebit or GuTransactionPoint.RestorePreimage or GuTransactionPoint.Cleanup)
                throw new InvalidOperationException($"Injected fault at {point}");
        };
        duringReleaseIdentityCheck = _ =>
        {
            if (Environment.StackTrace.Contains("MarkRogue")) throw new InvalidOperationException("Injected identity lookup failure during rogue logging");
        };
        GuSourceOutcome combinedOutcome;
        Exception escaped = null;
        GuRecord counted = null;
        try { combinedOutcome = Run(combined, combinedRequest, out counted, out reason); }
        catch (Exception error) { escaped = error; combinedOutcome = GuSourceOutcome.Refused; reason = error.Message; }
        slice0Probe = null;
        duringReleaseIdentityCheck = null;
        Check(escaped == null && combinedOutcome == GuSourceOutcome.Unresolved && counted is { CohortDebitCommitted: true, IsActionable: false }
            && counted.Unresolved == GuUnresolvedCause.ProjectionMissing && registry.ProjectionOf(counted) == null && registry.Resolve(counted.Id) == null
            && Slice0RecordsFor(combinedRequest.EventKey).Single() == counted && Mature(combined) == 2,
            $"After-debit fault, failed rollback, failed cleanup and a throwing identity lookup kept one counted, unresolved, unbound claim and no exception escaped. {reason}");
        Check(Run(combined, combinedRequest, out var countedReplay, out _) == GuSourceOutcome.Replayed && countedReplay == counted && Mature(combined) == 2,
            "The unresolved claim replays without a second debit");

        var early = New("recovery-early", (DevSpecies, 3, 0));
        Open(early);
        var earlyRequest = Prepare(early, "slice0-recovery-early");
        bool assignmentFailed = false;
        duringReleaseIdentityCheck = _ =>
        {
            string stack = Environment.StackTrace;
            if (stack.Contains("MarkRogue")) throw new InvalidOperationException("Injected identity lookup failure during rogue logging");
            if (!assignmentFailed && stack.Contains("StageAndCommitSource") && !stack.Contains("PrepareWildPawn"))
            {
                assignmentFailed = true;
                throw new InvalidOperationException("Injected identity lookup failure before the canonical record is assigned");
            }
        };
        slice0Probe = (point, _) => { if (point == GuTransactionPoint.Cleanup) throw new InvalidOperationException("Injected native cleanup failure"); };
        escaped = null;
        GuSourceOutcome earlyOutcome;
        try { earlyOutcome = Run(early, earlyRequest, out _, out reason); }
        catch (Exception error) { escaped = error; earlyOutcome = GuSourceOutcome.Refused; reason = error.Message; }
        slice0Probe = null;
        duringReleaseIdentityCheck = null;
        var blocked = Slice0RecordsFor(earlyRequest.EventKey).SingleOrDefault();
        Check(assignmentFailed && escaped == null && earlyOutcome == GuSourceOutcome.Quarantined && blocked is { IsQuarantined: true, CohortDebitCommitted: false }
            && Mature(early) == 3 && Run(early, earlyRequest, out _, out _) == GuSourceOutcome.Refused && Mature(early) == 3,
            $"A failure before the record was assigned, with failed cleanup and failed diagnostics, left a durable quarantined claim that blocks the key. {reason}");

        // ---- Low: rolled-back creations leave no recovery hints ------------------------------------------------------------
        var hints = (Dictionary<string, Thing>)AccessTools.Field(typeof(GuIndividualRegistry), "lastProjection").GetValue(registry);
        int hintsBefore = hints.Count;
        var hinted = New("hints", (DevSpecies, 6, 0));
        Open(hinted);
        slice0Probe = (point, _) => { if (point == GuTransactionPoint.BeforePublication) throw new InvalidOperationException("Injected fault before publication"); };
        for (int attempt = 0; attempt < 5; attempt++)
            Check(Run(hinted, Prepare(hinted, $"slice0-hint-{attempt}"), out _, out reason) == GuSourceOutcome.RolledBack, $"Hint attempt {attempt}. {reason}");
        slice0Probe = null;
        Check(hints.Count == hintsBefore && !hints.Values.Any(thing => thing is { Discarded: true }) && Mature(hinted) == 6,
            $"Five rolled-back creations left no recovery hints and no discarded Pawn references ({hintsBefore} -> {hints.Count})");
        Report("Recovery: combined after-debit, rollback, cleanup and diagnostic failures kept one counted unresolved claim; a failure before record assignment quarantined the key durably; rolled-back creations retained no Pawns.");

        File.WriteAllText(Slice0CorrectionsPath, JsonSerializer.Serialize(state with
        {
            Frozen = frozen, UnresolvedId = counted.Id, UnresolvedKey = combinedRequest.EventKey, UnresolvedRequest = combinedRequest,
            QuarantinedId = blocked.Id, QuarantinedRequest = earlyRequest, FrozenWarningSource = duplicated.id
        }));
    }

    private static void CheckFrozenScopes(Slice0Corrections state, Map cave, string stage)
    {
        var registry = IndividualRegistry;
        foreach (var scope in state.Frozen)
        {
            var source = GuSlice0Sources.Instance.sources.Single(entry => entry.id == scope.SourceId);
            var row = source.CohortFor(scope.Species);
            Check(row.MatureCount == scope.Mature && row.Revision == scope.Revision && scope.Records.All(id => registry.FindRecord(id) is { CohortDebitCommitted: true }),
                $"{stage}: frozen scope {scope.SourceId} kept its counts and every claim ({row.MatureCount} mature at revision {row.Revision})");
            string reason = null;
            Check(GuIndividualRegistry.TryPrepareSourceRequest(source, scope.Species, GuSlice0Source.ProbeTrigger, null, $"slice0-frozen-{stage}-{scope.SourceId}", out var request)
                && registry.TryMaterializeFromSource(source, request, cave, GenRadial.RadialCellsAround(source.anchor, 11.5f, false).First(cell =>
                    IsEmptyInventoryCell(cave, cell) && !cave.reservationManager.IsReserved(cell) && cell.DistanceToSquared(source.anchor) <= 132),
                    out _, out reason) == GuSourceOutcome.Refused && reason.Contains("frozen"),
                $"{stage}: frozen scope {scope.SourceId} still issues nothing. {reason}");
        }
    }

    private static void CheckRecoveryClaims(Slice0Corrections state, Map cave, string stage)
    {
        var registry = IndividualRegistry;
        var counted = registry.FindRecord(state.UnresolvedId);
        var source = GuSlice0Sources.Instance.sources.Single(entry => entry.id == state.UnresolvedRequest.SourceId);
        Check(counted is { CohortDebitCommitted: true, IsActionable: false } && counted.Unresolved == GuUnresolvedCause.ProjectionMissing
            && registry.TryMaterializeFromSource(source, state.UnresolvedRequest, cave, IntVec3.Invalid, out var replay, out _) == GuSourceOutcome.Replayed && replay == counted,
            $"{stage}: the combined-failure claim is still one counted unresolved individual and replays");
        var blocked = registry.FindRecord(state.QuarantinedId);
        var early = GuSlice0Sources.Instance.sources.Single(entry => entry.id == state.QuarantinedRequest.SourceId);
        Check(blocked is { IsQuarantined: true, CohortDebitCommitted: false } && early.CohortFor(DevSpecies).MatureCount == 3
            && registry.TryMaterializeFromSource(early, state.QuarantinedRequest, cave, IntVec3.Invalid, out _, out _) == GuSourceOutcome.Refused,
            $"{stage}: the quarantined key stays durably blocked with no debit");
    }

    private static void VerifySlice0CorrectionsReload(Map cave, List<Slice0Opening> openings)
    {
        var registry = IndividualRegistry;
        var state = JsonSerializer.Deserialize<Slice0Corrections>(File.ReadAllText(Slice0CorrectionsPath));
        Check(Log.Messages.Any(message => message.type == LogMessageType.Warning && message.text.Contains(state.FrozenWarningSource) && message.text.Contains("frozen")),
            "Load reconciliation reported the duplicated-claim scope as frozen after both owners loaded");
        CheckFrozenScopes(state, cave, "reload");
        CheckRecoveryClaims(state, cave, "reload");

        // The lure saved mid-wait completes in this process under the same durable intent.
        var actor = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == state.PendingActor);
        Check(actor.CurJobDef?.defName == "GU_LureWildGu" && HeldBeer(actor) == state.PendingBeer, "The saved lure job resumed in the fresh process");
        TickUntil(() => actor.CurJobDef?.defName != "GU_LureWildGu", 3000, "The resumed lure completed");
        var pending = Slice0RecordsFor(state.PendingKey).SingleOrDefault();
        Check(pending is { CohortDebitCommitted: true } && pending.CreationEvidence.Settlement == GuAssetSettlement.Settled && HeldBeer(actor) == state.PendingBeer - 1,
            $"The resumed lure committed one Liquor Worm under the saved intent key and spent one beer ({state.PendingKey})");

        // The interrupted settlement persisted; replaying the same intent with the same bait spends it once.
        var holder = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == state.InterruptedActor);
        var unspent = Slice0RecordsFor(state.InterruptedKey).Single();
        var bait = holder.inventory.innerContainer.Single(thing => thing.GetUniqueLoadID() == state.InterruptedBait);
        var spring = cave.listerThings.AllThings.Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp?.HabitatId == state.SpringId);
        Check(unspent.CreationEvidence.Settlement == GuAssetSettlement.Interrupted && unspent.CreationEvidence.AssetId == state.InterruptedBait,
            "The interrupted bait settlement survived the fresh process as an explicit durable outcome");
        int liquors = registry.Records.Count(record => record.SpeciesDefName == "GU_LiquorWorm" && record.CohortDebitCommitted);
        string reason = null;
        Check(GuIndividualRegistry.TryPrepareMaterializationRequest(spring, "GU_LiquorWorm", state.InterruptedKey, out var request)
            && registry.TryEncounterWildCohort(spring, request, GuSourceTriggers.SpringLure, new GuHeldLureAsset(spring, holder, null, bait), cave,
                IntVec3.Invalid, out var replayed, out reason) && replayed == unspent,
            $"The same lure intent replays after the reload. {reason}");
        Check(unspent.CreationEvidence.Settlement == GuAssetSettlement.Settled && bait.Destroyed && HeldBeer(holder) == 0
            && registry.Records.Count(record => record.SpeciesDefName == "GU_LiquorWorm" && record.CohortDebitCommitted) == liquors,
            "The replay spent the exact unspent bait once and drew nothing more");
        CheckSlice0Conservation(openings, "corrections reload");
        Report("Fresh process: frozen scopes stayed frozen, recovery claims stayed counted or blocked, the saved lure completed under its intent, and the interrupted bait was settled exactly once.");
    }

    private static void VerifySlice0CorrectionsFinal(Map cave, Slice0State slice0)
    {
        var registry = IndividualRegistry;
        var state = JsonSerializer.Deserialize<Slice0Corrections>(File.ReadAllText(Slice0CorrectionsPath));
        CheckFrozenScopes(state, cave, "final");
        CheckRecoveryClaims(state, cave, "final");
        Check(Slice0RecordsFor(state.PendingKey).Single().CreationEvidence.Settlement == GuAssetSettlement.Settled
            && Slice0RecordsFor(state.InterruptedKey).Single().CreationEvidence.Settlement == GuAssetSettlement.Settled,
            "Both lure settlements persisted as settled through the second fresh process");

        // ---- Medium 1: historical replay verifies the supplied anchor (destructive, so last) --------------------------------
        var original = cave.listerThings.AllThings.Single(thing => thing.TryGetComp<CompGuSpiritSpring>() is { } comp && comp.HabitatId == state.SpringId);
        var historical = registry.FindRecord(slice0.SpringSearch.GuId);
        AccessTools.Field(typeof(GuRecord), "creationEvidence").SetValue(historical, null);
        var legacy = new GuCohortMaterializationRequest(historical.CreationEventKey, state.SpringId, state.AnchorId, historical.SpeciesDefName, 0);
        Check(registry.TryMaterializeCohort(original.TryGetComp<CompGuSpiritSpring>(), legacy, cave, IntVec3.Invalid, out var first, out string reason) && first == historical,
            $"A historical record replays from its exact original anchor. {reason}");
        IntVec3 position = original.Position;
        original.DeSpawn();
        var replacement = (ThingWithComps)GenSpawn.Spawn(ThingMaker.MakeThing(original.def), position, cave);
        var comp = replacement.TryGetComp<CompGuSpiritSpring>();
        AccessTools.Field(typeof(CompGuSpiritSpring), "habitatId").SetValue(comp, state.SpringId);
        Check(!registry.TryMaterializeCohort(comp, legacy, cave, IntVec3.Invalid, out _, out reason) && reason.Contains("exact live spring anchor"),
            $"A replacement anchor carrying the same habitat ID cannot replay the historical event with the old credential. {reason}");
        Check(!registry.TryMaterializeCohort(comp, legacy with { SourceNativeId = replacement.GetUniqueLoadID() }, cave, IntVec3.Invalid, out _, out _),
            "Nor with the replacement's own credential");
        var springLure = slice0.SpringLure;
        Check(registry.TryMaterializeFromSource(GuIndividualRegistry.ForSpring(comp), springLure.Request, cave, IntVec3.Invalid, out var modern, out reason)
            == GuSourceOutcome.Replayed && modern.Id == springLure.GuId,
            $"A modern receipt still replays independently of the live credential. {reason}");
        Report("Historical replay requires the exact live anchor again; modern receipt replay stays independent of the live credential.");
    }
}
