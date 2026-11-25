using System;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Durable, non-authoritative work state for one will-enforcement attempt. It reserves no custody and grants no
    /// allegiance. Costs already paid are never refunded; progress may resume only under the same cultivator identity.
    /// </summary>
    public sealed class GuRefinementProcess : IExposable
    {
        private string processId;
        private string cultivatorId;
        private string previousOwnerId;
        private int totalWorkTicks;
        private int completedWorkTicks;
        private float totalEssenceCost;
        private float essenceSpent;
        private int lastAdvancedTick = -1;

        public string ProcessId => processId;
        public string CultivatorId => cultivatorId;
        public string PreviousOwnerId => previousOwnerId;
        public int TotalWorkTicks => totalWorkTicks;
        public int CompletedWorkTicks => completedWorkTicks;
        public int RemainingWorkTicks => Math.Max(0, totalWorkTicks - completedWorkTicks);
        public float TotalEssenceCost => totalEssenceCost;
        public float EssenceSpent => essenceSpent;
        public float RemainingEssence => Math.Max(0f, totalEssenceCost - essenceSpent);

        public GuRefinementProcess() { }

        internal GuRefinementProcess(string processId, string cultivatorId, string previousOwnerId,
            int totalWorkTicks, float totalEssenceCost, int startedTick)
        {
            this.processId = processId;
            this.cultivatorId = cultivatorId;
            this.previousOwnerId = previousOwnerId;
            this.totalWorkTicks = totalWorkTicks;
            this.totalEssenceCost = totalEssenceCost;
            lastAdvancedTick = startedTick;
        }

        internal bool IsValid => Guid.TryParseExact(processId, "N", out _)
            && Guid.TryParseExact(cultivatorId, "N", out _)
            && (string.IsNullOrEmpty(previousOwnerId) || Guid.TryParseExact(previousOwnerId, "N", out _))
            && totalWorkTicks is >= 1 and <= 600_000 && completedWorkTicks >= 0 && completedWorkTicks <= totalWorkTicks
            && float.IsFinite(totalEssenceCost) && totalEssenceCost > 0f
            && float.IsFinite(essenceSpent) && essenceSpent >= 0f && essenceSpent <= totalEssenceCost
            && lastAdvancedTick >= 0;

        internal bool TryAdvance(int tick, Hediff_Aperture aperture)
        {
            if (!IsValid || aperture == null) return false;
            if (completedWorkTicks >= totalWorkTicks || tick <= lastAdvancedTick) return true;
            // Price the cumulative work from the saved debit, avoiding error from repeatedly adding a rounded charge.
            float due = Math.Max(essenceSpent, (float)((double)totalEssenceCost * (completedWorkTicks + 1) / totalWorkTicks));
            if (!aperture.TrySpendAccruedEssence(essenceSpent, due)) return false;
            essenceSpent = due;
            completedWorkTicks++;
            lastAdvancedTick = tick;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref processId, "processId");
            Scribe_Values.Look(ref cultivatorId, "cultivatorId");
            Scribe_Values.Look(ref previousOwnerId, "previousOwnerId");
            Scribe_Values.Look(ref totalWorkTicks, "totalWorkTicks");
            Scribe_Values.Look(ref completedWorkTicks, "completedWorkTicks");
            Scribe_Values.Look(ref totalEssenceCost, "totalEssenceCost");
            Scribe_Values.Look(ref essenceSpent, "essenceSpent");
            Scribe_Values.Look(ref lastAdvancedTick, "lastAdvancedTick", -1);
        }
    }
}
