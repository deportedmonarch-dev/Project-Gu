using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Presentation
{
    /// <summary>
    /// One cosmetic layer: a sprite atlas plus timing, size and placement conventions. Profiles have no gameplay meaning.
    ///
    /// Atlas convention: frames are laid out left to right, then top to bottom, in a grid of <see cref="columns"/> by
    /// <see cref="rows"/> equal cells. Frame 0 is the top-left cell. Each cell's pivot is its center. Art faces "up"
    /// (toward the top of the image) in the direction of travel; the presenter rotates it to the projectile heading.
    /// Use straight (non-premultiplied) alpha with transparent padding around the shape.
    /// </summary>
    public sealed class GuFxProfileDef : Def
    {
        [NoTranslate] public string texPath;
        public int columns = 1;
        public int rows = 1;
        /// <summary>Frames actually used; may be fewer than columns × rows.</summary>
        public int frameCount = 1;
        /// <summary>Simulation ticks each frame stays visible. Frames follow game time, so pause and speed apply.</summary>
        public int ticksPerFrame = 4;
        /// <summary>Total lifetime in ticks. Attached layers also end when their anchor ends.</summary>
        public int lifetimeTicks = 16;
        /// <summary>Loop frames for the whole lifetime instead of holding the last frame.</summary>
        public bool loop;
        /// <summary>World size in cells (x = across, y = along the heading).</summary>
        public Vector2 drawSize = Vector2.one;
        /// <summary>Scale multiplier at the start and end of the lifetime.</summary>
        public float startScale = 1f;
        public float endScale = 1f;
        /// <summary>Ticks to fade in from transparent and fade out to transparent.</summary>
        public int fadeInTicks;
        public int fadeOutTicks;
        public Color color = Color.white;
        public ShaderTypeDef shaderType;
        public AltitudeLayer altitudeLayer = AltitudeLayer.MoteOverhead;
        /// <summary>Offset along the heading in cells; negative values trail behind an attached anchor.</summary>
        public float forwardOffset;
        /// <summary>Rotate to the event heading. When false the sprite is drawn upright.</summary>
        public bool orientToHeading = true;

        public int FrameAt(int age)
        {
            int frame = ticksPerFrame <= 0 ? 0 : age / ticksPerFrame;
            if (loop) return frameCount <= 0 ? 0 : frame % frameCount;
            return Mathf.Clamp(frame, 0, frameCount - 1);
        }

        public float AlphaAt(int age)
        {
            float alpha = 1f;
            if (fadeInTicks > 0 && age < fadeInTicks) alpha = Mathf.Min(alpha, (age + 1f) / fadeInTicks);
            int remaining = lifetimeTicks - age;
            if (fadeOutTicks > 0 && remaining < fadeOutTicks) alpha = Mathf.Min(alpha, Mathf.Max(0f, remaining) / fadeOutTicks);
            return Mathf.Clamp01(alpha);
        }

        public float ScaleAt(int age) =>
            Mathf.Lerp(startScale, endScale, lifetimeTicks <= 1 ? 1f : Mathf.Clamp01(age / (float)(lifetimeTicks - 1)));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (texPath.NullOrEmpty()) yield return $"{defName}: texPath is required";
            if (columns is < 1 or > 16 || rows is < 1 or > 16) yield return $"{defName}: columns and rows must be between 1 and 16";
            if (frameCount < 1 || frameCount > columns * rows) yield return $"{defName}: frameCount must be between 1 and columns × rows";
            if (ticksPerFrame is < 1 or > 600) yield return $"{defName}: ticksPerFrame must be between 1 and 600";
            if (lifetimeTicks is < 1 or > 2500) yield return $"{defName}: lifetimeTicks must be between 1 and 2500";
            if (drawSize.x <= 0f || drawSize.y <= 0f || drawSize.x > 20f || drawSize.y > 20f)
                yield return $"{defName}: drawSize components must be in (0, 20]";
        }
    }
}
