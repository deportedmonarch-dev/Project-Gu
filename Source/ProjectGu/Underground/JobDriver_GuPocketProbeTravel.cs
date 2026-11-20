using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ProjectGu.Underground
{
    public sealed class JobDriver_GuPocketProbeTravel : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            if (job.def == GuPassageHaulDefOf.GU_PassageHaulTravel)
                this.FailOn(() => GuPassageHauling.ActiveSite(pawn) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil travel = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            travel.initAction = () =>
            {
                var endpoint = job.targetA.Thing as Building_GuPocketProbeEndpoint;
                if (job.def == GuPassageHaulDefOf.GU_PassageHaulTravel)
                {
                    if (!GuPassageHauling.TryTravel(pawn, endpoint)) EndJobWith(JobCondition.Incompletable);
                }
                else if (!TryTravel(pawn, endpoint, out string reason))
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            };
            yield return travel;
        }

        internal static bool CanTravel(Pawn pawn, Building_GuPocketProbeEndpoint endpoint, out string reason) =>
            CanUsePassage(pawn, endpoint, out reason, requireDevMode: true);

        internal static bool CanUsePassage(Pawn pawn, Building_GuPocketProbeEndpoint endpoint, out string reason,
            bool requireDevMode = false)
        {
            if ((requireDevMode && !Prefs.DevMode) || endpoint?.Spawned != true || pawn?.Spawned != true || pawn.Map != endpoint.Map
                || !pawn.IsColonistPlayerControlled || pawn.Downed || pawn.GetLord() != null)
            {
                reason = "The probe requires a standing player-controlled colonist on this map.";
                return false;
            }
            if (pawn.carryTracker?.CarriedThing != null)
            {
                reason = "Put down the carried Thing before using the probe.";
                return false;
            }
            reason = null;
            return true;
        }

        internal static bool CanLand(Map map, IntVec3 cell, Pawn traveler)
        {
            if (!GuPocketProbeComponent.IsLive(map) || !cell.InBounds(map) || cell.Fogged(map) || !cell.Standable(map))
                return false;
            foreach (Thing thing in cell.GetThingList(map))
                if (thing is Pawn occupant && occupant != traveler)
                    return false;
            return true;
        }

        internal static bool TryTravel(Pawn pawn, Building_GuPocketProbeEndpoint endpoint, out string reason)
        {
            if (!CanTravel(pawn, endpoint, out reason))
                return false;

            var registry = Current.Game.GetComponent<GuPocketProbeComponent>();
            if (registry.OperationInProgress)
            {
                reason = "The pocket probe route is unavailable.";
                return false;
            }

            registry.OperationInProgress = true;
            try
            {
                Building_GuPocketProbeEndpoint destination;
                Func<bool> validateRoute;
                if (GuPocketProbeComponent.TryGetIndependentEndpoint(endpoint, out var independent, out var from))
                {
                    if (!registry.TryResolveIndependentTravel(independent, from, out var to, out reason))
                        return false;
                    destination = to.Anchor;
                    validateRoute = () => GuPocketProbeComponent.ValidateIndependentEndpoints(independent, from, to);
                }
                else if (GuPocketProbeComponent.TryGetSite(endpoint, out GuPocketSiteRecord site))
                {
                    if (site.Entrance == endpoint && !site.GenerationStarted && !GuPocketProbeComponent.TryGenerate(site, out reason))
                        return false;
                    destination = site.Entrance == endpoint ? site.Exit : site.Entrance;
                    validateRoute = () => GuPocketProbeComponent.HasValidRoute(site);
                }
                else
                {
                    reason = "The underground route is unavailable.";
                    return false;
                }

                if (!CanTravel(pawn, endpoint, out reason)) return false;
                if (!validateRoute())
                {
                    reason = "The underground route is closed.";
                    return false;
                }
                if (!TryCross(pawn, endpoint, destination, validateRoute, out reason))
                    return false;
            }
            finally
            {
                registry.OperationInProgress = false;
            }

            CameraJumper.TryJumpAndSelect(pawn);
            return true;
        }

        internal static bool TryCross(Pawn pawn, Building_GuPocketProbeEndpoint origin,
            Building_GuPocketProbeEndpoint destination, Func<bool> validateRoute, out string reason)
        {
            Map target = destination.Map;
            IntVec3 arrival = destination.Position;
            if (!CanLand(target, arrival, pawn) || !CanLand(pawn.Map, origin.Position, pawn)
                || !CanLand(pawn.Map, pawn.Position, pawn))
            {
                reason = "Clear the passage and its landing cell before traveling.";
                return false;
            }
            return TryMove(pawn, target, arrival, () => validateRoute()
                && destination.Map == target && destination.Position == arrival, out reason);
        }

        internal static bool TryMove(Pawn pawn, Map target, IntVec3 arrival, Func<bool> validateRoute, out string reason)
        {
            Map source = pawn.Map;
            IntVec3 origin = pawn.Position;
            Rot4 rotation = pawn.Rotation;
            try
            {
                pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                if (CanRestoreUnheld(pawn) && validateRoute() && CanLand(target, arrival, pawn))
                {
                    GenSpawn.Spawn(pawn, arrival, target, rotation);
                    if (pawn.Spawned && pawn.Map == target && pawn.Position == arrival
                        && validateRoute())
                    {
                        reason = null;
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Probe transfer failed for {pawn}. {exception}");
            }

            try
            {
                if (pawn.Spawned && pawn.Map == target)
                    pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                TryRestorePawn(pawn, source, origin, rotation);
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Probe rollback failed for {pawn}. {exception}");
            }

            if (pawn.Spawned && pawn.Map == source)
            {
                reason = "Travel failed. The colonist is back on the source map.";
                return false;
            }

            // Preserve custody in the engine if callbacks leave a live pawn outside both maps.
            try
            {
                if (CanRestoreUnheld(pawn) && !Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Probe world-pawn recovery failed for {pawn}. {exception}");
            }
            reason = "The probe could not restore the colonist. Stop and reload the last disposable test save.";
            Log.Error($"[Project GU] {reason} Pawn: {pawn}.");
            return false;
        }

        internal static void TryRestorePawn(Pawn pawn, Map map, IntVec3 origin, Rot4 rotation)
        {
            if (!CanRestoreUnheld(pawn) || !GuPocketProbeComponent.IsLive(map)) return;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, 4f, true))
            {
                if (!CanLand(map, cell, pawn)) continue;
                GenSpawn.Spawn(pawn, cell, map, rotation);
                return;
            }
        }

        internal static bool CanRestoreUnheld(Pawn pawn) =>
            !pawn.Spawned && !pawn.Destroyed && !pawn.Dead && pawn.holdingOwner == null;
    }
}
