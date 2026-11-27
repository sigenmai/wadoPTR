"""生成モデル・外部モデルを「軽量化＋掃除＋UV展開＋世界観に合わせたトゥーン調テクスチャ」にして書き出す。

使い方:
  blender -b --python tools/blender/stylize_asset.py -- <入力> <出力フォルダ> <名前> <目標三角形数> <最大寸法m> [テクスチャ解像度] [テクスチャ探索フォルダ]

処理:
  1. 読み込み → 1つのメッシュに結合 → 実寸にスケール → 底を z=0、中心を原点へ
  2. 掃除：重複頂点の結合、極小の浮いた破片の削除、法線の再計算
  3. 軽量化（Decimate）→ UV を新しく展開（Smart UV Project）
  4. 元の高密度モデルの色を、新しい UV のテクスチャへ転写（Python で自前に実装。
     この環境の Blender は Cycles が動かずベイクできないため）。
     各テクセルの 3D 位置から元モデルの最も近い面を探し、その面の UV で元テクスチャを読む。
     ＋ 低ポリの頂点ごとに AO（くぼみの暗さ）をレイで計算、高さ（足元の砂ぼこり用）も作る
  5. Python 側（tools/blender/toonify_texture.py）で、色を共通パレットに寄せ、明るさを段階化、
     AO を段階的な影に、足元を砂色に寄せて「手描き風トゥーン」に仕上げる
  6. 仕上げたテクスチャを貼って FBX 書き出し（Unity 向き：正面 +Z / 上 +Y）
"""
import bpy, bmesh, sys, os, math, subprocess
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, outdir, name = argv[0], argv[1], argv[2]
target_tris = int(argv[3])
max_dim = float(argv[4])
res = int(argv[5]) if len(argv) > 5 else 2048
tex_dirs = argv[6:] if len(argv) > 6 else []
os.makedirs(outdir, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__))

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(src)[1].lower()
if ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=src)
elif ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
elif ext == ".obj":
    bpy.ops.wm.obj_import(filepath=src)

# ---- 見つからないテクスチャを探して付け直す
for img in bpy.data.images:
    path = bpy.path.abspath(img.filepath).replace("\\", "/")
    if img.packed_file or (path and os.path.exists(path)):
        continue
    base = os.path.basename(path)
    for d in tex_dirs + [os.path.dirname(src), os.path.join(os.path.dirname(src), "..", "textures")]:
        cand = os.path.join(d, base)
        if os.path.exists(cand):
            img.filepath = cand
            img.reload()
            print("RELINK", base, "->", cand)
            break
    else:
        print("MISSING TEXTURE", img.name, path)

# ---- 元マテリアルを「色だけ」にする（Tripo は色テクスチャを Metallic にもつないでいて、
#      そのままだと金属扱いになり、ベイクした色が真っ黒になる）
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    nt = m.node_tree
    for n in nt.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            for sock in ("Metallic", "Roughness", "Normal", "Specular", "Specular IOR Level", "Transmission", "Transmission Weight"):
                if sock in n.inputs:
                    for l in list(n.inputs[sock].links):
                        nt.links.remove(l)
            n.inputs["Metallic"].default_value = 0.0
            n.inputs["Roughness"].default_value = 1.0

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
# 親の変形を確定させてから結合
for o in meshes:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in [o for o in bpy.data.objects if o.type != 'MESH']:
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
hi = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ---- 実寸・接地
s = max_dim / max(hi.dimensions)
hi.scale = (s, s, s)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
vs = [v.co for v in hi.data.vertices]
off = Vector(((min(v.x for v in vs) + max(v.x for v in vs)) / 2, (min(v.y for v in vs) + max(v.y for v in vs)) / 2, min(v.z for v in vs)))
for v in hi.data.vertices:
    v.co -= off
height = max(v.co.z for v in hi.data.vertices)
hi.name = name + "_High"
tris0 = sum(len(p.vertices) - 2 for p in hi.data.polygons)

# ---- 低ポリを複製して作る
lo = hi.copy()
lo.data = hi.data.copy()
lo.name = name
bpy.context.scene.collection.objects.link(lo)
bpy.ops.object.select_all(action='DESELECT')
lo.select_set(True)
bpy.context.view_layer.objects.active = lo

# 掃除
bm = bmesh.new()
bm.from_mesh(lo.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max_dim * 1e-4)
# 小さな浮いた破片（全体の 0.05% 未満の面数の島）を削除
bm.faces.ensure_lookup_table()
seen, islands = set(), []
for f in bm.faces:
    if f in seen:
        continue
    stack, isl = [f], []
    seen.add(f)
    while stack:
        g = stack.pop()
        isl.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h not in seen:
                    seen.add(h)
                    stack.append(h)
    islands.append(isl)
