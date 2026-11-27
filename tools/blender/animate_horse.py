import bpy, sys, math
from mathutils import Quaternion, Vector, Matrix
argv = sys.argv[sys.argv.index("--")+1:]
src, out_fbx = argv[0], argv[1]
FPS = 30
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

# --- rename bones to clear names ---
ren = {}
def chain(prefix, names):
    for i, n in enumerate(names): ren[n] = f"{prefix}_{i}"
chain("FrontL", [f"tripo::0_Left_Limb_{i}" for i in range(5)])
chain("FrontR", [f"tripo::0_Right_Limb_{i}" for i in range(5)])
chain("HindL", [f"bone_{i}" for i in range(21, 26)])
chain("HindR", [f"tripo::1_Right_Limb_{i}" for i in range(5)])
chain("Tail", ["bone_31", "tripo::Tail_0", "tripo::1_Left_Limb_0", "tripo::1_Left_Limb_1", "tripo::1_Left_Limb_2"])
chain("Spine", [f"tripo::Spine_{i}" for i in range(3)])
chain("Neck", ["tripo::Head_0", "tripo::Head_1", "tripo::Head_2"])
ren["tripo::Head_3"] = "Head"; ren["tripo::Head_4"] = "Jaw"
ren["bone_8"] = "EarL"; ren["bone_9"] = "EarR"; ren["tripo::Root"] = "Root"
for b in arm.data.bones:
    if b.name in ren:
        old = b.name; b.name = ren[old]
        vg = mesh.vertex_groups.get(old)
        if vg: vg.name = ren[old]

# --- scale to real horse size (~2.4 m long), face +Z forward for Unity ---
# model front is -X. Rotate so front is -Y in Blender (exports as +Z forward in Unity with default axes)
for o in (arm,):
    o.rotation_euler = (0, 0, math.radians(-90))
    s = 2.4 / mesh.dimensions.x
    o.scale = (s, s, s)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
mesh.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

LAT = Vector((1, 0, 0))   # lateral axis after rotation (front = -Y)
UP = Vector((0, 0, 1))
FWD = Vector((0, -1, 0))
pb = arm.pose.bones
for p in pb: p.rotation_mode = 'QUATERNION'

def rot(name, axis, deg):
    b = arm.data.bones[name]
    R = b.matrix_local.to_3x3()
    return Quaternion(R.inverted() @ axis, math.radians(deg))
def locv(name, v):
    b = arm.data.bones[name]
    return b.matrix_local.to_3x3().inverted() @ v

def key_all(frame):
    for p in pb:
        p.keyframe_insert("rotation_quaternion", frame=frame)
        p.keyframe_insert("location", frame=frame)

def reset():
    for p in pb:
        p.rotation_quaternion = Quaternion(); p.location = Vector()

LEGS = ["FrontL", "FrontR", "HindL", "HindR"]
def pose_leg(leg, phase, swing, lift, front):
    # phase 0..1; stance first half (leg moves back), swing second half (leg moves forward, flexed)
    s = math.sin(2*math.pi*phase)
    up = max(0.0, -math.sin(2*math.pi*phase))  # >0 during swing
    q0 = rot(f"{leg}_0", LAT, -swing*s)          # + = forward (toward -Y): rotation about +X moves -Z toward... sign checked by preview
    if front:
        q1 = rot(f"{leg}_1", LAT, lift*up*0.3)
        q2 = rot(f"{leg}_2", LAT, -lift*up)
        q3 = rot(f"{leg}_3", LAT, -lift*up*0.6)
    else:
        q1 = rot(f"{leg}_1", LAT, -lift*up*0.4)
        q2 = rot(f"{leg}_2", LAT, lift*up*0.7)
        q3 = rot(f"{leg}_3", LAT, -lift*up*0.6)
    pb[f"{leg}_0"].rotation_quaternion = q0
    pb[f"{leg}_1"].rotation_quaternion = q1
    pb[f"{leg}_2"].rotation_quaternion = q2
    pb[f"{leg}_3"].rotation_quaternion = q3

def make_cycle(name, seconds, offsets, swing, lift, bob, head, spine=0.0, tail=8.0, loop=True):
    act = bpy.data.actions.new(name)
    arm.animation_data_create(); arm.animation_data.action = act
    n = max(2, round(seconds*FPS))
    for f in range(n+1):
        t = f/n
        reset()
        for leg in LEGS:
            pose_leg(leg, (t + offsets[leg]) % 1.0, swing, lift, leg.startswith("Front"))
        beats = 2 if name in ("Walk", "Trot") else 1
        pb["Root"].location = locv("Root", UP*bob*math.sin(2*math.pi*beats*t))
        pb["Neck_0"].rotation_quaternion = rot("Neck_0", LAT, head*math.sin(2*math.pi*beats*t + 0.6))
        pb["Spine_1"].rotation_quaternion = rot("Spine_1", LAT, spine*math.sin(2*math.pi*t))
        pb["Tail_1"].rotation_quaternion = rot("Tail_1", FWD, tail*math.sin(2*math.pi*t)) @ rot("Tail_1", LAT, -spine*1.5)
        key_all(f+1)
    act.use_fake_user = True
    return act

