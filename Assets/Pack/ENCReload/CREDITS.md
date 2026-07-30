# ENCReload — Third‑Party Asset Credits & Licenses

Attribution / license record for every third‑party asset shipped in this pack / mod.
Much of this was recovered from the **embedded glTF `asset.extras`** metadata inside the source
`.glb` files (Sketchfab stamps author/license/source into downloads). Complete before public release.

**License quick‑guide** (all obligations assume you *distribute* the mod):
- **CC‑BY** → OK to ship + modify + commercial; **must credit** author + license + link, and note you modified it.
- **CC‑BY‑SA** → same as BY, **plus** your modified/baked version must carry CC‑BY‑SA too.
- **CC‑BY‑NC** → OK to modify + ship **only in a free / non‑commercial** mod; **must credit** author + license + link. Derivatives (baking/converting) ARE allowed. **Never** bundle in a commercial or permissively‑relicensed release (e.g. a distributed HAF package).
- **CC‑BY‑NC‑ND** → **NoDerivatives = you may NOT distribute a modified version. Baking/converting IS a modification. Do not ship.**
- **Fab / paid** → per the Fab EULA: OK when *incorporated into a Project* (a mod), not standalone.

Status: ✅ clear (credit as noted) · ⚠ needs its source/license found · ❌ **do NOT ship as‑is**

## 3D models

