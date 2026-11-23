using System.Diagnostics;
using System.Text.Json;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectGu.TpsProbe;

/// <summary>The engine disposes maps on exit. Headless maps have no GPU sections; the disposable tests use the same guard.</summary>
[StaticConstructorOnStartup]
internal static class TpsHeadlessExit
{
    static TpsHeadlessExit()
    {
        if (TpsConfig.Current == null || !GenCommandLine.CommandLineArgPassed("headless")) return;
        new Harmony("projectgu.tpsprobe.headless").Patch(AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.Dispose)),
            prefix: new HarmonyMethod(typeof(TpsHeadlessExit), nameof(HasSections)));
    }

    private static bool HasSections(Section[,] ___sections) => ___sections != null;
}

/// <summary>
/// M0 throughput probe. Uncapped mode drives TickManager.DoSingleTick from the frame update in batches of up to 50 ms
/// while native pacing is paused, timing every tick. Paced mode leaves native pacing on at a requested speed and measures
/// achieved ticks per wall-clock second. No patch touches the simulation; the only per-tick cost is two timestamps.
/// </summary>
public sealed class TpsProbe : GameComponent
{
    private const double BatchBudgetMs = 50;
    private enum Phase { Idle, Prepare, Warmup, Window, PacedWarmup, PacedWindow, Done }

    private static TpsConfig Config => TpsConfig.Current;
    private Phase phase = Phase.Idle;
    private int remainingTicks;
    private readonly List<double> windowTicks = new();
    private readonly List<UncappedWindow> uncapped = new();
    private readonly List<PacedWindow> paced = new();
    private int WindowCount => uncapped.Count + paced.Count;
    private readonly Stopwatch wall = new();
    private long windowStart;
    private int windowStartTick, windowFrames, pauses, forcedNormalFrames, totalFrames;
    private double multiplierSeconds, windowSeconds;
    private MemorySnapshot windowMemory;
    private object startSnapshot;
    private string failure;

    public TpsProbe(Game game) { }

    public override void StartedNewGame()
    {
        if (Config?.Prepare == true) { phase = Phase.Prepare; remainingTicks = Config.PrepareTicks; }
    }

    public override void LoadedGame()
    {
        if (Config == null || Config.Prepare) return;
        phase = Config.Mode == "paced" ? Phase.PacedWarmup : Phase.Warmup;
    }

    public override void GameComponentUpdate()
    {
        if (phase is Phase.Idle or Phase.Done || LongEventHandler.AnyEventNowOrWaiting) return;
        try
        {
            if (!wall.IsRunning) Begin();
            switch (phase)
            {
                case Phase.Prepare: RunPrepare(); break;
                case Phase.Warmup or Phase.Window: RunUncapped(); break;
                default: RunPaced(); break;
            }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Finish();
        }
    }

    private void Begin()
    {
        wall.Start();
        // Letters and threats must not pause a measurement. Prefs are written only to this run's own config folder.
        Prefs.AutomaticPauseMode = AutomaticPauseMode.Never;
        if (phase == Phase.Prepare) return;
        if (Config.Error != null) throw new ArgumentException(Config.Error);
        // Dev mode is required for the engine to load autostart.rws. Turn it off for the measured simulation.
        Prefs.DevMode = false;
        startSnapshot = Snapshot();
        if (phase == Phase.Warmup) { Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; remainingTicks = Config.WarmupTicks; }
        else
        {
            Find.TickManager.CurTimeSpeed = (TimeSpeed)Enum.Parse(typeof(TimeSpeed), Config.Speed, true);
            windowStart = Stopwatch.GetTimestamp();
        }
    }

    private void RunPrepare()
    {
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        long start = Stopwatch.GetTimestamp();
        while (remainingTicks > 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds < BatchBudgetMs)
        {
            Find.TickManager.DoSingleTick();
            remainingTicks--;
        }
        if (remainingTicks > 0) return;
        Validate();
        GameDataSaveLoader.SaveGame("tps-source");
        Finish();
    }

