using System;

namespace ProjectGu.Domain
{
    /// <summary>
    /// One authority for ordinary aptitude thresholds and baseline essence recovery.
    /// Units: essence points per 2,500 in-game ticks. B/D are design assumptions.
    /// Extreme physiques must have separate rules; this class does not classify them.
    /// No RimWorld dependencies: easy to test without launching the game.
    /// </summary>
    public static class AptitudeRules
    {
        public static int ClampOrdinary(int aptitude) => Math.Max(20, Math.Min(99, aptitude));
        public static string Grade(int aptitude)
        {
            if (aptitude >= 80) return "A";
            if (aptitude >= 60) return "B";
            if (aptitude >= 40) return "C";
            return "D";
        }
        public static float RegenerationPerHour(int aptitude)
        {
            if (aptitude >= 80) return 8f;
            if (aptitude >= 60) return 6f;
            if (aptitude >= 40) return 4f;
            return 2f;
        }
        // Not a population distribution: sampled only for legacy prototype pawns.
        // Awakening 0.2 will replace this with faction/scenario-aware generation.
    }
}
