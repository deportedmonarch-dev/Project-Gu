using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>Whether a rank's essence is the mortal primeval kind or the immortal kind.</summary>
    public enum GuEssenceKind { Primeval, Immortal }

    /// <summary>One minor stage of a rank: its name, the colour of its essence and a short description.</summary>
    public sealed class GuApertureStageVisual
    {
        public string label;
        /// <summary>Essence of this stage's quality. The sea layer and the stage tint read this colour.</summary>
        public Color essenceColor = Color.white;
        [MustTranslate] public string description;
        /// <summary>Optional hand-drawn override of the rank's base art for this stage only (path under Textures, no extension).</summary>
        [NoTranslate] public string baseTexPathOverride;
    }

    /// <summary>
    /// Presentation and terminology for one cultivation rank: the essence name and colours, and the replaceable aperture
    /// art. Pure presentation data; cultivation rules never read it. Ranks without <see cref="implemented"/> only name the
    /// future convention and are never shown as reachable. Art layers are grayscale and recoloured at runtime, so a rank's
    /// four minor stages normally share one set of images. See docs/ALPHA-4A-APERTURE-INTERFACE.md#art-pipeline.
    /// </summary>
    public sealed class GuApertureRankDef : Def
    {
        public int rank = 1;
        [MustTranslate] public string essenceLabel;
        public GuEssenceKind essenceKind = GuEssenceKind.Primeval;
        /// <summary>Representative colour of this rank's essence, used where no stage is known.</summary>
        public Color essenceColor = Color.white;
        /// <summary>True only when cultivation at this rank is playable.</summary>
        public bool implemented;
        /// <summary>Immortal apertures use their own structure art and are not a recoloured mortal aperture.</summary>
        public bool immortalAperture;

        // Layered art (paths under Textures, without extension). Missing layers fall back to generated shapes.
        [NoTranslate] public string baseTexPath;
        [NoTranslate] public string seaMaskTexPath;
        [NoTranslate] public string wallMaskTexPath;
        [NoTranslate] public string highlightsTexPath;
        [NoTranslate] public string cracksTexPath = "ProjectGU/UI/Aperture/Cracks";
        [NoTranslate] public string currentsTexPath = "ProjectGU/UI/Aperture/RefinementCurrents";

        /// <summary>How strongly the stage colour tints the grayscale aperture structure (0 keeps it gray).</summary>
        public float structureTint = 0.4f;

        public List<GuApertureStageVisual> stages = new List<GuApertureStageVisual>();

        public string EssenceLabelCap => essenceLabel.CapitalizeFirst();

        public GuApertureStageVisual Stage(int index) =>
            stages == null || stages.Count == 0 ? null : stages[Mathf.Clamp(index, 0, stages.Count - 1)];

        public Color StageColor(int index) => Stage(index)?.essenceColor ?? essenceColor;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (rank is < 1 or > 9) yield return $"{defName}: rank must be between 1 and 9";
            if (essenceLabel.NullOrEmpty()) yield return $"{defName}: essenceLabel is required";
            if (implemented && (stages == null || stages.Count != 4)) yield return $"{defName}: an implemented rank needs exactly four minor stages";
            if (implemented && (baseTexPath.NullOrEmpty() || seaMaskTexPath.NullOrEmpty() || wallMaskTexPath.NullOrEmpty()))
                yield return $"{defName}: an implemented rank needs baseTexPath, seaMaskTexPath and wallMaskTexPath";
            if (!float.IsFinite(structureTint) || structureTint < 0f || structureTint > 1f) yield return $"{defName}: structureTint must be between 0 and 1";
        }

        /// <summary>The definition for a rank, or null when none is loaded.</summary>
        public static GuApertureRankDef ForRank(int rank)
        {
            foreach (var def in DefDatabase<GuApertureRankDef>.AllDefsListForReading)
                if (def.rank == rank) return def;
            return null;
        }
    }
}
