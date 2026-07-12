# deploy_convert.py — turn a model animated by RIGID MOVING PARTS (node transforms, no skinning) into a bone-per-part
# SKINNED armature that the Factory's animated bake (rig_anim.py) can consume. Many Maya/Sketchfab models animate parts
# by moving separate nodes (a howitzer's trail legs, a turret, landing gear, folding wings, a crane) rather than skinning
# — rig_anim.py needs an armature, so this bridges the gap: it builds one bone per animated part (hierarchy preserved),
# retargets each node's animation onto its bone (Copy Transforms + bake), and rigidly binds each mesh to its bone at 100%.
# Soft-skinned character rigs (crew) collapse the bake, so a strip-list removes them (and any loose props).
#
# Run headless:
#   blender -b -P deploy_convert.py -- <in.glb> <out.glb> [start end] [stripCsv] [readyFrame] [legScale] [barrelScale] [recoilSrcStart recoilSrcEnd] [step] [mag]
#     start end   : trim the clip to this sub-range (the deploy). Omit = full clip.
#     stripCsv    : comma-separated name substrings to delete (crew/props). Omit = the M114 defaults below.
#     readyFrame  : (5b) source frame of the fully-elevated barrel; retargets the barrel to rise there over the deploy's back half.
#     legScale    : (5c) scale the leg spread (1 = full, 0.5 = half as wide).
#     barrelScale : (5b) scale the barrel elevation (>1 exaggerates past the source's firing max).
#     recoilSrcStart recoilSrcEnd : (5d) the recoil sub-range IN THE SOURCE clip; its kickback TIMING is remapped onto a recoil
#                                   tail appended after 'end', played on-fire from the deployed hold.
#     step        : (5d) source-frame sampling step for the recoil (default 2).
#     mag         : (5d) slide-distance scale (default 1 = the source distance; 2 ~= half the tube).
#   NOTE (5d): the clip bake keeps per-bone ROTATION but DISCARDS per-bone translation, so a literal barrel slide bakes to nothing.
#   The recoil is faked via an FK-arc: a hidden far-pivot 'RecoilArm' bone the tube hangs off, rotated so the tube swings on a long
#   arc that reads as a near-straight backward slide (the arm's rotation bakes; runtime FK rebuilds it). It keeps a slight swing;
#   DON'T counter-rotate the tube to straighten it — that needs translation the bake drops, and the model explodes in-game.
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
KILL = tuple(k.strip().lower() for k in argv[4].split(",")) if len(argv) > 4 and argv[4].strip() else \
    ("solder", "soldier", "pole", "string", "shell", "dynam", "ammun", "pcylinder1", "pcylinder3", "icosphere", "basicgal",
     "polysurface")   # polySurface1/5 = a loose prop floating ~20u above the gun (a stray shell), not part of the howitzer
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

# --- 5b. optional: RETARGET the barrel to its fully-elevated 'ready' pose by the deploy's end (argv[5] = readyFrame) ---
# The rest/deployed pose should be combat-ready (barrel up). In the source the barrel pauses at the aiming angle for a
# long crew-loading hold before rising to the firing elevation, so a plain trim would deploy then sit then finish. Instead
# capture the barrel's local pose at the firing frame and re-key JUST the barrel bones to rise there over the deploy's
# back half — legs spread, then barrel elevates fully, no dead pause. Only the barrel/cannon bones are touched.
# Blender 4.4+/5.x: fcurves live in slotted channelbags (action.fcurves removed). Clear the given bones' channels so
# their existing keys don't fight a retarget. Works on both legacy and slotted actions.
def clear_bone_channels(act, bone_names):
    bags = ([act] if hasattr(act, "fcurves") else []) + \
           [cb for layer in getattr(act, "layers", []) for strip in layer.strips for cb in getattr(strip, "channelbags", [])]
    for cb in bags:
        for fc in list(cb.fcurves):
            if any(('pose.bones["%s"]' % bn) in fc.data_path for bn in bone_names):
                cb.fcurves.remove(fc)

