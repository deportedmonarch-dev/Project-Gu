using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Botany
{
    /// <summary>
    /// Runtime side of <see cref="CompProperties_GuPlant"/>: extra harvest products through the native additional-yield
    /// hook, the player-facing summary, and the "Gather seeds" command for selected plants. It holds no saved state: whether a
    /// plant can be harvested or gathered again is its native growth, so old saves need no migration.
    /// </summary>
    [StaticConstructorOnStartup]
    public sealed class CompGuPlant : ThingComp
    {
        internal static readonly Texture2D GatherSeedsIcon = ContentFinder<Texture2D>.Get("ProjectGU/UI/GatherSeeds", false) ?? BaseContent.BadTex;

        public CompProperties_GuPlant Props => (CompProperties_GuPlant)props;
        private Plant Plant => parent as Plant;

        public override IEnumerable<ThingDefCountClass> GetAdditionalHarvestYield()
        {
            // The native harvest toil calls this only after a successful harvest of a plant that is still harvestable.
            foreach (var extra in Props.byproducts)
                if (extra?.thingDef != null && Rand.Chance(extra.chance))
                    yield return new ThingDefCountClass(extra.thingDef, extra.count);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (Props.seedCollection == null || Plant is not { Spawned: true } plant) yield break;
            var designation = GuPlantDefOf.GU_GatherSeeds;
            if (designation == null) yield break;
            bool designated = plant.Map.designationManager.DesignationOn(plant, designation) != null;
            var report = GuPlantFramework.CanGatherSeeds(plant);
            var command = new Command_Action
            {
                defaultLabel = designated ? "Cancel seed gathering" : "Gather seeds",
                defaultDesc = designated
                    ? "Cancel the order to gather seeds from this plant."
                    : $"Designate this {plant.def.label} for seed gathering. A grower collects {Props.sowingMaterial?.label ?? "seeds"} without uprooting it; "
                      + "the plant spends its flowering and must regrow before its next harvest or gathering.",
                icon = GatherSeedsIcon,
                groupKey = 0x47554753,
                action = () =>
                {
                    var manager = plant.Map?.designationManager;
                    if (manager == null) return;
                    var existing = manager.DesignationOn(plant, designation);
                    if (existing != null) manager.RemoveDesignation(existing);
                    else if (GuPlantFramework.CanGatherSeeds(plant).Accepted) manager.AddDesignation(new Designation(plant, designation));
                }
            };
            if (!designated && !report.Accepted) command.Disable(report.Reason);
            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            var text = new StringBuilder();
            var props = Props;
            if (Plant is { } plant && props.seedCollection != null && plant.Spawned)
            {
                float min = props.SeedMinGrowth(plant.def);
                text.Append(plant.Growth > min ? "Flowering: petals or seeds can be gathered" : $"Regrowing: flowers again at {min:P0} growth");
            }
            string feeds = GuPlantFramework.FeedsSummary(parent.def);
            if (!feeds.NullOrEmpty()) AppendLine(text, feeds);
            if (!props.cultivable) AppendLine(text, "Wild only: cannot be sown");
            else if (props.SeedGated) AppendLine(text, $"Sowing uses {props.sowingMaterialCount} {props.sowingMaterial.label} per plant");
            return text.ToString();
        }

        public override string GetDescriptionPart() => GuPlantFramework.Describe(parent.def, Props);

        private static void AppendLine(StringBuilder text, string line)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(line);
        }
    }

    [DefOf]
    public static class GuPlantDefOf
    {
        public static DesignationDef GU_GatherSeeds;
        public static JobDef GU_GatherPlantSeeds;
        public static JobDef GU_SowWithMaterial;

        static GuPlantDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(GuPlantDefOf));
    }
}