make_cycle("Walk",   1.10, {"HindL":0.0, "FrontL":0.25, "HindR":0.5, "FrontR":0.75}, swing=18, lift=35, bob=0.02, head=4)
make_cycle("Trot",   0.70, {"FrontL":0.0, "HindR":0.0, "FrontR":0.5, "HindL":0.5}, swing=24, lift=55, bob=0.05, head=3)
make_cycle("Canter", 0.60, {"HindR":0.0, "HindL":0.3, "FrontR":0.3, "FrontL":0.6}, swing=32, lift=70, bob=0.07, head=7, spine=4)
make_cycle("Gallop", 0.45, {"HindL":0.0, "HindR":0.1, "FrontL":0.55, "FrontR":0.65}, swing=30, lift=75, bob=0.07, head=7, spine=5)

# Idle: breathing, head, tail, ears
act = bpy.data.actions.new("Idle"); arm.animation_data.action = act
n = 3*FPS
for f in range(n+1):
    t = f/n; reset()
    pb["Spine_1"].rotation_quaternion = rot("Spine_1", LAT, 1.0*math.sin(2*math.pi*2*t))
    pb["Neck_0"].rotation_quaternion = rot("Neck_0", LAT, 3*math.sin(2*math.pi*t)) @ rot("Neck_0", UP, 4*math.sin(2*math.pi*t+1))
    pb["Tail_1"].rotation_quaternion = rot("Tail_1", FWD, 12*math.sin(2*math.pi*2*t))
    pb["EarL"].rotation_quaternion = rot("EarL", LAT, 10*max(0, math.sin(2*math.pi*3*t)))
    key_all(f+1)
act.use_fake_user = True

# Stamp (countdown fidget): alternate front hoof lifts
act = bpy.data.actions.new("Stamp"); arm.animation_data.action = act
n = round(1.0*FPS)
for f in range(n+1):
    t = f/n; reset()
    for leg, ph in (("FrontL", 0.0), ("FrontR", 0.5)):
        up = max(0.0, math.sin(2*math.pi*(t+ph)))**2
        pb[f"{leg}_0"].rotation_quaternion = rot(f"{leg}_0", LAT, -15*up)
        pb[f"{leg}_2"].rotation_quaternion = rot(f"{leg}_2", LAT, -60*up)
    pb["Neck_0"].rotation_quaternion = rot("Neck_0", LAT, 8*math.sin(2*math.pi*2*t))
    pb["Tail_1"].rotation_quaternion = rot("Tail_1", FWD, 20*math.sin(2*math.pi*2*t))
    key_all(f+1)
act.use_fake_user = True

# Jump (non-loop 0.8s): crouch, extend, tuck, land
act = bpy.data.actions.new("Jump"); arm.animation_data.action = act
keys = [(0.0, 0, 0, 0), (0.15, -0.08, 8, 0), (0.3, 0.05, -15, 0.3), (0.55, 0.05, 5, 1.0), (0.8, -0.04, 0, 0)]
for (tt, dz, pitch, tuck) in keys:
    reset()
    pb["Root"].location = locv("Root", UP*dz)
    pb["Root"].rotation_quaternion = rot("Root", LAT, pitch)
    for leg in LEGS:
        fr = leg.startswith("Front")
        pb[f"{leg}_0"].rotation_quaternion = rot(f"{leg}_0", LAT, (-45 if fr else 40)*tuck)
        pb[f"{leg}_2"].rotation_quaternion = rot(f"{leg}_2", LAT, (-90 if fr else 70)*tuck)
    key_all(round(tt*FPS)+1)
act.use_fake_user = True

# Stagger (hit reaction 0.8s): body sways sideways, head tosses
act = bpy.data.actions.new("Stagger"); arm.animation_data.action = act
n = round(0.8*FPS)
for f in range(n+1):
    t = f/n; reset(); d = math.sin(math.pi*t)
    pb["Root"].rotation_quaternion = rot("Root", FWD, 10*d*math.sin(2*math.pi*2*t))
    pb["Neck_0"].rotation_quaternion = rot("Neck_0", LAT, -20*d) @ rot("Neck_0", UP, 15*d*math.sin(2*math.pi*1.5*t))
    pb["FrontL_0"].rotation_quaternion = rot("FrontL_0", LAT, -20*d)
    pb["FrontL_2"].rotation_quaternion = rot("FrontL_2", LAT, -50*d)
    key_all(f+1)
act.use_fake_user = True

arm.animation_data.action = None
reset()
arm.name = "Horse"; mesh.name = "HorseMesh"
bpy.ops.wm.save_as_mainfile(filepath=out_fbx.replace(".fbx", ".blend"))
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=False, object_types={'ARMATURE','MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y')
print("DONE", [a.name for a in bpy.data.actions])
