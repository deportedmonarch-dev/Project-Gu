using System.Collections.Generic;
using ProjectGu.Domain;
using ProjectGu.Interface;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Sustenance
{
    /// <summary>
    /// Routine feeding: a Gu Master with the policy enabled feeds their own hungry, refined Gu when food is available.
    /// Inserted before the colonist's main behavior, so it is considered only when the pawn is choosing a new job: it never
    /// interrupts a running job such as a cultivation session, and drafted, downed or mentally broken pawns never reach it.
    /// It looks at most once every <see cref="CheckInterval"/> ticks per pawn, and not with enemies nearby.
    /// </summary>
    public sealed class JobGiver_GuTendOwnGu : ThinkNode_JobGiver
    {
        internal const int CheckInterval = 1250;
        internal const float EnemyRadius = 25f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            var component = GuSustenanceComponent.Instance;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (component == null || registry == null || pawn == null || !pawn.Spawned || pawn.Drafted || pawn.Downed
                || pawn.InMentalState || !pawn.IsColonistPlayerControlled) return null;
            int now = Find.TickManager.TicksGame;
            if (component.NextAutoTendCheck.TryGetValue(pawn.thingIDNumber, out int next) && now < next) return null;
            component.NextAutoTendCheck[pawn.thingIDNumber] = now + CheckInterval;
            if (!component.AutoTendEnabled(pawn) || GenAI.EnemyIsNear(pawn, EnemyRadius)) return null;

            foreach (var record in OwnGu(pawn, registry))
            {
                var snapshot = GuSustenance.Snapshot(registry, record);
                if (snapshot.Mode != GuSustenanceMode.Advancing || snapshot.Level < GuNourishmentLevel.Hungry) continue;
                if (GuSustenance.TryMakeFeedJob(pawn, record, true, out var job, out _, out _)) return job;
            }
            return null;
        }

        /// <summary>Refined Gu that recognize this pawn and are in its aperture or inventory.</summary>
        internal static IEnumerable<GuRecord> OwnGu(Pawn pawn, GuIndividualRegistry registry)
        {
            if (GuApertureActions.FindAperture(pawn) is { } aperture)
                foreach (var record in registry.ApertureOccupants(aperture))
                    if (registry.IsRecognizedByCultivator(record, pawn)) yield return record;
            if (pawn.inventory?.innerContainer is not { } inventory) yield break;
            for (int i = 0; i < inventory.Count; i++)
                if (inventory[i] is Thing_GuDormantProjection thing && registry.Resolve(thing) is { } record
                    && registry.IsRecognizedByCultivator(record, pawn)) yield return record;
        }
    }
}
