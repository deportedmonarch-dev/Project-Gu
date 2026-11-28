using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
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
        private static readonly HashSet<MapParent> abandoning = new();

        private static GuIndividualRegistry Registry => Current.Game?.GetComponent<GuIndividualRegistry>();
        private static GuWorldComponent Cultivators => Current.Game?.GetComponent<GuWorldComponent>();

        static GuLifecyclePatches()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Game), nameof(Game.DeinitAndRemoveMap))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeMapDeinit)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterMapDeinit)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(MapDeiniter), nameof(MapDeiniter.Deinit))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeMapDeinit)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterMapDeinit)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(MapParent), nameof(MapParent.Abandon))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeParentAbandon)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterParentAbandon)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))),
                    postfix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterWorldObjectRemoval)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.Destroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeCorpseDestroy)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCorpseDestroy)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.PostCorpseDestroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePostCorpseDestroy)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.Destroy))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePawnDestroy)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCultivatorScope)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.Kill))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePawnKill)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCultivatorScope)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.Discard))),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforePawnDiscard)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCultivatorScope)));
                harmony.Patch(Required(AccessTools.DeclaredMethod(typeof(WorldPawns), "RemoveAndDiscardPawnViaGC", new[] { typeof(Pawn) })),
                    prefix: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(BeforeWorldGcDiscard)),
                    finalizer: new HarmonyMethod(typeof(GuLifecyclePatches), nameof(AfterCultivatorScope)));
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

        private static void BeforeMapDeinit(Map map, out bool __state)
        {
            __state = Registry?.BeginMapTeardown(map) == true;
            if (__state) GuSurfaceEcology.Instance?.ObserveBeforeMapRemoval(map);
        }

        private static Exception AfterMapDeinit(Map map, bool __state, Exception __exception)
        {
            if (__state) Registry?.EndMapTeardown(map);
            return __exception;
        }

        private static void BeforeParentAbandon(MapParent __instance, out bool __state) => __state = abandoning.Add(__instance);

        private static Exception AfterParentAbandon(MapParent __instance, bool __state, Exception __exception)
        {
            if (__state) abandoning.Remove(__instance);
            return __exception;
        }

        private static void AfterWorldObjectRemoval(WorldObject __0, bool __runOriginal)
        {
            if (__runOriginal && __0 is Settlement settlement)
                GuSurfaceEcology.Instance?.ObserveRemovedSettlement(settlement, abandoning.Contains(settlement));
        }

        // Destroy clears the corpse contents before the finalizer runs, so the inner Pawn travels in __state.
        private sealed record CorpseScope(Pawn Pawn, GuWorldComponent.NativeScope Identity);

        private static void BeforeCorpseDestroy(Corpse __instance, out CorpseScope __state)
        {
            var pawn = __instance?.InnerPawn;
            __state = new CorpseScope(pawn, Cultivators?.BeginNativeObservation(pawn, GuWorldComponent.NativePath.CorpseDestroy, __instance));
            Registry?.NoteCorpseTeardown(__instance);
        }

        private static Exception AfterCorpseDestroy(CorpseScope __state, Exception __exception)
        {
            Registry?.ForgetCorpseTeardown(__state?.Pawn);
            return AfterCultivatorScope(__state?.Identity, __exception);
        }

        private static void BeforePostCorpseDestroy(Pawn pawn, bool discarded)
        {
            Cultivators?.ObservePostCorpse(pawn, discarded);
            Registry?.BeforeHolderTeardown(pawn, discarded);
        }

        private static void BeforePawnDestroy(Pawn __instance, DestroyMode mode, out GuWorldComponent.NativeScope __state)
        {
            __state = Cultivators?.BeginNativeObservation(__instance, GuWorldComponent.NativePath.PawnDestroy);
            Registry?.BeforeHolderVanish(__instance, mode);
        }

        private static void BeforePawnKill(Pawn __instance, out GuWorldComponent.NativeScope __state) =>
            __state = Cultivators?.BeginNativeObservation(__instance, GuWorldComponent.NativePath.Kill);

        private static void BeforePawnDiscard(Pawn __instance, out GuWorldComponent.NativeScope __state) =>
            __state = Cultivators?.BeginNativeObservation(__instance, GuWorldComponent.NativePath.Discard);

        private static void BeforeWorldGcDiscard(Pawn __0, out GuWorldComponent.NativeScope __state) =>
            __state = Cultivators?.BeginNativeObservation(__0, GuWorldComponent.NativePath.Gc);

        private static Exception AfterCultivatorScope(GuWorldComponent.NativeScope __state, Exception __exception)
        {
            try { __state?.World.EndNativeObservation(__state, __exception); }
            catch (Exception error) { Log.Error($"[Project GU] Cultivator native observation failed. {error}"); }
            return __exception;
        }

        private static void RetainGuCustody(Pawn pawn, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) && !pawn.Discarded && Registry?.IsCustodyCritical(pawn) == true)
                __result = "ProjectGuCustody";
        }
    }
}
