using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        private readonly Dictionary<string, GuIndividual> byCreationEvent = new(StringComparer.Ordinal);

        private void RebuildCreationEventBindings(IEnumerable<GuIndividual> records)
        {
            foreach (var claims in records.Where(record => !string.IsNullOrWhiteSpace(record.CreationEventKey))
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
        }

        internal static bool TryPrepareMaterializationRequest(IGuCohortHabitat habitat, string speciesDefName, string eventKey,
            out GuCohortMaterializationRequest request)
        {
            request = default;
            if (string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(habitat?.HabitatId)
                || habitat.Anchor is not { } source || habitat.CohortFor(speciesDefName) is not { } cohort) return false;
            request = new(eventKey, habitat.HabitatId, source.GetUniqueLoadID(), cohort.SpeciesDefName, cohort.Revision);
            return true;
        }

        internal bool TryMaterializeCohort(IGuCohortHabitat habitat, GuCohortMaterializationRequest request, Map map, IntVec3 cell,
            out GuIndividual individual, out string reason)
        {
            individual = null;
            reason = "The native cohort materialization safeguards are unavailable for this game configuration.";
            if (!CanUseDevelopmentCustody || ModsConfig.AnomalyActive) return false;
            reason = "The cohort materialization request has no exact habitat or event.";
            if (string.IsNullOrWhiteSpace(request.EventKey) || string.IsNullOrWhiteSpace(request.HabitatId)
                || string.IsNullOrWhiteSpace(request.SpeciesDefName) || request.ExpectedRevision < 0
                || habitat?.Anchor is not { } anchor || habitat.HabitatId != request.HabitatId
                || anchor.GetUniqueLoadID() != request.SourceNativeId) return false;

            if (byCreationEvent.TryGetValue(request.EventKey, out var previous))
            {
                reason = "This event has an ambiguous or quarantined claim, or belongs to a different source.";
                if (previous == null || previous.IsQuarantined || !previous.CohortDebitCommitted
                    || previous.OriginId != request.HabitatId || previous.OriginNativeId != request.SourceNativeId
                    || previous.SpeciesDefName != request.SpeciesDefName) return false;
                individual = previous;
                reason = $"This event already materialized Gu {previous.Id}.";
                return true;
            }

            reason = "This species has no eligible wild cohort projection.";
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(request.SpeciesDefName);
            if (species?.wildPopulationEnabled != true || FirstAdultProjectionStage(species.wildProjectionKind) is not { } adult)
                return false;
            reason = "The exact habitat cohort or revision is unavailable.";
            if (habitat.CohortFor(request.SpeciesDefName) is not { } cohort
                || cohort.Revision != request.ExpectedRevision || cohort.Revision == long.MaxValue) return false;
            reason = "This cohort has no mature Gu available.";
            if (cohort.MatureCount < 1) return false;

            var underground = Current.Game.GetComponent<GuPocketProbeComponent>();
            bool SourceAndPlacementReady(Pawn_GuWildProjection staged = null) =>
                IsMapOwned(anchor) && anchor.Map == map && habitat.Anchor == anchor && habitat.HabitatId == request.HabitatId
                && anchor.GetUniqueLoadID() == request.SourceNativeId && habitat.HasUniqueIdentity
                && CanPlaceDormant(map, cell, staged) && anchor.Position.DistanceToSquared(cell) <= 36
                && !map.reservationManager.IsReserved(cell) && underground?.HasProtectedProjectionRoute(map) == true
                && habitat.CohortFor(request.SpeciesDefName) == cohort && cohort.Revision == request.ExpectedRevision
                && cohort.MatureCount > 0;

            reason = "Choose an open cell within six cells of the exact habitat on a protected map.";
            if (!SourceAndPlacementReady()) return false;

            string id = Guid.NewGuid().ToString("N");
            if (individuals.Any(entry => string.Equals(entry?.Id, id, StringComparison.OrdinalIgnoreCase))) return false;
            int tick = Find.TickManager.TicksGame;
            Pawn_GuWildProjection prepared = null;
            GuIndividual record = null;
            bool debited = false;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "Native placement or the exact cohort changed during materialization.";
                prepared = PrepareWildPawn(species.wildProjectionKind, adult, map);
                record = GuIndividual.PrepareFromCohort(id, species, request, tick, prepared.GetUniqueLoadID());
                prepared.InitializeBinding(record);
                if (!CanRestoreDetached(prepared) || !CanRetire(prepared) || !SourceAndPlacementReady()
                    || !TryPlaceStagedWildPawn(prepared, record, species.wildProjectionKind, map, cell)
                    || !SourceAndPlacementReady(prepared)) return false;

                // The ledger debit, canonical record and event binding commit without native callbacks.
                if (!cohort.TryConsumeMature(1)) return false;
                debited = true;
                record.MarkCohortDebitCommitted();
                individuals.Add(record);
                byCreationEvent.Add(request.EventKey, record);
                Bind(record, prepared);
                prepared.SetReleaseSuppressed(false);
                if (!MatchesProjection(record, prepared) || !IsOnCell(prepared, map, cell)) return false;

                individual = record;
                reason = $"Materialized Gu {record.Id}.";
                return true;
            }
            catch (Exception exception)
            {
                reason = "A native callback interrupted cohort materialization.";
                Log.Warning($"[Project GU] {reason} {exception}");
                return false;
            }
            finally
            {
                try
                {
                    if (individual == null)
                    {
                        if (debited)
                        {
                            reason = "The cohort debit committed but the native Pawn binding failed. The event was quarantined.";
                            record.Quarantine(reason);
                            Unbind(record, prepared);
                            DiscardProvisionalWildPawn(prepared);
                        }
                        else if (!DiscardProvisionalWildPawn(prepared))
                        {
                            reason = "Provisional Pawn cleanup could not be verified. The event was quarantined without a cohort debit.";
                            record ??= GuIndividual.PrepareFromCohort(id, species, request, tick, null);
                            record.Quarantine(reason);
                            individuals.Add(record);
                            byCreationEvent.Add(request.EventKey, record);
                        }
                    }
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }
    }
}
