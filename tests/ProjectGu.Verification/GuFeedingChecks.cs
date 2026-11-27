using System.Diagnostics;
using System.Xml.Linq;
using ProjectGu.Domain;
using Verse;

/// <summary>
/// Managed checks of Alpha 5A feeding against the real assembly: species policies as declared in XML, the moon orchid's
/// bounded non-destructive harvest and its sustainability estimate, nourishment arithmetic (settling, suspension, grace,
/// starvation, meals, terminal threshold), configuration validation, and the cost of settling many individuals. Native
/// fixtures cover jobs, items, custody, save and load in game.
/// </summary>
internal static class GuFeedingChecks
{
    private static int passed;
    private const int Day = GuNourishmentRules.TicksPerDay;

    internal static void Run(string root)
    {
        var species = VerifySpeciesPolicies(root);
        VerifyOrchidHarvest(root, species);
        VerifyArithmetic();
        VerifyConfiguration();
        VerifyBenchmark();
        VerifySources(root);
        Console.WriteLine($"PASS: Alpha 5A Gu feeding checks ({passed}).");
    }

    private static Dictionary<string, XElement> VerifySpeciesPolicies(string root)
    {
        var defs = XDocument.Load(Path.Combine(root, "Defs/GuSpeciesDefs/GU_GuSpecies.xml")).Descendants("ProjectGu.Domain.GuSpeciesDef")
            .ToDictionary(node => (string)node.Element("defName")!);
        XElement Feeding(string defName) => defs[defName].Element("feeding")!;

        var moon = Feeding("GU_MoonlightGu");
        var petals = moon.Element("foods")!.Elements("li").Single();
        Check((string)moon.Element("status")! == "Simulated" && (string)petals.Element("thingDef")! == "GU_MoonOrchidPetals"
            && (int)petals.Element("mealCount")! == 2 && (float)petals.Element("daysPerMeal")! == 0.5f,
            "Moonlight Gu eats moon orchid petals, two per meal, each meal half a day: four petals a day as in the lore");
        var liquor = Feeding("GU_LiquorWorm");
        var beer = liquor.Element("foods")!.Elements("li").Single();
        Check((string)liquor.Element("status")! == "Simulated" && (string)beer.Element("thingDef")! == "Beer"
            && (int)beer.Element("mealCount")! == 1 && (float)beer.Element("daysPerMeal")! == 2f && beer.Element("note") != null,
            "Liquor Worm drinks beer, one bottle per meal sustaining two days, marked as a substitute for wine");
        Check(defs["GU_LiquorWorm"].Element("encounterLures")!.Elements("li").Single().Value == "Beer",
            "The separate beer lure is unchanged");
        var hope = Feeding("GU_HopeGu");
        Check((string)hope.Element("status")! == "NotImplemented" && hope.Element("foods") == null && hope.Element("note") != null,
            "Hope Gu explicitly has no implemented feeding policy and no invented diet");
        var dev = Feeding("GU_DevTestSpringGu");
        Check((string)dev.Element("status")! == "Exempt" && dev.Element("foods") == null && !string.IsNullOrEmpty((string)dev.Element("note")),
            "The developer species is explicitly exempt, with the exemption declared");
        foreach (string name in new[] { "GU_MoonlightGu", "GU_LiquorWorm" })
        {
            var f = Feeding(name);
            float terminal = (float)f.Element("terminalAfterStarvingDays")!, impair = (float)f.Element("impairAfterStarvingDays")!;
            float max = (float)f.Element("maxDays")!, meal = (float)f.Element("foods")!.Elements("li").Max(li => (float)li.Element("daysPerMeal")!);
            Check(terminal >= 7 && impair >= 1 && impair < terminal && max >= meal,
                $"{name}: starvation is forgiving ({impair} days to wither, {terminal} days to die after stores run out); a meal fits in storage");
        }
        return defs;
    }

