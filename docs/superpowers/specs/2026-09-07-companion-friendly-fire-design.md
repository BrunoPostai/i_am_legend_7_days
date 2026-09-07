# Companion Friendly-Fire Fix — Design

Date: 2026-09-07
Status: Approved (design)
Apply to: `I-Am-Legend-3.0` modpack, branch off `feat/vampire-burn-companion-ai`

## 1. Overview

A tamed companion currently takes damage from its owner — the player can literally punch their own pet. Root cause confirmed by decompiling v3.2.0:

- `EntityAlive.FriendlyFireCheck` is a stub returning `true` unconditionally (no owner-vs-pet logic).
- `CrystalHellHusbandry.dll` (CHH) only protects tamed pets against **zombie** damage (`EntityAlive_FriendlyFireCheck_TamedVsZombie_Patches`) and against taming/sedation damage during the taming process. It does NOT block owner-ambient melee/ranged/splash damage; `ProcessDamageResponseLocal_Patches` records owner-damage for a bond penalty rather than blocking it.

Goal: a tamed companion takes **zero damage from its owner or the owner's Steam friends** (punches, melee, gunfire, splash/AoE). Zombies and everyone else can still damage it.

Constraint: pure XML cannot fix this — it requires a small C# Harmony patch. New DLL approved by user (approach A).

## 2. Verified mechanics (from game install v3.2.0 + CHH 0.6.6)

- Patch point: `EntityAlive.ProcessDamageResponseLocal(DamageResponse)` — the server-authoritative damage application method. A Harmony **Prefix returning `false`** cancels the original → no damage.
- `DamageResponse.Source.getEntityId()` yields the attacker's entity id.
- CHH public API (verified surface): `CHHusbandry.Data.EntityInfoManager.TryGetValue(int, out EntityInfo)` → `EntityInfo.IsTamed`, `EntityInfo.OwnerId`, `EntityInfo.EntityId`.
- `EntityPlayer.IsFriendsWith(EntityPlayer)` — the same friend-relationship check CHH already uses.
- Toolchain proven: .NET Framework `csc.exe` (v4.0.30319) compiles a class library against `Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, and `0Harmony.dll` on this machine. No dotnet SDK needed.
- Mod load order (alphabetical): `0_TFP_Harmony`, `0a-SandboxPresetStateFix`, `80_LBD Returns Core 3.0`, `CrystalHellHusbandry`, `I Am Legend`. A `Z_CompanionFriendlyFire` folder sorts last — after every mod it depends on.

## 3. Architecture

New mod folder shipped in the modpack:

```
Mods/Z_CompanionFriendlyFire/
  ModInfo.xml
  CompanionFriendlyFireFix.dll     (build output)
  src/CompanionFriendlyFireFix.cs  (patch source, kept in-repo)
  build.ps1                       (compile shipper)
```

- **ModInfo.xml**: `Name=CompanionFriendlyFireFix`, loads last, requires nothing extra (all deps already present).
- **DLL**: `ModAPI` implementation + one `[HarmonyPatch]` Prefix on `EntityAlive.ProcessDamageResponseLocal`.
- **build.ps1**: calls `csc.exe` with references to the game + Harmony + CHH assemblies, writes the DLL into the mod folder.

## 4. The patch logic (Prefix)

### 4.1 Damage block (Prefix on `EntityAlive.ProcessDamageResponseLocal`)

`Prefix(EntityAlive __instance, DamageResponse __0)`:

1. `sourceId = __0.Source.getEntityId()`; if `0` → return true (no attacker, let original run).
2. `if !EntityInfoManager.TryGetValue(__instance.entityId, out info)` → return true (not a CHH companion).
3. `if !info.IsTamed` → return true (wild animal unaffected).
4. `ownerId = info.OwnerId`; if `ownerId <= 0` → return true.
5. `source = world.GetEntity(sourceId)`.
6. If `source is EntityPlayer player`:
   - if `player.entityId == ownerId` (owner) → return false (blocked).
   - if `player.IsFriendsWith(owner)` (friend) → return false (blocked).
7. Otherwise return true (zombies, animals, NPCs still damage the pet).

Semantics: returning `false` from the Prefix skips the original method → no damage applied. (Note: for a `void` original, Harmony rejects `ref bool __result` — a returning-false Prefix alone skips the original.)

### 4.2 Bond-penalty / "You hurt {pet}!" warning suppression

CHH runs its own Prefix on the same damage method (it loads before `Z_CompanionFriendlyFire`), so it records the owner-damage bond penalty and emits the `"You hurt {pet}!"` warning *before* our damage-blocking Prefix runs. Because we now prevent owner/friend damage, that warning and bond penalty are misleading and are suppressed with a **second, manually-applied Harmony Prefix on CHH's internal method** `CrystalHellHusbandry.Patches.EntityAlive_ProcessDamageResponseLocal_Patches.TryApplyOwnerDamageBondPenalty(EntityAlive, int)`.

- The target type is `internal` to CHHusbandry, so it is patched via **`AccessTools.Method`** + manual `harmony.Patch(prefix: ...)` in `InitMod` (not a compile-time `[HarmonyPatch(typeof(...))]`).
- The Prefix returns `false` (no-op) when the attacker is the pet's owner or friend (same `IsOwnerOrFriend` helper as the damage block); otherwise returns `true` (CHH behavior unchanged for real enemies/zombies).
- `InitMod` resolves the internal type by full name via an `AppDomain`-assembly scan; if resolution fails, it logs a warning and leaves the warning intact (fail-open).

## 5. Error handling / robustness

- Null-guard every lookup (`World`, `EntityInfoManager.Instance`, `EntityInfo`, `source`, `owner`) — a null deref in a Prefix would break all damage on the target entity.
- Wrap the body in a `try/catch` that logs via `Log.Out` and returns `true` on any unexpected exception (fail-open: never disable damage incorrectly).
- Static cache of the `Harmony` instance: `PatchAll` once in `ModAPI.InitMod`.
- Reads CHH only through public types/`EntityInfoManager.Instance` — reflection-free, version-tolerant.

## 6. Build

- `build.ps1` invokes `.NET Framework 4.0 csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- References (absolute paths to game install + repo mods):
  - `Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll` (from game Managed dir)
  - `0Harmony.dll` (from `Mods/0_TFP_Harmony/`)
  - `CHHusbandry.dll` (from `Mods/CrystalHellHusbandry/`)
  - (plus default `mscorlib`/`System` provided by csc)
- Output `CompanionFriendlyFireFix.dll` into `Mods/Z_CompanionFriendlyFire/`.
- Idempotent, no external packages.

## 7. Testing checklist

- [ ] Owner punches tamed wolf/shepherd → 0 damage.
- [ ] Owner melee/ranged/grenade splash near tamed pet → pet takes 0 damage.
- [ ] Owner attack does NOT trigger the "You hurt {pet}!" warning or any bond penalty.
- [ ] Steam friend punches the pet → 0 damage and no warning.
- [ ] Zombie attacks the tamed pet → still takes damage and can die.
- [ ] Fresh wild animal (not yet tamed) → still takes owner damage (taming works).
- [ ] All 52 mod XML still parse; mod loads with no errors in `output_log`.
- [ ] No console errors on damage events while the patch is active.

## 8. Out of scope / follow-ups

- Making pets invulnerable to zombies or all sources (not requested).
- Config file to toggle owner/friends protection (YAGNI; fixed rule).
- Multiplayer beyond Steam-friends semantics that CHH itself uses.