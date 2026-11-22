using System.Text.Json;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Engine probe for Gu Framework V2. It began as a characterization of how RimWorld 1.7 and the V1 registry respond to
/// ordinary native events (commit 8da9353) and now asserts the approved V2 outcome for each scenario.
/// </summary>
public sealed class CompV2IdentityProbe : ThingComp
{
    internal static int PostDestroyCalls;
    internal static int MapRemovedCalls;
    internal static int PostDeSpawnCalls;
    internal string LoadedId;
    internal int LoadedRevision = -1;

    public override void PostExposeData()
    {
        // Read the V1 projection keys from the same Thing node without writing duplicate keys.
        if (Scribe.mode != LoadSaveMode.LoadingVars) return;
        Scribe_Values.Look(ref LoadedId, "guIndividualId");
        Scribe_Values.Look(ref LoadedRevision, "guBindingRevision", -1);
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap) => PostDestroyCalls++;
    public override void Notify_MapRemoved() => MapRemovedCalls++;
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) => PostDeSpawnCalls++;
}

[StaticConstructorOnStartup]
internal static class V2EngineProbeInstaller
{
    static V2EngineProbeInstaller()
    {
        if (!GenCommandLine.CommandLineArgPassed("guv2engine")) return;
        foreach (string name in new[] { "GU_DevTestDormantGu", "GU_DevTestWildGu" })
        {
            var def = DefDatabase<ThingDef>.GetNamed(name);
            def.comps.Add(new CompProperties { compClass = typeof(CompV2IdentityProbe) });
            def.notifyMapRemoved = true;
        }
    }
}

public sealed partial class LiveLifecycleChecks
{
    private static bool V2Engine => GenCommandLine.CommandLineArgPassed("guv2engine");

    private static string Holder(Thing thing) => thing.Destroyed ? $"destroyed ({thing.ParentHolder?.GetType().Name ?? "no holder"})"
        : thing.Spawned ? $"map ground {thing.Map?.GetUniqueLoadID()}"
        : thing.ParentHolder is Pawn_InventoryTracker tracker ? $"inventory of {tracker.pawn.GetUniqueLoadID()} (dead={tracker.pawn.Dead}, freeColonist={tracker.pawn.IsFreeColonist}, inCorpse={tracker.pawn.Corpse != null})"
        : thing.ParentHolder?.GetType().Name ?? "detached";

    private static string State(GuIndividual record) =>
        $"custody={record.Custody.Kind} revision={record.Revision} quarantined={record.IsQuarantined}"
        + (record.IsQuarantined ? $" reason=\"{record.QuarantineReason}\"" : "");

    private static Pawn ExtraColonist(Map map, IntVec3 near)
    {
        var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            tile: map.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
        GenSpawn.Spawn(pawn, EmptyCargoCell(map, near, null, 3), map);
        foreach (Hediff condition in pawn.health.hediffSet.hediffs.ToArray()) pawn.health.RemoveHediff(condition);
        return pawn;
    }

    private static string V2ProbeFixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-v2-probe.json");
    private record ProbeExpectation(string Case, string Id, string Outcome, string Cause, string Evidence);

