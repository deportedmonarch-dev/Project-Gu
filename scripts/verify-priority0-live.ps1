param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = '',
    [ValidateSet('Core', 'IdeologyOdyssey', 'IdeologyReset', 'Awakening', 'Botanical', 'CargoPrototype', 'AutomaticHauling', 'Slice3A', 'Slice3B1', 'Slice3B2', 'Slice3B3A', 'Slice3B3B', 'Slice3B4', 'Slice3B5A', 'Slice3B5B', 'Slice3B5BPerf', 'VanillaGuInventory', 'V2EngineProbe', 'V1Migration', 'Phase2Lifecycle', 'DormantHauling', 'ManifestationProbe', 'CultivatorLifecycle', 'CultivatorMigration', 'Both', 'Regression')][string[]]$Profile = @('Both'),
    [switch]$Rendered,
    [string]$Legacy31Save = '',
    [string]$Legacy39Save = '',
    # Saves written by the v0.2.5 Schema V1 build. Each is copied, never modified, and checked by SHA-256.
    [string[]]$V1Saves = @(),
    [string]$V1Manifest = '',
    [string]$V1Phase2Evidence = '',
    [hashtable]$CultivatorV2Saves = @{},
    [string]$CultivatorVariantsSource = '',
    [string[]]$CultivatorFixtures = @(),
    [switch]$PlanOnly,
    # Keep the disposable game copy after the run for debugging. It is removed by default.
    [switch]$KeepGameCopy,
    # Extra official expansions for any profile, for example royalty, biotech, anomaly, odyssey.
    [ValidateSet('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey')][string[]]$Expansions = @(),
    # Independent test units run at once. 1 runs them one after another through the same scheduler.
    [ValidateRange(1, 8)][int]$Parallel = 1,
    # Development only. Stops launching new processes after the first failure. Running processes finish and keep their evidence.
    # By default every independent unit runs and the campaign reports complete results.
    [switch]$FailFast,
    [ValidateRange(30, 3600)][int]$StageTimeoutSeconds = 180,
    [ValidateRange(250, 10000)][int]$SampleIntervalMs = 1000,
    # Shared: one game copy for all workers, made read-only by an access rule while games run.
    # PerWorker: an independently writable copy for each worker slot. Use it only if the game must write to its folder.
    [ValidateSet('Shared', 'PerWorker')][string]$GameCopyMode = 'Shared',
    # The engine's full file log (Logs\Player.log) has no per-instance location: it always goes to the shared user data
    # folder. Test processes run with -NoFileLog so no instance writes outside its own folder; standard output is captured.
    # This switch, for sequential runs only, keeps the file log and copies it into each stage's evidence.
    [switch]$CaptureFileLog
)
$ErrorActionPreference = 'Stop'
$allowedCultivatorFixtures = @('aperture-conflicts', 'legacy-evidence', 'duplicate-identities',
    'valid-committed', 'valid-empty', 'partial-failure', 'failed-rollback',
    'V1-Slice3B1', 'V1-Slice3B2', 'V1-Slice3B3B', 'V1-Slice3B4', 'V1-Slice3B5A',
    'V1-V2EngineProbe', 'V1-VanillaGuInventory', 'V2-Phase2Lifecycle', 'V2-Slice3B3B',
    'V2-V2EngineProbe', 'Legacy31-derivative', 'Legacy39-derivative')
