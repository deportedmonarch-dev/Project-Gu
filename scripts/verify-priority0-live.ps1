param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = '',
    [ValidateSet('Core', 'IdeologyOdyssey', 'IdeologyReset', 'Awakening', 'Both')][string]$Profile = 'Both',
    [switch]$Rendered
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
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
    $profiles = if ($Profile -eq 'Both') { @('Core', 'IdeologyOdyssey') } else { @($Profile) }
    foreach ($name in $profiles) {
        $saveData = Join-Path $testRoot $name
        $config = Join-Path $saveData 'Config'
        New-Item -ItemType Directory -Path $config -Force | Out-Null
        $dlc = ''
        if ($name -in @('IdeologyOdyssey', 'IdeologyReset')) { $dlc = '<li>ludeon.rimworld.ideology</li>' }
        if ($name -eq 'IdeologyOdyssey') { $dlc += '<li>ludeon.rimworld.odyssey</li>' }
        $probe = switch ($name) {
            'IdeologyReset' { '-guemptycavernreset' }
            'Awakening' { '-guawakeningsafety' }
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
        if ($Rendered) {
            # Stock graphical quick-start can stall in the entry scene. Load a seeded disposable colony.
            Invoke-LiveStage $saveData 'bootstrap' $true $false '-gupriority0bootstrap'
            Invoke-LiveStage $saveData 'new' $false $true "-gupriority0fixture $probe"
        } else {
            Invoke-LiveStage $saveData 'new' $true $false $probe
        }
        Invoke-LiveStage $saveData 'reload' $false $Rendered.IsPresent $probe
        Write-Host "PASS $name disposable generation and fresh-process reload. Evidence in $saveData"
        Get-Content -LiteralPath (Join-Path $saveData 'priority0-result.txt')
    }
}
finally {
    $env:VK_LOADER_LAYERS_DISABLE = $originalLayerSetting
}
