# 荒野横断ホースレース Phase 1 Implementation Plan（v2）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> v1（`2026-11-26-vrc-horse-race-phase1.md`）を置き換える。v1 以降に決まった：乗り物（馬・ラクダ・骨の馬）、砂地、リボルバー、ロビー（受付・練習場・たまり場）、観戦、演出、世界観 UI、全員ボイス、作成済み 3D 素材の配置。

**Goal:** 3ステージ×5分・最大8人で、馬／ラクダに乗り、能力4種（砂塵・投げ縄・岩壁・鉄球）とリボルバーで妨害し合える VRChat ワールドを、Unity メニュー1つで組み上がる形で納品する。

**Architecture:** 数値とルール（乗り物の挙動・スタミナ・順位・ステージ終了・能力・リボルバー・速度補正の重ね掛け・中継カメラの切り替え・地形・コース点列）は Unity/VRChat に依存しない static メソッドに切り出し、リポジトリ内の dotnet テストで TDD する。UdonSharp の振る舞いとエディタ拡張はそれを呼ぶ薄い層にし、ユーザーの PC での手動プレイテスト（`docs/PLAYTEST.md`）で検証する。UI は世界の中の物（台帳・手配書・懐中時計・黒板・掲示板）として作る。

**Tech Stack:** Unity 2022.3 LTS（VRChat 推奨版）, VRChat SDK3 Worlds, UdonSharp 1.x（VCC）, C#。テストは .NET 8 + NUnit + 最小 UnityEngine スタブ（この環境に .NET 8 SDK 導入済み）。3D 素材は `Assets/HorseRace/Models/` に作成済み。

**Spec:** `docs/superpowers/specs/2026-11-26-vrc-horse-race-design.md`

## Global Constraints

- 原作の名前・キャラ・能力名・デザインを一切使わない（コード・UI 文言・アセット名すべて）。
- PC のみ（PC VR＋デスクトップ）。最大 8 人。ワールド内の全員の声が距離に関係なく聞こえる。
- 1 ステージ約 5 分、3 ステージ。ステージ終了 = 1 位ゴール後 60 秒 or 開始後 330 秒の早いほう。
- 乗り物：馬（毛色 6 種＋骨の馬、性能同じ）とラクダ。ラクダ＝最高速 11 m/s（馬 13）、スタミナ消費 1/1.6、砂地で減速しない（馬は ×0.85）、旋回 ×0.8、上り坂の減速 ×1.3。
- 挙動は VR/デスクトップ共通、入力層だけ分ける。酔い対策：座席のピッチ・上下動は平滑化、被弾で視点を強制回転させない。
- 能力 Lv1〜3、遺物で +1、ステージ開始で Lv1。遺物は拾われたらそのステージ中は消える。
- リボルバー：1 ステージ 6 発。構え中 ×0.9、外し 3 秒 ×0.8、命中で相手 1 秒 ×0.6、同じ相手に 3 発でその相手は遺物を 1 つ落とす（Lv−1、Lv1 なら落とさない）。カウントはステージごとにリセット。
- 速度補正は掛け算で重ね、下限 ×0.4。
- 当たり判定は撃った／発動した側のクライアント。効果はネットワークイベントで対象のオーナーへ。
- UI は世界観優先（spec 5.14）。画面に直接出すのは「順位」と「残り時間」の小さな補助表示のみ（古い紙・真鍮風、オプションで消せる）。
- 各ステージ約 1km 四方の Terrain、コース約 2.5km、原点近くに配置。厩舎・メサは背景専用（見えない壁で近づけない）。
- UdonSharp 1.x の制約内：ジェネリクス・ユーザー定義クラスのインスタンス・LINQ・try/catch 不可。共有ロジックは `UdonSharpBehaviour` 派生クラスの `public static` メソッド（引数・戻り値は primitive / 配列 / Vector3）。
- ランタイムは `Assets/HorseRace/`、名前空間 `HorseRace`。素材は `Assets/HorseRace/Models/` の既存ファイルを使う（無ければプリミティブで代用し、警告ログ）。

## Review Focus

1. **マスターがレース中に退出** → 新マスターがそのまま進行を引き継ぐ（時刻はサーバー時刻）。Task 3 `ShouldEndStage` を絶対時刻でテスト、PLAYTEST 項目。
2. **コース外落下・地形すり抜け** → 3 秒以内に最後の CP へ戻る。Task 3 `NeedsReset` テスト、PLAYTEST 項目。
3. **ステージ途中で参加** → 観戦扱い、次ステージで自動エントリー。Task 3 `CanEnterStage` テスト。
4. **同じ遺物を 2 人が同時に取る／3 発目の命中が同時に 2 人から来る** → 1 人だけ確定、遺物は 1 つだけ落ちる。Task 4 `RelicArbiter` と `RevolverMath.RegisterHit` のテスト。
5. **速度補正が何重にもかかって止まってしまう** → 掛け算の下限 ×0.4 で止まらない。Task 4 `SpeedMod.Combined` のテスト。

---

## File Structure

