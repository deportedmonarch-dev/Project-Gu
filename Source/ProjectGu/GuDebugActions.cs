using LudeonTK;
using RimWorld;
using Verse;
using ProjectGu.Domain;

namespace ProjectGu
{
    /// <summary>
    /// Dev-mode tools ONLY. These bypass spirit-spring ritual prerequisites,
    /// which will be introduced in 0.2.1+. Not available in normal gameplay.
    /// </summary>
    public static class GuDebugActions
    {
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
