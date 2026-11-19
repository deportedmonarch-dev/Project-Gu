param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'check-environment.ps1') -RimWorldPath $RimWorldPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Environment check failed.' }
$project = Join-Path $root 'Source\ProjectGu\ProjectGu.csproj'
dotnet build $project -c Release "-p:RimWorldPath=$RimWorldPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Send the compiler output to ChatGPT.' }
Write-Host "SUCCESS: $root\1.7\Assemblies\ProjectGu.dll"
