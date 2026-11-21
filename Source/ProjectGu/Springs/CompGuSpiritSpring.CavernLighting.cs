using System.Collections.Generic;
using System.Linq;
using ProjectGu.Underground;
using Verse;

namespace ProjectGu.Springs
{
    public sealed partial class CompGuSpiritSpring
    {
        private static readonly IntVec3[] BankLightOffsets =
        {
            new IntVec3(-8, 0, 5), new IntVec3(8, 0, 5),
            new IntVec3(-5, 0, 14), new IntVec3(5, 0, 14)
        };

        private readonly List<int> fixedGlowers = new();
        private readonly Dictionary<IntVec3, int> orchidGlowers = new();

        private void RefreshCavernLighting(Map map, List<IntVec3> orchids)
        {
            if (fixedGlowers.Count == 0)
            {
                fixedGlowers.Add(map.glowGrid.RegisterCustomGlower(parent.Position, 8.5f, new ColorInt(130, 199, 235), 0f, false));
                IntVec3 garden = cavernApproach + new IntVec3(4, 0, 0);
                if (garden.InBounds(map) && garden.Standable(map))
                    fixedGlowers.Add(map.glowGrid.RegisterCustomGlower(garden, 8f, new ColorInt(76, 95, 94), 0f, false));
                var shore = GuSpringCavernLayout.MainBasinDistances(parent.OccupiedRect(), cell => cell.InBounds(map),
                    cell => cell.GetTerrain(map).IsWater);
                var used = new HashSet<IntVec3>();
                foreach (IntVec3 offset in BankLightOffsets)
                {
                    IntVec3 origin = parent.Position + offset;
                    IntVec3 cell = shore.Where(entry => entry.Value is 1 or 2
                            && entry.Key.Standable(map) && !entry.Key.GetTerrain(map).IsWater && !used.Contains(entry.Key))
                        .Select(entry => entry.Key).OrderBy(candidate => (candidate - origin).LengthHorizontalSquared)
                        .FirstOrDefault(IntVec3.Invalid);
                    if (!cell.IsValid || !used.Add(cell)) continue;
                    fixedGlowers.Add(map.glowGrid.RegisterCustomGlower(cell, 9f, new ColorInt(59, 86, 97), 0f, false));
                }
            }

            foreach (IntVec3 cell in orchidGlowers.Keys.Where(cell => !orchids.Contains(cell)).ToArray())
            {
                map.glowGrid.DeregisterCustomGlower(orchidGlowers[cell]);
                orchidGlowers.Remove(cell);
            }
            foreach (IntVec3 cell in orchids)
                if (!orchidGlowers.ContainsKey(cell))
                    orchidGlowers.Add(cell, map.glowGrid.RegisterCustomGlower(cell, 4.5f, new ColorInt(93, 97, 157), 0f, false));
        }

        private void ClearCavernLighting(Map map)
        {
            if (map != null)
            {
                foreach (int handle in fixedGlowers) map.glowGrid.DeregisterCustomGlower(handle);
                foreach (int handle in orchidGlowers.Values) map.glowGrid.DeregisterCustomGlower(handle);
            }
            fixedGlowers.Clear();
            orchidGlowers.Clear();
        }
    }
}
