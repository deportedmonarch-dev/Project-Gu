using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Botany
{
    /// <summary>
    /// Native sowing that carries and spends planting material (targets: A the cell, B the material stack, count the material
    /// per plant). It follows <c>JobDriver_PlantSow</c> exactly, with one difference: the material is consumed in the same tick
    /// the sowing completes. An interrupted sowing removes the half-sown plant, as vanilla does, and the carried material is
    /// dropped by the native job cleanup, so nothing is spent and nothing is duplicated. The toil list never depends on runtime
    /// state, so a saved job resumes at the right toil.
    /// </summary>
    public sealed class JobDriver_GuSowWithMaterial : JobDriver
    {
        private float sowWorkDone;
        private bool committed;

        private Plant Plant => (Plant)job.GetTarget(TargetIndex.A).Thing;
        private CompProperties_GuPlant Props => GuPlantFramework.Props(job.plantDefToSow);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sowWorkDone, "sowWorkDone");
            Scribe_Values.Look(ref committed, "guMaterialCommitted");
        }

        private bool MaterialInInventory => job.targetB.Thing is { } thing && thing.holdingOwner != null
            && thing.holdingOwner == pawn.inventory?.innerContainer;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            if (MaterialInInventory) return true;
            return pawn.Reserve(job.targetB, job, 10, job.count, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var gotoCell = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch)
                .FailOn(() => PlantUtility.AdjacentSowBlocker(job.plantDefToSow, TargetA.Cell, Map) != null)
                .FailOn(() => !job.plantDefToSow.CanNowPlantAt(TargetLocA, Map))
                .FailOn(() => !HoldsMaterial() && !MaterialInInventory)
                .FailOn(() =>
                {
                    var things = TargetA.Cell.GetThingList(Map);
                    for (int i = 0; i < things.Count; i++)
                        if (things[i].def == job.plantDefToSow) return true;
                    return false;
                });

            yield return Toils_Jump.JumpIf(gotoCell, () => MaterialInInventory || HoldsMaterial());
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: false, subtractNumTakenFromJobCount: false)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B);
            yield return gotoCell;

            var sow = ToilMaker.MakeToil("GuSowWithMaterial");
            sow.initAction = () =>
            {
                TargetThingA = GenSpawn.Spawn(job.plantDefToSow, TargetLocA, Map);
                pawn.Reserve(TargetThingA, sow.actor.CurJob);
                var plant = Plant;
                plant.Growth = 0f;
                plant.sown = true;
            };
            sow.tickIntervalAction = delta =>
            {
                var actor = sow.actor;
                actor.skills?.Learn(SkillDefOf.Plants, 0.085f * delta);
                var plant = Plant;
                sowWorkDone += actor.GetStatValue(StatDefOf.PlantWorkSpeed) * delta;
                if (sowWorkDone < plant.def.plant.sowWork) return;
                // The single irreversible step: spend exactly the material for one plant, then finish the sowing in the same tick.
                if (!TryConsumeMaterial(out string reason))
                {
                    Messages.Message(reason, new LookTargets(plant), MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                committed = true;
                plant.Growth = 0.0001f;
                Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
                actor.records.Increment(RecordDefOf.PlantsSown);
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.SowedPlant, actor.Named(HistoryEventArgsNames.Doer)));
                if (plant.def.plant.humanFoodPlant)
                    Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.SowedHumanFoodPlant, actor.Named(HistoryEventArgsNames.Doer)));
                ReadyForNextToil();
            };
            sow.defaultCompleteMode = ToilCompleteMode.Never;
            sow.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            sow.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            sow.WithEffect(EffecterDefOf.Sow, TargetIndex.A);
            sow.WithProgressBar(TargetIndex.A, () => Plant == null ? 0f : sowWorkDone / Plant.def.plant.sowWork, interpolateBetweenActorAndTarget: true);
            sow.PlaySustainerOrSound(() => SoundDefOf.Interact_Sow);
            sow.AddFinishAction(() =>
            {
                // No material spent means no plant: remove the half-sown plant exactly as vanilla does on interruption.
                if (!committed && TargetThingA is Plant unfinished && !unfinished.Destroyed && unfinished.def == job.plantDefToSow
                    && unfinished.LifeStage == PlantLifeStage.Sowing)
                    unfinished.Destroy();
            });
            sow.activeSkill = () => SkillDefOf.Plants;
            yield return sow;
        }

        private bool HoldsMaterial() => pawn.carryTracker?.CarriedThing is { } carried && Props is { } props
            && carried.def == props.sowingMaterial && carried.stackCount >= props.sowingMaterialCount;

        private bool TryConsumeMaterial(out string reason)
        {
            var props = Props;
            reason = $"{pawn.LabelShort} has no {props?.sowingMaterial?.label ?? "planting material"} to sow {job.plantDefToSow.label}.";
            if (props?.sowingMaterial == null) return false;
            Thing source = null;
            if (HoldsMaterial()) source = pawn.carryTracker.CarriedThing;
            else if (pawn.inventory?.innerContainer is { } inventory)
                foreach (var thing in inventory)
                    if (thing.def == props.sowingMaterial && thing.stackCount >= props.sowingMaterialCount) { source = thing; break; }
            if (source == null || source.Destroyed) return false;
            int count = props.sowingMaterialCount;
            var spent = source.stackCount > count ? source.SplitOff(count) : source;
            spent.Destroy(DestroyMode.Vanish);
            reason = null;
            return true;
        }
    }
}
