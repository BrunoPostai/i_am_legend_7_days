# Changelog

All notable changes to the I Am Legend 3.0 modpack.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
versions refer to the `I Am Legend` mod's `ModInfo.xml` version (3.1.0.0).

---

## [3.1.x] — 2026-09-07 — Modpack overhaul

### Added

- **Vampire sun burn (doom-timer).** Dark Seekers now burn in direct sunlight. Burn
  damage is per-tier: base (10/tick), feral (17.5), radiated (25), charged (27.5),
  and infernal (30), each tick at `update_rate 0.25`. Tougher variants have bigger
  HP pools, so they survive a little longer — but none can out-last the sun.
  - The burn uses tag-gated effect groups (`feral` / `radiated` / `charged` /
    `infernal`); the feral gate is made exclusive so higher tiers do not stack the
    feral damage on top of their own.
- **Companion friendly-fire fix** (`Mods/Z_CompanionFriendlyFire`). A small C#
  Harmony mod loaded last that makes tamed companions immune to damage from their
  owner or the owner's Steam friends (punches, melee, gunfire, splash). Zombies and
  other players can still harm the pet.
  - Also suppresses the "You hurt {pet}!" warning and the owner-damage bond penalty
    that previously fired when an owner struck their own tamed animal.
  - Built from source with a bundled `build.ps1` (no SDK required — uses .NET
    Framework `csc.exe` against the game assemblies).
- **Companion zero fall damage.** Tamed companions no longer take fall damage in
  mountains or off ledges. Applied via a `FallDamageReduction base_set 0` effect
  group on every `CompanionProfile`/`AnimalTamingProfile`-bearing entity class
  (includes `animalBearSmall`).
- **Companion follow-first AI.** Predator companions (wolf, dire wolf, bear, zombie
  bear, mountain lion) now run `FollowOwner` at high priority and engage
  zombie-first targets (`class=EntityZombie,50,EntityBandit,30`), no longer chasing
  random stags or other players.
- **Companion danger avoidance.** Predator companions gain `CompanionTemperament`
  and `CompanionFearResponse = Defensive`, fleeing only at `CompanionFleeFearThreshold
  80` for 4s with a 10s cooldown.
- **Specs and plans.** Design docs and implementation plans under
  `docs/superpowers/` for the sun burn + companion AI, and the friendly-fire fix.

### Changed

- **Skills: HybridLearnByUse replaced by IzPLearnByDoing (LBD).** The `80_LBD
  Returns Core 3.0` mod is now the skill/progression system. This also fixed the
  talents window (`windowSkillList` / `windowSkillPerkInfo`) throwing a
  `NullReferenceException` on open (N key), which was caused by two mods both
  rebuilding those windows.
- Balanced/consistency fixes from the round-1 review pass across the modpack.

### Fixed

- **Talents window crash (N key)** — `NullReferenceException: Object reference not set
  to an instance of an object` on opening skills. Caused by HybridLearnByUse and
  the LBD mod both redefining `windowSkillList`/`windowSkillPerkInfo`; removing
  HybridLearnByUse resolved it.
- **Review round 1 (misc XML corrections):**
  - `I Am Legend`: quest localization keys pointed at missing entries
    (`IAmLegendTip02` → `IAmLegendTip`); `zombieDarkseekerSlimFemaleCharged`
    `PreviousTier` pointed at a vanilla class; main-menu `windows.xml` referenced a
    missing `Video/` folder (disabled); a stray `//` text node in `physicsbodies.xml`.
  - `HybridLearnByUse` (applied then replaced by LBD): two perks
    (`perkDemolitionsExpert`, `perkHiddenStrike`) were re-added without being
    removed, stacking vanilla + halved effects; several `CVarCompare` reads used an
    invalid `@`-prefixed cvar name. (These fixes shipped with the version that still
    contained HLBU; the mod has since been replaced by LBD.)
  - `CrystalHellHusbandry`: wolf AITasks were appended without removing vanilla ones;
    `companionRevivalBandage` had no `craft_time`; a `DescriptionKey` pointed at a
    missing localization key; several items lacked localization entries.
  - `0-SandboxPresetStateFix` folder renamed to `0a-SandboxPresetStateFix` so the
    Harmony wrapper (which it depends on) loads first.
  - Docs: `Configs.txt` → `Config.txt` references; `.gitignore` `**/.zip` → `*.zip`.
  - Typos in scripts/strings (`recieved` → `received`, etc.)

### Doc

- README reformulated: added **THE SUN** and **SKILLS & PROGRESSION** sections,
  rewrote **THE COMPANION** to describe follow-first AI, zombie-first targeting,
  danger avoidance, fall-damage immunity, and friendly-fire immunity, and documented
  the bundled mods that must ship with the pack.

---

## [3.1.0.0] — Initial

### Added

- Vampire-Fantasy "I Am Legend" experience: Dark Seekers only spawn at night,
  buildings are infested, no trading, 5× night spawn density, hybrid/progressive
  skills, German Shepherd companion, custom sandbox preset, custom Darkseeker
  models (normal/feral/radiated/charged/infernal + hunter/alpha/spitter/wight
  variants).

### Credits

- IzPrebuilt (IzPLearnByDoing/LBD), Shavick (CrystalHellHusbandry/Companions),
  WookieNookie (entity level inspiration), HellsJanitor (pack author).