# Generates the original, temporary Alpha 5A feeding placeholder art with System.Drawing (Windows PowerShell 5.1).
# Replace the output with hand-drawn art of the same size; no code or XML change is needed.
# Specifications: docs/ALPHA-5A-GU-FEEDING.md#art-specifications
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Color([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb([Math]::Max(0, [Math]::Min(255, $a)), $r, $g, $b) }

# Moon orchid petals: 64x64 item icon, a small loose heap of pale blue-violet petals with a faint inner glow.
$size = 64
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

# Soft glow under the heap.
for ($i = 6; $i -ge 1; $i--) {
    $brush = New-Object System.Drawing.SolidBrush((Color (10 + (6 - $i) * 5) 120 140 255))
    $r = 14 + $i * 2.6
    $g.FillEllipse($brush, 32 - $r, 38 - $r * 0.62, 2 * $r, 1.24 * $r)
}

# Petals: elongated teardrops at seeded angles and offsets, darker at the base and pale at the tip.
$random = New-Object System.Random(5)
$petals = @(
    @(22, 40, -35), @(41, 41, 30), @(30, 33, -70), @(35, 45, 80), @(26, 46, 15),
    @(39, 34, -10), @(31, 41, 55), @(44, 47, -55), @(20, 34, 100)
)
foreach ($p in $petals) {
    $state = $g.Save()
    $g.TranslateTransform([float]$p[0], [float]$p[1])
    $g.RotateTransform([float]$p[2])
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(0, 0, 6, -4, 13, -5, 17, 0)
    $path.AddBezier(17, 0, 13, 5, 6, 4, 0, 0)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(17, 0)),
        (Color 255 74 70 168), (Color 255 196 204 255))
    $g.FillPath($gradient, $path)
    $pen = New-Object System.Drawing.Pen((Color 150 52 48 120), 0.8)
    $g.DrawPath($pen, $path)
    $vein = New-Object System.Drawing.Pen((Color (90 + $random.Next(40)) 230 236 255), 0.7)
    $g.DrawLine($vein, 2, 0, 14, 0)
    $g.Restore($state)
}
# A few luminous motes.
foreach ($m in @(@(18, 28), @(46, 30), @(33, 26), @(49, 40))) {
    $brush = New-Object System.Drawing.SolidBrush((Color 200 226 236 255))
    $g.FillEllipse($brush, $m[0] - 1.2, $m[1] - 1.2, 2.4, 2.4)
}

$path = Join-Path $Root 'Textures\ProjectGU\Items\MoonOrchidPetals.png'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
$g.Dispose()
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "wrote Textures\ProjectGU\Items\MoonOrchidPetals.png"
