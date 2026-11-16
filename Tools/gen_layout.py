#!/usr/bin/env python3
"""処理室と外の路地のレイアウトを Assets/WadoPTR/Editor/layout.json に書き出す。

世界観: 巨大構造体の底、配線と配管に埋もれた路地裏の下請け解体屋。濡れた暗いコンクリートと錆、
桃色と赤のネオン、裸電球。出荷口は路地に向かって開いている(柵があり、外には出られない)。

このファイルが部屋の「唯一の設計図」。
  - Unity 側: Assets/WadoPTR/Editor/WadoWorldBuilder.cs が読んでシーンを組む
  - 確認用:   Tools/preview_layout.py が Blender で同じものを描画する
座標は Unity (x 右, y 上, z 奥, メートル)。yaw は度(上から見て時計回り)。
壁面パネルの向き: 奥の壁(+Z)=0 / 手前の壁(-Z)=180 / 右の壁(+X)=90 / 左の壁(-X)=-90
"""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BODY = json.load(open(os.path.join(ROOT, "Tools", "body_manifest.json")))

W, D, H = 9.0, 7.0, 3.2          # 部屋 幅(x) 奥行(z) 高さ
X0, X1, Z0, Z1 = -W / 2, W / 2, -D / 2, D / 2
TABLE_Y = 0.90                    # 処理台の天面
TZ = 0.9                          # 処理台・搬入レール・計量口が並ぶ線
BODY_X = -0.25                    # 台上の原体ルート

boxes, props, lights, labels, anchors, cables = [], [], [], [], [], []


def box(name, pos, size, mat, yaw=0, collider=True, tile=0.0, group="Room"):
    boxes.append({"name": name, "pos": list(pos), "size": list(size), "yaw": yaw, "mat": mat,
                  "collider": collider, "tile": tile, "group": group})


def prop(model, pos, yaw=0, scale=1.0, pitch=0, roll=0, collider=False, group="Props", name=None):
    props.append({"name": name or model, "model": model, "pos": list(pos), "yaw": yaw, "pitch": pitch, "roll": roll,
                  "scale": scale, "collider": collider, "group": group})


def light(name, pos, color, intensity, rng, kind="point"):
    lights.append({"name": name, "pos": list(pos), "color": list(color), "intensity": intensity, "range": rng, "kind": kind})


def label(name, text, pos, yaw, size, width, font="gothic", color="#111111", align="left", height=0.0,
          group="Signs", pitch=0, roll=0):
    labels.append({"name": name, "text": text, "pos": list(pos), "yaw": yaw, "pitch": pitch, "roll": roll, "size": size,
                   "width": width, "height": height, "font": font, "color": color, "align": align, "group": group})


def cable(a, b, sag=0.3, width=0.025):
    cables.append({"a": list(a), "b": list(b), "sag": sag, "width": width})


def neon_frame(name, center, w, h, wall, mat="neon_pink", t=0.035, group="Neon"):
    """壁の表示板を囲むネオン管。wall = back / front / right / left。center は板の表面の中心。"""
    cx, cy, cz = center
    if wall in ("back", "front"):
        box(name + "_T", (cx, cy + h / 2, cz), (w + t, t, t), mat, collider=False, group=group)
        box(name + "_B", (cx, cy - h / 2, cz), (w + t, t, t), mat, collider=False, group=group)
        box(name + "_L", (cx - w / 2, cy, cz), (t, h, t), mat, collider=False, group=group)
        box(name + "_R", (cx + w / 2, cy, cz), (t, h, t), mat, collider=False, group=group)
        box(name + "_Glow", (cx, cy, cz + (0.02 if wall == "front" else -0.02)), (w * 1.5, h * 1.6, 0.002), mat.replace("neon", "glow"), collider=False, group=group)
    else:
        box(name + "_T", (cx, cy + h / 2, cz), (t, t, w + t), mat, collider=False, group=group)
        box(name + "_B", (cx, cy - h / 2, cz), (t, t, w + t), mat, collider=False, group=group)
        box(name + "_L", (cx, cy, cz - w / 2), (t, h, t), mat, collider=False, group=group)
        box(name + "_R", (cx, cy, cz + w / 2), (t, h, t), mat, collider=False, group=group)
        box(name + "_Glow", (cx + (0.02 if wall == "left" else -0.02), cy, cz), (0.002, h * 1.6, w * 1.5), mat.replace("neon", "glow"), collider=False, group=group)


def anchor(name, pos, yaw=0):
    anchors.append({"name": name, "pos": list(pos), "yaw": yaw})


# ------------------------------------------------------------------ 躯体
WALL, FLOOR, CEIL = "wall", "floor", "ceiling"
DOCK_Z0, DOCK_Z1, DOCK_H = -3.3, -0.7, 2.8          # 出荷口 (右の壁の開口)
AX0, AX1 = X1 + 0.2, X1 + 4.8                         # 路地の幅 (x)
AZ0, AZ1 = -13.0, 13.0
AH = 16.0

box("Floor", (0, -0.1, 0), (W, 0.2, D), FLOOR, tile=3.0)
box("Ceiling", (0, H + 0.1, 0), (W, 0.2, D), CEIL, tile=3.0)
box("Wall_Back", (0, H / 2, Z1 + 0.1), (W + 0.4, H, 0.2), WALL, tile=2.6)
box("Wall_Front", (0, H / 2, Z0 - 0.1), (W + 0.4, H, 0.2), WALL, tile=2.6)
# 右の壁: 出荷口の分だけ開ける
box("Wall_Right_A", (X1 + 0.1, H / 2, (DOCK_Z1 + Z1) / 2), (0.2, H, Z1 - DOCK_Z1), WALL, tile=2.6)
box("Wall_Right_B", (X1 + 0.1, H / 2, (Z0 + DOCK_Z0) / 2), (0.2, H, DOCK_Z0 - Z0), WALL, tile=2.6)
box("Wall_Right_Top", (X1 + 0.1, (DOCK_H + H) / 2, (DOCK_Z0 + DOCK_Z1) / 2), (0.2, H - DOCK_H, DOCK_Z1 - DOCK_Z0), WALL, tile=2.6)
# 巻き上げたシャッターと柵
box("Shutter_Housing", (X1 - 0.1, DOCK_H + 0.11, (DOCK_Z0 + DOCK_Z1) / 2), (0.22, 0.22, DOCK_Z1 - DOCK_Z0 + 0.2), "rust_red", collider=False)
box("Shutter_Slab", (X1 + 0.02, DOCK_H - 0.07, (DOCK_Z0 + DOCK_Z1) / 2), (0.04, 0.14, DOCK_Z1 - DOCK_Z0), "door", collider=False, tile=1.3)
for n, z in (("Dock_Post_A", DOCK_Z0 + 0.06), ("Dock_Post_B", DOCK_Z1 - 0.06)):
    box(n, (X1 + 0.1, 0.55, z), (0.1, 1.1, 0.1), "hazard", collider=False)
