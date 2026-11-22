using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice3B4 => GenCommandLine.CommandLineArgPassed("guslice3b4");
    private static string ReleaseFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-release-fixture.json");
    private static Action<Pawn_GuWildProjection> afterReleaseSpawn;
    private static Action<Pawn_GuWildProjection> duringReleaseIdentityCheck;
    private static Pawn_GuWildProjection watchedReleaseTick;
    private static int releaseNativeTicks;

    private record ReleaseFixture(string CaveId, string SurfaceId, GuRecordSnapshot Stored,
        ApertureClaim[] Lost, GuRecordSnapshot Released = null);

    private static void ExerciseSlice3B4()
    {
        Map home = Find.CurrentMap;
        Pawn[] colonists = home.mapPawns.FreeColonistsSpawned.Take(3).ToArray();
        Pawn host = colonists[0], replacementHost = colonists[1], dyingHost = colonists[2];
        Map surface = ExtraSettlement();
        var site = OpenIndependentSite(host, surface);
        var entrance = site.Endpoints.Single(end => end.IsSurface).Anchor;
        Map cave = site.Levels.Single().LiveMap;
        foreach (var colonist in colonists) EnsureInventoryFixtureSpace(colonist);

        var registry = IndividualRegistry;
        var aperture = AddProbeAperture(host);
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        IntVec3 origin = OpenInventoryCell(host);
        Check(registry.TrySpawnDevelopmentGu(species, surface, origin, out var wild), "Created the original wild Gu");
        foreach (Hediff condition in wild.health.hediffSet.hediffs.ToArray()) wild.health.RemoveHediff(condition);
        var record = registry.Resolve(wild);
        var birth = Snapshot(record);
        string oldPawnId = wild.GetUniqueLoadID();
        Check(registry.TryMakeDormant(wild, birth.Revision, origin, out var dormant, out string reason), $"Dormancy committed. {reason}");
        VerifyReleaseStep(record, birth, GuBiologicalState.Dormant, new(GuCustodyKind.DormantGround, dormant.GetUniqueLoadID()), 1, 1, dormant);
        CheckOldPawnAbsent(oldPawnId);
        Check(registry.TryTakeDormant(dormant, dormant.GuRevision, host, out reason), $"Inventory pickup committed. {reason}");
        VerifyReleaseStep(record, birth, GuBiologicalState.Dormant,
            new(GuCustodyKind.DormantInventory, dormant.GetUniqueLoadID(), host.GetUniqueLoadID()), 1, 1, dormant);
        Check(registry.TryStoreDormant(dormant, dormant.GuRevision, host, aperture, out reason) && dormant.Destroyed, $"Aperture storage committed. {reason}");
        VerifyReleaseStep(record, birth, GuBiologicalState.Dormant,
            new(GuCustodyKind.Aperture, aperture.ApertureId, host.GetUniqueLoadID()), 2, 1);
        var stored = Snapshot(record);
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, entrance, out reason) && host.Map == cave
            && registry.ResolveApertureOccupant(aperture) == record && Snapshot(record) == stored, $"The original aperture crossed underground. {reason}");
        EnsureInventoryFixtureSpace(host);
        IntVec3 cell = OpenInventoryCell(host);
        bool TryRelease(Map map, IntVec3 target, int revision) =>
            registry.TryReleaseActive(record.Id, revision, host, aperture, map, target, out _, out _);
        var blocker = GenSpawn.Spawn(MakeStack(1), cell, cave);
        Check(!TryRelease(cave, cell, stored.Revision) && blocker.Spawned && Snapshot(registry.Resolve(record.Id)) == stored,
            "An occupied cell leaves the aperture and existing occupant intact");
        blocker.Destroy();
        Check(!TryRelease(surface, cell, stored.Revision) && !TryRelease(cave, IntVec3.Invalid, stored.Revision)
            && !TryRelease(cave, cell, stored.Revision - 1) && !TryRelease(cave, host.Position, stored.Revision)
            && Snapshot(registry.Resolve(record.Id)) == stored, "Invalid, cross-map, or stale requests preserve exact source custody");

        var route = site.Routes.Single();
        site.Routes.Remove(route);
        Check(!TryRelease(cave, cell, stored.Revision) && !ActuallyProjectedGu().Any()
            && Snapshot(registry.Resolve(record.Id)) == stored, "The independent level needs a usable registered route");
        site.Routes.Add(route);

        var removed = AddProbeAperture(replacementHost);
        var replaced = StoreFixtureGu(replacementHost, removed);
        replacementHost.health.RemoveHediff(removed);
        var replacement = AddProbeAperture(replacementHost);
        registry.ApplyPendingEvents();
        Check(replaced.Unresolved == GuUnresolvedCause.ApertureMissing && !replaced.IsQuarantined && registry.ResolveApertureOccupant(replacement) == null
            && !registry.TryReleaseActive(replaced.Id, replaced.Revision, replacementHost, replacement,
                home, OpenInventoryCell(replacementHost), out _, out _), "A same-Def replacement cannot release the former occupant");
        var dyingAperture = AddProbeAperture(dyingHost);
        var death = StoreFixtureGu(dyingHost, dyingAperture);
        IntVec3 deathCell = OpenInventoryCell(dyingHost);
        dyingHost.Kill(null);
        registry.ApplyPendingEvents();
        Check(death.Unresolved == GuUnresolvedCause.HostDead && !death.IsQuarantined && !registry.TryReleaseActive(death.Id, death.Revision, dyingHost, dyingAperture,
            home, deathCell, out _, out _), "A dead host cannot release a Gu");

        var missingHost = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            tile: home.Tile, forceGenerateNewPawn: true));
        GenSpawn.Spawn(missingHost, EmptyCargoCell(home, home.Center, null, 2), home);
        EnsureInventoryFixtureSpace(missingHost);
        var missing = StoreFixtureGu(missingHost, AddProbeAperture(missingHost));
        missingHost.Destroy(DestroyMode.Vanish);

        var late = StoreFixtureGu(replacementHost, replacement);
        var lateSource = Snapshot(late);
        var fault = new Harmony("projectgu.verification.slice3b4.release");
        try
        {
            fault.Patch(AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseSpawn)));
            fault.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.GetUniqueLoadID)),
                prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseIdentityCheck)));
            foreach (string method in new[] { "Tick", "TickInterval", "TickRare" })
                fault.Patch(AccessTools.DeclaredMethod(typeof(Pawn), method),
                    prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveReleaseTick)));
            Pawn_GuWildProjection prepared = null;
            bool isolated = false;
            const string reentrySave = "gu-active-release-reentry";
            afterReleaseSpawn = pawn =>
            {
                if (pawn.GuId != stored.Id) return;
                prepared = pawn;
                watchedReleaseTick = pawn;
                releaseNativeTicks = 0;
                AccessTools.DeclaredMethod(typeof(Pawn_GuWildProjection), "Tick").Invoke(pawn, null);
                AccessTools.DeclaredMethod(typeof(Pawn_GuWildProjection), "TickInterval").Invoke(pawn, new object[] { 1 });
                pawn.TickRare();
                isolated = pawn.Spawned && pawn.ReleaseSuppressed && releaseNativeTicks == 0 && pawn.CurJob == null
                    && !pawn.GetGizmos().Any() && !pawn.GetFloatMenuOptions(host).Any()
                    && registry.Resolve(pawn) == null && registry.ResolveApertureOccupant(aperture) == null
                    && Snapshot(record) == stored && !TryRelease(cave, cell, stored.Revision);
                GameDataSaveLoader.SaveGame(reentrySave);
                throw new InvalidOperationException("Injected failure after native active-release spawn");
            };
            Check(!TryRelease(cave, cell, stored.Revision) && isolated && prepared is { Discarded: true, Dead: false }
                && Snapshot(registry.Resolve(record.Id)) == stored && !File.Exists(GenFilePaths.FilePathForSavedGame(reentrySave)),
                "Interrupted placement kept the staged Pawn inert, blocked saves and reentry, and restored the aperture");
            CheckOldPawnAbsent(prepared.GetUniqueLoadID());

            afterReleaseSpawn = pawn =>
            {
                if (pawn.GuId == stored.Id) site.Routes.Remove(route);
            };
            Check(!TryRelease(cave, cell, stored.Revision) && site.Routes.Count == 0 && !ActuallyProjectedGu().Any()
                && Snapshot(registry.Resolve(record.Id)) == stored, "Route loss during native placement rolls back the provisional Pawn");
            site.Routes.Add(route);
            afterReleaseSpawn = null;

            Pawn_GuWildProjection failedAfterCommit = null;
            duringReleaseIdentityCheck = pawn =>
            {
                // Reads stored placement. Observed custody would call the hooked GetUniqueLoadID again.
                if (pawn.GuId != late.Id || pawn.ReleaseSuppressed || late.Placement != GuPlacementKind.Manifested) return;
                failedAfterCommit = pawn;
                duringReleaseIdentityCheck = null;
                throw new InvalidOperationException("Injected failure verifying native identity after active-release commit");
            };
            Check(!registry.TryReleaseActive(late.Id, late.Revision, replacementHost, replacement, home,
                    OpenInventoryCell(replacementHost), out _, out _)
                && failedAfterCommit is { Discarded: true, Dead: false } && !late.IsQuarantined
                && late.Unresolved == GuUnresolvedCause.ProjectionMissing && late.Revision == lateSource.Revision + 1
                && late.Manifestation == GuManifestationKind.Pawn && late.ProjectionId == failedAfterCommit.GetUniqueLoadID()
                && registry.Resolve(late.Id) == null && registry.ResolveApertureOccupant(replacement) == null,
                "A post-commit failure leaves the new wild claim unresolved without restoring a second owner");
            CheckOldPawnAbsent(failedAfterCommit.GetUniqueLoadID());
        }
        finally
        {
            fault.UnpatchAll(fault.Id);
            afterReleaseSpawn = duringReleaseIdentityCheck = null;
            watchedReleaseTick = null;
        }

        Check(!missing.IsQuarantined && !ActuallyProjectedGu().Any() && registry.Individuals.Count == 5,
            "The pre-release save has five records and zero physical Gu");
        var fixture = new ReleaseFixture(cave.GetUniqueLoadID(), surface.GetUniqueLoadID(), stored, new[] {
            new ApertureClaim(Snapshot(replaced), GuUnresolvedCause.ApertureMissing), new(Snapshot(death), GuUnresolvedCause.HostDead),
            new(Snapshot(missing), GuUnresolvedCause.HostUnavailable), new(Snapshot(late), GuUnresolvedCause.ProjectionMissing)
        });
        WriteReleaseFixture(fixture);
        GameDataSaveLoader.SaveGame(SaveName);
        Report("The original ID advanced through Wild, Dormant, Inventory and Aperture. Blocked placement, route loss, native callback rollback, reentry and an unresolved post-commit failure passed without quarantine.");
    }

    private static void ReleaseAfterReload()
    {
        var fixture = ReadReleaseFixture();
        var registry = IndividualRegistry;
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        Map surface = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.SurfaceId);
        AssertNativeAddress(cave);
        Pawn host = cave.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.GetUniqueLoadID() == fixture.Stored.Custody.HolderPawnId);
        var aperture = host.health.hediffSet.hediffs.OfType<Hediff_Aperture>()
            .Single(hediff => hediff.ApertureId == fixture.Stored.Custody.NativeOwnerId);
        var stored = registry.ResolveApertureOccupant(aperture);
        Check(stored != null && Snapshot(stored) == fixture.Stored && !ActuallyProjectedGu().Any()
            && !cave.GetComponent<GuIndividualMapCustody>().HasProtectedCustody, "Fresh process restored aperture custody with no projected Gu underground");
        CheckReleaseQuarantine(fixture);

        EnsureInventoryFixtureSpace(host);
        IntVec3 cell = OpenInventoryCell(host);
        Check(registry.TryReleaseActive(stored.Id, stored.Revision, host, aperture, cave, cell, out var released, out string reason),
            $"The exact aperture released one native Pawn. {reason}");
        VerifyReleaseStep(stored, fixture.Stored, GuBiologicalState.Active, new(GuCustodyKind.WildPawn, released.GetUniqueLoadID()), 1, 1, released);
        Check(ActuallyProjectedGu().Single() == released && !released.ReleaseSuppressed && released.Spawned
            && released.Map == cave && released.Position == cell && released.Faction == null && registry.ResolveApertureOccupant(aperture) == null
            && !registry.TryReleaseActive(stored.Id, fixture.Stored.Revision, host, aperture, cave, cell, out _, out _)
            && !registry.TryReleaseActive(stored.Id, stored.Revision, host, aperture, cave, cell, out _, out _),
            "One authorized wild Pawn replaced the aperture claim and both replays were rejected");
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(host, exit, out reason) && host.Map == surface, $"The host returned through the saved passage. {reason}");
        CheckGuAloneProtectsRoute(cave, surface, released);

        WriteReleaseFixture(fixture with { Released = Snapshot(stored) });
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Fresh-process aperture reload and release passed. The released wild Gu alone blocked removal of the last surface access.");
    }

    private static void VerifyReleasedReload()
    {
        var fixture = ReadReleaseFixture();
        Map cave = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.CaveId);
        Map surface = Find.Maps.Single(map => map.GetUniqueLoadID() == fixture.SurfaceId);
        AssertNativeAddress(cave);
        CheckReleaseQuarantine(fixture);
        var released = (Pawn_GuWildProjection)ActuallyProjectedGu().Single();
        var registry = IndividualRegistry;
        Check(registry.Resolve(fixture.Stored.Id) is { } record && registry.Resolve(released) == record
            && Snapshot(record) == fixture.Released && released.GuRevision == fixture.Released.Revision && !released.ReleaseSuppressed
            && released.GetUniqueLoadID() == fixture.Released.Custody.NativeOwnerId && released.Map == cave,
            "The second fresh process restored the exact live released Pawn with the original Gu identity and revision");
        CheckGuAloneProtectsRoute(cave, surface, released);
        Report("Fresh-process wild reload conserved the sole native Pawn, all five canonical IDs and the last-access protection.");
    }

    private static void CheckGuAloneProtectsRoute(Map cave, Map surface, Pawn_GuWildProjection released)
    {
        Check(!cave.mapPawns.AllPawns.Any(pawn => pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony)
            && cave.GetComponent<GuIndividualMapCustody>().HasProtectedCustody
            && Registry.ProtectsIndependentAccess(surface), "The Gu is the only protected occupant of the independent level");
        surface.Parent.Abandon(false);
        Check(GuPocketProbeComponent.IsLive(surface) && surface.Parent.Spawned && released.Spawned && released.Map == cave,
            "Native last-access abandonment was refused without moving the Gu");
    }

    private static void VerifyReleaseStep(GuIndividual record, GuRecordSnapshot original, GuBiologicalState biology,
        GuCustody custody, int revisionSteps, int stateSteps, Thing projection = null)
    {
        Check(Snapshot(record) == original with { Biology = biology, Custody = custody, Revision = original.Revision + revisionSteps,
                StateRevision = original.StateRevision + stateSteps }
            && IndividualRegistry.Resolve(original.Id) == record
            && (projection == null || (projection is IGuProjection tag && tag.GuId == original.Id && tag.GuRevision == record.Revision)),
            "The same canonical identity, species, rank and provenance made an exact single-revision transition");
    }

    private static void CheckReleaseQuarantine(ReleaseFixture fixture)
    {
        var registry = IndividualRegistry;
        Check(registry.Individuals.Count == fixture.Lost.Length + 1, "The full set of canonical records survived");
        foreach (var expected in fixture.Lost)
        {
            var record = registry.Individuals.Single(entry => entry.Id == expected.Record.Id);
            Check(Snapshot(record) == expected.Record && registry.Resolve(record.Id) == null && !record.IsQuarantined
                && record.Unresolved == expected.Cause && record.TerminalEvidence == GuTerminalEvidence.None,
                $"Lost hosts, replaced apertures and failed committed claims stay unresolved ({expected.Cause}) and never authorize another release");
        }
    }

    private static ReleaseFixture ReadReleaseFixture() => JsonSerializer.Deserialize<ReleaseFixture>(File.ReadAllText(ReleaseFixturePath));
    private static void WriteReleaseFixture(ReleaseFixture fixture) => File.WriteAllText(ReleaseFixturePath, JsonSerializer.Serialize(fixture));

    private static void ObserveReleaseSpawn(Pawn __instance)
    {
        if (__instance is Pawn_GuWildProjection gu) afterReleaseSpawn?.Invoke(gu);
    }

    private static void ObserveReleaseIdentityCheck(Thing __instance)
    {
        if (__instance is Pawn_GuWildProjection gu) duringReleaseIdentityCheck?.Invoke(gu);
    }

    private static void ObserveReleaseTick(Pawn __instance)
    {
        if (watchedReleaseTick != null && __instance == watchedReleaseTick) releaseNativeTicks++;
    }
}
