using System.Collections.Generic;
using RimWorld;
using Verse;
using ProjectGu.Domain;

namespace ProjectGu
{
    /// <summary>
    /// Save-backed registry. No mutable global static cache; one canonical record per
    /// pawn. 600-tick map discovery is retained as a safe interim compatibility path.
    /// No pawn-wide simulation or AI is performed in that scan.
    /// </summary>
    public class GuWorldComponent : GameComponent
    {
        private List<GuPawnRecord> records = new List<GuPawnRecord>();
        private Dictionary<Pawn, GuPawnRecord> byPawn = new Dictionary<Pawn, GuPawnRecord>();
        private static HediffDef MortalDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_MortalUnawakened");
        private static HediffDef FailedDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_MortalNoAptitude");
        private static HediffDef ApertureDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_Aperture");

        public GuWorldComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref records, "guPawnRecords", LookMode.Deep);
            if (records == null) records = new List<GuPawnRecord>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RebuildIndex();
        }

        private void RebuildIndex()
        {
            byPawn = new Dictionary<Pawn, GuPawnRecord>();
            // Remove corrupt/duplicate/null entries once after loading, not every tick.
            var valid = new List<GuPawnRecord>(records.Count);
            foreach (GuPawnRecord record in records)
            {
                if (record == null || record.Pawn == null || byPawn.ContainsKey(record.Pawn)) continue;
                byPawn.Add(record.Pawn, record);
                valid.Add(record);
            }
            records = valid;
        }

        public GuPawnRecord GetOrCreate(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return null;
            if (byPawn.TryGetValue(pawn, out GuPawnRecord existing)) return existing;

            // Migration MUST check existing 0.1.x aperture before rolling potential.
            var aperture = pawn.health.hediffSet.GetFirstHediffOfDef(ApertureDef) as Hediff_Aperture;
            var record = new GuPawnRecord { Pawn = pawn };
            if (aperture != null)
            {
                record.State = AwakeningState.Awakened;
                record.LatentAptitude = aperture.Aptitude;
                record.ExtremePhysiqueId = aperture.ExtremePhysiqueId;
                record.LegacyMigrated = true;
            }
            else
            {
                record.LatentAptitude = LatentAptitudeRules.Generate();
            }
            byPawn.Add(pawn, record);
            records.Add(record);
            SyncMortalStatus(pawn, record);
            return record;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 600 != 0) return;
            foreach (Map map in Find.Maps)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn != null && !pawn.Dead)
                    {
                        GuPawnRecord record = GetOrCreate(pawn);
                        if (record != null) SyncMortalStatus(pawn, record);
                    }
        }


        // These Hediffs communicate known social/cultivation status, NOT hidden aptitude.
        // Idempotent: repeated discovery scans do not create extra Hediffs.
        internal void SyncMortalStatus(Pawn pawn, GuPawnRecord record)
        {
            if (pawn == null || pawn.health == null || record == null) return;
            HediffDef needed = record.State == AwakeningState.Unattempted ? MortalDef :
                record.State == AwakeningState.Failed ? FailedDef : null;
            SyncOne(MortalDef);
            SyncOne(FailedDef);
            void SyncOne(HediffDef def)
            {
                if (def == null) return;
                Hediff present = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (def == needed)
                {
                    if (present == null) pawn.health.AddHediff(HediffMaker.MakeHediff(def, pawn));
                }
                else if (present != null) pawn.health.RemoveHediff(present);
            }
        }

        public bool TryAwaken(Pawn pawn, out string result)
        {
            return GuAwakeningService.TryAwaken(this, pawn, out result);
        }

        internal Hediff_Aperture GetAperture(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || ApertureDef == null) return null;
            return pawn.health.hediffSet.GetFirstHediffOfDef(ApertureDef) as Hediff_Aperture;
        }

        internal bool AddAperture(Pawn pawn, int aptitude)
        {
            if (ApertureDef == null || GetAperture(pawn) != null) return false;
            var hediff = HediffMaker.MakeHediff(ApertureDef, pawn) as Hediff_Aperture;
            if (hediff == null) return false;
            hediff.Initialize(aptitude);
            pawn.health.AddHediff(hediff);
            return GetAperture(pawn) != null;
        }
    }
}
