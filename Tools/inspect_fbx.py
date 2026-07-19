# inspect_fbx.py — convert a model into PER-CLIP inspection FBXs for the editor's clip-range picker dialog
# (preview / play / scrub, pick a start..end slice). One action per file, exported with EXACTLY the flags the
# proven rig_anim pipeline uses: the earlier single-file `bake_anim_use_all_actions=True` shortcut mis-evaluates
# slotted actions (Blender 4.4+/5) and baked the animation MIRRORED against the mesh (the M114's legs crossed).
# The armature object is renamed to the sentinel "HAFCLIP" so every FBX take is named "HAFCLIP|<action>" — the
# dialog strips that fixed prefix to recover the EXACT action name (which may itself contain '|', e.g. the
# Sketchfab "Soldier_reference_skeleton|Idle1").
#
#   blender -b -P inspect_fbx.py -- <in.glb|gltf|fbx|blend> <outDir>
import bpy, sys, os, re

argv = sys.argv[sys.argv.index("--") + 1:]
inp, outdir = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(inp)[1].lower()
if ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=inp)
elif ext == ".blend":
    bpy.ops.wm.open_mainfile(filepath=inp)
else:
    bpy.ops.import_scene.gltf(filepath=inp)

arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm is None:
    print("INSPECT ERROR: no armature — nothing to scrub"); sys.exit(1)
if arm.animation_data is None:
    arm.animation_data_create()
arm.name = "HAFCLIP"   # sentinel: FBX takes become "HAFCLIP|<action>" -> the dialog strips the fixed prefix

def assign(a):
    arm.animation_data.action = a
    try: arm.animation_data.action_slot = a.slots[0]   # Blender 4.4+/5 slotted actions
    except Exception: pass

os.makedirs(outdir, exist_ok=True)
count = 0
for a in list(bpy.data.actions):
    assign(a)
    fs, fe = [int(round(v)) for v in a.frame_range]
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = fs, fe
    safe = re.sub(r'[^A-Za-z0-9_.-]+', '_', a.name)
    out = os.path.join(outdir, safe + ".fbx")
    bpy.ops.export_scene.fbx(filepath=out, use_selection=False, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, object_types={'ARMATURE', 'MESH'})
    print("INSPECT wrote %s ('%s' frames %d..%d)" % (out, a.name, fs, fe))
    count += 1
print("INSPECT done: %d clip fbx(s)" % count)