    /// <summary>
    /// V2 acceptance form of the engine probe. Each scenario that quarantined a V1 record now has an asserted V2 outcome:
    /// ordinary movement stays bound, demonstrated death and destruction are terminal (Phase 2), lost maps and world Pawns keep identity at world locations, and nothing is quarantined.
    /// </summary>
    private static void ExerciseV2EngineProbe()
    {
        var registry = IndividualRegistry;
        Map home = Find.CurrentMap;
        var colonists = home.mapPawns.FreeColonistsSpawned.ToArray();
        Check(colonists.Length >= 3, "The quick-test colony supplies three colonists");
        Report($"Environment: RimWorld {VersionControl.CurrentVersionStringWithRev}, mods: {string.Join(", ", ModsConfig.ActiveModsInLoadOrder.Select(mod => mod.PackageId))}");
        var expectations = new List<ProbeExpectation>();

        void ExpectBound(string name, GuIndividual record, Thing projection, GuCustodyKind kind)
        {
            Check(record.IsActionable && registry.Resolve(projection) == record && record.Custody.Kind == kind,
                $"{name}: the same authorized manifestation is observed as {kind}, not quarantined. Observed {record.Custody.Kind}, {State(record)}");
            expectations.Add(new(name, record.Id, "bound", null, null));
            Report($"OBS {name}: {Holder(projection)}; {State(record)}");
        }

        void ExpectTerminal(string name, GuIndividual record, GuTerminalOutcome outcome)
        {
            registry.ApplyPendingEvents();
            registry.Sweep(int.MaxValue);
            Check(!record.IsQuarantined && record.Existence == GuExistence.Terminal && record.TerminalOutcome == outcome
                && !record.IsUnresolved && registry.Resolve(record.Id) == null && record.History.Any(entry => entry.Kind == GuLifecycleEventKind.Terminal),
                $"{name}: the demonstrated event ended the Gu as {outcome} with evidence. Observed {record.Existence}/{record.TerminalOutcome} {record.Unresolved}/{record.TerminalEvidence}");
            expectations.Add(new(name, record.Id, "terminal", outcome.ToString(), null));
            Report($"OBS {name}: terminal {record.TerminalOutcome}; last known {record.LastKnown?.Describe()}");
        }

        void ExpectWorldSite(string name, GuIndividual record, Map removed)
        {
            registry.ApplyPendingEvents();
            registry.Sweep(int.MaxValue);
            Check(!record.IsQuarantined && record.IsActionable && record.Existence == GuExistence.Living
                && record.Placement == GuPlacementKind.Contained && record.ContainerKind == GuContainerKind.WorldSite
                && record.LastKnown?.HasTile == true && (removed == null || record.LastKnown.MapId == removed.GetUniqueLoadID())
                && registry.Resolve(record.Id) == record && record.Custody.Kind == GuCustodyKind.WorldSite,
                $"{name}: the Gu kept its identity at an abstract world location, never terminal. Observed {record.Placement}/{record.ContainerKind} {record.Unresolved} {record.LastKnown?.Describe()}");
            expectations.Add(new(name, record.Id, "worldsite", null, record.LastKnown.Tile.ToString()));
            Report($"OBS {name}: world location {record.LastKnown.Describe()}");
        }

        // 1. A downed holder drops its inventory natively. Inspection must not change the record.
        Pawn downed = colonists[0];
        EnsureInventoryFixtureSpace(downed);
        var downedGu = PackTestGu(downed);
        var downedRecord = registry.Resolve(downedGu);
        HealthUtility.DamageUntilDowned(downed, allowBleedingWounds: false);
        Check(downed.Downed, "The first colonist was downed");
        string before = FullState(downedRecord);
        downedGu.GetInspectString();
        Check(FullState(downedRecord) == before, "Inspecting the dropped Gu did not mutate its record");
        ExpectBound("downed-holder", downedRecord, downedGu, GuCustodyKind.DormantGround);

        // 2. A killed holder.
        Pawn killed = colonists[1];
        EnsureInventoryFixtureSpace(killed);
        var killedGu = PackTestGu(killed);
        var killedRecord = registry.Resolve(killedGu);
        killed.Kill(null);
        Check(killed.Dead, "The second colonist was killed");
        ExpectBound("killed-holder", killedRecord, killedGu, GuCustodyKind.DormantGround);

        // 3. The holder becomes a prisoner.
        Pawn captive = colonists[2];
        EnsureInventoryFixtureSpace(captive);
        var captiveGu = PackTestGu(captive);
        var captiveRecord = registry.Resolve(captiveGu);
        var other = Find.FactionManager.AllFactions.First(faction => !faction.IsPlayer && !faction.Hidden && faction.def.humanlikeFaction);
        captive.SetFaction(other);
        captive.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
        Check(captive.IsPrisonerOfColony, "The third colonist became a prisoner of the colony");
        ExpectBound("prisoner-holder", captiveRecord, captiveGu, GuCustodyKind.DormantGround);

        // 4. Aperture host death, then resurrection of the same Pawn and Hediff.
        Pawn host = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(host);
        var aperture = AddProbeAperture(host);
        var stored = StoreFixtureGu(host, aperture);
        host.Kill(null);
        registry.ApplyPendingEvents();
        // Phase 2: the slain cultivator's aperture Gu stay contained and recoverable from the body.
        Check(stored.IsActionable && stored.Custody.Kind == GuCustodyKind.DeceasedAperture && registry.ResolveApertureOccupant(aperture) == stored,
            $"aperture-host-death: deceased-host custody, not unresolved. Observed {stored.Custody.Kind} {stored.Unresolved}");
        Report($"OBS aperture-host-death: deceased-host custody; {State(stored)}");
        Check(ResurrectionUtility.TryResurrect(host) && host.health.hediffSet.hediffs.Contains(aperture), "Resurrection kept the same aperture Hediff");
        Check(stored.IsActionable && registry.ResolveApertureOccupant(aperture) == stored && stored.Custody.Kind == GuCustodyKind.Aperture,
            "Resurrection found the same contained Gu with no recovery step");
        expectations.Add(new("aperture-resurrection", stored.Id, "bound", null, null));
        Report($"OBS aperture-resurrection: recovered; {State(stored)}");

        // 5. A dormant Gu destroyed on the ground.
        var groundGu = SpawnGroundGu(host);
        var groundRecord = registry.Resolve(groundGu);
        int destroyBefore = CompV2IdentityProbe.PostDestroyCalls;
        groundGu.Destroy(DestroyMode.KillFinalize);
        Check(CompV2IdentityProbe.PostDestroyCalls - destroyBefore == 1, "Destruction reached the projection comps once");
        ExpectTerminal("dormant-destroyed", groundRecord, GuTerminalOutcome.Destroyed);

        // 6. A wild Gu killed.
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        Check(registry.TrySpawnDevelopmentGu(species, home, EmptyCargoCell(home, colonists[0].Position, null, 4), out var wild),
            "Spawned a wild Gu to kill");
        var wildRecord = registry.Resolve(wild);
        wild.Kill(null);
        ExpectTerminal("wild-killed", wildRecord, GuTerminalOutcome.Died);

        // 7. A wild Gu Pawn passed to the world and collected by native world-pawn GC.
        Check(registry.TrySpawnDevelopmentGu(species, home, EmptyCargoCell(home, colonists[0].Position, null, 5), out var drifter),
            "Spawned a wild Gu to pass to the world");
        var drifterRecord = registry.Resolve(drifter);
        drifter.DeSpawn();
        Find.WorldPawns.PassToWorld(drifter);
        Check(registry.Resolve(drifter) == drifterRecord && drifterRecord.Custody.Kind == GuCustodyKind.WildPawn,
            "A living world Pawn is still the authorized manifestation before GC");
        Find.WorldPawns.gc.RunGC();
        // Phase 2: GC retains Pawns with Gu custody until reconciliation abstracts the Gu by its preservation policy.
        Check(!drifter.Discarded && registry.Resolve(drifter) == drifterRecord, "Native world-pawn GC retained the authorized Gu Pawn");
        ExpectWorldSite("world-gc", drifterRecord, null);
        Check(drifter.Discarded && drifterRecord.LastKnown.MapId == home.GetUniqueLoadID(),
            "The registry retired the former world Pawn and kept the map it last left as evidence");

        // 8. Removal of an unprotected map that holds a dormant Gu and an active Gu.
        var generator = DefDatabase<MapGeneratorDef>.GetNamed("GU_PocketProbeCavern");
        Map pocket = PocketMapUtility.GeneratePocketMap(new IntVec3(15, 1, 15), generator, null, home);
        pocket.fogGrid.ClearAllFog();
        IntVec3 PocketCell(int min) => GenRadial.RadialCellsAround(pocket.Center, 6f, false)
            .First(cell => cell.DistanceToSquared(pocket.Center) >= min * min && cell.Standable(pocket) && cell.GetThingList(pocket).Count == 0);
        Check(registry.TrySpawnDevelopmentGu(species, pocket, PocketCell(2), out var pocketWild), "Spawned a wild Gu in the pocket");
        foreach (Hediff condition in pocketWild.health.hediffSet.hediffs.ToArray()) pocketWild.health.RemoveHediff(condition);
        Check(registry.TryMakeDormant(pocketWild, pocketWild.GuRevision, pocketWild.Position, out var pocketDormant, out string reason),
            $"Made the pocket Gu dormant. {reason}");
        Check(registry.TrySpawnDevelopmentGu(species, pocket, PocketCell(3), out var pocketActive), "Spawned an active Gu in the pocket");
        var pocketDormantRecord = registry.Resolve(pocketDormant);
        var pocketActiveRecord = registry.Resolve(pocketActive);
        PocketMapUtility.DestroyPocketMap(pocket);
        Check(!Find.Maps.Contains(pocket), "The unprotected pocket map was removed");
        ExpectWorldSite("map-removal-dormant", pocketDormantRecord, pocket);
        ExpectWorldSite("map-removal-active-world-pawn", pocketActiveRecord, pocket);
        Check(pocketActive.Discarded, "The wild Gu Pawn passed to the world was retired after abstraction");

        // 9. Native Strip redistributes a living colonist's inventory.
        var stripTarget = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(stripTarget);
        var strippedGu = PackTestGu(stripTarget);
        var strippedRecord = registry.Resolve(strippedGu);
        stripTarget.Strip();
        ExpectBound("strip", strippedRecord, strippedGu, GuCustodyKind.DormantGround);

        // 10. The holder changes faction while keeping the Gu in inventory.
        var defector = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(defector);
        var defectorGu = PackTestGu(defector);
        var defectorRecord = registry.Resolve(defectorGu);
        defector.SetFaction(other);
        ExpectBound("faction-change", defectorRecord, defectorGu, GuCustodyKind.DormantInventory);
        Check(defectorRecord.Custody.HolderPawnId == defector.GetUniqueLoadID(), "The observed holder is the defector");

        Check(registry.Individuals.All(record => !record.IsQuarantined), "No ordinary-gameplay scenario quarantined a record");
        Report($"OBS summary: records={registry.Individuals.Count} quarantined=0 unresolved={registry.Individuals.Count(record => record.IsUnresolved)}");
        File.WriteAllText(V2ProbeFixturePath, JsonSerializer.Serialize(expectations));
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifyV2EngineProbeReload()
    {
        var registry = IndividualRegistry;
        var expectations = JsonSerializer.Deserialize<List<ProbeExpectation>>(File.ReadAllText(V2ProbeFixturePath));
        var things = Find.Maps.SelectMany(map => ThingOwnerUtility.GetAllThingsRecursively(map, false))
            .Concat(PawnsFinder.All_AliveOrDead.Where(pawn => pawn.inventory != null)
                .SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings()))
            .OfType<ThingWithComps>().Where(thing => thing is IGuProjection).Distinct().ToArray();
        var pawns = PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().ToArray();
        var projections = things.Concat(pawns).Distinct().ToArray();
        int matched = projections.Count(thing => thing.GetComp<CompV2IdentityProbe>() is { } comp
            && comp.LoadedId == ((IGuProjection)thing).GuId && comp.LoadedRevision == ((IGuProjection)thing).GuRevision);
        Report($"OBS comp-migration: projections={projections.Length}; probe comps read the manifestation keys on {matched}");
        Check(projections.Length > 0 && matched == projections.Length, "A second comp can read the manifestation keys on every loaded projection");

        foreach (var expected in expectations)
        {
            var record = registry.FindRecord(expected.Id);
            Check(record != null && !record.IsQuarantined, $"{expected.Case}: the record survived reload without quarantine");
            if (expected.Outcome == "bound")
                Check(registry.Resolve(record.Id) == record, $"{expected.Case}: rebound after a fresh-process reload. {State(record)}");
            else if (expected.Outcome == "terminal")
                Check(record.Existence == GuExistence.Terminal && record.TerminalOutcome.ToString() == expected.Cause && registry.Resolve(record.Id) == null,
                    $"{expected.Case}: the terminal outcome persisted. {record.Existence}/{record.TerminalOutcome}");
            else if (expected.Outcome == "worldsite")
                Check(registry.Resolve(record.Id) == record && record.Custody.Kind == GuCustodyKind.WorldSite
                    && record.LastKnown?.Tile.ToString() == expected.Evidence, $"{expected.Case}: the world location persisted. {record.LastKnown?.Describe()}");
            else
                Check(record.Unresolved.ToString() == expected.Cause && record.TerminalEvidence.ToString() == expected.Evidence
                    && registry.Resolve(record.Id) == null, $"{expected.Case}: unresolved cause and evidence persisted. {record.Unresolved}/{record.TerminalEvidence}");
            Report($"OBS reload {expected.Case}: {expected.Outcome} {record.Existence}/{record.TerminalOutcome} {record.Custody.Kind} {record.Unresolved}");
        }
        Check(registry.Individuals.All(record => !record.IsQuarantined), "No record is quarantined after reload");
    }
}
