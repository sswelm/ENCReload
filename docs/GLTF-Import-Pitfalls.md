# glTF Import Pitfalls — the Dug-out Canoe Findings (2026-07-30)

The Era-1 Dug-out Canoe (Sketchfab, Matt LeMoine, CC-BY-NC-SA-4.0) is the most pathological asset the
Factory has eaten. It forced six real pipeline defects into the open. This guide records what each one
looks like from the outside, so the next node-animated Sketchfab model doesn't cost a full day.

## The asset's pathology (why everything below happened)

- **Spec-violating GLB**: Sketchfab's exporter wrote `byteStride` on *animation* bufferViews (illegal).
  SharpGLTF/glbconv hard-fail → the multi-material atlas silently falls back to single-material
  ("every part samples material 0"). **Repair**: strip `byteStride` from the animation bufferViews only
  (mesh ones legitimately keep theirs), rewrite the JSON chunk, pad to 4 bytes. Swap the repaired data
  into the *original filename* — a Factory/Lab window overwrites registry edits on every Save/Bake.
- **Vestigial skin**: the file carries a 21-joint skin that **zero** mesh nodes reference. All motion is
  object-level node animation (hull rock / log bob / paddle strokes / sail sway on wrapper empties).
- **Two coordinate frames**: the static node TRS composes the assembled canoe in meters, while the
  animation channels live in a ~100× different unit frame with wrapper pivots parked 425k units out that
  only cancel through the full chain. Blender's live depsgraph composition of this is **scrambled**; the
  inspection-FBX round-trip (what the clip-range picker plays) re-composes the same take **correctly**.

## Symptom → cause table

| Symptom | Cause | Fix / knob |
|---|---|---|
| Whole model wears material 0, rest grey | glbconv crashed on byteStride → single-atlas fallback | repair the GLB (above) |
| Multi-material atlas missing one material after config changes | extraction cache was mtime-only; a deploy-converted (part-stripped) extraction looked "fresh" for the raw file | fixed: `<name>.mtl.src` source stamp |
| A rigid decor part (sail) dragged off its mount after bind | bone-parent→skin bound it to bones whose animated frame ≠ static layout | **Static parts (no bone-bind)** in the Animation Lab (`staticParts`) — keeps listed mesh/material substrings weightless at their authored position |
| deploy_convert silently deletes half the model | its default strip list contains `"polysurface"` (howitzer-era default) | put anything in **Deploy strip parts** (e.g. `camera`) — a non-empty value replaces the default list |
| Exported clip is a statue though the source animates | multi-slot action: a glTF import packs every object's animation into ONE action with a slot per object; `slots[0]` is not the armature's slot | fixed in `assign_action` — **gated to `localNodeAnim`**, see below |
| Sail/parts collapse to a clump after "fixing" the slot | the legacy static path's captures (flatten, bone-parent) *depend* on the frozen rig — un-freezing it relocated every capture | the slot fix + REST-hold only run under `localNodeAnim`; the static path is byte-faithful to every proven bake |

## Current state of node-animation support

- **Shipped & verified**: the static bake — `convertRig` + `keepTranslations` + `staticParts: Cloth` +
  clip `Take 001[120..360]`. Sail raised on the mast, textured, floating correctly in-game. No motion.
- **Parked (off-by-default)**: `localNodeAnim` ("Node animation → bones") — an evaluated-world transplant
  that samples each mesh's per-frame world matrix from the **inspection-FBX round-trip** (the only
  composition of this file that is provably correct) and keys one flat bone per mesh. The sampling,
  keying, slot handling and double-bind guard all work; the bind/vert-fold arrangement is still wrong
  (parts collapse into the hull). Do not enable it on a shipping model yet.

## Debugging method that finally worked

Iterate **headless, without Unity**, and *look at the result*:

1. Run the converter directly (empty argv strings vanish in PowerShell 5.1 — drive Blender from bash):
   `blender -b -P Tools/rig_anim.py -- <glb> <out.fbx> 24000 "" "<clip>" "" 1 "0,180,0" 1 "" 0 "" 1 "<staticParts>" <localNodeAnim 0|1>`
2. Measure placement numerically (cloth/part verts as % of model bbox) and motion (max per-vert
   displacement across frames) by re-importing the FBX in a second Blender run.
3. **Render the baked FBX to PNG** (Workbench engine, camera auto-framed from evaluated bounds) and look
   at it. The Lab preview shows the *bind pose* only — a clip-playing bake can look wrong in the preview
   and right in-game, and vice versa. The render at a mid-clip frame is the truth.
