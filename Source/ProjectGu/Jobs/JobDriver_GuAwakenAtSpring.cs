using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using ProjectGu.Springs;

namespace ProjectGu.Jobs
{
    /// <summary>Physical awakening attempt. Only the final toil commits; interruption is safe.</summary>
    public sealed class JobDriver_GuAwakenAtSpring : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600, TargetIndex.A);
            Toil finish = new Toil();
            finish.initAction = () =>
            {
                CompGuSpiritSpring spring = job.targetA.Thing?.TryGetComp<CompGuSpiritSpring>();
                GuAwakeningService.TryAwakenAtSpring(pawn, spring, out string outcome);
                Messages.Message(pawn.LabelShort + ": " + outcome,
                    MessageTypeDefOf.NeutralEvent, false);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}
