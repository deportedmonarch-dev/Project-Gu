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
    /// stage colours, so the four minor stages share one set of images. Layers are read once into small CPU arrays and
    /// composited into one texture at most 30 times a second while the orb is on screen. Animation follows real time and
    /// needs no game ticks, world scans or saved state. Missing art falls back to generated shapes with a single warning.
    /// </summary>
    public sealed class ApertureOrbRenderer
    {
        public const int Size = 256;
        private const float ComposeInterval = 1f / 30f;
        private static readonly Dictionary<string, ApertureLayers> layerCache = new();
        private static readonly float[] SinTable = BuildSinTable();

        private Texture2D output;
        private Color32[] pixels;
        private float lastCompose = -1f;
        private readonly float[] wave = new float[Size];
        private readonly float[] bandWave = new float[Size];
        private readonly float[] boundaries = new float[PrimevalSeaLayout.Qualities + 1];
        private readonly Color[] qualityColors = new Color[PrimevalSeaLayout.Qualities];

        /// <summary>Total compositions, for diagnostics and the rendered test.</summary>
        public int Compositions { get; private set; }
        public float LastSurfaceRow { get; private set; }
        public float[] LastBoundaries => boundaries;

        /// <summary>Draws the orb into a GUI rectangle. Call during OnGUI.</summary>
        public void Draw(Rect rect, ApertureOrbInput input)
        {
            if (input.Rank == null) return;
            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                float now = Time.realtimeSinceStartup;
                if (output == null || now - lastCompose >= ComposeInterval || lastCompose < 0f)
                {
                    Compose(input, now);
                    lastCompose = now;
                }
            }
            if (output != null) GUI.DrawTexture(rect, output, ScaleMode.ScaleToFit);
        }

        /// <summary>Composites one frame into the output texture.</summary>
        public void Compose(ApertureOrbInput input, float time)
        {
            var layers = Layers(input.Rank, input.Stage);
            if (output == null)
            {
                output = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "ProjectGU_ApertureOrb", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                pixels = new Color32[Size * Size];
            }

            var visual = input.Visual;
            bool dead = visual.Vitality == GuApertureVitality.Dead;
            float t = dead ? 0f : time;
            Color stageColor = input.Rank.StageColor(input.Stage);
            for (int q = 0; q < qualityColors.Length; q++) qualityColors[q] = input.Rank.StageColor(q);
            Color next = input.Rank.StageColor(Math.Min(input.Stage + 1, PrimevalSeaLayout.Qualities - 1));
            float tintAmount = input.Rank.structureTint;
            Color structure = Color.Lerp(Color.white, Brighten(stageColor, 1.6f), tintAmount);

            float surface = PrimevalSeaLayout.Layers(layers.RowCoverage, input.Amounts, input.Capacity, boundaries);
            LastSurfaceRow = surface;
            float seaBottom = boundaries[PrimevalSeaLayout.Qualities];
            bool anySea = surface < seaBottom - 0.01f;

            bool refining = (visual.Activity & GuApertureActivity.EssenceRefinement) != 0;
            bool enforcing = (visual.Activity & GuApertureActivity.WillEnforcement) != 0;
            bool nourishing = (visual.Activity & GuApertureActivity.NourishingWalls) != 0;
            float amplitude = dead ? 0f : enforcing ? 2.6f : 1.7f;
            for (int x = 0; x < Size; x++)
            {
                wave[x] = amplitude * (Sin(x * 0.045f + t * 1.25f) * 0.7f + Sin(x * 0.11f - t * 1.9f) * 0.3f);
                bandWave[x] = dead ? 0f : 1.1f * Sin(x * 0.06f - t * 0.7f);
            }
            float wallPulse = nourishing ? 0.85f + 0.3f * (0.5f + 0.5f * Sin(t * 3.6f)) : 1f;
            float currentAngle = t * (enforcing && !refining ? -0.55f : 0.8f);
            float cos = Mathf.Cos(currentAngle), sin = Mathf.Sin(currentAngle);
            float crack = visual.Vitality == GuApertureVitality.Cracked ? Mathf.Lerp(0.35f, 1f, visual.CrackSeverity) : 0f;
            const float centre = (Size - 1) * 0.5f;
            Color currentColor = refining ? Brighten(next, 1.5f) : Brighten(stageColor, 1.2f);
            float currentStrength = refining ? 0.85f : enforcing ? 0.35f : 0f;

            for (int y = 0; y < Size; y++)
            {
                int row = (Size - 1 - y) * Size; // Texture rows run bottom-up.
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    // Aperture structure.
                    float a = layers.BaseA[i];
                    float l = layers.BaseL[i];
                    float r = l * structure.r, g = l * structure.g, b = l * structure.b;

                    // Primeval sea.
                    float seaA = layers.SeaA[i];
                    if (seaA > 0f)
                    {
                        float surfaceHere = surface + wave[x];
                        if (anySea && y >= surfaceHere)
                        {
                            int q = PrimevalSeaLayout.QualityAt(Mathf.Clamp(y - bandWave[x], surface, seaBottom - 0.001f), boundaries);
                            if (q < 0) q = LowestLayer();
                            Color qc = qualityColors[q];
                            float detail = layers.SeaL[i];
                            float caustic = Sin(x * 0.11f + y * 0.05f + t * 1.7f) * Sin(x * 0.05f - y * 0.13f - t * 1.1f);
                            float depth = seaBottom > surface ? (y - surface) / (seaBottom - surface) : 0f;
                            float bright = (0.62f + 0.62f * detail + 0.14f * caustic) * (1f - 0.28f * depth);
                            if (dead) bright *= 0.45f;
                            float sr = qc.r * bright, sg = qc.g * bright, sb = qc.b * bright;
                            float lip = y - surfaceHere;
                            if (lip < 2.2f && !dead)
                            {
                                float k = 0.45f * (1f - lip / 2.2f);
                                sr += (1f - sr) * k; sg += (1f - sg) * k; sb += (1f - sb) * k;
                            }
                            if (currentStrength > 0f && layers.CurrentsA != null)
                            {
                                float dx = x - centre, dy = y - centre;
                                int cx = (int)(centre + dx * cos - dy * sin), cy = (int)(centre + dx * sin + dy * cos);
                                if ((uint)cx < Size && (uint)cy < Size)
                                {
                                    float c = layers.CurrentsA[cy * Size + cx] * currentStrength;
                                    sr += (currentColor.r - sr) * c; sg += (currentColor.g - sg) * c; sb += (currentColor.b - sb) * c;
                                }
                            }
                            float alpha = seaA * 0.94f;
                            r = sr * alpha + r * (1f - alpha); g = sg * alpha + g * (1f - alpha); b = sb * alpha + b * (1f - alpha);
                            a = alpha + a * (1f - alpha);
                        }
                        else if (!dead)
                        {
                            // Faint luminous mist in the empty part of the aperture.
                            float mist = seaA * (0.05f + 0.05f * Sin(y * 0.08f + t * 0.6f));
                            r += (stageColor.r - r) * mist; g += (stageColor.g - g) * mist; b += (stageColor.b - b) * mist;
                        }
                    }

                    // Aperture walls: the lit arc is the nourishment gathered toward the next minor stage.
                    float wallA = layers.WallA[i];
                    if (wallA > 0f)
                    {
                        float wl = layers.WallL[i];
                        bool lit = layers.Angle[i] <= input.WallArc && !dead;
                        float wr, wg, wb, wa;
                        if (lit)
                        {
                            float k = (0.55f + 0.75f * wl) * wallPulse;
                            wr = stageColor.r * k + 0.08f; wg = stageColor.g * k + 0.08f; wb = stageColor.b * k + 0.08f; wa = wallA * 0.95f;
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
                    if (layers.HighlightA != null)
                    {
                        float h = layers.HighlightA[i] * (dead ? 0.25f : 0.75f);
                        r += (1f - r) * h; g += (1f - g) * h; b += (1f - b) * h;
                        a = Math.Max(a, h);
                    }

                    // Independent crack overlay, any rank.
                    if (crack > 0f && layers.CracksA != null)
                    {
                        float k = layers.CracksA[i] * crack;
                        r += (0.05f - r) * k; g += (0.045f - g) * k; b += (0.04f - b) * k;
                        a = Math.Max(a, k);
                    }

                    if (dead)
                    {
                        float lum = r * 0.3f + g * 0.59f + b * 0.11f;
                        r = Mathf.Lerp(r, lum * 0.78f, 0.92f); g = Mathf.Lerp(g, lum * 0.75f, 0.92f); b = Mathf.Lerp(b, lum * 0.70f, 0.92f);
                    }

                    pixels[row + x] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
                }
            }
            output.SetPixels32(pixels);
            output.Apply(false);
            Compositions++;
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
            layerCache[key] = layers;
            return layers;
        }

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
    }
}
