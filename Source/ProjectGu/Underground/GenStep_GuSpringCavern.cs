using System;
using System.Linq;
using ProjectGu.Springs;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GenStep_GuSpringCavern : GenStep
    {
        public TerrainDef qiSoil;
        public TerrainDef shallowWater;
        public ThingDef rock;
        public ThingDef spring;
        public ThingDef exit;

        public override int SeedPart => 840763;

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef stone = rock?.building?.naturalTerrain;
            var springProps = spring?.GetCompProperties<CompProperties_GuSpiritSpring>();
            if (springProps == null || exit == null
                || new[] { qiSoil, shallowWater, stone }
                    .Any(terrain => terrain == null || terrain.passability == Traversability.Impassable))
                throw new InvalidOperationException("The spirit spring cavern requires walkable terrain and all fixture Defs.");

            // The native pocket generator seeds Rand per pocket parent and GenStep.
            var layout = new GuSpringCavernLayout(map.Size, spring.size, Rand.Int);
            foreach (IntVec3 cell in map.AllCells)
            {
                GuCavernGround ground = layout.GroundAt(cell);
                map.terrainGrid.SetTerrain(cell, ground switch
                {
                    GuCavernGround.Wall or GuCavernGround.Stone => stone,
                    GuCavernGround.ShallowWater => shallowWater,
                    _ => qiSoil
                });
                if (ground == GuCavernGround.Wall)
                    GuPocketProbeComponent.SpawnFixture(rock, cell, map);
            }

            Thing core = GuPocketProbeComponent.SpawnFixture(spring, layout.Spring, map);
            core.TryGetComp<CompGuSpiritSpring>().InitializeCavernFlora(Rand.Int, layout.Arrival);
            // Keep the exit last so a swallowed GenStep failure cannot commit an incomplete level.
            GuPocketProbeComponent.SpawnFixture(exit, layout.Arrival, map);
        }
    }
}
