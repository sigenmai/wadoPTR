# 使い方: blender -b --python tools/preview/desktop_hud.py -- r3|new|old <出力.png>（ui-r2-02/03・ui-r3-04 の確認。TMP の代わりに Blender の文字、紙は単色）
# デスクトップの目（60° 縦・16:9）から首の札・能力の札（r3：右下）・懐中時計（r3：右上）を描く（Unity の座標 → Blender：(x,y,z)→(x,z,y)、回転 X は符号が逆）
import bpy, math, sys
argv = sys.argv[sys.argv.index("--")+1:]
mode, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'MATERIAL'
sc.render.resolution_x, sc.render.resolution_y = 960, 540
w = bpy.data.worlds.new("w"); sc.world = w; w.color = (0.45, 0.55, 0.7)
def mat(name, c):
    m = bpy.data.materials.new(name); m.diffuse_color = (*c, 1); return m
paper, ink, red, ground = mat("p",(0.93,0.86,0.70)), mat("i",(0.23,0.15,0.08)), mat("r",(0.72,0.12,0.08)), mat("g",(0.6,0.45,0.3))
cam = bpy.data.cameras.new("c"); cam.sensor_fit='VERTICAL'; cam.angle_y = math.radians(60); cam.clip_start=0.01
co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
co.rotation_euler = (math.radians(90), 0, 0)   # Blender +Y = Unity の前
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 20, -1.6)); bpy.context.object.data.materials.append(ground)   # 地面（目の 1.6m 下）
def U(x, y, z): return (x, z, y)
def group(pos, pitch_unity, yaw_unity, scale):
    e = bpy.data.objects.new("g", None); sc.collection.objects.link(e)
    e.location = U(*pos)
    # Unity: 文字は -Z 側。Blender の文字（+Z に読める）を X +90° で -Y に向け、Unity の pitch は符号を逆に
    e.rotation_mode = 'ZXY'
    e.rotation_euler = (math.radians(90 - pitch_unity), 0, math.radians(-yaw_unity))
    e.scale = (scale, scale, scale)
    return e
def quad(parent, w, h, cx, cy, m, z=0.0):
    bpy.ops.mesh.primitive_plane_add(size=1, location=(0,0,0)); o = bpy.context.object
    o.scale = (w, h, 1); o.location = (cx, cy, z); o.parent = parent; o.data.materials.append(m)
def text(parent, s, size, cx, cy, m):
    cu = bpy.data.curves.new("t", 'FONT'); cu.body = s; cu.size = size; cu.align_x='CENTER'; cu.align_y='CENTER'
    o = bpy.data.objects.new("t", cu); sc.collection.objects.link(o); o.parent = parent; o.location = (cx, cy, 0.002); o.data.materials.append(m)
pouch = None
if mode == "r3":
    card = group((-0.40, -0.19, 0.55), 0, 0, 0.55)
    watch = group((0.40, 0.15, 0.55), -15, 0, 0.6)      # UiStyle.DesktopWatchOffset / Euler（ui-r3-04：右上、目へ向ける）
    pouch = group((0.40, -0.19, 0.55), 0, 0, 0.55)      # UiStyle.DesktopAbilityOffset（首の札の反対）
elif mode == "new":
    card = group((-0.40, -0.19, 0.55), 0, 0, 0.55)
    watch = group((0.30, -0.17, 0.5), 22, 0, 0.6)
else:
    card = group((-0.17, -0.12, 0.4), 0, 0, 1.0)
    watch = group((0.0, -0.2, 0.45), -20, 180, 1.0)
quad(card, 0.20, 0.13, 0, 0, paper); text(card, "3", 0.06, -0.045, 0.0, ink); text(card, "1:42", 0.025, 0.05, 0, ink)
text(card, "STAGE 1", 0.02, 0, 0.05, ink); text(card, "######---- GOAL", 0.016, 0, -0.05, ink)
for k in range(5): quad(card, 0.02, 0.02, (k-2)*0.03, -0.065-0.0325, red if k == 0 else mat("pip",(1,0.82,0.35)), 0.001)
quad(watch, 0.13, 0.13, 0, 0, paper)
for k in range(1, 13):
    a = math.radians(k*30); text(watch, str(k), 0.016, math.sin(a)*0.05, math.cos(a)*0.05, red if k >= 11 else ink)
text(watch, "0:42", 0.02, 0, -0.018, ink)
if pouch is not None:
    # 能力の札（UiStyle.POUCH_CARD_W × H = 0.07 × 0.10、上下の中心が原点）と Lv の宝石 3 つ（PouchGemLocal）・「E」
    quad(pouch, 0.07, 0.10, 0, 0, mat("wanted",(0.80,0.66,0.45)))
    text(pouch, "WANTED", 0.012, 0, 0.035, ink); text(pouch, "E", 0.03, 0, -0.015, mat("brass",(1,0.82,0.45)))
    for i in range(3): quad(pouch, 0.018, 0.018, -0.022 + i*0.022, -0.05-0.016, mat("gem",(1,0.82,0.35)) if i == 0 else mat("gemoff",(0.25,0.18,0.1)), 0.001)
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
