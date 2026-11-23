using System.Reflection;
using System.Xml;
using ProjectGu;
using ProjectGu.Domain;
using Verse;

internal static class CultivatorFoundationChecks
{
    private static int passed;
    private static readonly ThingDef Human = new() { defName = "Human", race = new RaceProperties { intelligence = Intelligence.Humanlike } };
    private static readonly HediffDef ApertureDef = new() { defName = "GU_Aperture" };

    internal static void Run(Game game, string directory)
    {
        CheckSchemas();
        CheckEarlyScribe(game, directory);
        CheckLegacyOutcomes();
        CheckApertureConflict();
        CheckApertureTransaction();
        CheckCommittedRoundTrip(directory);
        CheckAuthorityAndAwakening(game, directory);
        Console.WriteLine($"PASS: {passed} cultivator foundation checks (managed; no RimWorld process).");
    }

    private static void CheckSchemas()
    {
        const string schema = "<guCultivatorSchemaVersion>1</guCultivatorSchemaVersion>";
        const string complete = "<guCultivatorMigrationComplete>True</guCultivatorMigrationComplete>";
        const string lists = "<guCultivatorRecordsV1/><guCultivatorIntegrityV1/>";
        Check(Kind("") == GuCultivatorLoadKind.Legacy && Kind("<guPawnRecords/>") == GuCultivatorLoadKind.Legacy,
            "Absent new authority permits legacy import.");
        Check(Kind(schema + complete + lists) == GuCultivatorLoadKind.Committed, "Committed empty store remains current.");
        Check(Kind(schema + complete + lists.Replace("<guCultivatorRecordsV1/>", "<guCultivatorRecordsV1><li/></guCultivatorRecordsV1>"))
            == GuCultivatorLoadKind.Committed, "Committed nonempty envelope is recognized for row validation.");
        Check(Kind(schema + lists) == GuCultivatorLoadKind.Invalid
            && Kind(schema + complete.Replace("True", "False") + lists) == GuCultivatorLoadKind.Invalid,
            "Incomplete migration cannot authorize a save.");
        Check(Kind(schema.Replace(">1<", ">2<") + complete + lists) == GuCultivatorLoadKind.Invalid
            && Kind(schema + complete + lists + "<guPawnRecords/>") == GuCultivatorLoadKind.Invalid
            && Kind(schema + schema + complete + lists) == GuCultivatorLoadKind.Invalid,
            "Unsupported schemas and conflicting authorities fail closed.");
        Check(Kind(schema + complete + lists.Replace("<guCultivatorRecordsV1/>", "<guCultivatorRecordsV1 IsNull=\"True\"/>"))
            == GuCultivatorLoadKind.Invalid, "A null list is different from a valid empty list.");
    }

    private static void CheckEarlyScribe(Game game, string directory)
    {
        string path = Path.Combine(directory, "cultivator-legacy-managed.xml");
        File.WriteAllText(path, """
            <verification><component><guPawnRecords>
              <li><pawn>null</pawn><latentAptitude>invalid</latentAptitude><awakeningState>9</awakeningState></li>
              <li><pawn>Thing_Human4242</pawn><latentAptitude>47</latentAptitude></li>
              <li IsNull="True" />
            </guPawnRecords></component></verification>
            """);
        try
        {
            GuWorldComponent world = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref world, "component", game);
            var original = world.PendingLegacyRows;
            Check(original.Rows.Length == 3 && original.Rows[0].RawXml.Contains("<latentAptitude>invalid</latentAptitude>"),
                "LoadingVars captures complete legacy rows before field conversion.");
            Scribe.loader.FinalizeLoading();
            var after = world.PendingLegacyRows;
            Check(ReferenceEquals(after, original) && after.Rows.Length == 3
                && after.Rows[0].PawnToken == "null" && after.Rows[1].PawnToken == "Thing_Human4242"
                && after.Rows[2].RawXml.Contains("IsNull"),
                "Actual managed Scribe cross-reference and PostLoadInit phases keep all early legacy evidence.");
            world.LoadedGame();
            Check(!world.CanSaveCultivators && world.PendingLegacyRows.SourceXml == original.SourceXml,
                "LoadedGame without a successful registry handoff retains input and blocks saving.");
            var originalComponents = game.components;
            var brokenRegistry = new GuIndividualRegistry(game);
            game.components = [brokenRegistry, world];
            try
            {
                typeof(GuIndividualRegistry).GetField("records", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(brokenRegistry, null);
                try
                {
                    brokenRegistry.LoadedGame();
                    throw new InvalidOperationException("Expected the synthetic upstream registry failure.");
                }
                catch (NullReferenceException) { }
                Check(!world.CanSaveCultivators && ReferenceEquals(world.PendingLegacyRows, original),
                    "An exception before the upstream success handoff leaves the cultivator source intact.");
                Scribe.mode = LoadSaveMode.Saving;
                try
                {
                    ExpectFailure(world.ExposeData);
                    ExpectFailure(brokenRegistry.ExposeData);
                }
                finally { Scribe.mode = LoadSaveMode.Inactive; }
                Check(!world.CanSaveCultivators, "Both Scribe writers reject a save after upstream load failure.");
            }
            finally { game.components = originalComponents; }
        }
        finally { File.Delete(path); }
    }

