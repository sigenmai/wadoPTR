# 荒野横断ホースレース Phase 1 Implementation Plan

> **注意：この計画は `2026-11-27-vrc-horse-race-phase1-v2.md` に置き換えられました。**

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 3ステージ×5分・最大8人・能力4種（砂塵/投げ縄/岩壁/鉄球）で遊べる VRChat ワールドを、Unity メニュー1つで組み上がる形で納品する。

**Architecture:** 純粋な計算（馬の挙動・スタミナ・順位・ステージ終了・能力Lv・地形高さ・コース点列）は Unity/VRChat に依存しない static メソッドに切り出し、リポジトリ内の dotnet テストで TDD する。UdonSharp の振る舞い（馬・同期・レース進行・能力）とエディタ拡張（地形/コース/シーン生成）はそれらを呼ぶ薄い層にし、ユーザーの PC での手動プレイテストチェックリストで検証する。Phase 2（恐竜・爆弾・蜃気楼、3Dモデル差し替え）は別プランにする。

**Tech Stack:** Unity 2022.3 LTS（VRChat 推奨版）, VRChat SDK3 Worlds, UdonSharp 1.x（VCC 経由）, C#; テストは .NET 8 + NUnit + 最小 UnityEngine スタブ。

**Spec:** `docs/superpowers/specs/2026-11-26-vrc-horse-race-design.md`

## Global Constraints

- 原作（ジョジョ第7部）の名前・キャラ・能力名・デザインを一切使わない。コード・アセット・UI文言すべて。
- 対応は PC のみ（PC VR ＋ デスクトップ）。Quest ビルドは作らない。
- 最大8人。1ステージ約5分、3ステージ。ステージ終了 = 1位ゴール後60秒 or 開始後330秒の早いほう。
- 馬の挙動は VR/デスクトップ共通。入力層だけ分ける。
- 酔い対策：カメラ（=座席アンカー）はピッチ・上下動を平滑化、被弾で視点を強制回転させない。
- 能力 Lv1〜3、遺物で +1、ステージ開始で Lv1 リセット。遺物は拾われたらそのステージ中は消える。
- 能力の当たり判定は発動側クライアント、効果は ネットワークイベントで全員へ。
- 各ステージ約1km四方の Terrain、コース約2.5km、3ステージは原点近く（各中心が原点から±1.5km以内）に配置。
- UdonSharp 1.x の制約内で書く：ジェネリクス・ユーザー定義クラスのインスタンス・LINQ・try/catch 不可。共有ロジックは `UdonSharpBehaviour` 派生クラスの `public static` メソッド（引数・戻り値は primitive / 配列 / Vector3）。
- すべてのランタイムファイルは `Assets/HorseRace/` 以下、名前空間 `HorseRace`。

## Review Focus

1. **マスターがレース中に退出** → 新マスターがそのまま進行を引き継ぐ（ステージ開始時刻はサーバー時刻で同期し、ローカルタイマーに依存しない）。Task 3 の `ShouldEndStage` を絶対サーバー時刻で検証、Task 9 チェックリスト項目。
2. **コース外落下・地形すり抜け** → 最後に通過したチェックポイントへ3秒以内にリセット。Task 5 のリセット判定テスト、Task 9 項目。
3. **ステージ途中で参加** → 観戦扱いで、次ステージ開始時に自動エントリー。Task 6 のエントリー判定テスト。
4. **レース中に席を立つ（VRでジャンプ/降車）** → 再着席させる。2回目以降で DNF ではなく即再着席（降りられない）。Task 4 チェックリスト項目。
5. **同じ遺物を2人がほぼ同時に取る** → 遺物オーナーが先着1人だけを確定し、もう1人は何も起きない。Task 7 の `RelicArbiter` テスト。

---

## File Structure

