# ENCReload — Authoring Guide (the Model Factory & Labs)

The Unity-editor side of the **ENCReload** mod for [Humankind](https://www.games2gether.com/amplitude-studios/humankind).
It's a **Model Factory**: an editor tool that turns an ordinary 3D model (a `.glb`, `.fbx`, `.obj`, or `.blend`)
into a fully-working custom **unit skin** in-game — mesh, texture atlas, skeleton, and (optionally) its own animation clip —
with **no per-model code**. Point it at a model, set a few knobs, click **Bake**, rebuild the mod, and the unit shows up
in-game wearing your model.

It's the bake-time half of a two-part system:

| Repo | Runtime | Role |
|---|---|---|
| **ENCReload** (this repo) | Unity Editor | The Factory: import → bake → write the registry. |
| **HumankindAssetFramework** | BepInEx plugin, in-game | Reads the registry and injects each baked model onto its pawn at runtime. |

The two talk only through a small JSON registry (`enc_models.json`), so the editor tooling and the runtime injector stay
fully decoupled. That registry is now a versioned **pack** (a `schemaVersion`/`modId` wrapper around the model list): the
runtime is a **Humankind Asset Framework** host that merges ENC's pack with any number of third-party packs, so other
modders can augment their own units by shipping their own config + assets — no ENC edits. See
[`HumankindAssetFramework/docs/Multi-Mod.md`](https://github.com/sswelm/HumankindAssetFramework/blob/master/docs/Multi-Mod.md).

## How a bake works

```
your model (.glb/.fbx/.obj/.blend)
        │
        ├─ glbconv / Blender (Tools/)      → OBJ+MTL / rigged FBX, UVs normalized, decimated
        │
        ▼
Model Factory (Assets/Scripts/Editor/)
        ├─ pack a texture atlas (single- or multi-material)
        ├─ bake a Skeleton (+ ClipCollection for animated models) via the Amplitude SDK
        └─ write the model's entry to enc_models.json
        │
        ▼
(rebuild the mod) → the in-game plugin injects the baked skin onto the target unit
```

Custom units share the game's own GPU-instanced pawn renderer, so **instances are free** — the cost is the number of
*distinct model types* loaded, not units on screen. See the vertex-budget notes in the docs.

## Runtime tools — reskin & sound (no bake)

More editor windows drive overrides and focused workflows alongside the Factory:

- **Tools ▸ HAF ▸ Animation Lab** — a model's **animation** in its own dialog (docks as a tab beside the Factory):
  clip + bone-filter pickers, fire-on-attack, deploy-on-stop + recoil sliders, and **Save (no bake)** for runtime
  flags. The Factory owns the model (file/transform/size), the Lab owns the animation — settings are mutually
  exclusive and **enforced at bake time** (each window rebases on the registry and writes only its own fields).
  Bake from either window; the pipeline is identical.
- **Tools ▸ HAF ▸ Unit Retexture** — reskin an existing unit: a hot-loaded PNG, or a live **Desaturate + RGB** adjust of
  its own atlas. Isolated per unit (the original stays as-is), and free on the vertex budget.
- **Tools ▸ HAF ▸ Unit Sound** — give a unit **movement audio**: the game's own engine event by name, or custom WAVs as
  **Start (spool-up) → Travel (loop) → Stop (spool-down)** with a per-clip volume and an in-editor **▶** preview.

These write fields onto the unit's registry entry; see `HumankindAssetFramework/docs/Factory-Manual.md` §12–15.
Animated-model niceties (2026-07-18): **rotation is baked into the rig** when set (`0,0,0` = untouched legacy path),
the Blender re-slim runs **automatically** when its inputs change, and the old "Reuse extracted" checkbox is now
purely **"Keep extracted texture (hand-edits)"**.

## Beyond units — districts & props

Units were the first injection axis; the same bake core now drives two more, each with its own window:

- **Tools ▸ HAF ▸ District Factory** — put a custom static model on a **district tile** (e.g. ENC's Breeder Reactor).
  Bakes model → bone-free FxMesh and writes an `enc_districts.json` registry entry the plugin reads (any number of
  districts, each optionally isolated to its own tiles). See
  [`District-Visuals.md`](https://github.com/sswelm/HumankindAssetFramework/blob/master/docs/District-Visuals.md).
- **Tools ▸ HAF ▸ Prop Lab** — give a pawn a custom **weapon/gear prop** on an attachment slot (Humankind's Slingers
  finally carry an actual sling). Dumps any vanilla fragment as an authoring template, then bakes model → FxMesh →
  MeshCollection + fragment assets; the plugin registers the collection at load. See
  [`Pawn-Props.md`](https://github.com/sswelm/HumankindAssetFramework/blob/master/docs/Pawn-Props.md).

## Cavalry & chariots need a mount (`PresentationMountDefinition`)

Every unit whose presentation definition has **`UnitSpecification: Cavalry` or `Chariot`** must have at least one
matching **mount** entry in `Assets/Databases/UnitPresentation/PresentationMountDefinition.asset`, or the game logs
`No Variation for this presentation unit definition <name>` on every spawn of that unit. A mount is a
`PresentationSecondaryPawnDefinition` (the horse/camel/chariot-team) that carries the mount mesh; the rider is a
separate pawn. When the mod **re-declares** a vanilla cavalry unit's presentation in an `_ENC` file, it drops the
vanilla mount linkage — so the mount must be re-added here. This is *the* cause of the long-standing cavalry log
spam.

**Recipe (per unit):**
1. In `PresentationMountDefinition.asset`, duplicate a **working, plain** mount — e.g. `Era3_Common_Knights_Mount_01`
   (Era 3 horse) or `Era5_Common_Dragoons_Mount_01` (Era 5 horse). Clone chariots from a vanilla chariot mount in
   `Assets/~References/`.
2. Change **only** the mount's `PresentationUnitDefinition` reference to the target unit's
   `PresentationLandUnit_…_Default`. The mount's *name* is cosmetic — the engine matches on that reference, not the
   name. Leave `Description` and `Attachements` alone (they carry the horse meshes that already resolve in the bundle).
3. Export the mod and launch; the unit's `No Variation` line is gone. Find the full to-do list by cross-referencing
   every `UnitSpecification: 1|2` presentation unit against the mounts already present.

**Traps (each cost a crash-and-reset — learned the hard way):**
- **One mount is enough** to clear the error (the engine check is `MountDefinitionsPerVisualAffinities.Length == 0`).
  The vanilla count of **5** is *visual variety* — five differently-skinned horses picked at random per spawn, not a
  requirement. Five identical-except-name clones render the same horse and buy nothing; do one per unit unless you
  actually vary each clone's `characterPalette`.
- **Clone plain mounts, not culture-affinity ones.** A mount cloned from a `…AztecEmpire`-affinity variant onto a
  different unit destabilizes the load (a `LoadAsset`/`AnimatorControllerCollection` crash → generic
  `Mismatched mods` dialog + config reset), even when its top-level references look identical to a working mount.
- **`AnimatorOverrideController: <None>` is normal** for mounts — they animate via their `AnimationCapabilityProfile`
  (Mount), not an override. A null there is not the bug.
- A load failure shows only the generic **"Mismatched mods"** dialog; the real error is in the newest
  `Diagnostics (…).html` under `<GameData>\Humankind\Temporary Files`, after `Loading runtime module 'encreload'`.
  The `No Variation` lines only appear for units that actually **spawned** that session — the log is not the full
  list, so enumerate from the data.

## Technology stack

| Layer | Technology |
|---|---|
| Editor tooling (this repo) | **Unity 2021.3.1f1** — the same engine version Humankind itself runs on — with C# editor scripts in `Assets/Scripts/Editor/` |
| Asset baking | The **official Amplitude (Humankind) modding SDK**, driven by the Factory to produce the game's native asset types: `Skeleton`, `ClipCollection`, mesh collections, and texture atlases |
| Runtime injection | **[HumankindAssetFramework](https://github.com/sswelm/HumankindAssetFramework)**: a **BepInEx 5.4** plugin in C# (targets .NET Framework 4.7.1, the game's Mono runtime), using **Harmony** patches against the game's `Amplitude.Mercury` assemblies |
| `glbconv` | Standalone C# console app on **.NET 8** (self-contained exe, no install needed), built on **SharpGLTF** for GLB/glTF parsing |
| Model-prep scripts | **Python** scripts run headless inside **Blender** (`blender -b --python …`) using the `bpy` API — rigging, decimation, animation clip extraction |
| Regression guard | A **bash** script (`check_schema_parity.sh`) plus a Unity-menu smoke test |
| Editor ↔ runtime contract | A plain **JSON** registry (`enc_models.json`) — the only thing the two halves share |

## Repo layout

- **`Assets/Scripts/Editor/`** — the Factory. `ModelFactoryWindow` (the GUI), `UniversalBaker` (the bake pipeline),
  `ModelRegistry` (the `ModelDef` schema + registry read/write), `RetextureWindow` (texture-only reskins of vanilla
  units — hot-loaded PNG or grey variant, no bake), `DistrictFactoryWindow`/`DistrictRegistry`/`DistrictBaker` (the
  district axis), `PropBaker` (the Prop Lab — pawn attachment props), plus the Orphan-Resources and Database-Browser tabs.
- **`Tools/`** — the pre-bake toolchain: `glbconv` (GLB→OBJ+MTL, multi-material, UV-tile normalize), `rig_anim.py`
  (rig + one clip, join, decimate), `deploy_convert.py` (rigid-part animation → bone-per-part armature),
  `prep_model.py` (strip/decimate static meshes), and `check_schema_parity.sh` (a regression guard, below).
- **`Assets/Pack/ENCReload/`** — the **self-contained HAF pack** (the git-tracked source of truth): `pack.json` (the
  registry — a `schemaVersion`/`modId` wrapper + `models`), `sounds/` (the custom WAVs), `skins/` (the retexture PNGs).
  ENC ships as this one directory: a user drops it into `BepInEx/config/haf_packs/ENCReload/` and the plugin loads it,
  resolving its file-assets pack-relative (`sounds/`, `skins/`). The editor **dual-writes** every change — the live deploy
  under `haf_packs/ENCReload/` (what the running game reads) and this repo copy (git history; auto-restores the live copy
  if a reinstall/verify wipes the game folder). *(Was the loose `enc_models.backup.json` + shared `enc_sounds/`/`enc_skins/`
  in `BepInEx/config` — moved into the pack 2026-07-24 so the registry AND sounds/skins are publishable as one unit.)*
- **`Assets/Databases/enc_models.backup.json`** — **legacy**, superseded by the pack above; kept only as a historical
  copy of the pre-pack registry.
- **`Assets/Resources/`** — the baked outputs (`*_ModelMesh`, `*_Skeleton`, `*_Atlas`, …). **Gitignored** — they're
  regenerated by baking and shipped inside the built mod, not tracked here.
- **`Assets/FactorySource/`** — per-model bake *inputs* (extracted OBJ/FBX + source albedos). Also not shipped.

## Quick start

1. Open the project in Unity, open **Model Factory** (the custom editor window).
2. **3D resource** → pick or add a model; set its **Model file**, target **Pawn description** (the vanilla unit to
   replace), size, rotation, and — for a rigged model — the **animation clip**.
3. Click **Bake**. Watch the Console for `… DONE`.
4. **Rebuild the mod** (your normal Humankind build/export step) and relaunch. The plugin does the rest.

Full walkthrough, every knob explained, and troubleshooting: **[`docs/` in the HumankindAssetFramework repo](https://github.com/sswelm/HumankindAssetFramework/tree/master/docs)**
(`Factory-Manual.md`, `Capabilities.md`, `Vertex-Budget.md`).

## Regression guards

Bakes are manual, so a baker change can silently break a model you don't happen to re-bake. Two guards catch that — run
them before committing changes to the baker, the `Tools/` scripts, or the registry schema:

- **Bake Smoke Test** — `Tools ▸ HAF ▸ Bake Smoke Test` (Unity menu). Bakes one representative per bake-path and asserts
  each completes and produces valid assets. **Non-destructive** (throwaway names; your real assets + registry are
  untouched).
- **Schema parity** — `bash Tools/check_schema_parity.sh`. Verifies every registry key the runtime plugin reads is a
  field the baker writes, so the two hand-synced schemas can't silently drift.

## Adding a brand-NEW unit (the gameplay databases)

The Factory gives a unit its *look*; a **new** unit also needs gameplay data. The pattern is **clone an existing
similar unit's blocks** across five database assets (the Abomination — an animal cloned from the Animal presentation
family — is the reference example for creatures; the Light Assault Mech for vehicles):

1. **`Assets/Databases/Unit/LandUnitDefinition.asset`** — the unit itself: stats, movement, era, cost, class.
2. **`Assets/Databases/Unit/UnitFamilyDefinitionENC.asset`** — add it to a family so it appears in the roster.
3. **`Assets/Databases/Unit/LandUnitUIMappers.asset`** — display name, description, and portrait wiring.
4. **A technology unlock** — `Assets/Databases/Technologies/TechnologyDefinitionENC.asset` (or the era tech of your choice).
5. **Presentation definitions** — `PresentationPawnDefinition_*` + `PresentationUnitDefinition_Era*_ENC`: clone the
   donor's pawn description under a new name (e.g. `Era4_Common_Tigers_01`) **with the visual levels cleared** — this
   name is what the Factory entry's *Target pawn* points at, the contract between gameplay data and the injected model.

Then the usual loop: Factory entry on that pawn description → bake → rebuild the mod → the unit exists in-game with
your model. Sanity checks: the unit buildable via its tech, correct era art *fallback* until your bake lands, and
`NoAttackRotation`/`SubPawnComposition` inherited from a donor that behaves the way your unit should.

## Notes

- **Model licensing is your responsibility.** Baking embeds a model's geometry into the shipped mod. Only bake models
  whose license permits redistribution (CC0 / CC-BY / a commercial or explicit game-mod license) — a *personal-use*
  asset is not redistributable just because it's been baked.
- The baked assets are regenerated on every bake; only source, tooling, and the registry backup are tracked here.

## License

**Code and tooling are MIT; the mod content is not.** Specifically:

- **MIT** ([LICENSE](LICENSE)): all code, scripts, and project config — `Assets/Scripts/`, `Tools/`,
  `ProjectSettings/`, `Packages/` manifests. Fork it, vendor it, build your own Factory on it.
- **All rights reserved**: the ENC mod content — everything under **`Assets/Databases/`** (the mod's game data and
  the `enc_models.backup.json` registry backup). This is the ENCReload *mod itself*, not the tooling; please don't
  redistribute or rehost it.
- Not ours to license: the Amplitude/Humankind SDK and game content are never committed here (see `.gitignore`), and
  any baked third-party model geometry remains under its own model license.