    private static void CheckLegacyOutcomes()
    {
        var live = PawnAt(101);
        var duplicated = PawnAt(102);
        string liveId = live.GetUniqueLoadID(), repeatedId = duplicated.GetUniqueLoadID();
        var ancestorGu = Anchored("Thing_Human999", Guid.NewGuid().ToString("N"));
        var source = Rows($"<li><pawn>{liveId}</pawn><latentAptitude>67</latentAptitude><awakeningState>1</awakeningState><legacyMigrated>True</legacyMigrated></li>"
            + "<li><pawn>Thing_Human999</pawn><latentAptitude>45</latentAptitude></li>"
            + $"<li><pawn>{repeatedId}</pawn><latentAptitude>30</latentAptitude></li>"
            + $"<li><pawn>{repeatedId}</pawn><latentAptitude>61</latentAptitude></li>"
            + "<li><pawn>null</pawn><latentAptitude>broken</latentAptitude></li>"
            + "<li><latentAptitude>82</latentAptitude><unrecognized>evidence</unrecognized></li>"
            + "<li><pawn>Thing_Human888</pawn><latentAptitude>40</latentAptitude></li>");
        var plan = GuCultivatorPlan.PrepareLegacy(source, [Resolved(source.Rows[0], live), Resolved(source.Rows[2], duplicated),
            Resolved(source.Rows[3], duplicated)], [live, duplicated], [], [ancestorGu]);
        Check(plan.Records.Count == 2 && plan.ByPawn[live].LatentAptitude == 67
            && plan.ByPawn[live].State == AwakeningState.Failed && plan.ByPawn[live].LegacyMigrated,
            "A unique resolved source preserves valid aptitude and awakening fields.");
        var historical = plan.Records.Single(record => record.PawnLoadId == "Thing_Human999");
        Check(historical.Association == GuNativeAssociation.Unresolved && historical.Pawn == null
            && historical.ApertureIds.Count == 0 && historical.LifeUnknown()
            && historical.Significant, "Exact raw token supports history without native ownership or inferred death.");
        Check(plan.Evidence.Count == 6 && plan.Evidence.Select(item => item.SourceOrdinal)
            .SequenceEqual(Enumerable.Range(1, 6)) && plan.Evidence.All(entry => entry.RawEvidence == source.Rows[entry.SourceOrdinal].RawXml),
            "Every remaining legacy row retains its exact raw evidence.");
        Check(!plan.ByPawn.ContainsKey(duplicated) && plan.PawnClaims.Contains(repeatedId)
            && plan.PawnClaims.Contains("Thing_Human888"), "Conflicted and unmatched tokens block replacement identity creation.");
        Check(source.Rows.Length == 7 && ancestorGu.CustodyRevision == 2 && ancestorGu.AnchorHint == "Thing_Human999",
            "Planning keeps the input row count and exact existing Gu ownership and custody revision.");
        var contradictoryGu = Anchored("Thing_Human1234", ancestorGu.ContainerId);
        var ambiguous = GuCultivatorPlan.PrepareLegacy(
            Rows("<li><pawn>Thing_Human999</pawn></li>"), [], [], [], [ancestorGu, contradictoryGu]);
        Check(ambiguous.Records.Count == 0 && ambiguous.Evidence.Count == 1 && ambiguous.PawnClaims.Contains("Thing_Human999"),
            "Contradictory Gu anchors cannot validate a historical identity.");
    }