```
Assets/HorseRace/
  Scripts/Logic/            # 純粋 static。dotnet テスト対象
    HorseMath.cs            # 速度・旋回・スタミナ・着地硬直・カメラ平滑化
    RaceMath.cs             # 順位・ポイント・ステージ終了・エントリー判定・リセット判定
    AbilityMath.cs          # 能力ID・Lv・クールダウン・Lv別パラメータ・遺物先着
  Scripts/Runtime/          # UdonSharp。手動テスト
    HorseController.cs      # 1頭分：入力→HorseMath→移動、着席、同期送受信
    HorseInputVR.cs         # 手綱（手の位置）→ throttle/turn/sprint
    RaceManager.cs          # ロビー→ステージ1-3→結果、マスター保持の同期状態
    Checkpoint.cs           # 通過通知
    RelicPickup.cs          # 遺物
    AbilityCaster.cs        # 能力発動・当たり判定・効果イベント
    Effects/DustCloud.cs, LassoEffect.cs, RockWall.cs, IronBall.cs
    UI/LobbyPanel.cs, RaceHud.cs
  Editor/
    Gen/HeightmapGen.cs     # 純粋：ステージ種別→float[,] 高さ。dotnet テスト対象
    Gen/CoursePath.cs       # 純粋：制御点→等間隔点列・長さ。dotnet テスト対象
    Gen/StageDefinitions.cs # 3ステージの種別・シード・コース制御点（データ）
    TerrainBuilder.cs       # Terrain 生成（Unity API）
    CourseBuilder.cs        # チェックポイント/遺物/スタート/ゴール/リセット領域配置
    SceneAssembler.cs       # メニュー「HorseRace/Build World」
  Prefabs/                  # SceneAssembler が生成（プリミティブ製の仮モデル）
tests/
  UnityStubs/               # Vector3, Mathf, UdonSharpBehaviour 空基底, 属性
  HorseRace.Tests/          # NUnit。Logic と Editor/Gen の純粋ファイルをリンクしてビルド
docs/
  SETUP.md                  # クリック単位のセットアップ手順
  PLAYTEST.md               # 手動テストチェックリスト（Review Focus 含む）
```

---

### Task 1: テスト基盤 ＋ HorseMath

**Files:**
- Create: `tests/UnityStubs/UnityStubs.csproj`, `tests/UnityStubs/Stubs.cs`
- Create: `tests/HorseRace.Tests/HorseRace.Tests.csproj`（`<Compile Include="../../Assets/HorseRace/Scripts/Logic/**/*.cs" />` と `../../Assets/HorseRace/Editor/Gen/HeightmapGen.cs`, `CoursePath.cs` をリンク）
- Create: `Assets/HorseRace/Scripts/Logic/HorseMath.cs`
- Test: `tests/HorseRace.Tests/HorseMathTests.cs`

**Interfaces:**
- Produces（`public class HorseMath : UdonSharpBehaviour` の static）:
  - 定数 `GAIT_WALK=0, GAIT_TROT=1, GAIT_CANTER=2, GAIT_SPRINT=3`
  - `float GaitSpeed(int gait)` → 3, 6, 10, 13 (m/s)
  - `float TargetSpeed(int gait, float slopeDeg, bool exhausted)`：exhausted なら gait を CANTER に上限。上り（slopeDeg>0）で 1度あたり -1.5%、下りで +1%、係数は [0.5, 1.25] にクランプ
  - `float Approach(float current, float target, float dt)`：加速 4 m/s²、減速 8 m/s²
  - `float StepStamina(float stamina, bool sprinting, float dt)`：範囲 [0,100]、疾走で -12/s、非疾走で +8/s
  - `bool IsExhausted(float stamina, bool wasExhausted)`：0 で true、25 以上回復で false（ヒステリシス）
  - `float TurnRateDeg(float speed)`：速度0で 90°/s、13 m/s で 45°/s の線形
  - `float LandingStun(float fallHeight)`：3m 未満 0、以上は `(h-3)*0.15` 秒、上限 1.5
  - `float SmoothAngle(float current, float target, float dt)`：指数平滑、時定数 0.25s（カメラピッチ用）
- Stubs: `UnityEngine.Vector3`（x,y,z, +,-,*, magnitude, normalized, Dot, Lerp, Distance）, `UnityEngine.Mathf`（Clamp, Clamp01, Lerp, Exp, Min, Max, Abs, Sin, Cos, PerlinNoise は決定的な実装）, `UdonSharp.UdonSharpBehaviour` 空クラス, `UdonSharp.UdonSyncedAttribute`, `UdonSharp.UdonBehaviourSyncModeAttribute`, `BehaviourSyncMode` enum。

