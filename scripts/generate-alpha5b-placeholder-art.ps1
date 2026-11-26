# Generates the original, temporary Alpha 5B placeholder art with System.Drawing (Windows PowerShell 5.1).
# Replace any output with hand-drawn art of the same size; no code or XML change is needed.
# Specifications: docs/ALPHA-5B-PLAYTEST-CORRECTIONS.md#art-specifications
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Color([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb([Math]::Max(0, [Math]::Min(255, $a)), $r, $g, $b) }
function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}
function Save-Canvas($bmp, $g, [string]$relative) {
    $path = Join-Path $Root $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $relative"
}

# 1. Moon orchid seeds: 64x64 item icon. A small scatter of pale, slightly luminous teardrop seeds on a faint glow.
$bmp, $g = New-Canvas 64
for ($i = 6; $i -ge 1; $i--) {
    $brush = New-Object System.Drawing.SolidBrush((Color (8 + (6 - $i) * 4) 140 150 255))
    $r = 12 + $i * 2.4
    $g.FillEllipse($brush, 32 - $r, 38 - $r * 0.6, 2 * $r, 1.2 * $r)
}
$seeds = @(@(24, 36, -20), @(36, 34, 25), @(30, 42, 70), @(40, 43, -40), @(22, 45, 10), @(33, 28, -65), @(44, 37, 85))
foreach ($s in $seeds) {
    $state = $g.Save()
    $g.TranslateTransform([float]$s[0], [float]$s[1])
    $g.RotateTransform([float]$s[2])
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddBezier(0, 0, 2.5, -2.6, 6, -2.6, 7.5, 0)
    $path.AddBezier(7.5, 0, 6, 2.6, 2.5, 2.6, 0, 0)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(7.5, 0)),
        (Color 255 92 84 150), (Color 255 214 220 255))
    $g.FillPath($gradient, $path)
    $g.DrawPath((New-Object System.Drawing.Pen((Color 170 60 54 110), 0.6)), $path)
    $g.Restore($state)
}
foreach ($m in @(@(19, 30), @(46, 31), @(30, 24))) { $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color 190 228 236 255))), $m[0] - 1, $m[1] - 1, 2, 2) }
Save-Canvas $bmp $g 'Textures\ProjectGU\Items\MoonOrchidSeeds.png'

# 2. Gather seeds: 64x64 command icon. An orchid bloom above a cupped hand of seeds, in the vanilla order-icon palette.
$bmp, $g = New-Canvas 64
$petal = New-Object System.Drawing.SolidBrush((Color 255 168 164 236))
foreach ($angle in @(0, 72, 144, 216, 288)) {
    $state = $g.Save()
    $g.TranslateTransform(32, 22)
    $g.RotateTransform($angle)
    $g.FillEllipse($petal, -4, -15, 8, 15)
    $g.Restore($state)
}
$g.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 240 236 255))), 28, 18, 8, 8)
$cup = New-Object System.Drawing.Drawing2D.GraphicsPath
$cup.AddArc(12, 30, 40, 26, 0, 180)
$cup.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush((Color 235 214 196 168))), $cup)
$g.DrawPath((New-Object System.Drawing.Pen((Color 255 120 96 70), 2)), $cup)
foreach ($s in @(@(24, 44), @(31, 46), @(38, 44), @(28, 49), @(35, 50))) { $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 236 238 255))), $s[0] - 2, $s[1] - 2, 4, 4) }
Save-Canvas $bmp $g 'Textures\ProjectGU\UI\GatherSeeds.png'

# 3. Gather seeds designation overlay: 64x64, drawn over the designated plant like vanilla's harvest mark.
$bmp, $g = New-Canvas 64
$ring = New-Object System.Drawing.Pen((Color 230 200 210 255), 3)
$g.DrawEllipse($ring, 10, 10, 44, 44)
foreach ($s in @(@(26, 30), @(32, 36), @(38, 30), @(32, 26))) { $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color 240 236 240 255))), $s[0] - 3, $s[1] - 3, 6, 6) }
Save-Canvas $bmp $g 'Textures\ProjectGU\Designations\GatherSeeds.png'

# 4. Hope spirit: 64x64 soft luminous mote for the ambient spring spirits (drawn with the additive mote-glow shader).
#    A bright core, a soft halo and two faint wing-like flares, so it reads as a small living light rather than a dot.
$bmp, $g = New-Canvas 64
for ($i = 30; $i -ge 1; $i--) {
    $t = $i / 30.0
    $alpha = [int](255 * [Math]::Pow(1 - $t, 2.2))
    $brush = New-Object System.Drawing.SolidBrush((Color $alpha 220 240 255))
    $g.FillEllipse($brush, 32 - $i, 32 - $i, 2 * $i, 2 * $i)
}
foreach ($side in @(-1, 1)) {
    $state = $g.Save()
    $g.TranslateTransform(32, 32)
    $g.RotateTransform(-18 * $side)
    $wing = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wing.AddEllipse([float](4 * $side - ($(if ($side -lt 0) { 18 } else { 0 }))), -5, 18, 9)
    $g.FillPath((New-Object System.Drawing.SolidBrush((Color 70 200 230 255))), $wing)
    $g.Restore($state)
}
$g.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 255 255 255))), 29, 29, 6, 6)
Save-Canvas $bmp $g 'Textures\ProjectGU\Effects\HopeSpirit.png'
