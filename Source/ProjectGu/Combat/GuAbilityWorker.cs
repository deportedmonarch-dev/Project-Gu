using System;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ProjectGu.Combat
{
    /// <summary>Everything a worker may read about an authorized cast. Built by the service after authorization.</summary>
    public readonly struct GuCastContext
    {
        public readonly Pawn Caster;
        public readonly GuRecord Gu;
        public readonly Hediff_Aperture Aperture;
        public readonly GuAbilityDef Ability;
        public readonly string CastId;

        internal GuCastContext(Pawn caster, GuRecord gu, Hediff_Aperture aperture, GuAbilityDef ability, string castId)
        {
            Caster = caster;
            Gu = gu;
            Aperture = aperture;
            Ability = ability;
            CastId = castId;
        }
    }

    /// <summary>
    /// What a committed cast does. Workers validate targets and perform the launch; they never debit essence, set
    /// cooldowns or decide authorization, which the service owns. <see cref="TryLaunch"/> is all-or-nothing: returning
    /// false (or throwing) must leave nothing launched, so the service can return the uncommitted essence.
    /// </summary>
    public abstract class GuAbilityWorker
    {
        public GuAbilityDef def;

        /// <summary>Target rules shared by command targeting, warmup and the commit revalidation.</summary>
        public virtual bool ValidateTarget(Pawn caster, LocalTargetInfo target, out string reason)
        {
            reason = "Choose a target on the caster's map.";
            if (caster?.Map == null || !target.IsValid || !target.Cell.InBounds(caster.Map)) return false;
            if (target.HasThing)
            {
                reason = $"{def.LabelCap} can target pawns only, or a location.";
                if (!def.targetPawns || target.Thing is not Pawn pawn) return false;
                reason = "The target is no longer present.";
                if (pawn.Destroyed || !pawn.Spawned || pawn.Map != caster.Map || pawn.Dead) return false;
                reason = "A Gu Master cannot target themself.";
                if (pawn == caster) return false;
            }
            else
            {
                reason = $"{def.LabelCap} cannot target a location.";
                if (!def.targetLocations) return false;
                reason = "A Gu Master cannot target their own position.";
                if (target.Cell == caster.Position) return false;
            }

            float distance = caster.Position.DistanceTo(target.Cell);
            reason = $"Out of range ({distance:0.0} of {def.range:0} cells).";
            if (distance > def.range) return false;
            reason = "Too close to the caster.";
            if (distance < def.minRange) return false;
            reason = "No line of sight to the target.";
            if (def.requireLineOfSight && !GenSight.LineOfSight(caster.Position, target.Cell, caster.Map, true)) return false;
            reason = null;
            return true;
        }

        /// <summary>Performs the launch. Must be all-or-nothing.</summary>
        public abstract bool TryLaunch(GuCastContext context, LocalTargetInfo target, out Thing launched, out string reason);
    }

    /// <summary>Launches one native <see cref="Projectile_GuAbility"/> that owns movement and impact.</summary>
    public class GuAbilityWorker_Projectile : GuAbilityWorker
    {
        public override bool TryLaunch(GuCastContext context, LocalTargetInfo target, out Thing launched, out string reason)
        {
            launched = null;
            reason = "The ability could not form its projectile.";
            Map map = context.Caster.Map;
            if (def.projectileDef == null || map == null) return false;
            Projectile_GuAbility projectile = null;
            try
            {
                projectile = (Projectile_GuAbility)ThingMaker.MakeThing(def.projectileDef);
                projectile.ConfigureCast(context.CastId, context.Gu.Id, def);
                GenSpawn.Spawn(projectile, context.Caster.Position, map);
                projectile.Launch(context.Caster, context.Caster.DrawPos, target, target, ProjectileHitFlags.All,
                    preventFriendlyFire: true);
                launched = projectile;
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] {def.defName} launch failed before commitment. {exception}");
                if (projectile is { Destroyed: false })
                {
                    projectile.AbortBeforeCommit();
                    projectile.Destroy(DestroyMode.Vanish);
                }
                launched = null;
                return false;
            }
        }
    }

    /// <summary>Optional, cosmetic and auditory cues for a committed launch. Never affects outcomes.</summary>
    internal static class GuAbilityPresentation
    {
        internal static void Launched(GuCastContext context, Thing launched, LocalTargetInfo target)
        {
            try
            {
                Map map = context.Caster.Map;
                float heading = GuAbilityService.HeadingDegrees(context.Caster.DrawPos, target.CenterVector3);
                context.Ability.launchSound?.PlayOneShot(new TargetInfo(context.Caster.Position, map));
                GuFxMapComponent.TrySpawn(map, context.Ability.launchFx, context.Caster.DrawPos, heading);
                if (launched is { Spawned: true }) GuFxMapComponent.TrySpawn(map, context.Ability.travelFx, launched.DrawPos, heading, launched);
            }
            catch (Exception exception)
            {
                Log.WarningOnce($"[Project GU] A cosmetic launch cue failed; the cast is unaffected. {exception}", 0x6A55A11);
            }
        }
    }
}
