using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Native evidence capture for the Phase 2 lifecycle. Each patch only records evidence or retains custody. Canonical
    /// changes are applied later by the registry outside the native callback.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GuLifecyclePatches
    {
        internal const string HarmonyId = "projectgu.livingcultivation.lifecycle";
        internal static bool Installed { get; }

        private static GuIndividualRegistry Registry => Current.Game?.GetComponent<GuIndividualRegistry>();

        static GuLifecyclePatches()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeMapDeinit)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterMapDeinit)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.Destroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeCorpseDestroy)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCorpseDestroy)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.PostCorpseDestroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePostCorpseDestroy)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.Destroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePawnDestroy)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(WorldPawnGC), "GetCriticalPawnReason")),
                    postfix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(RetainGuCustody)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Gu lifecycle evidence patches could not be installed. Map removal and holder teardown will leave Gu unresolved. {exception}");
            }
        }

        private static System.Reflection.MethodInfo Required(System.Reflection.MethodInfo method) =>
            method ?? throw new MissingMethodException("A native lifecycle method required by Project GU was not found.");

        private static void BeforeMapDeinit(Map map) => Registry?.BeginMapTeardown(map);

        private static Exception AfterMapDeinit(Map map, Exception __exception)
        {
            Registry?.EndMapTeardown(map);
            return __exception;
        }

        // Destroy clears the corpse contents before the finalizer runs, so the inner Pawn travels in __state.
        private static void BeforeCorpseDestroy(Corpse __instance, out Pawn __state)
        {
            __state = __instance?.InnerPawn;
            Registry?.NoteCorpseTeardown(__instance);
        }

        private static Exception AfterCorpseDestroy(Pawn __state, Exception __exception)
        {
            Registry?.ForgetCorpseTeardown(__state);
            return __exception;
        }

        private static void BeforePostCorpseDestroy(Pawn pawn, bool discarded) => Registry?.BeforeHolderTeardown(pawn, discarded);

        private static void BeforePawnDestroy(Pawn __instance, DestroyMode mode) => Registry?.BeforeHolderVanish(__instance, mode);

        private static void RetainGuCustody(Pawn pawn, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) && !pawn.Discarded && Registry?.IsCustodyCritical(pawn) == true)
                __result = "ProjectGuCustody";
        }
    }
}
