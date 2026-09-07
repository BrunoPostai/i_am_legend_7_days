# Companion Friendly-Fire Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tamed companion takes zero damage from its owner or the owner's Steam friends, via a small C# Harmony patch mod.

**Architecture:** A new mod `Mods/Z_CompanionFriendlyFire/` (loads last) ships a class library (`CompanionFriendlyFireFix.dll`) with one Harmony Prefix on `EntityAlive.ProcessDamageResponseLocal(DamageResponse)`. The prefix returns `false` (skipping the original → no damage) when the attacker is the pet's owner or a friend of the owner. It reads owner/tamed state from CHH's **public** `EntityInfoManager` API. The DLL is built with the .NET Framework `csc.exe` via a checked-in `build.ps1`.

**Tech Stack:** C# (Framework 4.x, C#-5-compatible — no dotnet SDK; uses `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`), 7DTD `Assembly-CSharp.dll` (v3.2.0), HarmonyX `0Harmony.dll` (2.x, from `0_TFP_Harmony`), `CHHusbandry.dll` (CHH 0.6.6).

## Global Constraints

- Target game: V 3.2.0 (b10). Game install: `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die`. Mods load from the **game install's** `Mods` folder (separate from repo — sync after build).
- Do NOT modify any existing mod folder/DLL (no CHH, no IAL edits). This plan adds one new mod only.
- No dotnet SDK: compile with Framework `csc.exe` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- The patch must be **fail-open**: any unexpected exception logs and lets the original damage run. Never disable damage incorrectly.
- Rule is fixed: owner + Steam friends. No config file.
- `Z_CompanionFriendlyFire` must sort alphabetically after `I Am Legend` (verify folder name unchanged).
- Keep source + build script in the repo (`Mods/Z_CompanionFriendlyFire/src/` and `build.ps1`).
- Work on branch `feat/vampire-burn-companion-ai`. Commit per task.

---
### Task 1: ModInfo.xml + patch source skeleton

**Files:**
- Create: `Mods/Z_CompanionFriendlyFire/ModInfo.xml`
- Create: `Mods/Z_CompanionFriendlyFire/src/CompanionFriendlyFireFix.cs` (full patch)

**Interfaces:**
- Consumes: public APIs verified in v3.2.0/CHH 0.6.6 — `IModApi.InitMod(Mod)`, `EntityAlive.ProcessDamageResponseLocal(DamageResponse)`, `DamageResponse.Source`, `DamageSource.getEntityId()`, `EntityInfoManager.Instance`, `EntityInfoManager.TryGetValue(int, out EntityInfo)`, `EntityInfo.IsTamed/OwnerId/EntityId`, `EntityPlayer.IsFriendsWith(EntityPlayer)`, `EntityPlayer.entityId`.
- Produces: `CompanionFriendlyFireFix.CompanionFriendlyFireFixMod` (IModApi) and `CompanionFriendlyFireFix.FriendlyFirePatch` (Harmony Prefix). Later tasks build and deploy this DLL.

- [ ] **Step 1: Create ModInfo.xml**

```xml
<xml>
    <Name value="CompanionFriendlyFireFix" />
    <DisplayName value="Companion Friendly Fire Fix" />
    <Description value="Tamed companions take no damage from their owner or the owner's friends." />
    <Author value="HellsJanitor" />
    <Version value="1.0.0.0" />
    <Website value="" />
</xml>
```

- [ ] **Step 2: Create the patch source**

`Mods/Z_CompanionFriendlyFire/src/CompanionFriendlyFireFix.cs`:

