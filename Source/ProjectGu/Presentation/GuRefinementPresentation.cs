using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// The three refinement mechanics are distinct and each gets its own presentation:
    /// will-enforcement subdues one Gu and establishes recognition; essence refinement improves essence quality through an
    /// already refined Gu (Liquor Worm, presented by its channel manifestation); recipe refinement creates or combines Gu
    /// and does not exist yet.
    /// </summary>
    public enum GuRefinementKind { WillEnforcement, EssenceRefinement, RecipeRefinement }

    /// <summary>Cosmetic layers shown while one kind of refinement is genuinely running. Presentation only.</summary>
    public sealed class GuRefinementPresentationDef : Def
    {
        public GuRefinementKind kind;
        /// <summary>Sustained layers attached to the refining cultivator, drawn in list order.</summary>
        public List<GuFxProfileDef> cultivatorLayers = new();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (cultivatorLayers == null || cultivatorLayers.Count == 0 || cultivatorLayers.Contains(null))
                yield return $"{defName}: cultivatorLayers needs at least one resolved GuFxProfileDef";
        }
    }

    /// <summary>
    /// Reusable hook between refinement jobs and their effects. A job calls <see cref="Sustain"/> from its tick with a
    /// keep-alive derived from its own live state, and <see cref="End"/> from its finish action. Effects are keyed by the
    /// process, never duplicated and never saved; interruption, cancellation, death, despawning, a map change, a reload or
    /// switching effects off all leave nothing behind, and a reloaded job attaches them again from its saved process.
    /// </summary>
    public static class GuRefinementPresentation
    {
        public static GuRefinementPresentationDef For(GuRefinementKind kind)
        {
            foreach (var def in DefDatabase<GuRefinementPresentationDef>.AllDefsListForReading)
                if (def.kind == kind) return def;
            return null;
        }

        public static string Key(string processId, int layer) => $"gu-refine:{processId}:{layer}";

        /// <summary>Attaches any missing layer. Returns the number of layers now present.</summary>
        public static int Sustain(GuRefinementKind kind, Pawn cultivator, string processId, Func<bool> keepAlive)
        {
            if (string.IsNullOrEmpty(processId) || cultivator is not { Spawned: true } || For(kind) is not { } def) return 0;
            var map = cultivator.Map;
            int present = 0;
            for (int layer = 0; layer < def.cultivatorLayers.Count; layer++)
                if (GuFxMapComponent.TryAttachSustained(map, def.cultivatorLayers[layer], cultivator, Key(processId, layer), keepAlive)) present++;
            return present;
        }

        /// <summary>Removes every layer of a process from every map at once.</summary>
        public static void End(GuRefinementKind kind, string processId)
        {
            if (string.IsNullOrEmpty(processId) || Current.Game == null || For(kind) is not { } def) return;
            foreach (var map in Find.Maps)
            {
                var component = map.GetComponent<GuFxMapComponent>();
                if (component == null) continue;
                for (int layer = 0; layer < def.cultivatorLayers.Count; layer++) component.Detach(Key(processId, layer));
            }
        }

        /// <summary>Live layers of a process on a map, for diagnostics and tests.</summary>
        public static int Count(Map map, GuRefinementKind kind, string processId)
        {
            var component = map?.GetComponent<GuFxMapComponent>();
            if (component == null || For(kind) is not { } def) return 0;
            int count = 0;
            for (int layer = 0; layer < def.cultivatorLayers.Count; layer++) count += component.CountKeyed(Key(processId, layer));
            return count;
        }
    }
}
