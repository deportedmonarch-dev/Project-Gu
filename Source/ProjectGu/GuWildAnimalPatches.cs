using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    [StaticConstructorOnStartup]
    internal static class GuWildAnimalPatches
    {
        static GuWildAnimalPatches()
        {
            var harmony = new Harmony("projectgu.livingcultivation.wildanimals");
            Prefix(typeof(Designator_Hunt), nameof(Designator_Hunt.CanDesignateThing), nameof(RefuseHunt));
            Prefix(typeof(PawnColumnWorker_Hunt), "HasCheckbox", nameof(ExcludePawn));
            Prefix(typeof(WorkGiver_HunterHunt), nameof(WorkGiver_HunterHunt.HasJobOnThing), nameof(ExcludeThing));
            Prefix(typeof(TameUtility), nameof(TameUtility.CanTame), nameof(ExcludePawn));
            Prefix(typeof(IncidentWorker_AnimalInsanityMass), nameof(IncidentWorker_AnimalInsanityMass.AnimalUsable), nameof(ExcludePawn));
            Prefix(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.AgeTickInterval), nameof(FreezeAge));
            Prefix(typeof(JobGiver_Mate), "TryGiveJob", nameof(ExcludePawn));
            Prefix(typeof(JobGiver_ExitMap), "TryGiveJob", nameof(KeepBlackBoarOnMap));
            Prefix(typeof(JobDriver_Flee), "ExitMap", nameof(KeepFleeingBlackBoarOnMap));
            Prefix(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.CanAssignToTrain), nameof(RefuseTraining),
                new[] { typeof(TrainableDef), typeof(ThingDef), typeof(bool).MakeByRefType(), typeof(Pawn) });
            Postfix(typeof(IncidentWorker_SelfTame), "Candidates", nameof(FilterSelfTame));
            Postfix(typeof(JobDriver), "SetupToils", nameof(GuardExistingOrders));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.ButcherProducts)),
                prefix: new HarmonyMethod(typeof(GuCorpseFeeding), nameof(GuCorpseFeeding.GuardButchering)));

            void Prefix(Type type, string method, string patch, Type[] parameters = null) =>
                harmony.Patch(AccessTools.DeclaredMethod(type, method, parameters),
                    prefix: new HarmonyMethod(typeof(GuWildAnimalPatches), patch));
            void Postfix(Type type, string method, string patch) =>
                harmony.Patch(AccessTools.DeclaredMethod(type, method),
                    postfix: new HarmonyMethod(typeof(GuWildAnimalPatches), patch));
        }

        private static bool RefuseHunt(Thing __0, ref AcceptanceReport __result)
        {
            if (__0 is not Pawn_GuWildProjection) return true;
            __result = "Wild Gu can be captured directly.";
            return false;
        }

        private static bool ExcludePawn(Pawn __0) => __0 is not Pawn_GuWildProjection;
        private static bool ExcludeThing(Thing t) => t is not Pawn_GuWildProjection;
        private static bool FreezeAge(Pawn ___pawn) => ___pawn is not Pawn_GuWildProjection;
        private static bool KeepBlackBoarOnMap(Pawn __0) => __0 is not Pawn_GuWildProjection { IsBlackBoar: true };
        private static bool KeepFleeingBlackBoarOnMap(JobDriver_Flee __instance) => KeepBlackBoarOnMap(__instance.pawn);

        private static bool RefuseTraining(ThingDef pawnDef, ref bool visible, ref AcceptanceReport __result)
        {
            if (pawnDef?.thingClass is not Type pawnClass || !typeof(Pawn_GuWildProjection).IsAssignableFrom(pawnClass)) return true;
            visible = false;
            __result = false;
            return false;
        }

        private static void FilterSelfTame(ref IEnumerable<Pawn> __result) =>
            __result = __result.Where(pawn => pawn is not Pawn_GuWildProjection);

        private static void GuardExistingOrders(JobDriver __instance)
        {
            var job = __instance.job;
            if (job == null) return;
            if ((__instance is JobDriver_Hunt or JobDriver_Tame or JobDriver_Train && job.targetA.Thing is Pawn_GuWildProjection)
                || (__instance is JobDriver_Mate && (__instance.pawn is Pawn_GuWildProjection || job.targetA.Thing is Pawn_GuWildProjection))
                || (__instance is JobDriver_Ingest && __instance.pawn is Pawn_GuWildProjection))
                __instance.AddEndCondition(() => JobCondition.Incompletable);
        }
    }
}
