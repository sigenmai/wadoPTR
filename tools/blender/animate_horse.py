"""Tripo の馬モデル（四足リグ入り FBX）に歩様アニメーションを付けて書き出す。

使い方:
  blender -b --python tools/blender/animate_horse.py -- <入力.fbx> <出力.fbx>

歩様の作り方（参考文献は docs/horse-animation-reference.md）:
  - 各脚を「接地（stance）」と「遊脚（swing）」に分け、接地割合 = duty factor を歩様ごとに設定
      常歩 0.62 / 速歩 0.42 / 駆歩 0.32 / 襲歩 0.22
  - 着地順（右手前）:
      常歩: 左後→左前→右後→右前（4拍、等間隔）
      速歩: 左前+右後 → 右前+左後（2拍、斜対）
      駆歩: 左後 → 右後+左前 → 右前（3拍）→ 空中
      襲歩: 左後 → 右後 → 左前 → 右前（4拍）→ 空中（脚を集めた姿勢）
  - 接地中: 脚は前から後ろへ一定速度で動き（蹄が地面に止まって見える）、球節は荷重で沈む
  - 遊脚中: 前半で関節が屈曲し中盤で最大、後半で伸びて次の着地に備える
  - 胴の上下・前後の傾き・首の振りは「どの脚に体重がかかっているか」から計算する
"""
import bpy, sys, math
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_fbx = argv[0], argv[1]
FPS = 30
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

# ---------------------------------------------------------------- 骨の名前を整理
ren = {}
def chain(prefix, names):
    for i, n in enumerate(names):
        ren[n] = f"{prefix}_{i}"
chain("FrontL", [f"tripo::0_Left_Limb_{i}" for i in range(5)])
chain("FrontR", [f"tripo::0_Right_Limb_{i}" for i in range(5)])
chain("HindL", [f"bone_{i}" for i in range(21, 26)])
chain("HindR", [f"tripo::1_Right_Limb_{i}" for i in range(5)])
chain("Tail", ["bone_31", "tripo::Tail_0", "tripo::1_Left_Limb_0", "tripo::1_Left_Limb_1", "tripo::1_Left_Limb_2"])
chain("Spine", [f"tripo::Spine_{i}" for i in range(3)])
chain("Neck", ["tripo::Head_0", "tripo::Head_1", "tripo::Head_2"])
ren.update({"tripo::Head_3": "Head", "tripo::Head_4": "Jaw", "bone_8": "EarL", "bone_9": "EarR", "tripo::Root": "Root"})
for b in arm.data.bones:
    if b.name in ren:
        old = b.name
        b.name = ren[old]
        vg = mesh.vertex_groups.get(old)
        if vg:
            vg.name = ren[old]

# ---------------------------------------------------------------- 実寸（全長2.4m）・正面を -Y に
s = 2.4 / mesh.dimensions.x
arm.rotation_euler = (0, 0, math.radians(90))   # 元モデルの正面 -X → -Y
arm.scale = (s, s, s)
bpy.context.view_layer.update()
for o in (arm, mesh):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# 角度の向き: PITCH 軸まわりの + は「脚先が前へ」「鼻先が上へ」「胴の前が上へ」
PITCH = Vector((-1, 0, 0))
ROLL = Vector((0, 1, 0))
YAW = Vector((0, 0, 1))
UP = Vector((0, 0, 1))
pb = arm.pose.bones
for p in pb:
    p.rotation_mode = 'QUATERNION'

def rot(name, axis, deg):
    R = arm.data.bones[name].matrix_local.to_3x3()
    return Quaternion(R.inverted() @ axis, math.radians(deg))

def add_rot(name, axis, deg):
    pb[name].rotation_quaternion = pb[name].rotation_quaternion @ rot(name, axis, deg)

def set_loc(name, v):
    pb[name].location = arm.data.bones[name].matrix_local.to_3x3().inverted() @ v

def reset():
    for p in pb:
        p.rotation_quaternion = Quaternion()
        p.location = Vector()

