using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ProjectGu.Domain
{
    internal static class GuCorpseFeeding
    {
        private static HediffDef FleshConsumed => DefDatabase<HediffDef>.GetNamed("GU_CarcassConsumed");

        internal static bool HasFlesh(Thing food) => food is Corpse { InnerPawn: { } pawn } corpse
            && corpse.GetComp<CompRottable>() is { Stage: not RotStage.Dessicated }
            && !pawn.health.hediffSet.HasHediff(FleshConsumed);

        internal static void Consume(Corpse corpse)
        {
            var rot = corpse.GetComp<CompRottable>();
            corpse.InnerPawn.health.AddHediff(FleshConsumed);
            // Use native desiccation for appearance and ordinary edibility. The saved mark also guards direct butchering.
            rot.RotProgress = Math.Max(rot.RotProgress, rot.PropsRot.TicksToDessicated + 1);
        }

        internal static bool GuardButchering(Corpse __instance, ref IEnumerable<Thing> __result)
        {
            if (__instance.InnerPawn?.health.hediffSet.HasHediff(FleshConsumed) != true) return true;
            __result = Array.Empty<Thing>();
            return false;
        }
    }
}
