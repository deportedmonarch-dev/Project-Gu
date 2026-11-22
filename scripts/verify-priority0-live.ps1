param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = '',
    [ValidateSet('Core', 'IdeologyOdyssey', 'IdeologyReset', 'Awakening', 'Botanical', 'CargoPrototype', 'AutomaticHauling', 'Slice3A', 'Slice3B1', 'Slice3B2', 'Slice3B3A', 'Slice3B3B', 'Slice3B4', 'Slice3B5A', 'Slice3B5B', 'Slice3B5BPerf', 'VanillaGuInventory', 'V2EngineProbe', 'V1Migration', 'Phase2Lifecycle', 'DormantHauling', 'ManifestationProbe', 'Both', 'Regression')][string[]]$Profile = @('Both'),
    [switch]$Rendered,
    [string]$Legacy31Save = '',
    [string]$Legacy39Save = '',
    # Saves written by the v0.2.5 Schema V1 build. Each is copied, never modified, and checked by SHA-256.
    [string[]]$V1Saves = @(),
    # Keep the disposable game copy after the run for debugging. It is removed by default.
    [switch]$KeepGameCopy,
    # Extra official expansions for any profile, for example royalty, biotech, anomaly, odyssey.
    [ValidateSet('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey')][string[]]$Expansions = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$historical = @{ Legacy31 = $Legacy31Save; Legacy39 = $Legacy39Save }
