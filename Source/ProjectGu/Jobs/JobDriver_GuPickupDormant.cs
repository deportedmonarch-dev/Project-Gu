using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    public sealed class JobDriver_GuPickupDormant : JobDriver
    {
        private int expectedRevision = -1;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (job.targetA.Thing is not Thing_GuDormantProjection thing
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return false;
            int revision = expectedRevision < 0 ? thing.GuRevision : expectedRevision;
            if (thing.GuRevision != revision || !registry.CanOrderDormantPickup(thing, pawn) || !pawn.Reserve(thing, job, 1, -1, null, errorOnFailed))
                return false;
            expectedRevision = revision;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    string reason = "The dormant Gu is unavailable.";
                    if (job.targetA.Thing is Thing_GuDormantProjection thing
                        && Current.Game?.GetComponent<GuIndividualRegistry>()?.TryPickUpDormant(thing, expectedRevision, pawn, out reason) == true)
                        return;
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expectedRevision, "guExpectedRevision", -1);
        }
    }
}
