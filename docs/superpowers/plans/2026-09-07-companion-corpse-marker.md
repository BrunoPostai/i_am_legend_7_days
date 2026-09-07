# Companion Death: Corpse Persistence & Map Marker — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A dead tamed companion's corpse persists effectively forever and shows a map/compass marker, so the revival bandage is always usable. Bandage stays the only revival mechanism.

**Architecture:** Part 1 is pure XML — set `TimeStayAfterDeath="720000"` on tamable companion entity classes. Part 2 extends the existing `Z_CompanionFriendlyFire` C# mod (which we build with `csc.exe`) with a death→marker event: on `ModEvents.HEntityKilled`, if the killed entity is a tamed CHH companion, register an `animaltracking_wolf` NavObject at the corpse; remove it on revive/corpse-gone.

**Tech Stack:** 7DTD v3.2.0 XML patches; C# (Framework csc.exe, no SDK) + Harmony 2.13 + the game's `NavObjectManager`/`ModEvents` API; existing `Z_CompanionFriendlyFire` mod infra.

## Global Constraints

- Target game V 3.2.0. Game install: `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die`. Game loads mods from the **game install's** `Mods` folder (separate from repo).
- No dotnet SDK — compile with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Keep all C# ≤ C# 5 (no `?.`, no string interpolation).
- Do NOT modify any existing mod folder except adding to `Mods/Z_CompanionFriendlyFire/` (our own mod) and the two entityclasses.xml files we already own.
- Keep the existing patches (friendly-fire damage block, bond-penalty suppression, buff/DoT block, fall-damage zero, needs no-op). No regressions.
- Marker scope: only the owner's *tamed* companion corpse gets a marker; wild animals get none.
- Work on branch `main`. Commit per task.

---
### Task 1: XML — persist dead companion corpses

**Files:**
- Modify: `Mods/I Am Legend/Config/entityclasses.xml` (German Shepherd classes)
- Modify: `Mods/CrystalHellHusbandry/Config/entityclasses.xml` (scoped companion append)

**Interfaces:**
- Consumes: existing `CompanionProfile`/`AnimalTamingProfile` xpath scopes (already used by the fall-damage append).
- Produces: companions whose dead body stays ~10h real time.

- [ ] **Step 1: German Shepherd classes**

In `Mods/I Am Legend/Config/entityclasses.xml`, inside the `<entity_class name="animalGermanShepherd" extends="animalTemplateHostile">` block (line ~933), add after the stats/taming region (anywhere inside the entity_class, before its close):

```xml
<property name="TimeStayAfterDeath" value="720000"/>
```

Also add the same property to the other German Shepherd entity (`animalGermanShepherdCompanion`, line ~905 block) if that class is used as a spawnable companion corpse carrier.

- [ ] **Step 2: Scoped companion append in CHH**

In `Mods/CrystalHellHusbandry/Config/entityclasses.xml`, extend the existing fall-damage append to also set the corpse lifetime for ALL tamables (same xpath as the fall-damage append):

```xml
<append xpath="//entity_class[property[@name='CompanionProfile'] or property[@name='AnimalTamingProfile']]">
    <property name="TimeStayAfterDeath" value="720000"/>
</append>
```

(Add this as a second property inside the SAME append as fall-damage, or a new adjacent append — either is fine; keep it adjacent/readable.)

- [ ] **Step 3: Validate XML parses**

```powershell
[xml](Get-Content -LiteralPath "Mods/I Am Legend/Config/entityclasses.xml" -Raw) | Out-Null
[xml](Get-Content -LiteralPath "Mods/CrystalHellHusbandry/Config/entityclasses.xml" -Raw) | Out-Null
Write-Output "OK"
```
Expected: `OK`.

- [ ] **Step 4: Commit**

```powershell
git add "Mods/I Am Legend/Config/entityclasses.xml" "Mods/CrystalHellHusbandry/Config/entityclasses.xml"
git commit -m "feat: dead companion corpses persist ~10h (75000 ticks vs 300)"
```

---

### Task 2: C# — corpse map marker on companion death

**Files:**
- Modify: `Mods/Z_CompanionFriendlyFire/src/CompanionFriendlyFireFix.cs`
- Modify: `Mods/Z_CompanionFriendlyFire/build.ps1` (only if new references needed — `NavObjectManager`/`ModEvents` live in `Assembly-CSharp.dll`, already referenced)

**Interfaces:**
- Consumes (verified public API): `ModEvents.HEntityKilled` (`SEntityKilledData { Entity KilledEntitiy; Entity KillingEntity; }`), `ModEvents.EntityKilled.RegisterHandler(...)`, `NavObjectManager.Instance.RegisterNavObject(string, Vector3, string, bool, int, Entity)`, `NavObjectManager.UnRegisterNavObjectByOwnerEntity(Entity, string)`, CHH `EntityInfoManager.Instance.TryGetValue(int, out EntityInfo)`, `EntityInfo.IsTamed/OwnerId/Health`.
- Produces: a sixth Harmony patch class `CorpseMarkerPatch` registered in `InitMod`, plus a `NavObjectManager`-based marker created on tamed-companion death and cleaned up on revive.

- [ ] **Step 1: Add the death marker logic to the source**

Append to `CompanionFriendlyFireFix.cs` (below the existing patch classes):

