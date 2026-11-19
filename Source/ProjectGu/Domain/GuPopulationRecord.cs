using System;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// One authoritative aggregate cohort for one Gu species in one population custodian.
    /// Not an individual Gu, a Thing, or a global pool. Persisted by its owning habitat.
    /// String species keys preserve missing optional-addon populations without dropping them.
    /// </summary>
    public sealed class GuPopulationRecord : IExposable
    {
        private string speciesDefName = "";
        private int matureCount;
        private int juvenileCount;
        private int nextEcologyTick = -1;

        public string SpeciesDefName => speciesDefName;
        public int MatureCount => matureCount;
        public int JuvenileCount => juvenileCount;
        public long TotalCount => (long)matureCount + juvenileCount;
        public int NextEcologyTick => nextEcologyTick;

        // Parameterless constructor is required by RimWorld's deep Scribe loading.
        public GuPopulationRecord() { }

        public GuPopulationRecord(string species, int mature, int juveniles, int nextTick)
        {
            if (string.IsNullOrWhiteSpace(species)) throw new ArgumentException("Species cannot be empty.", nameof(species));
            if (mature < 0 || juveniles < 0) throw new ArgumentOutOfRangeException(nameof(mature), "Population counts cannot be negative.");
            speciesDefName = species;
            matureCount = mature;
            juvenileCount = juveniles;
            nextEcologyTick = nextTick;
        }

        public bool TryConsumeMature(int count)
        {
            if (count <= 0 || matureCount < count) return false;
            matureCount -= count;
            return true;
        }

        public void AdvanceDaily(int capacity, bool canReproduce, int birthsPerDay)
        {
            capacity = Math.Max(0, capacity);
            int freeAdultSpace = Math.Max(0, capacity - matureCount);
            int matured = Math.Min(juvenileCount, freeAdultSpace);
            matureCount += matured;
            juvenileCount -= matured;

            if (canReproduce && birthsPerDay > 0)
            {
                long freeSpace = Math.Max(0, capacity - TotalCount);
                juvenileCount += (int)Math.Min(freeSpace, birthsPerDay);
            }
        }

        public void SetNextEcologyTick(int tick) { nextEcologyTick = tick; }

        public void ExposeData()
        {
            Scribe_Values.Look(ref speciesDefName, "speciesDefName", "");
            Scribe_Values.Look(ref matureCount, "mature", 0);
            Scribe_Values.Look(ref juvenileCount, "juveniles", 0);
            Scribe_Values.Look(ref nextEcologyTick, "nextEcologyTick", -1);
        }
    }
}
