"""ワールド生成ツールと同じ計算結果（world.json / h*.bin / s*.bin）から下見用の 3D シーンを作って描画する。
blender -b --python preview_world.py -- <exportフォルダ> <assetsルート> <出力フォルダ>
Unity (x, y, z) → Blender (x, z, y)
"""
import bpy, bmesh, sys, os, json, struct, math, random
import numpy as np
from mathutils import Vector

_args = sys.argv[sys.argv.index("--") + 1:]
ex, assets, out = [a for a in _args if not a.startswith("--")][:3]
# --no-guide（course-r2-14）：コースの白い線（下見の目印）を描かない。騎手の目の絵が実際に見えるものだけになる
NO_GUIDE = "--no-guide" in _args or os.environ.get("NO_GUIDE") == "1"
os.makedirs(out, exist_ok=True)
W = json.load(open(os.path.join(ex, "world.json")))
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
random.seed(4)

def U(v):  # Unity → Blender
    return Vector((v[0], v[2], v[1]))

def read_grid(path):
    d = open(path, "rb").read()
    n = struct.unpack("<i", d[:4])[0]
    return np.frombuffer(d[4:], dtype="<f4").reshape(n, n)

def mat_color(name, rgb, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = 0.9
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, 1)
    return m

def img_material(name, arr):
    h, w, _ = arr.shape
    img = bpy.data.images.new(name, w, h)
    rgba = np.concatenate([arr, np.ones((h, w, 1), np.float32)], 2)
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = img
    m.node_tree.links.new(t.outputs[0], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1.0
    return m

def srgb2lin(c):
    return [((x + 0.055) / 1.055) ** 2.4 for x in c]

DIRT, ROCK, SAND = (0.62, 0.36, 0.22), (0.55, 0.45, 0.38), (0.90, 0.76, 0.52)

def terrain(stage):
    i = stage["index"]
    h = read_grid(os.path.join(ex, f"h{i}.bin")) * 200.0
    s = read_grid(os.path.join(ex, f"s{i}.bin"))
    step = 1   # art-r4-17：Unity と同じ 513 の格子（前の 257 は急な壁の頂をのこぎりの歯に見せていた）
    hh = h[::step, ::step]
    n = hh.shape[0]
    size = 1000.0
    ox, oz = stage["center"][0] - 500, stage["center"][2] - 500
    me = bpy.data.meshes.new(f"T{i}")
    verts = [(ox + x / (n - 1) * size, oz + z / (n - 1) * size, float(hh[z, x])) for z in range(n) for x in range(n)]
    faces = [(z * n + x, z * n + x + 1, (z + 1) * n + x + 1, (z + 1) * n + x) for z in range(n - 1) for x in range(n - 1)]
    me.from_pydata(verts, [], faces)
    uv = me.uv_layers.new()
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            z, x = divmod(vi, n)
            uv.data[li].uv = (x / (n - 1), z / (n - 1))
    # 色：TerrainBuilder と同じ考え方（砂 → 急斜面の岩 → 土）
    gy, gx = np.gradient(h, size / (h.shape[0] - 1))
    slope = np.degrees(np.arctan(np.hypot(gx, gy)))
    from PIL import Image
    S = np.asarray(Image.fromarray((np.clip(s, 0, 1) * 255).astype(np.uint8)).resize(slope.shape[::-1], Image.BILINEAR), np.float32) / 255   # art-r6-04：どのマスクも同じ双線形
    rock = np.clip((slope - 28) / 12, 0, 1) * (1 - S)
    dirt = stage.get("dirt", DIRT)
    sand_c = stage.get("sand", SAND)   # art-r4-06：ステージの主色の砂
    dirt_c = np.array(dirt)[None, None] * np.ones((*slope.shape, 1), np.float32)
    gtp = os.path.join(ex, f"gt{i}.bin")
    if os.path.exists(gtp):   # art-r5-05：土の色は縁から外へにじむ（WorldBounds.GroundTint）
        _gd = open(gtp, "rb").read(); TRn = struct.unpack("<i", _gd[:4])[0]
        GT = np.frombuffer(_gd[4:], dtype="<f4").reshape(TRn, TRn, 3)
        dirt_c = np.stack([np.asarray(Image.fromarray(GT[..., k].astype(np.float32), mode="F").resize(slope.shape[::-1], Image.BILINEAR), np.float32) for k in range(3)], -1)
    col = dirt_c * (1 - S[..., None] - rock[..., None]).clip(0, 1) + np.array(sand_c) * S[..., None] + np.array(ROCK) * rock[..., None]
    # art-r4-02：道の外の粒（3.5m）とまだら（45m）。ToonTerrain の _GrainAmp 0.07・_MottleAmp 0.05 と同じ考え方の値ノイズ
    ny, nx = slope.shape
    yy, xx = np.mgrid[0:ny, 0:nx].astype(np.float32) * (size / (nx - 1))
    def vnoise(px, pz, sc, seed):
        q = np.stack([px / sc, pz / sc]); i = np.floor(q); f = q - i; u = f * f * (3 - 2 * f)
        def hsh(a, b): return (np.sin(a * 127.1 + b * 311.7 + seed * 17.0) * 43758.5453) % 1.0
        a = hsh(i[0], i[1]); b = hsh(i[0] + 1, i[1]); c = hsh(i[0], i[1] + 1); d = hsh(i[0] + 1, i[1] + 1)
        return (a + (b - a) * u[0]) * (1 - u[1]) + (c + (d - c) * u[0]) * u[1]
    def noise2(px, pz, sc, seed): return (vnoise(px, pz, sc, seed) * 0.65 + vnoise(px * 2.03 + 17, pz * 2.03 + 17, sc, seed + 1) * 0.35) * 2 - 1
    # art-r5-13：粒 0.13・細かい擦れ 0.11（道の重み < 0.5 の所だけ）
    gamp = 0.03 if i == 2 else 0.13   # art-r6-17：境目の細かい粒は ±0.03
    grain = noise2(xx, yy, 3.5, 1) * gamp + noise2(xx + 311, yy + 311, 45.0, 2) * 0.05 + noise2(xx + 517, yy + 517, 1.2, 4) * 0.11
    grain = grain * (1 - 0.5 * S)   # art-r6-11：砂の上の粒は半分（ToonTerrain と同じ）
    if i == 2 and stage.get("strata"):
        # art-r6-17：境目の土の色は高さの帯（6m ごとに明・暗、±1.5m のゆらぎ、混ぜる割合 0.7）
        yb = h + noise2(xx + 913, yy + 913, 60.0, 9) * 1.5
        ph = np.mod(yb / 12.0, 1.0)
        def _ss(a, b, x): t_ = np.clip((x - a) / (b - a), 0, 1); return t_ * t_ * (3 - 2 * t_)
        band = (_ss(0.42, 0.58, ph) * (1 - _ss(0.92, 1.0, ph)))[..., None]
        bcol = np.array(stage["strata"][0])[None, None] * (1 - band) + np.array(stage["strata"][1])[None, None] * band
        wd = (1 - S[..., None] - rock[..., None]).clip(0, 1)
        col = col + (bcol - dirt_c) * 0.7 * wd
    # 道の踏み固めた土（HeightmapGen.TrailMask、ステージごとの ArtPalette.Trail：art-r2-02）
    tp = os.path.join(ex, f"t{i}.bin")
    if os.path.exists(tp) and stage.get("trail"):
        T = read_grid(tp)
        T = np.asarray(Image.fromarray((np.clip(T, 0, 1) * 255).astype(np.uint8)).resize(slope.shape[::-1], Image.BILINEAR), np.float32)[..., None] / 255
        off = 1 - np.clip((T - 0.4) / 0.2, 0, 1)
        col = col * (1 + grain[..., None] * off)
        tc = np.array(stage["trail"])[None, None] * np.ones_like(col)
        # art-r4-02：轍（G = 道の中心からの距離 / TRAIL_DIST_MAX、ToonTerrain と同じ）と蹄の擦れ
        gp_ = os.path.join(ex, f"g{i}.bin")
        if os.path.exists(gp_):
            # art-r5-01：G = 0.5 + 符号付きの横の距離 / (2·TRAIL_DIST_MAX)。d = (G − 0.5)·2·MAX、rut = 1 − smoothstep(w·0.35, w·0.65, ||d| − offset|)
            Gd = (read_grid(gp_) - 0.5) * 2.0 * stage.get("trailDistMax", 16.0)
            Gd = np.asarray(Image.fromarray(Gd.astype(np.float32), mode="F").resize(slope.shape[::-1], Image.BILINEAR), np.float32)
            # art-r6-04：ToonTerrain と同じ fwidth の考え方：1 画素（≈ 2m）より細い轍は覆う割合で薄める（2m の格子で丸いしみの列にしない）
            w_ = stage.get("rutWidth", 0.35); e_ = np.abs(np.abs(Gd) - stage.get("rutOffset", 0.9))
            fw_ = size / (Gd.shape[0] - 1)
            k_ = np.clip((e_ - (w_ * 0.35 - fw_)) / (w_ * 0.3 + 2 * fw_), 0, 1); rut = (1 - k_ * k_ * (3 - 2 * k_)) * min(1.0, w_ / (w_ + 2 * fw_))
            tc = tc * (1 - min(0.06, 1 - stage.get("rutShade", 0.94)) * rut)[..., None]
        tc = tc * (1 + noise2(xx + 77, yy + 77, 1.2, 3) * 0.11 * 0.3)[..., None]
        col = col * (1 - T) + tc * T
    # スタート・ゴールの線（course-r3-14、HeightmapGen.LineMask）：道の色 × 1.6（ToonTerrain の _LineBoost）
    lp_ = os.path.join(ex, f"l{i}.bin")
    if os.path.exists(lp_) and stage.get("trail"):
        L = read_grid(lp_)
        L = np.asarray(Image.fromarray((np.clip(L, 0, 1) * 255).astype(np.uint8)).resize(slope.shape[::-1], Image.BILINEAR), np.float32)[..., None] / 255
        col = col * (1 - L) + np.clip(np.array(stage["trail"]) * 1.6, 0, 1)[None, None] * L
    # 等高線をうっすら（立体感）
    band = (np.mod(h, 10.0) < 0.6)[..., None] * 0.06
    col = np.clip(col - band, 0, 1)
    col = np.array([srgb2lin(c) for c in col.reshape(-1, 3)]).reshape(col.shape).astype(np.float32)
    o = bpy.data.objects.new(f"Terrain_{stage['label']}", me)
    sc.collection.objects.link(o)
    o.data.materials.append(img_material(f"TerrainCol{i}", col))
    for p in me.polygons:
        p.use_smooth = True
    return o

def polyline(name, pts, radius, mat):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for k, p in enumerate(pts):
        sp.points[k].co = (*p, 1)
    cu.bevel_depth = radius
    cu.bevel_resolution = 1
    o = bpy.data.objects.new(name, cu)
    sc.collection.objects.link(o)
    o.data.materials.append(mat)
    return o

def box(name, loc, size, mat, rot_z=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=(0, 0, rot_z))
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.data.materials.append(mat)
    return o

def join_whole(meshes, new):
    """全体モデル（町・建物など、部品ごとに別オブジェクトで原点が部品の底）を、配置を保ったまま 1 つのメッシュにする。
    原点 = FBX の原点、回転なし（取り込み時の軸補正は形に焼き込む）。"""
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in new:
        if o not in meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    with bpy.context.temp_override(selected_editable_objects=meshes, active_object=meshes[0], object=meshes[0]):
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if len(meshes) > 1:
            bpy.ops.object.join()
    return meshes[0]

def import_part(path, part_prefix=None, index=None):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    fix_images(meshes, path)
    if part_prefix is None:
        return [join_whole(meshes, new)]
    keep = meshes
    if index is not None and len(meshes) > 1:
        meshes.sort(key=lambda o: o.name)
        keep = [meshes[index % len(meshes)]]
    for o in new:
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    for o in keep:
        o.location = (0, 0, 0)
    return keep

ROAD = mat_color("Road", srgb2lin((0.85, 0.72, 0.55)))
CPM = mat_color("CP", srgb2lin((0.95, 0.95, 0.9)))
BRIDGE = mat_color("Bridge", srgb2lin((0.5, 0.33, 0.24)))
JUMP = mat_color("Jump", srgb2lin((0.95, 0.8, 0.55)))
SHORT = mat_color("Shortcut", srgb2lin((0.95, 0.35, 0.2)), emit=1.5)
START = mat_color("Start", srgb2lin((0.95, 0.95, 0.95)))
GOAL = mat_color("Goal", srgb2lin((0.9, 0.2, 0.2)))

def ground_at(stage, x, z):
    i = stage["index"]
    h = GRID[i]
    n = h.shape[0]
    lx, lz = x - (stage["center"][0] - 500), z - (stage["center"][2] - 500)
    gx = min(max(lx / 1000 * (n - 1), 0), n - 1)
    gz = min(max(lz / 1000 * (n - 1), 0), n - 1)
    return float(h[int(round(gz)), int(round(gx))]) * 200.0

GRID = {}
# art-r4-03：下見の目印（コースの線・CP の柱・近道の帯）は実行時には無い。空撮（00_/1_/2_/3_）だけに描き、騎手の目・近景では隠す
GUIDES = []
for st in W["stages"]:
    GRID[st["index"]] = read_grid(os.path.join(ex, f"h{st['index']}.bin"))
    terrain(st)
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    pts = [U(p) + off + Vector((0, 0, 0.4)) for p in st["course"]]
    if not NO_GUIDE:
        GUIDES.append(polyline(f"Course{st['index']}", [q - Vector((0, 0, 0.3)) for q in pts], 0.5, ROAD))
        for k, p in enumerate(st["cps"]):
            q = U(p) + off
            GUIDES.append(box(f"CP{k}", q + Vector((14, 0, 1.5)), (0.4, 0.4, 3), CPM))
    # 遺物（art-r3-04）：本物の宝石（Gems_0N_01、1.1m）と足元の小石は extras（CourseBuilder.Relic と同じ）。ここでは青緑の灯り（7m）だけ足す
    for k, p in enumerate(st["relics"]):
        q = U(p) + off
        lt = bpy.data.lights.new(f"RelicGlow{st['index']}_{k}", "POINT"); lt.color = (0.2, 0.85, 0.8); lt.energy = 600; lt.shadow_soft_size = 0.3
        lo = bpy.data.objects.new(lt.name, lt); lo.location = q + Vector((0, 0, 1.0)); sc.collection.objects.link(lo)
    # スタート・ゴール・岩橋・ジャンプは Build World と同じ素材で置く（world.json の extras、下の place）
    if st["shortcut"] and not NO_GUIDE:
        a, b = U(st["shortcut"]["a"]) + off, U(st["shortcut"]["b"]) + off
        GUIDES.append(polyline("Shortcut", [a + Vector((0, 0, 1)), b + Vector((0, 0, 1))], 4.0, SHORT))

# ---- 色の決まり（GenUtil.Toonify / ArtPalette と同じ：art-r2-13）----
TINTS = W.get("tints", {})
def hsv(c):
    mx, mn = max(c), min(c); d = mx - mn
    if d <= 1e-5: return 0.0, (0.0 if mx <= 1e-5 else d / mx), mx
    r, g, b = c
    if mx == r: h = 60 * (((g - b) / d) % 6)
    elif mx == g: h = 60 * ((b - r) / d + 2)
    else: h = 60 * ((r - g) / d + 4)
    return h % 360, d / mx, mx
def lerp3(a, b, t): return [a[k] + (b[k] - a[k]) * t for k in range(3)]
# art-r5-06：白い壁は 4 色の板壁（ArtPalette.Sidings：古い木・色あせた納屋の赤・日干し煉瓦・黄土）を建物の名前の FNV-1a ハッシュで
SIDINGS = [[0.78, 0.66, 0.52], [0.60, 0.33, 0.25], [0.80, 0.60, 0.44], [0.78, 0.62, 0.36]]
def stable_hash(s):
    h = 2166136261
    for ch in (s or ""):
        h ^= ord(ch); h = (h * 16777619) & 0xFFFFFFFF
    return h
def siding_for(name): return SIDINGS[stable_hash(name) % len(SIDINGS)]
def cap_v(c, mx=0.82):
    m = max(c)
    return c if m <= mx or m <= 1e-5 else [x * mx / m for x in c]
def remap_base(asset, c, name=None):
    """ArtPalette.RemapBaseColor：町・建物の白い壁 → 4 色の板壁、灰色・青 → 古い木、緑 → セージ（色だけの素材）。明るさは 0.82 まで。"""
    if "WesternTown" not in asset and "Buildings/" not in asset and "RanchCompound" not in asset: return c
    h, s_, v = hsv(c)
    if v < 0.12: return c
    if v > 0.80 and s_ < 0.25: r = lerp3(c, siding_for(name or asset), 0.88)
    elif s_ < 0.12 and v > 0.25: r = lerp3(c, W["wood"], 0.65)
    elif 180 <= h <= 260 and s_ >= 0.08: r = lerp3(c, W["wood"], 0.7)
    elif 75 <= h < 170 and s_ >= 0.15: r = lerp3(c, W["sage"], 0.7)
    else: r = c
    return cap_v(r)
def lin2srgb(c): return [(1.055 * x ** (1 / 2.4) - 0.055) if x > 0.0031308 else 12.92 * x for x in c]
def styled(mat, asset, tint, name=None):
    """マテリアルを複製して Unity と同じ色の決まりを当てる。画像付きはアルファで切り抜き（半透明にしない）。"""
    if mat is None or not mat.use_nodes: return mat
    m = mat.copy()
    bs = m.node_tree.nodes.get("Principled BSDF")
    if bs is None: return m
    # art-r4-16：トゥーンには鏡面反射が無い（白い筋を出さない）
    for k_ in ("Specular IOR Level", "Specular"):
        if k_ in bs.inputs: bs.inputs[k_].default_value = 0.0
    inp = bs.inputs["Base Color"]
    if inp.is_linked:
        m.blend_method = "CLIP"; m.shadow_method = "CLIP"
        k = W.get("buildingTex") if ("Buildings/" in asset or "RanchCompound" in asset) else None
        mul = [tint[j] * (k[j] if k else 1.0) for j in range(3)]
        if any(abs(x - 1) > 1e-3 for x in mul):
            mix = m.node_tree.nodes.new("ShaderNodeMixRGB"); mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
            src = inp.links[0].from_socket
            m.node_tree.links.new(src, mix.inputs[1]); mix.inputs[2].default_value = (*mul, 1)
            m.node_tree.links.new(mix.outputs[0], inp)
    else:
        c = list(inp.default_value)[:3]
        c = srgb2lin([x * t for x, t in zip(remap_base(asset, lin2srgb(c), name), tint)])
        inp.default_value = (*c, 1)
    return m
def apply_style(obj, asset, part):
    kind = obj.name.split(".")[0].split("_")[-1] if part is None and "WesternTown" in asset else None
    if kind == "Ground": tint = W["townGround"]
    elif kind in ("Hill", "Rock"): tint = W["warmRock"]
    else: tint = TINTS.get(asset + "|" + (part or ""), [1, 1, 1])
    name = part or (obj.name.split(".")[0] if "WesternTown" in asset else asset)
    for slot in obj.material_slots:
        slot.material = styled(slot.material, asset, tint, name)

def fix_images(meshes, path):
    """art-r6-01：取り込んだ画像を読み直す（ヘッドレスの EGL では大きな画像が GPU に載らず「Texture Not Loaded」で真っ黒になった：MesaRockB）。
    アルファは使わない（不透明の岩・建物）、1024 より大きい画像は 1024 に縮める。読めなければ止める。"""
    seen = set()
    for o in meshes:
        for slot in o.material_slots:
            m = slot.material
            if m is None or not m.use_nodes: continue
            # art-r6-01：画像のない法線マップ（MesaRockB の Normal Map ノード）は接線が無く NaN の法線 → 霧も効かない真っ黒になった。トゥーンに法線マップは無いので外す
            for lk in list(m.node_tree.links):
                if lk.to_socket.name == "Normal" or lk.from_node.type == "NORMAL_MAP":
                    m.node_tree.links.remove(lk)
            for nd in m.node_tree.nodes:
                if nd.type != "TEX_IMAGE" or nd.image is None or nd.image.name in seen: continue
                img = nd.image; seen.add(img.name)
                if img.source == "FILE" and not img.packed_file:
                    fp = bpy.path.abspath(img.filepath)
                    if not os.path.exists(fp):   # .fbm の中に無ければ FBX の隣の同じ名前
                        alt = os.path.join(os.path.dirname(path), os.path.basename(fp))
                        if os.path.exists(alt): img.filepath = alt
                img.reload()
                img.alpha_mode = "NONE"
                _sz = tuple(img.size)   # 読み込み直しの後は使うまで読まれない（大きさで読ませる）
                if not img.has_data or _sz[0] == 0:
                    # GenUtil.MainTextureOf と同じ：FBX の隣（または .fbm）にある画像が 1 枚だけならそれを使う
                    d_ = os.path.dirname(path)
                    cands = [os.path.join(d_, f) for f in sorted(os.listdir(d_)) if f.lower().endswith((".png", ".jpg"))]
                    if len(cands) == 1:
                        img.filepath = cands[0]; img.reload(); _sz = tuple(img.size)
                if not img.has_data or _sz[0] == 0:
                    raise SystemExit(f"preview: texture not loaded {img.name} ({img.filepath}) for {path}")
                if img.size[0] > 1024 or img.size[1] > 1024:
                    img.scale(1024, 1024)
                # 画像の色（Base Color）だけを使う：アルファの線はつながない（CLIP で抜けた・黒く抜けた面を出さない）
                for lk in list(nd.outputs["Alpha"].links): m.node_tree.links.remove(lk)

# Build World と同じ配置計画（DressingPlan）で素材を置く
A = lambda *p: os.path.join(assets, *p)
CACHE = {}
PARTS = {}
TINT_CACHE = {}
def proto(asset, part):
    key = (asset, part)
    if key in CACHE:
        return CACHE[key]
    path = A("Models", asset)
    if not os.path.exists(path):
        CACHE[key] = None
        return None
    if part and path in PARTS:
        # 部品のパックは 1 回だけ取り込み、部品ごとに選ぶ（町のパックを部品の数だけ取り込むとメモリが足りない）
        allm = PARTS[path]
        sel = [o for o in allm if o.name.split(".")[0] == part] or [o for o in allm if o.name.startswith(part)]
        if not sel:
            CACHE[key] = None
            return None
        o = sel[0].copy()
        o.data = sel[0].data
        sc.collection.objects.link(o)
        apply_style(o, asset, part)
        # 親（FBX の軸補正）の回転を含めたワールドの向きを保つ（GenUtil.InstantiatePart と同じ）。
        # 前は親を外すだけで、杭の列 DesertProps_06/10 などが横倒し（骨のあばらのような弧）に描かれていた
        mw = sel[0].matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
        o.location = (0, 0, 0)
        o.hide_render = True
        o.hide_set(True)
        CACHE[key] = [o]
        return CACHE[key]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    fix_images(meshes, path)
    if part:
        for o in meshes:
            o.hide_render = True
            o.hide_set(True)
        PARTS[path] = meshes
        CACHE.pop(key, None)
        return proto(asset, part)
    if not part:
        # 全体モデル：部品ごとのオブジェクト（split_parts.py）を配置どおり 1 つにまとめる（色の決まりは部品ごとに先に当てる）
        for o in meshes: apply_style(o, asset, None)
        meshes = [join_whole(meshes, new)]
        new = meshes
    else:
        sel = [o for o in meshes if o.name.split(".")[0] == part] or [o for o in meshes if o.name.startswith(part)]
        meshes = sel[:1] if sel else meshes[:1]
        for o in meshes: apply_style(o, asset, part)
    keep = []
    for o in meshes:
        o.parent = None
        keep.append(o)
    for o in new:
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    # 部品は原点＝底面の中心になっている。全体モデルは原点のまま
    for o in keep:
        o.location = (0, 0, 0)
        o.hide_render = True  # 回転（取り込み時の軸補正）はそのまま残す
        o.hide_set(True)
    CACHE[key] = keep
    return keep

def place(plan, origin, ground=None):
    n = 0
    for q in plan:
        ps = proto(q["a"], q["p"])
        if not ps:
            continue
        for src in ps:
            o = src.copy()
            sc.collection.objects.link(o)
            o.hide_render = False
            o.hide_set(False)
            pos = Vector((origin[0] + q["pos"][0], origin[2] + q["pos"][2], origin[1] + q["pos"][1]))
            if ground is not None:
                pos.z = ground(q['pos'][0], q['pos'][2]) - 0.3
            o.location = pos
            from mathutils import Euler
            # Unity の pitch（ローカル X、負で前が上がる）→ Blender の X は符号が逆（art-r3-12：練習場のジャンプ台の丸太）
            o.rotation_euler = (Euler((-math.radians(q.get("pitch", 0.0)), math.radians(q["roll"]), -math.radians(q["yaw"]))).to_matrix() @ src.rotation_euler.to_matrix()).to_euler()
            s_ = q["s"]
            o.scale = (src.scale[0] * s_, src.scale[1] * s_, src.scale[2] * s_)
            if q.get("sv"):   # 引き伸ばし（Unity のローカル x, y, z）。取り込んだ部品のメッシュの軸は Unity と同じ（x, y = 上, z）で、
                sv = q["sv"]   # 軸の補正はオブジェクトの回転（X 90°）にある（Blender で WoodLogs_01 のメッシュを測って確認：1.45 × 0.32 × 0.99）
                o.scale = (src.scale[0] * sv[0], src.scale[1] * sv[1], src.scale[2] * sv[2])
            if q.get("t"):   # art-r5-04：置き物ごとの色（切り通しの段の明暗）
                tt = q["t"]
                for slot in o.material_slots:
                    base = slot.material
                    if base is None or not base.use_nodes: continue
                    # art-r6-01：スロットはメッシュに付いている（複製した物どうしで共有）。そのまま差し替えると同じメッシュの全部の物の色が
                    # 掛け算で重なり（メサ B は 21 回 × 0.5 = 真っ黒）になった。物ごと（OBJECT）のスロットにしてから、（マテリアル, 色）ごとに 1 つのマテリアルを付ける
                    slot.link = "OBJECT"
                    tkey = (base.name, tuple(round(x, 3) for x in tt))
                    if tkey in TINT_CACHE:
                        slot.material = TINT_CACHE[tkey]; continue
                    m2 = base.copy(); bs2 = m2.node_tree.nodes.get("Principled BSDF")
                    if bs2 is not None and not bs2.inputs["Base Color"].is_linked:
                        c2 = list(bs2.inputs["Base Color"].default_value)
                        bs2.inputs["Base Color"].default_value = (c2[0] * tt[0], c2[1] * tt[1], c2[2] * tt[2], 1)
                    elif bs2 is not None:
                        mix = m2.node_tree.nodes.new("ShaderNodeMixRGB"); mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
                        src_ = bs2.inputs["Base Color"].links[0].from_socket
                        m2.node_tree.links.new(src_, mix.inputs[1]); mix.inputs[2].default_value = (*tt, 1)
                        m2.node_tree.links.new(mix.outputs[0], bs2.inputs["Base Color"])
                    TINT_CACHE[tkey] = m2
                    slot.material = m2
            n += 1
    return n

S = {st["index"]: st for st in W["stages"]}
for st in W["stages"]:
    org = (st["center"][0] - 500, 0, st["center"][2] - 500)
    print("PLACED stage", st["index"], place(st["dress"], org))
    print("PLACED extras", st["index"], place(st.get("extras", []), org))

# ロビー（町）：地面は LobbyBuilder と同じ式（LobbyLayout.GroundHeight：ゆるい盛り上がり・低い尾根・北は高い・見晴らしの切れ目）を書き出した lobby.bin
LC = W["lobbyCenter"]
lob = Vector((LC[0], LC[2], 0))
FR = tuple(W['fenceRect']); GR = tuple(W['groundRect'])
_ld = open(os.path.join(ex, "lobby.bin"), "rb").read()
LNX, LNZ = struct.unpack("<ii", _ld[:8])
LH = np.frombuffer(_ld[8:], dtype="<f4").reshape(LNZ, LNX)
def lobby_h(x, z):
    i = min(max(int(round(x - GR[0])), 0), LNX - 1); j = min(max(int(round(z - GR[1])), 0), LNZ - 1)
    return float(LH[j, i])
nx, nz = int((GR[2] - GR[0]) / 2) + 1, int((GR[3] - GR[1]) / 2) + 1   # art-r5-10：広げたロビーの地面を 2m の格子で（段々にしない）
verts = []; faces = []
for j in range(nz):
    for i in range(nx):
        x = GR[0] + (GR[2] - GR[0]) * i / (nx - 1); z = GR[1] + (GR[3] - GR[1]) * j / (nz - 1)
        verts.append((lob.x + x, lob.y + z, lobby_h(x, z)))
for j in range(nz - 1):
    for i in range(nx - 1):
        a = j * nx + i; faces.append((a, a + 1, a + nx + 1, a + nx))
me = bpy.data.meshes.new("LobbyGround"); me.from_pydata(verts, [], faces)
og = bpy.data.objects.new("LobbyGround", me); sc.collection.objects.link(og)
og.data.materials.append(mat_color("LobbyGround", srgb2lin(DIRT)))
_before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=A("Models/Packs/WesternTown/WesternTown.fbx"))
_new = [o for o in bpy.data.objects if o not in _before]
_meshes = [o for o in _new if o.type == "MESH"]
fix_images(_meshes, A("Models/Packs/WesternTown/WesternTown.fbx"))
for o in _meshes: apply_style(o, "Packs/WesternTown/WesternTown.fbx", None)
for o in [join_whole(_meshes, _new)]:
    o.location = lob + Vector((0, 107, -13.7))   # 町の南端をロビー中心 +25m（LobbyBuilder と同じ）
