"""色のない建物 FBX を UV 展開し、実寸に拡大して書き出す。
blender -b --python tools/blender/uv_building.py -- <入力.fbx> <出力.fbx> <幅(m)> [チェッカー確認画像.png]
"""
import bpy, sys, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
src, out, width = argv[0], argv[1], float(argv[2])
check = argv[3] if len(argv) > 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
objs = [o for o in bpy.data.objects if o.type == 'MESH']
for o in objs:
    s = width / max(o.dimensions.x, o.dimensions.y)
    o.scale = (o.scale[0] * s, o.scale[1] * s, o.scale[2] * s)
bpy.context.view_layer.update()
for o in objs:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # 接地：底面を z=0、中心を原点に
    zs = [v.co.z for v in o.data.vertices]; xs = [v.co.x for v in o.data.vertices]; ys = [v.co.y for v in o.data.vertices]
    off = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    for v in o.data.vertices: v.co -= off
    if not o.data.uv_layers: o.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 2本目の UV（ライトマップ用、重なりなし）
    bpy.ops.mesh.uv_texture_add()
    o.data.uv_layers.active_index = 1
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.lightmap_pack(PREF_CONTEXT='ALL_FACES', PREF_MARGIN_DIV=0.3)
    bpy.ops.object.mode_set(mode='OBJECT')
    o.data.uv_layers.active_index = 0
    o.data.uv_layers[0].active_render = True
    o.data.uv_layers[1].name = "UVLightmap"
print("INFO", src.split('/')[-1], [tuple(round(d, 1) for d in o.dimensions) for o in objs], "uv", [[u.name for u in o.data.uv_layers] for o in objs])
if check:
    img = bpy.data.images.new("chk", 1024, 1024); img.generated_type = 'COLOR_GRID'
    sc = bpy.context.scene
    for o in objs:
        m = bpy.data.materials.new("chk"); m.use_nodes = True
        t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = img
        m.node_tree.links.new(t.outputs[0], m.node_tree.nodes['Principled BSDF'].inputs[0])
        o.data.materials.clear(); o.data.materials.append(m)
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 400; sc.render.resolution_y = 320
    cd = bpy.data.cameras.new("C"); c = bpy.data.objects.new("C", cd); sc.collection.objects.link(c); sc.camera = c
    cd.type = 'ORTHO'; cd.ortho_scale = width * 1.3
    d = Vector((1.0, -1.3, 0.7)).normalized(); c.location = Vector((0, 0, width * 0.25)) + d * width * 3
    c.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = check; bpy.ops.render.render(write_still=True)
    # 書き出しは元のマテリアル名に戻す（チェッカーは出力に含めない）
    for o in objs: o.data.materials.clear(); o.data.materials.append(bpy.data.materials.new(o.name + "_Mat"))
bpy.ops.export_scene.fbx(filepath=out, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, path_mode='COPY', embed_textures=True)
