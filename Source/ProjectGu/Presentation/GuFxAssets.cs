using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// Main-thread cache of one material and one UV-mapped quad per atlas frame for each profile. Textures come from the
    /// native content loader and materials from the native pools, so they share the game's own lifetime. A missing
    /// texture is reported once and the profile is skipped thereafter.
    /// </summary>
    internal static class GuFxAssets
    {
        private sealed class Entry
        {
            internal Material Material;
            internal Mesh[] Frames;
            internal bool Missing;
        }

        private static readonly Dictionary<GuFxProfileDef, Entry> cache = new();

        internal static bool Draw(GuFxInstance instance, int now, GuFxPose pose)
        {
            var profile = instance.Profile;
            var entry = Resolve(profile);
            if (entry.Missing) return false;
            int age = instance.Age(now);
            // Sustained layers hold their faded-in look for as long as their owner keeps them alive.
            int envelopeAge = instance.Sustained ? Math.Min(age, Math.Max(0, profile.fadeInTicks)) : age;
            float alpha = profile.AlphaAt(envelopeAge) * profile.color.a;
            if (alpha <= 0.01f) return false;
            Material material = alpha >= 0.99f ? entry.Material : FadedMaterialPool.FadedVersionOf(entry.Material, alpha);
            float scale = profile.ScaleAt(envelopeAge) * pose.Scale * profile.PulseAt(age);
            var size = new Vector3(profile.drawSize.x * scale, 1f, profile.drawSize.y * scale);
            Quaternion rotation = profile.orientToHeading ? Quaternion.AngleAxis(pose.Heading, Vector3.up) : Quaternion.identity;
            Graphics.DrawMesh(entry.Frames[profile.FrameAt(age)], Matrix4x4.TRS(pose.Position, rotation, size), material, 0);
            return true;
        }

        private static Entry Resolve(GuFxProfileDef profile)
        {
            if (cache.TryGetValue(profile, out var entry)) return entry;
            entry = new Entry();
            cache[profile] = entry;
            var texture = ContentFinder<Texture2D>.Get(profile.texPath, false);
            if (texture == null)
            {
                entry.Missing = true;
                Log.Warning($"[Project GU] Cosmetic effect {profile.defName} has no texture at {profile.texPath}; it will not be drawn.");
                return entry;
            }
            var color = profile.color;
            color.a = 1f;
            entry.Material = MaterialPool.MatFrom(profile.texPath, profile.shaderType?.Shader ?? ShaderDatabase.MoteGlow, color);
            entry.Frames = new Mesh[profile.frameCount];
            for (int frame = 0; frame < profile.frameCount; frame++)
                entry.Frames[frame] = FrameQuad(frame, profile.columns, profile.rows);
            return entry;
        }

        /// <summary>A unit quad in the XZ plane whose UVs select one atlas cell; frame 0 is the top-left cell.</summary>
        internal static Mesh FrameQuad(int frame, int columns, int rows)
        {
            var uv = FrameUv(frame, columns, rows);
            var mesh = new Mesh
            {
                name = $"GuFxFrame{frame}of{columns}x{rows}",
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
                },
                uv = new[]
                {
                    new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax),
                    new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin)
                }
            };
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The UV rectangle of one atlas cell, with frame 0 at the top-left of the image.</summary>
        internal static Rect FrameUv(int frame, int columns, int rows)
        {
            int column = frame % columns;
            int row = frame / columns;
            float width = 1f / columns;
            float height = 1f / rows;
            return new Rect(column * width, 1f - (row + 1) * height, width, height);
        }
    }
}