foreach ($path in $historical.Values) {
    if ($path -and !(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Historical save not found: $path" }
}
if (!$HarmonyPath) {
    $HarmonyPath = Join-Path $RimWorldPath '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'
}
$HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path
$harmonyMod = Split-Path (Split-Path (Split-Path $HarmonyPath))
$testRoot = Join-Path $root ('tests/obj/Priority0-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $testRoot 'RimWorld'
New-Item -ItemType Directory -Path $game -Force | Out-Null
robocopy $RimWorldPath $game /E /XD (Join-Path $RimWorldPath 'Mods') /R:1 /W:1 /NFL /NDL /NJH /NC /NS /NP
if ($LASTEXITCODE -ge 8) { throw "Isolated game copy failed with code $LASTEXITCODE." }

$source = Join-Path $root 'tests/ProjectGu.LiveVerification'
dotnet build (Join-Path $source 'ProjectGu.LiveVerification.csproj') -c Release "-p:RimWorldPath=$RimWorldPath" "-p:HarmonyPath=$HarmonyPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Live verification build failed.' }

$mods = Join-Path $game 'Mods'
$project = Join-Path $mods 'ProjectGu'
$test = Join-Path $mods 'ProjectGuPriority0Test'
$harmony = Join-Path $mods 'Harmony'
New-Item -ItemType Directory -Path $project,$harmony,(Join-Path $test 'About'),(Join-Path $test 'Assemblies') -Force | Out-Null
foreach ($entry in @('About','Defs','Patches','Textures','1.7')) {
    Copy-Item -LiteralPath (Join-Path $root $entry) -Destination $project -Recurse
}
foreach ($entry in @('About','1.7','LoadFolders.xml')) {
    Copy-Item -LiteralPath (Join-Path $harmonyMod $entry) -Destination $harmony -Recurse
}
Copy-Item -LiteralPath (Join-Path $source 'About.xml') -Destination (Join-Path $test 'About')
Copy-Item -LiteralPath (Join-Path $source 'Defs') -Destination $test -Recurse
Copy-Item -LiteralPath (Join-Path $source 'bin/Release/net10.0/ProjectGu.Verification.dll') -Destination (Join-Path $test 'Assemblies')

function Invoke-LiveStage([string]$SaveData, [string]$Stage, [bool]$Fresh, [bool]$UseRenderer, [string]$Extra = '') {
    $arguments = '-gupriority0test -savedatafolder="' + $SaveData + '"'
    if (!$UseRenderer) { $arguments = '-headless ' + $arguments }
    if ($Fresh) { $arguments = '-quicktest ' + $arguments }
    if ($Extra) { $arguments = "$Extra $arguments" }
    $launch = @{
        FilePath = Join-Path $game 'RimWorld.exe'
        WorkingDirectory = $game
        WindowStyle = if ($UseRenderer) { 'Normal' } else { 'Hidden' }
        PassThru = $true
        ArgumentList = $arguments
        RedirectStandardOutput = Join-Path $SaveData "$Stage.stdout.txt"
        RedirectStandardError = Join-Path $SaveData "$Stage.stderr.txt"
    }
    $process = Start-Process @launch
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($timer.Elapsed.TotalSeconds -gt 180) {
            $process.Kill()
            throw "RimWorld timed out in stage $Stage. See $SaveData"
        }
    }
    $result = Join-Path $SaveData 'priority0-result.txt'
    if (!(Test-Path -LiteralPath $result) -or !(Select-String -LiteralPath $result -SimpleMatch "PASS $Stage" -Quiet)) {
        if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result }
        throw "Live stage $Stage failed. See $SaveData"
    }
}

$originalLayerSetting = $env:VK_LOADER_LAYERS_DISABLE
try {
    $env:VK_LOADER_LAYERS_DISABLE = '~implicit~'
    $profiles = @(switch ($Profile) {
        'Both' { 'Core', 'IdeologyOdyssey' }
        'Regression' { 'IdeologyOdyssey', 'IdeologyReset', 'Awakening', 'Botanical', 'CargoPrototype', 'AutomaticHauling' }
        default { $_ }
    })
    foreach ($legacy in @('Legacy31', 'Legacy39')) {
        if ($historical[$legacy]) { $profiles += $legacy }
    }
    foreach ($name in ($profiles | Select-Object -Unique)) {
        $saveData = Join-Path $testRoot $name
        $config = Join-Path $saveData 'Config'
        New-Item -ItemType Directory -Path $config -Force | Out-Null
        $dlc = ''
        if ($name -in @('IdeologyOdyssey', 'IdeologyReset')) { $dlc = '<li>ludeon.rimworld.ideology</li>' }
        if ($name -eq 'IdeologyOdyssey') { $dlc += '<li>ludeon.rimworld.odyssey</li>' }
        foreach ($expansion in $Expansions) { if ($dlc -notlike "*ludeon.rimworld.$expansion*") { $dlc += "<li>ludeon.rimworld.$expansion</li>" } }
        $probe = switch ($name) {
            'IdeologyReset' { '-guemptycavernreset' }
            'Awakening' { '-guawakeningsafety' }
            'Botanical' { '-gubotanical' }
            'CargoPrototype' { '-gucargoprototype' }
            'AutomaticHauling' { '-guautomatichauling' }
            'Slice3A' { '-guslice3a' }
            'Slice3B1' { '-guslice3b1' }
            'Slice3B2' { '-guslice3b2' }
            'Slice3B3A' { '-guslice3b3a' }
            'Slice3B3B' { '-guslice3b3b' }
            'Slice3B4' { '-guslice3b4' }
            'Slice3B5A' { '-guslice3b5a' }
            'Slice3B5B' { '-guslice3b5b' }
            'Slice3B5BPerf' { '-guslice3b5bperf' }
            'VanillaGuInventory' { '-guvanillainventory' }
            'V2EngineProbe' { '-guv2engine' }
            'Phase2Lifecycle' { '-guphase2lifecycle' }
            'DormantHauling' { '-gudormanthauling' }
            'ManifestationProbe' { '-gumanifestprobe' }
            'Legacy31' { '-gulegacy31' }
            'Legacy39' { '-gulegacy39' }
            default { '' }
        }
        $configXml = @"
<ModsConfigData>
  <version>1.7.5014 rev1195</version>
  <activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li>$dlc<li>projectgu.livingcultivation</li><li>projectgu.priority0test</li></activeMods>
  <knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions>
</ModsConfigData>
"@
        $configXml | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $config 'ModsConfig.xml')
        '<PrefsData><devMode>true</devMode><runInBackground>true</runInBackground></PrefsData>' |
            Set-Content -Encoding UTF8 -LiteralPath (Join-Path $config 'Prefs.xml')
        if ($historical[$name]) {
            $original = (Resolve-Path -LiteralPath $historical[$name]).Path
            $hash = (Get-FileHash -LiteralPath $original).Hash
            $saves = Join-Path $saveData 'Saves'
            New-Item -ItemType Directory -Path $saves | Out-Null
            $copy = Join-Path $saves 'autostart.rws'
            Copy-Item -LiteralPath $original -Destination $copy
            if ($name -eq 'Legacy31') {
                # The historical generator left an empty test-only component in this fixture.
                $xml = [IO.File]::ReadAllText($copy)
                [IO.File]::WriteAllText($copy, $xml.Replace('<li Class="Legacy31" />', ''))
            }
            Invoke-LiveStage $saveData 'reload' $false $Rendered.IsPresent $probe
            if ((Get-FileHash -LiteralPath $original).Hash -ne $hash) { throw "Historical source file changed: $original" }
            Write-Host "PASS $name historical copy loaded. The original SHA-256 is unchanged. Evidence in $saveData"
            Get-Content -LiteralPath (Join-Path $saveData 'priority0-result.txt')
            continue
        }
        if ($name -eq 'V1Migration') {
            foreach ($sourceSave in $V1Saves) {
                $original = (Resolve-Path -LiteralPath $sourceSave).Path
                $hash = (Get-FileHash -LiteralPath $original).Hash
                $source = [IO.Path]::GetFileNameWithoutExtension($original)
                $migrationData = Join-Path $testRoot "V1Migration-$source"
                New-Item -ItemType Directory -Path (Join-Path $migrationData 'Saves'),(Join-Path $migrationData 'Config') -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $config 'ModsConfig.xml'),(Join-Path $config 'Prefs.xml') -Destination (Join-Path $migrationData 'Config')
                Copy-Item -LiteralPath $original -Destination (Join-Path $migrationData 'Saves/autostart.rws')
                Copy-Item -LiteralPath $original -Destination (Join-Path $migrationData 'v1-source.rws')
                Invoke-LiveStage $migrationData 'reload' $false $false "-guv1migration -guv1source=$source"
                Invoke-LiveStage $migrationData 'resave' $false $false "-guv1migration -guv1resave -guv1source=$source"
                if ((Get-FileHash -LiteralPath $original).Hash -ne $hash) { throw "V1 source save changed: $original" }
                Write-Host "PASS V1Migration $source. The original SHA-256 is unchanged. Evidence in $migrationData"
                Get-Content -LiteralPath (Join-Path $migrationData 'priority0-result.txt')
            }
            continue
        }
        if ($name -eq 'Slice3B5BPerf') {
            Invoke-LiveStage $saveData 'new' $true $false $probe
            $sample = 0
            foreach ($variant in @('empty', 'contained', 'contained', 'empty', 'empty', 'contained')) {
                $sample += 1
                $stage = "perf-$variant-$sample"
                Copy-Item -LiteralPath (Join-Path $saveData "Saves/gu-perf-$variant.rws") -Destination (Join-Path $saveData 'Saves/autostart.rws') -Force
                Invoke-LiveStage $saveData $stage $false $false "$probe -guperf-stage=$stage"
            }
            Write-Host "PASS $name matched native benchmark. Raw JSON samples in $saveData"
            Get-Content -LiteralPath (Join-Path $saveData 'priority0-result.txt')
            continue
        }
        if ($Rendered) {
            # Stock graphical quick-start can stall in the entry scene. Load a seeded disposable colony.
            Invoke-LiveStage $saveData 'bootstrap' $true $false '-gupriority0bootstrap'
            Invoke-LiveStage $saveData 'new' $false $true "-gupriority0fixture $probe"
        } else {
            Invoke-LiveStage $saveData 'new' $true $false $probe
        }
        Invoke-LiveStage $saveData 'reload' $false $Rendered.IsPresent $probe
        if ($name -eq 'Slice3B4') {
            Invoke-LiveStage $saveData 'released' $false $Rendered.IsPresent "$probe -guslice3b4released"
        }
        if ($name -eq 'Slice3B5B') {
            Invoke-LiveStage $saveData 'aperture' $false $Rendered.IsPresent "$probe -guslice3b5baperture"
            Invoke-LiveStage $saveData 'released' $false $Rendered.IsPresent "$probe -guslice3b5breleased"
        }
        Write-Host "PASS $name disposable generation and fresh-process reload. Evidence in $saveData"
        Get-Content -LiteralPath (Join-Path $saveData 'priority0-result.txt')
    }
}
finally {
    $env:VK_LOADER_LAYERS_DISABLE = $originalLayerSetting
    # The game copy is disposable and large. Evidence (saves, logs, results) lives beside it and is kept.
    if (!$KeepGameCopy -and (Test-Path -LiteralPath (Join-Path $game 'RimWorld.exe'))) {
        Remove-Item -LiteralPath $game -Recurse -Force -ErrorAction SilentlyContinue
    }
}
