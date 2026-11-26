using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ProjectGu.Botany
{
    /// <summary>Broad botanical category of a Project GU plant. Descriptive; no rule depends on it.</summary>
    public enum GuPlantCategory { Flower = 0, Herb = 1, Crop = 2, Tree = 3, Grass = 4, Fungus = 5, Vine = 6, Other = 7 }

    /// <summary>What a harvested product is for. Descriptive tags shown to the player and used by future systems.</summary>
    public enum GuPlantProductRole { GuFood = 0, Refinement = 1, HumanFood = 2, Medicine = 3, Trade = 4, Planting = 5, Other = 6 }

    /// <summary>An extra product a harvest can yield beside the plant's native <c>harvestedThingDef</c>.</summary>
    public sealed class GuPlantByproduct
    {
        public ThingDef thingDef;
        public int count = 1;
        /// <summary>Chance per successful harvest, 0..1.</summary>
        public float chance = 1f;
        public List<GuPlantProductRole> roles = new();
    }

    /// <summary>Optional seed collection from a living, flowering plant. It spends the same flowering as a petal harvest.</summary>
    public sealed class GuPlantSeedCollection
    {
        /// <summary>Seeds gathered at full growth (fewer when the plant is damaged); at least one on success.</summary>
        public int yield = 2;
        /// <summary>Work in ticks at a plant work speed of 1.</summary>
        public float work = 300f;
        /// <summary>Growth the plant needs before seeds can be gathered. Defaults to the native harvestMinGrowth.</summary>
        public float minGrowth = -1f;
        /// <summary>Growth the plant keeps afterwards. Defaults to the native harvestAfterGrowth; must stay below minGrowth.</summary>
        public float growthAfter = -1f;
        /// <summary>Whether a low Plants skill can fail the gathering, like a native harvest.</summary>
        public bool failable = true;
    }

    /// <summary>
    /// Declarative specification of a Project GU plant (Alpha 5B plant framework). The plant itself stays an ordinary
    /// native <c>ThingDef</c> with a <c>plant</c> block, so growing zones, sowing, growth, harvest designations, grower jobs,
    /// stockpiles and work priorities all stay native. This component only adds what native plants cannot express:
    /// seed-gated sowing, seed collection from living plants, extra harvest products, and the player-facing summary of habitat,
    /// cultivation and Gu resource relationships. Every hook it enables is restricted to plants that carry it.
    /// </summary>
    public sealed class CompProperties_GuPlant : CompProperties
    {
        public GuPlantCategory category = GuPlantCategory.Other;
        /// <summary>Where the plant grows naturally, for the player (for example "Primeval qi-rich soil in spirit spring caverns").</summary>
        [MustTranslate] public string naturalHabitat;
        /// <summary>Whether players may sow it at all. When false the plant is wild-only and sowing is refused.</summary>
        public bool cultivable = true;
        /// <summary>Planting material consumed when a sowing completes. Null means the plant sows freely, like a vanilla crop.</summary>
        public ThingDef sowingMaterial;
        /// <summary>Planting material consumed per plant sown.</summary>
        public int sowingMaterialCount = 1;
        /// <summary>Optional seed collection from living plants.</summary>
        public GuPlantSeedCollection seedCollection;
        /// <summary>Roles of the plant's native harvested product.</summary>
        public List<GuPlantProductRole> harvestRoles = new();
        /// <summary>Extra harvest products, added through the native additional-yield hook.</summary>
        public List<GuPlantByproduct> byproducts = new();
        /// <summary>Optional note on refinement or other future uses, shown to the player.</summary>
        [MustTranslate] public string usesNote;

        public CompProperties_GuPlant() => compClass = typeof(CompGuPlant);

        public bool SeedGated => cultivable && sowingMaterial != null;

        public float SeedMinGrowth(ThingDef plant) => seedCollection == null ? 1f
            : seedCollection.minGrowth >= 0f ? seedCollection.minGrowth : plant?.plant?.harvestMinGrowth ?? 1f;

        public float SeedGrowthAfter(ThingDef plant) => seedCollection == null ? 0f
            : seedCollection.growthAfter >= 0f ? seedCollection.growthAfter : plant?.plant?.harvestAfterGrowth ?? 0f;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            string owner = parentDef?.defName ?? "?";
            if (parentDef?.plant == null || parentDef.category != ThingCategory.Plant)
            {
                yield return $"{owner}: CompProperties_GuPlant belongs on a plant ThingDef";
                yield break;
            }
            var plant = parentDef.plant;
            if (plant.Harvestable && plant.harvestedThingDef == null) yield return $"{owner}: a harvest yield needs harvestedThingDef";
            // Repeat harvest must regrow: a plant that keeps more growth than it needs would be harvestable without end.
            if (plant.Harvestable && !plant.HarvestDestroys && plant.harvestAfterGrowth >= plant.harvestMinGrowth)
                yield return $"{owner}: harvestAfterGrowth ({plant.harvestAfterGrowth}) must be below harvestMinGrowth ({plant.harvestMinGrowth}) so each harvest needs regrowth";
            if (sowingMaterial != null)
            {
                if (sowingMaterial.category != ThingCategory.Item) yield return $"{owner}: sowingMaterial must be an item";
                if (sowingMaterialCount < 1 || sowingMaterialCount > sowingMaterial.stackLimit)
                    yield return $"{owner}: sowingMaterialCount must be between 1 and the material's stack limit";
                if (!plant.Sowable) yield return $"{owner}: sowingMaterial is set but the plant has no sowTags";
            }
            if (!cultivable && sowingMaterial != null) yield return $"{owner}: a wild-only plant (cultivable false) needs no sowingMaterial";
            if (seedCollection != null)
            {
                if (sowingMaterial == null) yield return $"{owner}: seedCollection needs a sowingMaterial to gather";
                if (seedCollection.yield < 1 || seedCollection.work <= 0f) yield return $"{owner}: seedCollection needs a positive yield and work";
                float min = SeedMinGrowth(parentDef), after = SeedGrowthAfter(parentDef);
                if (!(min > 0f && min <= 1f)) yield return $"{owner}: seedCollection minGrowth must be in (0, 1]";
                // Seed collection must also spend the flowering and regrow, or it would be unlimited.
                if (!(after > 0f && after < min)) yield return $"{owner}: seedCollection growthAfter must be above 0 (the plant survives) and below minGrowth";
            }
            foreach (var extra in byproducts ?? new List<GuPlantByproduct>())
                if (extra?.thingDef == null || extra.thingDef.category != ThingCategory.Item || extra.count < 1 || !(extra.chance > 0f && extra.chance <= 1f))
                    yield return $"{owner}: every byproduct needs an item thingDef, a positive count and a chance in (0, 1]";
        }
    }
}
