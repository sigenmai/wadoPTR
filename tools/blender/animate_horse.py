"""Tripo の馬モデル（四足リグ入り FBX）に歩様アニメーションを付けて書き出す。

使い方:
  blender -b --python tools/blender/animate_horse.py -- <入力.fbx> <出力.fbx>

歩様の作り方（参考文献は docs/horse-animation-reference.md）:
  - 各脚を「接地（stance）」と「遊脚（swing）」に分け、接地割合 = duty factor を歩様ごとに設定
      常歩 0.62 / 速歩 0.42 / 駆歩 0.32 / 襲歩 0.22
  - 着地順（右手前）:
      常歩: 左後→左前→右後→右前（4拍、等間隔）
      速歩: 左前+右後 → 右前+左後（2拍、斜対）
      駆歩: 左後 → 右後+左前 → 右前（3拍）→ 空中
      襲歩: 左後 → 右後 → 左前 → 右前（4拍）→ 空中（脚を集めた姿勢）
  - 接地中: 脚は前から後ろへ一定速度で動き（蹄が地面に止まって見える）、球節は荷重で沈む
  - 遊脚中: 前半で関節が屈曲し中盤で最大、後半で伸びて次の着地に備える
  - 胴の上下・前後の傾き・首の振りは「どの脚に体重がかかっているか」から計算する
"""
import bpy, sys, math, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gait_lib
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_fbx = argv[0], argv[1]
FPS = 30
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = FPS
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

# ---------------------------------------------------------------- 骨の名前を整理
ren = {}
def chain(prefix, names):
    for i, n in enumerate(names):
        ren[n] = f"{prefix}_{i}"
chain("FrontL", [f"tripo::0_Left_Limb_{i}" for i in range(5)])
chain("FrontR", [f"tripo::0_Right_Limb_{i}" for i in range(5)])
chain("HindL", [f"bone_{i}" for i in range(21, 26)])
chain("HindR", [f"tripo::1_Right_Limb_{i}" for i in range(5)])
chain("Tail", ["bone_31", "tripo::Tail_0", "tripo::1_Left_Limb_0", "tripo::1_Left_Limb_1", "tripo::1_Left_Limb_2"])
chain("Spine", [f"tripo::Spine_{i}" for i in range(3)])
chain("Neck", ["tripo::Head_0", "tripo::Head_1", "tripo::Head_2"])
ren.update({"tripo::Head_3": "Head", "tripo::Head_4": "Jaw", "bone_8": "EarL", "bone_9": "EarR", "tripo::Root": "Root"})
for b in arm.data.bones:
    if b.name in ren:
        old = b.name
        b.name = ren[old]
        vg = mesh.vertex_groups.get(old)
        if vg:
            vg.name = ren[old]

# ---------------------------------------------------------------- 実寸（全長2.4m）・正面を -Y に
s = 2.4 / mesh.dimensions.x
arm.rotation_euler = (0, 0, math.radians(90))   # 元モデルの正面 -X → -Y
arm.scale = (s, s, s)
bpy.context.view_layer.update()
for o in (arm, mesh):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

gait_lib.run(arm)
gait_lib.export(arm, mesh, out_fbx, "Horse")
