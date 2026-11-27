using System.Reflection;
using ProjectGu;
using ProjectGu.Domain;

/// <summary>
/// Alpha 6 Slice 0 managed checks against the real assembly: exact-only debit restoration with owner-side evidence,
/// creation-receipt payload conflicts and shape validation (rank agreement, asset settlement), compaction retention of
/// receipts, the registry's own scope-evidence check against substituted, inflated and duplicated claims, a seeded
/// sequence of production cohort debits checked by that same registry method after every step, and source guards for the
/// transaction ordering. Native staging, callbacks, saves, recovery and fresh-process reloads are verified by the Slice0
/// live profile.
/// </summary>
internal static class SourceTransactionChecks
{
    private static int passed;
    private const string Species = "GU_TestSpecies";

    internal static void Run(string root)
    {
        VerifyPreimage();
        VerifyReceipts();
        VerifyEvidence();
        VerifySeededConservation();
        VerifySources(root);
        Console.WriteLine($"PASS: Alpha 6 Slice 0 transaction checks ({passed}).");
    }

    private static void VerifyPreimage()
    {
        var cohort = new GuPopulationRecord(Species, 2, 1, -1);
        var before = cohort.Preimage;
        Check(cohort.EvidenceVersion == 0 && cohort.EvidenceIntact, "A row with no receipt debit carries no evidence and is intact");
        Check(cohort.TryDebitForReceipt("event-1") && cohort.Revision == 1 && cohort.MatureCount == 1 && cohort.ReceiptDebits == 1
            && cohort.EvidenceVersion == GuPopulationRecord.CurrentEvidenceVersion && cohort.EvidenceIntact,
            "A receipt debit removes one mature unit, advances the revision and folds the receipt into sealed evidence");
        Check(cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2 && cohort.JuvenileCount == 1 && cohort.Revision == 0
            && cohort.ReceiptDebits == 0 && cohort.EvidenceVersion == 0 && cohort.EvidenceIntact,
            "An unpublished debit restores exactly the preimage counts, revision and evidence");
        Check(!cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2, "Restoration cannot credit twice");
        Check(cohort.TryDebitForReceipt("event-2"), "Second debit");
        cohort.AdvanceDaily(5, false, 0);
        Check(cohort.Revision == 2 && !cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2 && cohort.EvidenceIntact,
            "Any change after the debit (maturation here) blocks restoration and keeps the evidence sealed");
        var empty = new GuPopulationRecord(Species, 0, 3, -1);
        Check(!empty.TryDebitForReceipt("event-3") && !empty.TryRestoreUnpublishedDebit(empty.Preimage) && empty.TotalCount == 3,
            "Juveniles are never debited and an empty preimage restores nothing");
    }

    private static GuSourceRequest Request(string key = "event-1", string kind = "test", string source = "source-a",
        string credential = "cred-1", string species = Species, string trigger = "probe", string asset = null,
        int rank = 1, GuCohortStage stage = GuCohortStage.Mature, long revision = 4) =>
        new(key, kind, source, credential, species, rank, stage, trigger, asset, revision);

