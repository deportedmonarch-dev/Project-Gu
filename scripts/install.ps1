param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'check-environment.ps1') -RimWorldPath $RimWorldPath
$dll = Join-Path $root '1.7\Assemblies\ProjectGu.dll'
if (!(Test-Path $dll)) { throw 'Run .\scripts\build.ps1 before installing.' }
$target = Join-Path $RimWorldPath 'Mods\ProjectGU-LivingCultivation'
New-Item -ItemType Directory -Force $target | Out-Null
foreach ($folder in @('About','Defs','Patches','Textures','1.7')) { Copy-Item (Join-Path $root $folder) $target -Recurse -Force }
# Important: never ship a stale root Assemblies DLL from older 1.6 builds.
if (Test-Path (Join-Path $target 'Assemblies\ProjectGu.dll')) { throw 'Remove the old root Assemblies folder from your installed mod before enabling 1.7.' }
foreach ($asset in @('SpiritSpring.png','SpringPitEntrance.png','SpringRopeExit.png',
                    'SpiritGrass\PatchA.png','SpiritGrass\PatchB.png','SpiritGrass\PatchC.png','SpiritGrass\PatchD.png',
                    'MoonOrchid\ColonyA.png','MoonOrchid\ColonyB.png')) {
    if (!(Test-Path (Join-Path $target "Textures\ProjectGU\$asset"))) { throw "Texture missing after install: $asset" }
}
Write-Host "Installed at $target. Enable in Mods and restart."
