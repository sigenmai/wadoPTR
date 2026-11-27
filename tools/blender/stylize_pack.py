"""ローポリのアセットパックを、形はそのままに「共通パレットの色」に塗り替えて、部品ごとに使いやすく書き出す。

使い方:
  blender -b --python tools/blender/stylize_pack.py -- <入力 .fbx/.blend> <出力フォルダ> <名前> <倍率> [テクスチャ探索フォルダ...]

- テクスチャのつながりを直す（relink.py）
- 全体を <倍率> で拡大縮小（パックごとに単位がバラバラなのでメートルにそろえる）
- 部品ごとに原点を「底面の中心」に移し、横一列に並べ直す（Unity で子オブジェクトを1つずつ取り出して使える）
- 部品名を <名前>_01, _02 ... に付け直す
- 色テクスチャを共通パレットに寄せたものに差し替え（recolor_texture.py）
- 面はフラット表示（ローポリ・トゥーン）
- --redrock を付けると、灰色の岩を峡谷の赤い岩の色に塗り替える
"""
import bpy, sys, os, subprocess
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import relink

argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, name, scale = argv[0], argv[1], argv[2], float(argv[3])
tex_dirs = [a for a in argv[4:] if not a.startswith("--")]
REDROCK = "--redrock" in argv   # 灰色の岩を赤い岩に塗り替える
os.makedirs(outdir, exist_ok=True)

if src.endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
relink.fix_textures(src, tex_dirs)

sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH' and o.visible_get()]
# 非表示・カメラ・ライト・空オブジェクトは捨てる
for o in list(bpy.data.objects):
    if o not in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
# アニメーション（燃える火の揺れ等）は外す
for o in meshes:
    o.animation_data_clear()

# ワールド変形を確定・拡大縮小
for o in meshes:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = Matrix.Scale(scale, 4) @ mw
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# 原点を底面の中心へ、一列に並べる（大きい順）
meshes.sort(key=lambda o: -max(o.dimensions))
x = 0.0
for i, o in enumerate(meshes):
    vs = [v.co for v in o.data.vertices]
    if not vs:
        continue
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    base = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for v in o.data.vertices:
        v.co -= base
    w = mx.x - mn.x
    o.location = Vector((x + w / 2, 0, 0))
    x += w + 1.0
    o.name = f"{name}_{i + 1:02d}"
    o.data.name = o.name
    # フラット表示
    for p in o.data.polygons:
        p.use_smooth = False

# 色テクスチャをパレットに寄せたものへ差し替え
done = {}
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    for n in m.node_tree.nodes:
        if n.type != 'BSDF_PRINCIPLED':
            continue
        # 金属・粗さ・法線のつながりは外す（トゥーンでは使わない）
        for sock in ("Metallic", "Roughness", "Normal"):
            for l in list(n.inputs[sock].links):
                m.node_tree.links.remove(l)
        n.inputs["Metallic"].default_value = 0.0
        n.inputs["Roughness"].default_value = 0.9
        bc = n.inputs["Base Color"]
        if bc.links and bc.links[0].from_node.type == 'TEX_IMAGE':
            img = bc.links[0].from_node.image
            if img is None or img.size[0] == 0:   # size を読むと画像が読み込まれる
                continue
            if img.name not in done:
                srcp = bpy.path.abspath(img.filepath)
                if img.packed_file or not os.path.exists(srcp):
                    srcp = os.path.join(outdir, f"_src_{len(done)}.png")
                    img.save_render(srcp)
                dst = os.path.join(outdir, f"{name}_Tex{len(done) + 1:02d}.png")
                r = subprocess.run(["python3", os.path.join(HERE, "recolor_texture.py"), srcp, dst] + (["0.85", "5", "redrock"] if REDROCK else []),
                                   capture_output=True, text=True,
                                   env={k: v for k, v in os.environ.items() if not k.startswith("PYTHON")})
                print(r.stdout.strip(), r.stderr.strip()[-300:])
                done[img.name] = bpy.data.images.load(dst)
            bc.links[0].from_node.image = done[img.name]
        else:
            # 単色マテリアルもパレットへ寄せる
            import colorsys
            c = list(bc.default_value[:3])
            tmp = os.path.join(outdir, "_flat.png")
            fi = bpy.data.images.new("_flat", 1, 1)
            fi.pixels = c + [1.0]
            fi.filepath_raw = tmp
            fi.file_format = 'PNG'
            fi.save()
            subprocess.run(["python3", os.path.join(HERE, "recolor_texture.py"), tmp, tmp, "0.8", "8"],
                           env={k: v for k, v in os.environ.items() if not k.startswith("PYTHON")})
            fi2 = bpy.data.images.load(tmp)
            bc.default_value = (*fi2.pixels[0:3], 1.0)
            os.remove(tmp)

for f in os.listdir(outdir):
    if f.startswith("_src_"):
        os.remove(os.path.join(outdir, f))

bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, f"{name}.fbx"), use_selection=True, object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         path_mode='COPY', embed_textures=False, mesh_smooth_type='FACE', bake_anim=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, f"_{name}.blend"))
print("RESULT", name, "parts", len(meshes), "tris", tris, "parts_sizes",
      [tuple(round(d, 1) for d in o.dimensions) for o in meshes[:6]])
