using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

[StaticConstructorOnStartup]
internal static class GuRegistryLoadTiming
{
    private const string StagePrefix = "-guperf-stage=";
    internal static string Stage => Environment.GetCommandLineArgs()
        .FirstOrDefault(arg => arg.StartsWith(StagePrefix, StringComparison.Ordinal))?.Substring(StagePrefix.Length);
    internal static double InitLoadingMs { get; private set; }
    internal static double LoadGameMs { get; private set; }
    internal static long LoadGameManagedBytes { get; private set; }

    static GuRegistryLoadTiming()
    {
        if (Stage == null) return;
        var harmony = new Harmony("projectgu.verification.registry-load-timing");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading)),
            prefix: new HarmonyMethod(typeof(GuRegistryLoadTiming), nameof(BeginInitLoading)),
            postfix: new HarmonyMethod(typeof(GuRegistryLoadTiming), nameof(EndInitLoading)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Game), nameof(Game.LoadGame)),
            prefix: new HarmonyMethod(typeof(GuRegistryLoadTiming), nameof(BeginGameLoad)),
            postfix: new HarmonyMethod(typeof(GuRegistryLoadTiming), nameof(EndGameLoad)));
    }

    private static void BeginInitLoading(string __0, out long __state) =>
        __state = string.Equals(Path.GetFileName(__0), "autostart.rws", StringComparison.OrdinalIgnoreCase)
            ? Stopwatch.GetTimestamp() : 0;

    private static void EndInitLoading(long __state)
    {
        if (__state != 0) InitLoadingMs = Stopwatch.GetElapsedTime(__state).TotalMilliseconds;
    }

    private static void BeginGameLoad(out (long Timestamp, long Bytes) __state) =>
        __state = (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    private static void EndGameLoad((long Timestamp, long Bytes) __state)
    {
        LoadGameManagedBytes = GC.GetAllocatedBytesForCurrentThread() - __state.Bytes;
        LoadGameMs = Stopwatch.GetElapsedTime(__state.Timestamp).TotalMilliseconds;
        Find.TickManager.Pause();
    }
}

public sealed partial class LiveLifecycleChecks
{
    private const int BenchmarkHostCount = 120;
    private static bool Slice3B5BPerf => GenCommandLine.CommandLineArgPassed("guslice3b5bperf");
    private static string BenchmarkFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-perf-fixture.json");

    private record BenchmarkFixture(int Tick, string MapId, string[] HostAndApertureIds);
    private record TimedOperation(double Microseconds, double ManagedBytes);
    private record BenchmarkSample(string Stage, int Records, int Hosts, int PhysicalGu, long SaveBytes, double SaveMs,
        double InitLoadingMs, double LoadGameMs, long LoadGameManagedBytes, TimedOperation Rebuild,
        TimedOperation OrdinaryRegistryTick, TimedOperation ValidationPoll, TimedOperation WorldTick);

