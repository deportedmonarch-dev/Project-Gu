using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static string C3Case => CommandValue("-guc3=");
    private static string C3Stage => CommandValue("-guc3stage=");
    private static string CommandValue(string option) => Environment.GetCommandLineArgs()
        .FirstOrDefault(arg => arg.StartsWith(option, StringComparison.Ordinal))?[option.Length..];
    private static string C3Path(string name) => Path.Combine(GenFilePaths.SaveDataFolderPath, name);
    private static GuWorldComponent C3World => Current.Game.GetComponent<GuWorldComponent>();
    private static GuCultivatorPlan C3Store => (GuCultivatorPlan)AccessTools.Field(typeof(GuWorldComponent), "store").GetValue(C3World);
    private static string[] c3Before;
    private static Dictionary<Hediff_Aperture, string> c3Apertures;
    private static Hediff_Aperture c3FaultedAperture;
    private static Exception c3Failure;
    private const string C3RollbackInterference = "c3-injected-external-aperture-write";

    [StaticConstructorOnStartup]
    private static class CultivatorMigrationProbe
    {
        static CultivatorMigrationProbe()
        {
            if (C3Stage == null) return;
            var harmony = new Harmony("projectgu.priority0test.c3");
            var capture = new HarmonyMethod(typeof(CultivatorMigrationProbe), nameof(CaptureBefore));
            foreach (string name in new[] { nameof(GuCultivatorPlan.PrepareLegacy), nameof(GuCultivatorPlan.PrepareCommitted) })
                harmony.Patch(AccessTools.DeclaredMethod(typeof(GuCultivatorPlan), name), prefix: capture);
            if (C3Stage == "blocked")
                harmony.Patch(AccessTools.DeclaredMethod(typeof(GuCultivatorPlan), nameof(GuCultivatorPlan.CommitApertureWrites)),
                    prefix: new HarmonyMethod(typeof(CultivatorMigrationProbe), nameof(InjectFailure)),
                    finalizer: new HarmonyMethod(typeof(CultivatorMigrationProbe), nameof(CaptureFailure)));
        }

        private static void CaptureBefore(IEnumerable<Hediff_Aperture> apertures)
        {
            c3Apertures = apertures.Where(a => a?.pawn != null).Distinct()
                .ToDictionary(a => a, a => a.ApertureId);
            c3Before = CaptureC3Conservation();
        }

        private static void InjectFailure(ref Action<int> checkpoint) => checkpoint = writes =>
        {
            if (writes != 1) return;
            c3FaultedAperture = c3Apertures.Keys.Single(a => a.ApertureId != c3Apertures[a]);
            if (C3Case == "failed-rollback")
                c3FaultedAperture.ReplaceCultivatorApertureId(c3FaultedAperture.ApertureId, C3RollbackInterference);
            throw new InvalidOperationException("C3 failure injected after the first aperture write.");
        };

        private static void CaptureFailure(Exception __exception) => c3Failure = __exception;
    }

    private static void VerifyCultivatorMigration()
    {
        string inputPath = C3Path("c3-input.rws");
        var input = XDocument.Load(inputPath);
        var source = C3Component(input);
        var conservation = CaptureC3Conservation();
        Check(c3Before != null && c3Before.SequenceEqual(conservation),
            "Cultivator publication preserves each Gu state and binding, projection, physical resource and spring cohort");
        if (C3Stage == "blocked")
        {
            VerifyC3Blocked(inputPath, source);
            return;
        }

        Check(C3World.CanSaveCultivators && C3Store != null, "Cultivator store published with writable save authority");
        VerifyC3Indexes();
        if (C3Stage == "migrate")
        {
            if (V1Migration)
            {
                CheckV1Migration(save: false);
                VerifyC3Phase2Classification();
            }
            if (C3Case is "historical-31" or "historical-39") VerifyHistoricalSpring(C3Case == "historical-31" ? 31 : 39);
            VerifyC3Source(source, input);
        }

        GameDataSaveLoader.SaveGame(SaveName);
        var committed = C3Component(XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName)));
        Check(GuWorldComponent.ClassifyEnvelope(ToC3Node(committed)) == GuCultivatorLoadKind.Committed
            && committed.Document.Descendants("guCultivatorRecordsV1").Count() == 1
            && !committed.Document.Descendants("guPawnRecords").Any(),
            "Save contains exactly one committed cultivator authority");
        var snapshot = new C3Snapshot(
            committed.Element("guCultivatorRecordsV1").Elements("li").Select(C3RecordXml).Order(StringComparer.Ordinal).ToArray(),
            committed.Element("guCultivatorIntegrityV1").Elements("li").Select(C3Xml).Order(StringComparer.Ordinal).ToArray(),
            conservation);
        string checkpoint = C3Path("c3-committed.json");
        if (C3Stage == "migrate")
            File.WriteAllText(checkpoint, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            var expected = JsonSerializer.Deserialize<C3Snapshot>(File.ReadAllText(checkpoint));
            Check(snapshot.Records.SequenceEqual(expected.Records), $"Independent {C3Stage} preserves IDs, revisions and ownership");
            Check(snapshot.Evidence.SequenceEqual(expected.Evidence), $"Independent {C3Stage} preserves raw evidence without duplicates");
            Check(snapshot.Conservation.SequenceEqual(expected.Conservation), $"Independent {C3Stage} preserves Gu custody and physical conservation");
        }
        Report($"C3 {C3Case}/{C3Stage}: records={C3Store.Records.Count} bound={C3Store.ByPawn.Count} "
            + $"apertureOwners={C3Store.ByAperture.Count} evidence={C3Store.Evidence.Count} Gu={IndividualRegistry.Records.Count}");
    }

    private sealed record C3Snapshot(string[] Records, string[] Evidence, string[] Conservation);

    private static XElement C3Component(XDocument document) => document.Descendants("li")
        .SingleOrDefault(node => (string)node.Attribute("Class") == typeof(GuWorldComponent).FullName);

    private static System.Xml.XmlNode ToC3Node(XElement element)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(C3Xml(element));
        return doc.DocumentElement;
    }

    private static string C3Xml(XElement element) => element.ToString(SaveOptions.DisableFormatting);

    private static string C3RecordXml(XElement row)
    {
        var stable = new XElement(row);
        stable.Element("observationTick")?.Remove();
        return C3Xml(stable);
    }

    private static string[] CaptureC3Conservation()
    {
        var registry = IndividualRegistry;
        var pawns = PawnsFinder.All_AliveOrDead;
        var things = Find.Maps.SelectMany(map => ThingOwnerUtility.GetAllThingsRecursively(map, false))
            .Concat(pawns.Cast<Thing>())
            .Concat(pawns.Where(pawn => pawn.inventory != null).SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings()))
            .Distinct().ToArray();
        var gu = registry.Records.Select(record => $"Gu|{DescribeStored(record)}|{record.Unresolved}|{record.QuarantineReason}"
            + $"|{registry.Resolve(record.Id) != null}|{registry.ProjectionOf(record)?.GetUniqueLoadID()}");
        var physical = things.Where(thing => thing.GuTag() != null || thing.def?.defName == "GU_PrimevalStone"
                || thing is Thing_GuDormantProjection or Pawn_GuWildProjection)
            .Select(thing => $"Thing|{thing.GetUniqueLoadID()}|{thing.def.defName}|{thing.stackCount}|{thing.MapHeld?.uniqueID}"
                + $"|{thing.PositionHeld}|{thing.ParentHolder switch { Thing thingHolder => thingHolder.GetUniqueLoadID(), Pawn_InventoryTracker inv => inv.pawn.GetUniqueLoadID(), _ => thing.ParentHolder?.GetType().FullName }}");
        var springs = things.OfType<ThingWithComps>().Select(thing => thing.GetComp<CompGuSpiritSpring>())
            .Where(spring => spring != null).ToArray();
        var reserves = springs.Select(spring => $"Spring|{spring.parent.GetUniqueLoadID()}|{spring.Reserve}");
        var populations = AccessTools.Field(typeof(CompGuSpiritSpring), "populations");
        var cohorts = springs.SelectMany(spring => ((List<GuPopulationRecord>)populations.GetValue(spring) ?? [])
            .Select(cohort => $"Cohort|{spring.parent.GetUniqueLoadID()}|{cohort.SpeciesDefName}|{cohort.MatureCount}|{cohort.JuvenileCount}|{cohort.Revision}"));
        return gu.Concat(physical).Concat(reserves).Concat(cohorts).Order(StringComparer.Ordinal).ToArray();
    }

    private static void VerifyC3Blocked(string inputPath, XElement source)
    {
        bool partial = C3Case == "partial-failure";
        Check(c3FaultedAperture != null && c3Failure != null
            && (partial ? c3Failure is InvalidOperationException : c3Failure is AggregateException),
            "Native load reached the injected write and reported the expected rollback outcome");
        if (!partial)
            Check(((AggregateException)c3Failure).InnerExceptions.Any(error =>
                    error.Message.Contains(c3FaultedAperture.GetUniqueLoadID(), StringComparison.Ordinal)
                    && error.Message.Contains(C3RollbackInterference, StringComparison.Ordinal)),
                "Failed native rollback logged the affected Hediff and the observed conflicting ID");
        Check(c3Apertures.All(pair => pair.Key.ApertureId == (!partial && pair.Key == c3FaultedAperture
                ? C3RollbackInterference : pair.Value)),
            "Rollback restored every unaffected preimage and exposed the one injected conflict when rollback failed");
        Check(!C3World.CanSaveCultivators && C3Store == null && C3World.PendingLegacyRows != null
            && XNode.DeepEquals(XElement.Parse(C3World.PendingLegacyRows.SourceXml), source?.Element("guPawnRecords")),
            "Failed migration preserved the source and published no cultivator store");
        string candidateName = "c3-rejected-save";
        string inputHash = Sha(inputPath);
        GameDataSaveLoader.SaveGame(candidateName);
        GameDataSaveLoader.SaveGame(SaveName);
        Check(!File.Exists(GenFilePaths.FilePathForSavedGame(candidateName))
            && Sha(GenFilePaths.FilePathForSavedGame(SaveName)) == inputHash && Sha(inputPath) == inputHash,
            "The engine refused both a new save and an overwrite of the failed input");
        Report($"C3 {C3Case}: {c3Failure.GetType().Name}, guarded and source SHA-256 {inputHash}");
    }

    private static void VerifyC3Phase2Classification()
    {
        var expected = JsonSerializer.Deserialize<MigratedRow[]>(File.ReadAllText(C3Path("c3-phase2-baseline.json")));
        var actual = IndividualRegistry.Records.ToDictionary(row => row.Id);
        Check(expected.Length == actual.Count, "Archived Phase 2 Gu population exactly accounted for");
        foreach (var row in expected)
        {
            Check(actual.TryGetValue(row.Id, out var record), $"Phase 2 Gu {row.Id} still exists");
            var before = row.Stored.Split('|');
            var after = DescribeStored(record).Split('|');
            // Fresh V1 imports may mint a different Phase 2 aperture ID. V1Migration verifies the native holder.
            before[13] = after[13];
            Check(before.SequenceEqual(after) && row.Unresolved == record.Unresolved.ToString()
                && row.Quarantine == record.QuarantineReason && row.Bound == (IndividualRegistry.Resolve(row.Id) != null),
                $"Gu {row.Id} matches the archived Phase 2 classification, placement and revisions");
        }
    }

    private static void VerifyC3Indexes()
    {
        var store = C3Store;
        foreach (var row in store.Records)
        {
            if (row.Association == GuNativeAssociation.Bound)
            {
                Check(row.Pawn != null && GuWorldComponent.HasNativeSaveOwner(row.Pawn)
                    && row.Pawn.GetUniqueLoadID() == row.PawnLoadId && store.ByPawn.GetValueOrDefault(row.Pawn) == row,
                    $"Bound identity {row.Id} has an exact native save owner");
                Check(row.ApertureIds.All(id => store.ByAperture.GetValueOrDefault(id) == row
                    && row.Pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Count(aperture => aperture.ApertureId == id) == 1),
                    $"Bound identity {row.Id} has only exact aperture links");
            }
            else
                Check(row.Pawn == null && !store.ByPawn.ContainsValue(row) && !store.ByAperture.ContainsValue(row),
                    $"Unresolved identity {row.Id} grants no native authority");
        }
        Check(store.ByPawn.Count == store.Records.Count(row => row.Association == GuNativeAssociation.Bound)
            && store.ById.Count == store.Records.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() == 1),
            "Identity and Pawn indexes have no arbitrary conflict winners");
        var tags = Find.Maps.SelectMany(map => ThingOwnerUtility.GetAllThingsRecursively(map, false))
            .Concat(PawnsFinder.All_AliveOrDead)
            .Concat(PawnsFinder.All_AliveOrDead.Where(pawn => pawn.inventory != null)
                .SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings())).Distinct().Where(thing => thing.GuTag() != null);
        Check(tags.Where(thing => IndividualRegistry.Resolve(thing) != null)
            .GroupBy(thing => thing.GuTag().GuId).All(group => group.Count() == 1),
            "No Gu ID acquired a second authorized physical manifestation");
    }

    private static void VerifyC3Source(XElement component, XDocument input)
    {
        var store = C3Store;
        if (input.Descendants("guIndividualRecordsV2").SingleOrDefault() is { } savedGu)
        {
            var nativeGu = IndividualRegistry.Records.ToDictionary(record => record.Id);
            var rows = savedGu.Elements("li").ToArray();
            Check(rows.Length == nativeGu.Count && rows.All(row => nativeGu.TryGetValue((string)row.Element("id"), out var record)
                && record.CustodyRevision == ((int?)row.Element("custodyRevision") ?? 1)),
                "Phase 2 V2 source Gu IDs and custody revisions match the native load");
        }
        var legacyRows = component?.Element("guPawnRecords")?.Elements("li").ToArray() ?? [];
        for (int ordinal = 0; ordinal < legacyRows.Length; ordinal++)
        {
            var row = legacyRows[ordinal];
            var record = store.Records.SingleOrDefault(item => item.CreationKey == $"legacy:{ordinal}");
            var evidence = store.Evidence.SingleOrDefault(item => item.SourceOrdinal == ordinal);
            Check(record != null || evidence != null, $"Legacy row {ordinal} accounted for");
            if (evidence != null)
                Check(XNode.DeepEquals(row, XElement.Parse(evidence.RawEvidence)), $"Legacy row {ordinal} raw XML preserved");
            if (record != null)
            {
                Check(record.PawnLoadId == (string)row.Element("pawn")
                    && record.LatentAptitude == ((int?)row.Element("latentAptitude") ?? 0)
                    && (int)record.State == ((int?)row.Element("awakeningState") ?? 0)
                    && record.ExtremePhysiqueId == ((string)row.Element("extremePhysiqueId") ?? "")
                    && record.LegacyMigrated == ((bool?)row.Element("legacyMigrated") ?? false),
                    $"Legacy row {ordinal} retains the exact cultivation fields");
                if (record.Association == GuNativeAssociation.Unresolved)
                    Check(record.CurrentLife == GuObservedLife.Unknown && record.LastObservedLife == GuObservedLife.Unknown
                        && record.ConfirmedDiscard == null && record.LastCorpseDestruction == null && record.LastProvenCorpselessDeath == null,
                        $"Unresolved legacy row {ordinal} has no inferred native lifecycle facts");
            }
        }

        foreach (var saved in input.Descendants("li").Where(node => (string)node.Attribute("Class") == typeof(Hediff_Aperture).FullName))
        {
            int load = (int)saved.Element("loadID");
            var aperture = c3Apertures.Keys.SingleOrDefault(item => item.loadID == load);
            Check(aperture != null && aperture.Aptitude == ((int?)saved.Element("guAptitude") ?? 40)
                && aperture.ExtremePhysiqueId == ((string)saved.Element("guExtremePhysiqueId") ?? "")
                && aperture.Rank == ((int?)saved.Element("guRank") ?? 1)
                && Math.Abs(aperture.Essence - ((float?)saved.Element("guEssence") ?? 0f)) < 0.0001f,
                $"Native Hediff {load} retained its legacy values");
            if (saved.Element("guApertureId") is { } id)
                Check(aperture.ApertureId == id.Value, $"Native Hediff {load} retained its exact saved ID");
            if (store.ByPawn.TryGetValue(aperture.pawn, out var bound))
                Check(aperture.ApertureId != null && store.ByAperture.GetValueOrDefault(aperture.ApertureId) == bound,
                    $"Cultivator migration retained the owner association for Hediff {load}");
            if (store.Records.SingleOrDefault(record => record.CreationKey == $"legacy-aperture:{aperture.pawn.GetUniqueLoadID()}") is { } imported)
                Check(imported.LatentAptitude == aperture.Aptitude && imported.ExtremePhysiqueId == aperture.ExtremePhysiqueId
                    && imported.State == AwakeningState.Awakened && imported.LegacyMigrated,
                    $"Aperture-only identity for Hediff {load} copied the exact Hediff fields");
        }

        switch (C3Case)
        {
            case "legacy-evidence":
                Check(store.Records.Single(row => row.CreationKey == "legacy:0").Association == GuNativeAssociation.Bound
                    && store.Records.Single(row => row.CreationKey == "legacy:1").Association == GuNativeAssociation.Unresolved
                    && !store.Records.Any(row => row.CreationKey is "legacy:2" or "legacy:3" or "legacy:4" or "legacy:5" or "legacy:6")
                    && store.Evidence.Count(entry => entry.SourceOrdinal >= 0) == 6,
                    "Only the exact bound Pawn and historically supported token produced identities");
                break;
            case "aperture-conflicts":
                Check(store.ByPawn.Count == 0 && store.ByAperture.Count == 0
                    && store.Evidence.Any(entry => entry.Key.StartsWith("invalid-aperture:", StringComparison.Ordinal))
                    && store.Evidence.Any(entry => entry.Key.StartsWith("aperture:", StringComparison.Ordinal)),
                    "Duplicate and malformed aperture IDs retained evidence and granted no ownership");
                break;
            case "duplicate-identities":
                Check(store.Records.Count == 2 && store.ById.Count == 0 && store.ByPawn.Count == 0 && store.ByAperture.Count == 0,
                    "Both duplicate identity claims survived without a winner");
                break;
            case "valid-committed":
                var row = store.Records.Single();
                var saved = component.Element("guCultivatorRecordsV1").Element("li");
                Check(row.Id == (string)saved.Element("id") && row.PawnLoadId == (string)saved.Element("pawnLoadId")
                    && row.Association == GuNativeAssociation.Bound && row.LatentAptitude == 0
                    && row.ExtremePhysiqueId == "c3-saved-physique" && row.State == AwakeningState.Awakened && row.LegacyMigrated,
                    "Committed identity and zero aptitude remain authoritative over the different native Hediff value");
                break;
            case "valid-empty":
                Check(store.Records.Count == 0 && store.Evidence.Count == 0, "Committed empty store reopened without legacy import");
                break;
            case "partial-failure":
            case "failed-rollback":
                Check(c3Apertures.Count(pair => pair.Value == null && GuCultivatorRecord.IsId(pair.Key.ApertureId)) >= 2,
                    "Pristine retry committed staged aperture writes after the failure was removed");
                break;
        }
    }
}
