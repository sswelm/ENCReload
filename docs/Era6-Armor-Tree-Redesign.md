# Era-6 Armor & Mechanized Tree — Redesign Plan

*A design plan, not an execution checklist. Captures the reasoning and decisions from the 2026-07
design session so it can be picked up later. Nothing here is implemented yet — the current tree is
unchanged except for the cavalry-mount work already shipped.*

---

## 1. The three organizing principles

Everything below follows from three rules the tree should obey:

1. **The triad.** The military tree marches in a repeating **Tank → Anti-tank → Infantry** rhythm.
   Consequence: every tank's successor is always **3 techs away**, with its counter and its infantry
   filling the two beats between. This is already visible in the earliest triad
   (Armored Warfare → Antitank Warfare → Mechanized Infantry Doctrine).
2. **The decade timeline.** In the Contemporary Era, **one tech column (`TechTreeX`, ~12 units apart)
   ≈ one decade.** A tech's column is *when* its unit belongs, not just how deep to research. Units
   must sit in their real-world decade. (Calibration point found: World Wide Web = 1991 sits at
   column X≈328, so 341≈2000, 353≈2010, 364≈2020, 383≈2030+.)
3. **Threat → counter.** Every unit has a counter, and the counter is a *different* kind of unit (not
   a mirror). This is the tree's balance DNA: Tank ↔ Anti-tank, Missile ↔ Active Protection,
   Drone ↔ Smart Munition.

---

## 2. Three lines = three survival philosophies

The single "tank line" was really **three** lines, each answering a different question. Splitting them
is the core structural insight.

| Line | Question it answers | Progression | Capstone |
|---|---|---|---|
| **Main battle** | *survive the hit* (mass + intercept) | Infantry → Medium → Universal → MBT | **Interceptor Tank** (APS) |
| **Light / recon** | *don't get hit* (stealth + laser) | Tankette → Light Tank → Armoured Recon → … | **Future Light Tank** (ex–"Stealth Tank") |
| **Carrier** | *keep the soldier alive* (protected delivery) | APC → Amphibious Transport → IMV → … | **Future APC** |

Key realization: **the Stealth Tank was never a real continuation of the *tank* concept** — stealth is
a different survival philosophy, and the PL-01 is literally a *light* tank (~35 t, CV90-based). So it
becomes the **Future Light Tank**, capstone of the light/recon line, and the main line ends cleanly at
the **Interceptor Tank** (peak of the armor/protection ladder).

### The armor/protection ladder (runs under the main line)
`Sloped Armor → Composite Armor → Reactive Armor → Active Protection Systems`
— each a better answer to *how do I survive being hit*. Reactive armor is "just a patch" (bolt-on ERA,
a spacer/prereq to push APS back); APS is the paradigm shift (defeats the incoming round entirely), so
it carries the heavy defensive bonus.

---

## 3. The main-line 14-step chain (data-verified)

Steps 1–4 are the Era-5 lead-in (**already correctly wired in the data**). Steps 5–14 are the Era-6
triads. Legend: ✅ correct · ✏️ rename display · ➡️ move unit's unlock · 🆕 new · 🎨 re-theme.

| # | Tech (display) | Unit | State |
|---|---|---|---|
| 1 | Combustion Engine | Tankette | ⚠️ no `CombustionEngine` tech exists; `Tankettes` on Automobile+Tank Warfare — decide entry point |
| 2 | Tank Warfare | Infantry Tank | ✅ `InfantryTanks` ← `Era5_TankWarfare` |
| 3 | Anti Tank Gun | AT Halftrack + Mech. Infantry | ✅ both ← `Era5_AntiTankGun` |
| 4 | Modern Highway | Towed Gun Howitzer | ✅ `TowedGunHowitzers` ← `Era5_ModernHighway` |
| 5 | **Sloped Armor** | Medium Tank | ✏️ `SlopedArmor` "Reactive Armor"→"Sloped Armor" · ➡️ `MediumTanks` (from Era6_05) · reposition early · 🎁 relocate its bonuses→APS |
| 6 | Anti Tank Warfare | Tank Destroyer | ✅ |
| 7 | **Mechanized Operations** | APC | 🆕 new tech (rename a placeholder) · ➡️ `ArmouredPersonnelCarriers` (from MechInfDoctrine) |
| 8 | Armoured Warfare (`Era6_05`) | Universal Tank | 🆕 clone Universal Tank from MBT → assign to Era6_05 |
| 9 | **Wire Guided Missile** | M901 ITV | ✏️ `WireGuidedMissile` "Transistor"→"Wire Guided Missile" · 🆕 M901 ITV vehicle |
| 10 | Mechanized Infantry Doctrine | Amphibious Transport | 🆕 clone Amphibious Transport |
| 11 | Composite Armor (`Era6_23`) | Main Battle Tank | ✅ |
| 12 | Armoured Combat Support (`SelfPropelledArtillery`) | Infantry Fighting Vehicle | 🎨 re-theme `AntiTankIFV`→"IFV" |
| 13 | **Protected Mobility** (`DartAmmunition`) | Infantry Mobility Vehicle + Mobile Artillery | ✏️ rename→"Protected Mobility" · 🆕 IMV (mine-resistant, Casspir-style) · Mobile Artillery already here |
| 14 | Active Protection Systems | Interceptor Tank | 🆕 clone Interceptor Tank · 🎁 receives Sloped Armor's relocated bonuses |