- [ ] **Step 1: スタブとテストプロジェクトを作り、失敗するテストを書く**

```csharp
[Test] public void GaitSpeeds() { Assert.AreEqual(13f, HorseMath.GaitSpeed(HorseMath.GAIT_SPRINT)); Assert.AreEqual(3f, HorseMath.GaitSpeed(HorseMath.GAIT_WALK)); }
[Test] public void Exhausted_CapsAtCanter() => Assert.AreEqual(10f, HorseMath.TargetSpeed(HorseMath.GAIT_SPRINT, 0f, true), 1e-4);
[Test] public void Uphill10Deg_Slows15Percent() => Assert.AreEqual(10f * 0.85f, HorseMath.TargetSpeed(HorseMath.GAIT_CANTER, 10f, false), 1e-4);
[Test] public void SteepSlope_ClampedToHalf() => Assert.AreEqual(5f, HorseMath.TargetSpeed(HorseMath.GAIT_CANTER, 60f, false), 1e-4);
[Test] public void Approach_AccelLimited() => Assert.AreEqual(4f, HorseMath.Approach(0f, 13f, 1f), 1e-4);
[Test] public void Approach_DecelFaster() => Assert.AreEqual(5f, HorseMath.Approach(13f, 0f, 1f), 1e-4);
[Test] public void Stamina_DrainsAndClamps() { Assert.AreEqual(88f, HorseMath.StepStamina(100f, true, 1f), 1e-4); Assert.AreEqual(0f, HorseMath.StepStamina(5f, true, 1f)); Assert.AreEqual(100f, HorseMath.StepStamina(98f, false, 1f)); }
[Test] public void Exhaustion_Hysteresis() { Assert.IsTrue(HorseMath.IsExhausted(0f, false)); Assert.IsTrue(HorseMath.IsExhausted(20f, true)); Assert.IsFalse(HorseMath.IsExhausted(25f, true)); }
[Test] public void TurnRate_SlowerAtSpeed() { Assert.AreEqual(90f, HorseMath.TurnRateDeg(0f), 1e-4); Assert.AreEqual(45f, HorseMath.TurnRateDeg(13f), 1e-4); }
[Test] public void LandingStun_Thresholds() { Assert.AreEqual(0f, HorseMath.LandingStun(2.9f)); Assert.AreEqual(0.75f, HorseMath.LandingStun(8f), 1e-4); Assert.AreEqual(1.5f, HorseMath.LandingStun(50f)); }
[Test] public void SmoothAngle_NoSnap() { float a = HorseMath.SmoothAngle(0f, 30f, 1f/60f); Assert.Greater(a, 0f); Assert.Less(a, 3f); }
```

- [ ] **Step 2: 実行して失敗を確認**
Run: `dotnet test tests/HorseRace.Tests` → Expected: ビルドエラー（HorseMath 未定義）

- [ ] **Step 3: `HorseMath.cs` を上記シグネチャで実装**

- [ ] **Step 4: 実行して成功を確認**
Run: `dotnet test tests/HorseRace.Tests` → Expected: 全テスト PASS

- [ ] **Step 5: Commit** — `git add tests Assets/HorseRace/Scripts/Logic/HorseMath.cs && git commit -m "feat: horse movement math with tests"`

---

### Task 2: 地形高さ生成 ＋ コース点列（純粋部分）

**Files:**
- Create: `Assets/HorseRace/Editor/Gen/HeightmapGen.cs`, `Assets/HorseRace/Editor/Gen/CoursePath.cs`
- Test: `tests/HorseRace.Tests/HeightmapGenTests.cs`, `tests/HorseRace.Tests/CoursePathTests.cs`