```csharp
using System;
using HarmonyLib;
using UnityEngine;
using CHHusbandry.Data;

namespace CompanionFriendlyFireFix
{
    // Entry point discovered by 7DTD mod loader (IModApi.InitMod).
    public class CompanionFriendlyFireFixMod : IModApi
    {
        public void InitMod(global::Mod _modInstance)
        {
            try
            {
                var harmony = new Harmony("com.hellsjanitor.CompanionFriendlyFireFix");
                harmony.PatchAll(GetType().Assembly);
                Log.Out("[CompanionFriendlyFireFix] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Failed to apply patches: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(EntityAlive), "ProcessDamageResponseLocal")]
    public static class FriendlyFirePatch
    {
        private static bool Prefix(EntityAlive __instance, ref bool __result, DamageResponse __0)
        {
            try
            {
                int sourceId = __0.Source.getEntityId();
                if (sourceId == 0) return true;                 // no attacker -> let damage run
                if (__instance == null) return true;

                // Not a CHH companion at all -> let damage run (wild animals unaffected).
                if (EntityInfoManager.Instance == null) return true;
                EntityInfo info;
                if (!EntityInfoManager.Instance.TryGetValue(__instance.entityId, out info)) return true;
                if (info == null || !info.IsTamed) return true;
                int ownerId = info.OwnerId;
                if (ownerId <= 0) return true;

                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return true;
                Entity source = world.GetEntity(sourceId);
                EntityPlayer player = source as EntityPlayer;
                if (player == null) return true;                // zombie/animal/NPC still damages pet

                if (player.entityId == ownerId)
                {
                    __result = false;                           // owner -> block
                    return false;
                }

                Entity owner = world.GetEntity(ownerId);
                EntityPlayer ownerPlayer = owner as EntityPlayer;
                if (ownerPlayer != null && player.IsFriendsWith(ownerPlayer))
                {
                    __result = false;                           // Steam friend -> block
                    return false;
                }
                return true;                                    // not owner, not friend -> let damage run
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Prefix exception (fail-open): " + ex);
                return true;                                    // fail-open: never silently disable damage
            }
        }
    }
}
```

- [ ] **Step 3: Self-review the source**

Check against the design logic step-for-step:
1. sourceId = attacker id → 0? let run (yes, `return true`).
2. EntityInfoManager null? let run (yes).
3. Not a CHH companion / not tamed → let run (yes).
4. ownerId <= 0 → let run (yes).
5. Attacker not EntityPlayer → let run (yes — zombies still hurt pets).
6. attacker == owner → block (`__result=false; return false`).
7. attacker friend of owner → block.
8. otherwise → let run.
All nulls guarded; whole body try/catch fail-open. `EntityInfo`, `EntityInfoManager`, `World`, `Mod`, `EntityPlayer`, `EntityAlive`, `DamageResponse`, `DamageSource`, `GameManager` all resolve in the game's Global namespace (unqualified works; `global::Mod` disambiguates from the `Mod` property pattern found in some XUi code — verify at compile).

- [ ] **Step 4: Commit**

```powershell
git add "Mods/Z_CompanionFriendlyFire/ModInfo.xml" "Mods/Z_CompanionFriendlyFire/src/CompanionFriendlyFireFix.cs"
git commit -m "feat: companion friendly-fire fix mod — source + ModInfo"
```

---

### Task 2: Build script (csc.exe) + compile + smoke test

**Files:**
- Create: `Mods/Z_CompanionFriendlyFire/build.ps1`
- Modify: (generated) `Mods/Z_CompanionFriendlyFire/CompanionFriendlyFireFix.dll`

**Interfaces:**
- Consumes: Task 1 source; game install paths; `csc.exe`.
- Produces: the compiled `CompanionFriendlyFireFix.dll` in the mod folder; `build.ps1` is idempotent/re-runnable so the user can rebuild after any game update.

- [ ] **Step 1: Create build.ps1**

```powershell
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
```

*(If the relative-path math for `$repoMods` proves wrong at runtime, correct it to resolve `Mods/` absolutely in the script — the requirement is "resolves to the repo `Mods` dir".)*

- [ ] **Step 2: Run the build**

```powershell
powershell -ExecutionPolicy Bypass -File "Mods/Z_CompanionFriendlyFire/build.ps1"
```
Expected: `Built ...CompanionFriendlyFireFix.dll (... bytes)`, exit 0. If a compile error is reported (usually an ambiguity or missing namespace for one of the referenced types), fix the source/script and re-run until it compiles. The earlier smoke test proved `csc.exe` + these references work on this machine. Known required references beyond game+deps: `LogLibrary.dll` (the global `Log` type), `netstandard.dll` + `System.Runtime.dll` (avoid `System.Object defined in netstandard` errors). Keep C# to ≤ C# 5 syntax — Framework `csc.exe` rejects `?.` (use explicit null checks).

- [ ] **Step 3: Sanity-inspect the DLL**

Confirm the assembly contains `CompanionFriendlyFireFixMod` and `FriendlyFirePatch` (e.g. `strings`/PowerShell check for these type names in the DLL). Quick check:
```powershell
$bytes = [System.IO.File]::ReadAllBytes("Mods/Z_CompanionFriendlyFire/CompanionFriendlyFireFix.dll")
$s = [System.Text.Encoding]::ASCII.GetString($bytes)
($s -match 'CompanionFriendlyFireFixMod'), ($s -match 'FriendlyFirePatch')
```
Expected: `True True`.

