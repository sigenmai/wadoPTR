"""骨のない四足動物のモデルを、軽量化 → 骨を自動配置 → 自動ウェイト → 歩様アニメーション付きで書き出す。
（ラクダ・骨の馬で使用。animate_camel.py を一般化したもの）

使い方:
  blender -b --python tools/blender/rig_quadruped.py -- <入力 .fbx/.obj> <出力.fbx> <プロファイル camel|horse> <名前> [頭の向き補正 Z回転(度)=0]

プロファイル：全長・脚の高さ・関節の高さ・歩様（ラクダ／馬）をまとめた設定。
頭の向き補正：元モデルの頭が +X を向くように回す角度。

- ポリゴン数を約2.2万に削減（UV とテクスチャは維持）
- 全長 3.0m にそろえる
- 脚の骨の位置は、メッシュを高さごとに輪切りにして脚の断面の中心を取って決める
- 歩様はラクダの特徴に合わせる：常歩は側対歩順の4拍、中速は側対歩（同じ側の前後脚を同時に出す）、
  速い走りは回転襲歩（後ろ左 → 後ろ右 → 前右 → 前左）
"""
import bpy, sys, math, os
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gait_lib

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_fbx, PROFILE, NAME = argv[0], argv[1], argv[2], argv[3]
ROTZ = float(argv[4]) if len(argv) > 4 and not argv[4].startswith("--") else 0.0
RIGID = "--rigid-islands" in argv   # 骨格モデルなど「部品ごとに固い」モデル用：部品単位で1本の骨に付ける
TARGET_TRIS = 22000
P = {
    # length: 全長 / leg_z: これより下を脚とみなす / neck_z: 首の断面を取る最低の高さ
    # front/hind: 付け根から下の関節の高さ（bz = 胴の下面の高さ）
    "camel": dict(length=3.0, leg_z=0.9, neck_z=1.0, neck_w=0.25, spine_up=0.15,
                  front=lambda bz: [bz + 0.25, bz - 0.05, 0.62, 0.16, 0.0],
                  hind=lambda bz: [bz + 0.35, bz - 0.05, 0.70, 0.16, 0.0]),
    "horse": dict(length=2.4, leg_z=0.72, neck_z=0.8, neck_w=0.2, spine_up=0.2,
                  front=lambda bz: [bz + 0.35, bz - 0.03, 0.48, 0.14, 0.0],
                  hind=lambda bz: [bz + 0.45, bz - 0.05, 0.55, 0.14, 0.0]),
}[PROFILE]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = gait_lib.FPS
if src.lower().endswith(".obj"):
    bpy.ops.wm.obj_import(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.rotation_euler.z += math.radians(ROTZ)

# ---------------------------------------------------------------- 実寸・軽量化（ここで頭は +X）
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
s = P["length"] / mesh.dimensions.x
mesh.scale = (s, s, s)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
dec = mesh.modifiers.new("decimate", 'DECIMATE')
dec.ratio = min(1.0, TARGET_TRIS / tris)
bpy.ops.object.modifier_apply(modifier=dec.name)
# 底を z=0、胴の中心を x=y=0 付近へ
vs = [v.co.copy() for v in mesh.data.vertices]
minz = min(v.z for v in vs)
cy = (min(v.y for v in vs) + max(v.y for v in vs)) / 2
for v in mesh.data.vertices:
    v.co.z -= minz
    v.co.y -= cy
vs = [v.co.copy() for v in mesh.data.vertices]
XMIN, XMAX = min(v.x for v in vs), max(v.x for v in vs)
H = max(v.z for v in vs)

# ---------------------------------------------------------------- 形の計測
def centroid(pts):
    n = len(pts)
    return Vector((sum(p.x for p in pts) / n, sum(p.y for p in pts) / n, sum(p.z for p in pts) / n)) if n else None

# 脚：低い位置（z < 0.9）の頂点を前後・左右で4つに分ける
low = [v for v in vs if v.z < P["leg_z"]]
xs_low = sorted(v.x for v in low)
x_split = (xs_low[0] + xs_low[-1]) / 2
def leg_pts(front, left):
    return [v for v in low if ((v.x > x_split) == front) and ((v.y > 0) == left)]

def leg_joint(pts, z, band=0.05):
    sl = [p for p in pts if abs(p.z - z) < band]
    c = centroid(sl)
    if c is None:
        c = centroid(pts)
    return Vector((c.x, c.y, z))

# 胴の下面（脚の付け根の高さ）：前脚・後脚の位置で一番下にある「脚でない」頂点
def belly_z(x):
    sl = [v for v in vs if abs(v.x - x) < 0.12 and v.z > P["leg_z"]]
    return min(v.z for v in sl) if sl else P["leg_z"] + 0.2

# 胴の下面は前後の脚の間（脚の頂点が混ざらない位置）で測る
_fx = centroid(leg_pts(True, True)).x
_hx = centroid(leg_pts(False, True)).x
BELLY = belly_z((_fx + _hx) / 2)
legs = {}
for name, front, left in (("FrontL", True, True), ("FrontR", True, False), ("HindL", False, True), ("HindR", False, False)):
    pts = leg_pts(front, left)
    foot = leg_joint(pts, 0.03, 0.04)
    bz = BELLY
    # 前：肩 → 肘 → 腕節 → 球節 → 地面 ／ 後：腰 → 膝 → 飛節 → 球節 → 地面
    heights = P["front"](bz) if front else P["hind"](bz)
    joints = []
    for h in heights:
        lz = P["leg_z"]
        j = leg_joint(pts, min(h, lz - 0.02)) if h < lz else leg_joint(pts, lz - 0.05)
        j.z = h
        joints.append(j)
    joints[0].y *= 0.6   # 付け根は胴の内側へ
    legs[name] = joints

# 背骨：後脚の付け根から前脚の付け根まで、胴の中くらいの高さ
hip_x = (legs["HindL"][0].x + legs["HindR"][0].x) / 2
sh_x = (legs["FrontL"][0].x + legs["FrontR"][0].x) / 2
spine_z = (legs["HindL"][0].z + legs["FrontL"][0].z) / 2 + P["spine_up"]
spine = [Vector((hip_x - 0.15, 0, spine_z)), Vector((hip_x + (sh_x - hip_x) * 0.4, 0, spine_z + 0.05)),
         Vector((hip_x + (sh_x - hip_x) * 0.75, 0, spine_z + 0.05)), Vector((sh_x + 0.1, 0, spine_z))]

# 首：胴の前から頭の先まで、x ごとの断面の中心をたどる
neck_start = spine[-1]
head_tip_x = XMAX
neck_pts = []
for i in range(1, 6):
    x = neck_start.x + (head_tip_x - neck_start.x) * i / 6
    sl = [v for v in vs if abs(v.x - x) < 0.06 and v.z > P["neck_z"] and abs(v.y) < P["neck_w"]]
    c = centroid(sl)
    if c:
        neck_pts.append(Vector((x, 0, c.z)))
tip_sl = [v for v in vs if v.x > XMAX - 0.08]
head_tip = Vector((XMAX, 0, centroid(tip_sl).z))

# しっぽ：一番後ろ・胴の高さ付近
tail_root = Vector((XMIN + 0.15, 0, spine_z + 0.1))
tail = [tail_root, tail_root + Vector((-0.08, 0, -0.2)), tail_root + Vector((-0.1, 0, -0.45)),
        tail_root + Vector((-0.1, 0, -0.65)), tail_root + Vector((-0.1, 0, -0.8))]

print("MEASURE belly", round(BELLY, 2), "spine_z", round(spine_z, 2), "neck", [tuple(round(c, 2) for c in p) for p in neck_pts],
      "head_tip", tuple(round(c, 2) for c in head_tip))
for k, j in legs.items():
    print("LEG", k, [tuple(round(c, 2) for c in p) for p in j])

# ---------------------------------------------------------------- 骨を作る
arm_data = bpy.data.armatures.new(NAME + "Armature")
arm = bpy.data.objects.new(NAME, arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones

def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head, b.tail = head, tail
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b

bone("Root", Vector((hip_x * 0.5 + sh_x * 0.5, 0, 0)), Vector((hip_x * 0.5 + sh_x * 0.5, 0, 0.5)))
bone("Spine_0", spine[0], spine[1], "Root")
bone("Spine_1", spine[1], spine[2], "Spine_0", True)
bone("Spine_2", spine[2], spine[3], "Spine_1", True)
chain = [neck_start] + neck_pts[:3]
bone("Neck_0", chain[0], chain[1], "Spine_2", True)
bone("Neck_1", chain[1], chain[2], "Neck_0", True)
bone("Neck_2", chain[2], chain[3], "Neck_1", True)
bone("Head", chain[3], head_tip, "Neck_2", True)
ear_base = chain[3] + (head_tip - chain[3]) * 0.3 + Vector((0, 0, 0.12))
bone("EarL", ear_base + Vector((0, 0.06, 0)), ear_base + Vector((-0.05, 0.12, 0.08)), "Head")
bone("EarR", ear_base + Vector((0, -0.06, 0)), ear_base + Vector((-0.05, -0.12, 0.08)), "Head")
bone("Tail_0", tail[0], tail[1], "Spine_0")
for i in range(1, 4):
    bone(f"Tail_{i}", tail[i], tail[i + 1], f"Tail_{i - 1}", True)
for name, j in legs.items():
    parent = "Spine_2" if name.startswith("Front") else "Spine_0"
    bone(f"{name}_0", j[0], j[1], parent)
    for i in range(1, 4):
        bone(f"{name}_{i}", j[i], j[i + 1], f"{name}_{i - 1}", True)
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- 正面を -Y に（馬と同じ向き）
for o in (arm, mesh):
    o.rotation_euler = (0, 0, math.radians(-90))
bpy.context.view_layer.update()
for o in (arm, mesh):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

# ---------------------------------------------------------------- 自動ウェイト
bpy.ops.object.select_all(action='DESELECT')
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
try:
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
except RuntimeError as e:
    print("AUTO WEIGHT FAILED, using envelope:", e)
    bpy.ops.object.parent_set(type='ARMATURE_ENVELOPE')
# 自動ウェイトで骨が付かなかった頂点（離れた部品）は、一番近い骨に付ける
def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length
bones = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones if b.name != "Root"]
fixed = 0
for v in mesh.data.vertices:
    if sum(g.weight for g in v.groups) < 1e-4:
        p = mesh.matrix_world @ v.co
        bn = min(bones, key=lambda t: seg_dist(p, t[1], t[2]))[0]
        vg = mesh.vertex_groups.get(bn) or mesh.vertex_groups.new(name=bn)
        vg.add([v.index], 1.0, 'REPLACE')
        fixed += 1
print("UNWEIGHTED fixed", fixed)

if RIGID:
    import bmesh
    bm = bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        st, cur = [v], []
        seen.add(v.index)
        while st:
            a = st.pop(); cur.append(a.index)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index); st.append(b)
        islands.append(cur)
    bm.free()
    mw = mesh.matrix_world
    leg_bones = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
                 for b in arm.data.bones if b.name.startswith(("Front", "Hind"))]
    body_bones = [(b.name, arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
                  for b in arm.data.bones if not b.name.startswith(("Front", "Hind", "Root", "Ear"))]
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    groups = {}
    counts = {"leg": 0, "body": 0}
    for isl in islands:
        pts = [mw @ mesh.data.vertices[i].co for i in isl]
        c = sum(pts, Vector()) / len(pts)
        near_leg = min(leg_bones, key=lambda t: seg_dist(c, t[1], t[2]))
        if seg_dist(c, near_leg[1], near_leg[2]) < 0.10 or c.z < BELLY - 0.05:
            bn = near_leg[0]; counts["leg"] += 1
        else:
            bn = min(body_bones, key=lambda t: seg_dist(c, t[1], t[2]))[0]; counts["body"] += 1
        vg = groups.get(bn) or groups.setdefault(bn, mesh.vertex_groups.new(name=bn))
        vg.add(isl, 1.0, 'REPLACE')
    for b in arm.data.bones:
        if b.name not in groups:
            mesh.vertex_groups.new(name=b.name)
    print("RIGID islands", len(islands), counts)
empty_groups = [g.name for g in mesh.vertex_groups
                if not any(g.index in [x.group for x in v.groups] for v in list(mesh.data.vertices)[::7])]
print("WEIGHTS groups", len(mesh.vertex_groups), "maybe-empty", empty_groups)

# ---------------------------------------------------------------- 歩様（ラクダ）
CAMEL_GAITS = {
    # 常歩：側対歩順の4拍（左後 → 左前 → 右後 → 右前）。首を大きく前後に振る
    "Walk":   dict(T=1.25, duty=0.65, off={"HindL": 0.0, "FrontL": 0.15, "HindR": 0.5, "FrontR": 0.65},
                   amp=16, fore_lift=45, hind_lift=35, load_k=0.5, bob=0.03, pitch=1.0, neck=8, neck_mode="nod", spine=0, tail=5),
    # 中速：側対歩（同じ側の前後脚が同時）。体が左右に揺れる
    "Trot":   dict(T=0.80, duty=0.45, off={"HindL": 0.0, "FrontL": 0.0, "HindR": 0.5, "FrontR": 0.5},
                   amp=20, fore_lift=60, hind_lift=45, load_k=0.7, bob=0.05, pitch=1.0, neck=4, neck_mode="steady", spine=0, tail=6),
    # 速い走り：回転襲歩（左後 → 右後 → 右前 → 左前）
    "Canter": dict(T=0.70, duty=0.36, off={"HindL": 0.0, "HindR": 0.12, "FrontR": 0.3, "FrontL": 0.42},
                   amp=24, fore_lift=75, hind_lift=55, load_k=0.9, bob=0.07, pitch=2.5, neck=8, neck_mode="rock", spine=2, tail=10),
    "Gallop": dict(T=0.58, duty=0.30, off={"HindL": 0.0, "HindR": 0.1, "FrontR": 0.3, "FrontL": 0.4},
                   amp=28, fore_lift=85, hind_lift=60, load_k=1.0, bob=0.08, pitch=3.0, neck=10, neck_mode="rock", spine=3, tail=12),
}
# ---------------------------------------------------------------- 色（テクスチャが無いモデルは単色）
if not mesh.data.materials or not any(m and m.use_nodes and any(n.type == 'TEX_IMAGE' for n in m.node_tree.nodes) for m in mesh.data.materials):
    col = {"horse": (0.86, 0.78, 0.62)}.get(PROFILE, (0.7, 0.6, 0.5))   # 日に焼けた骨の色
    m = bpy.data.materials.new(NAME + "_Mat")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*col, 1.0)
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    mesh.data.materials.clear()
    mesh.data.materials.append(m)

gait_lib.run(arm, CAMEL_GAITS if PROFILE == "camel" else None)
gait_lib.export(arm, mesh, out_fbx, NAME)
