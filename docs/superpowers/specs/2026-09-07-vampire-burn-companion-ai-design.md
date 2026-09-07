# I Am Legend — Vampire Sun Burn & Companion AI Design

Date: 2026-09-07
Status: Approved (design)
Apply to: `I-Am-Legend-3.0` modpack, branches off `main`

## 1. Overview

Two request features:

1. **Vampire sun burn** — Dark Seekers (vampires) are meant to burn in daylight. Today the sun burn only deals ~1 HP of damage (flat `0.5` per `0.25s` in `buffZombieSunBurn`). Goal: a **deadly doom-timer** burn so normal Dark Seekers die quickly in open sun, and **tougher tiers survive longer** (they have bigger HP pools, so they burn hot but can retreat to shade).
2. **Companion improvements** — the tamed companions (German Shepherd + all CHH tamable animals: wolf, boar, bear, mountain lion, stag, chicken, rabbit) should:
   - take **zero fall damage** (current: die in mountainous terrain),
   - **not take player AoE/splash friendly fire** (work in progress; see caveat),
   - act **smarter**: stay near the owner (follow priority), attack sensible targets, avoid standing in danger.

Constraint: pure XML/config changes. No new DLLs. We can read/tune the shipped DLL's surface properties, but cannot modify compiled behavior.

## 2. Verified mechanics (from game install)

- Sun burn trigger lives in `IAmLegendCore.dll` (applies `buffZombieSunBurn` when zombie is outside + daytime via `HasOpenSky`/`NightSkyExposure`). Not XML-tunable for *when* it fires, but the *damage* is XML: `Mods/I Am Legend/Config/buffs.xml:25-38`.
- Darkseeker tiers carry distinct tags in `entityclasses.xml` (verified): `feral`, `radiated`, `charged`, `infernal`. The game supports tag-restricted effect groups via `<requirement name="EntityTagCompare" tags="..."/>` (verified in vanilla `buffs.xml`).
- Per-tier burn uses flat `HealthChangeOT base_subtract`. 7DTD has no native %-of-max DoT; tier scaling is achieved by choosing flat DPS so burn survivability scales with each tier's HP pool.
- Fall damage: the game applies it through `_fallSpeed` cvar → `onSelfFallImpact` → `buffPlayerFallingDamage`. The passive effect **`FallDamageReduction`** exists and "takes the cvar `_fallSpeed` as the base value"; `base_set 0` cancels it (verified in vanilla buffs.xml doc comment at line 17133 and Assembly-CSharp enums).
- Friendly fire: vanilla `EntityAlive.FriendlyFireCheck` blocks owning-player damage (`LocPlayerIsOwner`/`targetIsOwner`/`FailedFriendlyFire` present). CHH already ships `EntityAlive_FriendlyFireCheck_TamedVsZombie_Patches` and sets `OwnerId`. Player-vs-pet splash may still slip through on certain damage paths inside the compiled DLL.

## 3. Feature A — Vampire sun burn (all-day doom timer)

File: `Mods/I Am Legend/Config/buffs.xml` — rework `buffZombieSunBurn`.

### 3.1 Structure

Keep `damage_type="heat"`, `stack_type="effect"`, `duration="0"`. Replace the single flat `HealthChangeOT 0.5` with tag-gated effect groups:

- Base group (no tier tag): all non-feral/radiated/charged/infernal darkseekers (normals, slims, crawlers, spiders, spitters w/o tier).
- Feral group: `EntityTagCompare tags="feral"`.
- Radiated group: `EntityTagCompare tags="radiated"`.
- Charged group: `EntityTagCompare tags="charged"`.
- Infernal group: `EntityTagCompare tags="infernal"`.

Each group carries `passive_effect name="HealthChangeOT" operation="base_subtract"` with a tier-specific DPS. The `update_rate` is chosen as a clean tick (e.g. `0.25`), and each value = DPS × update_rate.

### 3.2 Damage table (initial targets; refresh against actual tier HP during implementation)

Target feel: normal dies in ~5–6s in open sun; feral ~6s; radiated ~7s; charged ~8s; infernal ~10–12s (biggest pool, but still a hard timer). Values are per tick; example with `update_rate="0.25"` (4 ticks/s):

| Tier (tag) | Example MaxHP | Burn DPS | Per-tick value | Approx survival |
|---|---|---|---|---|
| base (no tag) | 200 | 40 | 10 | 5s |
| feral | 400 | 70 | 17.5 | ~6s |
| radiated | 700 | 100 | 25 | ~7s |
| charged | 900 | 110 | 27.5 | ~8s |
| infernal | 1100+ | 120 | 30 | >10s |

