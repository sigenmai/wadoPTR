#!/usr/bin/env python3
"""Poly Haven (CC0) から什器モデルとテクスチャを取得し、Unity 向けに正規化する。

使い方:  python Tools/fetch_polyhaven.py <cache_dir>
  1) 公式 API (api.polyhaven.com) から glTF とテクスチャ(1k)を cache_dir に保存
  2) Blender(bpy) で結合・必要なら減面・原点を底面中央へ・FBX 書き出し
  3) テクスチャを縮小して Assets/WadoPTR/Models/<name>/ に配置
  4) Tools/polyhaven_manifest.json に作者・寸法・質感の平均値を記録
要: pip install bpy pillow numpy
"""
import json, os, sys, urllib.request, subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "WadoPTR", "Models")
UA = {"User-Agent": "wadoptr-asset-fetch/1.0"}

# name: (max_tris, diffuse_px, origin)   origin: "base"=底面中央 / "center"=中心 / "keep"=そのまま
MODELS = {
    "machete": (4000, 1024, "keep"),
    "hatchet": (5500, 512, "keep"),
    "rusted_hacksaw": (5000, 512, "keep"),
    "fish_knife": (3000, 512, "keep"),
    "metal_office_desk": (7000, 1024, "base"),
    "Television_01": (2000, 512, "base"),
    "steel_frame_shelves_01": (4500, 1024, "base"),
    "plastic_crate_02": (6000, 512, "base"),
    "cardboard_box_01": (3000, 512, "base"),
    "industrial_pastic_container": (5000, 512, "base"),
    "metal_trash_can": (5000, 512, "base"),
    "trashbag": (4500, 512, "base"),
    "mounted_fluorescent_lights": (5000, 512, "center"),
    "security_camera_01": (5000, 512, "center"),
    "clipboard": (3000, 512, "center"),
    "wall_clock": (3700, 512, "center"),
    "WetFloorSign_01": (300, 512, "base"),
    "bleach_bottle": (3000, 512, "base"),
    "rubber_boots": (6000, 512, "base"),
    "medical_box": (3800, 512, "base"),
    "hand_truck": (6000, 512, "base"),
    "korean_fire_extinguisher_01": (5000, 512, "base"),
    "fire_alarm": (2800, 512, "center"),
    "utility_box_01": (4500, 512, "center"),
    "metal_stool_01": (5000, 512, "base"),
    "plastic_broom": (5000, 512, "base"),
    "old_bed_frame": (9000, 1024, "base"),
    "barrel_03": (1500, 512, "base"),
    "water_manhole_cover": (3000, 512, "base"),
    "office_notepads": (700, 512, "base"),
    "exterior_aircon_unit": (7000, 1024, "center"),
    "wheelchair_01": (12000, 1024, "base"),
    "hanging_industrial_lamp": (5000, 512, "keep"),
    "industrial_wall_lamp": (4500, 512, "center"),
    "security_light": (4700, 512, "center"),
    "utility_box_02": (5000, 512, "center"),
    "propane_tank": (4000, 512, "base"),
    "old_tyre": (2900, 512, "base"),
    "boombox": (5000, 512, "base"),
    "wooden_crate_02": (5200, 512, "base"),
    "modular_industrial_pipes_01": (6000, 1024, "center"),
}
# 床・壁などのタイリング素材: name: px
TEXTURES = {
    "concrete_wall_006": 1024,
    "rusty_metal_sheet": 1024,
    "rusty_painted_metal": 1024,
    "concrete_floor_worn_001": 1024,
    "dirty_concrete": 1024,
    "painted_metal_shutter": 1024,
}


def get(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=180) as r, open(path, "wb") as f:
        f.write(r.read())


def api(path):
    req = urllib.request.Request("https://api.polyhaven.com/" + path, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def fetch_model(name, cache):
    d = os.path.join(cache, "models", name)
    files = api("files/" + name)
    g = files["gltf"]["1k"]["gltf"]
    get(g["url"], os.path.join(d, name + ".gltf"))
    for rel, inc in g["include"].items():
        get(inc["url"], os.path.join(d, rel))
    info = api("info/" + name)
    with open(os.path.join(d, "info.json"), "w") as f:
        json.dump({"name": info.get("name"), "authors": list(info.get("authors", {}).keys())}, f)
    return d


def fetch_texture(name, cache):
    d = os.path.join(cache, "textures", name)
    files = api("files/" + name)
    for key, short in (("Diffuse", "diff"), ("nor_gl", "nor"), ("Rough", "rough")):
        if key in files:
            get(files[key]["1k"]["jpg"]["url"], os.path.join(d, f"{name}_{short}.jpg"))
    info = api("info/" + name)
    with open(os.path.join(d, "info.json"), "w") as f:
        json.dump({"name": info.get("name"), "authors": list(info.get("authors", {}).keys())}, f)
    return d


if __name__ == "__main__":
    cache = os.path.abspath(sys.argv[1])
    for n in MODELS:
        print("fetch model", n, flush=True)
        fetch_model(n, cache)
    for n in TEXTURES:
        print("fetch texture", n, flush=True)
        fetch_texture(n, cache)
    spec = os.path.join(cache, "spec.json")
    with open(spec, "w") as f:
        json.dump({"models": MODELS, "textures": TEXTURES, "out": OUT}, f)
    here = os.path.dirname(os.path.abspath(__file__))
    subprocess.check_call([sys.executable, os.path.join(here, "convert_polyhaven.py"), "--", cache])
