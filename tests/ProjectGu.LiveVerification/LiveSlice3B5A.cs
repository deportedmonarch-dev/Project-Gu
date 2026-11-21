using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B5A => GenCommandLine.CommandLineArgPassed("guslice3b5a");
    private static string CohortFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-cohort-fixture.json");
    private static Pawn_GuWildProjection watchedCohortDiscard;

    private record CohortClaim(GuCohortMaterializationRequest Request, GuRecordSnapshot Record, bool Debited, string Quarantine);
    private record CohortFixture(string CaveId, string HostId, string OtherHabitatId, string DuplicateId, int HopeAdults,
        CohortClaim Healthy, CohortClaim Undebited, CohortClaim Debited);

    private static void ExerciseSlice3B5A()
    {
        Map home = Find.CurrentMap;
        Pawn host = home.mapPawns.FreeColonistsSpawned.First();
        var site = OpenIndependentSite(host, home);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        Map cave = exit.Map;
        var spring = BotanicalSpring(cave);
        var other = SpawnAwakeningSpring(home, EmptyCargoCell(home, entrance.Position, null, 4));
        spring.DebugSeedSecondaryCohort();
        other.DebugSeedSecondaryCohort();
        var cohort = TestCohort(spring);
        int hope = spring.HopeGuAvailable;
        var registry = IndividualRegistry;
        Check(cohort.MatureCount == 2 && cohort.JuvenileCount == 1 && cohort.Revision == 0 && cohort.NextEcologyTick == -1,
            "The seeded test cohort starts with two adults and one juvenile and has no reproduction schedule");
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out string reason) && host.Map == cave,
            $"The colonist entered the source habitat's return route. {reason}");

        IntVec3 cell = GenRadial.RadialCellsAround(spring.parent.Position, 6f, false)
            .First(candidate => IsEmptyInventoryCell(cave, candidate)
                && GenAdj.CardinalDirections.Any(offset => IsEmptyInventoryCell(cave, candidate + offset)));
        var primary = CaptureCohort(spring, $"dev-cohort:{spring.HabitatId}:{CompGuSpiritSpring.TestSpecies}");
        var stale = CaptureCohort(spring, "fixture-stale");
        bool Rejected(GuCohortMaterializationRequest request) => !registry.TryMaterializeCohort(spring, request, cave, cell, out _, out _);
        Check(host.Position == exit.Position && Rejected(primary) && cohort.Revision == 0,
            "An occupied return passage rejects materialization");
        TransferToFixture(host, cave, EmptyCargoCell(cave, exit.Position, host, 2));

        var blocker = GenSpawn.Spawn(MakeStack(1), cell, cave);
        Prefs.DevMode = false;
        bool disabled = Rejected(primary);
        Prefs.DevMode = true;
        Check(disabled && Rejected(primary) && blocker.Spawned
            && !registry.TryMaterializeCohort(spring, primary, home, cell, out _, out _)
            && !registry.TryMaterializeCohort(other, primary, cave, cell, out _, out _)
            && Rejected(CaptureCohort(spring, "fixture-hope", "GU_HopeGu"))
            && cohort.MatureCount == 2 && spring.HopeGuAvailable == hope && registry.Individuals.Count == 0,
            "Blocked placement, wrong map, wrong habitat, out-of-scope species and disabled Development Mode leave both cohorts intact");
        blocker.Destroy();
        var habitatField = AccessTools.Field(typeof(CompGuSpiritSpring), "habitatId");
        string otherId = other.HabitatId;
        habitatField.SetValue(other, spring.HabitatId);
        Check(Rejected(primary) && cohort.MatureCount == 2 && !ActuallyProjectedGu().Any(),
            "A duplicate habitat ID on a different loaded map vetoes materialization");
        habitatField.SetValue(other, otherId);

        var fault = new Harmony("projectgu.verification.slice3b5a.cohort");
        CohortClaim healthy, undebited, debited;
        try
        {
            fault.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseSpawn)));
            fault.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.GetUniqueLoadID)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseIdentityCheck)));
            fault.Patch(AccessTools.DeclaredMethod(typeof(WorldPawns), nameof(WorldPawns.PassToWorld)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(RejectCohortDiscard)));
            Pawn_GuWildProjection aborted = null;
            bool stagedSafely = false;
            const string reentrySave = "gu-cohort-save-reentry";
            afterReleaseSpawn = pawn =>
            {
                aborted = pawn;
                stagedSafely = pawn.Spawned && pawn.ReleaseSuppressed && registry.Resolve(pawn) == null
                    && registry.Individuals.Count == 0 && cohort.MatureCount == 2 && Rejected(primary);
                GameDataSaveLoader.SaveGame(reentrySave);
                throw new InvalidOperationException("Injected cohort materialization spawn failure");
            };
            bool rejectedRollback = !registry.TryMaterializeCohort(spring, primary, cave, cell, out _, out string rollbackReason);
            Check(rejectedRollback && stagedSafely && aborted is { Discarded: true, Dead: false }
                && !File.Exists(GenFilePaths.FilePathForSavedGame(reentrySave))
                && registry.Individuals.Count == 0 && cohort.TotalCount == 3 && cohort.Revision == 0,
                $"Native callback rollback clears the staged Pawn and admits a safe retry without a debit or save reentry. {rollbackReason}");
            CheckOldPawnAbsent(aborted.GetUniqueLoadID());
            afterReleaseSpawn = null;

            Check(registry.TryMaterializeCohort(spring, primary, cave, cell, out var individual, out reason),
                $"Retry committed exactly one adult. {reason}");
            var wild = (Pawn_GuWildProjection)ActuallyProjectedGu().Single();
            Check(registry.Resolve(wild) == individual && individual.Id == wild.GuId && individual.Revision == wild.GuRevision
                && individual.Rank == individual.Species.minRank && wild.kindDef == individual.Species.wildProjectionKind
                && !wild.ReleaseSuppressed && wild.Map == cave && wild.Position == cell
                && cohort.MatureCount == 1 && cohort.JuvenileCount == 1 && cohort.Revision == 1,
                "One cohort debit produced one authorized species-defined wild projection");
            Check(Rejected(stale) && registry.TryMaterializeCohort(spring, primary, cave, cell, out var replay, out _)
                && replay == individual && registry.Individuals.Count == 1 && cohort.Revision == 1
                && !registry.TryMaterializeCohort(other, CaptureCohort(other, primary.EventKey), home, cell, out _, out _),
                "A stale new event and a conflicting-source replay are rejected while the original event returns the same Gu");
            CheckMaterializationConservation(spring, hope);

            Check(registry.TryMakeDormant(wild, wild.GuRevision, cell, out var dormant, out reason), $"Materialized Gu entered dormant custody. {reason}");
            IntVec3 hostCell = GenAdj.CardinalDirections.Select(offset => cell + offset).First(candidate => IsEmptyInventoryCell(cave, candidate));
            TransferToFixture(host, cave, hostCell);
            Check(registry.TryTakeDormant(dormant, dormant.GuRevision, host, out reason), $"The same Gu entered native inventory. {reason}");
            healthy = ReadCohortClaim(primary, individual);
            Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out reason) && host.Map == home
                && dormant.holdingOwner == host.inventory.GetDirectlyHeldThings() && registry.Resolve(dormant) == individual,
                $"The materialized Gu traveled through the passage in the exact native inventory. {reason}");
            TransferToFixture(host, home, EmptyCargoCell(home, entrance.Position, host, 2));

            var uncertain = CaptureCohort(spring, "fixture-uncertain-cleanup");
            afterReleaseSpawn = pawn =>
            {
                watchedCohortDiscard = pawn;
                throw new InvalidOperationException("Injected failure before cohort debit with uncertain retirement");
            };
            bool rejectedUncertain = !registry.TryMaterializeCohort(spring, uncertain, cave, cell, out _, out string uncertaintyReason);
            Check(rejectedUncertain && watchedCohortDiscard != null,
                $"Interrupted cleanup recorded an incomplete materialization. {uncertaintyReason}");
            CheckOldPawnAbsent(watchedCohortDiscard.GetUniqueLoadID());
            afterReleaseSpawn = null;
            watchedCohortDiscard = null;
            var incomplete = registry.Individuals.Single(entry => entry.CreationEventKey == uncertain.EventKey);
            undebited = ReadCohortClaim(uncertain, incomplete);
            Check(incomplete.IsQuarantined && !incomplete.CohortDebitCommitted && registry.Resolve(incomplete.Id) == null
                && Rejected(uncertain) && cohort.MatureCount == 1 && cohort.Revision == 1,
                "Uncertain precommit cleanup blocks replay and preserves the adult and cohort revision");
            CheckMaterializationConservation(spring, hope);

            var committedFault = CaptureCohort(spring, "fixture-committed-fault");
            Pawn_GuWildProjection badBinding = null;
            duringReleaseIdentityCheck = pawn =>
            {
                if (pawn.ReleaseSuppressed || cohort.Revision != 2) return;
                badBinding = pawn;
                duringReleaseIdentityCheck = null;
                throw new InvalidOperationException("Injected native identity failure after a cohort debit");
            };
            Check(Rejected(committedFault) && badBinding is { Discarded: true, Dead: false }, "Postcommit binding failure removed the native projection");
            CheckOldPawnAbsent(badBinding.GetUniqueLoadID());
            var lost = registry.Individuals.Single(entry => entry.CreationEventKey == committedFault.EventKey);
            debited = ReadCohortClaim(committedFault, lost);
            Check(lost.IsQuarantined && lost.CohortDebitCommitted && registry.Resolve(lost.Id) == null && Rejected(committedFault)
                && cohort.MatureCount == 0 && cohort.JuvenileCount == 1 && cohort.Revision == 2
                && Rejected(CaptureCohort(spring, "fixture-only-juveniles"))
                && ActuallyProjectedGu().Single() == dormant && registry.Individuals.Count == 3,
                "A committed quarantine conserves the debited identity and insufficient adults cause no new allocations");
            CheckMaterializationConservation(spring, hope);
        }
        finally
        {
            fault.UnpatchAll(fault.Id);
            afterReleaseSpawn = duringReleaseIdentityCheck = null;
            watchedCohortDiscard = null;
        }
        Check(TestCohort(other).MatureCount == 2 && TestCohort(other).Revision == 0, "The other habitat kept its exact cohort");

        GameDataSaveLoader.SaveGame(SaveName);
        string path = GenFilePaths.FilePathForSavedGame(SaveName);
        var xml = XDocument.Load(path);
        var records = xml.Descendants("guIndividualRecordsV1").Single();
        string duplicateId = Guid.NewGuid().ToString("N");
        // Simulate ambiguous saved event evidence in this disposable fixture only.
        var duplicate = new XElement(records.Elements("li").Single(entry => (string)entry.Element("id") == undebited.Record.Id));
        duplicate.SetElementValue("id", duplicateId);
        duplicate.Element("quarantineReason")?.Remove();
        records.Add(duplicate);
        xml.Save(path);
        var fixture = new CohortFixture(cave.GetUniqueLoadID(), host.GetUniqueLoadID(), otherId, duplicateId, hope, healthy, undebited, debited);
        File.WriteAllText(CohortFixturePath, JsonSerializer.Serialize(fixture));
        Report("Exact cohort debit and conservation passed through clean retry, stale request, duplicate habitat, insufficient adults, guarded callbacks, and separate precommit and postcommit quarantines.");
    }

    private static void VerifySlice3B5AReload()
    {
        var fixture = JsonSerializer.Deserialize<CohortFixture>(File.ReadAllText(CohortFixturePath));
        var registry = IndividualRegistry;
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        AssertNativeAddress(cave);
        Pawn host = home.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.HostId);
        var spring = BotanicalSpring(cave);
        var other = BotanicalSpring(home);
        var cohort = TestCohort(spring);
        var dormant = (Thing_GuDormantProjection)ActuallyProjectedGu().Single();
        var individual = registry.Resolve(dormant);
        Check(registry.Individuals.Count == 4 && dormant.holdingOwner == host.inventory.GetDirectlyHeldThings()
            && individual != null && !individual.IsQuarantined, "Reload retained one authorized native Thing and the exact inventory");
        foreach (var expected in new[] { fixture.Healthy, fixture.Undebited, fixture.Debited })
        {
            var entry = registry.Individuals.Single(record => record.Id == expected.Record.Id);
            Check(ReadCohortClaim(expected.Request, entry) == expected
                && registry.Resolve(entry.Id) == (entry.IsQuarantined ? null : individual),
                "Fresh-process load retained the exact event and its debit state and physical authorization");
        }
        var duplicate = registry.Individuals.Single(record => record.Id == fixture.DuplicateId);
        Check(duplicate.IsQuarantined && duplicate.QuarantineReason.Contains("Multiple individuals") && !duplicate.CohortDebitCommitted
            && registry.Resolve(duplicate.Id) == null && registry.Resolve(fixture.Undebited.Record.Id) == null,
            "Duplicate saved event claims quarantine all claimants");
        CheckMaterializationConservation(spring, fixture.HopeAdults);
        Check(cohort.MatureCount == 0 && cohort.JuvenileCount == 1 && cohort.Revision == 2
            && fixture.OtherHabitatId == other.HabitatId && TestCohort(other).MatureCount == 2
            && TestCohort(other).Revision == 0, "Source revision and both habitat populations persisted");

        var entrance = home.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.endpoint).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out string reason) && host.Map == cave
            && dormant.MapHeld == cave && registry.Resolve(dormant) == individual, $"The same Gu traveled back to the source after reload. {reason}");
        Check(registry.TryMaterializeCohort(spring, fixture.Healthy.Request, cave, IntVec3.Invalid, out var replay, out _)
            && replay == individual && !registry.TryMaterializeCohort(spring, fixture.Undebited.Request, cave, IntVec3.Invalid, out _, out _)
            && !registry.TryMaterializeCohort(spring, fixture.Debited.Request, cave, IntVec3.Invalid, out _, out _)
            && ActuallyProjectedGu().Single() == dormant && registry.Individuals.Count == 4 && cohort.Revision == 2,
            "Persisted event replay returns the same individual and refuses both quarantined events");
        CheckMaterializationConservation(spring, fixture.HopeAdults);
        Report("Fresh-process reload retained the conserved population, exact Gu and native inventory across passage travel, blocked ambiguous and incomplete events, and replayed the original event without allocation.");
    }

    private static GuCohortMaterializationRequest CaptureCohort(CompGuSpiritSpring spring, string key,
        string species = CompGuSpiritSpring.TestSpecies)
    {
        Check(GuIndividualRegistry.TryPrepareMaterializationRequest(spring, species, key, out var request), "Captured an exact cohort request");
        return request;
    }

    private static GuPopulationRecord TestCohort(CompGuSpiritSpring spring) =>
        ((IGuCohortHabitat)spring).CohortFor(CompGuSpiritSpring.TestSpecies);

    private static CohortClaim ReadCohortClaim(GuCohortMaterializationRequest request, GuIndividual record)
    {
        Check(record.Origin == GuIndividual.CohortOrigin && record.OriginId == request.HabitatId
            && record.OriginNativeId == request.SourceNativeId && record.SpeciesDefName == request.SpeciesDefName
            && record.CreationEventKey == request.EventKey, "The record retained the exact cohort origin, species and event");
        return new(request, Snapshot(record), record.CohortDebitCommitted, record.QuarantineReason);
    }

    private static void CheckMaterializationConservation(CompGuSpiritSpring spring, int hope)
    {
        long population = TestCohort(spring).TotalCount;
        int individuals = IndividualRegistry.Individuals.Count(record => record.CohortDebitCommitted
            && record.OriginId == spring.HabitatId && record.SpeciesDefName == CompGuSpiritSpring.TestSpecies);
        Check(population + individuals == 3 && spring.HopeGuAvailable == hope,
            $"Exact cohort conservation holds. Aggregate {population}, debited individuals {individuals}, Hope Gu {hope}");
    }

    private static void RejectCohortDiscard(Pawn __0, PawnDiscardDecideMode __1)
    {
        if (__0 == watchedCohortDiscard && __1 == PawnDiscardDecideMode.Discard)
            throw new InvalidOperationException("Injected failure before native provisional Pawn discard");
    }
}
