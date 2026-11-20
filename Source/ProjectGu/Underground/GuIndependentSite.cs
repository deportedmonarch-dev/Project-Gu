using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GuIndependentSite : IExposable
    {
        internal string SiteId;
        internal GuPocketSiteDef Kind;
        internal PlanetTile Geography = PlanetTile.Invalid;
        internal List<GuIndependentLevel> Levels = new();
        internal List<GuIndependentEndpoint> Endpoints = new();
        internal List<GuIndependentRoute> Routes = new();

        internal GuIndependentLevel Level(string id) => string.IsNullOrEmpty(id) ? null : Levels.Find(level => level?.LevelId == id);
        internal GuIndependentEndpoint Endpoint(string id) => string.IsNullOrEmpty(id) ? null : Endpoints.Find(end => end?.EndpointId == id);

        internal GuIndependentEndpoint DestinationFor(string endpointId)
        {
            if (string.IsNullOrEmpty(endpointId)) return null;
            var links = Routes.Where(route => route?.Other(endpointId) != null).Take(2).ToArray();
            return links.Length == 1 ? Endpoint(links[0].Other(endpointId)) : null;
        }

        internal bool CanReachSurface(string startLevel, Func<GuIndependentEndpoint, bool> usable)
        {
            if (Level(startLevel) == null) return false;
            var visited = new HashSet<string> { startLevel };
            var queue = new Queue<string>();
            queue.Enqueue(startLevel);
            while (queue.TryDequeue(out string level))
            {
                foreach (var start in Endpoints.Where(end => end?.LevelId == level && usable(end)))
                {
                    var end = DestinationFor(start.EndpointId);
                    if (end == null || !usable(end)) continue;
                    if (end.IsSurface) return true;
                    if (Level(end.LevelId) != null && visited.Add(end.LevelId)) queue.Enqueue(end.LevelId);
                }
            }
            return false;
        }

        internal static GuIndependentSite FirstLevel(GuPocketSiteDef kind, Map surface, Building_GuPocketProbeEndpoint entrance)
        {
            var site = new GuIndependentSite { SiteId = NewId(), Kind = kind, Geography = surface.Tile };
            var level = new GuIndependentLevel { LevelId = NewId() };
            var outside = new GuIndependentEndpoint { EndpointId = NewId(), Surface = surface, Anchor = entrance, Cell = entrance.Position };
            var inside = new GuIndependentEndpoint { EndpointId = NewId(), LevelId = level.LevelId };
            site.Levels.Add(level);
            site.Endpoints.Add(outside);
            site.Endpoints.Add(inside);
            site.Routes.Add(new GuIndependentRoute { RouteId = NewId(), First = outside.EndpointId, Second = inside.EndpointId });
            return site;
        }

        private static string NewId() => Guid.NewGuid().ToString("N");

        public void ExposeData()
        {
            Scribe_Values.Look(ref SiteId, "siteId");
            Scribe_Defs.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Geography, "geography", PlanetTile.Invalid);
            Scribe_Collections.Look(ref Levels, "levels", LookMode.Deep);
            Scribe_Collections.Look(ref Endpoints, "endpoints", LookMode.Deep);
            Scribe_Collections.Look(ref Routes, "routes", LookMode.Deep);
            Levels ??= new();
            Endpoints ??= new();
            Routes ??= new();
        }
    }

    public sealed class GuIndependentLevel : IExposable
    {
        internal string LevelId;
        internal GuUndergroundLevelParent Parent;
        // A true value with a missing parent or map is a closed, unresolved generation.
        internal bool GenerationStarted;

        internal Map LiveMap
        {
            get
            {
                if (Parent?.Spawned != true || Parent.Destroyed || Parent.LevelId != LevelId) return null;
                if (!Find.WorldGrid.PlanetLayers.Values.OfType<GuUndergroundAddressLayer>().Any(layer => layer.Owns(Parent.Tile)))
                    return null;
                Map map = Parent.Map;
                return GuPocketProbeComponent.IsLive(map) && !map.IsPocketMap && map.Parent == Parent
                    && map.generatorDef == Parent.Generator ? map : null;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref LevelId, "levelId");
            Scribe_References.Look(ref Parent, "parent");
            Scribe_Values.Look(ref GenerationStarted, "generationStarted");
        }
    }

    public sealed class GuIndependentEndpoint : IExposable
    {
        internal string EndpointId;
        internal string LevelId;
        internal Map Surface;
        internal Building_GuPocketProbeEndpoint Anchor;
        internal IntVec3 Cell = IntVec3.Invalid;
        internal bool IsSurface => LevelId == null;

        internal Map Resolve(GuIndependentSite site) => IsSurface
            ? GuPocketProbeComponent.IsSurfaceSettlement(Surface) ? Surface : null
            : site.Level(LevelId)?.LiveMap;

        internal bool HasAnchor(GuIndependentSite site) =>
            Anchor?.Spawned == true && Anchor.Map == Resolve(site);

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && Anchor?.Destroyed == true) Anchor = null;
            Scribe_Values.Look(ref EndpointId, "endpointId");
            Scribe_Values.Look(ref LevelId, "levelId");
            Scribe_References.Look(ref Surface, "surface");
            Scribe_References.Look(ref Anchor, "anchor");
            Scribe_Values.Look(ref Cell, "cell", IntVec3.Invalid);
        }
    }

    public sealed class GuIndependentRoute : IExposable
    {
        internal string RouteId;
        internal string First;
        internal string Second;

        internal string Other(string id) => First == id ? Second : Second == id ? First : null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref RouteId, "routeId");
            Scribe_Values.Look(ref First, "first");
            Scribe_Values.Look(ref Second, "second");
        }
    }
}
