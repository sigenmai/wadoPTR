# プレイテスト チェックリスト

Unity と VRChat クライアントで実際に確認する項目。自動テスト（`dotnet test` / `dotnet build tests/UdonCompileCheck`）では分からないものだけを書く。

## Task 6: 乗り物

### 事前準備（シーン構成）
- メニュー「HorseRace/Create Horse Materials」→「HorseRace/Build Mount Animator」を実行し、`Assets/HorseRace/Animations/Mount.controller` ができている
- 乗り物 1 頭の構成:
  - ルート: `MountController`、`VRCStation`（Player Mobility = Immobilize、Station Enter Player Location = SeatAnchor、Disable Station Exit は乗り手を割り当てた時点でコードが true にする（Jump で降車させないため。初期値はどちらでも可））、`Rigidbody`（Is Kinematic）、体の Collider
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
- [ ] ジャンプ（Space / ボタン）で降車しない（ロビー・カウントダウン中も）
- [ ] 何らかの理由で降車してもレース中はすぐ再着席する（保険）
- [ ] VR で手綱の左右差が 10cm 未満では曲がらない。スティックを倒している間はスティックが優先される
- [ ] ラクダは中速で側対歩、骨の馬も自分のクリップで動く（オーバーライドコントローラ）
- [ ] 低スタミナで耳が伏せる（Animator に上書きされない）
- [ ] 2 クライアントで相手の乗り物がガクつかない（0.1 秒遅延補間・最大 0.3 秒外挿）
- [ ] 座席のピッチ・上下動が平滑化されていて酔わない。被弾（ノックバック）で視点が勝手に回らない

## Task 7: チェックポイント

### セットアップ
- 各 CP は Is Trigger の Collider を持つ GameObject に `Checkpoint` を付ける（Collider は乗り物の体より広く、コースを横切る形に）
- `index`（スタートライン通過 = 0、最初の CP = 1、以降 +1）・`stageIndex`・`isGoal`（最後の CP だけ true）・`next`（次の CP、ゴールは空）を設定
- CP の Transform の向き（Y 回転）がリセット後の向きになる。コース進行方向に向ける
- `MountController` の `Current Stage` はレース管理側が設定する（既定 0）。`ResetProgress` でステージ開始時に初期化する

### 確認項目
- [ ] 逆走しても CP 番号が進まず、順位が上がらない
- [ ] CP を飛ばして先の CP に入っても進まない（順番通りにだけ進む）
- [ ] 崖下・地形の下に落ちたら 3 秒以内に最後に通った CP へ戻る（地形の下に抜けたときはほぼ即時）
- [ ] コースから 60m 以上離れて 3 秒たつと最後の CP へ戻る。戻ったときは速度 0
- [ ] ゴール通過でゴール時刻（`syncFinishAt`）が記録され、2 回目の通過では上書きされない
- [ ] 他のプレイヤーが CP に触れても自分の進行は変わらない
