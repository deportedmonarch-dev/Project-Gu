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
        /// <summary>Continuous rotation in degrees per tick, added to the heading. Follows game ticks, so it stops when paused.</summary>
        public float spinDegreesPerTick;
        /// <summary>Gentle breathing: scale varies by this fraction over <see cref="pulsePeriodTicks"/>. 0 disables it.</summary>
        public float pulseScale;
        public int pulsePeriodTicks = 120;

        // Orbit (pawn-attached sustained layers only). The sprite circles the anchor on an ellipse in the ground plane,
        // drawn just below the pawn's lowest render layer on the far half and just above its highest on the near half.
        public bool orbit;
        /// <summary>Ellipse radii in cells: x across the screen, y (world z) toward and away from the viewer.</summary>
        public Vector2 orbitRadius = new Vector2(0.42f, 0.16f);
        /// <summary>Offset of the orbit centre from the anchor's draw position, in cells (negative z moves it down-screen).</summary>
        public Vector2 orbitCenterOffset = new Vector2(0f, -0.12f);
        public int orbitPeriodTicks = 120;
        public bool orbitClockwise;
        /// <summary>Pawn render-layer units (RimWorld uses -10..100) for the near and far halves of the orbit.</summary>
        public float orbitFrontLayer = 100.6f;
        public float orbitBackLayer = -12.5f;
        /// <summary>Scale multiplier at the far point of the orbit; the near point is 1.</summary>
        public float orbitBackScale = 0.85f;

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

        /// <summary>Breathing multiplier at an age in ticks; 1 when pulsing is off.</summary>
        public float PulseAt(int age) => pulseScale == 0f || pulsePeriodTicks <= 0 ? 1f
            : 1f + pulseScale * Mathf.Sin(age % pulsePeriodTicks / (float)pulsePeriodTicks * 2f * Mathf.PI);

        public float ScaleAt(int age) =>
            Mathf.Lerp(startScale, endScale, lifetimeTicks <= 1 ? 1f : Mathf.Clamp01(age / (float)(lifetimeTicks - 1)));

        /// <summary>Orbit position at an age in ticks: ground-plane offset from the anchor, near or far half, tangent heading.</summary>
        public OrbitSample OrbitAt(int age)
        {
            float turn = orbitPeriodTicks <= 0 ? 0f : (age % orbitPeriodTicks) / (float)orbitPeriodTicks;
            float angle = turn * 2f * Mathf.PI * (orbitClockwise ? -1f : 1f);
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            var offset = new Vector2(orbitCenterOffset.x + orbitRadius.x * cos, orbitCenterOffset.y + orbitRadius.y * sin);
            float direction = orbitClockwise ? -1f : 1f;
            float tangentX = -orbitRadius.x * sin * direction, tangentZ = orbitRadius.y * cos * direction;
            float heading = Mathf.Atan2(tangentX, tangentZ) * Mathf.Rad2Deg;
            bool front = sin < 0f;
            float nearness = (1f - sin) * 0.5f;
            return new OrbitSample(offset, front, heading < 0f ? heading + 360f : heading,
                Mathf.Lerp(orbitBackScale, 1f, nearness), (front ? orbitFrontLayer : orbitBackLayer) * PawnLayerAltitude);
        }

        /// <summary>Altitude per pawn render layer, matching RimWorld's PawnRenderUtility.AltitudeForLayer.</summary>
        internal const float PawnLayerAltitude = 0.0003658537f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (texPath.NullOrEmpty()) yield return $"{defName}: texPath is required";
            if (columns is < 1 or > 16 || rows is < 1 or > 16) yield return $"{defName}: columns and rows must be between 1 and 16";
            if (frameCount < 1 || frameCount > columns * rows) yield return $"{defName}: frameCount must be between 1 and columns × rows";
            if (ticksPerFrame is < 1 or > 600) yield return $"{defName}: ticksPerFrame must be between 1 and 600";
            if (lifetimeTicks is < 1 or > 2500) yield return $"{defName}: lifetimeTicks must be between 1 and 2500";
            if (orbit && (orbitPeriodTicks is < 2 or > 6000 || orbitRadius.x < 0f || orbitRadius.y < 0f || orbitRadius.x > 5f || orbitRadius.y > 5f))
                yield return $"{defName}: orbit needs orbitPeriodTicks 2-6000 and radii between 0 and 5";
            if (!float.IsFinite(spinDegreesPerTick) || Mathf.Abs(spinDegreesPerTick) > 90f) yield return $"{defName}: spinDegreesPerTick must be between -90 and 90";
            if (!float.IsFinite(pulseScale) || pulseScale < 0f || pulseScale > 0.5f || pulsePeriodTicks is < 2 or > 6000)
                yield return $"{defName}: pulseScale must be 0-0.5 and pulsePeriodTicks 2-6000";
            if (drawSize.x <= 0f || drawSize.y <= 0f || drawSize.x > 20f || drawSize.y > 20f)
                yield return $"{defName}: drawSize components must be in (0, 20]";
        }
    }
    public readonly struct OrbitSample
    {
        public readonly Vector2 Offset;
        public readonly bool Front;
        public readonly float Heading;
        public readonly float Scale;
        public readonly float AltitudeOffset;

        public OrbitSample(Vector2 offset, bool front, float heading, float scale, float altitudeOffset)
        {
            Offset = offset;
            Front = front;
            Heading = heading;
            Scale = scale;
            AltitudeOffset = altitudeOffset;
        }
    }
}
