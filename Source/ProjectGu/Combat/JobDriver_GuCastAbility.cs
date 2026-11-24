using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Aims, then commits one Gu ability cast. The pending intent (which Gu and ability) is held by the combat ledger,
    /// keyed by this job's load ID, so it survives saving with the job. Ending the job before the launch toil, for any
    /// reason, spends nothing.
    /// </summary>
    public sealed class JobDriver_GuCastAbility : JobDriver
    {
        private GuCastIntent Intent => GuCombatLedger.Instance?.IntentFor(job.loadID);

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport() =>
            Intent?.Ability is { } ability ? $"using {ability.label}." : base.GetReport();

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(_ => GuCombatLedger.Instance?.RemoveIntent(job.loadID));
            int warmup = Intent?.Ability?.warmupTicks ?? 0;

            var aim = ToilMaker.MakeToil("GuAimAbility");
            aim.initAction = () =>
            {
                if (!StillValid(out string reason))
                {
                    Cancel(reason);
                    return;
                }
                pawn.pather?.StopDead();
                pawn.rotationTracker?.FaceTarget(job.targetA);
            };
            aim.tickAction = () =>
            {
                pawn.rotationTracker?.FaceTarget(job.targetA);
                // Early, free cancellation when the target clearly vanishes mid-aim.
                if (job.targetA.HasThing && (job.targetA.Thing.Destroyed || !job.targetA.Thing.Spawned))
                    Cancel("The target is no longer present.");
            };
            aim.defaultCompleteMode = ToilCompleteMode.Delay;
            aim.defaultDuration = warmup;
            aim.handlingFacing = true;
            if (warmup > 0) aim.WithProgressBarToilDelay(TargetIndex.None);
            yield return aim;

            var launch = ToilMaker.MakeToil("GuCommitAbility");
            launch.defaultCompleteMode = ToilCompleteMode.Instant;
            launch.initAction = () =>
            {
                if (!GuAbilityService.TryCommit(pawn, Intent, job.targetA, out _, out string reason))
                {
                    Cancel(reason);
                    return;
                }
                EndJobWith(JobCondition.Succeeded);
            };
            yield return launch;
        }

        private bool StillValid(out string reason)
        {
            var intent = Intent;
            reason = "The cast order was lost.";
            if (intent?.Ability == null) return false;
            return GuAbilityService.CanCast(pawn, intent.GuId, intent.Ability, out _, out reason)
                && GuAbilityService.ValidateTarget(pawn, intent.Ability, job.targetA, out reason);
        }

        private void Cancel(string reason)
        {
            string label = Intent?.Ability?.LabelCap ?? "The Gu ability";
            if (pawn.Faction == Faction.OfPlayer && !reason.NullOrEmpty())
                Messages.Message($"{label} cancelled: {reason} No primeval essence was spent.", new LookTargets(pawn),
                    MessageTypeDefOf.RejectInput, false);
            EndJobWith(JobCondition.Incompletable);
        }
    }
}