# art-r3-12：ロビーは LobbyBuilder と同じ本物の素材で描く（world.json の lobbyProps：柵・受付・スタート台・大時計・柵の馬・厩舎・たまり場・練習場）。
# 遊びに使う物の目印（LobbyLayout.FunctionalSpots の位置・半径の円柱）は DEBUG_SPOTS=1 のときだけ
if os.environ.get("DEBUG_SPOTS") == "1":
    GROUPS = {"plaza": ((0.9, 0.15, 0.1), ("Spawn", "Ledger", "Wanted", "StartBell", "HudLever")),
              "mounts": ((0.15, 0.35, 0.95), ("Parking", "Picker", "CorralFence")),
              "hangout": ((1.0, 0.55, 0.1), ("Campfire", "NoticeBoard", "LastRace", "TownCrier")),
              "practice": ((0.2, 0.85, 0.25), ("Practice",)),
              "spare": ((0.2, 0.9, 0.9), ("SpareRelic",))}
    GM = {k: mat_color("Spot_" + k, srgb2lin(c), emit=0.6) for k, (c, _) in GROUPS.items()}
    for sp in W.get("spots", []):
        g = next((k for k, (_, pre) in GROUPS.items() if any(sp["n"].startswith(x) for x in pre)), "plaza")
        if sp["n"] in ("PracticeJump", "PracticeBump"): continue
        hgt = 3.0 if sp["n"].startswith("NoticeBoard") or sp["n"].startswith("Wanted") else (2.2 if sp["r"] < 2 else 0.8)
        if sp["n"] == "Spawn": hgt = 0.1
        bpy.ops.mesh.primitive_cylinder_add(radius=sp["r"], depth=hgt, location=(lob.x + sp["x"], lob.y + sp["z"], hgt / 2), vertices=16)
        bpy.context.active_object.data.materials.append(GM[g])