    private void RunUncapped()
    {
        var ticks = Find.TickManager;
        ticks.CurTimeSpeed = TimeSpeed.Paused;  // Native pacing adds no ticks while the probe drives them.
        long batch = Stopwatch.GetTimestamp();
        while (remainingTicks > 0 && Stopwatch.GetElapsedTime(batch).TotalMilliseconds < BatchBudgetMs)
        {
            long before = Stopwatch.GetTimestamp();
            ticks.DoSingleTick();
            double ms = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            if (phase == Phase.Window) windowTicks.Add(ms);
            remainingTicks--;
        }
        if (remainingTicks > 0) return;
        if (phase == Phase.Window) CloseUncappedWindow();
        if (WindowCount >= Config.Windows) { Finish(); return; }
        Validate();
        phase = Phase.Window;
        remainingTicks = Config.WindowTicks;
        windowTicks.Clear();
        windowMemory = MemorySnapshot.Take();
        windowStart = Stopwatch.GetTimestamp();
    }

    private void CloseUncappedWindow()
    {
        double wallMs = Stopwatch.GetElapsedTime(windowStart).TotalMilliseconds;
        var memory = MemorySnapshot.Take();
        uncapped.Add(new UncappedWindow(WindowCount + 1, TpsStatistics.SummarizeTicks(windowTicks, wallMs), memory.Since(windowMemory)));
    }

    private void RunPaced()
    {
        var ticks = Find.TickManager;
        if (ticks.Paused)
        {
            pauses++;
            ticks.CurTimeSpeed = (TimeSpeed)Enum.Parse(typeof(TimeSpeed), Config.Speed, true);
        }
        double frameSeconds = Stopwatch.GetElapsedTime(windowStart).TotalSeconds - windowSeconds;
        windowSeconds += frameSeconds;
        windowFrames++;
        totalFrames++;
        if (ticks.slower.ForcedNormalSpeed) forcedNormalFrames++;
        multiplierSeconds += ticks.TickRateMultiplier * frameSeconds;
        double limit = phase == Phase.PacedWarmup ? Config.PacedWarmupSeconds : Config.PacedWindowSeconds;
        if (windowSeconds < limit) return;
        if (phase == Phase.PacedWindow)
        {
            int advanced = ticks.TicksGame - windowStartTick;
            var memory = MemorySnapshot.Take();
            paced.Add(new PacedWindow(WindowCount + 1, advanced, windowSeconds, advanced / windowSeconds, windowFrames / windowSeconds,
                (double)advanced / windowFrames, TpsStatistics.TargetTps(multiplierSeconds / windowSeconds), memory.Since(windowMemory)));
            if (WindowCount >= Config.Windows) { Finish(); return; }
        }
        Validate();
        phase = Phase.PacedWindow;
        windowStartTick = ticks.TicksGame;
        windowFrames = 0;
        windowSeconds = 0;
        multiplierSeconds = 0;
        windowMemory = MemorySnapshot.Take();
        windowStart = Stopwatch.GetTimestamp();
    }

    private static void Validate()
    {
        if (Find.Maps.Count == 0) throw new InvalidOperationException("No map is loaded.");
        if (Find.GameEnder.gameEnding) throw new InvalidOperationException("The colony is ending; the measurement would not be comparable.");
    }

