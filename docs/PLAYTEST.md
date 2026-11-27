# プレイテスト チェックリスト

Unity と VRChat クライアントで実際に確認する項目。自動テスト（`dotnet test` / `dotnet build tests/UdonCompileCheck`）では分からないものだけを書く。

## Task 6: 乗り物

### 事前準備（シーン構成）
- メニュー「HorseRace/Create Horse Materials」→「HorseRace/Build Mount Animator」を実行し、`Assets/HorseRace/Animations/Mount.controller` ができている
- 乗り物 1 頭の構成:
  - ルート: `MountController`、`VRCStation`（Player Mobility = Immobilize、Station Enter Player Location = SeatAnchor、Disable Station Exit はレース中（`raceActive`）に乗り手がいる間だけコードが true にする（レース中に Jump で降車させないため。ロビー・練習場では降りられる。初期値はどちらでも可））、`Rigidbody`（Is Kinematic）、体の Collider
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
- [ ] レース中（カウントダウン〜ゴール）はジャンプ（Space / ボタン）で降車しない。ロビー・練習場・ステージの合間はジャンプで降りられる
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
- **黒板**: 各 CP の黒板に `RankBoard`（`Board Text` は TextMeshPro）。たまり場の黒板は `Show Last Race` を ON にする（ロビー中は前回のレースの表＝ステージ別の着順と総合ポイント）
- **文字の型**: ワールド内の文字（台帳・黒板・懐中時計・手配書・鐘の札・レバーの札）は TextMeshPro（3D）、補助表示の文字だけ TextMeshProUGUI（ワールド空間 Canvas の中）
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
- [ ] 練習用の馬に杭で乗れて、ジャンプ・もう一度杭に触れる・練習場の外へ出る のどれでも降りる（降りた馬は杭の位置に戻る）。他の人が乗っている馬には乗れない
- [ ] たまり場の黒板に、ロビーに戻った後も前回のレースの表（ステージ別の着順・合計点）が残る。途中で抜けた人は載らない。新しいレースの総合結果で更新される
- [ ] 最終ステージの結果表示中は台帳にサインしても入れない
- [ ] ステージの合間に、既にエントリーしている人がサインし直しても乗り物・毛色・能力は変わらない（変えられるのはロビーだけ）
- [ ] カウントダウンの同期を取りこぼした（遅れて届いた）クライアントでも、レース開始後に自分の乗り物に座れる

## Task 9: 能力・遺物・リボルバー

### セットアップ（シーンに置くもの・つなぐもの）
- **前提**: Task 8 と同じ（VRChat SDK 3.8 以降・UdonSharp 1.2 以降、引数つきネットワークイベント `[NetworkCallable]` を使う）
- **レイヤー**（例）: `HR_Target`（乗り物の体と騎手の当たり判定）、`HR_Wall`（岩壁）。地形・岩は既存の地面レイヤー
  - 各乗り物の体の Collider を `HR_Target` に置き、騎手の上半身を覆うカプセル Collider（Is Trigger 可）を乗り物の子として追加して `HR_Target` に置く
  - `HR_Target` と `HR_Wall` は MountController の `Ground Mask` に含めない。`HR_Wall` は `Obstacle Mask` に含める
- **AbilityCaster**: 乗り物（レース用 8 頭＋練習用 2 頭）の MountController と**同じ GameObject** に 1 つずつ（同期は Manual のまま）
  - `Mount` = 同じ MountController、`Caster Id` = レース用は slot 番号 0〜7、練習用は 8・9（全部で重複させない）
  - 練習用の 2 頭だけ `Practice Mount` を ON、`Practice Area` を設定。全頭 `Race Manager` を設定
  - `Target Mask` = `HR_Target`（＋カカシのレイヤー）、`Block Mask` = 地形・岩、`Ground Mask` = 地形
  - `Pouch`・`Revolver`・`Dust Cloud`・`Lasso`・`Rock Wall`・`Balls`（3 個）・`Relic Pool`（予備の遺物 4 個、全頭同じ並び）・`Haze`（任意）をつなぐ
- **見た目オブジェクト**（乗り物ごとに 1 組。乗り物の子に置いてよい。起動時に親から外れる）
  - `DustCloud`: 半径 1m（`Base Radius`）で作った砂煙（球状のパーティクル）を `Visual`／`Particles` に
  - `LassoEffect`: LineRenderer（縄の色・太さ 3cm 程度）。`Mount Mask` = `HR_Target`
  - `RockWall`: 子に壁 3 枚（各幅 6m・高さ約 4m。`Packs/Cliffs` か `Packs/DesertRocks` の岩＋BoxCollider、レイヤー `HR_Wall`）を `Segments` に。`Ground Mask` = 地形
  - `IronBall` ×3: 直径 0.7m 程度の球（Collider なし）を `Visual` に。`Ground Mask` = 地形（＋`HR_Wall` ではねさせるなら追加）
  - `Haze`: 自分の頭を包む半透明の砂色の球（裏面描画、半径 0.3m）。ローカルだけで出る