LOBO = (LC[0], 0, LC[2])
print("PLACED belt", place(W.get("belt", []), LOBO))
print("PLACED crown", place(W.get("crown", []), LOBO))
print("PLACED lobby", place(W["lobby"], LOBO))
print("PLACED lobbyProps", place(W.get("lobbyProps", []), LOBO))
print("PLACED backdrop", place(W["backdrop"], (0, 0, 0)))
print("PLACED vista", place(W.get("vista", []), LOBO, ground=None))
# 外の地面（TerrainBuilder.BuildFiller と同じ考え方の高さ：filler.bin）
_fd = open(os.path.join(ex, "filler.bin"), "rb").read()
FN = struct.unpack("<i", _fd[:4])[0]; FX0, FZ0, FX1, FZ1 = struct.unpack("<ffff", _fd[4:20])
FH = np.frombuffer(_fd[20:], dtype="<f4").reshape(FN, FN)
fv = [(FX0 + (FX1 - FX0) * i / (FN - 1), FZ0 + (FZ1 - FZ0) * j / (FN - 1), float(FH[j, i])) for j in range(FN) for i in range(FN)]
ff = [(j * FN + i, j * FN + i + 1, (j + 1) * FN + i + 1, (j + 1) * FN + i) for j in range(FN - 1) for i in range(FN - 1)]
fme = bpy.data.meshes.new("OuterGround"); fme.from_pydata(fv, [], ff)
fo = bpy.data.objects.new("OuterGround", fme); sc.collection.objects.link(fo)
fo.data.materials.append(mat_color("Filler", srgb2lin(W.get("fillerDirt", (0.86, 0.72, 0.52)))))   # art-r3-10：外の地面はステージの土の色
# art-r5-05：外の地面の土は近いステージの色へにじむ（gtf.bin = WorldBounds.GroundTint）。頂点の色で塗る
_gfp = os.path.join(ex, "gtf.bin")
if os.path.exists(_gfp):
    _gf = open(_gfp, "rb").read(); GTN = struct.unpack("<i", _gf[:4])[0]; GX0, GZ0, GX1, GZ1 = struct.unpack("<ffff", _gf[4:20])
    GTF = np.frombuffer(_gf[20:], dtype="<f4").reshape(GTN, GTN, 3)
    ca = fme.color_attributes.new("Tint", "FLOAT_COLOR", "POINT")
    for vi, v in enumerate(fme.vertices):
        gx_ = int(round((v.co.x - GX0) / (GX1 - GX0) * (GTN - 1))); gz_ = int(round((v.co.y - GZ0) / (GZ1 - GZ0) * (GTN - 1)))
        c_ = GTF[min(max(gz_, 0), GTN - 1), min(max(gx_, 0), GTN - 1)]
        ca.data[vi].color = (*srgb2lin(list(c_)), 1)
    _fm = fo.data.materials[0]; _vc = _fm.node_tree.nodes.new("ShaderNodeVertexColor"); _vc.layer_name = "Tint"
    _fm.node_tree.links.new(_vc.outputs["Color"], _fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
for p_ in fme.polygons: p_.use_smooth = True

# 空と光
world = bpy.data.worlds.new("W"); sc.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (*srgb2lin(W.get("lobbyFog", (0.86, 0.72, 0.56))), 1)   # 空 = 霧の色（Unity の霧と同じ）
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 5.0
sun.rotation_euler = (math.radians(28), math.radians(0), math.radians(160))   # LightingPlan.SUN_EULER（高さ 62°）に近い向き
sc.collection.objects.link(sun)

engine = "BLENDER_EEVEE"
try:
    sc.render.engine = "BLENDER_EEVEE"
except Exception:
    engine = "BLENDER_WORKBENCH"
if sc.render.engine == "BLENDER_EEVEE":
    sc.eevee.taa_render_samples = 16
    sc.eevee.use_soft_shadows = True
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.view_settings.view_transform = "Standard"
sc.view_layers[0].use_pass_mist = True
world.mist_settings.start = 350; world.mist_settings.depth = 750; world.mist_settings.falloff = "LINEAR"
sc.use_nodes = True
nt = sc.node_tree
for n_ in list(nt.nodes): nt.nodes.remove(n_)
rl = nt.nodes.new("CompositorNodeRLayers"); mix = nt.nodes.new("CompositorNodeMixRGB"); comp = nt.nodes.new("CompositorNodeComposite")
mix.inputs[2].default_value = (*srgb2lin((0.86, 0.74, 0.58)), 1)
nt.links.new(rl.outputs["Mist"], mix.inputs[0]); nt.links.new(rl.outputs["Image"], mix.inputs[1])
# art-r4-10：空は霧と別の色（Unity の Skybox/Procedural の _SkyTint）。背景は透明で描いて、霧をかけた絵の後ろに空の色を敷く
sc.render.film_transparent = True
# art-r5-14：空は 2 色のグラデーション（地平線 = 霧の色 → 天頂 = 空の色、Skybox/Procedural の地平線の帯の近似）。撮るごとに画像を作り直す
SKY_IMG = bpy.data.images.new("SkyGradient", 1600, 900, float_buffer=True)
sky_rgb = nt.nodes.new("CompositorNodeImage"); sky_rgb.image = SKY_IMG
aover = nt.nodes.new("CompositorNodeAlphaOver")
mul_a = nt.nodes.new("CompositorNodeSetAlpha"); nt.links.new(mix.outputs[0], mul_a.inputs[0]); nt.links.new(rl.outputs["Alpha"], mul_a.inputs[1])
nt.links.new(sky_rgb.outputs[0], aover.inputs[1]); nt.links.new(mul_a.outputs[0], aover.inputs[2]); nt.links.new(aover.outputs[0], comp.inputs[0])
def sky_gradient(zenith, horizon, pitch_deg, vfov_deg):
    """行ごとの見上げ角で地平線の色から天頂の色へ（地平線から 30° で天頂の色）。色は sRGB、画像は線形。"""
    H_, W_ = 900, 1600
    ys = np.arange(H_, dtype=np.float32)
    elev = pitch_deg + (ys / (H_ - 1) - 0.5) * vfov_deg   # Blender の画像は下の行から
    t = np.clip(elev / 30.0, 0.0, 1.0)[:, None]
    z = np.array(srgb2lin(list(zenith)), np.float32)[None, :]; h_ = np.array(srgb2lin(list(horizon)), np.float32)[None, :]
    rows = h_ * (1 - t) + z * t
    px = np.ones((H_, W_, 4), np.float32); px[..., :3] = rows[:, None, :]
    SKY_IMG.pixels.foreach_set(px.ravel())

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.clip_end = 20000

SHOTS = [x for x in os.environ.get("SHOTS", "").split(",") if x]   # 例 SHOTS=7_rider_1,8_near_1_3 で一部だけ描く
def fog_at(x, y):
    """カメラのいる所のステージの霧の色（LightingPlan.ForStage、ロビーはロビーの色）。"""
    for st_ in W["stages"]:
        c_ = st_["center"]
        if abs(x - c_[0]) <= 600 and abs(y - c_[2]) <= 600: return st_.get("fog", W.get("lobbyFog"))
    return W.get("lobbyFog", (0.86, 0.74, 0.58))
def sky_at(x, y):
    """カメラのいる所のステージの空の色（LightingPlan.SkyTint、ロビーは荒野と同じ）。"""
    for st_ in W["stages"]:
        c_ = st_["center"]
        if abs(x - c_[0]) <= 600 and abs(y - c_[2]) <= 600: return st_.get("sky", (0.42, 0.58, 0.78))
    return W["stages"][0].get("sky", (0.42, 0.58, 0.78))
def debug_mat(prefix):
    for o in bpy.data.objects:
        if not o.name.startswith(prefix) or o.type != "MESH": continue
        print("DBG", o.name, o.hide_render, "uv", o.data.uv_layers.keys(), "mats", [sl.material.name if sl.material else None for sl in o.material_slots])
        for sl in o.material_slots:
            m = sl.material
            if m is None: continue
            print("DBG  mat", m.name, m.blend_method, [(n.type, getattr(n, "image", None) and (n.image.name, tuple(n.image.size), n.image.has_data, n.image.filepath)) for n in m.node_tree.nodes])
            print("DBG  links", [(l.from_node.type, l.from_socket.name, l.to_node.type, l.to_socket.name) for l in m.node_tree.links])
        break
def shot(name, loc, target, lens=35):
    if SHOTS and not any(name.startswith(x) for x in SHOTS):
        return
    if os.environ.get("DEBUG_MAT"): debug_mat(os.environ["DEBUG_MAT"])
    for g_ in GUIDES:   # art-r4-03：空撮だけ目印を描く（NO_GUIDE=1 なら最初から作らない）
        g_.hide_render = not name.startswith(("00_", "1_", "2_", "3_"))
    # art-r6-11：ステージごとの太陽の強さ（LightingPlan：砂漠 1.05、ほか 1.25）。5.0 = 1.25 のとき
    _st = next((st_ for st_ in W["stages"] if abs(loc[0] - st_["center"][0]) <= 600 and abs(loc[1] - st_["center"][2]) <= 600), None)
    sun.data.energy = 5.0 * (_st.get("sunIntensity", 1.25) if _st else 1.25) / 1.25
    fc = srgb2lin(fog_at(loc[0], loc[1]))
    world.node_tree.nodes["Background"].inputs[0].default_value = (*fc, 1)
    mix.inputs[2].default_value = (*fc, 1)
    sk = sky_at(loc[0], loc[1]); fo = fog_at(loc[0], loc[1])
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    # art-r5-14：空の 2 色のグラデーション（地平線 = 霧の色、天頂 = 空の色）
    _dv = Vector(target) - cam.location
    _pitch = math.degrees(math.atan2(_dv.z, max(1e-3, math.hypot(_dv.x, _dv.y))))
    _vfov = math.degrees(2 * math.atan(cam.data.sensor_width * 900 / 1600 / (2 * lens)))
    sky_gradient(sk, fo, _pitch, _vfov)
    sc.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)
    check_black(name, sc.render.filepath)

