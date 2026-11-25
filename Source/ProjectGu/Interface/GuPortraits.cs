using System.Collections.Generic;
using ProjectGu.Domain;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>
    /// Portraits for Gu cards, taken from the mod's own sprites: the wild south-facing sprite, then the dormant item
    /// sprite, then a neutral fallback. Resolved once per species on the main thread and cached.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GuPortraits
    {
        private static readonly Dictionary<string, Texture2D> cache = new();
        public static readonly Texture2D Fallback = ContentFinder<Texture2D>.Get("ProjectGU/UI/Aperture/GuFallback", false) ?? BaseContent.GreyTex;

        public static Texture2D For(GuSpeciesDef species)
        {
            if (species == null) return Fallback;
            if (cache.TryGetValue(species.defName, out var cached)) return cached;
            var texture = Wild(species) ?? Dormant(species) ?? Fallback;
            cache[species.defName] = texture;
            return texture;
        }

        /// <summary>The path a portrait came from, for diagnostics and tests.</summary>
        public static string SourceOf(GuSpeciesDef species)
        {
            if (species == null) return "fallback";
            if (Wild(species) != null) return "wild";
            return Dormant(species) != null ? "dormant" : "fallback";
        }

        private static Texture2D Wild(GuSpeciesDef species)
        {
            var stages = species.wildProjectionKind?.lifeStages;
            if (stages == null || stages.Count == 0) return null;
            string path = stages[stages.Count - 1].bodyGraphicData?.texPath;
            if (path.NullOrEmpty()) return null;
            return ContentFinder<Texture2D>.Get(path + "_south", false) ?? ContentFinder<Texture2D>.Get(path, false);
        }

        private static Texture2D Dormant(GuSpeciesDef species)
        {
            string path = species.dormantProjectionDef?.graphicData?.texPath;
            return path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path, false);
        }
    }
}
