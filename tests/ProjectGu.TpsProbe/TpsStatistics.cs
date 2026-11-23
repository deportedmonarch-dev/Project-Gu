namespace ProjectGu.TpsProbe;

/// <summary>Per-window tick statistics. Simulation figures exclude engine frame work; wall figures include it.</summary>
public sealed record TickSummary(int Ticks, double SimulationMs, double WallMs, double SimulationTps, double WallTps,
    double MeanTickMs, double P50TickMs, double P95TickMs, double P99TickMs, double MaxTickMs);

/// <summary>Spread of one measure across windows or processes.</summary>
public sealed record Variability(int Count, double Median, double Mean, double Min, double Max, double StdDev, double CvPercent);

/// <summary>A vanilla speed target and the measured headroom against it (measured TPS / target TPS).</summary>
public sealed record TargetHeadroom(string Name, double TargetTps, double Headroom, bool Met);

/// <summary>Pure statistics for the M0 throughput probe. No game references, so managed tests can exercise it.</summary>
public static class TpsStatistics
{
    /// <summary>
    /// RimWorld 1.7 TickManager targets 60 ticks per second times the speed multiplier: Fast 3 (180), Superfast 6 (360,
    /// or 12 when nothing is happening), Ultrafast 15 (900). Uncapped probe throughput is an upper bound, not a guarantee:
    /// the native loop also stops a frame after 2 × multiplier ticks or 45.45 ms.
    /// </summary>
    public static readonly (string Name, double Tps)[] VanillaTargets =
    {
        ("Fast 3x", 180), ("Superfast 6x", 360), ("Superfast idle 12x", 720), ("Ultrafast 15x", 900)
    };

    public static double TargetTps(double multiplier) => 60.0 * multiplier;

    /// <summary>Linear-interpolated percentile (0–100) of an ascending-sorted list.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double percent)
    {
        if (sorted == null || sorted.Count == 0) return double.NaN;
        if (percent <= 0) return sorted[0];
        if (percent >= 100) return sorted[sorted.Count - 1];
        double rank = percent / 100.0 * (sorted.Count - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    public static TickSummary SummarizeTicks(IReadOnlyList<double> tickMs, double wallMs)
    {
        if (tickMs == null || tickMs.Count == 0) throw new ArgumentException("A window needs at least one tick.", nameof(tickMs));
        var sorted = tickMs.OrderBy(value => value).ToArray();
        double simulationMs = sorted.Sum();
        return new TickSummary(sorted.Length, simulationMs, wallMs,
            simulationMs > 0 ? sorted.Length * 1000.0 / simulationMs : double.PositiveInfinity,
            wallMs > 0 ? sorted.Length * 1000.0 / wallMs : double.PositiveInfinity,
            simulationMs / sorted.Length, Percentile(sorted, 50), Percentile(sorted, 95), Percentile(sorted, 99), sorted[^1]);
    }

    /// <summary>Median, mean, range, sample standard deviation and coefficient of variation.</summary>
    public static Variability Spread(IEnumerable<double> values)
    {
        var sorted = (values ?? Enumerable.Empty<double>()).Where(value => !double.IsNaN(value)).OrderBy(value => value).ToArray();
        if (sorted.Length == 0) return new Variability(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        double mean = sorted.Average();
        double stdDev = sorted.Length > 1 ? Math.Sqrt(sorted.Sum(value => (value - mean) * (value - mean)) / (sorted.Length - 1)) : 0;
        return new Variability(sorted.Length, Percentile(sorted, 50), mean, sorted[0], sorted[^1], stdDev,
            mean != 0 ? 100.0 * stdDev / mean : double.NaN);
    }

    public static TargetHeadroom[] Headroom(double measuredTps) => VanillaTargets
        .Select(target => new TargetHeadroom(target.Name, target.Tps, measuredTps / target.Tps, measuredTps >= target.Tps)).ToArray();
}
