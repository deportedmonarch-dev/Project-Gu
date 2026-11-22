using System;
using Verse;
using ProjectGu.Domain;

namespace ProjectGu
{
    /// <summary>
    /// Prototype 0.1.1: canon-aligned aptitude and bounded regeneration.
    /// The serialized identifiers intentionally match 0.1 for save compatibility.
    /// Extreme physiques are represented separately, but are not rolled or activated yet.
    /// </summary>
    public class Hediff_Aperture : Hediff
    {
        public int Aptitude { get; private set; } = 40;
        public int Rank { get; private set; } = 1;
        public float Essence { get; private set; } = 0f;
        public string ExtremePhysiqueId { get; private set; } = "";

        // Transitional durable aperture identity (RFC-0004 §15.5). Assigned once and never changed.
        private string apertureId;

        /// <summary>The durable aperture ID, or null until a coordinator operation assigns it. Reads never assign it.</summary>
        public string ApertureId => apertureId;

        /// <summary>Assigns the ID once. Called only by registry operations and migration.</summary>
        internal string EnsureApertureId() => apertureId ??= Guid.NewGuid().ToString("N");

        public bool HasExtremePhysique => !string.IsNullOrEmpty(ExtremePhysiqueId);

        public string AptitudeGrade
        {
            get
            {
                if (HasExtremePhysique) return "Extreme Physique";
                return AptitudeRules.Grade(Aptitude);
            }
        }

        // An aptitude percentage represents the sea's share of the aperture.
        // For this first prototype, 1 percentage point = 1 essence unit.
        public float MaxEssence => HasExtremePhysique ? 100f : Aptitude;

        // Percentage points of a hypothetical full sea restored per 2,500 ticks.
        // A=8 and C=4 reflect the novel's early examples. B/D are tuning assumptions.
        // Extreme physique recovery is deferred to its dedicated mechanic; fallback
        // uses the A-grade rate until physique-specific behavior is implemented.
        public float RegenerationPerHour
        {
            get
            {
                if (HasExtremePhysique) return 8f;
                return AptitudeRules.RegenerationPerHour(Aptitude);
            }
        }

        public void Initialize(int aptitude)
        {
            Aptitude = AptitudeRules.ClampOrdinary(aptitude);
            Rank = 1;
            ExtremePhysiqueId = "";
            Essence = MaxEssence * 0.5f;
        }

        public override void Tick()
        {
            base.Tick();
            if (pawn != null && pawn.IsHashIntervalTick(250))
            {
                // 10 updates per 2,500-tick in-game hour.
                Essence = Math.Min(MaxEssence, Essence + RegenerationPerHour / 10f);
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            Current.Game?.GetComponent<GuIndividualRegistry>()?.EnqueueApertureEvent(this, GuUnresolvedCause.ApertureMissing, "The aperture Hediff was removed.");
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit)
        {
            base.Notify_PawnDied(dinfo, culprit);
            Current.Game?.GetComponent<GuIndividualRegistry>()?.EnqueueApertureEvent(this, GuUnresolvedCause.HostDead, "The aperture host died.");
        }

        public override string TipStringExtra =>
            "Aptitude: " + AptitudeGrade + (HasExtremePhysique
                ? " (" + ExtremePhysiqueId + ")"
                : "-grade (" + Aptitude + "/100)") + "\n" +
            "Cultivation: Rank " + Rank + "\n" +
            "Primeval essence: " + Essence.ToString("0.0") + "/" + MaxEssence.ToString("0.0") + "\n" +
            "Natural regeneration: " + RegenerationPerHour.ToString("0.0") + " essence/hour\n" +
            "Prototype: Gu activation, awakening and extreme physique mechanics pending.";

        public override void ExposeData()
        {
            base.ExposeData();
            int aptitude = Aptitude;
            int rank = Rank;
            float essence = Essence;
            string physique = ExtremePhysiqueId;
            Scribe_Values.Look(ref aptitude, "guAptitude", 40);
            Scribe_Values.Look(ref rank, "guRank", 1);
            Scribe_Values.Look(ref essence, "guEssence", 0f);
            Scribe_Values.Look(ref physique, "guExtremePhysiqueId", "");
            Scribe_Values.Look(ref apertureId, "guApertureId");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Preserve previously saved aptitude and essence. Old 0.1 grades
                // are reclassified using the corrected canonical thresholds.
                Aptitude = Math.Max(20, Math.Min(100, aptitude));
                Rank = Math.Max(1, rank);
                ExtremePhysiqueId = physique ?? "";
                Essence = Math.Max(0f, Math.Min(MaxEssence, essence));
            }
        }
    }
}
