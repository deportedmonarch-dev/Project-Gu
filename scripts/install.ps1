param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mods = Join-Path ([IO.Path]::GetFullPath($RimWorldPath)) 'Mods'
$target = Join-Path $mods 'ProjectGU-LivingCultivation'
$packageId = 'projectgu.livingcultivation'
$managedFolders = @('About', 'Defs', 'Languages', 'Patches', 'Textures', '1.7')
$manifest = Join-Path $target '.projectgu-install.json'
$legacyRetirements = @{
    # Git LF and Windows CRLF versions of the Alpha 5A ThinkTree patch.
    'Defs/ThinkTreeDefs/GU_GuTending.xml' = @(
        '5A6C0658A210D534A11F0CEF67E0B8B1440F065244DBC8671309D86E16BE2767',
        '6298B5C44BA26629E9B199B77865030673BAC07B2D53C87FCE5785126DD7B9A9'
    )
}

function Assert-InstallPath([string]$Path) {
    $entry = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Project GU installation does not write through links or junctions: $Path"
    }
}

function Get-InstallPath([string]$RelativePath) {
    $parts = $RelativePath -split '/'
    if ($parts.Count -lt 2 -or $parts[0] -notin $managedFolders) {
        throw "Invalid managed Project GU path: $RelativePath"
    }
    $path = $target
    foreach ($part in $parts) {
        if (!$part -or $part -match '[\x00-\x1f<>:"\\|?*]|[. ]$|^\s|^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') {
            throw "Invalid managed Project GU path: $RelativePath"
        }
        $path = Join-Path $path $part
        Assert-InstallPath $path
    }
    return $path
}

function Publish-InstallFile([string]$Source, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $staged = Join-Path $parent ('.projectgu-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        Copy-Item -LiteralPath $Source -Destination $staged
        if ([IO.File]::Exists($Destination)) {
            [IO.File]::Replace($staged, $Destination, [NullString]::Value)
        } else {
            [IO.File]::Move($staged, $Destination)
        }
    } finally {
        if ([IO.File]::Exists($staged)) { [IO.File]::Delete($staged) }
    }
}

Assert-InstallPath $mods
Assert-InstallPath $target
Assert-InstallPath $manifest
$about = Get-InstallPath 'About/About.xml'
if (Test-Path -LiteralPath $about) {
    $metadata = [xml](Get-Content -LiteralPath $about -Raw)
    if ($metadata.ModMetaData.packageId -ne $packageId) { throw "Another mod owns $target." }
}
if (Test-Path -LiteralPath (Join-Path $target 'Assemblies\ProjectGu.dll')) {
    throw "A pre-1.7 ProjectGu.dll is still installed in $target\Assemblies. Use a 1.7 layout before upgrading."
}

$previous = @{}
if (Test-Path -LiteralPath $manifest) {
    $record = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if ($record.packageId -ne $packageId -or $record.version -ne 1 -or !$record.files) {
        throw "Unrecognized Project GU install manifest: $manifest"
    }
    foreach ($entry in $record.files) {
        if ($entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or !$entry.path -or $previous.ContainsKey($entry.path)) {
            throw "Invalid Project GU install manifest entry: $($entry.path)"
        }
        $null = Get-InstallPath $entry.path
        $previous.Add($entry.path, @($entry.sha256))
    }
}
$legacyRetirements.GetEnumerator() | ForEach-Object {
    $previous[$_.Key] = @($previous[$_.Key] | Where-Object { $_ }) + $_.Value
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$buildOutput = Join-Path $tempRoot ('ProjectGuInstall-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildOutput | Out-Null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -RimWorldPath $RimWorldPath -HarmonyPath $HarmonyPath -OutputDirectory $buildOutput
    $payload = @{}
    foreach ($folder in $managedFolders) {
        $source = Join-Path $root $folder
        if (!(Test-Path -LiteralPath $source -PathType Container)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $source -File -Recurse -Force)) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            if ($relative -like '1.7/Assemblies/*') { continue }
            $payload.Add($relative, $file.FullName)
        }
    }
    $assemblyPath = '1.7/Assemblies/ProjectGu.dll'
    $payload.Add($assemblyPath, (Join-Path $buildOutput 'ProjectGu.dll'))
    $symbols = Join-Path $buildOutput 'ProjectGu.pdb'
    if (Test-Path -LiteralPath $symbols -PathType Leaf) { $payload.Add('1.7/Assemblies/ProjectGu.pdb', $symbols) }

    foreach ($asset in @('SpiritSpring.png','SpringPitEntrance.png','SpringRopeExit.png',
                        'SpiritGrass\PatchA.png','SpiritGrass\PatchB.png','SpiritGrass\PatchC.png','SpiritGrass\PatchD.png',
                        'MoonOrchid\ColonyA.png','MoonOrchid\ColonyB.png')) {
        if (!$payload.ContainsKey('Textures/ProjectGU/' + $asset.Replace('\', '/'))) {
            throw "Texture missing from the Project GU payload: $asset"
        }
    }

    $destinations = @{}
    $files = @(foreach ($relative in ($payload.Keys | Sort-Object)) {
        $destinations.Add($relative, (Get-InstallPath $relative))
        if (Test-Path -LiteralPath $destinations[$relative] -PathType Container) {
            throw "A directory blocks the managed Project GU file: $relative"
        }
        @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $payload[$relative]).Hash }
    })
    $retirements = @(foreach ($relative in ($previous.Keys | Sort-Object)) {
        if ($payload.ContainsKey($relative)) { continue }
        $path = Get-InstallPath $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        if ((Get-FileHash -LiteralPath $path).Hash -in $previous[$relative]) {
            $path
        } else {
            Write-Warning "Preserving locally changed retired Project GU file: $path"
        }
    })
    $newManifest = Join-Path $buildOutput 'install-manifest.json'
    @{ version = 1; packageId = $packageId; files = $files } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $newManifest -Encoding UTF8
    foreach ($relative in ($payload.Keys | Where-Object { $_ -ne $assemblyPath } | Sort-Object)) {
        Publish-InstallFile $payload[$relative] $destinations[$relative]
    }
    foreach ($path in $retirements) {
        Remove-Item -LiteralPath $path -Force
        Write-Host "Retired obsolete Project GU file $path"
    }
    Publish-InstallFile $payload[$assemblyPath] $destinations[$assemblyPath]
    Publish-InstallFile $newManifest $manifest
    Write-Host "Installed at $target. Enable in Mods and restart."
}
finally {
    $resolved = (Resolve-Path -LiteralPath $buildOutput).Path
    if ([IO.Path]::GetDirectoryName($resolved) -eq $tempRoot -and [IO.Path]::GetFileName($resolved) -match '^ProjectGuInstall-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
