"""西部劇風の木製掲示板をローポリで作る（ロビーの順位表・中継モニター・エントリー一覧用）。

blender -b --python tools/blender/make_noticeboard.py -- <出力フォルダ>

部品:
  NoticeBoard_Frame  柱2本・板の木枠・小さな屋根（木の色）
  NoticeBoard_Panel  表示面（Unity で順位表の UI や中継映像の RenderTexture を貼る）。UV は 0〜1 で全面
  NoticeBoard_Paper  ピンで留めた紙（飾り）
大きさ: 幅 4m × 高さ 3.6m（表示面 3.2m × 1.8m、16:9）
"""
import bpy, bmesh, sys, os, math, random
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(3)

def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = 0.9
    return m

def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

WOOD_D, WOOD, WOOD_L = mat("Wood_Dark", srgb("6b4530")), mat("Wood", srgb("8c5f3f")), mat("Wood_Light", srgb("ad7d52"))
PAPER = mat("Paper", srgb("f4dfba"))
PANEL = mat("Panel", srgb("45372e"))

def box(name, size, loc, m, rot=(0, 0, 0), jitter=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if jitter:
        for v in o.data.vertices:                 # 手作り感：頂点を少しずらす
            v.co += Vector([random.uniform(-jitter, jitter) for _ in range(3)])
    o.data.materials.append(m)
    return o

frame = []
W, PH = 4.0, 1.8            # 全体の幅、表示面の高さ
bottom, top = 1.2, 1.2 + PH + 0.2
# 柱
for x in (-W / 2 + 0.15, W / 2 - 0.15):
    frame.append(box("post", (0.22, 0.22, top + 0.25), (x, 0, (top + 0.25) / 2), WOOD_D, jitter=0.01))
# 板の木枠（上下左右）
frame.append(box("rail_top", (W - 0.1, 0.16, 0.18), (0, 0.02, top - 0.05), WOOD, jitter=0.008))
frame.append(box("rail_bot", (W - 0.1, 0.16, 0.18), (0, 0.02, bottom - 0.05), WOOD, jitter=0.008))
# 背板（縦の板を並べる）
n = 9
for i in range(n):
    x = -1.6 + 3.2 * (i + 0.5) / n
    frame.append(box("plank", (3.2 / n - 0.02, 0.06, PH + 0.1), (x, 0.08, bottom + PH / 2 + 0.05), random.choice([WOOD, WOOD_L]), jitter=0.006))
# 屋根（2枚の板）
for s in (-1, 1):
    frame.append(box("roof", (W + 0.4, 0.55, 0.08), (0, s * 0.22, top + 0.42), WOOD_D, rot=(math.radians(-s * 28), 0, 0), jitter=0.01))
frame.append(box("ridge", (W + 0.5, 0.12, 0.12), (0, 0, top + 0.55), WOOD_D))
# 足元の筋交い
for s in (-1, 1):
    frame.append(box("brace", (0.12, 0.6, 0.12), (s * (W / 2 - 0.15), 0.25, 0.25), WOOD_D, rot=(math.radians(40), 0, 0)))

bpy.ops.object.select_all(action='DESELECT')
for o in frame:
    o.select_set(True)
bpy.context.view_layer.objects.active = frame[0]
bpy.ops.object.join()
fr = bpy.context.active_object
fr.name = "NoticeBoard_Frame"

# 表示面（板の手前に少し浮かせる）。UV は全面 0〜1
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 0.035, bottom + PH / 2 + 0.05), rotation=(math.radians(90), 0, 0))
panel = bpy.context.active_object
panel.name = "NoticeBoard_Panel"
panel.scale = (3.2, PH, 1)
bpy.ops.object.transform_apply(scale=True)
panel.data.materials.append(PANEL)

# 飾りの紙（パネルの外：柱の内側の上端に小さく）
papers = []
for (x, z, r) in ((-1.75, top + 0.12, 6), (1.72, top + 0.1, -8)):
    papers.append(box("paper", (0.28, 0.01, 0.2), (x, -0.05, z), PAPER, rot=(0, math.radians(r), 0)))
bpy.ops.object.select_all(action='DESELECT')
for o in papers:
    o.select_set(True)
bpy.context.view_layer.objects.active = papers[0]
bpy.ops.object.join()
bpy.context.active_object.name = "NoticeBoard_Paper"

for o in bpy.data.objects:
    for p in o.data.polygons:
        p.use_smooth = False
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "NoticeBoard.fbx"), use_selection=True, object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                         mesh_smooth_type='FACE')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "_NoticeBoard.blend"))
print("RESULT tris", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects))
