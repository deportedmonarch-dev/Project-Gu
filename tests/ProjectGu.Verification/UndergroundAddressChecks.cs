using System.Reflection;
using System.Runtime.CompilerServices;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

internal static class UndergroundAddressChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Game game, string directory)
    {
        var world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.grid = new WorldGrid();
        world.worldObjects = new WorldObjectsHolder();
        game.World = world;
        // Reserve zero for the engine's root surface in this isolated world.
        var surface = new PlanetLayer();
        typeof(PlanetLayer).GetField("isRootSurface", Fields).SetValue(surface, true);
        ((IDictionary<int, PlanetLayer>)world.grid.PlanetLayers).Add(0, surface);
        typeof(WorldGrid).GetField("nextLayerId", Fields).SetValue(world.grid, 1);
        var biome = new BiomeDef { defName = "UGFixtureBiome", shortHash = 317 };
        DefDatabase<BiomeDef>.Add(biome);
        var layerDef = new PlanetLayerDef
        {
            defName = "GU_UndergroundAddresses", layerType = typeof(GuUndergroundAddressLayer),
            tileType = typeof(GuUndergroundAddressTile)
        };
        typeof(PlanetLayerDef).GetField("defaultBiome", Fields).SetValue(layerDef, biome);
        DefDatabase<PlanetLayerDef>.Add(layerDef);
        var parentDef = new WorldObjectDef { defName = "GU_UndergroundLevel", worldObjectClass = typeof(GuUndergroundLevelParent), saved = true };
        DefDatabase<WorldObjectDef>.Add(parentDef);
        var generator = new MapGeneratorDef { defName = "UGFixtureGenerator" };
        DefDatabase<MapGeneratorDef>.Add(generator);

        GuUndergroundAddressLayer layer = GuUndergroundAddressLayer.Register(world.grid, layerDef);
        Check(layer.LayerID == 1 && world.grid.PlanetLayers.Count == 2 && layer.TilesCount == 32
            && !layer.Visible && !layer.CanReachLayer && !layer.CanSelectLayer().Accepted && layer.Connections.Count == 0,
            "One small portal-only layer");
        var parents = new List<GuUndergroundLevelParent>();
        var maps = new List<Map>();
        typeof(Game).GetField("maps", Fields).SetValue(game, maps);
        for (int i = 0; i < 3; i++)
        {
            PlanetTile address = layer.AvailableAddress();
            Check(layer.Owns(address) && address.tileId == i && address != (PlanetTile)i, "A distinct non-surface address");
            var parent = new GuUndergroundLevelParent
            {
                def = parentDef, ID = 210 + i, LevelId = $"fixture-{i}", Generator = generator
            };
            typeof(WorldObject).GetField("tile", Fields).SetValue(parent, address);
            parents.Add(parent);
            world.worldObjects.AllWorldObjects.Add(parent);
            world.worldObjects.MapParents.Add(parent);
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            map.info = new MapInfo { parent = parent, Size = new IntVec3(31, 1, 31) };
            maps.Add(map);
            Check(game.FindMap(address) == map && world.worldObjects.MapParentAt(address) == parent && parent.Map == map,
                "Native address and identity lookups agree");
        }

        string path = Path.Combine(directory, "underground-address-fixture.xml");
        var site = MakeSite(parents);
        VerifyRouteLoss(site);
        VerifyLegacyProtection(game, maps);
        try
        {
            var mapInfo = maps.Select(map => map.info).ToList();
            Scribe.saver.InitSaving(path, "verification");
            Scribe_Deep.Look(ref layer, "layer");
            Scribe_Collections.Look(ref parents, "parents", LookMode.Deep);
            Scribe_Collections.Look(ref mapInfo, "mapInfo", LookMode.Deep);
            Scribe_Deep.Look(ref site, "site");
            Scribe.saver.FinalizeSaving();

            GuUndergroundAddressLayer restored = null;
            List<GuUndergroundLevelParent> restoredParents = null;
            List<MapInfo> restoredInfo = null;
            GuIndependentSite restoredSite = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref restored, "layer");
            Scribe_Collections.Look(ref restoredParents, "parents", LookMode.Deep);
            Scribe_Collections.Look(ref restoredInfo, "mapInfo", LookMode.Deep);
            Scribe_Deep.Look(ref restoredSite, "site");
            Scribe.loader.FinalizeLoading();

            Check(restored.LayerID == layer.LayerID && restored.TilesCount == layer.TilesCount
                && restored.Tiles.All(tile => tile.PrimaryBiome == biome && tile.temperature == 19f),
                "Scribe restores the layer, tile climate and biome");
            for (int i = 0; i < parents.Count; i++)
                Check(restoredParents[i].Tile == parents[i].Tile && restoredParents[i].LevelId == parents[i].LevelId
                    && restoredParents[i].MapGeneratorDef == generator && restoredInfo[i].parent == restoredParents[i],
                    "Scribe restores parent identity, generator, address and map reference");
            Check(restoredSite.SiteId == site.SiteId && restoredSite.Geography == site.Geography
                && restoredSite.Levels.Take(3).Select(level => level.Parent).SequenceEqual(restoredParents)
                && restoredSite.Level("unresolved").GenerationStarted && restoredSite.Level("unresolved").Parent == null,
                "Retain saved site, world geography and unresolved generation state");
            VerifyRouteLoss(restoredSite);
        }
        finally
        {
            File.Delete(path);
        }
        Console.WriteLine("PASS: native registration and Scribe of a 32-address layer, three independent parents and map bindings.");
        Console.WriteLine("PASS: independent route loss and alternate routes, unresolved generation, and legacy source protection policy.");
    }

    private static GuIndependentSite MakeSite(List<GuUndergroundLevelParent> parents)
    {
        var site = new GuIndependentSite
        {
            SiteId = "fixture-site",
            Geography = (PlanetTile)11,
            Levels = parents.Select(parent => new GuIndependentLevel
                { LevelId = parent.LevelId, Parent = parent, GenerationStarted = true }).ToList()
        };
        site.Levels.Add(new GuIndependentLevel { LevelId = "unresolved", GenerationStarted = true });
        AddRoute("surface-a", null, "level-a", parents[0].LevelId);
        AddRoute("down-a", parents[0].LevelId, "up-a", parents[1].LevelId);
        AddRoute("down-b", parents[1].LevelId, "up-b", parents[2].LevelId);
        AddRoute("surface-b", null, "level-b", parents[2].LevelId);
        return site;

        void AddRoute(string first, string firstLevel, string second, string secondLevel)
        {
            site.Endpoints.Add(new GuIndependentEndpoint { EndpointId = first, LevelId = firstLevel });
            site.Endpoints.Add(new GuIndependentEndpoint { EndpointId = second, LevelId = secondLevel });
            site.Routes.Add(new GuIndependentRoute { RouteId = first + second, First = first, Second = second });
        }
    }

    private static void VerifyRouteLoss(GuIndependentSite site)
    {
        Check(site.CanReachSurface("fixture-0", end => end.EndpointId != "surface-a"),
            "An alternate surface route through three levels remains usable");
        Check(!site.CanReachSurface("fixture-0", end => end.EndpointId is not ("surface-a" or "surface-b")),
            "No surface route after both exits close");
        Check(!site.CanReachSurface("fixture-0", end => end.EndpointId is not ("surface-a" or "down-b"))
            && site.Levels.Count == 4, "Broken internal leg closes egress without removing a level");
    }

    private static void VerifyLegacyProtection(Game game, List<Map> maps)
    {
        var registry = new GuPocketProbeComponent(game);
        var pocket = new GuPocketSiteRecord { Source = maps[0] };
        typeof(GuPocketProbeComponent).GetField("sites", Fields).SetValue(registry, new List<GuPocketSiteRecord> { pocket });
        Check(!registry.ProtectsLegacySource(maps[0]) && !registry.HasLegacyDependents,
            "Unused legacy entrance leaves vanilla source removal available");
        pocket.GenerationStarted = true;
        pocket.Cavern = maps[1];
        Check(registry.ProtectsLegacySource(maps[0]) && registry.ProtectsLegacyLevel(maps[1])
            && registry.HasLegacyDependents && !registry.ProtectsLegacySource(maps[2]),
            "Generated legacy level and its source are protected independently of a passage");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
