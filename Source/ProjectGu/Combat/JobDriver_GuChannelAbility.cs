using System.Collections.Generic;
using ProjectGu.Presentation;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Drives one channel session. The first toil commits or resumes the session; the second applies one pulse per
    /// interval while revalidating the Gu, caster and aperture every tick. Pulse progress lives in the saved session, so a
    /// save between ticks resumes exactly. Any end before completion suspends the session with its pulses kept.
    /// The optional manifestation is attached from the live session and detached whenever the job stops driving it.
    /// </summary>
    public sealed class JobDriver_GuChannelAbility : JobDriver
    {
        private const int VisualRetryTicks = 30;
        private string sessionId;
        [Unsaved] private int nextVisualAttempt;

        internal string SessionId => sessionId;
        private GuChannelSession Session => GuCombatLedger.Instance?.ChannelById(sessionId);

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport() => Session?.Ability is { } ability
            ? $"using {ability.label} ({Session.PulsesDone}/{Session.PulseCount})."
            : base.GetReport();

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(OnFinished);

            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    if (sessionId != null && Session is { } loaded)
                    {
                        loaded.Active = true;
                        return;
                    }
                    var ledger = GuCombatLedger.Instance;
                    var intent = ledger?.IntentFor(job.loadID);
                    if (!GuChannelService.TryBeginChannel(pawn, intent, out var session, out string reason))
                    {
                        Cancel(intent?.Ability, reason, spent: false);
                        return;
                    }
                    ledger.RemoveIntent(job.loadID);
                    sessionId = session.SessionId;
                }
            };

            var channel = ToilMaker.MakeToil("GuChannel");
            channel.defaultCompleteMode = ToilCompleteMode.Never;
            channel.initAction = () => pawn.pather?.StopDead();
            channel.tickAction = ChannelTick;
            channel.WithProgressBar(TargetIndex.None, () => Session is { PulseCount: > 0 } session
                ? (session.PulsesDone + session.TicksIntoPulse / (float)session.Ability.channel.pulseIntervalTicks) / session.PulseCount : 0f);
            yield return channel;
        }

        private void ChannelTick()
        {
            var session = Session;
            if (session == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            session.Active = true;
            if (!GuChannelService.StillAuthorized(pawn, session, out var grant, out string reason))
            {
                Cancel(session.Ability, reason, spent: true);
                return;
            }
            AttachVisual(session);

            var props = session.Ability.channel;
            if (++session.TicksIntoPulse < props.pulseIntervalTicks) return;
            session.TicksIntoPulse = 0;
            var worker = props.Worker(session.Ability);
            var result = worker.ApplyPulse(grant, session, out reason);
            if (result is GuPulseResult.Applied or GuPulseResult.Finished) session.PulsesDone++;
            if (result == GuPulseResult.Suspended)
            {
                // Nothing was applied or paid; the session stays resumable once the condition clears.
                Message($"{session.Ability.LabelCap} paused: {reason} {worker.KeptOnStop}", MessageTypeDefOf.NeutralEvent);
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            if (result == GuPulseResult.Stopped)
            {
                // Nothing more can apply under this binding; the session ends with its completed pulses kept.
                GuCombatLedger.Instance.EndChannel(session);
                Message($"{session.Ability.LabelCap} ended early: {reason}", MessageTypeDefOf.NeutralEvent);
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            // Finished: the pulse applied and nothing further can (for example, full bodily development).
            if (result == GuPulseResult.Applied && !session.Complete) return;
            GuCombatLedger.Instance.EndChannel(session);
            Message(worker.CompletionText(pawn, session), MessageTypeDefOf.PositiveEvent);
            EndJobWith(JobCondition.Succeeded);
        }

        private void AttachVisual(GuChannelSession session)
        {
            if (Find.TickManager.TicksGame < nextVisualAttempt || session.Ability.channel.manifestationFx is not { } profile) return;
            nextVisualAttempt = Find.TickManager.TicksGame + VisualRetryTicks;
            GuFxMapComponent.TryAttachSustained(pawn.Map, profile, pawn, session.SessionId, IsDrivingVisual);
        }

        /// <summary>The manifestation exists only while this job is the pawn's current job and drives a live session.</summary>
        private bool IsDrivingVisual() => pawn is { Spawned: true, Dead: false } && pawn.jobs?.curDriver == this
            && Session is { Active: true };

        private void OnFinished(JobCondition condition)
        {
            GuCombatLedger.Instance?.RemoveIntent(job.loadID);
            if (Session is { } session) session.Active = false;
            if (sessionId != null && pawn.MapHeld is { } map) map.GetComponent<GuFxMapComponent>()?.Detach(sessionId);
        }

        private void Cancel(GuAbilityDef ability, string reason, bool spent)
        {
            Message($"{ability?.LabelCap ?? "The Gu ability"} stopped: {reason}"
                + (spent ? " " + (ability?.channel?.Worker(ability).KeptOnStop ?? "Completed pulses are kept.") : " No primeval essence was spent."),
                MessageTypeDefOf.RejectInput);
            EndJobWith(JobCondition.Incompletable);
        }

        private void Message(string text, MessageTypeDef type)
        {
            if (pawn.Faction == Faction.OfPlayer) Messages.Message(text, new LookTargets(pawn), type, false);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sessionId, "guChannelSessionId");
        }
    }
}
