using System;
using System.Collections.Generic;
using ProjectGu.Springs;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// One kind of ambient spirit drawn around a spring, scaled from that spring's authoritative cohort. Declared on the
    /// spring's <see cref="CompProperties_GuSpiritSpring"/>. Purely cosmetic: no Thing, pawn, record or selection exists.
    /// </summary>
    public sealed class GuAmbientSpirits
    {
        public Domain.GuSpeciesDef species;
        public string texPath = "ProjectGU/Effects/HopeSpirit";
        public Color color = new Color(0.78f, 0.92f, 1f, 0.85f);
        /// <summary>Drawn spirits per living individual in the cohort (mature and juvenile), before the cap.</summary>
        public float perIndividual = 1f;
        public int maxSpirits = 14;
        /// <summary>Cells from the spring's centre within which spirits drift.</summary>
        public float radius = 3.6f;
        public float minRadius = 0.7f;
        public FloatRange size = new FloatRange(0.5f, 0.8f);
        /// <summary>Radians per second of slow orbit.</summary>
        public FloatRange orbitSpeed = new FloatRange(0.05f, 0.16f);
    }

    /// <summary>
    /// Draws luminous Hope Gu spirits drifting around spirit springs. The count comes from the spring's cohort (mature plus
    /// juvenile), so a depleted spring looks emptier and an empty one shows none. Ambient spirits are not individuals: they
    /// have no identity, AI, pathing, hunger or selection, and they never touch cohort counts. Drawing happens only for springs
    /// registered on the current map, only when the spring is on screen and not fogged, and only with the presentation setting
    /// on. Cost is a handful of mesh submissions per visible spring per frame; nothing runs per tick.
    /// </summary>
    internal static class GuSpringAmbience
    {
        private static readonly Dictionary<string, Material> materials = new();
        private static readonly HashSet<string> missing = new();

        internal static int LastFrameSpirits { get; private set; }

        /// <summary>Spirits this spring shows for one profile: the cohort size scaled and capped. Pure; used by the tests.</summary>
        internal static int SpiritCount(CompGuSpiritSpring spring, GuAmbientSpirits profile)
        {
            if (spring == null || profile?.species == null) return 0;
            int living = spring.MatureCountFor(profile.species.defName) + spring.JuvenileCountFor(profile.species.defName);
            if (living <= 0) return 0;
            return Math.Min(profile.maxSpirits, Math.Max(1, Mathf.CeilToInt(living * profile.perIndividual)));
        }

        internal static void BeginFrame() => LastFrameSpirits = 0;

        internal static void Draw(CompGuSpiritSpring spring)
        {
            if (ProjectGuMod.Settings?.ambientSpirits == false || spring?.parent is not { Spawned: true } anchor) return;
            var profiles = spring.AmbientProfiles;
            if (profiles == null || profiles.Count == 0) return;
            var map = anchor.Map;
            if (map != Find.CurrentMap || anchor.Position.Fogged(map)) return;
            Vector3 centre = anchor.TrueCenter();
            var view = Find.CameraDriver.CurrentViewRect;
            foreach (var profile in profiles)
            {
                int count = SpiritCount(spring, profile);
                if (count == 0) continue;
                int pad = Mathf.CeilToInt(profile.radius) + 1;
                if (!view.ExpandedBy(pad).Contains(anchor.Position)) continue;
                var material = MaterialFor(profile);
                if (material == null) continue;
                float time = Time.realtimeSinceStartup;
                int seed = (spring.HabitatId ?? "").GetHashCode() ^ profile.species.shortHash;
                for (int i = 0; i < count; i++)
                {
                    // Deterministic per-spirit parameters, so each spirit keeps its own path frame to frame.
                    float u1 = Hash01(seed, i, 1), u2 = Hash01(seed, i, 2), u3 = Hash01(seed, i, 3), u4 = Hash01(seed, i, 4);
                    float speed = Mathf.Lerp(profile.orbitSpeed.min, profile.orbitSpeed.max, u3) * (u4 < 0.5f ? -1f : 1f);
                    float angle = u1 * Mathf.PI * 2f + time * speed;
                    float r = Mathf.Lerp(profile.minRadius, profile.radius, Mathf.Sqrt(u2)) + 0.35f * Mathf.Sin(time * 0.4f + u4 * 9f);
                    // A slow figure-eight wobble and a gentle bob read as floating rather than orbiting.
                    float wobbleX = 0.28f * Mathf.Sin(time * 0.7f + u3 * 13f);
                    float wobbleZ = 0.22f * Mathf.Sin(time * 1.1f + u1 * 17f) + 0.12f * Mathf.Sin(time * 1.9f + u2 * 5f);
                    var pos = new Vector3(centre.x + Mathf.Cos(angle) * r + wobbleX, AltitudeLayer.MoteOverhead.AltitudeFor() + i * 0.0005f,
                        centre.z + Mathf.Sin(angle) * r * 0.8f + wobbleZ);
                    float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(time * (1.3f + u2) + u1 * 20f));
                    float alpha = Mathf.Clamp01(profile.color.a * pulse);
                    if (alpha < 0.02f) continue;
                    float size = Mathf.Lerp(profile.size.min, profile.size.max, u4) * (0.9f + 0.1f * pulse);
                    var mat = alpha >= 0.99f ? material : FadedMaterialPool.FadedVersionOf(material, alpha);
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size)), mat, 0);
                    LastFrameSpirits++;
                }
            }
        }

        private static Material MaterialFor(GuAmbientSpirits profile)
        {
            if (profile.texPath.NullOrEmpty() || missing.Contains(profile.texPath)) return null;
            string key = profile.texPath + "|" + profile.color;
            if (materials.TryGetValue(key, out var material)) return material;
            if (ContentFinder<Texture2D>.Get(profile.texPath, false) == null)
            {
                missing.Add(profile.texPath);
                Log.Warning($"[Project GU] Ambient spirit texture {profile.texPath} is missing; spring spirits will not be drawn.");
                return null;
            }
            var color = profile.color;
            color.a = 1f;
            material = MaterialPool.MatFrom(profile.texPath, ShaderDatabase.MoteGlow, color);
            materials[key] = material;
            return material;
        }

        private static float Hash01(int seed, int index, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)(index + 1) * 2246822519u ^ (uint)salt * 3266489917u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
