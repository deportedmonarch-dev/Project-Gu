$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$defsPath = Join-Path $root 'Defs\GuSpeciesDefs\GU_GuSpecies.xml'
$sourcePath = Join-Path $root 'Source\ProjectGu\Domain\GuSpeciesDef.cs'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw 'Missing GuSpeciesDef source.' }
if (-not (Test-Path -LiteralPath $defsPath -PathType Leaf)) { throw 'Missing Gu species XML.' }
[xml]$xml = Get-Content -LiteralPath $defsPath -Raw
$defs = @($xml.Defs.SelectNodes('ProjectGu.Domain.GuSpeciesDef'))
if (@($xml.Defs.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.Name -ne 'ProjectGu.Domain.GuSpeciesDef' }).Count -gt 0) { throw 'All Gu species declarations must use the fully qualified C# Def type.' }
if ($defs.Count -lt 2) { throw 'Need two distinct species definitions for framework tests.' }
$seen = @{}
foreach ($def in $defs) {
    $name = [string]$def.defName
    if ([string]::IsNullOrWhiteSpace($name)) { throw 'Gu species has no defName.' }
    if ($seen.ContainsKey($name)) { throw "Duplicate Gu species: $name" }
    $seen[$name] = $true
    $min = [int]$def.minRank; $max = [int]$def.maxRank
    if ($min -lt 1 -or $max -gt 9 -or $max -lt $min) { throw "Invalid Gu rank bounds: $name" }
    foreach ($field in @('pathTags','habitatTags','capabilityTags')) {
        $node = $def.SelectSingleNode($field)
        if ($null -eq $node) { continue }
        $tags = @($node.SelectNodes('li') | ForEach-Object { $_.InnerText.Trim() })
        if (@($tags | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) { throw "Empty $field on $name" }
        if (@($tags | Sort-Object -Unique).Count -ne $tags.Count) { throw "Duplicate $field on $name" }
    }
    # A wild species may intentionally have no fixed habitat: roaming Gu are valid.
    # Hope Gu's spring affinity is verified explicitly below.
}
$hope = $defs | Where-Object { $_.defName -eq 'GU_HopeGu' } | Select-Object -First 1
$moon = $defs | Where-Object { $_.defName -eq 'GU_MoonlightGu' } | Select-Object -First 1
if ($null -eq $hope -or $null -eq $moon) { throw 'Missing test species.' }
if ([string]$hope.wildPopulationEnabled -ne 'true' -or @($hope.habitatTags.li) -notcontains 'spirit_spring') {
    throw 'Hope Gu must have a spirit-spring habitat affinity.'
}
# Alpha 2: Moonlight Gu is an ordinary wild spring species with physical projections and one active ability.
if ([string]$moon.wildPopulationEnabled -ne 'true' -or [string]$moon.wildProjectionKind -ne 'GU_WildMoonlightGu' -or
    [string]$moon.dormantProjectionDef -ne 'GU_DormantMoonlightGu' -or @($moon.abilities.li) -notcontains 'GU_Moonblade') {
    throw 'Moonlight Gu must be acquirable from a spring with its own projections and grant GU_Moonblade.'
}
[xml]$abilityXml = Get-Content -LiteralPath (Join-Path $root 'Defs\GuAbilityDefs\GU_Moonblade.xml') -Raw
$moonblade = $abilityXml.Defs.SelectSingleNode('ProjectGu.Combat.GuAbilityDef[defName="GU_Moonblade"]')
if ($null -eq $moonblade -or [float]$moonblade.essenceCost -le 0 -or [string]$moonblade.projectileDef -ne 'GU_Projectile_Moonblade') {
    throw 'GU_Moonblade needs a positive essence cost and its native projectile.'
}
foreach ($texture in @('Effects\Moonblade\Projectile', 'Effects\Moonblade\Trail', 'Effects\Moonblade\Launch', 'Effects\Moonblade\Impact',
        'UI\Abilities\Moonblade', 'Gu\MoonlightGu\Wild_north', 'Gu\MoonlightGu\Wild_east', 'Gu\MoonlightGu\Wild_south', 'Gu\MoonlightGu\Dormant')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "Textures\ProjectGU\$texture.png"))) { throw "Missing Moonlight Gu texture: $texture" }
}
Write-Host 'PASS: two species, distinct habitats/lifecycles, rank ranges, and declaration structure.' -ForegroundColor Green
