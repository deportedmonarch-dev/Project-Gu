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

    private record GuFixture(string Case, GuIndividual Record, Pawn_GuWildProjection Pawn, string Outcome, string Expected, bool Rogue);
    /// <summary>Custody is observed from the native graph. Revision is the custody revision.</summary>
    private record GuRecordSnapshot(string Id, int Version, string Species, int Rank, string Origin, string OriginId,
        int CreatedTick, GuBiologicalState Biology, GuCustody Custody, int Revision, int StateRevision);
    /// <summary>Stored fields only. Used where malformed records make observed custody meaningless.</summary>
    private record GuStoredSnapshot(string Id, int Version, string Species, int Rank, string Origin, string OriginId, int CreatedTick,
        GuCondition Condition, GuPlacementKind Placement, string ProjectionId, string ContainerId, int CustodyRevision, int StateRevision);
    private record GuFixtureSnapshot(string Case, GuStoredSnapshot Record, string PawnId, string PawnTag, int PawnRevision,
        string Outcome, string Expected, bool Rogue);
    private record IndividualFixture(string MapId, string CultivatorId, int Reserve, GuFixtureSnapshot[] Gu);

    private static GuRecordSnapshot Snapshot(GuIndividual record) => new(record.Id, record.Version, record.SpeciesDefName,
        record.Rank, record.Origin, record.OriginId, record.CreatedTick, record.BiologicalState, record.Custody, record.Revision,
        record.StateRevision);

    private static GuStoredSnapshot Stored(GuIndividual record) => new(record.Id, record.Version, record.SpeciesDefName, record.Rank,
        record.Origin, record.OriginId, record.CreatedTick, record.Condition, record.Placement, record.ProjectionId, record.ContainerId,
        record.CustodyRevision, record.StateRevision);

    /// <summary>Every saved facet, for read-purity comparisons.</summary>
    private static string FullState(GuIndividual record) => $"{record.DebugDescription}|{record.QuarantineReason}|{record.UnresolvedSinceTick}";

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


        GuFixture Spawn(string name, string outcome, string expected = null, bool rogue = false)
        {
            IntVec3 cell = EmptyCargoCell(cave, exit.Position, null, 2);
            Check(registry.TrySpawnDevelopmentGu(species, cave, cell, out var pawn), $"The production spawn path creates {name}");
            var record = registry.Resolve(pawn);
            Check(record != null && registry.Resolve(record.Id) == record && record.Species == species
                && record.Custody == new GuCustody(GuCustodyKind.WildPawn, pawn.GetUniqueLoadID())
                && record.BiologicalState == GuBiologicalState.Active && record.Revision == 1 && record.StateRevision == 1
                && record.Origin == "development" && record.OriginId == cave.GetUniqueLoadID()
                && record.CreatedTick == Find.TickManager.TicksGame && Cultivation.GetOrCreate(pawn) == null,
                $"{name} received a single native binding and distinct individual data");
            return new GuFixture(name, record, pawn, outcome, expected, rogue);
        }

        // V2 classification (RFC-0004 §3.4-3.5): contradictions quarantine, missing evidence stays unresolved,
        // and a tag that claims another individual's identity is a rogue projection that never authorizes.
        var healthy = Spawn("healthy", "healthy");
        Check(healthy.Pawn.GetInspectString().Contains(healthy.Record.DebugDescription), "Pawn inspection displays its ID, revision and custody");
        var duplicateIdA = Spawn("duplicate record ID A", "quarantined", "Multiple records claim this Gu ID");
        var duplicateIdB = Spawn("duplicate record ID B", "quarantined", "Multiple records claim this Gu ID", rogue: true);
        var duplicateOwnerA = Spawn("duplicate native owner A", "quarantined", "Multiple records claim this native object");
        var duplicateOwnerB = Spawn("duplicate native owner B", "quarantined", "Multiple records claim this native object");
        // V1 quarantined both. V2 keeps A authorized because only its own Pawn matches, and rejects the copied tag as rogue.
        var duplicateProjectionA = Spawn("duplicate projection A", "healthy");
        var duplicateProjectionB = Spawn("duplicate projection B", "unresolved", nameof(GuUnresolvedCause.ProjectionMissing), rogue: true);
        var missingOwner = Spawn("missing native owner", "unresolved", nameof(GuUnresolvedCause.ProjectionMissing), rogue: true);
        var staleRevision = Spawn("stale revision", "quarantined", "The authorized native object disagrees");
        var missingSpecies = Spawn("missing species", "unresolved", nameof(GuUnresolvedCause.SpeciesUnavailable));
        var futureVersion = Spawn("unsupported version", "quarantined", "Unsupported Gu record version");

        // Introduce independent malformed claims in this disposable save only.
        System.Reflection.FieldInfo RecordField(string name) => AccessTools.Field(typeof(GuRecord), name);
        System.Reflection.FieldInfo TagField(string name) => AccessTools.Field(typeof(CompGuManifestation), name);
        RecordField("id").SetValue(duplicateIdB.Record, duplicateIdA.Record.Id.ToUpperInvariant());
        RecordField("projectionId").SetValue(duplicateOwnerB.Record, duplicateOwnerA.Record.ProjectionId);
        TagField("guId").SetValue(duplicateProjectionB.Pawn.GuTag(), duplicateProjectionA.Record.Id);
        RecordField("projectionId").SetValue(missingOwner.Record, "Thing_GU_UnavailableOwner");
        TagField("guRevision").SetValue(staleRevision.Pawn.GuTag(), staleRevision.Record.Revision + 1);
        RecordField("speciesDefName").SetValue(missingSpecies.Record, "GU_UnavailableSpecies");
        RecordField("version").SetValue(futureVersion.Record, GuRecord.CurrentVersion + 1);

        GuFixture[] fixtures = [healthy, duplicateIdA, duplicateIdB, duplicateOwnerA, duplicateOwnerB, duplicateProjectionA,
            duplicateProjectionB, missingOwner, staleRevision, missingSpecies, futureVersion];
        var snapshot = new IndividualFixture(cave.GetUniqueLoadID(), colonist.GetUniqueLoadID(), spring.Reserve,
            fixtures.Select(fixture => new GuFixtureSnapshot(fixture.Case, Stored(fixture.Record), fixture.Pawn.GetUniqueLoadID(),
                fixture.Pawn.GuId, fixture.Pawn.GuRevision, fixture.Outcome, fixture.Expected, fixture.Rogue)).ToArray());
        File.WriteAllText(IndividualFixturePath, JsonSerializer.Serialize(snapshot));
        Check(spring.HopeGuAvailable == 2 && spring.HabitatSupport == 7, "Development Gu creation leaves the existing spring cohort and habitat intact");
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved the exact healthy Gu ID {healthy.Record.Id} and ten deliberately malformed claims, plus the awakened colonist and spring.");
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

        // Read purity: inspecting every projection and building its menus must leave every record unchanged.
        var before = registry.Individuals.Select(FullState).ToArray();
        var viewer = cave.mapPawns.FreeColonistsSpawned.First();
        foreach (var pawn in pawns.Values)
        {
            pawn.GetInspectString();
            pawn.GetFloatMenuOptions(viewer).ToArray();
            registry.TryGetView(pawn, out _);
        }
        Check(registry.Individuals.Select(FullState).SequenceEqual(before), "Inspection, menus and views did not mutate any record");

        for (int index = 0; index < fixture.Gu.Length; index++)
        {
            var expected = fixture.Gu[index];
            var restored = registry.Individuals[index];
            var pawn = pawns[expected.PawnId];
            Check(Stored(restored) == expected.Record && pawn.GuId == expected.PawnTag && pawn.GuRevision == expected.PawnRevision
                && pawn.Map == cave && pawn.holdingOwner == cave.GetDirectlyHeldThings(), $"{expected.Case} kept its saved fields and native Pawn");
            Check(registry.RogueProjections.Contains(pawn.GuTag()) == expected.Rogue, $"{expected.Case} rogue classification is {expected.Rogue}");
            switch (expected.Outcome)
            {
                case "healthy":
                    Check(restored.IsActionable && registry.Resolve(restored.Id) == restored && registry.Resolve(pawn) == restored
                        && pawn.GetInspectString().Contains(restored.DebugDescription), $"{expected.Case} resolved by original ID, revision and exact Pawn");
                    break;
                case "quarantined":
                    Check(restored.IsQuarantined && restored.QuarantineReason.StartsWith(expected.Expected, StringComparison.Ordinal)
                        && registry.Resolve(restored.Id) == null && registry.Resolve(pawn) == null, $"{expected.Case} is quarantined as an evidential conflict");
                    break;
                default:
                    Check(!restored.IsQuarantined && restored.Unresolved.ToString() == expected.Expected
                        && restored.TerminalEvidence == GuTerminalEvidence.None && restored.Existence == GuExistence.Living
                        && registry.Resolve(restored.Id) == null && registry.Resolve(pawn) == null,
                        $"{expected.Case} is unresolved ({expected.Expected}) without a terminal outcome and retains all data");
                    break;
            }
            if (registry.Resolve(pawn) == null)
            {
                string text = pawn.GetInspectString();
                Check(text.Contains("not authorized") || text.Contains("rogue projection") || text.Contains("no canonical record"),
                    $"{expected.Case} remained inspectable without an authorized binding");
            }
        }

        const string checkedSave = "gu-quarantine-verified";
        GameDataSaveLoader.SaveGame(checkedSave);
        var document = XDocument.Load(GenFilePaths.FilePathForSavedGame(checkedSave));
        var savedRecords = document.Descendants("guIndividualRecordsV2").Single().Elements("li").ToArray();
        Check(!document.Descendants("guIndividualRecordsV1").Any(), "Schema V2 never writes the V1 record list");
        Check(savedRecords.Select(node => (string)node.Element("quarantineReason"))
            .SequenceEqual(registry.Individuals.Select(record => record.QuarantineReason))
            && savedRecords.Select(node => (string)node.Element("unresolved") ?? nameof(GuUnresolvedCause.None))
                .SequenceEqual(registry.Individuals.Select(record => record.Unresolved.ToString())),
            "The next save retains every quarantine and unresolved result");

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
        Report("The healthy Gu identity and native owner resolved after fresh-process reload. Contradictions were quarantined, missing evidence stayed unresolved, rogue tags were rejected, and no claim authorized a replacement.");
        Report("Inspection, menus and views were read-only. The existing aperture, once-only awakening, spring cohorts, native flora, reserve, stone and return passage all passed.");
    }
}
