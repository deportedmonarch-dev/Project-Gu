using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// Map-scoped, transient cosmetic effects. Gameplay code reports facts it has already committed (a launch, an impact);
    /// this component only decides how they look. Nothing here is saved, read back by gameplay or able to cancel a
    /// gameplay outcome. Instances expire by simulation ticks on every map, are culled to the camera view before drawing,
    /// and are bounded per map. A reload starts with no historical effects; live projectiles may re-attach their accent.
    /// </summary>
    public sealed class GuFxMapComponent : MapComponent
    {
        internal const int MaxInstances = 64;

        private readonly List<GuFxInstance> active = new();

        public GuFxMapComponent(Map map) : base(map) { }

        internal int ActiveCount => active.Count;
        internal IReadOnlyList<GuFxInstance> Active => active;
        internal int TotalSpawned { get; private set; }
        internal int TotalRetired { get; private set; }
        internal int TotalEvicted { get; private set; }
        internal int LastDrawSubmissions { get; private set; }

        /// <summary>Global cosmetic switch. Gameplay never reads it.</summary>
        internal static bool EffectsEnabled => ProjectGuMod.Settings?.cosmeticEffects ?? true;

        /// <summary>Schedules one effect at a fixed position, or attached to a live anchor. Returns false when skipped.</summary>
        internal static bool TrySpawn(Map map, GuFxProfileDef profile, Vector3 position, float heading, Thing anchor = null)
        {
            if (!EffectsEnabled || profile == null || map == null || map.Disposed) return false;
            if (anchor != null && (!anchor.Spawned || anchor.Map != map)) return false;
            return map.GetComponent<GuFxMapComponent>()?.Add(profile, position, heading, anchor) == true;
        }

        private bool Add(GuFxProfileDef profile, Vector3 position, float heading, Thing anchor)
        {
            if (active.Count >= MaxInstances)
            {
                active.RemoveAt(0);
                TotalEvicted++;
            }
            active.Add(new GuFxInstance(profile, Find.TickManager.TicksGame, position, heading, anchor));
            TotalSpawned++;
            return true;
        }

        public override void MapComponentTick()
        {
            if (active.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (!active[i].Expired(now, map)) continue;
                active.RemoveAt(i);
                TotalRetired++;
            }
        }

        public override void MapComponentDraw()
        {
            LastDrawSubmissions = 0;
            if (active.Count == 0 || !EffectsEnabled) return;
            int now = Find.TickManager.TicksGame;
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
            for (int i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (instance.Expired(now, map)) continue;
                Vector3 center = instance.CurrentPosition();
                if (!view.Contains(center.ToIntVec3())) continue;
                if (GuFxAssets.Draw(instance, now, center)) LastDrawSubmissions++;
            }
        }

        public override void MapRemoved()
        {
            TotalRetired += active.Count;
            active.Clear();
        }

        /// <summary>Retires every instance immediately, for example when cosmetic effects are switched off.</summary>
        internal void Clear()
        {
            TotalRetired += active.Count;
            active.Clear();
        }
    }

    internal sealed class GuFxInstance
    {
        internal readonly GuFxProfileDef Profile;
        internal readonly int StartTick;
        private readonly Vector3 position;
        private readonly float heading;
        internal readonly Thing Anchor;

        internal GuFxInstance(GuFxProfileDef profile, int startTick, Vector3 position, float heading, Thing anchor)
        {
            Profile = profile;
            StartTick = startTick;
            this.position = position;
            this.heading = heading;
            Anchor = anchor;
        }

        internal int Age(int now) => now - StartTick;

        internal bool Expired(int now, Map map) => Age(now) >= Profile.lifetimeTicks
            || Anchor != null && (Anchor.Destroyed || !Anchor.Spawned || Anchor.Map != map);

        /// <summary>Heading in degrees; attached instances follow their anchor's current heading.</summary>
        internal float Heading => Anchor is IGuFxHeadingSource source ? source.FxHeading : heading;

        internal Vector3 CurrentPosition()
        {
            Vector3 basePosition = Anchor != null ? Anchor.DrawPos : position;
            if (Profile.forwardOffset != 0f)
                basePosition += Quaternion.AngleAxis(Heading, Vector3.up) * new Vector3(0f, 0f, Profile.forwardOffset);
            basePosition.y = Profile.altitudeLayer.AltitudeFor();
            return basePosition;
        }
    }

    /// <summary>An anchor that reports its current heading in degrees around the vertical axis.</summary>
    internal interface IGuFxHeadingSource
    {
        float FxHeading { get; }
    }
}
