using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Underground
{
    [StaticConstructorOnStartup]
    internal static class GuUndergroundLifecycleGuards
    {
        private const string HarmonyId = "projectgu.livingcultivation.underground";
        [ThreadStatic] private static Map rejectedPocket;
        [ThreadStatic] private static GuUndergroundLevelParent rejectedLevel;
        internal static bool Installed { get; }
        private static GuPocketProbeComponent Registry => Current.Game?.GetComponent<GuPocketProbeComponent>();

        static GuUndergroundLifecycleGuards()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                Patch(harmony, typeof(WorldGrid), nameof(WorldGrid.RemovePlanetLayer), nameof(BeforeRemoveLayer));
                Patch(harmony, typeof(SettlementAbandonUtility), nameof(SettlementAbandonUtility.TryAbandonViaInterface), nameof(BeforeAbandon));
                Patch(harmony, typeof(SettlementAbandonUtility), "Abandon", nameof(BeforeAbandon));
                Patch(harmony, typeof(Settlement), nameof(Settlement.Abandon), nameof(BeforeParentAbandon));
                Patch(harmony, typeof(MapParent), nameof(MapParent.Abandon), nameof(BeforeParentAbandon));
                Patch(harmony, typeof(MapParent), nameof(MapParent.CheckRemoveMapNow), nameof(BeforeAutomaticRemoval));
                Patch(harmony, typeof(WorldObject), nameof(WorldObject.Destroy), nameof(BeforeWorldObjectDestroy));
                Patch(harmony, typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove), nameof(BeforeWorldObjectRemove));
                Patch(harmony, typeof(Game), nameof(Game.DeinitAndRemoveMap), nameof(BeforeMapRemoval));
                Patch(harmony, typeof(PocketMapUtility), nameof(PocketMapUtility.DestroyPocketMap), nameof(BeforeMapRemoval));
                harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)),
                    postfix: new HarmonyMethod(typeof(GuUndergroundLifecycleGuards), nameof(AfterPawnGizmos)));
                if (ModsConfig.OdysseyActive)
                {
                    Patch(harmony, typeof(GravshipUtility), nameof(GravshipUtility.PreLaunchConfirmation), nameof(BeforeGravshipLaunch));
                    Patch(harmony, typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.InitiateTakeoff), nameof(BeforeGravshipLaunch));
                    Patch(harmony, typeof(GravshipUtility), nameof(GravshipUtility.AbandonMap), nameof(BeforeMapRemoval));
                }
                if (ModsConfig.IdeologyActive)
                {
                    Patch(harmony, typeof(QuestPart_NewColony), "TileChosen", nameof(BeforeColonyReset));
                    Patch(harmony, typeof(QuestPart_NewColony), "InitMoveColony", nameof(BeforeColonyReset));
                    Patch(harmony, typeof(MoveColonyUtility), nameof(MoveColonyUtility.MoveColonyAndReset), nameof(BeforeDirectColonyReset));
                }
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Underground lifecycle protection could not be installed. {exception}");
            }
        }

        private static void Patch(Harmony harmony, Type declaringType, string method, string prefix)
        {
            MethodInfo original = AccessTools.DeclaredMethod(declaringType, method)
                ?? throw new MissingMethodException(declaringType.FullName, method);
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(GuUndergroundLifecycleGuards), prefix));
        }

        private static bool BeforeRemoveLayer(PlanetLayer __0) =>
            Allow(__0 is not GuUndergroundAddressLayer, "This underground address layer is retained by Project GU.");

        private static bool BeforeAbandon(MapParent __0) => BeforeParentAbandon(__0);

        private static bool BeforeParentAbandon(MapParent __instance) =>
            Allow(!ProtectsParent(__instance), "Protected underground content depends on this map.");

        private static bool BeforeAutomaticRemoval(MapParent __instance)
        {
            if (__instance is GuUndergroundLevelParent) return false;
            Map map = __instance.Map;
            // The native Settlement predicate already retains player-home maps.
            return map == null || (__instance.GetType() == typeof(Settlement) && map.IsPlayerHome) || !ProtectsMap(map);
        }

        private static bool BeforeWorldObjectDestroy(WorldObject __instance) => BeforeWorldObjectRemove(__instance);

        private static bool BeforeWorldObjectRemove(WorldObject __0) =>
            Allow(__0 is not MapParent parent || !ProtectsParent(parent), "Protected underground content depends on this map.");

        private static bool BeforeMapRemoval(Map __0) =>
            Allow(!ProtectsMap(__0), "Protected underground content depends on this map.");

        private static bool ProtectsParent(MapParent parent) => parent is GuUndergroundLevelParent
            ? parent != rejectedLevel : Registry != null && ProtectsMap(parent?.Map);

        private static bool ProtectsMap(Map map)
        {
            if (map == null || map == rejectedPocket || Registry is not { } registry) return false;
            if (map.Parent is GuUndergroundLevelParent parent) return parent != rejectedLevel;
            return registry.ProtectsLegacyLevel(map) || registry.ProtectsLegacySource(map) || registry.ProtectsIndependentAccess(map);
        }

        private static bool BeforeGravshipLaunch(Building_GravEngine __0)
        {
            Map map = __0?.Map;
            if (map?.Parent is GuUndergroundLevelParent)
                return Allow(false, "Gravships cannot launch from an underground level.");
            bool anchored = map?.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor) == true;
            return Allow(anchored || !ProtectsMap(map), "An unanchored launch would remove a protected underground route.");
        }

        private static bool ResetRisk => Registry is { } registry && (registry.HasLegacyDependents || registry.HasIndependentResetRisk);

        private static bool BeforeColonyReset() =>
            Allow(!ResetRisk, "Colony reset would affect protected underground contents or occupants.");

        private static void BeforeDirectColonyReset()
        {
            if (ResetRisk)
                throw new InvalidOperationException("Project GU blocked colony reset before map or pawn mutation.");
        }

        private static void AfterPawnGizmos(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance.Map?.Parent is GuUndergroundLevelParent && Registry is { } registry)
                __result = registry.RecoveryGizmos(__instance, __result);
        }

        private static bool Allow(bool accepted, string reason)
        {
            if (!accepted)
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            return accepted;
        }

        internal static void CleanupRejectedPocket(Map map)
        {
            rejectedPocket = map;
            try { PocketMapUtility.DestroyPocketMap(map); }
            finally { rejectedPocket = null; }
        }

        internal static bool IsRejectedPocket(Map map) => map == rejectedPocket;

        internal static void CleanupRejectedLevel(GuUndergroundLevelParent parent)
        {
            rejectedLevel = parent;
            try { parent.Destroy(); }
            finally { rejectedLevel = null; }
        }
    }
}
