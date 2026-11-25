using System.Collections.Generic;
using ProjectGu.Presentation;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    /// <summary>Exerts a mortal cultivator's will over one dormant Gu while carrying it in native inventory.</summary>
    public sealed class JobDriver_GuRefineMortalGu : JobDriver
    {
        private const int VisualRetryTicks = 30;
        private int expectedRevision = -1;
        private string processId;
        [Unsaved] private int nextVisualAttempt;
        [Unsaved] private bool refinementEnded;

        internal string ProcessId => processId;

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
            AddFinishAction(_ =>
            {
                refinementEnded = true;
                GuRefinementPresentation.End(GuRefinementKind.WillEnforcement, processId);
            });

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
                    if (completed)
                    {
                        Messages.Message($"{thing.Label} now recognizes {pawn.LabelShort}. Store it from the Aperture tab or Manage aperture Gu.",
                            new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }
                    SustainPresentation(thing, registry);
                }
            };
        }

        /// <summary>
        /// Attaches the will-suppression effect from the live process. The keep-alive holds only while this exact job is
        /// still driving the same process on this pawn, so every stop removes the effect in the tick it happens.
        /// </summary>
        private void SustainPresentation(Thing_GuDormantProjection thing, GuIndividualRegistry registry)
        {
            int now = Find.TickManager.TicksGame;
            if (now < nextVisualAttempt || processId == null) return;
            nextVisualAttempt = now + VisualRetryTicks;
            var driver = this;
            string process = processId;
            GuRefinementPresentation.Sustain(GuRefinementKind.WillEnforcement, pawn, process, () =>
                !driver.refinementEnded && driver.pawn.Spawned && !driver.pawn.Downed && driver.pawn.jobs?.curDriver == driver
                && registry.Resolve(thing)?.RefinementProcess?.ProcessId == process);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref expectedRevision, "guExpectedRevision", -1);
            Scribe_Values.Look(ref processId, "guRefinementProcessId");
        }
    }
}
