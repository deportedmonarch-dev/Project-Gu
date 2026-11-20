using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ProjectGu.Underground
{
    /// <summary>The engine's map addresses. Travel and geographic location are recorded separately.</summary>
    public sealed class GuUndergroundAddressLayer : PlanetLayer
    {
        public GuUndergroundAddressLayer() { }

        public GuUndergroundAddressLayer(int id, PlanetLayerDef def, float radius, Vector3 origin, Vector3 center,
            float viewAngle, int subdivisions, float extraCameraAltitude, float cameraOffset, float parallax)
            : base(id, def, radius, origin, center, viewAngle, subdivisions, extraCameraAltitude, cameraOffset, parallax)
        {
        }

        public override bool Visible => false;
        public override bool CanReachLayer => false;
        public override AcceptanceReport CanSelectLayer() => false;

        internal static GuUndergroundAddressLayer Resolve(WorldGrid grid, bool create)
        {
            var def = DefDatabase<PlanetLayerDef>.GetNamed("GU_UndergroundAddresses");
            var matches = grid.PlanetLayers.Values.Where(layer => layer.Def == def).Take(2).ToArray();
            if (matches.Length == 1 && matches[0] is GuUndergroundAddressLayer restored && restored.IsReady)
                return restored;
            if (matches.Length != 0)
                throw new InvalidOperationException("Project GU underground address layer is ambiguous or incomplete.");
            if (!create) return null;
            if (!GuUndergroundLifecycleGuards.Installed)
                throw new InvalidOperationException("Project GU underground lifecycle protections are unavailable.");
            return Register(grid, def);
        }

        internal static GuUndergroundAddressLayer Register(WorldGrid grid, PlanetLayerDef def)
        {
            var created = (GuUndergroundAddressLayer)grid.RegisterPlanetLayer(def, null, 100f, 180f, 0f, 1, 0f, 0f,
                Vector3.forward);
            // The native tile step sets up biome data. The surface world generator is never invoked.
            new WorldGenStep_Tiles().GenerateFresh("", created);
            if (!created.IsReady)
                throw new InvalidOperationException("Project GU underground address layer has no usable tiles.");
            return created;
        }

        private bool IsReady => LayerID > 0 && TilesCount > 1 && Tiles.Count == TilesCount;

        internal bool Owns(PlanetTile address) => address.Valid && InBounds(address)
            && ReferenceEquals(address.Layer, this) && this[address] != null;

        internal PlanetTile AvailableAddress()
        {
            for (int i = 0; i < TilesCount; i++)
            {
                PlanetTile address = PlanetTileForID(i);
                if (Current.Game.FindMap(address) == null && !Find.WorldObjects.AnyWorldObjectAt(address))
                    return address;
            }
            return PlanetTile.Invalid;
        }
    }

    public sealed class GuUndergroundAddressTile : Tile
    {
        public GuUndergroundAddressTile(PlanetTile address) : base(address)
        {
            // Both initial tile generation and native save restoration use this constructor.
            temperature = 19f;
        }
    }
}
