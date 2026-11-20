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
        public List<TerrainDef> growthSoils = new();
        public int waterRange;
        public int minimumSpacing = 1;
        public CompProperties_GuSpiritFlora() { compClass = typeof(CompGuSpiritFlora); }
    }
}
