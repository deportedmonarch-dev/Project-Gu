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
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil travel = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            travel.initAction = () =>
            {
                var endpoint = job.targetA.Thing as Building_GuPocketProbeEndpoint;
                if (!TryTravel(pawn, endpoint, out string reason))
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            };
            yield return travel;
        }

        internal static bool CanTravel(Pawn pawn, Building_GuPocketProbeEndpoint endpoint, out string reason)
        {
            if (!Prefs.DevMode || endpoint?.Spawned != true || pawn?.Spawned != true || pawn.Map != endpoint.Map
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

        private static bool TryTravel(Pawn pawn, Building_GuPocketProbeEndpoint endpoint, out string reason)
        {
            if (!CanTravel(pawn, endpoint, out reason))
                return false;

            var registry = Current.Game.GetComponent<GuPocketProbeComponent>();
            if (registry.OperationInProgress || !GuPocketProbeComponent.TryGetSite(endpoint, out GuPocketSiteRecord site))
            {
                reason = "The pocket probe route is unavailable.";
                return false;
            }

            registry.OperationInProgress = true;
            try
            {
                if (site.Entrance == endpoint && !site.GenerationStarted
                    && !GuPocketProbeComponent.TryGenerate(site, out reason))
                    return false;

                if (!CanTravel(pawn, endpoint, out reason))
                    return false;
                if (!GuPocketProbeComponent.HasValidRoute(site))
                {
                    reason = "The pocket probe route is closed.";
                    return false;
                }

                Building_GuPocketProbeEndpoint destination = site.Entrance == endpoint ? site.Exit : site.Entrance;
                if (!CanLand(destination.Map, destination.Position, pawn)
                    || !CanLand(pawn.Map, endpoint.Position, pawn) || !CanLand(pawn.Map, pawn.Position, pawn))
                {
                    reason = "Clear the passage and its landing cell before traveling.";
                    return false;
                }

                if (!TryMove(pawn, site, destination, out reason))
                    return false;
            }
            finally
            {
                registry.OperationInProgress = false;
            }

            CameraJumper.TryJumpAndSelect(pawn);
            return true;
        }

        private static bool TryMove(Pawn pawn, GuPocketSiteRecord site, Building_GuPocketProbeEndpoint destination,
            out string reason)
        {
            Map source = pawn.Map;
            IntVec3 origin = pawn.Position;
            Rot4 rotation = pawn.Rotation;
            Map target = destination.Map;
            IntVec3 arrival = destination.Position;
            try
            {
                pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                if (CanRestoreUnheld(pawn) && GuPocketProbeComponent.HasValidRoute(site)
                    && destination.Map == target && destination.Position == arrival && CanLand(target, arrival, pawn))
                {
                    GenSpawn.Spawn(pawn, arrival, target, rotation);
                    if (pawn.Spawned && pawn.Map == target && pawn.Position == arrival
                        && GuPocketProbeComponent.HasValidRoute(site))
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
                if (CanRestoreUnheld(pawn))
                {
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, 4f, true))
                    {
                        if (!CanLand(source, cell, pawn))
                            continue;
                        GenSpawn.Spawn(pawn, cell, source, rotation);
                        break;
                    }
                }
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

        private static bool CanRestoreUnheld(Pawn pawn) =>
            !pawn.Spawned && !pawn.Destroyed && !pawn.Dead && pawn.holdingOwner == null;
    }
}
