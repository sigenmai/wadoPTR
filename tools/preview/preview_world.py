"""ワールド生成ツールと同じ計算結果（world.json / h*.bin / s*.bin）から下見用の 3D シーンを作って描画する。
blender -b --python preview_world.py -- <exportフォルダ> <assetsルート> <出力フォルダ>
Unity (x, y, z) → Blender (x, z, y)
"""
import bpy, bmesh, sys, os, json, struct, math, random
import numpy as np
from mathutils import Vector

ex, assets, out = sys.argv[sys.argv.index("--") + 1:][:3]
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
    step = 2
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
    S = np.asarray(Image.fromarray((np.clip(s, 0, 1) * 255).astype(np.uint8)).resize(slope.shape[::-1]), np.float32) / 255
    rock = np.clip((slope - 28) / 12, 0, 1) * (1 - S)
    col = np.array(DIRT)[None, None] * (1 - S[..., None] - rock[..., None]).clip(0, 1) + np.array(SAND) * S[..., None] + np.array(ROCK) * rock[..., None]
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

def import_part(path, part_prefix=None, index=None):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    keep = meshes
    if part_prefix is not None and index is not None and len(meshes) > 1:
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
RELIC = mat_color("Relic", srgb2lin((0.2, 0.9, 0.8)), emit=4.0)
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
for st in W["stages"]:
    GRID[st["index"]] = read_grid(os.path.join(ex, f"h{st['index']}.bin"))
    terrain(st)
    off = Vector((st["center"][0] - 500, st["center"][2] - 500, 0))
    pts = [U(p) + off + Vector((0, 0, 0.4)) for p in st["course"]]
    polyline(f"Course{st['index']}", [q - Vector((0, 0, 0.3)) for q in pts], 0.5, ROAD)
    for k, p in enumerate(st["cps"]):
        q = U(p) + off
        box(f"CP{k}", q + Vector((14, 0, 1.5)), (0.4, 0.4, 3), CPM)
    for k, p in enumerate(st["relics"]):
        q = U(p) + off
        bpy.ops.mesh.primitive_ico_sphere_add(radius=3, location=q + Vector((0, 0, 4)))
        bpy.context.active_object.data.materials.append(RELIC)
    s0, s1 = U(st["course"][1]) + off, U(st["course"][-1]) + off
    box("StartLine", s0 + Vector((0, 0, 0.5)), (28, 3, 1), START)
    box("GoalGate", s1 + Vector((0, 0, 6)), (30, 2, 12), GOAL)
    if st["bridge"]:
        p = U(st["bridge"]["p"]) + off
        t = st["bridge"]["t"]
        yaw = math.atan2(t[2], t[0])
        box("RockBridge", p + Vector((0, 0, 0.5)), (26, 26, 2), BRIDGE, yaw)
    if st["jump"]:
        p = U(st["jump"]["p"]) + off
        t = st["jump"]["t"]
        yaw = math.atan2(t[2], t[0])
        box("DuneJump", p + Vector((0, 0, 1.5)), (16, 20, 3), JUMP, yaw)
    if st["shortcut"]:
        a, b = U(st["shortcut"]["a"]) + off, U(st["shortcut"]["b"]) + off
        polyline("Shortcut", [a + Vector((0, 0, 1)), b + Vector((0, 0, 1))], 4.0, SHORT)

# Build World と同じ配置計画（DressingPlan）で素材を置く
A = lambda *p: os.path.join(assets, *p)
CACHE = {}
def proto(asset, part):
    key = (asset, part)
    if key in CACHE:
        return CACHE[key]
    path = A("Models", asset)
    if not os.path.exists(path):
        CACHE[key] = None
        return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if part:
        sel = [o for o in meshes if o.name.split(".")[0] == part] or [o for o in meshes if o.name.startswith(part)]
        meshes = sel[:1] if sel else meshes[:1]
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
            o.rotation_euler = (Euler((0, math.radians(q["roll"]), -math.radians(q["yaw"]))).to_matrix() @ src.rotation_euler.to_matrix()).to_euler()
            s_ = q["s"]
            o.scale = (src.scale[0] * s_, src.scale[1] * s_, src.scale[2] * s_)
            n += 1
    return n

S = {st["index"]: st for st in W["stages"]}
for st in W["stages"]:
    org = (st["center"][0] - 500, 0, st["center"][2] - 500)
    print("PLACED stage", st["index"], place(st["dress"], org))

