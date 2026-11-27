"""外部モデルのテクスチャのつながりを直す（Sketchfab 等から落としたモデル向け）。

- 作者の PC のパス（D:/... など）を指したままの画像を、同梱の textures フォルダから名前で探して付け直す
- 色テクスチャがマテリアルにつながっていない場合、マテリアル名＋albedo/basecolor/diffuse のファイルを探してつなぐ
- フォルダに色テクスチャが1枚しかなければ、それを全マテリアルの色として使う
"""
import bpy, os, re

COLOR_HINTS = ("albedo", "basecolor", "base_color", "diffuse", "_d.", "_col", "color", "palette", "pallete", "gradient")
NON_COLOR = ("normal", "_n.", "rough", "_r.", "metal", "_ao", "ao.", "height", "skybox", "alpha")


def _norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


def _files(dirs):
    out = []
    for d in dirs:
        if d and os.path.isdir(d):
            for f in os.listdir(d):
                if f.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp")):
                    out.append(os.path.join(d, f))
    return out


def _is_color(path):
    b = os.path.basename(path).lower()
    return not any(k in b for k in NON_COLOR)


def fix_textures(src, extra_dirs=()):
    base = os.path.dirname(os.path.abspath(src))
    dirs = list(extra_dirs) + [base, os.path.join(base, "..", "textures"), os.path.join(base, "textures"),
                               os.path.join(base, "..", "..", "textures")]
    files = _files(dirs)
    by_norm = {_norm(os.path.splitext(os.path.basename(f))[0].replace(".tga", "")): f for f in files}

    # 1. 壊れたパスを付け直す
    for img in bpy.data.images:
        if img.source != 'FILE' or img.packed_file:
            continue
        p = bpy.path.abspath(img.filepath).replace("\\", "/")
        if p and os.path.exists(p):
            continue
        stem = os.path.splitext(os.path.basename(p))[0]
        cand = by_norm.get(_norm(stem)) or by_norm.get(_norm(img.name.split(".")[0]))
        if cand:
            img.filepath = cand
            img.reload()
            print("RELINK", img.name, "->", os.path.basename(cand))
        else:
            print("MISSING", img.name, p)

    color_files = [f for f in files if _is_color(f)]
    # 2. 色テクスチャが無いマテリアルにつなぐ
    for m in bpy.data.materials:
        if not m.use_nodes:
            continue
        nt = m.node_tree
        bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            continue
        bc = bsdf.inputs["Base Color"]
        if bc.links:
            node = bc.links[0].from_node
            if node.type == 'TEX_IMAGE' and node.image and node.image.size[0] > 0:
                continue
        mn = _norm(m.name.split(".")[0])
        pick = None
        for f in color_files:
            fn = _norm(os.path.basename(f))
            if mn and mn in fn and any(h.replace(".", "") in fn for h in ("albedo", "basecolor", "diffuse", "color")):
                pick = f
                break
        if pick is None:
            for f in color_files:
                if mn and mn in _norm(os.path.basename(f)):
                    pick = f
                    break
        if pick is None and len(color_files) == 1:
            pick = color_files[0]
        if pick:
            t = nt.nodes.new('ShaderNodeTexImage')
            t.image = bpy.data.images.load(pick, check_existing=True)
            nt.links.new(t.outputs["Color"], bc)
            print("HOOK", m.name, "->", os.path.basename(pick))
