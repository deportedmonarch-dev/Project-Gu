using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Springs
{
    /// <summary>Species-neutral habitat contribution. Species are freely defined in XML.</summary>
    public sealed class CompProperties_GuSpiritFlora : CompProperties
    {
        public int support = 1;
        public string ecologyProfile = "generic";
        public int cavernSupportUnits;
        public int cavernSupportFullAt;
        public List<TerrainDef> growthSoils = new();
        public int waterRange;
        public int minimumSpacing = 1;
        public CompProperties_GuSpiritFlora() { compClass = typeof(CompGuSpiritFlora); }

        internal long HabitatUnits(int patches, bool cavern)
        {
            if (!cavern) return (long)patches * Math.Max(0, support);
            int units = Math.Max(0, cavernSupportUnits);
            return cavernSupportFullAt > 0
                ? (long)Math.Min(patches, cavernSupportFullAt) * units / cavernSupportFullAt
                : (long)patches * units;
        }
    }
}