box("Dock_Bar_Hi", (X1 + 0.1, 1.0, (DOCK_Z0 + DOCK_Z1) / 2), (0.05, 0.07, DOCK_Z1 - DOCK_Z0), "hazard", collider=False)
box("Dock_Bar_Lo", (X1 + 0.1, 0.5, (DOCK_Z0 + DOCK_Z1) / 2), (0.05, 0.07, DOCK_Z1 - DOCK_Z0), "hazard", collider=False)
box("Dock_Blocker", (X1 + 0.1, 1.4, (DOCK_Z0 + DOCK_Z1) / 2), (0.2, 2.8, DOCK_Z1 - DOCK_Z0), "invisible")
box("Dock_Ramp", (X1 + 0.1, 0.01, (DOCK_Z0 + DOCK_Z1) / 2), (0.24, 0.02, DOCK_Z1 - DOCK_Z0), "rust", collider=False)
# 左の壁は搬入口の分だけ開ける (開口: z 0.45..1.35, y 0.85..1.55)
box("Wall_Left_A", (X0 - 0.1, H / 2, (Z0 + 0.45) / 2), (0.2, H, 0.45 - Z0), WALL, tile=2.6)
box("Wall_Left_B", (X0 - 0.1, H / 2, (1.35 + Z1) / 2), (0.2, H, Z1 - 1.35), WALL, tile=2.6)
box("Wall_Left_Low", (X0 - 0.1, 0.425, TZ), (0.2, 0.85, 0.9), WALL, tile=2.6)
box("Wall_Left_High", (X0 - 0.1, (1.55 + H) / 2, TZ), (0.2, H - 1.55, 0.9), WALL, tile=2.6)
# 搬入口の奥(暗い通路)
box("Hatch_Tunnel_Floor", (X0 - 1.2, 0.84, TZ), (2.0, 0.06, 1.0), "black", collider=False)
box("Hatch_Tunnel_Top", (X0 - 1.2, 1.6, TZ), (2.0, 0.1, 1.0), "black", collider=False)
box("Hatch_Tunnel_L", (X0 - 1.2, 1.2, TZ - 0.5), (2.0, 0.9, 0.1), "black", collider=False)
box("Hatch_Tunnel_R", (X0 - 1.2, 1.2, TZ + 0.5), (2.0, 0.9, 0.1), "black", collider=False)
box("Hatch_Tunnel_End", (X0 - 2.2, 1.2, TZ), (0.1, 0.9, 1.0), "black", collider=False)
box("Hatch_Frame_T", (X0 + 0.02, 1.59, TZ), (0.08, 0.08, 1.06), "rust")
box("Hatch_Frame_L", (X0 + 0.02, 1.2, TZ - 0.49), (0.08, 0.86, 0.08), "rust")
box("Hatch_Frame_R", (X0 + 0.02, 1.2, TZ + 0.49), (0.08, 0.86, 0.08), "rust")
# 腰壁に打ち付けた錆びた鉄板
box("Kick_Back", (0, 0.45, Z1 - 0.012), (W, 0.9, 0.024), "rust", collider=False, tile=1.8)
box("Kick_Front_A", (-2.3, 0.2, Z0 + 0.012), (4.4, 0.4, 0.024), "rust", collider=False, tile=1.8)
box("Kick_Front_B", (3.0, 0.3, Z0 + 0.012), (3.0, 0.6, 0.024), "rust", collider=False, tile=1.8)
box("Kick_Left", (X0 + 0.012, 0.42, -1.7), (0.024, 0.84, 3.6), "rust", collider=False, tile=1.8)
# 天井を這う配管と梁
box("Beam_A", (0, H - 0.12, -1.3), (W, 0.24, 0.18), "rust_red", collider=False, tile=2.0)
box("Beam_B", (0, H - 0.12, 2.3), (W, 0.24, 0.18), "rust_red", collider=False, tile=2.0)
for i, (z, r) in enumerate(((3.25, 0.09), (3.05, 0.06), (-3.2, 0.08), (-0.2, 0.05))):
    box(f"Pipe_Run{i}", (0, H - 0.2 - r - i * 0.02, z), (W, r * 2, r * 2), "rust" if i % 2 else "black", collider=False, tile=1.5)
for i, x in enumerate((-3.8, -1.1, 2.4)):
    box(f"Pipe_Cross{i}", (x, H - 0.34, 0), (0.1, 0.1, D), "black", collider=False)
box("Pipe_Drop_A", (X0 + 0.12, 1.9, -0.75), (0.09, 2.6, 0.09), "rust", collider=False)
box("Pipe_Drop_B", (X1 - 0.12, 1.6, 3.2), (0.12, 3.2, 0.12), "black", collider=False)
box("Pipe_Drop_C", (-0.25, 1.6, Z0 + 0.1), (0.07, 3.2, 0.07), "rust", collider=False)

