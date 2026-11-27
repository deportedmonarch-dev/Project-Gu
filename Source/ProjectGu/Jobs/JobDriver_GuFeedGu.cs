using System.Collections.Generic;
using ProjectGu.Domain;
using ProjectGu.Sustenance;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    /// <summary>
    /// Feeds one exact Gu individual with physical food. Target A is the food (a stack on the map, or an item already in the
    /// feeder's inventory); target B is a dormant Gu on the ground, or invalid when the Gu is in the feeder's aperture or
    /// inventory. The feeder fetches and carries the food, offers it for a short while, and the items are consumed only at
    /// the commitment toil after everything is revalidated. Any interruption before then drops or keeps the food intact.
    /// </summary>
    public sealed class JobDriver_GuFeedGu : JobDriver
    {
        internal const int FeedingTicks = 240;

        private GuFeedIntent intent;
        private int nextPortion;

        internal string GuId => intent?.GuId;
        internal bool Automatic => intent?.Automatic == true;
        private Thing Food => job.GetTarget(TargetIndex.A).Thing;
        private bool GroundGu => job.GetTarget(TargetIndex.B).HasThing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref intent, "guFeedIntent");
            Scribe_Values.Look(ref nextPortion, "guNextPortion");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Vanilla may call this on a cached driver before the job starts (ordered jobs), so only peek here.
            intent ??= GuSustenanceComponent.Instance?.PeekIntent(job.loadID);
            if (intent == null || Food == null) return false;
            if (job.targetQueueA is { } portions)
            {
                if (job.countQueue?.Count != portions.Count) return false;
                for (int i = nextPortion; i < portions.Count; i++)
                    if (portions[i].Thing is { Spawned: true } source
                        && !pawn.Reserve(source, job, GuFeedingWorker_Items.MaxPawnsPerStack, job.countQueue[i], null, errorOnFailed)) return false;
            }
            else if (Food.Spawned && !pawn.Reserve(Food, job, GuFeedingWorker_Items.MaxPawnsPerStack, job.count, null, errorOnFailed)) return false;
            return !GroundGu || pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);
        }

        public override void Notify_Starting()
        {
            base.Notify_Starting();
            // The job is now running: the driver owns its intent (saved with the job) and the pending entry is released.
            intent ??= GuSustenanceComponent.Instance?.PeekIntent(job.loadID);
            GuSustenanceComponent.Instance?.RemoveIntent(job.loadID);
        }

        public override string GetReport()
        {
            var record = GuId == null ? null : Current.Game?.GetComponent<GuIndividualRegistry>()?.FindRecord(GuId);
            string gu = record?.Species?.label ?? "Gu";
            return $"feeding {Food?.LabelNoCount ?? "food"} to {gu}.";
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // The exact individual must stay feedable by this pawn throughout. Checked every tick; a dictionary lookup.
            this.FailOn(() => !StillFeedable());
            if (GroundGu) this.FailOnDestroyedOrNull(TargetIndex.B);

            // The toil list is identical whatever the food's state, so a saved toil index stays valid after reloading.
            var offer = Toils_General.Wait(FeedingTicks, GroundGu ? TargetIndex.B : TargetIndex.None);
            offer.FailOn(() => !FoodInHand());
            if (GroundGu) offer.FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            offer.WithProgressBarToilDelay(GroundGu ? TargetIndex.B : TargetIndex.A);
            var gotoGu = GroundGu ? Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch).FailOnDespawnedNullOrForbidden(TargetIndex.B) : null;

            if (job.targetQueueA == null)
            {
                // Food already in the feeder's inventory or hands is not fetched.
                yield return Toils_Jump.JumpIf(gotoGu ?? offer, () => Food is not { Spawned: true });
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.A).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
                yield return Toils_Haul.StartCarryThing(TargetIndex.A, failIfStackCountLessThanJobCount: true).FailOnDestroyedNullOrForbidden(TargetIndex.A);
            }
            else
            {
                var next = Toils_General.Do(() =>
                {
                    if (nextPortion == job.targetQueueA.Count)
                    {
                        job.targetA = job.targetQueueA[0];
                        JumpToToil(gotoGu ?? offer);
                        return;
                    }
                    job.targetC = job.targetQueueA[nextPortion];
                });
                var collect = Toils_General.Do(CollectPortion);
                yield return next;
                yield return Toils_Jump.JumpIf(collect, () => job.targetC.Thing?.holdingOwner == pawn.inventory?.innerContainer);
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(TargetIndex.C).FailOnSomeonePhysicallyInteracting(TargetIndex.C);
                yield return collect;
                yield return Toils_Jump.Jump(next);
            }
            if (gotoGu != null) yield return gotoGu;
            yield return offer;

            var commit = ToilMaker.MakeToil("GuCommitFeeding");
            commit.defaultCompleteMode = ToilCompleteMode.Instant;
            commit.initAction = () =>
            {
                Thing food = Food;
                string label = food?.LabelNoCount;
                var record = Current.Game?.GetComponent<GuIndividualRegistry>()?.FindRecord(GuId);
                if (!GuSustenance.TryCommitFeeding(pawn, intent, food, job.GetTarget(TargetIndex.B).Thing, out int meals,
                    out string reason, job.targetQueueA, job.countQueue))
                {
                    if (!Automatic || job.playerForced) Messages.Message(reason, new LookTargets(pawn), MessageTypeDefOf.RejectInput, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!Automatic)
                    Messages.Message($"{pawn.LabelShort} fed {record?.Species?.label ?? "the Gu"} {meals} meal{(meals == 1 ? "" : "s")} of {label}.",
                        new LookTargets(pawn), MessageTypeDefOf.SilentInput, false);
            };
            yield return commit;
        }

        private void CollectPortion()
        {
            var food = job.targetC.Thing;
            var inventory = pawn.inventory?.innerContainer;
            int count = job.countQueue[nextPortion];
            if (inventory == null || food is not { Destroyed: false } || food.stackCount < count)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            if (food.holdingOwner != inventory)
            {
                if (!food.Spawned || food.Map != pawn.Map || food.IsForbidden(pawn)
                    || !pawn.CanReachImmediate(food, PathEndMode.ClosestTouch))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                IntVec3 origin = food.Position;
                var portion = food.SplitOff(count);
                if (portion.Spawned) portion.DeSpawn();
                if (!inventory.TryAdd(portion, canMergeWithExistingStacks: false))
                {
                    GenPlace.TryPlaceThing(portion, origin, pawn.Map, ThingPlaceMode.Near);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                job.targetQueueA[nextPortion] = portion;
            }
            nextPortion++;
        }

        private bool StillFeedable()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null || intent == null || registry.Resolve(intent.GuId) is not { } record
                || record.CustodyRevision != intent.CustodyRevision) return false;
            var access = registry.FeedAccess(pawn, record, out var dormant, out _);
            return access != GuFeedAccess.None && (access == GuFeedAccess.Ground) == GroundGu
                && (!GroundGu || job.GetTarget(TargetIndex.B).Thing == dormant);
        }

        private bool FoodInHand()
        {
            Thing food = Food;
            return food != null && !food.Destroyed && (pawn.carryTracker?.CarriedThing == food
                || (food.holdingOwner != null && food.holdingOwner == pawn.inventory?.innerContainer));
        }
    }
}
