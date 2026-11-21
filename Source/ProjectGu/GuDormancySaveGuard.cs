using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectGu
{
    [StaticConstructorOnStartup]
    internal static class GuDormancySaveGuard
    {
        internal static bool Installed { get; }

        static GuDormancySaveGuard()
        {
            try
            {
                var save = AccessTools.DeclaredMethod(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame), new[] { typeof(string) })
                    ?? throw new MissingMethodException(nameof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame));
                new Harmony("projectgu.livingcultivation.dormancy").Patch(save,
                    prefix: new HarmonyMethod(typeof(GuDormancySaveGuard), nameof(BeforeSave)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The dormancy save guard could not be installed. {exception}");
            }
        }

        private static bool BeforeSave()
        {
            if (Current.Game?.GetComponent<GuIndividualRegistry>()?.DormancyInProgress != true) return true;
            Messages.Message("Wait for the Gu dormancy transition to finish before saving.", MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
