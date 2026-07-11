# deploy_convert.py — turn a model animated by RIGID MOVING PARTS (node transforms, no skinning) into a bone-per-part
# SKINNED armature that the Factory's animated bake (rig_anim.py) can consume. Many Maya/Sketchfab models animate parts
# by moving separate nodes (a howitzer's trail legs, a turret, landing gear, folding wings, a crane) rather than skinning
# — rig_anim.py needs an armature, so this bridges the gap: it builds one bone per animated part (hierarchy preserved),
# retargets each node's animation onto its bone (Copy Transforms + bake), and rigidly binds each mesh to its bone at 100%.
# Soft-skinned character rigs (crew) collapse the bake, so a strip-list removes them (and any loose props).
#
# Run headless:
#   blender -b -P deploy_convert.py -- <input.glb> <output.glb> [startFrame endFrame] [stripCsv]
#     startFrame endFrame : trim the clip to this sub-range (e.g. just the deploy). Omit = full clip.
#     stripCsv            : comma-separated name substrings to delete (crew/props). Omit = the M114 defaults below.
# Checks BOTH the object name AND the mesh-data name (glTF import can name an object 'Object_NNN' while its mesh keeps
# the real name, so an object-name-only filter misses them). Node `matrix` transforms aren't handled (TRS only).
import bpy, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
inp, outp = argv[0], argv[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=inp)
scene = bpy.context.scene

# --- 1. strip crew + loose props (soft-skinned rigs = the bake-breakers; ammo/pole/string = loose firing props) ---
KILL = tuple(k.strip().lower() for k in argv[4].split(",")) if len(argv) > 4 else \
    ("solder", "soldier", "pole", "string", "shell", "dynam", "ammun", "pcylinder1", "pcylinder3", "icosphere", "basicgal")
def is_kill(o):
    names = [o.name.lower()]
    if getattr(o, "data", None) is not None and hasattr(o.data, "name"):
        names.append(o.data.name.lower())
    return any(k in n for n in names for k in KILL)
for obj in list(bpy.data.objects):
    if is_kill(obj):
        bpy.data.objects.remove(obj, do_unlink=True)
survivors = [o.name for o in bpy.data.objects if o.type == 'MESH']
print("DEPLOY after strip: %d objects, %d meshes: %s" % (len(bpy.data.objects), len(survivors), ", ".join(survivors)))

# --- 2. frame range from the surviving actions ---
fmin, fmax = 1e9, -1e9
for o in bpy.data.objects:
    if o.animation_data and o.animation_data.action:
        fr = o.animation_data.action.frame_range
        fmin = min(fmin, fr[0]); fmax = max(fmax, fr[1])
if fmin > fmax:
    fmin, fmax = 1.0, 1.0
fmin, fmax = int(fmin), int(fmax)
scene.frame_start, scene.frame_end = fmin, fmax
scene.frame_set(fmin)   # rest = deploy-start pose, so the bind is consistent
print("DEPLOY frame range: %d..%d" % (fmin, fmax))

# --- 3. which objects are ANIMATED parts (get a bone) vs plain meshes (get bound to a bone) ---
parts = [o for o in bpy.data.objects if o.animation_data and o.animation_data.action]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print("DEPLOY animated parts: %d, meshes: %d" % (len(parts), len(meshes)))
for p in parts:
    print("   part: %-40s parent=%s" % (p.name, p.parent.name if p.parent else None))

# --- 4. armature: one bone per animated part at its current (fmin) world pos, hierarchy mirrored ---
arm_data = bpy.data.armatures.new("DeployArm")
arm = bpy.data.objects.new("DeployArm", arm_data)
scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
bone_of = {}
for p in parts:
    b = arm_data.edit_bones.new(p.name)
    head = p.matrix_world.translation.copy()
    b.head = head
    b.tail = head + Vector((0, 0, 0.1))
    bone_of[p.name] = b.name
for p in parts:   # mirror object parenting onto the bones
    if p.parent and p.parent.name in bone_of:
        arm_data.edit_bones[bone_of[p.name]].parent = arm_data.edit_bones[bone_of[p.parent.name]]
bpy.ops.object.mode_set(mode='OBJECT')

# --- 5. retarget: each bone copies its part's WORLD transform, then bake to keyframes ---
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')
for p in parts:
    c = arm.pose.bones[bone_of[p.name]].constraints.new('COPY_TRANSFORMS')
    c.target = p
bpy.ops.nla.bake(frame_start=fmin, frame_end=fmax, only_selected=False,
                 visual_keying=True, clear_constraints=True, bake_types={'POSE'})
bpy.ops.object.mode_set(mode='OBJECT')
print("DEPLOY baked %d bones" % len(bone_of))

# --- 6. bind each mesh 100% to the bone of its nearest animated ancestor (rigid) ---
def anim_ancestor(o):
    while o:
        if o.name in bone_of:
            return o.name
        o = o.parent
    return None

bound = 0
for m in meshes:
    bname = anim_ancestor(m)
    if not bname:
        print("DEPLOY WARN no animated ancestor:", m.name); continue
    # detach from the old animated parent, keeping world transform at fmin
    mw = m.matrix_world.copy()
    m.parent = None
    m.matrix_world = mw
    # one vertex group = the bone, all verts at weight 1 (rigid)
    for vg in list(m.vertex_groups):
        m.vertex_groups.remove(vg)
    vg = m.vertex_groups.new(name=bname)
    vg.add(range(len(m.data.vertices)), 1.0, 'REPLACE')
    for mod in [md for md in m.modifiers if md.type == 'ARMATURE']:
        m.modifiers.remove(mod)
    am = m.modifiers.new("arm", 'ARMATURE')
    am.object = arm
    m.parent = arm   # object-parent to the armature (standard skinned setup)
    m.matrix_world = mw
    bound += 1
print("DEPLOY bound %d meshes" % bound)

# --- 7. delete the now-redundant animated empties (their motion lives on the bones) ---
for p in list(parts):
    if p.type != 'MESH':
        bpy.data.objects.remove(p, do_unlink=True)

# --- 7b. keep ONLY the armature's baked action; strip every other object's animation + purge stray actions,
#         so the export produces ONE clean deploy clip (not 17 leftover per-part animations) ---
arm_action = arm.animation_data.action if arm.animation_data else None
for o in bpy.data.objects:
    if o is not arm and o.animation_data:
        o.animation_data_clear()
for a in list(bpy.data.actions):
    if a is not arm_action:
        bpy.data.actions.remove(a)
if arm_action:
    arm_action.name = "deploy"   # clean clip name for the Factory picker (was an auto 'Action.NNN')
print("DEPLOY kept 1 action:", arm_action.name if arm_action else None)

# --- 8. export GLB, trimmed to the DEPLOY sub-range if given (argv: in out [start] [end]) ---
if len(argv) >= 4:
    scene.frame_start, scene.frame_end = int(argv[2]), int(argv[3])
    print("DEPLOY trim to frames %d..%d" % (scene.frame_start, scene.frame_end))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=outp, export_format='GLB', export_animations=True,
                          export_frame_range=True, export_yup=True)
print("DEPLOY wrote:", outp)
