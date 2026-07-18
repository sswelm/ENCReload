# ENCReload — Universal Model Factory (Humankind)

The Unity-editor side of the **ENCReload** mod for [Humankind](https://www.games2gether.com/amplitude-studios/humankind).
It's a **Universal Model Factory**: an editor tool that turns an ordinary 3D model (a `.glb`, `.fbx`, `.obj`, or `.blend`)
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
Universal Model Factory (Assets/Scripts/Editor/)
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

- **Tools ▸ ENC ▸ Animation Lab** — a model's **animation** in its own dialog (docks as a tab beside the Factory):
  clip + bone-filter pickers, fire-on-attack, deploy-on-stop + recoil sliders, and **Save (no bake)** for runtime
  flags. The Factory owns the model (file/transform/size), the Lab owns the animation — settings are mutually
  exclusive and **enforced at bake time** (each window rebases on the registry and writes only its own fields).
  Bake from either window; the pipeline is identical.
- **Tools ▸ ENC ▸ Unit Retexture** — reskin an existing unit: a hot-loaded PNG, or a live **Desaturate + RGB** adjust of
  its own atlas. Isolated per unit (the original stays as-is), and free on the vertex budget.
- **Tools ▸ ENC ▸ Unit Sound** — give a unit **movement audio**: the game's own engine event by name, or custom WAVs as
  **Start (spool-up) → Travel (loop) → Stop (spool-down)** with a per-clip volume and an in-editor **▶** preview.

These write fields onto the unit's registry entry; see `HumankindAssetFramework/docs/Factory-Manual.md` §12–15.
Animated-model niceties (2026-07-18): **rotation is baked into the rig** when set (`0,0,0` = untouched legacy path),
the Blender re-slim runs **automatically** when its inputs change, and the old "Reuse extracted" checkbox is now
purely **"Keep extracted texture (hand-edits)"**.

## Beyond units — districts & props

Units were the first injection axis; the same bake core now drives two more, each with its own window:

- **Tools ▸ ENC ▸ District Factory** — put a custom static model on a **district tile** (e.g. ENC's Breeder Reactor).
  Bakes model → bone-free FxMesh and writes an `enc_districts.json` registry entry the plugin reads (any number of
  districts, each optionally isolated to its own tiles). See
  [`District-Visuals.md`](https://github.com/sswelm/HumankindAssetFramework/blob/master/docs/District-Visuals.md).
- **Tools ▸ ENC ▸ Prop Lab** — give a pawn a custom **weapon/gear prop** on an attachment slot (Humankind's Slingers
  finally carry an actual sling). Dumps any vanilla fragment as an authoring template, then bakes model → FxMesh →
  MeshCollection + fragment assets; the plugin registers the collection at load. See
  [`Pawn-Props.md`](https://github.com/sswelm/HumankindAssetFramework/blob/master/docs/Pawn-Props.md).

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
- **`Assets/Databases/enc_models.backup.json`** — a git-tracked backup of the registry, written as a full HAF pack
  (`schemaVersion`/`modId` wrapper + `models`). The live registry lives in the game's `BepInEx/config/`; this backup
  auto-restores it if a reinstall/verify wipes the game folder, and doubles as the reference pack a joining modder copies.
- **`Assets/Resources/`** — the baked outputs (`*_ModelMesh`, `*_Skeleton`, `*_Atlas`, …). **Gitignored** — they're
  regenerated by baking and shipped inside the built mod, not tracked here.
- **`Assets/FactorySource/`** — per-model bake *inputs* (extracted OBJ/FBX + source albedos). Also not shipped.

## Quick start

1. Open the project in Unity, open **Universal Model Factory** (the custom editor window).
2. **3D resource** → pick or add a model; set its **Model file**, target **Pawn description** (the vanilla unit to
   replace), size, rotation, and — for a rigged model — the **animation clip**.
3. Click **Bake**. Watch the Console for `… DONE`.
4. **Rebuild the mod** (your normal Humankind build/export step) and relaunch. The plugin does the rest.

Full walkthrough, every knob explained, and troubleshooting: **[`docs/` in the HumankindAssetFramework repo](https://github.com/sswelm/HumankindAssetFramework/tree/master/docs)**
(`Factory-Manual.md`, `Capabilities.md`, `Vertex-Budget.md`).

## Regression guards

Bakes are manual, so a baker change can silently break a model you don't happen to re-bake. Two guards catch that — run
them before committing changes to the baker, the `Tools/` scripts, or the registry schema:

- **Bake Smoke Test** — `Tools ▸ ENC ▸ Bake Smoke Test` (Unity menu). Bakes one representative per bake-path and asserts
  each completes and produces valid assets. **Non-destructive** (throwaway names; your real assets + registry are
  untouched).
- **Schema parity** — `bash Tools/check_schema_parity.sh`. Verifies every registry key the runtime plugin reads is a
  field the baker writes, so the two hand-synced schemas can't silently drift.

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
