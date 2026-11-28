using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Domain
{
    public sealed partial class GuRecord
    {
        private GuEncounterRetention encounter;
        private GuPopulationReturnReceipt populationReturn;

        internal GuPopulationReturnReceipt PopulationReturn => populationReturn;
        internal bool HasClearedPopulationReturnPlacement => placement == default && manifestation == default && containerKind == default
            && projectionId == null && containerId == null && anchorHint == null && legacyApertureLoadId == null;

        internal void NoteEncounterInteraction(int tick, bool historical = false)
        {
            if (IsLiving) (encounter ??= new()).Observe(tick, historical);
        }

        internal bool RenewEncounterReference(string key, int now, int expiresAt) => IsLiving &&
            (encounter ??= new()).Renew(key, now, expiresAt);

        internal void ReleaseEncounterReference(string key) => encounter?.Release(key);

        internal bool EncounterProtected(int now, int quietTicks, int historyTicks)
        {
            var species = Species;
            if (species == null || UniquenessKey != null || Rank >= 6 || Rank >= species.individualPreservationMinRank
                || species.wildIdentityRetentionDays < 0 || Refinement != null || RefinementProcess != null)
                return true;
            int quiet = Math.Max(quietTicks, species.wildIdentityRetentionDays * GuSurfaceEcology.TicksPerDay);
            return now - (long)Math.Max(CreatedTick, encounter?.LastInteraction ?? -1) < quiet
                || encounter?.Protected(now, historyTicks) == true;
        }

        internal bool ReturnBlockedForBinding => encounter?.BlockedRevision == custodyRevision;
        internal void BlockReturnForBinding() => (encounter ??= new()).BlockedRevision = custodyRevision;
        internal bool ReturnRetryDue(int now) => encounter == null || now >= encounter.RetryAfter;
        internal void DeferReturn(int now) => (encounter ??= new()).RetryAfter = (int)Math.Min(int.MaxValue, now + 2500L);

        internal void CommitPopulationReturn(GuPopulationReturnReceipt receipt)
        {
            populationReturn = receipt;
            existence = GuExistence.Aggregated;
            endedTick = receipt.Tick;
            endEventKey = receipt.EventKey(id);
            placement = default;
            manifestation = default;
            containerKind = default;
            projectionId = containerId = anchorHint = legacyApertureLoadId = null;
            SurfaceResidencyEnded = true;
            custodyRevision++;
            stateRevision++;
            encounter = null;
        }
    }

    /// <summary>Saved renewal times and a bounded set of exact references owned by their issuing systems.</summary>
    internal sealed class GuEncounterRetention : IExposable
    {
        private int lastInteraction = -1;
        private int lastHistoricalInteraction = -1;
        private int blockedRevision = -1;
        private int retryAfter = -1;
        private Dictionary<string, int> references;
        private const int ReferenceLimit = 8;

        public GuEncounterRetention() { }

        internal int LastInteraction => lastInteraction;
        internal int BlockedRevision { get => blockedRevision; set => blockedRevision = value; }
        internal int RetryAfter { get => retryAfter; set => retryAfter = value; }

        internal void Observe(int tick, bool historical)
        {
            lastInteraction = Math.Max(lastInteraction, tick);
            if (historical) lastHistoricalInteraction = Math.Max(lastHistoricalInteraction, tick);
        }

        internal bool Renew(string key, int now, int expiresAt)
        {
            if (string.IsNullOrWhiteSpace(key) || expiresAt <= now) return false;
            Prune(now);
            references ??= new(StringComparer.Ordinal);
            if (references.TryGetValue(key, out int previous)) expiresAt = Math.Max(previous, expiresAt);
            else if (references.Count >= ReferenceLimit) return false;
            references[key] = expiresAt;
            Observe(now, historical: false);
            return true;
        }

        internal void Release(string key) => references?.Remove(key);

        internal bool Protected(int now, int historyTicks)
        {
            Prune(now);
            return references?.Count > 0 || lastHistoricalInteraction >= 0 && now - (long)lastHistoricalInteraction < historyTicks;
        }

        private void Prune(int now)
        {
            if (references == null) return;
            // .NET permits removing the current dictionary entry during enumeration.
            foreach (var entry in references)
                if (entry.Value <= now) references.Remove(entry.Key);
            if (references.Count == 0) references = null;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) Prune(Find.TickManager.TicksGame);
            Scribe_Values.Look(ref lastInteraction, "lastInteraction", -1);
            Scribe_Values.Look(ref lastHistoricalInteraction, "lastHistoricalInteraction", -1);
            Scribe_Values.Look(ref blockedRevision, "blockedRevision", -1);
            Scribe_Values.Look(ref retryAfter, "retryAfter", -1);
            Scribe_Collections.Look(ref references, "references", LookMode.Value, LookMode.Value);
        }
    }

    internal sealed class GuPopulationReturnReceipt : IExposable
    {
        private string site;
        private string mapParent;
        private int custodyBefore;
        private long cohortAfter;
        private int tick;

        public GuPopulationReturnReceipt() { }

        internal GuPopulationReturnReceipt(string site, string mapParent, int custodyBefore, long cohortAfter, int tick)
        {
            this.site = site;
            this.mapParent = mapParent;
            this.custodyBefore = custodyBefore;
            this.cohortAfter = cohortAfter;
            this.tick = tick;
        }

        internal string Site => site;
        internal int CustodyBefore => custodyBefore;
        internal long CohortAfter => cohortAfter;
        internal int Tick => tick;
        internal string EventKey(string id) => FormattableString.Invariant($"surface-return:{id}:{custodyBefore}");
        internal string CreditKey(string id) => FormattableString.Invariant($"{EventKey(id)}\n{site}\n{mapParent}\n{tick}");

        internal bool Matches(GuRecord record) => record.Existence == GuExistence.Aggregated && record.Rank == 1
            && !string.IsNullOrWhiteSpace(site) && site == record.SurfaceResidenceEvidence && !string.IsNullOrWhiteSpace(mapParent)
            && custodyBefore > 0 && custodyBefore < int.MaxValue && record.CustodyRevision == custodyBefore + 1
            && cohortAfter > 0 && tick >= record.CreatedTick && tick == record.EndedTick && record.EndEventKey == EventKey(record.Id)
            && record.TerminalOutcome == GuTerminalOutcome.None && record.TerminalEvidence == GuTerminalEvidence.None && !record.IsUnresolved
            && record.Condition == GuCondition.Active && record.Refinement == null && record.RefinementProcess == null
            && record.SurfaceResidencyEnded && record.HasClearedPopulationReturnPlacement;

        internal string Describe() => $"{site} ({mapParent}) | custody revision {custodyBefore} | cohort revision {cohortAfter} | +1 adult";

        public void ExposeData()
        {
            Scribe_Values.Look(ref site, "site");
            Scribe_Values.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref custodyBefore, "custodyBefore");
            Scribe_Values.Look(ref cohortAfter, "cohortAfter");
            Scribe_Values.Look(ref tick, "tick");
        }
    }
}
