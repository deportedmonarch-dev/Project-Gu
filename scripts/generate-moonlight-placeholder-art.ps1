# Generates the original, temporary Moonlight Gu placeholder art with System.Drawing (Windows PowerShell 5.1).
# Replace any output with hand-drawn art of the same size and grid; no code or XML change is needed.
# Specifications: docs/ALPHA-2-MOONLIGHT-GU.md#asset-specifications
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @{ Bitmap = $bmp; G = $g }
}
function Save-Canvas($c, [string]$relative) {
    $path = Join-Path $Root $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    $c.G.Dispose()
    $c.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.Bitmap.Dispose()
    Write-Host "wrote $relative"
}
function Color([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb([Math]::Max(0, [Math]::Min(255, $a)), $r, $g, $b) }

# A crescent whose convex edge faces up (-y in image space, the direction of travel).
function Get-CrescentRegion([float]$cx, [float]$cy, [float]$r, [float]$thickness) {
    $outer = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddEllipse($cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $inner = New-Object System.Drawing.Drawing2D.GraphicsPath
    $inner.AddEllipse($cx - $r * 1.02, $cy - $r + $thickness, 2 * $r * 1.02, 2 * $r * 1.02)
    $region = New-Object System.Drawing.Region($outer)
    $region.Exclude($inner)
    return $region
}
function Draw-Crescent($g, [float]$cx, [float]$cy, [float]$r, [float]$thickness, [float]$alpha) {
    for ($i = 6; $i -ge 0; $i--) {
        $grow = $i * 2.2
        $region = Get-CrescentRegion $cx ($cy - $grow * 0.15) ($r + $grow) ($thickness + $grow * 1.1)
        if ($i -le 1) { $col = Color ([int](235 * $alpha)) 236 246 255 }
        else { $col = Color ([int]((28 + (6 - $i) * 14) * $alpha)) 120 165 255 }
        $g.FillRegion((New-Object System.Drawing.SolidBrush($col)), $region)
        $region.Dispose()
    }
}
function Draw-Spark($g, [float]$x, [float]$y, [float]$size, [int]$alpha) {
    $brush = New-Object System.Drawing.SolidBrush((Color $alpha 225 238 255))
    $g.FillEllipse($brush, $x - $size / 2, $y - $size / 2, $size, $size)
    $pen = New-Object System.Drawing.Pen((Color ([int]($alpha * 0.6)) 170 200 255), 1.2)
    $g.DrawLine($pen, $x - $size * 1.6, $y, $x + $size * 1.6, $y)
    $g.DrawLine($pen, $x, $y - $size * 1.6, $x, $y + $size * 1.6)
}

$fx = 'Textures\ProjectGU\Effects\Moonblade'

# Projectile: 128x128, one frame, crescent facing up.
$c = New-Canvas 128 128
Draw-Crescent $c.G 64 70 40 14 1.0
Save-Canvas $c "$fx\Projectile.png"

# Trail: 4 frames of 128x128 in a 4x1 atlas. A tapering streak below the blade with drifting motes.
$c = New-Canvas 512 128
$rand = New-Object System.Random 7
for ($f = 0; $f -lt 4; $f++) {
    $ox = $f * 128
    for ($i = 0; $i -lt 10; $i++) {
        $t = $i / 9.0
        $w = 34 * (1 - $t) + 4
        $a = [int](110 * (1 - $t))
        $brush = New-Object System.Drawing.SolidBrush((Color $a 140 180 255))
        $g = $c.G
        $g.FillEllipse($brush, $ox + 64 - $w / 2, 8 + $t * 104, $w, 14)
    }
    for ($s = 0; $s -lt 5; $s++) {
        $sx = $ox + 40 + $rand.NextDouble() * 48
        $sy = 20 + (($s * 23 + $f * 9) % 100)
        Draw-Spark $c.G $sx $sy (2 + $rand.NextDouble() * 2.5) ([int](200 - $sy))
    }
}
Save-Canvas $c "$fx\Trail.png"

# Launch: 4 frames of 128x128, an opening crescent flash.
$c = New-Canvas 512 128
for ($f = 0; $f -lt 4; $f++) {
    $ox = $f * 128
    $t = $f / 3.0
    Draw-Crescent $c.G ($ox + 64) (72 - 6 * $t) (20 + 24 * $t) (8 + 6 * $t) (1.0 - 0.55 * $t)
    $pen = New-Object System.Drawing.Pen((Color ([int](170 * (1 - $t))) 190 215 255), (3 - 2 * $t))
    $r = 14 + 40 * $t
    $c.G.DrawEllipse($pen, $ox + 64 - $r, 64 - $r, 2 * $r, 2 * $r)
}
Save-Canvas $c "$fx\Launch.png"

# Impact: 8 frames of 128x128 in a 4x2 atlas. A crossing moon slash, a ring and scattering sparks.
$c = New-Canvas 512 256
for ($f = 0; $f -lt 8; $f++) {
    $ox = ($f % 4) * 128
    $oy = [Math]::Floor($f / 4) * 128
    $t = $f / 7.0
    $fade = 1.0 - $t
    $g = $c.G
    $state = $g.Save()
    $g.TranslateTransform($ox + 64, $oy + 64)
    $g.RotateTransform(-35)
    # Opening frames: a bright core flash so the hit reads at normal zoom against any target.
    if ($f -le 2) {
        $core = 30 - 6 * $f
        for ($k = 4; $k -ge 0; $k--) {
            $rr = $core + $k * 5
            $g.FillEllipse((New-Object System.Drawing.SolidBrush((Color ([int]((70 - $k * 12) * (1 - $f / 3.0) + 20)) 205 225 255))), -$rr, -$rr, 2 * $rr, 2 * $rr)
        }
    }
    Draw-Crescent $g 0 (8 - 10 * $t) (30 + 20 * $t) (16 - 6 * $t) ([Math]::Max(0.05, $fade))
    $g.Restore($state)
    $pen = New-Object System.Drawing.Pen((Color ([int](200 * $fade)) 170 205 255), (6 * $fade + 1))
    $r = 12 + 46 * $t
    $g.DrawEllipse($pen, $ox + 64 - $r, $oy + 64 - $r, 2 * $r, 2 * $r)
    for ($s = 0; $s -lt 8; $s++) {
        $angle = $s * [Math]::PI / 4 + 0.3
        $d = 12 + 46 * $t
        Draw-Spark $g ($ox + 64 + [Math]::Cos($angle) * $d) ($oy + 64 + [Math]::Sin($angle) * $d) (3.5 * $fade + 0.5) ([int](230 * $fade))
    }
}
Save-Canvas $c "$fx\Impact.png"

# Command icon: 64x64.
$c = New-Canvas 64 64
$c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 18 24 48))), 2, 2, 60, 60)
$c.G.DrawEllipse((New-Object System.Drawing.Pen((Color 255 90 120 200), 2)), 2, 2, 60, 60)
Draw-Crescent $c.G 32 36 19 7 1.0
Save-Canvas $c 'Textures\ProjectGU\UI\Abilities\Moonblade.png'

