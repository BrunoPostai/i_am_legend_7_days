# Companion Death: Corpse Persistence & Map Marker — Design

Date: 2026-09-07
Status: Approved (design)
Apply to: `I-Am-Legend-3.0` modpack, on `main`

## 1. Overview

When a tamed companion (the German Shepherd Samantha, or any CHH-tamed pet) dies, the
revival bandage can bring it back — but only while the corpse still exists. Today:

- The corpse is governed by vanilla `TimeStayAfterDeath` (inherited value `300` from
  `animalTemplateHostile`, ≈ 15s of real time at 20 ticks/sec). After it elapses, the
  body despawns and there is nothing left to revive.
- No map/compass marker marks the corpse, so it's hard to find quickly.

Goal: a dead companion's corpse persists effectively indefinitely, and a map + compass
marker shows where it is. The revival bandage remains the revival mechanism.

## 2. Verified mechanics (game v3.2.0 + CHH 0.6.6)

- `EntityAlive.timeStayAfterDeath` (System.Int32) is copied from the entity class
  `TimeStayAfterDeath` property (`CopyPropertiesFromEntityClass`).
- Vanilla ticks: 20/sec; 7DTD in-game day = 24000 ticks. Vanilla animals use 300
  (`animalTemplateHostile`).
- On death, CHH (`ModAPI.OnEntityKilled`) preserves the tamed `EntityInfo`
  ("died but tamed EntityInfo was preserved for revival") so revival is technically
  possible — it's purely the corpse despawn + discoverability that breaks the flow.
- CHH ships marker infrastructure (`CompanionTrackerService`, `UpsertClientMarker`,
  `NavObjectClass`, `NavObjectManager`) for *living* companions; nothing marks a corpse.
- Nav object classes available in vanilla `nav_objects.xml` include
  `animaltracking_wolf`, `quick_waypoint`, and `animaltracking_hostile` — suitable
  marker classes to repurpose for a corpse marker.
- `NavObjectManager` exposes `RegisterNavObject` / `UnRegisterNavObjectByPosition` /
  `UnRegisterNavObjectByClass` for placing and removing map/compass markers.

## 3. Architecture

Two small changes:

### 3.1 Corpse persistence — pure XML

Set `TimeStayAfterDeath` to a very large value on every tamable companion entity
class so the dead body remains revivable essentially indefinitely:

Target classes:
- `animalGermanShepherd` and `animalGermanShepherdCompanion` (I Am Legend,
  `Config/entityclasses.xml`)
- CHH tamables already targeted by the companion xpaths (`CompanionProfile` /
  `AnimalTamingProfile` classes: wolf, direWolf, bear, zombieBear, mountainLion,
  boar) via a scoped `<append xpath="//entity_class[...]">`.

Value: `720000` (= 36000s = 10h real time). Keeps the corpse "forever" for practical
play while still letting 7DTD's normal corpse lifecycle run its course if the player
abandons the area.

### 3.2 Corpse marker — extend the existing C# mod (`Z_CompanionFriendlyFire`)

Add a sixth Harmony patch set to the mod we already ship:

- **On companion death:** postfix/handler on CHH's `ModAPI.OnEntityKilled` (or the
  game `ModEvents`) — when the killed entity is a tamed CHH companion, register a
  `NavObject` at the corpse position using the `animaltracking_wolf` class (visible
  on map + compass).
- **On revive / despawn:** the marker is removed when the companion is revived
  (CHH `HandleCompanionRevived` path or a periodic validity check that the target
  corpse still exists), or when the corpse despawns.

Scope: only the *owner's* tamed companion gets a marker; wild animals get none.

## 4. Design decisions (YAGNI)

- No auto-respawn — bandage remains the revival mechanism (user decision).
- The marker is a simple persistent `NavObject`; no new UI, no HUD window.
- Corpse lifetime is a single XML value per class — no config file.
- Marker cleanup is best-effort periodic (validate target corpse still alive/dead-and-
  present); if revival already clears it, that's the primary path.

## 5. Testing checklist

- [ ] Kill a tamed companion → a `animaltracking_wolf` marker appears on map + compass.
- [ ] Dead body persists for an extended time (>= 10 min) without despawning.
- [ ] Revive with bandage → companion alive, marker gone.
- [ ] Kill a wild wolf/stag → no marker (only tamed get one).
- [ ] Fall/friendly-fire/needs fixes still work after the DLL update (regression).
- [ ] All mod XML parses; the new DLL loads with no Harmony errors.

## 6. Out of scope

- Auto-respawn / resurrection without the bandage.
- Custom marker art (reuse `animaltracking_wolf` icon).