    private static void PrepareGuRegistryBenchmark()
    {
        Map map = Find.CurrentMap;
        var hosts = map.mapPawns.FreeColonistsSpawned.ToList();
        var emptyCells = CellRect.CenteredOn(map.Center, 22).Cells.Where(cell => IsEmptyInventoryCell(map, cell))
            .Take(BenchmarkHostCount - hosts.Count).ToArray();
        Check(emptyCells.Length == BenchmarkHostCount - hosts.Count, "Enough map cells for the same 120 native hosts in both snapshots");
        foreach (IntVec3 cell in emptyCells)
        {
            var host = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                tile: map.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true,
                forceNoGear: true, fixedBiologicalAge: 25, developmentalStages: DevelopmentalStage.Adult));
            GenSpawn.Spawn(host, cell, map);
            hosts.Add(host);
        }
        var apertures = hosts.Select(AddProbeAperture).ToArray();
        var fixture = new BenchmarkFixture(Find.TickManager.TicksGame, map.GetUniqueLoadID(), BenchmarkHostIds(map));
        Check(fixture.HostAndApertureIds.Length == BenchmarkHostCount && !IndividualRegistry.Individuals.Any()
            && !ActuallyProjectedGu().Any(), "Baseline has 120 real hosts and apertures and zero Gu records");
        File.WriteAllText(BenchmarkFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame("gu-perf-empty");

        // Synthetic development-origin records isolate persistence and indexing from population changes and native spawning.
        // Each record refers to a different real aperture. The normal load validator must accept every binding.
        var registry = IndividualRegistry;
        var records = (List<GuIndividual>)AccessTools.Field(typeof(GuIndividualRegistry), "individuals").GetValue(registry);
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        foreach (var aperture in apertures)
        {
            var record = new GuIndividual(Guid.NewGuid().ToString("N"), species, "development", fixture.MapId, fixture.Tick, "benchmark-setup");
            record.CommitDormancy("benchmark-setup");
            record.CommitCustody(new(GuCustodyKind.DormantInventory, "benchmark-setup", aperture.pawn.GetUniqueLoadID()));
            record.CommitCustody(new(GuCustodyKind.Aperture, aperture.GetUniqueLoadID(), aperture.pawn.GetUniqueLoadID()));
            records.Add(record);
        }
        registry.LoadedGame();
        Check(apertures.Select(registry.ResolveApertureOccupant).Distinct().Count() == BenchmarkHostCount
            && apertures.All(aperture => registry.ResolveApertureOccupant(aperture) != null)
            && !ActuallyProjectedGu().Any() && Find.TickManager.TicksGame == fixture.Tick,
            "All 120 one-slot bindings are authorized and the native host simulation has not advanced");
        GameDataSaveLoader.SaveGame("gu-perf-contained");
        Report("Prepared matched 0/120 snapshots with the same 120 exact hosts and aperture Hediffs and zero physical Gu.");
    }

    private static void MeasureGuRegistryBenchmark()
    {
        string stage = GuRegistryLoadTiming.Stage;
        bool populated = stage.StartsWith("perf-contained-", StringComparison.Ordinal);
        int expected = populated ? BenchmarkHostCount : 0;
        var fixture = JsonSerializer.Deserialize<BenchmarkFixture>(File.ReadAllText(BenchmarkFixturePath));
        Map map = Find.CurrentMap;
        var registry = IndividualRegistry;
        var hosts = map.mapPawns.FreeColonistsSpawned;
        var occupants = hosts.Select(host => registry.ResolveApertureOccupant(host.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single()))
            .Where(record => record != null).ToArray();
        bool sameHosts = BenchmarkHostIds(map).SequenceEqual(fixture.HostAndApertureIds);
        Check(map.GetUniqueLoadID() == fixture.MapId && Find.TickManager.TicksGame == fixture.Tick
            && sameHosts && hosts.Count == BenchmarkHostCount,
            $"Matched snapshot expected {fixture.MapId} at tick {fixture.Tick}, got {map.GetUniqueLoadID()} at tick {Find.TickManager.TicksGame}"
            + $" with {hosts.Count} hosts. Exact host/Hediff pairs match: {sameHosts}.");
        Check(registry.Individuals.Count == expected && occupants.Length == expected
            && occupants.Select(record => record.Id).Distinct().Count() == expected && !ActuallyProjectedGu().Any()
            && GuRegistryLoadTiming.InitLoadingMs > 0 && GuRegistryLoadTiming.LoadGameMs > 0,
            $"Expected {expected} record-only bindings, got {registry.Individuals.Count} records and {occupants.Length} bindings."
            + $" XML load {GuRegistryLoadTiming.InitLoadingMs:F2} ms, game load {GuRegistryLoadTiming.LoadGameMs:F2} ms.");

        string source = GenFilePaths.FilePathForSavedGame(SaveName);
        long saveBytes = new FileInfo(source).Length;
        Check(!XDocument.Load(source).Descendants("guIndividualId").Any(), "The matched save contains no serialized Gu Pawn or Thing tags");
        const string timedSave = "gu-perf-timed";
        GameDataSaveLoader.SaveGame(timedSave);
        var save = MeasureOperation(() => GameDataSaveLoader.SaveGame(timedSave), 1, 3);
        Action rebuild = AccessTools.DeclaredMethod(typeof(GuIndividualRegistry), "RebuildBindings").CreateDelegate<Action>(registry);
        rebuild();
        var rebuildTiming = MeasureOperation(rebuild, 32, 5);

        var ticker = Find.TickManager;
        int originalTick = ticker.TicksGame;
        int pollTick = originalTick - originalTick % 250;
        TimedOperation ordinary, poll;
        try
        {
            ticker.DebugSetTicksGame(pollTick + 1);
            registry.GameComponentTick();
            ordinary = MeasureOperation(registry.GameComponentTick, 20000, 5);
            ticker.DebugSetTicksGame(pollTick);
            registry.GameComponentTick();
            poll = MeasureOperation(registry.GameComponentTick, 100, 5);
        }
        finally { ticker.DebugSetTicksGame(originalTick); }

        for (int tick = 0; tick < 64; tick++) ticker.DoSingleTick();
        var world = MeasureOperation(ticker.DoSingleTick, 50, 5);
        int projected = ActuallyProjectedGu().Count();
        Check(projected == 0 && registry.Individuals.Count == expected && registry.Individuals.All(record => !record.IsQuarantined),
            "Simulation retained the record-only population and generated no physical Gu");
        var sample = new BenchmarkSample(stage, expected, hosts.Count, projected, saveBytes, save.Microseconds / 1000,
            GuRegistryLoadTiming.InitLoadingMs, GuRegistryLoadTiming.LoadGameMs, GuRegistryLoadTiming.LoadGameManagedBytes,
            rebuildTiming, ordinary, poll, world);
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, $"{stage}.json"), JsonSerializer.Serialize(sample));
        Report($"Measured {stage} with {expected} authorized records, {hosts.Count} native hosts, and {projected} physical Gu.");
    }

    private static string[] BenchmarkHostIds(Map map) => map.mapPawns.FreeColonistsSpawned
        .Select(host => $"{host.GetUniqueLoadID()}/{host.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single().GetUniqueLoadID()}")
        .OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private static TimedOperation MeasureOperation(Action operation, int iterations, int samples)
    {
        var times = new double[samples];
        var allocations = new double[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++) operation();
            times[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / iterations;
            allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
        }
        Array.Sort(times);
        Array.Sort(allocations);
        return new(times[samples / 2], allocations[samples / 2]);
    }
}
