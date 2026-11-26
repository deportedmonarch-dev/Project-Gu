using System.Diagnostics;
using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Jobs;
using ProjectGu.Springs;
using ProjectGu.Sustenance;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

/// <summary>
/// Alpha 5A native acceptance, built on the Alpha 3 save (a disciple with a Liquor Worm and a Moonlight Gu in their
/// aperture, in the spring cavern). Development Mode stays off and every player action goes through the Aperture tab's
/// actions, the fallback menu or a native float menu.
///
/// Stage "feeding": the scheduler attaches nourishment without any interface; a Hope Gu gets no invented diet; real moon
/// orchids are harvested without being destroyed and refuse a second harvest until they regrow; nourishment declines over
/// real game time into hunger and starvation; an interrupted feeding keeps its petals; forbidden, unreachable and missing
/// food are refused without consuming anything; Moonlight Gu eats petals and Liquor Worm beer with exact consumption;
/// storing, withdrawing, carrying between the cavern and the surface and passing a Gu to another cultivator keep its
/// nourishment and feeding history; luring with beer is independent of feeding; routine feeding works without
/// interrupting an ordered job or a drafted pawn; withering blocks Moonblade and Liquor Worm refinement at the ability
/// service. It saves with one Gu sated, one hungry, one starving and a feeding job in progress.
/// Stage "reload": values are identical after a fresh process, the feeding job completes once, and two Gu die of
/// starvation through the terminal authority (one in the aperture, one carried). Stage "final": no second death after
/// another fresh process; scheduler cost is measured. Stage "legacy": the same save with every Alpha 5A element removed,
/// as an older build would have written it, loads with full nourishment, a grace period and no created food.
///
/// Long starvation periods are reached by draining the nourishment clock directly (see <see cref="Drain"/>), a test
/// fixture that stands in for days of ticking; the scheduler, terminal path and ability checks it triggers are the real ones.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha5A => GenCommandLine.CommandLineArgPassed("gualpha5a");
    private static string Alpha5AStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha5a-state.json");
    internal const string Alpha5AFeedingSave = "alpha5a-feeding";
    internal const string Alpha5ALegacySource = "alpha5a-legacy-source";
    private static ThingDef PetalDef => DefDatabase<ThingDef>.GetNamed("GU_MoonOrchidPetals");
    private static ThingDef OrchidDef => DefDatabase<ThingDef>.GetNamed("GU_MoonOrchid");
    private static GuSustenanceComponent SustenanceComponent => GuSustenanceComponent.Instance;
    private const int Day = GenDate.TicksPerDay;

    private sealed record NourishmentValues(string Id, int Nourishment, int Starving, int Meals, int Items, int LastFed, int FeedRevision, string Level, string Mode);
    private sealed record Alpha5AState(string CultivatorId, string LiquorId, string MoonId, string SecondId, string HopeId, int Tick,
        NourishmentValues[] Values, int Petals, int Beer, int Records, string FeedingGuId, int FeedingCount, int MoonMealsBefore,
        string[] Starved, int Letters);

    // ---------------------------------------------------------------------------------------------------------------
    private static void VerifyAlpha5AFeeding()
    {
        var (_, disciple, aperture, liquor) = LoadAlpha3State();
        string feedFailure = null, blocked = null, hopeReason = null;
        DisableFixtureAutosave();
        var registry = IndividualRegistry;
        var moon = registry.ApertureOccupants(aperture).Single(record => record.Id != liquor.Id);
        Map cave = disciple.Map;
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        var springThing = cave.listerThings.AllThings.First(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = springThing.TryGetComp<CompGuSpiritSpring>();
        var component = SustenanceComponent;
        Current.Game.CurrentMap = cave;
        Check(!Prefs.DevMode && component is { ActivatedTick: >= 0, ActivatedOnLegacySave: false }, "Feeding is active from the start of a new game");
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) component.SetAutoTend(colonist, false);
        TickUntil(() => Ledger.ChannelFor(liquor.Id) == null, 3000, "The saved Liquor Worm session completed");

        // 1. The scheduler attaches and advances nourishment with no interface open.
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 1);
        var ls = liquor.GetState<GuNourishment>();
        var ms = moon.GetState<GuNourishment>();
        Check(ls != null && ms != null && !ls.FromLegacySave && !ms.FromLegacySave && Snap(liquor).Mode == GuSustenanceMode.Advancing
            && Snap(moon).Mode == GuSustenanceMode.Advancing && Snap(liquor).NourishmentTicks > 0 && Snap(moon).NourishmentTicks > 0
            && ls.MealsEaten == 0 && ms.MealsEaten == 0,
            $"The scheduler attached nourishment to both refined Gu without any interface (Liquor {Days(Snap(liquor).NourishmentTicks)}, Moonlight {Days(Snap(moon).NourishmentTicks)})");
        Report($"Start: Liquor Worm {Days(Snap(liquor).NourishmentTicks)} ({Snap(liquor).Level}), Moonlight Gu {Days(Snap(moon).NourishmentTicks)} ({Snap(moon).Level}) at tick {Now}.");

        // 2. Hope Gu: no implemented policy, nothing invented, in any form.
        var known = registry.Records.ToHashSet();
        springThing.GetGizmos().OfType<Command_Action>().Single(command => command.defaultLabel == "Search for Hope Gu").action();
        var hope = registry.Records.Single(record => !known.Contains(record));
        bool hopeDormant = CaptureWild(disciple, hope, "Capture Hope Gu (unrefined)", required: false);
        if (!hopeDormant) Report("The Hope Gu could not be captured (no open cell under it); its no-diet policy is checked in wild form.");
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 1);
        var hopeThing = registry.ProjectionOf(hope);
        Check(hope.GetState<GuNourishment>() == null && Snap(hope).Status == GuFeedingStatus.NotImplemented && Snap(hope).Level == GuNourishmentLevel.Unconfigured
            && GuApertureActions.Feed(disciple, registry, hope) == null && !hopeThing.GetFloatMenuOptions(disciple).Any(option => option.Label.StartsWith("Feed", StringComparison.Ordinal))
            && registry.FeedAccess(disciple, hope, out _, out hopeReason) == GuFeedAccess.None && hopeReason.Contains("no individual feeding policy"),
            $"A Hope Gu has no invented diet: no state, no level, no feeding action ({hopeReason})");
        Check(!hopeThing.GetInspectString().Contains("Nourishment"), "The Hope Gu's inspection shows no nourishment");

        // 3. The UI reports each individual from the canonical record.
        var view = new ApertureView();
        view.Refresh(disciple);
        var liquorEntry = view.Gu.Single(entry => entry.Record == liquor);
        var moonEntry = view.Gu.Single(entry => entry.Record == moon);
        Check(liquorEntry.Nourishment.NourishmentTicks == Snap(liquor).NourishmentTicks && moonEntry.Nourishment.NourishmentTicks == Snap(moon).NourishmentTicks
            && liquorEntry.Foods.Single().Option.thingDef == ThingDefOf.Beer && liquorEntry.Foods.Single().Option.mealCount == 1
            && moonEntry.Foods.Single().Option.thingDef == PetalDef && moonEntry.Foods.Single().Option.mealCount == 2
            && moonEntry.Foods.Single().Available == AvailablePetals(disciple),
            "The Gu page shows each individual's nourishment, accepted food, meal size and food at hand");
        if (AvailablePetals(disciple) == 0)
            Check(moonEntry.FeedAction is { Available: false } && moonEntry.FeedAction.DisabledReason.Contains("moon orchid petals"),
                $"With no petals the Moonlight Gu's Feed action explains why ({moonEntry.FeedAction?.DisabledReason})");

        // 4. Harvest real moon orchids: petals without destroying the plant, then no second harvest until regrown.
        int orchids = cave.listerThings.ThingsOfDef(OrchidDef).Count;
        int petals0 = TotalItems(cave, PetalDef);
        var harvested = new List<(Plant Plant, float Before, int Yield)>();
        foreach (var orchid in cave.listerThings.ThingsOfDef(OrchidDef).OfType<Plant>().Where(plant => plant.HarvestableNow)
                     .OrderByDescending(plant => plant.Growth).Take(3).ToList())
        {
            int before = TotalItems(cave, PetalDef);
            float growth = orchid.Growth;
            cave.designationManager.AddDesignation(new Designation(orchid, DesignationDefOf.HarvestPlant));
            Check(disciple.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.HarvestDesignated, orchid), JobTag.Misc), "Harvest ordered");
            TickUntil(() => orchid.Growth < growth - 0.01f, 4000, $"The orchid at {orchid.Position} was harvested");
            TickUntil(() => false, 5);
            harvested.Add((orchid, growth, TotalItems(cave, PetalDef) - before));
        }
        Check(harvested.Count > 0 && harvested.Sum(entry => entry.Yield) > 0, $"Harvesting real moon orchids produced petals ({string.Join(", ", harvested.Select(h => $"{h.Yield} from growth {h.Before:0.00}"))})");
        var first = harvested.First().Plant;
        // Growth restarts at 0.7 and may already have ticked up slightly; the spring's own regrowth can only add orchids.
        Check(harvested.All(entry => entry.Plant.Spawned && !entry.Plant.Destroyed && entry.Plant.Growth >= 0.7f && entry.Plant.Growth < 0.75f && !entry.Plant.HarvestableNow)
            && cave.listerThings.ThingsOfDef(OrchidDef).Count >= orchids,
            "Each harvested orchid survived at growth 0.7, above the cavern-lighting threshold, and no orchid was removed "
            + $"(growth {string.Join(", ", harvested.Select(h => $"{h.Plant.Growth:0.0000} spawned={h.Plant.Spawned}"))}; orchids {orchids} -> {cave.listerThings.ThingsOfDef(OrchidDef).Count})");
        var designator = new Designator_PlantsHarvest();
        Check(!designator.CanDesignateThing(first).Accepted && cave.designationManager.DesignationOn(first, DesignationDefOf.HarvestPlant) == null,
            "A freshly harvested orchid cannot be designated again until it regrows");
        Check(PetalDef.EverHaulable && PetalDef.EverStorable(false) && PetalDef.thingCategories.Contains(ThingCategoryDefOf.PlantMatter),
            "Petals are haulable and stockpilable plant matter");
        int harvestedPetals = TotalItems(cave, PetalDef) - petals0;

        // 5. Nourishment declines with game time into hunger and starvation (auto-feeding off; the petals are forbidden).
        ForbidAllPetals(cave);
        int t0 = Now;
        int moon0 = Snap(moon).NourishmentTicks, liquor0 = Snap(liquor).NourishmentTicks;
        TickUntil(() => Snap(moon).Level >= GuNourishmentLevel.Hungry, 2 * Day, "The Moonlight Gu became hungry");
        int hungryAt = Now - t0;
        Check(Snap(moon).NourishmentTicks == Math.Max(0, moon0 - hungryAt) && Snap(moon).NourishmentTicks <= moon.Species.feeding.HungryTicks,
            $"Moonlight Gu nourishment fell by exactly the elapsed {hungryAt} ticks to the hunger threshold");
        TickUntil(() => Snap(moon).Level >= GuNourishmentLevel.Starving, 2 * Day, "The Moonlight Gu ran out of nourishment and is starving");
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod + 1);
        int elapsed = Now - t0;
        var moonStarving = Snap(moon);
        Check(moonStarving.NourishmentTicks == 0 && moonStarving.StarvingTicks == elapsed - moon0 && moonStarving.Level == GuNourishmentLevel.Starving
            && ms.notifiedLevel == GuNourishmentLevel.Starving,
            $"After {Days(elapsed)} the Moonlight Gu has starved for exactly {Days(moonStarving.StarvingTicks)} and the player was warned");
        Check(Snap(liquor).NourishmentTicks == Math.Max(0, liquor0 - elapsed) && Snap(liquor).Level >= GuNourishmentLevel.Hungry,
            $"The Liquor Worm declined by the same elapsed time ({Days(Snap(liquor).NourishmentTicks)}, {Snap(liquor).Level})");
        Check(SustenanceComponent.Intents.Count == 0 && disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName,
            "With auto-feeding off nothing was fed");
        // A starving (not yet withering) Gu still serves.
        SetEssence(aperture, aperture.MaxEssence);
        Check(GuAbilityService.CanCast(disciple, moon.Id, Moonblade, out _, out string starvingCast) || starvingCast.Contains("recovering"),
            $"A starving Moonlight Gu can still act ({starvingCast ?? "Moonblade ready"})");

        // 6. Bounded regrowth: the harvested orchid grows back and can be harvested again.
        float regrown = first.Growth;
        TickUntil(() => first.HarvestableNow, 3 * Day, "The harvested orchid regrew far enough to harvest again");
        Report($"Harvest: {harvestedPetals} petals from {harvested.Count} orchids; an orchid regrew from 0.70 to harvestable in {Days(Now - t0)} (growth rate {first.GrowthRate:0.00}).");
        Check(designator.CanDesignateThing(first).Accepted, "The regrown orchid accepts a new harvest designation");
        SustainColonists();
        // The starving Gu has now had a little longer.
        var ms0 = (ms.MealsEaten, ms.FeedRevision);

        // 7. Missing, forbidden and unreachable food are refused without consuming anything.
        // Haulers may have carried a fresh stack while the others were forbidden; forbid every stack now.
        bool AllForbidden() => AvailablePetals(disciple) == 0 && TotalItems(cave, PetalDef) == cave.listerThings.ThingsOfDef(PetalDef).Sum(t => t.stackCount);
        for (int attempt = 0; attempt < 30 && !AllForbidden(); attempt++) { ForbidAllPetals(cave); if (!AllForbidden()) Find.TickManager.DoSingleTick(); }
        Check(AllForbidden(), "Every petal on the map is forbidden and none is carried: "
            + string.Join("; ", cave.listerThings.ThingsOfDef(PetalDef).Select(t => $"{t.stackCount} at {t.Position} forbidden={t.IsForbidden(disciple)} holder={t.ParentHolder}"))
            + " | held: " + string.Join("; ", cave.mapPawns.AllPawnsSpawned.Where(p => p.carryTracker?.CarriedThing?.def == PetalDef || p.inventory?.innerContainer.Any(t => t.def == PetalDef) == true).Select(p => $"{p.LabelShort} job {p.CurJobDef?.defName}")));
        view.Refresh(disciple);
        moonEntry = view.Gu.Single(entry => entry.Record == moon);
        Check(moonEntry.FeedAction is { Available: false } && moonEntry.FeedAction.DisabledReason.Contains("moon orchid petals"),
            $"With every petal forbidden the Feed action is refused ({moonEntry.FeedAction?.DisabledReason})");
        var sealedCell = EnclosedCell(cave, disciple.Position, out var walls);
        var sealedPetals = SpawnItemAt(cave, PetalDef, 4, sealedCell);
        cave.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
        TickUntil(() => false, 3); // let regions and rooms rebuild around the new walls
        view.Refresh(disciple);
        moonEntry = view.Gu.Single(entry => entry.Record == moon);
        int totalPetals = TotalItems(cave, PetalDef);
        Check(moonEntry.FeedAction.Available && !moonEntry.FeedAction.Execute(out blocked) && blocked.Contains("available to")
            && TotalItems(cave, PetalDef) == totalPetals && sealedPetals.stackCount == 4 && SustenanceComponent.Intents.Count == 0,
            $"Petals behind walls are counted but cannot be reached; the order is refused and nothing is consumed ({blocked})");
        foreach (var wall in walls) wall.Destroy(DestroyMode.Vanish);
        sealedPetals.Destroy(DestroyMode.Vanish);
        cave.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
        TickUntil(() => false, 3);
        foreach (var stack in cave.listerThings.ThingsOfDef(PetalDef)) stack.SetForbidden(false, false);
        totalPetals = TotalItems(cave, PetalDef);

        // 8. An interrupted feeding keeps every petal and changes nothing.
        view.Refresh(disciple);
        var feed = view.Gu.Single(entry => entry.Record == moon).FeedAction;
        var beforeInterrupt = Snap(moon);
        Check(feed.Available && feed.Execute(out feedFailure), feedFailure ?? "feeding ordered");
        Check(disciple.CurJobDef?.defName == GuSustenance.FeedJobDefName && SustenanceComponent.Intents.Count == 0 && disciple.jobs.curDriver is JobDriver_GuFeedGu { GuId: var fedId } && fedId == moon.Id,
            $"The feeding job started for this exact Moonlight Gu and took its intent (job {disciple.CurJobDef?.defName}, intents {SustenanceComponent.Intents.Count}, "
            + $"driver Gu {(disciple.jobs.curDriver as JobDriver_GuFeedGu)?.GuId}, queued {disciple.jobs.jobQueue.Count})");
        TickUntil(() => disciple.carryTracker.CarriedThing?.def == PetalDef, 3000, "The disciple fetched petals by hand");
        disciple.drafter.Drafted = true;
        TickUntil(() => false, 2);
        disciple.drafter.Drafted = false;
        Check(TotalItems(cave, PetalDef) == totalPetals && disciple.carryTracker.CarriedThing == null && (ms.MealsEaten, ms.FeedRevision) == ms0
            && Snap(moon).StarvingTicks >= beforeInterrupt.StarvingTicks,
            "Drafting before the meal kept every petal (dropped, not destroyed) and fed nothing");

        // 9. Feed the Moonlight Gu: petals consumed exactly at the meal, nourishment exact.
        SpawnItem(cave, PetalDef, 8, disciple.Position, 4);
        totalPetals = TotalItems(cave, PetalDef);
        view.Refresh(disciple);
        feed = view.Gu.Single(entry => entry.Record == moon).FeedAction;
        Check(feed.Available && feed.Execute(out feedFailure), feedFailure ?? "feeding ordered");
        TickUntil(() => disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName, 4000, "The feeding job finished");
        int meals = ms.MealsEaten - ms0.MealsEaten;
        Check(meals >= 1 && TotalItems(cave, PetalDef) == totalPetals - 2 * meals && ms.FeedRevision == ms0.FeedRevision + 1 && ms.LastFoodDefName == PetalDef.defName
            && Snap(moon).StarvingTicks == 0 && Snap(moon).NourishmentTicks == Math.Min(moon.Species.feeding.MaxTicks, meals * Day / 2) - (Now - ms.LastFedTick),
            $"The Moonlight Gu ate {meals} meal(s): exactly {2 * meals} petals were consumed, starvation ended and nourishment is exact ({Days(Snap(moon).NourishmentTicks)})");

        // 10. Feed the Liquor Worm with beer from the fallback menu; beer is consumed exactly once.
        SpawnItem(cave, ThingDefOf.Beer, 2, disciple.Position, 4);
        int beer = TotalItems(cave, ThingDefOf.Beer);
        var liquorBefore = Snap(liquor);
        int liquorTick = Now, liquorMeals = ls.MealsEaten;
        var option = GuApertureGizmos.BuildOptions(disciple).Single(o => o.Label == $"Feed {GuApertureActions.Describe(liquor)}");
        Check(option.action != null, $"The fallback menu offers the same Feed action ({option.Label})");
        option.action();
        TickUntil(() => disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName, 4000, "The Liquor Worm feeding finished");
        int beerMeals = ls.MealsEaten - liquorMeals;
        int expectedLiquor = Math.Min(liquor.Species.feeding.MaxTicks, Math.Max(0, liquorBefore.NourishmentTicks - (ls.LastFedTick - liquorTick)) + beerMeals * 2 * Day) - (Now - ls.LastFedTick);
        Check(beerMeals >= 1 && ls.ItemsEaten == beerMeals && TotalItems(cave, ThingDefOf.Beer) == beer - beerMeals && ls.LastFoodDefName == "Beer"
            && Snap(liquor).NourishmentTicks == expectedLiquor && Snap(liquor).Level == GuNourishmentLevel.Sated,
            $"{beerMeals} beer(s) drunk, one per meal, each worth two days of nourishment ({Days(Snap(liquor).NourishmentTicks)})");

        // 11. Storing and withdrawing never reset nourishment or history.
        view.Refresh(disciple);
        var beforeStore = Snap(moon);
        var history = (ms.MealsEaten, ms.LastFedTick, ms.FeedRevision, ms.ItemsEaten);
        var withdraw = view.Gu.Single(entry => entry.Record == moon).Actions.Single(action => action.Kind == GuApertureActionKind.Withdraw);
        Check(withdraw.Execute(out string withdrawFailure), withdrawFailure ?? "withdrawn");
        Check(ReferenceEquals(moon.GetState<GuNourishment>(), ms) && Snap(moon).NourishmentTicks == beforeStore.NourishmentTicks && Snap(moon).Mode == GuSustenanceMode.Advancing
            && (ms.MealsEaten, ms.LastFedTick, ms.FeedRevision, ms.ItemsEaten) == history, "Withdrawal kept the same nourishment, history and hunger");
        TickUntil(() => false, 2500);
        Check(Snap(moon).NourishmentTicks == beforeStore.NourishmentTicks - 2500, "A carried dormant Gu keeps getting hungry");
        view.Refresh(disciple);
        var store = view.Gu.Single(entry => entry.Record == moon).Actions.Single(action => action.Kind == GuApertureActionKind.Store);
        int carried = Snap(moon).NourishmentTicks;
        Check(store.Execute(out string storeFailure) && Snap(moon).NourishmentTicks == carried && (ms.MealsEaten, ms.LastFedTick, ms.FeedRevision, ms.ItemsEaten) == history,
            storeFailure ?? "Storing it again kept the same nourishment and history");

        // 12. Cavern to surface and to another cultivator: one individual, one state, no reset.
        SustainColonists();
        VerifyTravelAndTransfer(disciple, aperture, liquor, cave, home);

        // 13. Luring with beer is independent of feeding.
        int mature = spring.MatureCountFor(LiquorSpecies);
        var lure = ThingMaker.MakeThing(ThingDefOf.Beer);
        Check(mature == 1 && disciple.inventory.innerContainer.TryAdd(lure, false), $"One mature Liquor Worm remains to lure ({mature})");
        beer = TotalItems(cave, ThingDefOf.Beer);
        var liquorLure = (Snap(liquor).NourishmentTicks, ls.MealsEaten, ls.FeedRevision, Tick: Now);
        SustainColonists();
        Check(disciple.IsColonistPlayerControlled && disciple.Map == cave && !disciple.Downed,
            $"The disciple can act at the spring (controlled {disciple.IsColonistPlayerControlled}, downed {disciple.Downed}, mental {disciple.MentalStateDef?.defName}, drafted {disciple.Drafted}, map {disciple.Map}, food {disciple.needs?.food?.CurLevelPercentage:P0})");
        known = registry.Records.ToHashSet();
        Order(disciple, springThing, "Lure Liquor Worm with beer");
        TickUntil(() => registry.Records.Count > known.Count, 3000, "The lure drew a wild Liquor Worm");
        var second = registry.Records.Single(record => !known.Contains(record));
        Check(TotalItems(cave, ThingDefOf.Beer) == beer - 1 && spring.MatureCountFor(LiquorSpecies) == mature - 1 && second.GetState<GuNourishment>() == null
            && Snap(second).Mode == GuSustenanceMode.Wild && ls.MealsEaten == liquorLure.MealsEaten && ls.FeedRevision == liquorLure.FeedRevision
            && Snap(liquor).NourishmentTicks == liquorLure.NourishmentTicks - (Now - liquorLure.Tick),
            "The lure consumed one beer and one adult from the cohort; it fed neither the lured Gu nor the stored Liquor Worm");
        CaptureWild(disciple, second, "Capture Liquor Worm (unrefined)");
        var secondThing = (Thing_GuDormantProjection)registry.ProjectionOf(second);
        Order(disciple, secondThing, $"Pick up {secondThing.Label}");
        int capturedAt = Now;
        TickUntil(() => secondThing.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, "Picked up the lured Liquor Worm");
        TickUntil(() => second.GetState<GuNourishment>() != null, GuSustenanceComponent.SweepPeriod + 1, "The scheduler took the captured Gu into care");
        var ss = second.GetState<GuNourishment>();
        int initial = second.Species.feeding.InitialTicks;
        Check(ss.MealsEaten == 0 && !ss.FromLegacySave && Snap(second).NourishmentTicks <= initial && Snap(second).NourishmentTicks >= initial - (Now - capturedAt),
            $"The captured Liquor Worm starts at the species' initial nourishment ({Days(Snap(second).NourishmentTicks)}), not a beer meal");

        // 14. Routine feeding. Alpha 5B replaced the routine feeding job with background provisioning: the hungry Liquor Worm
        // drinks the beer its Gu Master carries without any job, while the ordered job continues uninterrupted.
        Drain(liquor, Snap(liquor).NourishmentTicks - Day / 2);
        var carriedBeer = ThingMaker.MakeThing(ThingDefOf.Beer);
        Check(disciple.inventory.innerContainer.TryAdd(carriedBeer, false), "The Gu Master carries one beer");
        int beerBefore = TotalItems(cave, ThingDefOf.Beer);
        component.SetAutoTend(disciple, true);
        var waitJob = JobMaker.MakeJob(JobDefOf.Wait, 6000);
        Check(disciple.jobs.TryTakeOrderedJob(waitJob, JobTag.Misc), "Ordered a wait");
        int autoMeals = ls.MealsEaten;
        bool fedByJob = false;
        TickUntil(() => { fedByJob |= disciple.CurJobDef?.defName == GuSustenance.FeedJobDefName; return ls.MealsEaten > autoMeals; },
            GuSustenanceComponent.SweepPeriod + 10, "Background provisioning fed the hungry Liquor Worm");
        Check(!fedByJob && disciple.CurJob == waitJob && TotalItems(cave, ThingDefOf.Beer) == beerBefore - (ls.MealsEaten - autoMeals)
            && ls.LastFeedSource == "inventory", "Routine feeding used the carried beer without a job and did not interrupt the ordered job");
        disciple.jobs.EndCurrentJob(JobCondition.InterruptForced);
        component.SetAutoTend(disciple, false);
        Check(Snap(liquor).Level == GuNourishmentLevel.Sated, $"Routine feeding restored the Liquor Worm ({Days(Snap(liquor).NourishmentTicks)})");

        // 15. Withering blocks techniques at the ability service; feeding restores them.
        SetEssence(aperture, aperture.MaxEssence);
        Drain(moon, Snap(moon).NourishmentTicks + moon.Species.feeding.ImpairTicks);
        Drain(liquor, Snap(liquor).NourishmentTicks + liquor.Species.feeding.ImpairTicks);
        Check(Snap(moon).Level == GuNourishmentLevel.Withering && Snap(liquor).Level == GuNourishmentLevel.Withering, "Both stored Gu are withering");
        var target = SpawnTarget(cave, disciple.Position, 5, 10);
        Check(!GuAbilityService.CanCast(disciple, moon.Id, Moonblade, out _, out string castReason) && castReason.Contains("withering")
            && !GuAbilityService.TryOrderCast(disciple, moon.Id, Moonblade, target, out _)
            && !registry.IsAbilityGrantedBy(moon.Id, disciple, out _, out _),
            $"Moonblade is refused by the ability service ({castReason})");
        var command = AbilityGizmos(disciple).Single(gizmo => gizmo.Ability == Moonblade);
        Check(command.Disabled && command.disabledReason.Contains("withering"), $"The native command stays visible but disabled with the reason ({command.disabledReason})");
        Check(!GuChannelService.CanChannel(disciple, liquor.Id, LiquorRefinement, out _, out _, out string channelReason) && channelReason.Contains("withering"),
            $"Liquor Worm refinement is refused ({channelReason})");
        view.Refresh(disciple);
        var channel = view.ChannelActions.SingleOrDefault();
        Check(channel is { Available: false } && channel.DisabledReason.Contains("withering"), $"The Cultivation page explains it ({channel?.DisabledReason})");
        Check(moon.Refinement?.CultivatorId != null && liquor.Refinement?.CultivatorId != null && registry.ApertureOccupants(aperture).Count() == 2,
            "Starvation did not break allegiance or custody");
        SpawnItem(cave, PetalDef, 8, disciple.Position, 4);
        view.Refresh(disciple);
        Check(view.Gu.Single(entry => entry.Record == moon).FeedAction.Execute(out feedFailure), feedFailure ?? "feeding");
        TickUntil(() => Snap(moon).Level == GuNourishmentLevel.Sated, 4000, "Feeding ended the withering");
        Check(GuAbilityService.CanCast(disciple, moon.Id, Moonblade, out _, out castReason) || castReason.Contains("recovering"),
            $"A fed Moonlight Gu can act again ({castReason ?? "ready"})");
        DespawnTarget(target);
        SpawnItem(cave, ThingDefOf.Beer, 2, disciple.Position, 4);
        view.Refresh(disciple);
        Check(view.Gu.Single(entry => entry.Record == liquor).FeedAction.Execute(out feedFailure), feedFailure ?? "feeding");
        TickUntil(() => Snap(liquor).Level == GuNourishmentLevel.Sated, 4000, "The Liquor Worm was fed back to health");

        // 16. Save: Liquor Worm sated, the carried Liquor Worm hungry, the Moonlight Gu starving with a feeding job in progress.
        Drain(second, Snap(second).NourishmentTicks - Day / 2);
        Drain(moon, Snap(moon).NourishmentTicks + Day / 2);
        foreach (var stack in cave.listerThings.ThingsOfDef(PetalDef).ToList()) stack.Destroy(DestroyMode.Vanish);
        foreach (var held in disciple.inventory.innerContainer.Where(thing => thing.def == PetalDef).ToList()) held.Destroy(DestroyMode.Vanish);
        SpawnItem(cave, PetalDef, 6, disciple.Position, 6);
        view.Refresh(disciple);
        int moonMeals = ms.MealsEaten;
        Check(view.Gu.Single(entry => entry.Record == moon).FeedAction.Execute(out feedFailure), feedFailure ?? "feeding");
        var driver = (JobDriver_GuFeedGu)disciple.jobs.curDriver;
        TickUntil(() => disciple.carryTracker.CarriedThing?.def == PetalDef && driver.ticksLeftThisToil > 0 && driver.ticksLeftThisToil < JobDriver_GuFeedGu.FeedingTicks - 20,
            3000, "The disciple is part way through offering the petals");
        Check(Snap(liquor).Level == GuNourishmentLevel.Sated && Snap(second).Level == GuNourishmentLevel.Hungry && Snap(moon).Level == GuNourishmentLevel.Starving,
            "At the save: one sated, one hungry and one starving Gu");
        var state = new Alpha5AState(Cultivators.FindCultivator(disciple).CultivatorId, liquor.Id, moon.Id, second.Id, hope.Id, Now,
            new[] { Values(liquor), Values(moon), Values(second) }, TotalItems(cave, PetalDef), TotalItems(cave, ThingDefOf.Beer) + TotalItems(home, ThingDefOf.Beer),
            registry.Records.Count, moon.Id, disciple.CurJob.count, moonMeals, Array.Empty<string>(), Find.LetterStack.LettersListForReading.Count);
        File.WriteAllText(Alpha5AStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(Alpha5AFeedingSave);
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved with a feeding job in progress ({disciple.CurJob.count} petals in hand): Liquor Worm {state.Values[0].Level}, carried Liquor Worm {state.Values[2].Level}, Moonlight Gu {state.Values[1].Level}.");
        AssertNoErrors("alpha5a-feeding");
    }

    private static void VerifyTravelAndTransfer(Pawn disciple, Hediff_Aperture aperture, GuRecord liquor, Map cave, Map home)
    {
        var registry = IndividualRegistry;
        string feedFailure = null;
        var ls = liquor.GetState<GuNourishment>();
        var history = (ls.MealsEaten, ls.LastFedTick, ls.FeedRevision, ls.ItemsEaten);
        Check(registry.TryWithdrawDormant(liquor.Id, liquor.CustodyRevision, disciple, aperture, out var dormant, out string reason), reason ?? "withdrawn");
        int carried = Snap(liquor).NourishmentTicks;
        TravelTo(disciple, home);
        StepAway(disciple);
        Check(disciple.Map == home && dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings() && ReferenceEquals(liquor.GetState<GuNourishment>(), ls)
            && Snap(liquor).NourishmentTicks == carried && Snap(liquor).Mode == GuSustenanceMode.Advancing,
            "Climbing to the surface kept the carried Liquor Worm's nourishment exactly; hunger continues on the new map");
        var beerOption = liquor.Species.feeding.foods.Single();
        int surfaceBeer = TotalItems(home, ThingDefOf.Beer), caveBeer = TotalItems(cave, ThingDefOf.Beer);
        Check(liquor.Species.feeding.Worker.CountAvailable(disciple, beerOption) == home.listerThings.ThingsOfDef(ThingDefOf.Beer).Where(t => !t.IsForbidden(disciple)).Sum(t => t.stackCount)
            + disciple.inventory.innerContainer.Where(t => t.def == ThingDefOf.Beer).Sum(t => t.stackCount),
            $"On the surface only surface and carried beer is available ({surfaceBeer} here, {caveBeer} left in the cavern)");

        Check(disciple.inventory.innerContainer.TryDrop(dormant, disciple.Position, home, ThingPlaceMode.Near, out var dropped) && dropped == dormant
            && registry.Resolve(dormant) == liquor && Snap(liquor).Mode == GuSustenanceMode.Advancing, "Dropped on the surface, the Gu is still the same individual");
        // Another cultivator takes the Gu: the elder when able to act, otherwise any other capable colonist.
        bool Capable(Pawn p) => p != disciple && p.IsColonistPlayerControlled && !p.Downed && !p.InMentalState && !p.Drafted;
        Pawn elder = Clan(GuClanRole.Elder).FirstOrDefault(Capable) ?? PawnsFinder.AllMaps_FreeColonistsSpawned.Where(Capable).OrderByDescending(p => Apertures(p).Count).First();
        Report($"Custody transfer to {elder.LabelShort} ({GuClanRoles.RoleOf(elder)}).");
        TravelTo(elder, home);
        StepAway(elder);
        Order(elder, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == elder.inventory.GetDirectlyHeldThings(), 3000, "The elder picked up the disciple's Liquor Worm");
        Check((ls.MealsEaten, ls.LastFedTick, ls.FeedRevision, ls.ItemsEaten) == history && ReferenceEquals(liquor.GetState<GuNourishment>(), ls),
            "Passing to another cultivator kept the feeding history");
        SpawnItem(home, ThingDefOf.Beer, 1, elder.Position, 4);
        Drain(liquor, 2 * Day); // room for a meal; the drain changes no history
        Check((ls.MealsEaten, ls.LastFedTick, ls.FeedRevision, ls.ItemsEaten) == history, "Draining changed no history");
        var feed = GuApertureActions.Feed(elder, IndividualRegistry, liquor);
        Check(feed is { Available: true } && feed.Execute(out feedFailure), feedFailure ?? feed?.DisabledReason ?? "The elder can feed the carried Liquor Worm");
        TickUntil(() => elder.CurJobDef?.defName != GuSustenance.FeedJobDefName, 4000, "The elder's feeding finished");
        Check(ls.MealsEaten == history.MealsEaten + 1 && ls.LastFoodDefName == "Beer", "The elder fed it surface beer; the history records the meal");
        history = (ls.MealsEaten, ls.LastFedTick, ls.FeedRevision, ls.ItemsEaten);
        Check(elder.inventory.innerContainer.TryDrop(dormant, elder.Position, home, ThingPlaceMode.Near, out _), "The elder set it down");
        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 3000, "The disciple took it back");
        TravelTo(disciple, cave);
        int back = Snap(liquor).NourishmentTicks;
        Check(registry.TryStoreDormant(dormant, dormant.GuRevision, disciple, aperture, out reason), reason ?? "stored");
        Check(Snap(liquor).NourishmentTicks == back && (ls.MealsEaten, ls.LastFedTick, ls.FeedRevision, ls.ItemsEaten) == history
            && registry.ApertureOccupants(aperture).Contains(liquor), "Back underground and stored, it kept everything");
    }

    // Stage "reload": identical values after a fresh process; the in-progress feeding completes once; two Gu starve.
    private static void VerifyAlpha5AReload()
    {
        var (state, disciple, aperture, registry) = LoadAlpha5AState();
        var liquor = registry.Resolve(state.LiquorId);
        var moon = registry.Resolve(state.MoonId);
        var second = registry.Resolve(state.SecondId);
        Map cave = disciple.Map;
        Check(Now == state.Tick && registry.Records.Count == state.Records, "The save loaded at the same tick with the same records");
        foreach (var expected in state.Values)
        {
            var actual = Values(registry.Resolve(expected.Id));
            Check(actual == expected, $"Gu {expected.Id} has identical nourishment and history after reload ({actual} vs {expected})");
        }
        Check(disciple.jobs.curDriver is JobDriver_GuFeedGu driver && driver.GuId == state.MoonId && disciple.CurJob.count == state.FeedingCount
            && disciple.carryTracker.CarriedThing?.def == PetalDef && SustenanceComponent.Intents.Count == 0,
            "The feeding job resumed for the same Moonlight Gu with the petals still in hand");
        int petals = TotalItems(cave, PetalDef);
        var ms = moon.GetState<GuNourishment>();
        TickUntil(() => disciple.CurJobDef?.defName != GuSustenance.FeedJobDefName, 600, "The resumed feeding finished");
        int meals = ms.MealsEaten - state.MoonMealsBefore;
        Check(meals >= 1 && TotalItems(cave, PetalDef) == petals - 2 * meals && ms.FeedRevision == state.Values[1].FeedRevision + 1 && Snap(moon).StarvingTicks == 0,
            $"The meal was committed once after reload: {2 * meals} petals consumed, starvation ended");

        // Terminal starvation of a carried Gu and of a stored Gu, through the terminal authority, each exactly once.
        int letters = Find.LetterStack.LettersListForReading.Count;
        int claims = registry.ApertureClaimCount(aperture);
        var secondThing = registry.ProjectionOf(second);
        Drain(second, Snap(second).NourishmentTicks + second.Species.feeding.TerminalTicks);
        TickUntil(() => second.Existence != GuExistence.Living, GuSustenanceComponent.SweepPeriod + 1, "The scheduler ended the starved, carried Liquor Worm");
        Check(second.TerminalOutcome == GuTerminalOutcome.Died && second.EndEventKey == GuIndividualRegistry.StarvationEventKey(second.Id)
            && secondThing.Destroyed && registry.ProjectionOf(second) == null && registry.Resolve(second.Id) == null && registry.FindRecord(second.Id) == second
            && second.History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1 && registry.Records.Count == state.Records,
            "The carried Gu died of starvation once: a tombstone keeps its identity, its dormant body is gone, no record was removed");
        Drain(moon, Snap(moon).NourishmentTicks + moon.Species.feeding.TerminalTicks);
        TickUntil(() => moon.Existence != GuExistence.Living, GuSustenanceComponent.SweepPeriod + 1, "The scheduler ended the starved Moonlight Gu");
        Check(moon.TerminalOutcome == GuTerminalOutcome.Died && registry.ApertureClaimCount(aperture) == claims - 1
            && registry.ApertureOccupants(aperture).Single() == liquor && !GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == moon),
            "The stored Gu died once and its aperture place was freed; the Liquor Worm is untouched");
        Check(!registry.TryEndLifeByStarvation(moon, "again", out string again) && again.Contains("already"), $"A second starvation death is refused ({again})");
        Check(Find.LetterStack.LettersListForReading.Count == letters + 2, "Each death was reported with one letter");
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod * 2);
        Check(Find.LetterStack.LettersListForReading.Count == letters + 2 && moon.History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1,
            "Later sweeps produce no further deaths");
        File.WriteAllText(Alpha5AStatePath, JsonSerializer.Serialize(state with
        {
            Tick = Now, Values = new[] { Values(liquor) }, Petals = TotalItems(cave, PetalDef), Starved = new[] { second.Id, moon.Id },
            Letters = Find.LetterStack.LettersListForReading.Count
        }));
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha5a-reload");
    }

    // Stage "final": no duplicate death after another fresh process; scheduler cost.
    private static void VerifyAlpha5AFinal()
    {
        var (state, disciple, aperture, registry) = LoadAlpha5AState();
        Map cave = disciple.Map;
        var liquor = registry.Resolve(state.LiquorId);
        Check(registry.Records.Count == state.Records && state.Starved.All(id => registry.FindRecord(id) is { Existence: GuExistence.Terminal, TerminalOutcome: GuTerminalOutcome.Died } dead
                && dead.History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1 && registry.ProjectionOf(dead) == null),
            "After another fresh process both starved Gu are still single tombstones with one terminal event");
        Check(Values(liquor) == state.Values[0] && registry.ApertureOccupants(aperture).Single() == liquor && GuAbilityService.GrantsFor(disciple).Count() == 1,
            "The surviving Liquor Worm kept its nourishment and still grants its technique");
        var c = SustenanceComponent;
        c.ResetMeasurements();
        TickUntil(() => false, GuSustenanceComponent.SweepPeriod * 4);
        Check(Find.LetterStack.LettersListForReading.Count == state.Letters && state.Starved.All(id => registry.FindRecord(id).History.Count(entry => entry.Kind == GuLifecycleEventKind.Terminal) == 1),
            "No death is replayed after loading");
        double perCall = c.SweepCalls == 0 ? 0 : c.SweepTicks * 1e6 / Stopwatch.Frequency / c.SweepCalls;
        double maxCall = c.MaxSweepTicks * 1e6 / Stopwatch.Frequency;
        // Per-visit cost of the real scheduler path on a real record (mode observation, settle and level), 20,000 visits.
        int now = Now;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 20_000; i++) c.Visit(registry, liquor, now);
        double perVisit = (Stopwatch.GetTimestamp() - start) * 1e6 / Stopwatch.Frequency / 20_000;
        Report($"Scheduler: {c.SweepCalls} sweeps over {GuSustenanceComponent.SweepPeriod * 4} ticks, {c.RecordsVisited} visits, {perCall:0.0} µs per sweep, max {maxCall:0.0} µs; "
            + $"{perVisit:0.00} µs per individual visit, so 10,000 simulated Gu cost about {perVisit * 10_000 / 1000:0.0} ms per in-game hour, spread over ten sweeps.");
        Check(perVisit < 20, $"Visiting one individual costs {perVisit:0.00} µs");
        File.WriteAllText(Alpha5AStatePath, JsonSerializer.Serialize(state with
        {
            Tick = Now, Petals = TotalItems(cave, PetalDef),
            Beer = TotalItems(cave, ThingDefOf.Beer) + TotalItems(Find.Maps.Single(map => map.IsPlayerHome), ThingDefOf.Beer)
        }));
        GameDataSaveLoader.SaveGame(Alpha5ALegacySource);
        AssertNoErrors("alpha5a-final");
    }

    // Stage "legacy": the final save with every Alpha 5A element removed loads like an older save.
    private static void VerifyAlpha5ALegacy()
    {
        var (state, disciple, aperture, registry) = LoadAlpha5AState();
        var c = SustenanceComponent;
        var liquor = registry.Resolve(state.LiquorId);
        Map cave = disciple.Map;
        Check(c.ActivatedOnLegacySave && c.ActivatedTick == Now && registry.Records.All(record => record.GetState<GuNourishment>() == null),
            "The older save has no feeding data; feeding activated on load");
        Check(TotalItems(cave, PetalDef) == state.Petals
            && TotalItems(cave, ThingDefOf.Beer) + TotalItems(Find.Maps.Single(map => map.IsPlayerHome), ThingDefOf.Beer) == state.Beer,
            "No food was created or removed by activating feeding");
        var preview = Snap(liquor);
        Check(preview.NourishmentTicks == liquor.Species.feeding.MaxTicks && preview.InGrace && preview.Level == GuNourishmentLevel.Sated, "Existing Gu read as full and in their grace period");
        TickUntil(() => liquor.GetState<GuNourishment>() != null, GuSustenanceComponent.SweepPeriod + 1, "The scheduler initialized the existing Gu");
        var ls = liquor.GetState<GuNourishment>();
        Check(ls.FromLegacySave && ls.NourishmentTicks == liquor.Species.feeding.MaxTicks && ls.GraceUntilTick == ls.SettledTick + liquor.Species.feeding.LegacyGraceTicks
            && state.Starved.All(id => registry.FindRecord(id).GetState<GuNourishment>() == null),
            "The living Liquor Worm starts full with a three-day grace period; ended records get no state");
        TickUntil(() => false, Day / 2);
        Check(Snap(liquor).NourishmentTicks == liquor.Species.feeding.MaxTicks && Snap(liquor).Level == GuNourishmentLevel.Sated,
            $"No hunger during the grace period ({Snap(liquor).NourishmentTicks}/{liquor.Species.feeding.MaxTicks}, {Snap(liquor).Level}, mode {Snap(liquor).Mode})");
        Check(registry.Records.Count == state.Records && registry.ApertureOccupants(aperture).Single() == liquor,
            $"Nothing was duplicated or lost (records {registry.Records.Count}/{state.Records})");
        Check(GuAbilityService.GrantsFor(disciple).Count() == 1 || disciple.Downed || !disciple.Spawned,
            $"The Liquor Worm still grants its technique (grants {GuAbilityService.GrantsFor(disciple).Count()}, downed {disciple.Downed}, spawned {disciple.Spawned}, map {disciple.Map})");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha5a-legacy");
    }

    // ---------------------------------------------------------------------------------------------------------------
    private static (Alpha5AState, Pawn, Hediff_Aperture, GuIndividualRegistry) LoadAlpha5AState()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        DisableFixtureAutosave();
        var state = JsonSerializer.Deserialize<Alpha5AState>(File.ReadAllText(Alpha5AStatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        foreach (var colonist in PawnsFinder.AllMaps_FreeColonistsSpawned) SustenanceComponent.SetAutoTend(colonist, false);
        Current.Game.CurrentMap = disciple.Map;
        return (state, disciple, Apertures(disciple).Single(), IndividualRegistry);
    }

    /// <summary>
    /// The fixture ticks several in-game days inside one frame. The native autosaver queues a long event each day, which
    /// cannot run until the frame ends and meanwhile (correctly) blocks custody operations; in play it runs on the next frame.
    /// </summary>
    /// It also disables mood mental breaks, which several simulated days in a cavern otherwise produce at random: a pawn in a
    /// mental state ignores orders and forbidden items, which would make the fixture nondeterministic.
    private static void DisableFixtureAutosave()
    {
        SustainColonists();
        Prefs.AutosaveIntervalDays = 1000f;
        DebugSettings.enableRandomMentalStates = false;
        foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned) pawn.MentalState?.RecoverFromState();
    }

    /// <summary>Orders a native capture and retries the order if the walk ends without a capture (a wandering wild Gu).</summary>
    private static bool CaptureWild(Pawn pawn, GuRecord record, string label, bool required = true)
    {
        var registry = IndividualRegistry;
        for (int attempt = 1; attempt <= 8 && registry.ProjectionOf(record) is not Thing_GuDormantProjection; attempt++)
        {
            // Capture needs an open cell under the Gu (pre-existing rule); in the planted basin it may have to wander first.
            if (attempt > 1) TickUntil(() => false, 300);
            Order(pawn, registry.ProjectionOf(record), label);
            TickUntil(() => registry.ProjectionOf(record) is Thing_GuDormantProjection || pawn.CurJobDef?.defName != "GU_CaptureWildGu", 4000);
            TickUntil(() => registry.ProjectionOf(record) is Thing_GuDormantProjection, 30);
            if (attempt > 1) Report($"Capture of {record.SpeciesDefName} needed attempt {attempt}.");
        }
        if (registry.ProjectionOf(record) is Pawn_GuWildProjection roaming)
        {
            // Fallback: stand beside the wild Gu and call the same capture authority the job calls, reporting its reason.
            var map = roaming.Map;
            var beside = GenAdj.CellsAdjacentCardinal(roaming).FirstOrDefault(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null);
            pawn.jobs.StopAll();
            pawn.pather.StopDead();
            roaming.pather?.StopDead();
            if (beside.IsValid) { pawn.Position = beside; pawn.Notify_Teleported(); }
            bool captured = registry.TryCaptureWild(roaming, roaming.GuRevision, pawn, roaming.Position, out _, out string why);
            Report($"Capture of {record.SpeciesDefName} by job failed eight times; direct capture beside it: {captured} ({why}).");
        }
        var wild = registry.ProjectionOf(record);
        if (required) Check(wild is Thing_GuDormantProjection, $"Captured {record.SpeciesDefName} (form {wild?.GetType().Name}, at {wild?.Position}, pawn at {pawn.Position}, job {pawn.CurJobDef?.defName})");
        return wild is Thing_GuDormantProjection;
    }

    /// <summary>Puts every petal on the ground (haulers may be carrying some) and forbids all of it.</summary>
    private static void ForbidAllPetals(Map map)
    {
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (pawn.carryTracker?.CarriedThing?.def == PetalDef) { pawn.jobs.StopAll(); pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _); }
            foreach (var held in pawn.inventory?.innerContainer.Where(thing => thing.def == PetalDef).ToList() ?? new List<Thing>())
                pawn.inventory.innerContainer.TryDrop(held, pawn.Position, map, ThingPlaceMode.Near, out _);
        }
        foreach (var stack in map.listerThings.ThingsOfDef(PetalDef)) stack.SetForbidden(true, false);
    }

    /// <summary>Fixture environment: the colony's own food, rest and mood are topped up so days of simulation in a cavern
    /// (without the player tending the colony) cannot down or break the pawns under test. Gu nourishment is not touched.</summary>
    private static void SustainColonists()
    {
        foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
        {
            foreach (var need in new Need[] { pawn.needs?.food, pawn.needs?.rest, pawn.needs?.joy, pawn.needs?.mood })
                if (need != null) need.CurLevelPercentage = 1f;
            pawn.MentalState?.RecoverFromState();
            var malnutrition = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Malnutrition);
            if (malnutrition != null) pawn.health.RemoveHediff(malnutrition);
        }
    }

    private static int Now => Find.TickManager.TicksGame;

    private static GuNourishmentSnapshot Snap(GuRecord record) => GuSustenance.Snapshot(IndividualRegistry, record);

    private static NourishmentValues Values(GuRecord record)
    {
        var snapshot = Snap(record);
        var state = record.GetState<GuNourishment>();
        return new NourishmentValues(record.Id, snapshot.NourishmentTicks, snapshot.StarvingTicks, state?.MealsEaten ?? -1, state?.ItemsEaten ?? -1,
            state?.LastFedTick ?? -1, state?.FeedRevision ?? -1, snapshot.Level.ToString(), snapshot.Mode.ToString());
    }

    /// <summary>
    /// Test fixture: advances one individual's hunger clock by <paramref name="ticks"/> without ticking the world, standing in
    /// for days of play. It changes only the nourishment numbers; everything that reacts to them is the real code.
    /// </summary>
    private static void Drain(GuRecord record, int ticks)
    {
        var state = GuSustenance.Settle(IndividualRegistry, record, Now);
        Check(state != null && ticks >= 0, $"Gu {record.Id} has a nourishment state to drain");
        long remaining = (long)state.nourishmentTicks - ticks;
        if (remaining < 0)
        {
            state.starvingTicks += (int)Math.Min(-remaining, int.MaxValue / 4);
            state.nourishmentTicks = 0;
        }
        else state.nourishmentTicks = (int)remaining;
    }

    private static string Days(int ticks) => ITab_Pawn_Aperture.NourishmentDays(ticks);

    private static int AvailablePetals(Pawn pawn) =>
        pawn.Map.listerThings.ThingsOfDef(PetalDef).Where(thing => !thing.IsForbidden(pawn)).Sum(thing => thing.stackCount)
        + pawn.inventory.innerContainer.Where(thing => thing.def == PetalDef).Sum(thing => thing.stackCount);

    private static int TotalItems(Map map, ThingDef def) => map.listerThings.ThingsOfDef(def).Sum(thing => thing.stackCount)
        + map.mapPawns.AllPawnsSpawned.Sum(pawn => (pawn.inventory?.innerContainer.Where(thing => thing.def == def).Sum(thing => thing.stackCount) ?? 0)
            + (pawn.carryTracker?.CarriedThing is { } carried && carried.def == def ? carried.stackCount : 0));

    private static Thing SpawnItemAt(Map map, ThingDef def, int count, IntVec3 cell)
    {
        var thing = ThingMaker.MakeThing(def);
        thing.stackCount = count;
        return GenSpawn.Spawn(thing, cell, map);
    }

    /// <summary>An open cell whose eight neighbours are walled off for the test, so items placed there cannot be reached.</summary>
    private static IntVec3 EnclosedCell(Map map, IntVec3 near, out List<Thing> walls)
    {
        walls = new List<Thing>();
        var cell = GenRadial.RadialCellsAround(near, 14, true).First(candidate => candidate.DistanceTo(near) >= 6 && Open(candidate)
            && GenAdj.AdjacentCells.All(offset => Open(candidate + offset)));
        foreach (var offset in GenAdj.AdjacentCells)
            walls.Add(GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), cell + offset, map));
        return cell;
        bool Open(IntVec3 c) => c.InBounds(map) && c.Standable(map) && c.GetThingList(map).Count == 0 && !c.Fogged(map) && c.GetFirstPawn(map) == null;
    }

    /// <summary>Moves a pawn a few cells away from the passage so nothing it drops blocks the landing cell.</summary>
    private static void StepAway(Pawn pawn)
    {
        var map = pawn.Map;
        var cell = GenRadial.RadialCellsAround(pawn.Position, 9, true).First(c => c.DistanceTo(pawn.Position) >= 5 && c.InBounds(map) && c.Standable(map)
            && c.GetThingList(map).Count == 0 && !c.Fogged(map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly));
        pawn.jobs.StopAll();
        pawn.pather.StopDead();
        pawn.Position = cell;
        pawn.Notify_Teleported();
    }

    private static void TravelTo(Pawn pawn, Map target)
    {
        if (pawn.Map == target) return;
        var endpoint = pawn.Map.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().First();
        string reason = null;
        // The landing cell can be briefly occupied by another pawn walking past; wait for it to clear like a player would.
        for (int attempt = 0; attempt < 40 && pawn.Map != target; attempt++)
        {
            pawn.jobs.StopAll();
            if (JobDriver_GuPocketProbeTravel.TryTravel(pawn, endpoint, out reason) && pawn.Map == target) break;
            TickUntil(() => false, 30);
        }
        Check(pawn.Map == target, $"{pawn.LabelShort} travelled through the passage. {reason}");
    }
}