if ($CultivatorFixtures) {
    $unknownFixtures = @($CultivatorFixtures | Where-Object { $_ -notin $allowedCultivatorFixtures })
    if ($unknownFixtures) { throw "Unknown cultivator fixture selection: $($unknownFixtures -join ', ')" }
    if ('aperture-conflicts' -in $CultivatorFixtures -and !$CultivatorV2Saves['Phase2Lifecycle']) {
        throw 'The aperture-conflicts fixture requires CultivatorV2Saves.Phase2Lifecycle.'
    }
}
$historical = @{ Legacy31 = $Legacy31Save; Legacy39 = $Legacy39Save }
$profiles = @(switch ($Profile) {
    'Both' { 'Core', 'IdeologyOdyssey' }
    'Regression' { 'IdeologyOdyssey', 'IdeologyReset', 'Awakening', 'Botanical', 'CargoPrototype', 'AutomaticHauling' }
    default { $_ }
})
foreach ($legacy in @('Legacy31', 'Legacy39')) { if ($historical[$legacy]) { $profiles += $legacy } }
$profiles = @($profiles | Select-Object -Unique)
if ($CultivatorFixtures -and $profiles -notcontains 'CultivatorMigration') {
    throw 'CultivatorFixtures requires -Profile CultivatorMigration.'
}
if ($PlanOnly) {
    if ($Rendered -and $Parallel -gt 1) { throw 'Rendered runs require -Parallel 1.' }
    if ($CaptureFileLog -and $Parallel -gt 1) { throw '-CaptureFileLog requires -Parallel 1.' }
    if ($profiles -contains 'CultivatorMigration' -and !$CultivatorFixtures -and
        (!$V1Saves -or !$V1Manifest -or !$V1Phase2Evidence -or !$CultivatorV2Saves['Phase2Lifecycle'] -or !$CultivatorVariantsSource)) {
        throw 'CultivatorMigration requires all V1 and V2 fixture sources and the manifest.'
    }
    $selected = if ($CultivatorFixtures) { @($CultivatorFixtures) } else { @($allowedCultivatorFixtures) }
    foreach ($fixture in $selected) {
        $stages = if ($fixture -in @('partial-failure', 'failed-rollback')) {
            'blocked, restore pristine input, migrate, reload1, reload2'
        } else { 'migrate, reload1, reload2' }
        Write-Output "C3-$($fixture): $stages"
    }
    if ($Profile -ne 'CultivatorMigration') { Write-Output "Profiles: $($profiles -join ', ')" }
    return
}
$root = Split-Path -Parent $PSScriptRoot
$campaignTimer = [Diagnostics.Stopwatch]::StartNew()
foreach ($path in $historical.Values) {
    if ($path -and !(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Historical save not found: $path" }
}
if (!$HarmonyPath) {
    $HarmonyPath = Join-Path $RimWorldPath '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'
}
$HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path
$harmonyMod = Split-Path (Split-Path (Split-Path $HarmonyPath))
$objRoot = [IO.Path]::GetFullPath((Join-Path $root 'tests/obj'))

# ---------------------------------------------------------------------------------------------------------------------
# Profile planning and argument checks happen before any resource is created.
if ($profiles -contains 'CultivatorMigration' -and !$CultivatorFixtures -and
    (!$V1Saves -or !$V1Manifest -or !$V1Phase2Evidence -or !$CultivatorV2Saves['Phase2Lifecycle'] -or !$CultivatorVariantsSource)) {
    throw 'CultivatorMigration requires the V1 saves, manifest and Phase 2 result directory, a named Phase2Lifecycle V2 save and a legacy evidence source.'
}
if ($CultivatorFixtures -and !$CultivatorV2Saves['Phase2Lifecycle']) {
    throw 'Targeted cultivator fixtures require the Phase2Lifecycle source save.'
}
if ($Rendered -and $Parallel -gt 1) { throw 'Rendered runs open game windows and must use -Parallel 1.' }
if ($CaptureFileLog -and $Parallel -gt 1) { throw '-CaptureFileLog uses the shared engine log file and must use -Parallel 1.' }
if ($profiles -contains 'Slice3B5BPerf') {
    # The benchmark measures save, load and registry cost. Any concurrent game process would distort it.
    if ($profiles.Count -gt 1 -or $Parallel -gt 1) { throw 'Slice3B5BPerf must run alone: one profile and -Parallel 1.' }
    $running = @(Get-Process -Name RimWorld -ErrorAction SilentlyContinue)
    if ($running) { throw "Slice3B5BPerf must run alone, but $($running.Count) RimWorld process(es) are running." }
}
if ($profiles -contains 'V1Migration' -and !$V1Saves) { Write-Warning 'V1Migration selected without -V1Saves. It has nothing to run.' }

# ---------------------------------------------------------------------------------------------------------------------
# Run ownership. Each run holds an exclusive handle on run.lock for its whole lifetime. The operating system releases the
# handle when the runner exits or is killed, so an openable lock means the owner is gone. Another run's resources are
# removed only when its lock is free AND no process runs from its folder, which also covers the gaps between stages.
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$denyRights = [Security.AccessControl.FileSystemRights]'WriteData, AppendData, WriteExtendedAttributes, WriteAttributes, Delete, DeleteSubdirectoriesAndFiles'
function New-ReadOnlyRule {
    # Explicit rights only: icacls 'W' also denies SYNCHRONIZE, which blocks reading and execution.
    [Security.AccessControl.FileSystemAccessRule]::new($userSid, $denyRights, 'ContainerInherit, ObjectInherit', 'None', 'Deny')
}
function Set-GameReadOnly([string]$Path) {
    $dir = [IO.DirectoryInfo]$Path
    $acl = $dir.GetAccessControl('Access')
    $acl.AddAccessRule((New-ReadOnlyRule))
    $dir.SetAccessControl($acl)
}
function Clear-GameReadOnly([string]$Path) {
    $dir = [IO.DirectoryInfo]$Path
    $acl = $dir.GetAccessControl('Access')
    [void]$acl.RemoveAccessRuleAll((New-ReadOnlyRule))
    $dir.SetAccessControl($acl)
}
function Get-ProcessesUnder([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($full, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
}
function Test-GameCopyPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $full.StartsWith($objRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $full) -match '^RimWorld(-\d+)?$' -and
        (Test-Path -LiteralPath (Join-Path $full 'RimWorld.exe'))
}
# Removes a disposable game copy of this run. Saves, logs and results beside it are evidence and are never touched.
function Remove-GameCopy([string]$Path) {
    if (!$Path -or !(Test-Path -LiteralPath $Path) -or !(Test-GameCopyPath $Path)) { return }
    $full = [IO.Path]::GetFullPath($Path)
    if (Get-ProcessesUnder $full) { Write-Warning "Game copy still in use, kept: $full"; return }
    Clear-GameReadOnly $full
    if (!$KeepGameCopy) { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue }
}
# Stale-run recovery. Only removes the game copies of runs whose owner and child processes are both gone.
function Remove-StaleGameCopies([string]$OwnRun) {
    if (!(Test-Path -LiteralPath $objRoot)) { return }
    foreach ($run in Get-ChildItem -LiteralPath $objRoot -Directory) {
        if ($run.FullName -eq $OwnRun) { continue }
        $copies = @(Get-ChildItem -LiteralPath $run.FullName -Directory | Where-Object { Test-GameCopyPath $_.FullName })
        if (!$copies) { continue }
        $lockPath = Join-Path $run.FullName 'run.lock'
        $handle = $null
        if (Test-Path -LiteralPath $lockPath) {
            try { $handle = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None') }
            catch { continue }  # The owner still holds its lock: an active run.
        } else {
            # A run from the runner before run locks. Treat it as active until nothing in its evidence changed for two hours.
            $newest = Get-ChildItem -LiteralPath $run.FullName -Directory | Where-Object { !(Test-GameCopyPath $_.FullName) } |
                ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -File -ErrorAction SilentlyContinue } |
                Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            if (!$newest -or $newest.LastWriteTimeUtc -gt [DateTime]::UtcNow.AddHours(-2)) { continue }
        }
        try {
            # Holding the dead owner's lock prevents a second recovering runner from racing this one.
            if (Get-ProcessesUnder $run.FullName) { Write-Warning "Stale run still has game processes, kept: $($run.FullName)"; continue }
            foreach ($copy in $copies) {
                Clear-GameReadOnly $copy.FullName
                Remove-Item -LiteralPath $copy.FullName -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Removed stale game copy $($copy.FullName)"
            }
        }
        finally { if ($handle) { $handle.Dispose() } }
    }
}

# Hash manifest of a whole tree: every file's size and SHA-256 plus every directory, so creation, deletion, rename and
# modification are all visible.
function Get-TreeManifest([string]$Path) {
    $base = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $manifest = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    $sha = [Security.Cryptography.SHA256Cng]::new()  # SHA256.Create() is the managed implementation here: about 8x slower.
    try {
        foreach ($dir in [IO.Directory]::EnumerateDirectories($base, '*', 'AllDirectories')) { $manifest['dir:' + $dir.Substring($base.Length)] = 'dir' }
        foreach ($file in [IO.Directory]::EnumerateFiles($base, '*', 'AllDirectories')) {
            $stream = [IO.File]::Open($file, 'Open', 'Read', 'ReadWrite')
            try { $manifest['file:' + $file.Substring($base.Length)] = '{0}|{1}' -f $stream.Length, ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '') }
            finally { $stream.Dispose() }
        }
    } finally { $sha.Dispose() }
    $manifest
}
function Compare-TreeManifest($Before, $After) {
    $changes = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Before.Keys) {
        if (!$After.ContainsKey($key)) { $changes.Add("removed $key") }
        elseif ($After[$key] -ne $Before[$key]) { $changes.Add("changed $key") }
    }
    foreach ($key in $After.Keys) { if (!$Before.ContainsKey($key)) { $changes.Add("added $key") } }
    , $changes
}
function Write-Manifest($Manifest, [string]$Path) {
    [IO.File]::WriteAllLines($Path, [string[]]@($Manifest.GetEnumerator() | ForEach-Object { "$($_.Key)`t$($_.Value)" }))
}

function Copy-CheckedSave([string]$Source, [string]$Destination) {
    $path = (Resolve-Path -LiteralPath $Source).Path
    $entry = $sourceSaves[$path]
    Copy-Item -LiteralPath $path -Destination $Destination
    if ((Get-FileHash -LiteralPath $Destination).Hash -ne $entry.Before) {
        throw "Save copy does not match the locked source: $Destination"
    }
    ([IO.FileInfo]$Destination).IsReadOnly = $false
}

function Write-SourceSaveEvidence {
    $entries = @($sourceSaves.Values | Sort-Object Path | Select-Object Path, Before, After, Expected, Unchanged, Error)
    ConvertTo-Json -InputObject $entries |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $testRoot 'source-save-hashes.json')
}

# ---------------------------------------------------------------------------------------------------------------------
$runId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $root ('tests/obj/Priority0-' + $runId)
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$runLock = [IO.File]::Open((Join-Path $testRoot 'run.lock'), 'CreateNew', 'ReadWrite', 'None')
$workerSlots = if ($GameCopyMode -eq 'PerWorker') { $Parallel } else { 1 }
$games = @(if ($GameCopyMode -eq 'PerWorker') { 1..$workerSlots | ForEach-Object { Join-Path $testRoot "RimWorld-$_" } } else { Join-Path $testRoot 'RimWorld' })
$phases = [ordered]@{}
$units = New-Object System.Collections.Generic.List[object]
$samples = New-Object System.Collections.Generic.List[object]
$integrity = [ordered]@{ ReadOnlyEnforced = $false; ReadOnlyProbe = @(); GameCopyChanges = @(); IsolationProblems = @(); UserDataFolderChanged = $null; SystemSleep = @(); SystemSleepCheck = 'not checked' }
$campaignFailed = $false
$state = @{ StopScheduling = $false; AnyFailure = $false }
$sourceSaves = @{}
[ordered]@{
    RunId = $runId; ProcessId = $PID; StartedUtc = [DateTime]::UtcNow.ToString('o'); Machine = $env:COMPUTERNAME
    Profiles = $profiles; Parallel = $Parallel; FailFast = $FailFast.IsPresent; GameCopyMode = $GameCopyMode; Expansions = $Expansions
} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $testRoot 'run-owner.json')