    private static void CheckApertureConflict()
    {
        var first = PawnAt(201);
        var second = PawnAt(202);
        string id = Guid.NewGuid().ToString("N");
        var a = Attach(first, id);
        var b = Attach(second, id.ToUpperInvariant());
        var c = Attach(first, null);
        var source = Rows($"<li><pawn>{first.GetUniqueLoadID()}</pawn></li>");
        var plan = GuCultivatorPlan.PrepareLegacy(source, [Resolved(source.Rows[0], first)], [first, second], [a, b, c], []);
        plan.CommitApertureWrites(() => { });
        Check(plan.Records.Count == 2 && plan.Records.All(record => record.Association == GuNativeAssociation.Unresolved)
            && plan.ByPawn.Count == 0 && plan.ByAperture.Count == 0 && plan.PawnClaims.Count == 2,
            "Global duplicate aperture ID blocks both claimants, including the aperture-only owner.");
        Check(a.ApertureId == id && b.ApertureId == id.ToUpperInvariant() && c.ApertureId == null
            && plan.Evidence.Any(entry => entry.SourceOrdinal == 0)
            && plan.Evidence.Any(entry => entry.RawEvidence.Contains(a.GetUniqueLoadID()) && entry.RawEvidence.Contains(b.GetUniqueLoadID())),
            "Existing aperture IDs and both conflicting claims survive, with no provisional write.");

        var savedConflict = plan.Evidence.Single(entry => entry.Key.StartsWith("aperture:", StringComparison.Ordinal));
        string savedPayload = savedConflict.RawEvidence.Replace("\n", "\r\n", StringComparison.Ordinal);
        savedConflict.RawEvidence = savedPayload;
        int evidenceCount = plan.Evidence.Count;
        var reopened = GuCultivatorPlan.PrepareCommitted(plan.Records, plan.Evidence, [a, b, c], []);
        Check(reopened.Evidence.Count == evidenceCount && savedConflict.RawEvidence == savedPayload
            && reopened.ByPawn.Count == 0 && reopened.ByAperture.Count == 0,
            "Windows Scribe diagnostic line endings preserve the saved conflict without appending a duplicate.");

        var one = GuCultivatorPlan.NewRecord(first.GetUniqueLoadID(), "duplicate", first);
        var two = GuCultivatorPlan.NewRecord(second.GetUniqueLoadID(), "duplicate", second);
        two.Id = one.Id.ToUpperInvariant();
        var duplicate = GuCultivatorPlan.PrepareCommitted([one, two], [], [], []);
        Check(duplicate.Records.Count == 2 && duplicate.ById.Count == 0 && duplicate.ByPawn.Count == 0,
            "Committed conflicts retain both records and choose no identity winner.");
        var mismatched = GuCultivatorPlan.NewRecord(first.GetUniqueLoadID(), "mismatched-reference", second);
        var candidate = GuCultivatorPlan.NewRecord(second.GetUniqueLoadID(), "claimed-reference", second);
        var references = GuCultivatorPlan.PrepareCommitted([mismatched, candidate], [], [], []);
        Check(references.ByPawn.Count == 0 && references.Records.All(record => record.Association == GuNativeAssociation.Unresolved)
            && references.Evidence.Single().PawnClaims.ToHashSet().SetEquals([first.GetUniqueLoadID(), second.GetUniqueLoadID()]),
            "Contradictory references retain both tokens and suppress every affected Pawn binding.");
    }

