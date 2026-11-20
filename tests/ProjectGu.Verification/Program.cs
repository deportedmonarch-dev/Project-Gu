using ProjectGu.Springs;
using ProjectGu.Underground;
using Verse;
using System.Xml.Linq;

const int samples = 5760;
var root = args[0];
var springDef = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_SpiritSpring.xml"));
var springSettings = springDef.Descendants("li")
    .Single(node => (string)node.Attribute("Class") == "ProjectGu.Springs.CompProperties_GuSpiritSpring");
int habitatRadius = (int)springSettings.Element("cavernHabitatRadius")!;
int unitsPerPoint = (int)springSettings.Element("cavernSupportUnitsPerPoint")!;
int supportCap = (int)springSettings.Element("cavernMaxFloraSupport")!;
int maximumHope = (int)springSettings.Element("maxHopeGu")!;
var terrains = new Dictionary<GuCavernGround, TerrainDef>
{
    [GuCavernGround.RichSoil] = new() { defName = "SoilRich" },
    [GuCavernGround.Marsh] = new() { defName = "MarshyTerrain" },
    [GuCavernGround.ShallowWater] = new() { defName = "WaterShallow", tags = new() { "Water" } }
};
var targets = springSettings.Element("cavernFlora")!.Elements("li").Select(node =>
{
    string defName = node.Element("flora")!.Value;
    var flora = Directory.EnumerateFiles(Path.Combine(root, "Defs/ThingDefs"), "*.xml")
        .Select(XDocument.Load).SelectMany(document => document.Descendants("ThingDef"))
        .Single(definition => (string)definition.Element("defName") == defName);
    var props = flora.Descendants("li").Single(element =>
        (string)element.Attribute("Class") == "ProjectGu.Springs.CompProperties_GuSpiritFlora");
    var species = new CompProperties_GuSpiritFlora
    {
        support = (int)props.Element("support")!,
        cavernSupportUnits = (int)props.Element("cavernSupportUnits")!,
        growthSoils = props.Element("growthSoils")!.Elements("li")
            .Select(soil => terrains.Values.Single(terrain => terrain.defName == soil.Value)).ToList(),
        waterRange = (int)props.Element("waterRange")!,
        minimumSpacing = (int?)props.Element("minimumSpacing") ?? 1
    };
    return new GuCavernFloraTarget
    {
        flora = new ThingDef { defName = defName, comps = new() { species } },
        targetPatches = (int)node.Element("targetPatches")!
    };
}).ToList();
ManagedEngineChecks.Run(springDef, args[1], targets);

