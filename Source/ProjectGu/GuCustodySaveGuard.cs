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
                new Harmony("projectgu.livingcultivation.gucustody").Patch(save,
                    prefix: new HarmonyMethod(typeof(GuCustodySaveGuard), nameof(BeforeSave)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The Gu custody save guard could not be installed. {exception}");
            }
        }

        private static bool BeforeSave()
        {
            if (Current.Game?.GetComponent<GuIndividualRegistry>()?.CustodyTransitionInProgress != true) return true;
            Messages.Message("Wait for the Gu custody transition to finish before saving.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
