using System.Collections.Generic;
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
        public int cavernHabitatRadius = 9;
        public int cavernGrowthIntervalTicks = 30000;
        public int cavernSupportUnitsPerPoint = 5;
        public int cavernMaxFloraSupport = 7;
        public List<GuCavernFloraTarget> cavernFlora = new();
        public int botanicalHabitatRadius = 16;
        public List<GuCavernFloraTarget> botanicalCavernFlora = new();
        // Extra species cohorts placed in a newly generated spring, once. Hope Gu keep their legacy initialization.
        public List<GuInitialCohort> initialCohorts = new();
        public CompProperties_GuSpiritSpring() { compClass = typeof(CompGuSpiritSpring); }
    }

    public sealed class GuInitialCohort
    {
        public ProjectGu.Domain.GuSpeciesDef species;
        public int mature;
        public int juvenile;
    }

    public sealed class GuCavernFloraTarget
    {
        public ThingDef flora;
        public int targetPatches;
    }
}