**Interfaces:**
- Produces（通常の C# static クラス、Editor 専用なので U# 制約なし）:
  - `HeightmapGen.KIND_CANYON=0, KIND_DUNES=1, KIND_MESA=2`
  - `float[,] HeightmapGen.Generate(int resolution, int kind, int seed, Vector3[] coursePointsNormalized)`：値は [0,1]（Terrain 高さ 200m に対する比率）。コース点から 12m（正規化換算）以内は「走路」として起伏を平滑化し、勾配 25°以下に抑える。CANYON = 走路を谷底にした峡谷、DUNES = 正弦＋ノイズの砂丘、MESA = 段丘＋平坦な頂上
  - `float HeightmapGen.MaxSlopeDegAlong(float[,] h, float terrainSizeM, float heightM, Vector3[] coursePointsNormalized)`
  - `Vector3[] CoursePath.Sample(Vector3[] controlPoints, float spacing)`：centripetal Catmull-Rom、始点・終点を含む等間隔点列
  - `float CoursePath.Length(Vector3[] points)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Test] public void Generate_Deterministic() => CollectionAssert.AreEqual(HeightmapGen.Generate(129, HeightmapGen.KIND_CANYON, 7, Course()), HeightmapGen.Generate(129, HeightmapGen.KIND_CANYON, 7, Course()));
[Test] public void Generate_InRange([Values(0,1,2)] int kind) { foreach (var v in HeightmapGen.Generate(129, kind, 1, Course())) Assert.That(v, Is.InRange(0f, 1f)); }
[Test] public void CourseIsRideable([Values(0,1,2)] int kind) { var h = HeightmapGen.Generate(513, kind, 3, Course()); Assert.LessOrEqual(HeightmapGen.MaxSlopeDegAlong(h, 1000f, 200f, Course()), 25f); }
[Test] public void Sample_EvenSpacing() { var p = CoursePath.Sample(new[]{ new Vector3(0,0,0), new Vector3(100,0,0), new Vector3(100,0,100) }, 10f); for (int i = 1; i < p.Length - 1; i++) Assert.AreEqual(10f, Vector3.Distance(p[i-1], p[i]), 0.5f); }
[Test] public void Sample_HitsEndpoints() { var c = new[]{ new Vector3(0,0,0), new Vector3(50,0,0), new Vector3(80,0,40) }; var p = CoursePath.Sample(c, 5f); Assert.AreEqual(0f, Vector3.Distance(p[0], c[0]), 1e-3); Assert.AreEqual(0f, Vector3.Distance(p[p.Length-1], c[2]), 1e-3); }
```
（`Course()` はテスト内ヘルパー：正規化座標の蛇行する点列）

- [ ] **Step 2: 失敗を確認** — `dotnet test tests/HorseRace.Tests` → ビルドエラー
- [ ] **Step 3: 両ファイルを実装**。ノイズは `Mathf.PerlinNoise` を使い、スタブ側も決定的な実装にする
- [ ] **Step 4: 成功を確認** — 全 PASS
- [ ] **Step 5: Commit** — `feat: procedural heightmap and course path sampling`

---

### Task 3: RaceMath（順位・ポイント・ステージ終了・エントリー・リセット）

**Files:**
- Create: `Assets/HorseRace/Scripts/Logic/RaceMath.cs`
- Test: `tests/HorseRace.Tests/RaceMathTests.cs`

**Interfaces:**
- Produces（`public class RaceMath : UdonSharpBehaviour` の static）:
  - `const float STAGE_HARD_LIMIT = 330f, AFTER_FIRST_FINISH = 60f`
  - `bool ShouldEndStage(double serverNow, double stageStart, double firstFinishAt)`：`firstFinishAt < 0` は未ゴール
  - `int[] ComputeOrder(int[] cpReached, float[] distToNextCp, double[] finishAt, bool[] active)`：返り値は active なスロット番号を順位順に。完走者（finishAt≥0）は finishAt 昇順で先、未完走は cpReached 降順 → distToNextCp 昇順
  - `int PointsForPlace(int place)`（0始まり）：10, 8, 6, 5, 4, 3, 2, 1、それ以降 0
  - `bool CanEnterStage(int raceState, int stageIndexJoined, int currentStage)`：ロビー中は可、ステージ中は不可（次ステージから）
  - `bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse)`：地形下 -2m、またはコースから 60m 超が 3秒継続で true
  - State 定数 `STATE_LOBBY=0, STATE_COUNTDOWN=1, STATE_RACING=2, STATE_STAGE_RESULT=3, STATE_FINAL_RESULT=4`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Test] public void EndsAtHardLimit() { Assert.IsFalse(RaceMath.ShouldEndStage(1329.9, 1000, -1)); Assert.IsTrue(RaceMath.ShouldEndStage(1330, 1000, -1)); }
