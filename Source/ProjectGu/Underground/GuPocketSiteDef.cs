using System;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GuPocketSiteDef : Def
    {
        public MapGeneratorDef generator;
        public ThingDef endpoint;
        public int mapSize = 31;

        internal static GuPocketSiteDef Probe =>
            DefDatabase<GuPocketSiteDef>.GetNamedSilentFail("GU_PocketProbe");

        internal bool IsConfigured => generator?.pocketMapProperties?.biome != null
            && !generator.pocketMapProperties.destroyOnParentMapAbandoned
            && endpoint?.thingClass == typeof(Building_GuPocketProbeEndpoint)
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
                Thing rock = GenSpawn.Spawn(boundaryRock, cell, map);
                if (rock?.Spawned != true || rock.Map != map || rock.Position != cell)
                    throw new InvalidOperationException($"The probe boundary failed to spawn at {cell}.");
            }
            // Spawn the exit last. Its presence is required to commit the generated level.
            GenSpawn.Spawn(exit, map.Center, map);
        }
    }
}
