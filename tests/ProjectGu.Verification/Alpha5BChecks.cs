using System.Xml.Linq;
using ProjectGu.Botany;
using ProjectGu.Sustenance;
using RimWorld;
using Verse;

/// <summary>
/// Managed checks of Alpha 5B against the real assembly and XML: the moon orchid's plant-framework declaration and the
/// preserved Alpha 5A harvest bounds; the framework's configuration validation (no unlimited harvest or seed gathering);
/// petal and seed passage cargo; the provisioning sources and their order; and source guards for the rules that matter
/// most: background feeding creates no job and consumes only after revalidation, cultivation never restores Rest, and the
/// plant hooks only touch Project GU plants. Native fixtures cover the behaviour in game.
/// </summary>
internal static class Alpha5BChecks
{
    private static int passed;

    internal static void Run(string root)
    {
        VerifyOrchid(root);
        VerifyPlantConfiguration();
        VerifyCargoAndSources(root);
        VerifySources(root);
        Console.WriteLine($"PASS: Alpha 5B checks ({passed}).");
    }

    private static void VerifyOrchid(string root)
    {
        var doc = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_BotanicalPlants.xml"));
        var orchid = doc.Descendants("ThingDef").Single(node => (string)node.Element("defName")! == "GU_MoonOrchid");
        var plant = orchid.Element("plant")!;
        Check((float)plant.Element("harvestYield")! == 6f && (float)plant.Element("harvestAfterGrowth")! == 0.7f
            && (float)plant.Element("harvestMinGrowth")! == 0.85f && (string)plant.Element("harvestTag")! == "Standard"
            && plant.Element("sowTags")!.Elements("li").Any(li => li.Value == "Ground"),
            "Alpha 5A orchid harvest is preserved: 6 petals, growth back to 0.7, regrow past 0.85, native Harvest designation and sowing");
        var spec = orchid.Element("comps")!.Elements("li").Single(li => (string?)li.Attribute("Class") == "ProjectGu.Botany.CompProperties_GuPlant");
        Check((string)spec.Element("sowingMaterial")! == "GU_MoonOrchidSeeds" && (int)spec.Element("sowingMaterialCount")! == 1
            && spec.Element("seedCollection") != null && (int)spec.Element("seedCollection")!.Element("yield")! >= 1,
            "Moon orchids are seed-gated (one seed per plant) and give seeds from living plants");
        Check(orchid.Element("comps")!.Elements("li").Any(li => (string?)li.Attribute("Class") == "ProjectGu.Springs.CompProperties_GuSpiritFlora"),
            "The orchid keeps its cavern ecological support component");
        var grass = doc.Descendants("ThingDef").Single(node => (string)node.Element("defName")! == "GU_SpiritGrass");
        Check(grass.Element("comps")!.Elements("li").All(li => (string?)li.Attribute("Class") != "ProjectGu.Botany.CompProperties_GuPlant"),
            "Spirit grass is not given seed gating by accident");
        var seeds = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_MoonOrchidSeeds.xml")).Descendants("ThingDef").Single();
        Check((string)seeds.Element("defName")! == "GU_MoonOrchidSeeds" && seeds.Element("thingCategories")!.Elements("li").Any(),
            "Moon orchid seeds are an ordinary stockpilable item");
    }