[Test] public void Ends60sAfterFirst() { Assert.IsFalse(RaceMath.ShouldEndStage(1259, 1000, 1200)); Assert.IsTrue(RaceMath.ShouldEndStage(1260, 1000, 1200)); }
[Test] public void ServerTimeSurvivesMasterChange() => Assert.IsTrue(RaceMath.ShouldEndStage(5_000_330.0, 5_000_000.0, -1)); // 大きな絶対時刻でも正しい
[Test] public void Order_FinishersFirstThenProgress() {
  var o = RaceMath.ComputeOrder(new[]{5,9,9,7}, new[]{10f,50f,20f,0f}, new[]{-1.0,-1.0,-1.0,300.0}, new[]{true,true,true,true});
  CollectionAssert.AreEqual(new[]{3,2,1,0}, o); }
[Test] public void Order_SkipsInactive() => CollectionAssert.AreEqual(new[]{1}, RaceMath.ComputeOrder(new[]{3,1}, new[]{0f,0f}, new[]{-1.0,-1.0}, new[]{false,true}));
[Test] public void Points() { Assert.AreEqual(10, RaceMath.PointsForPlace(0)); Assert.AreEqual(1, RaceMath.PointsForPlace(7)); Assert.AreEqual(0, RaceMath.PointsForPlace(8)); }
[Test] public void LateJoiner_WaitsForNextStage() { Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_LOBBY, 0, 0)); Assert.IsFalse(RaceMath.CanEnterStage(RaceMath.STATE_RACING, 1, 1)); Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_STAGE_RESULT, 1, 1)); }
[Test] public void Reset_Rules() { Assert.IsTrue(RaceMath.NeedsReset(-2.1f, 0f, 0f)); Assert.IsFalse(RaceMath.NeedsReset(0f, 70f, 2.9f)); Assert.IsTrue(RaceMath.NeedsReset(0f, 70f, 3f)); }
```

- [ ] **Step 2: 失敗を確認** — ビルドエラー
- [ ] **Step 3: 実装**（ComputeOrder は配列の挿入ソート。U# 制約で Array.Sort の比較関数は使わない）
- [ ] **Step 4: 成功を確認**
- [ ] **Step 5: Commit** — `feat: race ranking, scoring and stage rules`

---

### Task 4: HorseController（着席・入力・移動・同期）＋ HorseInputVR

**Files:**
- Create: `Assets/HorseRace/Scripts/Runtime/HorseController.cs`, `HorseInputVR.cs`
- Create: `docs/PLAYTEST.md`（このタスクの項目から書き始める）

**Interfaces:**
- Consumes: `HorseMath.*`
- Produces（`HorseController : UdonSharpBehaviour`, `BehaviourSyncMode.Manual`）:
  - 同期変数: `[UdonSynced] Vector3 syncPos; float syncYaw; float syncSpeed; int syncStunMs`（送信 10Hz、`RequestSerialization`）
  - `public int slotIndex`（0-7、インスペクタで設定）
  - `public void AssignRider(int playerId)`：RaceManager から呼ぶ。対象プレイヤーがローカルなら Owner を取り着席
  - `public void TeleportTo(Vector3 pos, float yaw)`
  - `public void ApplySlow(float factor, float seconds)`, `public void ApplyKnock(Vector3 impulse, float stunSeconds)`, `public void ApplyPull(Vector3 targetPos, float speedBonus, float seconds)`：能力効果の受け口（オーナー側で処理）
  - `public float Stamina`, `public bool InputLocked`（カウントダウン中 true）
- 実装方針:
  - 移動は CharacterController でも Rigidbody でもなく、毎フレーム下向き Raycast（Terrain レイヤー）で接地・法線から坂角度を取り、`HorseMath` で速度、`transform` を直接更新。空中は重力 20 m/s²、着地で `LandingStun`
  - 席：子に `VRCStation`（Mobility=Immobilize, DisableStationExit=true）。`OnStationExited` でレース中なら即 `UseStation` し直す（Review Focus 4）
  - デスクトップ入力：`InputMoveVertical/Horizontal`、`InputJump`、Shift は `Input.GetKey(KeyCode.LeftShift)`、能力は `InputUse`
  - VR 入力（HorseInputVR）：両手の位置を鞍アンカー基準で取り、両手を手前に 15cm 以上引く→減速、左右差 10cm 以上→旋回、両手の上下速度 1.2 m/s 以上→疾走。スティック入力があればそちらを優先
  - 非オーナー側：`syncPos` へ 0.1s 遅延で補間＋`syncSpeed` で外挿（最大 0.3s）
  - 座席アンカーのピッチは `HorseMath.SmoothAngle` を通す。ヨーは馬と同じ（被弾で回さない）

- [ ] **Step 1: 実装**（上記）
- [ ] **Step 2: コンパイル確認** — Run: `dotnet test tests/HorseRace.Tests`（既存ロジックが壊れていないこと）。U# のコンパイルは Unity 側でのみ可能なため、PLAYTEST.md に「Unity Console にエラー 0」を最初の項目として書く
- [ ] **Step 3: PLAYTEST.md に項目を追加**：デスクトップで乗れる／走る・曲がる・跳ぶ／疾走でスタミナが減り尽きると駆歩まで／坂で速度が変わる／崖から落ちて硬直／VR で手綱操作が効く／VR で降車しようとしても再着席／2クライアントで相手の馬がガクつかない
- [ ] **Step 4: Commit** — `feat: horse controller with desktop/VR input and sync`

---

### Task 5: Checkpoint・コース外リセット

**Files:**
- Create: `Assets/HorseRace/Scripts/Runtime/Checkpoint.cs`
- Modify: `HorseController.cs`（リセット処理）

**Interfaces:**
- Consumes: `RaceMath.NeedsReset`, `HorseController.TeleportTo`
- Produces: `Checkpoint : UdonSharpBehaviour` — `public int index; public int stageIndex; public bool isGoal`。ローカルの騎手の馬が `OnTriggerEnter` したら `RaceManager.LocalReachedCheckpoint(stageIndex, index)` を呼ぶ。HorseController は最後に通過した CP の位置と向きを保持し、`NeedsReset` が true でそこへ `TeleportTo`
- [ ] **Step 1: 実装**
- [ ] **Step 2: `dotnet test` が通ることを確認**
- [ ] **Step 3: PLAYTEST.md に追加**：CP を飛ばして逆走しても順位が進まない／崖下・地形下に落ちると3秒以内に最後の CP へ戻る
- [ ] **Step 4: Commit** — `feat: checkpoints and off-course reset`

---

### Task 6: RaceManager ＋ ロビー/HUD

**Files:**
- Create: `Assets/HorseRace/Scripts/Runtime/RaceManager.cs`, `UI/LobbyPanel.cs`, `UI/RaceHud.cs`

**Interfaces:**
- Consumes: `RaceMath.*`, `HorseController.AssignRider/TeleportTo/InputLocked`, `AbilityCaster.ResetForStage()`（Task 7）
- Produces（`RaceManager : UdonSharpBehaviour`, Manual sync、Owner = インスタンスマスター）:
  - 同期: `int state; int stage; double stageStartServer; double firstFinishServer; int[] slotPlayerId(8); int[] slotAbility(8); int[] slotTotalPoints(8); int[] slotCp(8); double[] slotFinish(8)`
  - 各クライアントは自分の進捗（cp・次CPまでの距離・ゴール時刻）を自分の `HorseController` の同期変数に乗せる → マスターがそれを読み `ComputeOrder`
  - `public void LocalReachedCheckpoint(int stage, int index)`、`public void RequestJoin(int abilityId)`（ロビーで能力選択）、`public void RequestStart()`（マスターのみ有効）
  - 進行: LOBBY →(開始)→ COUNTDOWN 3秒（InputLocked）→ RACING（`ShouldEndStage` を `Networking.GetServerTimeInSeconds()` で毎秒判定）→ STAGE_RESULT 10秒（ポイント加算、遺物復活、能力 Lv リセット、途中参加者のエントリー）→ 次ステージ or FINAL_RESULT → 20秒後 LOBBY
  - `OnOwnershipTransferred`：新マスターは同期済み状態からそのまま続行（Review Focus 1）
  - `OnPlayerLeft`：該当スロットを inactive
  - HUD：順位、スタミナバー、能力アイコン＋クールダウン、Lv、残り時間。ロビー：能力4種のボタン（名前は 砂塵/投げ縄/岩壁/鉄球）と開始ボタン
- [ ] **Step 1: 実装**
- [ ] **Step 2: `dotnet test` 通過確認**
- [ ] **Step 3: PLAYTEST.md に追加**：3ステージ通しで総合結果が出る／1位ゴール後60秒で終了／誰もゴールしなくても5分30秒で終了／途中参加者は次ステージから参加／レース中にマスターが抜けても進行が止まらない／抜けた人の枠が順位から消える
- [ ] **Step 4: Commit** — `feat: race manager, lobby and HUD`

---

### Task 7: AbilityMath ＋ 能力4種 ＋ 遺物

**Files:**
- Create: `Assets/HorseRace/Scripts/Logic/AbilityMath.cs`
- Create: `Runtime/AbilityCaster.cs`, `Runtime/RelicPickup.cs`, `Runtime/Effects/DustCloud.cs`, `LassoEffect.cs`, `RockWall.cs`, `IronBall.cs`
- Test: `tests/HorseRace.Tests/AbilityMathTests.cs`

**Interfaces:**
- Produces（`AbilityMath : UdonSharpBehaviour` static）:
  - `ABILITY_DUST=0, ABILITY_LASSO=1, ABILITY_WALL=2, ABILITY_BALL=3`（Phase 2 で 4=恐竜,5=爆弾,6=蜃気楼）
  - `int LevelAfterRelic(int lv)`：最大3
  - `float Cooldown(int ability)`：DUST 18, LASSO 22, WALL 25, BALL 15
  - `float DustRadius(int lv)` 4/6/8 m、`float DustDuration(int lv)` 4/6/8 s、減速係数 0.8・視界かすみ
  - `float LassoRange(int lv)` 25/35/45 m、自分 +4 m/s 2秒、相手 0.7 倍 1秒
  - `int WallCount(int lv)` 1/2/3、壁幅 6 m、前方 20 m、持続 6 s
  - `int BallCount(int lv)` 1/2/3、命中で 0.6 倍 1.2 s＋よろけ、バウンド最大3回、カーブ = 照準ロール角 × 0.5°/m
  - `int RelicArbiter(int currentHolder, int claimantPlayerId)`：currentHolder が -1 なら claimant を返す、それ以外は currentHolder を返す
- 実装方針: AbilityCaster は各 HorseController の兄弟。発動はローカルで当たり判定（Physics.SphereCast / OverlapSphere）→ 対象の HorseController のオーナーへ `SendCustomNetworkEvent` 相当で効果を届ける（U# 1.x の引数付きネットワークイベント、無い SDK 版なら同期変数 `effectSeq` 方式）。エフェクトの見た目は全員に `NetworkEventTarget.All` で出す。RelicPickup は Owner が `RelicArbiter` で先着確定し `[UdonSynced] int holder` を更新、holder だけが Lv+1
- [ ] **Step 1: 失敗するテスト**

```csharp
[Test] public void Level_Caps() { Assert.AreEqual(2, AbilityMath.LevelAfterRelic(1)); Assert.AreEqual(3, AbilityMath.LevelAfterRelic(3)); }
[Test] public void Cooldowns() { Assert.AreEqual(18f, AbilityMath.Cooldown(AbilityMath.ABILITY_DUST)); Assert.AreEqual(15f, AbilityMath.Cooldown(AbilityMath.ABILITY_BALL)); }
[Test] public void LevelScaling() { Assert.AreEqual(8f, AbilityMath.DustRadius(3)); Assert.AreEqual(35f, AbilityMath.LassoRange(2)); Assert.AreEqual(3, AbilityMath.WallCount(3)); Assert.AreEqual(1, AbilityMath.BallCount(1)); }
[Test] public void Relic_FirstClaimWins() { int h = AbilityMath.RelicArbiter(-1, 42); Assert.AreEqual(42, h); Assert.AreEqual(42, AbilityMath.RelicArbiter(h, 7)); }
```
- [ ] **Step 2: 失敗を確認**
- [ ] **Step 3: AbilityMath を実装 → テスト PASS 確認**
- [ ] **Step 4: AbilityCaster・エフェクト4種・RelicPickup を実装**
- [ ] **Step 5: PLAYTEST.md に追加**：4能力それぞれが2クライアント間で効く／クールダウン表示どおり／遺物で Lv が上がり見た目・効果が強くなる／同時に取っても1人だけ／ステージ開始で Lv1／被弾で視点が勝手に回らない
- [ ] **Step 6: Commit** — `feat: four abilities and relics`

---

### Task 8: エディタ生成ツール（地形・コース・シーン組み立て）

**Files:**
- Create: `Assets/HorseRace/Editor/Gen/StageDefinitions.cs`, `TerrainBuilder.cs`, `CourseBuilder.cs`, `SceneAssembler.cs`

**Interfaces:**
- Consumes: `HeightmapGen.Generate`, `CoursePath.Sample`, 全 Runtime コンポーネント
- Produces:
  - `StageDefinitions`：3ステージ分の `kind`（CANYON / DUNES / MESA）、`seed`、`center`（(-1200,0,0) / (0,0,0) / (1200,0,0)）、コース制御点（約2.5km になる蛇行ルート、ステージ3はゴールの町＝プリミティブ建物）
  - メニュー `HorseRace/Build World`：現在のシーンに Terrain×3（1000m 四方、高さ 200m、解像度 513、トゥーン用の単色＋グラデーションの TerrainLayer 2〜3枚：荒野=赤茶/岩、砂漠=砂色）、CP（コース点列から 80m 間隔）、遺物（各ステージ4か所、CP 間の分岐やショートカット付近）、スタートライン（8頭横並び）、ゴール、ロビー（スポーン地点・能力選択パネル）、RaceManager、HorseController×8（プリミティブ製の馬：胴カプセル＋脚＋首）、VRCSceneDescriptor、Directional Light（トゥーン向けの強めの影色）を配置。再実行時は前回生成物（ルート `HorseRace_Generated`）を削除して作り直す
  - メニュー `HorseRace/Validate`：各コースの最大勾配・全長・CP 数をログ出力し、全長が 2.2〜2.8km、勾配 25°以下でなければ警告
- [ ] **Step 1: 実装**
- [ ] **Step 2: `dotnet test` 通過確認**
- [ ] **Step 3: PLAYTEST.md に追加**：Build World でエラーなくシーンができる／Validate が警告なし／各ステージを駆歩で走って約5分（4〜6分）
- [ ] **Step 4: Commit** — `feat: editor tools to generate terrain, course and scene`

---

### Task 9: セットアップ手順書 ＋ 最終チェックリスト

**Files:**
- Create: `docs/SETUP.md`
- Modify: `docs/PLAYTEST.md`（Review Focus 1〜5 が全部入っているか確認・不足を追記）、`README.md`（概要と2つの doc へのリンク）

**Interfaces:** なし

- [ ] **Step 1: SETUP.md をクリック単位で書く**：VRChat Creator Companion インストール → 新規 World プロジェクト（Unity 2022.3）→ UdonSharp 追加 → このリポジトリの `Assets/HorseRace` をプロジェクトの `Assets/` にコピー → 新規シーン → メニュー `HorseRace/Build World` → `HorseRace/Validate` → VRChat SDK パネルで Build & Test（2クライアント起動の設定方法含む）→ アップロード（非公開）
- [ ] **Step 2: PLAYTEST.md を通しで見直し**、Review Focus 5項目の対応項目があることを確認
- [ ] **Step 3: `dotnet test tests/HorseRace.Tests` 全 PASS を確認**
- [ ] **Step 4: Commit** — `docs: setup guide and playtest checklist`

---

## Phase 2（別プラン）
恐竜・爆弾・蜃気楼の追加、馬/恐竜の3Dモデル差し替え、効果音・BGM、数値調整。Phase 1 のプレイテスト結果を受けてから書く。
