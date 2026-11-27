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
    /// receipt. Order: custody guard (before any source, asset or native call), request, stable source identity, receipt
    /// replay, independent authority (the registered owner's exact binding), live credential, species/rank/stage/trigger/asset
    /// authorization, ownership consistency and owner-side debit evidence, exact cohort revision, placement, native staging
    /// with full revalidation after native callbacks, a callback-free commit window (event ownership, debit, receipt, record,
    /// binding), publication of one projection, then settlement of any spent asset, all inside the same guard.
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

        // ---- Independent source authority -----------------------------------------------------------------------------------

        /// <summary>
        /// The one live source the owning components hold under this identity, or null with the reason. Owners are the game's
        /// and each live map's components that implement <see cref="IGuPopulationSourceDirectory"/>; they read their own
        /// saved collections. A caller-supplied adapter is never consulted here.
        /// </summary>
        private static IGuPopulationSource ResolveSource(string ownerKind, string sourceId, out string refusal)
        {
            var found = new List<IGuPopulationSource>();
            if (Current.Game?.components is { } gameComponents)
                foreach (var component in gameComponents)
                    if (component is IGuPopulationSourceDirectory directory) directory.CollectSources(ownerKind, sourceId, found);
            foreach (var map in Find.Maps)
                foreach (var component in map.components)
                    if (component is IGuPopulationSourceDirectory directory) directory.CollectSources(ownerKind, sourceId, found);
            var bindings = found.Where(source => source?.Binding != null).GroupBy(source => source.Binding).Select(group => group.First()).ToList();
            refusal = bindings.Count == 0 ? "No registered owner holds this population source. A detached or unsaved source cannot issue Gu."
                : bindings.Count > 1 ? "More than one live source claims this identity. Mutation is blocked until ownership is repaired." : null;
            return bindings.Count == 1 ? bindings[0] : null;
        }

        /// <summary>The registered owner's source, when the supplied adapter is exactly its live binding.</summary>
        private static IGuPopulationSource Authority(IGuPopulationSource supplied, string ownerKind, string sourceId, out string refusal)
        {
            var authority = ResolveSource(ownerKind, sourceId, out refusal);
            if (authority == null) return null;
            if (supplied?.Binding == null || !ReferenceEquals(authority.Binding, supplied.Binding))
            {
                refusal = "The supplied population source is not the registered owner of this identity. A detached or fabricated source cannot issue Gu.";
                return null;
            }
            return authority;
        }

        // ---- Owner-side debit evidence ---------------------------------------------------------------------------------------

        /// <summary>
        /// Null when the cohort's own debit evidence agrees exactly with every committed receipt in its scope; otherwise why the
        /// scope is frozen. A revision floor alone cannot tell a committed ledger from an equal- or higher-revision substitute,
        /// so the row carries the count and chained digest of the receipts that debited it, and a seal over its state.
        /// Nothing is credited, deleted or adjusted: an ambiguous scope only refuses new issue until repaired.
        /// </summary>
        private string ScopeEvidenceRefusal(string ownerKind, string sourceId, string species, GuPopulationRecord cohort)
        {
            if (!cohort.EvidenceIntact)
                return "The source cohort's debit evidence was changed outside its owner (substituted or edited). The scope is frozen until repaired.";
            var receipts = records.Where(record => record != null && record.CohortDebitCommitted && record.CreationEvidence is { } receipt
                    && string.Equals(receipt.OwnerKind, ownerKind, StringComparison.Ordinal)
                    && string.Equals(receipt.SourceId, sourceId, StringComparison.Ordinal)
                    && string.Equals(record.SpeciesDefName, species, StringComparison.Ordinal))
                .Select(record => (After: record.CreationEvidence.RevisionAfter, Key: record.CreationEventKey))
                .OrderBy(entry => entry.After).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToList();
            for (int i = 1; i < receipts.Count; i++)
                if (receipts[i].After == receipts[i - 1].After)
                    return $"Two creation receipts claim the same debit (revision {receipts[i].After}) of this source cohort. The scope is frozen until repaired.";
            if (receipts.Count == 0)
                return cohort.ReceiptDebits == 0 ? null
                    : "The source cohort records receipt debits that no canonical record claims. The scope is frozen until repaired.";
            long digest = GuPopulationRecord.DigestSeed;
            foreach (var (after, key) in receipts) digest = GuPopulationRecord.ChainDigest(digest, after, key);
            return cohort.ReceiptDebits == receipts.Count && cohort.ReceiptDigest == digest ? null
                : $"The source cohort does not carry the debits of its {receipts.Count} committed creation receipt(s) "
                    + $"(it records {cohort.ReceiptDebits}). It was regenerated, substituted or restored out of order, so the scope is frozen until repaired.";
        }

        internal bool SurfaceEvidenceIntact(string sourceId, GuPopulationRecord cohort) =>
            ScopeEvidenceRefusal(GuSurfaceEcology.SourceKind, sourceId, GuSurfaceEcology.BlackBoar, cohort) == null;

        /// <summary>
        /// After both the registry and the source owners have loaded, reports every receipt scope whose live cohort disagrees with
        /// its receipts. Read-only: the same check refuses new issue from a frozen scope at the next transaction. A scope whose
        /// owner is not live (an unloaded map) is checked when it next issues.
        /// </summary>
        private void ReconcileSourceEvidence()
        {
            foreach (var scope in records.Where(record => record != null && record.CohortDebitCommitted && record.CreationEvidence != null)
                .GroupBy(record => (record.CreationEvidence.OwnerKind, record.CreationEvidence.SourceId, record.SpeciesDefName)))
            {
                try
                {
                    if (ResolveSource(scope.Key.OwnerKind, scope.Key.SourceId, out _) is not { } source
                        || source.CohortFor(scope.Key.SpeciesDefName) is not { } cohort) continue;
                    if (ScopeEvidenceRefusal(scope.Key.OwnerKind, scope.Key.SourceId, scope.Key.SpeciesDefName, cohort) is { } refusal)
                        SafeWarning($"[Project GU] Source {scope.Key.OwnerKind}:{scope.Key.SourceId}, {scope.Key.SpeciesDefName}: {refusal} "
                            + "Counts and individuals were left unchanged.");
                }
                catch (Exception exception) { SafeWarning($"[Project GU] Could not reconcile source evidence for {scope.Key.SourceId}. {exception.Message}"); }
            }
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

        /// <summary>A player encounter with an explicit trigger and the exact asset it spends, for example a lure's bait.</summary>
        internal bool TryEncounterWildCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, string trigger,
            IGuSourceAsset asset, Map map, IntVec3 cell, out GuRecord individual, out string reason) =>
            TryMaterializeSpringCohort(habitat, request, trigger, asset, map, cell, out individual, out reason);

        private bool TryMaterializeSpringCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, string trigger,
            IGuSourceAsset asset, Map map, IntVec3 cell, out GuRecord individual, out string reason)
        {
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(request.SpeciesDefName ?? "");
            var outcome = TryMaterializeFromSource(ForSpring(habitat),
                new GuSourceRequest(request.EventKey, SpringPopulationSource.Kind, request.HabitatId, request.SourceNativeId,
                    request.SpeciesDefName, species?.minRank ?? 0, GuCohortStage.Mature, trigger, asset?.Scope, request.ExpectedRevision),
                map, cell, out individual, out reason, asset);
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
            internal IGuSourceAsset Asset;
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
        /// <paramref name="asset"/> is the exact native asset the creation spends, settled once after the debit commits.
        /// </summary>
        internal GuSourceOutcome TryMaterializeFromSource(IGuPopulationSource source, GuSourceRequest request, Map map, IntVec3 cell,
            out GuRecord individual, out string reason, IGuSourceAsset asset = null)
        {
            individual = null;
            reason = "The native cohort materialization safeguards are unavailable for this game configuration.";
            // The guard is acquired before any source, asset or native call: a callback cannot re-enter preflight, save, or
            // observe a half-checked request. It also refuses saves and makes registry queries return nothing.
            if (source == null || !CanUseCustody) return GuSourceOutcome.Refused;
            CustodyTransitionInProgress = true;
            try { return MaterializeGuarded(source, request, map, cell, asset, out individual, ref reason); }
            finally { CustodyTransitionInProgress = false; }
        }

        private GuSourceOutcome MaterializeGuarded(IGuPopulationSource source, GuSourceRequest request, Map map, IntVec3 cell,
            IGuSourceAsset asset, out GuRecord individual, ref string reason)
        {
            individual = null;
            SourceTransaction tx;
            LifeStageAge adult;
            try
            {
                if (!Preflight(source, request, map, cell, asset, out tx, out adult, out individual, out var early, ref reason)) return early;
            }
            catch (Exception exception)
            {
                // Nothing was staged, debited or indexed before staging begins.
                individual = null;
                reason = "A source or native callback failed while the request was checked. Nothing changed.";
                SafeWarning($"[Project GU] {reason} Event {request.EventKey}. {exception}");
                return GuSourceOutcome.Refused;
            }

            GuSourceOutcome outcome;
            bool committed = false;
            reason = "Native placement or the exact cohort changed during materialization.";
            try { committed = StageAndCommitSource(tx, adult); }
            catch (Exception exception)
            {
                reason = "A native callback interrupted cohort materialization.";
                SafeWarning($"[Project GU] {reason} Event {request.EventKey}. {exception}");
            }
            if (committed)
            {
                individual = tx.Record;
                reason = $"Materialized Gu {tx.Record.Id}.";
                outcome = GuSourceOutcome.Committed;
            }
            else outcome = RecoverFailedSource(tx, ref reason, out individual);
            // The debit stands for a commit or an unresolved claim, so the spent asset is settled once, still inside the guard.
            if (tx.Asset != null && (outcome is GuSourceOutcome.Committed or GuSourceOutcome.Unresolved) && individual != null)
                SettleAsset(individual, tx.Asset, ref reason);
            return outcome;
        }

        private bool Preflight(IGuPopulationSource source, GuSourceRequest request, Map map, IntVec3 cell, IGuSourceAsset asset,
            out SourceTransaction tx, out LifeStageAge adult, out GuRecord individual, out GuSourceOutcome early, ref string reason)
        {
            tx = null;
            adult = null;
            individual = null;
            early = GuSourceOutcome.Refused;
            GuTransactionProbe.Reach(GuTransactionPoint.Preflight, request.EventKey);

            request = request with { AssetScope = GuCreationEvidence.NormalizeScope(request.AssetScope) };
            reason = "The materialization request has no exact source, species, trigger or event.";
            if (string.IsNullOrWhiteSpace(request.EventKey) || string.IsNullOrWhiteSpace(request.OwnerKind)
                || string.IsNullOrWhiteSpace(request.SourceId) || string.IsNullOrWhiteSpace(request.SpeciesDefName)
                || string.IsNullOrWhiteSpace(request.Trigger) || request.ExpectedRevision < 0) return false;
            reason = "The request names a different population source.";
            if (!string.Equals(source.OwnerKind, request.OwnerKind, StringComparison.Ordinal)
                || !string.Equals(source.SourceId, request.SourceId, StringComparison.Ordinal)) return false;

            // A committed event replays by its receipt before any revision or credential test, so a stale request still
            // returns the same individual with zero effect.
            if (byCreationEvent.TryGetValue(request.EventKey, out var previous))
            {
                early = Replay(previous, source, request, asset, out individual, ref reason);
                return false;
            }

            var authority = Authority(source, request.OwnerKind, request.SourceId, out reason);
            if (authority == null) return false;
            bool development = authority.RequiresDevelopmentMode(request.Trigger);
            reason = "The native cohort materialization safeguards are unavailable for this game configuration.";
            if (development && !Prefs.DevMode) return false;

            reason = "The population source's native credential is stale or unavailable. Reacquire it from the live source.";
            string credential = authority.CurrentCredential;
            if (string.IsNullOrWhiteSpace(credential) || !string.Equals(credential, request.Credential, StringComparison.Ordinal)) return false;

            reason = "This species has no eligible wild cohort projection.";
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(request.SpeciesDefName);
            if (species?.wildPopulationEnabled != true
                || !development && species.capabilityTags?.Contains("development_test") == true
                || !Thing_GuDormantProjection.IsConfigured(species.dormantProjectionDef)
                || FirstAdultProjectionStage(species.wildProjectionKind) is not { } stage)
                return false;
            adult = stage;
            // Slice 0 contract: a source issues a mature individual at the species' wild (minimum) rank only. The canonical
            // record is created at that rank, so the record, the receipt and the authorization always agree.
            reason = "The species cannot be issued at the requested rank or life stage.";
            if (request.Stage != GuCohortStage.Mature || request.Rank != species.minRank) return false;
            reason = "The encounter's asset does not match its request.";
            if (!string.Equals(GuCreationEvidence.NormalizeScope(asset?.Scope), request.AssetScope, StringComparison.Ordinal)
                || asset != null && string.IsNullOrWhiteSpace(asset.AssetId)) return false;
            string refusal = authority.RefuseIssue(species, request.Rank, request.Stage, request.Trigger, asset) ?? asset?.Refusal();
            if (refusal != null)
            {
                reason = refusal;
                return false;
            }

            reason = "More than one live source claims this identity. Mutation is blocked until ownership is repaired.";
            if (!authority.HasUniqueIdentity) return false;
            reason = "The exact source cohort is unavailable.";
            if (authority.CohortFor(request.SpeciesDefName) is not { } cohort || cohort.Revision == long.MaxValue
                || !string.Equals(cohort.SpeciesDefName, request.SpeciesDefName, StringComparison.Ordinal)) return false;
            string floorKey = FloorKey(request.OwnerKind, request.SourceId, request.SpeciesDefName);
            bool hadFloor = committedRevisionFloor.TryGetValue(floorKey, out long floor);
            if (hadFloor && cohort.Revision < floor)
            {
                reason = $"The source cohort (revision {cohort.Revision}) is older than its committed creation receipts (revision {floor}). "
                    + "Its ownership is inconsistent, so it cannot issue Gu until repaired.";
                SafeWarningOnce($"[Project GU] {reason} Source {request.OwnerKind}:{request.SourceId}, {request.SpeciesDefName}.", floorKey.GetHashCode());
                return false;
            }
            if (ScopeEvidenceRefusal(request.OwnerKind, request.SourceId, request.SpeciesDefName, cohort) is { } frozen)
            {
                reason = frozen;
                SafeWarningOnce($"[Project GU] {reason} Source {request.OwnerKind}:{request.SourceId}, {request.SpeciesDefName}.", ("evidence" + floorKey).GetHashCode());
                return false;
            }
            reason = "The source cohort changed since this request was prepared.";
            if (cohort.Revision != request.ExpectedRevision) return false;
            reason = "This cohort has no mature Gu available.";
            if (cohort.MatureCount < 1) return false;

            tx = new SourceTransaction
            {
                Source = authority, Asset = asset, Request = request, Species = species, Cohort = cohort, Preimage = cohort.Preimage,
                FloorKey = floorKey, HadFloor = hadFloor, PreviousFloor = floor, Map = map, Cell = cell,
                Id = Guid.NewGuid().ToString("N"), Tick = Find.TickManager.TicksGame
            };
            reason = authority.PlacementRefusal ?? "Choose an open, unreserved cell that this source permits on a live map.";
            if (map == null || byId.ContainsKey(tx.Id) || !SourceReady(tx, null, out _)) return false;
            return true;
        }

        private GuSourceOutcome Replay(GuRecord previous, IGuPopulationSource source, GuSourceRequest request, IGuSourceAsset asset,
            out GuRecord individual, ref string reason)
        {
            individual = null;
            reason = ReceiptRefusal(previous, request, out bool conflict);
            if (reason != null) return conflict ? GuSourceOutcome.Conflict : GuSourceOutcome.Refused;
            if (previous.CreationEvidence == null)
            {
                // Records created before Alpha 6 keep their original rule: the exact live spring anchor, not only the saved ID.
                var authority = Authority(source, request.OwnerKind, request.SourceId, out _);
                if (authority == null || !string.Equals(authority.CurrentCredential, request.Credential, StringComparison.Ordinal))
                {
                    reason = "This historical event can replay only from its exact live spring anchor.";
                    return GuSourceOutcome.Refused;
                }
            }
            individual = previous;
            reason = $"This event already materialized Gu {previous.Id}.";
            // A creation whose asset was not observed spent retries the same exact asset once, never a different one.
            if (asset != null && previous.CreationEvidence is { Settlement: GuAssetSettlement.Pending or GuAssetSettlement.Interrupted } receipt
                && string.Equals(receipt.AssetId, asset.AssetId, StringComparison.Ordinal))
                SettleAsset(previous, asset, ref reason);
            return GuSourceOutcome.Replayed;
        }

        private bool StageAndCommitSource(SourceTransaction tx, LifeStageAge adult)
        {
            string key = tx.Request.EventKey;
            var kind = tx.Species.wildProjectionKind;
            GuTransactionProbe.Reach(GuTransactionPoint.BeforeStaging, key);
            tx.Staged = PrepareWildPawn(kind, adult, tx.Map);
            tx.Record = GuRecord.PrepareFromSource(tx.Id, tx.Species, tx.Request, tx.Tick, tx.Staged.GetUniqueLoadID(), tx.Asset?.AssetId);
            tx.Staged.InitializeBinding(tx.Record);
            // Native SpawnSetup and modded callbacks run inside TryPlaceStagedWildPawn. The source, its authority, policy and
            // evidence, and the asset are revalidated afterwards.
            if (!CanRestoreDetached(tx.Staged) || !CanRetire(tx.Staged) || !RevalidateSource(tx, null)
                || !TryPlaceStagedWildPawn(tx.Staged, tx.Record, kind, tx.Map, tx.Cell) || !RevalidateSource(tx, tx.Staged))
                return false;
            GuTransactionProbe.Reach(GuTransactionPoint.AfterStagingBeforeDebit, key);

            // Callback-free commit window: event ownership, debit, receipt, canonical record and binding. No native code runs
            // until publication.
            if (byCreationEvent.ContainsKey(key)) return false;
            if (!tx.Cohort.TryDebitForReceipt(key)) return false;
            tx.Debited = true;
            GuTransactionProbe.Reach(GuTransactionPoint.AfterDebit, key);
            tx.Record.CommitSourceDebit(tx.Preimage.Revision + 1, tx.Asset != null ? GuAssetSettlement.Pending : GuAssetSettlement.None);
            if (!TryRecordSourceCreation(tx.Record, tx.FloorKey, tx.Preimage.Revision + 1))
                throw new InvalidOperationException($"Event {key} gained another claim inside the commit window.");
            BindProjection(tx.Record, tx.Staged);
            GuTransactionProbe.Reach(GuTransactionPoint.BeforePublication, key);

            tx.Staged.SetReleaseSuppressed(false);
            tx.Published = true;
            return MatchesManifestation(tx.Record, tx.Staged) && IsOnCell(tx.Staged, tx.Map, tx.Cell);
        }

        private bool SourceReady(SourceTransaction tx, Pawn_GuWildProjection staged, out string refusal)
        {
            var request = tx.Request;
            refusal = null;
            // Authority is resolved again from the owners: the binding must be the same live owner object.
            if (ResolveSource(request.OwnerKind, request.SourceId, out refusal) is not { } owner || !ReferenceEquals(owner.Binding, tx.Source.Binding))
                return false;
            var source = tx.Source;
            if (!string.Equals(source.CurrentCredential, request.Credential, StringComparison.Ordinal)
                || !string.Equals(source.OwnerKind, request.OwnerKind, StringComparison.Ordinal)
                || !string.Equals(source.SourceId, request.SourceId, StringComparison.Ordinal) || !source.HasUniqueIdentity
                || source.CohortFor(request.SpeciesDefName) != tx.Cohort || tx.Cohort.Revision != request.ExpectedRevision
                || !string.Equals(tx.Cohort.SpeciesDefName, request.SpeciesDefName, StringComparison.Ordinal)
                || tx.Cohort.MatureCount < 1 || byCreationEvent.ContainsKey(request.EventKey)) return false;
            if ((refusal = source.RefuseIssue(tx.Species, request.Rank, request.Stage, request.Trigger, tx.Asset)) != null) return false;
            if (tx.Asset != null && (refusal = tx.Asset.Refusal()) != null) return false;
            if ((refusal = ScopeEvidenceRefusal(request.OwnerKind, request.SourceId, request.SpeciesDefName, tx.Cohort)) != null) return false;
            return CanPlaceDormant(tx.Map, tx.Cell, staged) && !tx.Map.reservationManager.IsReserved(tx.Cell)
                && source.PlacementReady(tx.Map, tx.Cell, request.Trigger);
        }

        private bool RevalidateSource(SourceTransaction tx, Pawn_GuWildProjection staged)
        {
            GuTransactionProbe.Reach(GuTransactionPoint.SourceRevalidation, tx.Request.EventKey);
            return SourceReady(tx, staged, out _);
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

        /// <summary>Indexes a committed creation. Refuses, changing nothing, when another record already claims its event key.</summary>
        private bool TryRecordSourceCreation(GuRecord record, string floorKey, long revisionAfter)
        {
            if (byCreationEvent.TryGetValue(record.CreationEventKey, out var claim) && claim != record) return false;
            if (!byId.ContainsKey(record.Id)) records.Add(record);
            byId[record.Id] = record;
            byCreationEvent[record.CreationEventKey] = record;
            RaiseCommittedFloor(floorKey, revisionAfter);
            return true;
        }

        private void UnrecordSourceCreation(SourceTransaction tx)
        {
            var record = tx.Record;
            UnbindAll(record);
            ForgetRecoveryHint(record, tx.Staged);
            records.Remove(record);
            if (byId.TryGetValue(record.Id, out var known) && known == record) byId.Remove(record.Id);
            if (byCreationEvent.TryGetValue(record.CreationEventKey, out var claim) && claim == record)
                byCreationEvent.Remove(record.CreationEventKey);
            if (tx.HadFloor) committedRevisionFloor[tx.FloorKey] = tx.PreviousFloor;
            else committedRevisionFloor.Remove(tx.FloorKey);
        }

        // A failed creation's provisional Pawn is never a recovery target, so no hint may keep its native graph alive.
        private void ForgetRecoveryHint(GuRecord record, Thing staged)
        {
            if (record?.Id != null && lastProjection.TryGetValue(record.Id, out var hint) && (staged == null || hint == staged))
                lastProjection.Remove(record.Id);
        }

        // ---- Recovery --------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Before a debit, or after a debit whose preimage could be restored before publication: discard the shell and spend
        /// nothing; if the shell cannot be proven gone, quarantine the event with no debit. Otherwise keep exactly one counted
        /// unresolved individual with its receipt. The managed outcome (the durable claim and its state) is always published
        /// before any fallible native cleanup or diagnostic, and no native failure can escape before it is recorded.
        /// Never grants stock, deletes unrelated Things or adjusts counts silently.
        /// </summary>
        private GuSourceOutcome RecoverFailedSource(SourceTransaction tx, ref string reason, out GuRecord individual)
        {
            individual = null;
            if (tx.Debited && !tx.Published && TryRestoreSourcePreimage(tx))
            {
                // The unit is back in the cohort, so from here the record must never count it a second time.
                tx.Debited = false;
                try
                {
                    tx.Record.RevertUnpublishedDebit();
                    UnrecordSourceCreation(tx);
                    reason += " The debit was reversed before publication.";
                }
                catch (Exception exception)
                {
                    // Managed-only unwinding failed: the cohort is restored, so the claim must not count. Block the key instead.
                    SafeWarning($"[Project GU] Could not unwind the restored creation of event {tx.Request.EventKey}. {exception.Message}");
                }
            }
            if (tx.Debited) return KeepUnresolvedClaim(tx, ref reason, out individual);

            if (TryDiscardStaged(tx))
            {
                reason += " No Gu was spent and the event may be retried.";
                return GuSourceOutcome.RolledBack;
            }
            reason = "Provisional Pawn cleanup could not be verified. The event was quarantined without a cohort debit.";
            QuarantineUndebitedEvent(tx, reason);
            SafeMarkRogue(tx.Staged, reason);
            return GuSourceOutcome.Quarantined;
        }

        /// <summary>Managed only: one quarantined, undebited claim durably owns the event key, unless a committed claim already does.</summary>
        private void QuarantineUndebitedEvent(SourceTransaction tx, string reason)
        {
            string key = tx.Request.EventKey;
            var claim = tx.Record ?? GuRecord.PrepareFromSource(tx.Id, tx.Species, tx.Request, tx.Tick, "unplaced", tx.Asset?.AssetId);
            if (claim.CohortDebitCommitted) claim.RevertUnpublishedDebit();
            claim.Quarantine(reason);
            UnbindAll(claim);
            ForgetRecoveryHint(claim, null);
            // The key already belongs to another record: it is blocked by that owner, and a second claimant here would make the
            // next load quarantine the legitimate owner as a duplicate. Keep only the rogue shell evidence.
            if (byCreationEvent.TryGetValue(key, out var owner) && owner != null && owner != claim) return;
            if (!byId.ContainsKey(claim.Id)) records.Add(claim);
            byId[claim.Id] = claim;
            byCreationEvent[key] = claim;
        }

        private GuSourceOutcome KeepUnresolvedClaim(SourceTransaction tx, ref string reason, out GuRecord individual)
        {
            var record = tx.Record;
            individual = record;
            reason = "The cohort debit committed but the native Pawn binding failed. The individual is unresolved and still counted.";
            // Managed state first: the counted claim, its index, the unbinding and the unresolved overlay. Only then native cleanup.
            if (!record.CohortDebitCommitted)
                record.CommitSourceDebit(tx.Preimage.Revision + 1, tx.Asset != null ? GuAssetSettlement.Pending : GuAssetSettlement.None);
            if (!TryRecordSourceCreation(record, tx.FloorKey, tx.Preimage.Revision + 1))
            {
                // Another record claims the key: index this counted individual and freeze the event as ambiguous, as a load would.
                if (!byId.ContainsKey(record.Id)) records.Add(record);
                byId[record.Id] = record;
                if (byCreationEvent.TryGetValue(record.CreationEventKey, out var other)) other?.Quarantine("Multiple individuals claim this creation event.");
                record.Quarantine("Multiple individuals claim this creation event.");
                byCreationEvent[record.CreationEventKey] = null;
                RaiseCommittedFloor(tx.FloorKey, tx.Preimage.Revision + 1);
            }
            UnbindAll(record);
            ForgetRecoveryHint(record, tx.Staged);
            record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None, reason, tx.Tick);
            if (!TryDiscardStaged(tx)) SafeMarkRogue(tx.Staged, "A failed committed materialization could not discard its Pawn.");
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
                SafeWarning($"[Project GU] The debit of event {tx.Request.EventKey} could not be reversed; the individual stays counted. {exception.Message}");
                return false;
            }
        }

        private bool TryDiscardStaged(SourceTransaction tx)
        {
            if (tx.Staged == null) return true;
            try
            {
                GuTransactionProbe.Reach(GuTransactionPoint.Cleanup, tx.Request.EventKey);
                return DiscardProvisionalWildPawn(tx.Staged);
            }
            catch (Exception exception)
            {
                SafeWarning($"[Project GU] Cleanup of the provisional Pawn for event {tx.Request.EventKey} was interrupted. {exception.Message}");
                try { tx.Staged.SetReleaseSuppressed(true); }
                catch (Exception suppress) { SafeWarning($"[Project GU] Could not suppress the provisional Pawn. {suppress.Message}"); }
                try { DetachFailedRetirement(tx.Staged); }
                catch (Exception detach) { SafeWarning($"[Project GU] Could not detach the provisional Pawn. {detach.Message}"); }
                return false;
            }
        }

        private void SafeMarkRogue(Pawn_GuWildProjection staged, string reason)
        {
            try { MarkRogue(staged?.GuTag(), reason); }
            catch (Exception exception) { SafeWarning($"[Project GU] Could not mark a provisional Pawn rogue. {exception.Message}"); }
        }

        // ---- Asset settlement ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Spends the committed creation's exact asset once and records the observed outcome on its receipt. Inside the custody
        /// guard, so no save can capture a half-settled state. A failed spend is a durable <see cref="GuAssetSettlement.Interrupted"/>
        /// outcome that a replay of the same intent retries with the same asset.
        /// </summary>
        private static void SettleAsset(GuRecord record, IGuSourceAsset asset, ref string reason)
        {
            if (record.CreationEvidence is not { Settlement: GuAssetSettlement.Pending or GuAssetSettlement.Interrupted } receipt
                || !string.Equals(receipt.AssetId, asset.AssetId, StringComparison.Ordinal)) return;
            bool settled = false;
            try
            {
                GuTransactionProbe.Reach(GuTransactionPoint.AssetSettlement, record.CreationEventKey);
                settled = asset.SettlementRefusal() == null && asset.TrySettle();
            }
            catch (Exception exception)
            {
                SafeWarning($"[Project GU] Spending the asset of event {record.CreationEventKey} was interrupted. {exception.Message}");
            }
            receipt.MarkSettlement(settled ? GuAssetSettlement.Settled : GuAssetSettlement.Interrupted);
            if (!settled) reason += " The lure could not be used up; the encounter keeps a record of the unspent bait.";
        }

        // ---- Diagnostics that can never interrupt recovery -------------------------------------------------------------------

        private static void SafeWarning(string text)
        {
            try { Log.Warning(text); }
            catch { }
        }

        private static void SafeWarningOnce(string text, int key)
        {
            try { Log.WarningOnce(text, key); }
            catch { }
        }
    }
}
