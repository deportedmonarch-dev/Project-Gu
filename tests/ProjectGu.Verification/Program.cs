using ProjectGu.Springs;
using ProjectGu.Underground;
using ProjectGu.Domain;
using Verse;
using System.Xml.Linq;

const int seedRange = 5760;
int samples = args.Contains("--focused") ? 64 : seedRange;
var root = args[0];
var springDef = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_SpiritSpring.xml"));
var springSettings = springDef.Descendants("li")
    .Single(node => (string)node.Attribute("Class") == "ProjectGu.Springs.CompProperties_GuSpiritSpring");
var speciesDefs = XDocument.Load(Path.Combine(root, "Defs/GuSpeciesDefs/GU_GuSpecies.xml"));
var hopeSpecies = speciesDefs.Descendants("ProjectGu.Domain.GuSpeciesDef")
    .Single(node => (string)node.Element("defName")! == "GU_HopeGu");
var captureJob = XDocument.Load(Path.Combine(root, "Defs/JobDefs/GU_CaptureWild.xml"))
    .Descendants("JobDef").Single(node => (string)node.Element("defName")! == "GU_CaptureWildGu");
var dormantGuDef = XDocument.Load(Path.Combine(root, "Defs/ThingDefs/GU_DormantGu.xml"));
Check((string)hopeSpecies.Element("wildProjectionKind")! == "GU_WildGu"
    && (string)hopeSpecies.Element("dormantProjectionDef")! == "GU_DormantGu"
    && dormantGuDef.Descendants("ThingDef").Any(node => (string)node.Element("thingClass")! == "ProjectGu.Thing_GuDormantProjection")
    && (string)captureJob.Element("driverClass")! == "ProjectGu.Jobs.JobDriver_GuCaptureWild",
    "Ordinary Hope Gu acquisition uses configured species projections and a dedicated capture job");
Check(new GuRefinement().CultivatorId == null
    && File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Dormancy.cs"))
        .Contains("record.Refinement?.CultivatorId != null", StringComparison.Ordinal)
    && File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Aperture.cs"))
        .Contains("IsRecognizedByCultivator", StringComparison.Ordinal),
    "Physical Gu possession begins without refinement allegiance and mortal aperture access checks the existing bond");
var refinementJob = XDocument.Load(Path.Combine(root, "Defs/JobDefs/GU_Refinement.xml"))
    .Descendants("JobDef").Single(node => (string)node.Element("defName")! == "GU_RefineMortalGu");
Check((string)refinementJob.Element("driverClass")! == "ProjectGu.Jobs.JobDriver_GuRefineMortalGu"
    && (int)hopeSpecies.Element("refinementBaseWorkTicks")! == 9000
    && (float)hopeSpecies.Element("refinementEssenceCost")! == 12f
    && File.ReadAllText(Path.Combine(root, "Source/ProjectGu/Domain/GuRecord.cs"))
        .Contains("Scribe_Deep.Look(ref refinementProcess", StringComparison.Ordinal)
    && File.ReadAllText(Path.Combine(root, "Source/ProjectGu/GuIndividualRegistry.Refinement.cs"))
        .Contains("CompleteRefinement", StringComparison.Ordinal),
    "Will-enforcement uses a tunable job and saves process state on the canonical Gu record");
int unitsPerPoint = (int)springSettings.Element("cavernSupportUnitsPerPoint")!;
int supportCap = (int)springSettings.Element("cavernMaxFloraSupport")!;
int maximumHope = (int)springSettings.Element("maxHopeGu")!;
var terrains = new Dictionary<string, TerrainDef>
{
    ["SoilRich"] = new() { defName = "SoilRich" },
    ["MarshyTerrain"] = new() { defName = "MarshyTerrain" },
    ["WaterShallow"] = new() { defName = "WaterShallow", tags = new() { "Water" } },
    ["GU_PrimevalQiRichSoil"] = new() { defName = "GU_PrimevalQiRichSoil" }
};
var floraDefs = Directory.EnumerateFiles(Path.Combine(root, "Defs/ThingDefs"), "*.xml")
    .Select(XDocument.Load).SelectMany(document => document.Descendants("ThingDef"))
    .Where(definition => definition.Element("defName") != null)
    .ToDictionary(definition => (string)definition.Element("defName")!);
List<GuCavernFloraTarget> ReadTargets(string listName)
    => springSettings.Element(listName)!.Elements("li").Select(node =>
{
    string defName = node.Element("flora")!.Value;
    var props = floraDefs[defName].Descendants("li").Single(element =>
        (string)element.Attribute("Class") == "ProjectGu.Springs.CompProperties_GuSpiritFlora");
    var species = new CompProperties_GuSpiritFlora
    {
        support = (int)props.Element("support")!,
        cavernSupportUnits = (int)props.Element("cavernSupportUnits")!,
        cavernSupportFullAt = (int?)props.Element("cavernSupportFullAt") ?? 0,
        growthSoils = props.Element("growthSoils")!.Elements("li")
            .Select(soil => terrains[soil.Value]).ToList(),
        waterRange = (int?)props.Element("waterRange") ?? 0,
        minimumSpacing = (int?)props.Element("minimumSpacing") ?? 1
    };
    return new GuCavernFloraTarget
    {
        flora = new ThingDef { defName = defName, comps = new() { species } },
        targetPatches = (int)node.Element("targetPatches")!
    };
}).ToList();
var targets = ReadTargets("cavernFlora");
var botanicalTargets = ReadTargets("botanicalCavernFlora");
int botanicalRadius = (int)springSettings.Element("botanicalHabitatRadius")!;
ManagedEngineChecks.Run(springDef, args[1], targets);
ClanStartChecks.Run(root, args[1]);
GuGenerationChecks.Run(root);

