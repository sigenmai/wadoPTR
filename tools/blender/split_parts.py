"""つながって 1 つのメッシュになっているモデル（町・建物・群れ）を、建物・小物ごとの別オブジェクトに分けて書き出す。

使い方:
  blender -b --python tools/blender/split_parts.py -- <入力 .fbx> [オプション]

  --mode whole      （既定）FBX の全メッシュを 1 つにまとめてから分ける。マテリアルごとに分かれていた
                    建物の部品（壁・屋根・窓）も 1 つの建物にまとまる。部品名は <名前>_NN_<種類>。
  --mode perpart    パックの部品（<名前>_NN）ごとに分ける。1 つの部品の中に複数の物があれば、
                    元の部品名の空オブジェクト（原点は底の中心）の子に <部品名>_NN として並べる
                    （GenUtil.InstantiatePart は部品名で探すので、元の名前のままグループごと取り出せる）。
  --out <.fbx>      書き出し先（既定：入力を上書き。テクスチャは入力の隣のものをそのまま使う）
  --name <名前>     部品名の頭（既定：ファイル名）
  --eps <m>         横にこの距離以内で外接箱が触れている島を同じ物とみなす（既定 0.03）
  --eps-z <m>       縦のすき間の許容（既定 0.25：給水塔のタンクと脚のように上に載っている物）
  --merge <m>       この大きさより小さい物は、いちばん近い物（--merge-dist 以内）にくっつける（既定 0.6）
  --merge-dist <m>  （既定 1.5）
  --ground <割合>   外形の面積がモデル全体の外形のこの割合以上の島は地面（Ground）として別にする（既定 0.25）
  --max-size <m>    1 つの物の横幅がこれを超えたら、触れ方の判定を厳しくして分け直す（既定 0：しない）
  --loner <名前,...> マテリアル名にこれを含む島（サボテンなど）は、同じ種類の島としかまとめない
                    （建物の壁ぎわに立つサボテンが建物にくっつかない）
  --cut-x <x>       x = <x> の面でメッシュを切り、その左右で別の建物にする（くっついて建っている 2 棟：TownBlock）。
                    左右それぞれの島はすべて 1 つにまとめる（Tripo の建物は壁の板がばらばらで、触れ方ではまとまらないため）
  --contact verts   外接箱ではなく「頂点どうしが eps 以内」で触れているかを見る（木の枝が重なり合う林、
                    くっついて生えたサボテンなど、外接箱が重なるが別の物のとき）
  --kind <種類>     種類の推定をせず、すべてこの種類にする（例 Rock・Cactus・Tree）
  --no-rock         「三角形が少ない大きな物＝岩」の推定をしない（Tripo の建物の部品）
  --manifest <.json> 部品の一覧（名前・外接箱・重心・三角形数・種類）。既定：<出力>.parts.json
  --preview <dir>   部品ごとに色を変えた確認画像（斜め・真上・分解図）を書き出す
  --dry-run         書き出さない（確認画像と一覧だけ）

やること:
  1. すべてのメッシュを 1 つにまとめる（--mode whole）→ つながっている面の島に分ける
  2. 島を「外接箱が eps 以内で触れている／重なっている」でまとめる（union-find）。
     地面のように大きく平らな島はまとめの対象外（全部の建物とつながってしまうため）で、それぞれ 1 つの物。
  3. 小さすぎる物（--merge 未満）はいちばん近い物にくっつける（窓枠・看板などの飾りが建物から外れない）
  4. 物ごとに 1 つのオブジェクトへ結合し、原点を底面の中心へ（ワールドの位置は変えない＝配置はそのまま）
  5. 位置で安定した順番に並べて名前を付け、マテリアル・UV・テクスチャはそのまま FBX に書き出す
     （axis_forward='-Z', axis_up='Y', bake_space_transform=True。色の塗り替えはしない）
"""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not argv:
    print(__doc__); sys.exit(1)
src = argv[0]


def opt(key, default=None, conv=str):
    if key in argv:
        return conv(argv[argv.index(key) + 1])
    return default


