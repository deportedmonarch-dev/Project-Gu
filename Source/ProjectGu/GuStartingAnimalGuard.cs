using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ProjectGu
{
    [StaticConstructorOnStartup]
    internal static class GuStartingAnimalGuard
    {
        static GuStartingAnimalGuard()
        {
            // Scenario randomization and the editor share this list. Neither checks petness when choosing a named kind.
            new Harmony("projectgu.starting-animals").Patch(
                AccessTools.DeclaredMethod(typeof(ScenPart_StartingAnimal), "PossibleAnimals", new[] { typeof(bool) }),
                postfix: new HarmonyMethod(typeof(GuStartingAnimalGuard), nameof(ExcludeGuKinds)));
        }

        private static void ExcludeGuKinds(ref IEnumerable<PawnKindDef> __result) =>
            __result = __result.Where(kind => !Pawn_GuWildProjection.IsGuKind(kind));
    }
}