void VerifyFlora(GuSpringCavernLayout layout, Dictionary<IntVec3, int> shore, int seed)
{
    var physicalFlora = new Dictionary<IntVec3, ThingDef>();
    int fieldSeed = Gen.HashCombineInt(layout.Spring.GetHashCode(), layout.Arrival.GetHashCode());
    foreach (GuCavernFloraTarget target in botanicalTargets)
    {
        int Fill(int budget, int existing, Dictionary<IntVec3, ThingDef> flora, bool blocked = false) =>
            GuFloraGrowth.Fill(target, layout.Spring, botanicalRadius, seed, existing, budget,
                cell => layout.GroundAt(cell) switch
                {
                    GuCavernGround.QiSoil => terrains["GU_PrimevalQiRichSoil"],
                    GuCavernGround.ShallowWater => terrains["WaterShallow"],
                    _ => null
                },
                cell => !blocked && !layout.DryApproach.Contains(cell) && !flora.ContainsKey(cell),
                cell => flora.GetValueOrDefault(cell) == target.flora,
                cell => flora.TryAdd(cell, target.flora),
                cell => GuSpringCavernLayout.BasinPlantingScore(cell, layout.Spring, fieldSeed, shore));

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
    var grass = physicalFlora.Where(entry => entry.Value.defName == "GU_SpiritGrass").Select(entry => entry.Key).ToArray();
    Check(grass.Count(cell => shore.GetValueOrDefault(cell, 7) <= 3) >= 85,
        $"Grass concentrates around the main basin at seed {seed}");
    Check(grass.Count(cell => cell.z >= layout.Spring.z + 8) >= 20
        && grass.Count(cell => cell.z < layout.Spring.z + 8) >= 35,
        $"Both ends of the basin carry grass at seed {seed}");
    long rawUnits = physicalFlora.Values.GroupBy(def => def).Sum(group =>
        group.Key.GetCompProperties<CompProperties_GuSpiritFlora>().HabitatUnits(group.Count(), true));
    Check(rawUnits == 39 && GuFloraGrowth.EffectiveSupport(rawUnits, unitsPerPoint, supportCap) == 7,
        $"Initial habitat contribution at seed {seed}");
}

var size = new IntVec3(39, 1, 39);
var bounds = new CellRect(0, 0, size.x, size.z);
var openCounts = new List<int>();
var waterCounts = new List<int>();
var basinCounts = new List<int>();
var proportions = new List<double>();
var poolCounts = new List<int>();
var fertileCounts = new List<int>();
for (int sample = 0; sample < samples; sample++)
{
    int seed = sample * seedRange / samples;
    var layout = new GuSpringCavernLayout(size, new IntVec2(2, 2), seed);
    var open = bounds.Cells.Where(cell => layout.GroundAt(cell) != GuCavernGround.Wall).ToHashSet();
    var water = open.Where(cell => layout.GroundAt(cell) == GuCavernGround.ShallowWater).ToHashSet();
    var outlets = GuSpringCavernLayout.SouthernOutputs(layout.Footprint).ToList();
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
    Check(dryWalkable.All(cell => layout.GroundAt(cell) == GuCavernGround.QiSoil), $"Dry Qi-rich ground at seed {seed}");
    Check(GuSpringCavernLayout.SouthernShore(layout.Footprint).All(dryWalkable.Contains),
        $"Each wet outlet has a guaranteed adjacent Qi-rich shore at seed {seed}");
    var dryReachable = Connected(layout.Arrival, dryWalkable);
    for (int x = layout.Footprint.minX - 1; x <= layout.Footprint.maxX + 1; x++)
        Check(dryReachable.Contains(new IntVec3(x, 0, layout.Footprint.minZ - 1)), $"Dry shore at seed {seed}");
    Check(open.Except(layout.Footprint.Cells).ToHashSet()
        .SetEquals(Connected(layout.Arrival, open.Except(layout.Footprint.Cells).ToHashSet())),
        $"Walkable chamber connectivity at seed {seed}");

    var shore = GuSpringCavernLayout.MainBasinDistances(layout.Footprint, open.Contains, water.Contains);
    VerifyFlora(layout, shore, seed);

    openCounts.Add(open.Count);
    waterCounts.Add(water.Count);
    basinCounts.Add(basinCount);
    proportions.Add((double)basinCount / water.Count);
    poolCounts.Add(pools);
    fertileCounts.Add(open.Count(cell => layout.GroundAt(cell) == GuCavernGround.QiSoil));
}

Console.WriteLine($"PASS: {samples:N0} layouts. Open {openCounts.Min()}–{openCounts.Max()}, "
    + $"water {waterCounts.Min()}–{waterCounts.Max()}, basin {basinCounts.Min()}–{basinCounts.Max()} "
    + $"({proportions.Min():P1}–{proportions.Max():P1}), pools {poolCounts.Min()}–{poolCounts.Max()}, "
    + $"fertile {fertileCounts.Min()}–{fertileCounts.Max()}.");
Check(GuFloraGrowth.EffectiveSupport(0, unitsPerPoint, supportCap) == 0
    && GuFloraGrowth.EffectiveSupport(1, unitsPerPoint, supportCap) == 1
    && GuFloraGrowth.EffectiveSupport(long.MaxValue, unitsPerPoint, supportCap) == 7
    && GuFloraGrowth.HopeCapacity(7, maximumHope) == 17, "Cavern habitat support bounds");
Console.WriteLine($"PASS: {samples:N0} botanical placement and regrowth scenarios. "
    + string.Join(", ", botanicalTargets.Select(target => $"{target.targetPatches} {target.flora.defName}"))
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