    private void Finish()
    {
        phase = Phase.Done;
        string folder = GenFilePaths.SaveDataFolderPath;
        string verdict = failure == null ? "PASS" : "FAIL";
        try
        {
            if (!Config.Prepare)
            {
                object summary = null;
                if (failure == null && uncapped.Count > 0)
                {
                    var spread = TpsStatistics.Spread(uncapped.Select(window => window.Ticks.SimulationTps));
                    summary = new
                    {
                        SimulationTps = spread, WallTps = TpsStatistics.Spread(uncapped.Select(window => window.Ticks.WallTps)),
                        MeanTickMs = TpsStatistics.Spread(uncapped.Select(window => window.Ticks.MeanTickMs)),
                        P99TickMs = TpsStatistics.Spread(uncapped.Select(window => window.Ticks.P99TickMs)),
                        Headroom = TpsStatistics.Headroom(spread.Median)
                    };
                }
                else if (failure == null && paced.Count > 0)
                {
                    summary = new
                    {
                        AchievedTps = TpsStatistics.Spread(paced.Select(window => window.AchievedTps)),
                        MeanTargetTps = TpsStatistics.Spread(paced.Select(window => window.MeanTargetTps)),
                        Pauses = pauses, ForcedNormalSpeedFrames = forcedNormalFrames, Frames = totalFrames
                    };
                }
                var report = new
                {
                    Stage = Config.Stage, ProcessId = Environment.ProcessId, Mode = Config.Mode, Speed = Config.Mode == "paced" ? Config.Speed : null,
                    Config.WarmupTicks, Config.WindowTicks, Config.PacedWarmupSeconds, Config.PacedWindowSeconds,
                    Mods = LoadedModManager.RunningModsListForReading.Select(mod => mod.PackageId).ToArray(),
                    GameComponents = Current.Game.components.Select(component => component.GetType().FullName).ToArray(),
                    Start = startSnapshot, End = Snapshot(), UncappedWindows = uncapped, PacedWindows = paced, Summary = summary, Failure = failure
                };
                File.WriteAllText(Path.Combine(folder, $"tps-{Config.Stage}.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                    }));
            }
        }
        catch (Exception error)
        {
            verdict = "FAIL";
            failure = (failure ?? "") + " Report could not be written: " + error;
        }
        File.AppendAllText(Path.Combine(folder, "priority0-result.txt"),
            $"{verdict} {Config.Stage}{(failure == null ? "" : " " + failure)}{Environment.NewLine}");
        UnityEngine.Application.Quit();
    }

    /// <summary>Population and map counts. Taken outside timed ticks.</summary>
    private static object Snapshot() => new
    {
        TicksGame = Find.TickManager.TicksGame,
        Maps = Find.Maps.Select(map => new
        {
            map.uniqueID, Size = $"{map.Size.x}x{map.Size.z}", map.IsPlayerHome, SpawnedPawns = map.mapPawns.AllPawnsSpawned.Count,
            Things = map.listerThings.AllThings.Count
        }).ToArray(),
        WorldPawnsAlive = Find.WorldPawns.AllPawnsAlive.Count,
        WorldPawnsDead = Find.WorldPawns.AllPawnsDead.Count,
        Factions = Find.FactionManager.AllFactionsListForReading.Count,
        Memory = MemorySnapshot.Take()
    };
}

/// <summary>Managed heap, allocation, collection and process memory figures.</summary>
public sealed record MemorySnapshot(long ManagedHeapBytes, long AllocatedBytes, int Gen0, int Gen1, int Gen2, long WorkingSetBytes, long PrivateBytes)
{
    public static MemorySnapshot Take()
    {
        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(GC.GetTotalMemory(false), GC.GetTotalAllocatedBytes(), GC.CollectionCount(0), GC.CollectionCount(1),
            GC.CollectionCount(2), process.WorkingSet64, process.PrivateMemorySize64);
    }

    /// <summary>Window deltas for allocation and collections, end-of-window values for sizes.</summary>
    public MemoryDelta Since(MemorySnapshot start) => new(AllocatedBytes - start.AllocatedBytes, Gen0 - start.Gen0, Gen1 - start.Gen1,
        Gen2 - start.Gen2, ManagedHeapBytes, WorkingSetBytes, PrivateBytes);
}

public sealed record MemoryDelta(long AllocatedBytes, int Gen0, int Gen1, int Gen2, long ManagedHeapBytes, long WorkingSetBytes, long PrivateBytes);

public sealed record UncappedWindow(int Index, TickSummary Ticks, MemoryDelta Memory);

public sealed record PacedWindow(int Index, int Ticks, double WallSeconds, double AchievedTps, double FramesPerSecond, double MeanTicksPerFrame,
    double MeanTargetTps, MemoryDelta Memory);