# Wild Moonlight Gu: 64x64 north, east and south views of a small crescent-backed insect.
function Draw-GuBody($g, [float]$cx, [float]$cy, [float]$len, [float]$wid, [bool]$side) {
    $legPen = New-Object System.Drawing.Pen((Color 255 70 80 110), 1.6)
    for ($i = -1; $i -le 1; $i++) {
        $y = $cy + $i * $len * 0.22
        if ($side) { $g.DrawLine($legPen, $cx + $i * 6, $cy + $wid * 0.3, $cx + $i * 8 - 2, $cy + $wid * 0.75) }
        else { $g.DrawLine($legPen, $cx - $wid * 0.45, $y, $cx - $wid * 0.85, $y + 3); $g.DrawLine($legPen, $cx + $wid * 0.45, $y, $cx + $wid * 0.85, $y + 3) }
    }
    $body = New-Object System.Drawing.SolidBrush((Color 255 200 212 236))
    $shade = New-Object System.Drawing.SolidBrush((Color 255 120 138 180))
    if ($side) { $g.FillEllipse($shade, $cx - $len / 2, $cy - $wid / 2 + 2, $len, $wid); $g.FillEllipse($body, $cx - $len / 2, $cy - $wid / 2, $len, $wid - 3) }
    else { $g.FillEllipse($shade, $cx - $wid / 2, $cy - $len / 2 + 1, $wid, $len); $g.FillEllipse($body, $cx - $wid / 2 + 1, $cy - $len / 2, $wid - 2, $len - 2) }
}
foreach ($view in @('north', 'south', 'east')) {
    $c = New-Canvas 64 64
    if ($view -eq 'east') {
        Draw-GuBody $c.G 32 36 34 18 $true
        $c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 60 70 110))), 45, 31, 6, 6)
        Draw-Crescent $c.G 30 33 9 4 0.9
    } else {
        Draw-GuBody $c.G 32 34 34 20 $false
        Draw-Crescent $c.G 32 36 9 4 0.95
        if ($view -eq 'south') {
            $eye = New-Object System.Drawing.SolidBrush((Color 255 60 70 110))
            $c.G.FillEllipse($eye, 26, 46, 4, 4); $c.G.FillEllipse($eye, 34, 46, 4, 4)
        }
    }
    Save-Canvas $c "Textures\ProjectGU\Gu\MoonlightGu\Wild_$view.png"
}

# Dormant Moonlight Gu: 64x64, a curled crescent shell.
$c = New-Canvas 64 64
$c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 110 124 166))), 14, 22, 36, 26)
$c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 190 202 230))), 16, 22, 32, 22)
Draw-Crescent $c.G 32 36 12 5 0.9
Save-Canvas $c 'Textures\ProjectGU\Gu\MoonlightGu\Dormant.png'