```
Assets/HorseRace/
  Scripts/Logic/             # 純粋 static（dotnet テスト対象）
    MountMath.cs             # 乗り物の速度・旋回・スタミナ・坂・砂地・着地硬直・カメラ平滑化
    RaceMath.cs              # 順位・ポイント・ステージ終了・途中参加・リセット判定・状態定数
    AbilityMath.cs           # 能力 ID・Lv・クールダウン・Lv 別パラメータ・遺物先着
    RevolverMath.cs          # 弾数・外し/命中の効果・3 発で遺物落とし
    SpeedMod.cs              # 速度補正の重ね掛け（固定長配列）
    DirectorMath.cs          # 中継カメラの切り替え判定
  Scripts/Runtime/           # UdonSharp（手動テスト）
    MountController.cs       # 1 頭分：着席・入力・移動・同期・効果の受け口
    MountInputVR.cs          # 手綱（手の位置）→ throttle/turn/sprint
    MountVisual.cs           # Animator 駆動（歩様・速度比例の再生速度）・毛色／骨の馬の切替・息づかい
    Checkpoint.cs, SandZone.cs
    RaceManager.cs           # 進行（マスター保持）、全員ボイス
    Lobby/EntryLedger.cs, PosterCard.cs, MountPicker.cs, StartBell.cs, PracticeArea.cs
    Abilities/AbilityCaster.cs, DustCloud.cs, LassoEffect.cs, RockWall.cs, IronBall.cs, RelicPickup.cs, CharmPouch.cs
    Revolver/RevolverHolder.cs  # 構え・発射・命中判定・シリンダー/撃鉄の演出・残弾表示
    Show/SpectatorDirector.cs, NoticeBoardDisplay.cs, Starter.cs, GoalPresenter.cs, Podium.cs, TownCrier.cs
    UI/PocketWatch.cs, RankBoard.cs, MinimalHud.cs
  Shaders/HorseToon.shader   # 作成済み
  Editor/
    Gen/HeightmapGen.cs, CoursePath.cs   # 純粋（dotnet テスト対象）
    Gen/StageDefinitions.cs, TerrainBuilder.cs, CourseBuilder.cs, DressingBuilder.cs
    HorseMaterialBuilder.cs   # 作成済み（毛色マテリアル）
    SceneAssembler.cs         # メニュー「HorseRace/Build World」「HorseRace/Validate」
tests/UnityStubs/, tests/HorseRace.Tests/
docs/SETUP.md, docs/PLAYTEST.md
```

---

### Task 1: テスト基盤 ＋ MountMath

**Files:** Create `tests/UnityStubs/*`, `tests/HorseRace.Tests/HorseRace.Tests.csproj`（`Assets/HorseRace/Scripts/Logic/**/*.cs` と `Assets/HorseRace/Editor/Gen/HeightmapGen.cs`, `CoursePath.cs` をリンク）, `Assets/HorseRace/Scripts/Logic/MountMath.cs`; Test `tests/HorseRace.Tests/MountMathTests.cs`

**Interfaces（Produces, `MountMath : UdonSharpBehaviour` の static）:**
- 定数 `MOUNT_HORSE=0, MOUNT_CAMEL=1`、`GAIT_WALK=0, GAIT_TROT=1, GAIT_CANTER=2, GAIT_SPRINT=3`
- `float GaitSpeed(int mount, int gait)` → 馬 3/6/10/13、ラクダ 3/6/9/11
- `float TargetSpeed(int mount, int gait, float slopeDeg, bool exhausted, bool onSand)`：exhausted は CANTER 上限。上りは 1° あたり −1.5%（ラクダ ×1.3 → −1.95%）、下り +1%、係数を [0.5, 1.25] にクランプ。馬は onSand で ×0.85
- `float Approach(float current, float target, float dt)`：加速 4 m/s²、減速 8 m/s²
- `float StepStamina(int mount, float stamina, bool sprinting, float dt)`：[0,100]、疾走 −12/s（ラクダ −7.5/s）、非疾走 +8/s
- `bool IsExhausted(float stamina, bool wasExhausted)`：0 で true、25 以上で false
- `float TurnRateDeg(int mount, float speed)`：速度 0 で 90°/s、13 m/s で 45°/s の線形。ラクダ ×0.8
- `float LandingStun(float fallHeight)`：3m 未満 0、`(h-3)*0.15` 秒、上限 1.5
- `float SmoothAngle(float current, float target, float dt)`：時定数 0.25s
- Stubs: `UnityEngine.Vector3/Mathf`（PerlinNoise は決定的実装）, `UdonSharp.UdonSharpBehaviour`, `UdonSynced`/`UdonBehaviourSyncMode` 属性, `BehaviourSyncMode`

