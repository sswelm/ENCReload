# HAF effects — what you can put on an ENC unit

> **Status: HAF integration is planned for ENCReload 2.0.** This page is the reference for what will be available,
> written as the units are prototyped. Individual units are already running on HAF during development (the M114
> towed howitzer is the worked example), but the mod as shipped does not yet depend on it.

Every visual and behavioural effect [HAF](https://github.com/sswelm/HumankindAssetFramework) can apply to an ENC
unit, and where you set it. This is the *catalogue*; for how the pipeline works see
[HAF's Factory Manual](https://sswelm.github.io/HumankindAssetFramework/Factory-Manual.html), and when something
looks wrong go straight to
[Animation-Pitfalls](https://sswelm.github.io/HumankindAssetFramework/Animation-Pitfalls.html) — the odds are it is
on that page.

## How to read the "Apply" column

| Apply | What it costs you |
|---|---|
| **Bake** | Blender re-runs and new assets are written. Then rebuild the mod and relaunch. |
| **Save** | Registry only — no Blender, no new assets. Rebuild the mod and relaunch. |
| **Live** | A dial file under `BepInEx/config/`, polled about once a second. Edit while the game runs. |

A unit is one **registry entry**: a `resourceName` (the key) and a `pawnDescription` (the game unit it replaces).
The pawn description is a **substring** match and the **longest match wins**, so `Era5_Common_SiegeHowitzersCar_01`
beats a shorter `Era5_Common_SiegeHowitzers` entry on the same unit.

---

## The model itself

| Effect | Field / where | Apply |
|---|---|---|
| Replace a unit's 3D model | `modelFile` — Model Factory | Bake |
| Size, rotation, position offset (Z = waterline) | Model Factory ▸ Transform | Bake |
| Runtime scale multiplier (no re-bake) | `scale` | Save |
| Brightness / desaturate / RGB tint | Model Factory ▸ shading | Bake |
| Replace the texture | `textureFile` · Unit Retexture | Bake |
| Hide donor fragments that show through | `hideMeshes` | Save |
| Hide a donor's extra sub-pawns (the "GPU rotor" squadron) | `hideSubPawns` | Save |
| Temporarily show the ORIGINAL unit (A/B compare) | `disabled` | Save |

## Animation — the state machine

A **state-driven** entry plays a different clip per state. Set them in the **Animation Lab**; all are Bake.

| State | Field | Typical for a gun |
|---|---|---|
| Idle / reference *(defines the reference pose — must be real motion)* | `animClip` | `Spin` |
| Idle stance (override) | `animClipIdle` | `Deploy[20..20]` |
| Movement | `animClipMove` | `Spin` |
| After-movement (on arrival) | `animClipAfter` | `Deploy[0..20]` |
| Pre-movement (before it rolls) | `animClipPreMove` | `Deploy[20..0]` |
| Attack | `animClipAttack` | `Recoil` |
| Combat idle (locked in a battle) | `animClipCombat` | — |
| Occasional idle flavour one-shots | `animClipIdleAlt`, `animClipIdleAlt2` | — |

Supporting knobs: `attackRepeats` (replays per trigger), `idleAltInterval` (seconds between flavour clips),
`animPhaseSpread` (stops a multi-pawn unit animating in lockstep — **leave at 0.5**, 0 makes twelve canoes rock as
one raft).

> **Slice syntax:** `clip[a..b]` plays frames a→b, reversed if `a > b`; `clip[a..a]` holds one frame;
> `clip[a..b/n]` takes every nth frame (pacing is baked, never a runtime knob).

## Guns and artillery

| Effect | Field / where | Apply |
|---|---|---|
| Turret that tracks the target | `turretBone`, `turretAxis` | Save |
| Muzzle flash fires from your bone, not the donor's socket | `muzzleBone` | Save |
| Nudge the fire origin (flash + tracer start) | `muzzleOffset` "x,y,z" | Save |
| **Barrel elevates with target distance** | `gunElevMax` (deg, **positive raises**), `gunElevAxis` | Save |
| …ramp: raise / hold after firing / lower | `gunElevRise`, `gunElevHold`, `gunElevFall` | Save |
| Recoil — the barrel kicks back | Vehicle Lab ▸ *Recoil (fraction of tube)* | Bake |
| Deploy — split trails spread, gun raises | Vehicle Lab ▸ *Spread*, *Gun raise on deploy* | Bake |
| Clear the donor's streamed aim junk | `clearAimLayer` | Save |

Elevation ramps across a **1…8 tile** band — a point-blank shot commands none of the angle, an 8-tile shot all of
it. `gunElevRise > 0` also **delays the shot** until the gun is laid. Shipped defaults are **1 s up, 1 s hold, 1 s
down**, which reads as a crew working the gun.

> `clearAimLayer` and gun elevation both touch the same `BoneRotation` slots. If an elevation "applies" in the log
> but nothing moves, that is the collision — HAF now excludes the elevation's slot, but it is the first thing to
> suspect for any future slot-writing effect.

## Movement and bearing

| Effect | Field / where | Apply |
|---|---|---|
| Turn gradually into the heading instead of snapping | `turnRate` (deg/s), or per-category in `haf_turnease.txt` | Save / **Live** |
| Bank into the turn (aircraft, ships) | `turnBank`, `hoverbank`, `shipbank` | Save / **Live** |
| Pivot in place before moving off past N degrees | `pivot=` in `haf_turnease.txt` | **Live** |
| Nose-down pitch while moving | `moveTilt` | Save |
| Follow the terrain instead of floating flat | `hugDrop`, `hugLookahead`, `haf_hugterrain.txt` | Save / **Live** |
| Idle sway for floating units | Vehicle Lab ▸ *Wave rock* | Bake |
| Wheels/tracks/rotors that turn | Vehicle Lab ▸ *Spin* | Bake |
| Rotor trim (fine per-bone angle) | `haf_rotortrim.txt` | **Live** |

## Combat behaviour

| Effect | Field | Apply |
|---|---|---|
| Play the clip once when it attacks | `fireOnAttack` | Save |
| Hold a deployed pose when stopped, fold to travel | `deployOnStop`, `deployPoseTime`, `deploySpeed` | Save |
| Recoil pacing on the legacy deploy path | `recoilSpeed` | Save |
| Height offset in combat (a submarine dives) | `combatZ` | Save |

## Sound

Per-unit WAVs, all **Save**: `soundFile` (loop), `soundStartFile` / `soundStopFile` (engine start/stop),
`soundIdleFile` (+ `soundIdleInterval`, `soundIdleGroupRadius`), `soundAttackFile`, `soundDeathFile`,
`soundBattleFile` (war cry) — each with its own volume, and `soundAttackOffset` / `soundDeathOffset` /
`soundBattleOffset` to skip dead air at the head of a clip. `engineSound` fires the per-ship engine move sound;
`silenceDonorAudio` suppresses the borrowed donor's Wwise noise.

Author them in the **Sound Studio**; `haf_sound_catalog.txt` lists what the game exposes.

## Props and borrowed animation

| Effect | Field | Apply |
|---|---|---|
| Weapon glued to a bone (Prop Lab) | `handPropName`, `handPropBone`, `handPropAngles`, `handPropGuid`, `handPropMat` | Save |
| Fly the donor's own clip on your rig | `useDonorClip` | Save |
| Freeze the donor's idle bob on a static model | `freezeDonorAnim` | Save |
| Silence the donor's Mecanim VFX (the ghost rotor) | `silenceDonorVfx` | Save |
| Re-spawn pawns after load (borrowed-rotor race) | `respawnAfterLoad` | Save |

## Beyond single units

- **Formation Override** — how many pawns a unit fields, their spacing, and per-unit turn links (`turnPivot`).
- **District Factory** — a custom building per district, including its strategic-map footprint.
- **Projectiles**, **ground colours**, **unit retexture**, **Sound Studio** — each has its own window.

---

## Gotchas worth knowing before you start

1. **The registry is owned by the editor.** Never hand-edit `pack.json` while a Lab or the Factory has that entry
   open — its Save writes the stale in-memory copy back over your change.
2. **Two windows, one entry.** The Factory and the Animation Lab hold separate copies. After changing a field in
   one, **Reload** in the other before baking from it.
3. **Baked ≠ shipped.** A bake writes assets; the game still loads the previous ones until you rebuild the mod.
   *Tools ▸ HAF ▸ Ship Status* lists everything in that state.
4. **Previews lie.** The turntable plays *one clip in isolation*, so a gun that looks level there may be fine in
   game. In-game is the only truth.
5. **Read the log before theorising.** `BepInEx/LogOutput.log` carries `[Uni]`, `[Fire]`, `[Elev]`, `[BattleTurn]`
   and `[AnimDiag]` lines that usually name the cause outright — including the *order* things happened in, which is
   what catches timing bugs.
