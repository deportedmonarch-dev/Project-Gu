using Verse;

namespace ProjectGu.Domain
{
    // Saved separately from the visible aperture. Mortals are not given a fake hediff.
    public enum AwakeningState { Unattempted = 0, Failed = 1, Awakened = 2 }

    public sealed class GuPawnRecord : IExposable
    {
        public Pawn Pawn;
        public int LatentAptitude; // 0 means no natural awakening potential
        public string ExtremePhysiqueId = ""; // data hook; effects are not implemented
        public AwakeningState State = AwakeningState.Unattempted;
        public bool LegacyMigrated;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref LatentAptitude, "latentAptitude", 0);
            Scribe_Values.Look(ref ExtremePhysiqueId, "extremePhysiqueId", "");
            int stateValue = (int)State;
            Scribe_Values.Look(ref stateValue, "awakeningState", 0);
            Scribe_Values.Look(ref LegacyMigrated, "legacyMigrated", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                State = stateValue == 2 ? AwakeningState.Awakened :
                        stateValue == 1 ? AwakeningState.Failed : AwakeningState.Unattempted;
                if (LatentAptitude != 0 && (LatentAptitude < 20 || LatentAptitude > 100))
                    LatentAptitude = 0;
                if (ExtremePhysiqueId == null) ExtremePhysiqueId = "";
            }
        }
    }
}