limit = max(4, len(bm.faces) * 0.0005)
small = [f for isl in islands if len(isl) < limit for f in isl]
bmesh.ops.delete(bm, geom=small, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(lo.data)
bm.free()
print("CLEAN islands", len(islands), "removed_small_faces", len(small))

tris1 = sum(len(p.vertices) - 2 for p in lo.data.polygons)
if tris1 > target_tris:
    d = lo.modifiers.new("dec", 'DECIMATE')
    d.ratio = target_tris / tris1
    bpy.ops.object.modifier_apply(modifier=d.name)
tris2 = sum(len(p.vertices) - 2 for p in lo.data.polygons)

# UV（作り直し）
while lo.data.uv_layers:
    lo.data.uv_layers.remove(lo.data.uv_layers[0])
lo.data.uv_layers.new(name="UVMap")
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, correct_aspect=True)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.shade_smooth()
try:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))
except Exception:
    lo.data.use_auto_smooth = True
    lo.data.auto_smooth_angle = math.radians(50)

# ---- テクスチャ転写（Cycles のベイクがこの環境で動かないため、Python で自前に行う）
#  低ポリの各テクセル → 3D 位置 → 元モデルの最も近い面 → その面の UV → 元テクスチャの色
import numpy as np
from mathutils.bvhtree import BVHTree

def material_images(obj):
    """マテリアル番号ごとに (画像配列 or None, 単色) を返す"""
    out = []
    for slot in obj.material_slots:
        m = slot.material
        arr, flat = None, (0.6, 0.5, 0.4)
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED':
                    bc = n.inputs['Base Color']
                    flat = tuple(bc.default_value[:3])
                    if bc.links and bc.links[0].from_node.type == 'TEX_IMAGE':
                        img = bc.links[0].from_node.image
                        if img and img.has_data and img.size[0] > 0:
                            w, h = img.size
                            arr = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, :, :3]
        out.append((arr, flat))
    return out or [(None, (0.6, 0.5, 0.4))]

# 元モデルは転写用に 40 万三角形まで軽くする（UV は保たれる）
src_tris = sum(len(p.vertices) - 2 for p in hi.data.polygons)
if src_tris > 400000:
    bpy.ops.object.select_all(action='DESELECT'); hi.select_set(True); bpy.context.view_layer.objects.active = hi
    d = hi.modifiers.new("dec", 'DECIMATE'); d.ratio = 400000 / src_tris
    bpy.ops.object.modifier_apply(modifier=d.name)
hi.data.calc_loop_triangles()
hi_tris = hi.data.loop_triangles
hi_co = np.array([v.co[:] for v in hi.data.vertices], dtype=np.float64)
hi_tv = np.array([t.vertices[:] for t in hi_tris], dtype=np.int64)
hi_uvl = hi.data.uv_layers.active
hi_tuv = np.array([[hi_uvl.data[l].uv[:] for l in t.loops] for t in hi_tris], dtype=np.float64) if hi_uvl else None
hi_mat = np.array([t.material_index for t in hi_tris], dtype=np.int64)
imgs = material_images(hi)
bvh_hi = BVHTree.FromPolygons([tuple(map(float, c)) for c in hi_co], hi_tv.tolist(), all_triangles=True)
print("SOURCE tris", len(hi_tv), "materials", len(imgs), "has_uv", hi_tuv is not None)

# 低ポリの頂点 AO（半球へのレイで遮られた割合）
lo.data.calc_loop_triangles()
lo_co = np.array([v.co[:] for v in lo.data.vertices], dtype=np.float64)
lo_no = np.array([v.normal[:] for v in lo.data.vertices], dtype=np.float64)
lo_tv = np.array([t.vertices[:] for t in lo.data.loop_triangles], dtype=np.int64)
bvh_lo = BVHTree.FromPolygons([tuple(map(float, c)) for c in lo_co], lo_tv.tolist(), all_triangles=True)
rng = np.random.default_rng(1)
dirs = rng.normal(size=(24, 3)); dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
ao_dist = max_dim * 0.15
vert_ao = np.ones(len(lo_co))
for i, (c, n) in enumerate(zip(lo_co, lo_no)):
    hits = tot = 0
    for dv in dirs:
        if dv @ n <= 0.05:
            dv = dv - 2 * (dv @ n) * n
        o = Vector(c + n * max_dim * 2e-4)
        loc, _, _, _ = bvh_lo.ray_cast(o, Vector(dv), ao_dist)
        tot += 1
        hits += loc is not None
    vert_ao[i] = 1.0 - hits / tot

