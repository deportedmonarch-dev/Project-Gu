using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Domain
{
    public enum GuExistence { Living = 1, Terminal = 2, Transformed = 3, Aggregated = 4 }

    public enum GuCondition { Active = 1, Dormant = 2 }

    public enum GuPlacementKind { Manifested = 1, Contained = 2 }

    public enum GuManifestationKind { Pawn = 1, Thing = 2 }

    public enum GuContainerKind { MortalAperture = 1 }

    /// <summary>Why an otherwise living record cannot currently be acted on. None means resolved.</summary>
    public enum GuUnresolvedCause
    {
        None = 0,
        SpeciesUnavailable = 1,
        ProjectionMissing = 2,
        ProjectionEnded = 3,
        HostDead = 4,
        HostUnavailable = 5,
        ApertureMissing = 6
    }

    /// <summary>A demonstrated native event. Phase 1 records it as evidence and never makes the record terminal.</summary>
    public enum GuTerminalEvidence { None = 0, Destroyed = 1, Killed = 2, MapRemoved = 3, Discarded = 4 }

    /// <summary>Optional, species-specific persistent state. Created on demand by a capability.</summary>
    public abstract class GuStateComponent : IExposable
    {
        public virtual int Version => 1;
        public abstract void ExposeData();
    }

    /// <summary>Refinement allegiance, independent of custody and biology. Phase 1 stores no bonds.</summary>
    public sealed class GuRefinement : IExposable
    {
        internal string cultivatorId;
        internal int sinceTick = -1;

        public string CultivatorId => cultivatorId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cultivatorId, "cultivatorId");
            Scribe_Values.Look(ref sinceTick, "sinceTick", -1);
        }
    }

    /// <summary>
    /// Schema V2 canonical individual (RFC-0004). Identity, existence, condition, placement, refinement,
    /// species state and integrity are independent facets. Physical location of a manifestation is observed
    /// from the native graph and is not stored here.
    /// </summary>
    public sealed class GuRecord : IExposable
    {
        internal const int CurrentVersion = 2;
        internal const string CohortOrigin = "cohort";
        internal const string MigrationOrigin = "migration";

        // Identity and provenance
        private int version;
        private string id;
        private string speciesDefName;
        private int rank;
        private string origin;
        private string originId;
        private int createdTick = -1;
        private string creationEventKey;
        private string originNativeId;
        private bool cohortDebitCommitted;
        private string uniquenessKey;

        // Facets
        private GuExistence existence = GuExistence.Living;
        private GuCondition condition = GuCondition.Active;
        private GuPlacementKind placement = GuPlacementKind.Manifested;
        private GuManifestationKind manifestation = GuManifestationKind.Pawn;
        private string projectionId;
        private GuContainerKind containerKind = GuContainerKind.MortalAperture;
        private string containerId;
        private string anchorHint;
        private string legacyApertureLoadId;
        private GuRefinement refinement;
        private List<GuStateComponent> state = new();

        // Unresolved overlay. The placement facet keeps the last authorized placement for recovery.
        private GuUnresolvedCause unresolved;
        private GuTerminalEvidence terminalEvidence;
        private int unresolvedSinceTick = -1;
        private string unresolvedDetail;

        // Evidential integrity
        private string quarantineReason;
        private string legacyQuarantineReason;

        // Revisions
        private int custodyRevision;
        private int stateRevision;

        public int Version => version;
        public string Id => id;
        public string SpeciesDefName => speciesDefName;
        public GuSpeciesDef Species => string.IsNullOrWhiteSpace(speciesDefName)
            ? null : DefDatabase<GuSpeciesDef>.GetNamedSilentFail(speciesDefName);
        public int Rank => rank;
        public string Origin => origin;
        public string OriginId => originId;
        public int CreatedTick => createdTick;
        public string CreationEventKey => creationEventKey;
        public string OriginNativeId => originNativeId;
        public bool CohortDebitCommitted => cohortDebitCommitted;
        public string UniquenessKey => uniquenessKey;
        public GuExistence Existence => existence;
        public GuCondition Condition => condition;
        public GuPlacementKind Placement => placement;
        public GuManifestationKind Manifestation => manifestation;
        public string ProjectionId => placement == GuPlacementKind.Manifested ? projectionId : null;
        public GuContainerKind ContainerKind => containerKind;
        public string ContainerId => placement == GuPlacementKind.Contained ? containerId : null;
        public string AnchorHint => placement == GuPlacementKind.Contained ? anchorHint : null;
        public string LegacyApertureLoadId => legacyApertureLoadId;
        public GuRefinement Refinement => refinement;
        public IReadOnlyList<GuStateComponent> State => state;
        public GuUnresolvedCause Unresolved => unresolved;
        public bool IsUnresolved => unresolved != GuUnresolvedCause.None;
        public GuTerminalEvidence TerminalEvidence => terminalEvidence;
        public int UnresolvedSinceTick => unresolvedSinceTick;
        public string UnresolvedDetail => unresolvedDetail;
        public string QuarantineReason => quarantineReason;
        public bool IsQuarantined => !string.IsNullOrEmpty(quarantineReason);
        public string LegacyQuarantineReason => legacyQuarantineReason;
        public int CustodyRevision => custodyRevision;
        public int StateRevision => stateRevision;

        /// <summary>Living, unquarantined and resolved: eligible for coordinator operations.</summary>
        public bool IsActionable => existence == GuExistence.Living && !IsQuarantined && !IsUnresolved;

        public GuRecord() { }

        internal static GuRecord CreateManifested(string id, GuSpeciesDef species, string origin, string originId, int tick,
            GuCondition condition, GuManifestationKind kind, string projectionId) => new()
            {
                version = CurrentVersion,
                id = id,
                speciesDefName = species.defName,
                rank = species.minRank,
                origin = origin,
                originId = originId,
                createdTick = tick,
                condition = condition,
                placement = GuPlacementKind.Manifested,
                manifestation = kind,
                projectionId = projectionId,
                custodyRevision = 1,
                stateRevision = 1
            };

        internal static GuRecord PrepareFromCohort(string id, GuSpeciesDef species, GuCohortMaterializationRequest request,
            int tick, string pawnId)
        {
            var record = CreateManifested(id, species, CohortOrigin, request.HabitatId, tick, GuCondition.Active,
                GuManifestationKind.Pawn, pawnId);
            record.creationEventKey = request.EventKey;
            record.originNativeId = request.SourceNativeId;
            return record;
        }

        /// <summary>Field-by-field translation of a Schema V1 record (RFC-0004 §12).</summary>
        internal static GuRecord FromV1(GuIndividualV1 legacy, string apertureId, string legacyQuarantine, string retainedQuarantine)
        {
            var record = new GuRecord
            {
                version = CurrentVersion,
                id = legacy.Id,
                speciesDefName = legacy.SpeciesDefName,
                rank = legacy.Rank,
                origin = legacy.Origin,
                originId = legacy.OriginId,
                createdTick = legacy.CreatedTick,
                creationEventKey = legacy.CreationEventKey,
                originNativeId = legacy.OriginNativeId,
                cohortDebitCommitted = legacy.CohortDebitCommitted,
                condition = legacy.BiologicalState == GuV1BiologicalState.Dormant ? GuCondition.Dormant : GuCondition.Active,
                custodyRevision = legacy.Revision,
                stateRevision = 1,
                legacyQuarantineReason = legacyQuarantine,
                quarantineReason = retainedQuarantine
            };
            switch (legacy.CustodyKind)
            {
                case GuV1CustodyKind.Aperture:
                    record.placement = GuPlacementKind.Contained;
                    record.containerKind = GuContainerKind.MortalAperture;
                    record.containerId = apertureId;
                    record.anchorHint = legacy.HolderPawnId;
                    record.legacyApertureLoadId = legacy.NativeOwnerId;
                    break;
                case GuV1CustodyKind.WildPawn:
                    record.placement = GuPlacementKind.Manifested;
                    record.manifestation = GuManifestationKind.Pawn;
                    record.projectionId = legacy.NativeOwnerId;
                    break;
                default:
                    record.placement = GuPlacementKind.Manifested;
                    record.manifestation = GuManifestationKind.Thing;
                    record.projectionId = legacy.NativeOwnerId;
                    break;
            }
            return record;
        }

        internal void MarkCohortDebitCommitted() => cohortDebitCommitted = true;

        internal void Quarantine(string reason)
        {
            if (IsQuarantined) return;
            quarantineReason = reason;
            Log.Warning($"[Project GU] Quarantined Gu {id ?? "(missing ID)"}. {reason}");
        }

        internal void MarkUnresolved(GuUnresolvedCause cause, GuTerminalEvidence evidence, string detail, int tick)
        {
            // A demonstrated terminal event is never overwritten by a weaker, absence-of-evidence cause.
            if (IsUnresolved && terminalEvidence != GuTerminalEvidence.None && evidence == GuTerminalEvidence.None) return;
            if (unresolved == cause && terminalEvidence == evidence) return;
            unresolved = cause;
            terminalEvidence = evidence;
            unresolvedDetail = detail;
            if (unresolvedSinceTick < 0) unresolvedSinceTick = tick;
            Log.Message($"[Project GU] Gu {id} is unresolved: {cause}"
                + (evidence == GuTerminalEvidence.None ? "" : $" (evidence: {evidence})") + $". {detail}");
        }

        internal void ClearUnresolved()
        {
            unresolved = GuUnresolvedCause.None;
            terminalEvidence = GuTerminalEvidence.None;
            unresolvedSinceTick = -1;
            unresolvedDetail = null;
        }

        internal void ChangeCondition(GuCondition next)
        {
            if (condition == next) return;
            condition = next;
            stateRevision++;
        }

        internal void CommitManifestation(GuManifestationKind kind, string nativeId)
        {
            placement = GuPlacementKind.Manifested;
            manifestation = kind;
            projectionId = nativeId;
            containerId = null;
            anchorHint = null;
            custodyRevision++;
        }

        internal void CommitContainment(GuContainerKind kind, string container, string anchor)
        {
            placement = GuPlacementKind.Contained;
            containerKind = kind;
            containerId = container;
            anchorHint = anchor;
            projectionId = null;
            legacyApertureLoadId = null;
            custodyRevision++;
        }

        /// <summary>Records a migrated aperture identity found after migration. Does not change custody.</summary>
        internal void AdoptMigratedAperture(string apertureId)
        {
            containerId = apertureId;
            legacyApertureLoadId = null;
        }

        internal void UpdateAnchorHint(string anchor) => anchorHint = anchor;

        internal string DebugDescription => $"Gu {id ?? "(missing ID)"} | {speciesDefName} rank {rank} | custody revision {custodyRevision} | state revision {stateRevision}"
            + $"\n{existence} | {condition} | " + (placement == GuPlacementKind.Manifested
                ? $"manifested as {manifestation} ({projectionId})"
                : $"contained in {containerKind} {containerId ?? "(unassigned)"} (anchor {anchorHint})")
            + (creationEventKey == null ? "" : $"\nOrigin habitat: {originId} ({originNativeId}) | Event: {creationEventKey} | Adult debited: {cohortDebitCommitted}")
            + (IsUnresolved ? $"\nUnresolved: {unresolved}" + (terminalEvidence == GuTerminalEvidence.None ? "" : $", evidence {terminalEvidence}") + $" since {unresolvedSinceTick}. {unresolvedDetail}" : "")
            + (IsQuarantined ? $"\nQuarantined: {quarantineReason}" : "")
            + (legacyQuarantineReason == null ? "" : $"\nV1 quarantine before migration: {legacyQuarantineReason}");

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version");
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref speciesDefName, "speciesDefName");
            Scribe_Values.Look(ref rank, "rank");
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Values.Look(ref originId, "originId");
            Scribe_Values.Look(ref createdTick, "createdTick", -1);
            Scribe_Values.Look(ref creationEventKey, "creationEventKey");
            Scribe_Values.Look(ref originNativeId, "originNativeId");
            Scribe_Values.Look(ref cohortDebitCommitted, "cohortDebitCommitted");
            Scribe_Values.Look(ref uniquenessKey, "uniquenessKey");
            Scribe_Values.Look(ref existence, "existence", GuExistence.Living);
            Scribe_Values.Look(ref condition, "condition", GuCondition.Active);
            Scribe_Values.Look(ref placement, "placement", GuPlacementKind.Manifested);
            Scribe_Values.Look(ref manifestation, "manifestation", GuManifestationKind.Pawn);
            Scribe_Values.Look(ref projectionId, "projectionId");
            Scribe_Values.Look(ref containerKind, "containerKind", GuContainerKind.MortalAperture);
            Scribe_Values.Look(ref containerId, "containerId");
            Scribe_Values.Look(ref anchorHint, "anchorHint");
            Scribe_Values.Look(ref legacyApertureLoadId, "legacyApertureLoadId");
            Scribe_Deep.Look(ref refinement, "refinement");
            Scribe_Collections.Look(ref state, "state", LookMode.Deep);
            Scribe_Values.Look(ref unresolved, "unresolved", GuUnresolvedCause.None);
            Scribe_Values.Look(ref terminalEvidence, "terminalEvidence", GuTerminalEvidence.None);
            Scribe_Values.Look(ref unresolvedSinceTick, "unresolvedSinceTick", -1);
            Scribe_Values.Look(ref unresolvedDetail, "unresolvedDetail");
            Scribe_Values.Look(ref quarantineReason, "quarantineReason");
            Scribe_Values.Look(ref legacyQuarantineReason, "legacyQuarantineReason");
            Scribe_Values.Look(ref custodyRevision, "custodyRevision");
            Scribe_Values.Look(ref stateRevision, "stateRevision");
            state ??= new();
        }
    }

    /// <summary>Observed form of a record, derived from placement and the native graph. Never saved.</summary>
    public enum GuObservedForm { Unavailable = 0, ActivePawn = 1, GroundThing = 2, HeldThing = 3, OtherHolder = 4, Contained = 5, WorldPawn = 6 }

    /// <summary>Immutable read model (RFC-0004 §5.4).</summary>
    public readonly record struct GuView(string Id, string SpeciesDefName, int Rank, GuExistence Existence, GuCondition Condition,
        GuPlacementKind Placement, GuObservedForm Form, string NativeId, string HolderId, string MapId, int CustodyRevision,
        int StateRevision, GuUnresolvedCause Unresolved, GuTerminalEvidence Evidence, bool Quarantined, bool Rogue);
}
