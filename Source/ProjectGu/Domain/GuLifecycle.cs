using ProjectGu.Underground;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Structured world location captured from the native graph before teardown (owner decisions §4). It records actual
    /// world state. What the player knows about it is a separate, future concern.
    /// </summary>
    public sealed class GuLocation : IExposable
    {
        private int tile = -1;
        private int layer = -1;
        private IntVec3 cell = IntVec3.Invalid;
        private string mapId;
        private string mapParentId;
        private string mapParentDef;
        private string undergroundLevel;
        private string legacySourceParent;
        private string holderId;
        private int tick = -1;

        public int Tile => tile;
        public int Layer => layer;
        public IntVec3 Cell => cell;
        public string MapId => mapId;
        public string MapParentId => mapParentId;
        public string MapParentDef => mapParentDef;
        internal string UndergroundLevel => undergroundLevel;
        internal string LegacySourceParent => legacySourceParent;
        public string HolderId => holderId;
        public int Tick => tick;
        public bool HasTile => tile >= 0;

        public GuLocation() { }

        internal static GuLocation Capture(Map map, IntVec3 cell, string holderId, int tick)
        {
            var location = new GuLocation { cell = cell, holderId = holderId, tick = tick };
            if (map != null)
            {
                var tile = SiteTile(map);
                location.tile = tile.tileId;
                location.layer = tile.Valid ? tile.Layer?.LayerID ?? -1 : -1;
                location.mapId = map.GetUniqueLoadID();
                location.mapParentId = map.Parent?.GetUniqueLoadID();
                location.mapParentDef = map.Parent?.def?.defName;
                location.undergroundLevel = (map.Parent as GuUndergroundLevelParent)?.LevelId;
                // Legacy pocket teardown retains egress to its exact native source, including a regenerated source map.
                location.legacySourceParent = (map.Parent as PocketMapParent)?.sourceMap?.Parent?.GetUniqueLoadID();
            }
            return location;
        }

        internal static GuLocation FromTile(int tile, int layer, IntVec3 cell, string mapId, string holderId, int tick,
            string parentId = null, string parentDef = null, string level = null, string sourceParent = null) => new()
        {
            tile = tile, layer = layer, cell = cell, mapId = mapId, holderId = holderId, tick = tick,
            mapParentId = parentId, mapParentDef = parentDef, undergroundLevel = level, legacySourceParent = sourceParent
        };

        private static PlanetTile SiteTile(Map map) => !map.Tile.Valid && map.Parent is PocketMapParent { sourceMap: { } source }
            ? source.Tile : map.Tile;

        /// <summary>Checks the original native locality, including the parent and layer across map regeneration.</summary>
        internal bool IsAt(Map map)
        {
            if (map is not { Disposed: false, Parent: { Spawned: true } parent } || parent.Map != map || !Find.Maps.Contains(map)) return false;
            var address = SiteTile(map);
            if (!address.Valid || address.tileId != tile || address.Layer?.LayerID != layer) return false;
            string parentId = parent.GetUniqueLoadID();
            if (legacySourceParent == parentId) return true;
            return mapParentId == parentId && mapParentDef == parent.def?.defName
                && undergroundLevel == (parent as GuUndergroundLevelParent)?.LevelId;
        }

        public string Describe() => (HasTile ? $"tile {tile} layer {layer}" : "no tile")
            + (cell.IsValid ? $" cell {cell}" : "") + (mapId == null ? "" : $" map {mapId}")
            + (mapParentId == null ? "" : $" ({mapParentDef} {mapParentId})") + (holderId == null ? "" : $" held by {holderId}")
            + (tick < 0 ? "" : $" at tick {tick}");

        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref layer, "layer", -1);
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Values.Look(ref mapId, "mapId");
            Scribe_Values.Look(ref mapParentId, "mapParentId");
            Scribe_Values.Look(ref mapParentDef, "mapParentDef");
            Scribe_Values.Look(ref undergroundLevel, "undergroundLevel");
            Scribe_Values.Look(ref legacySourceParent, "legacySourceParent");
            Scribe_Values.Look(ref holderId, "holderId");
            Scribe_Values.Look(ref tick, "tick", -1);
        }
    }

    public enum GuLifecycleEventKind
    {
        Unresolved = 1,
        Terminal = 2,
        Retired = 3,
        Abstracted = 4,
        Recovered = 5,
        HostDied = 6,
        Operator = 7,
        Extracted = 8,
        Escaped = 9
    }

    /// <summary>One significant lifecycle fact with its evidence. Ordinary physical movement is never recorded.</summary>
    public sealed class GuLifecycleEvent : IExposable
    {
        private int tick = -1;
        private GuLifecycleEventKind kind;
        private string cause;
        private string eventKey;
        private string actorId;
        private GuLocation location;
        private string detail;

        public int Tick => tick;
        public GuLifecycleEventKind Kind => kind;
        public string Cause => cause;
        public string EventKey => eventKey;
        public string ActorId => actorId;
        public GuLocation Location => location;
        public string Detail => detail;

        public GuLifecycleEvent() { }

        internal GuLifecycleEvent(int tick, GuLifecycleEventKind kind, string cause, string eventKey, string actorId,
            GuLocation location, string detail)
        {
            this.tick = tick;
            this.kind = kind;
            this.cause = cause;
            this.eventKey = eventKey;
            this.actorId = actorId;
            this.location = location;
            this.detail = detail;
        }

        /// <summary>Drops narrative detail and location while keeping the fact, cause and key (retention, owner decisions §5).</summary>
        internal void Summarize()
        {
            detail = null;
            location = null;
        }

        public string Describe() => $"{kind} {cause} at {tick}" + (eventKey == null ? "" : $" key {eventKey}")
            + (actorId == null ? "" : $" by {actorId}") + (location == null ? "" : $" @ {location.Describe()}")
            + (detail == null ? "" : $". {detail}");

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", -1);
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref cause, "cause");
            Scribe_Values.Look(ref eventKey, "eventKey");
            Scribe_Values.Look(ref actorId, "actorId");
            Scribe_Deep.Look(ref location, "location");
            Scribe_Values.Look(ref detail, "detail");
        }
    }
}