# ------------------------------------------------------------------ 外の路地 (出荷口の向こう。入れない)
AXC = (AX0 + AX1) / 2
box("Alley_Floor", (AXC, -0.1, 0), (AX1 - AX0, 0.2, AZ1 - AZ0), FLOOR, tile=3.0, collider=False, group="Alley")
box("Alley_Far_Wall", (AX1 + 0.1, AH / 2, 0), (0.2, AH, AZ1 - AZ0), WALL, tile=3.2, collider=False, group="Alley")
box("Alley_Near_Above", (X1 + 0.3, (H + AH) / 2 + 0.1, 0), (0.2, AH - H - 0.2, AZ1 - AZ0), WALL, tile=3.2, collider=False, group="Alley")
box("Alley_Near_N", (X1 + 0.3, H / 2, (Z1 + AZ1) / 2 + 0.1), (0.2, H + 0.4, AZ1 - Z1 - 0.2), WALL, tile=3.2, collider=False, group="Alley")
box("Alley_Near_S", (X1 + 0.3, H / 2, (Z0 + AZ0) / 2 - 0.1), (0.2, H + 0.4, Z0 - AZ0 - 0.2), WALL, tile=3.2, collider=False, group="Alley")
box("Alley_End_N", (AXC, AH / 2, AZ1 + 0.1), (AX1 - AX0 + 0.4, AH, 0.2), "black", collider=False, group="Alley")
box("Alley_End_S", (AXC, AH / 2, AZ0 - 0.1), (AX1 - AX0 + 0.4, AH, 0.2), "black", collider=False, group="Alley")
box("Alley_Sky", (AXC, AH + 0.1, 0), (AX1 - AX0 + 0.4, 0.2, AZ1 - AZ0), "black", collider=False, group="Alley")
# 奥に続く明かり
box("Alley_Passage_N", (AXC + 0.4, 1.7, AZ1 - 0.02), (2.6, 3.4, 0.02), "glowpanel_warm", collider=False, group="Alley")
box("Alley_Passage_S", (AXC - 0.3, 1.5, AZ0 + 0.02), (2.2, 3.0, 0.02), "glowpanel_pink", collider=False, group="Alley")
# 頭上を渡る通路と張り出し
box("Bridge_A", (AXC, 4.7, 2.2), (AX1 - AX0, 0.45, 2.4), "rust_red", collider=False, tile=2.0, group="Alley")
box("Bridge_A_Rail", (AXC, 5.35, 1.05), (AX1 - AX0, 0.06, 0.06), "black", collider=False, group="Alley")
box("Bridge_B", (AXC, 8.8, -4.6), (AX1 - AX0, 0.6, 3.2), WALL, collider=False, tile=3.0, group="Alley")
box("Bridge_C", (AXC, 12.2, 5.5), (AX1 - AX0, 0.5, 2.0), "rust", collider=False, tile=2.0, group="Alley")
box("Balcony_A", (AX1 - 0.5, 3.35, -5.2), (1.0, 0.14, 3.6), "rust_red", collider=False, tile=1.5, group="Alley")
box("Balcony_A_Rail", (AX1 - 0.98, 3.9, -5.2), (0.04, 0.05, 3.6), "black", collider=False, group="Alley")
box("Balcony_B", (AX1 - 0.4, 6.4, 0.6), (0.8, 0.12, 4.4), "rust", collider=False, tile=1.5, group="Alley")
box("Awning", (AX1 - 0.55, 2.3, -2.7), (1.1, 0.06, 2.8), "rust_red", collider=False, tile=1.5, group="Alley")
# 向かいの店
box("Shop_Window", (AX1 - 0.02, 1.3, -2.7), (0.02, 1.4, 2.4), "glowpanel_warm", collider=False, group="Alley")
box("Shop_Counter", (AX1 - 0.3, 0.3, -2.7), (0.5, 0.6, 2.4), "rust", collider=False, tile=1.5, group="Alley")
box("Shop_Mullion_A", (AX1 - 0.04, 1.3, -3.3), (0.04, 1.4, 0.06), "black", collider=False, group="Alley")
box("Shop_Mullion_B", (AX1 - 0.04, 1.3, -2.1), (0.04, 1.4, 0.06), "black", collider=False, group="Alley")
for i, y in enumerate((0.95, 1.3, 1.65)):
    box(f"Shop_Shelf{i}", (AX1 - 0.05, y, -2.7), (0.03, 0.03, 2.4), "black", collider=False, group="Alley")
for i, (z, h) in enumerate(((-3.7, 0.2), (-3.5, 0.26), (-3.25, 0.16), (-2.9, 0.24), (-2.65, 0.18), (-2.45, 0.28), (-1.95, 0.2), (-1.75, 0.14), (-1.6, 0.25))):
    box(f"Shop_Jar{i}", (AX1 - 0.06, (0.965, 1.315, 1.665)[i % 3] + h / 2, z), (0.04, h, 0.1 + 0.02 * (i % 3)), "black", collider=False, group="Alley")
# 自動販売機
box("Vending_Body", (AX1 - 0.4, 0.92, 0.9), (0.7, 1.84, 1.0), "plastic_red", collider=False, group="Alley")
box("Vending_Front", (AX1 - 0.76, 1.15, 0.9), (0.02, 1.1, 0.84), "glowpanel_white", collider=False, group="Alley")
box("Vending_Slot", (AX1 - 0.76, 0.35, 0.9), (0.02, 0.22, 0.6), "black", collider=False, group="Alley")
# 窓明かり
for i, (y, z, w, h, m) in enumerate(((5.6, -6.8, 0.9, 0.6, "glowpanel_warm"), (7.9, 3.4, 0.7, 0.9, "glowpanel_warm"), (10.6, -1.5, 1.2, 0.5, "glowpanel_cyan"),
                                      (4.0, 6.8, 0.6, 0.8, "glowpanel_warm"), (13.2, 7.5, 0.8, 0.5, "glowpanel_warm"), (9.9, -8.5, 0.5, 0.7, "glowpanel_pink"))):
    box(f"Window{i}", (AX1 - 0.01, y, z), (0.02, h, w), m, collider=False, group="Alley")
