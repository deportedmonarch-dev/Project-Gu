using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ProjectGu.Combat
{
    /// <summary>One ability that one exact Gu individual currently grants to one exact Gu Master.</summary>
    public readonly struct GuAbilityGrant
    {
        public readonly Pawn Caster;
        public readonly Hediff_Aperture Aperture;
        public readonly GuRecord Gu;
        public readonly GuAbilityDef Ability;

        internal GuAbilityGrant(Pawn caster, Hediff_Aperture aperture, GuRecord gu, GuAbilityDef ability)
        {
            Caster = caster;
            Aperture = aperture;
            Gu = gu;
            Ability = ability;
        }
    }

    /// <summary>
    /// The execution contract for active Gu abilities:
    /// <list type="number">
    /// <item>Eligibility: the registry confirms the exact refined Gu is in the caster's own live aperture.</item>
    /// <item>Targeting: the ability worker's rules, applied when ordering and again at commitment.</item>
    /// <item>Commitment: in one tick, revalidate everything, debit the aperture once, launch, start the cooldown and
    /// record the cast. A launch failure in that tick returns the uncommitted essence.</item>
    /// <item>Execution: the native projectile owns flight, impact and damage.</item>
    /// <item>Presentation: optional cues after each committed fact.</item>
    /// </list>
    /// Cancellation policy: anything that stops a cast before commitment (interruption, drafting changes, death, an
    /// invalid target, a removed Gu, a cooldown or too little essence) costs nothing and starts no cooldown. After
    /// commitment the essence is spent and the cooldown runs, whatever happens to the caster, the Gu or the target.
    /// </summary>
    public static class GuAbilityService
    {
        internal const string CastJobDefName = "GU_CastGuAbility";

        internal static JobDef CastJob => DefDatabase<JobDef>.GetNamedSilentFail(CastJobDefName);

        public static IEnumerable<GuAbilityGrant> GrantsFor(Pawn pawn)
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null || pawn == null) yield break;
            foreach (var record in registry.AbilityGrantingOccupants(pawn, out var aperture))
                foreach (var ability in record.Species.abilities)
                    if (ability != null) yield return new GuAbilityGrant(pawn, aperture, record, ability);
        }

        /// <summary>Caster, individual, cooldown and essence checks. Targets are checked separately.</summary>
        public static bool CanCast(Pawn caster, string guId, GuAbilityDef ability, out GuAbilityGrant grant, out string reason)
        {
            grant = default;
            reason = "Gu abilities are unavailable in this game.";
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var ledger = GuCombatLedger.Instance;
            if (registry == null || ledger == null || ability == null) return false;
            reason = $"{ability.LabelCap} is a self-use ability.";
            if (ability.IsChannel) return false;
            reason = "Only an awakened Gu Master who is conscious and on a map can use aperture Gu.";
            if (!registry.TryGetAbilityCaster(caster, out _, out _)) return false;
            reason = "This Gu is not refined, stored in this Gu Master's aperture, or able to act.";
            if (!registry.IsAbilityGrantedBy(guId, caster, out var record, out var aperture)
                || record.Species?.abilities?.Contains(ability) != true)
            {
                if (registry.Resolve(guId) is { } starving && registry.StarvationImpairment(starving) is { } impairment) reason = impairment + ".";
                return false;
            }
            int remaining = ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame);
            reason = $"{ability.LabelCap} is recovering ({remaining.ToStringSecondsFromTicks()}).";
            if (remaining > 0) return false;
            if (ability.IsSustained && !GuAbilityWorker_Sustained.CanActivate(caster, ability, out reason)) return false;
            reason = $"Not enough primeval essence ({aperture.Essence:0.0} of {ability.essenceCost:0.0}).";
            if (!aperture.CanSpendEssence(ability.essenceCost)) return false;
            grant = new GuAbilityGrant(caster, aperture, record, ability);
            reason = null;
            return true;
        }

        public static bool ValidateTarget(Pawn caster, GuAbilityDef ability, LocalTargetInfo target, out string reason) =>
            ability.Worker.ValidateTarget(caster, target, out reason);

        /// <summary>Orders the cast job. Nothing is spent until the job's launch toil commits.</summary>
        public static bool TryOrderCast(Pawn caster, string guId, GuAbilityDef ability, LocalTargetInfo target, out string reason)
        {
            reason = "Use the technique's toggle to activate it.";
            if (ability?.IsSustained == true) return false;
            if (!CanCast(caster, guId, ability, out _, out reason) || !ValidateTarget(caster, ability, target, out reason)) return false;
            reason = "The cast job is unavailable.";
            if (CastJob is not { } jobDef || !caster.IsColonistPlayerControlled) return false;
            var job = JobMaker.MakeJob(jobDef, target);
            job.playerForced = true;
            var ledger = GuCombatLedger.Instance;
            ledger.RegisterIntent(job.loadID, new GuCastIntent { GuId = guId, Ability = ability, CreatedTick = Find.TickManager.TicksGame });
            if (!caster.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                ledger.RemoveIntent(job.loadID);
                reason = "The Gu Master could not begin casting.";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// The single irreversible step. Revalidates the exact individual, caster, cooldown, essence and target, then
        /// debits once and launches. Returns false with nothing spent when any check or the launch fails.
        /// </summary>
        public static bool TryCommit(Pawn caster, GuCastIntent intent, LocalTargetInfo target, out GuCastRecord cast, out string reason)
        {
            cast = null;
            reason = "The cast order is no longer valid.";
            if (intent?.Ability == null || string.IsNullOrEmpty(intent.GuId)) return false;
            var ability = intent.Ability;
            if (!CanCast(caster, intent.GuId, ability, out var grant, out reason)
                || !ValidateTarget(caster, ability, target, out reason)) return false;

            string castId = Guid.NewGuid().ToString("N");
            var context = new GuCastContext(caster, grant.Gu, grant.Aperture, ability, castId);
            string launchReason = "Primeval essence could not be committed.";
            bool launched;
            Thing projectile = null;
            try
            {
                launched = grant.Aperture.TrySpendEssenceForCast(ability.essenceCost,
                    () => ability.Worker.TryLaunch(context, target, out projectile, out launchReason));
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] {ability.defName} worker failed before commitment. {exception}");
                launched = false;
                launchReason = "The ability failed to form.";
            }
            if (!launched)
            {
                reason = launchReason;
                return false;
            }

            cast = new GuCastRecord
            {
                CastId = castId,
                GuId = grant.Gu.Id,
                Ability = ability,
                CasterId = caster.GetUniqueLoadID(),
                ApertureId = grant.Aperture.ApertureId,
                EssencePaid = ability.essenceCost,
                LaunchTick = Find.TickManager.TicksGame,
                TargetDescription = target.HasThing ? target.Thing.GetUniqueLoadID() : target.Cell.ToString()
            };
            if (ability.IsSustained) GuCombatLedger.Instance.NoteCommitment(cast.GuId, cast.LaunchTick, ability.cooldownTicks);
            else GuCombatLedger.Instance.Commit(cast, ability.cooldownTicks);
            GuAbilityPresentation.Launched(context, projectile, target);
            reason = null;
            return true;
        }

        /// <summary>Heading in degrees around the vertical axis, matching a native projectile's rotation.</summary>
        internal static float HeadingDegrees(Vector3 from, Vector3 to)
        {
            Vector3 direction = (to - from).Yto0();
            return direction.sqrMagnitude < 0.0001f ? 0f : Quaternion.LookRotation(direction).eulerAngles.y;
        }
    }
}
