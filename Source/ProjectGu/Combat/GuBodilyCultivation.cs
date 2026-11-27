using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Configuration for a bodily cultivation channel (Black Boar, White Boar and similar strengths). The species-specific
    /// values live here and in the attainment HediffDef, so another strength is a new HediffDef plus a new ability Def.
    /// </summary>
    public sealed class GuChannelProperties_BodilyCultivation : GuChannelProperties
    {
        /// <summary>The permanent attainment this channel develops. One per strength; severity 0–1 is development.</summary>
        public HediffDef attainment;
        /// <summary>Successful pulses from nothing to full development.</summary>
        public int pulsesToFullDevelopment = 150;
        /// <summary>Primeval essence paid by each successful pulse, atomically with its progress.</summary>
        public float essencePerPulse = 1f;

        public GuChannelProperties_BodilyCultivation() => workerClass = typeof(GuChannelWorker_BodilyCultivation);

        public float ProgressPerPulse => 1f / pulsesToFullDevelopment;

        public override IEnumerable<string> ConfigErrors(string owner)
        {
            foreach (string error in base.ConfigErrors(owner)) yield return error;
            if (workerClass == null || !typeof(GuChannelWorker_BodilyCultivation).IsAssignableFrom(workerClass))
                yield return $"{owner}: bodily cultivation properties need a GuChannelWorker_BodilyCultivation worker";
            if (pulsesToFullDevelopment is < 1 or > 100_000) yield return $"{owner}: channel.pulsesToFullDevelopment must be between 1 and 100000";
            if (!float.IsFinite(essencePerPulse) || essencePerPulse <= 0f || essencePerPulse > 1000f)
                yield return $"{owner}: channel.essencePerPulse must be finite, positive and at most 1000";
            if (attainment == null)
            {
                yield return $"{owner}: channel.attainment must name a HediffDef";
                yield break;
            }
            foreach (string error in Hediff_GuBodilyAttainment.DefErrors(attainment)) yield return $"{owner}: {error}";
        }
    }

    /// <summary>
    /// Bodily cultivation: each successful pulse pays <c>essencePerPulse</c> and raises the caster's attainment hediff by
    /// one step in a single aperture commitment, so essence and progress change together or not at all. Progress lives only
    /// on the caster's body; the Gu, the session and the registry keep no copy, so withdrawal, transfer, loss or death of
    /// the Gu leaves it in place. A fully developed attainment refuses every further session of that strength.
    /// </summary>
    public sealed class GuChannelWorker_BodilyCultivation : GuChannelWorker
    {
        private GuChannelProperties_BodilyCultivation Props => props as GuChannelProperties_BodilyCultivation;

        public override bool CanChannel(in GuAbilityGrant grant, out string reason)
        {
            var settings = Props;
            reason = "This technique is misconfigured.";
            if (settings?.attainment == null || grant.Caster?.health == null || grant.Aperture == null) return false;
            var existing = Hediff_GuBodilyAttainment.Of(grant.Caster, settings.attainment);
            reason = $"{settings.attainment.LabelCap} is already fully developed; this strength can grow no further.";
            if (existing is { FullyDeveloped: true }) return false;
            reason = $"Not enough primeval essence for a pulse ({grant.Aperture.Essence:0.0} of {settings.essencePerPulse:0.0}).";
            if (!grant.Aperture.CanSpendEssence(settings.essencePerPulse)) return false;
            reason = null;
            return true;
        }

        public override int BindingFor(in GuAbilityGrant grant) => 0;

        public override GuPulseResult ApplyPulse(in GuAbilityGrant grant, GuChannelSession session, out string reason)
        {
            var settings = Props;
            var pawn = grant.Caster;
            var aperture = grant.Aperture;
            reason = "This technique is misconfigured.";
            if (settings?.attainment == null || pawn?.health == null || aperture == null) return GuPulseResult.Stopped;
            reason = $"{settings.attainment.LabelCap} is fully developed.";
            if (Hediff_GuBodilyAttainment.Of(pawn, settings.attainment) is { FullyDeveloped: true }) return GuPulseResult.Stopped;
            reason = $"Not enough primeval essence remains for another pulse ({aperture.Essence:0.0} of {settings.essencePerPulse:0.0}).";
            if (!aperture.CanSpendEssence(settings.essencePerPulse)) return GuPulseResult.Suspended;

            float gained = 0f;
            bool full = false;
            // One commitment: the aperture restores the exact essence mixture if the body does not take the progress.
            bool committed = aperture.TrySpendEssenceForCast(settings.essencePerPulse,
                () => Hediff_GuBodilyAttainment.TryAdvance(pawn, settings.attainment, settings.ProgressPerPulse, out gained, out full));
            reason = "The body could not take in the cultivation.";
            if (!committed) return GuPulseResult.Stopped;
            session.Converted += gained;
            session.PulseEssencePaid += settings.essencePerPulse;
            reason = null;
            return full ? GuPulseResult.Finished : GuPulseResult.Applied;
        }

        public override string PerPulseText => Props is { } settings
            ? $"{settings.essencePerPulse:0.##} primeval essence for {settings.ProgressPerPulse:P1} development of {settings.attainment?.label}"
            : base.PerPulseText;

        public override string ProgressText(Pawn caster, GuChannelSession session) => Props is { } settings
            ? $"{session.Converted:P1} development gained this session for {session.PulseEssencePaid:0.##} essence"
              + (Hediff_GuBodilyAttainment.Of(caster, settings.attainment) is { } held ? $"; {held.Progress:P0} developed in all" : "")
            : base.ProgressText(caster, session);

        public override string KeptOnStop => "All development already gained is permanent; choose it again to resume.";

        public override string CompletionText(Pawn caster, GuChannelSession session)
        {
            var held = Props?.attainment is { } def ? Hediff_GuBodilyAttainment.Of(caster, def) : null;
            return held is { FullyDeveloped: true }
                ? $"{caster.LabelShort}'s {held.def.label} is fully developed."
                : $"{caster.LabelShort} finished {session.Ability.label}: {session.Converted:P1} development gained ({held?.Progress ?? 0f:P0} in all).";
        }
    }

    /// <summary>
    /// A permanent bodily attainment: one per strength, never bad, never tended or cured, kept through body-part
    /// restoration and resurrection. Severity is development from 0 to the def's maxSeverity (1). Stat bonuses come from the
    /// def's stages with <c>multiplyStatChangesBySeverity</c>, so the native stat explanation shows them and they grow
    /// continuously; stage labels name the developing condition in the Health tab.
    /// </summary>
    public class Hediff_GuBodilyAttainment : Hediff
    {
        private const float CompletionTolerance = 1e-4f;

        public float Progress => def.maxSeverity > 0f ? Math.Min(1f, Severity / def.maxSeverity) : 0f;
        public bool FullyDeveloped => Severity >= def.maxSeverity - CompletionTolerance;

        /// <summary>Development is permanent. Severity is never reduced by anything in Project GU.</summary>
        public override bool ShouldRemove => false;

        public override string LabelInBrackets => FullyDeveloped
            ? base.LabelInBrackets
            : $"{base.LabelInBrackets}{(base.LabelInBrackets.NullOrEmpty() ? "" : ", ")}{Progress:P0}";

        public static Hediff_GuBodilyAttainment Of(Pawn pawn, HediffDef def) =>
            pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) as Hediff_GuBodilyAttainment;

        public static IEnumerable<Hediff_GuBodilyAttainment> AllOf(Pawn pawn) =>
            pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_GuBodilyAttainment>() ?? Enumerable.Empty<Hediff_GuBodilyAttainment>();

        /// <summary>
        /// Adds one step of development, creating the whole-body attainment on the first step and snapping to full within
        /// rounding of the last. Returns false and changes nothing when the pawn already holds full development.
        /// </summary>
        internal static bool TryAdvance(Pawn pawn, HediffDef def, float step, out float gained, out bool full)
        {
            gained = 0f;
            full = false;
            if (pawn?.health == null || def == null || !(step > 0f)) return false;
            var held = Of(pawn, def);
            if (held is { FullyDeveloped: true }) return false;
            float before = held?.Severity ?? 0f;
            float after = Math.Min(def.maxSeverity, before + step);
            if (def.maxSeverity - after < step * 0.5f) after = def.maxSeverity;
            if (held == null)
            {
                held = (Hediff_GuBodilyAttainment)HediffMaker.MakeHediff(def, pawn);
                held.Severity = after;
                pawn.health.AddHediff(held);
                if (Of(pawn, def) != held) return false;
            }
            else
            {
                held.Severity = after;
                // Stage changes notify natively; within a stage the scaled bonuses still change, so refresh the caches.
                pawn.health.Notify_HediffChanged(held);
            }
            gained = held.Severity - before;
            full = held.FullyDeveloped;
            return gained > 0f;
        }

        internal static IEnumerable<string> DefErrors(HediffDef def)
        {
            if (def.hediffClass == null || !typeof(Hediff_GuBodilyAttainment).IsAssignableFrom(def.hediffClass))
                yield return $"{def.defName} must use hediffClass ProjectGu.Combat.Hediff_GuBodilyAttainment";
            if (Math.Abs(def.maxSeverity - 1f) > 1e-6f) yield return $"{def.defName} must have maxSeverity 1";
            if (def.isBad || def.tendable || def.everCurableByItem || def.chronic || def.forceRemoveOnResurrection || def.lethalSeverity > 0f)
                yield return $"{def.defName} must be not bad, untendable, not curable by items, not chronic, not lethal and kept on resurrection";
            if (!def.keepOnBodyPartRestoration) yield return $"{def.defName} must set keepOnBodyPartRestoration so resurrection keeps it";
            if (def.stages.NullOrEmpty() || def.stages.Any(stage => !stage.multiplyStatChangesBySeverity))
                yield return $"{def.defName} must define stages with multiplyStatChangesBySeverity so bonuses grow with development";
        }
    }
}