# ロビー（町）：柵の外は外へ向かって上り（0.6 の勾配で最大 12m）、外周 30m は尾根
lob = Vector((0, -820, 0))
FR = (-172.0, -112.0, 172.0, 232.0); GR = (-210.0, -150.0, 210.0, 270.0)
def lobby_h(x, z):
    dx = max(FR[0] - x, 0, x - FR[2]); dz = max(FR[1] - z, 0, z - FR[3])
    d = math.hypot(dx, dz)
    h = min(12.0, 0.6 * d)
    e = min(x - GR[0], GR[2] - x, z - GR[1], GR[3] - z)
    if e < 30: h += (1 - e / 30) * 22
    return h
nx, nz = 106, 106
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
for o in import_part(A("Models/Packs/WesternTown/WesternTown.fbx")):
    o.location = lob + Vector((0, 0, -13.7))
FENCE = mat_color("Fence", srgb2lin((0.45, 0.29, 0.18)))
for a_, b_ in W.get("fence", []):
    A_ = Vector((lob.x + a_[0], lob.y + a_[2], 0)); B_ = Vector((lob.x + b_[0], lob.y + b_[2], 0))
    mid = (A_ + B_) / 2; d = B_ - A_
    box("FencePiece", mid + Vector((0, 0, 0.8)), (d.length, 0.12, 1.57), FENCE, math.atan2(d.y, d.x))
print("PLACED belt", place(W.get("belt", []), (0, 0, -820), lobby_h))
print("PLACED lobby", place(W["lobby"], (0, 0, -820)))
print("PLACED backdrop", place(W["backdrop"], (0, 0, 0)))
bpy.ops.mesh.primitive_plane_add(size=9000, location=(0, 0, -6))
bpy.context.active_object.data.materials.append(mat_color("Filler", srgb2lin((0.80, 0.62, 0.44))))

# 空と光
world = bpy.data.worlds.new("W"); sc.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (*srgb2lin((0.62, 0.78, 0.92)), 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 5.0
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
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
nt.links.new(rl.outputs["Mist"], mix.inputs[0]); nt.links.new(rl.outputs["Image"], mix.inputs[1]); nt.links.new(mix.outputs[0], comp.inputs[0])

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.clip_end = 20000

def shot(name, loc, target, lens=35):
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    sc.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("SHOT", name)

world.mist_settings.start = 3000
if os.environ.get("LOBBY_ONLY"):
    for name, eye, tgt in (("9_lobby_eye_N", (0, -825, 1.7), (0, -560, 3)), ("9_lobby_eye_E", (60, -790, 1.7), (400, -790, 3)), ("9_lobby_eye_W", (-60, -790, 1.7), (-400, -790, 3)), ("9_lobby_ride_SE", (120, -880, 3.2), (300, -1000, 4)), ("4_lobby_town", (140, -1000, 70), (0, -820, 5))):
        shot(name, eye, tgt, 24 if name.startswith("9") else 30)
    raise SystemExit
shot("00_overview", (0, -2600, 1800), (0, 0, 0), 28)
world.mist_settings.start = 350
for st in W["stages"]:
    c = st["center"]
    shot(f"{st['index'] + 1}_stage_{st['index'] + 1}", (c[0] - 650, c[2] - 750, 520), (c[0], c[2], 20), 30)
lob_t = (0, -820, 5)
shot("4_lobby_town", (140, -1000, 70), lob_t, 30)
# 見どころの近景
b = S[0]["bridge"]
if b:
    off = Vector((S[0]["center"][0] - 500, S[0]["center"][2] - 500, 0))
    p = U(b["p"]) + off
    shot("5_rock_bridge", p + Vector((110, -140, 140)), p, 32)
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
        cam_pos = p0 - t * 40 + side * 45 + Vector((0, 0, 22))
        shot(f"8_near_{st['index'] + 1}_{j + 1}", cam_pos, p1 + Vector((0, 0, 2)), 26)
for name, eye, tgt in (("9_lobby_eye_N", (0, -825, 1.7), (0, -560, 3)), ("9_lobby_eye_S", (0, -820, 1.7), (0, -1100, 3)),
                       ("9_lobby_eye_E", (60, -790, 1.7), (400, -790, 3)), ("9_lobby_eye_W", (-60, -790, 1.7), (-400, -790, 3)),
                       ("9_lobby_ride_SE", (120, -880, 3.2), (300, -1000, 4))):
    shot(name, eye, tgt, 24)
print("ENGINE", sc.render.engine)
