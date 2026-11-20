param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld', [string]$HarmonyPath = '')
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
Assert-Gu (($references -join ',') -eq '0Harmony,Assembly-CSharp,UnityStubs,Waylith') 'Unexpected assembly references'
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
    'Textures\ProjectGU\MoonOrchid\ColonyA.png',
    'Textures\ProjectGU\MoonOrchid\ColonyB.png',
    'Textures\ProjectGU\SpiritGrass\PatchA.png',
    'Textures\ProjectGU\SpiritGrass\PatchB.png',
    'Textures\ProjectGU\SpiritGrass\PatchC.png',
    'Textures\ProjectGU\SpiritGrass\PatchD.png',
    'Textures\ProjectGU\SpringPitEntrance.png',
    'Textures\ProjectGU\SpringRopeExit.png',
    'Textures\ProjectGU\StonePassage.png',
    'docs\TEST-PLAN-0.2.0.md',
    'docs\TEST-PLAN-0.2.1.md'
)
foreach ($f in $requiredFiles) { Assert-Gu (Test-Path -LiteralPath (Join-Path $root $f) -PathType Leaf) "Missing file: $f" }
Assert-Gu (-not (Test-Path -LiteralPath (Join-Path $root 'Assemblies\ProjectGu.dll'))) 'Unexpected root assembly'

