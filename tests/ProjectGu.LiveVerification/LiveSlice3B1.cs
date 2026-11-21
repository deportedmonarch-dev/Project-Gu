using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B1 => GenCommandLine.CommandLineArgPassed("guslice3b1");
    private static GuIndividualRegistry IndividualRegistry => Current.Game.GetComponent<GuIndividualRegistry>();
    private static string IndividualFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-individual-fixture.json");

    private record GuFixture(string Case, GuIndividual Record, Pawn_GuWildProjection Pawn, string ExpectedQuarantine = null);
    private record GuRecordSnapshot(string Id, int Version, string Species, int Rank, string Origin, string OriginId,
        int CreatedTick, GuBiologicalState Biology, GuCustody Custody, int Revision);
    private record GuFixtureSnapshot(string Case, GuRecordSnapshot Record, string PawnId, string PawnTag, int PawnRevision, string ExpectedQuarantine);
    private record IndividualFixture(string MapId, string CultivatorId, int Reserve, GuFixtureSnapshot[] Gu);

    private static GuRecordSnapshot Snapshot(GuIndividual record) => new(record.Id, record.Version, record.SpeciesDefName,
        record.Rank, record.Origin, record.OriginId, record.CreatedTick, record.BiologicalState, record.Custody, record.Revision);

    private static void ExerciseSlice3B1()
    {
        var registry = IndividualRegistry;
        Check(registry.Individuals.Count == 0 && !PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().Any(),
            "The new game starts without individual Gu records or projections");
        Map home = Find.CurrentMap;
        Pawn colonist = home.mapPawns.FreeColonistsSpawned.First();
        var site = OpenIndependentSite(colonist, home);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        Map cave = exit.Map;
        Check(JobDriver_GuPocketProbeTravel.TryTravel(colonist, entrance, out string reason) && colonist.Map == cave,
            $"The existing passage brought the cultivator to the spring. {reason}");
        var spring = BotanicalSpring(cave);
        AssertBotanicalHabitat(cave, spring);
        int reserve = spring.Reserve;

        Cultivation.GetOrCreate(colonist).LatentAptitude = 63;
        Check(GuAwakeningService.TryAwakenAtSpring(colonist, spring, out reason) && spring.HopeGuAvailable == 2,
            $"The existing awakening service opens one aperture and debits one adult Hope Gu. {reason}");
        AssertAwakening(colonist, AwakeningState.Awakened, true);
        spring.CompGetGizmosExtra().OfType<Command_Action>().Single(action => action.defaultLabel == "GU: Produce stone now").action();
        Check(spring.Reserve == reserve - 1 && cave.listerThings.ThingsOfDef(StoneDef).Single().stackCount == 1,
            "Existing spring production creates one stone and debits one reserve");

        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        Check(species.wildProjectionKind?.race.thingClass == typeof(Pawn_GuWildProjection), "The developer species resolved its native Pawn Def");
        IntVec3 testCell = EmptyCargoCell(cave, exit.Position, null, 2);
        Prefs.DevMode = false;
        try
        {
            Check(!registry.TrySpawnDevelopmentGu(species, cave, testCell, out _) && registry.Individuals.Count == 0,
                "Production spawn is gated by Development Mode");
        }
        finally { Prefs.DevMode = true; }
        Check(!registry.TrySpawnDevelopmentGu(species, cave, colonist.Position, out _)
            && registry.Individuals.Count == 0, "Rejected placement creates no individual");

        GuFixture Spawn(string name, string expectedQuarantine = null)
        {
            IntVec3 cell = EmptyCargoCell(cave, exit.Position, null, 2);
            Check(registry.TrySpawnDevelopmentGu(species, cave, cell, out var pawn), $"The production spawn path creates {name}");
            var record = registry.Resolve(pawn);
            Check(record != null && registry.Resolve(record.Id) == record && record.Species == species
                && record.Custody == new GuCustody(GuCustodyKind.WildPawn, pawn.GetUniqueLoadID())
                && record.BiologicalState == GuBiologicalState.Active && record.Revision == 1
                && record.Origin == "development" && record.OriginId == cave.GetUniqueLoadID()
                && record.CreatedTick == Find.TickManager.TicksGame && Cultivation.GetOrCreate(pawn) == null,
                $"{name} received a single native binding and distinct individual data");
            return new GuFixture(name, record, pawn, expectedQuarantine);
        }

        var healthy = Spawn("healthy");
        Check(healthy.Pawn.GetInspectString().Contains(healthy.Record.DebugDescription), "Pawn inspection displays its ID, revision and custody");
        var duplicateIdA = Spawn("duplicate record ID A", "Multiple records");
        var duplicateIdB = Spawn("duplicate record ID B", "Multiple records");
        var duplicateOwnerA = Spawn("duplicate native owner A", "Multiple records");
        var duplicateOwnerB = Spawn("duplicate native owner B", "Multiple records");
        var duplicateProjectionA = Spawn("duplicate projection A", "Multiple Gu projections");
        var duplicateProjectionB = Spawn("duplicate projection B", "The exact live map-owned projection");
        var missingOwner = Spawn("missing native owner", "The exact live map-owned projection");
        var staleRevision = Spawn("stale revision", "The exact live map-owned projection");
        var missingSpecies = Spawn("missing species", "The saved Gu species is unavailable");
        var futureVersion = Spawn("unsupported version", "Unsupported Gu record version");

        // Introduce independent malformed claims in this disposable save only.
        AccessTools.Field(typeof(GuIndividual), "id").SetValue(duplicateIdB.Record, duplicateIdA.Record.Id.ToUpperInvariant());
        AccessTools.Field(typeof(GuIndividual), "nativeOwnerId").SetValue(duplicateOwnerB.Record, duplicateOwnerA.Record.Custody.NativeOwnerId);
        AccessTools.Field(typeof(Pawn_GuWildProjection), "guId").SetValue(duplicateProjectionB.Pawn, duplicateProjectionA.Record.Id);
        AccessTools.Field(typeof(GuIndividual), "nativeOwnerId").SetValue(missingOwner.Record, "Thing_GU_UnavailableOwner");
        AccessTools.Field(typeof(Pawn_GuWildProjection), "guRevision").SetValue(staleRevision.Pawn, staleRevision.Record.Revision + 1);
        AccessTools.Field(typeof(GuIndividual), "speciesDefName").SetValue(missingSpecies.Record, "GU_UnavailableSpecies");
        AccessTools.Field(typeof(GuIndividual), "version").SetValue(futureVersion.Record, GuIndividual.CurrentVersion + 1);

        GuFixture[] fixtures = [healthy, duplicateIdA, duplicateIdB, duplicateOwnerA, duplicateOwnerB, duplicateProjectionA,
            duplicateProjectionB, missingOwner, staleRevision, missingSpecies, futureVersion];
        var snapshot = new IndividualFixture(cave.GetUniqueLoadID(), colonist.GetUniqueLoadID(), spring.Reserve,
            fixtures.Select(fixture => new GuFixtureSnapshot(fixture.Case, Snapshot(fixture.Record), fixture.Pawn.GetUniqueLoadID(),
                fixture.Pawn.GuId, fixture.Pawn.GuRevision, fixture.ExpectedQuarantine)).ToArray());
        File.WriteAllText(IndividualFixturePath, JsonSerializer.Serialize(snapshot));
        Check(spring.HopeGuAvailable == 2 && spring.HabitatSupport == 7, "Development Gu creation leaves the existing spring cohort and habitat intact");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved the exact healthy Gu ID {healthy.Record.Id} and ten deliberately invalid claims, plus the awakened colonist and spring.");
    }

    private static void VerifySlice3B1Reload()
    {
        var fixture = JsonSerializer.Deserialize<IndividualFixture>(File.ReadAllText(IndividualFixturePath));
        var registry = IndividualRegistry;
        var pawns = PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().Distinct().ToDictionary(pawn => pawn.GetUniqueLoadID());
        Check(registry.Individuals.Count == fixture.Gu.Length && pawns.Count == fixture.Gu.Length,
            "Reload retained the exact set of records and projections without creating replacements");
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.MapId);
        AssertNativeAddress(cave);

        for (int index = 0; index < fixture.Gu.Length; index++)
        {
            var expected = fixture.Gu[index];
            var restored = registry.Individuals[index];
            var pawn = pawns[expected.PawnId];
            Check(Snapshot(restored) == expected.Record && pawn.GuId == expected.PawnTag && pawn.GuRevision == expected.PawnRevision
                && pawn.Map == cave && pawn.holdingOwner == cave.GetDirectlyHeldThings(), $"{expected.Case} kept its saved fields and native Pawn");
            if (expected.ExpectedQuarantine == null)
            {
                Check(!restored.IsQuarantined && registry.Resolve(restored.Id) == restored && registry.Resolve(pawn) == restored
                    && pawn.GetInspectString().Contains(restored.DebugDescription), "Healthy Gu resolved by original ID, revision and exact Pawn");
            }
            else
            {
                Check(restored.IsQuarantined && restored.QuarantineReason.StartsWith(expected.ExpectedQuarantine, StringComparison.Ordinal)
                    && registry.Resolve(restored.Id) == null && registry.Resolve(pawn) == null
                    && pawn.GetInspectString().Contains("quarantined"), $"{expected.Case} remained inspectable without an authorized binding");
            }
        }

        const string checkedSave = "gu-quarantine-verified";
        GameDataSaveLoader.SaveGame(checkedSave);
        var savedRecords = XDocument.Load(GenFilePaths.FilePathForSavedGame(checkedSave)).Descendants("guIndividualRecordsV1").Single().Elements("li");
        Check(savedRecords.Select(node => (string)node.Element("quarantineReason"))
            .SequenceEqual(registry.Individuals.Select(record => record.QuarantineReason)), "The next save retains every quarantine result");

        var spring = BotanicalSpring(cave);
        AssertBotanicalHabitat(cave, spring);
        Pawn colonist = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.CultivatorId);
        AssertAwakening(colonist, AwakeningState.Awakened, true);
        Check(!GuAwakeningService.TryAwakenAtSpring(colonist, spring, out _) && spring.HopeGuAvailable == 2
            && spring.Reserve == fixture.Reserve && cave.listerThings.ThingsOfDef(StoneDef).Single().stackCount == 1,
            "Reload preserves the aperture, once-only awakening, cohort, reserve and produced stone");
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(colonist, exit, out string reason) && colonist.Map.IsPlayerHome,
            $"The original cultivator returned through the saved passage. {reason}");
        Report("The original Gu identity and native owner resolved after fresh-process reload. All ten invalid claims were quarantined, retained on resave, and authorized no replacements.");
        Report("The existing aperture, once-only awakening, spring cohorts, native flora, reserve, stone and return passage all passed.");
    }
}
