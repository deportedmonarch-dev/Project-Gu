using System.Text.Json;
using System.Xml.Linq;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>Test species effect: leaves three steel where a Gu ended. Proves the hook runs once, after commit.</summary>
public sealed class Phase2TestResidueWorker : GuTerminalEffectWorker
{
    internal static int Calls;

    public override void Apply(GuRecord record, GuTerminalOutcome outcome, GuLocation location, Map map)
    {
        Calls++;
        if (map == null || location?.Cell.IsValid != true) return;
        var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
        steel.stackCount = 3;
        GenPlace.TryPlaceThing(steel, location.Cell, map, ThingPlaceMode.Near);
    }
}

/// <summary>
/// Phase 2 lifecycle acceptance (owner decisions §1–§5, §8): explicit terminal operations, deceased-host custody,
/// holder teardown, preservation policies, world-location recovery, operator recovery and compaction.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Phase2Lifecycle => GenCommandLine.CommandLineArgPassed("guphase2lifecycle");
    private static string Phase2FixturePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-phase2-lifecycle.json");
    private record Phase2Fixture(Dictionary<string, string> Ids, int RogueRevision, int RecordCount, int BulkCount,
        long TerminalBytesBefore, long TerminalBytesAfter, string ActorId);

    private static GuSpeciesDef Species(string defName) => DefDatabase<GuSpeciesDef>.GetNamed(defName);

    private static Thing_GuDormantProjection SpawnDormant(GuSpeciesDef species, Map map, IntVec3 cell)
    {
        var registry = IndividualRegistry;
        Check(registry.TrySpawnDevelopmentGu(species, map, cell, out var wild), $"Spawned a {species.defName} individual");
        foreach (Hediff condition in wild.health.hediffSet.hediffs.ToArray()) wild.health.RemoveHediff(condition);
        Check(registry.TryMakeDormant(wild, wild.GuRevision, cell, out var thing, out string reason), $"Made the {species.defName} Gu dormant. {reason}");
        return thing;
    }

    private static long TerminalRecordBytes()
    {
        var saved = XDocument.Load(GenFilePaths.FilePathForSavedGame(SaveName));
        return saved.Descendants("guIndividualRecordsV2").Single().Elements("li")
            .Where(node => (string)node.Element("existence") is "Terminal" or "Retired")
            .Sum(node => (long)node.ToString(SaveOptions.DisableFormatting).Length);
    }

    private static void ExercisePhase2Lifecycle()
    {
        var registry = IndividualRegistry;
        Map home = Find.CurrentMap;
        var dev = Species("GU_DevTestSpringGu");
        Pawn actor = home.mapPawns.FreeColonistsSpawned.First();
        EnsureInventoryFixtureSpace(actor);
        var ids = new Dictionary<string, string>();
        string reason;

        // A. Deliberate destruction by the living host, with idempotent replay of the event key.
        Pawn host = ExtraColonist(home, actor.Position);
        EnsureInventoryFixtureSpace(host);
        var aperture = AddProbeAperture(host);
        var contained = StoreFixtureGu(host, aperture);
        Check(!registry.TryEndLife(contained.Id, contained.Revision, GuTerminalOutcome.DeliberatelyDestroyed, "p2-destroy-contained", actor,
            "A stranger tried to destroy it.", out _) && contained.IsActionable, "Only the living host can destroy a Gu in its aperture");
        Check(registry.TryEndLife(contained.Id, contained.Revision, GuTerminalOutcome.DeliberatelyDestroyed, "p2-destroy-contained", host,
                "Denied to enemies.", out reason) && contained.Existence == GuExistence.Terminal
            && contained.TerminalOutcome == GuTerminalOutcome.DeliberatelyDestroyed && contained.EndActorId == host.GetUniqueLoadID()
            && contained.EndEventKey == "p2-destroy-contained" && !registry.ApertureOccupants(aperture).Any(),
            $"The host deliberately destroyed its contained Gu. {reason}");
        int ended = registry.Individuals.Count(record => !record.IsLiving);
        Check(registry.TryEndLife(contained.Id, contained.Revision, GuTerminalOutcome.DeliberatelyDestroyed, "p2-destroy-contained", host,
                "Replay.", out reason) && registry.Individuals.Count(record => !record.IsLiving) == ended
            && contained.History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1,
            $"Replaying the event key returns the original result and ends nothing else. {reason}");
        var heldThing = PackTestGu(actor);
        var held = registry.Resolve(heldThing);
        Check(!registry.TryEndLife(held.Id, held.Revision, GuTerminalOutcome.DeliberatelyDestroyed, "p2-destroy-contained", actor, "Reuse.", out _)
            && held.IsActionable, "An event key that ended one Gu cannot end another");
        ids["destroyedContained"] = contained.Id;

        Check(registry.TryEndLife(held.Id, held.Revision, GuTerminalOutcome.DeliberatelyDestroyed, "p2-destroy-held", actor,
                "Destroyed in hand.", out reason) && heldThing.Destroyed && !actor.inventory.GetDirectlyHeldThings().Contains(heldThing),
            $"The holder destroyed the Gu in its inventory and the Thing was retired. {reason}");
        registry.ApplyPendingEvents();
        Check(held.TerminalOutcome == GuTerminalOutcome.DeliberatelyDestroyed && held.History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1,
            "Retiring the held Thing was not reported as a second, physical ending");
        ids["destroyedHeld"] = held.Id;

        var consumedThing = SpawnDormant(Species("GU_TestRetireGu"), home, OpenInventoryCell(actor));
        var consumed = registry.Resolve(consumedThing);
        int steelBefore = home.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(thing => thing.stackCount);
        Check(registry.TryEndLife(consumed.Id, consumed.Revision, GuTerminalOutcome.Consumed, "p2-consume", null, "Consumed by a process.", out reason)
            && consumed.TerminalOutcome == GuTerminalOutcome.Consumed && consumedThing.Destroyed, $"A process consumed the Gu. {reason}");
        Check(Phase2TestResidueWorker.Calls == 1 && home.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(thing => thing.stackCount) == steelBefore + 3,
            "The species terminal effect left its declared residue exactly once");
        ids["consumed"] = consumed.Id;

        // B. Deceased-host custody. Extraction before resurrection is not reversed or duplicated by resurrection.
        Pawn slain = ExtraColonist(home, actor.Position);
        EnsureInventoryFixtureSpace(slain);
        var slainAperture = AddProbeAperture(slain);
        var kept = StoreFixtureGu(slain, slainAperture);
        slain.Kill(null);
        registry.ApplyPendingEvents();
        Check(kept.IsActionable && kept.Custody.Kind == GuCustodyKind.DeceasedAperture, "The slain cultivator's Gu stays recoverable from the body");
        var corpse = slain.Corpse;
        IntVec3 beside = GenAdj.CardinalDirections.Select(offset => corpse.Position + offset)
            .First(cell => cell.Standable(home) && cell.GetFirstPawn(home) == null);
        TransferToFixture(actor, home, beside);
        int keptRevision = kept.Revision;
        Check(registry.TryExtractFromDeceased(kept.Id, keptRevision, actor, out var extracted, out reason)
            && extracted.holdingOwner == actor.inventory.GetDirectlyHeldThings() && registry.Resolve(extracted) == kept
            && kept.Revision == keptRevision + 1 && kept.History.Any(entry => entry.Kind == GuLifecycleEventKind.Extracted),
            $"A colonist extracted the Gu from the corpse into native inventory. {reason}");
        Check(ResurrectionUtility.TryResurrect(slain) && slain.health.hediffSet.hediffs.Contains(slainAperture), "Resurrection kept the same aperture");
        registry.Sweep(int.MaxValue);
        Check(!registry.ApertureOccupants(slainAperture).Any() && registry.Resolve(extracted) == kept
            && ActuallyProjectedGu().Count(thing => thing.GuTag()?.GuId == kept.Id) == 1,
            "Resurrection did not restore the extracted Gu to the aperture or duplicate it");
        ids["extracted"] = kept.Id;

        // C and D. A destroyed corpse: the aperture Gu waits for an approved host-loss policy, and a physical Gu survives.
        Pawn burned = ExtraColonist(home, actor.Position);
        EnsureInventoryFixtureSpace(burned);
        var orphan = StoreFixtureGu(burned, AddProbeAperture(burned));
        burned.Kill(null);
        var burnedCorpse = burned.Corpse;
        IntVec3 corpseCell = burnedCorpse.Position;
        var carried = SpawnDormant(dev, home, EmptyCargoCell(home, corpseCell, null, 1));
        var carriedRecord = registry.Resolve(carried);
        int carriedRevision = carriedRecord.Revision;
        carried.DeSpawn();
        Check(burned.inventory.GetDirectlyHeldThings().TryAdd(carried, false), "Placed a physical Gu in the dead holder's inventory natively");
        burnedCorpse.Destroy();
        registry.ApplyPendingEvents();
        Check(orphan.IsLiving && !orphan.IsQuarantined && orphan.Unresolved == GuUnresolvedCause.HostCorpseDestroyed
            && orphan.TerminalEvidence == GuTerminalEvidence.HostCorpseDestroyed && orphan.LastKnown?.Cell == corpseCell
            && orphan.LastKnown.MapId == home.GetUniqueLoadID(),
            $"Corpse destruction left the aperture Gu unresolved with the corpse's location, never dead. {orphan.Unresolved} {orphan.LastKnown?.Describe()}");
        Check(carried.Spawned && carried.Map == home && registry.Resolve(carried) == carriedRecord && carriedRecord.Revision == carriedRevision,
            "The physical Gu survived its holder's corpse on the same map with unchanged revisions");
        ids["corpseAperture"] = orphan.Id;
        ids["corpseInventory"] = carriedRecord.Id;

        // E. A holder destroyed without dying keeps no Gu: they stay where it stood.
        Pawn vanishing = ExtraColonist(home, actor.Position);
        EnsureInventoryFixtureSpace(vanishing);
        var dropped = PackTestGu(vanishing);
        var droppedRecord = registry.Resolve(dropped);
        vanishing.Destroy(DestroyMode.Vanish);
        registry.ApplyPendingEvents();
        Check(dropped.Spawned && dropped.Map == home && registry.Resolve(dropped) == droppedRecord && droppedRecord.IsActionable,
            "A vanished holder's physical Gu stayed on the map, authorized");
        ids["vanishedHolder"] = droppedRecord.Id;

        // F. Preservation policies when an unprotected map is removed.
        Map pocket = PocketMapUtility.GeneratePocketMap(new IntVec3(15, 1, 15), DefDatabase<MapGeneratorDef>.GetNamed("GU_PocketProbeCavern"), null, home);
        pocket.fogGrid.ClearAllFog();
        IntVec3 PocketCell() => GenRadial.RadialCellsAround(pocket.Center, 6f, false)
            .First(cell => cell.Standable(pocket) && cell.GetThingList(pocket).Count == 0);
        var retire = registry.Resolve(SpawnDormant(Species("GU_TestRetireGu"), pocket, PocketCell()));
        var population = registry.Resolve(SpawnDormant(Species("GU_TestPopulationGu"), pocket, PocketCell()));
        var immortal = registry.Resolve(SpawnDormant(Species("GU_TestImmortalGu"), pocket, PocketCell()));
        var site = registry.Resolve(SpawnDormant(dev, pocket, PocketCell()));
        Check(registry.TrySpawnDevelopmentGu(Species("GU_TestPopulationGu"), pocket, PocketCell(), out var populationWild), "Spawned an active population Gu");
        var populationActive = registry.Resolve(populationWild);
        int residueCalls = Phase2TestResidueWorker.Calls;
        PocketMapUtility.DestroyPocketMap(pocket);
        registry.ApplyPendingEvents();
        registry.Sweep(int.MaxValue);
        Check(retire.Existence == GuExistence.Retired && retire.TerminalOutcome == GuTerminalOutcome.None
            && retire.History.Last().Kind == GuLifecycleEventKind.Retired && Phase2TestResidueWorker.Calls == residueCalls,
            "The retiring species left individual simulation under its declared policy. It did not die and left no residue");
        Check(population.IsLiving && population.Unresolved == GuUnresolvedCause.MapRemoved && population.TerminalEvidence == GuTerminalEvidence.MapRemoved
            && population.LastKnown?.HasTile == true, "With no population to receive it, the population species stayed unresolved with its location");
        Check(immortal.IsActionable && immortal.ContainerKind == GuContainerKind.WorldSite && immortal.LastKnown.IsAt(home),
            "Rank six overrode the retiring policy: the Gu kept its identity at the world location");
        Check(site.IsActionable && site.ContainerKind == GuContainerKind.WorldSite, "The developer species kept its identity at the world location");
        Find.WorldPawns.gc.RunGC();
        Check(registry.Resolve(populationWild) == populationActive && !populationWild.Discarded && populationActive.Custody.Kind == GuCustodyKind.WildPawn,
            "The active population Gu stayed a retained world Pawn while no population can receive it");
        ids["retired"] = retire.Id;
        ids["population"] = population.Id;
        ids["worldSite"] = site.Id;
        ids["populationWild"] = populationActive.Id;

        int immortalRevision = immortal.Revision;
        IntVec3 recoverCell = EmptyCargoCell(home, actor.Position, null, 2);
        Check(registry.WorldSiteRecordsAt(home).Contains(immortal)
            && registry.TryRecoverFromWorldSite(immortal.Id, immortalRevision, home, recoverCell, out var recovered, out reason)
            && recovered is Thing_GuDormantProjection && recovered.Position == recoverCell && registry.Resolve(recovered) == immortal
            && immortal.Revision == immortalRevision + 1 && immortal.Rank == 6, $"The world-location Gu was recovered on a map at the same tile. {reason}");
        ids["recovered"] = immortal.Id;

        // G. Operator tools. Inspection is read-only. Repairs re-check their evidence.
        string before = FullState(population);
        string report = registry.DescribeForOperator(population);
        Check(FullState(population) == before && report.Contains("declare lost") && !report.Contains("adopt"),
            "Operator inspection is read-only and offers only supported actions");
        Check(!registry.TryDeclareLost(droppedRecord.Id, null, out _), "An actionable Gu cannot be declared lost");
        Check(!registry.TryLiftQuarantine(droppedRecord.Id, null, out _), "A record without a quarantine cannot have one lifted");
        Check(registry.TryDeclareLost(population.Id, "Test operator decision.", out reason) && population.TerminalOutcome == GuTerminalOutcome.Lost
            && population.EndActorId == GuIndividualRegistry.OperatorActor, $"The operator declared the unresolved Gu lost. {reason}");

        // The authorized Thing leaves every holder and a copied tag stays on the ground. Only the copy reaches the save.
        var original = SpawnGroundGu(actor);
        var rogueRecord = registry.Resolve(original);
        var copy = (Thing_GuDormantProjection)ThingMaker.MakeThing(original.def);
        copy.InitializeBinding(rogueRecord);
        original.DeSpawn();
        Check(GenPlace.TryPlaceThing(copy, EmptyCargoCell(home, actor.Position, null, 3), home, ThingPlaceMode.Direct),
            "Placed a copied tag for the reload adoption check");
        ids["rogue"] = rogueRecord.Id;

        // H. Compaction of ended records after the retention period, with save-size measurement.
        const int bulk = 40;
        for (int index = 0; index < bulk; index++)
        {
            var thing = SpawnDormant(dev, home, EmptyCargoCell(home, actor.Position, null, 2));
            var record = registry.Resolve(thing);
            Check(registry.TryEndLife(record.Id, record.Revision, GuTerminalOutcome.Consumed, $"p2-bulk-{index}", null, "Bulk consumption.", out _),
                "Consumed a bulk fixture Gu");
            if (index == 0) ids["bulk0"] = record.Id;
        }
        registry.ApplyPendingEvents();
        GameDataSaveLoader.SaveGame(SaveName);
        long bytesBefore = TerminalRecordBytes();
        int endedCount = registry.Individuals.Count(record => !record.IsLiving);
        Check(registry.CompactRecords(Find.TickManager.TicksGame + GuIndividualRegistry.RetentionTicks - 1, int.MaxValue) == 0,
            "Nothing is compacted before the retention period ends");
        int compacted = registry.CompactRecords(Find.TickManager.TicksGame + GuIndividualRegistry.RetentionTicks, int.MaxValue);
        var bulk0 = registry.FindRecord(ids["bulk0"]);
        Check(compacted == endedCount && bulk0.IsCompacted && bulk0.EndEventKey == "p2-bulk-0" && bulk0.Origin == "development"
            && bulk0.SpeciesDefName == dev.defName && bulk0.TerminalOutcome == GuTerminalOutcome.Consumed && bulk0.History.Count == 0
            && bulk0.EndedTick >= 0 && bulk0.LastKnown == null, $"Compaction kept identity, provenance, outcome and keys. Compacted {compacted} of {endedCount}");
        Check(registry.TryEndLife(bulk0.Id, bulk0.CustodyRevision, GuTerminalOutcome.Consumed, "p2-bulk-0", null, "Replay.", out reason)
            && registry.Individuals.Count(record => !record.IsLiving) == endedCount, $"Replay protection survived compaction. {reason}");
        GameDataSaveLoader.SaveGame(SaveName);
        long bytesAfter = TerminalRecordBytes();
        Check(bytesAfter < bytesBefore, $"Compaction reduced the saved size of ended records: {bytesBefore} to {bytesAfter} bytes");
        Report($"MEASURE compaction: {endedCount} ended records, {bytesBefore} B before, {bytesAfter} B after "
            + $"({bytesBefore / endedCount} to {bytesAfter / endedCount} B per record)");

        Check(registry.Individuals.All(record => !record.IsQuarantined), "No Phase 2 scenario quarantined a record");
        File.WriteAllText(Phase2FixturePath, JsonSerializer.Serialize(new Phase2Fixture(ids, rogueRecord.CustodyRevision,
            registry.Individuals.Count, bulk, bytesBefore, bytesAfter, actor.GetUniqueLoadID())));
        Report("Deliberate destruction, consumption with residue, deceased-host extraction before resurrection, corpse and holder teardown, "
            + "three preservation policies, world-location recovery, operator tools and compaction passed.");
    }

    private static void VerifyPhase2LifecycleReload()
    {
        var registry = IndividualRegistry;
        var fixture = JsonSerializer.Deserialize<Phase2Fixture>(File.ReadAllText(Phase2FixturePath));
        GuIndividual Record(string key) => registry.FindRecord(fixture.Ids[key]);
        Check(registry.Individuals.Count == fixture.RecordCount && registry.Individuals.All(record => !record.IsQuarantined),
            "Every record survived reload without quarantine");

        foreach (var (key, outcome) in new[] { ("destroyedContained", GuTerminalOutcome.DeliberatelyDestroyed),
                     ("destroyedHeld", GuTerminalOutcome.DeliberatelyDestroyed), ("consumed", GuTerminalOutcome.Consumed),
                     ("population", GuTerminalOutcome.Lost), ("bulk0", GuTerminalOutcome.Consumed) })
            Check(Record(key) is { Existence: GuExistence.Terminal } ended && ended.TerminalOutcome == outcome && ended.IsCompacted,
                $"{key}: the compacted {outcome} tombstone persisted");
        Check(Record("retired") is { Existence: GuExistence.Retired, IsCompacted: true } retiredRecord && retiredRecord.History.Single().Cause == "MapRemoved", "The retired record persisted as a compacted tombstone that keeps its retirement cause");

        var extracted = Record("extracted");
        Check(registry.Resolve(extracted.Id) == extracted && extracted.Custody.Kind == GuCustodyKind.DormantInventory
            && extracted.Custody.HolderPawnId == fixture.ActorId, "The extracted Gu reloaded in the recipient's inventory");
        var orphan = Record("corpseAperture");
        Check(orphan.Unresolved == GuUnresolvedCause.HostCorpseDestroyed && orphan.LastKnown?.HasTile == true && registry.Resolve(orphan.Id) == null,
            "The corpse-destroyed aperture Gu stayed unresolved with its location");
        foreach (var key in new[] { "corpseInventory", "vanishedHolder", "recovered" })
            Check(registry.Resolve(Record(key).Id) == Record(key) && Record(key).Custody.Kind == GuCustodyKind.DormantGround,
                $"{key}: rebound on the ground");
        Check(registry.Resolve(Record("worldSite").Id) == Record("worldSite") && Record("worldSite").Custody.Kind == GuCustodyKind.WorldSite,
            "The world-location Gu reloaded at its abstract location");
        Check(registry.Resolve(Record("populationWild").Id) == Record("populationWild") && Record("populationWild").Custody.Kind == GuCustodyKind.WildPawn,
            "The retained world Pawn reloaded as the manifestation");

        var rogue = Record("rogue");
        var candidates = registry.RogueCandidates(rogue).ToArray();
        Check(rogue.Unresolved == GuUnresolvedCause.ProjectionMissing && candidates.Length == 1 && registry.Resolve(candidates[0].parent) == null,
            "The copied tag loaded as a rogue candidate and does not authorize");
        string before = FullState(rogue);
        Check(registry.DescribeForOperator(rogue).Contains("adopt the sole rogue candidate") && FullState(rogue) == before,
            "Read-only inspection offered adoption of the sole candidate");
        Check(registry.TryAdoptRogue(candidates[0], "Reload acceptance.", out string reason) && rogue.IsActionable
            && rogue.CustodyRevision == fixture.RogueRevision + 1 && candidates[0].GuRevision == rogue.CustodyRevision
            && registry.Resolve(candidates[0].parent) == rogue && rogue.History.Last().Cause == "AdoptedRogue",
            $"The operator adopted the candidate with a new custody revision and an audit entry. {reason}");
        Report($"Fresh-process reload kept every outcome. Compaction measured {fixture.TerminalBytesBefore} to {fixture.TerminalBytesAfter} bytes. The operator adopted the rogue candidate.");
    }
}