MODE = opt("--mode", "whole")
OUT = opt("--out", src)
NAME = opt("--name", os.path.splitext(os.path.basename(src))[0])
EPS = opt("--eps", 0.03, float)          # 横方向
EPS_Z = opt("--eps-z", 0.25, float)      # 縦方向（給水塔のタンクと脚の間など、上に載っている物のすき間）
MERGE = opt("--merge", 0.6, float)
MERGE_DIST = opt("--merge-dist", 1.5, float)
GROUND = opt("--ground", 0.25, float)
MAX_SIZE = opt("--max-size", 0.0, float)
CUT_X = opt("--cut-x", None, float)
NO_ROCK = "--no-rock" in argv
KIND = opt("--kind", None)
CONTACT = opt("--contact", "bbox")      # bbox | verts
LONER = [x for x in (opt("--loner", "") or "").split(",") if x]
MANIFEST = opt("--manifest", os.path.splitext(OUT)[0] + ".parts.json")
PREVIEW = opt("--preview", None)
DRY = "--dry-run" in argv

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for o in list(bpy.data.objects):
    if o.type not in ('MESH', 'EMPTY'):
        bpy.data.objects.remove(o, do_unlink=True)
# 親子・FBX の軸変換を確定（ワールドの形のまま、親なしのメッシュにする）
for o in [o for o in bpy.data.objects if o.type == 'MESH']:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in [o for o in bpy.data.objects if o.type == 'EMPTY']:
    bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()


