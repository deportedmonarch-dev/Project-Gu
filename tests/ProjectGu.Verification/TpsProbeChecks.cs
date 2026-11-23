using ProjectGu.TpsProbe;

/// <summary>Managed checks for the M0 throughput probe's statistics and argument parsing. No game process runs.</summary>
internal static class TpsProbeChecks
{
    internal static void Run()
    {
        // Percentiles interpolate linearly between ranks of the sorted samples.
        var sorted = new double[] { 1, 2, 3, 4 };
        Check(TpsStatistics.Percentile(sorted, 50) == 2.5, "Median of an even count interpolates");
        Check(TpsStatistics.Percentile(sorted, 0) == 1 && TpsStatistics.Percentile(sorted, 100) == 4, "Percentile bounds are the extremes");
        Check(Math.Abs(TpsStatistics.Percentile(sorted, 95) - 3.85) < 1e-9, "95th percentile interpolates between the top ranks");
        Check(double.IsNaN(TpsStatistics.Percentile(Array.Empty<double>(), 50)), "An empty sample has no percentile");

        // 100 ticks of 2 ms in 250 ms of wall time: simulation 500 TPS, wall 400 TPS.
        var window = TpsStatistics.SummarizeTicks(Enumerable.Repeat(2.0, 100).ToArray(), 250);
        Check(window.Ticks == 100 && Math.Abs(window.SimulationMs - 200) < 1e-9, "Window sums tick durations");
        Check(Math.Abs(window.SimulationTps - 500) < 1e-9 && Math.Abs(window.WallTps - 400) < 1e-9, "Simulation and wall TPS are separate");
        Check(window.P50TickMs == 2 && window.P99TickMs == 2 && window.MaxTickMs == 2, "Uniform ticks have uniform percentiles");
        var spiky = TpsStatistics.SummarizeTicks(new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 41 }, 50);
        Check(spiky.MaxTickMs == 41 && spiky.P50TickMs == 1 && Math.Abs(spiky.MeanTickMs - 5) < 1e-9, "A spike moves the mean and maximum, not the median");
        Check(Throws(() => TpsStatistics.SummarizeTicks(Array.Empty<double>(), 1)), "An empty window is rejected");

        // Sample standard deviation (n - 1) and coefficient of variation.
        var spread = TpsStatistics.Spread(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });
        Check(spread.Count == 8 && spread.Median == 4.5 && spread.Mean == 5 && spread.Min == 2 && spread.Max == 9, "Spread summary");
        Check(Math.Abs(spread.StdDev - Math.Sqrt(32.0 / 7)) < 1e-9, "Spread uses the sample standard deviation");
        Check(Math.Abs(spread.CvPercent - 100 * Math.Sqrt(32.0 / 7) / 5) < 1e-9, "Coefficient of variation is relative to the mean");
        Check(TpsStatistics.Spread(new double[] { 3 }).StdDev == 0, "One sample has no spread");
        Check(TpsStatistics.Spread(new[] { double.NaN, 1.0 }).Count == 1, "NaN samples are ignored");

        // Targets follow the 1.7 TickManager: 60 ticks per second times the speed multiplier.
        Check(TpsStatistics.TargetTps(3) == 180 && TpsStatistics.TargetTps(6) == 360 && TpsStatistics.TargetTps(15) == 900, "Vanilla speed targets");
        var headroom = TpsStatistics.Headroom(400);
        Check(headroom.Single(target => target.TargetTps == 360).Met && !headroom.Single(target => target.TargetTps == 720).Met, "Headroom against targets");
        Check(Math.Abs(headroom.Single(target => target.TargetTps == 180).Headroom - 400.0 / 180) < 1e-9, "Headroom is measured over target");

        // Arguments. Without -gutps the probe is inert.
        Check(TpsConfig.Parse(new[] { "RimWorld.exe", "-headless" }) == null, "No -gutps leaves the probe inactive");
        var defaults = TpsConfig.Parse(new[] { "-gutps=vanilla-1" });
        Check(defaults.Stage == "vanilla-1" && defaults.Mode == "uncapped" && defaults.WarmupTicks == 2500 && defaults.WindowTicks == 2500
            && defaults.Windows == 5 && !defaults.Prepare && defaults.Error == null, "Defaults");
        var paced = TpsConfig.Parse(new[] { "-gutps=gu-2", "-gutpsmode=Paced", "-gutpsspeed=ultrafast", "-gutpswindows=3", "-gutpspacedseconds=12.5", "-gutpswarmup=0", "-gutpswindow=-4" });
        Check(paced.Mode == "paced" && paced.Speed == "ultrafast" && paced.Windows == 3 && paced.PacedWindowSeconds == 12.5 && paced.Error == null,
            "Paced arguments parse");
        Check(paced.WarmupTicks == 0 && paced.WindowTicks == 2500, "Warmup may be zero; invalid counts keep their defaults");
        Check(TpsConfig.Parse(new[] { "-gutps=x", "-gutpsprepare", "-gutpsprepareticks=600" }) is { Prepare: true, PrepareTicks: 600 }, "Prepare arguments");
        Check(TpsConfig.Parse(new[] { "-gutps=x", "-gutpsmode=fast" }).Error != null, "Unknown mode is an error");
        Check(TpsConfig.Parse(new[] { "-gutps=x", "-gutpsmode=paced", "-gutpsspeed=Paused" }).Error != null, "Paused is not a measurable speed");
        Check(TpsConfig.Parse(new[] { "-gutps=x", "-gutpsmode=paced", "-gutpsspeed=warp" }).Error != null, "Unknown speed is an error");

        Console.WriteLine("PASS: TPS probe statistics, speed targets and argument parsing.");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TPS probe check failed: " + message);
    }
}
