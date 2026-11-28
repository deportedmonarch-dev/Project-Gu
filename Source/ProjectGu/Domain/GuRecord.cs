using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>Retired: removed from individual simulation under a declared accounting policy. Not a biological ending.</summary>
    public enum GuExistence { Living = 1, Terminal = 2, Transformed = 3, Aggregated = 4, Retired = 5 }

    /// <summary>Why a terminal record ended (RFC-0004 §3.2). Set only from a demonstrated event or an explicit operation.</summary>
    public enum GuTerminalOutcome { None = 0, Died = 1, Destroyed = 2, DeliberatelyDestroyed = 3, Consumed = 4, Lost = 5, Sacrificed = 6 }

    /// <summary>Abandonment policy when a Gu loses its native representation without dying (owner decisions §4).</summary>
    public enum GuPreservationPolicy { Individual = 0, ReturnToPopulation = 1, RetireFromSimulation = 2 }

    public enum GuCondition { Active = 1, Dormant = 2 }

    public enum GuPlacementKind { Manifested = 1, Contained = 2 }

    public enum GuManifestationKind { Pawn = 1, Thing = 2 }

    public enum GuContainerKind { MortalAperture = 1, WorldSite = 2 }

    /// <summary>Why an otherwise living record cannot currently be acted on. None means resolved.</summary>
    public enum GuUnresolvedCause
    {
        None = 0,
        SpeciesUnavailable = 1,
        ProjectionMissing = 2,
        ProjectionEnded = 3,
        HostDead = 4,
        HostUnavailable = 5,
        ApertureMissing = 6,
        HostCorpseDestroyed = 7,
        MapRemoved = 8
    }

    /// <summary>A demonstrated native event attached to an unresolved record. Death and destruction become terminal outcomes instead.</summary>
    public enum GuTerminalEvidence { None = 0, Destroyed = 1, Killed = 2, MapRemoved = 3, Discarded = 4, HostCorpseDestroyed = 5 }

    /// <summary>Optional, species-specific persistent state. Created on demand by a capability.</summary>
    public abstract class GuStateComponent : IExposable
    {
        public virtual int Version => 1;
        public abstract void ExposeData();
    }

    /// <summary>Refinement allegiance, independent of custody and biology. Assigned only after a process succeeds.</summary>
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
    public sealed partial class GuRecord : IExposable
    {
        internal const int CurrentVersion = 2;
        internal const string CohortOrigin = "cohort";
        internal const string MigrationOrigin = "migration";
        internal const string DevelopmentOrigin = "development";

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
        private GuCreationEvidence creationEvidence;

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
        private GuRefinementProcess refinementProcess;
        private List<GuStateComponent> state = new();

        // Unresolved overlay. The placement facet keeps the last authorized placement for recovery.
        private GuUnresolvedCause unresolved;
        private GuTerminalEvidence terminalEvidence;
        private int unresolvedSinceTick = -1;
        private string unresolvedDetail;

        // Lifecycle outcome and evidence (Phase 2). A terminal record is a tombstone: identity, provenance and accounting stay.
        private GuTerminalOutcome terminalOutcome;
        private int endedTick = -1;
        private string endEventKey;
        private string endActorId;
        private GuLocation lastKnown;
        private List<GuLifecycleEvent> history = new();
        private bool compacted;

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
        /// <summary>Alpha 6 creation receipt. Null on records created before Alpha 6, which keep the original spring rules.</summary>
        public GuCreationEvidence CreationEvidence => creationEvidence;
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
        public GuRefinementProcess RefinementProcess => refinementProcess;
        public IReadOnlyList<GuStateComponent> State => state;
        public GuUnresolvedCause Unresolved => unresolved;
        public bool IsUnresolved => unresolved != GuUnresolvedCause.None;
        public GuTerminalEvidence TerminalEvidence => terminalEvidence;
        public int UnresolvedSinceTick => unresolvedSinceTick;
        public string UnresolvedDetail => unresolvedDetail;
        public string QuarantineReason => quarantineReason;
        public bool IsQuarantined => !string.IsNullOrEmpty(quarantineReason);
        public string LegacyQuarantineReason => legacyQuarantineReason;
        public GuTerminalOutcome TerminalOutcome => terminalOutcome;
        public int EndedTick => endedTick;
        public string EndEventKey => endEventKey;
        public string EndActorId => endActorId;
        public GuLocation LastKnown => lastKnown;
        public IReadOnlyList<GuLifecycleEvent> History => history;
        public bool IsCompacted => compacted;
        public bool IsLiving => existence == GuExistence.Living;
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

        /// <summary>
        /// A provisional individual for one source materialization. It is not in the registry and has no debit until the
        /// transaction commits. The origin stays <see cref="CohortOrigin"/>; the receipt names the source kind and trigger.
        /// </summary>
        internal static GuRecord PrepareFromSource(string id, GuSpeciesDef species, GuSourceRequest request, int tick, string pawnId,
            string assetId = null)
        {
            var record = CreateManifested(id, species, CohortOrigin, request.SourceId, tick, GuCondition.Active,
                GuManifestationKind.Pawn, pawnId);
            record.creationEventKey = request.EventKey;
            record.originNativeId = request.Credential;
            record.creationEvidence = new GuCreationEvidence(request, assetId);
            return record;
        }

        /// <summary>The debit committed at the given cohort revision. Part of the callback-free commit window.</summary>
        internal void CommitSourceDebit(long revisionAfter, GuAssetSettlement settlement = GuAssetSettlement.None)
        {
            cohortDebitCommitted = true;
            creationEvidence?.MarkDebited(revisionAfter, settlement);
        }

        /// <summary>Only after the cohort preimage was restored, before publication: the record no longer proves a debit.</summary>
        internal void RevertUnpublishedDebit()
        {
            cohortDebitCommitted = false;
            creationEvidence?.ClearDebit();
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

        /// <summary>The first species state of this type, or null. Read-only.</summary>
        internal T GetState<T>() where T : GuStateComponent
        {
            if (state == null) return null;
            for (int i = 0; i < state.Count; i++)
                if (state[i] is T found) return found;
            return null;
        }

        /// <summary>Attaches one species state of a type the record does not yet carry. Never replaces an existing state.</summary>
        internal bool AttachState(GuStateComponent component)
        {
            if (component == null || existence != GuExistence.Living) return false;
            state ??= new();
            var type = component.GetType();
            for (int i = 0; i < state.Count; i++)
                if (state[i]?.GetType() == type) return false;
            state.Add(component);
            return true;
        }

        internal void Quarantine(string reason)
        {
            if (IsQuarantined) return;
            quarantineReason = reason;
            Log.Warning($"[Project GU] Quarantined Gu {id ?? "(missing ID)"}. {reason}");
        }

        /// <summary>Operator recovery only, after the registry re-checks that the contradiction no longer holds.</summary>
        internal void LiftQuarantine() => quarantineReason = null;

        internal void MarkUnresolved(GuUnresolvedCause cause, GuTerminalEvidence evidence, string detail, int tick, GuLocation location = null)
        {
            if (existence != GuExistence.Living) return;
            NoteLocation(location);
            // A demonstrated terminal event is never overwritten by a weaker, absence-of-evidence cause.
            if (IsUnresolved && terminalEvidence != GuTerminalEvidence.None && evidence == GuTerminalEvidence.None) return;
            if (unresolved == cause && terminalEvidence == evidence) return;
            unresolved = cause;
            terminalEvidence = evidence;
            unresolvedDetail = detail;
            if (unresolvedSinceTick < 0) unresolvedSinceTick = tick;
            AddHistory(new(tick, GuLifecycleEventKind.Unresolved, cause.ToString(), null, null, location,
                evidence == GuTerminalEvidence.None ? detail : $"Evidence {evidence}. {detail}"));
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

        internal bool SurfaceResidencyEnded { get; private set; }
        private string introducedSurfaceSite;
        internal string SurfaceResidence => !IsLiving || SurfaceResidencyEnded ? null : SurfaceResidenceEvidence;
        internal string SurfaceResidenceEvidence => introducedSurfaceSite
            ?? (CohortDebitCommitted && CreationEvidence?.OwnerKind == GuSurfaceEcology.SourceKind ? CreationEvidence.SourceId : null);
        internal bool IsAdmittedSurfaceResident => introducedSurfaceSite != null;
        private int escapeAdmissionRevision = -1;
        internal bool HasEscapeEvidence => escapeAdmissionRevision == custodyRevision
            && history?.Exists(entry => entry.Kind == GuLifecycleEventKind.Escaped) == true;
        internal void RecordEscapePlacement() => escapeAdmissionRevision = custodyRevision;

        internal void EndSurfaceResidency() => SurfaceResidencyEnded = true;

        internal void JoinSurfaceResidence(string site)
        {
            introducedSurfaceSite = site;
            SurfaceResidencyEnded = false;
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

        /// <summary>Ends any refinement bond. The Gu becomes masterless and can be refined by someone else.</summary>
        internal void ReleaseRefinement()
        {
            refinement = null;
            refinementProcess = null;
        }

        internal bool BeginRefinement(GuRefinementProcess process)
        {
            if (process == null || !process.IsValid || refinementProcess != null) return false;
            refinementProcess = process;
            stateRevision++;
            return true;
        }

        internal bool AbandonRefinement(string expectedProcessId)
        {
            if (refinementProcess?.ProcessId != expectedProcessId) return false;
            refinementProcess = null;
            stateRevision++;
            return true;
        }

        internal bool CompleteRefinement(string expectedProcessId, string expectedPreviousOwner, string cultivatorId, int tick)
        {
            if (refinementProcess?.ProcessId != expectedProcessId || !refinementProcess.IsValid
                || !string.Equals(refinement?.CultivatorId, expectedPreviousOwner, StringComparison.OrdinalIgnoreCase)
                || refinementProcess.CompletedWorkTicks != refinementProcess.TotalWorkTicks
                || refinementProcess.CultivatorId != cultivatorId) return false;
            refinement = new GuRefinement { cultivatorId = cultivatorId, sinceTick = tick };
            NoteEncounterInteraction(tick, historical: true);
            refinementProcess = null;
            stateRevision++;
            return true;
        }

        internal const int MaxHistory = 8;

        internal void AddHistory(GuLifecycleEvent entry)
        {
            history ??= new();
            history.Add(entry);
            // The newest entries are kept. The entry that ended the record is never dropped.
            while (history.Count > MaxHistory)
            {
                int index = history.FindIndex(item => item.Kind is not (GuLifecycleEventKind.Terminal or GuLifecycleEventKind.Retired));
                if (index < 0) break;
                history.RemoveAt(index);
            }
        }

        internal void NoteLocation(GuLocation location)
        {
            if (location != null) lastKnown = location;
        }

        /// <summary>Ends the individual after a demonstrated event or explicit operation. Identity and accounting remain.</summary>
        internal void EndLife(GuTerminalOutcome outcome, string eventKey, string actorId, GuLocation location, string detail, int tick)
        {
            if (existence != GuExistence.Living || outcome == GuTerminalOutcome.None) return;
            existence = GuExistence.Terminal;
            terminalOutcome = outcome;
            endedTick = tick;
            endEventKey = eventKey;
            endActorId = actorId;
            NoteLocation(location);
            ClearUnresolved();
            stateRevision++;
            AddHistory(new(tick, GuLifecycleEventKind.Terminal, outcome.ToString(), eventKey, actorId, location, detail));
            Log.Message($"[Project GU] Gu {id} ended: {outcome}. {detail}");
        }

        /// <summary>Removes the individual from simulation under a declared accounting policy. It is not a death.</summary>
        internal void Retire(string cause, GuLocation location, string detail, int tick)
        {
            if (existence != GuExistence.Living) return;
            existence = GuExistence.Retired;
            endedTick = tick;
            NoteLocation(location);
            ClearUnresolved();
            stateRevision++;
            AddHistory(new(tick, GuLifecycleEventKind.Retired, cause, null, null, location, detail));
            Log.Message($"[Project GU] Gu {id} retired from individual simulation: {cause}. {detail}");
        }

        internal static string WorldSiteId(GuLocation location) => location.MapParentId is { } parent
            ? $"site:{location.Layer}:{location.Tile}:{parent}" : $"site:{location.Layer}:{location.Tile}";

        /// <summary>Abstract world-location custody for a Gu whose native map or holder is gone (owner decisions §4).</summary>
        internal void CommitWorldSite(GuLocation location, string cause, string detail, int tick)
        {
            CommitContainment(GuContainerKind.WorldSite, WorldSiteId(location), location.MapId ?? WorldSiteId(location));
            lastKnown = location;
            ClearUnresolved();
            AddHistory(new(tick, GuLifecycleEventKind.Abstracted, cause, null, null, location, detail));
            Log.Message($"[Project GU] Gu {id} is held at an abstract world location: {location.Describe()}. {detail}");
        }

        /// <summary>
        /// Compacts a terminal or retired record after the retention period (owner decisions §5). Keeps what replay, receipts,
        /// conservation and uniqueness need: identity, species, provenance, event keys, debit, outcome and end tick.
        /// </summary>
        internal bool Compact()
        {
            if (existence == GuExistence.Living || compacted || IsQuarantined) return false;
            projectionId = null;
            containerId = null;
            anchorHint = null;
            legacyApertureLoadId = null;
            refinement = null;
            state?.Clear();
            encounter = null;
            lastKnown = null;
            unresolvedDetail = null;
            legacyQuarantineReason = null;
            // A terminal record already stores its outcome, tick, key and actor. Only a retirement keeps its cause in history.
            var ending = existence == GuExistence.Retired ? history?.FindLast(item => item.Kind == GuLifecycleEventKind.Retired) : null;
            history = new();
            if (ending != null)
            {
                ending.Summarize();
                history.Add(ending);
            }
            compacted = true;
            return true;
        }

        /// <summary>Summarizes narrative detail older than the cutoff on a living record. Facts, causes and keys stay.</summary>
        internal int SummarizeHistoryBefore(int cutoffTick)
        {
            int count = 0;
            if (history == null) return 0;
            for (int index = 0; index < history.Count - 1; index++)
            {
                var entry = history[index];
                if (entry.Tick >= cutoffTick || (entry.Detail == null && entry.Location == null)) continue;
                entry.Summarize();
                count++;
            }
            return count;
        }

        internal string DebugDescription => $"Gu {id ?? "(missing ID)"} | {speciesDefName} rank {rank} | custody revision {custodyRevision} | state revision {stateRevision}"
            + $"\n{existence} | {condition} | " + (populationReturn != null ? $"returned to {populationReturn.Describe()}" : placement == GuPlacementKind.Manifested
                ? $"manifested as {manifestation} ({projectionId})"
                : $"contained in {containerKind} {containerId ?? "(unassigned)"} (anchor {anchorHint})")
            + (creationEventKey == null ? "" : $"\nOrigin habitat: {originId} ({originNativeId}) | Event: {creationEventKey} | Adult debited: {cohortDebitCommitted}")
            + (creationEvidence == null ? "" : $"\nCreation receipt: {creationEvidence.Describe()}")
            + (IsUnresolved ? $"\nUnresolved: {unresolved}" + (terminalEvidence == GuTerminalEvidence.None ? "" : $", evidence {terminalEvidence}") + $" since {unresolvedSinceTick}. {unresolvedDetail}" : "")
            + (existence == GuExistence.Living ? "" : $"\nEnded: {existence}"
                + (terminalOutcome == GuTerminalOutcome.None ? "" : $" ({terminalOutcome})") + $" at {endedTick}"
                + (endEventKey == null ? "" : $" key {endEventKey}") + (compacted ? " [compacted]" : ""))
            + (lastKnown == null ? "" : $"\nLast known location: {lastKnown.Describe()}")
            + (IsQuarantined ? $"\nQuarantined: {quarantineReason}" : "")
            + (legacyQuarantineReason == null ? "" : $"\nV1 quarantine before migration: {legacyQuarantineReason}");

        internal string HistoryDescription => history == null || history.Count == 0 ? "No lifecycle evidence."
            : string.Join("\n", history.ConvertAll(entry => "- " + entry.Describe()));

        public void ExposeData()
        {
            Scribe_Deep.Look(ref encounter, "encounterRetention");
            Scribe_Deep.Look(ref populationReturn, "populationReturn");
            bool surfaceResidencyEnded = SurfaceResidencyEnded;
            Scribe_Values.Look(ref surfaceResidencyEnded, "surfaceResidencyEnded");
            SurfaceResidencyEnded = surfaceResidencyEnded;
            Scribe_Values.Look(ref introducedSurfaceSite, "introducedSurfaceSite");
            Scribe_Values.Look(ref escapeAdmissionRevision, "surfaceEscapeAdmissionRevision", -1);
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
            Scribe_Deep.Look(ref creationEvidence, "creationEvidence");
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
            Scribe_Deep.Look(ref refinementProcess, "refinementProcess");
            Scribe_Collections.Look(ref state, "state", LookMode.Deep);
            Scribe_Values.Look(ref unresolved, "unresolved", GuUnresolvedCause.None);
            Scribe_Values.Look(ref terminalEvidence, "terminalEvidence", GuTerminalEvidence.None);
            Scribe_Values.Look(ref unresolvedSinceTick, "unresolvedSinceTick", -1);
            Scribe_Values.Look(ref unresolvedDetail, "unresolvedDetail");
            Scribe_Values.Look(ref terminalOutcome, "terminalOutcome", GuTerminalOutcome.None);
            Scribe_Values.Look(ref endedTick, "endedTick", -1);
            Scribe_Values.Look(ref endEventKey, "endEventKey");
            Scribe_Values.Look(ref endActorId, "endActorId");
            Scribe_Deep.Look(ref lastKnown, "lastKnown");
            Scribe_Collections.Look(ref history, "history", LookMode.Deep);
            Scribe_Values.Look(ref compacted, "compacted");
            Scribe_Values.Look(ref quarantineReason, "quarantineReason");
            Scribe_Values.Look(ref legacyQuarantineReason, "legacyQuarantineReason");
            Scribe_Values.Look(ref custodyRevision, "custodyRevision");
            Scribe_Values.Look(ref stateRevision, "stateRevision");
            state ??= new();
            history ??= new();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && refinementProcess != null && !refinementProcess.IsValid)
            {
                Log.Warning($"[Project GU] Dropped malformed in-progress refinement state for Gu {id}; allegiance and custody are unchanged.");
                refinementProcess = null;
            }
        }
    }

    /// <summary>Observed form of a record, derived from placement and the native graph. Never saved.</summary>
    public enum GuObservedForm { Unavailable = 0, ActivePawn = 1, GroundThing = 2, HeldThing = 3, OtherHolder = 4, Contained = 5, WorldPawn = 6, ContainedInDeceased = 7, WorldSite = 8 }

    /// <summary>Immutable read model (RFC-0004 §5.4).</summary>
    public readonly record struct GuView(string Id, string SpeciesDefName, int Rank, GuExistence Existence, GuCondition Condition,
        GuPlacementKind Placement, GuObservedForm Form, string NativeId, string HolderId, string MapId, int CustodyRevision,
        int StateRevision, GuUnresolvedCause Unresolved, GuTerminalEvidence Evidence, bool Quarantined, bool Rogue);
}