def apply_transform(objs):
    with bpy.context.temp_override(selected_editable_objects=objs, active_object=objs[0], object=objs[0]):
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def world_co(o):
    n = len(o.data.vertices)
    co = np.empty(n * 3, dtype=np.float64)
    o.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    M = np.array(o.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def tris_of(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def separate_loose(o):
    """o をつながった島ごとのオブジェクトに分ける（o 自身も島の 1 つになる）。"""
    before = set(bpy.data.objects)
    for x in bpy.context.selected_objects:
        x.select_set(False)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='LOOSE')
    bpy.ops.object.mode_set(mode='OBJECT')
    return [o] + [x for x in bpy.data.objects if x not in before]


def join(objs):
    if len(objs) == 1:
        return objs[0]
    with bpy.context.temp_override(selected_editable_objects=objs, active_object=objs[0], object=objs[0]):
        bpy.ops.object.join()
    return objs[0]


class UF:
    def __init__(s, n): s.p = list(range(n))
    def f(s, a):
        while s.p[a] != a:
            s.p[a] = s.p[s.p[a]]; a = s.p[a]
        return a
    def u(s, a, b):
        a, b = s.f(a), s.f(b)
        if a != b: s.p[a] = b


def cluster(isl_objs):
    """島（オブジェクト）→ 物ごとのグループ（島の番号のリストのリスト）と、地面の島の番号。"""
    n = len(isl_objs)
    pts = [world_co(o) for o in isl_objs]
    mn = np.array([p.min(0) for p in pts]); mx = np.array([p.max(0) for p in pts])
    ext = mx - mn
    allmn, allmx = mn.min(0), mx.max(0)
    total_fp = max(1e-6, (allmx[0] - allmn[0]) * (allmx[1] - allmn[1]))
    fp = ext[:, 0] * ext[:, 1]
    # 地面・丘：外形がモデル全体の GROUND 以上、または 400m² 以上かつ全体の 3% 以上の島
    ground = [i for i in range(n) if n > 1 and (fp[i] >= GROUND * total_fp or (fp[i] >= 400.0 and fp[i] >= 0.03 * total_fp))]
    gset = set(ground)
    rest = [i for i in range(n) if i not in gset]

    def loner_of(o):
        used = set(p.material_index for p in o.data.polygons)
        names = [o.material_slots[k].material.name for k in used
                 if k < len(o.material_slots) and o.material_slots[k].material]
        for key in LONER:
            if names and all(key in nm for nm in names):
                return key
        return None
    lone = [loner_of(o) for o in isl_objs]

    def groups_for(idx, eps, eps_z):
        uf = UF(n)
        e = np.array([eps, eps, eps_z])
        if idx:
            a = np.array(idx)
            for i in idx:
                ov = np.all(mn[i] - e <= mx[a], axis=1) & np.all(mx[i] + e >= mn[a], axis=1)
                for j in a[ov]:
                    if lone[i] == lone[int(j)]:
                        uf.u(i, int(j))
        g = {}
        for i in idx:
            g.setdefault(uf.f(i), []).append(i)
        return list(g.values())

    def groups_by_verts(idx, eps):
        from mathutils.kdtree import KDTree
        uf = UF(n)
        total = sum(len(pts[i]) for i in idx)
        kd = KDTree(total)
        owner = []
        for i in idx:
            for p in pts[i]:
                kd.insert(p.tolist(), len(owner)); owner.append(i)
        kd.balance()
        for i in idx:
            for p in pts[i]:
                for (_, k, _) in kd.find_range(p.tolist(), eps):
                    j = owner[k]
                    if j != i and lone[i] == lone[j]:
                        uf.u(i, j)
        g = {}
        for i in idx:
            g.setdefault(uf.f(i), []).append(i)
        return list(g.values())

    groups = groups_by_verts(rest, EPS) if CONTACT == "verts" else groups_for(rest, EPS, EPS_Z)
    # 大きすぎる物：重なり（めり込み）だけでつなぎ直す（隣り合う建物が 1 つにならないように）
    if MAX_SIZE > 0:
        out = []
        for g in groups:
            gm, gx = mn[g].min(0), mx[g].max(0)
            if max(gx[0] - gm[0], gx[1] - gm[1]) > MAX_SIZE and len(g) > 1:
                out += groups_for(g, -0.05, -0.05)
            else:
                out.append(g)
        groups = out

    def gbox(g):
        return mn[g].min(0), mx[g].max(0)

    # 小さい物は近くの大きい物にくっつける（大きい物から順に、近い順）
    changed = True
    while changed:
        changed = False
        boxes = [gbox(g) for g in groups]
        size = [max(b[1] - b[0]) for b in boxes]
        order = sorted(range(len(groups)), key=lambda k: size[k])
        for k in order:
            if size[k] >= MERGE or len(groups) <= 1:
                continue
            best, bd = None, MERGE_DIST
            for j in range(len(groups)):
                if j == k or size[j] < size[k]:
                    continue
                d = np.maximum(0, np.maximum(boxes[j][0] - boxes[k][1], boxes[k][0] - boxes[j][1]))
                dist = float(np.linalg.norm(d))
                if dist <= bd:
                    best, bd = j, dist
            if best is not None:
                groups[best] = groups[best] + groups[k]
                groups.pop(k)
                changed = True
                break
    return groups, ground, mn, mx


def is_flat(ext):
    return ext[2] < 0.15 * max(ext[0], ext[1])


def guess_kind(o, mnv, mxv, is_ground, total_fp):
    """大まかな種類（名前の末尾と一覧に使う。配置には使わない）。"""
    if KIND and not is_ground:
        return KIND
    ext = mxv - mnv
    fp = ext[0] * ext[1]
    tris = tris_of(o)
    mats = " ".join((s.material.name if s.material else "").lower() for s in o.material_slots)
    if is_ground:
        return "Ground" if is_flat(ext) else "Hill"
    if any(k in mats for k in ("trunk", "leaf", "leaves", "foliage", "tree", "cact")):
        return "Tree"
    if any(k in mats for k in ("rock", "stone")):
        return "Rock"
    long_side, short_side = max(ext[0], ext[1]), max(1e-3, min(ext[0], ext[1]))
    if long_side > 12.0 and long_side / short_side > 8.0:
        return "Track" if ext[2] < 1.5 else "Fence"
    if not NO_ROCK and tris < 400 and max(ext) > 3.0:
        return "Rock"          # 大きいのに三角形が少ない＝ローポリの岩
    if ext[2] >= 3.0 and fp >= 15.0:
        return "Building"
    return "Prop"


def set_origin_bottom_centre(o):
    co = world_co(o)
    mnv, mxv = co.min(0), co.max(0)
    base = Vector((float(mnv[0] + mxv[0]) / 2, float(mnv[1] + mxv[1]) / 2, float(mnv[2])))
    mw_inv = o.matrix_world.inverted()
    local = mw_inv @ base
    o.data.transform(Matrix.Translation(-local))
    o.matrix_world = o.matrix_world @ Matrix.Translation(local)
    return mnv, mxv


def cut_at_x(o, x):
    """x = 一定の面でメッシュを切り、面の両側の面どうしをつながっていない状態にする（その後の島分けで左右に分かれる）。"""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(x, 0, 0), plane_no=(1, 0, 0))
    on = [e for e in bm.edges if all(abs(v.co.x - x) < 1e-4 for v in e.verts)]
    bmesh.ops.split_edges(bm, edges=on)
    bm.to_mesh(o.data)
    bm.free()


def cleanup_slots(o):
    used = set(p.material_index for p in o.data.polygons)
    with bpy.context.temp_override(selected_editable_objects=[o], active_object=o, object=o):
        if len(used) < len(o.material_slots):
            bpy.ops.object.material_slot_remove_unused()


