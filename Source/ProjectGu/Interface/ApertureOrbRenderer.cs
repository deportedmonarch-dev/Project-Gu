using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>What the orb shows this frame. Built from authoritative values by the tab; never saved.</summary>
    public struct ApertureOrbInput
    {
        public GuApertureRankDef Rank;
        /// <summary>The cultivator's actual minor stage (0 = Initial). It tints the aperture structure and walls.</summary>
        public int Stage;
        /// <summary>Raw essence per quality, Initial..Peak. Each quality is drawn as its own layer in its own colour.</summary>
        public double[] Amounts;
        public double Capacity;
        /// <summary>Wall nourishment progress toward the next minor stage, 0..1.</summary>
        public float WallArc;
        public GuApertureVisualState Visual;
    }

    /// <summary>
    /// Draws the living aperture: a spherical boundary, a layered primeval sea with a moving surface, the wall-progress
    /// ring, highlights and optional condition overlays. The rank's art is grayscale and recoloured here from the rank's
    /// stage colours, so the four minor stages share one set of images. Layers are read once into small CPU arrays.
    ///
    /// Cost control (measured in docs/ALPHA-4A-APERTURE-INTERFACE.md#renderer-performance):
    /// - Only the sea animates. Pixels outside the sea mask are composited once and kept until a static input changes
    ///   (rank art, stage, wall progress, condition, or wall nourishment starting or stopping, which makes the wall pulse).
    /// - The sea animates at <see cref="AnimationFps"/> frames a second, on a clock that advances only while the game runs
    ///   and the aperture is alive. Paused or dead, the orb is recomposed only when its inputs change.
    /// - Identical inputs on a frozen clock never recompose or re-upload.
    /// Composition is on the main thread during Repaint; it allocates nothing after the first frame.
    /// </summary>
    public sealed class ApertureOrbRenderer
    {
        /// <summary>Working resolution. The pixel loop indexes rows with shifts by 8, so this must stay 256.</summary>
        public const int Size = 256;
        public const float AnimationFps = 12f;
        private const float AnimationInterval = 1f / AnimationFps;
        private const float MaxClockStep = 0.25f;
        private static readonly Dictionary<string, ApertureLayers> layerCache = new();
        private static readonly float[] SinTable = BuildSinTable();

        private Texture2D output;
        private Color32[] pixels;
        private readonly float[] wave = new float[Size];
        private readonly float[] bandWave = new float[Size];
        // Caustic shimmer, separated with sin(a + b) = sin a cos b + cos a sin b: per-column terms change with time,
        // per-row terms never do. The mist term is per row.
        private readonly float[] causticAs = new float[Size], causticAc = new float[Size], causticBs = new float[Size], causticBc = new float[Size];
        private readonly float[] mistRow = new float[Size];
        private static readonly float[] RowAs = RowTable(0.05f, false), RowAc = RowTable(0.05f, true), RowBs = RowTable(-0.13f, false), RowBc = RowTable(-0.13f, true);
        private readonly float[] boundaries = new float[PrimevalSeaLayout.Qualities + 1];
        private readonly Color[] qualityColors = new Color[PrimevalSeaLayout.Qualities];

        // Animation clock: advances with real time only while the game runs, so a pause freezes the sea in place.
        private float clock;
        private float lastDrawRealtime = -1f;
        private float lastComposeClock = float.NegativeInfinity;

        // The inputs of the last composition, to skip identical frames and to know when the static pixels are stale.
        private readonly double[] lastAmounts = new double[PrimevalSeaLayout.Qualities];
        private ApertureOrbInput lastInput;
        private bool composedOnce;
        private ApertureLayers staticLayers;
        private int staticStage = -1;
        private float staticWallArc = -1f;
        private GuApertureVitality staticVitality;
        private float staticCrack = -1f;
        private bool staticNourishing;

        // Per-composition values shared by the pixel routine.
        private ApertureLayers layers;
        private GuApertureRankDef layersRank;
        private int layersStage = -1;
        private float t, surface, seaBottom, wallPulse, cos, sin, crack, currentStrength, structureR, structureG, structureB, wallArc;
        private bool anySea, dead;
        private Color stageColor, currentColor;

        /// <summary>Total compositions by this renderer, for diagnostics and the rendered test.</summary>
        public int Compositions { get; private set; }
        public int StaticRebuilds { get; private set; }
        public float LastSurfaceRow { get; private set; }
        public float[] LastBoundaries => boundaries;
        public float Clock => clock;

        /// <summary>Draws the orb into a GUI rectangle. Call during OnGUI.</summary>
        public void Draw(Rect rect, ApertureOrbInput input)
        {
            if (input.Rank == null) return;
            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                bool animating = AdvanceClock(input);
                bool changed = !composedOnce || !SameInputs(input);
                if (output == null || changed || animating && clock - lastComposeClock >= AnimationInterval) Compose(input, clock);
                else ApertureOrbStats.Skip();
            }
            if (output != null) GUI.DrawTexture(rect, output, ScaleMode.ScaleToFit);
        }

        /// <summary>Whether the sea is animating this frame; advances the clock when it is.</summary>
        private bool AdvanceClock(ApertureOrbInput input)
        {
            float now = Time.realtimeSinceStartup;
            float step = lastDrawRealtime < 0f ? 0f : Mathf.Clamp(now - lastDrawRealtime, 0f, MaxClockStep);
            lastDrawRealtime = now;
            bool animating = input.Visual.Vitality != GuApertureVitality.Dead && Find.TickManager is { Paused: false };
            if (animating) clock += step;
            return animating;
        }

        private bool SameInputs(ApertureOrbInput input)
        {
            if (input.Rank != lastInput.Rank || input.Stage != lastInput.Stage || input.Capacity != lastInput.Capacity
                || input.WallArc != lastInput.WallArc || input.Visual.Vitality != lastInput.Visual.Vitality
                || input.Visual.CrackSeverity != lastInput.Visual.CrackSeverity || input.Visual.Activity != lastInput.Visual.Activity
                || input.Visual.Preview != lastInput.Visual.Preview) return false;
            if ((input.Amounts == null) != (lastInput.Amounts == null)) return false;
            if (input.Amounts != null)
                for (int q = 0; q < lastAmounts.Length; q++)
                    if ((q < input.Amounts.Length ? input.Amounts[q] : 0d) != lastAmounts[q]) return false;
            return true;
        }

        private void Remember(ApertureOrbInput input)
        {
            lastInput = input;
            for (int q = 0; q < lastAmounts.Length; q++) lastAmounts[q] = input.Amounts != null && q < input.Amounts.Length ? input.Amounts[q] : 0d;
            lastInput.Amounts = input.Amounts == null ? null : lastAmounts;
            composedOnce = true;
        }

        /// <summary>Composites one frame into the output texture and uploads it.</summary>
        public void Compose(ApertureOrbInput input, float time)
        {
            output ??= new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "ProjectGU_ApertureOrb", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            long composeStart = ApertureOrbStats.Now(), composeBytes = ApertureOrbStats.Bytes();
            ComposeBuffer(input, time);
            ApertureOrbStats.Composed(composeStart, composeBytes);

            long uploadStart = ApertureOrbStats.Now(), uploadBytes = ApertureOrbStats.Bytes();
            output.SetPixels32(pixels);
            output.Apply(false);
            ApertureOrbStats.Uploaded(uploadStart, uploadBytes);
            lastComposeClock = time;
            Remember(input);
            Compositions++;
        }

        /// <summary>The composited pixels, bottom-up rows. Exposed for the managed parity checks.</summary>
        internal Color32[] Buffer => pixels;

        /// <summary>
        /// CPU composition only, no texture: a full pass when a static input changed, otherwise the animated pixels.
        /// Separate from the upload so the managed tests can prove the incremental pass equals a full one.
        /// </summary>
        internal void ComposeBuffer(ApertureOrbInput input, float time)
        {
            if (input.Rank != layersRank || input.Stage != layersStage)
            {
                // The cache lookup builds a key string, so it runs only when the rank or stage changes.
                layers = Layers(input.Rank, input.Stage);
                layersRank = input.Rank;
                layersStage = input.Stage;
            }
            pixels ??= new Color32[Size * Size];
            long prepareStart = ApertureOrbStats.Now();
            Prepare(input, time);
            ApertureOrbStats.Prepared(prepareStart);

            var visual = input.Visual;
            bool nourishing = (visual.Activity & GuApertureActivity.NourishingWalls) != 0 && !dead;
            if (layers != staticLayers || input.Stage != staticStage || input.WallArc != staticWallArc || visual.Vitality != staticVitality
                || crack != staticCrack || nourishing != staticNourishing)
            {
                // Static inputs changed: every pixel, including those outside the sea, is composited again.
                ComposeRange(null);
                staticLayers = layers;
                staticStage = input.Stage;
                staticWallArc = input.WallArc;
                staticVitality = visual.Vitality;
                staticCrack = crack;
                staticNourishing = nourishing;
                StaticRebuilds++;
                ApertureOrbStats.Rebuilt();
            }
            else
            {
                // Only the sea moves; the wall ring pulses too while the walls are being nourished.
                ComposeRange(layers.SeaIndices);
                if (nourishing) ComposeRange(layers.WallOnlyIndices);
            }
        }

        /// <summary>Per-composition values: sea geometry, waves, pulse, currents and overlay strengths.</summary>
        private void Prepare(ApertureOrbInput input, float time)
        {
            var visual = input.Visual;
            dead = visual.Vitality == GuApertureVitality.Dead;
            t = dead ? 0f : time;
            stageColor = input.Rank.StageColor(input.Stage);
            for (int q = 0; q < qualityColors.Length; q++) qualityColors[q] = input.Rank.StageColor(q);
            Color next = input.Rank.StageColor(Math.Min(input.Stage + 1, PrimevalSeaLayout.Qualities - 1));
            Color structure = Color.Lerp(Color.white, Brighten(stageColor, 1.6f), input.Rank.structureTint);
            structureR = structure.r; structureG = structure.g; structureB = structure.b;
            wallArc = input.WallArc;

            surface = PrimevalSeaLayout.Layers(layers.RowCoverage, input.Amounts, input.Capacity, boundaries);
            LastSurfaceRow = surface;
            seaBottom = boundaries[PrimevalSeaLayout.Qualities];
            anySea = surface < seaBottom - 0.01f;

            bool refining = (visual.Activity & GuApertureActivity.EssenceRefinement) != 0;
            bool enforcing = (visual.Activity & GuApertureActivity.WillEnforcement) != 0;
            bool nourishing = (visual.Activity & GuApertureActivity.NourishingWalls) != 0;
            float amplitude = dead ? 0f : enforcing ? 2.6f : 1.7f;
            for (int x = 0; x < Size; x++)
            {
                wave[x] = amplitude * (Sin(x * 0.045f + t * 1.25f) * 0.7f + Sin(x * 0.11f - t * 1.9f) * 0.3f);
                bandWave[x] = dead ? 0f : 1.1f * Sin(x * 0.06f - t * 0.7f);
                causticAs[x] = Mathf.Sin(x * 0.11f + t * 1.7f);
                causticAc[x] = Mathf.Cos(x * 0.11f + t * 1.7f);
                causticBs[x] = Mathf.Sin(x * 0.05f - t * 1.1f);
                causticBc[x] = Mathf.Cos(x * 0.05f - t * 1.1f);
                mistRow[x] = 0.05f + 0.05f * Mathf.Sin(x * 0.08f + t * 0.6f); // indexed by row
            }
            wallPulse = nourishing ? 0.85f + 0.3f * (0.5f + 0.5f * Sin(t * 3.6f)) : 1f;
            float currentAngle = t * (enforcing && !refining ? -0.55f : 0.8f);
            cos = Mathf.Cos(currentAngle);
            sin = Mathf.Sin(currentAngle);
            crack = visual.Vitality == GuApertureVitality.Cracked ? Mathf.Lerp(0.35f, 1f, visual.CrackSeverity) : 0f;
            currentColor = refining ? Brighten(next, 1.5f) : Brighten(stageColor, 1.2f);
            currentStrength = refining ? 0.85f : enforcing ? 0.35f : 0f;
        }

        /// <summary>
        /// Composites the listed pixels (indices in top-origin rows), or every pixel when <paramref name="indices"/> is
        /// null, into the bottom-up texture buffer. Everything the inner loop reads is copied into locals first so the
        /// JIT keeps it in registers; an earlier per-pixel method reading instance fields measured 2.5 times slower.
        /// </summary>
        private void ComposeRange(int[] indices)
        {
            const float centre = (Size - 1) * 0.5f;
            float[] baseA = layers.BaseA, baseL = layers.BaseL, seaAs = layers.SeaA, seaL = layers.SeaL, wallAs = layers.WallA, wallLs = layers.WallL;
            float[] angles = layers.Angle, highlights = layers.HighlightA, cracks = layers.CracksA, currents = layers.CurrentsA;
            float[] waves = wave, bands = bandWave, bounds = boundaries;
            float[] cAs = causticAs, cAc = causticAc, cBs = causticBs, cBc = causticBc, mists = mistRow;
            float[] rAs = RowAs, rAc = RowAc, rBs = RowBs, rBc = RowBc;
            Color32[] buffer = pixels;
            float surf = surface, bottom = seaBottom, pulse = wallPulse, cs = cos, sn = sin, crackK = crack, strength = currentStrength;
            float sR = structureR, sG = structureG, sB = structureB, arc = wallArc;
            bool sea = anySea, isDead = dead, useCurrents = strength > 0f && currents != null;
            Color stage = stageColor, current = currentColor;
            Color q0 = qualityColors[0], q1 = qualityColors[1], q2 = qualityColors[2], q3 = qualityColors[3];
            int lowest = LowestLayer();
            float depthScale = bottom > surf ? 1f / (bottom - surf) : 0f;
            float highlightScale = isDead ? 0.25f : 0.75f;
            int count = indices?.Length ?? Size * Size;

            for (int n = 0; n < count; n++)
            {
                int i = indices == null ? n : indices[n];
                int y = i >> 8, x = i & (Size - 1); // Size is 256.
                // Aperture structure.
                float a = baseA[i];
                float l = baseL[i];
                float r = l * sR, g = l * sG, b = l * sB;

                // Primeval sea.
                float seaA = seaAs[i];
                if (seaA > 0f)
                {
                    float surfaceHere = surf + waves[x];
                    if (sea && y >= surfaceHere)
                    {
                        float row = y - bands[x];
                        if (row < surf) row = surf;
                        else if (row > bottom - 0.001f) row = bottom - 0.001f;
                        int q = PrimevalSeaLayout.QualityAt(row, bounds);
                        if (q < 0) q = lowest;
                        Color qc = q == 0 ? q0 : q == 1 ? q1 : q == 2 ? q2 : q3;
                        float caustic = (cAs[x] * rAc[y] + cAc[x] * rAs[y]) * (cBs[x] * rBc[y] + cBc[x] * rBs[y]);
                        float bright = (0.62f + 0.62f * seaL[i] + 0.14f * caustic) * (1f - 0.28f * (y - surf) * depthScale);
                        if (isDead) bright *= 0.45f;
                        float sr = qc.r * bright, sg = qc.g * bright, sb = qc.b * bright;
                        float lip = y - surfaceHere;
                        if (lip < 2.2f && !isDead)
                        {
                            float k = 0.45f * (1f - lip / 2.2f);
                            sr += (1f - sr) * k; sg += (1f - sg) * k; sb += (1f - sb) * k;
                        }
                        if (useCurrents)
                        {
                            float dx = x - centre, dy = y - centre;
                            int cx = (int)(centre + dx * cs - dy * sn), cy = (int)(centre + dx * sn + dy * cs);
                            if ((uint)cx < Size && (uint)cy < Size)
                            {
                                float c = currents[(cy << 8) + cx] * strength;
                                sr += (current.r - sr) * c; sg += (current.g - sg) * c; sb += (current.b - sb) * c;
                            }
                        }
                        float alpha = seaA * 0.94f;
                        r = sr * alpha + r * (1f - alpha); g = sg * alpha + g * (1f - alpha); b = sb * alpha + b * (1f - alpha);
                        a = alpha + a * (1f - alpha);
                    }
                    else if (!isDead)
                    {
                        // Faint luminous mist in the empty part of the aperture.
                        float mist = seaA * mists[y];
                        r += (stage.r - r) * mist; g += (stage.g - g) * mist; b += (stage.b - b) * mist;
                    }
                }

                // Aperture walls: the lit arc is the nourishment gathered toward the next minor stage.
                float wallA = wallAs[i];
                if (wallA > 0f)
                {
                    float wl = wallLs[i];
                    float wr, wg, wb, wa;
                    if (angles[i] <= arc && !isDead)
                    {
                        float k = (0.55f + 0.75f * wl) * pulse;
                        wr = stage.r * k + 0.08f; wg = stage.g * k + 0.08f; wb = stage.b * k + 0.08f; wa = wallA * 0.95f;
                    }
                    else
                    {
                        float k = 0.18f + 0.28f * wl;
                        wr = k; wg = k * 1.02f; wb = k * 1.04f; wa = wallA * 0.75f;
                    }
                    r = wr * wa + r * (1f - wa); g = wg * wa + g * (1f - wa); b = wb * wa + b * (1f - wa);
                    a = wa + a * (1f - wa);
                }

                // Specular highlights.
                if (highlights != null)
                {
                    float h = highlights[i] * highlightScale;
                    r += (1f - r) * h; g += (1f - g) * h; b += (1f - b) * h;
                    if (h > a) a = h;
                }

                // Independent crack overlay, any rank.
                if (crackK > 0f && cracks != null)
                {
                    float k = cracks[i] * crackK;
                    r += (0.05f - r) * k; g += (0.045f - g) * k; b += (0.04f - b) * k;
                    if (k > a) a = k;
                }

                if (isDead)
                {
                    float lum = r * 0.3f + g * 0.59f + b * 0.11f;
                    r += (lum * 0.78f - r) * 0.92f; g += (lum * 0.75f - g) * 0.92f; b += (lum * 0.70f - b) * 0.92f;
                }

                buffer[((Size - 1 - y) << 8) + x] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
            }
        }

        private int LowestLayer()
        {
            for (int q = PrimevalSeaLayout.Qualities - 1; q >= 0; q--)
                if (boundaries[q + 1] - boundaries[q] > 0.01f) return q;
            return 0;
        }

        // ---- Layers ----------------------------------------------------------------------------------------------

        /// <summary>The grayscale layers for a rank (and stage override, when one is drawn), read once and cached.</summary>
        internal static ApertureLayers Layers(GuApertureRankDef rank, int stage)
        {
            string baseOverride = rank.Stage(stage)?.baseTexPathOverride;
            string key = rank.defName + "|" + (baseOverride ?? "");
            if (layerCache.TryGetValue(key, out var cached)) return cached;
            var layers = new ApertureLayers();
            var missing = new List<string>();
            if (!ReadLayer(baseOverride ?? rank.baseTexPath, out layers.BaseL, out layers.BaseA)) { missing.Add("base"); Generated.Base(out layers.BaseL, out layers.BaseA); }
            if (!ReadLayer(rank.seaMaskTexPath, out layers.SeaL, out layers.SeaA)) { missing.Add("sea mask"); Generated.Sea(out layers.SeaL, out layers.SeaA); }
            if (!ReadLayer(rank.wallMaskTexPath, out layers.WallL, out layers.WallA)) { missing.Add("wall mask"); Generated.Wall(out layers.WallL, out layers.WallA); }
            if (ReadLayer(rank.highlightsTexPath, out _, out var highlight)) layers.HighlightA = highlight;
            if (ReadLayer(rank.cracksTexPath, out _, out var cracks)) layers.CracksA = cracks;
            if (ReadLayer(rank.currentsTexPath, out _, out var currents)) layers.CurrentsA = currents;
            if (missing.Count > 0)
                Log.Warning($"[Project GU] Aperture art for {rank.defName} is missing ({string.Join(", ", missing)}); generated shapes are drawn instead.");

            Finish(layers);
            layerCache[key] = layers;
            return layers;
        }

        /// <summary>Derived data: per-row sea area, wall angles, and the pixel lists for incremental composition.</summary>
        private static void Finish(ApertureLayers layers)
        {
            layers.RowCoverage = new float[Size];
            layers.Angle = new float[Size * Size];
            const float centre = (Size - 1) * 0.5f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    layers.RowCoverage[y] += layers.SeaA[i];
                    // 0 at the top of the ring, increasing clockwise to 1.
                    float angle = Mathf.Atan2(x - centre, centre - y) / (2f * Mathf.PI);
                    layers.Angle[i] = angle < 0f ? angle + 1f : angle;
                }
            var sea = new List<int>();
            var wallOnly = new List<int>();
            for (int i = 0; i < Size * Size; i++)
            {
                if (layers.SeaA[i] > 0f) sea.Add(i);
                else if (layers.WallA[i] > 0f) wallOnly.Add(i);
            }
            layers.SeaIndices = sea.ToArray();
            layers.WallOnlyIndices = wallOnly.ToArray();
        }

        /// <summary>Test support: layers built from the generated fallback shapes, plus optional overlays, without textures.</summary>
        internal static ApertureLayers GeneratedLayers(float[] cracks = null, float[] currents = null, float[] highlights = null)
        {
            var layers = new ApertureLayers { CracksA = cracks, CurrentsA = currents, HighlightA = highlights };
            Generated.Base(out layers.BaseL, out layers.BaseA);
            Generated.Sea(out layers.SeaL, out layers.SeaA);
            Generated.Wall(out layers.WallL, out layers.WallA);
            Finish(layers);
            return layers;
        }

        /// <summary>Test support: makes a rank use the given layers instead of reading its textures.</summary>
        internal static void InjectLayers(GuApertureRankDef rank, ApertureLayers layers) => layerCache[rank.defName + "|"] = layers;

        /// <summary>Reads a texture into luminance and alpha arrays at the working size, rows from the top.</summary>
        private static bool ReadLayer(string path, out float[] luminance, out float[] alpha)
        {
            luminance = alpha = null;
            if (path.NullOrEmpty()) return false;
            Color32[] source = null;
            int width = 0, height = 0;
            var texture = ContentFinder<Texture2D>.Get(path, false);
            if (texture != null)
            {
                try
                {
                    source = texture.GetPixels32();
                    width = texture.width;
                    height = texture.height;
                }
                catch (Exception)
                {
                    source = null;
                }
            }
            if (source == null && !TryReadFromModFolder(path, out source, out width, out height)) return false;
            if (source.Length != width * height || width < 2 || height < 2) return false;

            luminance = new float[Size * Size];
            alpha = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                float sy = (y + 0.5f) * height / Size - 0.5f;
                for (int x = 0; x < Size; x++)
                {
                    float sx = (x + 0.5f) * width / Size - 0.5f;
                    Bilinear(source, width, height, sx, sy, out float l, out float a);
                    luminance[y * Size + x] = l;
                    alpha[y * Size + x] = a;
                }
            }
            return true;
        }

        /// <summary>Bilinear sample with top-origin coordinates. Unity pixel rows are bottom-up.</summary>
        private static void Bilinear(Color32[] source, int width, int height, float sx, float sy, out float luminance, out float alpha)
        {
            int x0 = Mathf.Clamp((int)Mathf.Floor(sx), 0, width - 1), y0 = Mathf.Clamp((int)Mathf.Floor(sy), 0, height - 1);
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            float fx = Mathf.Clamp01(sx - x0), fy = Mathf.Clamp01(sy - y0);
            Sample(source, width, height, x0, y0, out float l00, out float a00);
            Sample(source, width, height, x1, y0, out float l10, out float a10);
            Sample(source, width, height, x0, y1, out float l01, out float a01);
            Sample(source, width, height, x1, y1, out float l11, out float a11);
            luminance = Mathf.Lerp(Mathf.Lerp(l00, l10, fx), Mathf.Lerp(l01, l11, fx), fy);
            alpha = Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        private static void Sample(Color32[] source, int width, int height, int x, int yTop, out float luminance, out float alpha)
        {
            var c = source[(height - 1 - yTop) * width + x];
            luminance = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
            alpha = c.a / 255f;
        }

        private static bool TryReadFromModFolder(string path, out Color32[] source, out int width, out int height)
        {
            source = null;
            width = height = 0;
            var content = LoadedModManager.GetMod<ProjectGuMod>()?.Content;
            if (content == null) return false;
            foreach (string folder in content.foldersToLoadDescendingOrder)
            {
                string file = Path.Combine(folder, "Textures", path + ".png");
                if (!File.Exists(file)) continue;
                try
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(File.ReadAllBytes(file))) return false;
                    source = texture.GetPixels32();
                    width = texture.width;
                    height = texture.height;
                    UnityEngine.Object.Destroy(texture);
                    return true;
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] Could not read aperture art {file}. {exception.Message}");
                    return false;
                }
            }
            return false;
        }

        /// <summary>Simple shapes used when a layer's art is missing, so the interface still works.</summary>
        private static class Generated
        {
            private static float Radius(int x, int y) => Mathf.Sqrt(Sq(x - (Size - 1) * 0.5f) + Sq(y - (Size - 1) * 0.5f)) / (Size * 0.5f);
            private static float Sq(float v) => v * v;
            private static float Band(float r, float inner, float outer, float soft) =>
                Mathf.Clamp01((r - inner) / soft + 1f) * Mathf.Clamp01((outer - r) / soft + 1f);

            internal static void Base(out float[] l, out float[] a) => Fill(out l, out a, (x, y) =>
            {
                float r = Radius(x, y);
                float rim = Band(r, 0.86f, 0.95f, 0.02f);
                float inside = Mathf.Clamp01((0.9f - r) / 0.02f);
                float glow = Mathf.Clamp01((1f - r) / 0.05f) * (1f - rim) * (1f - inside) * 0.35f;
                return (0.10f + 0.5f * rim, Math.Max(Math.Max(rim, inside * 0.9f), glow));
            });

            internal static void Sea(out float[] l, out float[] a) => Fill(out l, out a, (x, y) =>
            {
                float r = Radius(x, y);
                return (0.55f - 0.35f * r, Mathf.Clamp01((0.82f - r) / 0.015f));
            });

            internal static void Wall(out float[] l, out float[] a) => Fill(out l, out a, (x, y) =>
            {
                float r = Radius(x, y);
                return (0.7f, Band(r, 0.835f, 0.875f, 0.01f));
            });

            private static void Fill(out float[] l, out float[] a, Func<int, int, (float, float)> shape)
            {
                l = new float[Size * Size];
                a = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var (lum, alpha) = shape(x, y);
                        l[y * Size + x] = lum;
                        a[y * Size + x] = alpha;
                    }
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static float[] RowTable(float frequency, bool cosine)
        {
            var table = new float[Size];
            for (int i = 0; i < Size; i++) table[i] = cosine ? Mathf.Cos(i * frequency) : Mathf.Sin(i * frequency);
            return table;
        }

        private static float[] BuildSinTable()
        {
            var table = new float[1024];
            for (int i = 0; i < table.Length; i++) table[i] = Mathf.Sin(i * 2f * Mathf.PI / table.Length);
            return table;
        }

        private static float Sin(float radians)
        {
            int index = (int)(radians * (1024f / (2f * Mathf.PI))) & 1023;
            return SinTable[index];
        }

        private static Color Brighten(Color c, float factor) =>
            new Color(Mathf.Clamp01(c.r * factor), Mathf.Clamp01(c.g * factor), Mathf.Clamp01(c.b * factor), 1f);

        private static byte ToByte(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);
    }

    /// <summary>A rank's art, resampled to the orb's working size, rows from the top.</summary>
    internal sealed class ApertureLayers
    {
        internal float[] BaseL, BaseA, SeaL, SeaA, WallL, WallA, HighlightA, CracksA, CurrentsA;
        internal float[] RowCoverage;
        internal float[] Angle;
        /// <summary>Pixels inside the sea mask: the only pixels that animate.</summary>
        internal int[] SeaIndices;
        /// <summary>Wall-ring pixels outside the sea, recomposited only while the walls pulse during nourishment.</summary>
        internal int[] WallOnlyIndices;
    }
}