- [ ] **Step 4: Commit**

```powershell
git add "Mods/Z_CompanionFriendlyFire/build.ps1" "Mods/Z_CompanionFriendlyFire/CompanionFriendlyFireFix.dll"
git commit -m "feat: build and compile companion friendly-fire fix DLL"
```

---

### Task 3: Sync to game install + full regression parse + load verification

**Files:**
- Sync: entire `Mods/` folder → game install `Mods/`
- Verify: game `output_log` for clean load

**Interfaces:**
- Consumes: Task 2 DLL; the whole branch's mod set.
- Produces: a byte-identical game-install `Mods` folder (the game reads THIS folder, not the repo) and evidence the new mod loads without errors.

- [ ] **Step 1: Full XML regression parse (all mods)**

```powershell
$fail=0;$count=0
Get-ChildItem "Mods" -Recurse -Filter *.xml | Where-Object { $_.FullName -notlike '*UIAtlases*' } | ForEach-Object {
  $count++
  try { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null } catch { $fail++; Write-Output "FAIL $($_.FullName)" }
}
Write-Output "Parsed=$count Fail=$fail"
```
Expected: `Fail=0`.

- [ ] **Step 2: Sync repo Mods → game install (clean copy)**

```powershell
$repoMods = "C:\Users\cechi\Documents\dev\7daysmod\I-Am-Legend-3.0\Mods"
$gameMods = "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods"
Get-ChildItem $gameMods -Directory | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
Get-ChildItem $gameMods -File | ForEach-Object { Remove-Item $_.FullName -Force }
Copy-Item "$repoMods\*" $gameMods -Recurse -Force
```
Then confirm the folder list is exactly: `0_TFP_Harmony`, `0a-SandboxPresetStateFix`, `80_LBD Returns Core 3.0`, `CrystalHellHusbandry`, `I Am Legend`, `Z_CompanionFriendlyFire`.

- [ ] **Step 3: Launch the game and confirm the new mod loads**

Start the client, let it reach the main menu, then quit. In the newest `C:\Users\cechi\AppData\Roaming\7DaysToDie\logs\output_log_client__*.txt` confirm:
- `[MODS]   Trying to load from folder: 'Z_CompanionFriendlyFire'`
- `[MODS]     Loaded Mod: CompanionFriendlyFireFix (1.0.0.0)`
- `[CompanionFriendlyFireFix] Harmony patches applied.`
No new errors from the mod.

- [ ] **Step 4: Commit (sync is not committed — repo holds the files)**

---

### Task 4: In-game acceptance checklist

**Files:** none (manual). Record results in the plan.

- [ ] **Step 1: Verify owner immunity** — tame a wolf/shepherd; punch, melee, shoot, and grenade-splash near it. Expected: pet takes **0 damage** every time.
- [ ] **Step 2: Verify friend immunity** — a Steam friend (same session) punches the pet. Expected: 0 damage.
- [ ] **Step 3: Verify non-owner still harms** — a zombie attacks the pet (direct and AoE). Expected: pet takes damage and can die; combat works.
- [ ] **Step 4: Verify wild animals unaffected** — punch a fresh untamed wolf/stag. Expected: it takes owner damage and taming still works.
- [ ] **Step 5: Verify no console errors** — open the console (F1) after the above; no `[CompanionFriendlyFireFix]` errors, no exceptions during damage events.

- [ ] **Step 6: Commit the recorded results**

```powershell
git add "docs/superpowers/plans/2026-09-07-companion-friendly-fire.md"
git commit -m "docs: record playtest results for companion friendly-fire fix"
```

---

## Self-Review Notes

- **Spec coverage:** owner+friends block (Task 1), Zombies/wild unaffected (Task 1 logic + Task 4), fail-open error handling (Task 1), build via csc.exe (Task 2), deploy/regression/load (Task 3), acceptance (Task 4). No placeholders — full source, exact build command, exact verification greps.
- **Type consistency:** `Prefix(EntityAlive __instance, ref bool __result, DamageResponse __0)` matches the object-parameter convention; `EntityInfoManager.Instance`, `TryGetValue(out EntityInfo)`, `EntityInfo.IsTamed/OwnerId`, `DamageResponse.Source.getEntityId()`, `EntityPlayer.IsFriendsWith` all verified public in the probe.
- **Global constraints honored:** no dotnet SDK (csc.exe), no edits to other mods, `Z_` loads last, source+script checked in, fail-open patch.