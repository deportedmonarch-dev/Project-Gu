using System;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        internal bool HasAuthorizedGroundCustody(Map map) => ready && byProjection.Any(binding =>
            binding.Key.Map == map && !binding.Value.IsQuarantined
            && binding.Value.Custody.Kind is GuCustodyKind.WildPawn or GuCustodyKind.DormantGround
            && MatchesProjection(binding.Value, binding.Key));

        internal bool TryReleaseActive(string id, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            Map map, IntVec3 cell, out Pawn_GuWildProjection released, out string reason)
        {
            released = null;
            reason = "The native active-release safeguards are unavailable for this game configuration.";
            if (!CanUseDevelopmentCustody || ModsConfig.AnomalyActive) return false;

            reason = "The Gu's exact aperture binding or revision is unavailable.";
            if (Resolve(id) is not { } record || record.Custody.Kind != GuCustodyKind.Aperture
                || record.Revision != expectedRevision || expectedRevision == int.MaxValue
                || ResolveApertureOccupant(aperture) != record || aperture.pawn != host) return false;

            reason = "The species needs a wild projection covered by the native retirement contract.";
            var kind = record.Species?.wildProjectionKind;
            if (kind?.race?.thingClass != typeof(Pawn_GuWildProjection)
                || kind.race.race?.lifeStageAges.FirstOrDefault(stage => stage.def.developmentalStage == DevelopmentalStage.Adult)
                    is not { } adult) return false;

            var underground = Current.Game.GetComponent<GuPocketProbeComponent>();
            GuCustody source = record.Custody;
            bool DestinationReady(Pawn_GuWildProjection staged = null) => IsMapOwned(host) && host.Map == map
                && !host.Downed && host.Position.AdjacentToCardinal(cell) && IsLiveAperture(aperture, source)
                && CanPlaceDormant(map, cell, staged) && !map.reservationManager.IsReserved(cell)
                && host.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)
                && underground?.HasProtectedReleaseRoute(host) == true;

            reason = "Choose an adjacent open cell on the host's settlement or an independent level with a usable return route.";
            if (!DestinationReady() || RejectConflictingProjection(record, null, out reason)) return false;

            Pawn_GuWildProjection prepared = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "The native wild Pawn could not be placed safely.";
                try
                {
                    prepared = (Pawn_GuWildProjection)PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, tile: map.Tile,
                        forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true, forceNoGear: true,
                        fixedBiologicalAge: adult.minAge, developmentalStages: DevelopmentalStage.Adult));
                    prepared.SetReleaseSuppressed(true);
                    prepared.InitializeBinding(record);
                    if (!CanRestoreDetached(prepared) || !CanRetire(prepared) || !DestinationReady()) return false;

                    if (GenSpawn.Spawn(prepared, cell, map) != prepared || !IsOnCell(prepared, map, cell)
                        || !prepared.ReleaseSuppressed || prepared.Dead || prepared.kindDef != kind || prepared.CurJob != null
                        || prepared.GuId != record.Id || prepared.GuRevision != expectedRevision || !CanRetire(prepared)
                        || !DestinationReady(prepared)) return false;

                    string pawnId = prepared.GetUniqueLoadID();
                    UnbindAperture(record);
                    record.CommitActiveRelease(pawnId);
                    committed = true;
                    prepared.InitializeBinding(record);
                    Bind(record, prepared);
                    prepared.SetReleaseSuppressed(false);
                    if (!MatchesProjection(record, prepared) || !IsOnCell(prepared, map, cell)) return false;

                    released = prepared;
                    reason = null;
                    return true;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted active Gu release.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }
            }
            finally
            {
                try
                {
                    if (released == null)
                    {
                        bool cleared = DiscardFailedReleasePawn(prepared);
                        if (committed)
                        {
                            reason = "Active custody committed but the native Pawn binding failed. The Gu was quarantined.";
                            record.Quarantine(reason);
                            Unbind(record, prepared);
                        }
                        else if (!cleared || !IsLiveAperture(aperture, source))
                        {
                            reason = "Release rollback could not restore the exact aperture binding. The Gu was quarantined.";
                            QuarantineAperture(record, reason);
                        }
                    }
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        private static bool DiscardFailedReleasePawn(Pawn_GuWildProjection pawn)
        {
            if (pawn == null) return true;
            pawn.SetReleaseSuppressed(true);
            try
            {
                if (pawn.Corpse is { Destroyed: false } corpse) corpse.Destroy(DestroyMode.Vanish);
                if (pawn.Spawned) pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                if (CanRestoreDetached(pawn)) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Release rollback could not discard the provisional Pawn. {exception}");
            }

            if ((pawn.Discarded || pawn.Destroyed) && !pawn.Spawned && pawn.holdingOwner == null && pawn.Corpse == null
                && !Find.WorldPawns.Contains(pawn) && !Find.WorldPawns.IsBeingDiscarded(pawn)) return true;
            DetachFailedRetirement(pawn);
            return false;
        }
    }
}
