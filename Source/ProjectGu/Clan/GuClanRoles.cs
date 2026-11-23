using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Clan
{
    public enum GuClanRole { None, Elder, Disciple, MortalWorker }

    /// <summary>
    /// Clan roles are social positions shown as health-tab markers. They are separate from
    /// cultivation state: a disciple stays a disciple whether or not awakening succeeds.
    /// </summary>
    public static class GuClanRoles
    {
        public const string ElderDefName = "GU_ClanElder";
        public const string DiscipleDefName = "GU_ClanDisciple";
        public const string WorkerDefName = "GU_ClanMortalWorker";

        private static HediffDef Def(GuClanRole role) => role switch
        {
            GuClanRole.Elder => DefDatabase<HediffDef>.GetNamedSilentFail(ElderDefName),
            GuClanRole.Disciple => DefDatabase<HediffDef>.GetNamedSilentFail(DiscipleDefName),
            GuClanRole.MortalWorker => DefDatabase<HediffDef>.GetNamedSilentFail(WorkerDefName),
            _ => null
        };

        public static GuClanRole RoleOf(Pawn pawn)
        {
            var hediffs = pawn?.health?.hediffSet;
            if (hediffs == null) return GuClanRole.None;
            foreach (GuClanRole role in new[] { GuClanRole.Elder, GuClanRole.Disciple, GuClanRole.MortalWorker })
                if (Def(role) is { } def && hediffs.HasHediff(def)) return role;
            return GuClanRole.None;
        }

        public static void SetRole(Pawn pawn, GuClanRole role)
        {
            if (pawn?.health == null) return;
            foreach (GuClanRole other in new[] { GuClanRole.Elder, GuClanRole.Disciple, GuClanRole.MortalWorker })
            {
                if (other == role || Def(other) is not { } def) continue;
                Hediff present = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (present != null) pawn.health.RemoveHediff(present);
            }
            if (Def(role) is { } wanted && !pawn.health.hediffSet.HasHediff(wanted))
                pawn.health.AddHediff(HediffMaker.MakeHediff(wanted, pawn));
        }

        /// <summary>
        /// The oldest adult becomes elder. The youngest remaining colonists old enough to train
        /// become disciples. Everyone else is a mortal worker.
        /// </summary>
        public static void Assign(List<Pawn> colonists, int discipleCount, int discipleMinAge)
        {
            var humans = colonists.Where(p => p?.RaceProps?.Humanlike == true && !p.Dead).ToList();
            GuClanRole[] roles = PlanRoles(humans.Select(p => p.ageTracker.AgeBiologicalYearsFloat).ToList(),
                discipleCount, discipleMinAge);
            for (int i = 0; i < humans.Count; i++) SetRole(humans[i], roles[i]);
        }

        /// <summary>Pure role selection by biological age. Ties keep the original colonist order.</summary>
        public static GuClanRole[] PlanRoles(IReadOnlyList<float> ages, int discipleCount, int discipleMinAge)
        {
            var roles = new GuClanRole[ages.Count];
            if (ages.Count == 0) return roles;
            int elder = Enumerable.Range(0, ages.Count).OrderByDescending(i => ages[i]).First();
            roles[elder] = GuClanRole.Elder;
            var disciples = Enumerable.Range(0, ages.Count)
                .Where(i => i != elder && ages[i] >= discipleMinAge)
                .OrderBy(i => ages[i]).Take(System.Math.Max(0, discipleCount)).ToHashSet();
            for (int i = 0; i < ages.Count; i++)
                if (i != elder) roles[i] = disciples.Contains(i) ? GuClanRole.Disciple : GuClanRole.MortalWorker;
            return roles;
        }

        /// <summary>
        /// A clan only recruits disciples who showed promise, so a gifted youth always has an
        /// ordinary aperture. Provisional weights: D45 C35 B15 A5. Not canon probabilities.
        /// </summary>
        public static int GiftedAptitude()
        {
            int n = Rand.RangeInclusive(1, 100);
            if (n <= 45) return Rand.RangeInclusive(20, 39);
            if (n <= 80) return Rand.RangeInclusive(40, 59);
            if (n <= 95) return Rand.RangeInclusive(60, 79);
            return Rand.RangeInclusive(80, 99);
        }
    }
}