    private static void CheckApertureTransaction()
    {
        var first = PawnAt(301);
        var second = PawnAt(302);
        var a = Attach(first, null);
        var b = Attach(second, null);
        var existing = Anchored(first.GetUniqueLoadID(), Guid.NewGuid().ToString("N"));
        var supply = new GuPopulationRecord("GU_HopeGu", 3, 1, 0);
        var inventoryStack = new ThingWithComps { thingIDNumber = 801, stackCount = 2 };
        int revision = existing.CustodyRevision;
        string originalAnchor = existing.AnchorHint;
        int publications = 0;
        var plan = GuCultivatorPlan.PrepareLegacy(Rows(""), [], [first, second], [a, b], [existing]);
        Check(a.ApertureId == null && b.ApertureId == null && plan.ByAperture.Count == 2
            && plan.Records.All(record => record.State == AwakeningState.Awakened && record.LatentAptitude == 40),
            "Aperture-only migration stages IDs and copies aptitude without touching native Hediffs.");

        ExpectFailure(() => plan.CommitApertureWrites(() => publications++, count =>
        {
            if (count == 1) throw new InvalidOperationException("Injected after the first native write.");
        }));
        Check(publications == 0 && a.ApertureId == null && b.ApertureId == null,
            "Failure after the first Hediff write restores both preimages and publishes nothing.");
        ExpectFailure(() => plan.CommitApertureWrites(() => throw new InvalidOperationException("Injected before publication.")));
        Check(a.ApertureId == null && b.ApertureId == null, "Failure before publication rolls back all applied Hediff writes.");
        Check(supply.MatureCount == 3 && supply.JuvenileCount == 1 && supply.Revision == 0
            && inventoryStack.stackCount == 2 && existing.CustodyRevision == revision && existing.AnchorHint == originalAnchor,
            "Failure preserves managed quantities and the exact existing Gu binding.");

        const string interference = "rollback-conflicting-preimage";
        var failure = new InvalidOperationException("Injected after the second aperture write.");
        AggregateException rollback = null;
        try
        {
            plan.CommitApertureWrites(() => publications++, count =>
            {
                if (count != 2) return;
                b.ReplaceCultivatorApertureId(b.ApertureId, interference);
                throw failure;
            });
        }
        catch (AggregateException error) { rollback = error; }
        Check(rollback?.InnerExceptions.Count == 2 && rollback.InnerExceptions[0] == failure
            && rollback.InnerExceptions[1].Message.Contains(b.GetUniqueLoadID(), StringComparison.Ordinal)
            && rollback.InnerExceptions[1].Message.Contains(interference, StringComparison.Ordinal)
            && publications == 0 && a.ApertureId == null && b.ApertureId == interference,
            "Failed rollback reports the affected aperture and observed ID, restores other preimages and publishes nothing.");
        b.ReplaceCultivatorApertureId(interference, null);

        var external = Guid.NewGuid().ToString("N");
        b.ReplaceCultivatorApertureId(null, external);
        ExpectFailure(() => plan.CommitApertureWrites(() => publications++));
        Check(a.ApertureId == null && b.ApertureId == external && publications == 0,
            "Preflight rejects a changed native ID before writing any aperture.");
        b.ReplaceCultivatorApertureId(external, null);
        second.health.hediffSet.hediffs.Remove(b);
        b.pawn = first;
        first.health.hediffSet.hediffs.Add(b);
        ExpectFailure(() => plan.CommitApertureWrites(() => publications++));
        Check(a.ApertureId == null && b.ApertureId == null && publications == 0,
            "Preflight rejects a Hediff moved to another Pawn after planning.");
        first.health.hediffSet.hediffs.Remove(b);
        b.pawn = second;
        second.health.hediffSet.hediffs.Add(b);
        plan.CommitApertureWrites(() => publications++);
        Check(publications == 1 && a.ApertureId != b.ApertureId && plan.ByAperture[a.ApertureId] == plan.ByPawn[first]
            && plan.ByAperture[b.ApertureId] == plan.ByPawn[second],
            "Committed physical IDs agree with the unique planned owner indexes.");
    }

