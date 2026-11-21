using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B5B => GenCommandLine.CommandLineArgPassed("guslice3b5b");

    private static void ContinueCohortLifecycleInAperture(CohortFixture fixture, Pawn host, Thing_GuDormantProjection thing,
        GuIndividual record, CompGuSpiritSpring spring, Building_GuPocketProbeEndpoint entrance)
    {
        var aperture = AddProbeAperture(host);
        Check(!IndividualRegistry.TryStoreDormant(thing, fixture.Healthy.Record.Revision - 1, host, aperture, out _)
            && ReadCohortClaim(fixture.Healthy.Request, record) == fixture.Healthy, "A stale deposit leaves the authorized inventory intact");
        Check(IndividualRegistry.TryStoreDormant(thing, fixture.Healthy.Record.Revision, host, aperture, out string reason),
            $"The previously materialized Gu entered its exact aperture. {reason}");
        var expected = fixture.Healthy.Record with {
            Revision = 4, Custody = new(GuCustodyKind.Aperture, aperture.GetUniqueLoadID(), host.GetUniqueLoadID())
        };
        Check(thing.Destroyed && !ActuallyProjectedGu().Any() && IndividualRegistry.ResolveApertureOccupant(aperture) == record
            && !IndividualRegistry.TryStoreDormant(thing, fixture.Healthy.Record.Revision, host, aperture, out _),
            "Deposit destroys the sole physical projection and rejects a duplicate");

        Map cave = host.Map;
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out reason) && host.Map == entrance.Map
            && IndividualRegistry.ResolveApertureOccupant(aperture) == record,
            $"Record-only custody crossed to the surface through the saved passage. {reason}");
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out reason) && host.Map == cave
            && IndividualRegistry.ResolveApertureOccupant(aperture) == record,
            $"Record-only custody returned to its source level. {reason}");
        CheckIntegratedCohortState(fixture, expected, record, spring);
        SaveCohortLifecycle(fixture with { Stored = expected });
        Report("Integrated aperture commit reached revision four, traveled both ways, and saved with zero native Gu projections.");
    }

    private static void ReleaseIntegratedCohort()
    {
        var fixture = ReadCohortLifecycle();
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        Map surface = Find.Maps.Single(map => map.IsPlayerHome);
        var spring = BotanicalSpring(cave);
        var registry = IndividualRegistry;
        Pawn host = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.HostId);
        var aperture = host.health.hediffSet.hediffs.OfType<Hediff_Aperture>()
            .Single(hediff => hediff.GetUniqueLoadID() == fixture.Stored.Custody.NativeOwnerId);
        var record = registry.ResolveApertureOccupant(aperture);
        Check(record != null && !ActuallyProjectedGu().Any(), "A separate process reconstructed the real aperture with no Pawn or Thing");
        CheckIntegratedCohortState(fixture, fixture.Stored, record, spring);

        EnsureInventoryFixtureSpace(host);
        IntVec3 cell = OpenInventoryCell(host);
        bool TryRelease(int revision, out Pawn_GuWildProjection pawn) =>
            registry.TryReleaseActive(record.Id, revision, host, aperture, cave, cell, out pawn, out _);
        var blocker = GenSpawn.Spawn(MakeStack(1), cell, cave);
        Check(!TryRelease(4, out _) && blocker.Spawned, "An occupied release destination leaves aperture custody intact");
        blocker.Destroy();
        Check(!TryRelease(3, out _), "An inventory-era revision cannot authorize aperture release");

        var fault = new Harmony("projectgu.verification.slice3b5b.release");
        Pawn_GuWildProjection provisional = null;
        try
        {
            fault.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseSpawn)));
            afterReleaseSpawn = pawn =>
            {
                if (pawn.GuId != record.Id) return;
                provisional = pawn;
                throw new InvalidOperationException("Injected integrated release interruption");
            };
            Check(!TryRelease(4, out _) && provisional is { Discarded: true, Dead: false }
                && !ActuallyProjectedGu().Any(), "Interrupted release cleaned up the staged Pawn");
        }
        finally
        {
            fault.UnpatchAll(fault.Id);
            afterReleaseSpawn = null;
        }
        CheckOldPawnAbsent(provisional.GetUniqueLoadID());
        CheckIntegratedCohortState(fixture, fixture.Stored, registry.ResolveApertureOccupant(aperture), spring);

        Check(TryRelease(4, out var released), "The original cohort Gu released from healthy aperture custody");
        var expected = fixture.Stored with {
            Revision = 5, Biology = GuBiologicalState.Active, Custody = new(GuCustodyKind.WildPawn, released.GetUniqueLoadID())
        };
        Check(registry.Resolve(released) == record && registry.ResolveApertureOccupant(aperture) == null
            && ActuallyProjectedGu().Single() == released && released.GuId == record.Id && released.GuRevision == 5
            && !TryRelease(4, out _) && !TryRelease(5, out _), "Release commits the fifth revision, replaces the slot, and rejects both replays");
        CheckIntegratedCohortState(fixture, expected, record, spring);
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out string reason) && host.Map == surface, $"Host returned without the released Pawn. {reason}");
        CheckGuAloneProtectsRoute(cave, surface, released);
        SaveCohortLifecycle(fixture with { Released = expected });
        Report("Integrated fresh-process aperture release preserved the cohort identity, rolled back a native fault, reached revision five, and protected its last access.");
    }

    private static void VerifyIntegratedCohortRelease()
    {
        var fixture = ReadCohortLifecycle();
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        AssertNativeAddress(cave);
        var released = (Pawn_GuWildProjection)ActuallyProjectedGu().Single();
        var record = IndividualRegistry.Resolve(released);
        Check(record != null && released.Map == cave && !released.ReleaseSuppressed
            && released.GetUniqueLoadID() == fixture.Released.Custody.NativeOwnerId && released.GuRevision == 5,
            "The final restart restored the same native released Pawn with revision five");
        CheckIntegratedCohortState(fixture, fixture.Released, record, BotanicalSpring(cave));
        CheckGuAloneProtectsRoute(cave, Find.Maps.Single(map => map.IsPlayerHome), released);
        Report("Integrated lifecycle conserved one cohort-origin Gu through revisions one to five and all three fresh-process reloads.");
    }

    private static void CheckIntegratedCohortState(CohortFixture fixture, GuRecordSnapshot expected,
        GuIndividual record, CompGuSpiritSpring spring)
    {
        var registry = IndividualRegistry;
        Check(record != null && registry.Individuals.Count == 4 && ReadCohortClaim(fixture.Healthy.Request, record)
            == fixture.Healthy with { Record = expected }, "The exact identity and cohort provenance survived the custody change");
        VerifyCohortQuarantine(fixture);
        Check(registry.TryMaterializeCohort(spring, fixture.Healthy.Request, spring.parent.Map, IntVec3.Invalid, out var replay, out _)
            && replay == record && TestCohort(spring).Revision == 2 && TestCohort(spring).MatureCount == 0
            && !registry.TryMaterializeCohort(spring, fixture.Undebited.Request, spring.parent.Map, IntVec3.Invalid, out _, out _)
            && !registry.TryMaterializeCohort(spring, fixture.Debited.Request, spring.parent.Map, IntVec3.Invalid, out _, out _),
            "The original event remains idempotent and neither quarantined event authorizes a replacement");
        CheckMaterializationConservation(spring, fixture.HopeAdults);
    }

    private static CohortFixture ReadCohortLifecycle() =>
        JsonSerializer.Deserialize<CohortFixture>(File.ReadAllText(CohortFixturePath));

    private static void SaveCohortLifecycle(CohortFixture fixture)
    {
        File.WriteAllText(CohortFixturePath, JsonSerializer.Serialize(fixture));
        GameDataSaveLoader.SaveGame(SaveName);
    }
}
