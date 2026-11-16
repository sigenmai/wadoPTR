#!/usr/bin/env python3
"""layout.json を Blender (Cycles/CPU) で描画して、Unity を開かずに部屋の見た目を確かめる。

  python Tools/preview_layout.py -- <out.png> [view]
      view = spawn | top | table | work | terminal | hatch | dock | back | corner | rail | chute | low
      または任意のカメラ:  "目x,目y,目z:注視x,注視y,注視z:画角"   例  "3.5,2.6,-2.8:0,0.9,0.9:70"

原体にかかる修整モザイク (WadoPTR/Mosaic シェーダー) は、原体だけのマスクを別に描いて後処理で再現する。

Unity(x, y, z) = Blender(-x, z, -y)。左手系→右手系の読み替えなので、見た目は Unity と同じ向きになる。
要: pip install bpy pillow
"""
import bpy, json, math, os, sys
from mathutils import Matrix, Vector
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, "Assets", "WadoPTR")
args = sys.argv[sys.argv.index("--") + 1:]
out_png = args[0]
view = args[1] if len(args) > 1 else "spawn"
L = json.load(open(os.path.join(A, "Editor", "layout.json"), encoding="utf-8"))
PH = json.load(open(os.path.join(ROOT, "Tools", "polyhaven_manifest.json"), encoding="utf-8"))

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
M = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))   # Blender -> Unity
Mi = M.inverted()
F = Matrix.Diagonal((1, 1, -1, 1))


def trs(pos, yaw=0, pitch=0, roll=0, scale=(1, 1, 1)):
    r = Matrix.Rotation(math.radians(yaw), 4, "Y") @ Matrix.Rotation(math.radians(pitch), 4, "X") @ Matrix.Rotation(math.radians(roll), 4, "Z")
    return Matrix.Translation(Vector(pos)) @ r @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1))


