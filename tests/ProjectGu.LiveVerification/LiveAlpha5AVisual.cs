using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Jobs;
using ProjectGu.Sustenance;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered Alpha 5A inspection. Loads the save written with a feeding job in progress in a windowed game and photographs
/// the feeding in the world, the Aperture tab's Gu cards and nourishment panels for a sated, a hungry and a starving Gu,
/// the meal being committed, the Techniques restriction, a withering Gu on the Cultivation page, a harvested moon orchid
/// with its petals, and a dormant Gu's float menu. Gameplay assertions still run; the screenshots are for human inspection.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static void StartAlpha5AVisual()
    {
        shotFolderName = "alpha5a-screenshots";
        Directory.CreateDirectory(ShotFolder);
        visualScript = Alpha5AVisualScript().GetEnumerator();
    }

    private static IEnumerable<Func<bool>> Alpha5AVisualScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var state = JsonSerializer.Deserialize<Alpha5AState>(File.ReadAllText(Alpha5AStatePath));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        DisableFixtureAutosave();
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) SustenanceComponent.SetAutoTend(colonist, false);
        var aperture = Apertures(disciple).Single();
        var liquor = registry.Resolve(state.LiquorId);
        var moon = registry.Resolve(state.MoonId);
        var second = registry.Resolve(state.SecondId);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        Note($"Loaded feeding save at tick {Now}: Liquor {Snap(liquor).Level}, Moonlight {Snap(moon).Level}, carried Liquor {Snap(second).Level}; screen {Screen.width}x{Screen.height}, UI scale {Prefs.UIScale}");

        // 1. The feeding job in the world: the disciple offering petals.
        Check(disciple.jobs.curDriver is JobDriver_GuFeedGu { GuId: var fedId } && fedId == moon.Id, "The feeding job was restored");
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 9f);
        yield return Frames(8);
        foreach (var wait in Shot("00-feeding-in-world")) yield return wait;

        // 2. The Gu page: cards with meters, and the starving Moonlight Gu's nourishment panel during the feeding job.
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        yield return Frames(3);
        var tab = ApertureTab;
        InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Aperture));
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos + new Vector3(6f, 0f, 0f), 18f);
        tab.Page = ApertureTabPage.Gu;
        tab.SelectedGuId = moon.Id;
        int fills = tab.FillCount;
        yield return Frames(10);
        Check(tab.FillCount > fills, "The Gu page was drawn by the inspection pane");
        foreach (var wait in Shot("10-gu-moonlight-starving")) yield return wait;
        foreach (var (id, name) in new[] { (liquor.Id, "11-gu-liquor-sated"), (second.Id, "12-gu-carried-liquor-hungry") })
        {
            tab.SelectedGuId = id;
            yield return Frames(8);
            foreach (var wait in Shot(name)) yield return wait;
        }

        // 3. The meal is committed; the panel follows.
        tab.SelectedGuId = moon.Id;
        foreach (var wait in StepUntil(() => disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName, 600, "The feeding completed")) yield return wait;
        yield return Frames(10);
        Check(Snap(moon).StarvingTicks == 0, "The Moonlight Gu was fed");
        foreach (var wait in Shot("13-gu-moonlight-fed")) yield return wait;

        // 4. Techniques: the starvation restriction is described where it affects real abilities.
        tab.Page = ApertureTabPage.Techniques;
        yield return Frames(8);
        foreach (var wait in Shot("20-techniques-sustenance")) yield return wait;

        // 5. A withering Liquor Worm: Cultivation shows why refinement is unavailable; the Gu page offers feeding.
        Drain(liquor, Snap(liquor).NourishmentTicks + liquor.Species.feeding.ImpairTicks);
        tab.View.Invalidate();
        tab.Page = ApertureTabPage.Cultivation;
        yield return Frames(10);
        Check(tab.View.ChannelActions.SingleOrDefault()?.DisabledReason?.Contains("withering") == true, "The Cultivation page explains the withering Liquor Worm");
        foreach (var wait in Shot("30-cultivation-withering")) yield return wait;
        tab.Page = ApertureTabPage.Gu;
        tab.SelectedGuId = liquor.Id;
        yield return Frames(8);
        foreach (var wait in Shot("31-gu-liquor-withering")) yield return wait;
        tab.Page = ApertureTabPage.Techniques;
        yield return Frames(8);
        foreach (var wait in Shot("32-techniques-withering")) yield return wait;
        // Several UI scales for the Gu page.
        float uiScale = Prefs.UIScale;
        tab.Page = ApertureTabPage.Gu;
        foreach (float scale in new[] { 1.5f, 2f })
        {
            Prefs.UIScale = scale;
            yield return Frames(10);
            foreach (var wait in Shot($"4{(scale == 2f ? 1 : 0)}-gu-scale-{scale:0.0}")) yield return wait;
        }
        Prefs.UIScale = uiScale;
        yield return Frames(6);

        // 6. A harvested moon orchid and its petals in the world.
        Find.Selector.ClearSelection();
        var orchid = cave.listerThings.ThingsOfDef(OrchidDef).OfType<Plant>().OrderBy(plant => plant.Growth).First();
        var petals = SpawnItem(cave, PetalDef, 6, orchid.Position, 2);
        Find.Selector.Select(orchid, false, false);
        Find.CameraDriver.SetRootPosAndSize(orchid.DrawPos, 8f);
        yield return Frames(10);
        foreach (var wait in Shot("50-orchid-and-petals")) yield return wait;
        Find.Selector.ClearSelection();
        Find.Selector.Select(petals, false, false);
        yield return Frames(6);
        foreach (var wait in Shot("51-petals-selected")) yield return wait;

        // 7. A dormant Gu on the ground: its inspection line and the Feed float menu.
        Find.Selector.ClearSelection();
        var dormant = registry.ProjectionOf(second) as Thing_GuDormantProjection;
        Check(dormant != null && disciple.inventory.innerContainer.TryDrop(dormant, disciple.Position, cave, ThingPlaceMode.Near, out _), "Set the carried Liquor Worm down");
        SpawnItem(cave, ThingDefOf.Beer, 1, dormant.Position, 3);
        Find.Selector.Select(dormant, false, false);
        Find.CameraDriver.SetRootPosAndSize(dormant.DrawPos, 8f);
        var options = dormant.GetFloatMenuOptions(disciple).ToList();
        Check(options.Any(option => option.Label.StartsWith("Feed", StringComparison.Ordinal) && option.action != null), "A colonist can feed a dormant Gu on the ground");
        Find.WindowStack.Add(new FloatMenu(options));
        yield return Frames(10);
        foreach (var wait in Shot("60-dormant-gu-feed-menu")) yield return wait;
        Find.WindowStack.TryRemove(typeof(FloatMenu));
        yield return Frames(4);
        Note($"Done: Liquor {Snap(liquor).Level}, Moonlight {Snap(moon).Level} {Days(Snap(moon).NourishmentTicks)}, carried Liquor {Snap(second).Level}.");
    }
}
