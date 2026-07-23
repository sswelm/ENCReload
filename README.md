# ENCReload

**ENCReload** is a comprehensive gameplay overhaul for **Humankind** that expands the game with hundreds of historical
technologies, units, districts, and mechanics while preserving the feel of the original game.

Originally focused on naval warfare, ENCReload has grown into a complete historical expansion that extends every
era — from the Ancient World to the near future — with new strategic choices, deeper military progression, and a more
authentic technological timeline.

Unlike many overhaul mods, ENCReload emphasizes **historical continuity** rather than simply adding more content.
Technologies unlock logical military and civilian developments, strategic resources remain relevant throughout the
game, and new unit classes fill historical gaps instead of replacing existing ones.

---

## Features

### Extended Technology Tree

ENCReload significantly expands the vanilla technology tree with dozens of additional technologies.

Examples include:

- Early Gunpowder Warfare
- Naval Expeditions
- Military Reforms
- Ballistics
- Cellular Networks
- Drone Technology
- Nanotechnology

The additional technologies create a smoother historical progression and allow important inventions to appear when
they actually became influential.

### Hundreds of New Units

ENCReload introduces an extensive roster of new units covering every era.

Examples include:

- Great Galley
- Fire Ship
- Bomb Ketch
- Steam Frigate
- Guided Missile Destroyer
- Stealth Missile Cruiser
- Recon Zeppelin
- Recon Helicopter
- Recon Drone
- Cruiser Submarine
- FPV Drone Team
- Expeditionary Infantry
- Augmented Infantry

Many new unit classes introduce entirely new tactical roles instead of simply increasing combat strength.

### Expanded Naval Warfare

Naval gameplay has been redesigned from the ground up.

The mod adds entirely new ship classes, smoother upgrade paths, and more specialized fleets.

Examples include:

- Light coastal vessels
- Ocean-going escorts
- Capital ships
- Commerce raiders
- Cruiser submarines
- Missile warships
- Stealth vessels

Every era now offers meaningful naval choices.

### Improved Historical Progression

Rather than compressing centuries of military development into a handful of technologies, ENCReload introduces
intermediate technologies and units to better represent historical evolution.

Examples include:

- Gradual development of firearms
- Specialized artillery
- Naval logistics
- Industrialization
- Electronic warfare
- Drone warfare

### Strategic Resources Matter

Strategic resources are no longer simple build requirements.

Resources such as **Iron, Coal, Oil, Uranium, and Horses** become long-term strategic assets that influence military
production and support throughout the game.

Managing your economy is just as important as winning battles.

### New Gameplay Mechanics

ENCReload introduces numerous new mechanics, including:

- Enhanced naval combat
- Expanded reconnaissance
- Stealth detection
- Improved bombardment
- New unit abilities
- Additional strategic resources
- Redesigned military progression

Many existing mechanics have also been rebalanced to create more interesting strategic decisions.

---

## Powered by Humankind Asset Framework (HAF)

ENCReload is the reference implementation of the
**[Humankind Asset Framework (HAF)](https://github.com/sswelm/HumankindAssetFramework)**.

HAF enables features that were previously impossible in Humankind modding, including:

- Custom 3D unit models
- Animated characters (state-driven idle / run / combat stance / attack fire)
- Custom districts
- Custom weapons and props (down to a rifle in a soldier's hands)
- Custom projectiles
- Runtime textures
- Custom sounds (engine, movement, creature voices: idle growl, attack roar, death rattle, battle war cry)
- Multi-mod asset packs

While ENCReload is fully playable on its own, it also serves as a showcase of what HAF makes possible for the
Humankind modding community.

---

## Design Philosophy

ENCReload follows several core principles:

- Expand rather than replace vanilla gameplay.
- Preserve historical authenticity where practical.
- Give every era meaningful military and technological choices.
- Reduce abrupt technological jumps.
- Encourage strategic planning over simple unit upgrades.
- Introduce new mechanics only when they add interesting gameplay.

---

## Current Status

ENCReload is under active development and continues to receive major updates.

Recent development includes:

- Humankind Asset Framework integration
- Custom animated units
- Custom district models
- Custom hand-held weapon props
- Projectile replacement system
- Expanded naval roster
- New technology branches
- Additional historical unit lines

---

## Requirements

- Humankind
- BepInEx
- Humankind Asset Framework (included)

---

## This repository — the authoring side (for modders)

This repo holds the **Unity project** that authors ENCReload's content: the game databases (tech tree, units,
mechanics) and the **HAF Authoring Tools** — the editor windows (Model Factory, Animation Lab, District Factory,
Prop Lab, Projectile Lab, Unit Retexture / Sound) that turn an ordinary 3D model into a working in-game asset with
no per-model code. The runtime half lives in
**[HumankindAssetFramework](https://github.com/sswelm/HumankindAssetFramework)**.

**➡ Full technical documentation: [AUTHORING.md](AUTHORING.md)** — the bake pipeline, every authoring window, the
registry contract, and the technology stack.

---

## Credits

Created by **FreeThinker (sswelm)**.

Special thanks to the Humankind modding community and the creators who have released 3D assets under open licenses.
