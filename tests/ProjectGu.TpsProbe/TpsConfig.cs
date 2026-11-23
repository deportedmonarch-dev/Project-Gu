using System.Globalization;

namespace ProjectGu.TpsProbe;

/// <summary>Command-line configuration. Null unless -gutps=&lt;stage&gt; is present, so the probe is inert otherwise.</summary>
internal sealed class TpsConfig
{
    public string Stage;
    public bool Prepare;
    public string Mode = "uncapped";
    public int WarmupTicks = 2500;
    public int WindowTicks = 2500;
    public int Windows = 5;
    public int PrepareTicks;
    public string Speed = "Superfast";
    public double PacedWarmupSeconds = 10;
    public double PacedWindowSeconds = 20;

    /// <summary>Why the arguments cannot run, or null. The probe reports it as a failure instead of measuring.</summary>
    public string Error;

    public static readonly TpsConfig Current = Parse(Environment.GetCommandLineArgs());

    internal static TpsConfig Parse(IEnumerable<string> args)
    {
        string Value(string name) => args.FirstOrDefault(arg => arg.StartsWith("-" + name + "=", StringComparison.Ordinal))?.Substring(name.Length + 2);
        int Int(string name, int fallback, int minimum = 1) => int.TryParse(Value(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= minimum ? value : fallback;
        double Real(string name, double fallback) => double.TryParse(Value(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 ? value : fallback;
        string stage = Value("gutps");
        if (string.IsNullOrEmpty(stage)) return null;
        var config = new TpsConfig { Stage = stage, Prepare = args.Contains("-gutpsprepare") };
        config.Mode = (Value("gutpsmode") ?? config.Mode).ToLowerInvariant();
        config.WarmupTicks = Int("gutpswarmup", config.WarmupTicks, 0);
        config.WindowTicks = Int("gutpswindow", config.WindowTicks);
        config.Windows = Int("gutpswindows", config.Windows);
        config.PrepareTicks = Int("gutpsprepareticks", 0, 0);
        config.Speed = Value("gutpsspeed") ?? config.Speed;
        config.PacedWarmupSeconds = Real("gutpspacedwarmup", config.PacedWarmupSeconds);
        config.PacedWindowSeconds = Real("gutpspacedseconds", config.PacedWindowSeconds);
        if (config.Mode is not ("uncapped" or "paced")) config.Error = $"Unknown -gutpsmode={config.Mode}.";
        else if (config.Mode == "paced" && (!Enum.TryParse<Verse.TimeSpeed>(config.Speed, true, out var speed) || speed == Verse.TimeSpeed.Paused)) config.Error = $"Unknown -gutpsspeed={config.Speed}.";
        return config;
    }
}