    private static void VerifyReceipts()
    {
        var species = new GuSpeciesDef { defName = Species, minRank = 1, maxRank = 2 };
        var request = Request(asset: "Beer");
        var record = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, request, 10, "Pawn_1", "Thing_Beer7");
        var receipt = record.CreationEvidence;
        Check(record.Origin == GuRecord.CohortOrigin && record.OriginId == "source-a" && record.OriginNativeId == "cred-1"
            && record.CreationEventKey == "event-1" && !record.CohortDebitCommitted && receipt.RevisionBefore == 4
            && receipt.RevisionAfter == -1 && receipt.AssetScope == "Beer" && receipt.AssetId == "Thing_Beer7"
            && receipt.Settlement == GuAssetSettlement.None,
            "A prepared record carries an uncommitted receipt with the exact source, scope and asset identity");
        Check(receipt.ShapeError(record) == null, "An uncommitted receipt is well formed while the record has no debit");
        record.CommitSourceDebit(5, GuAssetSettlement.Pending);
        Check(record.CohortDebitCommitted && receipt.RevisionAfter == 5 && receipt.Settlement == GuAssetSettlement.Pending
            && receipt.ShapeError(record) == null, "Commit records the debit revision and a pending asset settlement");
        receipt.MarkSettlement(GuAssetSettlement.Interrupted);
        Check(receipt.Settlement == GuAssetSettlement.Interrupted, "An interrupted settlement is recorded durably");
        receipt.MarkSettlement(GuAssetSettlement.Settled);
        receipt.MarkSettlement(GuAssetSettlement.Interrupted);
        Check(receipt.Settlement == GuAssetSettlement.Settled, "A settled asset is final and can never be settled or reopened again");
        Check(receipt.PayloadConflict(request, record.SpeciesDefName) == null
            && receipt.PayloadConflict(request with { ExpectedRevision = 9, Credential = "cred-after-reload" }, record.SpeciesDefName) == null,
            "The same payload replays even when its revision is stale and its native credential was reacquired");
        Check(receipt.PayloadConflict(request with { SourceId = "source-b" }, record.SpeciesDefName) == "source"
            && receipt.PayloadConflict(request with { OwnerKind = "spring" }, record.SpeciesDefName) == "source"
            && receipt.PayloadConflict(request with { SpeciesDefName = "GU_Other" }, record.SpeciesDefName) == "species"
            && receipt.PayloadConflict(request with { Trigger = "other" }, record.SpeciesDefName) == "trigger"
            && receipt.PayloadConflict(request with { AssetScope = "Wine" }, record.SpeciesDefName) == "asset scope"
            && receipt.PayloadConflict(request with { AssetScope = null }, record.SpeciesDefName) == "asset scope"
            && receipt.PayloadConflict(request with { Rank = 2 }, record.SpeciesDefName) == "rank or stage"
            && receipt.PayloadConflict(request with { Stage = GuCohortStage.Juvenile }, record.SpeciesDefName) == "rank or stage",
            "A reused key with a different source, species, trigger, asset scope, rank or stage is a conflict");
        var blank = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, Request(asset: " "), 10, "Pawn_2", "Thing_X");
        Check(blank.CreationEvidence.AssetScope == null && blank.CreationEvidence.AssetId == null
            && blank.CreationEvidence.PayloadConflict(Request(asset: ""), blank.SpeciesDefName) == null,
            "Blank asset scopes normalize to none and carry no asset identity");

        // Rank agreement (review Medium 2): the record is created at the species' wild rank; a receipt naming another rank is malformed.
        var ranked = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, Request(key: "event-rank", rank: 2), 10, "Pawn_3");
        Check(ranked.Rank == 1 && ranked.CreationEvidence.Rank == 2 && ranked.CreationEvidence.ShapeError(ranked)?.Contains("rank") == true,
            "A receipt whose authorized rank disagrees with the canonical record's rank is malformed and quarantines on load");

        SetField(receipt, "revisionAfter", 7L);
        Check(receipt.ShapeError(record) != null, "A receipt whose debit revision skips is malformed");
        SetField(receipt, "revisionAfter", 5L);
        SetField(record, "originNativeId", "cred-forged");
        Check(receipt.ShapeError(record) != null, "A receipt that disagrees with the record's native origin is malformed");
        SetField(record, "originNativeId", "cred-1");
        record.RevertUnpublishedDebit();
        Check(!record.CohortDebitCommitted && receipt.RevisionAfter == -1 && receipt.Settlement == GuAssetSettlement.None
            && receipt.ShapeError(record) == null, "Reverting an unpublished debit leaves a consistent uncommitted receipt");
        SetField(receipt, "settlement", GuAssetSettlement.Pending);
        Check(receipt.ShapeError(record) != null, "An undebited receipt with a pending settlement is malformed");
        SetField(receipt, "settlement", GuAssetSettlement.None);

        record.CommitSourceDebit(5);
        SetField(record, "existence", GuExistence.Terminal);
        Check(record.Compact() && record.CreationEvidence == receipt && record.CreationEventKey == "event-1" && record.CohortDebitCommitted,
            "Compaction keeps the creation receipt, event key and debit evidence, so replay protection survives");
    }

    // ---- Owner-side debit evidence through the registry's own scope check ----------------------------------------------

    private static readonly GuSpeciesDef TestSpecies = new() { defName = Species, minRank = 1, maxRank = 1 };

    private sealed class Ledger
    {
        internal readonly GuIndividualRegistry Registry = new(null);
        private readonly List<GuRecord> records;
        private readonly MethodInfo scopeCheck = typeof(GuIndividualRegistry).GetMethod("ScopeEvidenceRefusal", BindingFlags.Instance | BindingFlags.NonPublic)!;

        internal Ledger() { records = (List<GuRecord>)typeof(GuIndividualRegistry).GetField("records", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Registry)!; }

        internal GuRecord Commit(GuPopulationRecord cohort, string source, string key)
        {
            long before = cohort.Revision;
            var record = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), TestSpecies, Request(key: key, source: source, revision: before), 1, "Pawn_" + key);
            Check(cohort.TryDebitForReceipt(key), $"Debit {key}");
            record.CommitSourceDebit(before + 1);
            records.Add(record);
            return record;
        }

        internal void Add(GuRecord record) => records.Add(record);
        internal bool Remove(GuRecord record) => records.Remove(record);

        internal string Refusal(GuPopulationRecord cohort, string source = "source-a") =>
            (string)scopeCheck.Invoke(Registry, new object[] { "test", source, Species, cohort });
    }

    private static void VerifyEvidence()
    {
        var ledger = new Ledger();
        var cohort = new GuPopulationRecord(Species, 5, 2, -1);
        Check(ledger.Refusal(cohort) == null, "A scope with no receipts and a row with no evidence agree");
        var first = ledger.Commit(cohort, "source-a", "event-a1");
        ledger.Commit(cohort, "source-a", "event-a2");
        Check(ledger.Refusal(cohort) == null, "The row's evidence agrees with its two committed receipts");
        cohort.AdvanceDaily(10, true, 1);
        cohort.TryConsumeMature(1);
        Check(cohort.EvidenceIntact && ledger.Refusal(cohort) == null,
            "Legitimate intervening changes (maturation, a birth, a direct awakening sink) keep the evidence valid");
        ledger.Commit(cohort, "source-a", "event-a3");
        Check(ledger.Refusal(cohort) == null, "A commit after intervening changes extends the evidence");

        // Equal- and higher-revision substitution of the pre-debit stock (review High 4).
        foreach (long offset in new[] { 0L, 7L })
        {
            var substitute = new GuPopulationRecord(Species, 5, 2, -1);
            SetField(substitute, "revision", cohort.Revision + offset);
            Check(ledger.Refusal(substitute) is { } refusal && refusal.Contains("frozen"),
                $"A substituted row at the receipts' revision{(offset == 0 ? "" : " + " + offset)} is frozen");
        }
        // A copy of the row from before the last debit, with its revision raised to the receipt's RevisionAfter.
        var older = new GuPopulationRecord(Species, 5, 2, -1);
        var olderLedger = new Ledger();
        olderLedger.Commit(older, "source-a", "event-a1");
        olderLedger.Commit(older, "source-a", "event-a2");
        SetField(older, "revision", cohort.Revision);
        Check(ledger.Refusal(older) != null, "A row restored from before a committed debit is frozen even at an equal revision");
        // An inflated count on the real row, outside its own mutations.
        SetField(cohort, "matureCount", cohort.MatureCount + 3);
        Check(!cohort.EvidenceIntact && ledger.Refusal(cohort)?.Contains("outside its owner") == true, "An outside edit to the counts breaks the seal");
        cohort.AdvanceDaily(10, false, 0);
        Check(!cohort.EvidenceIntact, "A later legitimate mutation does not reseal an edited row");
        SetField(cohort, "matureCount", cohort.MatureCount - 3);

        // Duplicate claims of one debit revision, and a receipt that the row never recorded.
        var clean = new GuPopulationRecord(Species, 4, 0, -1);
        var dupLedger = new Ledger();
        var claimed = dupLedger.Commit(clean, "source-d", "event-d1");
        var duplicate = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), TestSpecies, Request(key: "event-d2", source: "source-d", revision: 0), 1, "Pawn_dup");
        duplicate.CommitSourceDebit(claimed.CreationEvidence.RevisionAfter);
        dupLedger.Add(duplicate);
        Check(dupLedger.Refusal(clean, "source-d")?.Contains("same debit") == true, "Two receipts claiming one debit revision freeze the scope");
        dupLedger.Remove(duplicate);
        Check(dupLedger.Refusal(clean, "source-d") == null, "Without the duplicate the scope agrees again");
        dupLedger.Remove(claimed);
        Check(dupLedger.Refusal(clean, "source-d") != null, "A row that records a debit no receipt claims is frozen");
        Check(first.CreationEvidence.RevisionAfter == 1, "Receipt revisions are the row's debit revisions");
    }

    /// <summary>
    /// A long seeded sequence over one cohort through the production debit, restoration and evidence methods: commits, stale
    /// replays, restored and unrestorable faults, and intervening maturation. After every step the cohort's anonymous units plus
    /// the counted individuals equal the opening stock, each event key owns at most one record, and the registry's own scope
    /// check accepts the row against the receipts.
    /// </summary>
    private static void VerifySeededConservation()
    {
        var random = new Random(60060);
        const int opening = 40;
        var ledger = new Ledger();
        var cohort = new GuPopulationRecord(Species, opening, 0, -1);
        var receipts = new Dictionary<string, GuRecord>(StringComparer.Ordinal);
        int restored = 0, unresolved = 0, replays = 0, matured = 0;
        for (int step = 0; step < 400; step++)
        {
            string key = random.Next(4) == 0 && receipts.Count > 0 ? receipts.Keys.ElementAt(random.Next(receipts.Count)) : $"event-{step}";
            if (receipts.ContainsKey(key))
            {
                replays++; // A replay changes neither count.
            }
            else if (cohort.MatureCount > 0)
            {
                var before = cohort.Preimage;
                var record = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), TestSpecies, Request(key: key, revision: cohort.Revision), 1, "Pawn_" + step);
                Check(cohort.TryDebitForReceipt(key), "Debit");
                switch (random.Next(6))
                {
                    case 0: // Fault inside the callback-free window: restore the preimage; the receipt never publishes.
                        Check(cohort.TryRestoreUnpublishedDebit(before), "Restore inside the window");
                        restored++;
                        break;
                    case 1: // Fault whose restoration cannot be proven: keep one counted unresolved claim with its receipt.
                        record.CommitSourceDebit(before.Revision + 1);
                        ledger.Add(record);
                        receipts[key] = record;
                        unresolved++;
                        break;
                    default:
                        record.CommitSourceDebit(before.Revision + 1);
                        ledger.Add(record);
                        receipts[key] = record;
                        break;
                }
            }
            if (random.Next(10) == 0)
            {
                cohort.AdvanceDaily(opening, false, 0); // Legitimate intervening change; nothing to mature here, revision unchanged.
                matured++;
            }
            Check(cohort.TotalCount + receipts.Count == opening && ledger.Refusal(cohort) == null,
                $"Conservation and evidence after step {step}: cohort {cohort.TotalCount} + individuals {receipts.Count} = {opening}");
        }
        Check(restored > 0 && unresolved > 0 && replays > 0 && matured > 0 && cohort.MatureCount == 0,
            $"The sequence exercised restore ({restored}), unresolved ({unresolved}), replay ({replays}), daily steps ({matured}) and exhaustion");
    }

    private static void VerifySources(string root)
    {
        string source = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Materialization.cs"));
        int Index(string text, int from = 0)
        {
            int index = source.IndexOf(text, from, StringComparison.Ordinal);
            Check(index >= 0, $"Transaction source contains {text}");
            return index;
        }
        // The guard is taken before any adapter call.
        int entry = Index("internal GuSourceOutcome TryMaterializeFromSource(");
        int guard = Index("CustodyTransitionInProgress = true;", entry);
        int guarded = Index("MaterializeGuarded(source, request", entry);
        Check(guard < guarded && source.IndexOf("source.", entry, StringComparison.Ordinal) > guarded,
            "The custody guard is acquired before the transaction calls the supplied source");
        int replay = Index("byCreationEvent.TryGetValue(request.EventKey, out var previous)");
        int authority = Index("var authority = Authority(source, request.OwnerKind, request.SourceId, out reason);");
        int credential = Index("string credential = authority.CurrentCredential;");
        int authorize = Index("authority.RefuseIssue(");
        int unique = Index("if (!authority.HasUniqueIdentity)");
        int floor = Index("cohort.Revision < floor");
        int evidence = Index("ScopeEvidenceRefusal(request.OwnerKind, request.SourceId, request.SpeciesDefName, cohort)");
        int stale = Index("if (cohort.Revision != request.ExpectedRevision)");
        Check(replay < authority && authority < credential && credential < authorize && authorize < unique && unique < floor
            && floor < evidence && evidence < stale,
            "Receipt replay precedes independent authority, credential, authorization, ownership, evidence and revision checks");
        int spawn = Index("TryPlaceStagedWildPawn(tx.Staged");
        int owned = Index("if (byCreationEvent.ContainsKey(key)) return false;");
        int debit = Index("tx.Cohort.TryDebitForReceipt(key)");
        int recorded = Index("TryRecordSourceCreation(tx.Record");
        int publish = Index("tx.Staged.SetReleaseSuppressed(false);");
        Check(spawn < owned && owned < debit && debit < recorded && recorded < publish,
            "Native staging precedes the event ownership check, the debit, the receipt and publication");
        Check(!source.Contains("byCreationEvent[record.CreationEventKey] = record;\n            RaiseCommittedFloor(tx.FloorKey")
            && source.Contains("if (byCreationEvent.TryGetValue(record.CreationEventKey, out var claim) && claim != record) return false;"),
            "Event insertion refuses an existing claim instead of overwriting it");
        int unresolvedMark = Index("record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing");
        int unresolvedNative = Index("if (!TryDiscardStaged(tx)) SafeMarkRogue(", unresolvedMark);
        Check(unresolvedMark < unresolvedNative, "The unresolved claim is published before native cleanup and diagnostics");
        int quarantine = Index("QuarantineUndebitedEvent(tx, reason);");
        int quarantineNative = Index("SafeMarkRogue(tx.Staged, reason);", quarantine);
        Check(quarantine < quarantineNative, "The undebited quarantine claim is published before native diagnostics");
        Check(!source.Contains("is not CompGuSpiritSpring") && !source.Contains("is CompGuSpiritSpring"),
            "The shared transaction has no spring type test; the spring rules live in its adapter");
        string factory = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Release.cs"));
        Check(factory.Contains("Pawn_GuWildProjection.GenerateForRegistry"), "Native Gu Pawns still come only from the gated registry factory");
        string job = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/Jobs/JobDriver_GuLureWildGu.cs"));
        Check(!job.Contains(".Destroy()") && !job.Contains("SplitOff") && job.Contains("TryLureWildGu(species, registry, pawn, job, lure"),
            "The lure job no longer spends bait itself; the transaction spends the exact held bait once");
        string spring = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Sources.cs"));
        Check(spring.Contains("if (species.UsesLureEncounter)") && spring.Contains("asset is not GuHeldLureAsset lure"),
            "The spring adapter enforces lure-only species and requires the exact held bait for a lure");
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }
}
