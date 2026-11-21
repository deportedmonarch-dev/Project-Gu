using LudeonTK;
using RimWorld;
using Verse;
using ProjectGu.Domain;
using System.Linq;

namespace ProjectGu
{
    /// <summary>
    /// Dev-mode tools ONLY. These bypass spirit-spring ritual prerequisites,
    /// which will be introduced in 0.2.1+. Not available in normal gameplay.
    /// </summary>
    public static class GuDebugActions
    {
        [DebugAction("Project GU", "Spawn test Gu", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnTestGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail("GU_DevTestSpringGu");
            if (registry?.TrySpawnDevelopmentGu(species, Find.CurrentMap, UI.MouseCell(), out _) != true)
                Messages.Message("Choose an open cell on a loaded map for the test Gu.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Make test Gu dormant", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeTestGuDormant(Pawn selected)
        {
            if (selected is not Pawn_GuWildProjection pawn
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return;
            if (!registry.TryMakeDormant(pawn, pawn.GuRevision, pawn.Position, out _, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Put selected dormant Gu in inventory", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TakeDormantGu(Pawn pawn)
        {
            if (Find.Selector.SingleSelectedThing is not Thing_GuDormantProjection thing
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry)
            {
                Messages.Message("Select a dormant Gu, then click the adjacent colonist.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!registry.TryTakeDormant(thing, thing.GuRevision, pawn, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Drop dormant Gu from selected colonist", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DropDormantGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (Find.Selector.SingleSelectedThing is not Pawn pawn || pawn.Map != Find.CurrentMap
                || pawn.inventory?.GetDirectlyHeldThings() is not { } inventory || registry == null)
            {
                Messages.Message("Select a colonist with a dormant Gu, then click an adjacent open cell.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 cell = UI.MouseCell();
            var options = inventory.OfType<Thing_GuDormantProjection>().Where(thing => registry.Resolve(thing) != null).Select(thing =>
            {
                int revision = thing.GuRevision;
                return new FloatMenuOption($"{thing.LabelCap} ({thing.GuId})", () =>
                {
                    if (!registry.TryDropDormant(thing, revision, pawn, cell, out string reason))
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                });
            }).ToList();
            if (options.Count == 0)
                Messages.Message("The selected colonist has no Gu with a valid inventory binding.", MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Project GU", "Log Gu individual records", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogIndividuals()
        {
            if (!Prefs.DevMode || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return;
            Log.Message(registry.Individuals.Count == 0 ? "[Project GU] No individual Gu records."
                : "[Project GU] Individual Gu records:\n" + string.Join("\n\n", registry.Individuals
                    .Where(record => record != null).Select(record => record.DebugDescription)));
        }

        [DebugAction("Project GU", "Inspect hidden cultivation potential", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Inspect(Pawn pawn)
        {
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            GuPawnRecord record = world?.GetOrCreate(pawn);
            if (record == null) return;
            string grade = record.LatentAptitude == 0 ? "none" :
                AptitudeRules.Grade(record.LatentAptitude) + " (" + record.LatentAptitude + ")";
            Messages.Message(pawn.LabelShort + ": state=" + record.State + ", hidden potential=" + grade,
                MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Project GU", "Test awakening on pawn", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Awaken(Pawn pawn)
        {
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            if (world == null) return;
            world.TryAwaken(pawn, out string result);
            Messages.Message(pawn.LabelShort + ": " + result, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
