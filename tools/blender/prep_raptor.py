"""ヴェロキラプトル（アニメーション付き FBX）を、能力「恐竜」の乗騎として使える形に整えて書き出す。

blender -b --python tools/blender/prep_raptor.py -- <Raptor_Animated_FBX.fbx> <テクスチャフォルダ> <出力フォルダ>

- 頭はもとから -Y 向き（馬・ラクダと同じ）。乗れる大きさにするため 1.3 倍（腰の高さ 約1.4m）
- 32 本のクリップから必要なものだけ残し、ゲーム側の名前に付け替える
    Idle   ← Idle2（98f）     Walk  ← Walk（その場）     Trot / Canter ← Run2（その場）
    Gallop ← Run1（その場）   Jump  ← Leap1（その場）    Stamp ← Idle1（32f）
    Stagger← Hit1（短いのけぞり。Hit3 は倒れ込むので不採用）   Roar  ← Roar1（咆哮）      Tackle← Tackle（体当たり）
  Blender の FBX 読み込みはクリップ名を失うため、FBX 内の並び（アルファベット順、
  2 フレームのポーズ 2 本を除く）から番号で対応付けている
- 体のテクスチャ（PatternA）を共通パレットに寄せる（recolor_texture.py）
"""
import bpy, sys, os, subprocess

argv = sys.argv[sys.argv.index("--") + 1:]
src, texdir, out = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__))
SCALE = 1.3

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

# FBX 内のクリップ順（Action_Pose / Standard_Pose を除く 32 本）
ORDER = ["Bite1", "Bite2", "Call1", "Death1", "Death2", "Drink", "EatPrey", "GoToSleep", "Hit1", "Hit2",
         "Hit3_RM", "Hit3", "Idle1", "Idle2", "Leap1_RM", "Leap1", "Leap2_RM", "Leap2", "Roar1", "Roar2",
         "Run1_RM", "Run1", "Run2_RM", "Run2", "Screech1", "SleepLoop", "SniffGround", "Tackle_RM", "Tackle",
         "WakeFromSleep", "Walk_RM", "Walk"]
base = "RaptorArmature|RaptorArmature|RaptorArmature|RaptorArmature"
src_actions = {}
for i, n in enumerate(ORDER):
    a = bpy.data.actions.get(base if i == 0 else f"{base}.{i:03d}")
    if a is None:
        raise SystemExit(f"clip {i} ({n}) not found")
    src_actions[n] = a

KEEP = {"Idle": "Idle2", "Walk": "Walk", "Trot": "Run2", "Canter": "Run2", "Gallop": "Run1", "Jump": "Leap1",
        "Stamp": "Idle1", "Stagger": "Hit1", "Roar": "Roar1", "Tackle": "Tackle"}
for game_name, clip in KEEP.items():
    a = src_actions[clip].copy()
    a.name = game_name
    a.use_fake_user = True
for a in list(bpy.data.actions):
    if a.name not in KEEP:
        bpy.data.actions.remove(a)
print("CLIPS", sorted((a.name, tuple(int(x) for x in a.frame_range)) for a in bpy.data.actions))

# 大きさ
arm.scale = [s * SCALE for s in arm.scale]
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

# テクスチャをパレットに寄せる
env = {k: v for k, v in os.environ.items() if not k.startswith("PYTHON")}
for m in bpy.data.materials:
    if not m.use_nodes:
        continue
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b is None:
        continue
    for sock in ("Metallic", "Roughness", "Normal"):
        for l in list(b.inputs[sock].links):
            m.node_tree.links.remove(l)
    b.inputs["Metallic"].default_value = 0.0
    bc = b.inputs["Base Color"]
    if bc.links and bc.links[0].from_node.type == 'TEX_IMAGE':
        img = bc.links[0].from_node.image
        name = os.path.basename(bpy.path.abspath(img.filepath)) or img.name
        srcp = os.path.join(texdir, name)
        if not os.path.exists(srcp):
            srcp = os.path.join(texdir, "Raptor_PatternA_Albedo.png" if "Body" in m.name else "Other_Albedo.png")
        dst = os.path.join(out, f"Raptor_{m.name}_Albedo.png")
        subprocess.run(["python3", os.path.join(HERE, "recolor_texture.py"), srcp, dst, "0.7", "6"], env=env)
        bc.links[0].from_node.image = bpy.data.images.load(dst)
    for n in list(m.node_tree.nodes):        # 使わない画像ノードを消す
        if n.type == 'TEX_IMAGE' and not any(l.from_node == n for l in m.node_tree.links):
            m.node_tree.nodes.remove(n)

arm.name = "Raptor"
mesh.name = "RaptorMesh"
arm.animation_data.action = None
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "_Raptor.blend"))
bpy.ops.export_scene.fbx(filepath=os.path.join(out, "Raptor.fbx"), object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         path_mode='COPY', embed_textures=False, axis_forward='-Z', axis_up='Y',
                         apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True)
print("RESULT dims", tuple(round(d, 2) for d in arm.dimensions), "tris", sum(len(p.vertices) - 2 for p in mesh.data.polygons))
