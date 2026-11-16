#!/usr/bin/env python3
"""OpenGameArt の CC0 人体ベースメッシュ (base-char.blend) を 6 部位に分割して FBX 化する。

  python Tools/build_body.py -- <base-char.blend> <texture2.png>

出力: Assets/WadoPTR/Models/body/{head,torso,arm_l,arm_r,leg_l,leg_r}.fbx, body_skin.jpg,
      Tools/body_manifest.json (各部位の中心オフセット。Unity 座標系)
姿勢: 仰向け。頭が Unity の -X、足が +X、顔が +Y(上)、背中が y=0。
各 FBX の原点は部位のバウンディング中心(掴んだときの支点になる)。
切断面にはスロット 1 のマテリアル "cut" が入る。
"""
import bpy, bmesh, json, os, sys
from mathutils import Vector, Matrix
import math
from PIL import Image, ImageEnhance

args = sys.argv[sys.argv.index("--") + 1:]
blend, tex = args[0], args[1]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "WadoPTR", "Models", "body")
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=blend, use_scripts=False)
src = bpy.data.objects["char"]
for m in list(src.modifiers):
    src.modifiers.remove(m)
mw = src.matrix_world.copy()

skin = bpy.data.materials.new("skin")
cut = bpy.data.materials.new("cut")


def lay(v):
    # 立位(Z 上, 正面 -Y) → 仰向け(頭 +X, 正面 +Z)。右手系の回転: (x, y, z) -> (z, -x, -y)
    return Vector((v.z, -v.x, -v.y))


def piece(name, planes):
    """planes: [(co, normal)] 法線側を残す。"""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.transform(mw)
    for co, no in planes:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=Vector(co), plane_no=Vector(no).normalized(),
                                     clear_inner=True, clear_outer=False)
        edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
        if edges:
            before = set(bm.faces)
            bmesh.ops.holes_fill(bm, edges=edges, sides=0)
            for f in set(bm.faces) - before:
                f.material_index = 1
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    if name in ARM_SWING:
        # 腕は A ポーズのままだと処理台からはみ出すので、肩を支点に体側へ寄せる
        pivot, ang = ARM_SWING[name]
        rot = Matrix.Rotation(ang, 4, "Y")
        for v in bm.verts:
            v.co = Vector(pivot) + rot @ (v.co - Vector(pivot))
    for v in bm.verts:
        v.co = lay(v.co)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(skin)
    me.materials.append(cut)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


NECK = 1.49
HIP = 0.80
SH = 0.25      # 肩の切断位置 |x|
SHZ = 1.36
ARM_SWING = {"arm_l": ((SH, 0, SHZ), math.radians(48)), "arm_r": ((-SH, 0, SHZ), math.radians(-48))}
planes_arm_l = [((SH, 0, SHZ), (1, 0, -0.25)), ((0, 0, 0.95), (0, 0, 1))]
planes_arm_r = [((-SH, 0, SHZ), (-1, 0, -0.25)), ((0, 0, 0.95), (0, 0, 1))]
parts = {
    "head": [((0, 0, NECK), (0, 0, 1))],
    "arm_l": planes_arm_l,
    "arm_r": planes_arm_r,
    "leg_l": [((0, 0, HIP), (0, 0, -1)), ((0, 0, 0), (1, 0, 0))],
    "leg_r": [((0, 0, HIP), (0, 0, -1)), ((0, 0, 0), (-1, 0, 0))],
    "torso": [((0, 0, NECK), (0, 0, -1)), ((0, 0, HIP), (0, 0, 1)),
              ((SH, 0, SHZ), (-1, 0, 0.25)), ((-SH, 0, SHZ), (1, 0, -0.25))],
}

objs = {n: piece(n, p) for n, p in parts.items()}
# 全体を「背中が z=0、胴体中心が原点」へ
allv = [v.co for o in objs.values() for v in o.data.vertices]
zmin = min(v.z for v in allv)
tv = [v.co for v in objs["torso"].data.vertices]
cx = (min(v.x for v in tv) + max(v.x for v in tv)) / 2
shift = Vector((cx, 0, zmin))
manifest = {}
for n, o in objs.items():
    for v in o.data.vertices:
        v.co -= shift
    vs = [v.co for v in o.data.vertices]
    mn = Vector([min(v[i] for v in vs) for i in range(3)])
    mx = Vector([max(v[i] for v in vs) for i in range(3)])
    c = (mn + mx) / 2
    for v in o.data.vertices:
        v.co -= c
    size = mx - mn
    # Unity(x, y, z) = Blender(-x, z, -y)
    manifest[n] = {"offset": [round(-c.x, 4), round(c.z, 4), round(-c.y, 4)],
                   "size": [round(size.x, 4), round(size.z, 4), round(size.y, 4)],
                   "tris": sum(len(p.vertices) - 2 for p in o.data.polygons)}
    for x in bpy.context.scene.objects:
        x.select_set(x == o)
    bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, n + ".fbx"), use_selection=True, object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        path_mode="STRIP", embed_textures=False, bake_anim=False, mesh_smooth_type="FACE")
    print("OK", n, manifest[n], flush=True)

# 肌テクスチャ: 血の気を抜いた冷蔵色へ
im = Image.open(tex).convert("RGB").resize((512, 512), Image.LANCZOS)
im = ImageEnhance.Color(im).enhance(0.28)
r, g, b = im.split()
r = r.point(lambda v: int(v * 0.90))
g = g.point(lambda v: int(v * 0.96))
b = b.point(lambda v: min(255, int(v * 1.08)))
im = Image.merge("RGB", (r, g, b))
im = ImageEnhance.Brightness(im).enhance(1.12)
im.save(os.path.join(OUT, "body_skin.jpg"), quality=88, optimize=True)

# 切取線の位置(原体ルート基準・Unity 座標)。立位の (x, z) から求める
def cut_at(stand_x, stand_z, y):
    return [round(-(stand_z - shift.x), 4), y, round(stand_x, 4)]
manifest["head"]["cut"] = cut_at(0, NECK, 0.14)
manifest["arm_l"]["cut"] = cut_at(SH, SHZ, 0.14)
manifest["arm_r"]["cut"] = cut_at(-SH, SHZ, 0.14)
manifest["leg_l"]["cut"] = cut_at(0.11, HIP, 0.13)
manifest["leg_r"]["cut"] = cut_at(-0.11, HIP, 0.13)
manifest["torso"]["cut"] = [0.0, 0.16, 0.0]
with open(os.path.join(ROOT, "Tools", "body_manifest.json"), "w") as f:
    json.dump(manifest, f, indent=1)
# プレビュー用に分割済みシーンを保存
if len(args) > 2:
    bpy.data.objects.remove(src)
    bpy.ops.wm.save_as_mainfile(filepath=args[2])