# Aperture management command icon: 64x64, a ringed aperture with a faint inner sea.
$c = New-Canvas 64 64
$c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 18 24 48))), 2, 2, 60, 60)
for ($k = 0; $k -lt 5; $k++) {
    $r = 22 - $k * 4
    $c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color (40 + $k * 30) 110 170 230))), 32 - $r, 32 - $r, 2 * $r, 2 * $r)
}
$c.G.DrawEllipse((New-Object System.Drawing.Pen((Color 255 200 215 240), 3)), 10, 10, 44, 44)
$c.G.DrawEllipse((New-Object System.Drawing.Pen((Color 255 90 120 200), 2)), 2, 2, 60, 60)
Save-Canvas $c 'Textures\ProjectGU\UI\ManageAperture.png'

# ---------------------------------------------------------------------------------------------------------------------
# Liquor Worm (Alpha 3). A plump, segmented amber worm. Replace with hand-drawn art of the same size and grid.
# Specifications: docs/ALPHA-3-LIQUOR-WORM.md#art-handoff
function Draw-Worm($g, [float]$cx, [float]$cy, [float]$length, [float]$width, [float]$angleDeg, [float]$phase, [int]$segments) {
    # Body runs along the local +y axis toward the head at the top (image -y), wriggling sideways by phase.
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy)
    $g.RotateTransform($angleDeg)
    $outline = New-Object System.Drawing.SolidBrush((Color 255 120 70 20))
    $fill = New-Object System.Drawing.SolidBrush((Color 255 236 168 66))
    $belly = New-Object System.Drawing.SolidBrush((Color 210 255 214 130))
    for ($s = $segments - 1; $s -ge 0; $s--) {
        $t = $s / [Math]::Max(1, $segments - 1)
        $y = ($t - 0.5) * $length
        $x = [Math]::Sin($t * 5.0 + $phase) * $width * 0.28
        $w = $width * (0.62 + 0.38 * [Math]::Sin([Math]::PI * (0.15 + 0.8 * (1 - $t))))
        $g.FillEllipse($outline, $x - $w / 2 - 1, $y - $w / 2 - 1, $w + 2, $w + 2)
        $g.FillEllipse($fill, $x - $w / 2, $y - $w / 2, $w, $w)
        $g.FillEllipse($belly, $x - $w / 4, $y - $w / 3, $w / 2, $w / 3)
    }
    $headY = -0.5 * $length
    $headX = [Math]::Sin($phase) * $width * 0.28
    $eye = New-Object System.Drawing.SolidBrush((Color 255 60 30 10))
    $g.FillEllipse($eye, $headX - $width * 0.22, $headY - $width * 0.12, $width * 0.14, $width * 0.14)
    $g.FillEllipse($eye, $headX + $width * 0.08, $headY - $width * 0.12, $width * 0.14, $width * 0.14)
    $g.Restore($state)
}

# Wild Liquor Worm: 64x64 north, east and south views.
foreach ($view in @('north', 'south', 'east')) {
    $c = New-Canvas 64 64
    switch ($view) {
        'north' { Draw-Worm $c.G 32 34 40 15 180 0.6 7 }
        'south' { Draw-Worm $c.G 32 32 40 15 0 0.6 7 }
        'east' { Draw-Worm $c.G 32 34 44 14 90 0.9 8 }
    }
    Save-Canvas $c "Textures\ProjectGU\Gu\LiquorWorm\Wild_$view.png"
}

# Dormant Liquor Worm: 64x64, curled into a ring.
$c = New-Canvas 64 64
for ($s = 0; $s -lt 10; $s++) {
    $a = $s / 10.0 * 2 * [Math]::PI * 0.85
    Draw-Worm $c.G (32 + [Math]::Cos($a) * 12) (34 + [Math]::Sin($a) * 9) 4 11 0 0 1
}
Save-Canvas $c 'Textures\ProjectGU\Gu\LiquorWorm\Dormant.png'

# Manifested Liquor Worm: 4 wriggle frames of 64x64 in a 4x1 atlas, head pointing up (direction of travel).
$c = New-Canvas 256 64
for ($f = 0; $f -lt 4; $f++) {
    $glow = New-Object System.Drawing.SolidBrush((Color 46 255 210 120))
    $c.G.FillEllipse($glow, $f * 64 + 14, 6, 36, 52)
    Draw-Worm $c.G ($f * 64 + 32) 32 44 15 0 ($f * [Math]::PI / 2) 7
}
Save-Canvas $c 'Textures\ProjectGU\Effects\LiquorWorm\Manifest.png'

# Liquor Worm refinement command icon: 64x64.
$c = New-Canvas 64 64
$c.G.FillEllipse((New-Object System.Drawing.SolidBrush((Color 255 40 26 14))), 2, 2, 60, 60)
$c.G.DrawEllipse((New-Object System.Drawing.Pen((Color 255 200 140 60), 2)), 2, 2, 60, 60)
$c.G.DrawEllipse((New-Object System.Drawing.Pen((Color 150 255 210 120), 2)), 12, 24, 40, 18)
Draw-Worm $c.G 32 30 34 13 30 0.8 7
Save-Canvas $c 'Textures\ProjectGU\UI\Abilities\LiquorWormRefinement.png'
