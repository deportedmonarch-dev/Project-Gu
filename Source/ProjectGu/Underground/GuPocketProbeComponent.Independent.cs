using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Underground
{
    public sealed partial class GuPocketProbeComponent
    {
        private List<GuIndependentSite> independentSites = new();

        internal void AddIndependentSite(GuPocketSiteDef kind, Building_GuPocketProbeEndpoint entrance) =>
            independentSites.Add(GuIndependentSite.FirstLevel(kind, entrance.Map, entrance));

        internal GuIndependentSite HaulingSiteFor(Pawn pawn) => independentSites.Find(site => site.Hauling?.Worker == pawn);

        internal static bool TryGetIndependentEndpoint(Building_GuPocketProbeEndpoint anchor,
            out GuIndependentSite site, out GuIndependentEndpoint endpoint)
        {
            site = null;
            endpoint = null;
            if (anchor == null || Current.Game?.GetComponent<GuPocketProbeComponent>() is not { } registry)
                return false;
            foreach (GuIndependentSite candidate in registry.independentSites)
            {
                endpoint = candidate.Endpoints.Find(end => end.Anchor == anchor);
                if (endpoint == null) continue;
                site = candidate;
                return true;
            }
            return false;
        }

        internal bool TryResolveIndependentTravel(GuIndependentSite site, GuIndependentEndpoint from,
            out GuIndependentEndpoint destination, out string reason)
        {
            destination = null;
            reason = "The underground route is closed.";
            if (!GuUndergroundLifecycleGuards.Installed || !from.HasAnchor(site))
                return false;

            // A passage selects one attached route. Additional passages may use the same level.
            destination = site.DestinationFor(from.EndpointId);
            if (destination == null) return false;
            if (!destination.IsSurface && site.Level(destination.LevelId) is { GenerationStarted: false } pending
                && !TryGenerateIndependent(site, pending, destination, from.Anchor.ConsumeGenerationRejection(), out reason))
                return false;
            return destination.HasAnchor(site);
        }

        private static bool TryGenerateIndependent(GuIndependentSite site, GuIndependentLevel level,
            GuIndependentEndpoint arrival, bool reject, out string reason)
        {
            reason = "The independent underground level could not be generated.";
            if (level.GenerationStarted || level.Parent != null || site.Kind?.IsConfigured != true
                || (site.Kind == GuPocketSiteDef.SpiritSpring && !GuSpringCavernIncidents.GuardsInstalled()))
                return false;
            if (LongEventHandler.AnyEventNowOrWaiting)
            {
                reason = "Wait for RimWorld to finish loading before opening a new level.";
                return false;
            }
            // Keep room for normal colony maps. This is an experimental provider's development limit.
            if (Find.WorldObjects.MapParents.Count(parent => parent is GuUndergroundLevelParent) >= 8)
            {
                reason = "The independent underground test limit of eight levels has been reached.";
                return false;
            }
            if (Find.Maps.Count >= sbyte.MaxValue)
            {
                reason = "RimWorld has reached its loaded-map limit.";
                return false;
            }

            GuUndergroundLevelParent parent = null;
            var fixtures = new HashSet<Thing>();
            try
            {
                var layer = GuUndergroundAddressLayer.Resolve(Find.WorldGrid, create: true);
                PlanetTile address = layer.AvailableAddress();
                if (!address.Valid) return false;
                var def = DefDatabase<WorldObjectDef>.GetNamed("GU_UndergroundLevel");
                parent = (GuUndergroundLevelParent)WorldObjectMaker.MakeWorldObject(def);
                parent.LevelId = level.LevelId;
                parent.Generator = site.Kind.generator;
                parent.Tile = address;
                level.GenerationStarted = true;
                level.Parent = parent;
                Find.WorldObjects.Add(parent);
                generationFixtures = fixtures;
                int size = site.Kind.mapSize;
                Map map = MapGenerator.GenerateMap(new IntVec3(size, 1, size), parent, parent.Generator, null, null, false, false);
                if (level.LiveMap != map || map == null || !layer.Owns(map.Tile))
                    return false;
                var exits = map.listerThings.ThingsOfDef(site.Kind.ExitDef);
                var exit = exits.Count == 1 ? exits[0] as Building_GuPocketProbeEndpoint : null;
                if (reject || exit?.Map != map || !JobDriver_GuPocketProbeTravel.CanLand(map, exit.Position, null))
                {
                    reason = "Independent generation was rejected or the return passage was invalid.";
                    if (!map.listerThings.AllThings.All(fixtures.Contains) || map.mapPawns.AllPawns.Any() || map.ChildPocketMaps.Any())
                        return false;
                    GuUndergroundLifecycleGuards.CleanupRejectedLevel(parent);
                    if (parent.Spawned || !parent.Destroyed || !map.Disposed || IsLive(map)) return false;
                    level.Parent = null;
                    level.GenerationStarted = false;
                    reason += " Retry is available.";
                    return false;
                }
                arrival.Anchor = exit;
                arrival.Cell = exit.Position;
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Independent generation failed for level {level.LevelId}. {exception}");
                return false;
            }
            finally
            {
                generationFixtures = null;
            }
        }

        internal static bool ValidateIndependentEndpoints(GuIndependentSite site, GuIndependentEndpoint from, GuIndependentEndpoint to) =>
            from.HasAnchor(site) && to.HasAnchor(site);

        internal bool TryGetIndependentArrival(Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map?.Parent is not GuUndergroundLevelParent parent) return false;
            foreach (var site in independentSites)
            {
                if (site.Level(parent.LevelId)?.LiveMap != map) continue;
                var endpoint = site.Endpoints.Find(end => end.LevelId == parent.LevelId && end.Cell.IsValid);
                if (endpoint == null) return false;
                cell = endpoint.Cell;
                return true;
            }
            return false;
        }

        private void NotifyIndependentMapRemoved(Map removed)
        {
            foreach (var site in independentSites)
                foreach (var end in site.Endpoints)
                    if (end.Surface == removed)
                    {
                        end.Surface = null;
                        end.Anchor = null;
                    }
        }

        private void ReportMissingIndependentLevels()
        {
            foreach (var site in independentSites)
                foreach (var level in site.Levels)
                    if (level is { GenerationStarted: true } && level.LiveMap == null)
                        Log.Error($"[Project GU] Independent route for site {site.SiteId}, level {level.LevelId} is closed. "
                            + "The saved native level binding could not be resolved. The generator will not run again.");
        }
    }
}
