using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Alpha 6 Slice 0: the generalized source-authorization and materialization boundary (RFC-0007). A population source
    /// supplies anonymous mature units; the registry alone turns exactly one of them into one canonical individual and its
    /// receipt. Order: gate, request, stable source identity, receipt replay, live credential, species/rank/stage/trigger
    /// authorization, ownership consistency, exact cohort revision, placement, native staging with revalidation after native
    /// callbacks, then a callback-free commit window (debit, receipt, record, binding) and publication of one projection.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        private readonly Dictionary<string, GuRecord> byCreationEvent = new(StringComparer.Ordinal);

        // Highest cohort revision each source has committed through a receipt, per species. A live cohort below it was
        // regenerated, reseeded or restored out of order, so its ownership is inconsistent. Transient, rebuilt on load.
        private readonly Dictionary<string, long> committedRevisionFloor = new(StringComparer.Ordinal);

        private static string FloorKey(string ownerKind, string sourceId, string species) => ownerKind + "\n" + sourceId + "\n" + species;

        private void RebuildCreationEventBindings(IEnumerable<GuRecord> source)
        {
            committedRevisionFloor.Clear();
            foreach (var claims in source.Where(record => !string.IsNullOrWhiteSpace(record.CreationEventKey))
                .GroupBy(record => record.CreationEventKey, StringComparer.Ordinal))
            {
                var claimants = claims.ToArray();
                if (claimants.Length == 1)
                    byCreationEvent.Add(claims.Key, claimants[0]);
                else
                {
                    byCreationEvent.Add(claims.Key, null);
                    foreach (var record in claimants) record.Quarantine("Multiple individuals claim this creation event.");
                }
            }
            // A malformed receipt quarantines its own record and does not move the floor.
            foreach (var record in source)
                if (record.CohortDebitCommitted && record.CreationEvidence is { } receipt && receipt.ShapeError(record) == null)
                    RaiseCommittedFloor(FloorKey(receipt.OwnerKind, receipt.SourceId, record.SpeciesDefName), receipt.RevisionAfter);
        }

        private void RaiseCommittedFloor(string key, long revision)
        {
            if (!committedRevisionFloor.TryGetValue(key, out long floor) || floor < revision) committedRevisionFloor[key] = revision;
        }

        // ---- Legacy spring entry points ------------------------------------------------------------------------------------

        internal static bool TryPrepareMaterializationRequest(IGuCohortHabitat habitat, string speciesDefName, string eventKey,
            out GuCohortMaterializationRequest request)
        {
            request = default;
            if (string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(habitat?.HabitatId)
                || habitat.Anchor is not { } source || habitat.CohortFor(speciesDefName) is not { } cohort) return false;
            request = new(eventKey, habitat.HabitatId, source.GetUniqueLoadID(), cohort.SpeciesDefName, cohort.Revision);
            return true;
        }

        /// <summary>Development Mode draw from a spring cohort through the shared transaction.</summary>
        internal bool TryMaterializeCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, Map map, IntVec3 cell,
            out GuRecord individual, out string reason) => TryMaterializeSpringCohort(habitat, request, GuSourceTriggers.Development,
                null, map, cell, out individual, out reason);

        /// <summary>A player's search of a spring.</summary>
        internal bool TryEncounterWildCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, Map map,
            IntVec3 cell, out GuRecord individual, out string reason) => TryMaterializeSpringCohort(habitat, request,
                GuSourceTriggers.SpringSearch, null, map, cell, out individual, out reason);

        /// <summary>A player encounter with an explicit trigger and asset scope, for example a lure and its bait Def.</summary>
        internal bool TryEncounterWildCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, string trigger,
            string assetScope, Map map, IntVec3 cell, out GuRecord individual, out string reason) =>
            TryMaterializeSpringCohort(habitat, request, trigger, assetScope, map, cell, out individual, out reason);

        private bool TryMaterializeSpringCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, string trigger,
            string assetScope, Map map, IntVec3 cell, out GuRecord individual, out string reason)
        {
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(request.SpeciesDefName ?? "");
            var outcome = TryMaterializeFromSource(ForSpring(habitat),
                new GuSourceRequest(request.EventKey, SpringPopulationSource.Kind, request.HabitatId, request.SourceNativeId,
                    request.SpeciesDefName, species?.minRank ?? 0, GuCohortStage.Mature, trigger, assetScope, request.ExpectedRevision),
                map, cell, out individual, out reason);
            // The legacy contract returns an individual only for a commit or replay.
            if (outcome is not (GuSourceOutcome.Committed or GuSourceOutcome.Replayed)) individual = null;
            return individual != null;
        }

        // ---- Generalized source transaction --------------------------------------------------------------------------------

        /// <summary>Captures an exact request against the source's current credential and cohort revision.</summary>
        internal static bool TryPrepareSourceRequest(IGuPopulationSource source, string speciesDefName, string trigger,
            string assetScope, string eventKey, out GuSourceRequest request)
        {
            request = default;
            if (source == null || string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(trigger)
                || string.IsNullOrWhiteSpace(source.SourceId) || string.IsNullOrWhiteSpace(source.CurrentCredential)
                || source.CohortFor(speciesDefName) is not { } cohort) return false;
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(cohort.SpeciesDefName);
            request = new(eventKey, source.OwnerKind, source.SourceId, source.CurrentCredential, cohort.SpeciesDefName,
                species?.minRank ?? 0, GuCohortStage.Mature, trigger, GuCreationEvidence.NormalizeScope(assetScope), cohort.Revision);
            return true;
        }

        private sealed class SourceTransaction
        {
            internal IGuPopulationSource Source;
            internal GuSourceRequest Request;
            internal GuSpeciesDef Species;
            internal GuPopulationRecord Cohort;
            internal GuCohortPreimage Preimage;
            internal string FloorKey;
            internal bool HadFloor;
            internal long PreviousFloor;
            internal Map Map;
            internal IntVec3 Cell;
            internal string Id;
            internal int Tick;
            internal Pawn_GuWildProjection Staged;
            internal GuRecord Record;
            internal bool Debited;
            internal bool Published;
        }

        /// <summary>
        /// Turns one mature unit of an authorized source into one canonical living individual, or replays the event's
        /// receipt. Never creates a second individual or a second debit for one event key. <paramref name="individual"/>
        /// is the created, replayed or unresolved canonical record, or null when nothing is counted for this event.
        /// </summary>
        internal GuSourceOutcome TryMaterializeFromSource(IGuPopulationSource source, GuSourceRequest request, Map map, IntVec3 cell,
            out GuRecord individual, out string reason)
        {
            individual = null;
            reason = "The native cohort materialization safeguards are unavailable for this game configuration.";
            if (source == null) return GuSourceOutcome.Refused;
            bool development = source.RequiresDevelopmentMode(request.Trigger);
            // Also refuses callback re-entry and saves: custody is unavailable while any transition is in progress.
            if (development ? !CanUseDevelopmentCustody : !CanUseCustody) return GuSourceOutcome.Refused;

            request = request with { AssetScope = GuCreationEvidence.NormalizeScope(request.AssetScope) };
            reason = "The materialization request has no exact source, species, trigger or event.";
            if (string.IsNullOrWhiteSpace(request.EventKey) || string.IsNullOrWhiteSpace(request.OwnerKind)
                || string.IsNullOrWhiteSpace(request.SourceId) || string.IsNullOrWhiteSpace(request.SpeciesDefName)
                || string.IsNullOrWhiteSpace(request.Trigger) || request.ExpectedRevision < 0) return GuSourceOutcome.Refused;
            reason = "The request names a different population source.";
            if (!string.Equals(source.OwnerKind, request.OwnerKind, StringComparison.Ordinal)
                || !string.Equals(source.SourceId, request.SourceId, StringComparison.Ordinal)) return GuSourceOutcome.Refused;

            // A committed event replays by its receipt before any revision or credential test, so a stale request still
            // returns the same individual with zero effect.
            if (byCreationEvent.TryGetValue(request.EventKey, out var previous))
            {
                reason = ReceiptRefusal(previous, request, out bool conflict);
                if (reason != null) return conflict ? GuSourceOutcome.Conflict : GuSourceOutcome.Refused;
                individual = previous;
                reason = $"This event already materialized Gu {previous.Id}.";
                return GuSourceOutcome.Replayed;
            }

            reason = "The population source's native credential is stale or unavailable. Reacquire it from the live source.";
            string credential = source.CurrentCredential;
            if (string.IsNullOrWhiteSpace(credential) || !string.Equals(credential, request.Credential, StringComparison.Ordinal))
                return GuSourceOutcome.Refused;

            reason = "This species has no eligible wild cohort projection.";
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(request.SpeciesDefName);
            if (species?.wildPopulationEnabled != true
                || !development && species.capabilityTags?.Contains("development_test") == true
                || !Thing_GuDormantProjection.IsConfigured(species.dormantProjectionDef)
                || FirstAdultProjectionStage(species.wildProjectionKind) is not { } adult)
                return GuSourceOutcome.Refused;
            reason = "The species cannot be issued at the requested rank or life stage.";
            if (request.Stage != GuCohortStage.Mature || request.Rank < species.minRank || request.Rank > species.maxRank)
                return GuSourceOutcome.Refused;
            if (source.RefuseIssue(species, request.Rank, request.Stage, request.Trigger, request.AssetScope) is { } refusal)
            {
                reason = refusal;
                return GuSourceOutcome.Refused;
            }

            reason = "More than one live source claims this identity. Mutation is blocked until ownership is repaired.";
            if (!source.HasUniqueIdentity) return GuSourceOutcome.Refused;
            reason = "The exact source cohort is unavailable.";
            if (source.CohortFor(request.SpeciesDefName) is not { } cohort || cohort.Revision == long.MaxValue) return GuSourceOutcome.Refused;
            string floorKey = FloorKey(request.OwnerKind, request.SourceId, request.SpeciesDefName);
            bool hadFloor = committedRevisionFloor.TryGetValue(floorKey, out long floor);
            if (hadFloor && cohort.Revision < floor)
            {
                reason = $"The source cohort (revision {cohort.Revision}) is older than its committed creation receipts (revision {floor}). "
                    + "Its ownership is inconsistent, so it cannot issue Gu until repaired.";
                Log.WarningOnce($"[Project GU] {reason} Source {request.OwnerKind}:{request.SourceId}, {request.SpeciesDefName}.",
                    floorKey.GetHashCode());
                return GuSourceOutcome.Refused;
            }
            reason = "The source cohort changed since this request was prepared.";
            if (cohort.Revision != request.ExpectedRevision) return GuSourceOutcome.Refused;
            reason = "This cohort has no mature Gu available.";
            if (cohort.MatureCount < 1) return GuSourceOutcome.Refused;

            var tx = new SourceTransaction
            {
                Source = source, Request = request, Species = species, Cohort = cohort, Preimage = cohort.Preimage,
                FloorKey = floorKey, HadFloor = hadFloor, PreviousFloor = floor, Map = map, Cell = cell,
                Id = Guid.NewGuid().ToString("N"), Tick = Find.TickManager.TicksGame
            };
            reason = source.PlacementRefusal ?? "Choose an open, unreserved cell that this source permits on a live map.";
            if (byId.ContainsKey(tx.Id) || !SourceReady(tx, null)) return GuSourceOutcome.Refused;

            GuSourceOutcome outcome;
            CustodyTransitionInProgress = true;
            try
            {
                bool committed = false;
                reason = "Native placement or the exact cohort changed during materialization.";
                try { committed = StageAndCommitSource(tx, adult); }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted cohort materialization.";
                    Log.Warning($"[Project GU] {reason} Event {request.EventKey}. {exception}");
                }
                if (committed)
                {
                    individual = tx.Record;
                    reason = $"Materialized Gu {tx.Record.Id}.";
                    outcome = GuSourceOutcome.Committed;
                }
                else outcome = RecoverFailedSource(tx, ref reason, out individual);
            }
            finally { CustodyTransitionInProgress = false; }
            return outcome;
        }

        private bool StageAndCommitSource(SourceTransaction tx, LifeStageAge adult)
        {
            string key = tx.Request.EventKey;
            var kind = tx.Species.wildProjectionKind;
            GuTransactionProbe.Reach(GuTransactionPoint.BeforeStaging, key);
            tx.Staged = PrepareWildPawn(kind, adult, tx.Map);
            tx.Record = GuRecord.PrepareFromSource(tx.Id, tx.Species, tx.Request, tx.Tick, tx.Staged.GetUniqueLoadID());
            tx.Staged.InitializeBinding(tx.Record);
            // Native SpawnSetup and modded callbacks run inside TryPlaceStagedWildPawn. The source is revalidated afterwards.
            if (!CanRestoreDetached(tx.Staged) || !CanRetire(tx.Staged) || !RevalidateSource(tx, null)
                || !TryPlaceStagedWildPawn(tx.Staged, tx.Record, kind, tx.Map, tx.Cell) || !RevalidateSource(tx, tx.Staged))
                return false;
            GuTransactionProbe.Reach(GuTransactionPoint.AfterStagingBeforeDebit, key);

            // Callback-free commit window: debit, receipt, canonical record and binding. No native code runs until publication.
            if (!tx.Cohort.TryConsumeMature(1)) return false;
            tx.Debited = true;
            GuTransactionProbe.Reach(GuTransactionPoint.AfterDebit, key);
            tx.Record.CommitSourceDebit(tx.Preimage.Revision + 1);
            RecordSourceCreation(tx);
            BindProjection(tx.Record, tx.Staged);
            GuTransactionProbe.Reach(GuTransactionPoint.BeforePublication, key);

            tx.Staged.SetReleaseSuppressed(false);
            tx.Published = true;
            return MatchesManifestation(tx.Record, tx.Staged) && IsOnCell(tx.Staged, tx.Map, tx.Cell);
        }

        private static bool SourceReady(SourceTransaction tx, Pawn_GuWildProjection staged)
        {
            var source = tx.Source;
            var request = tx.Request;
            return string.Equals(source.CurrentCredential, request.Credential, StringComparison.Ordinal)
                && string.Equals(source.OwnerKind, request.OwnerKind, StringComparison.Ordinal)
                && string.Equals(source.SourceId, request.SourceId, StringComparison.Ordinal) && source.HasUniqueIdentity
                && source.CohortFor(request.SpeciesDefName) == tx.Cohort && tx.Cohort.Revision == request.ExpectedRevision
                && tx.Cohort.MatureCount > 0 && CanPlaceDormant(tx.Map, tx.Cell, staged)
                && !tx.Map.reservationManager.IsReserved(tx.Cell) && source.PlacementReady(tx.Map, tx.Cell, request.Trigger);
        }

        private static bool RevalidateSource(SourceTransaction tx, Pawn_GuWildProjection staged)
        {
            GuTransactionProbe.Reach(GuTransactionPoint.SourceRevalidation, tx.Request.EventKey);
            return SourceReady(tx, staged);
        }

        /// <summary>Null when the event's receipt is the same payload. Conflict marks a reused key with a different payload.</summary>
        private static string ReceiptRefusal(GuRecord previous, GuSourceRequest request, out bool conflict)
        {
            conflict = false;
            if (previous == null || previous.IsQuarantined || !previous.CohortDebitCommitted)
                return "This event has an ambiguous, quarantined or uncommitted claim, so it cannot authorize a Gu.";
            string facet = previous.CreationEvidence is { } receipt ? receipt.PayloadConflict(request, previous.SpeciesDefName)
                // Records created before Alpha 6 keep their original rule: the exact spring habitat, anchor and species.
                : !string.Equals(request.OwnerKind, SpringPopulationSource.Kind, StringComparison.Ordinal)
                    || !string.Equals(previous.OriginId, request.SourceId, StringComparison.Ordinal) ? "source"
                : !string.Equals(previous.OriginNativeId, request.Credential, StringComparison.Ordinal) ? "source instance"
                : !string.Equals(previous.SpeciesDefName, request.SpeciesDefName, StringComparison.Ordinal) ? "species" : null;
            if (facet == null) return null;
            conflict = true;
            return $"This event key already belongs to a committed creation with a different {facet}. The reuse is rejected.";
        }

        private void RecordSourceCreation(SourceTransaction tx)
        {
            var record = tx.Record;
            if (!byId.ContainsKey(record.Id)) records.Add(record);
            byId[record.Id] = record;
            byCreationEvent[record.CreationEventKey] = record;
            RaiseCommittedFloor(tx.FloorKey, tx.Preimage.Revision + 1);
        }

        private void UnrecordSourceCreation(SourceTransaction tx)
        {
            var record = tx.Record;
            UnbindAll(record);
            records.Remove(record);
            if (byId.TryGetValue(record.Id, out var known) && known == record) byId.Remove(record.Id);
            if (byCreationEvent.TryGetValue(record.CreationEventKey, out var claim) && claim == record)
                byCreationEvent.Remove(record.CreationEventKey);
            if (tx.HadFloor) committedRevisionFloor[tx.FloorKey] = tx.PreviousFloor;
            else committedRevisionFloor.Remove(tx.FloorKey);
        }

        /// <summary>
        /// Before a debit, or after a debit whose preimage could be restored before publication: discard the shell and spend
        /// nothing; if the shell cannot be proven gone, quarantine the event with no debit. Otherwise keep exactly one counted
        /// unresolved individual with its receipt. Never grants stock, deletes unrelated Things or adjusts counts silently.
        /// </summary>
        private GuSourceOutcome RecoverFailedSource(SourceTransaction tx, ref string reason, out GuRecord individual)
        {
            individual = null;
            string key = tx.Request.EventKey;
            try
            {
                if (tx.Debited && !tx.Published && TryRestoreSourcePreimage(tx))
                {
                    // The unit is back in the cohort, so from here the record must never count it a second time.
                    tx.Debited = false;
                    tx.Record.RevertUnpublishedDebit();
                    UnrecordSourceCreation(tx);
                    reason += " The debit was reversed before publication.";
                }
                if (!tx.Debited)
                {
                    if (TryDiscardStaged(tx))
                    {
                        reason += " No Gu was spent and the event may be retried.";
                        return GuSourceOutcome.RolledBack;
                    }
                    reason = "Provisional Pawn cleanup could not be verified. The event was quarantined without a cohort debit.";
                    MarkRogue(tx.Staged?.GuTag(), reason);
                    var claim = tx.Record ?? GuRecord.PrepareFromSource(tx.Id, tx.Species, tx.Request, tx.Tick, "unplaced");
                    claim.Quarantine(reason);
                    if (!byId.ContainsKey(claim.Id)) records.Add(claim);
                    byId[claim.Id] = claim;
                    byCreationEvent[key] = claim;
                    return GuSourceOutcome.Quarantined;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Recovery of materialization event {key} failed. {exception}");
                if (!tx.Debited)
                {
                    // Nothing was spent. Block the event so an unproven shell or a half-indexed claim can never be reused.
                    try
                    {
                        MarkRogue(tx.Staged?.GuTag(), "Materialization recovery failed before any debit.");
                        if (tx.Record is { } claim)
                        {
                            claim.Quarantine("Materialization recovery failed before any debit.");
                            if (!byId.ContainsKey(claim.Id)) records.Add(claim);
                            byId[claim.Id] = claim;
                            byCreationEvent[key] = claim;
                        }
                    }
                    catch (Exception blocked) { Log.Error($"[Project GU] Could not quarantine event {key}. {blocked}"); }
                    return GuSourceOutcome.Quarantined;
                }
            }
            return KeepUnresolvedClaim(tx, ref reason, out individual);
        }

        private GuSourceOutcome KeepUnresolvedClaim(SourceTransaction tx, ref string reason, out GuRecord individual)
        {
            var record = tx.Record;
            individual = record;
            reason = "The cohort debit committed but the native Pawn binding failed. The individual is unresolved and still counted.";
            // Each step is independent so that a fault in one never loses the counted claim.
            if (!record.CohortDebitCommitted) record.CommitSourceDebit(tx.Preimage.Revision + 1);
            try { RecordSourceCreation(tx); }
            catch (Exception exception) { Log.Error($"[Project GU] Could not index unresolved Gu {record.Id}. {exception}"); }
            try { UnbindAll(record); }
            catch (Exception exception) { Log.Error($"[Project GU] Could not unbind unresolved Gu {record.Id}. {exception}"); }
            if (!TryDiscardStaged(tx)) MarkRogue(tx.Staged?.GuTag(), "A failed committed materialization could not discard its Pawn.");
            record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None, reason, tx.Tick);
            return GuSourceOutcome.Unresolved;
        }

        private static bool TryRestoreSourcePreimage(SourceTransaction tx)
        {
            try
            {
                GuTransactionProbe.Reach(GuTransactionPoint.RestorePreimage, tx.Request.EventKey);
                return tx.Source.CohortFor(tx.Request.SpeciesDefName) == tx.Cohort && tx.Cohort.TryRestoreUnpublishedDebit(tx.Preimage);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] The debit of event {tx.Request.EventKey} could not be reversed; the individual stays counted. {exception}");
                return false;
            }
        }

        private static bool TryDiscardStaged(SourceTransaction tx)
        {
            if (tx.Staged == null) return true;
            try { GuTransactionProbe.Reach(GuTransactionPoint.Cleanup, tx.Request.EventKey); }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Cleanup of the provisional Pawn for event {tx.Request.EventKey} was interrupted. {exception}");
                try { tx.Staged.SetReleaseSuppressed(true); }
                catch (Exception suppress) { Log.Warning($"[Project GU] Could not suppress the provisional Pawn. {suppress}"); }
                DetachFailedRetirement(tx.Staged);
                return false;
            }
            return DiscardProvisionalWildPawn(tx.Staged);
        }
    }
}
