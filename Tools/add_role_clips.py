# add_role_clips.py — augment an ALREADY-CONVERTED deploy GLB (a deploy_convert.py output) with the state-machine
# ROLE actions, IN PLACE. For GLBs converted before deploy_convert step 7c existed (the M114): re-running the
# original conversion needs its historical args; this instead samples the baked 'deploy' action the GLB already
# carries — deterministic, no reconstruction. Adds: unfold (after-move), fold (pre-move, reversed), folded (move
# stance), deployed (idle stance), recoil (attack, when a tail exists). The legacy 'deploy' action is untouched
# and stays the active one, so existing legacy bakes are unaffected.
#
#   blender -b -P add_role_clips.py -- <deploy.glb> <deployEndFraction> [outPath]
#     deployEndFraction : where the deploy segment ends, as a fraction of the clip (the registry's deployPoseTime,
#                         e.g. 0.72) — robust against fps remapping on import, unlike an absolute frame.
#     outPath           : omit to write IN PLACE (a .bak copy is made first).
import bpy, sys, os, shutil

argv = sys.argv[sys.argv.index("--") + 1:]
inp = argv[0]
frac = float(argv[1])
outp = argv[2] if len(argv) > 2 else inp

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=inp)

arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm is None:
    print("ROLES ERROR: no armature in %s" % inp); sys.exit(1)
act = bpy.data.actions.get("deploy") \
      or (arm.animation_data.action if arm.animation_data else None) \
      or (bpy.data.actions[0] if len(bpy.data.actions) else None)
if act is None:
    print("ROLES ERROR: no action in %s" % inp); sys.exit(1)
if arm.animation_data is None:
    arm.animation_data_create()

def assign(a):
    arm.animation_data.action = a
    try: arm.animation_data.action_slot = a.slots[0]          # Blender 4.4+/5 slotted actions
    except Exception: pass

assign(act)
fmin, fmax = [int(round(v)) for v in act.frame_range]
deploy_end = int(round(fmin + frac * (fmax - fmin)))
scene = bpy.context.scene
scene.frame_start, scene.frame_end = fmin, fmax
print("ROLES source action '%s' frames %d..%d, deploy segment ends at %d (fraction %.3f)" % (act.name, fmin, fmax, deploy_end, frac))

for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'

# snapshot the evaluated pose basis per frame (what the original bake keyed)
_snap = {}
for f in range(fmin, fmax + 1):
    scene.frame_set(f)
    bpy.context.view_layer.update()
    _snap[f] = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in arm.pose.bones}

def make_role(name, frames):
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)                          # idempotent re-runs
    a = bpy.data.actions.new(name)
    arm.animation_data.action = a
    try: arm.animation_data.action_slot = a.slots.new(id_type='OBJECT', name=arm.name)
    except Exception: pass
    for i, f in enumerate(frames):
        for pb in arm.pose.bones:
            loc, quat = _snap[f][pb.name]
            pb.location = loc
            pb.rotation_quaternion = quat
            pb.keyframe_insert('location', frame=fmin + i)    # keyed from fmin: stays inside any export frame range
            pb.keyframe_insert('rotation_quaternion', frame=fmin + i)
    print("ROLES made '%s' (%d frames)" % (name, len(frames)))
    return a

dep = list(range(fmin, deploy_end + 1))
make_role("unfold", dep)
make_role("fold", list(reversed(dep)))
make_role("folded", [fmin, fmin])                             # 2 identical frames: a valid HELD pose
make_role("deployed", [deploy_end, deploy_end])
has_recoil = fmax > deploy_end
if has_recoil:
    make_role("recoil", list(range(deploy_end, fmax + 1)))

assign(act)                                                   # legacy 'deploy' stays the active action

if os.path.abspath(outp) == os.path.abspath(inp):
    shutil.copy2(inp, inp + ".bak")
    print("ROLES backup: %s.bak" % inp)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=outp, export_format='GLB', export_animations=True, export_yup=True)
print("ROLES wrote %s with actions: %s" % (outp, sorted(a.name for a in bpy.data.actions)))
