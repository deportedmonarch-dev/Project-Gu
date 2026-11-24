using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha1 => GenCommandLine.CommandLineArgPassed("gualpha1");
    private static JobDef RefinementJob => DefDatabase<JobDef>.GetNamed("GU_RefineMortalGu");
    private static string Alpha1StatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha1-state.json");

    private sealed record Alpha1State(string GuId, string ThingId, int CustodyRevision, string EventKey,
        string HabitatId, string CultivatorId, string ApertureId, string ProcessId, int WorkTicks, int CompletedTicks,
        float EssenceSpent, float MeasuredDebit, float EssenceAtSave, int SpringReserve);

    private static void ExerciseAlpha1(Pawn disciple, GuCultivatorRecord cultivator, Hediff_Aperture aperture,
        Thing springThing, CompGuSpiritSpring spring, Thing exit)
    {
        var registry = IndividualRegistry;
        var search = springThing.GetGizmos().OfType<Command_Action>()
            .Single(command => command.defaultLabel == "Search for Hope Gu");
        Check(!Prefs.DevMode && !registry.Records.Any() && spring.HopeGuAvailable == 2,
            "The ordinary spring command is available after awakening with two adults and no individual Gu");
        search.action();

        var record = registry.Records.Single();
        var wild = (Pawn_GuWildProjection)registry.ProjectionOf(record);
        Check(wild.Spawned && wild.Map == disciple.Map && wild.GuId == record.Id && ActuallyProjectedGu().Single() == wild
            && record.SpeciesDefName == "GU_HopeGu" && record.IsActionable && record.Refinement == null
            && record.CohortDebitCommitted && record.OriginId == spring.HabitatId && spring.HopeGuAvailable == 1,
            $"One natural Hope Gu {record.Id} emerged from the clan cavern, consuming one adult");
        int wildRevision = record.CustodyRevision;

        Order(disciple, wild, "Capture Hope Gu (unrefined)");
        TickUntil(() => registry.ProjectionOf(record) is Thing_GuDormantProjection, 2400,
            "The ordinary capture job produced a dormant Gu");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(record);
        Check(dormant.Spawned && !wild.Spawned && dormant.GuId == record.Id && ActuallyProjectedGu().Single() == dormant
            && record.CustodyRevision == wildRevision + 1 && record.Refinement == null,
            "Capture replaced the wild Pawn with one unrefined ground Thing of the same identity");

        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400,
            "The newly awakened disciple picked up the same physical Gu");
        Order(disciple, exit, "Use underground passage");
        TickUntil(() => disciple.Map == Home, 3000, "The disciple carried the Gu back to the colony");
        int packedRevision = record.CustodyRevision;
        AssertAlpha1Inventory(disciple, dormant, record, packedRevision);
        Check(packedRevision == wildRevision + 1 && record.Refinement == null && registry.ApertureClaimCount(aperture) == 0
            && !GuApertureGizmos.BuildOptions(disciple).Any(option => option.Label.StartsWith("Store ", StringComparison.Ordinal)),
            "Possession and passage conserved custody without granting ownership or an aperture slot");

        RunAlpha1ApertureOption(disciple, "Enforce will over", record.Id, $"{12f:0.0} essence");
        float spent = MeasureAlpha1Debit(disciple, aperture, () => record.RefinementProcess is { CompletedWorkTicks: >= 1000 }, 1800);
        var process = record.RefinementProcess;
        Check(process is { IsValid: true, TotalEssenceCost: 12f } && process.EssenceSpent > 0f
            && Math.Abs(spent - process.EssenceSpent) < 0.02f && record.Refinement == null,
            $"The job worked for {process?.CompletedWorkTicks} ticks and debited {spent:0.000} essence before granting ownership");

        disciple.drafter.Drafted = true;
        TickUntil(() => disciple.CurJobDef != RefinementJob, 60, "Drafting interrupted refinement");
        int interruptedProgress = process.CompletedWorkTicks;
        float interruptedSpend = process.EssenceSpent;
        TickUntil(() => false, 300);
        Check(process == record.RefinementProcess && process.CompletedWorkTicks == interruptedProgress
            && process.EssenceSpent == interruptedSpend && record.Refinement == null,
            "The interrupted job stopped charging and kept paid work without granting ownership");
        AssertAlpha1Inventory(disciple, dormant, record, packedRevision);
        var state = new Alpha1State(record.Id, dormant.ThingID, packedRevision, record.CreationEventKey,
            spring.HabitatId, cultivator.CultivatorId, aperture.ApertureId, process.ProcessId, process.TotalWorkTicks,
            interruptedProgress, interruptedSpend, spent, aperture.Essence, spring.Reserve);
        File.WriteAllText(Alpha1StatePath, JsonSerializer.Serialize(state));
        AssertAlpha1Conservation(state, record);
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved interrupted will-enforcement, Gu {record.Id}, process {process.ProcessId}, {interruptedProgress}/{process.TotalWorkTicks} ticks.");
        AssertNoErrors("alpha1-new");
    }

    private static void VerifyAlpha1Interrupted()
    {
        var (state, disciple, aperture, record) = LoadAlpha1State();
        var dormant = (Thing_GuDormantProjection)IndividualRegistry.ProjectionOf(record);
        AssertAlpha1Inventory(disciple, dormant, record, state.CustodyRevision);
        var process = record.RefinementProcess;
        Check(dormant.ThingID == state.ThingId && disciple.Drafted && disciple.CurJobDef != RefinementJob
            && record.Refinement == null && process is { IsValid: true } && process.ProcessId == state.ProcessId
            && process.CultivatorId == state.CultivatorId && process.TotalWorkTicks == state.WorkTicks
            && process.CompletedWorkTicks == state.CompletedTicks && Math.Abs(process.EssenceSpent - state.EssenceSpent) < 0.0001f
            && Math.Abs(aperture.Essence - state.EssenceAtSave) < 0.0001f,
            "Fresh-process reload preserved the same inventory Thing, unowned Gu, interrupted process and paid essence");

        disciple.drafter.Drafted = false;
        RunAlpha1ApertureOption(disciple, "Resume will-enforcement", record.Id, "essence left");
        Check(record.RefinementProcess == process && process.CompletedWorkTicks == state.CompletedTicks,
            "The player resumed the existing process at saved progress");
        float remainingDebit = MeasureAlpha1Debit(disciple, aperture, () => record.Refinement != null, state.WorkTicks + 1000);
        Check(record.RefinementProcess == null && record.Refinement?.CultivatorId == state.CultivatorId
            && process.CompletedWorkTicks == process.TotalWorkTicks && Math.Abs(process.EssenceSpent - 12f) < 0.0001f
            && Math.Abs(state.MeasuredDebit + remainingDebit - 12f) < 0.02f,
            $"Completion created one owner after {process.CompletedWorkTicks} work ticks and {state.MeasuredDebit + remainingDebit:0.000} total essence");
        AssertAlpha1Inventory(disciple, dormant, record, state.CustodyRevision);

        RunAlpha1ApertureOption(disciple, "Store ", record.Id);
        Check(dormant.Destroyed, "The store action retired the physical inventory Thing");
        AssertAlpha1Stored(state, aperture, record);
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha1-reload");
    }

    private static void VerifyAlpha1Stored()
    {
        var (state, _, aperture, record) = LoadAlpha1State();
        AssertAlpha1Stored(state, aperture, record);
        Report($"Fresh-process reload preserved Gu {record.Id} solely in aperture {aperture.ApertureId}, bonded to {state.CultivatorId}.");
        AssertNoErrors("alpha1-stored");
    }

    private static (Alpha1State, Pawn, Hediff_Aperture, GuRecord) LoadAlpha1State()
    {
        Prefs.DevMode = false;
        var state = JsonSerializer.Deserialize<Alpha1State>(File.ReadAllText(Alpha1StatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var record = IndividualRegistry.Resolve(state.GuId);
        Check(disciple.Map == Home && aperture.ApertureId == state.ApertureId && FoundingDone && FoundedLetters == 1
            && Apertures(Clan(GuClanRole.Elder).Single()).Single().AptitudeGrade == "B" && Pits(Home).Count == 1,
            "Reload retained the same awakened clan, founding letter, surface pit and disciple's aperture");
        AssertAlpha1Conservation(state, record);
        return (state, disciple, aperture, record);
    }

    private static void AssertAlpha1Inventory(Pawn disciple, Thing_GuDormantProjection dormant, GuRecord record, int revision)
    {
        Check(dormant != null && dormant.GuId == record.Id && dormant.GuRevision == revision && record.CustodyRevision == revision
            && dormant.stackCount == 1 && dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings()
            && ActuallyProjectedGu().Single() == dormant && IndividualRegistry.Resolve(dormant) == record,
            "The exact disciple inventory owns the single physical manifestation at the same custody revision");
    }

    private static void AssertAlpha1Stored(Alpha1State state, Hediff_Aperture aperture, GuRecord record)
    {
        var registry = IndividualRegistry;
        Check(record.Refinement?.CultivatorId == state.CultivatorId && record.RefinementProcess == null
            && record.CustodyRevision == state.CustodyRevision + 1 && record.ContainerId == aperture.ApertureId
            && registry.ApertureClaimCount(aperture) == 1 && registry.ApertureOccupants(aperture).Single() == record
            && registry.IsRecognizedByCultivator(record, aperture.pawn) && !ActuallyProjectedGu().Any(),
            "One bond and aperture claim reference the original Gu with no remaining physical projection");
        AssertAlpha1Conservation(state, record);
    }

    private static void AssertAlpha1Conservation(Alpha1State state, GuRecord record)
    {
        var spring = Find.Maps.SelectMany(map => map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring")))
            .Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp.HabitatId == state.HabitatId);
        Check(record is { IsActionable: true, CohortDebitCommitted: true } && IndividualRegistry.Records.Single() == record
            && record.Id == state.GuId && record.CreationEventKey == state.EventKey && record.OriginId == state.HabitatId
            && spring.HopeGuAvailable == 1 && spring.HopeGuJuvenile == 0
            && spring.Reserve == state.SpringReserve && PhysicalStoneCount() == 60,
            "One Gu identity and event, one remaining mature Hope Gu, unchanged spring reserve and exactly 60 physical stones");
    }

    private static void RunAlpha1ApertureOption(Pawn disciple, string prefix, string guId, string detail = "")
    {
        Check(disciple.GetGizmos().OfType<Command_Action>().Any(command => command.defaultLabel == "Manage aperture Gu"),
            "The awakened disciple exposes Manage aperture Gu without Development Mode");
        var option = GuApertureGizmos.BuildOptions(disciple).Single(item =>
            item.Label.StartsWith(prefix, StringComparison.Ordinal) && item.Label.Contains(guId, StringComparison.Ordinal));
        Check(option.action != null && option.Label.Contains(detail, StringComparison.Ordinal), $"Ordinary option offered: {option.Label}");
        option.action();
        if (prefix != "Store ") Check(disciple.CurJobDef == RefinementJob, "Will-enforcement started through the aperture menu");
    }

    private static float MeasureAlpha1Debit(Pawn disciple, Hediff_Aperture aperture, Func<bool> done, int limit)
    {
        float startingEssence = aperture.Essence;
        float recovered = 0f;
        for (int tick = 0; tick < limit && !done(); tick++)
        {
            Find.TickManager.DoSingleTick();
            if (disciple.IsHashIntervalTick(250)) recovered += aperture.RegenerationPerHour / 10f;
        }
        Check(done(), $"Will-work did not advance as expected within {limit} ticks (job {disciple.CurJobDef?.defName}).");
        return startingEssence + recovered - aperture.Essence;
    }
}
