using System.Diagnostics;
using ProjectGu;
using ProjectGu.Domain;

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
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