- [ ] **Step 1: 失敗するテスト**
```csharp
[Test] public void GaitSpeeds() { Assert.AreEqual(13f, MountMath.GaitSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT)); Assert.AreEqual(11f, MountMath.GaitSpeed(MountMath.MOUNT_CAMEL, MountMath.GAIT_SPRINT)); }
[Test] public void HorseSlowsOnSand_CamelDoesNot() { Assert.AreEqual(8.5f, MountMath.TargetSpeed(0, 2, 0f, false, true), 1e-4); Assert.AreEqual(9f, MountMath.TargetSpeed(1, 2, 0f, false, true), 1e-4); }
[Test] public void CamelWorseUphill() => Assert.AreEqual(9f * (1 - 0.195f), MountMath.TargetSpeed(1, 2, 10f, false, false), 1e-4);
[Test] public void Exhausted_CapsAtCanter() => Assert.AreEqual(10f, MountMath.TargetSpeed(0, 3, 0f, true, false), 1e-4);
[Test] public void SlopeClamp() => Assert.AreEqual(5f, MountMath.TargetSpeed(0, 2, 60f, false, false), 1e-4);
[Test] public void Approach() { Assert.AreEqual(4f, MountMath.Approach(0, 13, 1), 1e-4); Assert.AreEqual(5f, MountMath.Approach(13, 0, 1), 1e-4); }
[Test] public void Stamina() { Assert.AreEqual(88f, MountMath.StepStamina(0, 100, true, 1), 1e-4); Assert.AreEqual(92.5f, MountMath.StepStamina(1, 100, true, 1), 1e-4); Assert.AreEqual(0f, MountMath.StepStamina(0, 5, true, 1)); Assert.AreEqual(100f, MountMath.StepStamina(0, 98, false, 1)); }
[Test] public void Exhaustion_Hysteresis() { Assert.IsTrue(MountMath.IsExhausted(0, false)); Assert.IsTrue(MountMath.IsExhausted(20, true)); Assert.IsFalse(MountMath.IsExhausted(25, true)); }
[Test] public void TurnRate() { Assert.AreEqual(90f, MountMath.TurnRateDeg(0, 0), 1e-4); Assert.AreEqual(45f, MountMath.TurnRateDeg(0, 13), 1e-4); Assert.AreEqual(72f, MountMath.TurnRateDeg(1, 0), 1e-4); }
[Test] public void LandingStun() { Assert.AreEqual(0f, MountMath.LandingStun(2.9f)); Assert.AreEqual(0.75f, MountMath.LandingStun(8f), 1e-4); Assert.AreEqual(1.5f, MountMath.LandingStun(50f)); }
[Test] public void SmoothAngle_NoSnap() { float a = MountMath.SmoothAngle(0, 30, 1f / 60); Assert.Greater(a, 0); Assert.Less(a, 3); }
```
- [ ] **Step 2:** `dotnet test tests/HorseRace.Tests` → ビルドエラー（未定義）
- [ ] **Step 3:** `MountMath.cs` を実装
- [ ] **Step 4:** `dotnet test tests/HorseRace.Tests` → PASS
- [ ] **Step 5:** Commit `feat: mount movement math with tests`

---

### Task 2: 地形高さ生成 ＋ コース点列 ＋ 砂地マスク（純粋部分）

**Files:** Create `Assets/HorseRace/Editor/Gen/HeightmapGen.cs`, `CoursePath.cs`; Test `HeightmapGenTests.cs`, `CoursePathTests.cs`

**Interfaces（通常の static クラス、Editor 専用）:**
- `HeightmapGen.KIND_CANYON=0, KIND_DUNES=1, KIND_MESA=2`
- `float[,] Generate(int resolution, int kind, int seed, Vector3[] coursePointsNormalized)`：[0,1]（高さ 200m 比）。コースから 12m 以内は走路として平滑化し勾配 25°以下。CANYON=谷底の走路、DUNES=正弦＋ノイズの砂丘、MESA=段丘
- `float MaxSlopeDegAlong(float[,] h, float terrainSizeM, float heightM, Vector3[] coursePointsNormalized)`
- `float[,] SandMask(int resolution, int kind, int seed)`：[0,1]、DUNES はほぼ全域 1、CANYON は谷底の一部、MESA は境目の帯
- `Vector3[] CoursePath.Sample(Vector3[] controlPoints, float spacing)`（centripetal Catmull-Rom、端点を含む等間隔）、`float CoursePath.Length(Vector3[] points)`

- [ ] **Step 1: 失敗するテスト**
```csharp
[Test] public void Deterministic() => CollectionAssert.AreEqual(HeightmapGen.Generate(129, 0, 7, Course()), HeightmapGen.Generate(129, 0, 7, Course()));
[Test] public void InRange([Values(0,1,2)] int k) { foreach (var v in HeightmapGen.Generate(129, k, 1, Course())) Assert.That(v, Is.InRange(0f, 1f)); }
[Test] public void Rideable([Values(0,1,2)] int k) { var h = HeightmapGen.Generate(513, k, 3, Course()); Assert.LessOrEqual(HeightmapGen.MaxSlopeDegAlong(h, 1000, 200, Course()), 25f); }
[Test] public void DunesMostlySand() { var m = HeightmapGen.SandMask(129, HeightmapGen.KIND_DUNES, 1); Assert.Greater(Mean(m), 0.8f); }
[Test] public void CanyonPartlySand() { float a = Mean(HeightmapGen.SandMask(129, HeightmapGen.KIND_CANYON, 1)); Assert.That(a, Is.InRange(0.05f, 0.4f)); }
[Test] public void EvenSpacing() { var p = CoursePath.Sample(new[]{ V(0,0,0), V(100,0,0), V(100,0,100) }, 10); for (int i = 1; i < p.Length - 1; i++) Assert.AreEqual(10f, Vector3.Distance(p[i-1], p[i]), 0.5f); }
[Test] public void HitsEndpoints() { var c = new[]{ V(0,0,0), V(50,0,0), V(80,0,40) }; var p = CoursePath.Sample(c, 5); Assert.AreEqual(0f, Vector3.Distance(p[0], c[0]), 1e-3); Assert.AreEqual(0f, Vector3.Distance(p[^1], c[2]), 1e-3); }
```
- [ ] **Step 2:** 失敗を確認 → **Step 3:** 実装 → **Step 4:** PASS → **Step 5:** Commit `feat: heightmap, sand mask and course path`