function Measure-Phase([string]$Name, [scriptblock]$Body) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try { & $Body } finally { $phases[$Name] = [math]::Round($timer.Elapsed.TotalMilliseconds) }
}

$userDataFolder = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
function Get-NewestWrite([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return $null }
    $newest = Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest) { $newest.LastWriteTimeUtc } else { $null }
}

# System sleep during a run suspends every game while the stage timers keep counting, which turns into false timeouts.
# Hold a "system required" power request for the run. It ends with this process. A forced sleep is detected afterwards.
Add-Type -Namespace ProjectGuRunner -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);'
[void][ProjectGuRunner.Power]::SetThreadExecutionState([uint32]'0x80000001')  # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
$campaignStartLocal = Get-Date

try {
Measure-Phase 'sourcePreflight' {
    $expected = @{}
    if ($V1Manifest) {
        $manifestPath = (Resolve-Path -LiteralPath $V1Manifest).Path
        foreach ($line in Get-Content -LiteralPath $manifestPath) {
            if ($line -notmatch '^([A-Fa-f0-9]{64}) \*([^/\\]+\.rws)$') { throw "Invalid V1 manifest line: $line" }
            $hash = $Matches[1]
            $path = (Resolve-Path -LiteralPath (Join-Path (Split-Path $manifestPath) $Matches[2])).Path
            $expected.Add($path, $hash)
        }
        $v1Paths = @($V1Saves | ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Select-Object -Unique)
        if ($v1Paths.Count -ne $expected.Count -or @($v1Paths | Where-Object { !$expected.ContainsKey($_) }).Count) {
            throw 'The supplied V1 saves must match the manifest inventory.'
        }
    }
    $sources = @($historical.Values) + @($V1Saves) + @($CultivatorV2Saves.Values) + @($CultivatorVariantsSource)
    foreach ($sourcePath in $sources | Where-Object { $_ }) {
        $path = (Resolve-Path -LiteralPath $sourcePath).Path
        if ($sourceSaves.ContainsKey($path)) { continue }
        # FileShare.Read permits independent copies and denies writers, rename and deletion until final hashing.
        $handle = [IO.File]::Open($path, 'Open', 'Read', 'Read')
        $entry = [pscustomobject]@{ Path = $path; Handle = $handle; Before = $null; After = $null
            Expected = $expected[$path]; Unchanged = $null; Error = $null }
        $sourceSaves.Add($path, $entry)
        $entry.Before = (Get-FileHash -InputStream $handle).Hash
        if ($entry.Expected -and $entry.Before -ne $entry.Expected) { throw "V1 source hash does not match the manifest: $path" }
    }
    if ($sourceSaves.Count) { Write-SourceSaveEvidence }
}
Measure-Phase 'staleRecovery' { Remove-StaleGameCopies $testRoot }

# Free-space preflight: every game copy plus saves and a safety margin.
Measure-Phase 'preflight' {
    $modsPrefix = [IO.Path]::GetFullPath((Join-Path $RimWorldPath 'Mods')) + '\'
    $script:gameBytes = 0L
    foreach ($file in ([IO.DirectoryInfo][IO.Path]::GetFullPath($RimWorldPath)).EnumerateFiles('*', 'AllDirectories')) {
        if (!$file.FullName.StartsWith($modsPrefix, [StringComparison]::OrdinalIgnoreCase)) { $script:gameBytes += $file.Length }
    }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($objRoot))
    $requiredBytes = [long]($script:gameBytes * 1.2 * $workerSlots) + 2GB
    if ($drive.AvailableFreeSpace -lt $requiredBytes) {
        throw ("Not enough free space for {2} disposable game cop(ies): {0:N1} GB free, {1:N1} GB required." -f ($drive.AvailableFreeSpace / 1GB), ($requiredBytes / 1GB), $workerSlots)
    }
}
$userDataBefore = Get-NewestWrite $userDataFolder

$master = $games[0]
Measure-Phase 'gameCopy' {
    New-Item -ItemType Directory -Path $master -Force | Out-Null
    robocopy $RimWorldPath $master /E /XD (Join-Path $RimWorldPath 'Mods') /R:1 /W:1 /NFL /NDL /NJH /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Isolated game copy failed with code $LASTEXITCODE." }
}

# The verification assembly builds into one folder shared by every run. A machine-wide mutex keeps a concurrent run from
# rebuilding it while this run builds or stages it.
$source = Join-Path $root 'tests/ProjectGu.LiveVerification'
$buildMutex = [Threading.Mutex]::new($false, 'Local\ProjectGu.LiveVerification.Build')
try {
    try { [void]$buildMutex.WaitOne() } catch [Threading.AbandonedMutexException] { }
    Measure-Phase 'build' {
        & dotnet build (Join-Path $source 'ProjectGu.LiveVerification.csproj') -c Release "-p:RimWorldPath=$RimWorldPath" "-p:HarmonyPath=$HarmonyPath" --nologo |
            Out-File -Encoding utf8 (Join-Path $testRoot 'build.log')
        if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $testRoot 'build.log') -Tail 30; throw 'Live verification build failed.' }
    }
    Measure-Phase 'modStaging' {
        $mods = Join-Path $master 'Mods'
        $project = Join-Path $mods 'ProjectGu'
        $test = Join-Path $mods 'ProjectGuPriority0Test'
        $harmony = Join-Path $mods 'Harmony'
        New-Item -ItemType Directory -Path $project,$harmony,(Join-Path $test 'About'),(Join-Path $test 'Assemblies') -Force | Out-Null
        foreach ($entry in @('About','Defs','Patches','Textures','1.7')) { Copy-Item -LiteralPath (Join-Path $root $entry) -Destination $project -Recurse }
        foreach ($entry in @('About','1.7','LoadFolders.xml')) { Copy-Item -LiteralPath (Join-Path $harmonyMod $entry) -Destination $harmony -Recurse }
        Copy-Item -LiteralPath (Join-Path $source 'About.xml') -Destination (Join-Path $test 'About')
        Copy-Item -LiteralPath (Join-Path $source 'Defs') -Destination $test -Recurse
        Copy-Item -LiteralPath (Join-Path $source 'bin/Release/net10.0/ProjectGu.Verification.dll') -Destination (Join-Path $test 'Assemblies')
    }
}
finally { $buildMutex.ReleaseMutex(); $buildMutex.Dispose() }

