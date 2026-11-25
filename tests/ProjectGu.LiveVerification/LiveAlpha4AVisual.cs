using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Presentation;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered Alpha 4A inspection. Loads the save written during will-enforcement in a windowed game and photographs the
/// suppression effect, then opens the real Aperture tab through the inspection pane and photographs every page, the
/// living sea while Liquor Worm refinement and wall nourishment run, the developer condition previews, three UI scales
/// and a nonhuman host. Gameplay assertions still run; the screenshots are for human inspection.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static void StartAlpha4AVisual()
    {
        shotFolderName = "alpha4a-screenshots";
        Directory.CreateDirectory(ShotFolder);
        visualScript = Alpha4AVisualScript().GetEnumerator();
    }

    private static IEnumerable<Func<bool>> Alpha4AVisualScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var state = JsonSerializer.Deserialize<Alpha4AState>(File.ReadAllText(Alpha4AStatePath));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var third = registry.Resolve(state.ThirdId);
        var liquor = registry.Resolve(state.LiquorId);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        float uiScale = Prefs.UIScale;
        Note($"Loaded refining save: process {third?.RefinementProcess?.ProcessId}, stage {aperture.Stage}, UI scale {uiScale}, screen {Screen.width}x{Screen.height}");

        // 1. The will-enforcement effect in the world.
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 11f);
        foreach (var wait in StepUntil(() => GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, state.ProcessId) == 2, 10,
            "The restored will-enforcement job attached its effect")) yield return wait;
        foreach (var wait in StepTicks(45)) yield return wait;
        for (int shot = 0; shot < 6; shot++)
        {
            foreach (var wait in Shot($"00-will-enforcement-{shot}")) yield return wait;
            foreach (var wait in StepTicks(10)) yield return wait;
        }
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 20f);
        yield return Frames(6);
        foreach (var wait in Shot("01-will-enforcement-default-zoom")) yield return wait;

        // 2. Open the real tab through the inspection pane.
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        yield return Frames(3);
        var tab = ApertureTab;
        InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Aperture));
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos + new Vector3(6f, 0f, 0f), 18f);
        foreach (var page in new[] { ApertureTabPage.Cultivation, ApertureTabPage.Gu, ApertureTabPage.Techniques, ApertureTabPage.Foundation })
        {
            tab.Page = page;
            if (page == ApertureTabPage.Gu) tab.SelectedGuId = third.Id;
            int fills = tab.FillCount;
            foreach (var wait in StepTicks(8)) yield return wait;
            yield return Frames(6);
            Check(tab.FillCount > fills, $"The {page} page was drawn by the inspection pane");
            foreach (var wait in Shot($"1{(int)page}-tab-{page}-refining")) yield return wait;
        }
        Check(tab.Orb.Compositions > 0, "The living sea was composited");
        foreach (var gu in new[] { state.LiquorId, state.MoonId })
        {
            tab.Page = ApertureTabPage.Gu;
            tab.SelectedGuId = gu;
            yield return Frames(6);
            foreach (var wait in Shot($"15-tab-Gu-{registry.Resolve(gu).Species.defName}")) yield return wait;
        }

        // 3. Finish will-enforcement a few ticks per frame and show the newly refined Gu that cannot fit the full aperture.
        SetEssence(aperture, aperture.MaxEssence);
        for (int frame = 0; frame < 1800 && third.Refinement == null; frame++) // up to 21,600 ticks: low-aptitude disciples need ~12,000
        {
            for (int tick = 0; tick < 12 && third.Refinement == null; tick++) Find.TickManager.DoSingleTick();
            yield return Frames(1);
        }
        Check(third.Refinement != null, "Will-enforcement completed in the rendered session");
        yield return Frames(4);
        Check(GuRefinementPresentation.Count(cave, GuRefinementKind.WillEnforcement, state.ProcessId) == 0, "Completion removed the effect");
        tab.Page = ApertureTabPage.Gu;
        tab.SelectedGuId = third.Id;
        tab.View.Invalidate();
        yield return Frames(8);
        foreach (var wait in Shot("20-tab-Gu-refined-aperture-full")) yield return wait;
        tab.Page = ApertureTabPage.Techniques;
        yield return Frames(6);
        foreach (var wait in Shot("21-tab-Techniques-three-Gu")) yield return wait;

        // 4. The living sea while Liquor Worm refinement runs. Test-only shortcut: clear the recovery left by earlier stages.
        ((Dictionary<string, int>)typeof(GuCombatLedger).GetField("cooldownUntil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Ledger)).Remove(liquor.Id);
        SetEssence(aperture, aperture.MaxEssence * 0.7f);
        tab.Page = ApertureTabPage.Cultivation;
        tab.View.Invalidate();
        yield return Frames(4);
        var channel = tab.View.ChannelActions.Single();
        string failure = channel.DisabledReason;
        Check(channel.Available && channel.Execute(out failure), failure ?? "Liquor Worm refinement started from the Cultivation page");
        foreach (var wait in StepTicks(30)) yield return wait;
        Check(Ledger.ChannelFor(liquor.Id) is { Active: true } session && cave.GetComponent<GuFxMapComponent>().CountKeyed(session.SessionId) == 1,
            "The channel runs with its waist orbit");
        for (int shot = 0; shot < 3; shot++)
        {
            foreach (var wait in StepTicks(260)) yield return wait;
            yield return Frames(4);
            foreach (var wait in Shot($"30-cultivation-liquor-worm-{shot}")) yield return wait;
        }
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 9f);
        yield return Frames(6);
        foreach (var wait in Shot("31-liquor-worm-orbit-with-tab")) yield return wait;
        disciple.drafter.Drafted = true;
        yield return Frames(3);
        disciple.drafter.Drafted = false;
        tab.View.Invalidate();
        yield return Frames(6);
        foreach (var wait in Shot("32-cultivation-liquor-suspended")) yield return wait;

        // 5. Wall nourishment from the Cultivation page.
        tab.View.Refresh(disciple);
        Check(tab.View.NourishAction is { Available: true } nourish && nourish.Execute(out failure), "Nourishment started from the Cultivation page");
        foreach (var wait in StepTicks(40)) yield return wait;
        yield return Frames(6);
        foreach (var wait in Shot("40-cultivation-nourishing")) yield return wait;
        disciple.drafter.Drafted = true;
        yield return Frames(2);
        disciple.drafter.Drafted = false;

        // 6. Developer previews of the condition overlays (presentation only).
        Prefs.DevMode = true;
        foreach (var vitality in new[] { GuApertureVitality.Cracked, GuApertureVitality.Dead })
        {
            tab.DevPreview = vitality;
            yield return Frames(8);
            foreach (var wait in Shot($"50-preview-{vitality}")) yield return wait;
        }
        tab.DevPreview = null;
        Prefs.DevMode = false;

        // 7. UI scales.
        foreach (float scale in new[] { 1.5f, 2f })
        {
            Prefs.UIScale = scale;
            foreach (var page in new[] { ApertureTabPage.Cultivation, ApertureTabPage.Gu })
            {
                tab.Page = page;
                yield return Frames(10);
                foreach (var wait in Shot($"6{(int)page}-scale-{scale:0.0}-{page}")) yield return wait;
            }
        }
        Prefs.UIScale = uiScale;
        yield return Frames(6);

        // 8. A nonhuman host is observed only.
        var animal = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(animal, FindTargetCell(cave, disciple.Position, 2, 6, emptyCell: true), cave);
        var hediff = (Hediff_Aperture)HediffMaker.MakeHediff(GuWorldComponent.ApertureDef, animal);
        hediff.Initialize(50);
        animal.health.AddHediff(hediff);
        Find.Selector.ClearSelection();
        Find.Selector.Select(animal, false, false);
        InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Aperture));
        tab.Page = ApertureTabPage.Cultivation;
        Find.CameraDriver.SetRootPosAndSize(animal.DrawPos + new Vector3(6f, 0f, 0f), 18f);
        yield return Frames(10);
        Check(tab.View.Pawn == animal && tab.View.Disclosure == ApertureDisclosure.Observed, "The tab shows the wild animal's aperture as observed only");
        foreach (var wait in Shot("70-nonhuman-observed")) yield return wait;
        tab.Page = ApertureTabPage.Gu;
        yield return Frames(6);
        foreach (var wait in Shot("71-nonhuman-gu-concealed")) yield return wait;
        Find.Selector.ClearSelection();
        animal.Destroy(DestroyMode.Vanish);
        yield return Frames(4);
        Note($"Done: orb compositions {tab.Orb.Compositions}, tab fills {tab.FillCount}, surface row {tab.Orb.LastSurfaceRow:0.#}; stage {aperture.Stage}; "
            + $"essence I {aperture.EssenceOfQuality(RankOneStage.Initial):0.##} M {aperture.EssenceOfQuality(RankOneStage.Middle):0.##} U {aperture.EssenceOfQuality(RankOneStage.Upper):0.##}");
    }
}
