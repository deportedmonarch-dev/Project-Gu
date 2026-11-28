using System.Diagnostics;
using ProjectGu;
using ProjectGu.Domain;
using Verse;

internal static class RegionalEcologyChecks
{
    internal static void Run()
    {
        var donor = new GuPopulationRecord("SpeciesA", 1, 0, -1);
        var recipient = new GuPopulationRecord("SpeciesA", 1, 0, -1);
        var competing = new GuPopulationRecord("SpeciesA", 0, 0, -1);
        var incompatible = new GuPopulationRecord("SpeciesB", 0, 0, -1);
        Require(!donor.TryTransferMatureTo(incompatible, 0, 0, 4, 0)
            && !donor.TryTransferMatureTo(recipient, 0, 0, 4, 3)
            && donor.MatureCount == 1 && donor.Revision == 0 && recipient.Revision == 0, "Rejected migration preserves both preimages");
        Require(donor.TryTransferMatureTo(recipient, 0, 0, 4, 2) && donor.MatureCount == 0 && recipient.MatureCount == 2
            && donor.Revision == 1 && recipient.Revision == 1, "Successful migration conserves anonymous units");
        Require(!donor.TryTransferMatureTo(competing, 0, 0, 4, 0) && !donor.TryTransferMatureTo(competing, 1, 0, 4, 0)
            && competing.MatureCount == 0 && competing.Revision == 0, "A competing or replayed request cannot spend the same unit");

        var retention = new GuEncounterRetention();
        retention.Observe(10, historical: true);
        Require(retention.Protected(69, 60) && !retention.Protected(70, 60), "Historical significance expires at its deadline");
        Require(retention.Renew("test:quest", 70, 90) && retention.Renew("test:quest", 80, 100)
            && retention.Protected(99, 60) && !retention.Protected(100, 60), "Renewal extends the exact lease and expired references are pruned");
        Require(retention.Renew("test:tracking", 100, 200), "A released or expired lease frees its slot");
        retention.Release("test:tracking");
        Require(!retention.Protected(101, 60), "An issuer can release active protection");

        var returned = new GuPopulationRecord("SpeciesA", 3, 0, -1);
        Require(!returned.TryCreditReturn("return", 0, 4, 1) && returned.Revision == 0 && returned.ReturnCredits == 0,
            "A full return destination keeps its preimage");
        Require(returned.TryCreditReturn("return", 0, 4, 0) && returned.MatureCount == 4 && returned.Revision == 1
            && returned.ReturnCredits == 1 && returned.ReturnDigest == GuPopulationRecord.ChainDigest(GuPopulationRecord.DigestSeed, 1, "return")
            && !returned.TryCreditReturn("return", 0, 4, 0), "Return credits exactly one adult with sealed evidence and rejects a stale revision");
        var beforeIssue = returned.Preimage;
        Require(returned.TryDebitForReceipt("new-specimen") && returned.EvidenceIntact && returned.ReceiptDebits == 1
            && returned.TryRestoreUnpublishedDebit(beforeIssue) && returned.EvidenceIntact && returned.ReturnCredits == 1
            && returned.Revision == 1 && returned.MatureCount == 4, "Aborting new issuance preserves earlier return evidence");

        const int rowCount = 50_000;
        var queue = new GuSurfaceEcology.DueSchedule();
        var rows = Enumerable.Range(0, rowCount).Select(index =>
            new GuSurfaceEcology.Address(index.ToString(), "Species" + index % 3, index % 5 == 0)).ToArray();
        var timer = Stopwatch.StartNew();
        foreach (var row in rows)
        {
            queue.Set(row, 2500);
            queue.Set(row, 2000);
            queue.Set(row, 2000);
        }
        double enqueueMs = timer.Elapsed.TotalMilliseconds;
        Require(queue.Count == rowCount && queue.FirstTick == 2000, "Rescheduling keeps exactly one entry per row");
        timer.Restart();
        int processed = 0, saturated = 0, turns = 0;
        var encountered = new HashSet<GuSurfaceEcology.Address>();
        while (queue.FirstTick == 2000)
        {
            int count = queue.Service(2000, row =>
            {
                Require(encountered.Add(row), "Each due row runs once");
                queue.Set(row, 4000);
            }, out bool budgetSaturated);
            Require(count <= 16, "Service budget held");
            processed += count;
            turns++;
            if (budgetSaturated) saturated++;
        }
        Require(processed == rowCount && queue.Count == rowCount && queue.FirstTick == 4000, "Every due row was processed and rescheduled");
        Console.WriteLine($"PASS regional scheduler synthetic rows={rowCount} touches={3 * rowCount} processed={processed} pending={queue.Count}"
            + $" turns={turns} saturated={saturated} enqueue_ms={enqueueMs:F1} service_ms={timer.Elapsed.TotalMilliseconds:F1}");

        VerifyReturnChurn();
    }

