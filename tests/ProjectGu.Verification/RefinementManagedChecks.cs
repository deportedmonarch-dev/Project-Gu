using ProjectGu;
using ProjectGu.Domain;
using Verse;

internal static class RefinementManagedChecks
{
    internal static void Run(string directory)
    {
        var species = new GuSpeciesDef
        {
            defName = "ManagedWillGu",
            refinementBaseWorkTicks = 9000,
            refinementEssenceCost = 12f,
            refinementResistance = 1f
        };
        var ordinary = new Hediff_Aperture();
        ordinary.Initialize(40);
        Check(GuRefinementRules.TryQuote(species, 1, null, Guid.NewGuid().ToString("N"), ordinary, out var baseline)
            && baseline.WorkTicks == 9000 && Math.Abs(baseline.EssenceCost - 12f) < 0.001f,
            "Rank-one baseline will-enforcement quote is 3.6 in-game hours and twelve essence.");

        var capable = new Hediff_Aperture();
        capable.Initialize(80);
        Check(GuRefinementRules.TryQuote(species, 1, null, Guid.NewGuid().ToString("N"), capable, out var highAptitude)
            && highAptitude.WorkTicks == 4500 && highAptitude.EssenceCost == baseline.EssenceCost,
            "Cultivator aptitude improves work rate without changing the Gu's essence cost.");
        Check(GuRefinementRules.TryQuote(species, 2, null, Guid.NewGuid().ToString("N"), ordinary, out var higherRank)
            && higherRank.WorkTicks == 13500 && Math.Abs(higherRank.EssenceCost - 18f) < 0.001f,
            "Higher Gu rank raises work and essence difficulty.");
        Check(GuRefinementRules.TryQuote(species, 1, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), ordinary, out var bonded)
            && bonded.WorkTicks == 15750 && Math.Abs(bonded.EssenceCost - 21f) < 0.001f,
            "Existing foreign allegiance adds contest resistance.");
        string owner = Guid.NewGuid().ToString("N");
        Check(!GuRefinementRules.TryQuote(species, 1, owner, owner, ordinary, out _)
            && !GuRefinementRules.TryQuote(species, 6, null, owner, ordinary, out _),
            "Already-recognized Gu and immortal-rank Gu are outside this mortal prototype.");
        species.refinementResistance = 2f;
        Check(GuRefinementRules.TryQuote(species, 1, null, Guid.NewGuid().ToString("N"), ordinary, out var resistant)
            && resistant.WorkTicks == 18000 && resistant.EssenceCost == 24f,
            "Species-configured Gu resistance scales both duration and essence.");

        string path = Path.Combine(directory, "gu-refinement-process-managed.xml");
        string processId = Guid.NewGuid().ToString("N");
        string cultivatorId = Guid.NewGuid().ToString("N");
        string priorOwner = Guid.NewGuid().ToString("N");
        var process = new GuRefinementProcess(processId, cultivatorId, priorOwner, 9000, 12f, 100);
        var aperture = new Hediff_Aperture();
        aperture.Initialize(40);
        bool allTicksAdvanced = true;
        for (int tick = 101; tick <= 151; tick++) allTicksAdvanced &= process.TryAdvance(tick, aperture);
        Check(allTicksAdvanced, "Refinement spends essence as work advances.");
        int progress = process.CompletedWorkTicks;
        float essenceAfterWork = aperture.Essence;
        Check(progress == 51 && essenceAfterWork < 20f
            && process.TryAdvance(151, aperture) && process.CompletedWorkTicks == progress,
            "A repeated work tick cannot double progress or charge.");
        try
        {
            var written = process;
            Scribe.saver.InitSaving(path, "verification");
            Scribe_Deep.Look(ref written, "process");
            Scribe.saver.FinalizeSaving();
            for (int reload = 1; reload <= 2; reload++)
            {
                GuRefinementProcess loaded = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "process");
                Scribe.loader.FinalizeLoading();
                Check(loaded is { IsValid: true } && loaded.ProcessId == processId
                    && loaded.CultivatorId == cultivatorId && loaded.PreviousOwnerId == priorOwner
                    && loaded.CompletedWorkTicks == progress && Math.Abs(loaded.EssenceSpent - process.EssenceSpent) < 0.001f,
                    $"Scribe reload {reload} preserves the exact contested identity, process owner, work and paid cost.");
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
