using System.Collections.Generic;
using ProjectGu;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    /// <summary>Physically converts one available unrefined wild Gu into its dormant ground manifestation.</summary>
    public sealed class JobDriver_GuCaptureWild : JobDriver
    {
        private int expectedRevision = -1;
        private int pursuitDeadline = -1;

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            if (pursuitDeadline < 0 && job.targetA.Thing is Pawn_GuWildProjection { UsesSurfaceEncounter: true })
                pursuitDeadline = Find.TickManager.TicksGame + 1200;
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (job.targetA.Thing is not Pawn_GuWildProjection wild
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return false;
            int revision = expectedRevision < 0 ? wild.GuRevision : expectedRevision;
            if (wild.GuRevision != revision || !registry.CanOrderWildCapture(wild, pawn)
                || !pawn.Reserve(wild, job, 1, -1, null, errorOnFailed)) return false;
            expectedRevision = revision;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            var pursuit = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            pursuit.AddPreTickIntervalAction(_ =>
            {
                if (job.targetA.Thing is not Pawn_GuWildProjection { UsesSurfaceEncounter: true } wild) return;
                if (pursuitDeadline < 0) pursuitDeadline = Find.TickManager.TicksGame + 1200;
                if (Find.TickManager.TicksGame >= pursuitDeadline)
                {
                    Messages.Message($"{pawn.LabelShort} gave up pursuing {wild.LabelShort}.", pawn, MessageTypeDefOf.NeutralEvent, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                wild.EvadeCapture(pawn);
            });
            yield return pursuit;
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    string reason = "The wild Gu is unavailable.";
                    // The dormant Gu rests in the wild Gu's cell, or the open neighbouring cell nearest the captor (Alpha 5B).
                    var destination = job.targetA.Thing is Pawn_GuWildProjection target ? GuIndividualRegistry.CaptureDestination(target, pawn) : IntVec3.Invalid;
                    if (!destination.IsValid && job.targetA.Thing is Pawn_GuWildProjection blocked)
                        reason = $"There is no open ground beside the wild {blocked.LabelShort} to hold it in dormant form. Clear items or buildings around it, or wait for it to move.";
                    else if (job.targetA.Thing is Pawn_GuWildProjection wild
                        && Current.Game?.GetComponent<GuIndividualRegistry>()?.TryCaptureWild(wild, expectedRevision, pawn,
                            destination, out var dormant, out reason) == true)
                    {
                        Messages.Message("Captured Gu in dormant form. It remains unrefined on the ground; pick it up to carry it.",
                            new LookTargets(dormant), MessageTypeDefOf.NeutralEvent, false);
                        return;
                    }
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                }
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expectedRevision, "guExpectedRevision", -1);
            Scribe_Values.Look(ref pursuitDeadline, "guPursuitDeadline", -1);
        }
    }
}
