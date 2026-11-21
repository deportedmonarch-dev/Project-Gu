using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ProjectGu.Underground
{
    /// <summary>Optional map-local protection for persistent custody.</summary>
    public interface IGuUndergroundCustodyProtection
    {
        bool HasProtectedCustody { get; }
    }

    public sealed partial class GuPocketProbeComponent
    {
        internal bool ProtectsIndependentAccess(Map surface)
        {
            if (surface == null) return false;
            foreach (var site in independentSites)
            {
                if (!site.Endpoints.Any(end => end?.Surface == surface)) continue;
                foreach (var level in site.Levels)
                    if (level?.LiveMap is Map map && HasProtectedOccupants(map)
                        && !HasUsableSurfaceRoute(site, level.LevelId, surface, null))
                        return true;
            }
            return false;
        }

        internal bool HasIndependentResetRisk => independentSites.Any(site => site.Levels.Any(level =>
            level?.LiveMap is Map map && (map.IsPlayerHome || HasProtectedOccupants(map))));

        private static bool HasProtectedOccupants(Map map) =>
            map.mapPawns.AllPawns.Any(pawn => pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony)
            || map.components.OfType<IGuUndergroundCustodyProtection>().Any(protection => protection.HasProtectedCustody);

        internal static bool HasUsableSurfaceRoute(GuIndependentSite site, string startLevel, Map excluded, Pawn traveler) =>
            site.CanReachSurface(startLevel, end =>
            {
                if (!end.HasAnchor(site)) return false;
                Map map = end.Anchor.Map;
                if ((excluded != null && map == excluded) || !JobDriver_GuPocketProbeTravel.CanLand(map, end.Anchor.Position, traveler))
                    return false;
                if (traveler != null && traveler.Map == map && !traveler.Downed)
                    return traveler.CanReach(end.Anchor, PathEndMode.Touch, Danger.Deadly);
                return true;
            });

        internal bool HasProtectedProjectionRoute(Map map, Pawn traveler = null)
        {
            if (!GuUndergroundLifecycleGuards.Installed || OperationInProgress) return false;
            if (IsSurfaceSettlement(map)) return true;
            return TryFindIndependentLevel(map, out var site, out var level)
                && (traveler == null || !traveler.Downed && traveler.Map == map)
                && HasUsableSurfaceRoute(site, level.LevelId, null, traveler);
        }

        private bool FindRecoverySite(Pawn pawn, out GuIndependentSite site, out GuIndependentLevel level)
        {
            site = null;
            level = null;
            return pawn?.Spawned == true && !pawn.Dead && pawn.Faction == Faction.OfPlayer
                && TryFindIndependentLevel(pawn.Map, out site, out level);
        }

        private bool TryFindIndependentLevel(Map map, out GuIndependentSite site, out GuIndependentLevel level)
        {
            site = null;
            level = null;
            if (map?.Parent is not GuUndergroundLevelParent parent) return false;
            site = independentSites.Find(candidate => candidate.Level(parent.LevelId)?.LiveMap == map);
            level = site?.Level(parent.LevelId);
            return level != null;
        }

        internal IEnumerable<Gizmo> RecoveryGizmos(Pawn pawn, IEnumerable<Gizmo> stock)
        {
            foreach (var gizmo in stock) yield return gizmo;
            if (!FindRecoverySite(pawn, out _, out _)) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Emergency return to surface",
                defaultDesc = "Recover this pawn when normal passages are unusable. Held items travel in the pawn's inventory.",
                action = () => Messages.Message(TryRecoverPawn(pawn, out string reason) ? "Emergency return complete." : reason,
                    MessageTypeDefOf.NeutralEvent, false)
            };
        }

        internal bool TryRecoverPawn(Pawn pawn, out string reason)
        {
            reason = "This pawn cannot use underground recovery.";
            if (OperationInProgress || !FindRecoverySite(pawn, out var site, out var level))
                return false;
            if (!pawn.Downed && HasUsableSurfaceRoute(site, level.LevelId, null, pawn))
            {
                reason = "A normal passage is available.";
                return false;
            }

            Map surface = null;
            IntVec3 landing = IntVec3.Invalid;
            foreach (var end in site.Endpoints.Where(end => end.IsSurface))
            {
                surface = end.Resolve(site);
                if (surface == null || !end.Cell.IsValid) continue;
                landing = GenRadial.RadialCellsAround(end.Cell, 10f, true)
                    .FirstOrDefault(cell => JobDriver_GuPocketProbeTravel.CanLand(surface, cell, pawn), IntVec3.Invalid);
                if (landing.IsValid) break;
            }
            bool landOnMap = landing.IsValid;
            if (!landOnMap && !CanRecoverToWorld(site.Geography))
            {
                reason = "No safe surface landing is available at this site.";
                return false;
            }

            OperationInProgress = true;
            try
            {
                var carry = pawn.carryTracker?.GetDirectlyHeldThings();
                Thing item = pawn.carryTracker?.CarriedThing;
                var inventory = pawn.inventory?.GetDirectlyHeldThings();
                if (item != null && !PackCarriedItem(item, carry, inventory))
                {
                    reason = "Put the carried pawn or item down before using emergency return.";
                    return false;
                }

                bool moved = landOnMap
                    ? JobDriver_GuPocketProbeTravel.TryMove(pawn, surface, landing, () => IsSurfaceSettlement(surface), out reason)
                    : RecoverToCaravan(pawn, site.Geography, out reason);
                if (!moved)
                {
                    if (item != null && item.holdingOwner == inventory) inventory.TryTransferToContainer(item, carry, false);
                    return false;
                }
                if (landOnMap) CameraJumper.TryJumpAndSelect(pawn);
                return true;
            }
            finally
            {
                OperationInProgress = false;
            }
        }

        private static bool PackCarriedItem(Thing item, ThingOwner carry, ThingOwner inventory)
        {
            int count = item.stackCount;
            return item is not Pawn && inventory != null && inventory.GetCountCanAccept(item, false) >= count
                && carry.TryTransferToContainer(item, inventory, count, false) == count && item.holdingOwner == inventory;
        }

        private static bool CanRecoverToWorld(PlanetTile tile) =>
            tile.Valid && tile.LayerDef == PlanetLayerDefOf.Surface && Find.WorldPathGrid.Passable(tile);

        private static bool RecoverToCaravan(Pawn pawn, PlanetTile tile, out string reason)
        {
            Map source = pawn.Map;
            IntVec3 position = pawn.Position;
            Rot4 rotation = pawn.Rotation;
            try
            {
                pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
                if (JobDriver_GuPocketProbeTravel.CanRestoreUnheld(pawn) && CanRecoverToWorld(tile))
                    CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, tile, true);
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Underground emergency caravan failed for {pawn}. {exception}");
            }
            if (pawn.GetCaravan() is { Spawned: true } caravan && caravan.Tile == tile && caravan.ContainsPawn(pawn))
            {
                reason = null;
                return true;
            }
            try
            {
                JobDriver_GuPocketProbeTravel.TryRestorePawn(pawn, source, position, rotation);
                if (JobDriver_GuPocketProbeTravel.CanRestoreUnheld(pawn) && !Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Could not restore emergency traveler {pawn}. {exception}");
            }
            reason = pawn.Spawned && pawn.Map == source ? "Emergency return failed. The pawn is back underground."
                : "Emergency return failed. Reload the last test save.";
            return false;
        }
    }
}