# Parse every gameplay definition and patch.
$xmlFolders = @((Join-Path $root 'Defs'), (Join-Path $root 'Patches'))
Get-ChildItem -Path $xmlFolders -Filter '*.xml' -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($root.Length + 1)
    $null = Read-GuXml $relative
}
$orchid = Read-GuXml 'Defs\ThingDefs\GU_MoonOrchidHabitat.xml'
$grass = Read-GuXml 'Defs\ThingDefs\GU_SpiritFlora.xml'
$spring = Read-GuXml 'Defs\ThingDefs\GU_SpiritSpring.xml'
$cavern = Read-GuXml 'Defs\MapGeneration\GU_SpiritSpringCavern.xml'
$probe = Read-GuXml 'Defs\MapGeneration\GU_PocketProbe.xml'
$passages = Read-GuXml 'Defs\ThingDefs\GU_SpringPassages.xml'
Assert-Gu ($null -ne $orchid.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]')) 'Moon orchid ecology component missing'
Assert-Gu ($null -ne $grass.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritFlora"]')) 'Spirit grass ecology component missing'
Assert-Gu ($null -ne $spring.SelectSingleNode('//li[@Class="ProjectGu.Springs.CompProperties_GuSpiritSpring"]')) 'Spring ecology component missing'
Assert-Gu ($null -ne $spring.SelectSingleNode('//ThingDef[defName="GU_PrimevalStone" and stackLimit="200"]')) 'Global primeval-stone stack limit'
Assert-Gu ($null -ne $cavern.SelectSingleNode('//ProjectGu.Underground.GuPocketSiteDef[mapSize="39" and endpoint="GU_SpringPitEntrance" and returnEndpoint="GU_SpringRopeExit"]')) 'Spring cavern dimensions or passage Defs'
Assert-Gu ($null -ne $cavern.SelectSingleNode('//ProjectGu.Underground.GuPocketSiteDef[independentOwner="true"]')) 'New spirit spring owner is not configured'
Assert-Gu ($null -ne $cavern.SelectSingleNode('//genStep[exit="GU_SpringRopeExit"]')) 'Spring generator exit mismatch'
Assert-Gu ($null -ne $probe.SelectSingleNode('//ProjectGu.Underground.GuPocketSiteDef[mapSize="31" and endpoint="GU_PocketProbeEndpoint"]')) 'Generic probe dimensions or endpoint changed'
Assert-Gu ($null -ne $passages.SelectSingleNode('//ThingDef[defName="GU_SpringPitEntrance" and graphicData/texPath="ProjectGU/SpringPitEntrance"]')) 'Pit sprite reference missing'
Assert-Gu ($null -ne $passages.SelectSingleNode('//ThingDef[defName="GU_SpringRopeExit" and graphicData/texPath="ProjectGU/SpringRopeExit"]')) 'Rope sprite reference missing'
Assert-Gu ($null -ne $grass.SelectSingleNode('//graphicData[texPath="ProjectGU/SpiritGrass" and graphicClass="Graphic_Random"]')) 'Grass variants missing'
Assert-Gu ($null -ne $orchid.SelectSingleNode('//graphicData[texPath="ProjectGU/MoonOrchid" and graphicClass="Graphic_Random"]')) 'Orchid variants missing'
$botanical = Read-GuXml 'Defs\ThingDefs\GU_BotanicalPlants.xml'
$qiSoil = Read-GuXml 'Defs\TerrainDefs\GU_PrimevalQiRichSoil.xml'
Assert-Gu ($null -ne $botanical.SelectSingleNode('//ThingDef[@ParentName="PlantBaseNonEdible" and defName="GU_SpiritGrass" and plant/maxMeshCount="9"]')) 'Native Spirit Grass mesh configuration'
Assert-Gu ($null -ne $botanical.SelectSingleNode('//ThingDef[defName="GU_MoonOrchid" and plant/sowTags/li="Ground" and plant/wildTerrainTags/li="GU_PrimevalQiRich"]')) 'Native Moon Orchid sowing and soil requirement'
Assert-Gu ($null -ne $qiSoil.SelectSingleNode('//TerrainDef[defName="GU_PrimevalQiRichSoil" and tags/li="GU_PrimevalQiRich" and affordances/li="GrowSoil"]')) 'Primeval qi-rich soil tag and growing affordance'
Assert-Gu ($null -ne $cavern.SelectSingleNode('//genStep[richSoil="GU_PrimevalQiRichSoil"]')) 'New caverns need qi-rich soil'
Assert-Gu (Test-Path -LiteralPath (Join-Path $root 'Textures\ProjectGU\Terrain\PrimevalQiRichSoil.png')) 'Primeval qi-rich soil texture missing'
$source = Get-Content -LiteralPath (Join-Path $root 'Source\ProjectGu\Springs\CompGuSpiritSpring.cs') -Raw
$saveKeys = @('guNextProductionTick','guLifetimeProduced','guHopeGuAvailable','guNextHopeRecoveryTick','guHabitatPatches','guReserve024','guMatureHope024','guJuvenileHope024','guNextEcology024','guNextFloraGrowthTick','guUsesBotanicalFlora')
foreach ($key in $saveKeys) { Assert-Gu ($source.Contains($key)) "Missing save field: $key" }
Write-Host 'PASS: XML, file structure, 1.7 metadata, ecology definitions and save-key presence.' -ForegroundColor Green

# Framework slice 1: species metadata checks (PowerShell only, no Python dependency).
& (Join-Path $PSScriptRoot 'verify-gu-framework.ps1')

# Slice 2 static safety net. These are structural assertions, not proof of
# Scribe correctness, in-game migration or gameplay behavior.
$populationRecord = Join-Path $root 'Source\ProjectGu\Domain\GuPopulationRecord.cs'
$populationLedger = Join-Path $root 'Source\ProjectGu\Springs\GuPopulationLedger.cs'
Assert-Gu (Test-Path -LiteralPath $populationRecord -PathType Leaf) 'Missing generic GuPopulationRecord'
Assert-Gu (Test-Path -LiteralPath $populationLedger -PathType Leaf) 'Missing generic GuPopulationLedger'
$popSource = Get-Content -LiteralPath $populationRecord -Raw
$ledgerSource = Get-Content -LiteralPath $populationLedger -Raw
$springSource = Get-Content -LiteralPath (Join-Path $root 'Source\ProjectGu\Springs\CompGuSpiritSpring.cs') -Raw
foreach ($key in @('guPopulationSchema025','guHabitatId025','guPopulationRecords025',
                   'guMatureHope024','guJuvenileHope024','guHopeGuAvailable','guReserve024',
                   'guNextEcology024','guNextProductionTick','guLifetimeProduced')) {
    Assert-Gu ($springSource.Contains($key)) "Population migration save key missing: $key"
}
Assert-Gu ($springSource.Contains('LookMode.Deep')) 'No deep Scribe serialization for cohorts'
Assert-Gu ($springSource.Contains('populationSchema == 1 && populations == null')) 'Missing guard for a versioned save without cohorts'
Assert-Gu ($springSource.Contains('if (!populationsReady) return;')) 'Legacy shadows are not guarded against invalid cohort data'
Assert-Gu ($springSource.Contains('GuPopulationLedger.TryConsumeMature')) 'Consumption does not use generic population ledger'
Assert-Gu ($popSource.Contains('IExposable')) 'Species population record is not saveable'
Assert-Gu ($ledgerSource.Contains('TryAddCohort')) 'Species-neutral cohort insertion missing'
Write-Host 'PASS: Slice 2 cohort schema, legacy keys and migration guards structurally present.' -ForegroundColor Green

& (Join-Path $PSScriptRoot 'build.ps1') -RimWorldPath $RimWorldPath -HarmonyPath $HarmonyPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$verification = Join-Path $root 'tests\ProjectGu.Verification\ProjectGu.Verification.csproj'
$verificationReferences = @("-p:RimWorldPath=$RimWorldPath")
if ($HarmonyPath) { $verificationReferences += "-p:HarmonyPath=$HarmonyPath" }
dotnet run --project $verification -c Release @verificationReferences -- $root $RimWorldPath
if ($LASTEXITCODE -ne 0) { throw 'Isolated cavern and spring verification failed.' }
Write-Host 'Static checks, game-linked compilation, and isolated managed tests passed. Live acceptance is separate.' -ForegroundColor Green
