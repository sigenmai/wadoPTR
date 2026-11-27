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
- 乗り物の体の Collider は Ground Mask に含めない（自分の体を地面と誤認しない）
- 連続する CP を結ぶ直線がコースから 60m 以内に収まるように CP を置く（コース外判定は直前の CP から次の CP への線分からの距離）
- CP の Transform の向き（Y 回転）がリセット後の向きになる。コース進行方向に向ける
- `MountController` の `Current Stage` はレース管理側が設定する（既定 0）。`ResetProgress` でステージ開始時に初期化する

### 確認項目
- [ ] 逆走しても CP 番号が進まず、順位が上がらない
- [ ] CP を飛ばして先の CP に入っても進まない（順番通りにだけ進む）
- [ ] 崖下・地形の下に落ちたら 3 秒以内に最後に通った CP へ戻る（地形の下に抜けたときはほぼ即時）
- [ ] コースから 60m 以上離れて 3 秒たつと最後の CP へ戻る。戻ったときは速度 0
- [ ] ゴール通過でゴール時刻（`syncFinishAt`）が記録され、2 回目の通過では上書きされない
- [ ] 他のプレイヤーが CP に触れても自分の進行は変わらない
- [ ] 深い谷（50m 超）を跳び越している間は、2 秒未満ならリセットされない。地面のない空間へ落ち続けたら 3 秒以内に戻る
- [ ] ゴール後はコース外へ惰性で行ってもリセットされない
- [ ] レース外（`raceActive` が false）・操作不能中は CP に触れても進まない

## Task 8: レース進行・ロビー

### セットアップ（シーンに置くもの・つなぐもの）
- **前提**: VRChat SDK 3.8 以降・UdonSharp 1.2 以降（引数つきネットワークイベント `[NetworkCallable]` を使う）。TextMeshPro を使用（日本語フォントの TMP Font Asset、台帳は手書き風、黒板はチョーク風を用意）
- **RaceManager**（空の GameObject 1 つ、Synchronization = Manual）
  - `Mounts` に MountController 8 頭（slot 0〜7 の順。各 MountController の `Slot Index` も同じ番号に）
  - `Start Points` に 24 個（index = ステージ × 8 + slot。Transform の Y 回転がスタート時の向き）
  - `First Checkpoints` に各ステージの CP（index 1）を 3 つ
  - `Finish Pens` に各ステージのゴール後の囲い（柵で囲った広場）の中心を 3 つ
  - `Mount Parking`（任意）に 8 か所、`Lobby Spawn` にロビーの戻り位置
  - `Starter`（任意、Task 10）／`Stage Reset Targets`（Task 9 の能力・遺物："ResetForStage" を受ける）／`Refill Targets`（Task 9 のリボルバー："Refill" を受ける）
  - `Ledgers`・`Rank Boards`・`Poster Cards`・`Mount Pickers`・`Start Bell`・`Practice Area` を下記のオブジェクトにつなぐ
- **手配書の壁**: カード 6 枚に `PosterCard`（Collider 付き）。kind 0 = 乗り物（value 0 馬／1 ラクダ）、kind 1 = 能力（value 0 砂塵／1 投げ縄／2 岩壁／3 鉄球）。`Poster Visual` に紙、`Taken Visual` にピンだけ残った跡（任意）、`Label` に文字（任意）
- **柵の馬**: 7 頭（毛色 0〜5＋骨の馬 6）に `MountPicker`（Collider 付き、`Coat`、`Visual` に MountVisual、`Chosen Marker` に鞍・リボンなど）。レース用 8 頭とは別の見た目だけの馬
- **受付カウンターの台帳**: `EntryLedger`（Collider 付き）、`Page Text` に台帳ページの TextMeshPro、`Sign Sound`（任意）
- **スタート台の鐘**: `StartBell`（Collider 付き）、`Ring Sound`・`Tap Sound`・`Note Text`（任意）
- **練習場**: 練習場全体を覆う Is Trigger の Collider ＋ `PracticeArea`（`Release Radius` は練習場の外周より少し大きく）。練習用の馬 2 頭（レース用とは別の MountController 一式）をつなぐ杭それぞれに `PracticeHorsePost`（Collider 付き、`Mount`・`Home`・`Coat`・`Race Manager`）を置き、`PracticeArea.Posts` に登録。カカシは Task 9
- **黒板**: 各 CP の黒板に `RankBoard`（`Board Text`）。たまり場にも 1 枚置くと結果が見られる
- **懐中時計**: 常に有効な空の GameObject に `PocketWatch`、時計のモデルはその子にして `Watch Root` に。`Face Text` に文字盤の TMP、針は任意
- **補助表示**: ロビーの真鍮のレバー（Collider 付き）に `MinimalHud`。`Hud Root` はレバーの子にしない別オブジェクト（古い紙のワールド空間 Canvas、小さく 0.001 スケール程度）に `Rank Text`・`Time Text`

### 確認項目
- [ ] 手配書に触れるとそのカードが自分にだけ壁から消え、同じ種類の前のカードは戻る（他の人の壁は変わらない）
- [ ] 柵の馬に触れると目印が付き、乗り物が馬になる
- [ ] 台帳にサインするとページに自分の名前・乗り物・能力が書かれる（2 クライアントで両方に見える）。もう一度サインすると内容が更新される（2 枠にならない）
- [ ] マスター以外が鐘に触っても始まらない（札に「主催者だけ」と出る）。マスターが鳴らすと全員に鐘が聞こえて開始
- [ ] カウントダウン開始で、エントリー者が練習場や練習用の馬に乗っていても自動でスタート地点の自分の乗り物に座る。3 秒間（Starter があれば旗と空砲まで、最大 6 秒）動けない
- [ ] 3 ステージ通しで総合結果が出て、20 秒後にロビーへ戻る（乗り物から降ろされロビーへテレポート、台帳が空になる）
- [ ] 1 位ゴール後 60 秒でステージ終了（懐中時計・補助表示の残り時間が 1:00 からに縮む）
- [ ] 誰もゴールしなくても 5:30 でステージ終了
- [ ] ステージ結果は 10 秒。黒板に着順と獲得点（10/8/6/5/4/3/2/1）と合計が出る
- [ ] ゴール 3 秒後にゴール後の囲いへ移され、囲いの中は自由に動ける
- [ ] 途中参加：レース中は台帳にサインしても入れない。ステージ結果中にサインすると次のステージから走れて、合計は 0 点から
- [ ] マスター退出でも続行（新マスターでタイマー・順位・ポイントがそのまま続く）
- [ ] 抜けた人の枠が消える（台帳・黒板から消え、その乗り物は空になる）
- [ ] ワールドの端同士（ロビーとステージ 3 のゴールなど）でも声が普通に聞こえる。後から入った人の声も聞こえる
- [ ] 補助表示（左下の古い紙：「3位」「残り 3:12」）がレース中だけ出る。レバーで消せる・戻せる（自分だけ）
- [ ] 懐中時計：VR は左手首、デスクトップは画面下に見え、残り時間が読める。レース外は消える
- [ ] 練習用の馬に杭で乗れて、もう一度杭に触れるか練習場の外へ出ると降りる。他の人が乗っている馬には乗れない