if ($GameCopyMode -eq 'PerWorker' -and $games.Count -gt 1) {
    Measure-Phase 'workerCopies' {
        foreach ($copy in $games[1..($games.Count - 1)]) {
            robocopy $master $copy /E /R:1 /W:1 /NFL /NDL /NJH /NC /NS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Worker game copy failed with code $LASTEXITCODE." }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Units are independent: each has its own save data folder (config, saves, caches, logs, results and timing) and a
# sequence of dependent steps. A failed step skips only the rest of its own unit.
function New-ProfileConfig([string]$SaveData, [string]$Name) {
    $config = Join-Path $SaveData 'Config'
    New-Item -ItemType Directory -Path $config -Force | Out-Null
    $dlc = ''
    if ($Name -in @('IdeologyOdyssey', 'IdeologyReset')) { $dlc = '<li>ludeon.rimworld.ideology</li>' }
    if ($Name -eq 'IdeologyOdyssey') { $dlc += '<li>ludeon.rimworld.odyssey</li>' }
    foreach ($expansion in $Expansions) { if ($dlc -notlike "*ludeon.rimworld.$expansion*") { $dlc += "<li>ludeon.rimworld.$expansion</li>" } }
    @"
<ModsConfigData>
  <version>1.7.5014 rev1195</version>
  <activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li>$dlc<li>projectgu.livingcultivation</li><li>projectgu.priority0test</li></activeMods>
  <knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions>
</ModsConfigData>
"@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $config 'ModsConfig.xml')
    '<PrefsData><devMode>true</devMode><runInBackground>true</runInBackground></PrefsData>' |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $config 'Prefs.xml')
}
function New-Unit([string]$Name, [string]$SaveData, [string]$PassMessage) {
    $unit = [pscustomobject]@{
        Name = $Name; SaveData = $SaveData; PassMessage = $PassMessage; Steps = (New-Object System.Collections.Generic.List[object])
        Index = 0; Status = 'QUEUED'; Slot = -1; Process = $null; Timer = $null; Failure = ''; StartedUtc = $null; FinishedUtc = $null
    }
    $units.Add($unit)
    $unit
}
function Add-Launch($Unit, [string]$Stage, [bool]$Fresh, [bool]$UseRenderer, [string]$Extra = '', [bool]$Timing = $true) {
    $Unit.Steps.Add([pscustomobject]@{ Kind = 'Launch'; Stage = $Stage; Fresh = $Fresh; Renderer = $UseRenderer; Extra = $Extra; Timing = $Timing
        Status = 'PENDING'; Detail = ''; LaunchUtc = $null; ProcessStartUtc = $null; ExitUtc = $null; WallMs = $null; ExitCode = $null
        ProcessId = $null; Slot = $null; PeakWorkingSet = 0L; PeakPrivate = 0L; Game = $null })
}
function Add-Action($Unit, [string]$Kind, [hashtable]$Data) {
    $step = [pscustomobject]@{ Kind = $Kind; Stage = $Kind; Status = 'PENDING'; Detail = '' }
    foreach ($key in $Data.Keys) { $step | Add-Member -NotePropertyName $key -NotePropertyValue $Data[$key] }
    $Unit.Steps.Add($step)
}

function Add-CultivatorMigrationUnit([string]$Label, [string]$Path, [string]$Case, [string]$Extra = '') {
    if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw "Invalid migration fixture label: $Label" }
    $name = "C3-$Label"
    $data = Join-Path $testRoot $name
    New-ProfileConfig $data $name
    New-Item -ItemType Directory -Path (Join-Path $data 'Saves') | Out-Null
    $working = Join-Path $data 'Saves/autostart.rws'
    Copy-CheckedSave $Path $working
    if ($Case -ne 'archive') {
        & (Join-Path $PSScriptRoot 'prepare-cultivator-migration-fixture.ps1') -Path $working -Case $Case
    }
    $inputCopy = Join-Path $data 'c3-input.rws'
    Copy-Item -LiteralPath $working -Destination $inputCopy
    $inputHash = (Get-FileHash -LiteralPath $inputCopy).Hash
    if ((Get-FileHash -LiteralPath $working).Hash -ne $inputHash) { throw "C3 working copy differs from its input: $name" }
    ([IO.FileInfo]$inputCopy).IsReadOnly = $true
    if ($Extra -like '*-guv1migration*') {
        Copy-CheckedSave $Path (Join-Path $data 'v1-source.rws')
        $v1name = [IO.Path]::GetFileNameWithoutExtension($Path)
        Copy-Item -LiteralPath (Join-Path $V1Phase2Evidence "V1Migration-$v1name/gu-migration-fixture.json") -Destination (Join-Path $data 'c3-phase2-baseline.json')
    }
    $sourcePath = (Resolve-Path -LiteralPath $Path).Path
    [ordered]@{ Case = $Case; Source = $sourcePath; SourceSHA256 = $sourceSaves[$sourcePath].Before; InputSHA256 = $inputHash } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $data 'c3-fixture-provenance.json')
    $unit = New-Unit $name $data "PASS $name migration and two independent reloads. Evidence in $data"
    $probe = "-guc3=$Case $Extra"
    if ($Case -in @('partial-failure', 'failed-rollback')) {
        Add-Launch $unit 'blocked' $false $false "$probe -guc3stage=blocked"
        Add-Action $unit 'RestoreC3Input' @{ From = $inputCopy; To = $working; Hash = $inputHash }
    }
    foreach ($stage in @('migrate', 'reload1', 'reload2')) {
        Add-Launch $unit $stage $false $false "$probe -guc3stage=$stage"
    }
}