# 向かいの壁のネオン看板 (路地側から室内へ向く面)
box("Sign_A", (AX1 - 0.06, 2.75, -0.85), (0.1, 3.3, 0.8), "neonpanel_pink", collider=False, group="Alley")
box("Sign_A_Glow", (AX1 - 0.13, 2.75, -0.85), (0.002, 4.8, 2.3), "glow_pink", collider=False, group="Alley")
box("Sign_B", (AX1 - 0.06, 3.45, -2.75), (0.1, 1.5, 2.3), "neonpanel_red", collider=False, group="Alley")
box("Sign_B_Glow", (AX1 - 0.13, 3.45, -2.75), (0.002, 2.6, 3.8), "glow_red", collider=False, group="Alley")
box("Sign_C", (AX1 - 0.06, 3.0, 3.9), (0.1, 0.8, 1.9), "neonpanel_red", collider=False, group="Alley")
box("Sign_C_Glow", (AX1 - 0.13, 3.0, 3.9), (0.002, 1.5, 3.0), "glow_red", collider=False, group="Alley")
box("Sign_D", (AX1 - 0.06, 2.75, -4.45), (0.1, 0.55, 0.55), "neonpanel_cyan", collider=False, group="Alley")
box("Sign_D_Glow", (AX1 - 0.13, 2.75, -4.45), (0.002, 1.2, 1.2), "glow_cyan", collider=False, group="Alley")
box("Sign_F", (AX1 - 0.06, 6.3, -3.4), (0.1, 1.8, 1.5), "neonpanel_pink", collider=False, group="Alley")
box("Sign_F_Glow", (AX1 - 0.13, 6.3, -3.4), (0.002, 3.0, 2.8), "glow_pink", collider=False, group="Alley")
box("Sign_E", (AX1 - 0.06, 10.3, 2.0), (0.1, 2.6, 0.7), "neonpanel_pink", collider=False, group="Alley")
box("Sign_E_Glow", (AX1 - 0.13, 10.3, 2.0), (0.002, 3.8, 1.9), "glow_pink", collider=False, group="Alley")
SIGN = "#3A0612"      # 光る看板の上の文字は暗い色で抜く
label("Sign_A_Text", "精\n肉\n買\n取", (AX1 - 0.12, 4.25, -0.85), 90, 0.6, 0.8, color=SIGN, align="center", group="Alley")
label("Sign_B_Text", "部材 高価買取\n即日現金 秘密厳守\n内容物 相談可\n二十四時間 搬入可", (AX1 - 0.12, 4.08, -1.72), 90, 0.21, 2.2, color=SIGN, group="Alley")
label("Sign_C_Text", "質  金融  身元不問", (AX1 - 0.12, 3.2, 4.78), 90, 0.2, 1.8, color=SIGN, group="Alley")
label("Sign_D_Text", "薬", (AX1 - 0.12, 2.98, -4.45), 90, 0.4, 0.5, color="#06222B", align="center", group="Alley")
label("Sign_E_Text", "宿\n泊\n休\n憩", (AX1 - 0.12, 11.45, 2.0), 90, 0.5, 0.6, color=SIGN, align="center", group="Alley")
label("Vending_Text", "飲料", (AX1 - 0.78, 1.95, 1.2), 90, 0.16, 0.6, color="#FFFFFF", group="Alley")
prop("exterior_aircon_unit", (AX1 - 0.2, 4.2, 6.2), yaw=-90, scale=0.7, group="Alley", name="Alley_Aircon_A")
prop("exterior_aircon_unit", (AX1 - 0.2, 8.6, -7.0), yaw=-90, scale=0.7, group="Alley", name="Alley_Aircon_B")
prop("metal_trash_can", (AX1 - 0.7, 0, -4.6), yaw=90, scale=0.75, group="Alley", name="Alley_Cans")
prop("trashbag", (AX1 - 0.7, 0, 2.2), yaw=40, scale=1.0, group="Alley", name="Alley_Bag_A")
prop("trashbag", (AX1 - 1.1, 0, 2.7), yaw=160, scale=0.9, group="Alley", name="Alley_Bag_B")
prop("barrel_03", (AX0 + 0.6, 0, 1.2), yaw=20, scale=0.95, group="Alley", name="Alley_Barrel")
prop("old_tyre", (AX0 + 0.5, 0, 2.2), yaw=75, roll=12, scale=1.0, group="Alley", name="Alley_Tyre")
prop("wooden_crate_02", (AX0 + 0.6, 0, -4.4), yaw=10, scale=1.0, group="Alley", name="Alley_Crate")
prop("propane_tank", (AX1 - 1.0, 0, -0.5), yaw=0, scale=1.0, group="Alley", name="Alley_Propane")
prop("modular_industrial_pipes_01", (AX1 - 0.2, 5.6, -1.9), yaw=-90, scale=1.6, group="Alley", name="Alley_Pipes")
prop("utility_box_02", (AX1 - 0.25, 1.6, 2.9), yaw=-90, scale=0.9, group="Alley", name="Alley_Box")
for i, (z0, y0, z1, y1, sag) in enumerate(((-6.0, 6.2, -2.0, 7.4, 0.9), (-1.0, 3.9, 4.0, 5.2, 0.7), (3.0, 9.5, -3.5, 10.2, 1.2), (-8.0, 4.4, 1.0, 3.6, 0.8),
                                           (6.5, 7.0, 0.5, 8.2, 1.0), (-4.0, 12.0, 5.0, 11.2, 1.4), (1.8, 2.9, -1.2, 3.3, 0.35), (8.5, 4.0, 3.5, 4.6, 0.6))):
    cable((AX0 + 0.12, y0, z0), (AX1 - 0.05, y1, z1), sag, 0.03 if i % 3 else 0.05)
for i, (y, z0, z1, sag) in enumerate(((5.8, -9.0, 9.0, 0.9), (3.5, -6.0, 6.0, 0.5), (9.0, -10.0, 4.0, 1.1))):
    cable((AX1 - 0.12, y, z0), (AX1 - 0.12, y + 0.3, z1), sag, 0.035)

