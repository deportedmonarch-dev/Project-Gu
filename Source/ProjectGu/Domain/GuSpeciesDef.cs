using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Data-only Gu species declaration. Not a Gu instance, population or ability.
    /// Verse DefDatabase is the canonical species catalog; addons can supply XML.
    /// </summary>
    /// <summary>
    /// Species hook that runs once after a terminal outcome commits, outside the transaction. It may place residue or
    /// materials. It must not change the record. Exceptions are logged and do not undo the outcome.
    /// </summary>
    public abstract class GuTerminalEffectWorker
    {
        public abstract void Apply(GuRecord record, GuTerminalOutcome outcome, GuLocation location, Map map);
    }

    public sealed class GuSpeciesDef : Def
    {
        public int minRank = 1;
        public int maxRank = 1;
        public bool wildPopulationEnabled;
        // Opts into the shared surface habitat, recovery and ground encounter behavior.
        public GuSurfaceHabitat surfaceHabitat;
        public GuSurfaceProfileDef surfaceProfile;
        // Minimum quiet period for this species. -1 preserves the exact individual indefinitely.
        public int wildIdentityRetentionDays;
        internal bool HasSurfacePopulation => wildPopulationEnabled && surfaceHabitat != GuSurfaceHabitat.None;
        public List<GuSurfaceSource> surfaceRecoverySources = new() { GuSurfaceSource.NaturalReplenishment, GuSurfaceSource.Migration };
        public PawnKindDef wildProjectionKind;
        public ThingDef dormantProjectionDef;
        public float wildCuriosityChancePerHour;

        // Tunable mortal will-enforcement prototype inputs; rank and cultivator capability scale these values.
        public int refinementBaseWorkTicks = 9000;
        public float refinementEssenceCost = 12f;
        public float refinementResistance = 1f;

        // Active abilities this species grants to the cultivator it recognizes while stored in that cultivator's aperture.
        public List<ProjectGu.Combat.GuAbilityDef> abilities = new List<ProjectGu.Combat.GuAbilityDef>();

        // Descriptive data for the Aperture tab. None of it changes gameplay.
        /// <summary>Short role shown on Gu cards and in the Techniques reference, for example "attack" or "support".</summary>
        [MustTranslate] public string role;
        /// <summary>Effects this Gu provides that are not listed abilities (passive or contextual), shown as bullets.</summary>
        [MustTranslate] public List<string> effectNotes = new List<string>();

        /// <summary>
        /// Individual feeding policy (Alpha 5A). Missing means no implemented policy: nothing is simulated and no diet is
        /// invented. The lure items above are bait, not meals.
        /// </summary>
        public GuFeedingProperties feeding;

        public GuFeedingStatus FeedingStatus => feeding?.status ?? GuFeedingStatus.NotImplemented;
        public GuFeedingProperties SimulatedFeeding => feeding?.Simulated == true ? feeding : null;

        // Optional lure encounter: instead of a plain search, a colonist sets out one of these items at the habitat and waits.
        public List<ThingDef> encounterLures = new List<ThingDef>();
        public int encounterLureTicks = 600;
        public bool UsesLureEncounter => encounterLures is { Count: > 0 };

        // Lifecycle policy (owner decisions §1, §4). Values are provisional until the owner approves species defaults.
        /// <summary>What happens to an ordinary individual whose native map or holder is removed without it dying.</summary>
        public GuPreservationPolicy abandonmentPolicy = GuPreservationPolicy.Individual;
        /// <summary>Individuals at or above this rank always keep their identity (Immortal Gu by default).</summary>
        public int individualPreservationMinRank = 6;
        /// <summary>Optional species effect after a terminal outcome, such as residue. Most species leave nothing.</summary>
        public Type terminalEffectWorker;

        // Extensible metadata only: tags do not execute behaviors.
        public List<string> pathTags = new List<string>();
        public List<string> habitatTags = new List<string>();
        public List<string> capabilityTags = new List<string>();

        public bool HasHabitatTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || habitatTags == null) return false;
            foreach (string current in habitatTags)
                if (string.Equals(current, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (minRank < 1 || minRank > 9)
                yield return $"{defName}: minRank must be between 1 and 9";
            if (maxRank < minRank || maxRank > 9)
                yield return $"{defName}: maxRank must be between minRank and 9";
            if (wildIdentityRetentionDays is < -1 or > 600)
                yield return $"{defName}: wildIdentityRetentionDays must be -1 or between 0 and 600";
            if (refinementBaseWorkTicks is < 1 or > 600_000)
                yield return $"{defName}: refinementBaseWorkTicks must be between 1 and 600000";
            if (surfaceHabitat != GuSurfaceHabitat.None && (!wildPopulationEnabled || minRank != 1 || wildProjectionKind == null || dormantProjectionDef == null))
                yield return $"{defName}: surface populations need enabled Rank 1 wild and dormant projections";
            if (surfaceRecoverySources != null && (surfaceRecoverySources.Distinct().Count() != surfaceRecoverySources.Count
                || surfaceRecoverySources.Any(source => source is not (GuSurfaceSource.NaturalReplenishment or GuSurfaceSource.Migration or GuSurfaceSource.NaturalFormation))
                || surfaceRecoverySources.Contains(GuSurfaceSource.NaturalFormation) && surfaceHabitat != GuSurfaceHabitat.Rocky))
                yield return $"{defName}: invalid surface source order or missing local formation habitat rule";
            if (!float.IsFinite(wildCuriosityChancePerHour) || wildCuriosityChancePerHour is < 0f or > 1f)
                yield return $"{defName}: wildCuriosityChancePerHour must be between 0 and 1";
            if (!float.IsFinite(refinementEssenceCost) || refinementEssenceCost <= 0f)
                yield return $"{defName}: refinementEssenceCost must be finite and positive";
            if (!float.IsFinite(refinementResistance) || refinementResistance < 0.1f || refinementResistance > 10f)
                yield return $"{defName}: refinementResistance must be between 0.1 and 10";
            // Phase 1 staging still relies on the legacy Pawn class for tick suppression (RFC-0004 §5.1).
            if (wildProjectionKind != null && (wildProjectionKind.race?.thingClass is not { } pawnClass
                || !typeof(Pawn_GuWildProjection).IsAssignableFrom(pawnClass)
                || !wildProjectionKind.race.HasComp(typeof(CompGuManifestation)) || !wildProjectionKind.race.notifyMapRemoved))
                yield return $"{defName}: wildProjectionKind requires a Gu wild Pawn race with CompGuManifestation and notifyMapRemoved";
            else if (wildProjectionKind != null && !Pawn_GuWildProjection.HasNativeGenerationExclusions(wildProjectionKind))
                yield return $"{defName}: wildProjectionKind must exclude native pet, biome, commerce, quest and incident generation";
            if (dormantProjectionDef != null && !Thing_GuDormantProjection.IsConfigured(dormantProjectionDef))
                yield return $"{defName}: dormantProjectionDef requires an inert, unstackable, untradeable Gu Thing with CompGuManifestation and notifyMapRemoved";
            if (encounterLureTicks is < 1 or > 60_000)
                yield return $"{defName}: encounterLureTicks must be between 1 and 60000";
            if (encounterLures != null && encounterLures.Any(lure => lure == null || lure.category != ThingCategory.Item))
                yield return $"{defName}: encounterLures must list item ThingDefs";
            if (abilities != null)
                foreach (var ability in abilities)
                    if (ability == null) yield return $"{defName}: abilities contains an unresolved GuAbilityDef";
            if (terminalEffectWorker != null && !typeof(GuTerminalEffectWorker).IsAssignableFrom(terminalEffectWorker))
                yield return $"{defName}: terminalEffectWorker must derive from ProjectGu.Domain.GuTerminalEffectWorker";
            if (feeding != null)
                foreach (string error in feeding.ConfigErrors(defName)) yield return error;
            foreach (string error in ValidateTags(pathTags, "pathTags")) yield return error;
            foreach (string error in ValidateTags(habitatTags, "habitatTags")) yield return error;
            foreach (string error in ValidateTags(capabilityTags, "capabilityTags")) yield return error;
            // Habitat tags are *preferences*, not universal prerequisites.
            // Wild Gu may exist freely on maps or in regional populations.
            // Specific hard prerequisites belong to pluggable species suitability policies.
        }

        private IEnumerable<string> ValidateTags(List<string> tags, string field)
        {
            if (tags == null) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    yield return $"{defName}: {field} contains an empty tag";
                else if (!seen.Add(tag.Trim()))
                    yield return $"{defName}: {field} contains duplicate tag '{tag}'";
            }
        }
    }
}
