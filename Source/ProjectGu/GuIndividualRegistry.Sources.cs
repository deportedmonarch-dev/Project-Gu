using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Springs;
using ProjectGu.Underground;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        /// <summary>The established spring as a population source for the shared transaction. Owns no state.</summary>
        internal static IGuPopulationSource ForSpring(IGuCohortHabitat habitat) => habitat == null ? null : new SpringPopulationSource(habitat);

        /// <summary>
        /// Legacy adapter (RFC-0007): established springs keep owning their cohort rows, habitat ID, anchor and rules.
        /// The adapter only exposes them to the shared transaction. The credential is the anchor's load ID, which is the
        /// same native object across saves and is what historical records name as <see cref="GuRecord.OriginNativeId"/>.
        /// Placement keeps the shipped rules: within six cells of the anchor, on a player-accessible map for player
        /// encounters, or a protected projection route for Development Mode draws.
        /// </summary>
        private sealed class SpringPopulationSource : IGuPopulationSource
        {
            internal const string Kind = "spring";
            private readonly IGuCohortHabitat habitat;

            internal SpringPopulationSource(IGuCohortHabitat habitat) { this.habitat = habitat; }

            public string OwnerKind => Kind;
            public string SourceId => habitat.HabitatId;
            public string CurrentCredential => habitat.Anchor?.GetUniqueLoadID();
            public bool HasUniqueIdentity => habitat.HasUniqueIdentity;
            public bool RequiresDevelopmentMode(string trigger) => trigger == GuSourceTriggers.Development;
            public GuPopulationRecord CohortFor(string speciesDefName) => habitat.CohortFor(speciesDefName);

            public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, string assetScope)
            {
                if (stage != GuCohortStage.Mature || rank != species.minRank) return "A spring issues only mature Gu at the species' wild rank.";
                switch (trigger)
                {
                    case GuSourceTriggers.Development:
                        return assetScope == null ? null : "A development draw from a spring carries no lure.";
                    case GuSourceTriggers.SpringSearch:
                        if (habitat is not CompGuSpiritSpring) return "Wild Gu can only be encountered from a live spirit spring.";
                        return assetScope == null ? null : "A spring search carries no lure.";
                    case GuSourceTriggers.SpringLure:
                        if (habitat is not CompGuSpiritSpring) return "Wild Gu can only be encountered from a live spirit spring.";
                        return species.UsesLureEncounter && species.encounterLures.Any(lure => lure?.defName == assetScope)
                            ? null : "That lure does not attract this species.";
                    default:
                        return "This spring does not authorize that encounter trigger.";
                }
            }

            // The shipped spring wording, kept exactly.
            public string PlacementRefusal => "Choose an open cell within six cells of the exact habitat on a protected map.";

            public bool PlacementReady(Map map, IntVec3 cell, string trigger)
            {
                if (habitat.Anchor is not { } anchor || !IsMapOwned(anchor) || anchor.Map != map
                    || anchor.Position.DistanceToSquared(cell) > 36) return false;
                return trigger == GuSourceTriggers.Development
                    ? Current.Game.GetComponent<GuPocketProbeComponent>()?.HasProtectedProjectionRoute(map) == true
                    : CompGuSpiritSpring.PlayerCanAccessEncounterMap(map);
            }
        }
    }
}
