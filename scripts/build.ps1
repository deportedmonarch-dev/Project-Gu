param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld', [string]$HarmonyPath = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'check-environment.ps1') -RimWorldPath $RimWorldPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Environment check failed.' }
$project = Join-Path $root 'Source\ProjectGu\ProjectGu.csproj'
$references = @("-p:RimWorldPath=$RimWorldPath")
if ($HarmonyPath) { $references += "-p:HarmonyPath=$HarmonyPath" }
dotnet build $project -c Release @references --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Send the compiler output to ChatGPT.' }
Write-Host "SUCCESS: $root\1.7\Assemblies\ProjectGu.dll"
