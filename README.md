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
| `tools/blender/` | モデルのリグ・アニメーション・スタイライズ用 Blender スクリプト |
| `tools/preview/` | ワールドを Unity なしで書き出してプレビューするツール |
| `tests/HorseRace.Tests/` | 計算ロジック（順位・速度補正・遺物・地形など）の単体テスト（`dotnet test tests/HorseRace.Tests`） |
| `tests/UdonCompileCheck/`, `tests/EditorCompileCheck/` | Unity 無しで型検査するためのプロジェクト（手書きスタブに対して `dotnet build`） |
| `docs/` | 手順書・チェックリスト・設計・計画・参考資料 |
