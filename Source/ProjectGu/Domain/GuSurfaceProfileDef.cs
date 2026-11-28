using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Domain
{
    public sealed class GuSurfaceProfileDef : Def
    {
        public List<GuSurfaceSuitabilityRule> rules = new();

        internal int Weight(BiomeDef biome, Hilliness hills, bool exposedRock)
        {
            int weight = 0;
            foreach (var rule in rules)
                if (rule.weight > weight && (!rule.exposedRockRequired || exposedRock) && rule.Matches(biome, hills))
                    weight = rule.weight;
            return weight;
        }

        internal bool NeedsRockObservation(BiomeDef biome, Hilliness hills) =>
            rules.Any(rule => rule.exposedRockRequired && rule.Matches(biome, hills));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var error in base.ConfigErrors()) yield return error;
            if (rules == null || rules.Count == 0 || rules.Any(rule => rule.weight is < 1 or > 3 || rule.biomes?.Contains(null) == true))
                yield return $"{defName}: surface suitability requires valid rules with weights from one to three";
        }
    }

    public sealed class GuSurfaceSuitabilityRule
    {
        public List<BiomeDef> biomes;
        public List<Hilliness> hilliness;
        public int weight;
        public bool exposedRockRequired;

        internal bool Matches(BiomeDef biome, Hilliness hills) =>
            (biomes == null || biomes.Contains(biome)) && (hilliness == null || hilliness.Contains(hills));
    }
}