def order_key(c):
    # 位置で安定した順番：南（-y）から北へ 10m の帯、帯の中は西（-x）から東へ
    return (round(float(c[1]) / 10.0), float(c[0]))


manifest = []
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
apply_transform(meshes)
before_counts = {"objects": len(meshes)}

if MODE == "whole":
    whole = join(meshes)
    whole.name = NAME
    if CUT_X is not None:
        cut_at_x(whole, CUT_X)
    islands = separate_loose(whole)
    before_counts["islands"] = len(islands)
    if CUT_X is not None:
        cx = [float(world_co(o).mean(0)[0]) for o in islands]
        groups = [[i for i in range(len(islands)) if cx[i] < CUT_X], [i for i in range(len(islands)) if cx[i] >= CUT_X]]
        groups = [g for g in groups if g]
        ground = []
        pts = [world_co(o) for o in islands]
        mn = np.array([p.min(0) for p in pts]); mx = np.array([p.max(0) for p in pts])
    else:
        groups, ground, mn, mx = cluster(islands)
    allmn, allmx = mn.min(0), mx.max(0)
    total_fp = (allmx[0] - allmn[0]) * (allmx[1] - allmn[1])
    parts = []
    flat = [i for i in ground if is_flat(mx[i] - mn[i])]
    if flat:
        parts.append((flat, True))          # 平らな地面の層はまとめて 1 つ
    for i in ground:
        if i not in flat:
            parts.append(([i], True))       # 丘はそれぞれ 1 つ
    for g in groups:
        parts.append((g, False))
    built = []
    for g, is_ground in parts:
        o = join([islands[i] for i in g])
        cleanup_slots(o)
        mnv, mxv = set_origin_bottom_centre(o)
        built.append((o, mnv, mxv, is_ground))
    built.sort(key=lambda t: (0 if t[3] else 1, order_key((t[1] + t[2]) / 2)))
    width = 2 if len(built) < 100 else 3
    for k, (o, mnv, mxv, is_ground) in enumerate(built):
        kind = guess_kind(o, mnv, mxv, is_ground, total_fp)
        o.name = f"{NAME}_{k + 1:0{width}d}_{kind}"
        o.data.name = o.name
        manifest.append({"name": o.name, "kind": kind, "tris": tris_of(o),
                         "bbox_min": [round(float(v), 3) for v in mnv], "bbox_max": [round(float(v), 3) for v in mxv],
                         "centroid": [round(float(v), 3) for v in world_co(o).mean(0)],
                         "materials": [s.material.name for s in o.material_slots if s.material]})
    out_objs = [t[0] for t in built]
else:
    out_objs = []
    before_counts["islands"] = 0
    for o in sorted(meshes, key=lambda x: x.name):
        pname = o.name
        islands = separate_loose(o)
        before_counts["islands"] += len(islands)
        groups, ground, mn, mx = cluster(islands)
        groups = [[i] for i in ground] + groups
        if len(groups) == 1:
            p = join(islands)
            p.name = pname; p.data.name = pname
            cleanup_slots(p)
            mnv, mxv = set_origin_bottom_centre(p)    # パックの部品の原点は底の中心（stylize_pack.py と同じ）
            out_objs.append(p)
            manifest.append({"name": pname, "kind": "Part", "tris": tris_of(p), "children": 0,
                             "bbox_min": [round(float(v), 3) for v in mnv], "bbox_max": [round(float(v), 3) for v in mxv]})
            continue
        # 元の部品の原点（底の中心）に空オブジェクトを置き、分けた物をその子にする
        subs = []
        for g in groups:
            s = join([islands[i] for i in g])
            cleanup_slots(s)
            mnv, mxv = set_origin_bottom_centre(s)
            subs.append((s, mnv, mxv))
        amn = np.min([t[1] for t in subs], axis=0); amx = np.max([t[2] for t in subs], axis=0)
        e = bpy.data.objects.new(pname, None)
        bpy.context.scene.collection.objects.link(e)
        e.location = Vector((float(amn[0] + amx[0]) / 2, float(amn[1] + amx[1]) / 2, float(amn[2])))
        bpy.context.view_layer.update()
        # 大きい順（1 番目がいちばん大きい物）
        subs.sort(key=lambda t: -float(np.prod(np.maximum(t[2] - t[1], 1e-3))))
        for k, (s, mnv, mxv) in enumerate(subs):
            s.name = f"{pname}_{k + 1:02d}"; s.data.name = s.name
            mw = s.matrix_world.copy()
            s.parent = e
            s.matrix_world = mw
        e.name = pname     # 島の 1 つが元の名前を持っていた（"Gems_01.001" になる）ので、子の名前を付け直した後で付ける
        assert e.name == pname, e.name
        out_objs.append(e)
        out_objs += [t[0] for t in subs]
        manifest.append({"name": pname, "kind": "Group", "children": len(subs),
                         "bbox_min": [round(float(v), 3) for v in amn], "bbox_max": [round(float(v), 3) for v in amx],
                         "parts": [{"name": t[0].name, "tris": tris_of(t[0]),
                                    "bbox_min": [round(float(v), 3) for v in t[1]],
                                    "bbox_max": [round(float(v), 3) for v in t[2]]} for t in subs]})

