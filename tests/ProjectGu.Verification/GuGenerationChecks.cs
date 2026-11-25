#nullable enable

using System.Xml.Linq;
using ProjectGu;
using RimWorld;
using Verse;

internal static class GuGenerationChecks
{
    internal static void Run(string root)
    {
        var defs = Directory.EnumerateFiles(Path.Combine(root, "Defs"), "*.xml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Root!.Elements()).ToArray();
        var raceParent = defs.Single(def => (string?)def.Attribute("Name") == "GU_WildProjectionRace");
        var kindParent = defs.Single(def => (string?)def.Attribute("Name") == "GU_WildProjectionKind");
        var kinds = defs.Where(def => def.Name == "ProjectGu.Domain.GuSpeciesDef")
            .Select(def => (string?)def.Element("wildProjectionKind")).Where(name => name != null).Distinct().ToArray();
        Check(kinds.Length >= 4 && kinds.All(name =>
        {
            var kind = defs.Single(def => def.Name == "PawnKindDef" && (string?)def.Element("defName") == name);
            var race = defs.Single(def => def.Name == "ThingDef" && (string?)def.Element("defName") == (string?)kind.Element("race"));
            return (string?)kind.Attribute("ParentName") == (string?)kindParent.Attribute("Name")
                && (string?)race.Attribute("ParentName") == (string?)raceParent.Attribute("Name");
        }), "All wild species inherit the shared Gu kind and race Defs");
        Check((string?)raceParent.Element("thingClass") == typeof(Pawn_GuWildProjection).FullName
            && (float?)raceParent.Element("race")?.Element("petness") == 0
            && (bool?)raceParent.Element("race")?.Element("neverIncludeInQuests") == true
            && (string?)raceParent.Element("tradeability") == "None"
            && (bool?)kindParent.Element("canArriveManhunter") == false,
            "Shared Defs declare the guarded Pawn class and native selection exclusions");

        var raceProps = new RaceProperties { neverIncludeInQuests = true, herdMigrationAllowed = false, hasGenders = false };
        var kindDef = new PawnKindDef
        {
            race = new ThingDef { race = raceProps, tradeability = Tradeability.None },
            canArriveManhunter = false, ecoSystemWeight = 0, isFighter = false
        };
        Check(Pawn_GuWildProjection.HasNativeGenerationExclusions(kindDef), "The validator accepts an excluded kind");
        raceProps.petness = 0.08f;
        Check(!Pawn_GuWildProjection.HasNativeGenerationExclusions(kindDef), "Inherited hare petness invalidates a Gu kind");
        raceProps.petness = 0;
        raceProps.neverIncludeInQuests = false;
        Check(!Pawn_GuWildProjection.HasNativeGenerationExclusions(kindDef), "Quest eligibility invalidates a Gu kind");
        raceProps.neverIncludeInQuests = true;
        kindDef.canArriveManhunter = true;
        Check(!Pawn_GuWildProjection.HasNativeGenerationExclusions(kindDef), "Manhunter eligibility invalidates a Gu kind");
        Console.WriteLine("PASS: shared wild Gu Def inheritance and managed generation policy.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
