using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;

/// <summary>M1 clan start: Def references, XML-to-field binding, role planning and aptitude rules.</summary>
internal static class ClanStartChecks
{
    private static int passed;

    internal static void Run(string root, string rimWorldPath)
    {
        var coreDefs = DefNames(Path.Combine(rimWorldPath, "Data", "Core", "Defs"));
        var modDefs = DefNames(Path.Combine(root, "Defs"));
        var scenario = XDocument.Load(Path.Combine(root, "Defs", "Scenarios", "GU_SouthernBorderClan.xml"));
        CheckScenario(scenario, coreDefs, modDefs, rimWorldPath);
        CheckRoles(root, coreDefs);
        CheckTravel(root);
        CheckPlanner();
        CheckAptitude();
        CheckConfigErrors();
        Console.WriteLine($"PASS: {passed} clan start checks (managed; no RimWorld process).");
    }

    private static HashSet<string> DefNames(string directory) =>
        Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "<defName>([^<]+)</defName>").Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static void CheckScenario(XDocument scenario, HashSet<string> core, HashSet<string> mod, string rimWorldPath)
    {
        var def = scenario.Descendants("ScenarioDef").Single();
        Check((string)def.Element("defName") == "GU_SouthernBorderClan", "Scenario defName is stable.");
        string parent = (string)def.Attribute("ParentName");
        string classic = File.ReadAllText(Path.Combine(rimWorldPath, "Data", "Core", "Defs", "Scenarios", "Scenarios_Classic.xml"));
        Check(classic.Contains($"Name=\"{parent}\""), $"Scenario parent {parent} exists in Core.");

        var parts = def.Element("scenario")!.Element("parts")!.Elements("li").ToList();
        foreach (var part in parts)
        {
            string partDef = (string)part.Element("def");
            Check(partDef != null && (core.Contains(partDef) || mod.Contains(partDef)), $"Scenario part Def {partDef} exists.");
            foreach (string field in new[] { "thingDef", "stuff" })
                if ((string)part.Element(field) is { } name)
                    Check(core.Contains(name) || mod.Contains(name), $"Scenario {field} {name} exists.");
        }
        Check(scenario.Descendants("ScenPartDef").Any(p => (string)p.Element("defName") == "GU_ClanStart"
            && (string)p.Element("scenPartClass") == typeof(ScenPart_GuClanStart).FullName), "Clan ScenPartDef binds the C# class.");

        var clan = parts.Single(p => (string)p.Attribute("Class") == typeof(ScenPart_GuClanStart).FullName);
        var fields = typeof(ScenPart_GuClanStart).GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name).ToHashSet();
        foreach (var element in clan.Elements().Where(e => e.Name.LocalName != "def"))
            Check(fields.Contains(element.Name.LocalName), $"Clan part XML field {element.Name.LocalName} binds a public field.");
        Check(parts.Any(p => (string)p.Element("thingDef") == "GU_PrimevalStone"), "The clan starts with primeval stones.");
        var pawns = parts.Single(p => (string)p.Element("def") == "ConfigPage_ConfigureStartingPawns");
        int count = (int)pawns.Element("pawnCount")!;
        int disciples = (int)clan.Element("discipleCount")!;
        Check(count >= disciples + 2, "The default start has an elder, all disciples and at least one mortal worker.");
    }

    private static void CheckRoles(string root, HashSet<string> core)
    {
        var roles = XDocument.Load(Path.Combine(root, "Defs", "HediffDefs", "GU_ClanRoles.xml"));
        var names = roles.Descendants("HediffDef").Select(d => (string)d.Element("defName")).Where(n => n != null).ToHashSet();
        Check(names.SetEquals(new[] { GuClanRoles.ElderDefName, GuClanRoles.DiscipleDefName, GuClanRoles.WorkerDefName }),
            "Role hediff Defs match the role constants.");
        foreach (var stat in roles.Descendants("statOffsets").SelectMany(s => s.Elements()))
            Check(core.Contains(stat.Name.LocalName), $"Role stat {stat.Name.LocalName} exists in Core.");
    }

    private static void CheckTravel(string root)
    {
        var site = XDocument.Load(Path.Combine(root, "Defs", "MapGeneration", "GU_SpiritSpringCavern.xml"))
            .Descendants("ProjectGu.Underground.GuPocketSiteDef").Single();
        Check((bool?)site.Element("playerTravel") == true && typeof(GuPocketSiteDef).GetField("playerTravel") != null,
            "The spirit spring route opts into ordinary travel.");
        var probe = XDocument.Load(Path.Combine(root, "Defs", "MapGeneration", "GU_PocketProbe.xml"))
            .Descendants("ProjectGu.Underground.GuPocketSiteDef").Single();
        Check(probe.Element("playerTravel") == null, "The developer probe stays Development Mode only.");
    }

    private static void CheckPlanner()
    {
        var roles = GuClanRoles.PlanRoles(new List<float> { 25f, 61f, 15f, 33f, 17f, 40f }, 2, 13);
        Check(roles[1] == GuClanRole.Elder, "The oldest colonist becomes elder.");
        Check(roles[2] == GuClanRole.Disciple && roles[4] == GuClanRole.Disciple, "The two youngest eligible colonists become disciples.");
        Check(roles[0] == GuClanRole.MortalWorker && roles[3] == GuClanRole.MortalWorker && roles[5] == GuClanRole.MortalWorker,
            "The rest become mortal workers.");

        roles = GuClanRoles.PlanRoles(new List<float> { 9f, 12f, 50f, 20f }, 2, 13);
        Check(roles[2] == GuClanRole.Elder && roles[3] == GuClanRole.Disciple
            && roles[0] == GuClanRole.MortalWorker && roles[1] == GuClanRole.MortalWorker, "Children below the minimum age are not disciples.");
        Check(GuClanRoles.PlanRoles(new List<float>(), 2, 13).Length == 0, "No colonists yields no roles.");
        Check(GuClanRoles.PlanRoles(new List<float> { 30f }, 2, 13).Single() == GuClanRole.Elder, "A lone founder is the elder.");
        Check(GuClanRoles.PlanRoles(new List<float> { 30f, 20f }, -1, 13)[1] == GuClanRole.MortalWorker, "A negative count makes no disciples.");
        roles = GuClanRoles.PlanRoles(new List<float> { 40f, 40f, 20f }, 0, 13);
        Check(roles.Count(r => r == GuClanRole.Elder) == 1 && roles[0] == GuClanRole.Elder, "Age ties select exactly one elder in colonist order.");
    }

    private static void CheckAptitude()
    {
        var grades = new Dictionary<string, int>();
        bool ordinary = true;
        for (int i = 0; i < 20000; i++)
        {
            int aptitude = GuClanRoles.GiftedAptitude();
            ordinary &= aptitude is >= 20 and <= 99 && GuCultivatorRecord.ValidAptitude(aptitude);
            string grade = AptitudeRules.Grade(aptitude);
            grades[grade] = grades.GetValueOrDefault(grade) + 1;
        }
        Check(ordinary, "20,000 gifted aptitudes are all ordinary and nonzero.");
        Check(grades.Keys.Count == 4 && grades["D"] > grades["C"] && grades["C"] > grades["B"] && grades["B"] > grades["A"],
            "Gifted aptitude spans all grades with D most common and A rarest.");
        Check(AptitudeRules.Grade(AptitudeRules.ClampOrdinary(60)) == "B" && AptitudeRules.Grade(AptitudeRules.ClampOrdinary(74)) == "B",
            "The default elder range is B-grade.");
    }

    private static void CheckConfigErrors()
    {
        var part = new ScenPart_GuClanStart { def = new ScenPartDef() };
        Check(!part.ConfigErrors().Any(), "Default clan part configuration is valid.");
        part.elderAptitude = new Verse.IntRange(10, 50);
        part.discipleCount = -1;
        part.entranceMinDistance = 30f;
        Check(part.ConfigErrors().Count() == 3, "Invalid aptitude, count and distances are reported.");

        // The Def's Scenario instance is reused across new games in one session.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var done = typeof(ScenPart_GuClanStart).GetField("foundingDone", flags)!;
        var pending = typeof(ScenPart_GuClanStart).GetField("foundingPending", flags)!;
        done.SetValue(part, true);
        pending.SetValue(part, true);
        part.PreConfigure();
        Check(!(bool)done.GetValue(part)! && !(bool)pending.GetValue(part)!, "A new game configuration resets founding progress.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Clan start check failed: " + message);
        passed++;
    }
}
