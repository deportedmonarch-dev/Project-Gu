using System;
using System.Runtime.CompilerServices;
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
    public partial class GuWorldComponent : GameComponent
    {
        // A failed first ceremony can retry the same unpublished aptitude in this game.
        private readonly ConditionalWeakTable<Pawn, GuCultivatorRecord> pendingDiscovery = new();
        private static HediffDef MortalDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_MortalUnawakened");
        private static HediffDef FailedDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_MortalNoAptitude");
        internal static HediffDef ApertureDef => DefDatabase<HediffDef>.GetNamedSilentFail("GU_Aperture");

        public GuWorldComponent(Game game) { this.game = game; }

        public GuCultivatorRecord GetOrCreate(Pawn pawn)
        {
            if (store == null || loadFailed || !GuCultivatorPlan.IsCandidate(pawn)) return null;
            if (store.ByPawn.TryGetValue(pawn, out var existing)) return existing;
            if (awakeningInProgress || HasDiscoveryConflict(pawn)) return null;
            var record = pendingDiscovery.GetValue(pawn, PrepareDiscovery);
            while (store.IdentityClaims.Contains(record.Id)) record.Id = Guid.NewGuid().ToString("N");
            ReserveDiscoveryCapacity();
            PublishDiscovery(record);
            SyncMortalStatus(pawn, record);
            return record;
        }

        private GuCultivatorRecord PrepareDiscovery(Pawn pawn)
        {
            string token = pawn.GetUniqueLoadID();
            var record = GuCultivatorPlan.NewRecord(token, $"native:{token}", pawn);
            // The existing discovery behavior still reads an aperture before rolling aptitude.
            var aperture = pawn.health.hediffSet.GetFirstHediffOfDef(ApertureDef) as Hediff_Aperture;
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
            return record;
        }

        private bool HasDiscoveryConflict(Pawn pawn)
        {
            string token = pawn.GetUniqueLoadID();
            return store.PawnClaims.Contains(token) || store.CreationClaims.Contains($"native:{token}");
        }

        private void PublishDiscovery(GuCultivatorRecord record)
        {
            store.ByPawn.Add(record.Pawn, record);
            store.ById.Add(record.Id, record);
            store.IdentityClaims.Add(record.Id);
            store.CreationClaims.Add(record.CreationKey);
            store.PawnClaims.Add(record.PawnLoadId);
            store.Records.Add(record);
            pendingDiscovery.Remove(record.Pawn);
        }

        private void ReserveDiscoveryCapacity()
        {
            store.ByPawn.EnsureCapacity(store.ByPawn.Count + 1);
            store.ById.EnsureCapacity(store.ById.Count + 1);
            store.IdentityClaims.EnsureCapacity(store.IdentityClaims.Count + 1);
            store.CreationClaims.EnsureCapacity(store.CreationClaims.Count + 1);
            store.PawnClaims.EnsureCapacity(store.PawnClaims.Count + 1);
            store.Records.EnsureCapacity(store.Records.Count + 1);
        }

        public override void GameComponentTick()
        {
            if (!CanSaveCultivators || Find.TickManager.TicksGame % 600 != 0) return;
            foreach (Map map in Find.Maps)
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    if (pawn != null && !pawn.Dead)
                    {
                        GuCultivatorRecord record = GetOrCreate(pawn);
                        if (record != null) SyncMortalStatus(pawn, record);
                    }
        }


        // These Hediffs communicate known social/cultivation status, NOT hidden aptitude.
        // Idempotent: repeated discovery scans do not create extra Hediffs.
        internal void SyncMortalStatus(Pawn pawn, GuCultivatorRecord record)
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

        internal bool TryBeginAwakening()
        {
            if (!CanSaveCultivators) return false;
            awakeningInProgress = true;
            return true;
        }

        internal void EndAwakening() => awakeningInProgress = false;

        internal GuCultivatorRecord PrepareAwakeningRecord(Pawn pawn)
        {
            if (!GuCultivatorPlan.IsCandidate(pawn)) return null;
            if (store.ByPawn.TryGetValue(pawn, out var record)) return record;
            return HasDiscoveryConflict(pawn) ? null : pendingDiscovery.GetValue(pawn, PrepareDiscovery);
        }

        internal bool PrepareAwakeningPublication(GuCultivatorRecord record, Hediff_Aperture aperture)
        {
            if (!awakeningInProgress || loadFailed || record.State != AwakeningState.Unattempted
                || record.StateRevision == int.MaxValue || !GuCultivatorPlan.IsCandidate(record.Pawn)
                || record.Pawn.Dead || record.Pawn.GetUniqueLoadID() != record.PawnLoadId
                || (aperture != null && (aperture.pawn != record.Pawn
                    || !record.Pawn.health.hediffSet.hediffs.Contains(aperture)
                    || !GuCultivatorRecord.IsId(aperture.ApertureId) || store.ApertureClaims.Contains(aperture.ApertureId))))
                return false;
            if (store.ByPawn.TryGetValue(record.Pawn, out var current) ? current != record
                : HasDiscoveryConflict(record.Pawn) || store.IdentityClaims.Contains(record.Id))
                return false;
            // Reserve storage before the irreversible debit. The publish path has no native callbacks.
            if (current == null) ReserveDiscoveryCapacity();
            if (aperture != null)
            {
                store.ByAperture.EnsureCapacity(store.ByAperture.Count + 1);
                store.ApertureClaims.EnsureCapacity(store.ApertureClaims.Count + 1);
                record.ApertureIds.EnsureCapacity(record.ApertureIds.Count + 1);
            }
            return true;
        }

        internal void PublishAwakening(GuCultivatorRecord record, Hediff_Aperture aperture)
        {
            if (!store.ByPawn.ContainsKey(record.Pawn)) PublishDiscovery(record);
            if (aperture != null)
            {
                store.ApertureClaims.Add(aperture.ApertureId);
                store.ByAperture.Add(aperture.ApertureId, record);
                record.ApertureIds.Add(aperture.ApertureId);
            }
            record.State = aperture == null ? AwakeningState.Failed : AwakeningState.Awakened;
            record.StateRevision++;
        }

        internal void FailAwakeningRecovery(Exception error) => FailLoad($"Awakening requires recovery. {error}");

        internal Hediff_Aperture CreateAperture(Pawn pawn, int aptitude)
        {
            HediffDef def = ApertureDef;
            if (def == null || pawn.health.hediffSet.GetFirstHediffOfDef(def) != null) return null;
            var aperture = HediffMaker.MakeHediff(def, pawn) as Hediff_Aperture;
            aperture?.Initialize(aptitude);
            if (aperture != null)
            {
                string id;
                do { id = Guid.NewGuid().ToString("N"); } while (store.ApertureClaims.Contains(id));
                aperture.ReplaceCultivatorApertureId(null, id);
            }
            return aperture;
        }
    }
}
