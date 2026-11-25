# Generates the original, temporary Alpha 4A aperture and will-enforcement placeholder art (Windows PowerShell 5.1).
# Replace any output with hand-drawn art at the same path; no code or XML change is needed.
# Specifications: docs/ALPHA-4A-APERTURE-INTERFACE.md#art-pipeline
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class GuApertureArt
{
    // ---- Shared helpers ----------------------------------------------------------------------------------------
    static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / (double)0x7fffffff;
        }
    }
    static double Smooth(double t) { return t * t * (3 - 2 * t); }
    static double Noise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = Smooth(x - xi), fy = Smooth(y - yi);
        double a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
    static double Fbm(double x, double y, int seed)
    {
        double sum = 0, amp = 0.5, freq = 1;
        for (int i = 0; i < 5; i++) { sum += amp * Noise(x * freq, y * freq, seed + i * 17); amp *= 0.5; freq *= 2.03; }
        return sum;
    }
    static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static double Band(double r, double inner, double outer, double soft)
    {
        return Clamp01((r - inner) / soft + 0.5) * Clamp01((outer - r) / soft + 0.5);
    }
    static void Put(Bitmap bmp, int x, int y, double lum, double alpha)
    {
        int l = (int)(Clamp01(lum) * 255 + 0.5), a = (int)(Clamp01(alpha) * 255 + 0.5);
        bmp.SetPixel(x, y, Color.FromArgb(a, l, l, l));
    }
    static void PutColor(Bitmap bmp, int x, int y, double r, double g, double b, double alpha)
    {
        bmp.SetPixel(x, y, Color.FromArgb((int)(Clamp01(alpha) * 255 + 0.5), (int)(Clamp01(r) * 255 + 0.5), (int)(Clamp01(g) * 255 + 0.5), (int)(Clamp01(b) * 255 + 0.5)));
    }
    delegate void Shader(double u, double v, double r, double angle, out double lum, out double alpha);
    static Bitmap Render(int size, Shader shader)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        double c = (size - 1) * 0.5;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (x - c) / (size * 0.5), v = (y - c) / (size * 0.5);
                double r = Math.Sqrt(u * u + v * v), angle = Math.Atan2(u, -v);
                double lum, alpha;
                shader(u, v, r, angle, out lum, out alpha);
                Put(bmp, x, y, lum, alpha);
            }
        return bmp;
    }

    // ---- Aperture orb layers (256x256 grayscale + alpha) ---------------------------------------------------------

    // The aperture boundary: a membrane-like shell with rim light, a dark translucent hollow and a faint outer aura.
    public static Bitmap Base(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            double shell = Band(r, 0.835, 0.935, 0.018);
            double light = Clamp01(0.5 - 0.5 * (u * 0.6 + v * 0.8) / Math.Max(r, 0.001));
            double grain = Fbm(u * 9 + 3, v * 9 + 7, 11);
            double ridges = 0.5 + 0.5 * Math.Sin(angle * 18 + grain * 6);
            double shellLum = 0.30 + 0.32 * light + 0.18 * grain + 0.08 * ridges;
            double hollow = Clamp01((0.86 - r) / 0.01 + 0.5);
            double hollowLum = 0.07 + 0.10 * Math.Pow(Clamp01(r / 0.86), 3) + 0.04 * Fbm(u * 5, v * 5, 23);
            double aura = Clamp01((1.0 - r) / 0.065) * Clamp01((r - 0.92) / 0.02 + 0.5) * 0.45;
            alpha = Math.Max(Math.Max(shell, hollow * 0.92), aura);
            lum = shell > 0.01 ? shellLum * shell + hollowLum * (1 - shell) : (hollow > 0 ? hollowLum : 0.75);
        });
    }

    // The primeval sea mask: alpha bounds the liquid; luminance is spherical depth shading with slow swirling texture.
    public static Bitmap SeaMask(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            alpha = Clamp01((0.795 - r) / 0.012 + 0.5);
            double nz = Math.Sqrt(Math.Max(0, 1 - Math.Pow(r / 0.8, 2)));
            double swirl = Fbm(u * 3.2 + Math.Sin(v * 4) * 0.6, v * 3.2 + Math.Cos(u * 4) * 0.6, 41);
            double vein = Math.Pow(1 - Math.Abs(Math.Sin(angle * 3 + r * 9 + swirl * 4)), 6);
            lum = 0.22 + 0.42 * nz + 0.22 * swirl + 0.10 * vein - 0.12 * Clamp01(v);
        });
    }

    // The aperture walls: a ring of crystalline plates just inside the boundary. Plates light up as walls are nourished.
    public static Bitmap WallMask(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            const int plates = 24;
            double t = (angle / (2 * Math.PI) + 1) % 1 * plates;
            double edge = Math.Min(t - Math.Floor(t), Math.Ceiling(t) - t);
            double seam = Clamp01(edge * 16);
            int plate = (int)Math.Floor(t);
            double ring = Band(r, 0.80, 0.852, 0.01);
            alpha = ring * (0.45 + 0.55 * seam);
            lum = 0.45 + 0.35 * Hash(plate, 3, 5) + 0.2 * Clamp01(1 - Math.Abs(r - 0.826) / 0.026);
        });
    }

    // Specular light: a soft crescent on the upper left of the sphere and a few glints.
    public static Bitmap Highlights(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            lum = 1;
            double arc = Band(r, 0.66, 0.77, 0.04);
            double dir = Math.Cos(angle + 0.85);
            double crescent = arc * Math.Pow(Clamp01(dir), 3) * 0.55;
            double glint = Math.Exp(-(Math.Pow(u + 0.36, 2) + Math.Pow(v + 0.48, 2)) / 0.0018) * 0.8;
            double rim = Band(r, 0.915, 0.94, 0.01) * Math.Pow(Clamp01(dir), 2) * 0.45;
            alpha = Math.Max(Math.Max(crescent, glint), rim);
        });
    }

    // Swirling refinement currents: three spiral arms inside the sea, faded at the centre and edge. Rotated at runtime.
    public static Bitmap Currents(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            lum = 1;
            if (r < 0.02) { alpha = 0; return; }
            double spiral = Math.Sin(3 * angle - 7.5 * Math.Log(r + 0.05) + Fbm(u * 4, v * 4, 61) * 1.5);
            double arm = Math.Pow(Clamp01(spiral), 10);
            double fade = Clamp01((r - 0.08) / 0.12) * Clamp01((0.76 - r) / 0.12);
            alpha = arm * fade * 0.85;
        });
    }

    // Branching cracks over the boundary and sea. Drawn as anti-aliased random-walk lines.
    public static Bitmap Cracks(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var random = new Random(9);
            double c = size * 0.5;
            for (int i = 0; i < 5; i++)
            {
                double a = i * 2 * Math.PI / 5 + random.NextDouble() * 0.6;
                Branch(g, random, c + Math.Sin(a) * size * 0.46, c - Math.Cos(a) * size * 0.46, a + Math.PI, size * 0.05, 4.2f, 7, size);
            }
        }
        return bmp;
    }
    static void Branch(Graphics g, Random random, double x, double y, double heading, double step, float width, int depth, int size)
    {
        for (int i = 0; i < depth * 2 && width > 0.6f; i++)
        {
            heading += (random.NextDouble() - 0.5) * 0.9;
            double nx = x + Math.Sin(heading) * step, ny = y - Math.Cos(heading) * step;
            using (var pen = new Pen(Color.FromArgb((int)(150 + 100 * width / 4.2f), 255, 255, 255), width))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, (float)x, (float)y, (float)nx, (float)ny);
            }
            x = nx; y = ny; width *= 0.88f;
            if (depth > 2 && random.NextDouble() < 0.28)
                Branch(g, random, x, y, heading + (random.NextDouble() < 0.5 ? -1 : 1) * (0.6 + random.NextDouble() * 0.5), step * 0.8, width * 0.8f, depth - 2, size);
        }
    }

    // Neutral Gu silhouette used only when a species has no sprite at all.
    public static Bitmap GuFallback(int size)
    {
        return Render(size, (double u, double v, double r, double angle, out double lum, out double alpha) =>
        {
            double body = Clamp01((0.55 - Math.Sqrt(u * u * 1.6 + Math.Pow(v - 0.05, 2))) / 0.04 + 0.5);
            double head = Clamp01((0.22 - Math.Sqrt(u * u + Math.Pow(v + 0.42, 2))) / 0.04 + 0.5);
            alpha = Math.Max(body, head) * 0.85;
            lum = 0.45 + 0.25 * Clamp01(-v);
        });
    }

    // ---- Will-enforcement effect (coloured, additive glow) --------------------------------------------------------

    // A ground ring of branching essence currents converging on the cultivator. Spun slowly at runtime.
    public static Bitmap SuppressionField(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        double c = (size - 1) * 0.5;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (x - c) / (size * 0.5), v = (y - c) / (size * 0.5);
                double r = Math.Sqrt(u * u + v * v), angle = Math.Atan2(u, -v);
                double outer = Math.Exp(-Math.Pow((r - 0.86) / 0.025, 2));
                double inner = Math.Exp(-Math.Pow((r - 0.56) / 0.018, 2)) * (0.5 + 0.5 * Math.Sin(angle * 12));
                double streams = Math.Pow(Clamp01(Math.Sin(6 * angle + 5.0 * Math.Log(r + 0.1) + Fbm(u * 6, v * 6, 71) * 2.2)), 14)
                    * Clamp01((r - 0.18) / 0.15) * Clamp01((0.86 - r) / 0.06);
                double glow = Math.Exp(-r * r / 0.05) * 0.18;
                double a = Clamp01(outer * 0.9 + inner * 0.55 + streams * 0.85 + glow);
                PutColor(bmp, x, y, 0.62 + 0.38 * streams, 0.95, 0.78 + 0.2 * outer, a);
            }
        return bmp;
    }

    // Four frames of swirling suppression currents closing in around the hands, 128x128 each in a 4x1 atlas.
    public static Bitmap SuppressionCurrents(int cell)
    {
        var bmp = new Bitmap(cell * 4, cell, PixelFormat.Format32bppArgb);
        double c = (cell - 1) * 0.5;
        for (int f = 0; f < 4; f++)
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    double u = (x - c) / (cell * 0.5), v = (y - c) / (cell * 0.5);
                    double r = Math.Sqrt(u * u + v * v), angle = Math.Atan2(u, -v) + f * Math.PI / 8;
                    double arms = Math.Pow(Clamp01(Math.Sin(2 * angle - 6.0 * Math.Log(r + 0.08))), 8);
                    double fade = Clamp01((r - 0.12) / 0.15) * Clamp01((0.92 - r) / 0.2);
                    double motes = Hash((int)(angle * 20) + f * 7, (int)(r * 30), 13) > 0.93 ? Clamp01((0.9 - r) / 0.3) : 0;
                    double a = Clamp01(arms * fade * 0.9 + motes * 0.6);
                    PutColor(bmp, f * cell + x, y, 0.70 + 0.3 * arms, 1.0, 0.85, a);
                }
        return bmp;
    }
}
'@

function Save-Art($bitmap, [string]$relative) {
    $path = Join-Path $Root $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "wrote $relative"
}

$ui = 'Textures\ProjectGU\UI\Aperture'
Save-Art ([GuApertureArt]::Base(256)) "$ui\Rank1_Base.png"
Save-Art ([GuApertureArt]::SeaMask(256)) "$ui\Rank1_SeaMask.png"
Save-Art ([GuApertureArt]::WallMask(256)) "$ui\Rank1_WallMask.png"
Save-Art ([GuApertureArt]::Highlights(256)) "$ui\Rank1_Highlights.png"
Save-Art ([GuApertureArt]::Cracks(256)) "$ui\Cracks.png"
Save-Art ([GuApertureArt]::Currents(256)) "$ui\RefinementCurrents.png"
Save-Art ([GuApertureArt]::GuFallback(64)) "$ui\GuFallback.png"
$fx = 'Textures\ProjectGU\Effects\WillEnforcement'
Save-Art ([GuApertureArt]::SuppressionField(256)) "$fx\SuppressionField.png"
Save-Art ([GuApertureArt]::SuppressionCurrents(128)) "$fx\Currents.png"
