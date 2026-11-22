using System;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        internal bool TryMakeDormant(Pawn_GuWildProjection pawn, int expectedRevision, IntVec3 destination,
            out Thing_GuDormantProjection dormant, out string reason)
        {
            dormant = null;
            reason = "The Gu's wild binding or revision is unavailable.";
            if (!Prefs.DevMode || CustodyTransitionInProgress || LongEventHandler.AnyEventNowOrWaiting || Scribe.mode != LoadSaveMode.Inactive
                || Resolve(pawn) is not { } record || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue) return false;
            if (!GuCustodySaveGuard.Installed)
            {
                reason = "The native dormancy safeguards are unavailable for this game configuration.";
                return false;
            }
            var def = record.Species?.dormantProjectionDef;
            if (record.Species?.capabilityTags?.Contains("development_test") != true || !Thing_GuDormantProjection.IsConfigured(def) || !CanRetire(pawn))
            {
                reason = "The development Gu must be an unowned, unharmed wild Pawn with empty equipment and inventory.";
                return false;
            }

            Map map = pawn.Map;
            IntVec3 origin = pawn.Position;
            Rot4 rotation = pawn.Rotation;
            reason = "The dormant Gu needs an open cell.";
            if (!CanPlaceDormant(map, destination, pawn)) return false;

            reason = "Dormancy staging could not establish a safe binding.";
            Thing_GuDormantProjection staged = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    staged = (Thing_GuDormantProjection)ThingMaker.MakeThing(def);
                    staged.InitializeBinding(record);
                    pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                    if (!CanRestoreDetached(pawn) || !CanPlaceDormant(map, destination, null)
                        || !GenPlace.TryPlaceThing(staged, destination, map, ThingPlaceMode.Direct, out Thing placed)
                        || placed != staged || !IsMapOwned(staged) || staged.Map != map || staged.Position != destination
                        || staged.def != def || staged.stackCount != 1 || staged.GuId != record.Id || staged.GuRevision != expectedRevision
                        || !CanRestoreDetached(pawn) || !CanRetire(pawn) || pawn.GuId != record.Id || pawn.GuRevision != expectedRevision
                        || pawn.GetUniqueLoadID() != record.ProjectionId) return false;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted dormancy placement.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }

                record.ChangeCondition(GuCondition.Dormant);
                record.CommitManifestation(GuManifestationKind.Thing, staged.GetUniqueLoadID());
                staged.InitializeBinding(record);
                committed = true;
                BindProjection(record, staged);

                try
                {
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                    if (pawn.Discarded && !pawn.Dead && !pawn.Spawned && pawn.holdingOwner == null
                        && !Find.WorldPawns.Contains(pawn) && !Find.WorldPawns.IsBeingDiscarded(pawn)
                        && MatchesManifestation(record, staged))
                    {
                        dormant = staged;
                        reason = null;
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] Native retirement failed for Gu {record.Id}. {exception}");
                }

                // The committed dormant Thing is authorized. The former Pawn keeps a stale tag and can never authorize.
                DetachFailedRetirement(pawn);
                MarkRogue(pawn.GuTag(), "Native retirement could not be verified after dormancy committed.");
                if (MatchesManifestation(record, staged)) dormant = staged;
                reason = "Dormancy committed. The former Pawn could not be retired and is recorded as a rogue projection.";
                return dormant != null;
            }
            finally
            {
                try
                {
                    if (!committed && !RestoreWildAfterFailedDormancy(record, pawn, staged, map, origin, rotation))
                        reason = "Dormancy rollback could not restore the original wild Pawn. See the record for its observed state.";
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        /// <summary>
        /// Anomaly adds death and discard notifications for unnatural corpses, mutants, creepjoiners, duplicates and hypnosis.
        /// A Gu projection carrying none of these is outside every Anomaly path (Phase 2 investigation). Others are refused.
        /// </summary>
        private static bool IsOutsideAnomalyLifecycle(Pawn pawn) => !ModsConfig.AnomalyActive
            || (!pawn.IsMutant && !pawn.IsCreepJoiner && !pawn.IsDuplicate && Find.Anomaly?.PawnHasUnnaturalCorpse(pawn) != true
                && Find.Anomaly?.IsPawnHypnotized(pawn) != true);

        private static bool CanRetire(Pawn_GuWildProjection pawn) => pawn.GetType() == typeof(Pawn_GuWildProjection) && IsOutsideAnomalyLifecycle(pawn)
            && pawn.Faction == null && !pawn.Downed && !pawn.IsPrisoner && !pawn.IsSlave && pawn.GetLord() == null
            && !Find.WorldPawns.Contains(pawn) && !QuestUtility.IsQuestLodger(pawn) && pawn.questTags?.Count is not > 0
            && pawn.relations?.RelatedToAnyoneOrAnyoneRelatedToMe != true && !Find.TaleManager.AnyTaleConcerns(pawn)
            && (pawn.ownership == null || (pawn.ownership.OwnedBed == null && pawn.ownership.AssignedGrave == null
                && pawn.ownership.AssignedThrone == null && pawn.ownership.AssignedMeditationSpot == null
                && pawn.ownership.AssignedDeathrestCasket == null))
            && pawn.health.hediffSet.hediffs.Count == 0 && pawn.inventory?.GetDirectlyHeldThings().Count is not > 0
            && pawn.carryTracker?.CarriedThing == null && pawn.equipment?.AllEquipmentListForReading.Count is not > 0
            && pawn.apparel?.WornApparel.Count is not > 0;

        private static bool CanPlaceDormant(Map map, IntVec3 cell, Pawn source) => map is { Disposed: false }
            && Find.Maps.Contains(map) && cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map)
            && cell.GetThingList(map).All(thing => thing == source);

        private static bool CanRestoreDetached(Pawn pawn) => JobDriver_GuPocketProbeTravel.CanRestoreUnheld(pawn)
            && !Find.WorldPawns.Contains(pawn);

        private bool RestoreWildAfterFailedDormancy(GuRecord record, Pawn_GuWildProjection pawn,
            Thing_GuDormantProjection staged, Map map, IntVec3 origin, Rot4 rotation)
        {
            if (staged != null && !staged.Destroyed)
            {
                try { staged.Destroy(DestroyMode.Vanish); }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] Dormancy rollback could not destroy its provisional Thing. {exception}");
                    DetachFailedRetirement(staged);
                }
            }

            bool destinationCleared = staged == null || (!staged.Spawned && staged.holdingOwner == null);
            try
            {
                if (destinationCleared && CanRestoreDetached(pawn))
                    JobDriver_GuPocketProbeTravel.TryRestorePawn(pawn, map, origin, rotation);
                if (destinationCleared && MatchesManifestation(record, pawn) && pawn.Map == map) return true;

                if (destinationCleared && CanRestoreDetached(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Dormancy rollback could not restore its original Pawn. {exception}");
            }

            // The record still authorizes the same Pawn. Its observed location is wherever recovery left it.
            if (!MatchesManifestation(record, pawn))
            {
                UnbindAll(record);
                record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                    "Dormancy rollback could not restore the original wild Pawn.", Find.TickManager.TicksGame);
            }
            return false;
        }

        private static void DetachFailedRetirement(Thing thing)
        {
            try
            {
                if (thing.Spawned) thing.DeSpawnOrDeselect(DestroyMode.Vanish);
                thing.holdingOwner?.Remove(thing);
                if (thing is Pawn pawn && Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] A retired Gu projection could not be detached. {exception}");
            }
        }
    }
}