    private static void VerifyOrchidHarvest(string root, Dictionary<string, XElement> species)
    {
        var things = Directory.EnumerateFiles(Path.Combine(root, "Defs/ThingDefs"), "*.xml").Select(XDocument.Load)
            .SelectMany(document => document.Descendants("ThingDef")).Where(node => node.Element("defName") != null)
            .ToDictionary(node => (string)node.Element("defName")!);
        var plant = things["GU_MoonOrchid"].Element("plant")!;
        float min = (float)plant.Element("harvestMinGrowth")!, after = (float)plant.Element("harvestAfterGrowth")!;
        float yield = (float)plant.Element("harvestYield")!, growDays = (float)plant.Element("growDays")!;
        Check((string)plant.Element("harvestedThingDef")! == "GU_MoonOrchidPetals" && (string)plant.Element("harvestTag")! == "Standard",
            "Moon orchids are harvested for petals through the native harvest designation");
        Check(after > 0f && after < min, $"Harvest keeps the plant (regrowth {after}) and it must regrow past {min} before the next harvest");
        Check(after >= 0.65f, "A harvested wild orchid stays above the 0.65 growth that lights the cavern");
        var petals = things["GU_MoonOrchidPetals"];
        Check(petals.Element("thingCategories")!.Elements("li").Any(li => li.Value == "PlantMatter") && (string)petals.Attribute("ParentName")! == "ResourceBase",
            "Petals are an ordinary haulable, stockpilable resource");

        // Balance estimate (not lore): plants grow 55% of the day (native resting), full growth gives full yield.
        float daysPerCycle = (1f - after) * growDays / 0.55f;
        float perOrchidPerDay = yield / daysPerCycle;
        var spring = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_SpiritSpring.xml"));
        int orchids = spring.Descendants("botanicalCavernFlora").Single().Elements("li")
            .Where(li => li.Element("flora")!.Value == "GU_MoonOrchid").Sum(li => (int)li.Element("targetPatches")!);
        float need = 4f; // petals per Moonlight Gu per day
        float supported = orchids * perOrchidPerDay * 0.75f / need; // 25% margin for failed harvests and lower growth
        Check(supported >= 1f, $"The cavern's {orchids} wild orchids sustain {supported:0.0} Moonlight Gu with a 25% margin ({perOrchidPerDay:0.00} petals per orchid per day)");
        Check(species["GU_MoonlightGu"].Element("habitatTags")!.Elements("li").Any(li => li.Value == "moon_orchid"), "Moonlight Gu remains a moon-orchid species");
    }

    private static GuFeedingProperties Props(float maxDays = 2f, float hungry = 0.5f, float initial = 1.5f, float grace = 3f, float impair = 2f, float terminal = 10f) => new()
    {
        status = GuFeedingStatus.Simulated,
        foods = new() { new GuFoodOption { thingDef = new ThingDef { defName = "TestPetals", label = "test petals", category = ThingCategory.Item, stackLimit = 75 }, mealCount = 2, daysPerMeal = 0.5f } },
        maxDays = maxDays,
        hungryBelowDays = hungry,
        initialDays = initial,
        legacyGraceDays = grace,
        impairAfterStarvingDays = impair,
        terminalAfterStarvingDays = terminal
    };

