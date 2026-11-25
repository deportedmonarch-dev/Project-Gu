using System.Xml.Linq;
using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Presentation;
using Verse;

/// <summary>
/// Managed checks of Alpha 4A presentation logic against the real assembly: primeval-sea layering by area and quality,
/// rank terminology and art paths, the aperture condition authority, visual-state rules, effect profile validation and
/// the refinement presentation Def. Native and rendered fixtures cover the tab, its actions and the effects in game.
/// </summary>
internal static class ApertureInterfaceChecks
{
    private static int passed;

    internal static void Run(string root)
    {
        VerifySeaLayout();
        VerifyStageAndQualityStaySeparate();
        VerifyRankDefinitions(root);
        VerifyConditions();
        VerifyProfiles(root);
        VerifySources(root);
        Console.WriteLine($"PASS: Alpha 4A aperture interface checks ({passed}).");
    }

    private static float[] Circle(int size, float radius)
    {
        var coverage = new float[size];
        float centre = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if ((x - centre) * (x - centre) + (y - centre) * (y - centre) <= radius * radius) coverage[y]++;
        return coverage;
    }

    private static void VerifySeaLayout()
    {
        var circle = Circle(256, 100f);
        float top = Array.FindIndex(circle, c => c > 0), bottom = Array.FindLastIndex(circle, c => c > 0) + 1;
        Check(PrimevalSeaLayout.RowForFilledFraction(circle, 0) == bottom && PrimevalSeaLayout.RowForFilledFraction(circle, 1) == top,
            "An empty sea sits at the bottom of the aperture and a full sea reaches its top");
        float half = PrimevalSeaLayout.RowForFilledFraction(circle, 0.5);
        Check(Math.Abs(half - 127.5f) < 1f, $"Half the area fills a sphere to its equator, not half its height ({half:0.##})");
        float quarter = PrimevalSeaLayout.RowForFilledFraction(circle, 0.25);
        Check(quarter - half > (bottom - top) * 0.15f && quarter < bottom, "A quarter of the area rises less than a quarter of the height above the bottom chord");
        float previous = bottom;
        for (int i = 1; i <= 20; i++)
        {
            float row = PrimevalSeaLayout.RowForFilledFraction(circle, i / 20.0);
            Check(row <= previous, "The surface rises monotonically as the sea fills");
            previous = row;
        }

        // Mixed quality: 10 Initial and 6 Middle of 40. Middle (denser) settles below Initial; the surface matches the total.
        var boundaries = new float[PrimevalSeaLayout.Qualities + 1];
        float surface = PrimevalSeaLayout.Layers(circle, new double[] { 10, 6, 0, 0 }, 40, boundaries);
        float expectedSurface = PrimevalSeaLayout.RowForFilledFraction(circle, 16 / 40.0);
        float middleTop = PrimevalSeaLayout.RowForFilledFraction(circle, 6 / 40.0);
        Check(Math.Abs(surface - expectedSurface) < 0.01f && Math.Abs(boundaries[0] - surface) < 0.01f && Math.Abs(boundaries[1] - middleTop) < 0.01f,
            "A mixed sea stacks Middle essence below Initial essence and its surface reflects the total volume");
        Check(boundaries[2] == boundaries[3] && boundaries[3] == boundaries[4] && boundaries[4] == bottom,
            "Absent Upper and Peak essence form no layer");
        Check(PrimevalSeaLayout.QualityAt((boundaries[0] + boundaries[1]) / 2, boundaries) == 0
            && PrimevalSeaLayout.QualityAt((boundaries[1] + boundaries[4]) / 2, boundaries) == 1
            && PrimevalSeaLayout.QualityAt(surface - 3, boundaries) == -1,
            "Rows inside each layer report that layer's quality, and rows above the surface report none");

        // A trace amount stays visible as a thin layer; the numbers remain authoritative.
        PrimevalSeaLayout.Layers(circle, new double[] { 20, 0, 0, 0.001 }, 40, boundaries);
        Check(boundaries[4] - boundaries[3] >= 1.49f, "A trace of Peak essence keeps a visible layer of at least 1.5 rows");
        PrimevalSeaLayout.Layers(circle, new double[] { 0, 0, 0, 0 }, 40, boundaries);
        Check(boundaries.All(row => row == bottom), "An empty aperture draws no layers");
        PrimevalSeaLayout.Layers(circle, new double[] { 10, 10, 10, 10 }, 40, boundaries);
        Check(Math.Abs(boundaries[0] - top) < 0.01f && boundaries[0] < boundaries[1] && boundaries[1] < boundaries[2] && boundaries[2] < boundaries[3],
            "A full sea of four qualities fills the aperture in four ordered layers");
        Check(PrimevalSeaLayout.WallArc(12, 24) == 0.5f && PrimevalSeaLayout.WallArc(30, 24) == 1f && PrimevalSeaLayout.WallArc(0, 0) == 1f,
            "Wall progress is the nourishment fraction, complete at Peak");
    }

