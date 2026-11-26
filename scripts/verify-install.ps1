param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$HarmonyPath) {
    $HarmonyPath = Join-Path $RimWorldPath '../../workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll'
}
$HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path
$evidence = New-Item -ItemType Directory -Force (Join-Path $root ('tests/obj/Install-' + [Guid]::NewGuid().ToString('N')))
$clean = Join-Path $evidence.FullName 'Clean'
$upgrade = Join-Path $evidence.FullName 'Upgrade'
$targetName = 'Mods/ProjectGU-LivingCultivation'
$cleanMod = Join-Path $clean $targetName
$upgradedMod = Join-Path $upgrade $targetName
$stale = 'Defs/ThinkTreeDefs/GU_GuTending.xml'
$installer = Join-Path $PSScriptRoot 'install.ps1'

function Assert-InstalledPayload([string]$Mod) {
    $manifest = Get-Content -LiteralPath (Join-Path $Mod '.projectgu-install.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $Mod $entry.path)).Hash -ne $entry.sha256) {
            throw "Installed payload hash mismatch: $($entry.path)"
        }
    }
    $obsolete = Get-ChildItem -LiteralPath $Mod -Recurse -Filter '*.xml' |
        Select-String -SimpleMatch 'ProjectGu.Sustenance.JobGiver_GuTendOwnGu'
    if ($obsolete) { throw "An obsolete XML reference is still installed: $($obsolete.Path)" }
    if (!(Test-Path -LiteralPath (Join-Path $Mod 'Defs/WorkTypeDefs/GU_UndergroundHauling.xml') -PathType Leaf)) {
        throw 'Missing dedicated hauling Work type.'
    }
    return $manifest
}

foreach ($game in @($clean, $upgrade)) {
    New-Item -ItemType Directory -Path $game | Out-Null
    foreach ($assembly in @('Assembly-CSharp.dll', 'UnityStubs.dll', 'Waylith.dll')) {
        Copy-Item -LiteralPath (Join-Path $RimWorldPath $assembly) -Destination $game
    }
}
$old = Join-Path $evidence.FullName 'Alpha5A'
$archive = Join-Path $evidence.FullName 'Alpha5A.zip'
git -C $root archive --format=zip --output=$archive d82c723769ab19dd9078ba3dfefd0a20ee8d10ce
if ($LASTEXITCODE -ne 0) { throw 'Could not archive the pinned Alpha 5A tree.' }
Expand-Archive -LiteralPath $archive -DestinationPath $old
& (Join-Path $old 'scripts/install.ps1') -RimWorldPath $upgrade -HarmonyPath $HarmonyPath
if (!(Select-String -LiteralPath (Join-Path $upgradedMod $stale) -SimpleMatch 'ProjectGu.Sustenance.JobGiver_GuTendOwnGu' -Quiet)) {
    throw 'The historical installation did not contain the Alpha 5A patch.'
}

$sentinel = Join-Path $upgradedMod 'Defs/LocalUserNotes.txt'
[IO.File]::WriteAllText($sentinel, 'Local notes to preserve.')
$sentinelHash = (Get-FileHash -LiteralPath $sentinel).Hash
& $installer -RimWorldPath $clean -HarmonyPath $HarmonyPath
& $installer -RimWorldPath $upgrade -HarmonyPath $HarmonyPath
$cleanManifest = Assert-InstalledPayload $cleanMod
$upgradeManifest = Assert-InstalledPayload $upgradedMod
if (Compare-Object @($cleanManifest.files.path) @($upgradeManifest.files.path)) { throw 'Clean and upgrade payloads differ.' }
if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $sentinelHash) { throw 'Local file changed during upgrade.' }
Write-Host 'PASS clean install and manifestless Alpha 5A upgrade.'

# Simulate two retired files from a manifest-aware build. Preserve the one edited after deployment.
$managed = 'Defs/RetiredProjectGu.txt'
$edited = 'Defs/EditedProjectGu.txt'
$manifestPath = Join-Path $upgradedMod '.projectgu-install.json'
foreach ($relative in @($managed, $edited)) {
    $file = Join-Path $upgradedMod $relative
    [IO.File]::WriteAllText($file, 'Original Project GU content.')
    $upgradeManifest.files += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file).Hash }
}
$editedPath = Join-Path $upgradedMod $edited
[IO.File]::WriteAllText($editedPath, 'Owner edits to preserve.')
$editedHash = (Get-FileHash -LiteralPath $editedPath).Hash
$upgradeManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
& $installer -RimWorldPath $upgrade -HarmonyPath $HarmonyPath
if ((Test-Path -LiteralPath (Join-Path $upgradedMod $managed)) -or (Get-FileHash -LiteralPath $editedPath).Hash -ne $editedHash) {
    throw 'Manifest-aware retirement did not respect recorded file ownership.'
}
$first = Assert-InstalledPayload $upgradedMod
& $installer -RimWorldPath $upgrade -HarmonyPath $HarmonyPath
$second = Assert-InstalledPayload $upgradedMod
if ((Compare-Object $first.files $second.files -Property path, sha256) -or
    (Get-FileHash -LiteralPath $sentinel).Hash -ne $sentinelHash -or
    (Get-FileHash -LiteralPath $editedPath).Hash -ne $editedHash) { throw 'Repeated installation changed the installed payload or local files.' }
Write-Host 'PASS manifest-aware retirement and repeated install.'
Write-Host "Clean Project GU deployment: $cleanMod"
Write-Host "Upgraded Project GU deployment: $upgradedMod"