---

### Task 3: RaceMath

**Files:** Create `Scripts/Logic/RaceMath.cs`; Test `RaceMathTests.cs`

**Interfaces（`RaceMath : UdonSharpBehaviour` static）:**
- `STAGE_HARD_LIMIT=330f, AFTER_FIRST_FINISH=60f`、状態 `STATE_LOBBY=0, STATE_COUNTDOWN=1, STATE_RACING=2, STATE_STAGE_RESULT=3, STATE_FINAL_RESULT=4`
- `bool ShouldEndStage(double serverNow, double stageStart, double firstFinishAt)`（未ゴールは −1）
- `int[] ComputeOrder(int[] cpReached, float[] distToNextCp, double[] finishAt, bool[] active)`：完走者は finishAt 昇順が先、未完走は cp 降順 → 距離昇順。挿入ソート
- `int PointsForPlace(int place)`：10,8,6,5,4,3,2,1、以降 0
- `bool CanEnterStage(int raceState)`：LOBBY と STAGE_RESULT のときだけ true
- `bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse)`：−2m 未満、または 60m 超が 3 秒

- [ ] **Step 1: 失敗するテスト**
```csharp
[Test] public void HardLimit() { Assert.IsFalse(RaceMath.ShouldEndStage(1329.9, 1000, -1)); Assert.IsTrue(RaceMath.ShouldEndStage(1330, 1000, -1)); }
[Test] public void AfterFirst() { Assert.IsFalse(RaceMath.ShouldEndStage(1259, 1000, 1200)); Assert.IsTrue(RaceMath.ShouldEndStage(1260, 1000, 1200)); }
[Test] public void LargeServerTime() => Assert.IsTrue(RaceMath.ShouldEndStage(5_000_330.0, 5_000_000.0, -1));
[Test] public void Order() => CollectionAssert.AreEqual(new[]{3,2,1,0}, RaceMath.ComputeOrder(new[]{5,9,9,7}, new[]{10f,50f,20f,0f}, new[]{-1.0,-1.0,-1.0,300.0}, new[]{true,true,true,true}));
[Test] public void OrderSkipsInactive() => CollectionAssert.AreEqual(new[]{1}, RaceMath.ComputeOrder(new[]{3,1}, new[]{0f,0f}, new[]{-1.0,-1.0}, new[]{false,true}));
[Test] public void Points() { Assert.AreEqual(10, RaceMath.PointsForPlace(0)); Assert.AreEqual(1, RaceMath.PointsForPlace(7)); Assert.AreEqual(0, RaceMath.PointsForPlace(8)); }
[Test] public void LateJoin() { Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_LOBBY)); Assert.IsFalse(RaceMath.CanEnterStage(RaceMath.STATE_RACING)); Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_STAGE_RESULT)); }
[Test] public void Reset() { Assert.IsTrue(RaceMath.NeedsReset(-2.1f, 0, 0)); Assert.IsFalse(RaceMath.NeedsReset(0, 70, 2.9f)); Assert.IsTrue(RaceMath.NeedsReset(0, 70, 3f)); }
```
- [ ] **Step 2〜5:** 失敗確認 → 実装 → PASS → Commit `feat: race ranking, scoring and stage rules`

---

### Task 4: AbilityMath ＋ RevolverMath ＋ SpeedMod

**Files:** Create `Scripts/Logic/AbilityMath.cs`, `RevolverMath.cs`, `SpeedMod.cs`; Test `AbilityMathTests.cs`, `RevolverMathTests.cs`, `SpeedModTests.cs`

**Interfaces:**
- `AbilityMath`：`ABILITY_DUST=0, LASSO=1, WALL=2, BALL=3`（Phase 2: 4 恐竜, 5 爆弾, 6 蜃気楼）／`int LevelAfterRelic(int lv)`（上限 3）／`float Cooldown(int a)`：18/22/25/15／`DustRadius(lv)` 4/6/8、`DustDuration(lv)` 4/6/8（中の相手 ×0.8）／`LassoRange(lv)` 25/35/45（自分 +4 m/s 2 秒、相手 ×0.7 1 秒）／`WallCount(lv)` 1/2/3（幅 6m、前方 20m、6 秒）／`BallCount(lv)` 1/2/3（命中 ×0.6 1.2 秒、バウンド 3 回、カーブ = ロール角 × 0.5°/m）／`int RelicArbiter(int holder, int claimant)`
- `RevolverMath`：`AMMO_PER_STAGE=6`、`AIM_FACTOR=0.9f`、`MISS_FACTOR=0.8f, MISS_SECONDS=3f`、`HIT_FACTOR=0.6f, HIT_SECONDS=1f`、`HITS_TO_DROP=3`／`int AmmoAfterShot(int ammo)`（0 未満にしない）／`bool CanShoot(int ammo, int raceState)`（RACING かつ ammo>0）／`int RegisterHit(int hitsOnTarget)` → 次の値（3 に達したら 0 に戻す）／`bool DropsRelic(int hitsBefore)`（hitsBefore==2 のとき true）／`int LevelAfterDrop(int lv)`（max(1, lv−1)）／`bool SpawnsDroppedRelic(int lv)`（lv>1）
- `SpeedMod`（固定長 8 スロットの配列で管理）：`float Combined(float[] factors, float[] remaining)`（remaining>0 のものを掛け合わせ、下限 0.4）／`void Add(float[] factors, float[] remaining, float factor, float seconds)`（空き or 最短残りのスロットを上書き）／`void Tick(float[] remaining, float dt)`

