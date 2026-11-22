using System.Diagnostics;
using System.Text.Json;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

// Game-side stage timing for the live runner. Enabled only by -gutimingstage=<stage>; the benchmark profile never passes it,
// so its measured saves and loads carry no extra patches. Marks are kept in memory and written once when the stage ends,
// so no file I/O happens inside a measured operation. A crash before the flush loses only the game-side marks; the runner
// still records process launch and exit.
internal static class StageTiming
{
    private const string StagePrefix = "-gutimingstage=";
    private static readonly List<Mark> Marks = new();
    private static readonly DateTime ProcessStartUtc = GetProcessStartUtc();
    private static bool flushed;

    internal static string Stage { get; } = Environment.GetCommandLineArgs()
        .FirstOrDefault(arg => arg.StartsWith(StagePrefix, StringComparison.Ordinal))?.Substring(StagePrefix.Length);

    internal static bool Enabled => Stage != null;

    private record Mark(string Name, DateTime Utc, double SinceProcessStartMs);
    private record TimingFile(string Stage, int ProcessId, DateTime ProcessStartUtc, IReadOnlyList<Mark> Marks, IReadOnlyList<string> Unobservable);

    private static readonly List<string> Unobservable = new();

    private static DateTime GetProcessStartUtc()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    internal static void Record(string name)
    {
        if (!Enabled || flushed) return;
        DateTime now = DateTime.UtcNow;
        lock (Marks) Marks.Add(new Mark(name, now, (now - ProcessStartUtc).TotalMilliseconds));
    }

    internal static void NotObservable(string what)
    {
        if (Enabled) lock (Marks) Unobservable.Add(what);
    }

    internal static void Flush()
    {
        if (!Enabled || flushed) return;
        flushed = true;
        try
        {
            TimingFile file;
            lock (Marks) file = new TimingFile(Stage, Environment.ProcessId, ProcessStartUtc, Marks.ToArray(), Unobservable.ToArray());
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, $"timing-{Stage}.json"),
                JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            Log.Warning($"[Project GU tests] Stage timing could not be written: {error.Message}");
        }
    }
}

// Mod constructors run while mod assemblies are instantiated, before Def loading.
public sealed class StageTimingMod : Mod
{
    public StageTimingMod(ModContentPack content) : base(content) => StageTiming.Record("modInit");
}

// Static constructors run at the end of startup data loading, before the entry scene can quick-start or load a save.
[StaticConstructorOnStartup]
internal static class StageTimingPatches
{
    static StageTimingPatches()
    {
        if (!StageTiming.Enabled) return;
        StageTiming.Record("startupComplete");
        var harmony = new Harmony("projectgu.verification.stagetiming");
        Bracket(harmony, typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld), "worldGen");
        Bracket(harmony, typeof(Game), nameof(Game.InitNewGame), "newGame");
        Bracket(harmony, typeof(Game), nameof(Game.LoadGame), "load");
        Bracket(harmony, typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame), "save");
    }

    private static void Bracket(Harmony harmony, Type type, string method, string mark)
    {
        try
        {
            var targets = AccessTools.GetDeclaredMethods(type).Where(candidate => candidate.Name == method && !candidate.IsGenericMethodDefinition).ToArray();
            if (targets.Length == 0) { StageTiming.NotObservable($"{type.Name}.{method}"); return; }
            var prefix = new HarmonyMethod(typeof(StageTimingPatches), mark + "Start");
            var postfix = new HarmonyMethod(typeof(StageTimingPatches), mark + "End");
            foreach (var target in targets) harmony.Patch(target, prefix: prefix, postfix: postfix);
        }
        catch (Exception error)
        {
            StageTiming.NotObservable($"{type.Name}.{method}: {error.Message}");
        }
    }

    private static void worldGenStart() => StageTiming.Record("worldGen.start");
    private static void worldGenEnd() => StageTiming.Record("worldGen.end");
    private static void newGameStart() => StageTiming.Record("newGame.start");
    private static void newGameEnd() => StageTiming.Record("newGame.end");
    private static void loadStart() => StageTiming.Record("load.start");
    private static void loadEnd() => StageTiming.Record("load.end");
    private static void saveStart() => StageTiming.Record("save.start");
    private static void saveEnd() => StageTiming.Record("save.end");
}
