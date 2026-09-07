# Vampire Sun Burn & Companion AI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Dark Seekers burn lethally-but-tier-scaled in daylight, and make tamed companions immune to fall damage, safer from friendly fire, and smarter (follow-first, zombie-targeted combat).

**Architecture:** Pure XML/config changes in the `I Am Legend` and `CrystalHellHusbandry` mod folders. The sun burn lives entirely in `Mods/I Am Legend/Config/buffs.xml` (the `IAmLegendCore.dll` already decides *when* to apply `buffZombieSunBurn`; this plan only retunes the damage). Companion changes live in `CrystalHellHusbandry/Config/entityclasses.xml` plus the German Shepherd block in `I Am Legend/Config/entityclasses.xml`. No DLLs, no new mods.

**Tech Stack:** 7 Days to Die v3.2.0 XML patch syntax (`xpath` append/set), buff passive effects, entity class properties. Game install: `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die`. Repo: this workspace. The game loads mods from the **game install's** `Mods` folder — that folder must be synced from the repo `Mods` folder after changes are committed (see Global Constraints).

## Global Constraints

- Target game: **V 3.2.0 (b10)**. Do not reference version-specific XML that doesn't exist there.
- Constraint: **pure XML/config. No C# mods.**
- Load order (alphabetical by folder): `0_TFP_Harmony`, `0a-SandboxPresetStateFix`, `80_LBD Returns Core 3.0`, `CrystalHellHusbandry`, `I Am Legend`. Do not rename folders.
- The game runs mods from `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods`. After each committed change, sync the repo `Mods` folder there (`robocopy` or manual copy; keep folder names identical).
- The `XUi`/talent windows are owned by the `80_LBD` mod. **Do not touch** `windowSkillList`, `windowSkillPerkInfo`, or any `XUi_InGame` files.
- Do not touch `buffNightBiomeZombie`, `buffBiomeZombieDawnDeath`, or the DLL's outdoors/daytime detection.
- After every XML edit: validate every mod XML parses (`[xml]` in PowerShell) before running the game.
- Work on branch `fix/review-round1`. Commit per task.

---

### Task 1: Rework `buffZombieSunBurn` into per-tier tag-gated damage

**Files:**
- Modify: `Mods/I Am Legend/Config/buffs.xml:25-38` (the `buffZombieSunBurn` block)

**Interfaces:**
- Consumes: existing `buffZombieBurnTrigger` → `buffZombieSunBurn` wiring (unchanged); the DLL applies this buff by name.
- Produces: a `buffZombieSunBurn` whose damage scales by entity tier. Nothing else consumes it directly.

- [ ] **Step 1: Replace the single damage group with per-tier groups**

Replace the current `buffZombieSunBurn` effect_group (lines 31-37) so the buff becomes:

```xml
<buff name="buffZombieSunBurn" hidden="true">
    <damage_type value="heat"/>
    <stack_type value="effect"/>
    <duration value="0"/>
    <update_rate value=".25"/>
    <!-- Damage while exposed — per-tier (flat DPS; tougher tiers have bigger HP pools so they survive longer) -->
    <effect_group>
        <requirement name="!EntityTagCompare" tags="feral,radiated,charged,infernal"/>
        <passive_effect name="HealthChangeOT" operation="base_subtract" value="10"/>
        <triggered_effect trigger="onSelfBuffStart" action="AddBuff" buff="buffIsOnFire"/>
    </effect_group>
    <effect_group>
        <requirement name="EntityTagCompare" tags="feral"/>
        <passive_effect name="HealthChangeOT" operation="base_subtract" value="17.5"/>
        <triggered_effect trigger="onSelfBuffStart" action="AddBuff" buff="buffIsOnFire"/>
    </effect_group>
    <effect_group>
        <requirement name="EntityTagCompare" tags="radiated"/>
        <passive_effect name="HealthChangeOT" operation="base_subtract" value="25"/>
        <triggered_effect trigger="onSelfBuffStart" action="AddBuff" buff="buffIsOnFire"/>
    </effect_group>
    <effect_group>
        <requirement name="EntityTagCompare" tags="charged"/>
        <passive_effect name="HealthChangeOT" operation="base_subtract" value="27.5"/>
        <triggered_effect trigger="onSelfBuffStart" action="AddBuff" buff="buffIsOnFire"/>
    </effect_group>
    <effect_group>
        <requirement name="EntityTagCompare" tags="infernal"/>
        <passive_effect name="HealthChangeOT" operation="base_subtract" value="30"/>
        <triggered_effect trigger="onSelfBuffStart" action="AddBuff" buff="buffIsOnFire"/>
    </effect_group>
</buff>
```

