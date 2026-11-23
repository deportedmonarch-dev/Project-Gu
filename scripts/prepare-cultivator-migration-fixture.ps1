param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Case
)
$ErrorActionPreference = 'Stop'
$workingSave = (Resolve-Path -LiteralPath $Path).Path
$root = Split-Path -Parent $PSScriptRoot
$campaignPrefix = [IO.Path]::GetFullPath((Join-Path $root 'tests/obj/Priority0-'))
if (!$workingSave.StartsWith($campaignPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $workingSave.Substring($campaignPrefix.Length) -notmatch '^[a-f0-9]{32}\\C3-[a-zA-Z0-9_-]+\\Saves\\autostart\.rws$') {
    throw 'C3 XML changes require a disposable campaign working save.'
}
$doc = [Xml.XmlDocument]::new()
$doc.PreserveWhitespace = $true
$doc.Load($workingSave)
if ($Case -in @('historical-31', 'historical-39')) {
    if ($Case -eq 'historical-31') {
        $oldComponent = $doc.SelectSingleNode('//li[@Class="Legacy31" and not(*)]')
        if ($oldComponent) {
            [void]$oldComponent.ParentNode.RemoveChild($oldComponent)
            $doc.Save($workingSave)
        }
    }
    return
}

function Add-Field($Parent, [string]$Name, [string]$Value = '') {
    $field = $doc.CreateElement($Name)
    $field.InnerText = $Value
    [void]$Parent.AppendChild($field)
    $field
}

function Set-ApertureId($Aperture, [string]$Id) {
    if ($Aperture['guApertureId']) { [void]$Aperture.RemoveChild($Aperture['guApertureId']) }
    if ($Id) { [void](Add-Field $Aperture 'guApertureId' $Id) }
}

function Add-LegacyRow([string]$Token, [string]$Aptitude, [string]$State, [string]$Physique) {
    $row = Add-Field $legacy 'li'
    if ($Token) { [void](Add-Field $row 'pawn' $Token) }
    [void](Add-Field $row 'latentAptitude' $Aptitude)
    [void](Add-Field $row 'awakeningState' $State)
    [void](Add-Field $row 'extremePhysiqueId' $Physique)
    [void](Add-Field $row 'legacyMigrated' 'True')
}

function Add-CommittedRow($Source, [string]$Id, [string]$Key) {
    $row = Add-Field $records 'li'
    foreach ($pair in @{
        id = $Id; pawn = $Source.Token; pawnLoadId = $Source.Token; creationKey = $Key; association = 'Bound'
        latentAptitude = '0'; extremePhysiqueId = 'c3-saved-physique'; awakeningState = 'Awakened'; legacyMigrated = 'True'
    }.GetEnumerator()) { [void](Add-Field $row $pair.Key $pair.Value) }
    $links = Add-Field $row 'apertures'
    [void](Add-Field $links 'li' $Source.Aperture.guApertureId)
}

$component = $doc.SelectSingleNode('//li[@Class="ProjectGu.GuWorldComponent"]')
if (!$component -or $component['guCultivatorSchemaVersion'] -or $component.SelectNodes('guPawnRecords/li').Count) {
    throw 'Expected a pre-3A Phase 2 source with no legacy rows for the C3 XML variants.'
}
$apertures = @($doc.SelectNodes('//li[@Class="ProjectGu.Hediff_Aperture"]') | ForEach-Object {
    $pawn = $_.ParentNode.ParentNode.ParentNode.ParentNode
    if (!$pawn['id']) { throw 'C3 aperture is missing its native host ID.' }
    [pscustomobject]@{ Aperture = $_; Token = 'Thing_' + $pawn.id }
})
if ($apertures.Count -ne 3 -or @($apertures | Where-Object { !$_.Aperture['guApertureId'] }).Count) {
    throw 'The C3 variant source must have its three Phase 2 aperture IDs.'
}
$legacy = $component['guPawnRecords']
if ($legacy) { [void]$component.RemoveChild($legacy) }

switch ($Case) {
    'legacy-evidence' {
        $anchors = $doc.SelectNodes('//guIndividualRecordsV2/li[containerId and anchorHint and not(containerKind)]')
        $absent = @($anchors | Where-Object { $_.unresolved -eq 'ApertureMissing' } | Select-Object -First 1)[0]
        $held = @($anchors | Where-Object { !$_.unresolved } | Select-Object -First 2)
        if (!$absent -or $held.Count -ne 2) { throw 'Legacy variant source lacks the expected Phase 2 anchor cases.' }
        $missingToken = 'Thing_Human2147483000'
        $absent['anchorHint'].InnerText = $missingToken
        $legacy = Add-Field $component 'guPawnRecords'
        Add-LegacyRow $held[0].anchorHint '0' '2' 'c3-valid'
        Add-LegacyRow $missingToken '68' '1' 'c3-historical'
        Add-LegacyRow 'null' '43' '1' 'c3-explicit-null'
        Add-LegacyRow '' '52' '0' 'c3-no-pawn-node'
        Add-LegacyRow $held[1].anchorHint '63' '2' 'c3-duplicate-one'
        Add-LegacyRow $held[1].anchorHint '64' '2' 'c3-duplicate-two'
        Add-LegacyRow 'Thing_Human2147483001' 'malformed' '0' 'c3-malformed'
    }
    'aperture-conflicts' {
        Set-ApertureId $apertures[1].Aperture $apertures[0].Aperture.guApertureId.ToUpperInvariant()
        Set-ApertureId $apertures[2].Aperture 'c3-invalid-aperture-id'
        $legacy = Add-Field $component 'guPawnRecords'
        foreach ($item in $apertures) { Add-LegacyRow $item.Token '63' '2' 'c3-aperture-conflict' }
    }
    { $_ -in @('duplicate-identities', 'valid-committed', 'valid-empty') } {
        [void](Add-Field $component 'guCultivatorSchemaVersion' '1')
        [void](Add-Field $component 'guCultivatorMigrationComplete' 'True')
        $records = Add-Field $component 'guCultivatorRecordsV1'
        [void](Add-Field $component 'guCultivatorIntegrityV1')
        $id = '2a89bd1ddc4a4b508ab22248f80de838'
        if ($Case -ne 'valid-empty') { Add-CommittedRow $apertures[1] $id 'c3-first' }
        if ($Case -eq 'duplicate-identities') { Add-CommittedRow $apertures[2] $id.ToUpperInvariant() 'c3-second' }
    }
    { $_ -in @('partial-failure', 'failed-rollback') } {
        foreach ($item in $apertures) { Set-ApertureId $item.Aperture '' }
        $legacy = Add-Field $component 'guPawnRecords'
        Add-LegacyRow $apertures[1].Token '53' '2' 'c3-recovery-preimage'
    }
    default { throw "Unknown C3 fixture variant: $Case" }
}
$doc.Save($workingSave)
