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
        /// <summary>True when an authorized manifestation is spawned on the map by itself, not held by a Pawn.</summary>
        internal bool HasAuthorizedGroundCustody(Map map) => ready && map != null && byProjection.Any(binding =>
            binding.Key.Spawned && binding.Key.Map == map && IsAuthorized(binding.Value, binding.Key));

        internal bool TryReleaseActive(string id, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            Map map, IntVec3 cell, out Pawn_GuWildProjection released, out string reason)
        {
            released = null;
            reason = "The native active-release safeguards are unavailable for this game configuration.";
            if (!CanUseDevelopmentCustody) return false;

            reason = "The Gu's aperture containment or revision is unavailable.";
            if (Resolve(id) is not { } record || record.Placement != GuPlacementKind.Contained
                || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue
                || !apertureOf.TryGetValue(record.Id, out var bound) || bound != aperture || aperture.pawn != host) return false;

            reason = "The species needs a wild projection covered by the native retirement contract.";
            var kind = record.Species?.wildProjectionKind;
            if (FirstAdultProjectionStage(kind) is not { } adult) return false;

            var underground = Current.Game.GetComponent<GuPocketProbeComponent>();
            bool DestinationReady(Pawn_GuWildProjection staged = null) => IsMapOwned(host) && host.Map == map
                && !host.Downed && host.Position.AdjacentToCardinal(cell) && IsLiveAperture(aperture, record)
                && CanPlaceDormant(map, cell, staged) && !map.reservationManager.IsReserved(cell)
                && host.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)
                && underground?.HasProtectedProjectionRoute(map, host) == true;

            reason = "Choose an adjacent open cell on the host's settlement or an independent level with a usable return route.";
            if (!DestinationReady()) return false;

            Pawn_GuWildProjection prepared = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "The native wild Pawn could not be placed safely.";
                try
                {
                    prepared = PrepareWildPawn(kind, adult, map);
                    prepared.InitializeBinding(record);
                    if (!CanRestoreDetached(prepared) || !CanRetire(prepared) || !DestinationReady()) return false;

                    if (!TryPlaceStagedWildPawn(prepared, record, kind, map, cell) || !DestinationReady(prepared)) return false;

                    record.ChangeCondition(GuCondition.Active);
                    record.CommitManifestation(GuManifestationKind.Pawn, prepared.GetUniqueLoadID());
                    committed = true;
                    prepared.InitializeBinding(record);
                    BindProjection(record, prepared);
                    prepared.SetReleaseSuppressed(false);
                    if (!MatchesManifestation(record, prepared) || !IsOnCell(prepared, map, cell)) return false;

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
                        bool cleared = DiscardProvisionalWildPawn(prepared);
                        if (committed)
                        {
                            reason = "Active release committed but the native Pawn binding failed. The Gu is unresolved.";
                            UnbindAll(record);
                            record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None, reason, Find.TickManager.TicksGame);
                        }
                        else
                        {
                            if (!cleared) MarkRogue(prepared?.GuTag(), "A failed release could not discard its provisional Pawn.");
                            if (!IsBoundAperture(aperture, record)) ApplyApertureState(record, host, Find.TickManager.TicksGame);
                        }
                    }
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        private static LifeStageAge FirstAdultProjectionStage(PawnKindDef kind) =>
            kind?.race?.thingClass == typeof(Pawn_GuWildProjection)
                ? kind.race.race?.lifeStageAges.FirstOrDefault(stage => stage.def.developmentalStage == DevelopmentalStage.Adult)
                : null;

        private static Pawn_GuWildProjection PrepareWildPawn(PawnKindDef kind, LifeStageAge adult, Map map)
        {
            var pawn = Pawn_GuWildProjection.GenerateForRegistry(new PawnGenerationRequest(kind, tile: map.Tile,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true, forceNoGear: true,
                fixedBiologicalAge: adult.minAge, developmentalStages: DevelopmentalStage.Adult));
            pawn.SetReleaseSuppressed(true);
            return pawn;
        }

        private static bool TryPlaceStagedWildPawn(Pawn_GuWildProjection pawn, GuRecord record, PawnKindDef kind,
            Map map, IntVec3 cell) => GenSpawn.Spawn(pawn, cell, map) == pawn && IsOnCell(pawn, map, cell)
                && pawn.ReleaseSuppressed && !pawn.Dead && pawn.kindDef == kind && pawn.CurJob == null
                && pawn.GuId == record.Id && pawn.GuRevision == record.CustodyRevision && CanRetire(pawn);

        private static bool DiscardProvisionalWildPawn(Pawn_GuWildProjection pawn)
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