# 低ポリ UV をラスタライズ
uvl = lo.data.uv_layers.active
tri_uv = np.array([[uvl.data[l].uv[:] for l in t.loops] for t in lo.data.loop_triangles], dtype=np.float64)
color = np.zeros((res, res, 3), np.float32)
ao_map = np.ones((res, res), np.float32)
h_map = np.zeros((res, res), np.float32)
filled = np.zeros((res, res), bool)
pts, owners = [], []
for ti in range(len(lo_tv)):
    uv = tri_uv[ti] * res - 0.5
    x0, y0 = np.floor(uv.min(0)).astype(int); x1, y1 = np.ceil(uv.max(0)).astype(int)
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, res - 1), min(y1, res - 1)
    if x1 < x0 or y1 < y0:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
    p = np.stack([gx.ravel(), gy.ravel()], 1).astype(np.float64)
    a, b, c = uv
    v0, v1 = b - a, c - a
    den = v0[0] * v1[1] - v1[0] * v0[1]
    if abs(den) < 1e-12:
        continue
    v2 = p - a
    w1 = (v2[:, 0] * v1[1] - v1[0] * v2[:, 1]) / den
    w2 = (v0[0] * v2[:, 1] - v2[:, 0] * v0[1]) / den
    w0 = 1 - w1 - w2
    eps = 0.5 / max(np.abs(v0).max(), np.abs(v1).max(), 1e-6)   # 境界の取りこぼし防止
    m = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
    if not m.any():
        continue
    W = np.clip(np.stack([w0[m], w1[m], w2[m]], 1), 0, None); W /= W.sum(1, keepdims=True)
    P = W @ lo_co[lo_tv[ti]]
    A = W @ vert_ao[lo_tv[ti]]
    xs, ys = p[m, 0].astype(int), p[m, 1].astype(int)
    ao_map[ys, xs] = A
    h_map[ys, xs] = P[:, 2] / max(height, 1e-3)
    filled[ys, xs] = True
    pts.append(np.column_stack([xs, ys, P]))
pts = np.concatenate(pts)
print("TEXELS", len(pts))
for x, y, px, py, pz in pts:
    loc, nrm, fi, dist = bvh_hi.find_nearest(Vector((px, py, pz)))
    if fi is None:
        continue
    mi = hi_mat[fi]
    arr, flat = imgs[mi] if mi < len(imgs) else imgs[0]
    if arr is None or hi_tuv is None:
        color[int(y), int(x)] = flat
        continue
    tv = hi_co[hi_tv[fi]]
    # 重心座標
    v0, v1, v2 = tv[1] - tv[0], tv[2] - tv[0], np.array(loc[:]) - tv[0]
    d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
    d20, d21 = v2 @ v0, v2 @ v1
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18:
        w = np.array([1 / 3, 1 / 3, 1 / 3])
    else:
        b1 = (d11 * d20 - d01 * d21) / den; b2 = (d00 * d21 - d01 * d20) / den
        w = np.clip(np.array([1 - b1 - b2, b1, b2]), 0, 1); w /= w.sum()
    u, v = (w @ hi_tuv[fi]) % 1.0
    h, wdt = arr.shape[:2]
    color[int(y), int(x)] = arr[min(int(v * h), h - 1), min(int(u * wdt), wdt - 1)]

# 隙間を広げて埋める（UV の島の外側 12px）
for _ in range(12):
    if filled.all():
        break
    acc = np.zeros_like(color); cnt = np.zeros((res, res), np.float32)
    acc_ao = np.zeros_like(ao_map); acc_h = np.zeros_like(h_map)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        sf = np.roll(filled, (dy, dx), (0, 1))
        acc += np.roll(color, (dy, dx), (0, 1)) * sf[..., None]
        acc_ao += np.roll(ao_map, (dy, dx), (0, 1)) * sf
        acc_h += np.roll(h_map, (dy, dx), (0, 1)) * sf
        cnt += sf
    new = (~filled) & (cnt > 0)
    color[new] = acc[new] / cnt[new][:, None]
    ao_map[new] = acc_ao[new] / cnt[new]
    h_map[new] = acc_h[new] / cnt[new]
    filled |= new


paths = {}
def save_png(tag, data):
    if data.ndim == 2:
        data = np.repeat(data[..., None], 3, 2)
    img = bpy.data.images.new(f"{name}_{tag}", res, res, alpha=False)
    rgba = np.concatenate([np.clip(data, 0, 1), np.ones((res, res, 1), np.float32)], 2)
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    p = os.path.join(outdir, f"_{name}_{tag}.png")
    img.filepath_raw = p; img.file_format = 'PNG'; img.save()
    paths[tag] = p
save_png("color", color)
save_png("ao", ao_map)
save_png("height", h_map)

# ---- トゥーン化（Python 側）
final_png = os.path.join(outdir, f"{name}_Albedo.png")
r = subprocess.run(["python3", os.path.join(HERE, "toonify_texture.py"), paths["color"], paths["ao"], paths["height"], final_png],
                   capture_output=True, text=True,
                   env={k: v for k, v in os.environ.items() if not k.startswith("PYTHON")})
print(r.stdout.strip(), r.stderr.strip()[-400:])
for p in paths.values():
    os.remove(p)

# ---- 仕上げのマテリアルで書き出し
final = bpy.data.images.load(final_png)
m = bpy.data.materials.new(name + "_Mat")
m.use_nodes = True
t = m.node_tree.nodes.new('ShaderNodeTexImage')
t.image = final
m.node_tree.links.new(t.outputs[0], m.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.9
lo.data.materials.clear()
lo.data.materials.append(m)
bpy.data.objects.remove(hi, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
lo.select_set(True)
out_fbx = os.path.join(outdir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, path_mode='COPY', embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, f"_{name}.blend"))
print("RESULT", name, "tris", tris0, "->", tris2, "size", tuple(round(d, 2) for d in lo.dimensions))