Final numbers are chosen during implementation after reading the `^cvar`-referenced HealthMax values in `entityclasses.xml` (note: those values are resolved by the DLL at runtime; fall back to a representative pool table if unreadable).

Keep the `buffIsOnFire` stacking trigger (`AddBuff buffIsOnFire`) but **verify interaction**:
- `buffIsOnFire` has an extinguish requirement group; ensure sun burn isn't accidentally removed by fire-extinguish logic.
- If it destabilizes the burn, drop the fire-stack and keep the sun burn pure.

Do **not** touch `buffNightBiomeZombie` / `buffBiomeZombieDawnDeath` (dawn purge is intended instant-kill).

### 3.3 Non-goals

- No new Config.txt key (hard-coded per-tier values, per user decision).
- The DLL's daytime/outdoors detection logic is unchanged.

## 4. Feature B — Companion improvements

Covers all CHH tamables + the I Am Legend German Shepherd.

### 4.1 Zero fall damage (`FallDamageReduction`)

Add to each companion entity class an `effect_group`:

```xml
<effect_group>
    <passive_effect name="FallDamageReduction" operation="base_set" value="0"/>
</effect_group>
```

Targets (in `CrystalHellHusbandry/Config/entityclasses.xml`) — every entity that declares a `CompanionProfile` (the tamable set): `animalWolf`, `animalDireWolf`, `animalBoar`, `animalBear`, `animalZombieBear`, `animalBearSmall`, `animalMountainLion`, `animalRabbit`, `animalChicken`, `animalStag`, `animalDoe`. (`animalZombieBoar` is not tamable — only `EvoEnabled=false` — so excluded.)
Target (in `I Am Legend/Config/entityclasses.xml`): `animalGermanShepherd`.
Where an entity is extended (e.g. German Shepherd extends `animalTemplateHostile`), prefer adding to the concrete companion classes so wild animals don't inherit the immunity.

### 4.2 Friendly fire (player AoE)

- Verify at runtime whether player-owner splash already respects `OwnerId`/`FriendlyFireCheck`.
- If it still hurts the pet: this is the one pure-XML gap (damage path lives in compiled DLL). Escalate to a small C# patch mod as a follow-up plan, or document as known limitation.
- Do not globally set animal→player faction to friendly (would break hunting/combat).

### 4.3 Smart AI (XML)

In each companion's entity class (`AITask` property list):

1. **Follow priority**: reorder so `FollowOwner, CHHusbandry` is high (before/between capture-avoid tasks), so the companion stays tight with the owner instead of wandering.
2. **Attack targeting**: tune `ApproachAndAttackTarget data="..."` so:
   - zombies are prioritized, bandits second,
   - the companion does not freely charge (lower priority) after random animals/players,
   - conservative targets for `AggressivePredator` profile are not globally overridden — only tuned per companion class.
3. **Danger avoidance**: set `CompanionTemperament`/`CompanionFearResponse` to the desired balance (e.g. stay Defensive for non-combat, Aggressive only while attacking owner target), and tune `CompanionFlee*`/fear thresholds so it backs out of zombie AoE.
4. **Wander when following**: ensure `FollowOwner` outranks `Wander`/`Look` in the list.

Known DLL limitation: teleport-to-owner and path-stuck timeouts are compiled constants (not XML properties). Document as "improved but not fully rewritable in pure XML" in release notes.

## 5. Testing checklist

- [ ] Sun burn: spawn normal/feral/radiated/charged/infernal darkseeker in open sun at midday → observe burn DPS and time-to-death per tier; verify tougher tiers survive longer.
- [ ] Sun burn: no burn indoors / at night / under cover (DLL detection).
- [ ] Fall damage: take companion up a mountain, walk off a ledge → 0 damage.
- [ ] Friendly fire: throw grenade / melee splash near owned companion → confirm whether it takes damage (document result).
- [ ] AI: order companion → Follow; confirm it stays within reasonable distance; confirm it engages zombies, not the owner or random passives; confirm run/no-input behavior.
- [ ] All 52 mod XMLs still parse (regression).

## 6. Out of scope / follow-ups

- Rewriting CHH follow-teleport constants (needs C# patch effort).
- If friendly-fire splash is confirmed broken in DLL: C# friendly-fire patch mod (separate plan).