using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool CultivatorLifecycle => GenCommandLine.CommandLineArgPassed("gucultivatorlifecycle");
    private static string CultivatorFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "cultivator-fixture.json");
    private static string CultivatorTracePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "cultivator-callbacks.txt");
    private static Pawn discardInjectionPawn;
    private static Action discardInjection;

    private record FactSnapshot(string Key, string Cause, int Tick, string Life);
    private record IdentitySnapshot(string Id, string PawnToken, int AssociationRevision, string Association, string Life,
        int ObservationTick, FactSnapshot Body, FactSnapshot Corpseless, FactSnapshot Discard, string[] Apertures);
    private record CultivatorFixture(Dictionary<string, IdentitySnapshot> Records, string DraftPawn, int DraftAptitude,
        string DraftId, int SpringId, int Hope, string KeptGu, int KeptRevision,
        string HeldGu, string HeldThing, int HeldRevision, string BeforeHash);

    private static IdentitySnapshot Snapshot(GuCultivatorRecord record) => new(record.CultivatorId, record.PawnLoadId,
        record.AssociationRevision, record.NativeAssociation.ToString(), record.LastObservedLife.ToString(), record.ObservationTick,
        Snapshot(record.LastCorpseDestruction), Snapshot(record.LastProvenCorpselessDeath),
        Snapshot(record.ConfirmedDiscard), record.ApertureIds.ToArray());

    private static FactSnapshot Snapshot(GuCultivatorFact fact) =>
        fact == null ? null : new FactSnapshot(fact.EventKey, fact.Cause, fact.Tick, fact.Life.ToString());

    private static void TraceCultivators(string message) =>
        File.AppendAllText(CultivatorTracePath,
            $"tick={Find.TickManager.TicksGame} {message}{Environment.NewLine}");

    private static void ExerciseCultivatorLifecycle()
    {
        Cultivation.CultivatorLifecycleTrace = TraceCultivators;
        Check(VersionControl.CurrentVersionStringWithRev == "1.7.5014 rev1195" && GuLifecyclePatches.Installed,
            "Pinned build and lifecycle patches are active");
        Map map = Find.CurrentMap;
        Pawn colonist = map.mapPawns.FreeColonistsSpawned.First();
        var pawns = new Dictionary<string, Pawn>();
        var records = new Dictionary<string, GuCultivatorRecord>();
        Pawn Track(string key, bool player = false, bool world = false)
        {
            Pawn pawn = player ? ExtraColonist(map, colonist.Position)
                : PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, tile: map.Tile,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true,
                    forceNoGear: true, fixedBiologicalAge: 25, developmentalStages: DevelopmentalStage.Adult));
            if (world) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            pawns.Add(key, pawn);
            records.Add(key, Cultivation.GetOrCreate(pawn));
            Check(records[key]?.NativeAssociation == GuNativeAssociation.Bound, $"Initial binding for {key}");
            return pawn;
        }

        var slain = Track("corpse", player: true);
        records["corpse"].LatentAptitude = 63;
        Check(Cultivation.TryAwaken(slain, out string awake), $"Cultivator aperture initialized. {awake}");
        var kept = StoreFixtureGu(slain, slain.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single());
        int keptRevision = kept.CustodyRevision;
        var destroyedBody = Track("body", player: true);
        var worldDead = Track("corpse-less", world: true);
        var worldDestroyed = Track("world-destroy", world: true);
        var explicitPawn = Track("explicit");
        var outer = Track("nested-gc", world: true);
        var inner = Track("nested-native");
        var interrupted = Track("interrupted", world: true);
        var afterError = Track("after-error");
        var missing = Track("missing", player: true);
        var deadGc = Track("dead-gc", world: true);
        var traveller = Track("off-map", player: true);
        var collectible = Track("natural-gc");
        var keeper = Track("keeper");
        var held = SpawnDormant(Species("GU_DevTestSpringGu"), map, EmptyCargoCell(map, colonist.Position, null, 5));
        var heldGu = IndividualRegistry.Resolve(held);
        held.DeSpawn();
        Check(keeper.inventory.GetDirectlyHeldThings().TryAdd(held, false), "Neutral candidate holds exact physical individual-policy Gu");
        Find.WorldPawns.PassToWorld(keeper);
        Find.WorldPawns.PassToWorld(collectible);

        var spring = SpawnAwakeningSpring(map, EmptyCargoCell(map, colonist.Position, null, 5));
        var draftPawn = ExtraColonist(map, colonist.Position);
        Check(Cultivation.TryBeginAwakening(), "Prepare unpublished aptitude fixture");
        var draft = Cultivation.PrepareAwakeningRecord(draftPawn);
        draft.LatentAptitude = 73;
        Cultivation.EndAwakening();
        int hope = spring.HopeGuAvailable;
        GameDataSaveLoader.SaveGame("before-cultivator-events");
        string before = GenFilePaths.FilePathForSavedGame("before-cultivator-events");
        string backup = Path.Combine(GenFilePaths.SaveDataFolderPath, "before-cultivator-events.backup.rws");
        File.Copy(before, backup);
        string hash = Sha(before);
        Check(Sha(backup) == hash, $"Destructive fixture source and its backup match SHA-256 {hash}");

        slain.Kill(null);
        Check(slain.Dead && slain.Corpse != null && records["corpse"].CurrentLife == GuObservedLife.Dead,
            "Native death leaves a dead bound cultivator with a corpse");
        destroyedBody.Kill(null);
        destroyedBody.Corpse.Destroy();
        worldDead.Kill(null);
        deadGc.Kill(null);
        Check(worldDead.Dead && worldDead.Corpse == null && Find.WorldPawns.Contains(worldDead), "Native world death produced no corpse");
        worldDestroyed.Destroy(DestroyMode.Vanish);
        Check(worldDestroyed.Destroyed && !worldDestroyed.Discarded && Find.WorldPawns.Contains(worldDestroyed),
            "Nonlethal native destruction kept the world-owned Pawn");
        missing.DeSpawn();
        Check(!missing.Dead && !missing.Discarded && !GuWorldComponent.HasNativeSaveOwner(missing),
            "Missing-reference control lost its native holder without dying or being discarded");
        traveller.DeSpawn();
        Find.WorldPawns.PassToWorld(traveller, PawnDiscardDecideMode.KeepForever);
        Check(Find.WorldPawns.Contains(traveller) && ReferenceEquals(Cultivation.GetOrCreate(traveller), records["off-map"]),
            "Map-to-world movement retains the same native association");
        Find.WorldPawns.PassToWorld(explicitPawn, PawnDiscardDecideMode.Discard);

        var nativeGc = AccessTools.DeclaredMethod(typeof(WorldPawns), "RemoveAndDiscardPawnViaGC", new[] { typeof(Pawn) });
        var injection = new Harmony("projectgu.verification.cultivator-discard");
        injection.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.Discard)),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(InjectDiscard)) { priority = Priority.Last });
        try
        {
            discardInjectionPawn = outer;
            discardInjection = () =>
            {
                Check(!Cultivation.CanSaveCultivators, "Identity guard sees the active discard scope");
                GameDataSaveLoader.SaveGame("unsafe-active-discard");
                Check(!File.Exists(GenFilePaths.FilePathForSavedGame("unsafe-active-discard")), "Active teardown save was rejected");
                Find.WorldPawns.PassToWorld(inner, PawnDiscardDecideMode.Discard);
            };
            nativeGc.Invoke(Find.WorldPawns, [outer]);
            nativeGc.Invoke(Find.WorldPawns, [deadGc]);
            discardInjectionPawn = interrupted;
            discardInjection = () => throw new InvalidOperationException("Injected native discard interruption");
            try
            {
                nativeGc.Invoke(Find.WorldPawns, [interrupted]);
                throw new Exception("Expected native discard failure");
            }
            catch (TargetInvocationException error) when (error.InnerException?.Message == "Injected native discard interruption")
            {
                Check(Cultivation.CanSaveCultivators && !interrupted.Discarded, "GC scope cleaned up and propagated the original exception");
            }
            Find.WorldPawns.PassToWorld(afterError, PawnDiscardDecideMode.Discard);
        }
        finally { discardInjection = null; discardInjectionPawn = null; }

        var reason = AccessTools.DeclaredMethod(typeof(WorldPawnGC), "GetCriticalPawnReason");
        Check((string)reason.Invoke(Find.WorldPawns.gc, [keeper]) == "ProjectGuCustody"
            && reason.Invoke(Find.WorldPawns.gc, [collectible]) == null, "Existing retention distinguishes a protected holder from an incidental cultivator");
        Find.WorldPawns.gc.RunGC();
        Check(!keeper.Discarded && collectible.Discarded && held.holdingOwner == keeper.inventory.GetDirectlyHeldThings(),
            "Actual GC keeps the exact protected Gu and discards an incidental identity");

        new Harmony("projectgu.verification.cultivator-awakening").Patch(AccessTools.Method(typeof(Hediff), nameof(Hediff.PostAdd)),
            postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterTestHediffAdded)));
        try
        {
            OnApertureAdded = () => throw new InvalidOperationException("Injected unpublished aptitude rollback");
            Check(!GuAwakeningService.TryAwakenAtSpring(draftPawn, spring, out _)
                && spring.HopeGuAvailable == hope && !draftPawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Any()
                && Cultivation.FindCultivator(draft.Id) == null, "Native PostAdd failure rolls back the unpublished awakening");
        }
        finally { OnApertureAdded = null; }

        int tick = Find.TickManager.TicksGame;
        Check(Cultivation.PendingCultivatorObservations > 0 && records["explicit"].ConfirmedDiscard == null,
            "Native observations are pending before the same-tick save");
        GameDataSaveLoader.SaveGame(SaveName);
        Check(Find.TickManager.TicksGame == tick && Cultivation.PendingCultivatorObservations == 0
            && records["explicit"].ConfirmedDiscard?.Cause == "NativeDiscard", "Save applies queued observations before the next tick");
        Check(records["body"].LastCorpseDestruction != null && records["body"].LastProvenCorpselessDeath == null
            && records["corpse-less"].LastProvenCorpselessDeath != null && records["corpse-less"].LastCorpseDestruction == null,
            "Body destruction and proven corpse-less death produce separate facts");
        Check(records["nested-gc"].ConfirmedDiscard?.Cause == "WorldPawnGC"
            && records["nested-native"].ConfirmedDiscard?.Cause == "NativeDiscard"
            && records["after-error"].ConfirmedDiscard?.Cause == "NativeDiscard"
            && records["natural-gc"].ConfirmedDiscard?.Cause == "WorldPawnGC"
            && records["dead-gc"].ConfirmedDiscard is { Cause: "WorldPawnGC", Life: GuObservedLife.Dead },
            "Exact correlation across nested native callbacks, error cleanup, actual GC and dead discard");
        Check(records["interrupted"].NativeAssociation == GuNativeAssociation.Unresolved
            && records["interrupted"].ConfirmedDiscard == null && records["missing"].ConfirmedDiscard == null,
            "Interrupted GC and missing references create no confirmed discard");
        string[] events = File.ReadAllLines(CultivatorTracePath);
        foreach (var (name, record) in records)
        {
            int discardCount = events.Count(line => line.Contains($" {record.Id} commit discard "));
            Check(discardCount == (record.ConfirmedDiscard == null ? 0 : 1), $"Exactly one trace commit per confirmed discard for {name}");
        }
        var outerEvents = events.Where(line => line.Contains($" {records["nested-gc"].Id} ")).ToList();
        int position = -1;
        foreach (string transition in new[] { "enter Gc ", "enter PawnDestroy ", "exit PawnDestroy ", "enter Discard ",
                     "PostCorpseDestroy discarded=True", "exit Discard ", "exit Gc ", "commit discard " })
        {
            position = outerEvents.FindIndex(position + 1, line => line.Contains(transition));
            Check(position >= 0, $"Native GC observation orders {transition.Trim()}");
        }
        Check(kept.Custody.Kind == GuCustodyKind.DeceasedAperture && kept.CustodyRevision == keptRevision
            && heldGu.IsActionable && Sha(before) == hash && Sha(backup) == hash, "Phase 2 custody and verified fixture source stay intact");

        var snapshots = records.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
        var xml = XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName));
        CheckInterruptedGcEvidence(xml, records["interrupted"].Id);
        foreach (string name in new[] { "missing", "body" })
        {
            var saved = xml.Descendants("guCultivatorRecordsV1").Single().Elements("li")
                .Single(row => (string)row.Element("id") == snapshots[name].Id);
            // Scribe omits the default enum member.
            if (((string)saved.Element("association") ?? "Unresolved") == "Unresolved")
                snapshots[name] = snapshots[name] with { Association = "Unresolved" };
        }
        Check(snapshots["missing"].Association == "Unresolved" && snapshots["missing"].Life == "Alive",
            "The unowned live Pawn produces an unresolved reference with historical life evidence");
        Check(xml.Descendants("guAwakeningAptitudeDraftsV1").Single().Elements("li")
            .Any(draftRow => (string)draftRow.Element("pawnLoadId") == draftPawn.GetUniqueLoadID()
                && (int?)draftRow.Element("aptitude") == 73 && draftRow.Element("id") == null),
            "Only the unpublished aptitude and exact native reference are saved");
        File.WriteAllText(CultivatorFixturePath, JsonSerializer.Serialize(new CultivatorFixture(snapshots,
            draftPawn.GetUniqueLoadID(), 73, draft.Id, spring.parent.thingIDNumber, hope, kept.Id, keptRevision,
            heldGu.Id, held.GetUniqueLoadID(), heldGu.CustodyRevision, hash)));
        Report("Cultivator event checkpoint saved with original Gu custody and confirmed lifecycle evidence.");
    }

    private static void InjectDiscard(Pawn __instance)
    {
        if (!ReferenceEquals(__instance, discardInjectionPawn)) return;
        var action = discardInjection;
        discardInjection = null;
        action?.Invoke();
    }

    private static void VerifyCultivatorLifecycleReload()
    {
        var fixture = ReadCultivatorFixture();
        CheckCultivatorSnapshots(fixture);
        var slain = Cultivation.FindCultivator(fixture.Records["corpse"].Id);
        Check(slain.Pawn is { Dead: true, Corpse: not null } && slain.CurrentLife == GuObservedLife.Dead,
            "Fresh process resolved the corpse-owned dead Pawn");
        var worldDead = Cultivation.FindCultivator(fixture.Records["corpse-less"].Id);
        Check(worldDead.Pawn is { Dead: true, Corpse: null } && Find.WorldPawns.Contains(worldDead.Pawn),
            "Fresh process resolved the native world-owned dead Pawn");
        var missing = Cultivation.FindCultivator(fixture.Records["missing"].Id);
        Check(missing.Pawn == null && missing.CurrentLife == GuObservedLife.Unknown && missing.LastObservedLife == GuObservedLife.Alive
            && missing.ConfirmedDiscard == null, "Missing reference preserves its historical observation and no native lifecycle fact");
        var traveller = Cultivation.FindCultivator(fixture.Records["off-map"].Id);
        Check(traveller.Pawn != null && Find.WorldPawns.Contains(traveller.Pawn),
            "Fresh process restores the retained world Pawn");
        Map home = Find.CurrentMap;
        GenSpawn.Spawn(traveller.Pawn, EmptyCargoCell(home, home.mapPawns.FreeColonistsSpawned.First().Position, null, 5), home);
        Check(traveller.Pawn.Spawned && !Find.WorldPawns.Contains(traveller.Pawn)
            && ReferenceEquals(Cultivation.GetOrCreate(traveller.Pawn), traveller),
            "Native world-to-map movement retains the same cultivator identity");
        var kept = IndividualRegistry.Resolve(fixture.KeptGu);
        Check(kept?.Custody.Kind == GuCustodyKind.DeceasedAperture && kept.CustodyRevision == fixture.KeptRevision
            && ReferenceEquals(IndividualRegistry.ResolveApertureOccupant(slain.Pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single()), kept),
            "Deceased Gu custody and revision persisted in the fresh process");
        Check(ResurrectionUtility.TryResurrect(slain.Pawn) && slain.CurrentLife == GuObservedLife.Alive
            && slain.Id == fixture.Records["corpse"].Id && slain.LastCorpseDestruction == null
            && kept.CustodyRevision == fixture.KeptRevision, "Ordinary resurrection keeps identity, surviving aperture and exact Gu");

        var draftPawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.DraftPawn);
        Check(Cultivation.TryBeginAwakening(), "Open reloaded draft for verification");
        var draft = Cultivation.PrepareAwakeningRecord(draftPawn);
        Cultivation.EndAwakening();
        Check(draft.LatentAptitude == fixture.DraftAptitude && draft.Id != fixture.DraftId
            && Cultivation.FindCultivator(draft.Id) == null, "Fresh process restores aptitude without publishing or reusing a provisional ID");
        var spring = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring"))
            .Single(thing => thing.thingIDNumber == fixture.SpringId).TryGetComp<CompGuSpiritSpring>();
        Check(spring.HopeGuAvailable == fixture.Hope && GuAwakeningService.TryAwakenAtSpring(draftPawn, spring, out _)
            && spring.HopeGuAvailable == fixture.Hope - 1 && Cultivation.GetOrCreate(draftPawn).Id == draft.Id
            && !GuAwakeningService.TryAwakenAtSpring(draftPawn, spring, out _) && spring.HopeGuAvailable == fixture.Hope - 1,
            "Reloaded draft awakens with one identity, one debit and no duplicate commit");
        GameDataSaveLoader.SaveGame(SaveName);
        foreach (string key in fixture.Records.Keys.ToArray())
            fixture.Records[key] = Snapshot(Cultivation.FindCultivator(fixture.Records[key].Id));
        fixture.Records["draftCommitted"] = Snapshot(draft);
        File.WriteAllText(CultivatorFixturePath, JsonSerializer.Serialize(fixture));
    }

    private static void VerifyCultivatorLifecycleSecondReload()
    {
        var fixture = ReadCultivatorFixture();
        CheckCultivatorSnapshots(fixture);
        var resurrected = Cultivation.FindCultivator(fixture.Records["corpse"].Id);
        var committed = Cultivation.FindCultivator(fixture.Records["draftCommitted"].Id);
        var kept = IndividualRegistry.Resolve(fixture.KeptGu);
        Check(resurrected.CurrentLife == GuObservedLife.Alive && resurrected.LastCorpseDestruction == null
            && kept?.CustodyRevision == fixture.KeptRevision
            && ReferenceEquals(IndividualRegistry.ResolveApertureOccupant(resurrected.Pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single()), kept),
            "Second fresh process retains revived identity and exact Gu revision");
        var spring = Find.CurrentMap.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring"))
            .Single(thing => thing.thingIDNumber == fixture.SpringId).TryGetComp<CompGuSpiritSpring>();
        Check(committed.LatentAptitude == fixture.DraftAptitude && committed.State == AwakeningState.Awakened
            && committed.ApertureIds.Count == 1 && spring.HopeGuAvailable == fixture.Hope - 1,
            "Second fresh process retains the completed awakening and single debit");
    }

    private static void VerifyCultivatorInvalidSave()
    {
        Check(!Cultivation.CanSaveCultivators, "An incomplete committed envelope closes native save authority");
        string checkpoint = GenFilePaths.FilePathForSavedGame(SaveName);
        string originalHash = Sha(checkpoint);
        GameDataSaveLoader.SaveGame("unsafe-invalid-cultivators");
        GameDataSaveLoader.SaveGame(SaveName);
        Check(!File.Exists(GenFilePaths.FilePathForSavedGame("unsafe-invalid-cultivators")),
            "Native save entry point cannot write over incomplete cultivator evidence");
        Check(Sha(checkpoint) == originalHash, "The unsafe overwrite attempt preserves the checkpoint bytes");
    }

    private static void CheckCultivatorSnapshots(CultivatorFixture fixture)
    {
        CheckInterruptedGcEvidence(XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName)), fixture.Records["interrupted"].Id);
        foreach (var (name, expected) in fixture.Records)
        {
            var record = Cultivation.FindCultivator(expected.Id);
            Check(record != null && JsonSerializer.Serialize(Snapshot(record)) == JsonSerializer.Serialize(expected),
                $"Durable cultivator evidence matches {name}: expected={JsonSerializer.Serialize(expected)} actual={(record == null ? "missing" : JsonSerializer.Serialize(Snapshot(record)))}");
        }
        var keeper = Cultivation.FindCultivator(fixture.Records["keeper"].Id);
        var held = keeper.Pawn.inventory.GetDirectlyHeldThings().OfType<Thing_GuDormantProjection>()
            .SingleOrDefault(thing => thing.GetUniqueLoadID() == fixture.HeldThing);
        Check(held != null && IndividualRegistry.Resolve(held) is { } gu
            && gu.Id == fixture.HeldGu && gu.CustodyRevision == fixture.HeldRevision,
            "Fresh process restored the exact retained native Gu Thing, holder, and custody revision");
        Check(Sha(GenFilePaths.FilePathForSavedGame("before-cultivator-events")) == fixture.BeforeHash
            && Sha(Path.Combine(GenFilePaths.SaveDataFolderPath, "before-cultivator-events.backup.rws")) == fixture.BeforeHash,
            "Fresh process verified original and backup bytes");
    }

    private static void CheckInterruptedGcEvidence(XDocument xml, string id)
    {
        var rows = xml.Descendants("guCultivatorIntegrityV1").Single().Elements("li")
            .Where(row => ((string)row.Element("key"))?.StartsWith($"interrupted-gc:{id}:", StringComparison.Ordinal) == true);
        Check(rows.SingleOrDefault()?.Element("rawEvidence")?.Value.Contains("previousAssociation=Bound") == true,
            "Scribe contains exactly one interruption evidence row with the saved prior association");
    }

    private static CultivatorFixture ReadCultivatorFixture() =>
        JsonSerializer.Deserialize<CultivatorFixture>(File.ReadAllText(CultivatorFixturePath));

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