if len(argv) > 5 and argv[5].strip():
    from mathutils import Quaternion
    ready_frame = int(argv[5])
    barrel_scale = float(argv[7]) if len(argv) > 7 and argv[7].strip() else 1.0   # >1 exaggerates the elevation (extrapolate)
    end_frame = int(argv[3]) if len(argv) > 3 else fmax
    mid = max(int(end_frame * 0.5), 1)
    barrel_bones = [bn for bn in bone_of.values() if any(k in bn.lower() for k in ("barrel", "cannon"))]
    scene.frame_set(ready_frame)
    ready = {bn: (arm.pose.bones[bn].rotation_quaternion.copy(), arm.pose.bones[bn].location.copy()) for bn in barrel_bones}
    clear_bone_channels(arm.animation_data.action, barrel_bones)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for bn in barrel_bones:      # rest (level) at mid-deploy, fully-elevated 'ready' (x barrel_scale) at the end
        pb = arm.pose.bones[bn]
        pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
        pb.keyframe_insert('rotation_quaternion', frame=mid); pb.keyframe_insert('location', frame=mid)
        rq, lc = ready[bn]
        ax, ang = rq.to_axis_angle(); rq = Quaternion(ax, ang * barrel_scale); lc = lc * barrel_scale   # amplify elevation (scale the angle) past the source's max
        pb.rotation_quaternion = rq; pb.location = lc
        pb.keyframe_insert('rotation_quaternion', frame=end_frame); pb.keyframe_insert('location', frame=end_frame)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("DEPLOY barrel retargeted to ready-frame %d over %d..%d (%d bones)" % (ready_frame, mid, end_frame, len(barrel_bones)))

# --- 5c. optional: SCALE the leg spread (argv[6] = factor; 0.5 = half as wide). The legs fold->spread in the source; we
#         scale the spread rotation via slerp(identity, full, factor) so the deployed stance is narrower, no re-authoring. ---
if len(argv) > 6 and argv[6].strip():
    leg_scale = float(argv[6])
    end_frame = int(argv[3]) if len(argv) > 3 else fmax
    spread_frame = max(int(end_frame * 0.5), 1)   # legs are fully spread by mid-deploy
    leg_bones = [bn for bn in bone_of.values() if "leg" in bn.lower()]
    scene.frame_set(fmin)                                                                          # true INITIAL (travel) pose
    folded = {bn: arm.pose.bones[bn].rotation_quaternion.copy() for bn in leg_bones}
    scene.frame_set(spread_frame)                                                                  # fully-spread pose
    full = {bn: arm.pose.bones[bn].rotation_quaternion.copy() for bn in leg_bones}
    scaled = {bn: folded[bn].slerp(full[bn], leg_scale) for bn in leg_bones}                       # 0 = initial, 1 = full spread
    clear_bone_channels(arm.animation_data.action, leg_bones)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for bn in leg_bones:      # INITIAL at start, SCALED spread by mid, held to the end (scale 0 = stay at initial width)
        pb = arm.pose.bones[bn]
        pb.rotation_quaternion = folded[bn]
        pb.keyframe_insert('rotation_quaternion', frame=fmin)
        pb.rotation_quaternion = scaled[bn]
        pb.keyframe_insert('rotation_quaternion', frame=spread_frame)
        pb.keyframe_insert('rotation_quaternion', frame=end_frame)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("DEPLOY legs scaled x%.2f from initial (%d bones), spread by %d held to %d" % (leg_scale, len(leg_bones), spread_frame, end_frame))

