param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Assert-Gu([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Structural validation failed: $Message" }
}
function Read-GuXml([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    Assert-Gu (Test-Path -LiteralPath $path -PathType Leaf) "Missing file: $RelativePath"
    try { return [xml](Get-Content -LiteralPath $path -Raw) }
    catch { throw "Structural validation failed: Invalid XML: $RelativePath. $_" }
}

$about = Read-GuXml 'About\About.xml'
$project = Read-GuXml 'Source\ProjectGu\ProjectGu.csproj'
$hediff = Read-GuXml 'Defs\HediffDefs\GU_Aperture.xml'
Assert-Gu ($about.ModMetaData.packageId -eq 'projectgu.livingcultivation') 'Incorrect package ID'
Assert-Gu ((@($about.ModMetaData.supportedVersions.li) -join ',') -eq '1.7') 'Supported version must be 1.7'
Assert-Gu ($project.Project.PropertyGroup.TargetFramework -eq 'net10.0') 'Incorrect target framework'
Assert-Gu ($project.Project.PropertyGroup.OutputPath -eq '../../1.7/Assemblies/') 'Unexpected assembly output path'
$references = @($project.Project.ItemGroup.Reference | ForEach-Object { $_.Include } | Sort-Object)
Assert-Gu (($references -join ',') -eq 'Assembly-CSharp,UnityStubs,Waylith') 'Unexpected assembly references'
Assert-Gu ($null -ne $hediff.SelectSingleNode('//HediffDef[defName="GU_Aperture" and hediffClass="ProjectGu.Hediff_Aperture"]')) 'Aperture definition mismatch'
$requiredFiles = @(
    'Source\ProjectGu\Hediff_Aperture.cs',
    'Source\ProjectGu\GuWorldComponent.cs',
    'Source\ProjectGu\GuAwakeningService.cs',
    'Source\ProjectGu\Domain\GuPawnRecord.cs',
    'Source\ProjectGu\Springs\CompGuSpiritSpring.cs',
    'Source\ProjectGu\Springs\GuEcologyMapComponent.cs',
    'Source\ProjectGu\Springs\CompGuSpiritFlora.cs',
    'Source\ProjectGu\Springs\CompProperties_GuSpiritFlora.cs',
    'Textures\ProjectGU\MoonOrchidHabitat.png',
    'docs\TEST-PLAN-0.2.0.md',
    'docs\TEST-PLAN-0.2.1.md'
)
foreach ($f in $requiredFiles) { Assert-Gu (Test-Path -LiteralPath (Join-Path $root $f) -PathType Leaf) "Missing file: $f" }
Assert-Gu (-not (Test-Path -LiteralPath (Join-Path $root 'Assemblies\ProjectGu.dll'))) 'Unexpected root assembly'

# Parse every gameplay XML definition, not just the three ecology defs.
Get-ChildItem -Path (Join-Path $root 'Defs') -Filter '*.xml' -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($root.Length + 1)
    $null = Read-GuXml $relative
}
$orchid = Read-GuXml 'Defs\ThingDefs\GU_MoonOrchidHabitat.xml'
$grass = Read-GuXml 'Defs\ThingDefs\GU_SpiritFlora.xml'
$spring = Read-GuXml 'Defs\ThingDefs\GU_SpiritSpring.xml'
Assert-Gu ($null -ne $orchid.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]')) 'Moon orchid ecology component missing'
Assert-Gu ($null -ne $grass.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]')) 'Spirit grass ecology component missing'
Assert-Gu ($null -ne $spring.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritSpring"]')) 'Spring ecology component missing'
$source = Get-Content -LiteralPath (Join-Path $root 'Source\ProjectGu\Springs\CompGuSpiritSpring.cs') -Raw
$saveKeys = @('guNextProductionTick','guLifetimeProduced','guHopeGuAvailable','guNextHopeRecoveryTick','guHabitatPatches','guReserve024','guMatureHope024','guJuvenileHope024','guNextEcology024')
foreach ($key in $saveKeys) { Assert-Gu ($source.Contains($key)) "Missing save field: $key" }
Write-Host 'PASS: XML, file structure, 1.7 metadata, ecology definitions and save-key presence.' -ForegroundColor Green

# Framework slice 1: species metadata checks (PowerShell only, no Python dependency).
& (Join-Path $PSScriptRoot 'verify-gu-framework.ps1')

& (Join-Path $PSScriptRoot 'build.ps1') -RimWorldPath $RimWorldPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Host 'Static checks + compilation passed. Proceed to the in-game test plan.' -ForegroundColor Green
