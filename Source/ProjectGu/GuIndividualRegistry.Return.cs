using System;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        internal void NoteSurfaceSearchResult(Pawn_GuWildProjection pawn) =>
            Resolve(pawn)?.NoteEncounterInteraction(Find.TickManager.TicksGame);

        private static bool HasEncounterInteraction(Pawn_GuWildProjection wild, Map map, IntVec3 cell)
        {
            if (Find.Selector?.IsSelected(wild) == true || map.reservationManager.IsReserved(wild)
                || map.physicalInteractionReservationManager.IsReserved(wild)) return true;

            bool revealed = !cell.Fogged(map);
            foreach (var actor in map.mapPawns.AllPawnsSpawned)
            {
                if (actor == wild || actor.Downed || actor.Dead) continue;
                if (Targets(actor.CurJob) || actor.mindState?.enemyTarget == wild
                    || actor.jobs?.jobQueue.Any(queued => Targets(queued.job)) == true) return true;
                if (revealed && actor.RaceProps.Humanlike && actor.Awake() && actor.Position.DistanceToSquared(cell) <= 144
                    && GenSight.LineOfSight(actor.Position, cell, map, true)) return true;
            }
            return false;

            bool Targets(Job job) => job != null && (job.targetA.Thing == wild || job.targetB.Thing == wild || job.targetC.Thing == wild
                || job.targetQueueA?.Exists(target => target.Thing == wild) == true || job.targetQueueB?.Exists(target => target.Thing == wild) == true
                || revealed && job.def?.driverClass == typeof(JobDriver_GuSearchSurface)
                    && job.targetA.Cell.DistanceToSquared(cell) <= JobDriver_GuSearchSurface.SearchRadius * JobDriver_GuSearchSurface.SearchRadius);
        }

        internal bool TryReturnSurfaceResident(string id, int expectedRevision, Map map, out GuPopulationReturnReceipt receipt)
        {
            receipt = null;
            if (FindRecord(id) is not { } record) return false;
            if (record.Existence == GuExistence.Aggregated)
            {
                var previous = record.PopulationReturn;
                if (record.IsQuarantined || previous?.CustodyBefore != expectedRevision || !previous.Matches(record)) return false;
                receipt = previous;
                return true;
            }

            if (!CanUseCustody || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue
                || record.StateRevision == int.MaxValue || record.Rank != 1 || record.SurfaceResidence is not { } site
                || record.ReturnBlockedForBinding || record.TerminalEvidence != GuTerminalEvidence.None
                || ProjectionOf(record) is not Pawn_GuWildProjection pawn || !IsMapOwned(pawn) || pawn.Map != map
                || !MatchesManifestation(record, pawn) || !record.IsActionable) return false;

            int now = Find.TickManager.TicksGame;
            if (!record.ReturnRetryDue(now)) return false;
            int quietTicks = Math.Clamp(ProjectGuMod.Settings?.encounterQuietDays ?? 1, 1, 600) * GuSurfaceEcology.TicksPerDay;
            int historyTicks = Math.Clamp(ProjectGuMod.Settings?.encounterHistoryDays ?? 60, 1, 600) * GuSurfaceEcology.TicksPerDay;
            var cell = pawn.Position;
            var rotation = pawn.Rotation;
            var ecology = GuSurfaceEcology.Instance;

            bool Protected()
            {
                if (HasEncounterInteraction(pawn, map, cell)) record.NoteEncounterInteraction(now);
                return record.EncounterProtected(now, quietTicks, historyTicks) || !CanRetire(pawn);
            }

            if (Protected() || pending.Count != 0 || ecology == null
                || !ecology.TryPrepareReturn(record, map, out var destination, out _)) return false;

            var cohort = destination.Cohort;
            long revision = cohort.Revision;
            int stateRevision = record.StateRevision;
            var prepared = new GuPopulationReturnReceipt(site, map.Parent.GetUniqueLoadID(), expectedRevision, revision + 1, now);
            string eventKey = prepared.EventKey(record.Id);
            if (byEndEvent.ContainsKey(eventKey)) return false;

            CustodyTransitionInProgress = true;
            try
            {
                try { pawn.DeSpawnOrDeselect(DestroyMode.Vanish); }
                catch (Exception exception)
                {
                    record.BlockReturnForBinding();
                    Log.Warning($"[Project GU] Native return detachment failed for Gu {id}. {exception}");
                    return false;
                }

                if (!CanRestoreDetached(pawn) || pawn.Discarded || Find.WorldPawns.IsBeingDiscarded(pawn) || !MatchesManifestation(record, pawn))
                {
                    if (!pawn.Dead && !pawn.Destroyed && !pawn.Discarded) record.BlockReturnForBinding();
                    return false;
                }
                if (!record.IsActionable || pending.Count != 0 || MapTeardownInProgress || record.CustodyRevision != expectedRevision
                    || record.StateRevision != stateRevision
                    || ProjectionOf(record) != pawn || map.Disposed || !Find.Maps.Contains(map) || Protected()
                    || !ecology.TryPrepareReturn(record, map, out var approved, out int remaining) || approved != destination
                    || approved.Cohort != cohort
                    || !GuSurfaceEcology.CommitReturnCredit(destination, prepared.CreditKey(record.Id), revision, remaining, now)) return false;

                record.CommitPopulationReturn(prepared);
                byEndEvent.Add(eventKey, record);
                IndexSurfaceReturn(record, append: true);
                UnbindAll(record);
                lastProjection.Remove(record.Id);
                lastHost.Remove(record.Id);
                pawn.SetReleaseSuppressed(true);
                receipt = prepared;
                RetireReturnedPawn(pawn, record);
                return true;
            }
            finally
            {
                try
                {
                    if (receipt == null)
                    {
                        record.DeferReturn(now);
                        RestoreFailedReturn(record, pawn, map, cell, rotation);
                    }
                }
                finally { CustodyTransitionInProgress = false; }
                ApplyPendingEvents();
            }
        }

        private void RestoreFailedReturn(GuRecord record, Pawn_GuWildProjection pawn, Map map, IntVec3 cell, Rot4 rotation)
        {
            if (pawn.Discarded || !CanRestoreDetached(pawn) || ProjectionOf(record) != pawn || Find.WorldPawns.IsBeingDiscarded(pawn)) return;
            try { JobDriver_GuPocketProbeTravel.TryRestorePawn(pawn, map, cell, rotation); }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Native return rollback failed for Gu {record.Id}. {exception}");
            }
            if (pawn.Discarded || !CanRestoreDetached(pawn)) return;

            int tick = Find.TickManager.TicksGame;
            PreserveDetachedProjection(record, pawn, GuLocation.Capture(map, cell, null, tick), tick);
        }

        private void PreserveDetachedProjection(GuRecord record, Pawn_GuWildProjection pawn, GuLocation location, int tick)
        {
            record.BlockReturnForBinding();
            try
            {
                if (!pawn.Discarded && CanRestoreDetached(pawn) && !Find.WorldPawns.IsBeingDiscarded(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Native custody of the detached Gu {record.Id} could not be restored. {exception}");
            }
            if (HasNativeCustody(pawn) || pending.Any(item => item.Projection == pawn)) return;
            UnbindAll(record);
            record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                "Native reconciliation could not restore an owned projection.", tick, location);
        }

        private void RetireReturnedPawn(Pawn_GuWildProjection pawn, GuRecord record)
        {
            try
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                if (pawn.Discarded && !pawn.Spawned && pawn.holdingOwner == null && !Find.WorldPawns.Contains(pawn)
                    && !Find.WorldPawns.IsBeingDiscarded(pawn)) return;
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Native retirement failed after Gu {record.Id} returned. {exception}");
            }
            var tag = pawn.GuTag();
            MarkRogue(tag, "The obsolete projection of a completed population return could not be discarded.");
            if (!pawn.Spawned && pawn.holdingOwner == null && !Find.WorldPawns.Contains(pawn)) rogues.Remove(tag);
        }
    }
}