def key_all(frame):
    for p in pb:
        p.keyframe_insert("rotation_quaternion", frame=frame)
        p.keyframe_insert("location", frame=frame)

def smooth(u):
    return u * u * (3 - 2 * u)

LEGS = ["FrontL", "FrontR", "HindL", "HindR"]

def limb_state(phase, duty):
    """phase 0 = 着地。戻り値 (前後角の割合 -1..+1, 屈曲 0..1, 荷重 0..1)"""
    if phase < duty:
        u = phase / duty
        return 0.9 - 1.9 * u, 0.0, math.sin(math.pi * u)
    u = (phase - duty) / (1 - duty)
    flex = math.sin(math.pi * min(1.0, u * 1.15))   # 屈曲のピークは遊脚中盤よりやや前
    return -1.0 + 1.9 * smooth(u), max(0.0, flex), 0.0

def pose_leg(leg, swing_pos, flex, load, g):
    front = leg.startswith("Front")
    add_rot(f"{leg}_0", PITCH, g["amp"] * swing_pos)
    if front:
        add_rot(f"{leg}_1", PITCH, g["fore_lift"] * flex * 0.35)      # 前腕を持ち上げる
        add_rot(f"{leg}_2", PITCH, -g["fore_lift"] * flex)            # 腕節（膝）を後ろへ折る
        add_rot(f"{leg}_3", PITCH, -g["fore_lift"] * 0.5 * flex + 12 * load * g["load_k"])  # 球節: 遊脚で屈曲、接地で沈む
    else:
        add_rot(f"{leg}_1", PITCH, -g["hind_lift"] * 0.45 * flex)     # 下腿
        add_rot(f"{leg}_2", PITCH, g["hind_lift"] * flex)             # 飛節で管骨を前へ
        add_rot(f"{leg}_3", PITCH, -g["hind_lift"] * 0.5 * flex + 10 * load * g["load_k"])

GAITS = {
    # T: 1完歩の秒数, duty: 接地割合, off: 着地タイミング, amp: 脚の振り角
    "Walk":   dict(T=1.10, duty=0.62, off={"HindL": 0.0, "FrontL": 0.25, "HindR": 0.5, "FrontR": 0.75},
                   amp=18, fore_lift=55, hind_lift=40, load_k=0.6, bob=0.025, pitch=1.5, neck=6, neck_mode="nod", spine=0, tail=6),
    "Trot":   dict(T=0.70, duty=0.42, off={"FrontL": 0.0, "HindR": 0.0, "FrontR": 0.5, "HindL": 0.5},
                   amp=24, fore_lift=80, hind_lift=60, load_k=0.8, bob=0.06, pitch=1.0, neck=2, neck_mode="steady", spine=0, tail=5),
    "Canter": dict(T=0.62, duty=0.32, off={"HindL": 0.0, "HindR": 0.2, "FrontL": 0.2, "FrontR": 0.4},
                   amp=30, fore_lift=95, hind_lift=70, load_k=1.0, bob=0.07, pitch=3.0, neck=7, neck_mode="rock", spine=3, tail=10),
    "Gallop": dict(T=0.45, duty=0.22, off={"HindL": 0.0, "HindR": 0.1, "FrontL": 0.3, "FrontR": 0.4},
                   amp=36, fore_lift=105, hind_lift=80, load_k=1.0, bob=0.07, pitch=3.0, neck=9, neck_mode="rock", spine=6, tail=14),
}

def sample(g, t):
    st = {}
    for leg in LEGS:
        st[leg] = limb_state((t - g["off"][leg]) % 1.0, g["duty"])
    front_load = st["FrontL"][2] + st["FrontR"][2]
    hind_load = st["HindL"][2] + st["HindR"][2]
    front_pos = (st["FrontL"][0] + st["FrontR"][0]) / 2
    hind_pos = (st["HindL"][0] + st["HindR"][0]) / 2
    return st, front_load, hind_load, front_pos, hind_pos

