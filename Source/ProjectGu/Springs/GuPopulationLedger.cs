using System;
using System.Collections.Generic;
using ProjectGu.Domain;

namespace ProjectGu.Springs
{
    /// <summary>
    /// Species-neutral operations on the habitat-owned, save-backed cohort list.
    /// No static cache, no hard-coded species switches, no ownership of persisted state.
    /// The caller determines species suitability and reproduction policy.
    /// </summary>
    public static class GuPopulationLedger
    {
        public static GuPopulationRecord Find(IList<GuPopulationRecord> records, string speciesDefName)
        {
            if (records == null || string.IsNullOrWhiteSpace(speciesDefName)) return null;
            foreach (GuPopulationRecord record in records)
                if (record != null && string.Equals(record.SpeciesDefName, speciesDefName, StringComparison.Ordinal))
                    return record;
            return null;
        }

        public static int Mature(IList<GuPopulationRecord> records, string speciesDefName)
        {
            return Find(records, speciesDefName)?.MatureCount ?? 0;
        }

        public static bool TryConsumeMature(IList<GuPopulationRecord> records, string speciesDefName, int count)
        {
            return Find(records, speciesDefName)?.TryConsumeMature(count) ?? false;
        }

        public static bool TryAddCohort(IList<GuPopulationRecord> records, GuPopulationRecord cohort)
        {
            if (records == null || cohort == null || string.IsNullOrWhiteSpace(cohort.SpeciesDefName))
                return false;
            if (Find(records, cohort.SpeciesDefName) != null) return false;
            records.Add(cohort);
            return true;
        }

        public static bool HasDuplicateSpecies(IList<GuPopulationRecord> records)
        {
            if (records == null) return false;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (GuPopulationRecord record in records)
                if (record == null || string.IsNullOrWhiteSpace(record.SpeciesDefName) ||
                    !found.Add(record.SpeciesDefName)) return true;
            return false;
        }
    }
}