# ------------------------------------------------------------------ 処理台まわり
box("Table_Top", (0, TABLE_Y - 0.04, TZ), (2.7, 0.08, 1.0), "steel", group="Line")
for i, (x, z) in enumerate(((-1.25, TZ - 0.42), (1.25, TZ - 0.42), (-1.25, TZ + 0.42), (1.25, TZ + 0.42))):
    box(f"Table_Leg{i}", (x, (TABLE_Y - 0.08) / 2, z), (0.06, TABLE_Y - 0.08, 0.06), "steel", collider=False, group="Line")
box("Table_Gutter", (0, TABLE_Y - 0.1, TZ - 0.53), (2.7, 0.05, 0.06), "rust", collider=False, group="Line")
# 搬入レール (搬入口 → 処理台)
box("Rail_Bed", ((X0 + -1.35) / 2, TABLE_Y - 0.05, TZ), (-1.35 - X0, 0.06, 0.72), "steel", group="Line")
for i in range(8):
    x = X0 + 0.25 + i * 0.38
    box(f"Rail_Roller{i}", (x, TABLE_Y - 0.012, TZ), (0.05, 0.03, 0.7), "rubber", collider=False, group="Line")
for i, x in enumerate((-4.0, -2.9, -1.8)):
    box(f"Rail_Leg{i}a", (x, (TABLE_Y - 0.08) / 2, TZ - 0.3), (0.05, TABLE_Y - 0.08, 0.05), "rust", collider=False, group="Line")
    box(f"Rail_Leg{i}b", (x, (TABLE_Y - 0.08) / 2, TZ + 0.3), (0.05, TABLE_Y - 0.08, 0.05), "rust", collider=False, group="Line")
# 計量口 (右の壁際)
CHX = 3.75
box("Chute_Body", (CHX, 0.42, TZ), (1.1, 0.84, 1.1), "rust", group="Line", tile=1.6)
box("Chute_Rim_F", (CHX, 0.9, TZ - 0.5), (1.1, 0.12, 0.1), "steel", group="Line")
box("Chute_Rim_B", (CHX, 0.9, TZ + 0.5), (1.1, 0.12, 0.1), "steel", group="Line")
box("Chute_Rim_L", (CHX - 0.5, 0.9, TZ), (0.1, 0.12, 0.9), "steel", group="Line")
box("Chute_Rim_R", (CHX + 0.5, 0.9, TZ), (0.1, 0.12, 0.9), "steel", group="Line")
box("Chute_Hole", (CHX, 0.845, TZ), (0.9, 0.01, 0.9), "black", collider=False, group="Line")
# 道具置き(刃物立て)
box("Stand_Top", (1.05, 0.83, -0.45), (0.55, 0.04, 0.4), "steel", group="Line")
box("Stand_Leg", (1.05, 0.405, -0.45), (0.08, 0.81, 0.08), "rust", collider=False, group="Line")
box("Stand_Foot", (1.05, 0.02, -0.45), (0.45, 0.04, 0.35), "rust", collider=False, group="Line")
box("Stand_Block", (1.05, 0.9, -0.45), (0.16, 0.1, 0.1), "rubber", collider=False, group="Line")
# 受け皿
box("Tray_Base", (3.75, 0.2, 2.35), (0.8, 0.4, 0.6), "rust", group="Line", tile=1.6)
box("Tray_Rim_F", (3.75, 0.45, 2.07), (0.8, 0.1, 0.04), "steel", group="Line")
box("Tray_Rim_B", (3.75, 0.45, 2.63), (0.8, 0.1, 0.04), "steel", group="Line")
box("Tray_Rim_L", (3.37, 0.45, 2.35), (0.04, 0.1, 0.6), "steel", group="Line")
box("Tray_Rim_R", (4.13, 0.45, 2.35), (0.04, 0.1, 0.6), "steel", group="Line")
# 排水溝
box("Drain", (0, 0.002, TZ - 0.95), (5.0, 0.004, 0.18), "black", collider=False)
for i in range(12):
    box(f"Drain_Bar{i}", (-2.3 + i * 0.42, 0.006, TZ - 0.95), (0.03, 0.006, 0.18), "rust", collider=False)

