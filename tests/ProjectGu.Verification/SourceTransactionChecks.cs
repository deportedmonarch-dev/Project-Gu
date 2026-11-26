using System.Reflection;
using ProjectGu.Domain;

/// <summary>
/// Alpha 6 Slice 0 managed checks against the real assembly: exact-only debit restoration, creation-receipt payload
/// conflicts and shape validation, compaction retention of receipts, a seeded cohort sequence that checks conservation
/// after every step, and source guards for the transaction ordering. These are managed checks. Native staging, callbacks,
/// saves and fresh-process reloads are verified by the Slice0 live profile.
/// </summary>
internal static class SourceTransactionChecks
{
    private static int passed;

    internal static void Run(string root)
    {
        VerifyPreimage();
        VerifyReceipts();
        VerifySeededConservation();
        VerifySources(root);
        Console.WriteLine($"PASS: Alpha 6 Slice 0 transaction checks ({passed}).");
    }

    private static void VerifyPreimage()
    {
        var cohort = new GuPopulationRecord("GU_TestSpecies", 2, 1, -1);
        var before = cohort.Preimage;
        Check(cohort.TryConsumeMature(1) && cohort.Revision == 1 && cohort.MatureCount == 1, "A debit removes one mature unit and advances the revision");
        Check(cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2 && cohort.JuvenileCount == 1 && cohort.Revision == 0,
            "An unpublished debit restores exactly the preimage counts and revision");
        Check(!cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2, "Restoration cannot credit twice");
        Check(cohort.TryConsumeMature(1), "Second debit");
        cohort.AdvanceDaily(5, false, 0);
        Check(cohort.Revision == 2 && !cohort.TryRestoreUnpublishedDebit(before) && cohort.MatureCount == 2,
            "Any change after the debit (maturation here) blocks restoration, so the debit stays");
        var empty = new GuPopulationRecord("GU_TestSpecies", 0, 3, -1);
        Check(!empty.TryConsumeMature(1) && !empty.TryRestoreUnpublishedDebit(empty.Preimage) && empty.TotalCount == 3,
            "Juveniles are never debited and an empty preimage restores nothing");
    }

    private static GuSourceRequest Request(string key = "event-1", string kind = "test", string source = "source-a",
        string credential = "cred-1", string species = "GU_TestSpecies", string trigger = "probe", string asset = null,
        int rank = 1, GuCohortStage stage = GuCohortStage.Mature, long revision = 4) =>
        new(key, kind, source, credential, species, rank, stage, trigger, asset, revision);

