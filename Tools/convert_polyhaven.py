#!/usr/bin/env python3
"""fetch_polyhaven.py が落とした glTF を Unity 向け FBX + 縮小テクスチャへ変換する (bpy 使用)。

  python Tools/convert_polyhaven.py -- <cache_dir>

座標系メモ: FBX は -Z forward / Y up で書き出す。Unity 側では
  Unity(x, y, z) = Blender(-x, z, -y)
になる(左右反転は Unity の FBX インポータが行う)。
"""
import bpy, bmesh, json, os, sys
from mathutils import Vector
from PIL import Image
import numpy as np

cache = sys.argv[sys.argv.index("--") + 1]
spec = json.load(open(os.path.join(cache, "spec.json")))
OUT = spec["out"]
manifest = {"models": {}, "textures": {}}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def save_jpg(src, dst, px, quality=88):
    im = Image.open(src).convert("RGB")
    if max(im.size) > px:
        im = im.resize((px, px), Image.LANCZOS)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.save(dst, quality=quality, optimize=True)


def classify(path):
    b = os.path.basename(path).lower()
    if "_nor_" in b:
        return "nor"
    if "_arm_" in b:
        return "arm"
    if "_diff_" in b or "_col_" in b or "diffuse" in b:
        return "diff"
    return None


def convert_model(name, max_tris, diff_px, origin):
    reset()
    d = os.path.join(cache, "models", name)
    bpy.ops.import_scene.gltf(filepath=os.path.join(d, name + ".gltf"))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in bpy.context.scene.objects:
        o.select_set(o.type == "MESH")
    bpy.context.view_layer.objects.active = meshes[0]
    # 親子関係を焼き込んでから結合
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name

    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > max_tris:
        m = obj.modifiers.new("dec", "DECIMATE")
        m.ratio = max_tris / tris
        bpy.ops.object.modifier_apply(modifier=m.name)
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)

    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    mn = Vector([min(v[i] for v in vs) for i in range(3)])
    mx = Vector([max(v[i] for v in vs) for i in range(3)])
    if origin == "base":
        off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    elif origin == "center":
        off = (mn + mx) / 2
    else:
        off = Vector((0, 0, 0))
    for v in obj.data.vertices:
        v.co -= off
    obj.location = (0, 0, 0)
    mn -= off
    mx -= off

    mats = []
    out_dir = os.path.join(OUT, name)
    os.makedirs(out_dir, exist_ok=True)
    for slot in obj.material_slots:
        mat = slot.material
        entry = {"name": mat.name, "diff": None, "nor": None, "smoothness": 0.4, "metallic": 0.0}
        if mat.node_tree:
            for n in mat.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    p = bpy.path.abspath(n.image.filepath)
                    kind = classify(p)
                    if not kind or not os.path.exists(p):
                        continue
                    safe = mat.name.replace(" ", "_").replace(".", "_")
                    if kind == "diff":
                        fn = f"{safe}_diff.jpg"
                        save_jpg(p, os.path.join(out_dir, fn), diff_px)
                        entry["diff"] = fn
                    elif kind == "nor":
                        fn = f"{safe}_nor.jpg"
                        save_jpg(p, os.path.join(out_dir, fn), 512, 90)
                        entry["nor"] = fn
                    elif kind == "arm":
                        a = np.asarray(Image.open(p).convert("RGB").resize((64, 64)), dtype=np.float32) / 255.0
                        entry["smoothness"] = round(float(1.0 - a[:, :, 1].mean()), 3)
                        entry["metallic"] = round(float(a[:, :, 2].mean()), 3)
        mats.append(entry)

    for o in bpy.context.scene.objects:
        o.select_set(o == obj)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out_dir, name + ".fbx"), use_selection=True, object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        path_mode="STRIP", embed_textures=False, bake_anim=False, mesh_smooth_type="FACE")
    info = json.load(open(os.path.join(d, "info.json")))
    # Unity 座標系での寸法 (x, y, z) = Blender (x, z, y)
    manifest["models"][name] = {
        "title": info["name"], "authors": info["authors"], "tris": tris, "materials": mats,
        "min": [round(-mx.x, 4), round(mn.z, 4), round(-mx.y, 4)],
        "max": [round(-mn.x, 4), round(mx.z, 4), round(-mn.y, 4)],
    }
    print("OK", name, tris, [round(x, 3) for x in (mx - mn)], flush=True)


def convert_texture(name, px):
    d = os.path.join(cache, "textures", name)
    out_dir = os.path.join(OUT, "..", "Textures", name)
    entry = {"diff": None, "nor": None, "smoothness": 0.3}
    for short in ("diff", "nor", "rough"):
        p = os.path.join(d, f"{name}_{short}.jpg")
        if not os.path.exists(p):
            continue
        if short == "rough":
            a = np.asarray(Image.open(p).convert("L").resize((64, 64)), dtype=np.float32) / 255.0
            entry["smoothness"] = round(float(1.0 - a.mean()), 3)
        else:
            fn = f"{name}_{short}.jpg"
            save_jpg(p, os.path.join(out_dir, fn), px if short == "diff" else 512)
            entry[short] = fn
    info = json.load(open(os.path.join(d, "info.json")))
    entry["title"] = info["name"]
    entry["authors"] = info["authors"]
    manifest["textures"][name] = entry
    print("OK tex", name, flush=True)


for n, (mt, px, org) in spec["models"].items():
    try:
        convert_model(n, mt, px, org)
    except Exception as e:  # 1 件の失敗で全体を止めない
        print("FAIL", n, repr(e), flush=True)
for n, px in spec["textures"].items():
    convert_texture(n, px)

with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "polyhaven_manifest.json"), "w") as f:
    json.dump(manifest, f, indent=1, ensure_ascii=False)