Measure-Phase 'planning' {
    foreach ($name in $profiles) {
        if ($name -eq 'CultivatorMigration') {
            if (!$CultivatorFixtures -or $CultivatorFixtures -contains 'aperture-conflicts') {
                Add-CultivatorMigrationUnit 'aperture-conflicts' $CultivatorV2Saves['Phase2Lifecycle'] 'aperture-conflicts'
            }
            if (!$CultivatorFixtures) { foreach ($path in $V1Saves) {
                $label = [IO.Path]::GetFileNameWithoutExtension($path)
                Add-CultivatorMigrationUnit "V1-$label" $path 'archive' "-guv1migration -guv1source=$label"
            }
            foreach ($label in $CultivatorV2Saves.Keys | Sort-Object) {
                Add-CultivatorMigrationUnit "V2-$label" $CultivatorV2Saves[$label] 'archive'
            }
            Add-CultivatorMigrationUnit 'legacy-evidence' $CultivatorVariantsSource 'legacy-evidence'
            foreach ($case in @('aperture-conflicts', 'duplicate-identities', 'valid-committed', 'valid-empty', 'partial-failure', 'failed-rollback')) {
                if ($case -eq 'aperture-conflicts') { continue }
                if ($CultivatorFixtures -and $CultivatorFixtures -notcontains $case) { continue }
                Add-CultivatorMigrationUnit $case $CultivatorV2Saves['Phase2Lifecycle'] $case
            }
            }
            continue
        }
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
            'CultivatorLifecycle' { '-gucultivatorlifecycle' }
            'DormantHauling' { '-gudormanthauling' }
            'ManifestationProbe' { '-gumanifestprobe' }
            'Legacy31' { '-gulegacy31' }
            'Legacy39' { '-gulegacy39' }
            default { '' }
        }
        $saveData = Join-Path $testRoot $name
        if ($historical[$name]) {
            if ($profiles -contains 'CultivatorMigration') {
                $size = $name.Substring(6)
                Add-CultivatorMigrationUnit "$name-derivative" $historical[$name] "historical-$size"
                continue
            }
            New-ProfileConfig $saveData $name
            $original = (Resolve-Path -LiteralPath $historical[$name]).Path
            $saves = Join-Path $saveData 'Saves'
            New-Item -ItemType Directory -Path $saves | Out-Null
            $copy = Join-Path $saves 'autostart.rws'
            Copy-CheckedSave $original $copy
            if ($name -eq 'Legacy31') {
                # The historical generator left an empty test-only component in this fixture.
                $xml = [IO.File]::ReadAllText($copy)
                [IO.File]::WriteAllText($copy, $xml.Replace('<li Class="Legacy31" />', ''))
            }
            $unit = New-Unit $name $saveData "PASS $name historical copy loaded. Evidence in $saveData"
            Add-Launch $unit 'reload' $false $Rendered.IsPresent $probe
            continue
        }
        if ($name -eq 'V1Migration') {
            foreach ($sourceSave in $V1Saves) {
                $original = (Resolve-Path -LiteralPath $sourceSave).Path
                $sourceName = [IO.Path]::GetFileNameWithoutExtension($original)
                $migrationData = Join-Path $testRoot "V1Migration-$sourceName"
                New-ProfileConfig $migrationData $name
                New-Item -ItemType Directory -Path (Join-Path $migrationData 'Saves') -Force | Out-Null
                Copy-CheckedSave $original (Join-Path $migrationData 'Saves/autostart.rws')
                Copy-CheckedSave $original (Join-Path $migrationData 'v1-source.rws')
                $unit = New-Unit "V1Migration-$sourceName" $migrationData "PASS V1Migration $sourceName. Evidence in $migrationData"
                Add-Launch $unit 'reload' $false $false "-guv1migration -guv1source=$sourceName"
                Add-Launch $unit 'resave' $false $false "-guv1migration -guv1resave -guv1source=$sourceName"
            }
            continue
        }
        New-ProfileConfig $saveData $name
        if ($name -eq 'Slice3B5BPerf') {
            # No game-side timing patches here: the benchmark's measured saves and loads must stay unpatched.
            $unit = New-Unit $name $saveData "PASS $name matched native benchmark. Raw JSON samples in $saveData"
            Add-Launch $unit 'new' $true $false $probe $false
            $sample = 0
            foreach ($variant in @('empty', 'contained', 'contained', 'empty', 'empty', 'contained')) {
                $sample += 1
                $stage = "perf-$variant-$sample"
                Add-Action $unit 'Copy' @{ From = (Join-Path $saveData "Saves/gu-perf-$variant.rws"); To = (Join-Path $saveData 'Saves/autostart.rws') }
                Add-Launch $unit $stage $false $false "$probe -guperf-stage=$stage" $false
            }
            continue
        }
        $unit = New-Unit $name $saveData "PASS $name disposable generation and fresh-process reload. Evidence in $saveData"
        if ($Rendered) {
            # Stock graphical quick-start can stall in the entry scene. Load a seeded disposable colony.
            Add-Launch $unit 'bootstrap' $true $false '-gupriority0bootstrap'
            Add-Launch $unit 'new' $false $true "-gupriority0fixture $probe"
        } else {
            Add-Launch $unit 'new' $true $false $probe
        }
        Add-Launch $unit 'reload' $false $Rendered.IsPresent $probe
        if ($name -eq 'CultivatorLifecycle') {
            Add-Launch $unit 'reload2' $false $Rendered.IsPresent "$probe -gucultivatorreload2"
            Add-Action $unit 'CultivatorInvalidFixture' @{}
            Add-Launch $unit 'invalid' $false $Rendered.IsPresent "$probe -gucultivatorinvalid"
        }
        if ($name -eq 'Slice3B4') { Add-Launch $unit 'released' $false $Rendered.IsPresent "$probe -guslice3b4released" }
        if ($name -eq 'Slice3B5B') {
            Add-Launch $unit 'aperture' $false $Rendered.IsPresent "$probe -guslice3b5baperture"
            Add-Launch $unit 'released' $false $Rendered.IsPresent "$probe -guslice3b5breleased"
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Shared game content is hashed, then locked read-only, and the lock is proven by attempted mutations before any game runs.
Measure-Phase 'hashBefore' {
    $script:manifests = @{}
    for ($i = 0; $i -lt $games.Count; $i++) {
        $script:manifests[$i] = Get-TreeManifest $games[$i]
        Write-Manifest $script:manifests[$i] (Join-Path $testRoot "game-manifest-$i.txt")
    }
}
if ($GameCopyMode -eq 'Shared') {
    Measure-Phase 'readOnly' {
        Set-GameReadOnly $master
        $probeFile = Join-Path $master 'Mods/ProjectGu/About/About.xml'
        $attempts = [ordered]@{
            'create file in game root' = { [IO.File]::WriteAllText((Join-Path $master 'gu-write-probe.tmp'), 'x') }
            'create file in a mod folder' = { [IO.File]::WriteAllText((Join-Path $master 'Mods/ProjectGu/gu-write-probe.tmp'), 'x') }
            'create directory' = { [void][IO.Directory]::CreateDirectory((Join-Path $master 'gu-write-probe-dir')); if (!(Test-Path -LiteralPath (Join-Path $master 'gu-write-probe-dir'))) { throw 'denied' } }
            'open existing file for writing' = { $s = [IO.File]::Open($probeFile, 'Open', 'ReadWrite'); $s.Dispose() }
            'rename file' = { [IO.File]::Move($probeFile, "$probeFile.renamed") }
            'delete file' = { [IO.File]::Delete($probeFile); if (!(Test-Path -LiteralPath $probeFile)) { throw 'deleted' } }
            'rename directory' = { [IO.Directory]::Move((Join-Path $master 'Mods/ProjectGu'), (Join-Path $master 'Mods/ProjectGu-renamed')) }
            'change attributes' = { [IO.File]::SetAttributes($probeFile, 'Hidden') }
            'read file' = { [void][IO.File]::ReadAllBytes($probeFile) }
        }
        $results = foreach ($entry in $attempts.GetEnumerator()) {
            $outcome = try { & $entry.Value; 'allowed' } catch { if ($_.Exception.Message -eq 'deleted') { 'allowed' } else { 'denied' } }
            [pscustomobject]@{ Operation = $entry.Key; Outcome = $outcome }
        }
        $integrity.ReadOnlyProbe = @($results)
        $results | Format-Table -AutoSize | Out-String | Set-Content -Encoding UTF8 (Join-Path $testRoot 'game-readonly-check.txt')
        $breaches = @($results | Where-Object { $_.Operation -ne 'read file' -and $_.Outcome -ne 'denied' })
        if ($breaches -or ($results | Where-Object { $_.Operation -eq 'read file' }).Outcome -ne 'allowed') {
            throw "Read-only enforcement of the shared game copy failed: $(($results | ForEach-Object { "$($_.Operation)=$($_.Outcome)" }) -join ', ')"
        }
        $integrity.ReadOnlyEnforced = $true
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# Scheduler. One process drives every worker, so ownership, timing, resource sampling and fail-fast live in one place.
$slotsFree = New-Object System.Collections.Generic.Queue[int]
for ($i = 0; $i -lt $Parallel; $i++) { $slotsFree.Enqueue($i) }
$active = New-Object System.Collections.Generic.List[object]

function Start-Launch($Unit, $Step) {
    $game = $games[[math]::Min($Unit.Slot, $games.Count - 1)]
    $arguments = '-gupriority0test -savedatafolder="' + $Unit.SaveData + '"'
    # The benchmark (the only untimed step) always runs alone and keeps its historical environment, file log included.
    if (!$CaptureFileLog -and $Step.Timing) { $arguments = "-NoFileLog $arguments" }
    if ($Step.Timing) { $arguments = "-gutimingstage=$($Step.Stage) $arguments" }
    if (!$Step.Renderer) { $arguments = '-headless ' + $arguments }
    if ($Step.Fresh) { $arguments = '-quicktest ' + $arguments }
    if ($Step.Extra) { $arguments = "$($Step.Extra) $arguments" }
    $launch = @{
        FilePath = Join-Path $game 'RimWorld.exe'
        WorkingDirectory = $game
        WindowStyle = if ($Step.Renderer) { 'Normal' } else { 'Hidden' }
        PassThru = $true
        ArgumentList = $arguments
        RedirectStandardOutput = Join-Path $Unit.SaveData "$($Step.Stage).stdout.txt"
        RedirectStandardError = Join-Path $Unit.SaveData "$($Step.Stage).stderr.txt"
    }
    $Step.LaunchUtc = [DateTime]::UtcNow
    $process = Start-Process @launch
    $null = $process.Handle  # Keeps the handle so the exit code and times stay readable after exit.
    $Step.ProcessId = $process.Id
    $Step.Slot = $Unit.Slot
    try { $Step.ProcessStartUtc = $process.StartTime.ToUniversalTime() } catch { }
    $Step.Status = 'RUNNING'
    $Unit.Process = $process
    $Unit.Timer = [Diagnostics.Stopwatch]::StartNew()
}

function Complete-Launch($Unit, $Step, [string]$Problem) {
    $process = $Unit.Process
    $Unit.Process = $null
    # Runner-observed exit, within one scheduler poll (100 ms). Process.ExitTime reads as 1601-01-01 for these processes.
    $Step.ExitUtc = [DateTime]::UtcNow
    $Step.ExitCode = try { $process.ExitCode } catch { $null }
    if ($CaptureFileLog) {
        $engineLog = Join-Path $userDataFolder 'Logs\Player.log'
        if (Test-Path -LiteralPath $engineLog) { Copy-Item -LiteralPath $engineLog -Destination (Join-Path $Unit.SaveData "$($Step.Stage).player.log") }
    }
    $Step.WallMs = [math]::Round(($Step.ExitUtc - $Step.LaunchUtc).TotalMilliseconds)
    $timingFile = Join-Path $Unit.SaveData "timing-$($Step.Stage).json"
    if (Test-Path -LiteralPath $timingFile) { $Step.Game = Get-Content -Raw -LiteralPath $timingFile | ConvertFrom-Json }
    $result = Join-Path $Unit.SaveData 'priority0-result.txt'
    if (!$Problem -and !((Test-Path -LiteralPath $result) -and (Select-String -LiteralPath $result -SimpleMatch "PASS $($Step.Stage)" -Quiet))) {
        $Problem = "Live stage $($Step.Stage) failed. See $($Unit.SaveData)"
    }
    if ($Problem) { Set-UnitFailed $Unit $Step $Problem; return }
    $Step.Status = 'PASS'
    $Unit.Index += 1
    Step-Unit $Unit
}

function Set-UnitFailed($Unit, $Step, [string]$Problem) {
    $Step.Status = 'FAIL'
    $Step.Detail = $Problem
    $Unit.Status = 'FAIL'
    $Unit.Failure = "$($Step.Stage): $Problem"
    $Unit.FinishedUtc = [DateTime]::UtcNow
    for ($i = $Unit.Index + 1; $i -lt $Unit.Steps.Count; $i++) { $Unit.Steps[$i].Status = 'SKIPPED'; $Unit.Steps[$i].Detail = 'depends on a failed stage' }
    $state.AnyFailure = $true
    if ($FailFast) { $state.StopScheduling = $true }
    Write-Host "FAIL $($Unit.Name) at $($Step.Stage): $Problem" -ForegroundColor Red
    $result = Join-Path $Unit.SaveData 'priority0-result.txt'
    if (Test-Path -LiteralPath $result) { Get-Content -LiteralPath $result | Select-Object -Last 5 }
}

function Step-Unit($Unit) {
    while ($Unit.Index -lt $Unit.Steps.Count) {
        $step = $Unit.Steps[$Unit.Index]
        if ($state.StopScheduling) {
            for ($i = $Unit.Index; $i -lt $Unit.Steps.Count; $i++) { $Unit.Steps[$i].Status = 'SKIPPED'; $Unit.Steps[$i].Detail = 'fail-fast' }
            $Unit.Status = 'SKIPPED'
            $Unit.Failure = 'fail-fast stopped this unit before it finished'
            $Unit.FinishedUtc = [DateTime]::UtcNow
            return
        }
        switch ($step.Kind) {
            'Launch' { Start-Launch $Unit $step; return }
            'Copy' { Copy-Item -LiteralPath $step.From -Destination $step.To -Force }
            'RestoreC3Input' {
                if ((Get-FileHash -LiteralPath $step.From).Hash -ne $step.Hash -or
                    (Get-FileHash -LiteralPath $step.To).Hash -ne $step.Hash) {
                    Set-UnitFailed $Unit $step 'The failed load changed a fixture input.'
                    return
                }
                Copy-Item -LiteralPath $step.From -Destination $step.To -Force
                ([IO.FileInfo]$step.To).IsReadOnly = $false
            }
            'CultivatorInvalidFixture' {
                $workingSave = Join-Path $Unit.SaveData 'Saves/autostart.rws'
                $verifiedCopy = Join-Path $Unit.SaveData 'cultivator-after-reload2.backup.rws'
                $sourceHash = (Get-FileHash -LiteralPath $workingSave).Hash
                Copy-Item -LiteralPath $workingSave -Destination $verifiedCopy
                if ((Get-FileHash -LiteralPath $verifiedCopy).Hash -ne $sourceHash) {
                    Set-UnitFailed $Unit $step 'Cultivator checkpoint backup differs from its source.'
                    return
                }
                $doc = New-Object Xml.XmlDocument
                $doc.PreserveWhitespace = $true
                $doc.Load($verifiedCopy)
                $marker = $doc.SelectSingleNode('//guCultivatorMigrationComplete')
                if ($null -eq $marker) { Set-UnitFailed $Unit $step 'Cultivator commit marker missing from checkpoint.'; return }
                $marker.InnerText = 'False'
                $doc.Save($workingSave)
                if ((Get-FileHash -LiteralPath $verifiedCopy).Hash -ne $sourceHash) {
                    Set-UnitFailed $Unit $step 'The immutable cultivator checkpoint changed.'
                    return
                }
                $step.Detail = "Invalid fixture created from unchanged backup SHA-256 $sourceHash"
            }
        }
        $step.Status = 'PASS'
        $Unit.Index += 1
    }
    $Unit.Status = 'PASS'
    $Unit.FinishedUtc = [DateTime]::UtcNow
    Write-Host $Unit.PassMessage -ForegroundColor Green
    Get-Content -LiteralPath (Join-Path $Unit.SaveData 'priority0-result.txt')
}

# Resource sampling. Memory and CPU of this run's game processes, system CPU and memory, and free disk.
$logicalCpus = [Environment]::ProcessorCount
Add-Type -AssemblyName Microsoft.VisualBasic
$computerInfo = New-Object Microsoft.VisualBasic.Devices.ComputerInfo
$systemCpu = try { $c = New-Object Diagnostics.PerformanceCounter('Processor', '% Processor Time', '_Total'); [void]$c.NextValue(); $c } catch { $null }
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($objRoot))
$cpuSeen = @{}
$lastSample = [Diagnostics.Stopwatch]::StartNew()
function Add-ResourceSample {
    $elapsedMs = $lastSample.Elapsed.TotalMilliseconds
    $lastSample.Restart()
    $workingSet = 0L; $private = 0L; $cpuDeltaMs = 0.0; $count = 0
    foreach ($unit in $active) {
        $process = $unit.Process
        if (!$process) { continue }
        try {
            $process.Refresh()
            if ($process.HasExited) { continue }
            $count += 1
            $ws = $process.WorkingSet64; $pm = $process.PrivateMemorySize64
            $workingSet += $ws; $private += $pm
            $cpu = $process.TotalProcessorTime.TotalMilliseconds
            $previous = if ($cpuSeen.ContainsKey($process.Id)) { $cpuSeen[$process.Id] } else { 0.0 }
            $cpuDeltaMs += [math]::Max(0, $cpu - $previous)
            $cpuSeen[$process.Id] = $cpu
            $step = $unit.Steps[$unit.Index]
            if ($ws -gt $step.PeakWorkingSet) { $step.PeakWorkingSet = $ws }
            if ($pm -gt $step.PeakPrivate) { $step.PeakPrivate = $pm }
        } catch { }
    }
    $samples.Add([pscustomobject]@{
        Utc = [DateTime]::UtcNow.ToString('o'); GameProcesses = $count
        GameWorkingSetMB = [math]::Round($workingSet / 1MB); GamePrivateMB = [math]::Round($private / 1MB)
        GameCpuPercentOfMachine = if ($elapsedMs -gt 0) { [math]::Round(100 * $cpuDeltaMs / ($elapsedMs * $logicalCpus), 1) } else { 0 }
        SystemCpuPercent = if ($systemCpu) { [math]::Round($systemCpu.NextValue(), 1) } else { $null }
        AvailableMemoryMB = [math]::Round($computerInfo.AvailablePhysicalMemory / 1MB)
        DiskFreeGB = [math]::Round($drive.AvailableFreeSpace / 1GB, 2)
    })
}

$originalLayerSetting = $env:VK_LOADER_LAYERS_DISABLE
$queue = New-Object System.Collections.Generic.Queue[object]
foreach ($unit in $units) { $queue.Enqueue($unit) }
try {
    $env:VK_LOADER_LAYERS_DISABLE = '~implicit~'
    Measure-Phase 'scheduler' {
        Add-ResourceSample
        while ($queue.Count -gt 0 -or $active.Count -gt 0) {
            while (!$state.StopScheduling -and $queue.Count -gt 0 -and $slotsFree.Count -gt 0) {
                $unit = $queue.Dequeue()
                $unit.Slot = $slotsFree.Dequeue()
                $unit.Status = 'RUNNING'
                $unit.StartedUtc = [DateTime]::UtcNow
                $active.Add($unit)
                Step-Unit $unit
            }
            if ($state.StopScheduling) {
                while ($queue.Count -gt 0) {
                    $unit = $queue.Dequeue()
                    $unit.Status = 'SKIPPED'; $unit.Failure = 'fail-fast: not started'
                    foreach ($step in $unit.Steps) { $step.Status = 'SKIPPED'; $step.Detail = 'fail-fast' }
                }
            }
            foreach ($unit in $active.ToArray()) {
                if ($unit.Process) {
                    $step = $unit.Steps[$unit.Index]
                    if ($unit.Process.HasExited) { Complete-Launch $unit $step '' }
                    elseif ($unit.Timer.Elapsed.TotalSeconds -gt $StageTimeoutSeconds) {
                        try { $unit.Process.Kill(); [void]$unit.Process.WaitForExit(15000) } catch { }
                        Complete-Launch $unit $step "RimWorld timed out after $StageTimeoutSeconds s in stage $($step.Stage). See $($unit.SaveData)"
                    }
                }
                if ($unit.Status -ne 'RUNNING') { [void]$active.Remove($unit); $slotsFree.Enqueue($unit.Slot) }
            }
            if ($lastSample.Elapsed.TotalMilliseconds -ge $SampleIntervalMs) { Add-ResourceSample }
            Start-Sleep -Milliseconds 100
        }
    }
}
finally {
    $env:VK_LOADER_LAYERS_DISABLE = $originalLayerSetting
}

# ---------------------------------------------------------------------------------------------------------------------
# Post-run integrity: the game copies are unchanged and every process wrote only into its own unit's folder.
Measure-Phase 'hashAfter' {
    for ($i = 0; $i -lt $games.Count; $i++) {
        $changes = Compare-TreeManifest $script:manifests[$i] (Get-TreeManifest $games[$i])
        foreach ($change in $changes) { $integrity.GameCopyChanges += "copy ${i}: $change" }
    }
}
foreach ($unit in $units) {
    foreach ($step in $unit.Steps | Where-Object { $_.Kind -eq 'Launch' -and $_.LaunchUtc }) {
        $log = Join-Path $unit.SaveData "$($step.Stage).stdout.txt"
        $head = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -TotalCount 40) -join "`n" } else { '' }
        if ($head -notlike "*-savedatafolder=$($unit.SaveData)*" -or $head -notlike "*Save data folder overridden to $($unit.SaveData)*") {
            $integrity.IsolationProblems += "$($unit.Name)/$($step.Stage): log does not show its own save data folder"
        }
        if ($step.Timing -and $step.Game -and ($step.Game.Stage -ne $step.Stage -or $step.Game.ProcessId -ne $step.ProcessId)) {
            $integrity.IsolationProblems += "$($unit.Name)/$($step.Stage): timing file belongs to stage $($step.Game.Stage), process $($step.Game.ProcessId)"
        }
    }
}
$userDataAfter = Get-NewestWrite $userDataFolder
$integrity.UserDataFolderChanged = ($userDataAfter -ne $userDataBefore)
if ($integrity.GameCopyChanges -and $GameCopyMode -eq 'Shared') { $campaignFailed = $true }
if ($integrity.IsolationProblems) { $campaignFailed = $true }
}
finally {
    # Runs after success, failure and Ctrl+C. Only this run's own child processes and game copies are touched.
    foreach ($unit in $units) {
        try {
            if ($unit.Process -and !$unit.Process.HasExited) {
                $unit.Status = 'ABORTED'
                $state.AnyFailure = $true
                $unit.Process.Kill()
                [void]$unit.Process.WaitForExit(15000)
            }
        }
        catch { Write-Warning "Could not stop the game for $($unit.Name): $($_.Exception.Message)" }
    }
    try {
        foreach ($entry in $sourceSaves.Values) {
            try {
                $entry.Handle.Position = 0
                $entry.After = (Get-FileHash -InputStream $entry.Handle).Hash
                $entry.Unchanged = $entry.Before -eq $entry.After
            }
            catch { $entry.Error = $_.Exception.Message }
            finally { $entry.Handle.Dispose() }
            if (!$entry.Unchanged) {
                $campaignFailed = $true
                Write-Warning "Source save could not be verified after campaign: $($entry.Path) $($entry.Error)"
            }
        }
        if ($sourceSaves.Count) { Write-SourceSaveEvidence }
    }
    finally {
        try {
            $cleanupTimer = [Diagnostics.Stopwatch]::StartNew()
            foreach ($game in $games) { Remove-GameCopy $game }
            $phases['cleanup'] = [math]::Round($cleanupTimer.Elapsed.TotalMilliseconds)
        }
        finally {
            [void][ProjectGuRunner.Power]::SetThreadExecutionState([uint32]'0x80000000')
            $runLock.Dispose()
        }
    }
}

