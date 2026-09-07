# Builds CompanionFriendlyFireFix.dll for 7DTD v3.2.0 into the mod folder.
# Usage: powershell -File Mods/Z_CompanionFriendlyFire/build.ps1
$ErrorActionPreference = 'Stop'

$game    = "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
$managed = Join-Path $game "7DaysToDie_Data\Managed"
$repoMods = Split-Path $PSScriptRoot -Parent  # Mods/  (this script lives in Mods/Z_CompanionFriendlyFire/)
$harmony = Join-Path (Resolve-Path (Join-Path $repoMods "0_TFP_Harmony")).Path "0Harmony.dll"
$chh     = Join-Path (Resolve-Path (Join-Path $repoMods "CrystalHellHusbandry")).Path "CHHusbandry.dll"
$csc     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src     = Join-Path $PSScriptRoot "src\CompanionFriendlyFireFix.cs"
$out     = Join-Path $PSScriptRoot "CompanionFriendlyFireFix.dll"

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
foreach ($p in @($managed, $harmony, $chh, $src)) { if (-not (Test-Path $p)) { throw "missing: $p" } }

& $csc /nologo /target:library /out:$out `
  "/r:$managed\Assembly-CSharp.dll" `
  "/r:$managed\UnityEngine.dll" `
  "/r:$managed\UnityEngine.CoreModule.dll" `
  "/r:$managed\LogLibrary.dll" `
  "/r:$managed\netstandard.dll" `
  "/r:$managed\System.Runtime.dll" `
  "/r:$harmony" `
  "/r:$chh" `
  $src

if ($LASTEXITCODE -ne 0) { throw "csc failed (exit $LASTEXITCODE)" }
Write-Output "Built $out ($((Get-Item $out).Length) bytes)"