**Actual workload:** ~3 display renames, 1 new tech (Mechanized Operations), 2 unit moves, 5 clones
(Universal Tank, M901 ITV, Amphibious Transport, IMV, Interceptor Tank), 1 bonus relocation, plus
prereq wiring to keep the chain linear.

### Naming notes (internal ≠ display drift to be aware of)
- `Era6_23` displays "Composite Armor" via a loc key (`%Technology_Era6_23Title`).
- `WireGuidedMissile` currently displays "Transistor" (wrong — rename).
- `SlopedArmor` currently displays "Reactive Armor" (it's vacating that name to become "Sloped Armor";
  the "Reactive Armor" name then goes to a *fresh* placeholder — see §5).

---

## 4. Unit identities / real-world anchors (for modelling & flavor)

- **Universal Tank** = the Centurion — the post-WWII "universal tank" that merged the cruiser/medium
  and infantry-tank doctrines; the conceptual bridge to the MBT. (Clone from MBT, dial stats down a notch.)
- **M901 ITV** = TOW carrier (the *vehicle* form of Wire-Guided-Missile; TOW-Infantry is the dismount).
- **Amphibious Transport** = wheeled amphibious APC, LAV / Stryker / TPz-Fuchs family. "LAV" is the
  era-correct ('80s) generic; "Stryker" is the 2000s descendant.
- **Infantry Mobility Vehicle (IMV)** = mine-resistant carrier. Anchor to the **Casspir/Buffel**
  (South African, ~1980) — the *origin* of the MRAP concept, so it's authentically '80s. Trait:
  mine/trap immunity.
- **Interceptor Tank** = APS-equipped MBT (Trophy/Arena/Afghanit, ~2011). Kinetic active defense.

---

## 5. The future / endgame cluster (the big design payoff)

The late tree resolves into a self-justifying web where every tech answers the threat the next one
creates.

### Two future capstones (same tech, opposite doctrine)
| | **Future Light Tank** | **Future APC** |
|---|---|---|
| Survives by | *absence* — optical cloak + laser point-defense | *endurance* — heavy armor + layered active defense |
| Kills incoming with | directed-energy laser (missiles **and** drones) | same defensive suite, but pointed at protecting the squad |
| Role | glass-cannon scout/flanker | mobile bunker — deliver living dismounts |
| Weakness | short-range swarm saturation (stealth fails up close) | saturation too, but soaks more |

- The **Future Light Tank** uses *light both ways*: cloak to vanish, high-power laser to shoot down
  incoming ATGMs **and drones**. This is what finally gives **Directed Energy Weapons → Military Laser**
  a real home (they power the laser) instead of drifting toward the naval branch.
- The **Future APC** deliberately does **not** rely on stealth — an APC full of dismounts can't gamble
  lives on invisibility, and stealth fails at short range against sophisticated swarms. It endures
  instead. Carrier-line through-line: *keep the soldier alive*, re-armored against each era's killer
  (small arms → mines/IEDs → drones/ATGMs).

### Reactive Armor spacer (fixes a pacing bug)
Relocating `SlopedArmor` to be the early Medium-Tank node **removes the spacer it was providing between
Protected Mobility and APS** (currently `…Protected Mobility → SlopedArmor → APS`). Reinstate a **fresh
"Reactive Armor" node** (rename a spare `Era6_##`) in that gap — light "patch" tech, token defensive
bonus, no headline unit. Restores the distance and finally homes the "reactive armor is just a patch"
concept. (Trade-off: bends "tanks 3 apart" by one step at the capstone — acceptable at the endgame.)

### Chronology pass (units in their real decades)
By the decade calibration, **APS (Trophy, 2011) is ~2 decades too early** — it's parked at column ~328
(1990s, next to WWW/Fuzzy Logic). Correct the late cluster rightward:
- Move **APS/Interceptor** right into the ~2010 column (353).
- Push **Stealth/Future Light Tank** further into the future (~2020 column, 364).
- Moving **Directed Energy Weapons** (341) and **Military Laser** (`Era6_29`, 364) frees exactly those
  columns. *Dependencies to re-point when they move:* `ActiveCamouflage` prereq → `ActiveProtectionSystems`;
  `FutureTechnology` prereq (off `Era6_29`) → something else.
- Open question: do DEW + Military Laser go **naval** (extend Stealth Ship — thematically apt for
  shipboard lasers) or stay **land** feeding the Future Light Tank's laser? Leaning **land**, since the
  laser capstone gives them a clearer home.

---

## 6. Drones & the counter — balance solved

The modern threat/counter pair that extends the tree's DNA into the drone age:

- **Drone** = *a miniature airplane that covers long distance economically.* Cheap, long-range, flexible,
  slow, interceptable. Owns the **strategic** initiative (comes from anywhere, in numbers).
  Currently the unit `DroneSquadFPV` is **orphaned (no tech unlocks it)** — it's unobtainable in-game.
  Give it an attack-drone tech (**Loitering Munitions / Attack Drones / Kamikaze Drones** — *not*
  "Smart Munitions", see below).
- **Smart Munition** = *a high-speed self-steering projectile that intercepts moving targets.* A
  missile-shell hybrid (Excalibur/Copperhead lineage); guided enough to hit a small evasive drone, fast
  enough to catch it, cheap enough to be economical, and **not itself a drone** — so it's the *real*
  counter without collapsing into a drone-vs-drone arms race. **Short range** = point defense (protects
  a bubble, not the map), which is exactly what keeps *both* the drone and its counter honest.

**Why Smart Munition is THE counter** (each alternative has a fatal gap): drone-vs-drone = arms race;
dumb flak = misses the jinking target; SAM = hits but ruins the cost exchange; **smart munition = hits,
affordable, non-drone.**

**Escalation ladder (rock-paper-scissors, all realistic):**
1. Radio FPV drones → beaten by **Electronic Warfare** (jam the link) — *secondary counter, not yet a
   unit; would slot in the signals branch (CellularNetwork/Ethernet).*
2. **Autonomous "smart" attack drones** → beat EW (no link to jam).
3. **Smart Munitions** (guided interceptor) + laser/flak → hard-kill within range, saturation-limited.

**Naming resolution:** "**Smart Munitions**" = the guided-shell **counter** (defensive/precision). The
FPV **attack** drone takes a drone name (Loitering/Attack/Kamikaze). Threat and counter get different
concepts *and* different words.

---

## 7. Existing counter-drone assets (data)
- `AntiAirDefence` (SAM-type) ← `GuidedMissiles`. Keep for *big* drones/aircraft (bad economics vs cheap FPVs).
- `AntiAirGuns` exists — the natural home for a smart/airburst counter-UAS round (the Gepard lesson).
- **No Electronic Warfare / jammer unit exists** — the missing secondary counter.

---

## 8. Open decisions (resolve before building)
1. **Step 1 entry point** — which existing early tech represents "Combustion Engine" (Automobile?), or
   leave the Tankette where it is.
2. **Era6_05 fallout** — when Medium Tank leaves for Sloped Armor and Universal Tank arrives, do the
   recon/light units (RedArmyTanks, ArmouredRecon, LightTanks) stay on Armoured Warfare or move?
3. **SP-Artillery double** (steps 10 & 13) — single (Protected Mobility only) or intentional double-unlock?
4. **DEW + Military Laser** — naval branch, or stay land feeding the Future Light Tank laser?
5. **Cadence vs spacer** — accept the one-step cadence bend at the capstone for the Reactive Armor spacer?
6. Which future vehicle to build first (Future Light Tank vs Future APC), and whether the Future APC is
   a distinct unit or a variant.

---

## 9. Execution note
When this gets built: **one triad/beat at a time** — make the change, export, load a save, read the
newest `Diagnostics (…).html` (the real error lives there, *not* in the generic "Mismatched mods"
dialog), commit the verified beat, move on. That's the rhythm that turned the July tech-reorg into
clean verified commits. Watch the **unlock-event family rule**: every unit inside one
`UnlockConstructible` event must share a `SerializableFamily`, or activation fails — give a unit its own
event when in doubt.
