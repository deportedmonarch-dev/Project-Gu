using ProjectGu.Underground;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static void VerifyHistoricalSpring(int size)
    {
        bool originalPocket = size == 31;
        int expectedStones = originalPocket ? 19 : 24;
        var cave = Find.Maps.Single(map => map.Size.x == size && map.Size.z == size
            && map.listerThings.ThingsOfDef(StoneDef).Sum(thing => thing.stackCount) == expectedStones
            && GuPocketSiteDef.IsSpringCavern(map));
        var spring = BotanicalSpring(cave);
        var flora = new[] { "GU_SpiritGrassHabitat", "GU_MoonOrchidHabitat" }
            .SelectMany(name => cave.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed(name))).ToArray();
        Check(flora.Length == (originalPocket ? 4 : 29) && flora.All(thing => thing is Building)
            && !spring.UsesBotanicalFlora && !spring.UsesCavernComposition
            && cave.listerThings.ThingsOfDef(NativeGrass).Count == 0 && cave.listerThings.ThingsOfDef(NativeOrchid).Count == 0,
            "The historical save loaded its original Building flora and growth profile");
        Check(spring.Reserve == (originalPocket ? 100000 : 99999) && spring.HopeGuAvailable == 3,
            "The historical reserve and original adult Hope Gu count survived");
        var terrain = cave.AllCells.GroupBy(cell => cell.GetTerrain(cave).defName)
            .OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}");
        const string terrain31 = "Granite_Rough=612,MarshyTerrain=53,Mud=45,Soil=210,SoilRich=11,WaterShallow=30";
        const string terrain39 = "Granite_Rough=880,MarshyTerrain=103,Mud=80,Soil=102,SoilRich=67,WaterShallow=289";
        Check(string.Join(",", terrain) == (originalPocket ? terrain31 : terrain39), "Historical terrain assignment survived");
        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        if (originalPocket)
        {
            Check(cave.IsPocketMap && GuPocketProbeComponent.TryGetSite(exit, out var legacy)
                && legacy.Cavern == cave && GuPocketProbeComponent.HasValidRoute(legacy), "Historical native pocket and route still resolve");
        }
        else
        {
            AssertNativeAddress(cave);
            Check(GuPocketProbeComponent.TryGetIndependentEndpoint(exit, out var site, out var endpoint)
                && endpoint.Resolve(site) == cave && site.Hauling == null,
                "Historical independent level and route identity resolve with the new assignment off by default");
        }
        Report($"Historical {size}x{size} cavern loaded with its original owner, terrain, {flora.Length} Building flora, reserve, 3 adult Hope Gu and {expectedStones} stones.");
    }
}