var size = new IntVec3(39, 1, 39);
var bounds = new CellRect(0, 0, size.x, size.z);
var openCounts = new List<int>();
var waterCounts = new List<int>();
var basinCounts = new List<int>();
var proportions = new List<double>();
var poolCounts = new List<int>();
var fertileCounts = new List<int>();
for (int seed = 0; seed < samples; seed++)
{
    var layout = new GuSpringCavernLayout(size, new IntVec2(2, 2), seed);
    var open = bounds.Cells.Where(cell => layout.GroundAt(cell) != GuCavernGround.Wall).ToHashSet();
    var water = open.Where(cell => layout.GroundAt(cell) == GuCavernGround.ShallowWater).ToHashSet();
    var outlets = CompGuSpiritSpring.BasinWaterOutputs(layout.Footprint).ToList();
    var basin = Connected(outlets[0], water);
    int basinCount = basin.Count;
    var otherWater = water.Except(basin).ToHashSet();
    int pools = 1;
    while (otherWater.Count > 0)
    {
        var pool = Connected(otherWater.First(), otherWater);
        Check(pool.Count >= 8 && pool.Count < basin.Count, $"Secondary pool size at seed {seed}");
        otherWater.ExceptWith(pool);
        pools++;
    }

    Check(open.Count > 720 && open.All(cell => cell.x >= 2 && cell.z >= 2 && cell.x < 37 && cell.z < 37),
        $"Usable geometry and shell at seed {seed}");
    Check(pools == 4 && basinCount > 140 && outlets.All(basin.Contains), $"Basin and outlets at seed {seed}");
    Check(basin.Average(cell => cell.z) > layout.Footprint.maxZ + 4, $"Southern bank at seed {seed}");
    Check((double)basinCount / water.Count is >= 0.60 and <= 0.75, $"Water dominance at seed {seed}");
    var dryWalkable = layout.DryApproach.Except(layout.Footprint.Cells).ToHashSet();
    Check(dryWalkable.All(cell => layout.GroundAt(cell) == GuCavernGround.Soil), $"Dry ground at seed {seed}");
    var dryReachable = Connected(layout.Arrival, dryWalkable);
    for (int x = layout.Footprint.minX - 1; x <= layout.Footprint.maxX + 1; x++)
        Check(dryReachable.Contains(new IntVec3(x, 0, layout.Footprint.minZ - 1)), $"Dry shore at seed {seed}");
    Check(open.Except(layout.Footprint.Cells).ToHashSet()
        .SetEquals(Connected(layout.Arrival, open.Except(layout.Footprint.Cells).ToHashSet())),
        $"Walkable chamber connectivity at seed {seed}");

    var physicalFlora = new Dictionary<IntVec3, ThingDef>();
    foreach (GuCavernFloraTarget target in targets)
    {
        int Fill(int budget, int existing, Dictionary<IntVec3, ThingDef> flora, bool blocked = false) =>
            GuFloraGrowth.Fill(target, layout.Spring, habitatRadius, seed, existing, budget,
                cell => terrains.GetValueOrDefault(layout.GroundAt(cell)),
                cell => !blocked && !layout.DryApproach.Contains(cell) && !outlets.Contains(cell) && !flora.ContainsKey(cell),
                cell => flora.GetValueOrDefault(cell) == target.flora,
                cell => flora.TryAdd(cell, target.flora));

        int initial = Fill(int.MaxValue, 0, physicalFlora);
        Check(initial == target.targetPatches, $"{target.flora.defName} placed {initial} at seed {seed}");
        // Rebuild the local set from physical entries as the spring does after restoring.
        var restored = new Dictionary<IntVec3, ThingDef>(physicalFlora);
        int restoredCount = restored.Values.Count(def => def == target.flora);
        Check(Fill(1, restoredCount, restored) == 0, $"Restored colony capacity at seed {seed}");
        restored.Remove(restored.First(entry => entry.Value == target.flora).Key);
        Check(Fill(1, restoredCount - 1, restored, blocked: true) == 0, $"Blocked regrowth at seed {seed}");
        Check(Fill(1, restoredCount - 1, restored) == 1 && restored.Count == physicalFlora.Count,
            $"Bounded recovery at seed {seed}");
    }

    int rawUnits = physicalFlora.Values.Sum(def => def.GetCompProperties<CompProperties_GuSpiritFlora>().cavernSupportUnits);
    Check(GuFloraGrowth.EffectiveSupport(rawUnits, unitsPerPoint, supportCap) == 7, $"Initial support at seed {seed}");

    openCounts.Add(open.Count);
    waterCounts.Add(water.Count);
    basinCounts.Add(basinCount);
    proportions.Add((double)basinCount / water.Count);
    poolCounts.Add(pools);
    fertileCounts.Add(open.Count(cell => layout.GroundAt(cell) is GuCavernGround.Marsh or GuCavernGround.RichSoil));
}

Console.WriteLine($"PASS: {samples:N0} layouts. Open {openCounts.Min()}–{openCounts.Max()}, "
    + $"water {waterCounts.Min()}–{waterCounts.Max()}, basin {basinCounts.Min()}–{basinCounts.Max()} "
    + $"({proportions.Min():P1}–{proportions.Max():P1}), pools {poolCounts.Min()}–{poolCounts.Max()}, "
    + $"fertile {fertileCounts.Min()}–{fertileCounts.Max()}.");
Check(GuFloraGrowth.EffectiveSupport(0, unitsPerPoint, supportCap) == 0
    && GuFloraGrowth.EffectiveSupport(1, unitsPerPoint, supportCap) == 1
    && GuFloraGrowth.EffectiveSupport(long.MaxValue, unitsPerPoint, supportCap) == 7
    && GuFloraGrowth.HopeCapacity(7, maximumHope) == 17, "Cavern habitat support bounds");
Console.WriteLine($"PASS: {samples:N0} physical placement and regrowth scenarios. "
    + string.Join(", ", targets.Select(target => $"{target.targetPatches} {target.flora.defName}"))
    + ". 39 units yield 7 support and Hope Gu capacity 17.");

static HashSet<IntVec3> Connected(IntVec3 origin, HashSet<IntVec3> cells)
{
    var seen = new HashSet<IntVec3> { origin };
    var queue = new Queue<IntVec3>();
    queue.Enqueue(origin);
    while (queue.TryDequeue(out IntVec3 cell))
        foreach (IntVec3 offset in GenAdj.CardinalDirections)
            if (cells.Contains(cell + offset) && seen.Add(cell + offset)) queue.Enqueue(cell + offset);
    return seen;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