    private static void VerifyPlantConfiguration()
    {
        var item = new ThingDef { defName = "TestSeeds", label = "test seeds", category = ThingCategory.Item, stackLimit = 75 };
        ThingDef Plant(float after, float min, bool sowable = true) => new()
        {
            defName = "TestPlant", label = "test plant", category = ThingCategory.Plant,
            plant = new PlantProperties
            {
                harvestYield = 6, harvestedThingDef = item, harvestAfterGrowth = after, harvestMinGrowth = min,
                sowTags = sowable ? new List<string> { "Ground" } : new List<string>()
            }
        };
        CompProperties_GuPlant Spec() => new()
        {
            sowingMaterial = item, sowingMaterialCount = 1,
            seedCollection = new GuPlantSeedCollection { yield = 2, work = 300 }
        };
        Check(!Spec().ConfigErrors(Plant(0.7f, 0.85f)).Any(), "A valid seed-gated plant reports no errors");
        Check(Spec().ConfigErrors(Plant(0.9f, 0.85f)).Any(e => e.Contains("harvestAfterGrowth")),
            "A repeat harvest that keeps more growth than it needs (unlimited harvesting) is rejected");
        var unlimitedSeeds = Spec();
        unlimitedSeeds.seedCollection.growthAfter = 0.9f;
        Check(unlimitedSeeds.ConfigErrors(Plant(0.7f, 0.85f)).Any(e => e.Contains("growthAfter")), "Unlimited seed gathering is rejected");
        var killing = Spec();
        killing.seedCollection.growthAfter = 0f;
        Check(killing.ConfigErrors(Plant(0.7f, 0.85f)).Any(e => e.Contains("growthAfter")), "Seed gathering that would kill the plant is rejected");
        Check(Spec().ConfigErrors(Plant(0.7f, 0.85f, sowable: false)).Any(e => e.Contains("sowTags")), "Planting material on an unsowable plant is rejected");
        var wild = Spec();
        wild.cultivable = false;
        Check(wild.ConfigErrors(Plant(0.7f, 0.85f)).Any(e => e.Contains("wild-only")), "A wild-only plant may not declare planting material");
        var badExtra = Spec();
        badExtra.byproducts.Add(new GuPlantByproduct { thingDef = item, count = 1, chance = 1.5f });
        Check(badExtra.ConfigErrors(Plant(0.7f, 0.85f)).Any(e => e.Contains("byproduct")), "Byproduct chances must be in (0, 1]");
        Check(Spec().SeedGated && Spec().SeedMinGrowth(Plant(0.7f, 0.85f)) == 0.85f && Spec().SeedGrowthAfter(Plant(0.7f, 0.85f)) == 0.7f,
            "Seed gathering defaults to the same flowering cycle as the harvest");
        Check(GuPlantFramework.ExternalSeedSystems.All(id => id == id.ToLowerInvariant()), "External seed system IDs are normalised");
    }

    private static void VerifyCargoAndSources(string root)
    {
        var site = XDocument.Load(Path.Combine(root, "Defs/MapGeneration/GU_SpiritSpringCavern.xml")).Descendants("ProjectGu.Underground.GuPocketSiteDef").Single();
        var cargo = site.Element("haulingCargo")!.Elements("li").Select(li => li.Value).ToList();
        Check(cargo.SequenceEqual(new[] { "GU_PrimevalStone", "GU_MoonOrchidPetals", "GU_MoonOrchidSeeds" }),
            $"Passage haulers carry primeval stone, moon orchid petals and seeds ({string.Join(", ", cargo)})");
        var sources = XDocument.Load(Path.Combine(root, "Defs/GuProvisionSourceDefs/GU_ProvisionSources.xml")).Descendants("ProjectGu.Sustenance.GuProvisionSourceDef")
            .OrderBy(node => (int)node.Element("order")!).Select(node => (string)node.Element("workerClass")!).ToList();
        Check(sources.SequenceEqual(new[] { typeof(GuProvisionSource_CultivatorInventory).FullName, typeof(GuProvisionSource_ColonyStockpile).FullName }),
            "Automatic feeding draws from the cultivator's inventory, then the colony's stockpiles");
        Check(new GuProvisionSourceDef { defName = "x", workerClass = typeof(string) }.ConfigErrors().Any(e => e.Contains("workerClass")),
            "A provisioning source must have a GuProvisionSource worker (the Storage Gu extension point)");
    }

