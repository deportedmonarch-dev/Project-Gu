using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Characterization probe for Gu Framework V2 planning. It records how RimWorld 1.7 and the V1 registry respond to
/// ordinary native events. Checks assert only that each scenario was set up. Outcomes are reported as observations.
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

    private static void ExerciseV2EngineProbe()
    {
        var registry = IndividualRegistry;
        Map home = Find.CurrentMap;
        var colonists = home.mapPawns.FreeColonistsSpawned.ToArray();
        Check(colonists.Length >= 3, "The quick-test colony supplies three colonists");
        Report($"Environment: RimWorld {VersionControl.CurrentVersionStringWithRev}, mods: {string.Join(", ", ModsConfig.ActiveModsInLoadOrder.Select(mod => mod.PackageId))}");

        // 1. A downed holder. Native MakeDowned drops inventory unless the Pawn is in bed.
        Pawn downed = colonists[0];
        EnsureInventoryFixtureSpace(downed);
        var downedGu = PackTestGu(downed);
        var downedRecord = registry.Individuals.Single(record => record.Id == downedGu.GuId);
        HealthUtility.DamageUntilDowned(downed, allowBleedingWounds: false);
        Check(downed.Downed, "The first colonist was downed");
        Report($"OBS downed-holder: dead={downed.Dead}; Gu holder after native downing: {Holder(downedGu)}; raw record before any read: {State(downedRecord)}");
        string inspect = downedGu.GetInspectString();
        Report($"OBS downed-holder: after one inspect-string read: {State(downedRecord)}; inspect text mentions quarantine={inspect.Contains("quarantined")}");

        // 2. A killed holder.
        Pawn killed = colonists[1];
        EnsureInventoryFixtureSpace(killed);
        var killedGu = PackTestGu(killed);
        var killedRecord = registry.Individuals.Single(record => record.Id == killedGu.GuId);
        killed.Kill(null);
        Check(killed.Dead, "The second colonist was killed");
        Report($"OBS killed-holder: corpse spawned={killed.Corpse?.Spawned}; Gu holder: {Holder(killedGu)}; raw record: {State(killedRecord)}");
        registry.Resolve(killedGu);
        Report($"OBS killed-holder: after registry resolve: {State(killedRecord)}");

        // 3. The holder becomes a prisoner without the Gu moving.
        Pawn captive = colonists[2];
        EnsureInventoryFixtureSpace(captive);
        var captiveGu = PackTestGu(captive);
        var captiveRecord = registry.Individuals.Single(record => record.Id == captiveGu.GuId);
        var other = Find.FactionManager.AllFactions.First(faction => !faction.IsPlayer && !faction.Hidden && faction.def.humanlikeFaction);
        captive.SetFaction(other);
        captive.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
        Check(captive.IsPrisonerOfColony, "The third colonist became a prisoner of the colony");
        Report($"OBS prisoner-holder: Gu holder: {Holder(captiveGu)}; raw record: {State(captiveRecord)}");
        registry.Resolve(captiveGu);
        Report($"OBS prisoner-holder: after registry resolve: {State(captiveRecord)}");

        // 4. Aperture host death and resurrection.
        Pawn host = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(host);
        var aperture = AddProbeAperture(host);
        string apertureId = aperture.GetUniqueLoadID();
        var stored = StoreFixtureGu(host, aperture);
        host.Kill(null);
        Report($"OBS aperture-host-death: host dead={host.Dead}; aperture still on dead host={host.health.hediffSet.hediffs.Contains(aperture)}; record: {State(stored)}");
        bool resurrected = ResurrectionUtility.TryResurrect(host);
        Report($"OBS aperture-resurrection: resurrected={resurrected}; same aperture instance={host.health.hediffSet.hediffs.Contains(aperture)} id={aperture.GetUniqueLoadID() == apertureId}; record: {State(stored)}; occupant resolves={registry.ResolveApertureOccupant(aperture) != null}");

        // 5. A dormant Gu destroyed on the ground.
        var groundGu = SpawnGroundGu(host.Dead ? colonists[0] : host);
        var groundRecord = registry.Individuals.Single(record => record.Id == groundGu.GuId);
        int destroyBefore = CompV2IdentityProbe.PostDestroyCalls;
        groundGu.Destroy(DestroyMode.KillFinalize);
        Report($"OBS dormant-destroyed: comp PostDestroy calls={CompV2IdentityProbe.PostDestroyCalls - destroyBefore}; raw record: {State(groundRecord)}");
        registry.Resolve(groundGu);
        Report($"OBS dormant-destroyed: after registry resolve: {State(groundRecord)}");

        // 6. A wild Gu killed.
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu");
        Check(registry.TrySpawnDevelopmentGu(species, home, EmptyCargoCell(home, colonists[0].Position, null, 4), out var wild),
            "Spawned a wild Gu to kill");
        var wildRecord = registry.Individuals.Single(record => record.Id == wild.GuId);
        wild.Kill(null);
        Report($"OBS wild-killed: dead={wild.Dead}; corpse spawned={wild.Corpse?.Spawned}; raw record: {State(wildRecord)}");
        registry.Resolve(wild);
        Report($"OBS wild-killed: after registry resolve: {State(wildRecord)}");

        // 7. A wild Gu Pawn passed to the world and collected by native world-pawn GC.
        Check(registry.TrySpawnDevelopmentGu(species, home, EmptyCargoCell(home, colonists[0].Position, null, 5), out var drifter),
            "Spawned a wild Gu to pass to the world");
        var drifterRecord = registry.Individuals.Single(record => record.Id == drifter.GuId);
        drifter.DeSpawn();
        Find.WorldPawns.PassToWorld(drifter);
        Find.WorldPawns.gc.RunGC();
        Report($"OBS world-gc: discarded={drifter.Discarded}; still a world pawn={Find.WorldPawns.Contains(drifter)}; raw record: {State(drifterRecord)}");
        registry.Resolve(drifter);
        Report($"OBS world-gc: after registry resolve: {State(drifterRecord)}");

        // 8. Removal of an unprotected map that holds a dormant Gu and a wild Gu.
        var generator = DefDatabase<MapGeneratorDef>.GetNamed("GU_PocketProbeCavern");
        Map pocket = PocketMapUtility.GeneratePocketMap(new IntVec3(15, 1, 15), generator, null, home);
        pocket.fogGrid.ClearAllFog();
        IntVec3 PocketCell(int min) => GenRadial.RadialCellsAround(pocket.Center, 6f, false)
            .First(cell => cell.DistanceToSquared(pocket.Center) >= min * min && cell.Standable(pocket) && cell.GetThingList(pocket).Count == 0);
        Check(registry.TrySpawnDevelopmentGu(species, pocket, PocketCell(2), out var pocketWild), "Spawned a wild Gu in the pocket");
        foreach (Hediff condition in pocketWild.health.hediffSet.hediffs.ToArray()) pocketWild.health.RemoveHediff(condition);
        IntVec3 dormantCell = pocketWild.Position;
        Check(registry.TryMakeDormant(pocketWild, pocketWild.GuRevision, dormantCell, out var pocketDormant, out string reason),
            $"Made the pocket Gu dormant. {reason}");
        Check(registry.TrySpawnDevelopmentGu(species, pocket, PocketCell(3), out var pocketActive), "Spawned an active Gu in the pocket");
        var pocketDormantRecord = registry.Individuals.Single(record => record.Id == pocketDormant.GuId);
        var pocketActiveRecord = registry.Individuals.Single(record => record.Id == pocketActive.GuId);
        int destroyBeforeMap = CompV2IdentityProbe.PostDestroyCalls;
        int removedBefore = CompV2IdentityProbe.MapRemovedCalls;
        PocketMapUtility.DestroyPocketMap(pocket);
        Report($"OBS map-removal: map still loaded={Find.Maps.Contains(pocket)}; dormant destroyed={pocketDormant.Destroyed} spawned={pocketDormant.Spawned} holder={pocketDormant.ParentHolder?.GetType().Name ?? "none"}; comp PostDestroy calls={CompV2IdentityProbe.PostDestroyCalls - destroyBeforeMap}; comp Notify_MapRemoved calls={CompV2IdentityProbe.MapRemovedCalls - removedBefore}");
        Report($"OBS map-removal: active Gu world pawn={Find.WorldPawns.Contains(pocketActive)} discarded={pocketActive.Discarded} dead={pocketActive.Dead}; raw records: dormant {State(pocketDormantRecord)} | active {State(pocketActiveRecord)}");
        registry.Resolve(pocketDormant);
        registry.Resolve(pocketActive);
        Report($"OBS map-removal: after registry resolve: dormant {State(pocketDormantRecord)} | active {State(pocketActiveRecord)}");

        // 9. Native Strip redistributes a living colonist's inventory.
        var stripTarget = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(stripTarget);
        var strippedGu = PackTestGu(stripTarget);
        var strippedRecord = registry.Individuals.Single(record => record.Id == strippedGu.GuId);
        stripTarget.Strip();
        Report($"OBS strip: Gu holder after native Strip: {Holder(strippedGu)}; raw record: {State(strippedRecord)}");

        // 10. The holder changes faction while keeping the Gu in inventory.
        var defector = ExtraColonist(home, colonists[0].Position);
        EnsureInventoryFixtureSpace(defector);
        var defectorGu = PackTestGu(defector);
        var defectorRecord = registry.Individuals.Single(record => record.Id == defectorGu.GuId);
        defector.SetFaction(other);
        Report($"OBS faction-change: Gu holder: {Holder(defectorGu)}; raw record: {State(defectorRecord)}");
        registry.Resolve(defectorGu);
        Report($"OBS faction-change: after registry resolve: {State(defectorRecord)}");

        Report($"OBS summary: records={registry.Individuals.Count} quarantined={registry.Individuals.Count(record => record.IsQuarantined)}");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifyV2EngineProbeReload()
    {
        var registry = IndividualRegistry;
        var things = Find.Maps.SelectMany(map => ThingOwnerUtility.GetAllThingsRecursively(map, false))
            .Concat(PawnsFinder.All_AliveOrDead.Where(pawn => pawn.inventory != null)
                .SelectMany(pawn => pawn.inventory.GetDirectlyHeldThings()))
            .OfType<ThingWithComps>().Where(thing => thing is IGuProjection).Distinct().ToArray();
        var pawns = PawnsFinder.All_AliveOrDead.OfType<Pawn_GuWildProjection>().ToArray();
        var projections = things.Concat(pawns).Distinct().ToArray();
        int matched = projections.Count(thing => thing.GetComp<CompV2IdentityProbe>() is { } comp
            && comp.LoadedId == ((IGuProjection)thing).GuId && comp.LoadedRevision == ((IGuProjection)thing).GuRevision);
        Report($"OBS comp-migration: projections={projections.Length}; comps attached={projections.Count(thing => thing.GetComp<CompV2IdentityProbe>() != null)}; comp read same keys={matched}");
        Report($"OBS reload: records={registry.Individuals.Count} quarantined={registry.Individuals.Count(record => record.IsQuarantined)}");
        foreach (var record in registry.Individuals)
            Report($"OBS reload record {record.Id}: {State(record)}");
        Check(projections.Length > 0 && matched == projections.Length, "Every loaded V1 projection exposed its saved ID and revision to an added comp");
    }
}
