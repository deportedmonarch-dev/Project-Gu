using System.Text.Json;
using ProjectGu;
using ProjectGu.Botany;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Presentation;
using ProjectGu.Springs;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered Alpha 5B inspection. Loads the mid-sowing save in a windowed game and photographs the Hope Gu ambience around the
/// spring (and its absence with the setting off), real on-screen multi-selection of moon orchids, the orchid right-click menu
/// and inspection, sowing in progress, the spring's encounter guide, and the Gu page's automatic-feeding history. Gameplay
/// assertions still run (drawn spirit count equals the cohort-derived count; selection contains only orchids; the cohort and
/// registry never change while spirits are drawn); the screenshots are for human inspection.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static void StartAlpha5BVisual()
    {
        shotFolderName = "alpha5b-screenshots";
        Directory.CreateDirectory(ShotFolder);
        visualScript = Alpha5BVisualScript().GetEnumerator();
    }

    private static IEnumerable<Func<bool>> Alpha5BVisualScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var state = JsonSerializer.Deserialize<Alpha5BState>(File.ReadAllText(Alpha5BStatePath));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        DisableFixtureAutosave();
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        var springThing = cave.listerThings.AllThings.First(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = springThing.TryGetComp<CompGuSpiritSpring>();
        var profile = spring.AmbientProfiles.Single();
        int expected = GuSpringAmbience.SpiritCount(spring, profile);
        int hope = spring.MatureCountFor(profile.species.defName) + spring.JuvenileCountFor(profile.species.defName);
        int records = registry.Records.Count;
        Note($"Loaded mid-sowing save at tick {Now}; {hope} living Hope Gu, {expected} ambient spirits expected; screen {Screen.width}x{Screen.height}");

        // 1. Ambient Hope Gu spirits around the spring, from the cohort; then none with the setting off.
        Find.Selector.ClearSelection();
        Find.CameraDriver.SetRootPosAndSize(springThing.TrueCenter(), 10f);
        yield return Frames(20);
        Check(GuSpringAmbience.LastFrameSpirits == expected, $"The frame drew {GuSpringAmbience.LastFrameSpirits} spirits for {expected} expected from {hope} living Hope Gu");
        foreach (var wait in Shot("00-hope-spirits")) yield return wait;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        yield return Frames(45);
        Find.TickManager.Pause();
        foreach (var wait in Shot("01-hope-spirits-later")) yield return wait;
        Check(spring.MatureCountFor(profile.species.defName) + spring.JuvenileCountFor(profile.species.defName) == hope && registry.Records.Count == records
            && !Find.Selector.SelectedObjects.Any(), "Drawing spirits changed no cohort, record or selection");
        ProjectGuMod.Settings.ambientSpirits = false;
        yield return Frames(6);
        Check(GuSpringAmbience.LastFrameSpirits == 0, "With the setting off no spirits are drawn");
        foreach (var wait in Shot("02-hope-spirits-off")) yield return wait;
        ProjectGuMod.Settings.ambientSpirits = true;

        // 2. On-screen multi-selection of orchids, as a double-click selects them.
        var orchids = cave.listerThings.ThingsOfDef(OrchidDef).OfType<Plant>().ToList();
        var focus = orchids.OrderBy(plant => plant.Position.DistanceTo(springThing.Position)).First();
        Find.CameraDriver.SetRootPosAndSize(focus.DrawPos, 14f);
        yield return Frames(10);
        var vanilla = ThingSelectionUtility.MultiSelectableThingsInScreenRectDistinct(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight)).ToList();
        var view = Find.CameraDriver.CurrentViewRect;
        int inView = orchids.Count(o => view.Contains(o.Position) && ThingSelectionUtility.SelectableByMapClick(o));
        Note($"View {view}; {inView} orchids in view; vanilla screen-rect enumerator returned {vanilla.Count} things ({vanilla.Count(t => t.def == OrchidDef)} orchids)");
        GuPlantPatches.SelectMatching(Find.Selector, focus, GuPlantPatches.ThingsInView(cave, OrchidDef));
        yield return Frames(6);
        var selected = Find.Selector.SelectedObjects.OfType<Thing>().ToList();
        Check(inView >= 2 && selected.Count == inView && selected.All(thing => thing.def == OrchidDef),
            $"Double-click selection picked all {inView} orchids in view and nothing else (selected {selected.Count})");
        foreach (var wait in Shot("10-orchids-multiselected")) yield return wait;

        // 3. One orchid: inspection with the plant framework summary, then the right-click menu.
        Find.Selector.ClearSelection();
        Find.Selector.Select(focus, false, false);
        Find.CameraDriver.SetRootPosAndSize(focus.DrawPos, 8f);
        yield return Frames(8);
        Check(focus.GetInspectString().Contains("Moonlight Gu"), "The orchid's inspection names the Gu its petals feed");
        foreach (var wait in Shot("11-orchid-inspect")) yield return wait;
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        var options = RightClick(disciple, focus).Where(o => o.Label.Contains("orchid", StringComparison.OrdinalIgnoreCase)).ToList();
        Check(options.Count >= 2, $"The orchid's right-click menu offers harvest and seed options ({string.Join(" | ", options.Select(o => o.Label))})");
        Find.WindowStack.Add(new FloatMenu(options));
        yield return Frames(10);
        foreach (var wait in Shot("12-orchid-right-click")) yield return wait;
        Find.WindowStack.TryRemove(typeof(FloatMenu));

        // 4. Sowing in progress at the saved moment.
        Find.Selector.ClearSelection();
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 8f);
        yield return Frames(8);
        Check(disciple.CurJobDef == GuPlantDefOf.GU_SowWithMaterial, "The disciple is sowing with a seed in hand");
        foreach (var wait in Shot("20-sowing-with-seed")) yield return wait;

        // 5. The spring: encounter guide and the lure explanation.
        Find.Selector.Select(springThing, false, false);
        Find.CameraDriver.SetRootPosAndSize(springThing.TrueCenter(), 9f);
        yield return Frames(10);
        Check(springThing.GetInspectString().Contains("Encounters:"), "The spring's inspection carries the encounter guide");
        foreach (var wait in Shot("30-spring-encounters")) yield return wait;

        // 6. The Gu page: automatic-feeding history.
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        yield return Frames(3);
        var tab = ApertureTab;
        InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Aperture));
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos + new Vector3(6f, 0f, 0f), 18f);
        tab.Page = ApertureTabPage.Gu;
        tab.SelectedGuId = state.MoonId;
        yield return Frames(10);
        foreach (var wait in Shot("40-gu-page-feeding-history")) yield return wait;
        Note($"Done at tick {Now}.");
    }
}
