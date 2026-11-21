using Verse;

namespace ProjectGu.Domain
{
    internal readonly record struct GuCohortMaterializationRequest(string EventKey, string HabitatId, string SourceNativeId,
        string SpeciesDefName, long ExpectedRevision);

    internal interface IGuCohortHabitat
    {
        string HabitatId { get; }
        Thing Anchor { get; }
        bool HasUniqueIdentity { get; }
        // Return the habitat's authoritative instance without changing native state.
        GuPopulationRecord CohortFor(string speciesDefName);
    }
}
