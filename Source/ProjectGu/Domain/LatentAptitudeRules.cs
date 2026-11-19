using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Provisional balance: 50% no natural aperture; among successful ordinary
    /// candidates D55 C30 B12 A3. NOT claimed as canon probabilities.
    /// NPCs receive this result once, not when they begin a ceremony.
    /// </summary>
    public static class LatentAptitudeRules
    {
        public static int Generate()
        {
            if (Rand.Value < 0.50f) return 0;
            int n = Rand.RangeInclusive(1, 100);
            if (n <= 55) return Rand.RangeInclusive(20, 39);
            if (n <= 85) return Rand.RangeInclusive(40, 59);
            if (n <= 97) return Rand.RangeInclusive(60, 79);
            return Rand.RangeInclusive(80, 99);
        }
    }
}
