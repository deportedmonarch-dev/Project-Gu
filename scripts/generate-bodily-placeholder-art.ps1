# Generates the temporary Black Boar cultivation command icon with System.Drawing (Windows PowerShell 5.1).
# Replace the output with hand-drawn art of the same size; no code or XML change is needed.
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 128
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

# A dark, heavy boar head silhouette with pale tusks: original placeholder geometry.
$body = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 34, 30, 32))
$rim = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 150, 60, 50), 5)
$g.FillEllipse($body, 18, 26, 92, 82)
$g.DrawEllipse($rim, 18, 26, 92, 82)
$g.FillPolygon($body, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(30, 36)), (New-Object System.Drawing.PointF(22, 8)), (New-Object System.Drawing.PointF(50, 30))))
$g.FillPolygon($body, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(98, 36)), (New-Object System.Drawing.PointF(106, 8)), (New-Object System.Drawing.PointF(78, 30))))
$snout = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 58, 60))
$g.FillEllipse($snout, 44, 70, 40, 28)
$eye = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 220, 90, 60))
$g.FillEllipse($eye, 40, 52, 10, 8)
$g.FillEllipse($eye, 78, 52, 10, 8)
$tusk = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 238, 230, 210))
$g.FillPolygon($tusk, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(46, 88)), (New-Object System.Drawing.PointF(30, 72)), (New-Object System.Drawing.PointF(52, 94))))
$g.FillPolygon($tusk, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(82, 88)), (New-Object System.Drawing.PointF(98, 72)), (New-Object System.Drawing.PointF(76, 94))))

$path = Join-Path $Root 'Textures/ProjectGU/UI/Abilities/BlackBoarCultivation.png'
$g.Dispose()
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "wrote $path"