Keep everything else in the file unchanged (lines 19-24 `buffZombieBurnTrigger`, lines 40-54 night/dawn buffs, line 57 `buffIsOnFire` append).

- [ ] **Step 2: Validate XML parses**

Run (PowerShell, repo root):
```powershell
[xml](Get-Content -LiteralPath "Mods/I Am Legend/Config/buffs.xml" -Raw) | Out-Null
Write-Output "OK"
```
Expected: prints `OK`. Fix any XML syntax error (mismatched tags, bad attribute quoting) before proceeding.

- [ ] **Step 3: Sanity-check the tag gate against entity classes**

Confirm each darkseeker tier really carries exactly one of the gated tags, and that base uses "not any-tier":
```powershell
[regex]::Matches((Get-Content "Mods/I Am Legend/Config/entityclasses.xml" -Raw), '<property name="Tags" value="([^"]+)"') |
  ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
```
Expected: each tag list contains `feral`, `radiated`, `charged`, or `infernal` at most once and never two of them together. Non-tiered classes (normals/slims/crawlers/spiders/spitters without a tier tag) carry none of those tags — they hit the base group.

- [ ] **Step 4: Commit**

```powershell
git add "Mods/I Am Legend/Config/buffs.xml"
git commit -m "feat: tier-scaled vampire sun burn (doom-timer per darkseeker tier)"
```

---

### Task 2: Zero fall damage for all tamable companions

**Files:**
- Modify: `Mods/CrystalHellHusbandry/Config/entityclasses.xml` (add one global append near the top, after line 7)

**Interfaces:**
- Consumes: every entity class that declares a `CompanionProfile` property (the tamable set: animalWolf, animalDireWolf, animalBoar, animalBear, animalZombieBear, animalBearSmall, animalMountainLion, animalRabbit, animalChicken, animalStag, animalDoe — plus the German Shepherd in the I Am Legend file, which also carries `CompanionProfile`).
- Produces: a globally-scoped append so any future companion automatically gets fall immunity too.

- [ ] **Step 1: Add the fall-damage immunity append**

Insert after the `animalZombieDog` block (after line 7):

```xml
    <!-- Companions never take fall damage (mountains/ledges) -->
    <append xpath="//entity_class[property[@name='CompanionProfile']]">
        <effect_group>
            <passive_effect name="FallDamageReduction" operation="base_set" value="0"/>
        </effect_group>
    </append>
```

- [ ] **Step 2: Validate XML parses**

```powershell
[xml](Get-Content -LiteralPath "Mods/CrystalHellHusbandry/Config/entityclasses.xml" -Raw) | Out-Null
Write-Output "OK"
```
Expected: `OK`.

- [ ] **Step 3: Confirm the set of affected classes**

