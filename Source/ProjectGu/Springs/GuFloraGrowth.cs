using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Springs
{
    internal static class GuFloraGrowth
    {
        internal static int Fill(GuCavernFloraTarget target, IntVec3 spring, int radius, int seed,
            int existing, int budget, Func<IntVec3, TerrainDef> terrainAt, Func<IntVec3, bool> canOccupy,
            Func<IntVec3, bool> hasSameSpecies, Func<IntVec3, bool> trySpawn)
        {
            int remaining = Math.Min(target.targetPatches - existing, budget);
            if (remaining <= 0) return 0;

            var species = target.flora.GetCompProperties<CompProperties_GuSpiritFlora>();
            var random = new Random(seed);
            // Bias toward the spring while letting seeded variation break up the rings.
            var cells = CellRect.CenteredOn(spring, radius).Cells
                .OrderBy(cell => random.Next(100) + (cell - spring).LengthHorizontalSquared);
            int planted = 0;
            foreach (IntVec3 cell in cells)
            {
                if (!SuitableGround(species, cell, terrainAt) || !canOccupy(cell)
                    || NearSameSpecies(cell, species.minimumSpacing, hasSameSpecies)
                    || !trySpawn(cell))
                    continue;
                if (++planted == remaining) break;
            }
            return planted;
        }

        internal static bool SuitableGround(CompProperties_GuSpiritFlora species, IntVec3 cell,
            Func<IntVec3, TerrainDef> terrainAt)
        {
            TerrainDef ground = terrainAt(cell);
            return ground != null && species.growthSoils.Contains(ground) && NearWater(cell, species.waterRange, terrainAt);
        }

        private static bool NearWater(IntVec3 cell, int range, Func<IntVec3, TerrainDef> terrainAt)
        {
            range = Math.Clamp(range, 0, 6);
            if (range == 0) return true;
            foreach (IntVec3 nearby in CellRect.CenteredOn(cell, range).Cells)
            {
                if (Math.Abs(nearby.x - cell.x) + Math.Abs(nearby.z - cell.z) > range)
                    continue;
                TerrainDef terrain = terrainAt(nearby);
                if (terrain?.IsWater == true && terrain.passability != Traversability.Impassable)
                    return true;
            }
            return false;
        }

        private static bool NearSameSpecies(IntVec3 cell, int spacing, Func<IntVec3, bool> hasSameSpecies)
        {
            int range = Math.Clamp(spacing - 1, 0, 4);
            return range > 0 && CellRect.CenteredOn(cell, range).Cells.Any(hasSameSpecies);
        }

        internal static int EffectiveSupport(long units, int unitsPerPoint, int maximum) =>
            units <= 0 || maximum <= 0 ? 0 : (int)Math.Clamp(units / Math.Max(1, unitsPerPoint), 1, maximum);

        internal static int HopeCapacity(int support, int configuredMaximum) =>
            (int)Math.Clamp(3L + 2L * support, 3, Math.Max(3, configuredMaximum));
    }
}
