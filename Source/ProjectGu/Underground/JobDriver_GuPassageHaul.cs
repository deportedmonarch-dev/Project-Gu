using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ProjectGu.Underground
{
    public sealed class JobDriver_GuPassageHaulPack : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => GuPassageHauling.ActiveSite(pawn) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_General.Do(() =>
            {
                if (!GuPassageHauling.TryPack(pawn, job)) EndJobWith(JobCondition.Incompletable);
            });
        }
    }

    public sealed class JobDriver_GuPassageHaulStore : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => GuPassageHauling.ActiveSite(pawn)?.Hauling.Cargo != job.targetA.Thing);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return Toils_General.Do(() =>
            {
                // Native inventory placement uses the same physical stack/merge path as vanilla hauling.
                if (!GuPassageHauling.TryStore(pawn, job.targetA.Thing, job.targetB.Cell, job))
                    EndJobWith(JobCondition.Incompletable);
            });
        }
    }
}