def check_black(name, fp):
    """art-r6-01：真っ黒（全部の色 < 3/255）の画素が 0.3% を超えたら、その画素へカメラから光線を飛ばして物の名前を出して止める（BLACK_OK=1 で止めない）。"""
    try:
        img = bpy.data.images.load(fp, check_existing=False)
    except Exception:
        return
    W_, H_ = img.size
    px = np.empty(W_ * H_ * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(H_, W_, 4)
    bpy.data.images.remove(img)
    blk = np.all(px[..., :3] < 3.0 / 255.0, axis=-1)
    frac = float(blk.mean())
    if frac <= 0.003:
        return
    ys, xs = np.nonzero(blk)
    names = {}
    fr = [cam.matrix_world @ v for v in cam.data.view_frame(scene=sc)]   # 右上・右下・左下・左上
    dg = bpy.context.evaluated_depsgraph_get()
    for k in np.linspace(0, len(xs) - 1, min(40, len(xs))).astype(int):
        u, v = (xs[k] + 0.5) / W_, (ys[k] + 0.5) / H_
        pt = fr[2] + (fr[1] - fr[2]) * u + (fr[3] - fr[2]) * v
        hit, loc, nrm, idx, obj, mat = sc.ray_cast(dg, cam.location, (pt - cam.location).normalized())
        if hit: names[obj.name.split(".")[0]] = names.get(obj.name.split(".")[0], 0) + 1
    msg = f"BLACK {name}: {frac * 100:.2f}% pure-black pixels; objects: {sorted(names.items(), key=lambda t: -t[1])}"
    print(msg)
    if not os.environ.get("BLACK_OK"):
        raise SystemExit(msg)

world.mist_settings.start = 3000
if os.environ.get("LOBBY_ONLY"):
    _o = (lob.x, lob.y)
    for name, eye, tgt, lens in (("L1_aerial", (-150, -80, 150), (0, 100, 0), 26),
                                 ("L2_top", (0, 107, 300), (0, 107.1, 0), 30),
                                 ("L3_plaza_spawn_north", (W["spawn"][0], W["spawn"][2], 1.7), (W["spawn"][0] + 90 * math.sin(math.radians(W["spawnYaw"])), W["spawn"][2] + 90 * math.cos(math.radians(W["spawnYaw"])), 2), 24),
                                 ("L4_plaza_overview", (5, 8, 14), (-18, 40, 0), 24),
                                 ("L5_west_strip", (-33, 17, 9), (-24, 80, 0), 24),
                                 ("L6_practice_lane", (25, 82, 3.2), (28, 140, 2), 24),
                                 ("L7_practice_from_above", (55, 60, 40), (27, 114, 0), 26),
                                 ("L8_south_outward", (W["spawn"][0], 28, 1.7), (W["spawn"][0], -280, 3), 24),
                                 ("L9_east_outward", (30, 100, 3.2), (400, 100, 3), 24)):
        shot(name, (_o[0] + eye[0], _o[1] + eye[1], eye[2]), (_o[0] + tgt[0], _o[1] + tgt[1], tgt[2]), lens)
    raise SystemExit
shot("00_overview", (0, -2600, 1800), (0, 0, 0), 28)
world.mist_settings.start = 1200; world.mist_settings.depth = 1500   # ステージ全景は霧を薄く（外周の尾根ごしでも見える）
for st in W["stages"]:
    c = st["center"]
    shot(f"{st['index'] + 1}_stage_{st['index'] + 1}", (c[0] - 500, c[2] - 900, 900), (c[0], c[2], 40), 30)
world.mist_settings.start = 350; world.mist_settings.depth = 750
lob_t = (lob.x, lob.y, 5)
shot("4_lobby_town", (lob.x + 140, lob.y - 180, 70), lob_t, 30)
# 見どころの近景
b = S[0]["bridge"]
if b:
    off = Vector((S[0]["center"][0] - 500, S[0]["center"][2] - 500, 0))
    p = U(b["p"]) + off
    t3 = Vector((b["t"][0], b["t"][2], 0)).normalized(); r3 = Vector((t3.y, -t3.x, 0))
    # 溝の縁（橋の手前 10m・溝の端の外 38m）から 30° 見下ろす
    q = p + t3 * 10 + r3 * 38
    q.z = max(p.z + math.tan(math.radians(30)) * (q - p).length, ground_at(S[0], q.x, q.y) + 3)
    shot("5_rock_bridge", q, p, 32)
j = S[1]["jump"]
if j:
    off = Vector((S[1]["center"][0] - 500, S[1]["center"][2] - 500, 0))
    p = U(j["p"]) + off
    shot("6_dune_jump", p + Vector((-60, -70, 30)), p, 30)
# コース沿いの近景（コースの横 45m・高さ 22m から、少し先のコースを見る）
for st in W["stages"]:
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    c = st["course"]
    for j, f in enumerate((0.15, 0.45, 0.75)):
        k = int(len(c) * f)
        p0 = U(c[k]) + off
        p1 = U(c[min(k + 8, len(c) - 1)]) + off
        t = (p1 - p0); t.z = 0; t.normalize()
        side = Vector((-t.y, t.x, 0))
        cam_pos = p0 - t * 40 + side * 45
        cam_pos.z = max(p0.z, ground_at(st, cam_pos.x, cam_pos.y)) + 22   # 斜面・壁の中に入らない
        shot(f"8_near_{st['index'] + 1}_{j + 1}", cam_pos, p1 + Vector((0, 0, 2)), 26)
# 騎手の目の高さ（地面 + 2.6m）でコースを見る：スタート・途中の一番きついカーブ・見せ場・ゴール前
def rider_eye(st, k, ahead=10, name=""):
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    c = st["course"]
    k = max(0, min(k, len(c) - 2))
    p0 = U(c[k]) + off
    p1 = U(c[min(k + ahead, len(c) - 1)]) + off
    p0.z = max(p0.z, ground_at(st, p0.x, p0.y)) + 2.6
    p1.z = max(p1.z, ground_at(st, p1.x, p1.y)) + 2.0
    shot(name, p0, p1, 22)
def nearest_k(st, q):
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    return min(range(len(st["course"])), key=lambda i: ((U(st["course"][i]) + off).xy - q.xy).length)
for st in W["stages"]:
    c = st["course"]; n = len(c); sid = st["index"] + 1
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    rider_eye(st, 1, 8, f"7_rider_{sid}_a_start")
    best, bk = -1, n // 2
    for i in range(int(n * 0.25), int(n * 0.75)):
        a = U(c[i]) - U(c[i - 3]); b = U(c[i + 3]) - U(c[i]); a.z = b.z = 0
        if a.length > 0 and b.length > 0:
            ang = a.angle(b)
            if ang > best: best, bk = ang, i
    rider_eye(st, bk - 4, 8, f"7_rider_{sid}_b_corner")
    sp = st["bridge"] or st["jump"] or st.get("pass")
    if sp:
        k = nearest_k(st, U(sp["p"]) + off)
        rider_eye(st, k - 6, 6, f"7_rider_{sid}_c_setpiece")
    elif st["shortcut"] is None and st["relics"]:   # 見せ場がないステージは中ほどの遺物
        k = nearest_k(st, U(st["relics"][len(st["relics"]) // 2]) + off)
        rider_eye(st, k - 6, 6, f"7_rider_{sid}_c_setpiece")
    elif st["shortcut"]:
        k = nearest_k(st, U(st["shortcut"]["a"]) + off)
        p0 = U(c[max(k - 3, 0)]) + off; p0.z = max(p0.z, ground_at(st, p0.x, p0.y)) + 2.6
        q = U(st["shortcut"]["b"]) + off
        shot(f"7_rider_{sid}_c_setpiece", p0, q + Vector((0, 0, 2)), 22)
    # course-r3-11：ゴール前は書き出したゴールの添字から（200m・120m 手前、60m 手前）。前は最後の点（流しの中）から撮っていた
    gk = st.get("goalK", n - 9)
    rider_eye(st, gk - 20, 12, f"7_rider_{sid}_d_finish")
    rider_eye(st, gk - 12, 8, f"7_rider_{sid}_d_finish_120")
    rider_eye(st, gk - 6, 6, f"7_rider_{sid}_d_finish_60")
    # course-r3-11：近道の入口（50m 手前から）
    if st.get("scEntryK", -1) >= 0:
        rider_eye(st, st["scEntryK"] - 5, 5, f"7_rider_{sid}_e_shortcut")
# 町の中（ロビー中心からの相対）から。目の前 8m に物があれば、町の通りの向き（北）へずらして最初に当たる物が 12m より遠くなる所から撮る
from mathutils import Vector as _V
def eye_shot(name, rel_eye, rel_tgt, lens=24):
    e = lob + _V((rel_eye[0], rel_eye[1], 0)); e.z = rel_eye[2]
    tg = lob + _V((rel_tgt[0], rel_tgt[1], 0)); tg.z = rel_tgt[2]
    dg = bpy.context.evaluated_depsgraph_get()
    for _ in range(12):
        d = (tg - e).normalized()
        hit, loc, *_r = sc.ray_cast(dg, e, d, distance=12.0)
        if not hit: break
        e = e + _V((0, 4.0, 0)); tg = tg + _V((0, 4.0, 0))
    shot(name, tuple(e), tuple(tg), lens)
sp = W["spawn"]; syaw = math.radians(W["spawnYaw"])
eye_shot("9_lobby_eye_N", (sp[0], sp[2], 1.7), (sp[0] + math.sin(syaw) * 200, sp[2] + math.cos(syaw) * 200, 3))   # スポーン → 町の大通り
# art-r3-12：東・西・南もスポーンから（スポーンの向き ±90°・180°。前の固定の座標は LOBBY_CENTER が動く前のもので、建物の中から撮っていた）
for nm, da in (("E", 90.0), ("W", -90.0), ("S", 180.0)):
    a_ = syaw + math.radians(da)
    eye_shot(f"9_lobby_eye_{nm}", (sp[0], sp[2], 1.7), (sp[0] + math.sin(a_) * 200, sp[2] + math.cos(a_) * 200, 3))
for gi, gaz in enumerate(W.get("vistaGaps", [])):   # 広場 → 南・南東の見晴らしの切れ目
    a_ = math.radians(gaz)
    shot(f"9_lobby_vista_{gi + 1}", tuple(lob + _V((sp[0], sp[2], 1.7))), tuple(lob + _V((sp[0] + math.sin(a_) * 400, sp[2] + math.cos(a_) * 400, 8))), 24)
shot("9_lobby_practice_rider", tuple(lob + _V((27.5, 89, 2.6))), tuple(lob + _V((28, 130, 1.5))), 24)   # 練習場を騎手の目の高さで
shot("9_lobby_ride_SE", tuple(lob + _V((120, -60, lobby_h(120, -60) + 3.2))), tuple(lob + _V((300, -180, lobby_h(300, -180) + 4))), 24)   # art-r5-10：地面の高さ + 3.2m
# 境目の切り通し（300m 手前から：三角の切れ込みが見えるか）
pa = S[2].get("pass")
if pa:
    off = Vector((S[2]["center"][0] - 500, S[2]["center"][2] - 500, 0))
    c = S[2]["course"]
    k = nearest_k(S[2], U(pa["p"]) + off)
    rider_eye(S[2], max(k - 26, 0), 26, "7_rider_3_c_pass_approach")   # コースの点は 10m ごと（260m 手前）
    rider_eye(S[2], max(k - 12, 0), 12, "7_rider_3_c_pass_120")         # course-r3-02/11：120m・250m 手前（切り通しの口が見えるか）
    rider_eye(S[2], max(k - 25, 0), 25, "7_rider_3_c_pass_250")
print("ENGINE", sc.render.engine)