def make_gait(name, g):
    act = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = act
    n = max(4, round(g["T"] * FPS))
    # 胴の上下・傾きの平均を引くため先に1周計算
    samples = [sample(g, f / n) for f in range(n)]
    mean_total = sum(s[1] + s[2] for s in samples) / n
    mean_diff = sum(s[2] - s[1] for s in samples) / n
    for f in range(n + 1):
        t = f / n
        st, fl, hl, fp, hp = samples[f % n]
        reset()
        for leg in LEGS:
            pose_leg(leg, *st[leg], g)
        total = fl + hl
        # 荷重が大きいほど胴が沈む（空中期に最も高い）
        set_loc("Root", UP * (-g["bob"] * (total - mean_total)))
        # 後肢で蹴っている間は前が上がり、前肢で受けると前が下がる（駆歩・襲歩の揺れ）
        pitch = g["pitch"] * ((hl - fl) - mean_diff) * 0.5
        add_rot("Root", PITCH, pitch)
        # 首: 常歩は前肢の荷重でうなずく / 速歩はほぼ固定 / 駆歩・襲歩は胴と逆に振って頭を安定させつつ前後に伸縮
        if g["neck_mode"] == "nod":
            neck = -g["neck"] * (fl - sum(s[1] for s in samples) / n)
        elif g["neck_mode"] == "steady":
            neck = -pitch + g["neck"] * math.sin(2 * math.pi * 2 * t)
        else:
            neck = -pitch * 0.7 + g["neck"] * math.sin(2 * math.pi * t + 1.2)
        add_rot("Neck_0", PITCH, neck * 0.6)
        add_rot("Neck_1", PITCH, neck * 0.4)
        add_rot("Head", PITCH, -neck * 0.5)   # 頭は首の動きを少し打ち消す
        # 背中: 脚を集める（前肢が後ろ・後肢が前）と丸まり、伸ばすと反る
        if g["spine"]:
            spine = g["spine"] * (hp - fp) * 0.5
            add_rot("Spine_0", PITCH, -spine)
            add_rot("Spine_1", PITCH, -spine)
        # しっぽ: 胴の傾きに遅れてついてくる＋左右の揺れ
        add_rot("Tail_1", PITCH, -pitch * 1.5 + 4 * math.sin(2 * math.pi * t - 1.0))
        add_rot("Tail_2", ROLL, g["tail"] * 0.5 * math.sin(2 * math.pi * t))
        add_rot("Tail_3", ROLL, g["tail"] * 0.5 * math.sin(2 * math.pi * t - 0.8))
        key_all(f + 1)
    act.use_fake_user = True

for name, g in GAITS.items():
    make_gait(name, g)

# ---------------------------------------------------------------- 待機
act = bpy.data.actions.new("Idle")
arm.animation_data.action = act
n = 4 * FPS
for f in range(n + 1):
    t = f / n
    reset()
    add_rot("Spine_1", ROLL, 0.6 * math.sin(2 * math.pi * 3 * t))            # 呼吸
    add_rot("Neck_0", PITCH, 3 * math.sin(2 * math.pi * t))
    add_rot("Neck_1", YAW, 5 * math.sin(2 * math.pi * t + 1.0))
    add_rot("Tail_1", ROLL, 10 * math.sin(2 * math.pi * 2 * t))
    add_rot("Tail_3", ROLL, 10 * math.sin(2 * math.pi * 2 * t - 0.8))
    add_rot("EarL", PITCH, 15 * max(0.0, math.sin(2 * math.pi * 3 * t)) ** 2)
    add_rot("EarR", PITCH, 15 * max(0.0, math.sin(2 * math.pi * 2 * t + 2)) ** 2)
    # 片方の後肢で休む（重心を少し移す）
    add_rot("HindR_2", PITCH, 8)
    add_rot("HindR_3", PITCH, -12)
    key_all(f + 1)
act.use_fake_user = True

