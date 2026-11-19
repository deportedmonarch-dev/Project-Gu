using Verse;

namespace ProjectGu.Springs
{
    public sealed class CompProperties_GuSpiritSpring : CompProperties
    {
        public int productionIntervalTicks = 60000;
        public int stonesPerCycle = 1;
        public string stoneDefName = "GU_PrimevalStone";
        // Prototype preset, not a claimed automatic mapping to RimWorld geography.
        public string regionProfile = "SouthernBorder";
        public int initialReserve = 100000;
        public int purityPercent = 75;
        public int habitatRadius = 5;
        public int maxHopeGu = 30;
        public CompProperties_GuSpiritSpring() { compClass = typeof(CompGuSpiritSpring); }
    }
}