- **CharmPouch**（腰のお守り袋）: 鞍の脇の小さな袋のモデル（乗り物の子）。`Pouch Renderer`、`Ready Material`（光る）・`Idle Material`、`Glow`（任意）、`Level Marks` に刻印 3 個（光るもの）、`Level Marks Unlit`（任意）。初期状態は消灯にしておく（乗っていない人の袋は更新されない）
- **RevolverHolder**: `Packs/Revolver/Revolver.fbx` を部品を組んだまま乗り物の子（鞍の脇のホルスター位置）に置き、ルートに `VRC Pickup`（Auto Hold = Yes、Exact Gun にグリップ位置）＋ Rigidbody（Is Kinematic）＋ `RevolverHolder`（スクリプトが NoVariableSync を指定。ネットワークイベントだけ使う）。VRCObjectSync は付けない
  - `Caster` = 同じ乗り物の AbilityCaster、`Pickup` = 上の VRC Pickup
  - `Cylinder` = Revolving_Cylinder、`Hammer` = Hammer、`Trigger Part` = Trigger、`Bullets` = Bullet1〜Bullet6（この順）、`Muzzle` = 銃口に置いた空の子（z+ を弾の向きに）
  - 回転軸（`Cylinder Axis`・`Hammer Axis`・`Trigger Axis`）は FBX の向きに合わせて調整
  - `Fx Root`（銃口の火・煙のパーティクルの親）、`Muzzle Flash`、`Smoke`、`Gunshot`、`Dry Fire`、`Tracer`（任意）
- **RelicPickup**（遺物）: `Packs/Gems/Gems.fbx` の宝石 1 種（色違いの 1 つ）を `Visual` に。Is Trigger の Collider（半径 1.5m 程度）と同じ GameObject に置く。`Race Manager`・`Ground Mask` を設定
  - コース上の遺物（各ステージ数か所）: `Is Spare` OFF
  - 落とした遺物用の予備 4 個: `Is Spare` ON、`Pool` に予備 4 個すべて（4 個とも同じ並び）。置き場所はどこでもよい（出るときに移動する）
  - 練習場の遺物（任意）: `Practice Respawn Seconds` を 10 など（拾われてから戻る）
- **カカシ**（練習場）: `PracticeDummy`（`Pivot` = 揺れる部分）、当たり判定の Collider を AbilityCaster の `Target Mask` に含まれるレイヤーに
- **RaceManager**: `Stage Reset Targets` に全 AbilityCaster（10）＋全 RelicPickup（コース上・予備・練習場）、`Refill Targets` に全 RevolverHolder（10）

### 操作
- 能力: デスクトップ = E（照準は視線）、VR = トリガー（照準は押した手の向き。リボルバーを持っている手のトリガーは射撃）
- 鉄球のカーブ: VR は手を左右に傾ける、デスクトップは A / D を押しながら投げる
- リボルバー: VR = ホルスターの銃をつかむと構え、トリガーで撃つ（離すとホルスターに戻る）／デスクトップ = 右クリックを押している間構え、左クリックで撃つ

### 確認項目
- [ ] 4 能力が 2 クライアント間で効く: 砂塵（後方に砂煙、中を通った相手が減速し視界がかすむ）／投げ縄（前方の相手に縄が掛かり自分は加速・相手は一瞬減速、両方の画面で縄が見える）／岩壁（前方の地面から岩がせり上がり、両方のクライアントでぶつかる、6 秒で沈む）／鉄球（地面で 3 回はねて消える、当たった相手がよろけて減速、傾けて投げると曲がる）
- [ ] 腰のお守り袋の光でクールダウンがわかる（使うと消え、使えるようになると光る。数字は出ない）。刻印の数 = Lv
- [ ] 遺物に触れると消えて Lv が上がる（刻印が増える、最大 3）。拾われた遺物は全員の画面から消え、そのステージ中は戻らない
- [ ] 2 人が同時に同じ遺物に触れても、Lv が上がるのは 1 人だけ
- [ ] ステージ開始で Lv1・クールダウン解除・弾 6 発（シリンダーの弾が 6 個見える）・コース上の遺物が戻る・落ちた遺物が消える
- [ ] リボルバーを外すと 3 秒遅くなる（×0.8）。構えている間は少し遅い（×0.9）
- [ ] 命中で相手の馬がよろけ、1 秒遅くなる（×0.6）
- [ ] 同じ相手に 3 発当てるとその相手の Lv が 1 下がり、相手の少し後ろに遺物が落ちて誰でも拾える（1 秒後から）。相手が Lv1 なら落ちない。カウントはステージごとにリセット
- [ ] 撃つたびに撃鉄が動きシリンダーが 60° 回り、弾が 1 個消える。6 発撃つと撃てない（カチッという音だけ）
- [ ] 銃声・銃口の火と煙が他のクライアントでも出る
- [ ] 被弾（リボルバー・鉄球・投げ縄・砂塵）で視点が回らない
- [ ] 練習場: 練習用の馬で手配書で選んだ能力が使え、手配書を取り替えると能力も替わる。クールダウンが 1/3、弾が減らない。カカシに当てると揺れる（外し扱いにならない）
- [ ] レース外（ロビー・カウントダウン中・ゴール後の囲い）ではレース用の乗り物で能力・リボルバーが使えない