    private static void VerifyArithmetic()
    {
        var props = Props();
        var option = props.foods[0];
        var fresh = GuNourishmentRules.Create(props, 1000, legacy: false);
        Check(fresh.NourishmentTicks == (int)(1.5f * Day) && fresh.GraceUntilTick == -1 && !fresh.FromLegacySave && fresh.SettledTick == 1000,
            "An individual first taken into care starts at the species' initial nourishment");
        var legacy = GuNourishmentRules.Create(props, 1000, legacy: true);
        Check(legacy.NourishmentTicks == props.MaxTicks && legacy.GraceUntilTick == 1000 + 3 * Day && legacy.FromLegacySave,
            "A Gu from an older save starts full with a three-day grace period");

        // Settling is additive and idempotent: one big step equals many small ones, and repeating a tick changes nothing.
        var a = GuNourishmentRules.Create(props, 0, false);
        var b = GuNourishmentRules.Create(props, 0, false);
        GuNourishmentRules.Settle(a, 0, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(b, 0, GuSustenanceMode.Advancing);
        for (int t = 2500; t <= 5 * Day; t += 2500) GuNourishmentRules.Settle(a, t, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(b, 5 * Day, GuSustenanceMode.Advancing);
        Check(a.NourishmentTicks == b.NourishmentTicks && a.StarvingTicks == b.StarvingTicks && a.StarvingTicks == (int)(3.5f * Day) && a.NourishmentTicks == 0,
            $"Settling in many steps equals one step ({a.StarvingTicks} vs {b.StarvingTicks} starving ticks)");
        int before = a.StarvingTicks;
        GuNourishmentRules.Settle(a, 5 * Day, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(a, 5 * Day - 100, GuSustenanceMode.Advancing);
        Check(a.StarvingTicks == before && a.SettledTick == 5 * Day, "Re-settling the same or an earlier tick (as after a reload) deducts nothing again");

        // Projection is read-only.
        var p = GuNourishmentRules.Create(props, 0, false);
        var projected = GuNourishmentRules.Project(p, Day, true);
        Check(projected.Nourishment == (int)(0.5f * Day) && p.NourishmentTicks == (int)(1.5f * Day) && p.SettledTick == 0, "Projection does not change the state");

        // Suspension: an interval observed suspended at either end costs nothing and restores nothing.
        var s = GuNourishmentRules.Create(props, 0, false);
        GuNourishmentRules.Settle(s, 0, GuSustenanceMode.OffMap);
        GuNourishmentRules.Settle(s, 3 * Day, GuSustenanceMode.OffMap);
        Check(s.NourishmentTicks == (int)(1.5f * Day) && s.StarvingTicks == 0, "Off-map time neither starves nor feeds");
        GuNourishmentRules.Settle(s, 3 * Day + 2500, GuSustenanceMode.Advancing);
        Check(s.NourishmentTicks == (int)(1.5f * Day), "Returning to a map starts hunger from the observed return, not retroactively");
        GuNourishmentRules.Settle(s, 3 * Day + 5000, GuSustenanceMode.Advancing);
        Check(s.NourishmentTicks == (int)(1.5f * Day) - 2500, "Hunger then advances normally");

        // Grace period: no hunger before it ends, normal hunger after.
        var g = GuNourishmentRules.Create(props, 0, true);
        GuNourishmentRules.Settle(g, 2 * Day, GuSustenanceMode.Advancing);
        Check(g.NourishmentTicks == props.MaxTicks && g.GraceUntilTick == 3 * Day, "No hunger during the grace period");
        GuNourishmentRules.Settle(g, 4 * Day, GuSustenanceMode.Advancing);
        Check(g.NourishmentTicks == props.MaxTicks - Day && g.GraceUntilTick == -1, "Hunger counts only the time after the grace period");

        // Levels and thresholds.
        Check(GuNourishmentRules.LevelOf(props, props.MaxTicks, 0) == GuNourishmentLevel.Sated
            && GuNourishmentRules.LevelOf(props, props.HungryTicks + 1, 0) == GuNourishmentLevel.Sated
            && GuNourishmentRules.LevelOf(props, props.HungryTicks, 0) == GuNourishmentLevel.Hungry
            && GuNourishmentRules.LevelOf(props, 1, 0) == GuNourishmentLevel.Hungry
            && GuNourishmentRules.LevelOf(props, 0, 0) == GuNourishmentLevel.Starving
            && GuNourishmentRules.LevelOf(props, 0, props.ImpairTicks - 1) == GuNourishmentLevel.Starving
            && GuNourishmentRules.LevelOf(props, 0, props.ImpairTicks) == GuNourishmentLevel.Withering,
            "Sated, hungry, starving and withering follow the configured thresholds");
        Check(!GuNourishmentRules.IsTerminal(props, 0, props.TerminalTicks - 1) && GuNourishmentRules.IsTerminal(props, 0, props.TerminalTicks)
            && !GuNourishmentRules.IsTerminal(props, 1, props.TerminalTicks * 2), "Death comes only after the full starvation period, and never while any food remains");
        Check(GuNourishmentRules.LevelOf(null, 0, 0) == GuNourishmentLevel.Unconfigured
            && GuNourishmentRules.LevelOf(new GuFeedingProperties { status = GuFeedingStatus.Exempt, note = "x" }, 0, 99999999) == GuNourishmentLevel.Exempt,
            "Unconfigured and exempt species never become hungry");
        var missed = GuNourishmentRules.Create(props, 0, false);
        GuNourishmentRules.Settle(missed, 0, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(missed, (int)(1.5f * Day) + Day, GuSustenanceMode.Advancing);
        Check(GuNourishmentRules.LevelOf(props, missed.NourishmentTicks, missed.StarvingTicks) == GuNourishmentLevel.Starving
            && !GuNourishmentRules.IsTerminal(props, missed.NourishmentTicks, missed.StarvingTicks), "Missing a day of meals starves the Gu but does not kill it");

        // Meals: whole meals that fit (half a meal of room suffices), capped storage, starvation ends.
        Check(GuNourishmentRules.MealsWanted(props, option, props.MaxTicks) == 0
            && GuNourishmentRules.MealsWanted(props, option, props.MaxTicks - option.TicksPerMeal / 2 + 1) == 0
            && GuNourishmentRules.MealsWanted(props, option, props.MaxTicks - option.TicksPerMeal / 2) == 1
            && GuNourishmentRules.MealsWanted(props, option, 0) == 4, "A sated Gu refuses food; an empty one takes four half-day meals");
        var f = GuNourishmentRules.Create(props, 0, false);
        GuNourishmentRules.Settle(f, 0, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(f, 6 * Day, GuSustenanceMode.Advancing);
        int revision = f.FeedRevision;
        GuNourishmentRules.Feed(f, props, option, 3, 6 * Day);
        Check(f.NourishmentTicks == (int)(1.5f * Day) && f.StarvingTicks == 0 && f.MealsEaten == 3 && f.ItemsEaten == 6
            && f.FeedRevision == revision + 1 && f.LastFedTick == 6 * Day && f.LastFoodDefName == "TestPetals",
            "Feeding three meals restores a day and a half, ends starvation and records the meal history");
        GuNourishmentRules.Feed(f, props, option, 5, 6 * Day);
        Check(f.NourishmentTicks == props.MaxTicks, "Storage never exceeds the species maximum");

        // A huge elapsed interval settles in one step without overflow.
        var big = GuNourishmentRules.Create(props, 0, false);
        GuNourishmentRules.Settle(big, 0, GuSustenanceMode.Advancing);
        GuNourishmentRules.Settle(big, int.MaxValue - 10, GuSustenanceMode.Advancing);
        Check(big.NourishmentTicks == 0 && big.StarvingTicks > 0 && big.StarvingTicks <= int.MaxValue / 2, "A very long interval settles once, without overflow");
    }

    private static void VerifyConfiguration()
    {
        Check(!Props().ConfigErrors("ok").Any(), "A valid simulated policy reports no errors");
        Check(Props(terminal: 1f).ConfigErrors("x").Any(error => error.Contains("terminalAfterStarvingDays")), "A death sooner than three days of starvation is rejected");
        Check(Props(impair: 12f).ConfigErrors("x").Any(error => error.Contains("impairAfterStarvingDays")), "Withering must come before death");
        Check(Props(hungry: 3f).ConfigErrors("x").Any(error => error.Contains("hungryBelowDays")), "The hunger threshold must be within storage");
        var empty = Props();
        empty.foods.Clear();
        Check(empty.ConfigErrors("x").Any(error => error.Contains("at least one food")), "A simulated policy without food is rejected");
        Check(new GuFeedingProperties { status = GuFeedingStatus.Exempt }.ConfigErrors("x").Any(error => error.Contains("declare why")),
            "An exemption without a declared reason is rejected");
        Check(!new GuFeedingProperties().ConfigErrors("x").Any() && new GuFeedingProperties().status == GuFeedingStatus.NotImplemented,
            "A missing policy means not implemented, never a default diet");
        var badWorker = Props();
        badWorker.workerClass = typeof(string);
        Check(badWorker.ConfigErrors("x").Any(error => error.Contains("workerClass")), "The feeding worker must derive from GuFeedingWorker");
        Check(Props().Worker is GuFeedingWorker_Items items && items.props != null, "The default worker plans physical item meals");
    }

    private static void VerifyBenchmark()
    {
        // Settling cost per individual: the scheduler's per-record arithmetic for 10,000 Gu, as one full period would do.
        var props = Props();
        var states = new GuNourishment[10_000];
        for (int i = 0; i < states.Length; i++) states[i] = GuNourishmentRules.Create(props, 0, i % 3 == 0);
        for (int i = 0; i < states.Length; i++) GuNourishmentRules.Settle(states[i], 1, GuSustenanceMode.Advancing); // warm up
        var times = new List<double>();
        for (int round = 1; round <= 9; round++)
        {
            long start = Stopwatch.GetTimestamp();
            int now = round * 2500;
            for (int i = 0; i < states.Length; i++)
                GuNourishmentRules.Settle(states[i], now, i % 7 == 0 ? GuSustenanceMode.OffMap : GuSustenanceMode.Advancing);
            times.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }
        times.Sort();
        double median = times[times.Count / 2];
        Console.WriteLine($"INFO: settling 10,000 Gu takes {median:0.###} ms (median of 9); the live scheduler spreads this over ten calls per in-game hour.");
        Check(median < 20.0, $"Settling 10,000 individuals is cheap ({median:0.###} ms)");
    }

    private static void VerifySources(string root)
    {
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        string abilities = Read("Source/ProjectGu/GuIndividualRegistry.Abilities.cs");
        Check(abilities.Contains("StarvationImpairment(found) != null", StringComparison.Ordinal),
            "Starvation impairment is enforced at the authoritative ability-authorization boundary");
        string driver = Read("Source/ProjectGu/Jobs/JobDriver_GuFeedGu.cs");
        Check(driver.Split("TryCommitFeeding").Length == 2 && !driver.Contains("Destroy(", StringComparison.Ordinal),
            "The feeding job consumes food only through the single commitment call");
        string sustenance = Read("Source/ProjectGu/Sustenance/GuSustenance.cs");
        int destroy = sustenance.IndexOf("option.Consume(", StringComparison.Ordinal), feed = sustenance.IndexOf("GuNourishmentRules.Feed(", StringComparison.Ordinal);
        Check(destroy > 0 && feed > destroy && sustenance.IndexOf("record.CustodyRevision != intent.CustodyRevision", StringComparison.Ordinal) < destroy,
            "Commitment revalidates the individual's custody revision before consuming, and applies the meal in the same step");
        string registry = Read("Source/ProjectGu/GuIndividualRegistry.Sustenance.cs");
        Check(registry.Contains("byEndEvent.TryGetValue(key", StringComparison.Ordinal) && registry.Contains("GuTerminalOutcome.Died", StringComparison.Ordinal)
            && registry.Contains("UnbindAll(record);", StringComparison.Ordinal), "Starvation death uses the terminal transaction with one idempotent event key");
        string scheduler = Read("Source/ProjectGu/Domain/GuSpeciesDef.cs") + Read("Source/ProjectGu/Sustenance/GuSustenanceComponent.cs") + sustenance
            + Read("Source/ProjectGu/Domain/GuFeeding.cs") + Read("Source/ProjectGu/Domain/GuFeedingWorker_Items.cs")
            + Read("Source/ProjectGu/Sustenance/GuProvisioning.cs") + registry;
        Check(new[] { "GU_MoonlightGu", "GU_LiquorWorm", "GU_HopeGu", "Moonlight", "Liquor", "Beer", "GU_MoonOrchid" }
                .All(name => !scheduler.Contains(name, StringComparison.Ordinal)),
            "The feeding scheduler, rules and worker contain no species or food names");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }
}
