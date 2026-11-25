using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Self-use channel abilities. Same authorization as targeted abilities: the exact refined Gu must be in its own
    /// cultivator's live aperture. A new session commits once when its job starts: one essence debit, cooldown start and
    /// session record. Each later pulse is applied by the worker against the authoritative state. Interruption keeps every
    /// completed pulse and suspends the session; its owner may resume the remaining pulses without paying or waiting for
    /// the cooldown, as long as the session's binding (for Liquor Worm, the cultivator's stage) still holds.
    /// </summary>
    public static class GuChannelService
    {
        internal const string ChannelJobDefName = "GU_ChannelGuAbility";

        internal static JobDef ChannelJob => DefDatabase<JobDef>.GetNamedSilentFail(ChannelJobDefName);

        public static bool CanChannel(Pawn caster, string guId, GuAbilityDef ability, out GuAbilityGrant grant,
            out GuChannelSession resumable, out string reason)
        {
            grant = default;
            resumable = null;
            reason = "Gu abilities are unavailable in this game.";
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var ledger = GuCombatLedger.Instance;
            if (registry == null || ledger == null || ability?.channel == null) return false;
            reason = "Only an awakened Gu Master who is conscious and on a map can use aperture Gu.";
            if (!registry.TryGetAbilityCaster(caster, out _, out _)) return false;
            // Channeling needs a settled cultivator; refusing here keeps a drafted order from paying for a session it cannot run.
            reason = "Undraft the Gu Master to channel this Gu.";
            if (caster.Drafted) return false;
            reason = "This Gu is not refined, stored in this Gu Master's aperture, or able to act.";
            if (!registry.IsAbilityGrantedBy(guId, caster, out var record, out var aperture)
                || record.Species?.abilities?.Contains(ability) != true) return false;

            var worker = ability.channel.Worker(ability);
            var existing = ledger.ChannelFor(record.Id);
            reason = $"{ability.LabelCap} is already in use.";
            if (existing is { Active: true }) return false;
            if (!worker.CanChannel(aperture, out reason)) return false;
            grant = new GuAbilityGrant(caster, aperture, record, ability);
            if (existing is { Complete: false } && existing.Ability == ability && existing.CasterId == caster.GetUniqueLoadID()
                && existing.ApertureId == aperture.ApertureId && existing.Binding == worker.BindingFor(aperture))
            {
                resumable = existing;
                reason = null;
                return true;
            }

            int remaining = ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame);
            reason = $"{ability.LabelCap} is recovering ({remaining.ToStringSecondsFromTicks()}).";
            if (remaining > 0) return false;
            reason = $"Not enough primeval essence ({aperture.Essence:0.0} of {ability.essenceCost:0.0}).";
            if (!aperture.CanSpendEssence(ability.essenceCost)) return false;
            reason = null;
            return true;
        }

        /// <summary>Orders the channel job. Nothing is spent until the job's first toil commits.</summary>
        public static bool TryOrderChannel(Pawn caster, string guId, GuAbilityDef ability, out string reason)
        {
            if (!CanChannel(caster, guId, ability, out _, out _, out reason)) return false;
            reason = "The channel job is unavailable.";
            if (ChannelJob is not { } jobDef || !caster.IsColonistPlayerControlled) return false;
            var job = JobMaker.MakeJob(jobDef);
            job.playerForced = true;
            var ledger = GuCombatLedger.Instance;
            ledger.RegisterIntent(job.loadID, new GuCastIntent { GuId = guId, Ability = ability, CreatedTick = Find.TickManager.TicksGame });
            if (!caster.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                ledger.RemoveIntent(job.loadID);
                reason = "The Gu Master could not begin.";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// Resumes a suspended session, or commits a new one: revalidate, debit the activation cost once, start the
        /// cooldown and record the session, all in one tick.
        /// </summary>
        public static bool TryBeginChannel(Pawn caster, GuCastIntent intent, out GuChannelSession session, out string reason)
        {
            session = null;
            reason = "The order is no longer valid.";
            if (intent?.Ability?.channel == null || string.IsNullOrEmpty(intent.GuId)) return false;
            var ability = intent.Ability;
            if (!CanChannel(caster, intent.GuId, ability, out var grant, out var resumable, out reason)) return false;
            if (resumable != null)
            {
                resumable.Active = true;
                session = resumable;
                return true;
            }

            reason = "Primeval essence could not be committed.";
            if (!grant.Aperture.TrySpendEssence(ability.essenceCost)) return false;
            session = new GuChannelSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                GuId = grant.Gu.Id,
                Ability = ability,
                CasterId = caster.GetUniqueLoadID(),
                ApertureId = grant.Aperture.ApertureId,
                Binding = ability.channel.Worker(ability).BindingFor(grant.Aperture),
                EssencePaid = ability.essenceCost,
                StartedTick = Find.TickManager.TicksGame,
                Active = true
            };
            GuCombatLedger.Instance.BeginChannel(session, ability.cooldownTicks);
            reason = null;
            return true;
        }

        /// <summary>Revalidates the exact Gu, caster and aperture of an active session. Called every channel tick.</summary>
        internal static bool StillAuthorized(Pawn caster, GuChannelSession session, out Hediff_Aperture aperture, out string reason)
        {
            aperture = null;
            reason = "The Gu Master can no longer channel.";
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null || session == null || caster.Drafted) return false;
            if (!registry.TryGetAbilityCaster(caster, out _, out _)) return false;
            reason = "The Gu left the Gu Master's aperture or no longer recognizes them.";
            if (!registry.IsAbilityGrantedBy(session.GuId, caster, out _, out var live) || live.ApertureId != session.ApertureId) return false;
            aperture = live;
            reason = null;
            return true;
        }
    }
}