- [ ] **Step 1: 失敗するテスト**
```csharp
[Test] public void Level_Caps() { Assert.AreEqual(2, AbilityMath.LevelAfterRelic(1)); Assert.AreEqual(3, AbilityMath.LevelAfterRelic(3)); }
[Test] public void Cooldowns() { Assert.AreEqual(18f, AbilityMath.Cooldown(0)); Assert.AreEqual(15f, AbilityMath.Cooldown(3)); }
[Test] public void Scaling() { Assert.AreEqual(8f, AbilityMath.DustRadius(3)); Assert.AreEqual(35f, AbilityMath.LassoRange(2)); Assert.AreEqual(3, AbilityMath.WallCount(3)); Assert.AreEqual(1, AbilityMath.BallCount(1)); }
[Test] public void Relic_FirstClaimWins() { int h = AbilityMath.RelicArbiter(-1, 42); Assert.AreEqual(42, h); Assert.AreEqual(42, AbilityMath.RelicArbiter(h, 7)); }
[Test] public void Ammo() { Assert.AreEqual(5, RevolverMath.AmmoAfterShot(6)); Assert.AreEqual(0, RevolverMath.AmmoAfterShot(0)); Assert.IsFalse(RevolverMath.CanShoot(0, RaceMath.STATE_RACING)); Assert.IsFalse(RevolverMath.CanShoot(6, RaceMath.STATE_COUNTDOWN)); }
[Test] public void ThirdHitDrops() { Assert.IsFalse(RevolverMath.DropsRelic(0)); Assert.IsFalse(RevolverMath.DropsRelic(1)); Assert.IsTrue(RevolverMath.DropsRelic(2)); Assert.AreEqual(0, RevolverMath.RegisterHit(2)); Assert.AreEqual(2, RevolverMath.RegisterHit(1)); }
[Test] public void DropLevel() { Assert.AreEqual(2, RevolverMath.LevelAfterDrop(3)); Assert.AreEqual(1, RevolverMath.LevelAfterDrop(1)); Assert.IsFalse(RevolverMath.SpawnsDroppedRelic(1)); Assert.IsTrue(RevolverMath.SpawnsDroppedRelic(2)); }
[Test] public void SpeedMod_StacksWithFloor() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 0.6f, 1); SpeedMod.Add(f, r, 0.8f, 3); Assert.AreEqual(0.48f, SpeedMod.Combined(f, r), 1e-4); SpeedMod.Add(f, r, 0.5f, 2); Assert.AreEqual(0.4f, SpeedMod.Combined(f, r), 1e-4); }
[Test] public void SpeedMod_Expires() { var f = new float[8]; var r = new float[8]; SpeedMod.Add(f, r, 0.6f, 1); SpeedMod.Tick(r, 1.01f); Assert.AreEqual(1f, SpeedMod.Combined(f, r), 1e-4); }
[Test] public void SpeedMod_FullSlotsOverwriteShortest() { var f = new float[8]; var r = new float[8]; for (int i = 0; i < 8; i++) SpeedMod.Add(f, r, 0.99f, 10 + i); SpeedMod.Add(f, r, 0.5f, 5); Assert.AreEqual(5f, r.Min()); }
```
- [ ] **Step 2〜5:** 失敗確認 → 実装 → PASS → Commit `feat: abilities, revolver and speed modifier rules`

---

### Task 5: DirectorMath（中継カメラの切り替え）

**Files:** Create `Scripts/Logic/DirectorMath.cs`; Test `DirectorMathTests.cs`

**Interfaces:** `MIN_SHOT=5f, MAX_SHOT=15f, BATTLE_GAP=8f, HIT_SHOW=3f`／`int ChooseTarget(int current, float shotTime, int[] order, float[] gapToAhead, int lastHitSlot, float sinceHit)`：shotTime<MIN_SHOT なら current。sinceHit<HIT_SHOW かつ lastHitSlot≥0 ならそのスロット。2 頭の差が BATTLE_GAP m 以内の組があれば最上位の組の後ろ側。shotTime≥MAX_SHOT なら current 以外を優先。どれも無ければ先頭（order[0]）。order 空なら −1。

- [ ] **Step 1: 失敗するテスト**
```csharp
[Test] public void HoldsMinShot() => Assert.AreEqual(2, DirectorMath.ChooseTarget(2, 4.9f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
[Test] public void ShowsHit() => Assert.AreEqual(1, DirectorMath.ChooseTarget(0, 6f, new[]{0,1,2}, new[]{0f,50f,50f}, 1, 1f));
[Test] public void PrefersBattle() => Assert.AreEqual(2, DirectorMath.ChooseTarget(0, 6f, new[]{0,1,2}, new[]{0f,50f,3f}, -1, 99));
[Test] public void DefaultsToLeader() => Assert.AreEqual(0, DirectorMath.ChooseTarget(1, 6f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
[Test] public void ForcesChangeAtMax() => Assert.AreNotEqual(0, DirectorMath.ChooseTarget(0, 15f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
[Test] public void Empty() => Assert.AreEqual(-1, DirectorMath.ChooseTarget(0, 6f, new int[0], new float[0], -1, 99));
```
- [ ] **Step 2〜5:** 失敗確認 → 実装 → PASS → Commit `feat: spectator director rules`