for o in out_objs:
    if o.type == 'MESH':
        for p in o.data.polygons:
            p.use_smooth = False

nparts = sum(1 for o in out_objs if o.type == 'MESH')
print("SPLIT", NAME, "mode", MODE, "objects", before_counts["objects"], "islands", before_counts["islands"], "-> parts", nparts)

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'OBJECT'
    sc.display.shading.show_object_outline = True
    import colorsys
    k = 0
    for o in out_objs:
        if o.type == 'MESH':
            # 隣どうしが似た色にならないよう、色相を黄金比ずつずらす
            r, g, b = colorsys.hsv_to_rgb((k * 0.618034) % 1.0, 0.65 if k % 2 else 0.9, 0.95 if k % 3 else 0.7)
            o.color = (r, g, b, 1)
            k += 1
    sc.render.resolution_x = 1600; sc.render.resolution_y = 1200
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.92, 0.92, 0.95)
    cd = bpy.data.cameras.new("PreviewCam"); cam = bpy.data.objects.new("PreviewCam", cd)
    sc.collection.objects.link(cam); sc.camera = cam
    cd.type = 'ORTHO'
    ms = [o for o in out_objs if o.type == 'MESH']

    def bounds():
        bpy.context.view_layer.update()
        pts = np.concatenate([world_co(o) for o in ms])
        return pts.min(0), pts.max(0)

    def shoot(tag, d):
        a, b = bounds(); c = (a + b) / 2; size = float(max(b - a))
        cd.clip_end = size * 20
        dv = Vector(d).normalized()
        if tag.startswith("top"):
            cd.ortho_scale = float(max(b[0] - a[0], (b[1] - a[1]) * 4 / 3)) * 1.05
        else:
            cd.ortho_scale = size * 1.4
        cam.location = Vector(c.tolist()) + dv * size * 4
        cam.rotation_euler = (-dv).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(PREVIEW, f"{NAME}_{tag}.png")
        bpy.ops.render.render(write_still=True)

    shoot("iso", (1, -1.2, 0.9))
    shoot("top", (0, -0.001, 1))
    # 分解図：部品を中心から外へ広げる（地面・丘は隠す）
    a, b = bounds(); c0 = (a + b) / 2
    saved = {o: o.matrix_world.copy() for o in ms}
    hidden = []
    for o in ms:
        if o.name.endswith("_Ground") or o.name.endswith("_Hill"):
            o.hide_render = True; hidden.append(o); continue
        co = world_co(o); c = (co.min(0) + co.max(0)) / 2
        dvec = c - c0; dvec[2] = 0
        o.location = o.location + Vector((dvec * 0.6).tolist())
    ms = [o for o in ms if o not in hidden]
    shoot("exploded_iso", (1, -1.2, 0.9))
    shoot("top_exploded", (0, -0.001, 1))
    ms = list(saved.keys())
    for o, m in saved.items():
        o.matrix_world = m; o.hide_render = False
    bpy.data.objects.remove(cam, do_unlink=True)

with open(MANIFEST if not DRY else os.path.join(PREVIEW or "/tmp", NAME + ".parts.json"), "w") as f:
    json.dump({"source": os.path.basename(src), "mode": MODE, "objects_before": before_counts["objects"],
               "islands": before_counts["islands"], "parts": nparts, "items": manifest}, f, indent=1, ensure_ascii=False)

if not DRY:
    bpy.ops.object.select_all(action='DESELECT')
    for o in out_objs:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             path_mode='COPY', embed_textures=False, mesh_smooth_type='FACE', bake_anim=False)
    print("WROTE", OUT, MANIFEST)