# ------------------------------------------------------------------ 壁面の板(掲示・表示の下地)とネオン
box("Board_Money", (0, 2.62, Z1 - 0.03), (3.0, 0.78, 0.06), "led_black", collider=False, group="Boards")
neon_frame("Neon_Money", (0, 2.62, Z1 - 0.075), 3.06, 0.84, "back", "neon_pink")
box("Board_Manual", (0, 1.48, Z1 - 0.04), (2.5, 1.36, 0.02), "paper", collider=False, group="Boards")
box("Board_Market", (-3.05, 1.75, Z1 - 0.045), (2.1, 1.5, 0.06), "led_black", collider=False, group="Boards")
neon_frame("Neon_Market", (-3.05, 1.75, Z1 - 0.09), 2.16, 1.56, "back", "neon_red")
box("Board_Lines", (3.05, 1.75, Z1 - 0.045), (2.1, 1.5, 0.06), "led_black", collider=False, group="Boards")
neon_frame("Neon_Lines", (3.05, 1.75, Z1 - 0.09), 2.16, 1.56, "back", "neon_cyan")
box("Board_Scale", (X1 - 0.03, 1.95, TZ), (0.06, 0.8, 1.3), "led_black", collider=False, group="Boards")
neon_frame("Neon_Scale", (X1 - 0.075, 1.95, TZ), 1.36, 0.86, "right", "neon_pink")
box("Board_Slip", (X1 - 0.03, 1.55, 2.45), (0.04, 1.2, 1.25), "paper", collider=False, group="Boards")
box("Slip_Printer", (X1 - 0.12, 2.22, 2.45), (0.24, 0.16, 1.3), "plastic_grey", collider=False, group="Boards")
box("Board_Hatch", (X0 + 0.03, 2.05, TZ), (0.06, 0.62, 1.5), "led_black", collider=False, group="Boards")
neon_frame("Neon_Hatch", (X0 + 0.075, 2.05, TZ), 1.56, 0.68, "left", "neon_red")
box("Panel_Call", (X0 + 0.03, 1.25, -0.2), (0.06, 0.7, 0.75), "rust", collider=False, group="Boards", tile=1.2)
box("Board_Terminal", (-2.3, 1.5, Z0 + 0.04), (2.9, 2.1, 0.08), "led_black", collider=False, group="Boards")
neon_frame("Neon_Terminal", (-2.3, 1.5, Z0 + 0.095), 2.96, 2.16, "front", "neon_pink")
box("Board_Roster", (2.2, 1.9, Z0 + 0.02), (1.5, 1.1, 0.03), "paper", collider=False, group="Boards")
box("Board_Transfer", (3.75, 1.5, Z0 + 0.03), (1.3, 1.5, 0.06), "paper", collider=False, group="Boards")
# 退勤口(開かない)
box("Door", (0.55, 1.05, Z0 + 0.03), (1.0, 2.1, 0.06), "door", collider=False, group="Boards", tile=1.4)
box("Door_Frame_T", (0.55, 2.14, Z0 + 0.04), (1.16, 0.08, 0.1), "rust", collider=False, group="Boards")
box("Door_Frame_L", (0.0, 1.07, Z0 + 0.04), (0.08, 2.14, 0.1), "rust", collider=False, group="Boards")
box("Door_Frame_R", (1.1, 1.07, Z0 + 0.04), (0.08, 2.14, 0.1), "rust", collider=False, group="Boards")
box("Door_Handle", (0.95, 1.05, Z0 + 0.09), (0.04, 0.16, 0.05), "steel", collider=False, group="Boards")
box("Door_Lamp", (0.55, 2.36, Z0 + 0.05), (0.75, 0.2, 0.06), "led_black", collider=False, group="Boards")
box("Board_Hygiene", (X0 + 0.015, 1.95, -1.4), (0.02, 0.9, 1.25), "paper", collider=False, group="Boards")

# ------------------------------------------------------------------ 什器 (Poly Haven, CC0)
prop("hanging_industrial_lamp", (0, H, TZ), scale=0.62, name="Lamp_Table")
box("Bulb_Table", (0, H - 0.78, TZ), (0.07, 0.09, 0.07), "bulb_warm", collider=False, group="Neon")
prop("hanging_industrial_lamp", (-2.3, H, -2.2), scale=0.62, name="Lamp_Terminal")
box("Bulb_Terminal", (-2.3, H - 0.78, -2.2), (0.07, 0.09, 0.07), "bulb_warm", collider=False, group="Neon")
prop("mounted_fluorescent_lights", (3.2, H - 0.03, -1.6), yaw=90, scale=1.6, name="Tube_Dock")
prop("industrial_wall_lamp", (X0 + 0.09, 2.6, -2.6), yaw=90, scale=1.2)
prop("security_light", (X1 - 0.25, 2.8, 3.0), yaw=-120, scale=1.0)
prop("metal_office_desk", (2.4, 0, -3.02), yaw=180, scale=0.78, collider=True)
prop("Television_01", (2.95, 0.615, -3.0), yaw=200, scale=0.9)
prop("boombox", (2.25, 0.617, -3.05), yaw=170, scale=0.8)
prop("office_notepads", (1.95, 0.617, -2.85), yaw=20, scale=0.35)
prop("clipboard", (2.7, 0.63, -2.75), yaw=75, pitch=90, scale=1.0)
prop("metal_stool_01", (2.3, 0, -2.3), yaw=30, scale=0.75)
prop("steel_frame_shelves_01", (3.6, 0, 3.05), yaw=0, scale=0.085, collider=True)
prop("plastic_crate_02", (3.25, 0.41, 3.0), yaw=8, scale=1.0)
prop("plastic_crate_02", (3.85, 0.41, 3.05), yaw=-5, scale=1.0, name="plastic_crate_02_b")
prop("cardboard_box_01", (3.3, 0.83, 3.0), yaw=14, scale=1.0)
prop("industrial_pastic_container", (3.9, 0.83, 3.0), yaw=0, scale=0.9)
prop("bleach_bottle", (3.55, 1.24, 3.0), yaw=40, scale=1.0)
prop("bleach_bottle", (3.7, 1.24, 3.05), yaw=-70, scale=1.0, name="bleach_bottle_b")
prop("metal_trash_can", (-4.0, 0, -2.6), yaw=90, scale=0.75)
prop("trashbag", (-3.95, 0, -1.75), yaw=30, scale=1.0)
prop("trashbag", (-3.6, 0, -1.4), yaw=200, scale=0.85, name="trashbag_b")
prop("trashbag", (-4.05, 0, -1.2), yaw=95, scale=0.9, name="trashbag_c")
prop("trashbag", (4.0, 0, -0.35), yaw=140, scale=0.9, name="trashbag_d")
prop("WetFloorSign_01", (1.9, 0, -0.9), yaw=200, scale=1.0)
prop("rubber_boots", (-0.45, 0, -3.15), yaw=175, scale=0.8)
prop("plastic_broom", (1.32, 0, -3.3), yaw=0, pitch=-8, scale=1.0)
prop("hand_truck", (3.7, 0, -2.6), yaw=-110, scale=1.0)
prop("wooden_crate_02", (3.9, 0, -1.35), yaw=80, scale=1.0)
prop("cardboard_box_01", (3.85, 0.465, -1.4), yaw=30, scale=0.9, name="cardboard_box_dock")
prop("propane_tank", (-0.85, 0, 3.2), yaw=0, scale=1.0)
prop("old_tyre", (-0.2, 0, 3.3), yaw=0, roll=14, scale=1.0)
prop("korean_fire_extinguisher_01", (-0.2, 0, -3.25), yaw=180, scale=1.0)
prop("fire_alarm", (1.45, 2.3, Z0 + 0.02), yaw=180, scale=1.6)
prop("utility_box_01", (X0 + 0.22, 1.6, -3.0), yaw=90, scale=0.8)
prop("utility_box_02", (X1 - 0.2, 1.7, -0.25), yaw=-90, scale=0.6)
prop("security_camera_01", (X1 - 0.3, H - 0.4, Z1 - 0.5), yaw=-135, scale=1.0)
prop("wall_clock", (2.2, 2.75, Z0 + 0.03), yaw=180, scale=1.1)
prop("barrel_03", (-4.0, 0, 2.95), yaw=0, scale=0.95, collider=True)
prop("barrel_03", (-3.3, 0, 3.0), yaw=50, scale=0.95, collider=True, name="barrel_03_b")
prop("old_bed_frame", (-2.0, 0, 2.7), yaw=90, scale=0.9, collider=True)
prop("wheelchair_01", (1.5, 0, 2.75), yaw=205, scale=1.0)
prop("water_manhole_cover", (-1.2, 0.0, -1.2), yaw=0, scale=0.7)
prop("exterior_aircon_unit", (X0 + 0.2, 2.55, 2.4), yaw=90, scale=0.6)
prop("modular_industrial_pipes_01", (-3.3, 2.2, Z0 + 0.2), yaw=180, scale=0.9, name="Pipes_Front")
prop("rusted_hacksaw", (-4.0, 0.9, 2.95), yaw=35, pitch=90, scale=1.0)
prop("hatchet", (1.22, 0.865, -0.42), yaw=20, pitch=90, scale=1.0)
prop("fish_knife", (0.9, 0.855, -0.38), yaw=30, pitch=90, scale=1.0)
# 許可が下りると現れる保冷箱
prop("medical_box", (3.02, 0, 0.15), yaw=80, scale=1.0, group="OrganShelf")
# 室内を這う配線
cable((-4.4, 3.0, -0.7), (-2.3, 3.05, -2.3), 0.35)
cable((-2.3, 3.05, -2.3), (0, 3.1, TZ), 0.5)
cable((0, 3.1, TZ), (3.2, 3.1, -1.6), 0.45)
cable((0, 3.1, TZ), (0, 3.02, Z1 - 0.1), 0.25)
cable((-3.05, 2.55, Z1 - 0.06), (-1.5, 2.62, Z1 - 0.06), 0.3, 0.02)
cable((1.5, 2.62, Z1 - 0.06), (2.0, 2.5, Z1 - 0.06), 0.15, 0.02)
cable((X0 + 0.08, 2.4, 0.2), (X0 + 0.08, 2.9, -2.6), 0.4, 0.02)
cable((-0.85, 2.58, Z0 + 0.1), (0.2, 2.42, Z0 + 0.1), 0.25, 0.02)
cable((3.2, 3.1, -1.6), (X1 + 0.3, 3.6, -2.0), 0.3)

