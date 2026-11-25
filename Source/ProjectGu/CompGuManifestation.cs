using System.Collections.Generic;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public sealed class CompProperties_GuManifestation : CompProperties
    {
        public CompProperties_GuManifestation() => compClass = typeof(CompGuManifestation);
    }

    /// <summary>
    /// Identity tag of a native Gu manifestation (RFC-0004 §5.1). It saves the V1 projection keys, so legacy
    /// projections migrate in place. A tag never authorizes itself. The registry decides which tagged object is
    /// the authorized manifestation.
    /// </summary>
    public sealed class CompGuManifestation : ThingComp
    {
        // Filled during save loading and consumed by the registry in LoadedGame. Cleared when a load starts.
        internal static readonly List<CompGuManifestation> LoadBuffer = new();

        private string guId;
        private int guRevision;
        // Where this object last left a map. Native state, not canonical: used as evidence once the map is gone.
        private int lastTile = -1;
        private int lastLayer = -1;
        private IntVec3 lastCell = IntVec3.Invalid;
        private string lastMapId;

        public string GuId => guId;
        public int GuRevision => guRevision;
        // Transient: location captured by the projection class just before native destruction removes its holder.
        private GuLocation endingLocation;

        internal void CaptureEndingLocation()
        {
            if (!string.IsNullOrEmpty(guId)) endingLocation = GuIndividualRegistry.LocationOf(parent);
        }

        internal GuLocation TakeEndingLocation()
        {
            var location = endingLocation;
            endingLocation = null;
            return location;
        }

        internal bool HasLastLocation => lastTile >= 0;

        internal GuLocation LastLocation(int tick) =>
            GuLocation.FromTile(lastTile, lastLayer, lastCell, lastMapId, null, tick);

        /// <summary>Transient. Set by the registry when this tag claims an ID without being the authorized manifestation.</summary>
        internal bool Rogue { get; set; }

        internal void Bind(GuRecord record)
        {
            guId = record.Id;
            guRevision = record.CustodyRevision;
            Rogue = false;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref guId, "guIndividualId");
            Scribe_Values.Look(ref guRevision, "guBindingRevision");
            Scribe_Values.Look(ref lastTile, "guLastTile", -1);
            Scribe_Values.Look(ref lastLayer, "guLastLayer", -1);
            Scribe_Values.Look(ref lastCell, "guLastCell", IntVec3.Invalid);
            Scribe_Values.Look(ref lastMapId, "guLastMapId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && (!string.IsNullOrEmpty(guId) || parent is Pawn_GuWildProjection)) LoadBuffer.Add(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (map == null) return;
            var location = GuLocation.Capture(map, parent.Position, null, -1);
            lastTile = location.Tile;
            lastLayer = location.Layer;
            lastCell = parent.Position;
            lastMapId = map.GetUniqueLoadID();
        }

        // Native lifecycle hooks only enqueue. The registry applies events after the native callback returns.
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null) => Report(GuTerminalEvidence.Killed);

        public override void PostDestroy(DestroyMode mode, Map previousMap) =>
            Report(mode == DestroyMode.KillFinalize ? GuTerminalEvidence.Killed : GuTerminalEvidence.Destroyed);

        public override void Notify_MapRemoved() => Report(GuTerminalEvidence.MapRemoved);

        private void Report(GuTerminalEvidence evidence)
        {
            if (!string.IsNullOrEmpty(guId))
                Current.Game?.GetComponent<GuIndividualRegistry>()?.EnqueueProjectionEvent(parent, evidence);
        }
    }

    internal interface IGuProjection
    {
        string GuId { get; }
        int GuRevision { get; }
    }

    internal static class GuManifestationExtensions
    {
        internal static CompGuManifestation GuTag(this Thing thing) =>
            thing is ThingWithComps withComps ? withComps.GetComp<CompGuManifestation>() : null;
    }
}
