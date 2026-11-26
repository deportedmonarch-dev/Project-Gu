using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        // V1 quarantine reasons that describe an ordinary custody mismatch rather than contradictory evidence.
        // Migration re-evaluates these against the live native graph (RFC-0004 §12).
        private static readonly string[] ReevaluatedV1Reasons =
        {
            "The bound Gu projection is no longer live in its expected native custody.",
            "The exact live projection, native holder and revision could not be resolved.",
            "The bound aperture or host is no longer live.",
            "The exact live aperture and host could not be resolved.",
            "The aperture host died.",
            "The bound aperture was removed.",
            "The saved Gu species is unavailable.",
            "Multiple records claim this exact aperture slot.",
            "An aperture-held Gu still has a physical projection."
        };

        private void MigrateLegacyRecords(IReadOnlyCollection<CompGuManifestation> loaded)
        {
            var legacy = legacyV1?.Where(record => record != null).ToArray() ?? Array.Empty<GuIndividualV1>();
            legacyV1 = null;
            LastMigrationSummary = null;
            if (legacy.Length == 0) return;
            if (records.Count > 0)
            {
                Log.Warning($"[Project GU] Both V1 and V2 Gu records are present. Ignoring {legacy.Length} V1 records.");
                return;
            }

            Dictionary<string, Pawn> pawns = legacy.Any(record => record.CustodyKind == GuV1CustodyKind.Aperture) ? PawnsByLoadId() : null;
            int reevaluated = 0, retained = 0;
            foreach (var old in legacy)
            {
                string legacyReason = string.IsNullOrEmpty(old.QuarantineReason) ? null : old.QuarantineReason;
                bool reevaluate = legacyReason != null && ReevaluatedV1Reasons.Any(reason => legacyReason.StartsWith(reason, StringComparison.Ordinal));
                string retainedReason = legacyReason != null && !reevaluate ? $"Retained from Schema V1: {legacyReason}"
                    : old.Version != GuIndividualV1.SchemaVersion ? "Unsupported Gu record version in Schema V1." : null;
                if (reevaluate) reevaluated++;
                if (retainedReason != null) retained++;

                string apertureId = null;
                if (old.CustodyKind == GuV1CustodyKind.Aperture && old.HolderPawnId != null && pawns != null
                    && pawns.TryGetValue(old.HolderPawnId, out var host))
                    apertureId = host.health?.hediffSet?.hediffs.OfType<Hediff_Aperture>()
                        .FirstOrDefault(hediff => hediff.GetUniqueLoadID() == old.NativeOwnerId)?.EnsureApertureId();
                records.Add(GuRecord.FromV1(old, apertureId, legacyReason, retainedReason));
            }
            LastMigrationSummary = $"Migrated {legacy.Length} Schema V1 Gu records: {reevaluated} V1 quarantines re-evaluated, {retained} retained.";
            Log.Message("[Project GU] " + LastMigrationSummary);
        }

        private static string RecordError(GuRecord record)
        {
            if (record.Version != GuRecord.CurrentVersion) return "Unsupported Gu record version.";
            if (!record.IsLiving && record.Existence is not (GuExistence.Terminal or GuExistence.Retired))
                return "Unsupported Gu existence.";
            if (!Guid.TryParseExact(record.Id, "N", out _) || record.CustodyRevision < 1 || record.StateRevision < 1
                || string.IsNullOrWhiteSpace(record.Origin) || string.IsNullOrWhiteSpace(record.OriginId) || record.CreatedTick < 0)
                return "Invalid Gu identity, revision or provenance.";
            if ((record.Origin == GuRecord.CohortOrigin && (string.IsNullOrWhiteSpace(record.CreationEventKey)
                || string.IsNullOrWhiteSpace(record.OriginNativeId) || !record.CohortDebitCommitted))
                || (record.Origin != GuRecord.CohortOrigin && record.CohortDebitCommitted))
                return "The saved cohort materialization has incomplete provenance or debit evidence.";
            // Alpha 6 receipts are additive. A record without one keeps the original spring rules above.
            if (record.CreationEvidence?.ShapeError(record) is { } receiptError) return receiptError;
            if (record.Species is { } species && (record.Rank < species.minRank || record.Rank > species.maxRank))
                return "The saved rank is outside the species range.";
            // A terminal or retired record is a tombstone. Compaction clears its placement fields.
            if (!record.IsLiving) return null;
            return record.Placement switch
            {
                GuPlacementKind.Contained when record.ContainerKind == GuContainerKind.WorldSite
                    => string.IsNullOrWhiteSpace(record.ContainerId) || record.LastKnown?.HasTile != true
                        ? "An abstract world-location Gu has no location." : null,
                GuPlacementKind.Manifested when string.IsNullOrWhiteSpace(record.ProjectionId) => "A manifested Gu has no native object ID.",
                GuPlacementKind.Manifested when (record.Manifestation == GuManifestationKind.Pawn) != (record.Condition == GuCondition.Active)
                    => "The manifestation form contradicts the biological condition.",
                GuPlacementKind.Contained when string.IsNullOrWhiteSpace(record.AnchorHint)
                    || (record.ContainerId == null && record.LegacyApertureLoadId == null) => "A contained Gu has no container identity.",
                GuPlacementKind.Manifested or GuPlacementKind.Contained => null,
                _ => "Unsupported Gu placement."
            };
        }

        private void RebuildBindings(IReadOnlyCollection<CompGuManifestation> loaded)
        {
            byId.Clear();
            byProjection.Clear();
            projectionOf.Clear();
            apertureOf.Clear();
            lastHost.Clear();
            lastProjection.Clear();
            rogues.Clear();
            byCreationEvent.Clear();
            byEndEvent.Clear();
            records.RemoveAll(record => record == null);
            int tick = Find.TickManager?.TicksGame ?? 0;

            // Evidential integrity: contradictions that need outside evidence to repair. Duplicate creation events are
            // checked first so that the shared contradiction, not a derived field error, is the recorded reason.
            RebuildCreationEventBindings(records);
            RebuildEndEventBindings(records);
            foreach (var record in records.Where(record => !record.IsQuarantined))
                if (RecordError(record) is { } error) record.Quarantine(error);
            foreach (var group in records.Where(record => record.Id != null).GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                    foreach (var record in group) record.Quarantine("Multiple records claim this Gu ID.");
                byId[group.Key] = group.First();
            }
            foreach (var group in records.Where(record => !record.IsQuarantined && record.Existence == GuExistence.Living
                    && record.Placement == GuPlacementKind.Manifested)
                .GroupBy(record => record.ProjectionId, StringComparer.Ordinal).Where(group => group.Count() > 1))
                foreach (var record in group) record.Quarantine("Multiple records claim this native object.");

            var tags = loaded.Where(tag => tag.parent != null && !tag.parent.Destroyed && !tag.parent.Discarded)
                .ToLookup(tag => tag.GuId, StringComparer.OrdinalIgnoreCase);
            var claimed = new HashSet<CompGuManifestation>();
            Dictionary<string, Pawn> pawns = null;

            foreach (var record in records)
            {
                foreach (var tag in tags[record.Id ?? ""]) claimed.Add(tag);
                if (record.IsQuarantined) continue;
                if (!record.IsLiving)
                {
                    // An ended individual can never be manifested again, for example by native resurrection of its old Pawn.
                    foreach (var tag in tags[record.Id ?? ""].Where(tag => tag.parent is not Pawn { Dead: true }))
                        MarkRogue(tag, $"The individual has ended ({record.Existence}).");
                    continue;
                }
                // Phase 1 recorded demonstrated death and destruction as evidence on unresolved records. The owner approved
                // explicit outcomes, so those become terminal. Map removal and discards stay unresolved: they never establish death.
                if (record.IsUnresolved && record.TerminalEvidence is GuTerminalEvidence.Killed or GuTerminalEvidence.Destroyed)
                {
                    var outcome = record.Manifestation == GuManifestationKind.Pawn && record.TerminalEvidence == GuTerminalEvidence.Killed
                        ? GuTerminalOutcome.Died : GuTerminalOutcome.Destroyed;
                    record.EndLife(outcome, null, null, null, $"Converted from Phase 1 evidence: {record.UnresolvedDetail}", tick);
                    continue;
                }
                if (record.TerminalEvidence != GuTerminalEvidence.None) continue;
                if (record.Species == null)
                {
                    record.MarkUnresolved(GuUnresolvedCause.SpeciesUnavailable, GuTerminalEvidence.None,
                        $"Species Def {record.SpeciesDefName} is not loaded. All data is retained.", tick);
                    continue;
                }

                if (record.Placement == GuPlacementKind.Manifested)
                {
                    var candidates = tags[record.Id].ToArray();
                    var exact = candidates.Where(tag => tag.parent.GetUniqueLoadID() == record.ProjectionId).ToArray();
                    foreach (var tag in candidates.Except(exact))
                        MarkRogue(tag, "Another native object is the authorized manifestation.");
                    if (exact.Length == 1 && exact[0].GuRevision != record.CustodyRevision)
                    {
                        record.Quarantine("The authorized native object disagrees with the record about its binding revision.");
                        continue;
                    }
                    if (exact.Length == 1 && MatchesManifestation(record, exact[0].parent))
                    {
                        record.ClearUnresolved();
                        BindProjection(record, exact[0].parent);
                        continue;
                    }
                    var evidence = exact.Length == 1 && exact[0].parent is Pawn { Dead: true } ? GuTerminalEvidence.Killed : GuTerminalEvidence.None;
                    record.MarkUnresolved(evidence == GuTerminalEvidence.None ? GuUnresolvedCause.ProjectionMissing : GuUnresolvedCause.ProjectionEnded,
                        evidence, exact.Length == 1 ? "The native object loaded but is not a valid manifestation."
                            : candidates.Length == 0 ? "No native object with this identity loaded."
                            : $"Only unauthorized candidates loaded: {string.Join(", ", candidates.Select(tag => tag.parent.GetUniqueLoadID()))}.", tick);
                    continue;
                }

                foreach (var tag in tags[record.Id]) MarkRogue(tag, "The Gu is held in record-only containment.");
                if (record.ContainerKind == GuContainerKind.WorldSite) continue;
                pawns ??= PawnsByLoadId();
                if (!pawns.TryGetValue(record.AnchorHint, out var host))
                {
                    record.MarkUnresolved(GuUnresolvedCause.HostUnavailable, GuTerminalEvidence.None,
                        $"Aperture host {record.AnchorHint} did not load.", tick);
                    continue;
                }
                lastHost[record.Id] = host;
                if (record.ContainerId == null && record.LegacyApertureLoadId != null
                    && host.health?.hediffSet?.hediffs.OfType<Hediff_Aperture>()
                        .FirstOrDefault(hediff => hediff.GetUniqueLoadID() == record.LegacyApertureLoadId) is { } legacy)
                    record.AdoptMigratedAperture(legacy.EnsureApertureId());
                ApplyApertureState(record, host, tick);
            }

            foreach (var tag in loaded.Where(tag => !claimed.Contains(tag) && tag.parent != null && !tag.parent.Destroyed))
                MarkRogue(tag, string.IsNullOrWhiteSpace(tag.GuId) ? "The saved projection has no Gu identity."
                    : "No canonical record has this identity.");
            ready = true;
        }

        /// <summary>Binds a contained record to its live aperture or marks the reason it cannot be reached.</summary>
        private void ApplyApertureState(GuRecord record, Pawn host, int tick)
        {
            var aperture = FindAperture(host, record.ContainerId);
            if (aperture != null && IsBoundAperture(aperture, record))
            {
                record.ClearUnresolved();
                BindAperture(record, aperture);
                return;
            }
            UnbindAll(record);
            lastHost[record.Id] = host;
            // A dead host with its aperture stays bound above (deceased-host custody). Only a discarded host, or a destroyed
            // living one, is unavailable.
            if (host.Discarded || (host.Destroyed && !host.Dead))
                record.MarkUnresolved(GuUnresolvedCause.HostUnavailable, GuTerminalEvidence.None, $"Aperture host {record.AnchorHint} was removed.", tick);
            else
                record.MarkUnresolved(GuUnresolvedCause.ApertureMissing, GuTerminalEvidence.None,
                    $"Aperture {record.ContainerId ?? record.LegacyApertureLoadId} is not on host {record.AnchorHint}.", tick);
        }
    }
}
