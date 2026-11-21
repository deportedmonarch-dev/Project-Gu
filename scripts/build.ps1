param(
    [string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'check-environment.ps1') -RimWorldPath $RimWorldPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Environment check failed.' }
$project = Join-Path $root 'Source\ProjectGu\ProjectGu.csproj'
$references = @("-p:RimWorldPath=$RimWorldPath")
if ($HarmonyPath) { $references += "-p:HarmonyPath=$HarmonyPath" }
if ($OutputDirectory) { $references += @('--output', $OutputDirectory, '--no-incremental') }
dotnet build $project -c Release @references --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Send the compiler output to ChatGPT.' }
$dll = if ($OutputDirectory) { Join-Path $OutputDirectory 'ProjectGu.dll' } else { Join-Path $root '1.7\Assemblies\ProjectGu.dll' }
if (!(Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Build completed without $dll." }
Write-Host "SUCCESS: $dll"
