using System.Collections.Generic;
using Verse;

namespace ProjectGu.Springs
{
    /// <summary>Map-local, transient subscriber list. Never serialized; comps re-register on spawn/load.</summary>
    public sealed class GuEcologyMapComponent : MapComponent
    {
        private readonly HashSet<CompGuSpiritSpring> springs = new HashSet<CompGuSpiritSpring>();
        public GuEcologyMapComponent(Map map) : base(map) { }
        public void Register(CompGuSpiritSpring spring)
        {
            if (spring == null) return;
            // Never merge cohorts by ID: spring ThingComps remain authoritative.
            foreach (CompGuSpiritSpring other in springs)
            {
                if (other == null || ReferenceEquals(other, spring)) continue;
                if (!string.IsNullOrEmpty(spring.HabitatId) &&
                    spring.HabitatId == other.HabitatId)
                    Log.ErrorOnce("[Project GU] Duplicate spring habitat ID detected: " + spring.HabitatId,
                        spring.HabitatId.GetHashCode());
            }
            springs.Add(spring);
        }
        public void Unregister(CompGuSpiritSpring spring) { if (spring != null) springs.Remove(spring); }
        public void NotifyFloraChanged(IntVec3 cell)
        {
            // One event per changed plant; only nearby springs recalculate. No per-tick map scan.
            foreach (var spring in springs)
                spring.NotifyNearbyFloraChanged(cell);
        }
    }
}
