using System.IO;
using System.Linq;
using System.Text.Json;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool SurfaceManifestation => GenCommandLine.CommandLineArgPassed("gusurfacemanifestation");
    private static string ManifestationStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-surface-manifestation.json");
    private sealed record ManifestationState(int Tile, string[] PhysicalIds, int[] Adults, long[] Revisions, string Returned, int Custody);
    private static readonly string[] ManifestationSpecies = { BlackSpecies, WhiteSpecies, StoneSpecies };

    private static Pawn_GuWildProjection[] SurfaceBodies(Map map) => map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>()
        .Where(pawn => IndividualRegistry.Resolve(pawn)?.Species?.HasSurfacePopulation == true).ToArray();

    private static void ExerciseSurfaceManifestation()
    {
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        var registry = IndividualRegistry;
        var ecology = GuSurfaceEcology.Instance;
        var settings = ProjectGuMod.Settings;
        var home = Find.CurrentMap;
        var nativeTile = Find.WorldGrid.Surface.Tiles.First(candidate => candidate.PrimaryBiome.defName == "TemperateForest"
            && candidate.hilliness is Hilliness.SmallHills or Hilliness.LargeHills
            && TileFinder.IsValidTileForNewSettlement(candidate.tile) && !Find.WorldObjects.AnyWorldObjectAt(candidate.tile));
        var map = RegionalSettlement(nativeTile.tile).Map;
        Current.Game.CurrentMap = home;
        RegionalTick();
        var rows = ManifestationSpecies.Select(species => ecology.LocalityAt(map, species)).ToArray();
        foreach (var row in rows)
            while (row.Cohort.TryRecruitMature(4, 0)) { }
        var camera = SurfaceCameraTarget();
        int start = Now;
        int previousBudget = settings.localityManifestationBudget;
        var bounds = new CellRect(0, 0, map.Size.x, map.Size.z);
        try
        {
            map.fogGrid.Refog(bounds);
            settings.localityManifestationBudget = 4;
            for (int i = 0; i < 5; i++) EncounterSample(ecology, Now + 1);
            Check(SurfaceBodies(map).Length == 4 && rows.Sum(row => row.Cohort.MatureCount) == 8,
                "The total locality limit caps ambient manifestation at four paid residents across three independent species");
            settings.localityManifestationBudget = 6;
            for (int i = 0; i < 5; i++) EncounterSample(ecology, Now + 1);
            var initial = SurfaceBodies(map);
            Check(initial.Length == 6 && rows.All(row => row.Cohort.MatureCount == 2 && row.Cohort.ReceiptDebits == 2)
                && ManifestationSpecies.All(species => initial.Count(pawn => registry.Resolve(pawn).SpeciesDefName == species) == 2),
                "Four original units per species yielded two physical residents and two anonymous adults per species");
            Check(initial.All(pawn => pawn.Position.Fogged(map) && GuSurfaceEcology.AmbientHabitat(map, pawn.Position,
                    registry.Resolve(pawn).Species.surfaceHabitat) && !GuSurfaceEcology.SearchGround(map, pawn.Position))
                && map.AllCells.All(cell => cell.Fogged(map)) && Find.CurrentMap == home && SurfaceCameraTarget() == camera
                && !LiveMessages().Any(message => message.startingTick >= start && message.text.Contains("wild")),
                "Native habitat manifestation preserved fog, camera position and discovery visibility");

            var target = initial.First(pawn => registry.Resolve(pawn).SpeciesDefName == BlackSpecies);
            var record = registry.Resolve(target);
            var targetCell = target.Position;
            foreach (var pawn in initial.Where(pawn => pawn != target))
                Check(registry.Resolve(pawn).RenewEncounterReference("fixture:retention", Now, Now + 20 * GuSurfaceEcology.TicksPerDay),
                    "Verified a named lease for an existing physical resident");
            var actor = home.mapPawns.FreeColonistsSpawned.First();
            var actorOrigin = actor.Position;
            var actorCell = GenAdj.CardinalDirections.Select(offset => targetCell + offset)
                .First(cell => GuIndividualRegistry.CanPlaceSurfaceWild(map, cell, null));
            map.fogGrid.Unfog(actorCell);
            TransferToFixture(actor, map, actorCell);
            map.fogGrid.Refog(new CellRect(targetCell.x, targetCell.z, 1, 1));
            var black = rows[0].Cohort;
            long revision = black.Revision;
            int recordCount = registry.Records.Count;
            Check(GuSurfaceEcology.FindWildGu(actor, targetCell, 0.1f, record.Species) == null && targetCell.Fogged(map),
                "Environmental search leaves a fogged Gu and its coordinates hidden");
            map.fogGrid.Unfog(targetCell);
            Check(GuSurfaceEcology.FindWildGu(actor, targetCell, 0.1f, record.Species) == target
                && target.Position == targetCell && registry.Records.Count == recordCount && black.Revision == revision
                && Find.CurrentMap == home && SurfaceCameraTarget() == camera,
                "Detection locates the existing accessible Pawn without a new debit, movement or camera change");
            registry.NoteSurfaceSearchResult(target);

            var white = rows[1];
            Check(white.Cohort.TryConsumeMature(1), "Fixture reserves one habitat slot for an independent significant arrival");
            var whiteCell = map.AllCells.First(cell => GuIndividualRegistry.CanPlaceSurfaceWild(map, cell, null));
            map.fogGrid.Unfog(whiteCell);
            Check(registry.TrySpawnDevelopmentGu(DefDatabase<GuSpeciesDef>.GetNamed(WhiteSpecies), map, whiteCell,
                configuredSpecies: true, out var thirdWhite), "A previously existing third White Boar enters the map through native placement");
            var protectedWhite = registry.Resolve(thirdWhite);
            Check(ecology.TryIntroduceExisting(protectedWhite.Id, protectedWhite.CustodyRevision, map)
                && protectedWhite.RenewEncounterReference("fixture:rare-arrival", Now, Now + 20 * GuSurfaceEcology.TicksPerDay),
                "The significant arrival occupies the same White Boar habitat independently of the ambient cap");
            TransferToFixture(actor, home, actorOrigin);
            settings.localityManifestationBudget = 2;
            EncounterSample(ecology, Now + 1);
            Check(SurfaceBodies(map).Length == 7 && thirdWhite.Spawned && registry.Resolve(thirdWhite) == protectedWhite,
                "Lowering a cap preserves exact existing residents, including three White Boars");

            int custody = record.CustodyRevision;
            int day = GuSurfaceEcology.TicksPerDay;
            Find.TickManager.DebugSetTicksGame(Now + day);
            Check(registry.TryReturnSurfaceResident(record.Id, custody, map, out _) && black.MatureCount == 3
                && SurfaceBodies(map).Length == 6, "Returning one ordinary Boar freed one physical slot and credited one adult");
            settings.localityManifestationBudget = 7;
            EncounterSample(ecology, Now + day);
            var after = SurfaceBodies(map);
            Check(after.Length == 7 && after.All(pawn => pawn.GuId != record.Id) && registry.Resolve(thirdWhite) == protectedWhite
                && black.MatureCount == 2 && black.ReceiptDebits == 3 && black.ReturnCredits == 1,
                "Remanifestation used a new ID and spent exactly the returned adult while retaining the over-cap Gu");
            map.fogGrid.Refog(bounds);
            File.WriteAllText(ManifestationStatePath, JsonSerializer.Serialize(new ManifestationState(map.Tile.tileId,
                after.Select(pawn => pawn.GuId).Order().ToArray(), rows.Select(row => row.Cohort.MatureCount).ToArray(),
                rows.Select(row => row.Cohort.Revision).ToArray(), record.Id, custody)));
            AssertNoErrors("manifestation new");
            GameDataSaveLoader.SaveGame(SaveName);
            Report($"Surface fixture has {after.Length} active projections, six significant residents, one ordinary resident and one aggregate tombstone."
                + $" Current-format save size {new FileInfo(GenFilePaths.FilePathForSavedGame(SaveName)).Length} bytes.");
        }
        finally { settings.localityManifestationBudget = previousBudget; }
    }

    private static void VerifySurfaceManifestationReload()
    {
        var state = JsonSerializer.Deserialize<ManifestationState>(File.ReadAllText(ManifestationStatePath));
        var map = Current.Game.FindMap(Find.WorldGrid.Surface.PlanetTileForID(state.Tile));
        var bodies = SurfaceBodies(map);
        var rows = ManifestationSpecies.Select(species => GuSurfaceEcology.Instance.LocalityAt(map, species)).ToArray();
        Check(bodies.Select(pawn => pawn.GuId).Order().SequenceEqual(state.PhysicalIds) && bodies.All(pawn => pawn.Position.Fogged(map))
            && map.AllCells.All(cell => cell.Fogged(map)) && rows.Select(row => row.Cohort.MatureCount).SequenceEqual(state.Adults)
            && rows.Select(row => row.Cohort.Revision).SequenceEqual(state.Revisions),
            "Fresh-process load preserved every projection ID, hidden cell and species balance");
        Check(IndividualRegistry.TryReturnSurfaceResident(state.Returned, state.Custody, map, out _)
            && IndividualRegistry.Resolve(state.Returned) == null && rows[0].Cohort.ReturnCredits == 1,
            "The saved return replays once with no obsolete Pawn or additional population credit");
        Check(IndividualRegistry.ReconciliationQueueCount == IndividualRegistry.Records.Count(record => record.IsLiving),
            "The fresh-process reconciliation queue contains living individuals and excludes tombstones");
        Report($"Current-format load restored {bodies.Length} active site projections and {IndividualRegistry.ReconciliationQueueCount} living reconciliation entries across the fixture.");
        AssertNoErrors("manifestation reload");
    }
}
