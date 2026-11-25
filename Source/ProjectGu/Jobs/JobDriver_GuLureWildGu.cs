using System.Collections.Generic;
using System.Linq;
using ProjectGu.Springs;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    /// <summary>
    /// Sets out one lure item at a spirit spring and waits for a wild Gu that craves it. Target A is the spring; target B
    /// is a lure item on the map, or invalid when the pawn already holds one in inventory. Interruption before the wait
    /// ends keeps the item. The item is consumed only after the spring materializes an individual.
    /// </summary>
    public sealed class JobDriver_GuLureWildGu : JobDriver
    {
        private CompGuSpiritSpring Spring => job.targetA.Thing?.TryGetComp<CompGuSpiritSpring>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            Spring != null && (!job.targetB.HasThing || pawn.Reserve(job.targetB, job, 1, 1, null, errorOnFailed));

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            if (job.targetB.HasThing)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            }
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var wait = ToilMaker.MakeToil("GuWaitForLure");
            wait.initAction = () =>
            {
                if (HeldLure() == null)
                {
                    Messages.Message("The lure is no longer in hand.", MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            wait.tickAction = () => pawn.rotationTracker?.FaceTarget(job.targetA);
            wait.handlingFacing = true;
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.defaultDuration = LureTicks();
            wait.WithProgressBarToilDelay(TargetIndex.A);
            yield return wait;

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var lure = HeldLure();
                    var spring = Spring;
                    var species = lure == null ? null : spring?.LureSpeciesFor(lure.def);
                    var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
                    string reason = "Nothing at this spring answers that lure any more.";
                    if (species == null || registry == null || !spring.TryEncounterWildGu(species, registry, out var individual, out reason))
                    {
                        Messages.Message(reason ?? "The lure drew nothing.", MessageTypeDefOf.RejectInput, false);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    string lureLabel = lure.LabelNoCount;
                    (lure.stackCount > 1 ? lure.SplitOff(1) : lure).Destroy();
                    Messages.Message($"Drawn by the {lureLabel}, a wild {species.label} crawled out beside the spring. Right-click it with a colonist to capture it.",
                        new LookTargets(registry.ProjectionOf(individual)), MessageTypeDefOf.PositiveEvent, false);
                    EndJobWith(JobCondition.Succeeded);
                }
            };
        }

        private int LureTicks()
        {
            var spring = Spring;
            var lure = HeldLure() ?? job.targetB.Thing;
            return lure != null && spring?.LureSpeciesFor(lure.def) is { } species ? species.encounterLureTicks : 600;
        }

        private Thing HeldLure()
        {
            var spring = Spring;
            if (spring == null) return null;
            bool Accepts(Thing thing) => thing != null && spring.LureSpeciesFor(thing.def) != null;
            if (Accepts(pawn.carryTracker?.CarriedThing)) return pawn.carryTracker.CarriedThing;
            return pawn.inventory?.innerContainer?.FirstOrDefault(Accepts);
        }
    }
}
