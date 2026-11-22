using System.Text.Json;
using System.Xml.Linq;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Loads a real save written by the v0.2.5 (Schema V1) build and verifies V1-to-V2 migration field by field against the
/// V1 XML, the expected classification for that source, and a fresh-process round trip of the migrated V2 save.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool V1Migration => GenCommandLine.CommandLineArgPassed("guv1migration");
    private static bool V1Resave => GenCommandLine.CommandLineArgPassed("guv1resave");
    private static string V1SourceName => Environment.GetCommandLineArgs()
        .FirstOrDefault(arg => arg.StartsWith("-guv1source=", StringComparison.Ordinal))?.Substring("-guv1source=".Length);
    private static string V1SourcePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "v1-source.rws");
    private static string MigrationFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-migration-fixture.json");

    private record V1Row(string Id, int Version, string Species, int Rank, string Origin, string OriginId, int CreatedTick,
        int Biology, int Custody, string NativeOwner, string Holder, int Revision, string Quarantine, string EventKey,
        string OriginNative, bool Debited);
    private record MigratedRow(string Id, string Stored, string Unresolved, string Quarantine, bool Bound);

    // Expected (bound, unresolved, quarantined) per V1 source, derived from each fixture's final V1 state. See the journal.
    private static readonly Dictionary<string, (int Bound, int Unresolved, int Quarantined)> ExpectedMigration = new()
    {
        ["Slice3B1"] = (2, 3, 6),
        ["Slice3B2"] = (1, 0, 1),
        ["Slice3B3B"] = (2, 4, 1),
        ["Slice3B4"] = (1, 3, 1),
        ["Slice3B5A"] = (1, 0, 3),
        ["VanillaGuInventory"] = (1, 0, 0),
        ["V2EngineProbe"] = (7, 4, 0)
    };

    private static readonly string[] OrdinaryV1Reasons =
    {
        "The bound Gu projection is no longer live", "The exact live projection, native holder and revision could not be resolved",
        "The bound aperture or host is no longer live", "The exact live aperture and host could not be resolved",
        "The aperture host died", "The bound aperture was removed", "The saved Gu species is unavailable"
    };

    private static V1Row[] ReadV1Rows(string path)
    {
        var list = XDocument.Load(path).Descendants("guIndividualRecordsV1").Single().Elements("li");
        int Int(XElement node, string name, int fallback = 0) => int.TryParse((string)node.Element(name), out int value) ? value : fallback;
        return list.Select(node => new V1Row((string)node.Element("id"), Int(node, "version"), (string)node.Element("speciesDefName"),
            Int(node, "rank"), (string)node.Element("origin"), (string)node.Element("originId"), Int(node, "createdTick", -1),
            Int(node, "biologicalState"), Int(node, "custodyKind"), (string)node.Element("nativeOwnerId"), (string)node.Element("holderPawnId"),
            Int(node, "revision"), (string)node.Element("quarantineReason"), (string)node.Element("creationEventKey"),
            (string)node.Element("originNativeId"), (string)node.Element("cohortDebitCommitted") == "True")).ToArray();
    }

    private static void VerifyV1Migration()
    {
        string name = V1SourceName;
        Check(name != null && ExpectedMigration.ContainsKey(name), $"Known V1 migration source {name}");
        var expected = ExpectedMigration[name];
        var rows = ReadV1Rows(V1SourcePath);
        var registry = IndividualRegistry;
        var records = registry.Records;
        Check(rows.Length > 0 && records.Count == rows.Length && registry.LastMigrationSummary != null,
            $"All {rows.Length} V1 records migrated exactly once. {registry.LastMigrationSummary}");

        for (int index = 0; index < rows.Length; index++)
        {
            var old = rows[index];
            var record = records[index];
            Check(record.Id == old.Id && record.SpeciesDefName == old.Species && record.Rank == old.Rank && record.Origin == old.Origin
                && record.OriginId == old.OriginId && record.CreatedTick == old.CreatedTick && record.CreationEventKey == old.EventKey
                && record.OriginNativeId == old.OriginNative && record.CohortDebitCommitted == old.Debited
                && record.Version == GuRecord.CurrentVersion && record.CustodyRevision == old.Revision && record.StateRevision == 1
                && (int)record.Condition == old.Biology && record.LegacyQuarantineReason == old.Quarantine,
                $"V1 record {old.Id} kept identity, provenance, rank, condition, revision and its V1 diagnosis");
            bool aperture = old.Custody == (int)GuV1CustodyKind.Aperture;
            Check(aperture ? record.Placement == GuPlacementKind.Contained && record.AnchorHint == old.Holder
                    && (record.ContainerId != null || record.LegacyApertureLoadId == old.NativeOwner)
                : record.Placement == GuPlacementKind.Manifested && record.ProjectionId == old.NativeOwner
                    && record.Manifestation == (old.Custody == (int)GuV1CustodyKind.WildPawn ? GuManifestationKind.Pawn : GuManifestationKind.Thing),
                $"V1 record {old.Id} mapped its custody kind to the V2 placement");
            if (record.IsQuarantined)
                Check(!OrdinaryV1Reasons.Any(reason => record.QuarantineReason.Contains(reason)),
                    $"V1 record {old.Id} is not quarantined for an ordinary custody mismatch: {record.QuarantineReason}");
            if (registry.Resolve(record.Id) is { } bound && record.Placement == GuPlacementKind.Manifested)
                Check(registry.ProjectionOf(record) is { } thing && thing.GuTag().GuId == record.Id && thing.GuTag().GuRevision == record.CustodyRevision
                    && thing.GetUniqueLoadID() == old.NativeOwner, $"V1 record {old.Id} rebound its original native object in place");
        }

        var tags = PawnsFinder.All_AliveOrDead.Cast<Thing>().Concat(Find.Maps.SelectMany(map => ThingOwnerUtility.GetAllThingsRecursively(map, false)))
            .Concat(PawnsFinder.All_AliveOrDead.Where(pawn => pawn.inventory != null).SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings()))
            .Distinct().Where(thing => thing.GuTag() != null).ToArray();
        Check(tags.Where(thing => registry.Resolve(thing) != null).GroupBy(thing => thing.GuTag().GuId).All(group => group.Count() == 1),
            "No Gu ID has more than one authorized native manifestation");

        int boundCount = records.Count(record => registry.Resolve(record.Id) != null);
        int unresolvedCount = records.Count(record => record.IsUnresolved && !record.IsQuarantined);
        int quarantinedCount = records.Count(record => record.IsQuarantined);
        Report($"OBS {name}: bound={boundCount} unresolved={unresolvedCount} quarantined={quarantinedCount} rogue={registry.RogueProjections.Count}");
        foreach (var record in records)
            Report($"OBS {name} {record.Id}: {(registry.Resolve(record.Id) != null ? "bound " + registry.Observe(record).Form : record.IsQuarantined ? "quarantined: " + record.QuarantineReason : "unresolved: " + record.Unresolved + "/" + record.TerminalEvidence)} | V1: {record.LegacyQuarantineReason ?? "healthy"}");
        Check(boundCount == expected.Bound && unresolvedCount == expected.Unresolved && quarantinedCount == expected.Quarantined,
            $"{name} migrated to the expected classification {expected}");

        File.WriteAllText(MigrationFixturePath, JsonSerializer.Serialize(records.Select(record => new MigratedRow(record.Id,
            DescribeStored(record), record.Unresolved.ToString(), record.QuarantineReason, registry.Resolve(record.Id) != null)).ToArray()));
        GameDataSaveLoader.SaveGame(SaveName);
        var saved = XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName));
        Check(!saved.Descendants("guIndividualRecordsV1").Any() && saved.Descendants("guIndividualRecordsV2").Single().Elements("li").Count() == rows.Length,
            "The migrated save writes only the V2 record list");
        Report($"PASS {name} migration: {rows.Length} V1 records classified as expected and resaved as Schema V2.");
    }

    private static void VerifyV1MigrationResave()
    {
        var expected = JsonSerializer.Deserialize<MigratedRow[]>(File.ReadAllText(MigrationFixturePath));
        var registry = IndividualRegistry;
        Check(registry.LastMigrationSummary == null && registry.Records.Count == expected.Length,
            "The V2 save loaded without running migration again");
        var actual = registry.Records.Select(record => new MigratedRow(record.Id, DescribeStored(record), record.Unresolved.ToString(),
            record.QuarantineReason, registry.Resolve(record.Id) != null)).ToArray();
        Check(actual.SequenceEqual(expected), "The fresh-process V2 reload reproduced every migrated record, outcome and binding");
        Report($"PASS {V1SourceName} V2 round trip: {expected.Length} records identical after a fresh-process reload.");
    }

    private static string DescribeStored(GuRecord record) => string.Join("|", record.Id, record.Version, record.SpeciesDefName, record.Rank,
        record.Origin, record.OriginId, record.CreatedTick, record.CreationEventKey, record.CohortDebitCommitted, record.Condition,
        record.Placement, record.Manifestation, record.ProjectionId, record.ContainerId, record.AnchorHint, record.CustodyRevision,
        record.StateRevision, record.LegacyQuarantineReason);
}
