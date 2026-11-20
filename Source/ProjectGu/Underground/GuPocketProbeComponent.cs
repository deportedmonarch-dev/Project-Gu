using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GuPocketSiteRecord : IExposable
    {
        internal string SiteId;
        internal string LevelId;
        internal GuPocketSiteDef Kind;
        internal Map Source;
        internal Map Cavern;
        internal Building_GuPocketProbeEndpoint Entrance;
        internal Building_GuPocketProbeEndpoint Exit;
        internal bool GenerationStarted;

        internal string Status => Cavern != null && GuPocketProbeComponent.HasValidRoute(this)
            ? "Cavern bound"
            : GenerationStarted ? "Route closed" : "Cavern pending";

        public void ExposeData()
        {
            Scribe_Values.Look(ref SiteId, "siteId");
            Scribe_Values.Look(ref LevelId, "levelId");
            Scribe_Defs.Look(ref Kind, "kind");
            Scribe_References.Look(ref Source, "source");
            Scribe_References.Look(ref Cavern, "cavern");
            Scribe_References.Look(ref Entrance, "entrance");
            Scribe_References.Look(ref Exit, "exit");
            Scribe_Values.Look(ref GenerationStarted, "generationStarted");
        }
    }

    public sealed partial class GuPocketProbeComponent : GameComponent
    {
        private List<GuPocketSiteRecord> sites = new();
        private readonly HashSet<GuPocketSiteRecord> reportedInvalidSites = new();
        [ThreadStatic] private static HashSet<Thing> generationFixtures;
        internal bool OperationInProgress { get; set; }

        public GuPocketProbeComponent(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sites, "guPocketProbeSitesV1", LookMode.Deep);
            sites ??= new List<GuPocketSiteRecord>();
            Scribe_Collections.Look(ref independentSites, "guIndependentUndergroundSitesV1", LookMode.Deep);
            independentSites ??= new();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ReportMissingIndependentLevels();
            foreach (GuPocketSiteRecord site in sites)
                if (site?.Cavern != null && (!HasLiveSourceSettlement(site.Source) || !IsRegisteredPocket(site, site.Cavern)))
                    ReportInvalidSite(site);
        }

        internal void NotifyMapRemoved(Map removedMap)
        {
            NotifyIndependentMapRemoved(removedMap);
            foreach (GuPocketSiteRecord site in sites)
            {
                if (site?.Source == removedMap && !site.GenerationStarted)
                {
                    site.Source = null;
                    site.Entrance = null;
                }
                if (site?.Cavern != null
                    && (site.Source == removedMap || (site.Cavern == removedMap && !GuUndergroundLifecycleGuards.IsRejectedPocket(removedMap))))
                    ReportInvalidSite(site);
            }
        }

        internal void NotifyEndpointRemoved(Building_GuPocketProbeEndpoint anchor)
        {
            if (TryGetIndependentEndpoint(anchor, out _, out var end)) end.Anchor = null;
            if (TryGetSite(anchor, out var legacy))
            {
                if (legacy.Entrance == anchor) legacy.Entrance = null;
                if (legacy.Exit == anchor) legacy.Exit = null;
            }
        }

        private void ReportInvalidSite(GuPocketSiteRecord site)
        {
            if (reportedInvalidSites.Add(site))
                Log.Error($"[Project GU] Persistent underground route closed for site {site.SiteId}, level {site.LevelId}. "
                    + "Its source or cavern map binding became invalid. Reload a save from before the removal.");
        }

        internal void AddSite(GuPocketSiteDef kind, Building_GuPocketProbeEndpoint entrance)
        {
            sites.Add(new GuPocketSiteRecord
            {
                SiteId = Guid.NewGuid().ToString("N"),
                LevelId = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Source = entrance.Map,
                Entrance = entrance
            });
        }

        internal static bool TryGetSite(Building_GuPocketProbeEndpoint endpoint, out GuPocketSiteRecord site)
        {
            site = Current.Game?.GetComponent<GuPocketProbeComponent>()?.sites.Find(candidate =>
                candidate != null && (candidate.Entrance == endpoint || candidate.Exit == endpoint));
            return site != null;
        }

        internal static bool IsLive(Map map) => map != null && !map.Disposed && Find.Maps.Contains(map);

        internal bool ProtectsLegacyLevel(Map map) => map != null && sites.Any(site => site?.Cavern == map);

        internal bool ProtectsLegacySource(Map map) => map != null && sites.Any(site =>
            site != null && (site.Cavern != null || site.GenerationStarted)
            && (site.Source == map || site.Cavern?.PocketMapParent?.sourceMap == map));

        internal bool HasLegacyDependents => sites.Any(site =>
            site != null && (site.Cavern != null || site.GenerationStarted));

        internal static bool TryGetCavernArrival(Map map, out IntVec3 arrival)
        {
            var registry = Current.Game?.GetComponent<GuPocketProbeComponent>();
            var site = registry?.sites.Find(candidate =>
                candidate != null && candidate.Cavern == map && candidate.Exit?.Spawned == true
                && candidate.Exit.Map == map);
            arrival = site?.Exit.Position ?? IntVec3.Invalid;
            return arrival.IsValid || registry?.TryGetIndependentArrival(map, out arrival) == true;
        }

        private static bool HasLiveSourceSettlement(Map map) => IsLive(map) && !map.IsPocketMap
            && map.Parent is Settlement { Spawned: true, Destroyed: false };

        internal static bool IsSurfaceSettlement(Map map) => HasLiveSourceSettlement(map)
            && map.Tile.Valid && map.Tile.LayerDef == PlanetLayerDefOf.Surface && map.IsPlayerHome;

        internal static bool HasValidSource(GuPocketSiteRecord site) =>
            IsSurfaceSettlement(site.Source) && site.Entrance?.Spawned == true && site.Entrance.Map == site.Source;

        internal static bool HasValidRoute(GuPocketSiteRecord site) =>
            HasValidSource(site) && IsRegisteredPocket(site, site.Cavern)
            && site.Exit?.Spawned == true && site.Exit.Map == site.Cavern;

        private static bool IsRegisteredPocket(GuPocketSiteRecord site, Map map) =>
            IsLive(map) && map.IsPocketMap && map.Parent is PocketMapParent parent
            && !parent.Destroyed && parent.Map == map && parent.sourceMap == site.Source
            && site.Kind?.generator != null && parent.MapGeneratorDef == site.Kind.generator
            && map.generatorDef == site.Kind.generator
            && Find.World.pocketMaps.Contains(parent);

        internal static Thing SpawnFixture(ThingDef def, IntVec3 cell, Map map)
        {
            Thing fixture = ThingMaker.MakeThing(def);
            Thing spawned = GenSpawn.Spawn(fixture, cell, map, Rot4.North);
            if (spawned != fixture || !fixture.Spawned || fixture.Map != map || fixture.Position != cell)
                throw new InvalidOperationException($"Could not place {def.defName} at {cell}.");
            generationFixtures?.Add(fixture);
            return fixture;
        }

        internal static bool TryGenerate(GuPocketSiteRecord site, out string reason)
        {
            reason = "The probe generator is unavailable or the source is invalid.";
            if (site.GenerationStarted || site.Cavern != null || site.Kind?.IsConfigured != true || !HasValidSource(site))
                return false;
            if (!GuUndergroundLifecycleGuards.Installed)
            {
                reason = "Underground lifecycle protections are unavailable.";
                return false;
            }

            if (site.Kind == GuPocketSiteDef.SpiritSpring && !GuSpringCavernIncidents.GuardsInstalled())
            {
                reason = "The spirit spring cavern requires its infestation guards to be active.";
                return false;
            }

            var oldMaps = new HashSet<Map>(Find.Maps);
            site.GenerationStarted = true;
            int size = site.Kind.mapSize;
            bool reject = site.Entrance.ConsumeGenerationRejection();
            var fixtures = new HashSet<Thing>();
            generationFixtures = fixtures;
            try
            {
                Map map = PocketMapUtility.GeneratePocketMap(new IntVec3(size, 1, size),
                    site.Kind.generator, null, site.Source);
                if (!IsRegisteredPocket(site, map) || oldMaps.Contains(map) || !HasValidSource(site))
                {
                    reason = "The cavern registration failed. The probe route is closed.";
                    return false;
                }

                site.Cavern = map;
                var exits = map.listerThings.ThingsOfDef(site.Kind.ExitDef);
                var exit = exits.Count == 1 ? exits[0] as Building_GuPocketProbeEndpoint : null;
                if (reject || exit?.Spawned != true || exit.Map != map
                    || !JobDriver_GuPocketProbeTravel.CanLand(map, exit.Position, null))
                {
                    string failure = reject ? "Probe generation rejected deliberately." : "The cavern exit failed validation.";
                    // This operation can only discard its newly generated map and its exact fixture instances.
                    if (!map.listerThings.AllThings.All(fixtures.Contains)
                        || map.mapPawns.AllPawns.Any() || map.ChildPocketMaps.Any())
                    {
                        reason = failure + " The cavern contains additional Things. The probe route is closed.";
                        return false;
                    }
                    PocketMapParent parent = map.PocketMapParent;
                    GuUndergroundLifecycleGuards.CleanupRejectedPocket(map);
                    if (!Find.Maps.Contains(map) && !Find.World.pocketMaps.Contains(parent) && map.Disposed)
                    {
                        site.Cavern = null;
                        site.GenerationStarted = false;
                        reason = failure + " The fresh cavern was removed; retry is available.";
                        return false;
                    }
                    reason = "Probe cleanup did not complete. The route is closed.";
                    return false;
                }

                site.Exit = exit;
                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = "Pocket generation or cleanup failed. The traveler stayed on the surface; the route is closed.";
                Log.Error($"[Project GU] {reason} Probe site {site.SiteId}. {exception}");
                return false;
            }
            finally
            {
                generationFixtures = null;
            }
        }
    }

    public sealed class GuPocketMapLifecycleObserver : MapComponent
    {
        public GuPocketMapLifecycleObserver(Map map) : base(map) { }

        public override void MapRemoved() =>
            Current.Game?.GetComponent<GuPocketProbeComponent>()?.NotifyMapRemoved(map);
    }
}
