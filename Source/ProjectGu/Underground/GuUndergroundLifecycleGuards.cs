using System;
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
        internal static bool Installed { get; }

        static GuUndergroundLifecycleGuards()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                var removeLayer = AccessTools.Method(typeof(WorldGrid), nameof(WorldGrid.RemovePlanetLayer));
                harmony.Patch(removeLayer, prefix: new HarmonyMethod(typeof(GuUndergroundLifecycleGuards), nameof(BeforeRemoveLayer)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Underground lifecycle protection could not be installed. {exception}");
            }
        }

        private static bool BeforeRemoveLayer(PlanetLayer layer)
        {
            if (layer is not GuUndergroundAddressLayer) return true;
            Messages.Message("This underground address layer is retained by Project GU.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