    private static void VerifySources(string root)
    {
        string Read(string path) => File.ReadAllText(Path.Combine(root, path));
        string provisioning = Read("Source/ProjectGu/Sustenance/GuProvisioning.cs");
        Check(!provisioning.Contains("JobMaker", StringComparison.Ordinal) && !provisioning.Contains("TryTakeOrderedJob", StringComparison.Ordinal)
            && !provisioning.Contains("StartJob", StringComparison.Ordinal),
            "Background provisioning creates no job: the Gu Master is never interrupted or moved");
        int revalidate = provisioning.IndexOf("Worker.Revalidate(cultivator, supply.Thing)", StringComparison.Ordinal);
        int destroy = provisioning.IndexOf("option.Consume(", StringComparison.Ordinal);
        int feed = provisioning.IndexOf("GuNourishmentRules.Feed(", StringComparison.Ordinal);
        Check(revalidate > 0 && destroy > 0 && feed > destroy && provisioning.Split("option.Consume(").Length == 2,
            "Provisioning revalidates every planned stack, consumes in one place, then applies the meal");
        Check(provisioning.Contains("IsInValidStorage", StringComparison.Ordinal) && provisioning.Contains("IsForbidden(Faction.OfPlayer)", StringComparison.Ordinal)
            && provisioning.Contains("CanReserveStack", StringComparison.Ordinal) && provisioning.Contains("CanReach(", StringComparison.Ordinal)
            && provisioning.Contains("thing.Map != cultivator.Map", StringComparison.Ordinal),
            "Stockpile supplies must be in valid storage, unforbidden, unreserved, reachable and on the cultivator's own map");
        string scheduler = Read("Source/ProjectGu/Sustenance/GuSustenanceComponent.cs");
        Check(scheduler.Contains("state.Mode == GuSustenanceMode.Advancing", StringComparison.Ordinal) && scheduler.Contains("GuProvisioning.TryProvision", StringComparison.Ordinal)
            && !File.Exists(Path.Combine(root, "Source/ProjectGu/Sustenance/JobGiver_GuTendOwnGu.cs"))
            && !File.Exists(Path.Combine(root, "Defs/ThinkTreeDefs/GU_GuTending.xml")),
            "Automatic feeding runs from the bounded scheduler only for advancing (on-map, in-care) Gu; the job-based routine is gone");
        foreach (string path in new[] { "Source/ProjectGu/Jobs/JobDriver_GuNourishAperture.cs", "Source/ProjectGu/Jobs/JobDriver_GuRefineMortalGu.cs",
                     "Source/ProjectGu/Hediff_Aperture.Cultivation.cs", "Source/ProjectGu/Combat/JobDriver_GuChannelAbility.cs" })
        {
            string text = Read(path);
            Check(!text.Contains("TickResting", StringComparison.Ordinal) && !text.Contains("asleep", StringComparison.Ordinal)
                && !text.Contains("needs.rest", StringComparison.Ordinal) && !text.Contains("Need_Rest", StringComparison.Ordinal),
                $"{Path.GetFileName(path)} never restores or replaces the Rest need (cultivation is not sleep)");
        }
        string patches = Read("Source/ProjectGu/Botany/GuPlantPatches.cs") + Read("Source/ProjectGu/Botany/GuPlantFramework.cs");
        Check(patches.Contains("IsGuPlant(plant.def)", StringComparison.Ordinal) && patches.Contains("Props(plantDef) is not { } props) return job;", StringComparison.Ordinal),
            "The selection and sowing hooks return immediately for plants without the Project GU plant specification");
        string sow = Read("Source/ProjectGu/Botany/JobDriver_GuSowWithMaterial.cs");
        int consume = sow.IndexOf("TryConsumeMaterial(out", StringComparison.Ordinal), grow = sow.IndexOf("plant.Growth = 0.0001f", StringComparison.Ordinal);
        Check(consume > 0 && grow > consume && sow.Split("spent.Destroy").Length == 2 && sow.Contains("if (!committed", StringComparison.Ordinal),
            "Seed-gated sowing spends material in one place, in the completing tick, and removes an unpaid half-sown plant");
        string capture = Read("Source/ProjectGu/GuIndividualRegistry.Dormancy.cs");
        Check(capture.Contains("thing.def.category == ThingCategory.Plant", StringComparison.Ordinal) && capture.Contains("CaptureDestination", StringComparison.Ordinal),
            "Capture accepts plant-covered ground and falls back to an adjacent open cell");
        string wild = Read("Source/ProjectGu/Pawn_GuWildProjection.cs");
        Check(wild.Contains("if (def == null || def != permittedRace)", StringComparison.Ordinal), "The wild projection's PostMake guard is unchanged");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        passed++;
    }
}