# A resume from sleep or hibernation during the run invalidates its timings and any timeout it produced.
try {
    $integrity.SystemSleep = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'; Id = 42, 107; StartTime = $campaignStartLocal } -ErrorAction Stop |
        ForEach-Object { '{0:o} {1}' -f $_.TimeCreated, $(if ($_.Id -eq 42) { 'entering sleep' } else { 'resumed' }) })
    $integrity.SystemSleepCheck = 'complete'
}
catch {
    $integrity.SystemSleep = @()
    $integrity.SystemSleepCheck = "unavailable: $($_.Exception.Message)"
    Write-Warning "System sleep events could not be inspected; campaign timing validity is unverified: $($_.Exception.Message)"
}
if ($integrity.SystemSleep) { $campaignFailed = $true; Write-Warning "The system slept during this run. Its timings and timeouts are not valid: $($integrity.SystemSleep -join '; ')" }

# ---------------------------------------------------------------------------------------------------------------------
# Reports: timing.csv (one row per process), resources.csv (samples), campaign.json and campaign-summary.txt.
function Get-Mark($Marks, [string]$Name) {
    $mark = @($Marks | Where-Object { $_.Name -eq $Name }) | Select-Object -First 1
    if ($mark) { [double]$mark.SinceProcessStartMs } else { $null }
}
function Get-Span($Marks, [string]$From, [string]$To) {
    $a = Get-Mark $Marks $From; $b = Get-Mark $Marks $To
    if ($null -ne $a -and $null -ne $b) { [math]::Round($b - $a) } else { $null }
}
$rows = foreach ($unit in $units) {
    foreach ($step in $unit.Steps | Where-Object { $_.Kind -eq 'Launch' }) {
        $marks = if ($step.Game) { @($step.Game.Marks) } else { @() }
        $saveMs = 0.0; $saveCount = 0; $open = $null
        foreach ($mark in $marks) {
            if ($mark.Name -eq 'save.start') { $open = [double]$mark.SinceProcessStartMs }
            elseif ($mark.Name -eq 'save.end' -and $null -ne $open) { $saveMs += [double]$mark.SinceProcessStartMs - $open; $saveCount += 1; $open = $null }
        }
        $testMs = Get-Span $marks 'test.start' 'test.end'
        $exitRequested = Get-Mark $marks 'exitRequested'
        [pscustomobject]@{
            Unit = $unit.Name; Stage = $step.Stage; Slot = $step.Slot; Result = $step.Status; ProcessId = $step.ProcessId
            RunnerLaunchUtc = if ($step.LaunchUtc) { $step.LaunchUtc.ToString('o') } else { '' }
            RunnerWallMs = $step.WallMs
            LaunchToProcessStartMs = if ($step.ProcessStartUtc) { [math]::Round(($step.ProcessStartUtc - $step.LaunchUtc).TotalMilliseconds) } else { $null }
            GameStartToModInitMs = if ($null -ne (Get-Mark $marks 'modInit')) { [math]::Round((Get-Mark $marks 'modInit')) } else { $null }
            ModInitToStartupCompleteMs = Get-Span $marks 'modInit' 'startupComplete'
            GameStartToStartupCompleteMs = if ($null -ne (Get-Mark $marks 'startupComplete')) { [math]::Round((Get-Mark $marks 'startupComplete')) } else { $null }
            WorldGenMs = Get-Span $marks 'worldGen.start' 'worldGen.end'
            NewGameMapGenMs = Get-Span $marks 'newGame.start' 'newGame.end'
            LoadMs = Get-Span $marks 'load.start' 'load.end'
            ReadyToTestMs = Get-Span $marks 'playing' 'test.start'
            TestMs = $testMs
            TestSaves = $saveCount
            TestSaveMs = [math]::Round($saveMs)
            TestExcludingSavesMs = if ($null -ne $testMs) { [math]::Round($testMs - $saveMs) } else { $null }
            ExitRequestedToProcessExitMs = if ($null -ne $exitRequested -and $step.ProcessStartUtc -and $step.ExitUtc) {
                [math]::Round(($step.ExitUtc - $step.ProcessStartUtc).TotalMilliseconds - $exitRequested) } else { $null }
            PeakWorkingSetMB = [math]::Round($step.PeakWorkingSet / 1MB)
            PeakPrivateMB = [math]::Round($step.PeakPrivate / 1MB)
            Detail = $step.Detail
        }
    }
}
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $testRoot 'timing.csv')
$samples | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $testRoot 'resources.csv')

