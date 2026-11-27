using System;
using System.Collections.Generic;
using ProjectGu.Presentation;
using UnityEngine;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Data for one active Gu ability. The species lists which abilities it grants; the worker class decides what a
    /// committed cast does. Costs, targeting rules and timing live here so a new Gu species can reuse the projectile
    /// worker by adding XML, or supply a new worker for a different effect.
    /// </summary>
    /// <summary>Reference grouping for Gu techniques. Gu serve many purposes besides attack.</summary>
    public enum GuAbilityCategory { Combat, Cultivation, Support, Defense, Movement, Information, Utility, Refinement, Transformation }

    public sealed class GuAbilityDef : Def
    {
        public Type workerClass = typeof(GuAbilityWorker_Projectile);

        /// <summary>Primeval essence debited once, at launch commitment. A channel debits it once per new session; zero is allowed for channels that charge per pulse.</summary>
        public float essenceCost = 1f;
        /// <summary>Ticks after a committed launch before the same Gu individual can be used again.</summary>
        public int cooldownTicks;
        /// <summary>Aiming ticks before launch. Interruption during warmup costs nothing.</summary>
        public int warmupTicks = 30;
        public float range = 20f;
        public float minRange = 1.5f;
        public bool targetPawns = true;
        public bool targetLocations = true;
        public bool requireLineOfSight = true;

        /// <summary>Native projectile spawned by <see cref="GuAbilityWorker_Projectile"/>.</summary>
        public ThingDef projectileDef;
        public SoundDef launchSound;
        [NoTranslate] public string iconPath;

        // Optional cosmetic layers. Missing or disabled profiles change nothing in gameplay.
        public GuFxProfileDef launchFx;
        public GuFxProfileDef travelFx;
        public GuFxProfileDef impactFx;

        // Descriptive data for the Aperture tab's Techniques reference. None of it changes gameplay.
        /// <summary>What kind of technique this is. Cultivation channels also appear as controls on the Cultivation page.</summary>
        public GuAbilityCategory category = GuAbilityCategory.Combat;
        /// <summary>Plain statements of what the technique does, shown as bullets.</summary>
        [MustTranslate] public List<string> effectNotes = new List<string>();
        /// <summary>Important restrictions, shown as bullets.</summary>
        [MustTranslate] public List<string> limitations = new List<string>();

        /// <summary>When set, the ability is a self-use timed channel instead of a targeted launch.</summary>
        public GuChannelProperties channel;
        public bool IsChannel => channel != null;

        [Unsaved] private GuAbilityWorker worker;
        [Unsaved] private Texture2D icon;

        public GuAbilityWorker Worker => worker ??= CreateWorker();

        public Texture2D Icon
        {
            get
            {
                if (icon == null && !iconPath.NullOrEmpty()) icon = ContentFinder<Texture2D>.Get(iconPath, false);
                return icon ?? BaseContent.BadTex;
            }
        }

        private GuAbilityWorker CreateWorker()
        {
            var created = (GuAbilityWorker)Activator.CreateInstance(workerClass);
            created.def = this;
            return created;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (workerClass == null || !typeof(GuAbilityWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return $"{defName}: workerClass must be a concrete ProjectGu.Combat.GuAbilityWorker";
            // A channel may charge only per pulse (bodily cultivation), so its activation cost may be zero.
            if (!float.IsFinite(essenceCost) || essenceCost < 0f || (essenceCost == 0f && channel == null) || essenceCost > 1000f)
                yield return $"{defName}: essenceCost must be finite, at most 1000, and positive unless the ability is a channel";
            if (cooldownTicks is < 0 or > 600_000) yield return $"{defName}: cooldownTicks must be between 0 and 600000";
            if (warmupTicks is < 0 or > 6000) yield return $"{defName}: warmupTicks must be between 0 and 6000";
            if (!float.IsFinite(range) || range < 1f || range > 60f) yield return $"{defName}: range must be between 1 and 60";
            if (!float.IsFinite(minRange) || minRange < 0f || minRange >= range) yield return $"{defName}: minRange must be at least 0 and below range";
            if (channel != null)
                foreach (string error in channel.ConfigErrors(defName)) yield return error;
            if (channel == null && !targetPawns && !targetLocations) yield return $"{defName}: the ability must target pawns, locations or both";
            if (channel == null && workerClass == typeof(GuAbilityWorker_Projectile)
                && (projectileDef?.projectile == null || !typeof(Projectile_GuAbility).IsAssignableFrom(projectileDef.thingClass)))
                yield return $"{defName}: the projectile worker requires a projectileDef whose thingClass derives from ProjectGu.Combat.Projectile_GuAbility";
        }
    }
}
