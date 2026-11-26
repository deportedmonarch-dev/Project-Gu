using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Springs
{
    /// <summary>Map-local, transient subscriber list. Never serialized; comps re-register on spawn/load.</summary>
    public sealed class GuEcologyMapComponent : MapComponent
    {
        private readonly HashSet<CompGuSpiritSpring> springs = new HashSet<CompGuSpiritSpring>();
        public GuEcologyMapComponent(Map map) : base(map) { }

        /// <summary>Alpha 5B: draws each registered spring's ambient spirits (current map only; see GuSpringAmbience).</summary>
        public override void MapComponentDraw()
        {
            ProjectGu.Presentation.GuSpringAmbience.BeginFrame();
            foreach (var spring in springs) ProjectGu.Presentation.GuSpringAmbience.Draw(spring);
        }

        internal bool ContainsSpring(CompGuSpiritSpring spring) => springs.Contains(spring);

        internal bool HasOtherSpringWithId(CompGuSpiritSpring spring) => springs.Any(other =>
            other != spring && other.parent.Spawned && other.parent.Map == map && other.HabitatId == spring.HabitatId);

        public void Register(CompGuSpiritSpring spring)
        {
            if (spring == null || !springs.Add(spring)) return;
            // Never merge cohorts by ID: spring ThingComps remain authoritative.
            foreach (CompGuSpiritSpring other in springs)
            {
                if (other == null || ReferenceEquals(other, spring)) continue;
                if (!string.IsNullOrEmpty(spring.HabitatId) &&
                    spring.HabitatId == other.HabitatId)
                    Log.ErrorOnce("[Project GU] Duplicate spring habitat ID detected: " + spring.HabitatId,
                        spring.HabitatId.GetHashCode());
                other.NotifySpringChanged(spring);
            }
        }
        public void Unregister(CompGuSpiritSpring spring)
        {
            if (!springs.Remove(spring)) return;
            NotifyHabitatRangeChanged(spring);
        }

        internal void NotifyHabitatRangeChanged(CompGuSpiritSpring spring)
        {
            foreach (CompGuSpiritSpring other in springs)
                if (other != spring) other.NotifySpringChanged(spring);
        }

        internal CompGuSpiritSpring HabitatOwnerAt(IntVec3 cell)
        {
            CompGuSpiritSpring owner = null;
            int distance = int.MaxValue;
            foreach (CompGuSpiritSpring spring in springs)
            {
                if (!spring.parent.Spawned || spring.parent.Map != map || !spring.HabitatContains(cell))
                    continue;
                int candidate = (spring.parent.Position - cell).LengthHorizontalSquared;
                if (owner == null || candidate < distance
                    || (candidate == distance && spring.parent.thingIDNumber < owner.parent.thingIDNumber))
                {
                    owner = spring;
                    distance = candidate;
                }
            }
            return owner;
        }

        public void NotifyFloraChanged(IntVec3 cell)
        {
            // Batch invalidation until a nearby spring needs its support or ticks.
            foreach (var spring in springs)
                spring.NotifyNearbyFloraChanged(cell);
        }
    }
}
