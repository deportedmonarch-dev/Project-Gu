using System.Text.Json;
using HarmonyLib;
using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private const string PrioritizePassageLabel = "Prioritize underground hauling";
    private static string LogisticsStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha5b-logistics.json");
    private static ThingDef[] LogisticsKinds => new[] { StoneDef, PetalDef, SeedDef };
    private static readonly System.Reflection.FieldInfo StoneProduction =
        AccessTools.Field(typeof(CompGuSpiritSpring), "lifetimeProduced");
    private static readonly System.Reflection.FieldInfo NextStoneProduction =
        AccessTools.Field(typeof(CompGuSpiritSpring), "nextProductionTick");

    private sealed record LogisticsState(string Worker, string Cargo, int Stone, int Petals, int Seeds);

    private static void VerifyAlpha5BLogistics()
    {
        var (_, disciple, _, liquor) = LoadAlpha3State();
        var (home, entrance, exit, site) = LogisticsRoute();
        Map cave = exit.Map;
        Current.Game.CurrentMap = cave;
        PrepareLogistics();
        var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned
            .Where(pawn => !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)).ToList();
        foreach (var pawn in colonists) pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
        var haulers = colonists.Where(pawn => pawn.Map == home).Take(2).ToArray();
        Check(haulers.Length == 2, "Two surface colonists can haul");

        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        foreach (int distance in Enumerable.Range(4, 8))
            stockpile.AddCell(EmptyCargoCell(home, entrance.Position, null, distance));
        var filter = stockpile.GetStoreSettings().filter;
        foreach (var pawn in haulers) { pawn.drafter.Drafted = false; SetOnlyHaulingWork(pawn); }
        foreach (var stack in cave.listerThings.ThingsOfDef(StoneDef)) stack.SetForbidden(true, false);
        AssertNoLogisticsDispatch(site, haulers, home, "No eligible cargo");

        ToggleHauling(entrance, false);
        TickUntil(() => Ledger.ChannelFor(liquor.Id) == null, 3000, "The saved Liquor Worm session completed");
        VerifyOrchidInteractions(disciple, cave);
        foreach (var stack in cave.listerThings.ThingsOfDef(StoneDef)) stack.SetForbidden(false, false);
        var spring = LogisticsSpring(cave);
        int produced = StonesProduced(spring);
        TickUntil(() => { SustainLogistics(); return StonesProduced(spring) > produced; }, Day + 1000,
            "The spring produced a scheduled stone batch with Dev Mode off");
        ReportLogisticsSources(home, entrance, exit, site);

        AssertNoLogisticsDispatch(site, haulers, home, "Paused passage");
        foreach (var def in LogisticsKinds) filter.SetAllow(def, false);
        ToggleHauling(entrance, true);
        AssertNoLogisticsDispatch(site, haulers, home, "Destination filters reject all cargo");
        PrioritizePassage(haulers[0], entrance);
        Check(!IsPassageWork(haulers[0]), "A priority order cannot bypass destination filters");
        foreach (var def in LogisticsKinds) filter.SetAllow(def, true);

        disciple.jobs.StopAll();
        disciple.drafter.Drafted = true;
        disciple.Position = exit.Position;
        disciple.Notify_Teleported();
        AssertNoLogisticsDispatch(site, haulers, home, "Occupied underground landing");
        ToggleHauling(entrance, false);
        StepAway(disciple);
        Check(JobDriver_GuPocketProbeTravel.CanLand(cave, exit.Position, null), "The landing is open again");

        // A forced order uses the same job and native reservation as ordinary dispatch.
        Pawn ordered = haulers[0];
        haulers[1].drafter.Drafted = true;
        ordered.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 600), JobTag.Misc);
        ToggleHauling(entrance, true);
        TickUntil(() => false, 300);
        Check(ordered.CurJobDef == JobDefOf.Wait && !IsPassageWork(ordered), "Hauling did not replace an ordered wait");
        PrioritizePassage(ordered, entrance);
        Check(ordered.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulTravel && ordered.CurJob.playerForced
            && home.reservationManager.ReservedBy(entrance, ordered, ordered.CurJob), "Prioritize issued and reserved an actual passage job");
        ordered.drafter.Drafted = true;
        ordered.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
        Check(ordered.Map == home && !home.reservationManager.IsReserved(entrance) && site.Hauling.Cargo == null,
            "Interrupting departure released the passage and left the source stacks intact");
        foreach (var pawn in haulers) pawn.drafter.Drafted = false;

        int petals = AllItems(PetalDef), seeds = AllItems(SeedDef), stones = AllItems(StoneDef);
        int beforeProduction = StonesProduced(spring);
        TickUntil(() =>
        {
            SustainLogistics();
            CheckLogisticsTotals(spring, beforeProduction, stones, petals, seeds);
            Check(haulers.Count(pawn => pawn.Map == cave || IsPassageWork(pawn)) <= 1,
                "Two eligible workers never started the same trip");
            return site.Hauling.Cargo != null;
        }, 9000);
        Pawn worker = site.Hauling.Worker;
        Thing cargo = site.Hauling.Cargo;
        Check(worker?.Map == cave && cargo?.def == StoneDef && cargo.holdingOwner == worker.inventory.innerContainer,
            $"A surface worker autonomously descended and packed real spring stone. {LogisticsStatus(site, haulers)}");
        Check(haulers.Contains(worker), "The trip belongs to one of the two surface candidates");
        Report($"Native work chose {worker.LabelShort} without a travel order. Packed {cargo.stackCount} {cargo.def.defName}; the second candidate stayed on the surface.");

        // Sustain the supply while the first batch is held. The next two trips must choose the crop products
        // even though another real batch of stones is accessible.
        worker.drafter.Drafted = true;
        Check(worker.Map == cave && site.Hauling.Cargo == cargo, "Drafting immediately retained native inventory custody");
        // The first batch used the normal schedule. Bring the next deadline forward so a full day drafted in an
        // unconditioned cavern does not make temperature and incapacitation part of the logistics fixture.
        NextStoneProduction.SetValue(spring, Now + 500);
        int previous = StonesProduced(spring);
        TickUntil(() =>
        {
            SustainLogistics();
            CheckLogisticsTotals(spring, beforeProduction, stones, petals, seeds);
            return StonesProduced(spring) > previous;
        }, 1500, "The scheduled spring produced again while the first load was held");
        Check(worker.Map == cave && site.Hauling.Cargo == cargo && cave.listerThings.ThingsOfDef(StoneDef).Count > 0,
            $"Drafting preserved the first load and left a second batch waiting. Expected {cargo}, "
            + $"owner={cargo.holdingOwner}, downed={worker.Downed}, stone stacks below={cave.listerThings.ThingsOfDef(StoneDef).Count}. "
            + LogisticsStatus(site, haulers));

        var state = new LogisticsState(worker.ThingID, cargo.ThingID, AllItems(StoneDef), petals, seeds);
        File.WriteAllText(LogisticsStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha5b-logistics");
    }

    private static void VerifyAlpha5BLogisticsReload()
    {
        var (home, _, exit, site) = LogisticsRoute();
        PrepareLogistics();
        var saved = JsonSerializer.Deserialize<LogisticsState>(File.ReadAllText(LogisticsStatePath));
        var haul = site.Hauling;
        Map cave = exit.Map;
        var spring = LogisticsSpring(cave);
        Pawn worker = haul.Worker;
        Check(haul.Automatic && haul.Enabled && worker?.ThingID == saved.Worker && worker?.Map == cave
            && haul.Cargo?.ThingID == saved.Cargo && haul.Cargo.holdingOwner == worker.inventory.innerContainer,
            "The automatic trip restored its original worker and physical stack in a fresh process");
        Check(AllItems(StoneDef) == saved.Stone && AllItems(PetalDef) == saved.Petals && AllItems(SeedDef) == saved.Seeds,
            "Fresh-process totals exactly match the save");
        worker.drafter.Drafted = false;

        int initial = StonesProduced(spring);
        int stoneAtHome = StoredOn(home, StoneDef);
        var rotation = new List<ThingDef> { StoneDef };
        Thing last = haul.Cargo;
        TickUntil(() =>
        {
            SustainLogistics();
            CheckLogisticsTotals(spring, initial, saved.Stone, saved.Petals, saved.Seeds);
            var cargo = haul.Cargo;
            if (cargo != null && cargo != last)
            {
                rotation.Add(cargo.def);
                if (cargo.def == PetalDef || cargo.def == SeedDef)
                    Check(cave.listerThings.ThingsOfDef(StoneDef).Any(item => !item.IsForbidden(Faction.OfPlayer)
                        && cave.reachability.CanReach(exit.Position, item, PathEndMode.ClosestTouch, TraverseMode.PassDoors)),
                        $"The trip took {cargo.def.defName} with more reachable stone still waiting");
            }
            last = cargo;
            return StoredOn(home, PetalDef) == saved.Petals && StoredOn(home, SeedDef) == saved.Seeds;
        }, 16000);
        Check(StoredOn(home, StoneDef) > stoneAtHome && StoredOn(home, PetalDef) == saved.Petals && StoredOn(home, SeedDef) == saved.Seeds,
            $"All three real products reached surface storage. {LogisticsStatus(site, PawnsFinder.AllMaps_FreeColonistsSpawned)}");
        Check(rotation.Take(3).SequenceEqual(LogisticsKinds), $"Rotation was stone, petals, seeds ({string.Join(", ", rotation.Select(def => def.defName))})");
        Check(worker.Map == home && haul.Cargo == null && haul.Worker == null, "Delivery returned the original worker to the surface and released the trip");
        Report($"Fresh-process delivery stored {StoredOn(home, StoneDef) - stoneAtHome} new stone, {saved.Petals} harvested petals and {saved.Seeds} gathered seeds. Native inventory, storage, and spring credits reconcile.");
        AssertNoErrors("alpha5b-logistics-reload");
    }

    private static void VerifyOwnerLogisticsSave()
    {
        var (home, entrance, exit, site) = LogisticsRoute();
        PrepareLogistics();
        ReportLogisticsSources(home, entrance, exit, site);
        ThingDef[] kinds = LogisticsKinds;
        int[] expected = kinds.Select(def => StoredOn(home, def) + exit.Map.listerThings.ThingsOfDef(def).Sum(item => item.stackCount)).ToArray();
        Check(expected[1] > 0 && expected[2] > 0, "The owner checkpoint contains both orchid products");
        bool Delivered() => kinds.Select((def, i) => StoredOn(home, def) >= expected[i]).All(done => done);
        TickUntil(() => { SustainLogistics(); return Delivered(); }, 18000);
        string amounts = string.Join(", ", kinds.Select((def, i) => $"{def.defName} {StoredOn(home, def)}/{expected[i]}"));
        Check(Delivered(), $"Owner checkpoint delivery ({amounts}). {LogisticsStatus(site, PawnsFinder.AllMaps_FreeColonistsSpawned)}");
        Report($"Owner checkpoint delivered {amounts} with original Work priorities and stockpile settings.");
        AssertNoErrors("alpha5b-owner-logistics");
    }

    private static (Map, Building_GuPocketProbeEndpoint, Building_GuPocketProbeEndpoint, GuIndependentSite) LogisticsRoute()
    {
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        var entrance = home.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(entrance, out var site, out _), "The scenario passage resolved");
        return (home, entrance, site.Endpoints.Single(end => !end.IsSurface).Anchor, site);
    }

    private static void PrepareLogistics()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        DisableFixtureAutosave();
        foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned) SustenanceComponent.SetAutoTend(pawn, false);
    }

    private static void SustainLogistics()
    {
        if (Now % 500 == 0) SustainColonists();
    }

    private static void PrioritizePassage(Pawn pawn, Building_GuPocketProbeEndpoint entrance) =>
        RightClick(pawn, entrance).Single(option => option.Label == PrioritizePassageLabel).action();

    private static bool IsPassageWork(Pawn pawn) => pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulTravel
        || pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulPack || pawn.CurJobDef == GuPassageHaulDefOf.GU_PassageHaulStore;

    private static void AssertNoLogisticsDispatch(GuIndependentSite site, Pawn[] haulers, Map home, string reason)
    {
        TickUntil(() =>
        {
            SustainLogistics();
            Check(haulers.All(pawn => pawn.Map == home && !IsPassageWork(pawn)), $"{reason} must suppress dispatch. {LogisticsStatus(site, haulers)}");
            return false;
        }, 500);
        Report($"{reason} blocked dispatch for both surface workers.");
    }

    private static CompGuSpiritSpring LogisticsSpring(Map cave) => cave.listerThings.AllThings
        .Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).Single(comp => comp != null);

    private static int StonesProduced(CompGuSpiritSpring spring) => (int)StoneProduction.GetValue(spring);

    private static void CheckLogisticsTotals(CompGuSpiritSpring spring, int produced, int stones, int petals, int seeds) =>
        Check(AllItems(StoneDef) == stones + StonesProduced(spring) - produced && AllItems(PetalDef) == petals && AllItems(SeedDef) == seeds,
            "Ground, native inventory and carrying totals match supply and actual spring production");

    private static string LogisticsStatus(GuIndependentSite site, IEnumerable<Pawn> pawns) =>
        $"Route enabled={site.Hauling?.Enabled}, automatic={site.Hauling?.Automatic}, next probe={site.Hauling?.NextCheckTick}, "
        + $"worker={site.Hauling?.Worker?.LabelShort}, cargo={site.Hauling?.Cargo}; "
        + string.Join(" | ", pawns.Select(pawn => $"{pawn.LabelShort} {pawn.Map}:{pawn.Position} "
            + $"work={pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling)} job={pawn.CurJobDef?.defName} drafted={pawn.Drafted}"));

    private static void ReportLogisticsSources(Map home, Building_GuPocketProbeEndpoint entrance,
        Building_GuPocketProbeEndpoint exit, GuIndependentSite site)
    {
        Map cave = exit.Map;
        foreach (var item in LogisticsKinds.SelectMany(def => cave.listerThings.ThingsOfDef(def)))
        {
            bool accepts = home.haulDestinationManager.AllGroupsListInPriorityOrder.Any(group => group.Settings.AllowedToAccept(item));
            bool reachable = cave.reachability.CanReach(exit.Position, item, PathEndMode.ClosestTouch, TraverseMode.PassDoors);
            Report($"{item.ThingID}: {item.GetType().Name} x{item.stackCount} at {item.Position}; always={item.def.alwaysHaulable}, "
                + $"forbidden={item.IsForbidden(Faction.OfPlayer)}, stored={item.IsInValidStorage()}, reserved={cave.reservationManager.IsReserved(item)}, "
                + $"surface accepts={accepts}, reached from cave landing={reachable}");
        }
        Report($"Passage {entrance.Position} to {exit.Position}. {LogisticsStatus(site, PawnsFinder.AllMaps_FreeColonistsSpawned)}");
    }
}
