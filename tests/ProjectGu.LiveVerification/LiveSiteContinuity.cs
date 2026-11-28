using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool SiteContinuity => GenCommandLine.CommandLineArgPassed("gusitecontinuity");
    private static string SiteStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-site-continuity.json");
    private static Map rejectSiteTeardown;
    private const string TeardownFault = "Injected failure before native map teardown";
    private record SiteState(int Tile, string Parent, string Black, int BlackCustody, string White, string WhitePawn,
        string Holder, string Held, int Founding, int Adults, long Revision, int Consumed, int Interval, int Receipts);

    private static void FailBeforeSiteTeardown(Map __0)
    {
        if (__0 != rejectSiteTeardown) return;
        Check(IndividualRegistry.MapTeardownInProgress, "The native teardown prefix captured its custody scope");
        throw new InvalidOperationException(TeardownFault);
    }

    private static void ExerciseSiteContinuity()
    {
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        RelationAudit("before secondary map generation");
        var registry = IndividualRegistry;
        var ecology = GuSurfaceEcology.Instance;
        var home = Find.CurrentMap;
        var tile = Find.WorldGrid.Surface.Tiles.First(candidate => candidate.PrimaryBiome.defName == "TemperateForest"
            && TileFinder.IsValidTileForNewSettlement(candidate.tile) && !Find.WorldObjects.AnyWorldObjectAt(candidate.tile)).tile;
        var parent = RegionalSettlement(tile);
        var map = parent.Map;
        Current.Game.CurrentMap = home;
        RelationAudit("after secondary map generation before Gu admission");
        RegionalTick();
        var blackRow = ecology.LocalityAt(tile, BlackSpecies);

        IntVec3 Open(Map target) => target.AllCells.First(cell => GuIndividualRegistry.CanPlaceDormant(target, cell, null));
        (GuRecord Record, Pawn_GuWildProjection Pawn) Admit(string species)
        {
            Check(registry.TrySpawnDevelopmentGu(DefDatabase<GuSpeciesDef>.GetNamed(species), map, Open(map),
                configuredSpecies: true, out var pawn), "Created one real unowned fixture Gu before ecological admission");
            var record = registry.Resolve(pawn);
            Check(ecology.TryIntroduceExisting(record.Id, record.CustodyRevision, map), "Admitted the existing ID through the habitat boundary");
            return (record, pawn);
        }

        var black = Admit(BlackSpecies);
        var white = Admit(WhiteSpecies);
        Check(black.Record.RenewEncounterReference("fixture:site-tracking", Now, Now + 20 * GuSurfaceEcology.TicksPerDay),
            "The tracked resident has active exact-identity protection");
        var injury = HediffMaker.MakeHediff(HediffDef.Named("Cut"), white.Pawn,
            white.Pawn.health.hediffSet.GetNotMissingParts().First(part => part.parent != null));
        white.Pawn.health.AddHediff(injury);
        var host = home.mapPawns.FreeColonistsSpawned.First();
        TransferToFixture(host, map, Open(map));
        var held = PackTestGu(host);
        var heldRecord = registry.Resolve(held);
        registry.ApplyPendingEvents();

        var fault = new Harmony("projectgu.verification.site-teardown");
        try
        {
            rejectSiteTeardown = map;
            fault.Patch(AccessTools.DeclaredMethod(typeof(MapDeiniter), nameof(MapDeiniter.Deinit)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(FailBeforeSiteTeardown)) { priority = Priority.Last });
            bool failed = false;
            try { Current.Game.DeinitAndRemoveMap(map, false); }
            catch (InvalidOperationException exception) when (exception.Message == TeardownFault) { failed = true; }
            registry.ApplyPendingEvents();
            Check(failed && !registry.MapTeardownInProgress && Find.Maps.Contains(map) && !blackRow.Unloaded
                && registry.Resolve(black.Pawn) == black.Record && registry.Resolve(held) == heldRecord,
                "A failed native teardown leaves loaded habitat and legitimate custody intact");
        }
        finally { fault.UnpatchAll(fault.Id); rejectSiteTeardown = null; }

        blackRow.ReplacementTicks = 2 * GuSurfaceEcology.TicksPerDay;
        Current.Game.DeinitAndRemoveMap(map, false);
        registry.ApplyPendingEvents();
        Check(parent.Spawned && !parent.HasMap && blackRow.Unloaded && black.Record.ContainerKind == GuContainerKind.WorldSite
            && black.Record.LastKnown?.MapParentId == parent.GetUniqueLoadID() && black.Pawn.Discarded,
            "Native unload preserves the settlement, its occupied habitat and the tracked Gu at the exact site");
        Check(registry.Resolve(white.Pawn) == white.Record && Find.WorldPawns.Contains(white.Pawn)
            && white.Pawn.health.hediffSet.hediffs.Contains(injury), "Native world custody preserves the injured body and its condition");
        Check(Find.WorldPawns.Contains(host) && registry.Resolve(held) == heldRecord && held.ParentHolder == host.inventory,
            "A legitimate holder taking Gu through native teardown keeps exact inventory custody");
        RelationAudit("after native unload");

        var impostor = RegionalSettlement(tile);
        Current.Game.CurrentMap = home;
        var unrelatedMap = impostor.Map;
        var before = blackRow.Cohort.Preimage;
        int found = blackRow.FoundingCount, receipts = blackRow.Receipts.Count;
        Check(!black.Record.LastKnown.IsAt(unrelatedMap) && !registry.WorldSiteRecordsAt(unrelatedMap).Contains(black.Record)
            && !registry.TryRecoverFromWorldSite(black.Record.Id, black.Record.CustodyRevision, unrelatedMap, Open(unrelatedMap), out _, out _),
            "An unrelated native parent cannot claim an exact individual on the same tile and layer");
        RegionalTick();
        Check(ecology.LocalityAt(unrelatedMap, BlackSpecies) == blackRow && blackRow.Cohort.Preimage == before
            && blackRow.FoundingCount == found && blackRow.Receipts.Count == receipts,
            "A different map parent does not repeat habitat founding or change saved stock and receipts");
        Current.Game.DeinitAndRemoveMap(unrelatedMap, false);
        registry.ApplyPendingEvents();
        impostor.Destroy();
        Check(blackRow.Blocked == GuSurfaceEcology.BlockedBy.Destroyed && parent.Spawned,
            "Destroying a settlement records the native event while the remembered original parent survives");
        RelationAudit("before save");
        File.WriteAllText(SiteStatePath, JsonSerializer.Serialize(new SiteState(tile.tileId, parent.GetUniqueLoadID(), black.Record.Id,
            black.Record.CustodyRevision, white.Record.Id, white.Pawn.GetUniqueLoadID(), host.GetUniqueLoadID(), heldRecord.Id,
            found, blackRow.Cohort.MatureCount, blackRow.Cohort.Revision, blackRow.ConsumedTick, blackRow.ReplacementTicks, receipts)));
        AssertNoErrors("site new");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifySiteContinuityReload()
    {
        var state = JsonSerializer.Deserialize<SiteState>(File.ReadAllText(SiteStatePath));
        var registry = IndividualRegistry;
        var ecology = GuSurfaceEcology.Instance;
        var tile = Find.WorldGrid.Surface.PlanetTileForID(state.Tile);
        var parent = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().Single(objectOnPlanet => objectOnPlanet.GetUniqueLoadID() == state.Parent);
        var row = ecology.LocalityAt(tile, BlackSpecies);
        var tracked = registry.Resolve(state.Black);
        var injured = registry.Resolve(state.White);
        var held = registry.ProjectionOf(registry.Resolve(state.Held));
        var host = Find.WorldPawns.AllPawnsAliveOrDead.Single(pawn => pawn.GetUniqueLoadID() == state.Holder);
        Check(!parent.HasMap && tracked?.ContainerKind == GuContainerKind.WorldSite && tracked.CustodyRevision == state.BlackCustody
            && tracked.LastKnown.MapParentId == state.Parent && row.Cohort.MatureCount == state.Adults && row.Cohort.Revision == state.Revision
            && row.FoundingCount == state.Founding && row.ConsumedTick == state.Consumed && row.ReplacementTicks == state.Interval
            && row.Receipts.Count == state.Receipts, "Fresh-process load preserves exact site custody and every saved habitat balance and clock");
        Check(registry.ProjectionOf(injured) is Pawn_GuWildProjection retained && retained.GetUniqueLoadID() == state.WhitePawn
            && Find.WorldPawns.Contains(retained) && held?.ParentHolder == host.inventory && registry.IsCustodyCritical(host),
            "Fresh-process load preserves the exact injured world Pawn and Gu carried by a native world holder");

        var home = Find.CurrentMap;
        GenMapParent(parent);
        var map = parent.Map;
        Current.Game.CurrentMap = home;
        int otherLayer = GuUndergroundAddressLayer.Resolve(Find.WorldGrid, create: true).LayerID;
        var wrongLayer = GuLocation.FromTile(tile.tileId, otherLayer, IntVec3.Invalid, null, null, Now, state.Parent, parent.def.defName);
        Check(!wrongLayer.IsAt(map) && tracked.LastKnown.IsAt(map), "Even the same tile ordinal and parent ID fail across real world layers");
        var movedTile = Find.WorldGrid.Surface.Tiles.First(candidate => candidate.tile != tile && !Find.WorldObjects.AnyWorldObjectAt(candidate.tile)).tile;
        try
        {
            parent.Tile = movedTile;
            Check(!tracked.LastKnown.IsAt(map), "Relocating the original parent to a new native tile cannot claim a resident of its old locality");
        }
        finally { parent.Tile = tile; }
        EncounterSample(ecology, Now + 1);
        Check(registry.Resolve(state.Black) == tracked && registry.ProjectionOf(tracked) is Pawn_GuWildProjection { Spawned: true } recovered
            && recovered.Map == map && tracked.CustodyRevision == state.BlackCustody + 1 && row.Cohort.Revision == state.Revision
            && row.Cohort.MatureCount == state.Adults && row.FoundingCount == state.Founding && row.Receipts.Count == state.Receipts,
            "Regeneration under the exact original native parent recovers the tracked ID without another adult debit or founding event");
        parent.Abandon(false);
        Check(!parent.Spawned && row.Blocked == GuSurfaceEcology.BlockedBy.Abandoned,
            "Native abandonment is recorded when the world object leaves, even before its map closes");
        if (Find.Maps.Contains(map)) Current.Game.DeinitAndRemoveMap(map, false);
        registry.ApplyPendingEvents();
        Check(!parent.Spawned && !Find.Maps.Contains(map) && row.Blocked == GuSurfaceEcology.BlockedBy.Abandoned
            && tracked.ContainerKind == GuContainerKind.WorldSite && tracked.LastKnown.MapParentId == state.Parent,
            "Native abandonment leaves habitat and exact retained identity evidence at the remembered parent");
        AssertNoErrors("site reload");
    }

    private static void RelationAudit(string phase)
    {
        foreach (string entry in LiveNativeRelationTrace.MissingTargets(phase)) Report(entry);
    }
}
