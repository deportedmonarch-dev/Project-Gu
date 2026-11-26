using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ProjectGu.Botany
{
    /// <summary>
    /// The plant framework's two native hooks, each restricted to plants carrying <see cref="CompProperties_GuPlant"/>:
    /// <list type="bullet">
    /// <item>Seed-gated sowing: a native grower sow job for a seed-gated plant becomes a sow job that carries and spends its
    /// planting material, or is refused with a reason (<see cref="GuPlantFramework.GateSowJob"/>).</item>
    /// <item>Double-click selection: vanilla picks any pawn within a cell of the cursor before the plant under it, so in a
    /// cavern where wild Gu and colonists wander among the orchids a double-click often selected pawns instead. When the cursor
    /// is on a Project GU plant and not on a pawn, the same-def plants on screen are selected, exactly as vanilla does for an
    /// ordinary thing.</item>
    /// </list>
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GuPlantPatches
    {
        internal const string HarmonyId = "projectgu.livingcultivation.plants";
        internal static bool SowingGateInstalled { get; }
        internal static bool SelectionInstalled { get; }

        static GuPlantPatches()
        {
            var harmony = new Harmony(HarmonyId);
            try
            {
                var sow = AccessTools.DeclaredMethod(typeof(WorkGiver_GrowerSow), nameof(WorkGiver_GrowerSow.JobOnCell))
                    ?? throw new MissingMethodException(nameof(WorkGiver_GrowerSow), nameof(WorkGiver_GrowerSow.JobOnCell));
                harmony.Patch(sow, postfix: new HarmonyMethod(typeof(GuPlantPatches), nameof(AfterGrowerSowJob)));
                SowingGateInstalled = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Seed-gated sowing could not be installed; Project GU plants will sow without planting material. {exception}");
            }
            try
            {
                var select = AccessTools.DeclaredMethod(typeof(Selector), "SelectAllMatchingObjectUnderMouseOnScreen")
                    ?? throw new MissingMethodException(nameof(Selector), "SelectAllMatchingObjectUnderMouseOnScreen");
                harmony.Patch(select, prefix: new HarmonyMethod(typeof(GuPlantPatches), nameof(BeforeSelectAllMatching)));
                SelectionInstalled = true;
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Project GU plant double-click selection could not be installed; vanilla selection applies. {exception}");
            }
        }

        private static void AfterGrowerSowJob(Pawn pawn, bool forced, ref Job __result)
        {
            if (__result?.def == JobDefOf.Sow) __result = GuPlantFramework.GateSowJob(pawn, __result, forced);
        }

        private static bool BeforeSelectAllMatching(Selector __instance)
        {
            try
            {
                var map = Find.CurrentMap;
                if (map == null) return true;
                Vector3 mouse = UI.MouseMapPosition();
                IntVec3 cell = UI.MouseCell();
                if (!cell.InBounds(map) || cell.Fogged(map) || cell.GetPlant(map) is not { } plant || !GuPlantFramework.IsGuPlant(plant.def)) return true;
                // A pawn directly under the cursor is what the player clicked; leave that to vanilla.
                var pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                    if ((pawns[i].DrawPos - mouse).MagnitudeHorizontal() < 0.4f) return true;
                SelectMatching(__instance, plant, ThingsInView(map, plant.def));
                return false;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce($"[Project GU] Project GU plant selection failed; using vanilla selection. {exception}", 0x47555053);
                return true;
            }
        }

        /// <summary>
        /// Same-def things in the camera's current view. Read straight from the view rectangle and the thing lister, which does
        /// not depend on converting screen corners to map cells.
        /// </summary>
        internal static IEnumerable<Thing> ThingsInView(Map map, ThingDef def)
        {
            var view = Find.CameraDriver.CurrentViewRect;
            var things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
                if (things[i].Spawned && view.Contains(things[i].Position)) yield return things[i];
        }

        /// <summary>Selects the clicked plant and every candidate of the same def and faction. Other plants are never selected.</summary>
        internal static int SelectMatching(Selector selector, Plant clicked, IEnumerable<Thing> candidates)
        {
            int selected = 0;
            if (!selector.IsSelected(clicked)) { selector.Select(clicked, playSound: false, forceDesignatorDeselect: false); selected++; }
            foreach (var thing in candidates)
            {
                if (thing == clicked || thing.def != clicked.def || thing.Faction != clicked.Faction || selector.IsSelected(thing)
                    || !ThingSelectionUtility.SelectableByMapClick(thing)) continue;
                selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                selected++;
            }
            return selected;
        }
    }
}
