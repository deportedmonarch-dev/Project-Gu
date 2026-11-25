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
    /// </summary>
    public sealed class GuChannelProperties
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

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (workerClass == null || !typeof(GuChannelWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return $"{owner}: channel.workerClass must be a concrete ProjectGu.Combat.GuChannelWorker";
            if (pulseIntervalTicks is < 1 or > 60_000) yield return $"{owner}: channel.pulseIntervalTicks must be between 1 and 60000";
            if (pulseCount is < 1 or > 1000) yield return $"{owner}: channel.pulseCount must be between 1 and 1000";
            if (!float.IsFinite(amountPerPulse) || amountPerPulse <= 0f || amountPerPulse > 1000f)
                yield return $"{owner}: channel.amountPerPulse must be finite, positive and at most 1000";
        }
    }

    public enum GuPulseResult { Applied, Finished, Stopped }

    /// <summary>
    /// The effect of one channel. Workers read and change only state that already has an authority (for essence, the
    /// exact aperture), apply each pulse synchronously and all-or-nothing, and never keep their own copies.
    /// </summary>
    public abstract class GuChannelWorker
    {
        public GuAbilityDef ability;
        public GuChannelProperties props;

        /// <summary>Whether a new session may start now. Also used before resuming.</summary>
        public abstract bool CanChannel(Hediff_Aperture aperture, out string reason);

        /// <summary>Source state a session is bound to; a change in it ends the session.</summary>
        public abstract int BindingFor(Hediff_Aperture aperture);

        /// <summary>Applies one pulse. Returns Stopped with a reason when nothing more can be applied.</summary>
        public abstract GuPulseResult ApplyPulse(Hediff_Aperture aperture, GuChannelSession session, out string reason);
    }

    /// <summary>
    /// Liquor Worm: converts raw essence of the cultivator's current Rank 1 stage into the next quality through the
    /// aperture's own <see cref="Hediff_Aperture.TryUpgradeEssence"/>. Volume, stage, wall progress and capacity are
    /// unchanged; Peak has no output, so nothing ever reaches Rank 2 quality.
    /// </summary>
    public sealed class GuChannelWorker_EssenceRefinement : GuChannelWorker
    {
        public override bool CanChannel(Hediff_Aperture aperture, out string reason)
        {
            reason = "Only a Rank 1 aperture can refine its essence this way.";
            if (aperture == null || aperture.Rank != 1) return false;
            reason = "Rank 1 Peak essence has no higher Rank 1 quality to become.";
            if (aperture.Stage == RankOneStage.Peak) return false;
            reason = $"No {aperture.Stage}-quality essence is stored to refine.";
            return aperture.EssenceOfQuality(aperture.Stage) > 0d;
        }

        public override int BindingFor(Hediff_Aperture aperture) => (int)aperture.Stage;

        public override GuPulseResult ApplyPulse(Hediff_Aperture aperture, GuChannelSession session, out string reason)
        {
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
        public double Converted;
        public float EssencePaid;
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
            Scribe_Values.Look(ref StartedTick, "startedTick");
            Scribe_Values.Look(ref Active, "active");
        }
    }
}