---

### Task 6: MountController ＋ 入力 ＋ 見た目（馬・ラクダ・骨の馬）

**Files:** Create `Runtime/MountController.cs`, `MountInputVR.cs`, `MountVisual.cs`, `SandZone.cs`; Create `docs/PLAYTEST.md`（この項目から）

**Interfaces:**
- Consumes: `MountMath.*`, `SpeedMod.*`
- Produces（`MountController`, Manual sync）:
  - 同期: `[UdonSynced] Vector3 syncPos; float syncYaw; float syncSpeed; int syncGait; int syncMount; int syncCoat; int syncCp; float syncDistToCp; double syncFinishAt`（10Hz）
  - `public int slotIndex`、`public void AssignRider(int playerId, int mount, int coat)`（coat: 0〜5 毛色, 6 骨の馬）、`public void TeleportTo(Vector3 pos, float yaw)`、`public void AddSpeedMod(float factor, float seconds)`、`public void ApplyKnock(Vector3 impulse, float stun)`、`public void ApplyPull(Vector3 target, float bonus, float seconds)`、`public bool InputLocked`、`public float Stamina`、`public bool OnSand`
- 方針: レイキャスト接地・坂角度→`MountMath`→`transform` 直接更新、空中は重力 20 m/s²。座席 `VRCStation`（Immobilize、退出時はレース中なら再着席）。非オーナーは 0.1s 遅延補間＋最大 0.3s 外挿。座席アンカーのピッチは `SmoothAngle`。砂地は `SandZone`（トリガー）と Terrain の砂レイヤー比率（Task 11 が `SandMask` から塗る）で判定。
- `MountVisual`：`Horse.fbx`/`Camel.fbx`/`SkeletonHorse.fbx` の子を `syncMount/syncCoat` で切替、毛色は `HorseMaterialBuilder` のマテリアル、Animator の歩様パラメータと `speed = 実速度 / 歩様の基準速度`、クロスフェード 0.2s。スタミナは息づかい音（低スタミナで荒く）・汗パーティクル・耳を伏せる（Phase 1 は音と耳のみで可）。
- [ ] **Step 1:** 実装
- [ ] **Step 2:** `dotnet test` が通ることを確認（ロジック非破壊）
- [ ] **Step 3:** PLAYTEST.md：Unity Console エラー 0／デスクトップで乗って走る・曲がる・跳ぶ／疾走でスタミナが減り尽きると駆歩まで（息づかいが荒くなる）／坂で速度が変わる／崖から落ちて硬直／砂地で馬だけ遅い／ラクダは曲がりにくく坂に弱い／骨の馬・毛色が他人からも同じに見える／VR の手綱操作／VR で降車しても再着席／2 クライアントで相手がガクつかない
- [ ] **Step 4:** Commit `feat: mount controller, VR reins, visuals and sand`

---

### Task 7: Checkpoint・コース外リセット

**Files:** Create `Runtime/Checkpoint.cs`; Modify `MountController.cs`
**Interfaces:** `Checkpoint`：`index, stageIndex, isGoal`、ローカル騎手の `OnTriggerEnter` で `RaceManager.LocalReachedCheckpoint(stage, index)`。MountController は最後の CP の位置・向きを保持し `RaceMath.NeedsReset` で `TeleportTo`。
- [ ] Step 1: 実装 → Step 2: `dotnet test` → Step 3: PLAYTEST：逆走・CP 飛ばしで順位が進まない／崖下・地形下で 3 秒以内に戻る → Step 4: Commit `feat: checkpoints and off-course reset`

---

### Task 8: RaceManager ＋ 世界観ロビー ＋ 最小補助 HUD ＋ 全員ボイス

**Files:** Create `Runtime/RaceManager.cs`, `Lobby/EntryLedger.cs`, `PosterCard.cs`, `MountPicker.cs`, `StartBell.cs`, `PracticeArea.cs`, `UI/PocketWatch.cs`, `RankBoard.cs`, `MinimalHud.cs`

