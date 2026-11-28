using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Botany;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Presentation;
using ProjectGu.Springs;
using ProjectGu.Sustenance;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

/// <summary>
/// Alpha 5B native acceptance, on the Alpha 3 save (a disciple in the spring cavern with a Liquor Worm and a Moonlight Gu in
/// their aperture). Development Mode is off throughout except for the one step that exercises the developer spawn tool.
/// Player actions go through the real right-click menu (<c>FloatMenuMakerMap.GetOptions</c>), the native grower and haul
/// work givers, gizmos and the Aperture tab's actions.
///
/// Stage "integration": moon orchid selection, right-click harvest and seed gathering with reasons, seed-gated sowing with
/// interruption, passage hauling of petals and stones with fairness, background provisioning (no job, exact consumption,
/// stockpile and inventory sources, forbidden, loose, other-map and reserved food refused, the policy toggle, two Gu
/// Masters sharing one supply), Hope Gu ambience from the cohort, capture in dense spirit grass and with an adjacent
/// fallback, the registry-safe developer spawn, will-enforcement and storing, and the Rest need during overnight
/// cultivation. It saves with a sowing in progress, cargo in a hauler's pack and hungry Gu.
/// Stage "reload": a fresh process resumes the sowing (one seed spent once), delivers the cargo without duplication, keeps
/// every nourishment value, and keeps provisioning.
/// Stage "legacy5a": a save written by the Alpha 5A build (d82c723) loads, its feeding job commits once, its orchids
/// harvest and give seeds, and background provisioning feeds its Gu.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha5B => GenCommandLine.CommandLineArgPassed("gualpha5b");
    private static string Alpha5BStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha5b-state.json");
    internal const string Alpha5BMidwaySave = "alpha5b-midway";
    private static ThingDef SeedDef => DefDatabase<ThingDef>.GetNamed("GU_MoonOrchidSeeds");
    private static ThingDef GrassDef => DefDatabase<ThingDef>.GetNamed("GU_SpiritGrass");

    private sealed record Alpha5BState(string CultivatorId, string ElderId, string LiquorId, string MoonId, string ElderGuId, int Tick,
        NourishmentValues[] Values, int Seeds, int Petals, int Stones, int SowX, int SowZ, int Records, string HaulerId, int Cargo,
        string CargoDef, int Hope, int Spirits);

    // ---------------------------------------------------------------------------------------------------------------
    private static void VerifyAlpha5BIntegration()
    {
        var (_, disciple, aperture, liquor) = LoadAlpha3State();
        DisableFixtureAutosave();
        var registry = IndividualRegistry;
        var moon = registry.ApertureOccupants(aperture).Single(record => record.Id != liquor.Id);
        Map cave = disciple.Map;
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        var springThing = cave.listerThings.AllThings.First(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = springThing.TryGetComp<CompGuSpiritSpring>();
        var component = SustenanceComponent;
        Current.Game.CurrentMap = cave;
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) component.SetAutoTend(colonist, false);
        TickUntil(() => Ledger.ChannelFor(liquor.Id) == null, 3000, "The saved Liquor Worm session completed");
        Check(!Prefs.DevMode && GuPlantPatches.SowingGateInstalled && GuPlantPatches.SelectionInstalled, "Development Mode is off and both plant hooks are installed");
        Check(!DefDatabase<ThinkTreeDef>.AllDefs.Any(def => def.defName.StartsWith("GU_", StringComparison.Ordinal)),
            "Project GU inserts no think-tree node: automatic feeding is not a job, and nothing outranks rest");
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 1);

        VerifyOrchidInteractions(disciple, cave);
        var sowCell = VerifySeedGatedSowing(disciple, cave);
        var (hauler, site) = VerifyPetalHauling(disciple, cave, home);
        GuPassageHauling.SetEnabled(site.Hauling, false); // keep the provisioning controls (loose petals) where they are
        VerifyProvisioning(disciple, aperture, liquor, moon, cave, home);
        var (hopeLiving, spirits) = VerifyHopeAmbienceAndCapture(disciple, cave, spring, springThing);
        var (elder, elderGu) = VerifyDeveloperSpawnAndSecondMaster(disciple, cave, home);
        VerifySharedSupply(disciple, moon, elder, elderGu, cave);
        VerifyRestDuringCultivation(disciple, aperture);
        SustainColonists();

        // Save: a sowing in progress, cargo packed by the hauler, and two hungry Gu.
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) component.SetAutoTend(colonist, false);
        Drain(liquor, Math.Max(0, Snap(liquor).NourishmentTicks - Day / 2));
        Drain(moon, Math.Max(0, Snap(moon).NourishmentTicks - Day / 4));
        GuPassageHauling.SetEnabled(site.Hauling, true);
        var cargo = StartPassageCargo(hauler, site, cave);
        SustainColonists();
        var sowing = StartSowing(disciple, cave, sowCell, out int seedsAtSave);
        var state = new Alpha5BState(Cultivators.FindCultivator(disciple).CultivatorId, Cultivators.FindCultivator(elder).CultivatorId,
            liquor.Id, moon.Id, elderGu.Id, Now, new[] { Values(liquor), Values(moon), Values(elderGu) }, seedsAtSave,
            AllItems(PetalDef), AllItems(StoneDef), sowCell.x, sowCell.z, registry.Records.Count, hauler.ThingID, cargo.stackCount, cargo.def.defName,
            hopeLiving, spirits);
        File.WriteAllText(Alpha5BStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(Alpha5BMidwaySave);
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved with {disciple.LabelShort} sowing at {sowCell} ({sowing}), {hauler.LabelShort} carrying {cargo.stackCount} {cargo.def.label}, "
            + $"Liquor Worm {Snap(liquor).Level} and Moonlight Gu {Snap(moon).Level}. Sowing driver: {DriverState(disciple)}");
        AssertNoErrors("alpha5b-integration");
    }

    // ---- I. Moon orchid interactions --------------------------------------------------------------------------------
    private static void VerifyOrchidInteractions(Pawn disciple, Map cave)
    {
        var orchids = cave.listerThings.ThingsOfDef(OrchidDef).OfType<Plant>().ToList();
        var grass = cave.listerThings.ThingsOfDef(GrassDef);
        Check(orchids.Count >= 3 && grass.Count > 0, $"The naturally generated cavern has wild moon orchids ({orchids.Count}) and spirit grass ({grass.Count})");
        Check(orchids.All(plant => plant.TryGetComp<CompGuPlant>() != null), "Wild orchids carry the plant framework component");

        // A. Double-click selection through the patched native selector: only orchids, every selectable orchid.
        Find.Selector.ClearSelection();
        var anyPawnBeside = cave.mapPawns.AllPawnsSpawned.FirstOrDefault(p => orchids.Any(o => o.Position.DistanceTo(p.Position) <= 1.5f));
        int selected = GuPlantPatches.SelectMatching(Find.Selector, orchids[0], cave.listerThings.AllThings.ToList());
        var picked = Find.Selector.SelectedObjects.OfType<Thing>().ToList();
        int selectable = orchids.Count(ThingSelectionUtility.SelectableByMapClick);
        Check(picked.Count == selectable && selected == selectable && picked.All(thing => thing.def == OrchidDef),
            $"Selecting matching plants from an orchid selects all {selectable} selectable orchids and no spirit grass or pawns "
            + $"(selected {string.Join(", ", picked.GroupBy(t => t.def.defName).Select(g => $"{g.Count()} {g.Key}"))}; a pawn beside an orchid: {anyPawnBeside?.LabelShort ?? "none"})");
        Find.Selector.ClearSelection();

        // B. Right-click harvesting of a wild orchid with the colonist selected (no designation, no growing zone).
        int petals0 = AllItems(PetalDef);
        Plant harvested = null;
        int yield = 0;
        foreach (var orchid in orchids.Where(plant => plant.HarvestableNow).OrderByDescending(plant => plant.Growth).Take(3).ToList())
        {
            float growth = orchid.Growth;
            int before = AllItems(PetalDef);
            var option = RightClick(disciple, orchid).FirstOrDefault(o => o.Label.StartsWith("Harvest moon orchid", StringComparison.Ordinal));
            Check(option?.action != null, $"Right-clicking a mature wild orchid offers Harvest. Offered: {string.Join(" | ", RightClick(disciple, orchid).Select(o => o.Label))}");
            option.action();
            Check(disciple.CurJobDef == JobDefOf.HarvestDesignated && cave.designationManager.DesignationOn(orchid, DesignationDefOf.HarvestPlant) != null,
                "The order designated the orchid and started the native harvest job");
            TickUntil(() => orchid.Growth < growth - 0.01f, 4000, $"The orchid at {orchid.Position} was harvested");
            TickUntil(() => false, 5);
            harvested ??= orchid;
            yield += AllItems(PetalDef) - before;
            if (yield > 0) break;
        }
        Check(harvested != null && yield > 0 && yield <= 6 * 3, $"Right-click harvesting produced petals ({yield})");
        Check(harvested.Spawned && harvested.Growth >= 0.7f && harvested.Growth < 0.75f && !harvested.HarvestableNow,
            $"The orchid survived at growth 0.7 and is not harvestable again ({harvested.Growth:0.000})");
        var refusal = RightClick(disciple, harvested).FirstOrDefault(o => o.Label.StartsWith("Cannot harvest moon orchid", StringComparison.Ordinal));
        Check(refusal != null && refusal.action == null && refusal.Label.Contains("regrowing"),
            $"A freshly harvested orchid explains why it cannot be harvested ({refusal?.Label})");
        Check(!new Designator_PlantsHarvest().CanDesignateThing(harvested).Accepted, "The native Harvest designator also refuses it until it regrows");

        // A reserved orchid names who holds it.
        var other = PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault(p => p != disciple && p.Map == cave);
        var reservedOrchid = orchids.FirstOrDefault(plant => plant != harvested && plant.HarvestableNow);
        if (reservedOrchid != null && other != null)
        {
            var hold = JobMaker.MakeJob(JobDefOf.Wait, 600);
            other.jobs.StartJob(hold, JobCondition.InterruptForced);
            Check(other.Reserve(reservedOrchid, hold), "Fixture: another colonist reserved an orchid");
            var reserved = RightClick(disciple, reservedOrchid).FirstOrDefault(o => o.Label.StartsWith("Cannot harvest", StringComparison.Ordinal));
            Check(reserved != null && reserved.Label.Contains("reserved"), $"An orchid reserved by another colonist explains it ({reserved?.Label})");
            other.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        // C. Seed gathering: distinct from petals, spends the same flowering, the plant survives.
        int seeds0 = AllItems(SeedDef);
        int gathered = 0;
        Plant seeded = null;
        foreach (var orchid in orchids.Where(plant => plant.HarvestableNow && plant != harvested).OrderByDescending(plant => plant.Growth).ToList())
        {
            float growth = orchid.Growth;
            int petalsBefore = AllItems(PetalDef), before = AllItems(SeedDef);
            var option = RightClick(disciple, orchid).FirstOrDefault(o => o.Label.StartsWith("Gather moon orchid seeds", StringComparison.Ordinal));
            Check(option?.action != null, $"Right-clicking a flowering orchid offers seed gathering. Offered: {string.Join(" | ", RightClick(disciple, orchid).Select(o => o.Label))}");
            option.action();
            Check(disciple.CurJobDef == GuPlantDefOf.GU_GatherPlantSeeds, "The seed-gathering job started");
            TickUntil(() => orchid.Growth < growth - 0.01f, 4000, $"Seeds were gathered from the orchid at {orchid.Position}");
            TickUntil(() => false, 5);
            Check(AllItems(PetalDef) == petalsBefore, "Gathering seeds gave no petals");
            seeded ??= orchid;
            gathered += AllItems(SeedDef) - before;
            if (gathered > 0) { seeded = orchid; break; }
        }
        Check(seeded != null && gathered > 0 && gathered <= 2, $"Seed gathering produced moon orchid seeds ({gathered})");
        Check(seeded.Spawned && Math.Abs(seeded.Growth - 0.7f) < 0.01f && !GuPlantFramework.CanGatherSeeds(seeded).Accepted && !seeded.HarvestableNow,
            "Gathering spent the flowering: growth 0.7, no petals or seeds until it regrows, and the plant survives");
        var seedGizmo = seeded.GetGizmos().OfType<Command_Action>().Single(c => c.defaultLabel == "Gather seeds");
        Check(seedGizmo.Disabled && seedGizmo.disabledReason.Contains("regrowing"), $"The plant's Gather seeds command explains why ({seedGizmo.disabledReason})");
        Check(!new Designator_GuGatherSeeds { }.CanDesignateThing(grass.First()).Accepted, "Spirit grass cannot be designated for seeds");
        Check(orchids.Count(plant => plant.Spawned) == orchids.Count, "No orchid was destroyed by harvesting or gathering");
        Report($"Orchids: {selectable} selected together; right-click harvest gave {yield} petals; right-click gathering gave {gathered} seeds; both plants survived at 0.70.");
        Check(AllItems(SeedDef) == seeds0 + gathered && AllItems(PetalDef) == petals0 + yield, "Items are exactly what the harvests produced");
    }

    // ---- Plant framework: seed-gated sowing ---------------------------------------------------------------------
    private static IntVec3 VerifySeedGatedSowing(Pawn disciple, Map cave)
    {
        // As a player would in the Work tab: the disciple grows and cuts plants.
        Check(!disciple.WorkTypeIsDisabled(WorkTypeDefOf.Growing) && !disciple.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting), $"{disciple.LabelShort} can grow and cut plants");
        disciple.workSettings.EnableAndInitialize();
        disciple.workSettings.SetPriority(WorkTypeDefOf.Growing, 3);
        disciple.workSettings.SetPriority(WorkTypeDefOf.PlantCutting, 3);
        var soil = DefDatabase<TerrainDef>.GetNamed("GU_PrimevalQiRichSoil");
        var cells = cave.AllCells.Where(c => c.GetTerrain(cave) == soil && c.GetThingList(cave).Count == 0 && cave.zoneManager.ZoneAt(c) == null
                && OrchidDef.CanNowPlantAt(c, cave) && disciple.CanReach(c, PathEndMode.Touch, Danger.Deadly) && !c.Fogged(cave))
            .OrderBy(c => c.DistanceToSquared(disciple.Position)).Take(3).ToList();
        Check(cells.Count == 3, $"Three open qi-rich soil cells for a moon orchid growing zone ({cells.Count})");
        var zone = new Zone_Growing(cave.zoneManager);
        cave.zoneManager.RegisterZone(zone);
        foreach (var cell in cells) zone.AddCell(cell);
        zone.SetPlantDefToGrow(OrchidDef);
        var sow = (WorkGiver_GrowerSow)DefDatabase<WorkGiverDef>.GetNamed("GrowerSow").Worker;
        var wanted = AccessTools.Field(typeof(WorkGiver_Grower), "wantedPlantDef");

        // Without seeds: refused with a reason, natively and in the right-click menu.
        foreach (var stack in cave.listerThings.ThingsOfDef(SeedDef)) stack.SetForbidden(true, false); GuPlantFramework.InvalidateMaterialCache();
        int seeds = AllItems(SeedDef);
        wanted.SetValue(null, null);
        JobFailReason.Clear();
        Check(sow.JobOnCell(disciple, cells[0], forced: true) == null && JobFailReason.Reason?.Contains("no moon orchid seeds", StringComparison.OrdinalIgnoreCase) == true,
            $"With no usable seeds the native grower refuses to sow moon orchids ({JobFailReason.Reason})");
        var menu = RightClickCell(disciple, cells[0]).Where(o => o.Label.IndexOf("sow", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        Check(menu.Any(o => o.action == null && o.Label.Contains("no moon orchid seeds", StringComparison.OrdinalIgnoreCase)),
            $"Right-clicking the zone explains it ({string.Join(" | ", menu.Select(o => o.Label))})");
        Check(AllItems(SeedDef) == seeds, "Nothing was consumed");

        // With seeds: the native order carries one seed, an interruption keeps it, completion spends exactly one.
        foreach (var stack in cave.listerThings.ThingsOfDef(SeedDef)) stack.SetForbidden(false, false); GuPlantFramework.InvalidateMaterialCache();
        wanted.SetValue(null, null);
        var job = sow.JobOnCell(disciple, cells[0], forced: true);
        Check(job?.def == GuPlantDefOf.GU_SowWithMaterial && job.targetB.Thing?.def == SeedDef && job.count == 1 && job.plantDefToSow == OrchidDef,
            $"With seeds available the native grower issues a seed-carrying sow job ({job?.def?.defName})");
        var order = RightClickCell(disciple, cells[0]).FirstOrDefault(o => o.action != null && o.Label.IndexOf("sow", StringComparison.OrdinalIgnoreCase) >= 0);
        Check(order != null, $"Right-click offers prioritized sowing ({string.Join(" | ", RightClickCell(disciple, cells[0]).Select(o => o.Label))})");
        order.action();
        Check(disciple.CurJobDef == GuPlantDefOf.GU_SowWithMaterial, $"Prioritized sowing started the seed-gated job ({disciple.CurJobDef?.defName})");
        TickUntil(() => cells[0].GetPlant(cave) is { def: var d, LifeStage: PlantLifeStage.Sowing } && d == OrchidDef, 3000, "The disciple began sowing with a seed in hand");
        Check(disciple.carryTracker.CarriedThing?.def == SeedDef && AllItems(SeedDef) == seeds, "The seed is carried, not yet spent");
        disciple.drafter.Drafted = true;
        TickUntil(() => false, 3);
        Check(cells[0].GetPlant(cave)?.def != OrchidDef && AllItems(SeedDef) == seeds && disciple.carryTracker.CarriedThing == null,
            $"An interrupted sowing removed the half-sown orchid and kept the seed, dropped not destroyed (plant {cells[0].GetPlant(cave)?.def.defName} {cells[0].GetPlant(cave)?.LifeStage}, "
            + $"seeds {seeds} -> {AllItems(SeedDef)}, carried {disciple.carryTracker.CarriedThing}, job {disciple.CurJobDef?.defName})");
        disciple.drafter.Drafted = false;
        // Undrafted, the grower may pick the sowing up on its own (native Growing work); otherwise the player orders it again.
        wanted.SetValue(null, null);
        GuPlantFramework.InvalidateMaterialCache();
        bool autonomous = disciple.CurJobDef == GuPlantDefOf.GU_SowWithMaterial;
        if (!autonomous)
        {
            var again = RightClickCell(disciple, cells[0]);
            order = again.FirstOrDefault(o => o.action != null && o.Label.IndexOf("sow", StringComparison.OrdinalIgnoreCase) >= 0);
            Check(order != null, $"After the interruption sowing can be ordered again ({string.Join(" | ", again.Select(o => o.Label))})");
            order.action();
        }
        int Sown() => cells.Count(c => c.GetPlant(cave) is { LifeStage: not PlantLifeStage.Sowing } p && p.def == OrchidDef);
        TickUntil(() => Sown() >= 1, 12000, "An orchid was sown");
        // Stop further sowing so every seed is accounted for, and let any sowing already under way finish.
        foreach (var stack in cave.listerThings.ThingsOfDef(SeedDef)) stack.SetForbidden(true, false); GuPlantFramework.InvalidateMaterialCache();
        TickUntil(() => disciple.CurJobDef != GuPlantDefOf.GU_SowWithMaterial, 12000, "The sowing work settled");
        TickUntil(() => false, 5);
        Check(Sown() >= 1 && AllItems(SeedDef) == seeds - Sown() && disciple.carryTracker.CarriedThing?.def != SeedDef
            && cells.All(c => c.GetPlant(cave) is not { LifeStage: PlantLifeStage.Sowing }),
            $"Each completed sowing spent exactly one seed: {Sown()} orchid(s) sown, seeds {seeds} -> {AllItems(SeedDef)} (grower {(autonomous ? "resumed on its own" : "ordered again")})");
        var sown = cells.Select(c => c.GetPlant(cave)).First(p => p?.def == OrchidDef);
        Check(sown.sown && sown.TryGetComp<CompGuPlant>() != null && !sown.HarvestableNow, "A sown orchid is an ordinary growing moon orchid");
        // Vanilla crops are untouched by the gate.
        wanted.SetValue(null, null);
        Check(GuPlantFramework.GateSowJob(disciple, new Job(JobDefOf.Sow, cells[1]) { plantDefToSow = ThingDefOf.Plant_Potato }, true)?.def == JobDefOf.Sow,
            "A vanilla crop's sow job passes through unchanged");
        Report($"Sowing: refused without seeds, interrupted without loss; {Sown()} orchid(s) sown for {seeds - AllItems(SeedDef)} seed(s), the grower {(autonomous ? "resuming by native work" : "ordered again")}.");
        Check(cells.Any(c => c.GetPlant(cave) == null), "A zone cell is left for the save-time sowing");
        return cells.First(c => c.GetPlant(cave) == null);
    }

    // ---- II. Underground petal hauling --------------------------------------------------------------------------
    private static (Pawn, GuIndependentSite) VerifyPetalHauling(Pawn disciple, Map cave, Map home)
    {
        var endpoint = home.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().First();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(endpoint, out var site, out _), "The cavern route resolved");
        var surface = site.Endpoints.Single(end => end.IsSurface).Anchor;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        var elders = Clan(GuClanRole.Elder);
        Pawn hauler = PawnsFinder.AllMaps_FreeColonistsSpawned.ToList().Where(p => p != disciple && !p.WorkTypeIsDisabled(GuPassageHauling.WorkType)
            && !elders.Contains(p)).OrderBy(p => p.Map == home ? 0 : 1).First();
        if (hauler.Map != home) TravelTo(hauler, home);
        StepAway(hauler);
        // A surface stockpile accepting stones, petals and seeds.
        var cells = Enumerable.Range(0, 4).Select(i => EmptyCargoCell(home, surface.Position, hauler, 4 + i)).Distinct().ToList();
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        foreach (var cell in cells) stockpile.AddCell(cell);
        var filter = stockpile.GetStoreSettings().filter;
        filter.SetDisallowAll();
        foreach (var def in new[] { StoneDef, PetalDef, SeedDef }) filter.SetAllow(def, true);
        // Two stone stacks and one petal stack in the cavern, outside storage. Without rotation the stones would go first.
        // Seeds stay forbidden here so the sowing steps keep them in the cavern.
        foreach (var stack in Find.Maps.SelectMany(m => m.listerThings.ThingsOfDef(SeedDef)).ToList()) stack.SetForbidden(true, false);
        foreach (var stack in cave.listerThings.ThingsOfDef(PetalDef).ToList()) stack.SetForbidden(true, false);
        var stoneA = GenSpawn.Spawn(MakeStack(30), EmptyCargoCell(cave, exit.Position, null, 4), cave);
        var stoneB = GenSpawn.Spawn(MakeStack(25), EmptyCargoCell(cave, exit.Position, null, 5), cave);
        var petals = SpawnItemAt(cave, PetalDef, 12, EmptyCargoCell(cave, exit.Position, null, 6));
        int stones = AllItems(StoneDef), petalTotal = AllItems(PetalDef);
        SetOnlyHaulingWork(hauler, GuPassageHauling.WorkType);
        Prefs.DevMode = false;
        surface.GetFloatMenuOptions(hauler).Single(option => option.Label == AssignHaulerLabel).action();
        Check(site.Hauling.Worker == hauler && site.Hauling.Enabled, $"{hauler.LabelShort} was assigned to underground hauling without Development Mode");
        var packed = new List<string>();
        Thing last = null;
        bool Delivered() => StoredOn(home, PetalDef) >= 12 && StoredOn(home, StoneDef) >= 55;
        TickUntil(() =>
        {
            var cargo = site.Hauling.Cargo;
            if (cargo != null && cargo != last) { packed.Add(cargo.def.defName); last = cargo; }
            if (cargo == null) last = null;
            Check(AllItems(StoneDef) == stones && AllItems(PetalDef) == petalTotal, "Hauling never creates or loses an item");
            return Delivered();
        }, 3 * Day, "Petals and both stone stacks reached surface storage");
        Check(packed.Count >= 3 && packed[0] == StoneDef.defName && packed[1] == PetalDef.defName,
            $"Petals were carried on the second trip, not after every stone stack ({string.Join(" -> ", packed)})");
        Check(StoredOn(home, PetalDef) == 12, "All twelve petals arrived in surface storage");
        foreach (var stack in cave.listerThings.ThingsOfDef(PetalDef).ToList()) stack.SetForbidden(false, false);
        Report($"Passage hauling: {hauler.LabelShort} carried {string.Join(" -> ", packed)}; all {stones} stones and {petalTotal} petals conserved.");
        return (hauler, site);
    }

    // ---- III. Background provisioning ---------------------------------------------------------------------------
    private static void VerifyProvisioning(Pawn disciple, Hediff_Aperture aperture, GuRecord liquor, GuRecord moon, Map cave, Map home)
    {
        var registry = IndividualRegistry;
        var component = SustenanceComponent;
        var ms = moon.GetState<GuNourishment>();
        var ls = liquor.GetState<GuNourishment>();
        // Clear every loose petal and beer the earlier steps left, so each item below is accounted for.
        foreach (var def in new[] { PetalDef, ThingDefOf.Beer })
        {
            foreach (var stack in cave.listerThings.ThingsOfDef(def).ToList()) stack.Destroy(DestroyMode.Vanish);
            foreach (var held in disciple.inventory.innerContainer.Where(t => t.def == def).ToList()) held.Destroy(DestroyMode.Vanish);
        }
        // A cavern stockpile with petals and beer, one forbidden petal stack in it, loose petals outside, and beer on the surface.
        var cells = Enumerable.Range(0, 5).Select(i => EmptyCargoCell(cave, disciple.Position, disciple, 3 + i)).Distinct().ToList();
        var pantry = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, cave.zoneManager);
        cave.zoneManager.RegisterZone(pantry);
        foreach (var cell in cells.Take(3)) pantry.AddCell(cell);
        var stored = SpawnItemAt(cave, PetalDef, 10, cells[0]);
        var storedBeer = SpawnItemAt(cave, ThingDefOf.Beer, 3, cells[1]);
        var forbidden = SpawnItemAt(cave, PetalDef, 4, cells[2]);
        forbidden.SetForbidden(true, false);
        var loose = SpawnItemAt(cave, PetalDef, 6, cells[4]);
        // Fixture: other cavern colonists are drafted for this step so ordinary hauling cannot move the control stacks.
        var drafted = cave.mapPawns.FreeColonistsSpawned.Where(p => p != disciple && !p.Drafted && p.drafter != null).ToList();
        foreach (var pawn in drafted) pawn.drafter.Drafted = true;
        if (!disciple.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)) disciple.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
        int surfaceBeer = AllItemsOn(home, ThingDefOf.Beer);
        Check(StoreUtility.IsInValidStorage(stored) && StoreUtility.IsInValidStorage(storedBeer) && !StoreUtility.IsInValidStorage(loose),
            "Fixture: petals and beer are in a cavern stockpile, one stack of petals lies loose");

        // The toggle off: a hungry Gu is not fed.
        Drain(moon, Snap(moon).NourishmentTicks - moon.Species.feeding.HungryTicks + 600);
        Drain(liquor, Snap(liquor).NourishmentTicks - liquor.Species.feeding.HungryTicks + 600);
        component.SetAutoTend(disciple, false);
        var off = (ms.MealsEaten, ls.MealsEaten);
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 1);
        Check((ms.MealsEaten, ls.MealsEaten) == off && stored.stackCount == 10, $"With the policy off nothing is fed (meals {off} -> {(ms.MealsEaten, ls.MealsEaten)}, pantry stack {stored.stackCount} spawned={stored.Spawned} at {stored.PositionHeld} holder={stored.ParentHolder}; disciple job {disciple.CurJobDef?.defName})");

        // The policy on: both Gu are fed in the background while the Gu Master keeps an ordered job.
        component.SetAutoTend(disciple, true);
        var wait = JobMaker.MakeJob(JobDefOf.Wait, 6000);
        Check(disciple.jobs.TryTakeOrderedJob(wait, JobTag.Misc), "Fixture: the Gu Master is busy with an ordered job");
        var moonBefore = (ms.MealsEaten, ms.ItemsEaten, ms.AutomaticMeals);
        var liquorBefore = (ls.MealsEaten, ls.ItemsEaten, ls.AutomaticMeals);
        bool interrupted = false;
        TickUntil(() =>
        {
            interrupted |= disciple.CurJob != wait || disciple.CurJobDef?.defName == GuSustenance.FeedJobDefName;
            return ms.MealsEaten > moonBefore.MealsEaten && ls.MealsEaten > liquorBefore.MealsEaten;
        }, GuSustenanceComponent.SweepPeriod * 2 + 10, "Automatic feeding fed both hungry Gu");
        int moonMeals = ms.MealsEaten - moonBefore.MealsEaten, liquorMeals = ls.MealsEaten - liquorBefore.MealsEaten;
        Check(!interrupted && disciple.CurJob == wait && component.Intents.Count == 0,
            "The Gu Master was never interrupted: no feeding job, no movement, the ordered job continued");
        Check(stored.stackCount == 10 - 2 * moonMeals && ms.AutomaticMeals == moonBefore.AutomaticMeals + moonMeals && ms.LastFeedSource == "stockpile"
            && ms.ItemsEaten == moonBefore.ItemsEaten + 2 * moonMeals,
            $"The Moonlight Gu ate {moonMeals} meal(s): exactly {2 * moonMeals} stockpiled petals were used ({stored.stackCount} left)");
        Check((storedBeer.Destroyed ? 0 : storedBeer.stackCount) == 3 - liquorMeals && ls.LastFeedSource == "stockpile" && liquorMeals >= 1,
            $"The Liquor Worm drank {liquorMeals} stockpiled beer(s)");
        int surfaceBeerAfter = AllItemsOn(home, ThingDefOf.Beer);
        Check(forbidden.stackCount == 4 && loose.stackCount == 6 && surfaceBeerAfter == surfaceBeer,
            $"Forbidden petals, loose petals and the surface's beer were not touched "
            + $"(forbidden {forbidden.stackCount}/4, loose {loose.stackCount}/6, surface beer {surfaceBeerAfter}/{surfaceBeer})");
        Check(Snap(moon).Level == GuNourishmentLevel.Sated && Snap(liquor).Level == GuNourishmentLevel.Sated, "Both Gu are sated");
        foreach (var pawn in drafted) pawn.drafter.Drafted = false;
        disciple.jobs.EndCurrentJob(JobCondition.InterruptForced);

        // The cultivator's own inventory is used first.
        int Pantry() => cave.listerThings.ThingsOfDef(PetalDef).Where(t => StoreUtility.IsInValidStorage(t) && !t.IsForbidden(Faction.OfPlayer)).Sum(t => t.stackCount);
        Thing PantryStack()
        {
            var stack = cave.listerThings.ThingsOfDef(PetalDef).FirstOrDefault(t => StoreUtility.IsInValidStorage(t) && !t.IsForbidden(Faction.OfPlayer));
            if (stack == null) return SpawnItemAt(cave, PetalDef, 12, pantry.Cells.First(c => c.GetFirstItem(cave) == null));
            stack.stackCount = Math.Max(stack.stackCount, 12);
            return stack;
        }
        PantryStack();
        var carried = ThingMaker.MakeThing(PetalDef);
        carried.stackCount = 2;
        Check(disciple.inventory.innerContainer.TryAdd(carried, false), "Fixture: two petals in the Gu Master's inventory");
        Drain(moon, Snap(moon).NourishmentTicks - Day / 4);
        int pantryPetals = Pantry();
        int before = ms.MealsEaten;
        TickUntil(() => ms.MealsEaten > before, GuSustenanceComponent.SweepPeriod + 10, "Automatic feeding fed the Moonlight Gu again");
        int eaten = 2 * (ms.MealsEaten - before);
        Check(disciple.inventory.innerContainer.All(t => t.def != PetalDef) && ms.LastFeedSource.StartsWith("inventory", StringComparison.Ordinal)
            && Pantry() == pantryPetals - (eaten - 2),
            $"Petals the Gu Master carried were eaten first ({ms.LastFeedSource}; {eaten} eaten, pantry {pantryPetals} -> {Pantry()})");

        // A stack another colonist has claimed is never taken; the reason is shown.
        Drain(moon, Snap(moon).NourishmentTicks - moon.Species.feeding.HungryTicks + 300);
        var stored2 = PantryStack();
        var claimant = PawnsFinder.AllMaps_FreeColonistsSpawned.FirstOrDefault(p => p != disciple && p.Map == cave && !p.Downed);
        if (claimant == null)
        {
            var elders = Clan(GuClanRole.Elder);
            claimant = PawnsFinder.AllMaps_FreeColonistsSpawned.ToList().First(p => p != disciple && !p.Downed && !elders.Contains(p));
            TransferToFixture(claimant, cave, EmptyCargoCell(cave, disciple.Position, disciple, 6));
        }
        var claim = JobMaker.MakeJob(JobDefOf.Wait, 3000);
        claimant.jobs.StartJob(claim, JobCondition.InterruptForced);
        Check(claimant.Reserve(stored2, claim, 1, stored2.stackCount), "Fixture: another colonist reserved the whole pantry stack");
        int claimed = Pantry();
        before = ms.MealsEaten;
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 10);
        Check(ms.MealsEaten == before && Pantry() == claimed && component.LastProvision.TryGetValue(moon.Id, out var last) && !last.Fed
            && last.Reason.Contains("moon orchid petals"),
            $"A reserved stack is not taken; the reason is recorded ({(component.LastProvision.TryGetValue(moon.Id, out var l) ? l.Reason : "none")})");
        claimant.jobs.EndCurrentJob(JobCondition.InterruptForced);
        TickUntil(() => ms.MealsEaten > before, GuSustenanceComponent.SweepPeriod + 10, "Released, the stack fed the Gu");
        component.SetAutoTend(disciple, false);
        Report($"Provisioning: fed {moonMeals} Moonlight and {liquorMeals} Liquor Worm meal(s) with no job; inventory first; forbidden, loose, reserved and other-map food untouched.");
    }

    // ---- IV/V. Hope Gu ambience, encounters and capture in spirit grass ----------------------------------------
    private static (int, int) VerifyHopeAmbienceAndCapture(Pawn disciple, Map cave, CompGuSpiritSpring spring, Thing springThing)
    {
        var registry = IndividualRegistry;
        var profile = spring.AmbientProfiles.Single();
        string hope = profile.species.defName;
        int Living() => spring.MatureCountFor(hope) + spring.JuvenileCountFor(hope);
        int NonSurfaceRecords() => registry.Records.Count(record => record.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind);
        int WildCount() => cave.mapPawns.AllPawnsSpawned.Count(p => p is Pawn_GuWildProjection);
        int DormantCount() => cave.listerThings.AllThings.Count(thing => thing is Thing_GuDormantProjection);
        int living = Living(), records = NonSurfaceRecords(), wildBefore = WildCount(), dormantBefore = DormantCount();
        int spirits = GuSpringAmbience.SpiritCount(spring, profile);
        Check(spirits == Math.Min(profile.maxSpirits, living) && (living == 0) == (spirits == 0),
            $"The spring shows one ambient spirit per living Hope Gu, capped at {profile.maxSpirits} ({spirits} for {living})");
        TickUntil(() => false, 600);
        Check(Living() == living && NonSurfaceRecords() == records && WildCount() == wildBefore && DormantCount() == dormantBefore,
            $"Ambient spirits create no Gu record or projection and never change the cohort "
            + $"(cohort {living} -> {Living()}, non-surface records {records} -> {NonSurfaceRecords()}, "
            + $"wild {wildBefore} -> {WildCount()}, dormant {dormantBefore} -> {DormantCount()})");
        string inspect = springThing.GetInspectString();
        Check(inspect.Contains("Encounters:") && inspect.Contains("Liquor Worm: lure with beer") && inspect.Contains("Hope Gu: use Search"),
            $"The spring explains how each population is met:\n{inspect}");
        var gizmos = springThing.GetGizmos().OfType<Command_Action>().ToList();
        Check(gizmos.Any(g => g.defaultLabel == "Lure Liquor Worm") && !gizmos.Any(g => g.defaultLabel == "Search for Liquor Worm"),
            "Liquor Worm keeps its separate lure: the spring explains it and offers no search");
        var moonSearch = gizmos.SingleOrDefault(g => g.defaultLabel == "Search for Moonlight Gu");
        if (spring.MatureCountFor("GU_MoonlightGu") == 0)
            Check(moonSearch is { Disabled: true } && moonSearch.disabledReason.Contains("Moonlight"), $"A depleted search stays visible with its reason ({moonSearch?.disabledReason})");

        if (spring.HopeGuAvailable <= 0) { Report("No mature Hope Gu remain at this spring; the capture check uses the developer-spawned Gu instead."); return (living, spirits); }
        var known = registry.Records.ToHashSet();
        gizmos.Single(g => g.defaultLabel == "Search for Hope Gu").action();
        var hopeGu = registry.Records.Single(record => !known.Contains(record));
        Check(Living() == living - 1 && GuSpringAmbience.SpiritCount(spring, profile) == Math.Min(profile.maxSpirits, living - 1),
            "Encountering one Hope Gu took one adult from the cohort, and the ambience follows the cohort");
        var wild = (Pawn_GuWildProjection)registry.ProjectionOf(hopeGu);
        // Wait for the wild Gu to stand in spirit grass on its own, as in the reported case.
        TickUntil(() => wild.Spawned && wild.Position.GetPlant(cave)?.def == GrassDef, 6000);
        bool inGrass = wild.Position.GetPlant(cave)?.def == GrassDef;
        var options = wild.GetFloatMenuOptions(disciple).ToList();
        var capture = options.FirstOrDefault(o => o.Label == "Capture Hope Gu (unrefined)");
        Check(capture?.action != null, $"A wild Hope Gu {(inGrass ? "standing in spirit grass " : "")}can be captured ({string.Join(" | ", options.Select(o => o.Label))})");
        wild.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 4000), JobCondition.InterruptForced); // fixture: hold it still for the walk
        capture.action();
        TickUntil(() => registry.ProjectionOf(hopeGu) is Thing_GuDormantProjection, 3000, "The Hope Gu was captured by the ordinary job");
        var dormant = registry.ProjectionOf(hopeGu);
        var caughtAt = wild.GuTag().LastLocation(Now).Cell;
        bool caughtInGrass = caughtAt.GetPlant(cave)?.def == GrassDef;
        Check(dormant.Spawned && dormant.Map == cave && dormant.Position.DistanceToSquared(caughtAt) <= 2,
            $"The dormant Hope Gu lies at its native capture cell or an adjacent open cell ({caughtAt} -> {dormant.Position})");
        Report($"Hope Gu: {spirits} ambient spirits for {living} living; captured {(caughtInGrass ? "in spirit grass" : "on open ground")} at {caughtAt}.");
        return (Living(), GuSpringAmbience.SpiritCount(spring, profile));
    }

    // ---- V. Developer spawn; will-enforcement and storage by a second Gu Master ---------------------------------
    private static (Pawn, GuRecord) VerifyDeveloperSpawnAndSecondMaster(Pawn disciple, Map cave, Map home)
    {
        var registry = IndividualRegistry;
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_MoonlightGu");
        var springThing = cave.listerThings.AllThings.First(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = springThing.TryGetComp<CompGuSpiritSpring>();
        int records = registry.Records.Count, cohort = spring.MatureCountFor(species.defName);

        // Vanilla creation of a Gu body is refused with an explanation, and nothing enters the registry.
        string refusal = null;
        try { ThingMaker.MakeThing(species.wildProjectionKind.race); }
        catch (InvalidOperationException exception) { refusal = exception.Message; }
        Check(refusal != null && refusal.Contains("Spawn registered wild Gu") && registry.Records.Count == records,
            $"Vanilla spawning of a Gu body is refused and explained ({refusal})");
        IntVec3 cell = EmptyCargoCell(cave, disciple.Position, disciple, 3);
        Check(!registry.TrySpawnDevelopmentGu(species, cave, cell, configuredSpecies: true, out _), "Outside Development Mode the developer spawn is refused");
        Prefs.DevMode = true;
        bool spawned = registry.TrySpawnDevelopmentGu(species, cave, cell, configuredSpecies: true, out var wild);
        Prefs.DevMode = false;
        var record = registry.Resolve(wild);
        Check(spawned && record != null && record.Origin == GuRecord.DevelopmentOrigin && record.SpeciesDefName == species.defName
            && registry.Records.Count == records + 1 && spring.MatureCountFor(species.defName) == cohort
            && wild.GetInspectString().Contains("Development test Gu"),
            "The developer tool spawned one registered wild Moonlight Gu through the registry, marked as development origin, with no cohort debit");

        // The elder captures it with Development Mode off. Something lies under it, so the dormant Gu rests beside it.
        Pawn elder = Clan(GuClanRole.Elder).First();
        var elderAperture = Apertures(elder).Single();
        if (elder.Map != cave) TravelTo(elder, cave);
        SustainColonists();
        wild.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 5000), JobCondition.InterruptForced); // fixture: hold it still
        IntVec3 at = wild.Position;
        var blocker = SpawnItemAt(cave, ThingDefOf.Steel, 5, at);
        Check(GuIndividualRegistry.CaptureDestination(wild, elder) is var fallback && fallback.IsValid && fallback != at && fallback.AdjacentTo8Way(at),
            "With an item under the wild Gu, capture uses an open neighbouring cell");
        Order(elder, wild, "Capture Moonlight Gu (unrefined)");
        TickUntil(() => registry.ProjectionOf(record) is Thing_GuDormantProjection, 3000, "The elder captured the developer-spawned Gu");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(record);
        Check(dormant.Position != at && dormant.Position.AdjacentTo8Way(at) && blocker.Spawned && blocker.stackCount == 5,
            $"The dormant Gu rests beside the occupied cell ({dormant.Position} next to {at}); the item was not disturbed");
        blocker.Destroy(DestroyMode.Vanish);
        Order(elder, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == elder.inventory.GetDirectlyHeldThings(), 3000, "The elder picked it up");

        // Will-enforcement and storage through the Aperture actions.
        var view = new ApertureView();
        view.Refresh(elder);
        var enforce = view.Gu.Single(entry => entry.Record == record).Actions.Single(action => action.Kind == GuApertureActionKind.EnforceWill);
        SetEssence(elderAperture, elderAperture.MaxEssence);
        Check(enforce.Execute(out string failure), failure ?? "Will-enforcement started");
        TickUntil(() =>
        {
            if (elderAperture.Essence < elderAperture.MaxEssence * 0.5f) SetEssence(elderAperture, elderAperture.MaxEssence);
            if (elder.CurJob == null || elder.CurJobDef?.defName != "GU_RefineMortalGu")
            {
                SustainColonists();
                view.Refresh(elder);
                view.Gu.SingleOrDefault(entry => entry.Record == record)?.Actions.SingleOrDefault(action => action.Kind == GuApertureActionKind.ResumeWill)?.Execute(out _);
            }
            return registry.IsRecognizedByCultivator(record, elder);
        }, 30000, "The elder's will-enforcement refined the Moonlight Gu");
        view.Refresh(elder);
        var store = view.Gu.Single(entry => entry.Record == record).Actions.Single(action => action.Kind == GuApertureActionKind.Store);
        Check(store.Execute(out failure) && registry.ApertureOccupants(elderAperture).Contains(record), failure ?? "Stored in the elder's aperture");
        TickUntil(() => record.GetState<GuNourishment>() != null, GuSustenanceComponent.SweepPeriod + 1, "The scheduler took the elder's Gu into care");
        Report($"Developer spawn: registered Moonlight Gu {record.Id} (development origin), captured beside an item, refined and stored by {elder.LabelShort}.");
        return (elder, record);
    }

    // ---- III. Two Gu Masters, one supply ------------------------------------------------------------------------
    private static void VerifySharedSupply(Pawn disciple, GuRecord moon, Pawn elder, GuRecord elderGu, Map cave)
    {
        var component = SustenanceComponent;
        var pantry = cave.zoneManager.AllZones.OfType<Zone_Stockpile>().First();
        foreach (var stack in cave.listerThings.ThingsOfDef(PetalDef).ToList()) stack.Destroy(DestroyMode.Vanish);
        foreach (var pawn in new[] { disciple, elder })
            foreach (var held in pawn.inventory.innerContainer.Where(t => t.def == PetalDef).ToList()) held.Destroy(DestroyMode.Vanish);
        int Pantry() => cave.listerThings.ThingsOfDef(PetalDef).Where(StoreUtility.IsInValidStorage).Sum(t => t.stackCount);
        IntVec3 FreeCell() => pantry.Cells.First(c => c.GetFirstItem(cave) == null);
        var a = moon.GetState<GuNourishment>();
        var b = elderGu.GetState<GuNourishment>();
        int Meals() => a.MealsEaten + b.MealsEaten;
        // Scarcity: two starving Moonlight Gu of two masters, one meal of petals. Exactly one meal is eaten.
        SpawnItemAt(cave, PetalDef, 2, FreeCell());
        Drain(moon, Snap(moon).NourishmentTicks);
        Drain(elderGu, Snap(elderGu).NourishmentTicks);
        component.SetAutoTend(disciple, true);
        component.SetAutoTend(elder, true);
        int meals0 = Meals();
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod * 2 + 10);
        Check(Meals() == meals0 + 1 && TotalItems(cave, PetalDef) == 0, $"With one meal of petals for two hungry Gu, exactly one meal was eaten (meals +{Meals() - meals0}, cavern petals left {TotalItems(cave, PetalDef)})");
        // Plenty: both masters' Gu are fed from the same pantry, with exact consumption.
        SpawnItemAt(cave, PetalDef, 20, FreeCell());
        var before = (a.MealsEaten, b.MealsEaten);
        TickUntil(() => a.MealsEaten > before.Item1 && b.MealsEaten > before.Item2 || Snap(moon).Level == GuNourishmentLevel.Sated && Snap(elderGu).Level == GuNourishmentLevel.Sated,
            GuSustenanceComponent.SweepPeriod * 2 + 10, "Both masters' Gu were fed from the shared pantry");
        int used = 2 * (a.MealsEaten - before.Item1 + b.MealsEaten - before.Item2);
        Check(Snap(moon).Level == GuNourishmentLevel.Sated && Snap(elderGu).Level == GuNourishmentLevel.Sated && Pantry() == 20 - used && TotalItems(cave, PetalDef) == 20 - used,
            $"Two Gu Masters shared one pantry: {used} petals were eaten, exactly what their meals needed ({Pantry()} left)");
        component.SetAutoTend(elder, false);
        component.SetAutoTend(disciple, false);
        Report($"Shared supply: one meal for two starving Gu fed exactly one; with plenty both masters' Gu ate {used} petals from one pantry.");
    }

    // ---- VI. Cultivation is not sleep ----------------------------------------------------------------------------
    private static void VerifyRestDuringCultivation(Pawn disciple, Hediff_Aperture aperture)
    {
        var rest = disciple.needs.rest;
        Check(rest != null, "The disciple has the vanilla Rest need");
        rest.CurLevel = 0.32f;
        float start = rest.CurLevel, previous = start;
        bool restedWhileCultivating = false, rose = false, slept = false;
        int cultivated = 0, orders = 0;
        var kinds = new HashSet<string>();
        // A long session, as a player ordering cultivation through the night would run it: whenever a session ends (walls
        // full, a channel finished) the next one is ordered, until exhaustion forces sleep or most of a day has passed.
        bool IsCultivation(JobDef def) => def != null && (def.defName == "GU_NourishAperture" || def.defName == "GU_ChannelGuAbility");
        for (int tick = 0; tick < 40000 && !slept; tick++)
        {
            if (aperture.Essence < aperture.MaxEssence * 0.3f) SetEssence(aperture, aperture.MaxEssence);
            var job = disciple.CurJobDef;
            if (job == JobDefOf.LayDown || disciple.jobs.curDriver?.asleep == true) { slept = true; break; }
            if (!IsCultivation(job))
            {
                var view = new ApertureView();
                view.Refresh(disciple);
                var action = view.NourishAction is { Available: true } nourish ? nourish : view.ChannelActions.FirstOrDefault(a => a.Available);
                if (action != null && action.Execute(out _)) orders++;
                if (view.AdvanceAction is { Available: true } advance && action == null) advance.Execute(out _);
            }
            if (IsCultivation(disciple.CurJobDef))
            {
                kinds.Add(disciple.CurJobDef.defName);
                cultivated++;
                restedWhileCultivating |= rest.Resting;
                rose |= rest.CurLevel > previous + 0.0001f;
            }
            previous = rest.CurLevel;
            Find.TickManager.DoSingleTick();
        }
        Check(orders >= 1 && cultivated > 5000 && !restedWhileCultivating && !rose && rest.CurLevel < start - 0.05f,
            $"Over {cultivated} ticks of cultivation ({string.Join(", ", kinds)}; {orders} orders) the Rest need only fell ({start:0.000} -> {rest.CurLevel:0.000}) and the pawn never rested");
        Report($"Rest: {cultivated} ticks of ordered cultivation ({string.Join(", ", kinds)}, {orders} orders); Rest {start:0.00} -> {rest.CurLevel:0.00} ({rest.CurCategory}); "
            + (slept ? "exhaustion then put the pawn to ordinary sleep." : "the budget ended before involuntary sleep."));
        disciple.jobs.EndCurrentJob(JobCondition.InterruptForced);
    }

    // ---- Save helpers ---------------------------------------------------------------------------------------------
    private static Thing StartPassageCargo(Pawn hauler, GuIndependentSite site, Map cave)
    {
        // The provisioning pantries are finished with; without them cavern haulers cannot stockpile the cargo petals first.
        foreach (var zone in cave.zoneManager.AllZones.OfType<Zone_Stockpile>().ToList()) zone.Delete();
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        SpawnItemAt(cave, PetalDef, 9, EmptyCargoCell(cave, exit.Position, null, 5));
        site.Hauling.NextCheckTick = 0;
        if (hauler.Drafted) hauler.drafter.Drafted = false;
        TickUntil(() => { if (Now % 2500 == 0) SustainColonists(); return site.Hauling.Cargo != null; }, Day);
        Check(site.Hauling.Cargo != null, $"{hauler.LabelShort} packed passage cargo (hauler on {hauler.Map} at {hauler.Position}, job {hauler.CurJobDef?.defName}, drafted {hauler.Drafted}, "
            + $"carrying {hauler.carryTracker.CarriedThing}, enabled {site.Hauling.Enabled}, worker {site.Hauling.Worker?.LabelShort}, plan {GuPassageHauling.Plan(hauler)?.def?.defName}; "
            + $"loose petals in cavern {cave.listerThings.ThingsOfDef(PetalDef).Count(t => !StoreUtility.IsInValidStorage(t) && !t.IsForbidden(Faction.OfPlayer))}; surface room for petals "
            + $"{Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.zoneManager.AllZones.OfType<Zone_Stockpile>()).Sum(z => z.Cells.Sum(c => c.GetItemStackSpaceLeftFor(z.Map, PetalDef)))})");
        var cargo = site.Hauling.Cargo;
        Check(cargo.stackCount > 0 && !cargo.Destroyed, $"The packed cargo is a real stack ({cargo.stackCount} {cargo.def.defName}, destroyed={cargo.Destroyed}); hauler {hauler.LabelShort} on {hauler.Map}, job {hauler.CurJobDef?.defName}, inventory: "
            + string.Join(", ", hauler.inventory.innerContainer.Select(t => $"{t.stackCount} {t.def.defName}")));
        // Hold the cargo for the save: surface storage refuses petals until the reload stage allows them again.
        foreach (var zone in Find.Maps.Where(m => m.IsPlayerHome).SelectMany(m => m.zoneManager.AllZones.OfType<Zone_Stockpile>()))
            zone.GetStoreSettings().filter.SetAllow(cargo.def, false);
        return cargo;
    }

    private static string StartSowing(Pawn disciple, Map cave, IntVec3 cell, out int seeds)
    {
        foreach (var stack in cave.listerThings.ThingsOfDef(SeedDef)) stack.SetForbidden(false, false); GuPlantFramework.InvalidateMaterialCache();
        if (AllItems(SeedDef) == 0) SpawnItem(cave, SeedDef, 2, disciple.Position, 4);
        seeds = AllItems(SeedDef);
        AccessTools.Field(typeof(WorkGiver_Grower), "wantedPlantDef").SetValue(null, null);
        var order = RightClickCell(disciple, cell).First(o => o.action != null && o.Label.IndexOf("sow", StringComparison.OrdinalIgnoreCase) >= 0);
        order.action();
        TickUntil(() => cell.GetPlant(cave) is { LifeStage: PlantLifeStage.Sowing } plant && plant.def == OrchidDef, 3000, "The disciple is sowing at the save");
        TickUntil(() => false, 30);
        Check(disciple.CurJobDef == GuPlantDefOf.GU_SowWithMaterial && disciple.carryTracker.CarriedThing?.def == SeedDef && AllItems(SeedDef) == seeds,
            "At the save: sowing in progress with the seed in hand");
        return disciple.CurJobDef.defName;
    }

    // ---- Stage "reload" -------------------------------------------------------------------------------------------
    private static void VerifyAlpha5BReload()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        DisableFixtureAutosave();
        var state = JsonSerializer.Deserialize<Alpha5BState>(File.ReadAllText(Alpha5BStatePath));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        Check(Now == state.Tick && registry.Records.Count == state.Records, "The save loaded at the same tick with the same records");
        foreach (var expected in state.Values)
        {
            var actual = Values(registry.Resolve(expected.Id));
            Check(actual == expected, $"Gu {expected.Id} kept identical nourishment and history ({actual} vs {expected})");
        }
        var cell = new IntVec3(state.SowX, 0, state.SowZ);
        Check(disciple.CurJobDef == GuPlantDefOf.GU_SowWithMaterial && disciple.carryTracker.CarriedThing?.def == SeedDef && AllItems(SeedDef) == state.Seeds,
            "The sowing resumed with the seed still in hand and nothing spent");
        var log = new List<string>();
        for (int i = 0; i < 12000 && !(cell.GetPlant(cave) is { LifeStage: not PlantLifeStage.Sowing } done && done.def == OrchidDef); i++)
        {
            if (i % 2000 == 0) log.Add($"t{i}: job {disciple.CurJobDef?.defName} toil {disciple.jobs.curDriver?.CurToilIndex} plant {cell.GetPlant(cave)?.def.defName}/{cell.GetPlant(cave)?.Growth:0.00000} carried {disciple.carryTracker.CarriedThing?.LabelShort} pos {disciple.Position} {DriverState(disciple)}");
            Find.TickManager.DoSingleTick();
        }
        Check(cell.GetPlant(cave) is { LifeStage: not PlantLifeStage.Sowing } p2 && p2.def == OrchidDef, "The resumed sowing completed: " + string.Join(" | ", log));
        TickUntil(() => false, 30);
        Check(AllItems(SeedDef) == state.Seeds - 1, $"The resumed sowing spent exactly one seed ({state.Seeds} -> {AllItems(SeedDef)})");

        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        var endpoint = home.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().First();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(endpoint, out var site, out _), "The route resolved");
        TickUntil(() => false, 300);
        Check(site.Hauling.Cargo is { } cargo && cargo.stackCount == state.Cargo && cargo.def.defName == state.CargoDef
            && site.Hauling.Worker?.ThingID == state.HaulerId, "The hauler still holds the same packed cargo");
        int petals = AllItems(PetalDef);
        Check(petals == state.Petals, $"Petals are conserved across the reload ({petals} vs {state.Petals})");
        foreach (var zone in home.zoneManager.AllZones.OfType<Zone_Stockpile>()) zone.GetStoreSettings().filter.SetAllow(DefDatabase<ThingDef>.GetNamed(state.CargoDef), true);
        TickUntil(() => site.Hauling.Cargo == null, Day, "The cargo was delivered after reloading");
        Check(AllItems(PetalDef) == petals && AllItems(StoneDef) == state.Stones, "Delivery after reload created and lost nothing");

        // Provisioning continues after reload.
        SustainColonists();
        var liquor = registry.Resolve(state.LiquorId);
        var ls = liquor.GetState<GuNourishment>();
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, cave.zoneManager);
        cave.zoneManager.RegisterZone(stockpile);
        IntVec3 pantryCell = EmptyCargoCell(cave, disciple.Position, disciple, 3);
        stockpile.AddCell(pantryCell);
        var beer = SpawnItemAt(cave, ThingDefOf.Beer, 2, pantryCell);
        SustenanceComponent.SetAutoTend(disciple, true);
        int meals = ls.MealsEaten;
        TickUntil(() => ls.MealsEaten > meals, GuSustenanceComponent.SweepPeriod + 10);
        Check(ls.MealsEaten > meals, $"Automatic feeding works after reload (last attempt: {(SustenanceComponent.LastProvision.TryGetValue(liquor.Id, out var why) ? why.Reason : "none")}; "
            + $"Liquor {Snap(liquor).Level} {Snap(liquor).Mode}; disciple on {disciple.Map} downed={disciple.Downed} food={disciple.needs.food?.CurLevel:0.00}; beer {beer.stackCount} spawned={beer.Spawned} stored={StoreUtility.IsInValidStorage(beer)})");
        Check((beer.Destroyed ? 0 : beer.stackCount) == 2 - (ls.MealsEaten - meals), "Exactly the drunk beer was used");

        // Performance of the provisioning path on a hungry Gu whose master has no beer: cold, then with the negative cache warm.
        foreach (var stack in Find.Maps.SelectMany(m => m.listerThings.ThingsOfDef(ThingDefOf.Beer)).ToList()) stack.Destroy(DestroyMode.Vanish);
        foreach (var held in disciple.inventory.innerContainer.Where(t => t.def == ThingDefOf.Beer).ToList()) held.Destroy(DestroyMode.Vanish);
        Drain(liquor, Snap(liquor).NourishmentTicks - liquor.Species.feeding.HungryTicks + 300);
        var sustenance = SustenanceComponent;
        GuProvisioning.ResetCaches();
        long t0 = Stopwatch.GetTimestamp();
        sustenance.Visit(registry, liquor, Now);
        double cold = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
        t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < 5000; i++) sustenance.Visit(registry, liquor, Now);
        double warm = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 5000;
        var fed = registry.Resolve(state.MoonId);
        Check(sustenance.LastProvision.TryGetValue(liquor.Id, out var miss) && !miss.Fed && miss.Reason.Contains("beer"), "With no beer anywhere the attempt is refused with its reason");
        Report($"Provisioning cost: a hungry visit with no food costs {cold:0.0} µs cold and {warm:0.00} µs with the negative cache; "
            + $"100 hungry Gu Masters would cost about {warm * 100 / 1000:0.000} ms per in-game hour.");
        Check(warm < 50, $"A cached hungry visit costs {warm:0.00} µs");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha5b-reload");
    }

    // ---- Stage "legacy5a": a save written by the Alpha 5A build -------------------------------------------------------
    private sealed record Alpha5AIds(string CultivatorId, string LiquorId, string MoonId, string SecondId, string HopeId);

    private static void VerifyAlpha5BLegacy()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        DisableFixtureAutosave();
        var ids = JsonSerializer.Deserialize<Alpha5AIds>(File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha5a-ids.json")));
        var registry = IndividualRegistry;
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == ids.CultivatorId);
        Map cave = disciple.Map;
        Current.Game.CurrentMap = cave;
        var liquor = registry.Resolve(ids.LiquorId);
        var moon = registry.Resolve(ids.MoonId);
        Check(liquor?.GetState<GuNourishment>() != null && moon?.GetState<GuNourishment>() != null && !SustenanceComponent.ActivatedOnLegacySave,
            "The Alpha 5A save loaded with its feeding states and no re-activation");
        Check(liquor.GetState<GuNourishment>().AutomaticMeals == 0 && liquor.GetState<GuNourishment>().LastFeedSource == null,
            "New Alpha 5B history fields start empty on an Alpha 5A save");
        // Its feeding job (Alpha 5A physical feeding) resumes and commits once.
        var ms = moon.GetState<GuNourishment>();
        int petals = TotalItems(cave, PetalDef), meals = ms.MealsEaten;
        Check(disciple.jobs.curDriver is ProjectGu.Jobs.JobDriver_GuFeedGu, $"The Alpha 5A feeding job was restored ({disciple.CurJobDef?.defName})");
        TickUntil(() => disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName, 600, "The restored feeding job finished");
        Check(ms.MealsEaten > meals && TotalItems(cave, PetalDef) == petals - 2 * (ms.MealsEaten - meals) && ms.LastFeedSource?.StartsWith("fed by", StringComparison.Ordinal) == true,
            "The Alpha 5A feeding committed once, with exact petals, under the new build");
        // Its orchids work with the framework: right-click harvest and seeds.
        var orchids = cave.listerThings.ThingsOfDef(OrchidDef).OfType<Plant>().ToList();
        Check(orchids.Count > 0 && orchids.All(plant => plant.TryGetComp<CompGuPlant>() != null), "Existing orchids gained the plant component without migration");
        TickUntil(() => { if (Now % 2500 == 0) SustainColonists(); return orchids.Any(plant => plant.HarvestableNow); }, 3 * Day);
        var flowering = orchids.FirstOrDefault(plant => plant.HarvestableNow);
        if (flowering != null)
        {
            SustainColonists();
            var option = RightClick(disciple, flowering).FirstOrDefault(o => o.action != null && (o.Label.StartsWith("Harvest moon orchid", StringComparison.Ordinal)
                || o.Label.StartsWith("Prioritize", StringComparison.Ordinal)));
            Check(option != null, $"An existing orchid offers harvesting ({string.Join(" | ", RightClick(disciple, flowering).Select(o => o.Label))})");
        }
        // Background provisioning feeds its refined Liquor Worm from a stockpile.
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) SustenanceComponent.SetAutoTend(colonist, false);
        SustenanceComponent.SetAutoTend(disciple, true);
        var ls = liquor.GetState<GuNourishment>();
        Drain(liquor, Snap(liquor).NourishmentTicks - liquor.Species.feeding.HungryTicks + 300);
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, cave.zoneManager);
        cave.zoneManager.RegisterZone(stockpile);
        IntVec3 cell = EmptyCargoCell(cave, disciple.Position, disciple, 3);
        stockpile.AddCell(cell);
        var beer = SpawnItemAt(cave, ThingDefOf.Beer, 2, cell);
        int drunk = ls.MealsEaten;
        TickUntil(() => ls.MealsEaten > drunk, GuSustenanceComponent.SweepPeriod + 10, "Background provisioning fed the Alpha 5A save's Liquor Worm");
        Check((beer.Destroyed ? 0 : beer.stackCount) == 2 - (ls.MealsEaten - drunk) && ls.AutomaticMeals == ls.MealsEaten - drunk,
            "It drank exactly the stockpiled beer it needed");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha5b-legacy5a");
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------
    private static List<FloatMenuOption> RightClick(Pawn pawn, Thing target)
    {
        Current.Game.CurrentMap = target.Map;
        return FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, target.TrueCenter(), out _);
    }

    private static List<FloatMenuOption> RightClickCell(Pawn pawn, IntVec3 cell)
    {
        Current.Game.CurrentMap = pawn.Map;
        return FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, cell.ToVector3Shifted(), out _);
    }

    private static int AllItems(ThingDef def) => Find.Maps.Sum(map => TotalItems(map, def));

    private static int AllItemsOn(Map map, ThingDef def) => TotalItems(map, def);

    private static int StoredOn(Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Where(StoreUtility.IsInValidStorage).Sum(t => t.stackCount);
}

public sealed partial class LiveLifecycleChecks
{
    /// <summary>Diagnostic: the sowing driver's private progress and the pawn's tick-relevant state.</summary>
    private static string DriverState(Pawn pawn)
    {
        var driver = pawn.jobs.curDriver;
        if (driver == null) return "no driver";
        object F(Type type, string name) => AccessTools.Field(type, name)?.GetValue(driver);
        return $"work={F(driver.GetType(), "sowWorkDone")} committed={F(driver.GetType(), "committed")} wantNext={F(typeof(JobDriver), "wantBeginNextToil")} "
            + $"ended={F(typeof(JobDriver), "ended")} ticksLeft={driver.ticksLeftThisToil} speed={pawn.GetStatValue(StatDefOf.PlantWorkSpeed):0.00} "
            + $"busy={pawn.stances.FullBodyBusy} food={pawn.needs.food?.CurLevel:0.000} rest={pawn.needs.rest?.CurLevel:0.000} downed={pawn.Downed} "
            + $"toil={driver.CurToilIndex} queue={pawn.jobs.jobQueue.Count} spawned={pawn.Spawned} suspended={pawn.Suspended}";
    }
}
