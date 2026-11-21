using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectGu
{
    [StaticConstructorOnStartup]
    internal static class GuGearInventoryGuard
    {
        private static readonly Func<ITab_Pawn_Gear, Pawn> GearPawn;
        internal static bool Installed { get; }

        static GuGearInventoryGuard()
        {
            try
            {
                var tab = typeof(ITab_Pawn_Gear);
                GearPawn = AccessTools.PropertyGetter(tab, "SelPawnForGear").CreateDelegate<Func<ITab_Pawn_Gear, Pawn>>();
                var drop = AccessTools.DeclaredMethod(tab, "InterfaceDrop", new[] { typeof(Thing) })
                    ?? throw new MissingMethodException(tab.FullName, "InterfaceDrop");
                new Harmony("projectgu.livingcultivation.gearinventory").Patch(drop,
                    prefix: new HarmonyMethod(typeof(GuGearInventoryGuard), nameof(BeforeGearDrop)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The native Gear custody guard could not be installed. {exception}");
            }
        }

        private static bool BeforeGearDrop(ITab_Pawn_Gear __instance, Thing __0)
        {
            if (__0 is not Thing_GuDormantProjection thing) return true;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null) return false;
            if (!registry.TryDropDormantFromGear(thing, GearPawn(__instance), out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
