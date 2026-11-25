using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Interface;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered performance profile of the Aperture tab's orb. Loads the will-enforcement save in a windowed game, warms the
/// tab up, then measures fixed-length windows of real frames: tab closed, Cultivation page at Normal, Fast and Superfast
/// speed, paused, the Gu page (no orb), UI scale 2, Liquor Worm refinement running, and tab closed again as a drift check.
/// Each window records frame time (average, 95th percentile, worst), the orb's compositions, uploads and skips with their
/// average and worst cost, and managed allocations and gen-0 collections on the main thread. Results go to
/// alpha4a-perf.txt in the save-data folder. Gameplay is not asserted beyond what the profile needs.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private const int PerfWindowFrames = 300;
    private static readonly StringBuilder perfReport = new();
    // The optimized renderer exposes AnimationFps; the 47be46d baseline does not, and is measured without thresholds.
    private static readonly bool PerfThresholds = typeof(ApertureOrbRenderer).GetField("AnimationFps") != null;
    private static float PerfTargetFps => PerfThresholds ? (float)typeof(ApertureOrbRenderer).GetField("AnimationFps")!.GetValue(null)! : 0f;
    private static int lastWindowCompositions;
    private static float lastWindowSeconds;

    private static void StartAlpha4APerf()
    {
        shotFolderName = "alpha4a-perf";
        Directory.CreateDirectory(ShotFolder);
        visualScript = Alpha4APerfScript().GetEnumerator();
    }

    private static IEnumerable<Func<bool>> Alpha4APerfScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var state = JsonSerializer.Deserialize<Alpha4AState>(File.ReadAllText(Alpha4AStatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var liquor = IndividualRegistry.Resolve(state.LiquorId);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        float uiScale = Prefs.UIScale;
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos + new Vector3(6f, 0f, 0f), 18f);
        perfReport.Clear();
        perfReport.AppendLine($"Screen {Screen.width}x{Screen.height}, UI scale {uiScale}, {PerfWindowFrames} frames per window, Stopwatch {Stopwatch.Frequency} Hz");
        perfReport.AppendLine("window | frames | frame avg ms | p95 ms | max ms | ticks | orb compose n | avg ms | max ms | upload n | avg ms | max ms | skipped | static rebuilds | prepare avg ms | orb alloc KB (compose+upload) | frame alloc KB | gen0");

        var tab = ApertureTab;
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        yield return Frames(30);

        foreach (var wait in Measure("tab closed, normal", TimeSpeed.Normal)) yield return wait;

        InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Aperture));
        tab.Page = ApertureTabPage.Cultivation;
        yield return Frames(60); // Warm-up: layer reads and texture creation happen once here.
        foreach (var wait in Shot("perf-cultivation")) yield return wait;

        foreach (var wait in Measure("cultivation, normal", TimeSpeed.Normal)) yield return wait;
        if (PerfThresholds) Check(lastWindowCompositions <= PerfTargetFps * lastWindowSeconds * 1.25f + 3,
            $"The running orb animates at most about {PerfTargetFps} FPS ({lastWindowCompositions} compositions in {lastWindowSeconds:0.0} s)");
        foreach (var wait in Measure("cultivation, fast", TimeSpeed.Fast)) yield return wait;
        foreach (var wait in Measure("cultivation, superfast", TimeSpeed.Superfast)) yield return wait;
        foreach (var wait in Measure("cultivation, paused", TimeSpeed.Paused)) yield return wait;
        if (PerfThresholds) Check(lastWindowCompositions <= 2, $"A paused, unchanged orb is not recomposed ({lastWindowCompositions} compositions in {lastWindowSeconds:0.0} s)");
        tab.Page = ApertureTabPage.Gu;
        yield return Frames(10);
        foreach (var wait in Measure("gu page, normal", TimeSpeed.Normal)) yield return wait;
        tab.Page = ApertureTabPage.Cultivation;
        Prefs.UIScale = 2f;
        yield return Frames(20);
        foreach (var wait in Shot("perf-scale-2")) yield return wait;
        foreach (var wait in Measure("cultivation, UI scale 2, normal", TimeSpeed.Normal)) yield return wait;
        Prefs.UIScale = uiScale;
        yield return Frames(20);

        // Liquor Worm refinement running (refinement currents). Test-only shortcut: clear the leftover recovery.
        ((Dictionary<string, int>)typeof(GuCombatLedger).GetField("cooldownUntil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Ledger)).Remove(liquor.Id);
        SetEssence(Apertures(disciple).Single(), Apertures(disciple).Single().MaxEssence * 0.7f);
        if (disciple.Drafted) disciple.drafter.Drafted = false;
        Check(GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string reason), reason ?? "channel ordered");
        foreach (var wait in StepTicks(3)) yield return wait;
        yield return Frames(10);
        foreach (var wait in Measure("cultivation, liquor worm channel, normal", TimeSpeed.Normal)) yield return wait;
        foreach (var wait in Shot("perf-liquor")) yield return wait;

        ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).CloseOpenTab();
        yield return Frames(10);
        Check(Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect || ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType != typeof(ITab_Pawn_Aperture),
            "The tab is closed for the drift check");
        foreach (var wait in Measure("tab closed again, normal", TimeSpeed.Normal)) yield return wait;

        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha4a-perf.txt"), perfReport.ToString());
        Note(perfReport.ToString());
    }

    private static IEnumerable<Func<bool>> Measure(string label, TimeSpeed speed)
    {
        var frameMs = new float[PerfWindowFrames];
        DebugViewSettings.neverForceNormalSpeed = true; // Test only: measure real Fast and Superfast despite danger alerts.
        if (speed == TimeSpeed.Paused) Find.TickManager.Pause();
        else Find.TickManager.CurTimeSpeed = speed;
        yield return Frames(5);
        ApertureOrbStats.Reset();
        int startTick = Find.TickManager.TicksGame;
        int gen0 = GC.CollectionCount(0);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < PerfWindowFrames; i++)
        {
            yield return Frames(1);
            frameMs[i] = Time.unscaledDeltaTime * 1000f;
        }
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        lastWindowCompositions = ApertureOrbStats.Compositions;
        lastWindowSeconds = frameMs.Sum() / 1000f;
        int ticks = Find.TickManager.TicksGame - startTick;
        Find.TickManager.Pause();
        var sorted = (float[])frameMs.Clone();
        Array.Sort(sorted);
        perfReport.AppendLine($"{label} | {PerfWindowFrames} | {frameMs.Average():0.00} | {sorted[(int)(PerfWindowFrames * 0.95f)]:0.00} | {sorted[^1]:0.00} | {ticks} | "
            + $"{ApertureOrbStats.Compositions} | {ApertureOrbStats.ComposeAverageMs:0.000} | {ApertureOrbStats.ComposeMaxMs:0.000} | "
            + $"{ApertureOrbStats.Uploads} | {ApertureOrbStats.UploadAverageMs:0.000} | {ApertureOrbStats.UploadMaxMs:0.000} | {ApertureOrbStats.Skipped} | {ApertureOrbStats.StaticRebuilds} | {ApertureOrbStats.PrepareAverageMs:0.000} | {ApertureOrbStats.ComposeBytes / 1024.0:0}+{ApertureOrbStats.UploadBytes / 1024.0:0} | "
            + $"{(allocatedAfter - allocated) / 1024.0:0} | {GC.CollectionCount(0) - gen0}");
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha4a-perf.txt"), perfReport.ToString());
    }
}
