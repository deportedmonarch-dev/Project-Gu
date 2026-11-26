using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using Verse;

/// <summary>Test-only population sources for Alpha 6 Slice 0. Saved with the game so fresh processes see the same cohorts.</summary>
public sealed class GuSlice0Sources : GameComponent
{
    internal List<GuSlice0Source> sources = new();

    public GuSlice0Sources(Game game) { }

    internal static GuSlice0Sources Instance => Current.Game?.GetComponent<GuSlice0Sources>();

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref sources, "guSlice0TestSources", LookMode.Deep);
        sources ??= new();
    }
}

/// <summary>
/// A test-only independently authorized source. Its stable identity is saved; its native-instance credential is not:
/// every process (and every simulated instance replacement) gets a new one, like a live map generation token.
/// </summary>
public sealed class GuSlice0Source : IExposable, IGuPopulationSource
{
    internal const string Kind = "slice0-test";
    internal const string ProbeTrigger = "slice0-probe";
    internal const string BaitTrigger = "slice0-bait";
    internal const string BaitScope = "Beer";

    internal string id = "";
    internal string mapId = "";
    internal IntVec3 anchor = IntVec3.Invalid;
    internal List<GuPopulationRecord> cohorts = new();
    private string credential = NewCredential();

    private static string NewCredential() => "slice0-instance:" + Guid.NewGuid().ToString("N");
    internal void ReplaceNativeInstance() => credential = NewCredential();
    internal Map BoundMap => Find.Maps.FirstOrDefault(map => map.GetUniqueLoadID() == mapId);

    public string OwnerKind => Kind;
    public string SourceId => id;
    public string CurrentCredential => BoundMap != null ? credential : null;
    public bool HasUniqueIdentity => GuSlice0Sources.Instance?.sources.Count(source => source.id == id) == 1;
    public bool RequiresDevelopmentMode(string trigger) => true;
    public GuPopulationRecord CohortFor(string speciesDefName) => GuPopulationLedger.Find(cohorts, speciesDefName);

