using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Springs;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class GenStep_GuSpringCavern : GenStep
    {
        public TerrainDef soil;
        public TerrainDef richSoil;
        public TerrainDef marsh;
        public TerrainDef mud;
        public TerrainDef shallowWater;
        public ThingDef rock;
        public ThingDef spring;
        public ThingDef moonOrchid;
        public ThingDef spiritGrass;
        public ThingDef exit;

        public override int SeedPart => 840763;

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef stone = rock?.building?.naturalTerrain;
            var springProps = spring?.GetCompProperties<CompProperties_GuSpiritSpring>();
            if (stone == null || springProps == null || moonOrchid == null || spiritGrass == null || exit == null
                || new[] { soil, richSoil, marsh, mud, shallowWater, stone }
                    .Any(terrain => terrain == null || terrain.passability == Traversability.Impassable))
                throw new InvalidOperationException("The spirit spring cavern requires walkable terrain and all fixture Defs.");

            // The native pocket generator seeds Rand per pocket parent and GenStep.
            IntVec3 center = map.Center;
            IntVec3 arrival = center + new IntVec3(-6 + Rand.RangeInclusive(-1, 1), 0, -8);
            IntVec3 basin = center + new IntVec3(Rand.RangeInclusive(-2, 2), 0, Rand.RangeInclusive(-1, 1));
            CellRect footprint = GenAdj.OccupiedRect(basin, Rot4.North, spring.size);
            var dry = MakeDryApproach(arrival, footprint);
            var waterOutputs = new HashSet<IntVec3>(CompGuSpiritSpring.BasinWaterOutputs(footprint));
            float phase = Rand.Range(0f, 6.28f);
            var open = new HashSet<IntVec3>(map.AllCells.Where(cell =>
                dry.Contains(cell) || waterOutputs.Contains(cell) || IsOpen(cell, center, map.Size, phase)));
            KeepConnectedTo(arrival, open);

            // Rank whole patches by wetness. The dry approach is excluded from wet terrain.
            var wet = map.AllCells.Where(cell => open.Contains(cell) && !dry.Contains(cell))
                .OrderByDescending(cell => waterOutputs.Contains(cell))
                .ThenByDescending(cell => Wetness(cell, basin, phase)).ToList();
            int exposed = open.Count - footprint.Width * footprint.Height;
            int waterCount = (int)MathF.Round(exposed * 0.08f);
            int mudEnd = waterCount + (int)MathF.Round(exposed * 0.12f);
            int fertileEnd = mudEnd + (int)MathF.Round(exposed * 0.17f);
            var terrainByCell = new Dictionary<IntVec3, TerrainDef>();
            for (int i = 0; i < wet.Count && i < fertileEnd; i++)
                terrainByCell[wet[i]] = i < waterCount ? shallowWater
                    : i < mudEnd ? mud
                    : Wetness(wet[i], basin, phase) > 0.33f ? marsh : richSoil;

            foreach (IntVec3 cell in wet.Skip(fertileEnd)
                .OrderByDescending(cell => Ripple(cell.x - center.x, cell.z - center.z, phase))
                .Take((int)MathF.Round(exposed * 0.08f)))
                terrainByCell[cell] = stone;

            var floraCells = wet.Where(cell =>
                    Math.Abs(cell.x - basin.x) <= springProps.habitatRadius
                    && Math.Abs(cell.z - basin.z) <= springProps.habitatRadius
                    && (!terrainByCell.TryGetValue(cell, out TerrainDef terrain) || terrain.fertility >= 1f))
                .OrderByDescending(cell => terrainByCell.ContainsKey(cell))
                .Take(4).ToList();
            if (floraCells.Count != 4 || !dry.All(open.Contains))
                throw new InvalidOperationException("The spirit spring cavern has no connected approach or habitat sites.");

            foreach (IntVec3 cell in map.AllCells)
            {
                if (!open.Contains(cell))
                {
                    map.terrainGrid.SetTerrain(cell, stone);
                    GuPocketProbeComponent.SpawnFixture(rock, cell, map);
                    continue;
                }
                map.terrainGrid.SetTerrain(cell, terrainByCell.TryGetValue(cell, out TerrainDef terrain) ? terrain : soil);
            }

            // Plant the habitat first so the ordinary spring spawn hook sees all support.
            for (int i = 0; i < floraCells.Count; i++)
                GuPocketProbeComponent.SpawnFixture(i == 0 ? moonOrchid : spiritGrass, floraCells[i], map);
            GuPocketProbeComponent.SpawnFixture(spring, basin, map);
            // Keep the exit last so a swallowed GenStep failure cannot commit an incomplete level.
            GuPocketProbeComponent.SpawnFixture(exit, arrival, map);
        }

        private static HashSet<IntVec3> MakeDryApproach(IntVec3 arrival, CellRect footprint)
        {
            var dry = new HashSet<IntVec3>(footprint.ExpandedBy(1).Cells);
            foreach (IntVec3 cell in new CellRect(arrival.x - 1, arrival.z - 1, 3, 3).Cells)
                dry.Add(cell);

            int turnZ = (arrival.z + footprint.minZ) / 2;
            int approachX = footprint.minX;
            foreach (IntVec3 cell in new CellRect(arrival.x, arrival.z, 2, turnZ - arrival.z + 1).Cells)
                dry.Add(cell);
            foreach (IntVec3 cell in new CellRect(arrival.x, turnZ, approachX - arrival.x + 2, 2).Cells)
                dry.Add(cell);
            foreach (IntVec3 cell in new CellRect(approachX, turnZ, 2, footprint.minZ - turnZ + 1).Cells)
                dry.Add(cell);
            return dry;
        }

        private static bool IsOpen(IntVec3 cell, IntVec3 center, IntVec3 size, float phase)
        {
            if (cell.x < 2 || cell.z < 2 || cell.x >= size.x - 2 || cell.z >= size.z - 2)
                return false;
            float x = cell.x - center.x;
            float z = cell.z - center.z;
            float radius = x * x / 156f + z * z / 132f;
            float ripple = Ripple(x, z, phase);
            return radius < 0.95f + 0.21f * ripple && (radius < 0.4f || ripple < 1.1f);
        }

        private static float Wetness(IntVec3 cell, IntVec3 basin, float phase)
        {
            float x = cell.x - basin.x;
            float z = cell.z - basin.z;
            float bend = MathF.Sin(z * 0.60f + phase);
            float eastPool = 1f - ((x - 5f - bend) * (x - 5f - bend) / 32f + (z - 1f) * (z - 1f) / 45f);
            float westPool = 0.90f - ((x + 4f) * (x + 4f) / 20f + (z - 4f) * (z - 4f) / 19f);
            float channel = 0.91f - MathF.Abs(x - 1f - 2f * bend) * 0.24f
                - MathF.Abs(z - 7f) * 0.09f;
            float outerPatch = 0.59f - ((x + 6f) * (x + 6f) + (z + 4f) * (z + 4f)) / 28f;
            return MathF.Max(MathF.Max(eastPool, westPool), MathF.Max(channel, outerPatch))
                + 0.10f * Ripple(x, z, phase);
        }

        private static float Ripple(float x, float z, float phase) =>
            MathF.Sin(x * 0.53f + z * 0.27f + phase)
            + 0.52f * MathF.Cos(z * 0.81f - x * 0.31f + phase);

        private static void KeepConnectedTo(IntVec3 arrival, HashSet<IntVec3> open)
        {
            var visited = new HashSet<IntVec3> { arrival };
            var queue = new Queue<IntVec3>();
            queue.Enqueue(arrival);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + offset;
                    if (open.Contains(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            open.IntersectWith(visited);
        }
    }
}
