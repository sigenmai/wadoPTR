#!/usr/bin/env python3
"""処理室のレイアウトを Assets/WadoPTR/Editor/Resources... ではなく Assets/WadoPTR/Editor/layout.json に書き出す。

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

boxes, props, lights, labels, anchors = [], [], [], [], []


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


def anchor(name, pos, yaw=0):
    anchors.append({"name": name, "pos": list(pos), "yaw": yaw})


# ------------------------------------------------------------------ 躯体
box("Floor", (0, -0.1, 0), (W, 0.2, D), "floor", tile=2.0)
box("Ceiling", (0, H + 0.1, 0), (W, 0.2, D), "ceiling", tile=3.0)
box("Wall_Back", (0, H / 2, Z1 + 0.1), (W + 0.4, H, 0.2), "tile_wall", tile=1.2)
box("Wall_Front", (0, H / 2, Z0 - 0.1), (W + 0.4, H, 0.2), "tile_wall", tile=1.2)
box("Wall_Right", (X1 + 0.1, H / 2, 0), (0.2, H, D), "tile_wall", tile=1.2)
# 左の壁は搬入口の分だけ開ける (開口: z 0.45..1.35, y 0.85..1.55)
box("Wall_Left_A", (X0 - 0.1, H / 2, (Z0 + 0.45) / 2), (0.2, H, 0.45 - Z0), "tile_wall", tile=1.2)
box("Wall_Left_B", (X0 - 0.1, H / 2, (1.35 + Z1) / 2), (0.2, H, Z1 - 1.35), "tile_wall", tile=1.2)
box("Wall_Left_Low", (X0 - 0.1, 0.425, TZ), (0.2, 0.85, 0.9), "tile_wall", tile=1.2)
box("Wall_Left_High", (X0 - 0.1, (1.55 + H) / 2, TZ), (0.2, H - 1.55, 0.9), "tile_wall", tile=1.2)
# 搬入口の奥(暗い通路)
box("Hatch_Tunnel_Floor", (X0 - 1.2, 0.84, TZ), (2.0, 0.06, 1.0), "black", collider=False)
box("Hatch_Tunnel_Top", (X0 - 1.2, 1.6, TZ), (2.0, 0.1, 1.0), "black", collider=False)
box("Hatch_Tunnel_L", (X0 - 1.2, 1.2, TZ - 0.5), (2.0, 0.9, 0.1), "black", collider=False)
box("Hatch_Tunnel_R", (X0 - 1.2, 1.2, TZ + 0.5), (2.0, 0.9, 0.1), "black", collider=False)
box("Hatch_Tunnel_End", (X0 - 2.2, 1.2, TZ), (0.1, 0.9, 1.0), "black", collider=False)
box("Hatch_Frame_T", (X0 + 0.02, 1.59, TZ), (0.08, 0.08, 1.06), "steel")
box("Hatch_Frame_L", (X0 + 0.02, 1.2, TZ - 0.49), (0.08, 0.86, 0.08), "steel")
box("Hatch_Frame_R", (X0 + 0.02, 1.2, TZ + 0.49), (0.08, 0.86, 0.08), "steel")
# 腰壁の汚れ止め(ステンレスの巾木)
for n, p, s in (("Kick_Back", (0, 0.15, Z1 - 0.01), (W, 0.3, 0.02)), ("Kick_Front", (0, 0.15, Z0 + 0.01), (W, 0.3, 0.02)),
                ("Kick_Right", (X1 - 0.01, 0.15, 0), (0.02, 0.3, D))):
    box(n, p, s, "steel", collider=False)

# ------------------------------------------------------------------ 処理台まわり
box("Table_Top", (0, TABLE_Y - 0.04, TZ), (2.7, 0.08, 1.0), "steel", group="Line")
for i, (x, z) in enumerate(((-1.25, TZ - 0.42), (1.25, TZ - 0.42), (-1.25, TZ + 0.42), (1.25, TZ + 0.42))):
    box(f"Table_Leg{i}", (x, (TABLE_Y - 0.08) / 2, z), (0.06, TABLE_Y - 0.08, 0.06), "steel", collider=False, group="Line")
box("Table_Gutter", (0, TABLE_Y - 0.1, TZ - 0.53), (2.7, 0.05, 0.06), "steel", collider=False, group="Line")
# 搬入レール (搬入口 → 処理台)
box("Rail_Bed", ((X0 + -1.35) / 2, TABLE_Y - 0.05, TZ), (-1.35 - X0, 0.06, 0.72), "steel", group="Line")
for i in range(8):
    x = X0 + 0.25 + i * 0.38
    box(f"Rail_Roller{i}", (x, TABLE_Y - 0.012, TZ), (0.05, 0.03, 0.7), "rubber", collider=False, group="Line")
for i, x in enumerate((-4.0, -2.9, -1.8)):
    box(f"Rail_Leg{i}a", (x, (TABLE_Y - 0.08) / 2, TZ - 0.3), (0.05, TABLE_Y - 0.08, 0.05), "steel", collider=False, group="Line")
    box(f"Rail_Leg{i}b", (x, (TABLE_Y - 0.08) / 2, TZ + 0.3), (0.05, TABLE_Y - 0.08, 0.05), "steel", collider=False, group="Line")
# 計量口 (右の壁際)
CHX = 3.75
box("Chute_Body", (CHX, 0.42, TZ), (1.1, 0.84, 1.1), "steel", group="Line")
box("Chute_Rim_F", (CHX, 0.9, TZ - 0.5), (1.1, 0.12, 0.1), "steel", group="Line")
box("Chute_Rim_B", (CHX, 0.9, TZ + 0.5), (1.1, 0.12, 0.1), "steel", group="Line")
box("Chute_Rim_L", (CHX - 0.5, 0.9, TZ), (0.1, 0.12, 0.9), "steel", group="Line")
box("Chute_Rim_R", (CHX + 0.5, 0.9, TZ), (0.1, 0.12, 0.9), "steel", group="Line")
box("Chute_Hole", (CHX, 0.845, TZ), (0.9, 0.01, 0.9), "black", collider=False, group="Line")
# 道具置き(刃物立て)
box("Stand_Top", (1.05, 0.83, -0.45), (0.55, 0.04, 0.4), "steel", group="Line")
box("Stand_Leg", (1.05, 0.405, -0.45), (0.08, 0.81, 0.08), "steel", collider=False, group="Line")
box("Stand_Foot", (1.05, 0.02, -0.45), (0.45, 0.04, 0.35), "steel", collider=False, group="Line")
box("Stand_Block", (1.05, 0.9, -0.45), (0.16, 0.1, 0.1), "rubber", collider=False, group="Line")
# 受け皿
box("Tray_Base", (3.75, 0.2, -0.45), (0.8, 0.4, 0.6), "steel", group="Line")
box("Tray_Rim_F", (3.75, 0.45, -0.73), (0.8, 0.1, 0.04), "steel", group="Line")
box("Tray_Rim_B", (3.75, 0.45, -0.17), (0.8, 0.1, 0.04), "steel", group="Line")
box("Tray_Rim_L", (3.37, 0.45, -0.45), (0.04, 0.1, 0.6), "steel", group="Line")
box("Tray_Rim_R", (4.13, 0.45, -0.45), (0.04, 0.1, 0.6), "steel", group="Line")
# 排水溝
box("Drain", (0, 0.002, TZ - 0.95), (5.0, 0.004, 0.18), "black", collider=False)
for i in range(12):
    box(f"Drain_Bar{i}", (-2.3 + i * 0.42, 0.006, TZ - 0.95), (0.03, 0.006, 0.18), "steel", collider=False)

# ------------------------------------------------------------------ 壁面の板(掲示・表示の下地)
# 奥の壁
box("Board_Money", (0, 2.62, Z1 - 0.03), (3.0, 0.78, 0.06), "led_black", collider=False, group="Boards")
box("Board_Manual", (0, 1.48, Z1 - 0.02), (2.5, 1.36, 0.02), "paper", collider=False, group="Boards")
box("Board_Market", (-3.05, 1.75, Z1 - 0.03), (2.1, 1.5, 0.06), "led_black", collider=False, group="Boards")
box("Board_Lines", (3.05, 1.75, Z1 - 0.03), (2.1, 1.5, 0.06), "led_black", collider=False, group="Boards")
# 右の壁: 計量表示・伝票
box("Board_Scale", (X1 - 0.03, 1.95, TZ), (0.06, 0.8, 1.3), "led_black", collider=False, group="Boards")
box("Board_Slip", (X1 - 0.03, 1.55, 2.45), (0.04, 1.2, 1.25), "paper", collider=False, group="Boards")
box("Slip_Printer", (X1 - 0.12, 2.22, 2.45), (0.24, 0.16, 1.3), "plastic_grey", collider=False, group="Boards")
# 左の壁: 搬入表示
box("Board_Hatch", (X0 + 0.03, 2.05, TZ), (0.06, 0.62, 1.5), "led_black", collider=False, group="Boards")
box("Panel_Call", (X0 + 0.03, 1.25, -0.2), (0.06, 0.7, 0.75), "plastic_grey", collider=False, group="Boards")
# 手前の壁: 発注端末・名簿・異動
box("Board_Terminal", (-2.3, 1.5, Z0 + 0.04), (2.9, 2.1, 0.08), "led_black", collider=False, group="Boards")
box("Board_Roster", (2.2, 1.9, Z0 + 0.02), (1.5, 1.1, 0.03), "paper", collider=False, group="Boards")
box("Board_Transfer", (3.75, 1.5, Z0 + 0.03), (1.3, 1.5, 0.06), "plastic_grey", collider=False, group="Boards")
# 退勤口(開かない)
box("Door", (0.55, 1.05, Z0 + 0.03), (1.0, 2.1, 0.06), "door", collider=False, group="Boards")
box("Door_Frame_T", (0.55, 2.14, Z0 + 0.04), (1.16, 0.08, 0.1), "steel", collider=False, group="Boards")
box("Door_Frame_L", (0.0, 1.07, Z0 + 0.04), (0.08, 2.14, 0.1), "steel", collider=False, group="Boards")
box("Door_Frame_R", (1.1, 1.07, Z0 + 0.04), (0.08, 2.14, 0.1), "steel", collider=False, group="Boards")
box("Door_Handle", (0.95, 1.05, Z0 + 0.09), (0.04, 0.16, 0.05), "steel", collider=False, group="Boards")

# ------------------------------------------------------------------ 什器 (Poly Haven, CC0)
prop("mounted_fluorescent_lights", (0, H - 0.03, TZ), yaw=0, scale=1.6, name="Tube_Table")
prop("mounted_fluorescent_lights", (-2.5, H - 0.03, -2.2), yaw=0, scale=1.6, name="Tube_Terminal")
prop("mounted_fluorescent_lights", (3.2, H - 0.03, -1.6), yaw=90, scale=1.6, name="Tube_Dock")
prop("rollershutter_door", (X1 - 0.16, 0, -1.9), yaw=90, scale=1.0, name="Shutter")
prop("metal_office_desk", (2.4, 0, -3.02), yaw=180, scale=0.78, collider=True)
prop("Television_01", (2.95, 0.615, -3.0), yaw=200, scale=0.9)
prop("office_notepads", (2.2, 0.617, -2.95), yaw=20, scale=0.45)
prop("clipboard", (1.85, 0.63, -2.9), yaw=75, pitch=90, scale=1.0)
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
prop("WetFloorSign_01", (1.9, 0, -0.9), yaw=200, scale=1.0)
prop("rubber_boots", (-0.45, 0, -3.15), yaw=175, scale=0.8)
prop("plastic_broom", (1.32, 0, -3.3), yaw=0, pitch=-8, scale=1.0)
prop("hand_truck", (3.95, 0, -2.75), yaw=-150, scale=1.0)
prop("korean_fire_extinguisher_01", (-0.2, 0, -3.25), yaw=180, scale=1.0)
prop("fire_alarm", (1.45, 2.3, Z0 + 0.02), yaw=180, scale=1.6)
prop("utility_box_01", (X0 + 0.22, 1.6, -3.0), yaw=90, scale=0.8)
prop("security_camera_01", (X1 - 0.3, H - 0.35, Z1 - 0.3), yaw=-135, scale=1.0)
prop("wall_clock", (2.2, 2.75, Z0 + 0.03), yaw=180, scale=1.1)
prop("barrel_03", (-4.0, 0, 2.95), yaw=0, scale=0.95, collider=True)
prop("barrel_03", (-3.3, 0, 3.0), yaw=50, scale=0.95, collider=True, name="barrel_03_b")
prop("old_bed_frame", (-2.0, 0, 2.7), yaw=90, scale=0.9, collider=True)
prop("wheelchair_01", (1.5, 0, 2.75), yaw=205, scale=1.0)
prop("water_manhole_cover", (-1.2, 0.0, -1.2), yaw=0, scale=0.7)
prop("exterior_aircon_unit", (X0 + 0.2, 2.55, 2.4), yaw=90, scale=0.6)
prop("rusted_hacksaw", (-4.0, 0.9, 2.95), yaw=35, pitch=90, scale=1.0)
prop("hatchet", (1.22, 0.865, -0.42), yaw=20, pitch=90, scale=1.0)
prop("fish_knife", (0.9, 0.855, -0.38), yaw=30, pitch=90, scale=1.0)
# 許可が下りると現れる保冷箱
prop("medical_box", (3.02, 0, 0.15), yaw=80, scale=1.0, group="OrganShelf")

# ------------------------------------------------------------------ 照明
light("Light_Table", (0, 2.75, TZ), (0.86, 0.95, 1.0), 1.55, 7.5)
light("Light_Terminal", (-2.5, 2.75, -2.2), (0.86, 0.95, 1.0), 1.1, 6.0)
light("Light_Dock", (3.2, 2.75, -1.6), (0.9, 0.95, 1.0), 0.9, 6.0)
light("Light_Hatch", (X0 + 0.5, 1.9, TZ), (1.0, 0.25, 0.15), 1.1, 3.0)

# ------------------------------------------------------------------ 掲示 (固定の文面)
INK = "#1B1B1B"
RED = "#B3261E"
label("Manual_Title", "作業標準書  第三版", (-1.14, 2.09, Z1 - 0.035), 0, 0.125, 2.3, color=INK)
label("Manual_Sub", "和堂PTR株式会社  第七加工センター  夜間処理室", (-1.14, 1.93, Z1 - 0.035), 0, 0.045, 2.3, color="#555555")
label("Manual_Body",
      "<b>一、呼出</b>　搬入口の緑のボタンで原体を呼ぶ。\n"
      "<b>二、分割</b>　刃物を取り、<color=#B3261E>切取線</color>に沿って入れる。\n"
      "　　　　　線は一本ずつ。体幹は最後に開く。\n"
      "<b>三、計量</b>　外れた部材を計量口へ運ぶ。\n"
      "　　　　　鮮度は時間とともに落ちる。\n"
      "<b>四、発注</b>　残高は端末で設備と人員に替える。",
      (-1.14, 1.82, Z1 - 0.035), 0, 0.082, 2.35, color=INK)
label("Manual_Foot", "手順についての質問は受け付けていません。", (-1.14, 0.94, Z1 - 0.035), 0, 0.05, 2.3, color=RED)
label("Money_Caption", "本日残高", (-1.38, 2.94, Z1 - 0.07), 0, 0.07, 1.2, font="dot", color="#7A8B8F")
label("Market_Title", "本日の買取相場", (-3.98, 2.40, Z1 - 0.07), 0, 0.09, 1.9, font="dot", color="#FFD27A")
label("Market_Foot", "基準比。九十秒ごとに改定。", (-3.98, 1.12, Z1 - 0.07), 0, 0.045, 1.9, font="dot", color="#7A8B8F")
label("Lines_Title", "稼働状況", (2.12, 2.40, Z1 - 0.07), 0, 0.09, 1.9, font="dot", color="#FFD27A")
label("Scale_Caption", "計量", (X1 - 0.07, 2.30, TZ + 0.58), 90, 0.06, 1.1, font="dot", color="#7A8B8F")
label("Chute_Sign", "計 量 口", (CHX - 0.56, 0.62, TZ + 0.3), 90, 0.11, 0.6, color="#DDDDDD")
label("Slip_Caption", "伝票", (X1 - 0.06, 2.08, 3.02), 90, 0.05, 0.6, font="dot", color=INK)
label("Hatch_Caption", "搬 入 口", (X0 + 0.07, 1.72, 0.5), -90, 0.09, 0.8, color="#DDDDDD")
label("Call_Label", "搬入呼出", (X0 + 0.07, 1.5, -0.4), -90, 0.055, 0.5, color=INK, align="center")
label("Sweep_Label", "床清掃", (X0 + 0.07, 1.5, 0.0), -90, 0.055, 0.5, color=INK, align="center")
label("Terminal_Title", "発注端末  WADO-PTR/7", (-0.95, 2.47, Z0 + 0.085), 180, 0.075, 3.0, font="dot", color="#FFD27A")
label("Roster_Title", "夜勤者名簿", (2.87, 2.38, Z0 + 0.04), 180, 0.075, 1.3, color=INK)
label("Transfer_Title", "異動願", (4.33, 2.17, Z0 + 0.065), 180, 0.08, 1.1, color=INK)
label("Door_Sign", "退勤口", (0.82, 2.33, Z0 + 0.1), 180, 0.07, 0.6, color="#7CFF9B", font="dot")
label("Door_Note", "本日の退勤予定者  0 名", (0.95, 1.62, Z0 + 0.065), 180, 0.035, 0.8, color=INK)
label("Shutter_Sign", "出 荷 口", (X1 - 0.34, 2.62, -2.55), 90, 0.11, 1.4, color="#DDDDDD")
label("Tray_Label", "受 け 皿", (3.42, 0.3, -0.755), 0, 0.07, 0.7, color="#DDDDDD")
# 衛生掲示
label("Hygiene",
      "<size=140%><b>衛生五則</b></size>\n一　手洗いは三十秒\n二　私語をしない\n三　原体に名前をつけない\n四　原体に話しかけない\n五　返事があっても作業を止めない",
      (X0 + 0.035, 2.33, -1.95), -90, 0.05, 1.1, color=INK)
box("Board_Hygiene", (X0 + 0.015, 1.95, -1.4), (0.02, 0.9, 1.25), "paper", collider=False, group="Boards")

# ------------------------------------------------------------------ 基準点 (スクリプトが使う位置)
anchor("Spawn", (0, 0, -2.2), 0)
anchor("Table", (BODY_X, TABLE_Y, TZ))
anchor("Hatch", (X0 - 1.35, TABLE_Y, TZ))
anchor("HatchDoor", (X0 + 0.05, 1.2, TZ))
anchor("Chute", (CHX, 0.95, TZ))
anchor("Tray", (3.75, 0.5, -0.45))
anchor("BladeRest", (1.05, 0.95 + 0.2136, -0.45))
anchor("BeltFrom", (1.35, TABLE_Y, TZ))
anchor("Money", (-1.38, 2.84, Z1 - 0.07), 0)
anchor("Market", (-3.98, 2.24, Z1 - 0.07), 0)
anchor("Lines", (2.12, 2.22, Z1 - 0.07), 0)
anchor("Scale", (X1 - 0.07, 2.2, TZ + 0.58), 90)
anchor("Slip", (X1 - 0.06, 1.98, 3.02), 90)
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
anchor("Memo5", (1.42, 1.25, Z1 - 0.035), 0)
anchor("Memo6", (0.55, 1.25, Z0 + 0.065), 180)
anchor("Memo7", (X0 + 0.07, 1.38, 1.75), -90)

out = {
    "room": {"w": W, "d": D, "h": H, "tableY": TABLE_Y, "tableZ": TZ},
    "boxes": boxes, "props": props, "lights": lights, "labels": labels, "anchors": anchors,
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
print("boxes", len(boxes), "props", len(props), "labels", len(labels), "anchors", len(anchors))
