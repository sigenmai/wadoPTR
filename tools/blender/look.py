"""モデルの中身（大きさ・三角形数・骨・テクスチャ）を表示し、斜めから見たプレビュー画像を書き出す。
blender -b --python tools/blender/look.py -- <モデル(.fbx/.blend)> <出力.png>
"""
import bpy, sys, os, math
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import relink
argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
if src.endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src)
    for o in list(bpy.data.objects):
        if o.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(o)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(src)[1].lower()
    if ext == ".obj":
        bpy.ops.wm.obj_import(filepath=src)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=src)
    else:
        bpy.ops.import_scene.fbx(filepath=src)
relink.fix_textures(src)
sc = bpy.context.scene
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3); tris = 0
for o in sc.objects:
    if o.type == "MESH" and o.visible_get():
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
        tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
print("INFO", os.path.basename(src), "objs", len([o for o in sc.objects if o.type == "MESH"]), "size", tuple(round(v, 2) for v in (mx - mn)),
      "tris", tris, "bones", sum(len(o.data.bones) for o in bpy.data.objects if o.type == "ARMATURE"))
ctr = (mn + mx) / 2; size = max(mx - mn)
sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "TEXTURE"; sc.display.shading.light = "STUDIO"
sc.render.resolution_x = 400; sc.render.resolution_y = 320
cd = bpy.data.cameras.new("C"); c = bpy.data.objects.new("C", cd); sc.collection.objects.link(c); sc.camera = c
cd.type = "ORTHO"; cd.ortho_scale = size * 1.3; cd.clip_end = size * 10
d = Vector((1.0, -1.3, 0.7)).normalized()
c.location = ctr + d * size * 3
c.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
sc.render.filepath = out; bpy.ops.render.render(write_still=True)
