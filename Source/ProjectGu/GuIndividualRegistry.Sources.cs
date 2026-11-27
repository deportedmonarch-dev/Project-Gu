using System.Collections.Generic;
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

        /// <summary>The spring sources a map's ecology component holds under a habitat ID (its <see cref="IGuPopulationSourceDirectory"/>).</summary>
        internal static void CollectSpringSources(IEnumerable<CompGuSpiritSpring> springs, string ownerKind, string sourceId,
            List<IGuPopulationSource> into)
        {
            if (ownerKind != SpringPopulationSource.Kind || string.IsNullOrEmpty(sourceId)) return;
            foreach (var spring in springs)
                if (spring?.parent?.Spawned == true && spring.HabitatId == sourceId) into.Add(new SpringPopulationSource(spring));
        }

        /// <summary>
        /// Legacy adapter (RFC-0007): established springs keep owning their cohort rows, habitat ID, anchor and rules.
        /// The adapter only exposes them to the shared transaction; its binding is the spring component, which the map's
        /// ecology component registers. The credential is the anchor's load ID, which is the same native object across saves
        /// and is what historical records name as <see cref="GuRecord.OriginNativeId"/>. Placement keeps the shipped rules:
        /// within six cells of the anchor, on a player-accessible map for player encounters, or a protected projection route
        /// for Development Mode draws.
        /// </summary>
        private sealed class SpringPopulationSource : IGuPopulationSource
        {
            internal const string Kind = "spring";
            private readonly IGuCohortHabitat habitat;

            internal SpringPopulationSource(IGuCohortHabitat habitat) { this.habitat = habitat; }

            public string OwnerKind => Kind;
            public string SourceId => habitat.HabitatId;
            public object Binding => habitat;
            public string CurrentCredential => habitat.Anchor?.GetUniqueLoadID();
            public bool HasUniqueIdentity => habitat.HasUniqueIdentity;
            public bool RequiresDevelopmentMode(string trigger) => trigger == GuSourceTriggers.Development;
            public GuPopulationRecord CohortFor(string speciesDefName) => habitat.CohortFor(speciesDefName);

            public string RefuseIssue(GuSpeciesDef species, int rank, GuCohortStage stage, string trigger, IGuSourceAsset asset)
            {
                if (stage != GuCohortStage.Mature || rank != species.minRank) return "A spring issues only mature Gu at the species' wild rank.";
                switch (trigger)
                {
                    case GuSourceTriggers.Development:
                        return asset == null ? null : "A development draw from a spring carries no lure.";
                    case GuSourceTriggers.SpringSearch:
                        if (habitat is not CompGuSpiritSpring) return "Wild Gu can only be encountered from a live spirit spring.";
                        // Lure-only species are authorized here, at the source boundary, not merely hidden from the search command.
                        if (species.UsesLureEncounter)
                            return $"A {species.label} answers only a lure. Set out {string.Join(" or ", species.encounterLures.Select(lure => lure.label))} at the spring with a colonist.";
                        return asset == null ? null : "A spring search carries no lure.";
                    case GuSourceTriggers.SpringLure:
                        if (habitat is not CompGuSpiritSpring spring) return "Wild Gu can only be encountered from a live spirit spring.";
                        if (asset is not GuHeldLureAsset lure || lure.Spring != spring)
                            return "A lure needs the bait in a colonist's hands at this spring.";
                        return species.UsesLureEncounter && species.encounterLures.Any(def => def?.defName == lure.Scope)
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
