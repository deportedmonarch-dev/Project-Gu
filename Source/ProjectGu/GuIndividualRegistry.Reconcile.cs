using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Reconciliation (RFC-0004 §5.4, §15.2). Native hooks enqueue events. A scheduled sweep checks a bounded slice of
    /// records. Both apply changes outside native callbacks and never depend on the player opening a UI.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        internal const int SweepInterval = 250;
        internal const int SweepBudget = 512;

        private readonly Queue<PendingEvent> pending = new();
        private int sweepCursor;

        private readonly record struct PendingEvent(Thing Projection, Hediff_Aperture Aperture, GuUnresolvedCause Cause,
            GuTerminalEvidence Evidence, string Detail);

        internal int PendingEventCount => pending.Count;

        internal void EnqueueProjectionEvent(Thing projection, GuTerminalEvidence evidence)
        {
            if (projection != null)
                pending.Enqueue(new(projection, null, GuUnresolvedCause.ProjectionEnded, evidence,
                    $"Native object {projection.GetUniqueLoadID()} reported {evidence} on {projection.MapHeld?.GetUniqueLoadID() ?? "no map"}."));
        }

        internal void EnqueueApertureEvent(Hediff_Aperture aperture, GuUnresolvedCause cause, string detail)
        {
            if (aperture != null) pending.Enqueue(new(null, aperture, cause, GuTerminalEvidence.None, detail));
        }

        public override void GameComponentTick()
        {
            if (!ready || CustodyTransitionInProgress) return;
            if (pending.Count > 0) ApplyPendingEvents();
            if (Find.TickManager.TicksGame % SweepInterval == 0) Sweep(SweepBudget);
        }

        /// <summary>Applies queued native events. Exposed for fixtures that need a deterministic reconciliation point.</summary>
        internal void ApplyPendingEvents()
        {
            if (!ready || CustodyTransitionInProgress) return;
            int tick = Find.TickManager.TicksGame;
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                if (item.Projection != null)
                {
                    // Only the authorized manifestation affects the record. Retired or rogue objects are ignored.
                    if (!byProjection.TryGetValue(item.Projection, out var record) || record.Existence != GuExistence.Living) continue;
                    UnbindAll(record);
                    record.MarkUnresolved(GuUnresolvedCause.ProjectionEnded, item.Evidence, item.Detail, tick);
                    continue;
                }
                foreach (var record in apertureOf.Where(binding => binding.Value == item.Aperture).Select(binding => byId[binding.Key]).ToArray())
                {
                    UnbindAll(record);
                    lastHost[record.Id] = item.Aperture.pawn;
                    record.MarkUnresolved(item.Cause, GuTerminalEvidence.None, item.Detail, tick);
                }
            }
        }

        /// <summary>Checks up to <paramref name="budget"/> living records and repairs indexes or recovers unresolved ones.</summary>
        internal void Sweep(int budget)
        {
            if (!ready || CustodyTransitionInProgress || records.Count == 0) return;
            int tick = Find.TickManager.TicksGame;
            int count = System.Math.Min(budget, records.Count);
            for (int step = 0; step < count; step++)
            {
                if (sweepCursor >= records.Count) sweepCursor = 0;
                var record = records[sweepCursor++];
                if (record == null || record.IsQuarantined || record.Existence != GuExistence.Living || record.Id == null) continue;
                if (record.Placement == GuPlacementKind.Manifested) SweepManifested(record, tick);
                else SweepContained(record, tick);
            }
        }

        private void SweepManifested(GuRecord record, int tick)
        {
            if (!record.IsUnresolved && projectionOf.TryGetValue(record.Id, out var thing))
            {
                if (MatchesManifestation(record, thing)) return;
                var evidence = thing.Discarded ? GuTerminalEvidence.Discarded
                    : thing.Destroyed ? GuTerminalEvidence.Destroyed
                    : thing is Pawn { Dead: true } ? GuTerminalEvidence.Killed : GuTerminalEvidence.None;
                UnbindAll(record);
                record.MarkUnresolved(evidence == GuTerminalEvidence.None ? GuUnresolvedCause.ProjectionMissing : GuUnresolvedCause.ProjectionEnded,
                    evidence, $"Native object {record.ProjectionId} is no longer a valid manifestation.", tick);
                return;
            }
            // Recovery is limited to the same native object that was last authorized.
            if (record.IsUnresolved && record.TerminalEvidence == GuTerminalEvidence.None
                && record.Unresolved == GuUnresolvedCause.ProjectionMissing
                && lastProjection.TryGetValue(record.Id, out var last) && MatchesManifestation(record, last))
            {
                record.ClearUnresolved();
                BindProjection(record, last);
            }
        }

        private void SweepContained(GuRecord record, int tick)
        {
            if (!record.IsUnresolved)
            {
                if (apertureOf.TryGetValue(record.Id, out var aperture) && IsLiveAperture(aperture, record)) return;
                var host = aperture?.pawn ?? (lastHost.TryGetValue(record.Id, out var known) ? known : null);
                if (host != null) ApplyApertureState(record, host, tick);
                else
                {
                    UnbindAll(record);
                    record.MarkUnresolved(GuUnresolvedCause.HostUnavailable, GuTerminalEvidence.None,
                        $"Aperture host {record.AnchorHint} is not available.", tick);
                }
                return;
            }
            // Resurrection, aperture restoration or a returning host recover the same containment.
            if (record.Unresolved is GuUnresolvedCause.HostDead or GuUnresolvedCause.HostUnavailable or GuUnresolvedCause.ApertureMissing
                && lastHost.TryGetValue(record.Id, out var lastKnown) && lastKnown is { Dead: false, Destroyed: false, Discarded: false }
                && FindAperture(lastKnown, record.ContainerId) is { } restored && IsLiveAperture(restored, record))
            {
                record.ClearUnresolved();
                BindAperture(record, restored);
            }
        }
    }
}
