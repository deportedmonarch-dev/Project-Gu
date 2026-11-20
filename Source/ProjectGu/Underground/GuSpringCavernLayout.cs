using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Underground
{
    internal enum GuCavernGround { Wall, Stone, QiSoil, ShallowWater }

    /// <summary>Seeded terrain plan. The native generator applies it only when creating a new map.</summary>
    internal sealed class GuSpringCavernLayout
    {
        internal readonly IntVec3 Arrival;
        internal readonly IntVec3 Spring;
        internal readonly CellRect Footprint;
        internal readonly HashSet<IntVec3> DryApproach;
        private readonly HashSet<IntVec3> open;
        private readonly HashSet<IntVec3> water;
        private readonly Dictionary<IntVec3, int> basinDistance;
        private readonly IntVec3 center;
        private readonly float phase;

        internal GuSpringCavernLayout(IntVec3 size, IntVec2 springSize, int seed)
        {
            var random = new Random(seed);
            center = new IntVec3(size.x / 2, 0, size.z / 2);
            phase = (float)(random.NextDouble() * Math.Tau);
            Arrival = Offset(center, -9 + random.Next(-1, 2), -10 + random.Next(-1, 2));
            Spring = Offset(center, random.Next(-1, 2), -4 + random.Next(-1, 2));
            Footprint = GenAdj.OccupiedRect(Spring, Rot4.North, springSize);
            DryApproach = MakeDryApproach(Arrival, Footprint);

            var westOutcrop = Offset(center, -10 + random.Next(-1, 2), 8 + random.Next(-1, 2));
            var eastOutcrop = Offset(center, 11 + random.Next(-1, 2), 8 + random.Next(-1, 2));
            var chamber = new HashSet<IntVec3>(DryApproach);
            foreach (IntVec3 cell in new CellRect(2, 2, size.x - 4, size.z - 4).Cells)
            {
                float x = cell.x - center.x;
                float z = cell.z - center.z;
                float radius = x * x / 294f + z * z / 247f;
                if (radius < 1f + 0.19f * Ripple(x, z)
                    && !InEllipse(cell, westOutcrop, 1.9f, 2.5f)
                    && !InEllipse(cell, eastOutcrop, 2.1f, 1.8f))
                    chamber.Add(cell);
            }

            var mainPool = Offset(Spring, random.Next(-1, 2), 8);
            var outlets = SouthernOutputs(Footprint).ToArray();
            var inlet = new CellRect(Footprint.minX, Footprint.maxZ + 2, Footprint.Width, mainPool.z - Footprint.maxZ - 1);
            chamber.UnionWith(inlet.Cells);
            open = Connected(Arrival, chamber);
            water = new HashSet<IntVec3>();
            AddPool(mainPool, 8.5f + (float)random.NextDouble() * 0.6f, 6.6f + (float)random.NextDouble() * 0.5f, inlet.Cells);
            AddPool(Offset(center, -12, -3 + random.Next(-1, 2)), 3.3f, 4.0f);
            AddPool(Offset(center, 12, -3 + random.Next(-1, 2)), 3.2f, 3.9f);
            AddPool(Offset(center, 7 + random.Next(-1, 2), -10), 2.5f, 2.8f);
            basinDistance = MainBasinDistances(Footprint, open.Contains, water.Contains);

            if (DryApproach.Any(cell => !open.Contains(cell))
                || outlets.Any(cell => !water.Contains(cell))
                || !Connected(Arrival, open.Where(cell => !Footprint.Contains(cell)).ToHashSet()).IsSupersetOf(outlets))
                throw new InvalidOperationException("The spirit spring cavern has no connected approach or basin outlets.");
        }

        internal GuCavernGround GroundAt(IntVec3 cell)
        {
            if (!open.Contains(cell)) return GuCavernGround.Wall;
            if (DryApproach.Contains(cell)) return GuCavernGround.QiSoil;
            if (water.Contains(cell)) return GuCavernGround.ShallowWater;
            if (basinDistance.GetValueOrDefault(cell, int.MaxValue) <= 3) return GuCavernGround.QiSoil;

            float x = cell.x - center.x;
            float z = cell.z - center.z;
            float ripple = Ripple(x, z);
            return x * x / 294f + z * z / 247f + ripple * 0.12f > 0.71f
                ? GuCavernGround.Stone : GuCavernGround.QiSoil;
        }

        internal static IEnumerable<IntVec3> SouthernOutputs(CellRect footprint)
        {
            for (int x = footprint.minX; x <= footprint.maxX; x++)
                yield return new IntVec3(x, 0, footprint.maxZ + 2);
        }

        internal static IEnumerable<IntVec3> SouthernShore(CellRect footprint) =>
            SouthernOutputs(footprint).Select(cell => cell + IntVec3.South);

        private void AddPool(IntVec3 origin, float width, float depth, IEnumerable<IntVec3> inlet = null)
        {
            var candidates = new HashSet<IntVec3>();
            int radius = (int)MathF.Ceiling(MathF.Max(width, depth) * 1.2f);
            foreach (IntVec3 cell in CellRect.CenteredOn(origin, radius).Cells)
            {
                if (!open.Contains(cell) || DryApproach.Contains(cell) || water.Contains(cell)
                    || GenAdj.CardinalDirections.Any(offset => water.Contains(cell + offset)))
                    continue;
                float x = (cell.x - origin.x) / width;
                float z = (cell.z - origin.z) / depth;
                float shoreline = Ripple(cell.x - center.x, cell.z - center.z);
                if (x * x + z * z < 1f + 0.12f * shoreline)
                    candidates.Add(cell);
            }
            if (inlet != null) candidates.UnionWith(inlet);
            water.UnionWith(Connected(origin, candidates));
        }

        internal static Dictionary<IntVec3, int> MainBasinDistances(CellRect footprint, Func<IntVec3, bool> inBounds,
            Func<IntVec3, bool> isWater)
        {
            var distances = new Dictionary<IntVec3, int>();
            var queue = new Queue<IntVec3>();
            foreach (var outlet in SouthernOutputs(footprint))
                if (inBounds(outlet) && isWater(outlet) && distances.TryAdd(outlet, 0))
                    queue.Enqueue(outlet);
            while (queue.TryDequeue(out IntVec3 cell))
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + offset;
                    if (inBounds(next) && isWater(next) && distances.TryAdd(next, 0))
                        queue.Enqueue(next);
                }

            foreach (IntVec3 cell in distances.Keys) queue.Enqueue(cell);
            while (queue.TryDequeue(out IntVec3 cell))
            {
                int distance = distances[cell] + 1;
                if (distance > 6) continue;
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                    if (inBounds(cell + offset) && distances.TryAdd(cell + offset, distance))
                        queue.Enqueue(cell + offset);
            }
            return distances;
        }

        internal static float BasinPlantingScore(IntVec3 cell, IntVec3 spring, int seed, Dictionary<IntVec3, int> shore)
        {
            int distance = shore.GetValueOrDefault(cell, 7);
            float variation = MathF.Sin(cell.x * 0.58f + cell.z * 0.28f + seed % 97)
                + 0.6f * MathF.Cos(cell.z * 0.66f - cell.x * 0.33f);
            return (distance - 1) * 13f + MathF.Sqrt((cell - spring).LengthHorizontalSquared) * 0.95f + variation * 9f;
        }

        internal static HashSet<IntVec3> MakeDryApproach(IntVec3 arrival, CellRect footprint)
        {
            var dry = new HashSet<IntVec3>(footprint.ExpandedBy(1).Cells);
            dry.UnionWith(CellRect.CenteredOn(arrival, 1).Cells);
            dry.UnionWith(new CellRect(arrival.x + 2, arrival.z - 2, 5, 4).Cells);
            int turnZ = (arrival.z + footprint.minZ) / 2;
            dry.UnionWith(new CellRect(arrival.x, arrival.z, 2, turnZ - arrival.z + 1).Cells);
            dry.UnionWith(new CellRect(arrival.x, turnZ, footprint.minX - arrival.x + 2, 2).Cells);
            dry.UnionWith(new CellRect(footprint.minX, turnZ, 2, footprint.minZ - turnZ + 1).Cells);
            return dry;
        }

        private static HashSet<IntVec3> Connected(IntVec3 origin, HashSet<IntVec3> cells)
        {
            var visited = new HashSet<IntVec3>();
            if (!cells.Contains(origin)) return visited;
            var queue = new Queue<IntVec3>();
            queue.Enqueue(origin);
            visited.Add(origin);
            while (queue.TryDequeue(out IntVec3 cell))
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                    if (cells.Contains(cell + offset) && visited.Add(cell + offset))
                        queue.Enqueue(cell + offset);
            return visited;
        }

        private static IntVec3 Offset(IntVec3 cell, int x, int z) => cell + new IntVec3(x, 0, z);

        private static bool InEllipse(IntVec3 cell, IntVec3 origin, float width, float depth)
        {
            float x = (cell.x - origin.x) / width;
            float z = (cell.z - origin.z) / depth;
            return x * x + z * z < 1f;
        }

        private float Ripple(float x, float z) => MathF.Sin(x * 0.53f + z * 0.27f + phase)
            + 0.52f * MathF.Cos(z * 0.81f - x * 0.31f + phase);
    }
}