# ---------------------------------------------------------------- 足踏み（カウントダウン）
act = bpy.data.actions.new("Stamp")
arm.animation_data.action = act
n = round(1.2 * FPS)
for f in range(n + 1):
    t = f / n
    reset()
    for leg, ph in (("FrontL", 0.0), ("FrontR", 0.5)):
        up = max(0.0, math.sin(2 * math.pi * (t + ph))) ** 2
        add_rot(f"{leg}_0", PITCH, 10 * up)
        add_rot(f"{leg}_2", PITCH, -75 * up)
        add_rot(f"{leg}_3", PITCH, -35 * up)
    add_rot("Neck_0", PITCH, 6 * math.sin(2 * math.pi * 2 * t))
    add_rot("Head", YAW, 8 * math.sin(2 * math.pi * t))
    add_rot("Tail_1", ROLL, 18 * math.sin(2 * math.pi * 2 * t))
    key_all(f + 1)
act.use_fake_user = True

# ---------------------------------------------------------------- ジャンプ（ループなし 0.9 秒）
# 踏み切り（後肢で沈む）→ 前肢を畳んで前上がり → 空中で全肢を畳む → 前肢から着地
act = bpy.data.actions.new("Jump")
arm.animation_data.action = act
keys = [
    # t,    沈み,  胴pitch, 前肢畳み, 後肢畳み, 首
    (0.00,  0.00,   0,      0.0,      0.0,      0),
    (0.15, -0.12,  -4,      0.3,      0.0,     -8),
    (0.30,  0.05,  14,      1.0,      0.2,     10),
    (0.50,  0.08,   0,      1.0,      1.0,      0),
    (0.70,  0.02, -10,      0.2,      0.8,    -10),
    (0.90,  0.00,   0,      0.0,      0.0,      0),
]
for (tt, dz, pitch, ftuck, htuck, neck) in keys:
    reset()
    set_loc("Root", UP * dz)
    add_rot("Root", PITCH, pitch)
    for leg in ("FrontL", "FrontR"):
        add_rot(f"{leg}_0", PITCH, 35 * ftuck)
        add_rot(f"{leg}_2", PITCH, -110 * ftuck)
        add_rot(f"{leg}_3", PITCH, -40 * ftuck)
    for leg in ("HindL", "HindR"):
        add_rot(f"{leg}_0", PITCH, -25 * htuck + 15 * ftuck * (1 - htuck))
        add_rot(f"{leg}_2", PITCH, 70 * htuck)
        add_rot(f"{leg}_3", PITCH, -40 * htuck)
    add_rot("Neck_0", PITCH, neck)
    add_rot("Tail_1", PITCH, -pitch * 1.5)
    key_all(round(tt * FPS) + 1)
act.use_fake_user = True

# ---------------------------------------------------------------- よろけ（被弾 0.8 秒）
act = bpy.data.actions.new("Stagger")
arm.animation_data.action = act
n = round(0.8 * FPS)
for f in range(n + 1):
    t = f / n
    reset()
    d = math.sin(math.pi * t)
    add_rot("Root", ROLL, 8 * d * math.sin(2 * math.pi * 1.5 * t))
    add_rot("Root", PITCH, 4 * d)
    add_rot("Neck_0", PITCH, 18 * d)
    add_rot("Neck_1", YAW, 15 * d * math.sin(2 * math.pi * 1.5 * t))
    add_rot("FrontL_0", PITCH, 20 * d)
    add_rot("FrontL_2", PITCH, -70 * d)
    add_rot("HindR_0", PITCH, -10 * d)
    add_rot("EarL", PITCH, -25 * d)
    add_rot("EarR", PITCH, -25 * d)
    key_all(f + 1)
act.use_fake_user = True

arm.animation_data.action = None
reset()
arm.name = "Horse"
mesh.name = "HorseMesh"
bpy.ops.wm.save_as_mainfile(filepath=out_fbx.replace(".fbx", ".blend"))
bpy.ops.export_scene.fbx(
    filepath=out_fbx, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y',
    apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True)
print("DONE", sorted(a.name for a in bpy.data.actions))
