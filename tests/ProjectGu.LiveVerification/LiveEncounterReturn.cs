using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static bool EncounterReturn => GenCommandLine.CommandLineArgPassed("guencounterreturn");
    private static string ReturnStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-encounter-return.json");
    private static Pawn_GuWildProjection returnProbe;
    private static System.Action afterReturnDetach;
    private static bool refuseReturnDiscard;
    private record ReturnState(int Tile, string Id, int Custody, long Revision, int Mature, string ProtectedId);

    private static void AfterReturnDetach(Pawn __instance)
    {
        if (__instance == returnProbe && afterReturnDetach is { } action)
        {
            afterReturnDetach = null;
            action();
        }
    }

    private static bool BeforeReturnDiscard(Pawn pawn, PawnDiscardDecideMode discardMode) =>
        !refuseReturnDiscard || pawn != returnProbe || discardMode != PawnDiscardDecideMode.Discard;

    private static void InstallReturnFaults(Harmony harmony)
    {
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.DeSpawn)),
            postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterReturnDetach)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(WorldPawns), nameof(WorldPawns.PassToWorld)),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(BeforeReturnDiscard)));
    }

    private static Pawn_GuWildProjection SurfacePawn(Map map, string species) =>
        map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>().Single(pawn => IndividualRegistry.Resolve(pawn)?.SpeciesDefName == species);

    private static void EncounterSample(GuSurfaceEcology ecology, int earliest)
    {
        Find.TickManager.DebugSetTicksGame(((earliest + 2499) / 2500) * 2500);
        ecology.GameComponentTick();
    }

    private static void ExerciseEncounterReturn()
    {
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        var registry = IndividualRegistry;
        var ecology = GuSurfaceEcology.Instance;
        var home = Find.CurrentMap;
        var tile = Find.WorldGrid.Surface.Tiles.First(candidate => candidate.PrimaryBiome.defName == "TemperateForest"
            && TileFinder.IsValidTileForNewSettlement(candidate.tile) && !Find.WorldObjects.AnyWorldObjectAt(candidate.tile)).tile;
        var map = RegionalSettlement(tile).Map;
        Current.Game.CurrentMap = home;
        RegionalTick();
        var black = ecology.LocalityAt(map, BlackSpecies);
        var white = ecology.LocalityAt(map, WhiteSpecies);
        foreach (var row in new[] { black, white })
            if (row.Cohort.MatureCount == 0) Check(row.Cohort.TryRecruitMature(4, 0), "Fixture supplies one finite adult");
        EncounterSample(ecology, Now + 1);
        var pawn = SurfacePawn(map, BlackSpecies);
        var record = registry.Resolve(pawn);
        var whiteRecord = registry.Resolve(SurfacePawn(map, WhiteSpecies));
        int custody = record.CustodyRevision;
        bool Attempt(Map destination) => registry.TryReturnSurfaceResident(record.Id, custody, destination, out _);
        int day = GuSurfaceEcology.TicksPerDay;
        Check(!Attempt(map) && pawn.Spawned, "New physical encounters receive a one-day quiet interval");
        Find.TickManager.DebugSetTicksGame(Now + day);
        Check(!Attempt(home) && !registry.TryReturnSurfaceResident(record.Id, custody - 1, map, out _),
            "Unrelated destinations and stale custody revisions are refused");

        var actor = home.mapPawns.FreeColonistsSpawned.First();
        var oldPosition = actor.Position;
        actor.DeSpawn();
        var distant = map.AllCells.First(cell => cell.DistanceToSquared(pawn.Position) > 400
            && GuIndividualRegistry.CanPlaceDormant(map, cell, null));
        GenSpawn.Spawn(actor, distant, map);
        var hold = JobMaker.MakeJob(JobDefOf.Wait, 100);
        Check(actor.Reserve(pawn, hold) && !Attempt(map), "A native reservation prevents return");
        map.reservationManager.Release(pawn, actor, hold);
        Find.TickManager.DebugSetTicksGame(Now + day);
        actor.mindState.enemyTarget = pawn;
        Check(!Attempt(map), "An active pursuit target prevents return outside observation range");
        actor.mindState.enemyTarget = null;
        Find.TickManager.DebugSetTicksGame(Now + day);
        var search = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GU_SearchSurface"), pawn.Position);
        actor.jobs.StartJob(search, JobCondition.InterruptForced);
        Check(actor.CurJob == search && !Attempt(map), "A live search covering the revealed encounter temporarily protects it");
        actor.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        Find.TickManager.DebugSetTicksGame(Now + day);
        registry.NoteSurfaceSearchResult(pawn);
        Check(!Attempt(map), "A completed search result renews the saved quiet interval");
        actor.DeSpawn();
        GenSpawn.Spawn(actor, oldPosition, home);

        Find.TickManager.DebugSetTicksGame(Now + day);
        var health = HediffMaker.MakeHediff(HediffDef.Named("Cut"), pawn, pawn.health.hediffSet.GetNotMissingParts().First(part => part.parent != null));
        pawn.health.AddHediff(health);
        Check(!Attempt(map), "A physical wound blocks abstraction after the quiet interval");
        pawn.health.RemoveHediff(health);
        pawn.SetFaction(Faction.OfPlayer);
        Check(!Attempt(map) && registry.Resolve(pawn) == record, "An actual native owner retains the exact individual");
        pawn.SetFaction(null);
        Check(record.EncounterProtected(Now + 59 * day, day, 60 * day) && !record.EncounterProtected(Now + 60 * day, day, 60 * day),
            "A completed ownership change extends retention and ultimately decays");
        Find.TickManager.DebugSetTicksGame(Now + 60 * day);
        int rarity = record.Species.wildIdentityRetentionDays;
        try
        {
            record.Species.wildIdentityRetentionDays = 120;
            Check(!Attempt(map), "A rare species can extend ordinary retention beyond the historical interval");
            record.Species.wildIdentityRetentionDays = -1;
            Check(!Attempt(map), "Indefinite species retention blocks return regardless of elapsed time");
        }
        finally { record.Species.wildIdentityRetentionDays = rarity; }
        var uniqueness = AccessTools.Field(typeof(GuRecord), "uniquenessKey");
        var rank = AccessTools.Field(typeof(GuRecord), "rank");
        try
        {
            uniqueness.SetValue(record, "fixture:unique");
            Check(!Attempt(map), "A unique individual retains its identity regardless of ordinary encounter age");
            uniqueness.SetValue(record, null);
            rank.SetValue(record, 6);
            Check(!Attempt(map) && record.EncounterProtected(Now, day, 60 * day),
                "Immortal ranks receive indefinite independent protection");
        }
        finally { uniqueness.SetValue(record, null); rank.SetValue(record, 1); }

        while (black.Cohort.TryRecruitMature(4, 0)) { }
        var fullRevision = black.Cohort.Revision;
        Check(!Attempt(map) && black.Cohort.Revision == fullRevision && black.Cohort.MatureCount == 4 && pawn.Spawned,
            "A full destination preserves the individual and every anonymous unit");
        Check(black.Cohort.TryConsumeMature(1), "Fixture restores a free destination slot");

        var fault = new Harmony("projectgu.verification.encounter-return");
        try
        {
            InstallReturnFaults(fault);
            returnProbe = pawn;
            afterReturnDetach = () =>
            {
                Check(!pawn.Spawned && registry.CustodyTransitionInProgress && !Attempt(map)
                    && !registry.TryCaptureWild(pawn, custody, actor, pawn.Position, out _, out _),
                    "A native callback cannot reenter return or capture during staged detachment");
                const string nestedSave = "gu-return-reentry";
                GameDataSaveLoader.SaveGame(nestedSave);
                Check(!File.Exists(GenFilePaths.FilePathForSavedGame(nestedSave)), "The native entry refuses to save an unowned staging state");
                string directSave = GenFilePaths.FilePathForSavedGame("gu-return-direct");
                bool wrote = false;
                SafeSaver.Save(directSave, "savegame", () => wrote = true, false);
                Check(!wrote && !File.Exists(directSave), "Direct SafeSaver entry aborts before any writer runs during staging");
                Check(black.Cohort.TryRecruitMature(4, 0), "Fixture changes capacity through the real population boundary");
            };
            long beforeFault = black.Cohort.Revision;
            Check(!Attempt(map) && registry.Resolve(pawn) == record && pawn.Map == map && black.Cohort.Revision == beforeFault + 1
                && black.Cohort.ReturnCredits == 0 && black.Cohort.TryConsumeMature(1),
                "A destination change rolls back native detachment without crediting or replacing the exact individual");
        }
        finally { fault.UnpatchAll(fault.Id); afterReturnDetach = null; returnProbe = null; }

        Find.TickManager.DebugSetTicksGame(Now + 2500);
        var before = black.Cohort.Preimage;
        Check(registry.TryReturnSurfaceResident(record.Id, custody, map, out var receipt) && record.Existence == GuExistence.Aggregated
            && pawn.Discarded && !Find.WorldPawns.Contains(pawn) && registry.ProjectionOf(record) == null
            && black.Cohort.MatureCount == before.Mature + 1 && black.Cohort.Revision == before.Revision + 1
            && black.Cohort.ReturnCredits == 1 && receipt.CohortAfter == black.Cohort.Revision,
            "Native return retires one projection and credits exactly one adult with a matching receipt");
        Check(Attempt(map) && !registry.TryReturnSurfaceResident(record.Id, custody + 1, map, out _) && black.Cohort.ReturnCredits == 1,
            "An exact receipt replays idempotently and a changed custody intent is refused");
        Check(whiteRecord.RenewEncounterReference("fixture:tracking", Now, Now + 3 * day), "Fixture installs an active saved tracking lease");
        File.WriteAllText(ReturnStatePath, JsonSerializer.Serialize(new ReturnState(tile.tileId, record.Id, custody,
            black.Cohort.Revision, black.Cohort.MatureCount, whiteRecord.Id)));
        AssertNoErrors("return new");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static (ReturnState State, Map Map, GuSurfaceEcology.Locality Row, GuRecord Record) CheckReturnSave(bool compacted)
    {
        var state = JsonSerializer.Deserialize<ReturnState>(File.ReadAllText(ReturnStatePath));
        var tile = Find.WorldGrid.Surface.PlanetTileForID(state.Tile);
        var map = Current.Game.FindMap(tile);
        var row = GuSurfaceEcology.Instance.LocalityAt(map, BlackSpecies);
        var record = IndividualRegistry.FindRecord(state.Id);
        Check(record is { Existence: GuExistence.Aggregated, IsQuarantined: false } && record.IsCompacted == compacted
            && IndividualRegistry.Resolve(state.Id) == null && IndividualRegistry.ProjectionOf(record) == null,
            "A fresh process restored only the exact return tombstone");
        Check(IndividualRegistry.TryReturnSurfaceResident(state.Id, state.Custody, map, out var replay)
            && replay.CohortAfter == state.Revision && row.Cohort.Revision == state.Revision && row.Cohort.MatureCount == state.Mature
            && row.Cohort.ReturnCredits == 1 && IndividualRegistry.SurfaceEvidenceIntact(replay.Site, row.Cohort),
            "Fresh-process replay validates the sealed one-adult credit and cannot pay it twice");
        return (state, map, row, record);
    }

    private static void VerifyEncounterReturnReload()
    {
        var saved = CheckReturnSave(compacted: false);
        var tracked = IndividualRegistry.Resolve(saved.State.ProtectedId);
        int day = GuSurfaceEcology.TicksPerDay;
        Check(tracked != null && tracked.EncounterProtected(Now + 2 * day, day, 60 * day),
            "A fresh process restores the active tracking lease");
        var digest = AccessTools.Field(typeof(GuPopulationRecord), "returnDigest");
        long expected = saved.Row.Cohort.ReturnDigest;
        try
        {
            digest.SetValue(saved.Row.Cohort, expected ^ 1L);
            IndividualRegistry.CompactRecords(Now + GuIndividualRegistry.RetentionTicks, IndividualRegistry.Records.Count);
            Check(!saved.Record.IsCompacted, "Compaction preserves the detailed record while its cohort credit cannot be verified");
        }
        finally { digest.SetValue(saved.Row.Cohort, expected); }
        IndividualRegistry.CompactRecords(Now + GuIndividualRegistry.RetentionTicks, IndividualRegistry.Records.Count);
        Check(saved.Record.IsCompacted && IndividualRegistry.TryReturnSurfaceResident(saved.State.Id, saved.State.Custody, saved.Map, out _),
            "Compaction preserves the old ID, replay and destination credit");
        AssertNoErrors("return reload");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifyCompactedReturn()
    {
        var saved = CheckReturnSave(compacted: true);
        var ecology = GuSurfaceEcology.Instance;
        var registry = IndividualRegistry;
        var white = ecology.LocalityAt(saved.Map, WhiteSpecies);
        var competing = (Pawn_GuWildProjection)registry.ProjectionOf(registry.Resolve(saved.State.ProtectedId));
        Find.TickManager.DebugSetTicksGame(Now + 3 * GuSurfaceEcology.TicksPerDay);
        Check(!registry.Resolve(competing).EncounterProtected(Now, GuSurfaceEcology.TicksPerDay, 60 * GuSurfaceEcology.TicksPerDay),
            "A tracking lease expires on its original deadline through two fresh processes");
        var fault = new Harmony("projectgu.verification.encounter-return");
        try
        {
            InstallReturnFaults(fault);
            returnProbe = competing;
            refuseReturnDiscard = true;
            var whiteRecord = registry.Resolve(competing);
            long whiteRevision = white.Cohort.Revision;
            Check(registry.TryReturnSurfaceResident(whiteRecord.Id, whiteRecord.CustodyRevision, saved.Map, out _)
                && whiteRecord.Existence == GuExistence.Aggregated && competing.ReleaseSuppressed && !competing.Spawned
                && !Find.WorldPawns.Contains(competing) && white.Cohort.Revision == whiteRevision + 1,
                "Failed post-commit native discard suppresses the obsolete Pawn without undoing the single credit");
        }
        finally { fault.UnpatchAll(fault.Id); refuseReturnDiscard = false; returnProbe = null; }

        EncounterSample(ecology, Now + 1);
        var replacement = SurfacePawn(saved.Map, BlackSpecies);
        var next = registry.Resolve(replacement);
        Check(next.Id != saved.State.Id && saved.Row.Cohort.MatureCount == saved.State.Mature - 1
            && saved.Row.Cohort.Revision == saved.State.Revision + 1 && saved.Row.Cohort.ReturnCredits == 1,
            "Later native manifestation spends one adult and receives a new canonical ID");

        var home = Find.CurrentMap;
        var actor = home.mapPawns.FreeColonistsSpawned.First();
        var oldPosition = actor.Position;
        actor.DeSpawn();
        var captureCell = GenAdj.CardinalDirections.Select(offset => replacement.Position + offset)
            .First(cell => GuIndividualRegistry.CanPlaceDormant(saved.Map, cell, null));
        GenSpawn.Spawn(actor, captureCell, saved.Map);
        int capturedRevision = next.CustodyRevision;
        Check(registry.TryCaptureWild(replacement, capturedRevision, actor, GuIndividualRegistry.CaptureDestination(replacement, actor),
                out var dormant, out _) && registry.Resolve(dormant) == next && next.Condition == GuCondition.Dormant
            && !registry.TryReturnSurfaceResident(next.Id, capturedRevision, saved.Map, out _),
            "Successful native capture retains the exact ID and invalidates any return of the former binding");
        var aperture = actor.health.hediffSet.hediffs.OfType<Hediff_Aperture>().FirstOrDefault() ?? AddProbeAperture(actor);
        if (!actor.Position.AdjacentToCardinal(dormant.Position))
        {
            var pickupCell = GenAdj.CardinalDirections.Select(offset => dormant.Position + offset)
                .First(cell => GuIndividualRegistry.CanPlaceDormant(saved.Map, cell, null));
            actor.DeSpawn();
            GenSpawn.Spawn(actor, pickupCell, saved.Map);
        }
        Check(registry.TryTakeDormant(dormant, next.CustodyRevision, actor, out var pickupReason),
            $"The formerly wild resident enters legitimate native inventory. {pickupReason}");
        BindFixtureRefinement(next, actor);
        Check(registry.TryStoreDormant(dormant, next.CustodyRevision, actor, aperture, out var storeReason),
            $"The same Gu enters legitimate aperture custody. {storeReason}");
        Check(!registry.TryReturnSurfaceResident(next.Id, next.CustodyRevision, saved.Map, out _),
            "Aperture custody cannot return to anonymous stock");
        next.ReleaseRefinement(); // Fixture stands in for the end of allegiance before unowned release.
        EnsureInventoryFixtureSpace(actor);
        var releaseCell = OpenInventoryCell(actor);
        Check(registry.TryReleaseActive(next.Id, next.CustodyRevision, actor, aperture, saved.Map, releaseCell,
                out var freed, out var releaseReason) && registry.Resolve(freed) == next,
            $"Native release keeps the formerly captured individual. {releaseReason}");
        Check(ecology.TryIntroduceExisting(next.Id, next.CustodyRevision, saved.Map),
            "The released individual has explicit membership in the legitimate habitat");
        actor.DeSpawn();
        GenSpawn.Spawn(actor, oldPosition, home);
        registry.ApplyPendingEvents();

        int day = GuSurfaceEcology.TicksPerDay;
        Find.TickManager.DebugSetTicksGame(Now + 59 * day);
        Check(!registry.TryReturnSurfaceResident(next.Id, next.CustodyRevision, saved.Map, out _) && freed.Spawned,
            "Historical significance still protects a formerly captured and released resident at 59 days");
        Find.TickManager.DebugSetTicksGame(Now + day);
        Check(!next.EncounterProtected(Now, day, 60 * day) && ecology.TryPrepareReturn(next, saved.Map, out _, out _),
            "The saved history has expired and the habitat can accept exactly one adult");
        var releasedStock = saved.Row.Cohort.MatureCount;
        Check(registry.TryReturnSurfaceResident(next.Id, next.CustodyRevision, saved.Map, out _)
            && next.Existence == GuExistence.Aggregated && saved.Row.Cohort.MatureCount == releasedStock + 1,
            "A previously captured common Gu returns once its historical protection expires");

        EncounterSample(ecology, Now + GuSurfaceEcology.TicksPerDay);
        var victim = SurfacePawn(saved.Map, WhiteSpecies);
        var victimRecord = registry.Resolve(victim);
        int custody = victimRecord.CustodyRevision;
        long revision = white.Cohort.Revision;
        Find.TickManager.DebugSetTicksGame(Now + day);
        var deathCell = victim.Position;
        try
        {
            InstallReturnFaults(fault);
            returnProbe = victim;
            afterReturnDetach = () =>
            {
                GenSpawn.Spawn(victim, deathCell, saved.Map);
                victim.Kill(null);
                Check(registry.PendingEventCount != 0, "A real native death is queued during return staging");
            };
            Check(!registry.TryReturnSurfaceResident(victimRecord.Id, custody, saved.Map, out _),
                "Native death during detachment prevents the population credit");
        }
        finally { fault.UnpatchAll(fault.Id); afterReturnDetach = null; returnProbe = null; }
        Check(victimRecord.Existence == GuExistence.Terminal && victimRecord.TerminalOutcome == GuTerminalOutcome.Died
            && white.Cohort.Revision == revision && white.Cohort.ReturnCredits == 1, "Death wins and mints no adult");
        AssertNoErrors("return compacted");
    }
}