    private static void CheckCommittedRoundTrip(string directory)
    {
        string path = Path.Combine(directory, "cultivator-record-managed.xml");
        var original = GuCultivatorPlan.NewRecord("Thing_Human999", "legacy:3", null);
        original.Association = GuNativeAssociation.Detached;
        original.LastObservedLife = GuObservedLife.Dead;
        original.ObservationTick = 17;
        original.LastCorpseDestruction = new GuCultivatorFact { EventKey = "corpse:1", Tick = 11, Cause = "body", Life = GuObservedLife.Dead };
        original.ConfirmedDiscard = new GuCultivatorFact { EventKey = "discard:1", Tick = 17, Cause = "GC", Life = GuObservedLife.Dead };
        original.ApertureIds.Add(Guid.NewGuid().ToString("N"));
        try
        {
            var written = original;
            Scribe.saver.InitSaving(path, "verification");
            Scribe_Deep.Look(ref written, "record");
            Scribe.saver.FinalizeSaving();
            for (int pass = 0; pass < 2; pass++)
            {
                GuCultivatorRecord loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "record");
                Scribe.loader.FinalizeLoading();
                var plan = GuCultivatorPlan.PrepareCommitted([loaded], [], [], []);
                Check(plan.ById[original.Id].Association == GuNativeAssociation.Detached && plan.ByPawn.Count == 0
                    && loaded.LastObservedLife == GuObservedLife.Dead && loaded.CurrentLife == GuObservedLife.Unknown
                    && loaded.LastCorpseDestruction?.EventKey == "corpse:1" && loaded.ConfirmedDiscard?.EventKey == "discard:1"
                    && loaded.ApertureIds.SequenceEqual(original.ApertureIds), $"Managed Scribe reload {pass + 1} preserves identity, facts and links.");
            }
            var corrupted = File.ReadAllText(path).Replace("<lastObservedLife>Dead</lastObservedLife>",
                "<lastObservedLife>unsupported-life</lastObservedLife>");
            Check(corrupted.Contains("unsupported-life"), "Committed corruption fixture took effect.");
            File.WriteAllText(path, corrupted);
            GuCultivatorRecord malformed = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref malformed, "record");
            Scribe.loader.FinalizeLoading();
            ExpectFailure(() => GuCultivatorPlan.PrepareCommitted([malformed], [], [], []));
            Check(malformed?.IsValid != true, "A malformed lifecycle value fails closed even if Scribe supplies a default.");
        }
        finally { File.Delete(path); }
    }

    private static void CheckAuthorityAndAwakening(Game game, string directory)
    {
        string path = Path.Combine(directory, "cultivator-store-managed.xml");
        var originalComponents = game.components;
        try
        {
            var world = new GuWorldComponent(game);
            var registry = new GuIndividualRegistry(game);
            game.components = [registry, world];
            world.StartedNewGame();
            Check(!world.CanSaveCultivators, "Fresh identity store waits for the actual registry component.");
            registry.StartedNewGame();
            Check(world.CanSaveCultivators, "Phase 2 completion publishes a valid empty store.");
            Scribe.saver.InitSaving(path, "verification");
            Scribe_Deep.Look(ref world, "component", game);
            Scribe.saver.FinalizeSaving();
            var document = Node(File.ReadAllText(path));
            Check(GuWorldComponent.ClassifyEnvelope(document["component"]) == GuCultivatorLoadKind.Committed
                && document["component"]!["guPawnRecords"] == null, "Actual empty-store writer emits one complete authority.");

            for (int pass = 0; pass < 2; pass++)
            {
                GuWorldComponent reopened = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref reopened, "component", game);
                Scribe.loader.FinalizeLoading();
                registry = new GuIndividualRegistry(game);
                game.components = [registry, reopened];
                if (pass == 0) registry.LoadedGame();
                else reopened.LoadedGame();
                Check(!reopened.CanSaveCultivators, $"Readiness ordering {pass + 1} waits for both components.");
                if (pass == 0) reopened.LoadedGame();
                else registry.LoadedGame();
                reopened.LoadedGame();
                Check(reopened.CanSaveCultivators && reopened.PendingLegacyRows == null,
                    $"Readiness ordering {pass + 1} publishes a committed empty store once.");
                world = reopened;
            }

            var pawn = PawnAt(401);
            Check(world.TryBeginAwakening() && !world.CanSaveCultivators, "An active awakening closes save authority.");
            var draft = world.PrepareAwakeningRecord(pawn);
            Check(draft != null && world.GetOrCreate(pawn) == null && !world.PrepareAwakeningPublication(draft, new Hediff_Aperture()),
                "An invalid provisional aperture cannot publish a new cultivator.");
            world.EndAwakening();
            Check(world.CanSaveCultivators && world.FindCultivator(draft.Id) == null,
                "Abandoned awakening leaves no public identity and reopens safe saving.");
            Check(world.TryBeginAwakening() && ReferenceEquals(draft, world.PrepareAwakeningRecord(pawn)),
                "Same-game retry uses the unpublished identity and aptitude without another roll.");
            var aperture = Attach(pawn, Guid.NewGuid().ToString("N"));
            Check(world.PrepareAwakeningPublication(draft, aperture), "Exactly attached aperture passes the prepared publication checks.");
            world.PublishAwakening(draft, aperture);
            world.EndAwakening();
            Check(ReferenceEquals(world.GetOrCreate(pawn), draft) && world.FindCultivator(draft.Id) == draft
                && draft.ApertureIds.SequenceEqual(new[] { aperture.ApertureId }) && draft.StateRevision == 2
                && draft.State == AwakeningState.Awakened, "Publication exposes one record and the exact owner link.");
            var healthState = typeof(Pawn_HealthTracker).GetField("healthState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var previousLife = healthState.GetValue(pawn.health);
            healthState.SetValue(pawn.health, Enum.Parse(healthState.FieldType, "Dead"));
            Check(draft.CurrentLife == GuObservedLife.Dead && draft.ConfirmedDiscard == null,
                "Observed death leaves native association and discard evidence independent.");
            healthState.SetValue(pawn.health, previousLife);
            Check(draft.CurrentLife == GuObservedLife.Alive && ReferenceEquals(world.FindCultivator(draft.Id), draft),
                "Returning the same bound Pawn to life preserves identity without a new record.");
        }
        finally
        {
            game.components = originalComponents;
            File.Delete(path);
        }
    }

    private static GuCultivatorLoadKind Kind(string xml) => GuWorldComponent.ClassifyEnvelope(Node("<component>" + xml + "</component>"));
    private static GuLegacyCultivatorRows Rows(string xml) => new(Node("<guPawnRecords>" + xml + "</guPawnRecords>"));

    private static XmlElement Node(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.DocumentElement!;
    }

    private static Pawn PawnAt(int id)
    {
        var pawn = new Pawn { def = Human, thingIDNumber = id };
        pawn.health = new Pawn_HealthTracker(pawn);
        return pawn;
    }

    private static Hediff_Aperture Attach(Pawn pawn, string id)
    {
        var aperture = new Hediff_Aperture { pawn = pawn, def = ApertureDef };
        aperture.ReplaceCultivatorApertureId(null, id);
        pawn.health.hediffSet.hediffs.Add(aperture);
        return aperture;
    }

    private static GuPawnRecord Resolved(GuLegacyCultivatorRow row, Pawn pawn)
    {
        var loaded = new GuPawnRecord { Pawn = pawn };
        typeof(GuPawnRecord).GetProperty("RawXml", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(loaded, row.RawXml);
        return loaded;
    }

    private static GuRecord Anchored(string pawnToken, string apertureId)
    {
        var record = GuRecord.CreateManifested(Guid.NewGuid().ToString("N"), new GuSpeciesDef { defName = "ManagedCultivatorFixture" },
            "development", "managed", 4, GuCondition.Dormant, GuManifestationKind.Thing, "ManagedThing");
        record.CommitContainment(GuContainerKind.MortalAperture, apertureId, pawnToken);
        return record;
    }

    private static void ExpectFailure(Action operation)
    {
        try { operation(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the cultivator foundation guard to reject the operation.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed++;
    }

    private static bool LifeUnknown(this GuCultivatorRecord record) =>
        record.CurrentLife == GuObservedLife.Unknown && record.LastObservedLife == GuObservedLife.Unknown && record.ConfirmedDiscard == null;
}
