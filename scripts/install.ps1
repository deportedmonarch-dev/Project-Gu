param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $RimWorldPath 'Mods\ProjectGU-LivingCultivation'
if (Test-Path -LiteralPath (Join-Path $target 'Assemblies\ProjectGu.dll')) {
    throw 'Remove the old root Assemblies folder from your installed mod before enabling 1.7.'
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$buildOutput = Join-Path $tempRoot ('ProjectGuInstall-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildOutput | Out-Null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -RimWorldPath $RimWorldPath -HarmonyPath $HarmonyPath -OutputDirectory $buildOutput
    $dll = Join-Path $buildOutput 'ProjectGu.dll'
    $versionTarget = Join-Path $target '1.7'
    $assemblyTarget = Join-Path $versionTarget 'Assemblies'
    New-Item -ItemType Directory -Force $assemblyTarget | Out-Null
    foreach ($folder in @('About','Defs','Languages','Patches','Textures')) {
        Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $target -Recurse -Force
    }
    $versionSource = Join-Path $root '1.7'
    if (Test-Path -LiteralPath $versionSource) {
        Get-ChildItem -LiteralPath $versionSource -Exclude 'Assemblies' | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $versionTarget -Recurse -Force
        }
    }
    foreach ($asset in @('SpiritSpring.png','SpringPitEntrance.png','SpringRopeExit.png',
                        'SpiritGrass\PatchA.png','SpiritGrass\PatchB.png','SpiritGrass\PatchC.png','SpiritGrass\PatchD.png',
                        'MoonOrchid\ColonyA.png','MoonOrchid\ColonyB.png')) {
        if (!(Test-Path -LiteralPath (Join-Path $target "Textures\ProjectGU\$asset"))) { throw "Texture missing after install: $asset" }
    }
    $symbols = Join-Path $buildOutput 'ProjectGu.pdb'
    if (Test-Path -LiteralPath $symbols -PathType Leaf) { Copy-Item -LiteralPath $symbols -Destination $assemblyTarget -Force }
    Copy-Item -LiteralPath $dll -Destination $assemblyTarget -Force
    Write-Host "Installed at $target. Enable in Mods and restart."
}
finally {
    $resolved = (Resolve-Path -LiteralPath $buildOutput).Path
    if ([IO.Path]::GetDirectoryName($resolved) -eq $tempRoot -and [IO.Path]::GetFileName($resolved) -match '^ProjectGuInstall-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
