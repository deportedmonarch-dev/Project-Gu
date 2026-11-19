param([string]$RimWorldPath = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is missing. Install SDK 10 and restart PowerShell.' }
$sdks = @(dotnet --list-sdks)
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) { throw "A .NET 10 SDK is required. Installed: $($sdks -join ', ')" }
foreach ($file in @('Assembly-CSharp.dll', 'Waylith.dll', 'UnityStubs.dll')) {
  if (!(Test-Path (Join-Path $RimWorldPath $file))) { throw "Missing $file in $RimWorldPath. Choose Steam's RimWorld 1.7 unstable branch and pass the game root path." }
}
Write-Host "PASS: .NET 10 SDK and RimWorld 1.7 assembly layout found at $RimWorldPath"