```csharp
// NavObjectManager public API used for the corpse marker.
// Handler registered via ModEvents.EntityKilled.RegisterHandler(...) — same pattern
// CHH uses (CHHusbandry.ModAPI.OnEntityKilled). Handler signature: void(SEntityKilledData&).
public static class CorpseMarkerPatch
{
    private static readonly System.Collections.Generic.Dictionary<int, NavObject> _markers =
        new System.Collections.Generic.Dictionary<int, NavObject>();

    private static bool IsTamedCompanion(EntityAlive e, out int ownerId)
    {
        ownerId = 0;
        if (e == null) return false;
        if (EntityInfoManager.Instance == null) return false;
        EntityInfo info;
        if (!EntityInfoManager.Instance.TryGetValue(e.entityId, out info)) return false;
        if (info == null || !info.IsTamed) return false;
        ownerId = info.OwnerId;
        return ownerId > 0;
    }

    // Registered handler (matches CHH's own OnEntityKilled signature).
    public static void OnEntityKilled(ModEvents.SEntityKilledData args)
    {
        try
        {
            EntityAlive dead = args.KilledEntitiy as EntityAlive;
            int ownerId;
            if (!IsTamedCompanion(dead, out ownerId)) return;
            if (NavObjectManager.Instance == null) return;

            int id = dead.entityId;
            NavObject old;
            if (_markers.TryGetValue(id, out old) && old != null)
            {
                NavObjectManager.Instance.UnRegisterNavObject(old);
                _markers.Remove(id);
            }
            NavObject marker = NavObjectManager.Instance.RegisterNavObject(
                "animaltracking_wolf", dead.position, dead.entityName, false, id, dead);
            if (marker != null) { _markers[id] = marker; }
        }
        catch (Exception ex)
        {
            Log.Error("[CompanionFriendlyFireFix] Corpse marker exception (fail-open): " + ex);
        }
    }

    public static void Register()
    {
        // Same registration CHH uses: ModEvents.EntityKilled.RegisterHandler(handler)
        // where handler is a ModEventHandlerDelegate<SEntityKilledData> (void, by-ref arg).
        ModEvents.EntityKilled.RegisterHandler(OnEntityKilled);
    }

    public static void ClearAll()
    {
        if (NavObjectManager.Instance == null) return;
        foreach (var kv in _markers) { if (kv.Value != null) NavObjectManager.Instance.UnRegisterNavObject(kv.Value); }
        _markers.Clear();
    }
}
```

Then, in `InitMod`, after the needs-suppression registration, add:

```csharp
CorpseMarkerPatch.Register();
```

Note: verify at compile that `RegisterNavObject` binds to the `(String, Vector3, String, Boolean, Int32, Entity)` overload; if it doesn't, adjust the argument list to whichever positional overload binds (`String, Entity, String, Boolean` also exists and can carry `dead` as the owner entity). `ModEvents.SEntityKilledData.KilledEntitiy` is spelled exactly as shown (`KilledEntitiy`).

- [ ] **Step 2: Build with csc**

```powershell
powershell -ExecutionPolicy Bypass -File "Mods/Z_CompanionFriendlyFire/build.ps1"
```
Expected: `Built ...CompanionFriendlyFireFix.dll (... bytes)`, exit 0. Fix any compile errors (typeof binding for `ModEvents`, namespace for `NavObjectManager`) until it builds. `ModEvents` and `NavObjectManager` are in the global namespace in `Assembly-CSharp.dll`.

- [ ] **Step 3: Sanity check the DLL**

Confirm the new `CorpseMarkerPatch` type is present (PowerShell string check on the DLL bytes). Expected `True`.

- [ ] **Step 4: Commit**

```powershell
git add "Mods/Z_CompanionFriendlyFire/src/CompanionFriendlyFireFix.cs" "Mods/Z_CompanionFriendlyFire/CompanionFriendlyFireFix.dll"
git commit -m "feat: corpse marker for tamed companions on death"
```

---

### Task 3: Sync to game install + full regression

**Files:** sync `Mods/` → game install.

- [ ] **Step 1: Full XML regression parse** (all mods, exclude UIAtlases). Expected `Fail=0`.
- [ ] **Step 2: Clean sync repo `Mods` → game install `Mods`** (delete existing subdirs then copy). Confirm folders: `0_TFP_Harmony`, `0a-SandboxPresetStateFix`, `80_LBD Returns Core 3.0`, `CrystalHellHusbandry`, `I Am Legend`, `Z_CompanionFriendlyFire`.
- [ ] **Step 3: Launch game; in `output_log` confirm:**
  - `[MODS] Loaded Mod: CompanionFriendlyFireFix (1.0.0.0)`
  - `[CompanionFriendlyFireFix] ... patches applied.` (all previous + no error lines)
  - No `Failed to patch` / `NullReferenceException` in the mod's own log lines.
- [ ] **Step 4:** (sync isn't committed — repo holds files)

---

### Task 4: In-game acceptance

- [ ] Kill a tamed companion → `animaltracking_wolf` marker on map + compass at the corpse.
- [ ] Body persists ≥ 10 min.
- [ ] Revive with bandage → companion alive, marker gone.
- [ ] Kill a wild wolf/stag → no marker.
- [ ] Regression: owner/friend punch → 0 damage & no warning; bleeding doesn't apply; no fall damage; no hunger/thirst.
- [ ] Record results in the plan + commit.

---

## Self-Review Notes

- **Spec coverage:** corpse persistence (Task 1), marker on death (Task 2), deploy/regression (Task 3), acceptance (Task 4).
- **Type consistency:** `ModEvents.SEntityKilledData` field `KilledEntitiy` (spelled exactly as in the game), `NavObjectManager.RegisterNavObject` had two positional overloads — the plan uses the `(string, Vector3, string, bool, int, Entity)` whose args match the spec's description; the implementer should pick whichever overload actually binds and adjust args to match (compile-time TDD).
- **Fail-open:** marker registration is wrapped in try/catch; failure never breaks the kill event.
- **Global constraints held:** no SDK (csc.exe), no edits to other mods, marker only for tamed, C# ≤ 5.