```powershell
[regex]::Matches((Get-Content "Mods/CrystalHellHusbandry/Config/entityclasses.xml" -Raw), "entity_class\[@name='(animal[A-Za-z]+)'\]") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
```
Expected: wolf, direWolf, boar, bear, zombieBear, bearSmall, mountainLion, rabbit, chicken, stag, doe. (don't count zombieBoar — it has no CompanionProfile and is intentionally excluded).

- [ ] **Step 4: Commit**

```powershell
git add "Mods/CrystalHellHusbandry/Config/entityclasses.xml"
git commit -m "feat: zero fall damage for tamed companions"
```

---

### Task 3: Follow-first AI task ordering for predator companions

**Files:**
- Modify: `Mods/CrystalHellHusbandry/Config/entityclasses.xml` (add a global set block)
- Modify: `Mods/I Am Legend/Config/entityclasses.xml` German Shepherd block (lines 960-972)

**Interfaces:**
- Consumes: the existing CHH AI-task list each predator companion already defines (`AITask-1..11` with `FollowOwner, CHHusbandry` at index 6).
- Produces: `FollowOwner, CHHusbandry` at index 3 (high priority), breeding/trough/fear shifted down one. Target list (index 7 `ApproachAndAttackTarget`) becomes zombie-first.

- [ ] **Step 1: Add a scoped reorder for the CHH predator companions**

Append to `Mods/CrystalHellHusbandry/Config/entityclasses.xml` (place after the fall-damage append from Task 2). Scope with `contains(., 'FollowOwner')` so passive livestock (rabbit/chicken/stag/doe, which keep vanilla AI) are untouched:

```xml
    <!-- Follow-first AI for predator companions -->
    <set xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]/property[@name='AITask-3']/@value">FollowOwner, CHHusbandry</set>
    <set xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]/property[@name='AITask-4']/@value">BreedingApproach, CHHusbandry</set>
    <set xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]/property[@name='AITask-5']/@value">TroughApproach, CHHusbandry</set>
    <set xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]/property[@name='AITask-6']/@value">FearResponse, CHHusbandry</set>
```

(this swaps the old order 3=Breeding,4=Trough,5=Fear,6=Follow into 3=Follow,4=Breeding,5=Trough,6=Fear in one pass).

- [ ] **Step 2: Retune the attack-target list for zombies-first**

For each predator companion class that sets `AITask-7` (in `CrystalHellHusbandry/Config/entityclasses.xml` — wolf, direWolf, boar, bear, zombieBear, bearSmall, mountainLion) change the `ApproachAndAttackTarget` `data` attribute to:

```
class=EntityZombie,50,EntityBandit,30
```

Do this with one scoped set:
```xml
    <set xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]/property[@name='AITask-7']/@data">class=EntityZombie,50,EntityBandit,30</set>
```
(This drops `EntityPlayer` and `EntityAnimalStag` targets so the pet neither charge-chases random animals nor goes after other players.)

- [ ] **Step 3: Same ordering + target retune for the German Shepherd**

In `Mods/I Am Legend/Config/entityclasses.xml`, the German Shepherd block (lines 960-972) already lists the canonical 1-11 AI order. Reorder to match Task 3 Step 1 and retune the target list. Replace lines 960-972 with:

```xml
					<property name="AITask-1" value="BreakBlock"/>
					<property name="AITask-2" value="Territorial"/>
					<property name="AITask-3" value="FollowOwner, CHHusbandry"/>
					<property name="AITask-4" value="BreedingApproach, CHHusbandry"/>
					<property name="AITask-5" value="TroughApproach, CHHusbandry"/>
					<property name="AITask-6" value="FearResponse, CHHusbandry"/>
					<property name="AITask-7" value="ApproachAndAttackTarget" data="class=EntityZombie,50,EntityBandit,30"/>
					<property name="AITask-8" value="ApproachSpot"/>
					<property name="AITask-9" value="Look"/>
					<property name="AITask-10" value="Wander"/>
					<property name="AITask-11" value=""/>
```

- [ ] **Step 4: Validate both files parse**

```powershell
[xml](Get-Content -LiteralPath "Mods/CrystalHellHusbandry/Config/entityclasses.xml" -Raw) | Out-Null
[xml](Get-Content -LiteralPath "Mods/I Am Legend/Config/entityclasses.xml" -Raw) | Out-Null
Write-Output "OK"
```
Expected: `OK`.

- [ ] **Step 5: Commit**

```powershell
git add "Mods/CrystalHellHusbandry/Config/entityclasses.xml" "Mods/I Am Legend/Config/entityclasses.xml"
git commit -m "feat: follow-first AI and zombie-first targeting for companions"
```

---

### Task 4: Danger-avoidance tuning (fear/flee balance)

**Files:**
- Modify: `Mods/CrystalHellHusbandry/Config/entityclasses.xml` (add global property sets)

**Interfaces:**
- Consumes: the CHH `Companion*` property system (DLL reads these as entity-class properties — verified names: `CompanionFearRadius`, `CompanionCalmRadius`, `CompanionFlee*`, `CompanionTemperament`).
- Produces: predator companions react to being hurt/overwhelmed without standing in zombie AoE.

- [ ] **Step 1: Add conservative fear/flee properties to predator companions**

Append to `Mods/CrystalHellHusbandry/Config/entityclasses.xml`:

```xml
    <!-- Danger avoidance: flee only when seriously hurt, keep a defensive posture -->
    <append xpath="//entity_class[property[@name='CompanionProfile'] and property[contains(@value, 'FollowOwner')]]">
        <property name="CompanionTemperament" value="Defensive"/>
        <property name="CompanionFearResponse" value="Defensive"/>
        <property name="CompanionFleeFearThreshold" value="80"/>
        <property name="CompanionFleeDurationSeconds" value="4"/>
        <property name="CompanionFleeCooldownSeconds" value="10"/>
    </append>
```

Rationale: `AggressivePredator` sets combat posture; this keeps it aggressive in a fight but lets it disengage (flee) only when fear is extreme (80/100), and not repeatedly (10s cooldown). Apply only to predator companions (contains FollowOwner), never to passive livestock.

- [ ] **Step 2: Validate XML parses**

```powershell
[xml](Get-Content -LiteralPath "Mods/CrystalHellHusbandry/Config/entityclasses.xml" -Raw) | Out-Null
Write-Output "OK"
```
Expected: `OK`.

- [ ] **Step 3: Commit**

```powershell
git add "Mods/CrystalHellHusbandry/Config/entityclasses.xml"
git commit -m "feat: companion danger-avoidance (fear/flee tuning)"
```

---

### Task 5: Friendly-fire verification route (pure-XML gap)

**Files:**
- Verify: `Mods/I Am Legend/Config/npc.xml`, `Mods/CrystalHellHusbandry/Config/npc.xml`

**Interfaces:**
- Consumes: the game's `EntityAlive.FriendlyFireCheck` (already blocks owner damage when `OwnerId` is set — verified in v3.2.0 assembly).
- Produces: a documented test pass/fail on player splash hitting an owned pet; no code changes unless the faction needs adjusting.

- [ ] **Step 1: Inspect the two npc.xml files for unrelated faction overrides**

```powershell
Get-Content "Mods/I Am Legend/Config/npc.xml"
Get-Content "Mods/CrystalHellHusbandry/Config/npc.xml"
```
Expected: `I Am Legend/npc.xml` is empty/trivial; `CrystalHellHusbandry/npc.xml` contains only commented-out faction relationships. If any uncommented `relationship` for the `animals` faction exists that sets the player relationship friendly, that is the ONLY scenario this plan removes it — otherwise **make no changes here**.

- [ ] **Step 2: Run the in-game check and record the outcome**

In a test world: tame/own a companion, throw a grenade and swing a melee splash near it. Check whether it takes damage. Record the result, then write it under the plan in the testing checklist (see Task 7). No code changes for this task unless the check FAILS and a faction override is the cause (then amend `npc.xml` to remove the wrong relationship and re-commit with a note).

- [ ] **Step 3: Commit (if any faction change was needed; otherwise create an empty commit is NOT allowed — just proceed)**

If a change was made:
```powershell
git add "Mods/I Am Legend/Config/npc.xml" "Mods/CrystalHellHusbandry/Config/npc.xml"
git commit -m "fix: remove player-friendly animal faction override causing splash friendly-fire"
```

---

### Task 6: Sync repo Mods to the game install and full regression parse

**Files:**
- Sync: entire `Mods/` folder → `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods`

**Interfaces:**
- Consumes: all tasks' outputs.
- Produces: a game-install `Mods` folder byte-identical to the repo, so in-game testing reflects committed state. (Critical: the game does NOT read the repo directly.)

- [ ] **Step 1: Full XML regression parse**

```powershell
$fail=0;$count=0
Get-ChildItem "Mods" -Recurse -Filter *.xml | Where-Object { $_.FullName -notlike '*UIAtlases*' } | ForEach-Object {
  $count++
  try { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null } catch { $fail++; Write-Output "FAIL $($_.FullName)" }
}
Write-Output "Parsed=$count Fail=$fail"
```
Expected: `Fail=0`.

- [ ] **Step 2: Sync repo Mods → game Mods**

```powershell
$repo = "C:\Users\cechi\Documents\dev\7daysmod\I-Am-Legend-3.0\Mods"
$game = "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods"
Get-ChildItem $game -Directory | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
Get-ChildItem $game -File | ForEach-Object { Remove-Item $_.FullName -Force }
Copy-Item "$repo\*" $game -Recurse -Force
```
Then verify folder list is exactly: `0_TFP_Harmony`, `0a-SandboxPresetStateFix`, `80_LBD Returns Core 3.0`, `CrystalHellHusbandry`, `I Am Legend`.

- [ ] **Step 3: Commit** (sync is not committed — the repo already holds the files)

---

### Task 7: In-game acceptance checklist

**Files:**
- None (manual verification). Record results under this plan / release notes.

- [ ] **Step 1: Run the game (client, local world) and verify the following; check each box only when observed.**

- Sun burn: spawn a normal, feral, radiated, charged, and infernal darkseeker in open sun at midday. Confirm burn damage ticks and that each dies in ~5s/6s/7s/8s/10s+ respectively (tougher tiers survive longer).
- Sun burn: no burn at night, indoors, or under cover.
- Fall damage: take a tamed wolf/shepherd up a mountain and walk off a ledge → 0 damage.
- Friendly fire: throw a grenade / melee splash near the owned pet → record pass/fail as decided in Task 5.
- AI: order the pet to Follow; confirm it stays close. Confirm it engages zombies (not the owner or random stags), and that when seriously hurt (fear ~80+) it backs off then returns after the cooldown.

- [ ] **Step 2: Record outcomes** — add a short `## Playtest` section to the plan file with the pass/fail of each bullet above, commit it.

```powershell
git add "docs/superpowers/plans/2026-09-07-vampire-burn-companion-ai.md"
git commit -m "docs: record playtest results for sun burn and companion AI"
```

---

## Self-Review Notes

- **No placeholders:** every XML block is written out in full above; validation commands are exact.
- **Spec coverage:** sun burn (Task 1), fall damage (Task 2), follow-first + targeting (Task 3), danger avoidance (Task 4), friendly-fire gap (Task 5), sync/regression (Task 6), acceptance (Task 7). Note: teleport-to-owner / stuck-path constants are compiled DLL values — outside pure-XML scope, documented limitation in the spec.
- **Type consistency:** property names (`CompanionProfile`, `AITask-N`, `HealthChangeOT`, `FallDamageReduction`, `EntityTagCompare`) match the spec and verified game v3.2.0 names.
- **Safe scope:** the scoped xpaths (`contains(@value, 'FollowOwner')`) guarantee we never touch passive livestock or the LBD-owned XUi windows.