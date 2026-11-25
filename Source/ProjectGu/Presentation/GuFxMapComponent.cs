using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// Map-scoped, transient cosmetic effects. Gameplay code reports facts it has already committed (a launch, an impact,
    /// an active channel); this component only decides how they look. Nothing here is saved, read back by gameplay or able
    /// to cancel a gameplay outcome. Timed instances expire by simulation ticks on every map; sustained instances live only
    /// while their owner's keep-alive check holds and their anchor stays on this map. All are culled to the camera view
    /// before drawing and bounded per map. A reload starts empty; live gameplay state re-attaches what it needs.
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

        /// <summary>Schedules one timed effect at a fixed position, or attached to a live anchor. Returns false when skipped.</summary>
        internal static bool TrySpawn(Map map, GuFxProfileDef profile, Vector3 position, float heading, Thing anchor = null)
        {
            if (!EffectsEnabled || profile == null || map == null || map.Disposed) return false;
            if (anchor != null && (!anchor.Spawned || anchor.Map != map)) return false;
            return map.GetComponent<GuFxMapComponent>()?.Add(new GuFxInstance(profile, Find.TickManager.TicksGame, position, heading, anchor)) == true;
        }

        /// <summary>
        /// Attaches a sustained effect to a live anchor under a unique key. A key that is already present is kept, so
        /// repeated calls never duplicate the effect. The instance ends as soon as <paramref name="keepAlive"/> fails.
        /// </summary>
        internal static bool TryAttachSustained(Map map, GuFxProfileDef profile, Thing anchor, string key, Func<bool> keepAlive)
        {
            if (!EffectsEnabled || profile == null || map == null || map.Disposed || anchor is not { Spawned: true } || anchor.Map != map
                || string.IsNullOrEmpty(key) || keepAlive == null || !keepAlive()) return false;
            var component = map.GetComponent<GuFxMapComponent>();
            if (component == null) return false;
            if (component.FindKeyed(key) != null) return true;
            return component.Add(new GuFxInstance(profile, Find.TickManager.TicksGame, anchor.DrawPos, 0f, anchor, key, keepAlive));
        }

        internal GuFxInstance FindKeyed(string key)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i].Key == key) return active[i];
            return null;
        }

        internal int CountKeyed(string key)
        {
            int count = 0;
            for (int i = 0; i < active.Count; i++)
                if (active[i].Key == key) count++;
            return count;
        }

        /// <summary>Retires the sustained instance with this key immediately.</summary>
        internal void Detach(string key)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Key != key) continue;
                active.RemoveAt(i);
                TotalRetired++;
            }
        }

        private bool Add(GuFxInstance instance)
        {
            if (active.Count >= MaxInstances)
            {
                active.RemoveAt(0);
                TotalEvicted++;
            }
            active.Add(instance);
            TotalSpawned++;
            return true;
        }

        public override void MapComponentTick()
        {
            if (active.Count == 0) return;
            if (!EffectsEnabled)
            {
                Clear();
                return;
            }
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
                var pose = instance.PoseAt(now);
                if (!view.Contains(pose.Position.ToIntVec3())) continue;
                if (GuFxAssets.Draw(instance, now, pose)) LastDrawSubmissions++;
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

    /// <summary>Where and how one instance is drawn this frame.</summary>
    internal readonly struct GuFxPose
    {
        internal readonly Vector3 Position;
        internal readonly float Heading;
        internal readonly float Scale;
        internal readonly bool Front;

        internal GuFxPose(Vector3 position, float heading, float scale, bool front)
        {
            Position = position;
            Heading = heading;
            Scale = scale;
            Front = front;
        }
    }

    internal sealed class GuFxInstance
    {
        internal readonly GuFxProfileDef Profile;
        internal readonly int StartTick;
        private readonly Vector3 position;
        private readonly float heading;
        internal readonly Thing Anchor;
        internal readonly string Key;
        private readonly Func<bool> keepAlive;

        internal GuFxInstance(GuFxProfileDef profile, int startTick, Vector3 position, float heading, Thing anchor,
            string key = null, Func<bool> keepAlive = null)
        {
            Profile = profile;
            StartTick = startTick;
            this.position = position;
            this.heading = heading;
            Anchor = anchor;
            Key = key;
            this.keepAlive = keepAlive;
        }

        internal bool Sustained => keepAlive != null;

        internal int Age(int now) => now - StartTick;

        internal bool Expired(int now, Map map)
        {
            if (Anchor != null && (Anchor.Destroyed || !Anchor.Spawned || Anchor.Map != map)) return true;
            if (!Sustained) return Age(now) >= Profile.lifetimeTicks;
            try { return !keepAlive(); }
            catch (Exception exception)
            {
                Log.WarningOnce($"[Project GU] A cosmetic keep-alive check failed; the effect is removed. {exception}", 0x6A55A13);
                return true;
            }
        }

        /// <summary>Heading in degrees; attached instances follow their anchor's current heading.</summary>
        internal float Heading => Anchor is IGuFxHeadingSource source ? source.FxHeading : heading;

        internal GuFxPose PoseAt(int now)
        {
            if (Profile.orbit && Anchor != null)
            {
                var sample = Profile.OrbitAt(Age(now));
                Vector3 anchorPos = Anchor.DrawPos;
                var orbitPos = new Vector3(anchorPos.x + sample.Offset.x, anchorPos.y + sample.AltitudeOffset, anchorPos.z + sample.Offset.y);
                return new GuFxPose(orbitPos, sample.Heading, sample.Scale, sample.Front);
            }

            float currentHeading = Heading;
            Vector3 basePosition = Anchor != null ? Anchor.DrawPos : position;
            if (Profile.forwardOffset != 0f)
                basePosition += Quaternion.AngleAxis(currentHeading, Vector3.up) * new Vector3(0f, 0f, Profile.forwardOffset);
            basePosition.y = Profile.altitudeLayer.AltitudeFor();
            return new GuFxPose(basePosition, currentHeading, 1f, true);
        }
    }

    /// <summary>An anchor that reports its current heading in degrees around the vertical axis.</summary>
    internal interface IGuFxHeadingSource
    {
        float FxHeading { get; }
    }
}