    private static void VerifyReturnChurn()
    {
        const int cycles = 16_000;
        const int siteCount = 128;
        var species = new GuSpeciesDef { defName = "SyntheticSurface" };
        var cohorts = Enumerable.Range(0, siteCount).Select(_ => new GuPopulationRecord(species.defName, 4, 0, -1)).ToArray();
        var tombstones = new List<GuRecord>(cycles);
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < cycles; i++)
        {
            var cohort = cohorts[i % siteCount];
            var site = $"0/{i % siteCount}/0";
            var request = new GuSourceRequest($"surface/{site}/{cohort.Revision}", GuSurfaceEcology.SourceKind, site, "Map_synthetic",
                species.defName, 1, GuCohortStage.Mature, GuSurfaceEcology.AmbientTrigger, null, cohort.Revision);
            var record = GuRecord.PrepareFromSource(Guid.NewGuid().ToString("N"), species, request, i, "Pawn_synthetic");
            Require(cohort.TryDebitForReceipt(request.EventKey), "Churn fixture pays one adult for each new ID");
            record.CommitSourceDebit(cohort.Revision);
            record.AddHistory(new(i, GuLifecycleEventKind.Recovered, "Fixture", null, null, GuLocation.FromTile(i % siteCount,
                0, IntVec3.Zero, "Map_synthetic", null, i), "Synthetic recovery history superseded by an ecological return."));
            var receipt = new GuPopulationReturnReceipt(site, "WorldObject_synthetic", record.CustodyRevision, cohort.Revision + 1, i + 1);
            Require(cohort.TryCreditReturn(receipt.CreditKey(record.Id), cohort.Revision, 4, 0), "Churn fixture credits its exact adult");
            record.CommitPopulationReturn(receipt);
            tombstones.Add(record);
        }
        var cycleMs = timer.Elapsed.TotalMilliseconds;
        Require(cohorts.All(cohort => cohort.MatureCount == 4 && cohort.ReceiptDebits == cycles / siteCount
            && cohort.ReturnCredits == cycles / siteCount && cohort.EvidenceIntact), "Repeated issuance and return conserve every habitat's stock");

        var sample = tombstones.Take(1024).ToList();
        long bytesBefore = SerializedSize(sample);
        timer.Restart();
        Require(tombstones.All(record => record.Compact() && record.PopulationReturn.Matches(record)
            && record.History.Count == 0 && record.ProjectionId == null), "Every compacted tombstone retains proof and clears old narrative and custody");
        var compactMs = timer.Elapsed.TotalMilliseconds;
        long bytesAfter = SerializedSize(sample);
        Require(bytesAfter < bytesBefore && tombstones.Select(record => record.Id).Distinct().Count() == cycles,
            "Compaction reduces saved size and retains all distinct replay IDs");
        Console.WriteLine($"PASS managed return churn sites={siteCount} cycles={cycles} living=0 aggregates={tombstones.Count}"
            + $" settle_ms={cycleMs:F1} compact_ms={compactMs:F1} saved_1024_before={bytesBefore} saved_1024_after={bytesAfter}");
    }

    private static long SerializedSize(List<GuRecord> records)
    {
        string path = Path.GetTempFileName();
        try
        {
            Scribe.saver.InitSaving(path, "returnChurn");
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            Scribe.saver.FinalizeSaving();
            return new FileInfo(path).Length;
        }
        finally { File.Delete(path); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
