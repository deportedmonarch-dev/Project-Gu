using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    /// <summary>Exerts a mortal cultivator's will over one dormant Gu while carrying it in native inventory.</summary>
    public sealed class JobDriver_GuRefineMortalGu : JobDriver
    {
        private int expectedRevision = -1;
        private string processId;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (job.targetA.Thing is not Thing_GuDormantProjection thing
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry
                || !registry.CanOrderRefinement(thing, pawn)) return false;
            if (thing.Spawned && !pawn.Reserve(thing, job, 1, -1, null, errorOnFailed)) return false;
            expectedRevision = thing.GuRevision;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (job.targetA.Thing is Thing_GuDormantProjection initial && initial.Spawned)
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    string reason = "The dormant Gu is no longer available.";
                    if (job.targetA.Thing is not Thing_GuDormantProjection thing
                        || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    var inventory = pawn.inventory?.GetDirectlyHeldThings();
                    if (inventory == null || (thing.holdingOwner != inventory
                        && !registry.TryPickUpDormant(thing, expectedRevision, pawn, out reason)))
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    expectedRevision = thing.GuRevision;
                    if (!registry.TryBeginOrResumeRefinement(thing, expectedRevision, pawn,
                        out processId, out _, out reason))
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                        EndJobWith(JobCondition.Incompletable);
                    }
                }
            };

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                tickAction = () =>
                {
                    string reason = "The refinement process stopped.";
                    if (job.targetA.Thing is not Thing_GuDormantProjection thing
                        || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry
                        || !registry.TryAdvanceRefinement(thing, expectedRevision, pawn, processId, out bool completed, out reason))
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    if (!completed) return;
                    Messages.Message($"{thing.Label} now recognizes {pawn.LabelShort}. Use Manage aperture Gu to store it.",
                        new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                    EndJobWith(JobCondition.Succeeded);
                }
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expectedRevision, "guExpectedRevision", -1);
            Scribe_Values.Look(ref processId, "guRefinementProcessId");
        }
    }
}
