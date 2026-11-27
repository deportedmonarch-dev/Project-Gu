using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Jobs;
using ProjectGu.Presentation;
using ProjectGu.Springs;
using RimWorld;
using Verse;
using Verse.AI;

/// <summary>
/// Alpha 4A native acceptance. Builds on the Alpha 3 save (a disciple channeling Liquor Worm refinement with a Moonlight
/// Gu beside it). Every control is exercised through the same <see cref="GuApertureAction"/> objects the Aperture tab
/// draws, with Development Mode off. Stage "aperture" covers tab visibility, accurate stage and mixed-quality values,
/// resuming the Liquor Worm channel, storage, withdrawal, nourishment and advancement, Moonblade from the native command,
/// a third Gu's will-enforcement with its effect through interruption, cancellation and effects-off, a nonhuman aperture
/// host, unauthorized and stale actions, then saves mid-refinement. Stage "reload" checks the fresh-process load and
/// completes the refinement.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha4A => GenCommandLine.CommandLineArgPassed("gualpha4a");
    private static string Alpha4AStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha4a-state.json");
    internal const string Alpha4ARefiningSave = "alpha4a-refining";

    private sealed record Alpha4AState(string LiquorId, string MoonId, string ThirdId, string CultivatorId, string ApertureId,
        string ProcessId, int CompletedWorkTicks, double EssenceSpent, int ThirdRevision);

    private static ITab_Pawn_Aperture ApertureTab => (ITab_Pawn_Aperture)InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Aperture));

    private static void VerifyAlpha4AAperture()
    {
        var (_, disciple, aperture, liquor) = LoadAlpha3State();
        var registry = IndividualRegistry;
        var moon = registry.Resolve(IndividualRegistry.ApertureOccupants(aperture).Single(record => record.Id != liquor.Id).Id);
        Map cave = disciple.Map;
        var fx = cave.GetComponent<GuFxMapComponent>();
        Check(!Prefs.DevMode, "Development Mode is off");

        // 1. The tab is added to pawn races and appears only for aperture hosts.
        var tab = ApertureTab;
        Check(ApertureTabInstaller.RacesWithTab > 1 && disciple.def.inspectorTabsResolved.Contains(tab), "The Aperture tab is installed on pawn races");
        Check(TabVisibleFor(disciple), "The Aperture tab appears for the awakened disciple");
        Pawn worker = Clan(GuClanRole.MortalWorker).FirstOrDefault(pawn => Apertures(pawn).Count == 0);
        Check(worker == null || !TabVisibleFor(worker), "The Aperture tab is hidden for a mortal without an aperture");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        Check(TabVisibleFor(elder), "The Aperture tab appears for the elder, another aperture host");

        // 2. Stage and essence quality are separate; the mixed sea is shown as it is.
        var view = new ApertureView();
        view.Refresh(disciple);
        var session = Ledger.ChannelFor(liquor.Id);
        Check(view.Valid && view.Disclosure == ApertureDisclosure.Full && view.CanManage && view.Rank == 1 && view.Stage == (int)RankOneStage.Initial
            && view.RankDef?.defName == "GU_ApertureRank1" && view.RankDef.essenceLabel == "Green Copper Primeval Essence",
            "The view reports Rank 1 Initial Stage with Green Copper Primeval Essence");
        Check(view.Amounts[0] == aperture.EssenceOfQuality(RankOneStage.Initial) && view.Amounts[1] == aperture.EssenceOfQuality(RankOneStage.Middle)
            && view.Amounts[1] >= 5 && view.Amounts[0] > 0 && view.Amounts[2] == 0 && view.Amounts[3] == 0
            && Math.Abs(view.Total - Total(aperture)) < 1e-9 && view.Capacity == aperture.MaxEssence,
            $"The view's exact breakdown matches the aperture (Initial {view.Amounts[0]:0.###}, Middle {view.Amounts[1]:0.###}) while the stage stays Initial");
        var orb = view.OrbInput();
        Check(orb.Stage == 0 && orb.Amounts[1] == view.Amounts[1] && orb.Amounts[0] == view.Amounts[0] && orb.Visual.Vitality == GuApertureVitality.Healthy,
            "The orb is tinted by the actual stage and draws the Initial and Middle layers separately, with no invented condition");
        Check((view.Activity & GuApertureActivity.EssenceRefinement) != 0 && view.ActiveChannel == session && session is { Active: true }
            && view.ActivityLabel.Contains("pulses"), $"The live Liquor Worm session shows as activity ({view.ActivityLabel})");
        var channel = view.ChannelActions.Single();
        Check(!channel.Available && channel.GuId == liquor.Id, $"A running session cannot be started twice ({channel.DisabledReason})");

        // 3. Liquor Worm refinement resumes through the Cultivation action; the orbit returns; nothing is charged again.
        disciple.drafter.Drafted = true;
        TickUntil(() => !session.Active, 3, "Drafting suspended the channel");
        view.Refresh(disciple);
        Check(!view.ChannelActions.Single().Available && view.NourishAction?.DisabledReason == "requires an undrafted, conscious cultivator",
            "A drafted cultivator's cultivation actions are refused with a reason");
        disciple.drafter.Drafted = false;
        view.Refresh(disciple);
        channel = view.ChannelActions.Single();
        int committed = Ledger.CommittedTotal;
        double before = Total(aperture);
        Check(channel.Available && channel.Label.StartsWith("Resume Liquor Worm refinement (", StringComparison.Ordinal),
            $"The Cultivation page offers to resume the same session ({channel.Label})");
        Check(channel.Execute(out string failure), failure ?? "resumed");
        TickUntil(() => session.Active && fx.CountKeyed(session.SessionId) == 1, 5, "The resumed session drives the waist orbit again");
        Check(Ledger.CommittedTotal == committed && Total(aperture) >= before - 1e-9, "Resuming through the tab charged nothing and committed nothing new");
        TickUntil(() => Ledger.ChannelFor(liquor.Id) == null, 3000, "The resumed session completed");
        TickUntil(() => false, 2);
        Check(fx.CountKeyed(session.SessionId) == 0, "Completion removed the orbit");

        // 4. Withdraw and store through the Gu page, with identity and custody conserved; stale and foreign actions refused.
        view.Refresh(disciple);
        Check(view.Gu.Count(entry => entry.Placement == GuApertureGuPlacement.Aperture) == 2 && view.Claims == 2
            && view.Gu.All(entry => entry.RecognizesHost && entry.Actions.Any(action => action.Kind == GuApertureActionKind.Withdraw)),
            "The Gu page lists both stored Gu as individuals, each recognizing the disciple and offering withdrawal");
        Check(view.KnownGu.Count == 2 && GuPortraits.SourceOf(moon.Species) == "wild" && GuPortraits.SourceOf(liquor.Species) == "wild",
            "Gu cards use each species' own wild sprite");
        int records = registry.Records.Count();
        int revision = moon.CustodyRevision;
        var withdraw = view.Gu.Single(entry => entry.Record == moon).Actions.Single(action => action.Kind == GuApertureActionKind.Withdraw);
        Check(withdraw.Execute(out failure), failure ?? "withdrawn");
        var dormant = registry.ProjectionOf(moon) as Thing_GuDormantProjection;
        Check(dormant != null && dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings() && moon.CustodyRevision == revision + 1
            && registry.Records.Count() == records && registry.Resolve(moon.Id) == moon && !GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == moon),
            "Withdrawal moved the same Moonlight Gu to inventory with one custody revision and no new record");
        Check(!withdraw.Execute(out failure) && failure != null && registry.ProjectionOf(moon) == dormant && moon.CustodyRevision == revision + 1,
            $"Re-running the stale withdrawal is refused and changes nothing ({failure})");
        Check(GuApertureActions.Withdraw(elder, Apertures(elder).Single(), registry, moon) == null
            && GuApertureActions.Store(elder, Apertures(elder).Single(), registry, dormant) == null,
            "The elder cannot withdraw or store a Gu that recognizes the disciple");
        view.Refresh(disciple);
        var moonEntry = view.Gu.Single(entry => entry.Record == moon);
        var store = moonEntry.Actions.Single(action => action.Kind == GuApertureActionKind.Store);
        Check(moonEntry.Placement == GuApertureGuPlacement.Inventory && store.Available, "The carried Moonlight Gu offers storage");
        Check(store.Execute(out failure), failure ?? "stored");
        AssertStoredOnly(registry, aperture, new[] { liquor, moon }, dormant);
        Check(moon.CustodyRevision == revision + 2 && registry.Records.Count() == records, "Storage returned the same individual with one more revision");

        // 5. Moonblade still comes from the pawn's native command bar.
        var target = SpawnTarget(cave, disciple.Position, 5, 10);
        var command = AbilityGizmos(disciple).Single(gizmo => gizmo.Ability == Moonblade);
        SetEssence(aperture, aperture.MaxEssence);
        Check(!command.Disabled, "The native Moonblade command is enabled");
        Check(GuAbilityService.TryOrderCast(disciple, moon.Id, Moonblade, target, out string reason), reason ?? "Moonblade ordered");
        TickUntil(() => Ledger.InFlight.Any(), Moonblade.warmupTicks + 5, "Moonblade launched from the native command");
        TickUntil(() => !Ledger.InFlight.Any(), 200, "Moonblade resolved");
        DespawnTarget(target);

        // 6. Nourish the walls and advance the stage through the Cultivation actions.
        view.Refresh(disciple);
        Check(view.AdvanceAction is { Available: false } && view.AdvanceAction.DisabledReason.Contains("more nourishment"),
            $"Advancing is refused until the walls are nourished ({view.AdvanceAction?.DisabledReason})");
        for (int attempt = 0; attempt < 40 && !aperture.CanAdvanceRankOne; attempt++)
        {
            SetEssence(aperture, aperture.MaxEssence);
            view.Refresh(disciple);
            Check(view.NourishAction.Execute(out failure), failure ?? "nourishing");
            Check(disciple.CurJob?.def == JobDriver_GuNourishAperture.NourishJob, "Nourishment started from the Cultivation page");
            TickUntil(() => aperture.CanAdvanceRankOne || disciple.CurJob?.def != JobDriver_GuNourishAperture.NourishJob, 30_000);
        }
        Check(aperture.CanAdvanceRankOne, "The walls were fully nourished");
        view.Refresh(disciple);
        Check(view.NourishAction is { Available: false } && view.AdvanceAction is { Available: true, Label: "Advance to Middle" },
            "With full walls nourishment is refused and advancement offered");
        Check(view.AdvanceAction.Execute(out failure) && aperture.Stage == RankOneStage.Middle && aperture.WallNourishment == 0, failure ?? "Advanced to Middle");
        view.Refresh(disciple);
        Check(view.Stage == (int)RankOneStage.Middle && view.OrbInput().WallArc == 0f, "The view follows the new stage");

        // 7. A third Gu: will-enforcement with its visible effect, interruption, cancellation and effects-off.
        var springThing = cave.listerThings.AllThings.First(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var known = registry.Records.ToHashSet();
        springThing.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == "Search for Moonlight Gu").action();
        var third = registry.Records.Single(record => !known.Contains(record));
        Order(disciple, registry.ProjectionOf(third), "Capture Moonlight Gu (unrefined)");
        TickUntil(() => registry.ProjectionOf(third) is Thing_GuDormantProjection, 2400, "Captured the third Gu");
        var thirdThing = (Thing_GuDormantProjection)registry.ProjectionOf(third);
        Order(disciple, thirdThing, $"Pick up {thirdThing.Label}");
        TickUntil(() => thirdThing.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, "Picked up the third Gu");
        view.Refresh(disciple);
        var thirdEntry = view.Gu.Single(entry => entry.Record == third);
        Check(thirdEntry.Placement == GuApertureGuPlacement.Inventory && !thirdEntry.RecognizesHost
            && thirdEntry.Actions.Select(action => action.Kind).SequenceEqual(new[] { GuApertureActionKind.EnforceWill })
            && !view.KnownGu.Contains(third),
            "An unrefined carried Gu offers only will-enforcement and grants no technique");
        SetEssence(aperture, aperture.MaxEssence);
        Check(thirdEntry.Actions[0].Execute(out failure) && disciple.CurJobDef == RefinementJob, failure ?? "Will-enforcement started from the Gu page");
        TickUntil(() => third.RefinementProcess != null, 10, "A refinement process began");
        string processId = third.RefinementProcess.ProcessId;
        TickUntil(() => GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 2, 5,
            "Will-enforcement shows its two suppression layers");
        view.Refresh(disciple);
        Check((view.Activity & GuApertureActivity.WillEnforcement) != 0 && view.ActiveRefinement?.ProcessId == processId,
            $"The aperture shows will-enforcement activity ({view.ActivityLabel})");

        int work = third.RefinementProcess.CompletedWorkTicks;
        disciple.drafter.Drafted = true;
        TickUntil(() => false, 2);
        Check(GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 0, "Drafting removed the effect at once");
        int afterStop = third.RefinementProcess.CompletedWorkTicks;
        TickUntil(() => false, 120);
        Check(third.RefinementProcess.ProcessId == processId && third.RefinementProcess.CompletedWorkTicks == afterStop && afterStop >= work,
            "The interrupted process keeps its progress and advances no further");
        disciple.drafter.Drafted = false;

        view.Refresh(disciple);
        var resume = view.Gu.Single(entry => entry.Record == third).Actions.Single(action => action.Kind == GuApertureActionKind.ResumeWill);
        Check(resume.Label.StartsWith("Resume will-enforcement:", StringComparison.Ordinal) && resume.Execute(out failure), failure ?? "resumed");
        TickUntil(() => GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 2, 5, "The resumed job re-attached the effect");
        IntVec3 away = GenRadial.RadialCellsAround(disciple.Position, 4, true).First(cell => cell != disciple.Position && cell.Standable(cave));
        disciple.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, away), JobTag.Misc);
        TickUntil(() => false, 2);
        Check(GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 0, "Ordering another job removed the effect at once");

        view.Refresh(disciple);
        Check(view.Gu.Single(entry => entry.Record == third).Actions.Single(action => action.Kind == GuApertureActionKind.ResumeWill).Execute(out failure), failure ?? "resumed");
        TickUntil(() => GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 2, 5, "Re-attached after cancellation");
        ProjectGuMod.Settings.cosmeticEffects = false;
        try
        {
            int progress = third.RefinementProcess.CompletedWorkTicks;
            TickUntil(() => false, 60);
            Check(GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 0 && fx.ActiveCount == 0
                && third.RefinementProcess.CompletedWorkTicks > progress,
                "With effects off nothing is drawn while will-enforcement continues identically");
        }
        finally { ProjectGuMod.Settings.cosmeticEffects = true; }
        TickUntil(() => GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, processId) == 2, 35, "Re-enabling effects re-attached exactly the two layers");

        // 8. A nonhuman aperture host: no human assumptions, no unauthorized controls, nothing hidden revealed.
        VerifyNonhumanHost(cave, disciple.Position);

        // 9. Save part way through will-enforcement.
        TickUntil(() => third.RefinementProcess.CompletedWorkTicks > 600, 2000);
        var process = third.RefinementProcess;
        var state = new Alpha4AState(liquor.Id, moon.Id, third.Id, Cultivators.FindCultivator(disciple).CultivatorId, aperture.ApertureId,
            process.ProcessId, process.CompletedWorkTicks, process.EssenceSpent, third.CustodyRevision);
        File.WriteAllText(Alpha4AStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(Alpha4ARefiningSave);
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved during will-enforcement {process.ProcessId}: {process.CompletedWorkTicks}/{process.TotalWorkTicks} ticks, {process.EssenceSpent:0.###} essence; stage {aperture.Stage}.");
        AssertNoErrors("alpha4a-aperture");
    }

    // Stage "reload": a fresh process during will-enforcement.
    private static void VerifyAlpha4AReload()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        var state = JsonSerializer.Deserialize<Alpha4AState>(File.ReadAllText(Alpha4AStatePath));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var third = registry.Resolve(state.ThirdId);
        Map map = disciple.Map;
        int cavernRecords = registry.Records.Count(record => record.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind);
        Check(cavernRecords == 3 && registry.ApertureOccupants(aperture).Select(record => record.Id).ToHashSet().SetEquals(new[] { state.LiquorId, state.MoonId })
            && third?.RefinementProcess?.ProcessId == state.ProcessId && third.RefinementProcess.CompletedWorkTicks == state.CompletedWorkTicks
            && Math.Abs(third.RefinementProcess.EssenceSpent - state.EssenceSpent) < 0.0001 && third.CustodyRevision == state.ThirdRevision,
            $"Reload kept exactly three cavern Gu: two stored and the third mid-refinement with its exact progress (records {cavernRecords}, "
            + $"occupants {string.Join(",", registry.ApertureOccupants(aperture).Select(record => record.Id))}, process {third?.RefinementProcess?.ProcessId}, "
            + $"work {third?.RefinementProcess?.CompletedWorkTicks}/{state.CompletedWorkTicks}, spent {third?.RefinementProcess?.EssenceSpent:R}/{state.EssenceSpent:R}, "
            + $"revision {third?.CustodyRevision}/{state.ThirdRevision})");
        Check(ActuallyProjectedGu().Count(thing => registry.FindRecord(thing.GuTag()?.GuId)?.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind) == 1
            && registry.ProjectionOf(third) is Thing_GuDormantProjection held
            && held.holdingOwner == disciple.inventory.GetDirectlyHeldThings(),
            "Each Gu has exactly one placement: two in the aperture, one carried, no duplicate projection");
        Check(disciple.jobs.curDriver is JobDriver_GuRefineMortalGu driver && driver.ProcessId == state.ProcessId, "The will-enforcement job was restored");
        Check(GuRefinementPresentation.Count(map, GuRefinementKind.WillEnforcement, state.ProcessId) == 0, "No effect is replayed from the save");
        TickUntil(() => GuRefinementPresentation.Count(map, GuRefinementKind.WillEnforcement, state.ProcessId) == 2, 3,
            "The restored job re-attached the effect from its saved process");
        var view = new ApertureView();
        view.Refresh(disciple);
        Check(view.Stage == (int)RankOneStage.Middle && (view.Activity & GuApertureActivity.WillEnforcement) != 0, "The view rebuilds from the loaded state");

        SetEssence(aperture, aperture.MaxEssence);
        TickUntil(() => third.Refinement != null, 40_000, "The reloaded will-enforcement completed");
        TickUntil(() => false, 2);
        Check(GuRefinementPresentation.Count(map, GuRefinementKind.WillEnforcement, state.ProcessId) == 0 && third.RefinementProcess == null,
            "Completion removed the effect");
        view.Refresh(disciple);
        var entry = view.Gu.Single(item => item.Record == third);
        var store = entry.Actions.Single(action => action.Kind == GuApertureActionKind.Store);
        Check(entry.RecognizesHost && !store.Available && store.DisabledReason.Contains("full (2/2)") && view.KnownGu.Contains(third),
            $"The newly refined Gu cannot enter the full aperture ({store.DisabledReason}) and is listed among known Gu");
        Check(!store.Execute(out _) && registry.ApertureOccupants(aperture).Count() == 2, "A refused storage changes nothing");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha4a-reload");
    }

    private static void VerifyNonhumanHost(Map map, IntVec3 near)
    {
        var animal = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(animal, FindTargetCell(map, near, 2, 8, emptyCell: true), map);
        var hediff = (Hediff_Aperture)HediffMaker.MakeHediff(GuWorldComponent.ApertureDef, animal);
        hediff.Initialize(50);
        animal.health.AddHediff(hediff);
        try
        {
            Check(TabVisibleFor(animal), "The Aperture tab appears for a nonhuman aperture host");
            var view = new ApertureView();
            view.Refresh(animal);
            Check(view.Valid && view.Disclosure == ApertureDisclosure.Observed && !view.CanManage && view.Gu.Count == 0 && view.KnownGu.Count == 0
                && view.NourishAction == null && view.AdvanceAction == null && view.ChannelActions.Count == 0 && view.OrbInput().Amounts == null,
                "A wild nonhuman host is observed only: rank and stage, no essence, no Gu and no controls");
            animal.SetFaction(Faction.OfPlayer);
            view.Refresh(animal);
            Check(view.Disclosure == ApertureDisclosure.Full && !view.CanManage && view.NourishAction == null && view.Amounts[0] == hediff.EssenceOfQuality(RankOneStage.Initial),
                "A tamed nonhuman host shows its full aperture but offers no controls a colonist's body would need");
            Check(!animal.GetGizmos().OfType<Command_Action>().Any(gizmo => gizmo.defaultLabel == "Manage aperture Gu") && GuApertureGizmos.BuildOptions(animal).Count >= 1,
                "No management command is offered for the animal, and the read-only menu builder tolerates it");
        }
        finally
        {
            animal.Destroy(DestroyMode.Vanish);
        }
    }

    private static bool TabVisibleFor(Pawn pawn)
    {
        if (pawn.Map != null) Current.Game.CurrentMap = pawn.Map;
        Find.Selector.ClearSelection();
        Find.Selector.Select(pawn, false, false);
        bool visible = ApertureTab.IsVisible;
        Find.Selector.ClearSelection();
        return visible;
    }
}