**Interfaces:**
- Consumes: `RaceMath.*`, `MountController.*`, `AbilityCaster.ResetForStage()`, `RevolverHolder.Refill()`
- Produces（`RaceManager`, Manual, Owner=マスター）: 同期 `int state, stage; double stageStartServer, firstFinishServer; int[8] slotPlayerId, slotMount, slotCoat, slotAbility, slotTotalPoints`／`LocalReachedCheckpoint(int stage, int index)`／`RequestEntry(int mount, int coat, int ability)`／`RequestStart()`（マスターのみ）／`GetRankOf(int playerId) → int`／`GetRemainingSeconds() → float`／`OnOwnershipTransferred` で続行／`OnPlayerLeft` で枠を inactive／`OnPlayerJoined` と開始時に全員の `SetVoiceDistanceFar(10000)` `SetVoiceDistanceNear(0)` `SetVoiceLowpass(false)`
- 進行: LOBBY →(鐘)→ COUNTDOWN（Task 10 の Starter が旗＋空砲 3 発、InputLocked）→ RACING（毎秒 `ShouldEndStage`）→ STAGE_RESULT 10s（ポイント、遺物復活、能力 Lv・弾リセット、途中参加者エントリー）→ 次 or FINAL_RESULT → 20s 後 LOBBY
- ロビー（spec 5.14）: `PosterCard`（手配書を手に取る＝乗り物・能力を選ぶ。カード文言は 砂塵／投げ縄／岩壁／鉄球／馬／ラクダ）、`MountPicker`（柵の馬に近づいて Interact で毛色・骨の馬を選ぶ）、`EntryLedger`（台帳にサイン＝`RequestEntry`。台帳のページにエントリー者名を手書き風フォントで表示）、`StartBell`（マスターが鳴らす＝`RequestStart`）、`PracticeArea`（練習用の馬 2 頭・カカシ、能力を自由に切替・クールダウン 1/3、リボルバー無限）
- 補助 HUD: `PocketWatch`（残り時間。VR は左手首、デスクトップは画面下）、`RankBoard`（CP の黒板に順位）、`MinimalHud`（画面端の古い紙風：順位・残り時間のみ、オプションで非表示）
- [ ] Step 1: 実装 → Step 2: `dotnet test` → Step 3: PLAYTEST：台帳サイン〜鐘で開始／3 ステージ通しで総合結果／1 位ゴール後 60 秒で終了／誰もゴールしなくても 5:30 で終了／途中参加は次ステージから／マスター退出でも続行／抜けた人の枠が消える／ワールドの端同士でも声が聞こえる／補助 HUD を消せる → Step 4: Commit `feat: race manager, diegetic lobby, minimal HUD and global voice`

---

### Task 9: 能力 4 種 ＋ 遺物 ＋ リボルバー

**Files:** Create `Abilities/AbilityCaster.cs`, `DustCloud.cs`, `LassoEffect.cs`, `RockWall.cs`, `IronBall.cs`, `RelicPickup.cs`, `CharmPouch.cs`, `Revolver/RevolverHolder.cs`

**Interfaces:**
- Consumes: `AbilityMath.*`, `RevolverMath.*`, `MountController.AddSpeedMod/ApplyKnock/ApplyPull`
- `AbilityCaster`：`Cast()`、`ResetForStage()`、`int Level`、`void DropRelic()`（Lv−1、`SpawnsDroppedRelic` なら足元に `RelicPickup` を出す）。当たりはローカル判定（SphereCast/OverlapSphere）→ 対象オーナーへイベント、見た目は全員。
- `RelicPickup`：Owner が `RelicArbiter` で先着確定、`[UdonSynced] int holder`。見た目は `Packs/Gems`（色違いを遺物 1 種として使用）
- `CharmPouch`：腰のお守り袋。使用可能で発光、Lv の数だけ刻印が光る（クールダウンの見える化）
- `RevolverHolder`：`Refill()`、構え（VR：グリップで手に持つ／デスクトップ：右クリック）中は `AddSpeedMod(0.9, 0.2)` を毎フレーム更新、発射で `AmmoAfterShot`・撃鉄とシリンダー 60° 回転・銃声と煙、レイキャスト命中→相手へ `AddSpeedMod(HIT_FACTOR, HIT_SECONDS)`＋よろけ、相手ごとの命中数（ローカル配列）で `RegisterHit`/`DropsRelic` → 相手のオーナーに `DropRelic` を要求（相手側で二重処理しないよう、要求に「何発目か」を付けて相手オーナーが最終判定）、外れ→自分に `AddSpeedMod(MISS_FACTOR, MISS_SECONDS)`。残弾はシリンダーの弾の見た目そのもの（`Revolver.fbx` の Bullet1〜6 を表示/非表示）
- [ ] Step 1〜3: `dotnet test` の既存 PASS を確認しつつ実装
- [ ] Step 4: PLAYTEST：4 能力が 2 クライアント間で効く／お守り袋の光でクールダウンがわかる／遺物で Lv が上がる／同時に取っても 1 人／ステージ開始で Lv1 と弾 6 発／外すと 3 秒遅くなる／命中でよろける／同じ相手に 3 発で遺物が落ち、拾える／被弾で視点が回らない
- [ ] Step 5: Commit `feat: abilities, relics and revolver`

---

### Task 10: 観戦 ＋ 演出

**Files:** Create `Show/SpectatorDirector.cs`, `NoticeBoardDisplay.cs`, `Starter.cs`, `GoalPresenter.cs`, `Podium.cs`, `TownCrier.cs`

