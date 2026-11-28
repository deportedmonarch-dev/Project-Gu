using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

[StaticConstructorOnStartup]
internal static class LiveNativeRelationTrace
{
    private static readonly Dictionary<Pawn, string> created = new();
    private static readonly HashSet<Pawn> admitted = new();
    private static readonly HashSet<Pawn> discarded = new();

    static LiveNativeRelationTrace()
    {
        if (!GenCommandLine.CommandLineArgPassed("gusitecontinuity") && !GenCommandLine.CommandLineArgPassed("guregionalecology")) return;
        var harmony = new Harmony("projectgu.verification.site-relations");
        void Observe(Type type, string method, string callback) => harmony.Patch(AccessTools.DeclaredMethod(type, method),
            postfix: new HarmonyMethod(typeof(LiveNativeRelationTrace), callback));
        Observe(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation), nameof(RelationAdded));
        Observe(typeof(WorldPawns), nameof(WorldPawns.PassToWorld), nameof(WorldAdmission));
        Observe(typeof(Pawn), nameof(Pawn.Discard), nameof(NativeDiscard));
    }

    private static void RelationAdded(Pawn __1)
    {
        if (__1?.RaceProps.Humanlike != true || __1.Spawned || __1.Discarded || Find.WorldPawns?.Contains(__1) == true
            || created.Count >= 4096 || created.ContainsKey(__1)) return;
        var origin = new StackTrace().GetFrames().Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.Assembly == typeof(Pawn).Assembly).Take(6)
            .Select(method => $"{method.DeclaringType.Name}.{method.Name}");
        created.Add(__1, string.Join(" > ", origin));
    }

    private static void WorldAdmission(Pawn __0)
    {
        if (created.ContainsKey(__0) && Find.WorldPawns.Contains(__0)) admitted.Add(__0);
    }

    private static void NativeDiscard(Pawn __instance)
    {
        if (created.ContainsKey(__instance) && __instance.Discarded) discarded.Add(__instance);
    }

    internal static IEnumerable<string> MissingTargets(string stage)
    {
        var stored = PawnsFinder.All_AliveOrDead.ToHashSet();
        int missing = 0;
        foreach (var pawn in stored.Where(pawn => pawn.RaceProps.Humanlike && pawn.relations != null))
        foreach (var relation in pawn.relations.DirectRelations)
        {
            var target = relation.otherPawn;
            if (target?.RaceProps.Humanlike != true || stored.Contains(target)) continue;
            missing++;
            yield return $"Native relation {stage} | {pawn.GetUniqueLoadID()} {relation.def.defName} {target.GetUniqueLoadID()}"
                + $" | world admission {admitted.Contains(target)} | discard {target.Discarded || discarded.Contains(target)}"
                + $" | origin {created.GetValueOrDefault(target, "outside trace")}";
        }
        yield return $"Native relation {stage} | {missing} human targets outside the native pawn graph";
    }
}