    private static void VerifyReceipts()
    {
        var species = new GuSpeciesDef { defName = "GU_TestSpecies", minRank = 1, maxRank = 1 };
        var request = Request(asset: "Beer");
        var record = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, request, 10, "Pawn_1");
        var receipt = record.CreationEvidence;
        Check(record.Origin == GuRecord.CohortOrigin && record.OriginId == "source-a" && record.OriginNativeId == "cred-1"
            && record.CreationEventKey == "event-1" && !record.CohortDebitCommitted && receipt.RevisionBefore == 4
            && receipt.RevisionAfter == -1 && receipt.AssetScope == "Beer", "A prepared record carries an uncommitted receipt with the exact source and scope");
        Check(receipt.ShapeError(record) == null, "An uncommitted receipt is well formed while the record has no debit");
        record.CommitSourceDebit(5);
        Check(record.CohortDebitCommitted && receipt.RevisionAfter == 5 && receipt.ShapeError(record) == null, "Commit records the debit revision");
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
        var blank = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, Request(asset: " "), 10, "Pawn_2");
        Check(blank.CreationEvidence.AssetScope == null && blank.CreationEvidence.PayloadConflict(Request(asset: ""), blank.SpeciesDefName) == null,
            "Blank asset scopes normalize to none");

        SetField(receipt, "revisionAfter", 7L);
        Check(receipt.ShapeError(record) != null, "A receipt whose debit revision skips is malformed");
        SetField(receipt, "revisionAfter", 5L);
        SetField(record, "originNativeId", "cred-forged");
        Check(receipt.ShapeError(record) != null, "A receipt that disagrees with the record's native origin is malformed");
        SetField(record, "originNativeId", "cred-1");
        record.RevertUnpublishedDebit();
        Check(!record.CohortDebitCommitted && receipt.RevisionAfter == -1 && receipt.ShapeError(record) == null,
            "Reverting an unpublished debit leaves a consistent uncommitted receipt");

        record.CommitSourceDebit(5);
        SetField(record, "existence", GuExistence.Terminal);
        Check(record.Compact() && record.CreationEvidence == receipt && record.CreationEventKey == "event-1" && record.CohortDebitCommitted,
            "Compaction keeps the creation receipt, event key and debit evidence, so replay protection survives");
    }

    /// <summary>
    /// A long seeded sequence over one cohort: commits, stale replays, restored and unrestorable faults. After every step the
    /// cohort's anonymous units plus the counted individuals equal the opening stock, and each event key owns at most one.
    /// </summary>
    private static void VerifySeededConservation()
    {
        var random = new Random(60060);
        const int opening = 40;
        var cohort = new GuPopulationRecord("GU_TestSpecies", opening, 0, -1);
        var receipts = new Dictionary<string, long>(StringComparer.Ordinal);
        int counted = 0, restored = 0, unresolved = 0, replays = 0;
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
                Check(cohort.TryConsumeMature(1), "Debit");
                switch (random.Next(5))
                {
                    case 0: // Fault inside the callback-free window: restore the preimage.
                        Check(cohort.TryRestoreUnpublishedDebit(before), "Restore inside the window");
                        restored++;
                        break;
                    case 1: // Fault whose restoration cannot be proven: keep one counted unresolved claim.
                        receipts[key] = cohort.Revision;
                        counted++;
                        unresolved++;
                        break;
                    default:
                        receipts[key] = cohort.Revision;
                        counted++;
                        break;
                }
            }
            Check(cohort.TotalCount + counted == opening && receipts.Count == counted,
                $"Conservation after step {step}: cohort {cohort.TotalCount} + individuals {counted} = {opening}");
        }
        Check(restored > 0 && unresolved > 0 && replays > 0 && cohort.MatureCount == 0,
            $"The sequence exercised restore ({restored}), unresolved ({unresolved}), replay ({replays}) and exhaustion");
    }

    private static void VerifySources(string root)
    {
        string source = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Materialization.cs"));
        int Index(string text)
        {
            int index = source.IndexOf(text, StringComparison.Ordinal);
            Check(index >= 0, $"Transaction source contains {text}");
            return index;
        }
        int replay = Index("byCreationEvent.TryGetValue(request.EventKey, out var previous)");
        int credential = Index("string credential = source.CurrentCredential;");
        int authorize = Index("source.RefuseIssue(");
        int unique = Index("if (!source.HasUniqueIdentity)");
        int floor = Index("cohort.Revision < floor");
        int stale = Index("if (cohort.Revision != request.ExpectedRevision)");
        Check(replay < credential && credential < authorize && authorize < unique && unique < floor && floor < stale,
            "Receipt replay precedes credential, authorization, ownership and revision checks");
        int spawn = Index("TryPlaceStagedWildPawn(tx.Staged");
        int debit = Index("tx.Cohort.TryConsumeMature(1)");
        int recorded = Index("RecordSourceCreation(tx);");
        int publish = Index("tx.Staged.SetReleaseSuppressed(false);");
        Check(spawn < debit && debit < recorded && recorded < publish, "Native staging precedes the debit, which precedes the receipt and publication");
        Check(!source.Contains("is not CompGuSpiritSpring") && !source.Contains("is CompGuSpiritSpring"),
            "The shared transaction has no spring type test; the spring rules live in its adapter");
        string factory = File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Release.cs"));
        Check(factory.Contains("Pawn_GuWildProjection.GenerateForRegistry"), "Native Gu Pawns still come only from the gated registry factory");
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }
}