# ------------------------------------------------------------------ 照明 (すべてリアルタイム・影なし)
light("Light_Table", (0, 2.25, TZ), (1.0, 0.8, 0.58), 1.8, 6.0)
light("Light_Neon_Back", (0, 2.5, 2.7), (1.0, 0.22, 0.48), 1.7, 7.0)
light("Light_Terminal", (-2.3, 1.9, -2.4), (1.0, 0.3, 0.45), 1.5, 5.5)
light("Light_Hatch", (X0 + 0.5, 1.9, TZ), (1.0, 0.16, 0.1), 1.3, 3.5)
light("Light_Dock", (3.2, 2.75, -1.6), (0.7, 0.92, 1.0), 0.9, 4.5)
light("Light_Alley", (AXC + 0.9, 3.0, -1.6), (1.0, 0.25, 0.5), 3.0, 12.0)
light("Light_Alley_Shop", (AX1 - 1.0, 1.4, -2.7), (1.0, 0.62, 0.3), 1.4, 5.0)

# ------------------------------------------------------------------ 掲示 (固定の文面)
INK = "#1B1B1B"
RED = "#B3261E"
PINK = "#FF5C93"
DIM = "#9A6578"
label("Manual_Title", "作業標準書  第三版", (-1.14, 2.09, Z1 - 0.055), 0, 0.125, 2.3, color=INK)
label("Manual_Sub", "和堂PTR  下請第七  夜間処理室", (-1.14, 1.93, Z1 - 0.055), 0, 0.045, 2.3, color="#4A4038")
label("Manual_Body",
      "<b>一、呼出</b>　搬入口の緑のボタンで原体を呼ぶ。\n"
      "<b>二、分割</b>　刃物を取り、<color=#B3261E>切取線</color>に沿って入れる。\n"
      "　　　　　線は一本ずつ。体幹は最後に開く。\n"
      "<b>三、計量</b>　外れた部材を計量口へ運ぶ。\n"
      "　　　　　鮮度は時間とともに落ちる。\n"
      "<b>四、発注</b>　残高は端末で設備と人員に替える。",
      (-1.14, 1.82, Z1 - 0.055), 0, 0.082, 2.35, color=INK)
