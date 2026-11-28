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
