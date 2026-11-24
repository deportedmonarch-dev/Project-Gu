using System.Text.Json;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B3C => GenCommandLine.CommandLineArgPassed("guslice3b3c");
    private static string RefinementFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-refinement-fixture.json");

    private sealed record RefinementFixture(string GuId, string CultivatorId, string ApertureId,
        int CustodyRevision, string ProcessId, int TotalWorkTicks, int CompletedWorkTicks,
        float EssenceSpent, float TotalEssenceCost);

    private static void ExerciseSlice3B3C()
    {
        Map map = Find.CurrentMap;
        Pawn host = map.mapPawns.FreeColonistsSpawned.First();
        EnsureInventoryFixtureSpace(host);
        Hediff_Aperture aperture = AddProbeAperture(host);
        var world = Current.Game.GetComponent<GuWorldComponent>();
        var cultivator = world.GetOrCreate(host);
        Check(cultivator != null && cultivator.State == AwakeningState.Awakened,
            "The fixture refiner has a durable awakened cultivator identity.");
        string apertureId = Guid.NewGuid().ToString("N");
        aperture.ReplaceCultivatorApertureId(null, apertureId);
        cultivator.ApertureIds.Add(apertureId);

        Prefs.DevMode = true;
        var gu = PackTestGu(host);
        Prefs.DevMode = false;
        var registry = IndividualRegistry;
        var record = registry.Resolve(gu);
        string guId = record.Id;
        string cultivatorId = cultivator.CultivatorId;
        int custodyRevision = record.CustodyRevision;
        string jobName = "GU_RefineMortalGu";
        var option = GuApertureGizmos.BuildOptions(host).Single(item => item.Label.Contains("Enforce will over", StringComparison.Ordinal));
        option.action();
        Check(host.CurJobDef == DefDatabase<JobDef>.GetNamed(jobName),
            "The awakened cultivator starts timed will-enforcement from the aperture management menu.");

        TickUntil(() => record.RefinementProcess is { CompletedWorkTicks: > 1000 }, 1800,
            "Will-work advances and consumes essence on the same Gu identity.");
        var process = record.RefinementProcess;
        Check(GuRefinementRules.TryQuote(record.Species, record.Rank, null, cultivatorId, aperture, out var quote),
            "The native refiner has a valid prototype difficulty quote.");
        Check(process is { IsValid: true } && process.CultivatorId == cultivatorId && record.Refinement == null
            && gu.holdingOwner == host.inventory.GetDirectlyHeldThings() && record.CustodyRevision == custodyRevision
            && process.EssenceSpent > 0f && Math.Abs(process.TotalEssenceCost - 12f) < 0.001f
            && process.TotalWorkTicks == quote.WorkTicks,
            $"Captured Gu remains unbonded during exertion; identity/custody stay fixed. work={process?.CompletedWorkTicks}/{process?.TotalWorkTicks} expected={quote.WorkTicks}, spent={process?.EssenceSpent}, cost={process?.TotalEssenceCost}, custody={record.CustodyRevision}/{custodyRevision}, holder={gu.holdingOwner == host.inventory.GetDirectlyHeldThings()}, bonded={record.Refinement != null}");
        int interruptedProgress = process.CompletedWorkTicks;
        float interruptedSpend = process.EssenceSpent;
        string processId = process.ProcessId;
        host.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        Check(record.RefinementProcess?.ProcessId == processId && record.RefinementProcess.CompletedWorkTicks == interruptedProgress
            && Math.Abs(record.RefinementProcess.EssenceSpent - interruptedSpend) < 0.0001f && record.Refinement == null
            && record.CustodyRevision == custodyRevision,
            "Interruption preserves paid essence and completed will-work, without granting ownership or moving the Gu.");

        var resume = GuApertureGizmos.BuildOptions(host).Single(item => item.Label.Contains("Resume will-enforcement", StringComparison.Ordinal));
        resume.action();
        Check(host.CurJobDef == DefDatabase<JobDef>.GetNamed(jobName), "The same cultivator can resume its reserved refinement process.");
        TickUntil(() => record.RefinementProcess.CompletedWorkTicks > interruptedProgress + 500, 900,
            "Resumed will-enforcement advances the same process.");
        process = record.RefinementProcess;
        File.WriteAllText(RefinementFixturePath, JsonSerializer.Serialize(new RefinementFixture(guId, cultivatorId,
            apertureId, custodyRevision, process.ProcessId, process.TotalWorkTicks, process.CompletedWorkTicks,
            process.EssenceSpent, process.TotalEssenceCost)));
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Interrupted mortal refinement saved in progress with the original Gu identity, owner claim, paid essence, and inventory custody.");
    }

    private static void VerifySlice3B3CReload()
    {
        var fixture = JsonSerializer.Deserialize<RefinementFixture>(File.ReadAllText(RefinementFixturePath));
        var registry = IndividualRegistry;
        Pawn host = Find.Maps.SelectMany(map => map.mapPawns.AllPawnsSpawned)
            .Single(pawn => Current.Game.GetComponent<GuWorldComponent>().FindCultivator(pawn)?.CultivatorId == fixture.CultivatorId);
        var cultivator = Current.Game.GetComponent<GuWorldComponent>().FindCultivator(host);
        var aperture = host.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single(hediff => hediff.ApertureId == fixture.ApertureId);
        var record = registry.Resolve(fixture.GuId);
        var gu = ActuallyProjectedGu().OfType<Thing_GuDormantProjection>().Single(thing => thing.GuId == fixture.GuId);
        var process = record.RefinementProcess;
        Check(record.Refinement == null && process is { IsValid: true }
            && process.ProcessId == fixture.ProcessId && process.CultivatorId == fixture.CultivatorId
            && process.TotalWorkTicks == fixture.TotalWorkTicks && process.CompletedWorkTicks >= fixture.CompletedWorkTicks
            && process.EssenceSpent + 0.0001f >= fixture.EssenceSpent
            && record.CustodyRevision == fixture.CustodyRevision && gu.holdingOwner == host.inventory.GetDirectlyHeldThings()
            && host.CurJobDef == DefDatabase<JobDef>.GetNamed("GU_RefineMortalGu"),
            "Fresh-process reload resumes the saved refinement job and same Gu process without awarding allegiance early.");

        TickUntil(() => record.Refinement?.CultivatorId == cultivator.CultivatorId,
            fixture.TotalWorkTicks + 1000, "Will-enforcement completes and bonds the Gu only after all work and essence are paid.");
        Check(record.RefinementProcess == null && record.CustodyRevision == fixture.CustodyRevision
            && record.IsActionable && gu.holdingOwner == host.inventory.GetDirectlyHeldThings()
            && ActuallyProjectedGu().Count(thing => thing.GuTag()?.GuId == fixture.GuId) == 1
            && registry.IsRecognizedByCultivator(record, host),
            "Successful refinement sets one exact owner, clears the process, and conserves one physical Gu at unchanged custody.");
        Check(registry.TryStoreDormant(gu, gu.GuRevision, host, aperture, out string reason)
            && gu.Destroyed && registry.ApertureOccupants(aperture).Single() == record
            && registry.IsRecognizedByCultivator(record, host),
            $"The completed bond authorizes the existing mortal aperture custody path. {reason}");
        Report("Two-process lifecycle: interrupted will-work resumed after reload, spent essence was retained, one owner bond committed at completion, and that same identity entered its refiner's aperture.");
    }
}
