# inspect_fbx.py — convert a model into PER-CLIP inspection FBXs for the editor's clip-range picker dialog
# (preview / play / scrub, pick a start..end slice). One action per file, exported with the proven rig_anim flags.
#
# Fidelity rules learned the hard way:
# - the animation is exported COMPLETE — rotations AND locations. An earlier location-strip (meant to mimic the
#   shipping pipeline) CAUSED the M114's crossed-legs preview: the trail legs spread by rotation+translation, and
#   rotation-only swings them about the wrong pivot, sweeping them inward. The picker is a RAW animation player of
#   the source; the shipping pipeline's rotation-only conversion is made safe by its own rest-fold step, not here.
# - clip names are recorded in a manifest.txt (filename<TAB>exact action name): FBX take names are unreliable
#   (slot-assignment quirks yielded a take named "Scene"), and the safe filename can't round-trip characters like
#   the '|' in Sketchfab action names.
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

# JOIN all meshes into ONE object — the proven pipeline's prep. HARD-LEARNED: Unity's FBX import of this rig as
# MULTIPLE mesh objects mirrors the animation against the meshes (the M114 previewed with crossed legs while the
# very same FBX sampled correctly in Blender); the single joined mesh in armature space imports consistently.
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for m in meshes:
    if m.data.users > 1:
        m.data = m.data.copy()          # single-user-ize (join refuses multi-user data)
if len(meshes) > 1:
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    print("INSPECT joined %d meshes" % len(meshes))

def assign(a):
    arm.animation_data.action = a
    try: arm.animation_data.action_slot = a.slots[0]   # Blender 4.4+/5 slotted actions
    except Exception: pass

os.makedirs(outdir, exist_ok=True)
manifest = []
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
    manifest.append("%s\t%s" % (safe + ".fbx", a.name))
    print("INSPECT wrote %s ('%s' frames %d..%d)" % (out, a.name, fs, fe))
    count += 1
with open(os.path.join(outdir, "manifest.txt"), "w", encoding="utf-8") as f:
    f.write("\n".join(manifest))
print("INSPECT done: %d clip fbx(s) + manifest" % count)
