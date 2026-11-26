using System;
using System.Runtime.CompilerServices;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>The cohort life stage a materialization debits. Only mature units leave a population as individuals.</summary>
    public enum GuCohortStage { Mature = 1, Juvenile = 2 }

    /// <summary>Result of one source materialization attempt (RFC-0007, Alpha 6 Slice 0).</summary>
    internal enum GuSourceOutcome
    {
        /// <summary>Nothing changed: the gate, request, authorization, source state or placement refused it.</summary>
        Refused = 0,
        /// <summary>The event key already has a receipt whose source, trigger, species, rank, stage or asset scope differs.</summary>
        Conflict = 1,
        /// <summary>The event already committed. The original individual is returned and no count changed.</summary>
        Replayed = 2,
        /// <summary>One mature unit became one canonical living individual with one published projection.</summary>
        Committed = 3,
        /// <summary>The attempt failed and every managed preimage was restored. The event may be retried.</summary>
        RolledBack = 4,
        /// <summary>Cleanup of the unauthorized native shell could not be proven. The event is blocked and nothing was debited.</summary>
        Quarantined = 5,
        /// <summary>The debit stands and one counted canonical individual is unresolved, with its receipt. Replays return it.</summary>
        Unresolved = 6
    }

    /// <summary>
    /// Stable identity of one population source plus the live credential of its native instance. Requests carry both; receipts
    /// keep the stable identity. The credential guards against a stale or replaced native object and is reacquired after load.
    /// </summary>
    internal readonly record struct GuSourceRequest(string EventKey, string OwnerKind, string SourceId, string Credential,
        string SpeciesDefName, int Rank, GuCohortStage Stage, string Trigger, string AssetScope, long ExpectedRevision);

    /// <summary>
    /// An independently authorized supplier of anonymous cohort units. The registry owns the transaction: identity, receipts,
    /// staging, debit ordering, publication and recovery. A source answers only for its own state and policy. Every member is
    /// read-only; the registry debits the authoritative cohort instance returned by <see cref="CohortFor"/>.
    /// </summary>
    internal interface IGuPopulationSource
    {
        /// <summary>Stable owner class, saved in receipts, for example <c>spring</c>.</summary>
        string OwnerKind { get; }

        /// <summary>Stable source identity, saved in receipts and as the record's origin. Never a transient native ID.</summary>
        string SourceId { get; }

        /// <summary>Credential of the live native instance that currently owns the source, or null when it is unbound.</summary>
        string CurrentCredential { get; }

        /// <summary>True when exactly one live instance claims <see cref="SourceId"/>. Ambiguous ownership blocks mutation.</summary>
        bool HasUniqueIdentity { get; }

        /// <summary>Developer and fixture triggers require Development Mode; player triggers use the ordinary custody gate.</summary>
        bool RequiresDevelopmentMode(string trigger);

        /// <summary>The authoritative cohort instance for the species, or null. Must not change native state.</summary>
        GuPopulationRecord CohortFor(string speciesDefName);

        /// <summary>Null when this source may issue the species at this rank, stage, trigger and asset scope; otherwise why not.</summary>
        string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, string assetScope);

        /// <summary>Source-specific placement policy. The registry separately requires an open, unreserved, live cell.</summary>
        bool PlacementReady(Map map, IntVec3 cell, string trigger);

        /// <summary>Player-facing explanation when placement is refused, in this source's own terms.</summary>
        string PlacementRefusal { get; }
    }

    /// <summary>Exact cohort counts and revision captured before a debit, for restoration inside the callback-free window.</summary>
    internal readonly record struct GuCohortPreimage(int Mature, int Juvenile, long Revision);

    /// <summary>Named checkpoints in the source transaction. The probe is inert in play and lets native fixtures inject faults.</summary>
    internal enum GuTransactionPoint
    {
        BeforeStaging = 1,
        SourceRevalidation = 2,
        AfterStagingBeforeDebit = 3,
        AfterDebit = 4,
        BeforePublication = 5,
        RestorePreimage = 6,
        Cleanup = 7
    }

    internal static class GuTransactionProbe
    {
        /// <summary>No production behavior. Verification patches this method to throw or re-enter at a named point.</summary>
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void Reach(GuTransactionPoint point, string eventKey) { }
    }

    /// <summary>Triggers the spring adapter authorizes. Other sources define their own.</summary>
    internal static class GuSourceTriggers
    {
        internal const string SpringSearch = "spring-search";
        internal const string SpringLure = "spring-lure";
        internal const string Development = "development";
    }

    /// <summary>
    /// Durable creation receipt for a cohort materialization (RFC-0007 conservation ledger). Stored on the canonical record
    /// and retained through compaction, so a committed event can never be reused. Additive: records created before Alpha 6
    /// have none and keep their original spring validation rules.
    /// </summary>
    public sealed class GuCreationEvidence : IExposable
    {
        internal const int CurrentVersion = 1;

        private int version;
        private string ownerKind;
        private string sourceId;
        private string credential;
        private string trigger;
        private string assetScope;
        private GuCohortStage stage = GuCohortStage.Mature;
        private int rank;
        private long revisionBefore = -1;
        private long revisionAfter = -1;

        public int Version => version;
        public string OwnerKind => ownerKind;
        public string SourceId => sourceId;
        public string Credential => credential;
        public string Trigger => trigger;
        public string AssetScope => assetScope;
        public GuCohortStage Stage => stage;
        public int Rank => rank;
        public long RevisionBefore => revisionBefore;
        public long RevisionAfter => revisionAfter;

        public GuCreationEvidence() { }

        internal GuCreationEvidence(GuSourceRequest request)
        {
            version = CurrentVersion;
            ownerKind = request.OwnerKind;
            sourceId = request.SourceId;
            credential = request.Credential;
            trigger = request.Trigger;
            assetScope = NormalizeScope(request.AssetScope);
            stage = request.Stage;
            rank = request.Rank;
            revisionBefore = request.ExpectedRevision;
        }

        internal static string NormalizeScope(string scope) => string.IsNullOrWhiteSpace(scope) ? null : scope;

        internal void MarkDebited(long after) => revisionAfter = after;

        internal void ClearDebit() => revisionAfter = -1;

        /// <summary>Null when a reuse of this receipt's event key is the same payload; otherwise the conflicting facet.</summary>
        internal string PayloadConflict(GuSourceRequest request, string speciesDefName)
        {
            if (!string.Equals(ownerKind, request.OwnerKind, StringComparison.Ordinal)
                || !string.Equals(sourceId, request.SourceId, StringComparison.Ordinal)) return "source";
            if (!string.Equals(speciesDefName, request.SpeciesDefName, StringComparison.Ordinal)) return "species";
            if (!string.Equals(trigger, request.Trigger, StringComparison.Ordinal)) return "trigger";
            if (!string.Equals(assetScope, NormalizeScope(request.AssetScope), StringComparison.Ordinal)) return "asset scope";
            if (rank != request.Rank || stage != request.Stage) return "rank or stage";
            return null;
        }

        /// <summary>Shape and agreement with the record that carries it. A malformed receipt quarantines its record on load.</summary>
        internal string ShapeError(GuRecord record)
        {
            if (version != CurrentVersion) return "Unsupported creation receipt version.";
            if (record.Origin != GuRecord.CohortOrigin || string.IsNullOrWhiteSpace(ownerKind) || string.IsNullOrWhiteSpace(trigger)
                || !string.Equals(sourceId, record.OriginId, StringComparison.Ordinal)
                || !string.Equals(credential, record.OriginNativeId, StringComparison.Ordinal)
                || rank < 1 || stage != GuCohortStage.Mature || revisionBefore < 0)
                return "The creation receipt disagrees with the record's source, species stage or revision.";
            if (record.CohortDebitCommitted ? revisionBefore == long.MaxValue || revisionAfter != revisionBefore + 1 : revisionAfter != -1)
                return "The creation receipt's debit revision contradicts the record's debit evidence.";
            return null;
        }

        internal string Describe() => $"{ownerKind}:{sourceId} via {trigger}" + (assetScope == null ? "" : $" ({assetScope})")
            + $", {stage} rank {rank}, revision {revisionBefore} -> {(revisionAfter < 0 ? "uncommitted" : revisionAfter.ToString())}";

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version");
            Scribe_Values.Look(ref ownerKind, "ownerKind");
            Scribe_Values.Look(ref sourceId, "sourceId");
            Scribe_Values.Look(ref credential, "credential");
            Scribe_Values.Look(ref trigger, "trigger");
            Scribe_Values.Look(ref assetScope, "assetScope");
            Scribe_Values.Look(ref stage, "stage", GuCohortStage.Mature);
            Scribe_Values.Look(ref rank, "rank");
            Scribe_Values.Look(ref revisionBefore, "revisionBefore", -1L);
            Scribe_Values.Look(ref revisionAfter, "revisionAfter", -1L);
        }
    }
}
