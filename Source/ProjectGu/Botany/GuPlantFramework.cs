using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Botany
{
    /// <summary>
    /// Shared rules of the Project GU plant framework. Everything here applies only to plants whose def carries
    /// <see cref="CompProperties_GuPlant"/>; vanilla and third-party plants are never touched.
    /// </summary>
    public static class GuPlantFramework
    {
        /// <summary>Package IDs of external seed systems. When one is active it owns sowing, so Project GU does not gate it too.</summary>
        internal static readonly string[] ExternalSeedSystems =
        {
            "owlchemist.seedspleaselite", "evyatar108.seedspleaselite", "rimworld.seedsplease", "notfood.seedsplease",
            "seedsplease.lite", "rainbeau.seedsplease"
        };

        private static bool? externalSeedSystem;
        private static Dictionary<ThingDef, string> feedsCache;

        public static CompProperties_GuPlant Props(ThingDef def) => def?.plant == null ? null : def.GetCompProperties<CompProperties_GuPlant>();

        public static bool IsGuPlant(ThingDef def) => Props(def) != null;

        /// <summary>Whether a known external seed system is active. Detected once from the active mod list.</summary>
        public static bool ExternalSeedSystemActive
        {
            get
            {
                externalSeedSystem ??= ModsConfig.ActiveModsInLoadOrder.Any(mod =>
                    mod?.PackageIdPlayerFacing is { } id && (ExternalSeedSystems.Contains(id.ToLowerInvariant())
                        || id.IndexOf("seedsplease", StringComparison.OrdinalIgnoreCase) >= 0));
                return externalSeedSystem.Value;
            }
        }

        /// <summary>Whether Project GU itself gates sowing of this plant on planting material.</summary>
        public static bool GatesSowing(ThingDef plantDef) => Props(plantDef) is { SeedGated: true } && !ExternalSeedSystemActive;

        // ---- Harvest and seed availability --------------------------------------------------------------------

        /// <summary>Why this plant can or cannot be harvested now, in the player's terms.</summary>
        public static AcceptanceReport CanHarvest(Plant plant)
        {
            if (plant is not { Spawned: true, Destroyed: false }) return "The plant is gone.";
            var def = plant.def.plant;
            if (!def.Harvestable || def.harvestedThingDef == null) return $"{plant.LabelCap} has nothing to harvest.";
            if (plant.Blighted) return $"{plant.LabelCap} is blighted.";
            if (plant.HarvestableNow) return true;
            return GrowthReason(plant, def.harvestMinGrowth);
        }

        /// <summary>Why seeds can or cannot be gathered from this plant now.</summary>
        public static AcceptanceReport CanGatherSeeds(Plant plant)
        {
            if (plant is not { Spawned: true, Destroyed: false }) return "The plant is gone.";
            var props = Props(plant.def);
            if (props?.seedCollection == null || props.sowingMaterial == null) return $"{plant.LabelCap} gives no seeds.";
            if (plant.Blighted) return $"{plant.LabelCap} is blighted.";
            float min = props.SeedMinGrowth(plant.def);
            return plant.Growth > min ? true : GrowthReason(plant, min);
        }

        private static AcceptanceReport GrowthReason(Plant plant, float min)
        {
            // A non-destructive harvest leaves growth at harvestAfterGrowth, so a plant between that and min is regrowing.
            float after = plant.def.plant.harvestAfterGrowth;
            bool regrowing = !plant.def.plant.HarvestDestroys && plant.Growth >= after - 0.001f && plant.Growth <= min;
            return regrowing
                ? $"{plant.LabelCap} was harvested and is regrowing ({plant.Growth:P0} of {min:P0})"
                : $"{plant.LabelCap} is not mature ({plant.Growth:P0} of {min:P0})";
        }

        /// <summary>Seeds gathered from one plant now: the configured yield scaled by health, at least one.</summary>
        public static int SeedYieldNow(Plant plant)
        {
            var props = Props(plant?.def);
            if (props?.seedCollection == null) return 0;
            float scaled = props.seedCollection.yield * UnityEngine.Mathf.Lerp(0.5f, 1f, (float)plant.HitPoints / Math.Max(1, plant.MaxHitPoints));
            return Math.Max(1, GenMath.RoundRandom(scaled));
        }

        // ---- Seed-gated sowing --------------------------------------------------------------------------------

        private static int cacheTick = -1, cacheFrame = -1;
        private static readonly Dictionary<(int Pawn, ThingDef Def), Thing> materialCache = new();

        /// <summary>Drops cached material lookups (used by fixtures that change forbidden state within one frame).</summary>
        internal static void InvalidateMaterialCache() => materialCache.Clear();

        /// <summary>
        /// Finds planting material this pawn could legitimately use: its own inventory first, then the nearest reachable,
        /// unforbidden, unreserved stack on its map with enough for one plant. Cached per pawn, tick and frame, because the native
        /// grower scan evaluates many cells in one go. Never reserves or consumes anything.
        /// </summary>
        internal static Thing FindSowingMaterial(Pawn pawn, CompProperties_GuPlant props, bool forced, out bool fromInventory)
        {
            fromInventory = false;
            if (pawn == null || props?.sowingMaterial == null) return null;
            if (pawn.inventory?.innerContainer is { } inventory)
                foreach (var thing in inventory)
                    if (thing.def == props.sowingMaterial && thing.stackCount >= props.sowingMaterialCount)
                    {
                        fromInventory = true;
                        return thing;
                    }
            if (!pawn.Spawned || pawn.Map is not { } map) return null;
            // One grower scan evaluates many cells in one tick and frame; a paused game or a UI click is a new frame.
            int tick = Find.TickManager.TicksGame, frame = RealTime.frameCount;
            if (tick != cacheTick || frame != cacheFrame) { materialCache.Clear(); cacheTick = tick; cacheFrame = frame; }
            var key = (pawn.thingIDNumber, props.sowingMaterial);
            if (materialCache.TryGetValue(key, out var cached) && (cached == null || UsableMaterial(pawn, cached, props, forced))) return cached;
            Thing found = null;
            if (map.listerThings.ThingsOfDef(props.sowingMaterial).Count > 0)
                found = GenClosest.ClosestThingReachable(pawn.Position, map, ThingRequest.ForDef(props.sowingMaterial), PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn, Danger.Some), 9999f, thing => UsableMaterial(pawn, thing, props, forced));
            materialCache[key] = found;
            return found;
        }

        internal static bool UsableMaterial(Pawn pawn, Thing thing, CompProperties_GuPlant props, bool forced) =>
            thing is { Spawned: true, Destroyed: false } && thing.def == props.sowingMaterial && thing.stackCount >= props.sowingMaterialCount
            && !thing.IsForbidden(pawn) && !thing.IsBurning()
            && pawn.CanReserve(thing, 10, props.sowingMaterialCount, null, forced);

        /// <summary>
        /// Turns a native sow job for a seed-gated plant into a sow job that carries and consumes its planting material, or
        /// refuses it with a reason when no material is available. Other plants and other jobs pass through unchanged.
        /// </summary>
        internal static Job GateSowJob(Pawn pawn, Job job, bool forced)
        {
            if (job?.def != JobDefOf.Sow || job.plantDefToSow is not { } plantDef || Props(plantDef) is not { } props) return job;
            if (!props.cultivable)
            {
                JobFailReason.Is($"{plantDef.label} grows only in the wild");
                return null;
            }
            if (!props.SeedGated || ExternalSeedSystemActive || GuPlantDefOf.GU_SowWithMaterial == null) return job;
            var material = FindSowingMaterial(pawn, props, forced, out bool fromInventory);
            if (material == null)
            {
                JobFailReason.Is($"no {props.sowingMaterial.label} available");
                return null;
            }
            var gated = JobMaker.MakeJob(GuPlantDefOf.GU_SowWithMaterial, job.targetA, material);
            gated.plantDefToSow = plantDef;
            gated.count = props.sowingMaterialCount;
            gated.playerForced = job.playerForced;
            return gated;
        }

        // ---- Player-facing summaries --------------------------------------------------------------------------

        /// <summary>"Moon orchid petals feed Moonlight Gu", derived from the species feeding definitions, or null.</summary>
        public static string FeedsSummary(ThingDef plantDef)
        {
            if (plantDef == null) return null;
            feedsCache ??= new Dictionary<ThingDef, string>();
            if (feedsCache.TryGetValue(plantDef, out var cached)) return cached;
            var parts = new List<string>();
            foreach (var product in Products(plantDef))
            {
                var eaters = DefDatabase<GuSpeciesDef>.AllDefsListForReading
                    .Where(species => species.SimulatedFeeding?.foods?.Any(food => food?.thingDef == product) == true)
                    .Select(species => species.label).ToList();
                if (eaters.Count > 0) parts.Add($"{product.LabelCap} feed {string.Join(", ", eaters)}");
            }
            string summary = parts.Count == 0 ? null : string.Join("; ", parts);
            feedsCache[plantDef] = summary;
            return summary;
        }

        /// <summary>Every item the plant can yield: the native harvest product, byproducts and gathered seeds.</summary>
        public static IEnumerable<ThingDef> Products(ThingDef plantDef)
        {
            if (plantDef?.plant?.harvestedThingDef is { } main) yield return main;
            var props = Props(plantDef);
            if (props == null) yield break;
            foreach (var extra in props.byproducts)
                if (extra?.thingDef != null) yield return extra.thingDef;
            if (props.seedCollection != null && props.sowingMaterial != null) yield return props.sowingMaterial;
        }

        public static string Describe(ThingDef def, CompProperties_GuPlant props)
        {
            if (def == null || props == null) return null;
            var text = new StringBuilder();
            text.Append($"Project GU plant ({props.category.ToString().ToLowerInvariant()}).");
            if (!props.naturalHabitat.NullOrEmpty()) text.Append($"\nNatural habitat: {props.naturalHabitat}");
            if (!props.cultivable) text.Append("\nCultivation: wild only.");
            else if (props.SeedGated)
                text.Append($"\nCultivation: sow in a growing zone; each plant uses {props.sowingMaterialCount} {props.sowingMaterial.label}"
                    + (ExternalSeedSystemActive ? " (an external seed system is active and manages sowing)." : "."));
            else text.Append("\nCultivation: sow freely in a growing zone.");
            if (def.plant.harvestedThingDef != null)
            {
                text.Append($"\nHarvest: up to {def.plant.harvestYield:0} {def.plant.harvestedThingDef.label}");
                text.Append(def.plant.HarvestDestroys ? "; harvesting removes the plant."
                    : $"; the plant survives at {def.plant.harvestAfterGrowth:P0} growth and must regrow past {def.plant.harvestMinGrowth:P0} before its next harvest.");
                if (props.harvestRoles.Count > 0) text.Append($" Uses: {string.Join(", ", props.harvestRoles.Select(RoleLabel))}.");
            }
            if (props.seedCollection != null && props.sowingMaterial != null)
                text.Append($"\nSeeds: gather about {props.seedCollection.yield} {props.sowingMaterial.label} from a flowering plant without uprooting it. "
                    + "This spends the same flowering as a harvest.");
            foreach (var extra in props.byproducts.Where(extra => extra?.thingDef != null))
                text.Append($"\nAlso yields: {extra.count} {extra.thingDef.label}" + (extra.chance < 1f ? $" ({extra.chance:P0} chance)" : ""));
            string feeds = FeedsSummary(def);
            if (!feeds.NullOrEmpty()) text.Append($"\nGu food: {feeds}.");
            if (!props.usesNote.NullOrEmpty()) text.Append($"\n{props.usesNote}");
            return text.ToString();
        }

        private static string RoleLabel(GuPlantProductRole role) => role switch
        {
            GuPlantProductRole.GuFood => "Gu food",
            GuPlantProductRole.Refinement => "Gu refinement",
            GuPlantProductRole.HumanFood => "food",
            GuPlantProductRole.Medicine => "medicine",
            GuPlantProductRole.Trade => "trade",
            GuPlantProductRole.Planting => "planting",
            _ => "other"
        };
    }
}
