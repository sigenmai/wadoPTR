# プレイテスト チェックリスト

Unity と VRChat クライアントで実際に確認する項目。自動テスト（`dotnet test` / `dotnet build tests/UdonCompileCheck`）では分からないものだけを書く。

## Task 6: 乗り物

### 事前準備（シーン構成）
- メニュー「HorseRace/Create Horse Materials」→「HorseRace/Build Mount Animator」を実行し、`Assets/HorseRace/Animations/Mount.controller` ができている
- 乗り物 1 頭の構成:
  - ルート: `MountController`、`VRCStation`（Player Mobility = Immobilize、Station Enter Player Location = SeatAnchor、Disable Station Exit はオフ）、`Rigidbody`（Is Kinematic）、体の Collider
  - 子 `SeatAnchor`（座席）
  - 子 `Visual`（`MountVisual`、Horse / Camel / SkeletonHorse のモデル。各 Animator に Mount.controller、毛色マテリアル 6 種を Bay, Chestnut, Black, Gray, Palomino, Dun の順に設定、息づかい AudioSource はループ）
  - 子 `ReinsVR`（`MountInputVR`、mount と seatAnchor を設定）
- `MountController` の Ground Mask に地形のレイヤー、Obstacle Mask に壁・建物のレイヤーを設定
- 砂地の範囲に Is Trigger の Collider ＋ `SandZone` を置く

### 確認項目
- [ ] Unity Console のエラーが 0（UdonSharp のコンパイルを含む）
- [ ] デスクトップで乗って走る・曲がる・跳ぶ（W/S で歩様を上げ下げ、A/D で旋回、Space でジャンプ）
- [ ] 疾走（Shift）でスタミナが減り、尽きると駆歩までしか出ない（息づかいが荒くなる・耳が伏せる）。休むと戻る
- [ ] 坂で速度が変わる（上りで遅く、下りで速く）
- [ ] 崖から落ちると着地で硬直する（高いほど長い、よろめくアニメ）
- [ ] 砂地で馬だけ遅くなる（ラクダは変わらない）
- [ ] ラクダは馬より曲がりにくく、上り坂に弱い。最高速も低い
- [ ] 骨の馬・毛色が他人からも同じに見える（2 クライアントで確認）
- [ ] VR の手綱操作：片手を引くとその側へ曲がる、両手を引くと減速、両手を振ると疾走、低スタミナで手が振動する
- [ ] VR で降車（スティックのボタン等）してもレース中はすぐ再着席する
- [ ] 2 クライアントで相手の乗り物がガクつかない（0.1 秒遅延補間・最大 0.3 秒外挿）
- [ ] 座席のピッチ・上下動が平滑化されていて酔わない。被弾（ノックバック）で視点が勝手に回らない
