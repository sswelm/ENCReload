# What HAF brings to ENCReload

> **Status: HAF integration is planned for ENCReload 2.0.** Everything below is configured and running in
> development; the mod as shipped does not depend on it yet.

[HAF](https://sswelm.github.io/HumankindAssetFramework/) is a visual-enhancement layer for Humankind. ENCReload adds
the *content* — technologies, units, districts, wonders, mechanics; HAF changes what that content **looks like on
the map**.
Vanilla modding stops at reskinning the game's existing (largely human) units. HAF replaces a unit's actual 3D
model, animates it, gives it sound, and makes it behave believably as it moves and fights.

This page is the inventory of what we have actually configured — not what HAF *could* do.

---

## Effects that apply to whole unit types

These are global dials, so they change **every** unit of that class at once, including vanilla ones ENC never
touches.

| Effect | Setting | What you see |
|---|---|---|
| **Gradual turning** | land **180°/s**, turret **90°/s**, hover **180°/s**, ships **90°/s**, humans **off** | Units swing round to their heading instead of snapping. Ships turn ponderously; tanks briskly. |
| **Pivot in place** | `pivot=90` | A ground or naval unit facing more than 90° away **turns on the spot first, then drives off** — no more tanks sliding sideways across the hex grid. |
| **Banking** | hover **6°**, ships **3°** | Helicopters lean into a turn; ships heel. |
| **Terrain hugging** | drop **−2**, lookahead **1.5**, ease **4** | Units sit *on* the ground and follow slopes rather than floating flat over them. Skipped on Exploitations and Ruins. |
| **Battle turning** | `hold=1` | In deployed battles, a unit finishes turning toward its target before the attack fires. |

Formation overrides reshape how a unit's pawns are laid out: **Turtle-9**, **Close-9**, **Close-5** and
**Scatter-Spaced-5** are re-authored globally, plus per-unit layouts for **Biplanes**, **Biremes** (single pawn),
the **Dugout Canoe** (a 3-pawn wedge) and the **Anti-Tank IFV** (which always fully turns before moving).

---

## Units with a custom model

Twenty-two units currently carry HAF configuration. Grouped by what is most noticeable about them:

### Artillery — the deepest work

| Unit | What you see |
|---|---|
| **Towed Gun Howitzer** (Era 6) · **Siege Howitzer Car** (Era 5) | A full M114 firing cycle: rolls with **turning wheels**, **folds its trails and lowers the gun** before it moves, **spreads them and raises the barrel** on arrival. Aiming at a target it **elevates further the further away that target is** (10° up to 45° across a 1–8 tile band), waits until it is properly laid, then **recoils** — the tube kicking back through its cradle and riding home. |
| **Organ Gun** (Era 4) · **Volley Gun** (Era 5) | Custom models with rolling wheels, ground-seated, with their own firing sound. |

### Vehicles

| Unit | What you see |
|---|---|
| **Universal Tanks** (Era 6) | Custom tank, tracks and wheels driven from its own animation. |
| **Tank Destroyers** (Era 6) | Custom model, aiming gun with **distance-based barrel elevation**. |
| **Anti-Tank IFV** (Era 6) | Custom model with a **traversing turret** that tracks its target; always turns fully before moving. |
| **Armoured Car** (Era 5) | Turret, **wheels that spin while moving and stand still when parked**, engine start/stop sounds. |
| **Hovercraft** (Era 6) | Custom model with engine sounds. |

### Aircraft

| Unit | What you see |
|---|---|
| **Attack Helicopter** · **Recon Helicopter** (Era 6) | Custom airframes with engine start/stop sound. |
| **Stealth Helicopter** (Era 6) | Flies on its **donor's own flight animation** — hover bob and pitch — **banks into turns**, hugs terrain, with its extra ghost sub-pawns and stray VFX suppressed. |
| **Recon Drone** (Era 6) | Quadcopter with **its own spinning propellers**, plus flight sound. |
| **Drone Squad FPV** (Era 6) | Squad model carrying a **hand prop** weapon. |
| **Zeppelin** (Era 5) · **Recon Zeppelin** (Era 6) | Airships held rigid so they do not inherit the donor's wobble. |

### Naval

| Unit | What you see |
|---|---|
| **Dugout Canoe** (Era 1) | Paddling animation, a 3-pawn wedge formation, and **per-pawn phase offset** so the canoes do not paddle in lockstep like one raft. |
| **Hand-Cranked Submarines** (Era 5) | Custom model that **sits lower in the water in combat**. |
| **Stealth Cruiser** · **Stealth Corvettes** (Era 6) | Custom/retextured hulls with engine sound. |

### Creatures and infantry

| Unit | What you see |
|---|---|
| **Abominations** (Era 6) | Custom creature with idle, movement and attack animations, an **occasional idle growl** and a distinct attack roar. |
| **Light Assault Mech** (Era 6) | Custom mech, retextured and tinted, with its own movement sound. |

---

## Buildings on the map

Units are not the whole of it — HAF also replaces what a **constructible** looks like on its tile, and keeps that
same model visible when you zoom out to the strategic map instead of substituting a generic decal.

| Constructible | What you see |
|---|---|
| **The Oracle** (Era 1 Artificial Wonder) | A custom Greek temple standing on the wonder's tile, on Mediterranean prairie ground with its own hex sculpt. It runs through the game's **native Artificial Wonder pipeline** — its own row in the wonder visual database, native affinity, card portraits, and the vanilla **bottom-to-roof level-build reveal** when a save reloads. Zoomed out it shows **the temple itself** as its strategic footprint, flattened to a sheet and in black-and-white, with the generic decal suppressed. |
| **Breeder Reactor** (Base district) | A custom reactor building on temperate ground, with the same strategic-map treatment — its own silhouette as the footprint rather than a decal. |

Both are **isolated**, meaning the swap touches only their own tiles and leaves every other district in the city
alone. The Oracle in particular is the deepest single piece of HAF work: getting a player-authored wonder onto the
engine's own wonder chain took a decompile of that chain
([Wonder-Spike](https://sswelm.github.io/HumankindAssetFramework/Wonder-Spike.html)).

---

## The kinds of effect in play

Drawn from the configuration above, these are the levers ENC actually uses:

- **Custom 3D models** — all 22 units, sized, rotated and seated on the ground.
- **State-driven animation** — a different clip for idle, moving, arriving, departing and attacking.
- **Turning, banking and terrain-following** — applied per unit *type*, so the whole battlefield moves better.
- **Guns** — traversing turrets, muzzle flash anchored to the right bone, distance-proportional elevation, recoil.
- **Sound** — engine start/stop, movement loops, idle flavour, attack and death.
- **Formations** — pawn counts and layouts per unit.
- **Donor control** — hiding borrowed sub-pawns, silencing borrowed VFX and audio, freezing borrowed animation.
- **Buildings** — a custom model on a district or wonder tile, and that same model as its strategic-map footprint.

For the full list of what HAF can do beyond what ENC uses, see
[HAF's Capabilities page](https://sswelm.github.io/HumankindAssetFramework/Capabilities.html). For how to configure
any of it, the [Factory Manual](https://sswelm.github.io/HumankindAssetFramework/Factory-Manual.html); when
something looks wrong,
[Animation-Pitfalls](https://sswelm.github.io/HumankindAssetFramework/Animation-Pitfalls.html).
