# 荒野横断ホースレース（VRChat ワールド）

馬・ラクダ・骨の馬に乗って、荒野 → 砂漠 → 境目の町を駆け抜ける、最大 8 人の VRChat 用レースワールドです（PC VR ＋ デスクトップ対応、Quest 非対応）。大陸横断の競馬レースにヒントを得たオリジナルのゲームで、既存の作品やキャラクターとは関係ありません。

- 3 ステージを走り、合計ポイントで競う
- 手配書で選ぶ能力（砂塵・投げ縄・岩壁・鉄球）と、リボルバーによる妨害。遺物を拾うと能力が Lv アップ
- ロビー（たまり場）に観戦用の中継カメラと掲示板、練習場あり
- ワールドは Unity のメニュー「HorseRace/Build World」で自動生成されます

現在は **Phase 1**（レース本体まで）。恐竜・爆弾・蜃気楼・徒歩参加・観客などは Phase 2 の予定です。

> 注意：Unity でのコンパイルはまだ一度もしていません。最初に動かすときは [SETUP.md](docs/SETUP.md) の「未検証のポイント」を読んでください。

## まず読むもの
- **[HorseRace_Install.bat](HorseRace_Install.bat) — ワンボタンインストーラー（Windows）。ダブルクリックで最新版を Unity プロジェクトに入れ、ワールドまで自動で組み立てる**
- [docs/SETUP.md](docs/SETUP.md) — セットアップ手順（VCC → Unity → Build World → Build & Test → 非公開アップロード）
- [docs/PLAYTEST.md](docs/PLAYTEST.md) — プレイテストのチェックリスト（最後に設計レビューの重点 5 項目）

## 設計資料
- [設計書（spec）](docs/superpowers/specs/2026-11-26-vrc-horse-race-design.md)
- [実装計画（plan）](docs/superpowers/plans/2026-11-27-vrc-horse-race-phase1-v2.md)
- [3D モデル生成プロンプト](docs/model-prompts.md)
- [馬の歩様アニメーション資料](docs/horse-animation-reference.md)
- `docs/preview/` — Unity の外で作った見た目のプレビュー画像（`docs/preview/world/*.jpg` はワールド全体の完成イメージ）

## フォルダ構成
| パス | 内容 |
|---|---|
| `Assets/HorseRace/` | Unity プロジェクトにコピーするもの。`Scripts/`（UdonSharp のランタイムコード）、`Editor/`（Build World・Validate などのメニュー）、`Models/`（FBX・テクスチャ）、`Fonts/`（IPA ゴシック）、`Shaders/` |
| `tools/blender/` | モデルのリグ・アニメーション・スタイライズ用 Blender スクリプト（下の「Blender のスクリプト」） |
| `tools/preview/` | ワールドを Unity なしで書き出してプレビューするツール |
| `tests/HorseRace.Tests/` | 計算ロジック（順位・速度補正・遺物・地形など）の単体テスト（`dotnet test tests/HorseRace.Tests`） |
| `tests/UdonCompileCheck/`, `tests/EditorCompileCheck/` | Unity 無しで型検査するためのプロジェクト（手書きスタブに対して `dotnet build`） |
| `docs/` | 手順書・チェックリスト・設計・計画・参考資料 |

## Blender のスクリプト（`tools/blender/`、Blender 4.0 を `blender -b --python <script> -- <引数>` で実行）
| スクリプト | 内容 |
|---|---|
| `stylize_pack.py` | アセットパックを共通パレットに塗り替え、部品ごと（`<名前>_NN`、原点は底の中心）に書き出す |
| `stylize_scene.py` | 配置ごと使うシーン（町）を軽くして色をそろえる（マテリアルごとにまとめる） |
| `split_parts.py` | **つながって 1 つになったモデルを建物・小物ごとの別オブジェクトに分ける**（色・UV・テクスチャはそのまま、配置もそのまま、原点は各部品の底の中心、同じファイル名に上書き）。部品の一覧 `<名前>.parts.json`（名前・外形・重心・三角形数・種類）と、部品ごとに色を変えた確認画像（`--preview <dir>`）を出す |
| `uv_building.py` / `relink.py` / `recolor_texture.py` | 建物の UV 展開・テクスチャのつなぎ直し・パレットへの塗り替え |
| `rig_quadruped.py` / `animate_horse.py` / `animate_camel.py` | 四つ足のリグと歩様アニメーション |

`split_parts.py` で今のモデルを分けたときの引数（やり直すときは元の FBX に戻してから）:
```
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Packs/WesternTown/WesternTown.fbx --loner "Material #44"   # 町 → 137 個
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Buildings/RanchCompound.fbx --eps 0.4 --eps-z 0.4 --merge 2.5 --merge-dist 2.5 --no-rock   # 牧場 → 11 個
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Buildings/TownBlock.fbx --cut-x 0.15 --no-rock      # くっついた 2 棟 → 2 個
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Props/CactusGroup/CactusGroup.fbx --kind Cactus --contact verts --eps 0.05 --merge 0.2 --ground 2
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Props/AcaciaGrove/AcaciaGrove.fbx --kind Tree --contact verts --eps 0.1 --merge 1.5 --ground 2
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/ThirdParty/RockSetStylized/RockSetStylized.fbx --kind Rock --ground 2
blender -b --python tools/blender/split_parts.py -- Assets/HorseRace/Models/Packs/Gems/Gems.fbx --mode perpart --merge 0.01   # Gems_0N → 宝石 8 個（Gems_0N_01..08）の組
```
