using System.Collections.Generic;
using RimWorld;

using Verse;
using Verse.AI;
using Verse.Sound;

namespace ProjectGu.Botany
{
    /// <summary>
    /// Architect "Gather seeds" order: designates flowering Project GU plants that give seeds. Only plants with a
    /// <see cref="GuPlantSeedCollection"/> accept it, so the order never affects vanilla or third-party plants.
    /// </summary>
    public sealed class Designator_GuGatherSeeds : Designator
    {
        public Designator_GuGatherSeeds()
        {
            defaultLabel = "Gather seeds";
            defaultDesc = "Designate flowering Project GU plants for seed gathering. A grower collects their seeds without uprooting them; "
                + "each plant spends its flowering and must regrow before its next harvest or gathering.";
            icon = CompGuPlant.GatherSeedsIcon;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_HarvestPlants;
        }

        protected override DesignationDef Designation => GuPlantDefOf.GU_GatherSeeds;
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map)) return false;
            return c.GetPlant(Map) is { } plant ? CanDesignateThing(plant) : "MessageMustDesignatePlants".Translate();
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (c.GetPlant(Map) is { } plant) DesignateThing(plant);
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (t is not Plant plant || GuPlantFramework.Props(plant.def)?.seedCollection == null) return false;
            if (Map.designationManager.DesignationOn(plant, Designation) != null) return false;
            return GuPlantFramework.CanGatherSeeds(plant);
        }

        public override void DesignateThing(Thing t)
        {
            Map.designationManager.RemoveAllDesignationsOn(t);
            Map.designationManager.AddDesignation(new Designation(t, Designation));
        }

        public override void SelectedUpdate() => GenUI.RenderMouseoverBracket();
    }

    /// <summary>Grower work: gather seeds from designated plants (Plant cutting, beside native harvesting).</summary>
    public sealed class WorkGiver_GuGatherSeeds : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            var designation = GuPlantDefOf.GU_GatherSeeds;
            if (designation == null) yield break;
            foreach (var item in pawn.Map.designationManager.designationsByDef[designation]) yield return item.target.Thing;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            GuPlantDefOf.GU_GatherSeeds == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(GuPlantDefOf.GU_GatherSeeds);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not Plant plant || GuPlantDefOf.GU_GatherPlantSeeds == null
                || pawn.Map.designationManager.DesignationOn(plant, GuPlantDefOf.GU_GatherSeeds) == null) return null;
            var report = GuPlantFramework.CanGatherSeeds(plant);
            if (!report.Accepted)
            {
                JobFailReason.Is(report.Reason);
                return null;
            }
            if (plant.IsBurning() || !pawn.CanReserve(plant, 1, -1, null, forced)) return null;
            return JobMaker.MakeJob(GuPlantDefOf.GU_GatherPlantSeeds, plant);
        }

        public override string PostProcessedGerund(Job job) => "gathering seeds from";
    }

    /// <summary>
    /// Gathers seeds from one living plant. In the completing tick it revalidates the flowering, spawns the seeds and sets the
    /// plant's growth back, so the plant survives and must regrow before it can give petals or seeds again. Interrupted work
    /// gathers nothing and changes nothing.
    /// </summary>
    public sealed class JobDriver_GuGatherSeeds : JobDriver
    {
        private float workDone;

        private Plant Plant => (Plant)job.targetA.Thing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workDone, "guSeedWork");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !GuPlantFramework.CanGatherSeeds(Plant).Accepted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var gather = ToilMaker.MakeToil("GuGatherSeeds");
            gather.tickIntervalAction = delta =>
            {
                var actor = gather.actor;
                actor.skills?.Learn(SkillDefOf.Plants, 0.085f * delta);
                workDone += actor.GetStatValue(StatDefOf.PlantWorkSpeed) * delta;
                var props = GuPlantFramework.Props(Plant.def);
                if (props?.seedCollection == null || workDone < props.seedCollection.work) return;
                Complete(actor, props);
                ReadyForNextToil();
            };
            gather.defaultCompleteMode = ToilCompleteMode.Never;
            gather.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            gather.WithEffect(EffecterDefOf.Harvest_Plant, TargetIndex.A);
            gather.WithProgressBar(TargetIndex.A, () =>
            {
                var props = Plant == null ? null : GuPlantFramework.Props(Plant.def);
                return props?.seedCollection == null ? 0f : workDone / props.seedCollection.work;
            }, interpolateBetweenActorAndTarget: true);
            gather.PlaySustainerOrSound(() => Plant?.def.plant.soundHarvesting);
            gather.activeSkill = () => SkillDefOf.Plants;
            yield return gather;
        }

        private void Complete(Pawn actor, CompProperties_GuPlant props)
        {
            var plant = Plant;
            var designation = Map.designationManager.DesignationOn(plant, GuPlantDefOf.GU_GatherSeeds);
            if (designation != null) Map.designationManager.RemoveDesignation(designation);
            if (!GuPlantFramework.CanGatherSeeds(plant).Accepted) return;
            int count = GuPlantFramework.SeedYieldNow(plant);
            float skill = actor.GetStatValue(StatDefOf.PlantHarvestYield);
            bool failed = props.seedCollection.failable && actor.RaceProps.Humanlike && Rand.Value > skill;
            // The flowering is spent either way, exactly like a failed native harvest.
            plant.Growth = props.SeedGrowthAfter(plant.def);
            plant.def.plant.soundHarvestFinish?.PlayOneShot(actor);
            Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            if (failed)
            {
                MoteMaker.ThrowText((actor.DrawPos + plant.DrawPos) / 2f, Map, "TextMote_HarvestFailed".Translate(), 3.65f);
                return;
            }
            var seeds = ThingMaker.MakeThing(props.sowingMaterial);
            seeds.stackCount = count;
            if (actor.Faction != Faction.OfPlayer) seeds.SetForbidden(true);
            GenPlace.TryPlaceThing(seeds, actor.Position, Map, ThingPlaceMode.Near);
        }
    }
}