$launched = @($rows | Where-Object { $_.RunnerWallMs })
$peak = [ordered]@{
    GameProcesses = ($samples | Measure-Object GameProcesses -Maximum).Maximum
    GameWorkingSetMB = ($samples | Measure-Object GameWorkingSetMB -Maximum).Maximum
    GamePrivateMB = ($samples | Measure-Object GamePrivateMB -Maximum).Maximum
    GameCpuPercentOfMachine = ($samples | Measure-Object GameCpuPercentOfMachine -Maximum).Maximum
    SystemCpuPercent = ($samples | Where-Object { $null -ne $_.SystemCpuPercent } | Measure-Object SystemCpuPercent -Maximum).Maximum
    MinAvailableMemoryMB = ($samples | Measure-Object AvailableMemoryMB -Minimum).Minimum
    MinDiskFreeGB = ($samples | Measure-Object DiskFreeGB -Minimum).Minimum
}
$evidenceBytes = (Get-ChildItem -LiteralPath $testRoot -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
$counts = [ordered]@{}
foreach ($status in 'PASS', 'FAIL', 'SKIPPED', 'ABORTED') { $counts[$status] = @($units | Where-Object { $_.Status -eq $status }).Count }
if ($counts.FAIL -or $counts.SKIPPED -or $counts.ABORTED -or ($units | Where-Object { $_.Status -notin 'PASS', 'FAIL', 'SKIPPED', 'ABORTED' })) { $campaignFailed = $true }
$campaign = [ordered]@{
    RunId = $runId; Profiles = $profiles; Parallel = $Parallel; FailFast = $FailFast.IsPresent; GameCopyMode = $GameCopyMode
    Expansions = $Expansions; LogicalProcessors = $logicalCpus
    TotalPhysicalMemoryMB = [math]::Round($computerInfo.TotalPhysicalMemory / 1MB)
    CampaignWallMs = [math]::Round($campaignTimer.Elapsed.TotalMilliseconds)
    PhasesMs = $phases; Units = $counts
    Processes = $launched.Count
    SumProcessWallMs = ($launched | Measure-Object RunnerWallMs -Sum).Sum
    GameCopyBytes = $gameBytes; EvidenceBytes = $evidenceBytes
    Peak = $peak; Integrity = $integrity
    UnitResults = @($units | ForEach-Object { [ordered]@{ Name = $_.Name; Status = $_.Status; Failure = $_.Failure; SaveData = $_.SaveData } })
}
$campaign | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $testRoot 'campaign.json')

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("Campaign $runId  parallel=$Parallel  mode=$GameCopyMode  failFast=$($FailFast.IsPresent)")
$summary.Add(('Wall {0:N1} s; scheduler {1:N1} s; {2} processes, summed process wall {3:N1} s' -f ($campaign.CampaignWallMs / 1000), ($phases['scheduler'] / 1000), $campaign.Processes, ($campaign.SumProcessWallMs / 1000)))
$summary.Add('Phases (ms): ' + (($phases.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '))
$summary.Add(('Peak: {0} game processes, {1} MB working set, {2} MB private, game CPU {3}% of machine, system CPU {4}%, min available memory {5} MB, min disk free {6} GB' -f
    $peak.GameProcesses, $peak.GameWorkingSetMB, $peak.GamePrivateMB, $peak.GameCpuPercentOfMachine, $peak.SystemCpuPercent, $peak.MinAvailableMemoryMB, $peak.MinDiskFreeGB))
$summary.Add(('Read-only enforced: {0}; game copy changes: {1}; isolation problems: {2}; user data folder changed: {3}' -f
    $integrity.ReadOnlyEnforced, @($integrity.GameCopyChanges).Count, @($integrity.IsolationProblems).Count, $integrity.UserDataFolderChanged))
foreach ($change in @($integrity.GameCopyChanges) + @($integrity.IsolationProblems)) { $summary.Add("  $change") }
if ($integrity.SystemSleep) { $summary.Add("INVALID RUN: the system slept: $($integrity.SystemSleep -join '; ')") }
$summary.Add('')
foreach ($unit in $units) { $summary.Add(('{0,-8} {1,-28} {2}' -f $unit.Status, $unit.Name, $unit.Failure)) }
$summary.Add(('Units: {0}' -f (($counts.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')))
$summary.Add("Evidence in $testRoot")
$summary | Set-Content -Encoding UTF8 (Join-Path $testRoot 'campaign-summary.txt')
Write-Host ''
$summary | ForEach-Object { Write-Host $_ }
if ($campaignFailed) { exit 1 }