label("Manual_Foot", "手順についての質問は受け付けていません。", (-1.14, 0.94, Z1 - 0.055), 0, 0.05, 2.3, color=RED)
label("Money_Caption", "本日残高", (-1.38, 2.94, Z1 - 0.07), 0, 0.07, 1.2, font="dot", color=DIM)
label("Market_Title", "本日の買取相場", (-3.98, 2.40, Z1 - 0.085), 0, 0.09, 1.9, font="dot", color=PINK)
label("Market_Foot", "基準比。九十秒ごとに改定。", (-3.98, 1.12, Z1 - 0.085), 0, 0.045, 1.9, font="dot", color=DIM)
label("Lines_Title", "稼働状況", (2.12, 2.40, Z1 - 0.085), 0, 0.09, 1.9, font="dot", color="#6FE3FF")
label("Scale_Caption", "計量", (X1 - 0.07, 2.30, TZ + 0.58), 90, 0.06, 1.1, font="dot", color=DIM)
label("Chute_Sign", "計 量 口", (CHX - 0.56, 0.62, TZ + 0.3), 90, 0.11, 0.6, color="#E8C9B0")
label("Slip_Caption", "伝票", (X1 - 0.06, 2.08, 3.02), 90, 0.05, 0.6, font="dot", color=INK)
label("Hatch_Caption", "搬 入 口", (X0 + 0.07, 1.72, 0.5), -90, 0.09, 0.8, color="#E8C9B0")
label("Call_Label", "搬入呼出", (X0 + 0.07, 1.5, -0.4), -90, 0.055, 0.5, color="#E8C9B0", align="center")
label("Sweep_Label", "床清掃", (X0 + 0.07, 1.5, 0.0), -90, 0.055, 0.5, color="#E8C9B0", align="center")
label("Terminal_Title", "発注端末  WADO-PTR/7", (-0.95, 2.47, Z0 + 0.085), 180, 0.075, 3.0, font="dot", color=PINK)
label("Roster_Title", "夜勤者名簿", (2.87, 2.38, Z0 + 0.04), 180, 0.075, 1.3, color=INK)
label("Transfer_Title", "異動願", (4.33, 2.17, Z0 + 0.065), 180, 0.08, 1.1, color=INK)
label("Door_Sign", "退勤口", (0.82, 2.43, Z0 + 0.085), 180, 0.085, 0.6, color="#6FE3FF", font="dot")
label("Door_Note", "本日の退勤予定者  0 名", (0.95, 1.62, Z0 + 0.065), 180, 0.035, 0.8, color="#E8C9B0")
label("Shutter_Sign", "出 荷 口", (X1 - 0.215, 2.97, -1.5), 90, 0.11, 1.4, color="#E8C9B0")
box("Dock_Plate", (X1 + 0.07, 1.04, -2.0), (0.02, 0.16, 1.9), "hazard", collider=False)
label("Dock_Warn", "部材以外の持ち出しを禁ず", (X1 + 0.055, 1.085, -1.12), 90, 0.07, 1.9, color="#1B1B1B")
label("Tray_Label", "受 け 皿", (3.42, 0.3, 2.045), 0, 0.07, 0.7, color="#E8C9B0")
label("Hygiene",
      "<size=140%><b>衛生五則</b></size>\n一　手洗いは三十秒\n二　私語をしない\n三　原体に名前をつけない\n四　原体に話しかけない\n五　返事があっても作業を止めない",
      (X0 + 0.035, 2.33, -1.95), -90, 0.05, 1.1, color=INK)

# ------------------------------------------------------------------ 基準点 (スクリプトが使う位置)
anchor("Spawn", (0, 0, -2.2), 0)
anchor("Table", (BODY_X, TABLE_Y, TZ))
anchor("Hatch", (X0 - 1.35, TABLE_Y, TZ))
anchor("HatchDoor", (X0 + 0.05, 1.2, TZ))
anchor("Chute", (CHX, 0.95, TZ))
anchor("Tray", (3.75, 0.5, 2.35))
anchor("BladeRest", (1.05, 0.95 + 0.2136, -0.45))
anchor("BeltFrom", (1.35, TABLE_Y, TZ))
anchor("Money", (-1.38, 2.84, Z1 - 0.07), 0)
anchor("Market", (-3.98, 2.24, Z1 - 0.07), 0)
anchor("Lines", (2.12, 2.22, Z1 - 0.07), 0)
anchor("Scale", (X1 - 0.07, 2.2, TZ + 0.58), 90)
anchor("Slip", (X1 - 0.055, 1.98, 3.02), 90)
anchor("HatchText", (X0 + 0.07, 2.28, 0.2), -90)
anchor("CallButton", (X0 + 0.07, 1.25, -0.4), -90)
anchor("SweepButton", (X0 + 0.07, 1.25, 0.0), -90)
anchor("Terminal", (-0.95, 2.32, Z0 + 0.085), 180)
anchor("Roster", (2.87, 2.24, Z0 + 0.04), 180)
anchor("Transfer", (4.33, 2.02, Z0 + 0.065), 180)
anchor("Schedule", (X0 + 0.03, 1.7, 2.45), -90)
# 前任者の走り書き (累計出荷額で順に現れる)
anchor("Memo0", (0.93, 0.852, -0.56), 12)
anchor("Memo1", (-0.72, 0.88, Z0 + 0.085), 180)
anchor("Memo2", (X0 + 0.07, 0.86, -0.2), -90)
anchor("Memo3", (2.2, 1.22, Z0 + 0.04), 180)
anchor("Memo4", (X1 - 0.07, 1.42, 1.25), 90)
anchor("Memo5", (1.42, 1.3, Z1 - 0.045), 0)
anchor("Memo6", (0.55, 1.25, Z0 + 0.065), 180)
anchor("Memo7", (X0 + 0.07, 1.38, 1.75), -90)

out = {
    "room": {"w": W, "d": D, "h": H, "tableY": TABLE_Y, "tableZ": TZ},
    "boxes": boxes, "props": props, "lights": lights, "labels": labels, "anchors": anchors, "cables": cables,
    "body": [{"name": n, "offset": v["offset"], "size": v["size"], "cut": v["cut"]} for n, v in BODY.items()],
}
dst = os.path.join(ROOT, "Assets", "WadoPTR", "Editor", "layout.json")
os.makedirs(os.path.dirname(dst), exist_ok=True)
with open(dst, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
# Unity 側が読む什器の材質表 (Poly Haven の変換結果から)
PH = json.load(open(os.path.join(ROOT, "Tools", "polyhaven_manifest.json"), encoding="utf-8"))
models = {"list": [{"key": k, "materials": v["materials"]} for k, v in PH["models"].items()]}
with open(os.path.join(ROOT, "Assets", "WadoPTR", "Editor", "models.json"), "w", encoding="utf-8") as f:
    json.dump(models, f, ensure_ascii=False, indent=1)
print("boxes", len(boxes), "props", len(props), "labels", len(labels), "anchors", len(anchors), "cables", len(cables))