def img_mat(name, path, scale=None, tint=(1, 1, 1), rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if path and os.path.exists(path):
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(path, check_existing=True)
        if scale:
            t.projection = "BOX"
            tc = m.node_tree.nodes.new("ShaderNodeTexCoord")
            mp = m.node_tree.nodes.new("ShaderNodeMapping")
            mp.inputs["Scale"].default_value = (scale, scale, scale)
            m.node_tree.links.new(tc.outputs["Object"], mp.inputs["Vector"])
            m.node_tree.links.new(mp.outputs["Vector"], t.inputs["Vector"])
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (*tint, 1)
    return m


def flat(name, rgb, rough=0.6, metal=0.0, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


T = os.path.join(A, "Textures")


def tex(name, scale, rough, metal=0.0, tint=None):
    m = img_mat(name, os.path.join(T, name, name + "_diff.jpg"), scale, rough=rough, metal=metal)
    if tint:
        b = m.node_tree.nodes["Principled BSDF"]
        t = [n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"][0]
        mx = m.node_tree.nodes.new("ShaderNodeMix")
        mx.data_type = "RGBA"
        mx.blend_type = "MULTIPLY"
        mx.inputs[0].default_value = 1.0
        m.node_tree.links.new(t.outputs["Color"], mx.inputs[6])
        mx.inputs[7].default_value = (*tint, 1)
        m.node_tree.links.new(mx.outputs[2], b.inputs["Base Color"])
    return m


# WadoWorldBuilder.cs の BuildMaterials と同じ名前・同じ意図
MATS = {
    "floor": tex("concrete_floor_worn_001", 0.35, 0.12, tint=(0.55, 0.5, 0.5)),
    "ceiling": tex("dirty_concrete", 0.35, 0.9, tint=(0.35, 0.32, 0.32)),
    "wall": tex("concrete_wall_006", 0.4, 0.75, tint=(0.8, 0.72, 0.7)),
    "rust": tex("rusty_metal_sheet", 0.8, 0.7, 0.4, tint=(0.7, 0.62, 0.58)),
    "rust_red": tex("rusty_painted_metal", 0.6, 0.65, 0.3, tint=(0.75, 0.6, 0.58)),
    "door": tex("painted_metal_shutter", 1.0, 0.6, 0.5, tint=(0.6, 0.55, 0.55)),
    "steel": flat("steel", (0.2, 0.2, 0.21), 0.32, 0.85),
    "black": flat("black", (0.008, 0.007, 0.008), 0.9),
    "rubber": flat("rubber", (0.02, 0.02, 0.022), 0.8),
    "led_black": flat("led_black", (0.012, 0.01, 0.012), 0.35),
    "paper": flat("paper", (0.3, 0.26, 0.19), 0.9),
    "plastic_grey": flat("plastic_grey", (0.16, 0.16, 0.17), 0.6),
    "plastic_red": flat("plastic_red", (0.35, 0.04, 0.05), 0.5),
    "hazard": flat("hazard", (0.75, 0.55, 0.05), 0.6),
    "neon_pink": flat("neon_pink", (1.0, 0.12, 0.42), 0.5, emit=14.0),
    "neon_red": flat("neon_red", (1.0, 0.06, 0.08), 0.5, emit=12.0),
    "neon_cyan": flat("neon_cyan", (0.1, 0.8, 1.0), 0.5, emit=9.0),
    "neonpanel_pink": flat("neonpanel_pink", (1.0, 0.14, 0.4), 0.5, emit=7.0),
    "neonpanel_red": flat("neonpanel_red", (1.0, 0.07, 0.1), 0.5, emit=6.0),
    "neonpanel_cyan": flat("neonpanel_cyan", (0.15, 0.85, 1.0), 0.5, emit=6.0),
    "glowpanel_warm": flat("glowpanel_warm", (1.0, 0.62, 0.28), 0.5, emit=2.2),
    "glowpanel_pink": flat("glowpanel_pink", (1.0, 0.2, 0.45), 0.5, emit=2.0),
    "glowpanel_cyan": flat("glowpanel_cyan", (0.3, 0.85, 1.0), 0.5, emit=2.0),
    "glowpanel_white": flat("glowpanel_white", (0.85, 0.95, 1.0), 0.5, emit=4.0),
    "bulb_warm": flat("bulb_warm", (1.0, 0.75, 0.4), 0.5, emit=30.0),
}
skin = img_mat("skin", os.path.join(A, "Models", "body", "body_skin.jpg"))
cut = flat("cut", (0.22, 0.02, 0.03), 0.35)


def add_box(b):
    if b["mat"] == "invisible" or b["mat"].startswith("glow_"):
        return      # 当たり判定だけの箱と、ネオンのにじみ(後処理で再現する)
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.object
    o.name = b["name"]
    o.matrix_world = Mi @ trs(b["pos"], b["yaw"], 0, 0, b["size"]) @ M
    o.data.materials.append(MATS.get(b["mat"], MATS["plastic_grey"]))


cache = {}


def add_fbx(path, mat_fn):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.type == "MESH":
            for i, s in enumerate(o.material_slots):
                s.material = mat_fn(s.material.name if s.material else "", i)
    return new


def ph_mat(model):
    def fn(mat_name, i):
        base = mat_name.split(".0")[0] if mat_name.endswith((".001", ".002")) and mat_name not in [m["name"] for m in PH["models"][model]["materials"]] else mat_name
        for m in PH["models"][model]["materials"]:
            if m["name"] == base or m["name"] == mat_name:
                key = model + "/" + m["name"]
                if key not in cache:
                    p = os.path.join(A, "Models", model, m["diff"]) if m["diff"] else None
                    cache[key] = img_mat(key, p, rough=1 - m["smoothness"], metal=m["metallic"])
                return cache[key]
        return MATS["plastic_grey"]
    return fn


for b in L["boxes"]:
    add_box(b)
for p in L["props"]:
    if p["group"] == "OrganShelf":
        continue
    objs = add_fbx(os.path.join(A, "Models", p["model"], p["model"] + ".fbx"), ph_mat(p["model"]))
    s = p["scale"]
    for o in objs:
        o.matrix_world = Mi @ trs(p["pos"], p["yaw"], p["pitch"], p["roll"], (s, s, s)) @ M @ o.matrix_world

# 配線 (たるんだケーブル)
cable_mat = flat("cable", (0.006, 0.006, 0.007), 0.7)
for c in L.get("cables", []):
    a_, b_ = Vector(c["a"]), Vector(c["b"])
    cu = bpy.data.curves.new("cable", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = c["width"] / 2
    cu.bevel_resolution = 1
    sp = cu.splines.new("POLY")
    n = 14
    sp.points.add(n)
    for k in range(n + 1):
        t = k / n
        p = a_.lerp(b_, t)
        p.y -= c["sag"] * 4 * t * (1 - t)
        bp = Mi @ p
        sp.points[k].co = (bp.x, bp.y, bp.z, 1)
    o = bpy.data.objects.new("cable", cu)
    sc.collection.objects.link(o)
    o.data.materials.append(cable_mat)

# 原体(処理台の上)
anch = {a["name"]: a for a in L["anchors"]}
tp = anch["Table"]["pos"]
body_objs = []
for part in L["body"]:
    objs = add_fbx(os.path.join(A, "Models", "body", part["name"] + ".fbx"), lambda n, i: skin if i == 0 else cut)
    pos = [tp[k] + part["offset"][k] for k in range(3)]
    for o in objs:
        o.matrix_world = Mi @ trs(pos) @ M @ o.matrix_world
        body_objs.append(o)
    # 切取線 (WadoWorldBuilder.cs の MakeBodyPart と同じ形)
    c = list(part["cut"])
    along_x = part["name"] in ("arm_l", "arm_r", "torso")
    ln, top = 0.21, 0.218
    if part["name"] == "head":
        ln, top = 0.16, 0.186
    elif part["name"].startswith("arm"):
        ln, top = 0.15, 0.176
        c[0] += 0.025
    elif part["name"] == "torso":
        ln, top = 0.44, 0.256
    bpy.ops.mesh.primitive_cube_add(size=1)
    mk = bpy.context.object
    mk.matrix_world = Mi @ trs((tp[0] + c[0], tp[1] + top - 0.0275, tp[2] + c[2]), 90 if along_x else 0, 0, 0, (0.006, 0.055, ln)) @ M
    mk.data.materials.append(flat("mark_" + part["name"], (1.0, 0.25, 0.05), 0.5, emit=3.0))

# 刃物
br = anch["BladeRest"]["pos"]
for o in add_fbx(os.path.join(A, "Models", "machete", "machete.fbx"), ph_mat("machete")):
    o.matrix_world = Mi @ trs(br) @ M @ o.matrix_world

# 掲示
FONTS = {"gothic": "MPLUS1p-Bold.ttf", "dot": "DotGothic16-Regular.ttf", "hand": "Yomogi-Regular.ttf"}
fonts = {k: bpy.data.fonts.load(os.path.join(A, "Fonts", v)) for k, v in FONTS.items()}
import re


def hexrgb(h):
    h = h.lstrip("#")
    return tuple((int(h[i:i + 2], 16) / 255) ** 2.2 for i in (0, 2, 4))


def add_text(text, pos, yaw, size, font, color, align="left", emit=0.0, pitch=0, roll=0):
    plain = re.sub(r"<[^>]+>", "", text)
    cu = bpy.data.curves.new("t", "FONT")
    cu.body = plain
    cu.font = fonts[font]
    cu.size = size
    cu.space_line = 1.25
    cu.align_x = "CENTER" if align == "center" else "LEFT"
    cu.align_y = "TOP"
    o = bpy.data.objects.new("text", cu)
    sc.collection.objects.link(o)
    o.matrix_world = Mi @ trs(pos, yaw, pitch, roll) @ F
    o.data.materials.append(flat("ink" + color + str(emit), hexrgb(color), 0.8, emit=emit))
    return o


for lb in L["labels"]:
    led = lb["font"] == "dot" and lb["color"] not in ("#1B1B1B",)
    add_text(lb["text"], lb["pos"], lb["yaw"], lb["size"], lb["font"], lb["color"], lb["align"], 3.0 if led else 0.0, lb["pitch"], lb["roll"])
# 動的表示のダミー (実機ではスクリプトが書き換える)
add_text("¥12万3,400", anch["Money"]["pos"], 0, 0.3, "dot", "#FF7AA8", emit=5)
add_text("頭部  105%  ▲\n上肢   85%  ▼\n下肢  120%  ▲\n体幹   95%  →\n―――  取扱不可", anch["Market"]["pos"], 0, 0.1, "dot", "#FADCE6", emit=2.5)
a = anch["Terminal"]
for i in range(10):
    p = [a["pos"][0] - 0.24, a["pos"][1] - i * 0.175, a["pos"][2]]
    add_text("刃物研磨 申請  Lv.0", p, 180, 0.075, "dot", "#FADCE6", emit=2.5)
    p2 = [p[0], p[1] - 0.092, p[2]]
    add_text("¥3,000  一振りの切断深度", p2, 180, 0.05, "dot", "#FFE27A", emit=2.5)
add_text("原体 No.7-04101\n区分 成人・不詳", anch["HatchText"]["pos"], -90, 0.1, "dot", "#FF6A5E", emit=3)
add_text("体幹\n29.51 kg  鮮度 A\n¥2,140", anch["Scale"]["pos"], 90, 0.085, "dot", "#6FE3FF", emit=3)

for lt in L["lights"]:
    d = bpy.data.lights.new(lt["name"], "POINT")
    d.energy = lt["intensity"] * 150
    d.color = lt["color"]
    d.shadow_soft_size = 0.25
    o = bpy.data.objects.new(lt["name"], d)
    sc.collection.objects.link(o)
    o.matrix_world = Mi @ trs(lt["pos"])

VIEWS = {
    "spawn": ((0, 1.6, -3.1), (0, 1.35, 3.0), 62),
    "top": ((0.0, 9.5, -0.6), (0, 0, 0.05), 62),
    "table": ((-1.9, 1.75, -0.9), (0.3, 0.9, 0.9), 60),
    "work": ((0.1, 1.62, -0.05), (-0.3, 0.95, 0.95), 78),
    "terminal": ((-1.6, 1.6, -0.3), (-2.6, 1.4, -3.5), 66),
    "hatch": ((-1.2, 1.6, -1.0), (-4.5, 1.3, 0.7), 62),
    "dock": ((0.6, 1.6, 0.2), (6.5, 2.2, -2.2), 78),
    "alley": ((2.2, 1.6, -1.2), (9.0, 2.6, -2.2), 80),
    "back": ((0.5, 1.6, 2.6), (0.6, 1.3, -3.5), 75),
    "corner": ((4.1, 2.9, -3.1), (-0.6, 0.7, 0.9), 78),
    "rail": ((-3.6, 1.55, -0.4), (1.5, 0.95, 1.0), 72),
    "chute": ((3.3, 1.65, 2.4), (-0.4, 0.9, 0.7), 72),
    "low": ((-0.9, 1.15, 2.1), (0.1, 0.98, 0.9), 70),
}
if view in VIEWS:
    eye, tgt, fov = VIEWS[view]
else:
    e_, t_, f_ = view.split(":")
    eye = tuple(float(x) for x in e_.split(","))
    tgt = tuple(float(x) for x in t_.split(","))
    fov = float(f_)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
e = Mi @ Vector(eye)
t = Mi @ Vector(tgt)
cam.location = e
cam.rotation_euler = (t - e).to_track_quat("-Z", "Y").to_euler()
cam.data.lens_unit = "FOV"
cam.data.angle = math.radians(fov)
cam.data.clip_start = 0.05
sc.camera = cam
if view == "top":
    for o in bpy.data.objects:
        if o.name in ("Ceiling", "Alley_Sky") or o.name.startswith(("Beam_", "Pipe_Run", "Pipe_Cross", "Bridge_", "Balcony_", "Awning", "cable", "Lamp_", "model")) and o.location.z > 2.9:
            o.hide_render = True
    for o in bpy.data.objects:
        if o.name.startswith("Tube_") or o.name.startswith("mounted_fluorescent"):
            o.hide_render = True

w = bpy.data.worlds.new("w")
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.03, 0.02, 0.025, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.6 if view != "top" else 8.0
sc.world = w
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = int(os.environ.get("SAMPLES", "40"))
sc.cycles.use_denoising = True
sc.render.resolution_x = int(os.environ.get("RESX", "1280"))
sc.render.resolution_y = int(os.environ.get("RESY", "720"))
sc.view_settings.view_transform = "Filmic" if "Filmic" in [i.identifier for i in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else sc.view_settings.view_transform
sc.render.filepath = out_png
bpy.ops.render.render(write_still=True)

# ---- 修整モザイクの再現: 原体だけ白く光らせたマスクを描き、その範囲をブロック化する
white = flat("mask_white", (1, 1, 1), 1.0, emit=1.0)
black = flat("mask_black", (0, 0, 0), 1.0)
black.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.0
for o in sc.objects:
    if o.type in ("MESH", "FONT"):
        m = white if o in body_objs else black
        if len(o.material_slots) == 0:
            o.data.materials.append(m)
        for sl in o.material_slots:
            sl.material = m
    elif o.type == "LIGHT":
        o.hide_render = True
w.node_tree.nodes["Background"].inputs[1].default_value = 0.0
sc.cycles.samples = 4
sc.cycles.use_denoising = False
sc.view_settings.view_transform = "Standard"
mask_png = out_png + ".mask.png"
sc.render.filepath = mask_png
bpy.ops.render.render(write_still=True)

img = Image.open(out_png).convert("RGB")
mask = Image.open(mask_png).convert("L").point(lambda v: 255 if v > 90 else 0)
os.remove(mask_png)
dist = (Vector(eye) - Vector(tp)).length
focal = (img.width / 2) / math.tan(math.radians(fov) / 2)
block = max(4, int(round(0.034 / max(dist, 0.3) * focal)))
small = img.resize((max(1, img.width // block), max(1, img.height // block)), Image.BOX)
mosaic = small.resize(img.size, Image.NEAREST)
img.paste(mosaic, (0, 0), mask)
# ネオンのにじみ (Unity 側では WadoPTR/Glow の板で出している) と霧を後処理で近似する
from PIL import ImageFilter, ImageChops
bright = img.point(lambda v: 0 if v < 190 else int((v - 190) * 3.2))
halo = bright.filter(ImageFilter.GaussianBlur(img.width / 55))
halo2 = bright.filter(ImageFilter.GaussianBlur(img.width / 16))
img = ImageChops.screen(img, halo.point(lambda v: int(v * 0.8)))
img = ImageChops.screen(img, halo2.point(lambda v: int(v * 0.55)))
img.save(out_png)
print("SAVED", out_png, "block", block)
