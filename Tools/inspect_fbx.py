# inspect_fbx.py — convert a model into an INSPECTION FBX carrying ALL its animation clips, for the editor's
# clip-range picker dialog (preview / play / scrub, pick a start..end slice). Pure format conversion: no joins,
# no decimation, no rig surgery — every action exports as its own FBX take, which Unity imports as one
# AnimationClip each.
#
#   blender -b -P inspect_fbx.py -- <in.glb|gltf|fbx|blend> <out.fbx>
import bpy, sys, os

argv = sys.argv[sys.argv.index("--") + 1:]
inp, outp = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(inp)[1].lower()
if ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=inp)
elif ext == ".blend":
    bpy.ops.wm.open_mainfile(filepath=inp)
else:
    bpy.ops.import_scene.gltf(filepath=inp)

os.makedirs(os.path.dirname(outp), exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=outp, bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, add_leaf_bones=False)
print("INSPECT wrote: %s  actions: %s" % (outp, sorted(a.name for a in bpy.data.actions)))
