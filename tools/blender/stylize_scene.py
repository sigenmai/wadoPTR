"""配置ごと使うシーン（町など）を、配置はそのままに軽くして世界観の色にそろえて書き出す。

使い方:
  blender -b --python tools/blender/stylize_scene.py -- <入力 .fbx> <出力フォルダ> <名前> <倍率> [--redrock] [テクスチャ探索フォルダ...]

- テクスチャのつながりを直す（見つからない画像は単色にする）
- 単色マテリアルの色も、テクスチャも、共通パレットに寄せる
- 同じマテリアルの部品を1つのメッシュにまとめる（部品数が多いと Unity で重いため）
- 全体の原点は土台の底面の中心
- 面はフラット表示（ローポリ・トゥーン）
"""
import bpy, sys, os, subprocess
from mathutils import Vector, Matrix
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import relink

argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, name, scale = argv[0], argv[1], argv[2], float(argv[3])
REDROCK = "--redrock" in argv
tex_dirs = [a for a in argv[4:] if not a.startswith("--")]
os.makedirs(outdir, exist_ok=True)
ENV = {k: v for k, v in os.environ.items() if not k.startswith("PYTHON")}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
relink.fix_textures(src, tex_dirs)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in list(bpy.data.objects):
    if o not in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
for o in meshes:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = Matrix.Scale(scale, 4) @ mw
    o.animation_data_clear()

# ---- 色をパレットへ
def recolor_rgb(rgb):
    tmp = os.path.join(outdir, "_c.png")
    img = bpy.data.images.new("_c", 1, 1)
    img.pixels = list(rgb) + [1.0]
    img.filepath_raw = tmp; img.file_format = 'PNG'; img.save()
    subprocess.run(["python3", os.path.join(HERE, "recolor_texture.py"), tmp, tmp, "0.8", "8"] + (["redrock"] if REDROCK else []), env=ENV)
    out = bpy.data.images.load(tmp)
    c = tuple(out.pixels[0:3])
    bpy.data.images.remove(out); bpy.data.images.remove(img); os.remove(tmp)
    return c

tex_done = {}
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    nt = m.node_tree
    b = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b is None:
        continue
    for sock in ("Metallic", "Roughness", "Normal", "Alpha"):
        if sock in b.inputs:
            for l in list(b.inputs[sock].links):
                nt.links.remove(l)
    b.inputs["Metallic"].default_value = 0.0
    b.inputs["Roughness"].default_value = 0.9
    bc = b.inputs["Base Color"]
    img = bc.links[0].from_node.image if bc.links and bc.links[0].from_node.type == 'TEX_IMAGE' else None
    if img is not None and img.size[0] > 0:
        if img.name not in tex_done:
            srcp = bpy.path.abspath(img.filepath)
            if img.packed_file or not os.path.exists(srcp):
                # 読み込めているがパスが古い画像は、一度書き出してから使う
                srcp = os.path.join(outdir, f"_src_{len(tex_done)}.png")
                img.save_render(srcp)
            dst = os.path.join(outdir, f"{name}_Tex{len(tex_done) + 1:02d}.png")
            subprocess.run(["python3", os.path.join(HERE, "recolor_texture.py"), srcp, dst] + (["0.85", "5", "redrock"] if REDROCK else []), env=ENV)
            tex_done[img.name] = bpy.data.images.load(dst)
        bc.links[0].from_node.image = tex_done[img.name]
    else:
        # 見つからない画像は外して単色に
        for l in list(bc.links):
            nt.links.remove(l)
    # どこにもつながっていない・読み込めない画像ノードは消す（プレビューや書き出しで紫色になるのを防ぐ）
    for n in list(nt.nodes):
        if n.type == 'TEX_IMAGE' and (n.image is None or n.image.size[0] == 0 or not any(l.from_node == n for l in nt.links)):
            nt.nodes.remove(n)
        bc.default_value = (*recolor_rgb(bc.default_value[:3]), 1.0)
print("MATERIALS", len(bpy.data.materials), "textures", len(tex_done))
for f in os.listdir(outdir):
    if f.startswith("_src_"):
        os.remove(os.path.join(outdir, f))

# ---- マテリアルごとに結合
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
whole = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# 原点：底面の中心
co = np.array([v.co[:] for v in whole.data.vertices])
base = Vector(((co[:, 0].min() + co[:, 0].max()) / 2, (co[:, 1].min() + co[:, 1].max()) / 2, co[:, 2].min()))
for v in whole.data.vertices:
    v.co -= base
for p in whole.data.polygons:
    p.use_smooth = False
# 重複頂点の結合・使われていないマテリアル枠の整理
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.001)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.material_slot_remove_unused()
# 同じ見た目のマテリアル（同じ色・同じテクスチャ）を1つに
key_to_idx, remap = {}, {}
for i, s in enumerate(whole.material_slots):
    m = s.material
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m and m.use_nodes else None
    bc = b.inputs["Base Color"] if b else None
    img = bc.links[0].from_node.image.name if bc and bc.links else None
    k = (img, tuple(round(c, 3) for c in bc.default_value[:3])) if bc else ("none",)
    remap[i] = key_to_idx.setdefault(k, i)
for p in whole.data.polygons:
    p.material_index = remap[p.material_index]
bpy.ops.object.material_slot_remove_unused()
# マテリアルごとに別オブジェクトへ分ける（Unity で静的バッチしやすい）
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='MATERIAL')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for i, o in enumerate(parts):
    o.name = f"{name}_{i + 1:02d}_{o.material_slots[0].material.name if o.material_slots else 'none'}"
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts)
dims = Vector(co.max(0) - co.min(0))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, f"{name}.fbx"), use_selection=True, object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         path_mode='COPY', embed_textures=False, mesh_smooth_type='FACE', bake_anim=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, f"_{name}.blend"))
print("RESULT", name, "objects", len(meshes), "->", len(parts), "tris", tris, "size", tuple(round(d, 1) for d in dims))
