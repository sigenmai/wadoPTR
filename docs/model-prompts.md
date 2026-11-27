# 3Dモデル生成プロンプト集

Tripo3D（または Meshy などの生成サービス）の **Text to 3D** に、各モデルの「プロンプト」をそのまま貼り付けて使う。
英語のほうが生成の精度が高いので、プロンプトは英語にしている。

## 共通ルール

- **プロンプトの最後に、毎回この1文を付ける**（全モデルの見た目をそろえるため）
  ```
  stylized toon game asset, hand-painted look, bold simple shapes, flat warm colors, clean silhouette, wild west desert theme, no text, no logo, no ground, no background
  ```
- **ネガティブプロンプト**（入力欄があれば）
  ```
  realistic, photorealistic, text, logo, watermark, ground plane, base, pedestal, background
  ```
- **ポリゴン数（Face limit）**：表の値を目安にする。PC 用なので多少オーバーしても問題ない
- **書き出し形式**：**FBX**（骨・アニメーション付きのもの）または GLB（それ以外）
- **ファイル名**：表の「ファイル名」のとおりにして、`Assets/HorseRace/Models/` に入れる。Build World ツールはこの名前で自動的に差し替える

---

## Phase 1

### ★★★ 馬（一番大事）

| 項目 | 内容 |
|---|---|
| ファイル名 | `Horse.fbx` |
| Face limit | 15000 |
| プロンプト | `a stylized cartoon horse, plain light gray coat with no markings, dark mane and tail, no saddle, no rider, standing in a neutral T-pose-like stance with all four legs straight and slightly apart, head looking forward, symmetrical, friendly expressive eyes` |
| 生成後 | **Rig（骨入れ）→ Quadruped（四足）** を選ぶ。アニメーションは使えるものを全部付けて書き出す：`idle`, `walk`, `trot`, `run`/`gallop`, `jump`。無いアニメーションがあっても大丈夫（Unity 側で速度を変えて補う） |

- 毛色は Unity 側で色を変えて5〜6色にするので、**明るい灰色の無地** で作る
- 鞍は別のモデルにする（鞍だけ色を変えられるようにするため）
- 脚がくっついたり、5本になったりしたら作り直す

### ★★★ 鞍・手綱

| ファイル名 | Face limit | プロンプト |
|---|---|---|
| `Saddle.glb` | 4000 | `a western leather horse saddle with stirrups and a saddle horn, light tan leather with simple stitched trim, isolated object, viewed from the side` |
| `Reins.glb` | 2000 | `a simple horse bridle and leather reins, isolated object, laid out flat` |

### ★★ 町・ゲート・表彰台

| ファイル名 | Face limit | プロンプト |
|---|---|---|
| `Saloon.glb` | 12000 | `a two-story wooden wild west saloon building with a front porch, swinging doors, balcony and a blank wooden sign board` |
| `Stable.glb` | 10000 | `a wooden horse stable barn with open stalls, hay bales and a sloped roof` |
| `House.glb` | 8000 | `a small one-story wooden frontier house with a porch and a chimney` |
| `GeneralStore.glb` | 10000 | `a wooden wild west general store with a porch, barrels and crates out front, blank sign board` |
| `WaterTower.glb` | 6000 | `a tall wooden water tower on four legs with a ladder` |
| `StartGate.glb` | 6000 | `a wide wooden race start arch gate made of two thick log posts and a crossbeam with a blank banner, colorful pennant flags` |
| `GoalGate.glb` | 6000 | `a wide wooden race finish arch gate with a checkered blank banner, a bell hanging from the crossbeam, festive flags` |
| `Podium.glb` | 4000 | `a three-step wooden winners podium with steps of different heights, painted gold silver and bronze tops` |

### ★★ 自然物

| ファイル名 | Face limit | プロンプト |
|---|---|---|
| `RockA.glb` | 3000 | `a large reddish-brown desert boulder with layered sandstone stripes` |
| `RockB.glb` | 3000 | `a cluster of three jagged red canyon rocks` |
| `Mesa.glb` | 6000 | `a small flat-topped red sandstone mesa rock formation with layered stripes` |
| `Cactus.glb` | 2000 | `a tall saguaro cactus with two curved arms` |
| `DeadTree.glb` | 3000 | `a dry dead desert tree with twisted bare branches` |
| `RopeBridge.glb` | 6000 | `a long rope and wooden plank suspension bridge, straight, isolated` |

### ★★ 遺物（オリジナルデザイン）

| ファイル名 | Face limit | プロンプト |
|---|---|---|
| `Relic.glb` | 4000 | `a mysterious ancient relic: a round sun-shaped stone amulet carved with spiral patterns, a glowing turquoise gem in the center, wrapped with a faded red cloth strap, floating treasure item` |

### ★ 能力の小道具・ロビー

| ファイル名 | Face limit | プロンプト |
|---|---|---|
| `IronBall.glb` | 2000 | `a heavy dark iron ball with rivets and a few dents, slightly worn metal` |
| `RockWall.glb` | 5000 | `a wide jagged rock wall slab bursting out of the ground, cracked red sandstone, wider than tall` |
| `LassoCoil.glb` | 2000 | `a coiled cowboy lasso rope with a loop, isolated object` |
| `Scarecrow.glb` | 4000 | `a scarecrow practice target dummy on a wooden post with a straw body, a sack head with a painted bullseye target` |
| `Campfire.glb` | 3000 | `a campfire with stacked logs and a ring of stones, no flames` |
| `Bench.glb` | 2000 | `a rustic wooden bench made of split logs` |
| `MonitorFrame.glb` | 3000 | `a large wooden billboard frame on two posts with a blank flat rectangular panel in the center, wild west style` |

---

## Phase 2（今は作らなくてOK）

| ファイル名 | Face limit | プロンプト | 生成後 |
|---|---|---|---|
| `Raptor.fbx` | 15000 | `a stylized cartoon small raptor dinosaur, sandy orange scales with dark stripes, standing on two legs, long tail, neutral pose, mount-sized, friendly but fierce` | Rig → **Biped または該当する骨格**、`idle`, `run`, `roar`（あれば） |
| `Townsperson.fbx` | 10000 | `a stylized cartoon wild west townsperson, simple shirt, vest and hat, standing in a T-pose` | Rig → **Biped**、`idle`, `walk`, `wave`, `cheer` |
| `Bomb.glb` | 2000 | `a round black cartoon bomb with a short fuse and a metal cap` | — |
