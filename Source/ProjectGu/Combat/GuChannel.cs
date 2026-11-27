using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Self-use, timed channel for an ability. Instead of launching a projectile, the caster works for
    /// <see cref="pulseCount"/> pulses of <see cref="pulseIntervalTicks"/> each, and the worker applies one bounded effect
    /// per pulse. The ability's <c>essenceCost</c> and <c>cooldownTicks</c> apply once, when a new session commits.
    /// Workers that need their own configuration declare a subclass and the ability's XML selects it with <c>Class="…"</c>.
    /// </summary>
    public class GuChannelProperties
    {
        public Type workerClass = typeof(GuChannelWorker_EssenceRefinement);
        public int pulseIntervalTicks = 250;
        public int pulseCount = 10;
        /// <summary>Worker-defined amount per pulse; for essence refinement, raw essence converted.</summary>
        public float amountPerPulse = 1f;
        /// <summary>Optional pawn-attached manifestation shown only while a session is actively channeling.</summary>
        public GuFxProfileDef manifestationFx;

        [Unsaved] private GuChannelWorker worker;

        public GuChannelWorker Worker(GuAbilityDef ability)
        {
            if (worker != null) return worker;
            worker = (GuChannelWorker)Activator.CreateInstance(workerClass);
            worker.ability = ability;
            worker.props = this;
            return worker;
        }

        public virtual IEnumerable<string> ConfigErrors(string owner)
        {
            if (workerClass == null || !typeof(GuChannelWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return $"{owner}: channel.workerClass must be a concrete ProjectGu.Combat.GuChannelWorker";
            if (pulseIntervalTicks is < 1 or > 60_000) yield return $"{owner}: channel.pulseIntervalTicks must be between 1 and 60000";
            if (pulseCount is < 1 or > 1000) yield return $"{owner}: channel.pulseCount must be between 1 and 1000";
            if (!float.IsFinite(amountPerPulse) || amountPerPulse <= 0f || amountPerPulse > 1000f)
                yield return $"{owner}: channel.amountPerPulse must be finite, positive and at most 1000";
            if (workerClass != null && typeof(GuChannelWorker_BodilyCultivation).IsAssignableFrom(workerClass) && this is not GuChannelProperties_BodilyCultivation)
                yield return $"{owner}: a bodily cultivation channel needs Class=\"ProjectGu.Combat.GuChannelProperties_BodilyCultivation\"";
        }
    }

    public enum GuPulseResult
    {
        /// <summary>The pulse applied; more pulses may follow.</summary>
        Applied,
        /// <summary>The pulse applied and nothing further can apply; the session completes now.</summary>
        Finished,
        /// <summary>The pulse applied nothing; the session ends with earlier pulses kept.</summary>
        Stopped,
        /// <summary>The pulse applied nothing for now (for example, too little essence); the session is suspended and may resume.</summary>
        Suspended
    }

    /// <summary>
    /// The effect of one channel. Each call receives the live grant (caster, exact aperture, Gu record and ability) the
    /// service has just authorized. Workers read and change only state that already has an authority (essence: the
    /// aperture; bodily attainment: the caster's own hediff), apply each pulse synchronously and all-or-nothing, and never
    /// keep their own copies. Workers never authorize, set cooldowns or debit the activation cost.
    /// </summary>
    public abstract class GuChannelWorker
    {
        public GuAbilityDef ability;
        public GuChannelProperties props;

        /// <summary>Whether a new session may start now. Also used before resuming.</summary>
        public abstract bool CanChannel(in GuAbilityGrant grant, out string reason);

        /// <summary>Source state a session is bound to; a change in it ends the session.</summary>
        public abstract int BindingFor(in GuAbilityGrant grant);

        /// <summary>Applies one pulse. Returns Stopped with a reason when nothing more can be applied.</summary>
        public abstract GuPulseResult ApplyPulse(in GuAbilityGrant grant, GuChannelSession session, out string reason);

        // Presentation. None of these change gameplay.

        /// <summary>One line per pulse for the command, Techniques reference and Cultivation page.</summary>
        public virtual string PerPulseText => $"up to {props.amountPerPulse:0.##} essence refined";

        /// <summary>Running-session progress for the Cultivation page.</summary>
        public virtual string ProgressText(Pawn caster, GuChannelSession session) => $"{session.Converted:0.##} essence refined this session";

        /// <summary>What a stopped session keeps, appended to the interruption message.</summary>
        public virtual string KeptOnStop => "Completed refinement is kept; choose it again to resume.";

        /// <summary>Message when a session completes.</summary>
        public virtual string CompletionText(Pawn caster, GuChannelSession session) =>
            $"{caster.LabelShort} finished {session.Ability.label}: {session.Converted:0.##} essence refined to the next quality.";

        /// <summary>Whether a live session counts as essence refinement in the aperture's activity display.</summary>
        public virtual bool RefinesEssence => false;

        /// <summary>The activation cost in words. Channels that charge per pulse have none.</summary>
        public string ActivationText => ability.essenceCost > 0f
            ? $"{ability.essenceCost:0.##} primeval essence when a new session begins; resuming is free"
            : "nothing to begin; essence is paid by each successful pulse";
    }

    /// <summary>
    /// Liquor Worm: converts raw essence of the cultivator's current Rank 1 stage into the next quality through the
    /// aperture's own <see cref="Hediff_Aperture.TryUpgradeEssence"/>. Volume, stage, wall progress and capacity are
    /// unchanged; Peak has no output, so nothing ever reaches Rank 2 quality.
    /// </summary>
    public sealed class GuChannelWorker_EssenceRefinement : GuChannelWorker
    {
        public override bool RefinesEssence => true;

        public override bool CanChannel(in GuAbilityGrant grant, out string reason)
        {
            var aperture = grant.Aperture;
            reason = "Only a Rank 1 aperture can refine its essence this way.";
            if (aperture == null || aperture.Rank != 1) return false;
            reason = "Rank 1 Peak essence has no higher Rank 1 quality to become.";
            if (aperture.Stage == RankOneStage.Peak) return false;
            reason = $"No {aperture.Stage}-quality essence is stored to refine.";
            return aperture.EssenceOfQuality(aperture.Stage) > 0d;
        }

        public override int BindingFor(in GuAbilityGrant grant) => (int)grant.Aperture.Stage;

        public override GuPulseResult ApplyPulse(in GuAbilityGrant grant, GuChannelSession session, out string reason)
        {
            var aperture = grant.Aperture;
            var source = (RankOneStage)session.Binding;
            reason = "The cultivator's stage changed; the remaining refinement no longer applies.";
            if (aperture.Rank != 1 || aperture.Stage != source || source == RankOneStage.Peak) return GuPulseResult.Stopped;
            double amount = Math.Min(props.amountPerPulse, aperture.EssenceOfQuality(source));
            reason = $"No {source}-quality essence remains to refine.";
            if (amount <= 0d) return GuPulseResult.Stopped;
            reason = "The aperture refused the conversion.";
            if (!aperture.TryUpgradeEssence(source, amount)) return GuPulseResult.Stopped;
            session.Converted += amount;
            reason = null;
            return GuPulseResult.Applied;
        }
    }

    /// <summary>
    /// One channel session for one Gu individual and caster. Completed pulses are permanent. An interrupted session is
    /// kept suspended so its owner can resume the remaining pulses without paying again.
    /// </summary>
    public sealed class GuChannelSession : IExposable
    {
        public string SessionId;
        public string GuId;
        public GuAbilityDef Ability;
        public string CasterId;
        public string ApertureId;
        public int Binding;
        public int PulsesDone;
        public int TicksIntoPulse;
        /// <summary>Worker-defined amount applied this session: essence refined, or bodily progress gained.</summary>
        public double Converted;
        /// <summary>Activation cost paid when the session committed.</summary>
        public float EssencePaid;
        /// <summary>Essence paid by pulses this session, for workers that charge per pulse.</summary>
        public double PulseEssencePaid;
        public int StartedTick;
        /// <summary>True while a live job is driving the session. Visuals derive from this plus that job.</summary>
        public bool Active;

        public int PulseCount => Ability?.channel?.pulseCount ?? 0;
        public bool Complete => PulsesDone >= PulseCount;

        public void ExposeData()
        {
            Scribe_Values.Look(ref SessionId, "sessionId");
            Scribe_Values.Look(ref GuId, "guId");
            Scribe_Defs.Look(ref Ability, "ability");
            Scribe_Values.Look(ref CasterId, "casterId");
            Scribe_Values.Look(ref ApertureId, "apertureId");
            Scribe_Values.Look(ref Binding, "binding");
            Scribe_Values.Look(ref PulsesDone, "pulsesDone");
            Scribe_Values.Look(ref TicksIntoPulse, "ticksIntoPulse");
            Scribe_Values.Look(ref Converted, "converted");
            Scribe_Values.Look(ref EssencePaid, "essencePaid");
            Scribe_Values.Look(ref PulseEssencePaid, "pulseEssencePaid");
            Scribe_Values.Look(ref StartedTick, "startedTick");
            Scribe_Values.Look(ref Active, "active");
        }
    }
}
