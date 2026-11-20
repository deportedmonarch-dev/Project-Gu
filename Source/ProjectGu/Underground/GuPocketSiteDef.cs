using System;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GuPocketSiteDef : Def
    {
        public MapGeneratorDef generator;
        public ThingDef endpoint;
        public ThingDef returnEndpoint;
        public int entranceClearance;
        public int mapSize = 31;

        internal ThingDef ExitDef => returnEndpoint ?? endpoint;

        internal static GuPocketSiteDef Probe =>
            DefDatabase<GuPocketSiteDef>.GetNamedSilentFail("GU_PocketProbe");

        internal static GuPocketSiteDef SpiritSpring =>
            DefDatabase<GuPocketSiteDef>.GetNamedSilentFail("GU_SpiritSpringCavern");

        internal static bool IsSpringCavern(Map map) => map?.IsPocketMap == true
            && SpiritSpring?.generator is MapGeneratorDef generator && map.generatorDef == generator;

        internal bool IsConfigured => generator?.pocketMapProperties?.biome != null
            && !generator.pocketMapProperties.destroyOnParentMapAbandoned
            && endpoint?.thingClass == typeof(Building_GuPocketProbeEndpoint)
            && ExitDef?.thingClass == typeof(Building_GuPocketProbeEndpoint)
            && entranceClearance is >= 0 and <= 2
            && mapSize >= 11 && mapSize <= 65;
    }

    public sealed class GenStep_GuPocketProbe : GenStep
    {
        public TerrainDef floor;
        public ThingDef boundaryRock;
        public ThingDef exit;

        public override int SeedPart => 672031;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (floor == null || boundaryRock == null || exit == null)
                throw new InvalidOperationException("The probe chamber requires a floor, boundary rock and exit.");

            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, floor);
                if (cell.x != 0 && cell.z != 0 && cell.x != map.Size.x - 1 && cell.z != map.Size.z - 1)
                    continue;
                GuPocketProbeComponent.SpawnFixture(boundaryRock, cell, map);
            }
            // Spawn the exit last. Its presence is required to commit the generated level.
            GuPocketProbeComponent.SpawnFixture(exit, map.Center, map);
        }
    }
}