**Interfaces:**
- Consumes: `DirectorMath.ChooseTarget`, `RaceManager` の状態・順位、`MountController` の位置
- `SpectatorDirector`：カメラ 1 台（RenderTexture 960×540）、毎 0.5 秒 `ChooseTarget`、追従はなめらかに（後方斜め上）。参加者のクライアントでは描画しない（ロビーにいる人だけ）。掲示板下のボタン（木札）で「○位を追う」個人切替
- `NoticeBoardDisplay`：`NoticeBoard.fbx` の `NoticeBoard_Panel` に、中継（RenderTexture）／順位表（電報風）／エントリー一覧のいずれかを表示
- `Starter`：スタートラインの人物（プリミティブ可）が旗を振り、空砲 3 発（音）→ 号砲で `InputLocked=false`。馬はカウント中 `Stamp` アニメ
- `GoalPresenter`：ゴールゲート通過で紙吹雪・着順の木札が跳ねる、1 位で教会の鐘（全員）
- `Podium`：総合結果で上位 3 人を `Podium.fbx` へテレポート、花火
- `TownCrier`：ステージ結果を新聞売りの呼び声（効果音）＋掲示板に電報
- [ ] Step 1: 実装 → Step 2: `dotnet test` → Step 3: PLAYTEST：ロビーの掲示板で先頭・競り合い・命中場面が切り替わる（5〜15 秒）／木札で追う人を変えられる（自分だけ）／旗と空砲 3 発でスタート／ゴール演出と鐘／表彰台と花火 → Step 4: Commit `feat: spectator camera and race presentation`

---

### Task 11: エディタ生成ツール（地形・コース・素材配置・シーン組み立て）

**Files:** Create `Editor/Gen/StageDefinitions.cs`, `TerrainBuilder.cs`, `CourseBuilder.cs`, `DressingBuilder.cs`, `Editor/SceneAssembler.cs`

**Interfaces:**
- Consumes: `HeightmapGen.*`, `CoursePath.*`, 全 Runtime、`HorseMaterialBuilder.Create()`、`Models/` の素材
- `StageDefinitions`：3 ステージの kind（CANYON/DUNES/MESA）・seed・中心（(-1200,0,0)/(0,0,0)/(1200,0,0)）・コース制御点（約 2.5km）・遺物 4 か所・徒歩用の近道は Phase 2
- `TerrainBuilder`：Terrain×3（1000m、高さ 200m、解像度 513）。TerrainLayer：荒野＝赤茶の土・岩、砂漠＝砂（`SandMask` で砂レイヤーを塗る）
- `CourseBuilder`：CP（80m 間隔、黒板付き）、遺物、スタートライン（8 頭）、ゴールゲート、コース外リセット
- `DressingBuilder`（素材配置）:
  - ロビー＝`Packs/WesternTown`（地形に埋めて配置、土台の底が地面下になるよう沈める）＋受付カウンター・台帳・手配書の壁・柵の馬・鐘（`Packs/FarmProps` の机・樽・木箱、`Props/NoticeBoard`×3）＋練習場（坂・ジャンプ台・崖下り、`Packs/HayBales`・カカシ）＋たまり場（`Packs/Campfire`、`Packs/WoodLogs` のベンチ）
  - 荒野ステージ：`Packs/Cliffs`（峡谷の壁）、`Packs/DesertRocks`・`Packs/RockCollection`（選んだ 20〜40 個）、`Packs/DesertProps`、`Props/DeadTree`、`Props/AcaciaGrove`
  - 砂漠ステージ：`Packs/Cacti`、`Props/CactusGroup`、`Props/Tent`（オアシス）、`Packs/DesertProps` の骨
  - 境目ステージ：`Props/MesaRock`・`ThirdParty/MesaRockB`（背景、見えない壁）、ゴールの町＝`Packs/WesternTown` の複製 or `Buildings/*`、`Props/Podium`
  - 背景専用：`Props/Stable`、メサ類は周囲に BoxCollider の見えない壁
  - すべてのマテリアルを `HorseRace/Toon` に差し替え（テクスチャは引き継ぎ、`_Flatten` 0.7）
  - 素材が見つからなければプリミティブで代用し警告
- `SceneAssembler`：メニュー `HorseRace/Build World`（`HorseRace_Generated` を作り直す：Terrain・コース・ドレッシング・ロビー・RaceManager・MountController×8・練習用の馬×2・SpectatorDirector・VRCSceneDescriptor・ライト）、`HorseRace/Validate`（全長 2.2〜2.8km、勾配 25°以下、CP 数、砂地の割合、素材の欠け）
- [ ] Step 1: 実装 → Step 2: `dotnet test` → Step 3: PLAYTEST：Build World がエラーなく完了／Validate が警告なし／各ステージを駆歩で 4〜6 分／町の土台が地面から浮いていない／厩舎・メサに近づけない → Step 4: Commit `feat: editor tools for terrain, course, dressing and scene`

---

### Task 12: セットアップ手順書 ＋ 最終チェックリスト

**Files:** Create `docs/SETUP.md`; Modify `docs/PLAYTEST.md`, `README.md`
- [ ] Step 1: SETUP.md をクリック単位で（VCC → World プロジェクト（Unity 2022.3）→ UdonSharp → `Assets/HorseRace` をコピー → `HorseRace/Create Horse Materials` → 新規シーン → `HorseRace/Build World` → `HorseRace/Validate` → Build & Test（2 クライアント）→ 非公開アップロード）
- [ ] Step 2: PLAYTEST.md に Review Focus 1〜5 の項目があることを確認
- [ ] Step 3: `dotnet test tests/HorseRace.Tests` 全 PASS
- [ ] Step 4: Commit `docs: setup guide and playtest checklist`

---

## Phase 2（別プラン）
恐竜・爆弾・蜃気楼、徒歩での参加（縛りプレイ）、町の人・観客、効果音・BGM の差し替え、数値調整。Phase 1 のプレイテスト結果を受けて書く。
