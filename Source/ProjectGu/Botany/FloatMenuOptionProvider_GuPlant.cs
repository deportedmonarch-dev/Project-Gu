using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Botany
{
    /// <summary>
    /// Right-click harvesting and seed gathering for Project GU plants, with the selected colonist. Discovered natively like
    /// every other float-menu provider. It offers "Harvest" for an undesignated wild or planted plant (vanilla only offers
    /// "Prioritize" once a designation or growing zone exists, and those vanilla options are left to vanilla), and "Gather
    /// seeds" where the plant gives seeds. An option that cannot be taken stays visible, disabled, with the reason.
    /// </summary>
    public sealed class FloatMenuOptionProvider_GuPlant : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            if (clickedThing is not Plant plant || GuPlantFramework.Props(plant.def) is not { } props) yield break;
            var pawn = context.FirstSelectedPawn;
            if (pawn == null || !pawn.IsColonistPlayerControlled) yield break;
            var harvest = HarvestOption(pawn, plant);
            if (harvest != null) yield return harvest;
            if (props.seedCollection != null) yield return SeedOption(pawn, plant);
        }

        private static FloatMenuOption HarvestOption(Pawn pawn, Plant plant)
        {
            var def = plant.def.plant;
            if (!def.Harvestable || def.harvestedThingDef == null) return null;
            var map = plant.Map;
            // Vanilla already offers "Prioritize harvesting" for designated plants and for plants in their own growing zone.
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null) return null;
            if (plant.HarvestableNow && plant.Position.GetPlantToGrowSettable(map) is { } settable && settable.GetPlantDefToGrow() == plant.def) return null;
            string label = $"Harvest {plant.Label} ({def.harvestedThingDef.label})";
            string reason = Refusal(pawn, plant, WorkTypeDefOf.PlantCutting, GuPlantFramework.CanHarvest(plant));
            if (reason != null) return new FloatMenuOption($"Cannot harvest {plant.Label}: {reason}", null);
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                if (GuPlantFramework.CanHarvest(plant).Accepted && !plant.Destroyed)
                {
                    if (map.designationManager.DesignationOn(plant) is { } other && other.def != DesignationDefOf.HarvestPlant)
                        map.designationManager.RemoveDesignation(other);
                    if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) == null)
                        map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
                    var job = JobMaker.MakeJob(JobDefOf.HarvestDesignated, plant);
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }), pawn, plant);
        }

        private static FloatMenuOption SeedOption(Pawn pawn, Plant plant)
        {
            var props = GuPlantFramework.Props(plant.def);
            string reason = Refusal(pawn, plant, WorkTypeDefOf.PlantCutting, GuPlantFramework.CanGatherSeeds(plant));
            if (reason != null) return new FloatMenuOption($"Cannot gather seeds from {plant.Label}: {reason}", null);
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption($"Gather {props.sowingMaterial.label} from {plant.Label}", () =>
            {
                var map = plant.Map;
                if (map == null || !GuPlantFramework.CanGatherSeeds(plant).Accepted) return;
                if (map.designationManager.DesignationOn(plant) is { } other) map.designationManager.RemoveDesignation(other);
                map.designationManager.AddDesignation(new Designation(plant, GuPlantDefOf.GU_GatherSeeds));
                var job = JobMaker.MakeJob(GuPlantDefOf.GU_GatherPlantSeeds, plant);
                job.playerForced = true;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), pawn, plant);
        }

        /// <summary>The first reason this pawn cannot work this plant now, or null.</summary>
        internal static string Refusal(Pawn pawn, Plant plant, WorkTypeDef workType, AcceptanceReport plantReport)
        {
            if (!plantReport.Accepted) return plantReport.Reason.NullOrEmpty() ? "not possible now" : plantReport.Reason;
            if (pawn.WorkTypeIsDisabled(workType)) return $"{pawn.LabelShort} is incapable of {workType.gerundLabel}";
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) return $"{pawn.LabelShort} cannot manipulate";
            if (plant.IsForbidden(pawn)) return plant.Position.InAllowedArea(pawn) ? "forbidden" : "outside the allowed area";
            if (plant.IsBurning()) return "burning";
            if (!pawn.CanReach(plant, PathEndMode.Touch, Danger.Deadly)) return "no path";
            if (!pawn.CanReserve(plant, 1, -1, null, true))
            {
                var holder = plant.Map.reservationManager.FirstRespectedReserver(plant, pawn);
                return holder != null ? $"reserved by {holder.LabelShort}" : "reserved";
            }
            if (!PlantUtility.PawnWillingToCutPlant_Job(plant, pawn)) return $"{pawn.LabelShort} is unwilling";
            return null;
        }
    }
}
