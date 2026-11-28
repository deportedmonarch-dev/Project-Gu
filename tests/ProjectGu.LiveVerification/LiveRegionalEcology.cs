using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool RegionalEcology => GenCommandLine.CommandLineArgPassed("guregionalecology");
    private static string RegionalStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-regional-ecology.json");
    private record RegionalState(int Tile, string Region, int Consumed, int Interval, int Ordinal, long DonorRevision,
        int DonorCount, string BlackResident, string WhiteResident, int Maps, int WorldObjects);

    private static void RegionalTick(int elapsed = 250)
    {
        int tick = ((Now + elapsed + 249) / 250) * 250;
        if (tick % 2500 == 0) tick += 250;
        Find.TickManager.DebugSetTicksGame(tick);
        GuSurfaceEcology.Instance.GameComponentTick();
    }

    private static Settlement RegionalSettlement(PlanetTile tile)
    {
        var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile = tile;
        settlement.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(settlement);
        GenMapParent(settlement);
        return settlement;
    }

    private static void ExerciseRegionalEcology()
    {
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        var ecology = GuSurfaceEcology.Instance;
        var blackDef = DefDatabase<GuSpeciesDef>.GetNamed(BlackSpecies);
        var whiteDef = DefDatabase<GuSpeciesDef>.GetNamed(WhiteSpecies);
        var stoneDef = DefDatabase<GuSpeciesDef>.GetNamed(StoneSpecies);
        var home = Find.CurrentMap;
        var nativeTile = Find.WorldGrid.Surface.Tiles.First(tile =>
        {
            if (tile.PrimaryBiome != BiomeDef.Named("TemperateForest") || tile.hilliness != Hilliness.Flat
                || !TileFinder.IsValidTileForNewSettlement(tile.tile) || Find.WorldObjects.AnyWorldObjectAt(tile.tile)) return false;
            var bucket = GuSurfaceEcology.RegionOf(tile.tile);
            return GuSurfaceEcology.DonorWitness(tile.tile, blackDef, bucket) != null
                && GuSurfaceEcology.DonorWitness(tile.tile, stoneDef, bucket) == null;
        });
        var tile = nativeTile.tile;
        var region = GuSurfaceEcology.RegionOf(tile);
        var otherLayer = GuUndergroundAddressLayer.Resolve(Find.WorldGrid, create: true).AvailableAddress();
        Check(GuSurfaceEcology.DonorWitness(tile, blackDef, region + "/different") == null
            && GuSurfaceEcology.RegionOf(otherLayer) == null && GuSurfaceEcology.DonorWitness(otherLayer, blackDef, region) == null,
            "An incompatible region or a real underground-layer address cannot supply a surface donor");
        nativeTile.PrimaryBiome = BiomeDef.Named("Desert");
        var settlement = RegionalSettlement(tile);
        var map = settlement.Map;
        RegionalTick();
        var black = ecology.LocalityAt(tile, BlackSpecies);
        var white = ecology.LocalityAt(tile, WhiteSpecies);
        var stone = ecology.LocalityAt(tile, StoneSpecies);
        Check(new[] { black, white, stone }.All(row => row is { FoundingCount: 0, Established: false, Blocked: GuSurfaceEcology.BlockedBy.Habitat }),
            "A native desert observation records three independent initialized absences");
        Check(ecology.RegionalPopulation(region, BlackSpecies) == null, "Observation alone realizes no regional donor");
        Current.Game.CurrentMap = home;
        Current.Game.DeinitAndRemoveMap(map, false);
        stone.EvidenceTick = -1; // Simulate a locality whose reliable sampling record is unavailable.
        RegionalTick(10 * GuSurfaceEcology.TicksPerDay);
        Check(new[] { black, white, stone }.All(row => row.Cohort.TotalCount == 0 && row.ReplacementTicks == -1)
            && stone.Blocked == GuSurfaceEcology.BlockedBy.Unknown && black.Blocked == GuSurfaceEcology.BlockedBy.Habitat,
            "Hard-ineligible and unknown unloaded rows collect no stock or recovery latch");
        nativeTile.PrimaryBiome = BiomeDef.Named("TemperateForest");
        GenMapParent(settlement);
        map = settlement.Map;
        var localRocks = Find.World.NaturalRockTypesIn(tile).ToArray();
        foreach (var existing in localRocks.SelectMany(rock => map.listerThings.ThingsOfDef(rock)).ToArray()) existing.Destroy();
        RegionalTick();
        Check(black.Cohort.TotalCount == 0 && white.Cohort.TotalCount == 0 && black.ReplacementTicks >= 3 * GuSurfaceEcology.TicksPerDay
            && stone.Blocked == GuSurfaceEcology.BlockedBy.Habitat, "Restoration starts Boar time without founding stock. Flat forest alone cannot support Stone Skin");
        int mapCount = Find.Maps.Count;
        int worldObjects = Find.WorldObjects.AllWorldObjects.Count;
        black.ReplacementTicks = white.ReplacementTicks = 250;
        RegionalTick();
        foreach (var row in new[] { black, white })
        {
            var receipt = row.Receipts.Single();
            var donor = ecology.RegionalPopulation(region, row.Cohort.SpeciesDefName);
            Check(receipt.Source == GuSurfaceSource.Migration && receipt.Donor == region
                && receipt.DonorRevision == donor.Cohort.Revision && receipt.RecipientRevision == row.Cohort.Revision
                && row.Cohort.MatureCount == 1 && donor.FoundingCount is >= 1 and <= 3
                && donor.Cohort.MatureCount == donor.FoundingCount - 1, "Colonization debits one finite donor and credits one recipient");
        }
        ecology.GameComponentTick();
        Check(black.Receipts.Count == 1 && white.Receipts.Count == 1 && Find.Maps.Count == mapCount
            && Find.WorldObjects.AllWorldObjects.Count == worldObjects, "A repeated same-epoch touch commits no second transfer and generates no map or world object");

        var cell = map.AllCells.First(candidate => GuSurfaceEcology.SearchGround(map, candidate)
            && GuIndividualRegistry.CanPlaceDormant(map, candidate, null));
        GenSpawn.Spawn(localRocks[0], cell, map);
        RegionalTick();
        Check(stone.ExposedRock && stone.HabitatWeight == 1 && stone.Cohort.MatureCount == 0,
            "A measured exposed face restores eligibility without granting stock");
        stone.ReplacementTicks = 250;
        RegionalTick();
        Check(stone.Receipts.Single().Source == GuSurfaceSource.NaturalFormation && stone.Cohort.MatureCount == 1,
            "Declared formation uses current local rock evidence when no regional Stone Skin donor qualifies");

        string Introduce(GuSpeciesDef species, bool escape)
        {
            var placement = map.AllCells.First(candidate => GuIndividualRegistry.CanPlaceDormant(map, candidate, null));
            Check(IndividualRegistry.TrySpawnDevelopmentGu(species, map, placement, configuredSpecies: true, out var wild),
                "An independently registered fixture individual exists before ecological admission");
            var record = IndividualRegistry.Resolve(wild);
            int count = IndividualRegistry.Records.Count;
            int revision = record.CustodyRevision;
            Check(!ecology.TryAdmitEscape(record.Id, revision, map) && !ecology.TryIntroduceExisting(record.Id, revision - 1, map),
                "Escape without evidence and introduction with stale custody are refused");
            if (escape)
            {
                record.AddHistory(new(Now, GuLifecycleEventKind.Escaped, "FixtureEscape", null, null, null, null));
                record.RecordEscapePlacement();
            }
            bool admitted = escape ? ecology.TryAdmitEscape(record.Id, revision, map) : ecology.TryIntroduceExisting(record.Id, revision, map);
            Check(admitted && !ecology.TryIntroduceExisting(record.Id, revision, map)
                && IndividualRegistry.Records.Count == count && IndividualRegistry.Resolve(wild) == record,
                "Admission preserves the registered ID and rejects a second membership");
            return record.Id;
        }
        string blackResident = Introduce(blackDef, escape: false);
        string whiteResident = Introduce(whiteDef, escape: true);
        Current.Game.CurrentMap = home;
        Current.Game.DeinitAndRemoveMap(map, false);
        Find.TickManager.DoSingleTick();
        Check(black.Unloaded && stone.Unloaded && IndividualRegistry.Resolve(blackResident)?.ContainerKind == GuContainerKind.WorldSite,
            "Native teardown recorded final habitat and retained the living world-site Gu");
        var donorBlack = ecology.RegionalPopulation(region, BlackSpecies);
        int halfInterval = black.ReplacementTicks / 2;
        RegionalTick(halfInterval);
        // Due work has not settled this partial interval. Its saved cursor plus remaining time locate the same deadline.
        Check(black.Cohort.MatureCount == 1 && Now > black.ConsumedTick && Now < black.ConsumedTick + black.ReplacementTicks,
            "The save checkpoint is midway to an unloaded replacement");
        File.WriteAllText(RegionalStatePath, JsonSerializer.Serialize(new RegionalState(tile.tileId, region, black.ConsumedTick,
            black.ReplacementTicks, black.ReplacementOrdinal, donorBlack.Cohort.Revision, donorBlack.Cohort.MatureCount, blackResident,
            whiteResident, Find.Maps.Count, Find.WorldObjects.AllWorldObjects.Count)));
        AssertNoErrors("regional new");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifyRegionalEcologyReload()
    {
        var state = JsonSerializer.Deserialize<RegionalState>(File.ReadAllText(RegionalStatePath));
        var tile = Find.WorldGrid.Surface.PlanetTileForID(state.Tile);
        var ecology = GuSurfaceEcology.Instance;
        var black = ecology.LocalityAt(tile, BlackSpecies);
        var white = ecology.LocalityAt(tile, WhiteSpecies);
        var stone = ecology.LocalityAt(tile, StoneSpecies);
        var donor = ecology.RegionalPopulation(state.Region, BlackSpecies);
        Check(GuSurfaceEcology.RegionOf(tile) == state.Region && black.ConsumedTick == state.Consumed
            && black.ReplacementTicks == state.Interval && black.ReplacementOrdinal == state.Ordinal
            && donor.Cohort.Revision == state.DonorRevision && donor.Cohort.MatureCount == state.DonorCount,
            "A fresh process preserved regional identity, post-transfer stock and the outstanding local cursor");
        Check(black.Receipts.Count(receipt => receipt.Source == GuSurfaceSource.Migration) == 1
            && black.Receipts.Any(receipt => receipt.Source == GuSurfaceSource.Introduction && receipt.Individual == state.BlackResident)
            && white.Receipts.Any(receipt => receipt.Source == GuSurfaceSource.Escape && receipt.Individual == state.WhiteResident),
            "Migration and exact-ID membership evidence survived save/load");
        Check(IndividualRegistry.Resolve(state.BlackResident)?.ContainerKind == GuContainerKind.WorldSite
            && IndividualRegistry.Resolve(state.WhiteResident)?.ContainerKind == GuContainerKind.WorldSite, "Both resident IDs survived without a loaded map");

        RegionalTick(state.Consumed + state.Interval - Now);
        Check(black.Cohort.MatureCount == 2 && black.Receipts.Last().Source == GuSurfaceSource.NaturalReplenishment
            && black.ReplacementTicks > 0, "The unloaded path used the original deadline and replenished above threshold");
        int blackDue = black.ConsumedTick + black.ReplacementTicks;
        RegionalTick(blackDue - Now);
        Check(black.Cohort.MatureCount == 3 && black.ReplacementTicks == -1,
            "Capacity four includes the living Black Boar world-site resident");

        long revision = black.Cohort.Revision;
        long discarded = stone.DiscardedTicks;
        RegionalTick(70 * GuSurfaceEcology.TicksPerDay);
        Check(black.Cohort.Revision == revision && white.Cohort.MatureCount == 3 && stone.Cohort.MatureCount == 4
            && stone.DiscardedTicks - discarded >= 40 * GuSurfaceEcology.TicksPerDay
            && Find.Maps.Count == state.Maps && Find.WorldObjects.AllWorldObjects.Count == state.WorldObjects,
            "All three species settled independently, with a bounded overdue window and zero map or world object generation");
        var settlement = Find.WorldObjects.Settlements.Single(site => site.Tile == tile);
        settlement.Destroy();
        long beforeDestroy = stone.Cohort.Revision;
        RegionalTick(10 * GuSurfaceEcology.TicksPerDay);
        Check(stone.Blocked == GuSurfaceEcology.BlockedBy.Destroyed && stone.Cohort.Revision == beforeDestroy,
            "Native destruction blocks the original locality without minting a substitute");
        var home = Find.CurrentMap;
        settlement = RegionalSettlement(tile);
        var map = settlement.Map;
        RegionalTick();
        ecology.GameComponentTick();
        Check(ecology.LocalityAt(map, BlackSpecies) == black && black.ReplacementTicks == -1 && black.Cohort.Revision == revision
            && black.Blocked == GuSurfaceEcology.BlockedBy.None, "New reliable map evidence restores the original row without replaying founding stock or a transfer");
        Current.Game.CurrentMap = home;
        Report(ecology.Diagnostics());
        AssertNoErrors("regional reload");
    }
}
