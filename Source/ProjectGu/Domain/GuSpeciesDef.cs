using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Data-only Gu species declaration. Not a Gu instance, population or ability.
    /// Verse DefDatabase is the canonical species catalog; addons can supply XML.
    /// </summary>
    public sealed class GuSpeciesDef : Def
    {
        public int minRank = 1;
        public int maxRank = 1;
        public bool wildPopulationEnabled;
        public PawnKindDef wildProjectionKind;
        public ThingDef dormantProjectionDef;

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
            if (wildProjectionKind != null && (wildProjectionKind.race?.thingClass is not { } pawnClass
                || !typeof(Pawn_GuWildProjection).IsAssignableFrom(pawnClass)))
                yield return $"{defName}: wildProjectionKind requires a Gu wild Pawn race";
            if (dormantProjectionDef != null && !Thing_GuDormantProjection.IsConfigured(dormantProjectionDef))
                yield return $"{defName}: dormantProjectionDef requires an inert, unstackable Gu Thing";
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