# --- 5d. optional: RECOIL-ON-FIRE tail — EXTRACT the source's own kickback (argv[8]=recoilSrcStart, argv[9]=recoilSrcEnd,
#         argv[10]=step default 2) and remap it onto the deployed pose as a tail after the deploy. The source clip already
#         animates a real firing recoil (the tube slams back + down then slowly runs out); we transfer that rigid motion,
#         expressed relative to the source's aim pose, onto OUR deployed hold — faithful to the original, not synthesized.
#         The runtime plays this tail once on ArtilleryStrikeStarted from the deployed hold (deployPoseTime = deployEnd/outEnd).
#         Same clip, no extra slot. Carriage/legs stay planted (only the barrel/cannon bones get keys). ---
recoil_out_end = None
if len(argv) > 8 and argv[8].strip():
    from mathutils import Matrix
    deploy_end = int(argv[3])
    rs = int(argv[8]); re = int(argv[9])                       # recoil sub-range IN THE SOURCE clip
    step = int(argv[10]) if len(argv) > 10 and argv[10].strip() else 2
    recoil_bones = [bn for bn in bone_of.values() if any(k in bn.lower() for k in ("barrel", "cannon"))]
    bone_to_src = {bone_of[p.name]: p for p in parts if p.name in bone_of}   # bone -> its source node (still animated 0..fmax)
    # parents before children so a child's local back-solves against the parent's ALREADY-posed recoil
    def bone_depth(bn):
        d = 0; b = arm.data.bones[bn].parent
        while b: d += 1; b = b.parent
        return d
    ordered = sorted([bn for bn in recoil_bones if bn in bone_to_src], key=bone_depth)

    # Phase A — read the source: capture each tube node's world matrix at the aim frame + across the recoil.
    frames = list(range(rs, re + 1, step))
    if frames[-1] != re: frames.append(re)
    scene.frame_set(rs)
    m_aim = {bn: bone_to_src[bn].matrix_world.copy() for bn in ordered}
    src_w = {bn: {} for bn in ordered}
    for t in frames:
        scene.frame_set(t)
        for bn in ordered:
            src_w[bn][t] = bone_to_src[bn].matrix_world.copy()

    # Phase B — write onto the bones: hold the scene at deploy_end (carriage/legs deployed), pose the recoil bones for each
    # mapped frame f = deploy_end + (t - rs), and key them. target = home @ (aim^-1 @ src_t)  = the source's relative motion
    # (in the aim's own frame) applied to our deployed pose. Parents first + a depsgraph update so children back-solve right.
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    scene.frame_set(deploy_end)
    m_home = {bn: arm.pose.bones[bn].matrix.copy() for bn in ordered}
    prev_q = {}
    def key_bone(bn, f):   # keyframe loc+rot, forcing quaternion continuity (else a sign flip makes Blender lerp the long way)
        pb = arm.pose.bones[bn]
        q = pb.rotation_quaternion.copy()
        if bn in prev_q and q.dot(prev_q[bn]) < 0.0: q.negate()
        pb.rotation_quaternion = q; prev_q[bn] = q
        pb.keyframe_insert('location', frame=f)
        pb.keyframe_insert('rotation_quaternion', frame=f)
    # A bone's OWN local translation is DROPPED by the bake (verified: sliding cannon2's local left its baked bbox unchanged) —
    # only ROTATION survives. But a bone's position derived from an ANCESTOR's rotation DOES bake (forward kinematics). So add a
    # hidden RECOIL-ARM bone with its pivot placed FAR from the tube, reparent the tube under it, and rotate the ARM: the tube
    # swings through a long arc that, over the recoil distance, reads as a near-straight backward SLIDE. The arm's ROTATION bakes;
    # the tube's arc is rebuilt by FK at runtime. Only the tube hangs off the arm (wheels/legs are untouched). argv[11] = slide
    # magnitude scale (default 1 = the source distance). Driven by the source's clean barrel-relative-to-cradle slide profile.
    driver = max(ordered, key=lambda bn: max((src_w[bn][t].translation - m_aim[bn].translation).length for t in frames))
    parent_bone = arm.data.bones[driver].parent
    cradle = parent_bone.name if parent_bone and parent_bone.name in bone_to_src else driver
    tube_root = cradle if cradle in m_home else driver
    mag = float(argv[11]) if len(argv) > 11 and argv[11].strip() else 1.0
    Sc_aim = src_w[cradle][rs]; Sb_aim = src_w[driver][rs]
    Cbar3 = (m_home[driver] @ Sb_aim.inverted()).to_3x3()
    slide = {}
    for t in frames:
        bt = Sc_aim @ (src_w[cradle][t].inverted() @ src_w[driver][t])   # barrel world, cradle FROZEN at aim = clean slide (no re-aim)
        slide[t] = (Cbar3 @ (bt.translation - Sb_aim.translation)) * mag  # slide vector, output space
    peak = max(slide.values(), key=lambda v: v.length)
    dist = peak.length or 1.0
    d = peak.normalized()                                      # slide direction (output)
    A = d.cross(Vector((0, 0, 1)))
    if A.length < 1e-4: A = d.cross(Vector((0, 1, 0)))
    A = A.normalized()                                         # arc rotation axis (perp to slide, horizontal-ish)
    R = 400.0                                                  # pivot distance: large -> the arc ~ a straight slide (tiny re-aim)
    radius = A.cross(d).normalized()
    tube_head = m_home[tube_root].translation.copy()
    pivot = tube_head - radius * R                             # place the pivot R away, perpendicular to the slide

    # insert a RecoilArm bone (head=pivot) between the tube and its parent
    bpy.ops.object.mode_set(mode='EDIT')
    ra = arm_data.edit_bones.new("RecoilArm"); ra_name = ra.name
    ra.head = pivot; ra.tail = pivot + A * 10.0
    teb = arm_data.edit_bones[tube_root]
    ra.parent = teb.parent
    teb.parent = ra
    bpy.ops.object.mode_set(mode='POSE')
    ra_rest = arm.data.bones[ra_name].matrix_local.copy()     # armature-space rest of the arm
    scene.frame_set(deploy_end)                               # parents held at their deployed pose while we back-solve
    prev_q.clear()
    def key_arm(f):
        pb = arm.pose.bones[ra_name]
        q = pb.rotation_quaternion.copy()
        if 'ra' in prev_q and q.dot(prev_q['ra']) < 0.0: q.negate()
        pb.rotation_quaternion = q; prev_q['ra'] = q
        pb.keyframe_insert('location', frame=f); pb.keyframe_insert('rotation_quaternion', frame=f)
    for hold in (0, deploy_end):                              # identity through the whole deploy so it can't disturb it
        arm.pose.bones[ra_name].matrix = ra_rest; bpy.context.view_layer.update(); key_arm(hold)
    for t in frames:
        f = deploy_end + (t - rs)
        theta = -(slide[t].length) / R * (1 if slide[t].dot(d) >= 0 else -1)   # arc length R*theta along the slide dir
        tgt = Matrix.Translation(pivot) @ Matrix.Rotation(theta, 4, A) @ Matrix.Translation(-pivot) @ ra_rest
        arm.pose.bones[ra_name].matrix = tgt; bpy.context.view_layer.update(); key_arm(f)
    recoil_out_end = deploy_end + (frames[-1] - rs)
    arm.pose.bones[ra_name].matrix = ra_rest; bpy.context.view_layer.update(); key_arm(recoil_out_end)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("DEPLOY recoil (ARC slide x%g, R=%g, peak=%.1f) tail %d..%d via RecoilArm; tube '%s'" %
          (mag, R, dist, deploy_end, recoil_out_end, tube_root))

# --- 6. bind each mesh 100% to the bone of its nearest animated ancestor (rigid) ---
def anim_ancestor(o):
    while o:
        if o.name in bone_of:
            return o.name
        o = o.parent
    return None

scene.frame_set(fmin)   # CRITICAL: bind at the rest frame (matches the armature rest), NOT wherever a retarget left the
                        # scene — else the mesh is baked in a posed (spread) position and the animation deforms it AGAIN.
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

# --- 8. export GLB, trimmed to the DEPLOY (+recoil tail) sub-range if given (argv: in out [start] [end] ...) ---
if len(argv) >= 4:
    trim_end = recoil_out_end if recoil_out_end is not None else int(argv[3])   # extend past the deploy to include the recoil tail
    scene.frame_start, scene.frame_end = int(argv[2]), trim_end
    print("DEPLOY trim to frames %d..%d" % (scene.frame_start, scene.frame_end))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=outp, export_format='GLB', export_animations=True,
                          export_frame_range=True, export_yup=True)
print("DEPLOY wrote:", outp)