| Resource | Author (credit) | License | Source | Status |
|---|---|---|---|---|
| Abominations | **CommunicationNode** (sketchfab.com/Starven38) | **CC‑BY‑SA‑4.0** | [SCP‑682 low‑poly style](https://sketchfab.com/3d-models/scp-682-low-poly-style-e40059dbf3334081a1577602b8d98335) | ✅ (credit + share‑alike) |
| DroneSquadFPV | **MrYoink** (sketchfab.com/mryoinkie) | **CC‑BY‑4.0** | [Combine Soldier](https://sketchfab.com/3d-models/combine-soldier-6fa87ed8061b4bda85cd5b4b8bd7b7b2) | ✅ (credit) |
| Hovercraft | **lm9241221** (sketchfab.com/lm9241221) | **CC‑BY‑4.0** | [LCAC esboço](https://sketchfab.com/3d-models/lcac-esboco-c22158e65f6f4cd99b16fd27653cf24f) | ✅ (credit) |
| Zeppelin / ReconZeppelin | **MMD_SonicNewYear** (sketchfab.com/MMD_SonicNewYear) | **CC‑BY‑4.0** | [Дирижабль HD](https://sketchfab.com/3d-models/hd-92734a2c283e4d889fecbb010aaf7822) | ✅ (credit) |
| StealthHelicopter | **manilov.ap** (sketchfab.com/manilov.ap) | **CC‑BY‑4.0** | [Rah66](https://sketchfab.com/3d-models/rah66-f9f7a920d24b42c19d87a2d569e27436) | ✅ (credit) |
| HandCrankedSubmarines | **Fareastern Loner** (Tucuru@3D) — Fab | **Fab Personal** (fab.com/eula) — OK incorporated in a Project | Fab, invoice A1122204036 (textures partly Texture.com, seller‑cleared) | ✅* (confirm EULA clause) |
| **VolleyGun** (Era5) | **Orpind** — **"Ozhiga" organ gun (1)** | **Fab license** (Personal, purchased 2026‑07‑24) — permits incorporation into a distributed mod | Fab (fab.com); file: `D:/3DModels/Ozhiga Organ Gun/ozhiga_organ_gun_1.glb` (invoice: __) | ✅ licensed — ⚠ **MUST RE‑BAKE:** the currently‑baked asset is still the free NC‑ND *variant 2*; the Fab purchase is *variant 1*. modelFile is now re‑pointed at the Fab file → **re‑bake VolleyGun from it** so the shipped asset derives from the licensed copy (look changes slightly to variant 1). |
| **ArmouredCar** (Era5) | **Red Blue Pixel Studio** — "Ehrhardt E V" | **Fab (Personal)** (fab.com/eula) — OK incorporated in a Project | Fab, UE "Game Template"; extracted from Unreal → `D:/3DModels/Armored Car Ehnhardt/Ehrhardt_Spin.glb` (rigged, spinning wheels; bakes with convertRig ON) | ✅ (Fab license — record invoice ID) |
| **Tank_1** (T‑62 style — unit TBD) | **KapI777** (sketchfab.com/KapI777) — credit line: *"Tank_1+Animation by KapI777"* | **CC‑BY‑NC‑4.0** (Attribution‑NonCommercial; derivatives OK, credit + **non‑commercial only**) | [Tank_1+Animation](https://sketchfab.com/3d-models/tank-1animation-337e338947e540c7b2417b6c7fb0f986) — 23.8k tris / 13.8k verts, rigged + animated; downloaded **GLB** (88 MB, 1k tex) → `D:/Downloads/` then `D:/3DModels/Tank_1/` | ✅ (free mod: credit) — ⚠ **NC:** never in a commercial / permissive HAF‑package release |
| **DugoutCanoe** (Era1 naval transport) | **Matt LeMoine** (sketchfab.com/Matt_LeMoine) | **CC‑BY‑NC‑SA‑4.0** (credit + **non‑commercial** + **share‑alike** on the baked derivative) | [Dug‑out Canoe](https://sketchfab.com/3d-models/dug-out-canoe-9da489418c594fbbbc071747c3076ef2) — 21.7k tris / 17.8k verts, 4 materials (Bark/Grain/Rope/Cloth), rigged 21 bones + 15s "Take 001"; GLB 1k → `D:/3DModels/Dugout Canoe/dug-out_canoe.glb` | ✅ (free mod: credit) — ⚠ **NC + SA:** never in a commercial / permissively‑relicensed HAF‑package release |
| AttackHelicopter | ? (Cobra) — metadata stripped on Blender re‑export | ⚠ find in your Sketchfab library | `Cobra.glb` | ⚠ |
| OrganGun (Era4) | **user‑textured from scratch**; base geometry from "Medieval Organ Gun / Ribauldequin" download (id 4905272) | ⚠ base‑model source/license TBD (STL‑style import — likely a 3D‑print model site) | `Ribauldequin_textured.glb` | ⚠ (separate unit from VolleyGun) |
| ReconDrone | ? — metadata stripped | ⚠ | `drone_clean.glb` | ⚠ |
| ReconHelicopter | ? (Bell H‑13) — metadata stripped | ⚠ | `Bel-H-13-Cleaned.glb` | ⚠ |
| StealthCruiser | ? (Zumwalt) — metadata stripped | ⚠ | `zumwalt_clean.glb` | ⚠ |
| TowedGunHowitzers | ? (M114) — metadata stripped | ⚠ | `m114_howitzer_in_action.glb` | ⚠ |
| LightAssaultMech | ? (robo1A) — FBX, no glTF metadata | ⚠ | `uploads_files_6690430_robo1A.fbx` | ⚠ |

*The ⚠ rows lost their embedded metadata because they were re‑exported through Blender (generator = "Khronos glTF Blender I/O") before import. Find each in your **Sketchfab → My Library / liked models** (or wherever downloaded) and record author + license.*

## Sounds (pack `sounds/`)

| File | Author / Source | License | Status |
|---|---|---|---|
| Abomination_idle.wav (capaholiczsfx‑creature‑snarl‑very‑close‑403154) | ⚠ | ⚠ | ⚠ |
| Abomination_attack.wav (yodguard‑creature‑beam‑attack‑with‑roar‑4‑482510) | ⚠ | ⚠ | ⚠ |
| drone / ReconDrone_* / DronStart / DroneStop / dronTravel | ⚠ | ⚠ | ⚠ |

## Textures (pack `skins/`)

| File | Origin | License | Status |
|---|---|---|---|
| Retex_Era6_Common_StealthCorvettes_01.png | painted / derived from vanilla atlas? | ⚠ | ⚠ |
| Retex_Era6_Common_LightAssaultMech_01.png | ⚠ | ⚠ | ⚠ |
| LightAssaultMech.png | ⚠ | ⚠ | ⚠ |

---
**⚠ ACTION BEFORE RELEASE:** (1) **VolleyGun is CC‑BY‑NC‑ND — remove it or get permission; it can't ship baked.** (2) Find the license for the 7 Blender‑stripped models + the 2 SFX. (3) All CC‑BY/BY‑SA models above must be credited (author, license, link, "modified") in the mod's public credits.
