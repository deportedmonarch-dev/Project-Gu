using System;

namespace ProjectGu.Domain
{
    internal readonly record struct GuRefinementQuote(int WorkTicks, float EssenceCost, float Capability,
        float RankResistance, float AllegianceResistance);

    /// <summary>
    /// Prototype tuning seam for future will, deduction, thought, path-specialization and aura modifiers.
    /// Current mortal rules use species resistance, Gu rank, existing allegiance and aperture aptitude/rank.
    /// </summary>
    internal static class GuRefinementRules
    {
        internal static bool TryQuote(GuSpeciesDef species, int rank, string existingOwner, string refiner,
            Hediff_Aperture aperture, out GuRefinementQuote quote)
        {
            quote = default;
            if (species == null || aperture == null || rank is < 1 or > 5
                || species.refinementBaseWorkTicks is < 1 or > 600_000
                || !float.IsFinite(species.refinementEssenceCost) || species.refinementEssenceCost <= 0f
                || !float.IsFinite(species.refinementResistance) || species.refinementResistance is < 0.1f or > 10f
                || string.IsNullOrWhiteSpace(refiner)
                || string.Equals(existingOwner, refiner, StringComparison.OrdinalIgnoreCase)) return false;

            float rankResistance = 1f + 0.5f * (rank - 1);
            float allegianceResistance = string.IsNullOrEmpty(existingOwner) ? 1f : 1.75f;
            float capability = Math.Clamp(aperture.Aptitude / 40f * (1f + (aperture.Rank - 1) * 0.2f), 0.5f, 2f);
            float resistance = species.refinementResistance * rankResistance * allegianceResistance;
            double work = Math.Ceiling(species.refinementBaseWorkTicks * (double)resistance / capability);
            double cost = species.refinementEssenceCost * (double)resistance;
            if (work is < 1 or > 600_000 || !double.IsFinite(cost) || cost <= 0d || cost > float.MaxValue) return false;
            quote = new((int)work, (float)cost, capability, rankResistance, allegianceResistance);
            return true;
        }
    }
}
