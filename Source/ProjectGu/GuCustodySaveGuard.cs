using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectGu
{
    [StaticConstructorOnStartup]
    internal static class GuCustodySaveGuard
    {
        internal static bool Installed { get; }

        static GuCustodySaveGuard()
        {
            try
            {
                var save = AccessTools.DeclaredMethod(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame), new[] { typeof(string) })
                    ?? throw new MissingMethodException(nameof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame));
                var safeSave = AccessTools.DeclaredMethod(typeof(SafeSaver), nameof(SafeSaver.Save),
                    new[] { typeof(string), typeof(string), typeof(Action), typeof(bool) })
                    ?? throw new MissingMethodException(nameof(SafeSaver), nameof(SafeSaver.Save));
                var harmony = new Harmony("projectgu.livingcultivation.gucustody");
                harmony.Patch(save, prefix: new HarmonyMethod(typeof(GuCustodySaveGuard), nameof(BeforeSave)));
                harmony.Patch(safeSave, prefix: new HarmonyMethod(typeof(GuCustodySaveGuard), nameof(BeforeSafeSave)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The Gu custody save guard could not be installed. {exception}");
            }
        }

        // The native deep writer catches component exceptions and may finish writing. Game saves must stop before it starts.
        private static bool BeforeSafeSave(string __1) => __1 != "savegame" || BeforeSave();

        private static bool BeforeSave()
        {
            var game = Current.Game;
            if (game?.GetComponent<GuWorldComponent>()?.CanSaveCultivators == false)
            {
                Messages.Message("Cultivator identity is not ready to save. See the Project GU log if this persists.",
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var registry = game?.GetComponent<GuIndividualRegistry>();
            if (registry is { CustodyTransitionInProgress: true } or { MapTeardownInProgress: true })
            {
                Messages.Message("Wait for the Gu custody transition to finish before saving.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (Combat.GuCombatLedger.Instance?.SustainedActivationInProgress == true)
            {
                Messages.Message("Wait for the Gu technique to finish activating before saving.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            registry?.ApplyPendingEvents();
            game?.GetComponent<GuWorldComponent>()?.ApplyNativeCultivatorObservations();
            return true;
        }
    }
}