    /// <summary>An Initial-stage cultivator holding Middle essence keeps Initial stage; the sea shows both qualities.</summary>
    private static void VerifyStageAndQualityStaySeparate()
    {
        var aperture = new Hediff_Aperture();
        aperture.Initialize(40); // 20 Initial essence at awakening.
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 8), "Liquor Worm-style conversion succeeded");
        var amounts = Enumerable.Range(0, 4).Select(q => aperture.EssenceOfQuality((RankOneStage)q)).ToArray();
        Check(aperture.Stage == RankOneStage.Initial && amounts[0] == 12 && amounts[1] == 8,
            "Stage stays Initial while the sea holds 12 Initial and 8 Middle essence");
        var boundaries = new float[PrimevalSeaLayout.Qualities + 1];
        var circle = Circle(256, 100f);
        PrimevalSeaLayout.Layers(circle, amounts, aperture.MaxEssence, boundaries);
        Check(boundaries[1] - boundaries[0] > 1f && boundaries[2] - boundaries[1] > 1f && boundaries[3] == boundaries[2],
            "The orb draws separate Initial and Middle layers rather than a uniform Middle sea");
    }

    private static void VerifyRankDefinitions(string root)
    {
        var xml = XDocument.Load(Path.Combine(root, "Defs", "GuApertureRankDefs", "GU_ApertureRanks.xml"));
        var ranks = xml.Root!.Elements("ProjectGu.Interface.GuApertureRankDef").ToDictionary(e => (int)e.Element("rank")!);
        var expected = new Dictionary<int, string>
        {
            [1] = "Green Copper Primeval Essence", [2] = "Red Steel Primeval Essence", [3] = "White Silver Primeval Essence",
            [4] = "Yellow Gold Primeval Essence", [5] = "Purple Crystal Primeval Essence", [6] = "Green Grape Immortal Essence",
            [7] = "Red Date Immortal Essence", [8] = "White Litchi Immortal Essence", [9] = "Yellow Apricot Immortal Essence"
        };
        Check(ranks.Count == 9 && expected.All(pair => (string)ranks[pair.Key].Element("essenceLabel")! == pair.Value),
            "All nine ranks carry the canonical essence names");
        Check(ranks.Values.Count(e => (string?)e.Element("implemented") == "true") == 1 && (string?)ranks[1].Element("implemented") == "true",
            "Only Rank 1 is marked implemented");
        Check(Enumerable.Range(6, 4).All(r => (string?)ranks[r].Element("essenceKind") == "Immortal" && (string?)ranks[r].Element("immortalAperture") == "true")
            && Enumerable.Range(1, 5).All(r => (string?)ranks[r].Element("essenceKind") != "Immortal"),
            "Ranks 6 to 9 are immortal essence with their own aperture identity");
        var stages = ranks[1].Element("stages")!.Elements("li").Select(li => (string)li.Element("label")!).ToArray();
        Check(stages.SequenceEqual(new[] { "Initial", "Middle", "Upper", "Peak" }), "Rank 1 defines its four minor stages in order");
        var colors = ranks[1].Element("stages")!.Elements("li").Select(li => (string)li.Element("essenceColor")!).ToArray();
        Check(colors.Distinct().Count() == 4, "Each Rank 1 stage has its own essence colour, read from one central definition");
        foreach (string field in new[] { "baseTexPath", "seaMaskTexPath", "wallMaskTexPath", "highlightsTexPath" })
        {
            string path = (string)ranks[1].Element(field)!;
            Check(File.Exists(Path.Combine(root, "Textures", path.Replace('/', Path.DirectorySeparatorChar) + ".png")), $"Rank 1 {field} exists: {path}");
        }
        var def = new GuApertureRankDef { defName = "ManagedRank", rank = 1, essenceLabel = "x", implemented = true };
        Check(def.ConfigErrors().Count() == 2, "An implemented rank without stages or art is reported");
        def.stages = Enumerable.Range(0, 4).Select(i => new GuApertureStageVisual { label = i.ToString() }).ToList();
        def.baseTexPath = def.seaMaskTexPath = def.wallMaskTexPath = "a";
        Check(!def.ConfigErrors().Any() && def.Stage(9)!.label == "3" && def.Stage(-1)!.label == "0", "A complete rank validates and clamps stage lookups");
    }

    private sealed class FixedCondition : IGuApertureConditionSource
    {
        internal GuApertureVitality? Answer;
        internal bool Throw;
        public bool TryGetCondition(Hediff_Aperture aperture, out GuApertureVitality vitality, out float crackSeverity)
        {
            if (Throw) throw new InvalidOperationException("source failure");
            vitality = Answer ?? GuApertureVitality.Healthy;
            crackSeverity = 0.6f;
            return Answer != null;
        }
    }

    private static void VerifyConditions()
    {
        var aperture = new Hediff_Aperture();
        aperture.Initialize(40);
        Check(GuApertureConditions.SourceCount == 0 && GuApertureConditions.Resolve(aperture, out float none) == GuApertureVitality.Healthy && none == 0,
            "With no authoritative source an aperture is always healthy; no cracks are invented");
        var source = new FixedCondition();
        GuApertureConditions.Register(source);
        Check(GuApertureConditions.Resolve(aperture, out _) == GuApertureVitality.Healthy, "A declining source leaves the aperture healthy");
        source.Answer = GuApertureVitality.Cracked;
        Check(GuApertureConditions.Resolve(aperture, out float severity) == GuApertureVitality.Cracked && severity == 0.6f,
            "An authoritative source can report a cracked aperture with its severity");
        source.Answer = null;
        source.Throw = true;
        Check(GuApertureConditions.Resolve(aperture, out _) == GuApertureVitality.Healthy, "A failing source falls back to healthy");
        source.Throw = false;

        var dead = new GuApertureVisualState(GuApertureVitality.Dead, 0.9f, GuApertureActivity.EssenceRefinement | GuApertureActivity.NourishingWalls);
        var healthy = new GuApertureVisualState(GuApertureVitality.Healthy, 0.9f, GuApertureActivity.NourishingWalls);
        var cracked = new GuApertureVisualState(GuApertureVitality.Cracked, 3f, GuApertureActivity.None);
        Check(!dead.Functional && dead.Activity == GuApertureActivity.None && dead.CrackSeverity == 0 && healthy.CrackSeverity == 0
            && cracked.CrackSeverity == 1f && healthy.Functional,
            "A dead aperture shows no activity; crack severity applies only to cracked apertures and is clamped");
    }

    private static void VerifyProfiles(string root)
    {
        var profile = new GuFxProfileDef { defName = "ManagedSpin", texPath = "x", spinDegreesPerTick = 0.6f, pulseScale = 0.05f, pulsePeriodTicks = 100 };
        Check(!profile.ConfigErrors().Any() && profile.PulseAt(0) == 1f && Math.Abs(profile.PulseAt(25) - 1.05f) < 0.0001f,
            "Spinning, breathing effect profiles validate and breathe by their configured fraction");
        profile.spinDegreesPerTick = 200f;
        profile.pulseScale = 2f;
        Check(profile.ConfigErrors().Count() == 2, "Out-of-range spin and breathing are reported");
        var presentation = new GuRefinementPresentationDef { defName = "ManagedPresentation" };
        Check(presentation.ConfigErrors().Count() == 1, "A refinement presentation without layers is reported");
        Check(GuRefinementPresentation.Key("p", 0) != GuRefinementPresentation.Key("p", 1) && GuRefinementPresentation.Key("p", 0) != GuRefinementPresentation.Key("q", 0),
            "Each refinement layer has its own key per process");

        var fx = XDocument.Load(Path.Combine(root, "Defs", "Presentation", "GU_RefinementPresentation.xml"));
        var layers = fx.Root!.Element("ProjectGu.Presentation.GuRefinementPresentationDef")!.Element("cultivatorLayers")!.Elements("li").Select(li => li.Value).ToArray();
        var profiles = fx.Root.Elements("ProjectGu.Presentation.GuFxProfileDef").ToDictionary(e => (string)e.Element("defName")!);
        Check(layers.Length == 2 && layers.All(profiles.ContainsKey), "The will-enforcement presentation lists two defined layers");
        foreach (var layer in profiles.Values)
        {
            string path = (string)layer.Element("texPath")!;
            Check(File.Exists(Path.Combine(root, "Textures", path.Replace('/', Path.DirectorySeparatorChar) + ".png")), $"Will-enforcement texture exists: {path}");
        }
        Check(File.Exists(Path.Combine(root, "Languages", "English", "Keyed", "ProjectGU.xml"))
            && XDocument.Load(Path.Combine(root, "Languages", "English", "Keyed", "ProjectGU.xml")).Root!.Element("GU_ApertureTab")?.Value == "Aperture",
            "The tab label key is defined");
    }

    /// <summary>Static guards on the presentation boundary: the tab is never saved and drives gameplay only through actions.</summary>
    private static void VerifySources(string root)
    {
        string folder = Path.Combine(root, "Source", "ProjectGu", "Interface");
        var sources = Directory.GetFiles(folder, "*.cs").ToDictionary(Path.GetFileName, File.ReadAllText);
        Check(sources.Values.All(text => !text.Contains("Scribe_") && !text.Contains("ExposeData")),
            "No Aperture interface type saves state");
        Check(sources.Where(pair => pair.Key!.StartsWith("ITab_Pawn_Aperture")).All(pair =>
                !pair.Value.Contains("TryStoreDormant") && !pair.Value.Contains("TryWithdrawDormant") && !pair.Value.Contains("TryAdvanceRankOne")
                && !pair.Value.Contains("TryOrderChannel") && !pair.Value.Contains("TryTakeOrderedJob")),
            "The tab commits nothing directly; every control runs a revalidating GuApertureAction");
        string gizmos = File.ReadAllText(Path.Combine(root, "Source", "ProjectGu", "GuApertureGizmos.cs"));
        Check(gizmos.Contains("GuApertureActions.Store") && gizmos.Contains("GuApertureActions.Withdraw") && gizmos.Contains("GuApertureActions.EnforceWill"),
            "The fallback menu builds from the same actions as the tab");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Aperture interface check failed: " + message);
        passed++;
        Console.WriteLine("PASS: " + message);
    }
}