    public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, string assetScope) =>
        stage != GuCohortStage.Mature || rank != species.minRank ? "The test source issues only mature wild-rank Gu."
        : trigger == ProbeTrigger ? assetScope == null ? null : "A probe draw carries no asset."
        : trigger == BaitTrigger ? assetScope == BaitScope ? null : "The test bait trigger requires its declared bait."
        : "The test source does not authorize that trigger.";

    public string PlacementRefusal => "Choose an open cell near the test source anchor on its bound map.";

    public bool PlacementReady(Map map, IntVec3 cell, string trigger) =>
        map != null && map == BoundMap && anchor.IsValid && anchor.DistanceToSquared(cell) <= 144;

    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id", "");
        Scribe_Values.Look(ref mapId, "mapId", "");
        Scribe_Values.Look(ref anchor, "anchor", IntVec3.Invalid);
        Scribe_Collections.Look(ref cohorts, "cohorts", LookMode.Deep);
        cohorts ??= new();
    }
}

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice0 => GenCommandLine.CommandLineArgPassed("guslice0");
    private static string Slice0StatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-slice0-state.json");
    private const string DevSpecies = CompGuSpiritSpring.TestSpecies;
    private const string HopeSpecies = "GU_HopeGu";
    private static Action<GuTransactionPoint, string> slice0Probe;

    private record Slice0Opening(string OwnerKind, string SourceId, string Species, long Total);
    private record Slice0Claim(GuSourceRequest Request, string GuId, string ProjectionId, long RevisionAfter);
    private record Slice0State(string CaveId, string SourceId, string SecondSourceId, List<Slice0Opening> Openings,
        Slice0Claim Primary, Slice0Claim Baited, Slice0Claim Unresolved, Slice0Claim Published, Slice0Claim ActorA,
        GuSourceRequest ActorB, GuSourceRequest Quarantined, string QuarantinedId, Slice0Claim Hope,
        Slice0Claim SpringSearch, Slice0Claim SpringLure, long DevRevision, long HopeRevision, int HopeMature,
        int Individuals, Slice0Claim PostReload = null);

    private static void Slice0ProbePrefix(GuTransactionPoint __0, string __1) => slice0Probe?.Invoke(__0, __1);

    private static Harmony PatchSlice0Faults()
    {
        var harmony = new Harmony("projectgu.verification.slice0");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(GuTransactionProbe), nameof(GuTransactionProbe.Reach)),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(Slice0ProbePrefix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup)),
            postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseSpawn)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.GetUniqueLoadID)),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseIdentityCheck)));
        return harmony;
    }

    // ---- Stage new ----------------------------------------------------------------------------------------------------

    private static void ExerciseSlice0(Pawn disciple, CompGuSpiritSpring spring)
    {
        var registry = IndividualRegistry;
        Map cave = disciple.Map;
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        Check(registry.Records.Count == 0, "The fresh clan has no canonical Gu before Slice 0 begins");

        // Spring openings after the Hope Gu awakening (a legitimate direct sink with no record).
        var springSource = GuIndividualRegistry.ForSpring(spring);
        var openings = new List<Slice0Opening>
        {
            new(springSource.OwnerKind, spring.HabitatId, "GU_MoonlightGu", springSource.CohortFor("GU_MoonlightGu").TotalCount),
            new(springSource.OwnerKind, spring.HabitatId, "GU_LiquorWorm", springSource.CohortFor("GU_LiquorWorm").TotalCount)
        };

        // ---- Spring adapter under ordinary play -------------------------------------------------------------------------
        Prefs.DevMode = false;
        var moon = DefDatabase<GuSpeciesDef>.GetNamed("GU_MoonlightGu");
        var liquor = DefDatabase<GuSpeciesDef>.GetNamed("GU_LiquorWorm");
        long moonBefore = springSource.CohortFor(moon.defName).Revision;
        Check(spring.TryEncounterWildGu(moon, registry, out var moonGu, out string reason), $"Ordinary spring search committed through the shared transaction. {reason}");
        var moonReceipt = moonGu.CreationEvidence;
        Check(moonReceipt is { OwnerKind: "spring", Trigger: GuSourceTriggers.SpringSearch, AssetScope: null, Stage: GuCohortStage.Mature }
            && moonReceipt.RevisionBefore == moonBefore && moonReceipt.RevisionAfter == moonBefore + 1 && moonGu.Origin == GuRecord.CohortOrigin
            && moonGu.OriginId == spring.HabitatId && moonGu.OriginNativeId == spring.parent.GetUniqueLoadID() && moonGu.CohortDebitCommitted
            && moonGu.CreationEventKey == $"player-encounter:{spring.HabitatId}:{moon.defName}:{moonBefore}",
            "The spring search receipt names the legacy spring source, its anchor credential, trigger and debit revision");
        var springSearch = new GuSourceRequest(moonGu.CreationEventKey, "spring", spring.HabitatId, spring.parent.GetUniqueLoadID(),
            moon.defName, moon.minRank, GuCohortStage.Mature, GuSourceTriggers.SpringSearch, null, moonBefore);
        var legacySearch = new GuCohortMaterializationRequest(springSearch.EventKey, spring.HabitatId, springSearch.Credential, moon.defName, moonBefore);
        Check(registry.TryEncounterWildCohort(spring, legacySearch, cave, IntVec3.Invalid, out var moonReplay, out _) && moonReplay == moonGu
            && springSource.CohortFor(moon.defName).Revision == moonBefore + 1,
            "The stale spring search request replays the same Gu with no second debit");
        Check(registry.TryMaterializeFromSource(springSource, springSearch with { Trigger = GuSourceTriggers.SpringLure, AssetScope = "Beer" },
                cave, IntVec3.Invalid, out _, out reason) == GuSourceOutcome.Conflict
            && !registry.TryEncounterWildCohort(spring, legacySearch, GuSourceTriggers.SpringLure, "Beer", cave, IntVec3.Invalid, out _, out _),
            $"The spring search key reused as a lure is a payload conflict. {reason}");

        long liquorBefore = springSource.CohortFor(liquor.defName).Revision;
        Check(!spring.TryEncounterWildGu(liquor, registry, out _, out _, GuSourceTriggers.SpringLure, ThingDefOf.WoodLog)
            && springSource.CohortFor(liquor.defName).Revision == liquorBefore, "A lure the species does not answer is refused without a debit");
        var beer = DefDatabase<ThingDef>.GetNamed("Beer");
        Check(spring.TryEncounterWildGu(liquor, registry, out var liquorGu, out reason, GuSourceTriggers.SpringLure, beer),
            $"A beer lure committed through the shared transaction. {reason}");
        Check(liquorGu.CreationEvidence is { Trigger: GuSourceTriggers.SpringLure, AssetScope: "Beer" }
            && liquorGu.CreationEventKey == $"player-lure:{spring.HabitatId}:{liquor.defName}:{liquorBefore}",
            "The lure receipt records its trigger and bait scope under its own event key");
        var springLure = new GuSourceRequest(liquorGu.CreationEventKey, "spring", spring.HabitatId, spring.parent.GetUniqueLoadID(),
            liquor.defName, liquor.minRank, GuCohortStage.Mature, GuSourceTriggers.SpringLure, "Beer", liquorBefore);
        Check(registry.TryMaterializeFromSource(springSource, springLure, cave, IntVec3.Invalid, out var liquorReplay, out _) == GuSourceOutcome.Replayed
            && liquorReplay == liquorGu
            && registry.TryMaterializeFromSource(springSource, springLure with { AssetScope = "Wine" }, cave, IntVec3.Invalid, out _, out _) == GuSourceOutcome.Conflict
            && registry.TryMaterializeFromSource(springSource, springLure with { Trigger = GuSourceTriggers.SpringSearch, AssetScope = null },
                cave, IntVec3.Invalid, out _, out _) == GuSourceOutcome.Conflict,
            "The lure replays, and a different bait or trigger under its key is a conflict");
        CheckSlice0Conservation(openings, "spring encounters");
        Report($"Spring adapter: search and lure committed Moonlight Gu {moonGu.Id} and Liquor Worm {liquorGu.Id} with receipts, replayed, and rejected conflicting payloads.");

        // ---- Test-only source -------------------------------------------------------------------------------------------
        Prefs.DevMode = true;
        IntVec3 anchor = spring.parent.Position;
        var source = new GuSlice0Source { id = "slice0-source-" + Guid.NewGuid().ToString("N"), mapId = cave.GetUniqueLoadID(), anchor = anchor };
        source.cohorts.Add(new GuPopulationRecord(DevSpecies, 8, 1, -1));
        source.cohorts.Add(new GuPopulationRecord(HopeSpecies, 2, 0, -1));
        var other = new GuSlice0Source { id = "slice0-other-" + Guid.NewGuid().ToString("N"), mapId = cave.GetUniqueLoadID(), anchor = anchor };
        other.cohorts.Add(new GuPopulationRecord(DevSpecies, 1, 0, -1));
        GuSlice0Sources.Instance.sources.Add(source);
        GuSlice0Sources.Instance.sources.Add(other);
        openings.Add(new(GuSlice0Source.Kind, source.id, DevSpecies, 9));
        openings.Add(new(GuSlice0Source.Kind, source.id, HopeSpecies, 2));
        openings.Add(new(GuSlice0Source.Kind, other.id, DevSpecies, 1));
        var dev = source.CohortFor(DevSpecies);

        GuSourceRequest Prepare(string key, string species = DevSpecies, string trigger = GuSlice0Source.ProbeTrigger, string scope = null)
        {
            Check(GuIndividualRegistry.TryPrepareSourceRequest(source, species, trigger, scope, key, out var prepared), $"Prepared {key}");
            return prepared;
        }
        IntVec3 Cell() => GenRadial.RadialCellsAround(anchor, 11.5f, false).First(candidate => IsEmptyInventoryCell(cave, candidate)
            && !cave.reservationManager.IsReserved(candidate) && candidate.DistanceToSquared(anchor) <= 132);
        GuSourceOutcome Run(GuSourceRequest request, out GuRecord individual, out string why, IGuPopulationSource from = null) =>
            registry.TryMaterializeFromSource(from ?? source, request, cave, Cell(), out individual, out why);
        int GuPawns() => PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().Count();
        void Unchanged(int mature, long revision, int records, string what) =>
            Check(dev.MatureCount == mature && dev.Revision == revision && registry.Records.Count == records,
                $"{what}: dev cohort {dev.MatureCount} mature at revision {dev.Revision}, {registry.Records.Count} records");

        Prefs.DevMode = false;
        Check(Run(Prepare("slice0-devmode-off"), out _, out reason) == GuSourceOutcome.Refused, $"A fixture source needs Development Mode. {reason}");
        Prefs.DevMode = true;

        var harmony = PatchSlice0Faults();
        Slice0Claim primaryClaim, baitedClaim, unresolvedClaim, publishedClaim, actorClaim, hopeClaim;
        GuSourceRequest actorB, quarantined;
        string quarantinedId;
        try
        {
            // a. Failure before staging.
            int pawns = GuPawns(), records = registry.Records.Count;
            slice0Probe = (point, key) => { if (point == GuTransactionPoint.BeforeStaging) throw new InvalidOperationException("Injected fault before staging"); };
            Check(Run(Prepare("slice0-before-staging"), out _, out reason) == GuSourceOutcome.RolledBack && GuPawns() == pawns,
                $"A fault before staging creates no native object and spends nothing. {reason}");
            slice0Probe = null;
            Unchanged(8, 0, records, "Before-staging rollback");

            // b. Failure during native SpawnSetup, with callback re-entry and a save request inside staging.
            var primary = Prepare("slice0-primary");
            Pawn_GuWildProjection aborted = null;
            bool reentryRefused = false;
            const string stagingSave = "gu-slice0-staging-save";
            afterReleaseSpawn = pawn =>
            {
                aborted = pawn;
                reentryRefused = pawn.ReleaseSuppressed && registry.Resolve(pawn) == null
                    && registry.TryMaterializeFromSource(source, primary, cave, Cell(), out _, out _) == GuSourceOutcome.Refused
                    && registry.TryMaterializeFromSource(source, primary with { EventKey = "slice0-reentry-other" }, cave, Cell(), out _, out _) == GuSourceOutcome.Refused
                    && dev.MatureCount == 8;
                GameDataSaveLoader.SaveGame(stagingSave);
                throw new InvalidOperationException("Injected native SpawnSetup failure");
            };
            Check(Run(primary, out _, out reason) == GuSourceOutcome.RolledBack && reentryRefused && aborted is { Discarded: true, Dead: false }
                && !File.Exists(GenFilePaths.FilePathForSavedGame(stagingSave)),
                $"A SpawnSetup fault rolled back, refused callback re-entry and the save request, and discarded the shell. {reason}");
            afterReleaseSpawn = null;
            CheckOldPawnAbsent(aborted.GetUniqueLoadID());
            Unchanged(8, 0, records, "SpawnSetup rollback");

            Check(Run(primary, out var primaryGu, out reason) == GuSourceOutcome.Committed, $"The same request committed on retry. {reason}");
            var primaryPawn = (Pawn_GuWildProjection)registry.ProjectionOf(primaryGu);
            Check(registry.Resolve(primaryPawn) == primaryGu && !primaryPawn.ReleaseSuppressed && primaryGu.CohortDebitCommitted
                && primaryGu.CreationEvidence is { OwnerKind: GuSlice0Source.Kind, Trigger: GuSlice0Source.ProbeTrigger, RevisionBefore: 0, RevisionAfter: 1 }
                && primaryGu.OriginId == source.id && primaryGu.OriginNativeId == primary.Credential && primaryGu.Rank == 1,
                "Exactly one mature unit became one canonical individual with one published projection and a durable receipt");
            Unchanged(7, 1, records + 1, "Primary commit");
            primaryClaim = new(primary, primaryGu.Id, primaryPawn.GetUniqueLoadID(), 1);
            CheckSlice0Conservation(openings, "primary");

            // c. Replay: stale revision and a replaced native instance still replay; a new request with the old credential does not.
            var staleCredential = Prepare("slice0-stale-credential");
            source.ReplaceNativeInstance();
            Check(Run(primary, out var replay, out _) == GuSourceOutcome.Replayed && replay == primaryGu
                && registry.TryMaterializeFromSource(source, primary, cave, IntVec3.Invalid, out var replayNoCell, out _) == GuSourceOutcome.Replayed
                && replayNoCell == primaryGu,
                "The committed request replays after its revision went stale and its native credential was replaced");
            Check(Run(staleCredential, out _, out reason) == GuSourceOutcome.Refused, $"A stale native credential cannot authorize a new transaction. {reason}");
            Unchanged(7, 1, records + 1, "Replays");

            // d. A receipt with an asset scope, then payload conflicts.
            var baited = Prepare("slice0-baited", trigger: GuSlice0Source.BaitTrigger, scope: GuSlice0Source.BaitScope);
            Check(Run(baited, out var baitedGu, out reason) == GuSourceOutcome.Committed
                && baitedGu.CreationEvidence is { Trigger: GuSlice0Source.BaitTrigger, AssetScope: GuSlice0Source.BaitScope },
                $"An asset-scoped draw committed. {reason}");
            baitedClaim = new(baited, baitedGu.Id, registry.ProjectionOf(baitedGu).GetUniqueLoadID(), 2);
            Check(Run(primary with { Trigger = GuSlice0Source.BaitTrigger, AssetScope = GuSlice0Source.BaitScope }, out _, out _) == GuSourceOutcome.Conflict
                && Run(primary with { SpeciesDefName = HopeSpecies }, out _, out _) == GuSourceOutcome.Conflict
                && Run(primary with { SourceId = other.id, Credential = other.CurrentCredential }, out _, out _, other) == GuSourceOutcome.Conflict
                && Run(baited with { AssetScope = "Wine" }, out _, out _) == GuSourceOutcome.Conflict
                && Run(baited with { AssetScope = null }, out _, out _) == GuSourceOutcome.Conflict
                && Run(baited with { Trigger = GuSlice0Source.ProbeTrigger, AssetScope = null }, out _, out reason) == GuSourceOutcome.Conflict,
                $"Reused keys with a different trigger, species, source or asset scope are conflicts. {reason}");
            Check(Run(primary with { SourceId = other.id }, out _, out _) == GuSourceOutcome.Refused,
                "A request naming a different source than the one supplied is refused before any receipt lookup");
            Unchanged(6, 2, records + 2, "Conflicts");

            // e. Authorization: rank, stage, trigger, asset scope and species.
            var fresh = Prepare("slice0-authorization");
            Check(Run(fresh with { Rank = 2 }, out _, out _) == GuSourceOutcome.Refused
                && Run(fresh with { Stage = GuCohortStage.Juvenile }, out _, out _) == GuSourceOutcome.Refused
                && Run(fresh with { Trigger = "slice0-unknown" }, out _, out _) == GuSourceOutcome.Refused
                && Run(fresh with { Trigger = GuSlice0Source.BaitTrigger, AssetScope = "WoodLog" }, out _, out _) == GuSourceOutcome.Refused
                && Run(fresh with { SpeciesDefName = "GU_MoonlightGu" }, out _, out _) == GuSourceOutcome.Refused
                && !GuIndividualRegistry.TryPrepareSourceRequest(source, "GU_MoonlightGu", GuSlice0Source.ProbeTrigger, null, "x", out _),
                "Wrong rank, juvenile stage, unknown trigger, wrong bait and a species the source does not hold are all refused");
            Unchanged(6, 2, records + 2, "Authorization refusals");

            // f. Failure after staging but before the debit, with re-entry and a save request inside the transaction.
            var afterStaging = Prepare("slice0-after-staging");
            Pawn_GuWildProjection stagedShell = null;
            bool windowRefused = false;
            slice0Probe = (point, key) =>
            {
                if (point != GuTransactionPoint.AfterStagingBeforeDebit) return;
                stagedShell = cave.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>().Single(pawn => pawn.ReleaseSuppressed);
                windowRefused = registry.TryMaterializeFromSource(source, afterStaging, cave, Cell(), out _, out _) == GuSourceOutcome.Refused
                    && registry.Resolve(primaryGu.Id) == null;
                GameDataSaveLoader.SaveGame(stagingSave);
                throw new InvalidOperationException("Injected fault after staging, before the debit");
            };
            Check(Run(afterStaging, out _, out reason) == GuSourceOutcome.RolledBack && windowRefused && stagedShell is { Discarded: true }
                && !File.Exists(GenFilePaths.FilePathForSavedGame(stagingSave)) && registry.Resolve(primaryGu.Id) == primaryGu,
                $"A fault after staging rolled back, refused re-entry and the save, and discarded the staged shell. {reason}");
            slice0Probe = null;
            Unchanged(6, 2, records + 2, "After-staging rollback");

            // g. Failures during exact source revalidation after native callbacks.
            int reached = 0;
            slice0Probe = (point, key) => { if (point == GuTransactionPoint.SourceRevalidation && ++reached == 2) throw new InvalidOperationException("Injected revalidation fault"); };
            Check(Run(Prepare("slice0-revalidation-fault"), out _, out reason) == GuSourceOutcome.RolledBack && reached == 2,
                $"A fault inside post-callback source revalidation rolled back. {reason}");
            slice0Probe = null;
            afterReleaseSpawn = pawn => source.ReplaceNativeInstance();
            Check(Run(Prepare("slice0-instance-replaced"), out _, out reason) == GuSourceOutcome.RolledBack,
                $"A source instance replaced during native callbacks fails revalidation. {reason}");
            afterReleaseSpawn = pawn => dev.AdvanceDaily(99, false, 0);
            Check(Run(Prepare("slice0-cohort-changed"), out _, out reason) == GuSourceOutcome.RolledBack && dev.MatureCount == 7 && dev.JuvenileCount == 0,
                $"A cohort that changed during native callbacks (a juvenile matured) fails revalidation with no debit. {reason}");
            afterReleaseSpawn = null;
            Unchanged(7, 3, records + 2, "Revalidation rollbacks");
            CheckSlice0Conservation(openings, "revalidation");

            // h. Failure between the debit and receipt publication: the preimage is restored exactly.
            var afterDebit = Prepare("slice0-after-debit");
            bool commitWindowRefused = false;
            slice0Probe = (point, key) =>
            {
                if (point != GuTransactionPoint.AfterDebit) return;
                commitWindowRefused = dev.MatureCount == 6 && dev.Revision == 4
                    && registry.TryMaterializeFromSource(source, afterDebit, cave, Cell(), out _, out _) == GuSourceOutcome.Refused
                    && registry.TryMaterializeFromSource(source, afterDebit with { EventKey = "slice0-window-other" }, cave, Cell(), out _, out _) == GuSourceOutcome.Refused;
                throw new InvalidOperationException("Injected fault between debit and receipt publication");
            };
            Check(Run(afterDebit, out var none, out reason) == GuSourceOutcome.RolledBack && none == null && commitWindowRefused,
                $"A fault after the debit restored the exact preimage before publication. {reason}");
            slice0Probe = null;
            Unchanged(7, 3, records + 2, "Restored debit");
            Check(!registry.Records.Any(record => record.CreationEventKey == afterDebit.EventKey), "No receipt survives a restored debit");
            Check(Run(afterDebit, out _, out reason) == GuSourceOutcome.Committed, $"The restored event committed on retry. {reason}");
            Unchanged(6, 4, records + 3, "Retry after restored debit");

            // i. Failure after the receipt and binding were indexed, before publication: everything is unwound.
            var beforePublication = Prepare("slice0-before-publication");
            slice0Probe = (point, key) => { if (point == GuTransactionPoint.BeforePublication) throw new InvalidOperationException("Injected fault before publication"); };
            Check(Run(beforePublication, out _, out reason) == GuSourceOutcome.RolledBack
                && !registry.Records.Any(record => record.CreationEventKey == beforePublication.EventKey),
                $"A fault before publication removed the indexed receipt and restored the debit. {reason}");
            slice0Probe = null;
            Unchanged(6, 4, records + 3, "Unwound before publication");

            // j. Failure after the debit whose restoration also fails: one counted unresolved canonical claim.
            var unrestorable = Prepare("slice0-unrestorable");
            slice0Probe = (point, key) =>
            {
                if (point is GuTransactionPoint.AfterDebit or GuTransactionPoint.RestorePreimage)
                    throw new InvalidOperationException($"Injected fault at {point}");
            };
            Check(Run(unrestorable, out var lost, out reason) == GuSourceOutcome.Unresolved && lost != null && lost.CohortDebitCommitted
                && lost.IsLiving && !lost.IsQuarantined && lost.Unresolved == GuUnresolvedCause.ProjectionMissing && registry.Resolve(lost.Id) == null
                && lost.CreationEvidence is { RevisionBefore: 4, RevisionAfter: 5 },
                $"An unrestorable debit kept exactly one counted unresolved individual with its receipt. {reason}");
            slice0Probe = null;
            Unchanged(5, 5, records + 4, "Unresolved claim");
            Check(Run(unrestorable, out var lostReplay, out _) == GuSourceOutcome.Replayed && lostReplay == lost,
                "The unresolved claim replays without a second debit");
            unresolvedClaim = new(unrestorable, lost.Id, null, 5);
            Unchanged(5, 5, records + 4, "Unresolved replay");

            // k. Failure after publication (postcondition): the debit stands as one counted unresolved individual.
            var postcondition = Prepare("slice0-postcondition");
            duringReleaseIdentityCheck = pawn =>
            {
                if (pawn.ReleaseSuppressed || dev.Revision != 6) return;
                duringReleaseIdentityCheck = null;
                throw new InvalidOperationException("Injected postcondition failure after publication");
            };
            Check(Run(postcondition, out var published, out reason) == GuSourceOutcome.Unresolved && published.CohortDebitCommitted
                && published.Unresolved == GuUnresolvedCause.ProjectionMissing && registry.Resolve(published.Id) == null,
                $"A postcondition failure after publication kept one counted unresolved individual. {reason}");
            duringReleaseIdentityCheck = null;
            publishedClaim = new(postcondition, published.Id, null, 6);
            Unchanged(4, 6, records + 5, "Post-publication failure");

            // l. Native cleanup fails before any debit: the event is quarantined and the shell counts nothing.
            quarantined = Prepare("slice0-cleanup-fails");
            Pawn_GuWildProjection unproven = null;
            afterReleaseSpawn = pawn => { unproven = pawn; throw new InvalidOperationException("Injected SpawnSetup failure before cleanup"); };
            slice0Probe = (point, key) => { if (point == GuTransactionPoint.Cleanup) throw new InvalidOperationException("Injected native cleanup failure"); };
            Check(Run(quarantined, out _, out reason) == GuSourceOutcome.Quarantined && unproven != null && !unproven.Spawned
                && registry.Resolve(unproven) == null && registry.RogueProjections.Any(tag => tag.parent == unproven),
                $"Unproven cleanup quarantined the event and left an unbound, rogue shell. {reason}");
            afterReleaseSpawn = null;
            slice0Probe = null;
            var claim = registry.Records.Single(record => record.CreationEventKey == quarantined.EventKey);
            quarantinedId = claim.Id;
            Check(claim.IsQuarantined && !claim.CohortDebitCommitted && Run(quarantined, out _, out _) == GuSourceOutcome.Refused,
                "The quarantined event carries no debit and blocks replay");
            Unchanged(4, 6, records + 6, "Quarantined cleanup");
            CheckSlice0Conservation(openings, "fault matrix");

            // m. Two actors compete for the last mature Gu.
            for (int drain = 0; drain < 3; drain++)
                Check(Run(Prepare($"slice0-drain-{drain}"), out _, out reason) == GuSourceOutcome.Committed, $"Drain {drain}. {reason}");
            Unchanged(1, 9, records + 9, "Drained to the last mature Gu");
            var actorA = Prepare("slice0-actor-a");
            actorB = Prepare("slice0-actor-b");
            bool interleavedRefused = false;
            afterReleaseSpawn = pawn => interleavedRefused = registry.TryMaterializeFromSource(source, actorB, cave, Cell(), out _, out _) == GuSourceOutcome.Refused;
            Check(Run(actorA, out var actorGu, out reason) == GuSourceOutcome.Committed && interleavedRefused, $"Actor A won the last mature Gu; actor B's interleaved attempt was refused. {reason}");
            afterReleaseSpawn = null;
            Check(Run(actorB, out _, out reason) == GuSourceOutcome.Refused && dev.MatureCount == 0, $"Actor B's request is stale after A's commit. {reason}");
            Check(Run(Prepare("slice0-actor-b-retry"), out _, out reason) == GuSourceOutcome.Refused, $"Actor B's fresh request finds no mature Gu. {reason}");
            actorClaim = new(actorA, actorGu.Id, registry.ProjectionOf(actorGu).GetUniqueLoadID(), 10);
            Unchanged(0, 10, records + 10, "Last mature Gu");

            // n. An independent species cohort in the same source.
            var hope = Prepare("slice0-hope-1", HopeSpecies);
            Check(Run(hope, out var hopeGu, out reason) == GuSourceOutcome.Committed && hopeGu.SpeciesDefName == HopeSpecies
                && source.CohortFor(HopeSpecies).MatureCount == 1, $"A second species committed from its own cohort. {reason}");
            hopeClaim = new(hope, hopeGu.Id, registry.ProjectionOf(hopeGu).GetUniqueLoadID(), 1);

            // o. Ambiguous and inconsistent ownership are blocked.
            var clone = new GuSlice0Source { id = source.id, mapId = source.mapId, anchor = anchor };
            clone.cohorts.Add(new GuPopulationRecord(HopeSpecies, 5, 0, -1));
            GuSlice0Sources.Instance.sources.Add(clone);
            Check(Run(Prepare("slice0-duplicate", HopeSpecies), out _, out reason) == GuSourceOutcome.Refused
                && reason.StartsWith("More than one live source", StringComparison.Ordinal), $"A duplicated source identity is blocked. {reason}");
            GuSlice0Sources.Instance.sources.Remove(clone);
            CheckRegeneratedSourceBlocked(source, HopeSpecies, "slice0-regenerated", cave, Cell());
            CheckSlice0Conservation(openings, "stage new");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            slice0Probe = null;
            afterReleaseSpawn = duringReleaseIdentityCheck = null;
        }

        int individuals = registry.Records.Count(record => record.CohortDebitCommitted && record.IsLiving);
        var state = new Slice0State(cave.GetUniqueLoadID(), source.id, other.id, openings, primaryClaim, baitedClaim, unresolvedClaim,
            publishedClaim, actorClaim, actorB, quarantined, quarantinedId, hopeClaim,
            new(springSearch, moonGu.Id, registry.ProjectionOf(moonGu).GetUniqueLoadID(), moonBefore + 1),
            new(springLure, liquorGu.Id, registry.ProjectionOf(liquorGu).GetUniqueLoadID(), liquorBefore + 1),
            dev.Revision, source.CohortFor(HopeSpecies).Revision, source.CohortFor(HopeSpecies).MatureCount, individuals);
        File.WriteAllText(Slice0StatePath, JsonSerializer.Serialize(state));
        AssertNoErrors("slice0-new");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Slice 0 fault matrix passed: {individuals} counted individuals, every failure point rolled back, restored, quarantined or kept one unresolved claim, and two actors split the last mature Gu once.");
    }

    /// <summary>A source whose cohort revision falls below its committed receipts (regenerated or restored out of order) is blocked.</summary>
    private static void CheckRegeneratedSourceBlocked(GuSlice0Source source, string species, string key, Map map, IntVec3 cell)
    {
        var cohort = source.CohortFor(species);
        var revision = AccessTools.Field(typeof(GuPopulationRecord), "revision");
        long actual = cohort.Revision;
        int records = IndividualRegistry.Records.Count;
        revision.SetValue(cohort, 0L);
        try
        {
            Check(GuIndividualRegistry.TryPrepareSourceRequest(source, species, GuSlice0Source.ProbeTrigger, null, key, out var request)
                && IndividualRegistry.TryMaterializeFromSource(source, request, map, cell, out _, out string reason) == GuSourceOutcome.Refused
                && reason.Contains("inconsistent") && IndividualRegistry.Records.Count == records,
                "A source cohort older than its committed receipts cannot issue Gu");
        }
        finally { revision.SetValue(cohort, actual); }
    }

    private static void CheckSlice0Conservation(List<Slice0Opening> openings, string label)
    {
        var registry = IndividualRegistry;
        foreach (var opening in openings)
        {
            IGuPopulationSource source = opening.OwnerKind == GuSlice0Source.Kind
                ? GuSlice0Sources.Instance.sources.Single(entry => entry.id == opening.SourceId)
                : GuIndividualRegistry.ForSpring(Find.Maps.SelectMany(map => map.listerThings.AllThings)
                    .Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(spring => spring?.HabitatId == opening.SourceId));
            long units = source.CohortFor(opening.Species).TotalCount;
            var counted = registry.Records.Where(record => record.CohortDebitCommitted && record.OriginId == opening.SourceId
                && record.SpeciesDefName == opening.Species).ToList();
            Check(units + counted.Count == opening.Total && counted.All(record => record.IsLiving),
                $"Conservation ({label}) for {opening.Species} at {opening.SourceId}: {units} cohort units + {counted.Count} counted individuals = {opening.Total}");
        }
        // An unauthorized shell counts nothing: every spawned Gu Pawn either resolves to a counted record or is a known rogue.
        foreach (var pawn in Find.Maps.SelectMany(map => map.mapPawns.AllPawnsSpawned).OfType<Pawn_GuWildProjection>())
            Check(registry.Resolve(pawn) is { } owner ? owner.CohortDebitCommitted || owner.Origin != GuRecord.CohortOrigin
                : registry.RogueProjections.Any(tag => tag.parent == pawn), $"Spawned Gu Pawn {pawn.GetUniqueLoadID()} is counted or a known rogue");
    }

    // ---- Stage reload --------------------------------------------------------------------------------------------------

    private static (Slice0State State, GuSlice0Source Source, GuSlice0Source Other, Map Cave) LoadSlice0()
    {
        var state = JsonSerializer.Deserialize<Slice0State>(File.ReadAllText(Slice0StatePath));
        var sources = GuSlice0Sources.Instance.sources;
        Check(sources.Count == 2, $"Both saved test sources loaded ({sources.Count})");
        var source = sources.Single(entry => entry.id == state.SourceId);
        var other = sources.Single(entry => entry.id == state.SecondSourceId);
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == state.CaveId);
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        return (state, source, other, cave);
    }

    private static void VerifySlice0Reload()
    {
        var (state, source, other, cave) = LoadSlice0();
        var registry = IndividualRegistry;
        var dev = source.CohortFor(DevSpecies);
        var hopeCohort = source.CohortFor(HopeSpecies);
        Check(dev.MatureCount == 0 && dev.JuvenileCount == 0 && dev.Revision == state.DevRevision && hopeCohort.MatureCount == state.HopeMature
            && hopeCohort.Revision == state.HopeRevision && other.CohortFor(DevSpecies).MatureCount == 1 && other.CohortFor(DevSpecies).Revision == 0,
            "Reload neither reseeded nor credited any source cohort");
        Check(registry.Records.Count(record => record.CohortDebitCommitted && record.IsLiving) == state.Individuals,
            $"Reload kept exactly {state.Individuals} counted individuals");
        Check(source.CurrentCredential != state.Primary.Request.Credential, "The native source credential is new in this process");

        IntVec3 Cell() => GenRadial.RadialCellsAround(source.anchor, 11.5f, false).First(candidate => IsEmptyInventoryCell(cave, candidate)
            && !cave.reservationManager.IsReserved(candidate) && candidate.DistanceToSquared(source.anchor) <= 132);
        VerifySlice0Claims(state, source, cave);

        // Old credential: refused for a new event. Reacquired credential: authorized.
        var old = state.Hope.Request with { EventKey = "slice0-hope-2", ExpectedRevision = hopeCohort.Revision };
        Check(registry.TryMaterializeFromSource(source, old, cave, Cell(), out _, out string reason) == GuSourceOutcome.Refused,
            $"A credential from the previous process cannot authorize a new transaction. {reason}");
        Check(GuIndividualRegistry.TryPrepareSourceRequest(source, HopeSpecies, GuSlice0Source.ProbeTrigger, null, "slice0-hope-2", out var reacquired),
            "Reacquired the live source credential");
        Check(registry.TryMaterializeFromSource(source, reacquired, cave, Cell(), out var hope2, out reason) == GuSourceOutcome.Committed,
            $"A reacquired credential authorized the next transaction. {reason}");
        Check(hopeCohort.MatureCount == 0 && hope2.CreationEvidence.RevisionAfter == state.HopeRevision + 1, "The post-reload commit debited once");
        CheckRegeneratedSourceBlocked(source, HopeSpecies, "slice0-regenerated-after-load", cave, Cell());
        var clone = new GuSlice0Source { id = source.id, mapId = source.mapId, anchor = source.anchor };
        GuSlice0Sources.Instance.sources.Add(clone);
        Check(registry.TryMaterializeFromSource(source, reacquired with { EventKey = "slice0-dup-after-load" }, cave, Cell(), out _, out reason)
            == GuSourceOutcome.Refused && reason.StartsWith("More than one live source", StringComparison.Ordinal),
            $"A duplicated source identity after load is blocked by the uniqueness check. {reason}");
        GuSlice0Sources.Instance.sources.Remove(clone);
        CheckSlice0Conservation(state.Openings, "reload");
        File.WriteAllText(Slice0StatePath, JsonSerializer.Serialize(state with
        {
            PostReload = new(reacquired, hope2.Id, registry.ProjectionOf(hope2).GetUniqueLoadID(), hope2.CreationEvidence.RevisionAfter),
            Individuals = state.Individuals + 1
        }));
        AssertNoErrors("slice0-reload");
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Fresh-process reload kept every receipt, binding and count, replayed stale requests, rejected conflicts, refused the old credential and committed with a reacquired one.");
    }

    private static void VerifySlice0Final()
    {
        var (state, source, other, cave) = LoadSlice0();
        var registry = IndividualRegistry;
        Check(registry.Records.Count(record => record.CohortDebitCommitted && record.IsLiving) == state.Individuals,
            $"The second fresh process kept exactly {state.Individuals} counted individuals");
        VerifySlice0Claims(state, source, cave);
        var post = state.PostReload;
        Check(registry.FindRecord(post.GuId) is { CohortDebitCommitted: true } record && record.CreationEvidence.RevisionAfter == post.RevisionAfter
            && registry.Resolve(record.Id) == record && registry.ProjectionOf(record).GetUniqueLoadID() == post.ProjectionId
            && registry.TryMaterializeFromSource(source, post.Request, cave, IntVec3.Invalid, out var replay, out _) == GuSourceOutcome.Replayed && replay == record
            && source.CohortFor(HopeSpecies).MatureCount == 0,
            "The commit made after the first reload persisted and replays with its stale credential");
        CheckSlice0Conservation(state.Openings, "final");
        AssertNoErrors("slice0-final");
        Report("Second fresh-process reload preserved the post-reload commit, every receipt and exact conservation.");
    }

    private static void VerifySlice0Claims(Slice0State state, GuSlice0Source source, Map cave)
    {
        var registry = IndividualRegistry;
        var springThing = cave.listerThings.AllThings.Single(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = GuIndividualRegistry.ForSpring(springThing.TryGetComp<CompGuSpiritSpring>());
        GuSourceOutcome Replay(IGuPopulationSource from, GuSourceRequest request, out GuRecord found) =>
            registry.TryMaterializeFromSource(from, request, cave, IntVec3.Invalid, out found, out _);
        foreach (var (claim, from, bound) in new[]
        {
            (state.Primary, (IGuPopulationSource)source, true), (state.Baited, source, true), (state.ActorA, source, true), (state.Hope, source, true),
            (state.Unresolved, source, false), (state.Published, source, false), (state.SpringSearch, spring, true), (state.SpringLure, spring, true)
        })
        {
            var record = registry.FindRecord(claim.GuId);
            Check(record is { CohortDebitCommitted: true, IsLiving: true, IsQuarantined: false } && record.CreationEventKey == claim.Request.EventKey
                && record.CreationEvidence?.RevisionAfter == claim.RevisionAfter && record.OriginId == claim.Request.SourceId,
                $"Receipt {claim.Request.EventKey} survived the fresh process with its revision and source");
            Check(bound ? registry.Resolve(record.Id) == record && registry.ProjectionOf(record)?.GetUniqueLoadID() == claim.ProjectionId
                    : record.Unresolved == GuUnresolvedCause.ProjectionMissing && registry.Resolve(record.Id) == null,
                $"Gu {record.Id} kept its {(bound ? "exact native binding" : "unresolved counted claim")}");
            Check(Replay(from, claim.Request, out var replay) == GuSourceOutcome.Replayed && replay == record,
                $"Event {claim.Request.EventKey} replays after reload with a stale revision and credential");
        }
        Check(Replay(source, state.Primary.Request with { Trigger = GuSlice0Source.BaitTrigger, AssetScope = GuSlice0Source.BaitScope }, out _) == GuSourceOutcome.Conflict
            && Replay(source, state.Baited.Request with { AssetScope = "Wine" }, out _) == GuSourceOutcome.Conflict
            && Replay(spring, state.SpringLure.Request with { AssetScope = null, Trigger = GuSourceTriggers.SpringSearch }, out _) == GuSourceOutcome.Conflict,
            "Conflicting payloads are still rejected after reload");
        Check(registry.FindRecord(state.QuarantinedId) is { IsQuarantined: true, CohortDebitCommitted: false }
            && Replay(source, state.Quarantined, out _) == GuSourceOutcome.Refused && Replay(source, state.ActorB, out _) == GuSourceOutcome.Refused,
            "The quarantined event and the losing actor's request still authorize nothing");
    }

    // ---- Historical saves ----------------------------------------------------------------------------------------------

    private static string Slice0LegacyPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-slice0-legacy.json");
    private record Slice0Legacy(List<string> LegacyIds, List<string> LegacyKeys, string NewGuId, string NewKey, string SpringId, string Species, long Total);

    private static void VerifySlice0Legacy()
    {
        var registry = IndividualRegistry;
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        string label = GenCommandLine.TryGetCommandLineArg("guslice0source", out string value) ? value : "historical";
        var legacy = registry.Records.Where(record => record.Origin == GuRecord.CohortOrigin).ToList();
        Check(legacy.All(record => record.CreationEvidence == null), "Every historical cohort record has no Alpha 6 receipt");
        Check(!registry.Records.Any(record => record.QuarantineReason?.Contains("receipt") == true), "No historical record was quarantined by receipt validation");
        var springs = Find.Maps.SelectMany(map => map.listerThings.AllThings).Select(thing => thing.TryGetComp<CompGuSpiritSpring>())
            .Where(spring => spring != null).ToList();
        int replayed = 0;
        foreach (var record in legacy.Where(record => record.CohortDebitCommitted && !record.IsQuarantined))
        {
            var spring = springs.SingleOrDefault(candidate => candidate.HabitatId == record.OriginId);
            if (spring == null) continue;
            var request = new GuCohortMaterializationRequest(record.CreationEventKey, record.OriginId, record.OriginNativeId, record.SpeciesDefName, 0);
            Check(registry.TryMaterializeCohort(spring, request, spring.parent.Map, IntVec3.Invalid, out var replay, out string reason) && replay == record,
                $"Historical event {record.CreationEventKey} replays under its original rule. {reason}");
            Check(!registry.TryMaterializeCohort(spring, request with { SourceNativeId = request.SourceNativeId + "-other" }, spring.parent.Map,
                IntVec3.Invalid, out _, out _), "A historical event under another anchor credential is rejected");
            replayed++;
        }
        Report($"{label}: {legacy.Count} historical cohort records, {replayed} replayed under the original spring rule, {springs.Count} springs.");

        string newId = null, newKey = null, springId = null, species = null;
        long total = 0;
        Prefs.DevMode = false;
        foreach (var spring in springs.Where(candidate => CompGuSpiritSpring.PlayerCanAccessEncounterMap(candidate.parent.Map)))
        {
            var candidate = DefDatabase<GuSpeciesDef>.AllDefs.FirstOrDefault(def => def.wildPopulationEnabled && !def.UsesLureEncounter
                && def.capabilityTags?.Contains("development_test") != true && spring.MatureCountFor(def.defName) > 0
                && def.defName != HopeSpecies);
            candidate ??= DefDatabase<GuSpeciesDef>.GetNamedSilentFail(HopeSpecies) is { } hope && spring.MatureCountFor(HopeSpecies) > 0 ? hope : null;
            if (candidate == null) continue;
            var source = GuIndividualRegistry.ForSpring(spring);
            long before = source.CohortFor(candidate.defName).Revision;
            total = source.CohortFor(candidate.defName).TotalCount + registry.Records.Count(record => record.CohortDebitCommitted
                && record.OriginId == spring.HabitatId && record.SpeciesDefName == candidate.defName);
            Check(spring.TryEncounterWildGu(candidate, registry, out var created, out string reason), $"A new search on a historical spring committed. {reason}");
            Check(created.CreationEvidence is { OwnerKind: "spring" } receipt && receipt.RevisionBefore == before
                && created.OriginNativeId == spring.parent.GetUniqueLoadID(), "The new record on a historical spring carries an Alpha 6 receipt");
            (newId, newKey, springId, species) = (created.Id, created.CreationEventKey, spring.HabitatId, candidate.defName);
            break;
        }
        Prefs.DevMode = true;
        if (newId == null) Report($"{label}: no accessible historical spring had eligible search stock; the new-commit check was not applicable.");
        File.WriteAllText(Slice0LegacyPath, JsonSerializer.Serialize(new Slice0Legacy(legacy.Select(record => record.Id).ToList(),
            legacy.Select(record => record.CreationEventKey).ToList(), newId, newKey, springId, species, total)));
        AssertNoErrors("slice0-legacy");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifySlice0LegacyReload()
    {
        var registry = IndividualRegistry;
        var saved = JsonSerializer.Deserialize<Slice0Legacy>(File.ReadAllText(Slice0LegacyPath));
        for (int index = 0; index < saved.LegacyIds.Count; index++)
            Check(registry.FindRecord(saved.LegacyIds[index]) is { CreationEvidence: null } record && record.CreationEventKey == saved.LegacyKeys[index],
                "Historical records kept their original provenance and no receipt after a resave");
        if (saved.NewGuId != null)
        {
            var record = registry.FindRecord(saved.NewGuId);
            var spring = Find.Maps.SelectMany(map => map.listerThings.AllThings).Select(thing => thing.TryGetComp<CompGuSpiritSpring>())
                .Single(candidate => candidate?.HabitatId == saved.SpringId);
            var source = GuIndividualRegistry.ForSpring(spring);
            Check(record is { CohortDebitCommitted: true, IsQuarantined: false } && record.CreationEvidence != null
                && registry.TryMaterializeFromSource(source, new GuSourceRequest(saved.NewKey, "spring", saved.SpringId, record.OriginNativeId,
                    record.SpeciesDefName, record.Rank, GuCohortStage.Mature, GuSourceTriggers.SpringSearch, null, 0),
                    spring.parent.Map, IntVec3.Invalid, out var replay, out _) == GuSourceOutcome.Replayed && replay == record,
                "The new commit on the historical spring persisted with its receipt and replays");
            long units = source.CohortFor(saved.Species).TotalCount;
            int counted = registry.Records.Count(entry => entry.CohortDebitCommitted && entry.OriginId == saved.SpringId && entry.SpeciesDefName == saved.Species);
            Check(units + counted == saved.Total, $"Historical spring conservation: {units} units + {counted} counted = {saved.Total}");
        }
        AssertNoErrors("slice0-legacyreload");
        Report("Historical save resaved and reloaded in a fresh process with its original records and validation rules.");
    }
}
