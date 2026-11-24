using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ProjectGu.Domain;
using ProjectGu.Springs;
using Verse;

internal static class ManagedEngineChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(XDocument defs, string rimWorldPath, List<GuCavernFloraTarget> targets)
    {
        var directory = Directory.CreateTempSubdirectory("project-gu-engine-");
        const BindingFlags privateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        var mods = directory.CreateSubdirectory("Mods");
        typeof(GenFilePaths).GetField("saveDataPath", privateStatic).SetValue(null, directory.FullName);
        typeof(GenFilePaths).GetField("modsFolderPath", privateStatic).SetValue(null, mods.FullName);
        typeof(GenFilePaths).GetField("officialModsFolderPath", privateStatic).SetValue(null, Path.Combine(rimWorldPath, "Data"));
        FieldInfo prefs = typeof(Prefs).GetField("data", privateStatic);
        prefs.SetValue(null, RuntimeHelpers.GetUninitializedObject(prefs.FieldType));
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", PrivateInstance).SetValue(game, new List<Map>());
        Current.Game = game;
        try
        {
            ThingDef stone = VerifyNativeStoneStacks(defs);
            var restoredFlora = VerifySpringMigrationAndScribe(stone, targets);
            VerifyOverlappingHabitat(game, restoredFlora);
            UndergroundAddressChecks.Run(game, directory.FullName);
            CultivatorFoundationChecks.Run(game, directory.FullName);
            RefinementManagedChecks.Run(directory.FullName);
        }
        finally
        {
            Current.Game = null;
            string config = Path.Combine(directory.FullName, "Config");
            if (Directory.Exists(config))
            {
                File.Delete(Path.Combine(config, "ModsConfig.xml"));
                Directory.Delete(config);
            }
            mods.Delete();
            directory.Delete();
        }
    }

    private static ThingDef VerifyNativeStoneStacks(XDocument defs)
    {
        XElement xml = defs.Descendants("ThingDef").Single(def => (string)def.Element("defName") == "GU_PrimevalStone");
        var stone = new ThingDef
        {
            defName = "GU_PrimevalStone", thingClass = typeof(ThingWithComps), category = ThingCategory.Item,
            stackLimit = (int)xml.Element("stackLimit"), useHitPoints = true, destroyable = true
        };
        Check(stone.stackLimit == 200, "Global stone limit");
        var stack = Stone(stone, 1);
        for (int expected = 2; expected <= stone.stackLimit; expected++)
        {
            var produced = Stone(stone, 1);
            Check(CompGuSpiritSpring.CanMergeProductionBatch(stack, produced)
                && stack.TryAbsorbStack(produced, true) && stack.stackCount == expected && produced.Destroyed,
                $"Native stack grew to {expected}");
        }
        var overflow = Stone(stone, 1);
        Check(!CompGuSpiritSpring.CanMergeProductionBatch(stack, overflow)
            && !stack.TryAbsorbStack(overflow, true) && stack.stackCount == 200 && overflow.stackCount == 1,
            "Full stone stack conserves the incoming item");

        var nearlyFull = Stone(stone, 199);
        var two = Stone(stone, 2);
        Check(!CompGuSpiritSpring.CanMergeProductionBatch(nearlyFull, two)
            && !nearlyFull.TryAbsorbStack(two, true) && nearlyFull.stackCount == 200 && two.stackCount == 1,
            "Whole batch validator skips a partial merge; native absorption conserves its remainder");
        var incompatible = Stone(new ThingDef { defName = "MockOtherResource", category = ThingCategory.Item, stackLimit = 200 }, 1);
        Check(!CompGuSpiritSpring.CanMergeProductionBatch(Stone(stone, 1), incompatible), "Different resource stays separate");
        Map grid = GridMap();
        IntVec3 outlet = new(5, 0, 5);
        var placedStack = Stone(stone, 1);
        placedStack.Position = outlet;
        grid.thingGrid.ThingsListAt(outlet).Add(placedStack);
        int acknowledged = 0;
        bool placed = GenPlace.TryPlaceThing(Stone(stone, 1), outlet, grid, ThingPlaceMode.Direct,
            (physical, quantity) =>
            {
                Check(physical == placedStack, "Placement callback returned the original physical stack");
                acknowledged += quantity;
            });
        Check(placed && acknowledged == 1 && placedStack.stackCount == 2, "Native Direct merge acknowledges exactly one");
        Console.WriteLine("PASS: native Direct callback and stone absorption from 1 to 200, full and partial stacks, incompatible items.");
        return stone;
    }

    private static ThingWithComps Stone(ThingDef def, int count) => new()
    {
        def = def, stackCount = count, HitPoints = 100, thingIDNumber = 101
    };

    private static List<Building> VerifySpringMigrationAndScribe(ThingDef stoneDef, List<GuCavernFloraTarget> targets)
    {
        var props = new CompProperties_GuSpiritSpring();
        var springDef = new ThingDef
        {
            defName = "GU_SpiritSpring", thingClass = typeof(Building), category = ThingCategory.Building,
            comps = new() { props }
        };
        DefDatabase<ThingDef>.Add(springDef);
        DefDatabase<ThingDef>.Add(stoneDef);
        DefDatabase<GuSpeciesDef>.Add(new GuSpeciesDef { defName = "GU_HopeGu" });
        var flora = new List<Building>();
        foreach (var target in targets)
        {
            target.flora.thingClass = typeof(Building);
            target.flora.category = ThingCategory.Building;
            DefDatabase<ThingDef>.Add(target.flora);
            flora.Add(new Building { def = target.flora, thingIDNumber = 102 + flora.Count });
        }
        string directory = Directory.CreateTempSubdirectory("project-gu-verification-").FullName;
        try
        {
            string legacy = Path.Combine(directory, "legacy.xml");
            File.WriteAllText(legacy, """
                <verification>
                  <spring Class="Building">
                    <def>GU_SpiritSpring</def><id>GU_SpiritSpring100</id>
                    <guReserve024>43</guReserve024><guLifetimeProduced>12</guLifetimeProduced>
                    <guMatureHope024>4</guMatureHope024><guJuvenileHope024>2</guJuvenileHope024>
                    <guNextEcology024>54000</guNextEcology024><guNextProductionTick>56000</guNextProductionTick>
                  </spring>
                </verification>
                """);
            var (spring, _, _) = Load(legacy);
            var comp = spring.GetComp<CompGuSpiritSpring>();
            Check(comp.Reserve == 43 && comp.HopeGuAvailable == 4 && comp.HopeGuJuvenile == 2
                && SpringField<int>(comp, "nextFloraGrowthTick") == -1, "Legacy spring inventories, cohorts and disabled regrowth");
            string habitat = comp.HabitatId;
            string failure = (string)typeof(CompGuSpiritSpring).GetMethod("AttemptProduction", PrivateInstance)
                .Invoke(comp, null);
            Check(failure.Contains("no live map") && comp.Reserve == 43
                && SpringField<int>(comp, "lifetimeProduced") == 12 && SpringField<int>(comp, "nextProductionTick") == 56000,
                "Failed production protects the reserve, totals and timer");

            var cohorts = SpringField<List<GuPopulationRecord>>(comp, "populations");
            cohorts.Add(new GuPopulationRecord("MissingAddonSpecies", 2, 1, 78000));
            cohorts[0].AdvanceDaily(3, true, 1);
            Check(cohorts[0].MatureCount == 4 && cohorts[0].JuvenileCount == 2 && cohorts[0].Revision == 0,
                "Legacy cohort starts at revision zero and a no-op preserves it");
            cohorts[0].AdvanceDaily(17, true, 1);
            Check(cohorts[0].MatureCount == 6 && cohorts[0].JuvenileCount == 1 && cohorts[0].Revision == 1,
                "One daily population change advances the revision once");
            Check(!cohorts[0].TryConsumeMature(7) && cohorts[0].Revision == 1,
                "A rejected debit preserves the cohort revision");

            typeof(CompGuSpiritSpring).GetField("nextFloraGrowthTick", PrivateInstance).SetValue(comp, 30000);
            ThingWithComps confirmedStack = Stone(stoneDef, 200);
            string current = Path.Combine(directory, "current.xml");
            Scribe.saver.InitSaving(current, "verification");
            Scribe_Deep.Look(ref spring, "spring");
            Scribe_Deep.Look(ref confirmedStack, "stack");
            Scribe_Collections.Look(ref flora, "flora", LookMode.Deep);
            Scribe.saver.FinalizeSaving();

            var (restored, restoredStack, restoredFlora) = Load(current);
            var restoredComp = restored.GetComp<CompGuSpiritSpring>();
            var savedCohorts = SpringField<List<GuPopulationRecord>>(restoredComp, "populations");
            var savedOther = savedCohorts[1];
            Check(restoredComp.HabitatId == habitat && restoredComp.Reserve == 43 && restoredComp.HopeGuAvailable == 6
                && restoredComp.HopeGuJuvenile == 1 && SpringField<int>(restoredComp, "nextFloraGrowthTick") == 30000
                && savedCohorts[0].Revision == 1 && savedOther.Revision == 0
                && savedOther.SpeciesDefName == "MissingAddonSpecies" && savedOther.TotalCount == 3
                && restoredStack.stackCount == 200 && restoredStack.def == stoneDef
                && restoredFlora.Select(plant => plant.def).SequenceEqual(flora.Select(plant => plant.def))
                && restoredFlora.All(plant => plant.GetComp<CompGuSpiritFlora>() != null),
                "Native Scribe retains cohorts, reserve, habitat and growth schedule, and a physical stack");
            Console.WriteLine("PASS: native Scribe fixture round-trip, legacy cohort migration, Hope Gu conservation, failed production accounting.");
            return restoredFlora;
        }
        finally
        {
            // The only entries created here are the two verification fixtures.
            File.Delete(Path.Combine(directory, "legacy.xml"));
            File.Delete(Path.Combine(directory, "current.xml"));
            Directory.Delete(directory);
        }
    }

    private static (Building, ThingWithComps, List<Building>) Load(string path)
    {
        Building spring = null;
        ThingWithComps stone = null;
        List<Building> flora = null;
        Scribe.loader.InitLoading(path);
        Scribe_Deep.Look(ref spring, "spring");
        Scribe_Deep.Look(ref stone, "stack");
        Scribe_Collections.Look(ref flora, "flora", LookMode.Deep);
        Scribe.loader.FinalizeLoading();
        return (spring, stone, flora);
    }

    private static T SpringField<T>(CompGuSpiritSpring comp, string name) =>
        (T)typeof(CompGuSpiritSpring).GetField(name, PrivateInstance).GetValue(comp);

    private static void VerifyOverlappingHabitat(Game game, List<Building> restoredFlora)
    {
        var firstMap = GridMap();
        var secondMap = GridMap();
        typeof(Game).GetField("maps", PrivateInstance).SetValue(game, new List<Map> { firstMap, secondMap });
        var first = new GuEcologyMapComponent(firstMap);
        var second = new GuEcologyMapComponent(secondMap);
        firstMap.components.Add(first);
        secondMap.components.Add(second);
        IntVec3 query = new(12, 0, 12);
        var left = SpringAt(new IntVec3(10, 0, 12), id: 3, mapIndex: 0);
        var right = SpringAt(new IntVec3(14, 0, 12), id: 2, mapIndex: 0);
        var otherMap = SpringAt(query, id: 1, mapIndex: 1);
        first.Register(left);
        first.Register(right);
        second.Register(otherMap);
        var plant = restoredFlora[0];
        plant.Position = query;
        firstMap.thingGrid.ThingsListAt(query).Add(plant);
        SetMapIndex(plant, 0);
        plant.GetComp<CompGuSpiritFlora>().PostSpawnSetup(true);
        int support = plant.GetComp<CompGuSpiritFlora>().Support;
        Check(first.HabitatOwnerAt(query) == right && second.HabitatOwnerAt(query) == otherMap,
            "Stable tie break and independent map habitats");
        Check(right.HabitatSupport == support && left.HabitatSupport == 0 && otherMap.HabitatSupport == 0,
            "Each physical patch contributes its legacy weight to one spring");
        Check(first.HabitatOwnerAt(query + IntVec3.West) == left, "Closest source owns the patch");
        first.Unregister(right);
        Check(first.HabitatOwnerAt(query) == left && second.HabitatOwnerAt(query) == otherMap,
            "Removed spring releases patch ownership locally");
        Check(left.HabitatSupport == support, "Nearest remaining spring recounts the physical patch");
        firstMap.thingGrid.ThingsListAt(query).Remove(plant);
        SetMapIndex(plant, -1);
        plant.GetComp<CompGuSpiritFlora>().PostDeSpawn(firstMap);
        Check(left.HabitatSupport == 0, "Flora despawn invalidates the credited support");
        Console.WriteLine("PASS: nearest-spring assignment, stable ties, despawn reassignment, and map isolation.");
    }

    private static Map GridMap()
    {
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(21, 1, 21) };
        map.cellIndices = new CellIndices(map);
        map.thingGrid = new ThingGrid(map);
        map.components = new List<MapComponent>();
        return map;
    }

    private static CompGuSpiritSpring SpringAt(IntVec3 cell, int id, sbyte mapIndex)
    {
        var fixture = new Building { thingIDNumber = id, Position = cell };
        SetMapIndex(fixture, mapIndex);
        var spring = new CompGuSpiritSpring { parent = fixture };
        spring.Initialize(new CompProperties_GuSpiritSpring());
        return spring;
    }

    private static void SetMapIndex(Thing thing, sbyte index) =>
        typeof(Thing).GetField("mapIndexOrState", PrivateInstance).SetValue(thing, index);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
