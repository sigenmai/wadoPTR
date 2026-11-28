using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ロビーの柵と外を隠す木の帯（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。座標はロビー中心からの相対（m）。
    ///   柵：FENCE_RECT（町の外形 + 8m。広場・たまり場・掲示板・練習場はすべて町の中）を一周する木の柵（FarmProps_03 を高さ ×1.6、柱の間隔 ≤ 2.74m）。
    ///       練習場も柵の中なので出入口（Gates）は無し＝外へ出る切れ目は無い（必要になったら Gates に足す）。
    ///   外：柵のすぐ外の地面はゆるく盛り上がり（OUT_SLOPE、OUT_RISE_MAX まで）、その先は低い外周の尾根と、その上の岩の冠（RimCrown、切れ目あり）。
    ///       北（町の奥の岩山の裏）だけは高い尾根のまま（ステージの地形を隠す）。柵の外 3〜10m に木の帯（3 列・南は 2 列、互い違い）。
    ///       南と南東に 2 つの「見晴らしの切れ目」（VistaGaps）：尾根・木・岩を置かず、広場から谷越しに遠くの山並み（Backdrop）が見える。
    ///   → 柵の中の目の高さ（1.6〜3.2m）から水平〜少し下向きに見ると、見えるのは柵・木・尾根・空、切れ目からは遠くの景色だけ（VisibilityRay でテスト）。
    ///      ステージの地形は尾根の下か、霧でほぼ隠れる距離（StagesHiddenFromLobby でテスト）。
    /// </summary>
    public static class LobbyLayout
    {
        /// <summary>町の外形（ロビー中心からの相対。Blender で FBX を測った値 81.17m × 164.38m を、南端 z = 25・東西の中心 x = 0 に置いたとき）。
        /// 実際の外形は LobbyBuilder が測り、柵が足りなければ広げる。</summary>
        public static readonly Vector4 TOWN_NOMINAL_RECT = new Vector4(-40.59f, 25f, 40.59f, 189.38f);
        public const float TOWN_MARGIN_M = 8f;
        /// <summary>
        /// 柵の長方形（minX, minZ, maxX, maxZ）。ロビー全体 = 西部の町（WesternTown、Blender で測った外形 81.2m × 164.4m、x ±40.6・z 25〜189.4）
        /// ＋ 四方に TOWN_MARGIN_M（8m）。約 97m × 180m（約 1.75 万 m²）。広場・柵の馬・たまり場・練習場はすべて町の中の空き地にある。
        /// 実際の町が入らなければ FitFenceRect で広げる。
        /// </summary>
        public static readonly Vector4 FENCE_RECT = new Vector4(TOWN_NOMINAL_RECT.x - TOWN_MARGIN_M, TOWN_NOMINAL_RECT.y - TOWN_MARGIN_M,
            TOWN_NOMINAL_RECT.z + TOWN_MARGIN_M, TOWN_NOMINAL_RECT.w + TOWN_MARGIN_M);   // (-48.59, 17, 48.59, 197.38)
        /// <summary>柵から地面（Terrain）の端までの幅：柵の外の盛り上がり＋木の帯＋外周の尾根（RIM_WIDTH_M）が入る。</summary>
        public const float GROUND_BORDER_M = 38f;
        /// <summary>ロビーの地面（Terrain、長方形）の範囲 = 柵を GROUND_BORDER_M 広げたもの。</summary>
        public static readonly Vector4 GROUND_RECT = new Vector4(FENCE_RECT.x - GROUND_BORDER_M, FENCE_RECT.y - GROUND_BORDER_M,
            FENCE_RECT.z + GROUND_BORDER_M, FENCE_RECT.w + GROUND_BORDER_M);
        /// <summary>
        /// art-r5-10：ロビーの Terrain（LobbyGround）の範囲 = GROUND_RECT を TERRAIN_EXTEND_M 広げたもの。見える土手と尾根を細かいロビーの高さマップで描く
        /// （前は外の地面の 10m のセルで段々に見え、南東の角は虚空の上に浮いて見えた）。尾根の形（RimEdgeDistance）は GROUND_RECT のまま、その外は尾根の頂の高さが続く。
        /// </summary>
        public const float TERRAIN_EXTEND_M = 200f;
        public static readonly Vector4 TERRAIN_RECT = new Vector4(GROUND_RECT.x - TERRAIN_EXTEND_M, GROUND_RECT.y - TERRAIN_EXTEND_M,
            GROUND_RECT.z + TERRAIN_EXTEND_M, GROUND_RECT.w + TERRAIN_EXTEND_M);
        /// <summary>art-r5-10：町へ近づく道の通路（中心線から APPROACH_CORRIDOR_HALF_M は町の高さ、その外 APPROACH_CORRIDOR_FADE_M でもとの地面へ）。</summary>
        public const float APPROACH_CORRIDOR_HALF_M = 24f, APPROACH_CORRIDOR_FADE_M = 30f;

        /// <summary>art-r5-10：点 rel（ロビー中心からの相対）を町の高さへ下げる重み（柵の南の外、近づく道の通路で 1）。柵の中は 0（町の地面はそのまま）。</summary>
        public static float ApproachCorridorWeight(Vector3 rel)
        {
            if (rel.z >= FENCE_RECT.y) return 0f;
            float d = WorldLayout.DistToPolylineXZ(rel, StreetAndApproach());
            float t = Mathf.Clamp01((d - APPROACH_CORRIDOR_HALF_M) / APPROACH_CORRIDOR_FADE_M);
            return 1f - t * t * (3f - 2f * t);
        }
        /// <summary>
        /// 町の奥の見えない壁（z、町の南端 25 のとき）。町の模型には z ≈ 140〜141 に東西いっぱいの木の柵（高さ約 2m）があり、
        /// その北（教会の裏の空き地・岩山）は背景専用：柵のすぐ北に見えない壁を置いて、柵の両端の隙間と岩山へ行けないようにする。
        /// </summary>
        public const float TOWN_BACK_Z = 141.5f;

        // ---- 町の中の配置（ロビー中心からの相対 m、町の南端 z = 25・通りの高さ y = 0）。Blender で測った町の空き地（建物・岩・サボテン・起伏の無い所）----
        // 広場＝町の南の入口（x -29〜-7、z 25〜48 の空き地）
        // art-r2-12：スポーンは大通りの中心線（x ≈ 0.4）の 3m 西、大通りの軸（北）をまっすぐ向く。正面 18〜25m にスタート台と次のレースの時計
        public static readonly Vector3 SPAWN = new Vector3(-2.5f, 0f, 30f);
        /// <summary>スポーンの向き（北から東へ °）：大通りの軸（北 = 0°）との差は ±10° 以内。</summary>
        public const float SPAWN_YAW = 2f;
        /// <summary>サルーン（大通りの西側の 2 階建て、WesternTown_047）。スポーンから正面 ±30° に見える。</summary>
        public const string SALOON_PART = "WesternTown_047_Building";
        public static readonly Vector3 LEDGER_DESK = new Vector3(-13.6f, 0f, 40.5f);    // 受付カウンター（南＝スポーンを向く。大通りへの視線の左）
        public static readonly Vector3 WANTED_WALL = new Vector3(-26f, 0f, 36.5f);      // 手配書の壁（掲示板 ×1.2、南を向く。視線の外の西）
        public static readonly Vector3 START_BELL = new Vector3(-1.8f, 0f, 51f);        // スタート台（3m 四方）と号砲・次のレースの時計（スポーンの正面 21m、大通りの上）
        public static readonly Vector3 HUD_LEVER = new Vector3(-8f, 0f, 29f);           // 補助表示のレバー
        /// <summary>町の大時計（ui-r2-13）：大通りの軸の上、スタート台の 11m 奥（スポーンの正面 32m）。掲示板 ×1.4（幅 6.3m）を南（スポーン）へ向ける。
        /// 通りの両脇（x -4.6 と 6.4 の建物）とは 2m 以上あける。</summary>
        /// art-r5-16：大通りの軸から外して東寄り（スポーンの視線から約 6m）、スポーンへ 25° 振る・×1.0（前は軸の上に ×1.4 で、通りの奥をふさぐ暗い板に見えた）。
        public static readonly Vector3 TOWN_CLOCK = new Vector3(3.6f, 0f, 58f);
        public const float TOWN_CLOCK_SCALE = 1.0f, TOWN_CLOCK_YAW = 25f;
        /// <summary>操作の看板（ui-r2-07）：練習場の看板の東隣。</summary>
        public static readonly Vector3 PRACTICE_CONTROLS = new Vector3(28.4f, 0f, 88f);
        // 西の空き地（西の建物の列 x ≤ -14.6 と町の西の端 x -36 の間）
        public static readonly Vector3 PARKING_FIRST = new Vector3(-33.5f, 0f, 37f);   // 乗り物の待機場所 ×8（北へ PARKING_STEP ずつ、東を向く）
        public const float PARKING_STEP = 2.3f;
        public static readonly Vector3 CORRAL_FIRST = new Vector3(-21f, 0f, 47.5f);    // 柵の馬 ×7（北へ CORRAL_STEP ずつ、東を向く）
        public const float CORRAL_STEP = 2.2f;
        public static readonly Vector3 HANGOUT_FIRE = new Vector3(-25f, 0f, 72.5f);    // たまり場の焚き火（ベンチは半径 3.2m）
        public static readonly Vector3 NOTICE_BOARDS = new Vector3(-25f, 0f, 80f);     // 掲示板 3 枚（東西に 6m ずつ、南＝焚き火を向く）
        public static readonly Vector3 LAST_RACE_BOARD = new Vector3(-33.5f, 0f, 72.5f); // 前回レースの黒板（焚き火から西を見て読む）
        public static readonly Vector3 TOWN_CRIER = new Vector3(-17.5f, 0f, 71.5f);    // 新聞売り（西＝焚き火を向く）
        /// <summary>「あなたの戦績」の掲示板（system-r3-15、掲示板 ×0.6）：前回レースの黒板の 6m 南、同じく焚き火から西を見て読む。</summary>
        public static readonly Vector3 RECORD_BOARD = new Vector3(-33.5f, 0f, 66.5f);
        public static readonly Vector3 SPARE_POOL = new Vector3(-32.5f, 0f, 125f);     // 予備の遺物 ×4（北へ 4m ずつ、普段は出ていない）
        /// <summary>厩舎（背景専用）：町の奥の柵（z 140）の北、見えない壁の向こう。14m × 6m、正面は南。</summary>
        public static readonly Vector3 STABLE = new Vector3(-29f, 0f, 148.5f);

        /// <summary>
        /// 練習場：町の東の線路の東（北東の空き地、給水塔のまわり）。長方形（minX, minZ, maxX, maxZ）＝トリガーの箱。
        /// 南の端に練習用の馬の杭（広場から線路沿いに北へ来て最初に着く所）、ジャンプ台（北へ跳ぶ）、カカシ、北の端に小さな丘。
        /// </summary>
        public static readonly Vector4 PRACTICE_RECT = new Vector4(19f, 87f, 36f, 140f);
        public static Vector3 PRACTICE_CENTER => new Vector3((PRACTICE_RECT.x + PRACTICE_RECT.z) * 0.5f, 0f, (PRACTICE_RECT.y + PRACTICE_RECT.w) * 0.5f);
        /// <summary>練習用の馬に乗ったまま練習場の外へ出たと見なす距離（中心から、箱の角 ≈ 27.8m より少し大きく）。</summary>
        public static float PracticeReleaseRadius => 0.5f * Mathf.Sqrt((PRACTICE_RECT.z - PRACTICE_RECT.x) * (PRACTICE_RECT.z - PRACTICE_RECT.x)
            + (PRACTICE_RECT.w - PRACTICE_RECT.y) * (PRACTICE_RECT.w - PRACTICE_RECT.y)) + 3f;
        public static readonly Vector3 PRACTICE_POST_FIRST = new Vector3(23.5f, 0f, 90.5f);   // 杭 ×2（北へ 4.5m、馬はその 2.5m 東）
        public static readonly Vector3 PRACTICE_JUMP = new Vector3(29f, 0f, 101f);           // ジャンプ台（6m × 6.2m、13°（砂漠の踏み切りと同じ）、北へ上る。着地は z 104〜117）
        public static readonly Vector3 PRACTICE_BUMP = new Vector3(25.5f, 0f, 132f);         // 小さな丘（7m 四方を 45° 回した低い台、上面 0.45m）
        public static readonly Vector3 PRACTICE_DUMMY_1 = new Vector3(23.5f, 0f, 111f);
        public static readonly Vector3 PRACTICE_DUMMY_2 = new Vector3(33f, 0f, 129f);
        public static readonly Vector3 PRACTICE_RELIC = new Vector3(33f, 0f, 108f);
        /// <summary>往復タイム（system-r1-18）：南の門（杭の北）・北の門（丘の北）の z と、記録の掲示板。</summary>
        public const float PRACTICE_GATE_SOUTH_Z = 94f;
        public const float PRACTICE_GATE_NORTH_Z = 136.5f;
        public static readonly Vector3 PRACTICE_LAP_BOARD = new Vector3(31.5f, 0f, 90.5f);

        /// <summary>
        /// ロビーの手順の札（ui-r3-11）：一 手配書の壁・二 柵の馬・三 受付・四 スタート台の、それぞれ 2〜4m 手前（スポーン側）の空いた地面。
        /// 札はスポーンのほうを向く（立てた丸太の杭に手配書の紙 0.6 × 0.45m）。
        /// </summary>
        public static readonly Vector3[] StepSigns =
        {
            new Vector3(-23.0f, 0f, 34.3f),   // 一 手配書を取る（壁の東の端の手前）
            new Vector3(-18.3f, 0f, 46.4f),   // 二 馬を選ぶ（柵の馬の東）
            new Vector3(-14.8f, 0f, 37.6f),   // 三 台帳にサイン（受付の机の南西）
            new Vector3(-4.0f, 0f, 47.6f),    // 四 スタート台へ（台の南西、号砲の札の横）
        };
        public const float STEP_SIGN_RADIUS = 0.5f;
        /// <summary>4 手順の表（掲示板 ×0.6）：スポーンの右手前（正面 4m は低い起伏・大通りの眺めなので外す）。</summary>
        public static readonly Vector3 STEPS_BOARD = new Vector3(2.6f, 0f, 30.4f);

        /// <summary>札の向き（北から東へ °）：スポーンから札へ向かう向き＝スポーン側から読める。</summary>
        public static float FaceFromSpawn(Vector3 p) => WorldLayout.YawDeg(new Vector3(p.x - SPAWN.x, 0f, p.z - SPAWN.z).normalized);

        /// <summary>ロビーの飾り（DressingPlan.ForLobby）を置かない円（x, _, z, 半径）：南の入口の前（町の南の辺と柵の間＝広場への入口）。
        /// 遊びに使う物はすべて町の中（飾りは町の外形の外だけ）なので、それ以外の円は要らない。</summary>
        public static List<Vector4> LobbyDressingExclusions()
        {
            var l = new List<Vector4> { new Vector4(0f, 0f, TOWN_NOMINAL_RECT.y - 4f, 44f) };
            foreach (var q in LotFill()) l.Add(new Vector4(q.position.x, 0f, q.position.z, q.radius + 1f));   // art-r5-11：埋めた敷地
            return l;
        }

        /// <summary>遊びに使う物の位置と半径（テストで「柵の中・町の建物の外」を確かめる）。</summary>
        public static List<KeyValuePair<string, Vector4>> FunctionalSpots()
        {
            var l = new List<KeyValuePair<string, Vector4>>();
            void Add(string n, Vector3 p, float r) => l.Add(new KeyValuePair<string, Vector4>(n, new Vector4(p.x, 0f, p.z, r)));
            Add("Spawn", SPAWN, 1f);
            Add("LedgerDesk", LEDGER_DESK, 1.8f);   // FarmProps_05 の机（半径 1.6m）
            for (int i = -1; i <= 1; i++) Add("WantedWall_" + (i + 1), WANTED_WALL + new Vector3(i * 2.1f, 0f, 0f), 1.2f);   // 幅 6.2m・厚さ 0.2m の板
            Add("StartBell", START_BELL, 2.2f);
            Add("HudLever", HUD_LEVER, 0.6f);
            Add("TownClock", TOWN_CLOCK, 2.4f);   // 板の半分の幅 2.25m（×1.0）
            for (int i = 0; i < 8; i++) Add("Parking_" + i, PARKING_FIRST + new Vector3(0f, 0f, i * PARKING_STEP), 1.4f);
            for (int i = 0; i < 7; i++) Add("Picker_" + i, CORRAL_FIRST + new Vector3(0f, 0f, i * CORRAL_STEP), 1.4f);
            Add("CorralFenceS", CORRAL_FIRST + new Vector3(-3.5f, 0f, -1.5f), 0.3f);
            Add("CorralFenceN", CORRAL_FIRST + new Vector3(-3.5f, 0f, 6 * CORRAL_STEP + 0.8f), 0.3f);   // すぐ北にサボテン
            Add("Campfire+Benches", HANGOUT_FIRE, 4f);
            for (int i = -1; i <= 1; i++) Add("NoticeBoard_" + (i + 1), NOTICE_BOARDS + new Vector3(i * 6f, 0f, 0f), 2.3f);
            Add("LastRaceBoard", LAST_RACE_BOARD, 1.5f);
            Add("RecordBoard", RECORD_BOARD, 1.4f);   // system-r3-15
            Add("TownCrier", TOWN_CRIER, 0.6f);
            for (int i = 0; i < 4; i++) Add("SpareRelic_" + i, SPARE_POOL + new Vector3(0f, 0f, -6f + i * 4f), 0.8f);
            for (int i = 0; i < 2; i++)
            {
                Add("PracticePost_" + i, PRACTICE_POST_FIRST + new Vector3(0f, 0f, i * 4.5f), 0.5f);
                Add("PracticeHome_" + i, PRACTICE_POST_FIRST + new Vector3(2.5f, 0f, i * 4.5f), 1.4f);
            }
            Add("PracticeJump", PRACTICE_JUMP, 4.4f);
            Add("PracticeBump", PRACTICE_BUMP, 5f);
            Add("PracticeDummy_1", PRACTICE_DUMMY_1, 0.8f);
            Add("PracticeDummy_2", PRACTICE_DUMMY_2, 0.8f);
            Add("PracticeRelic", PRACTICE_RELIC, 0.8f);
            Add("PracticeLapBoard", PRACTICE_LAP_BOARD, 1.4f);
            Add("PracticeControls", PRACTICE_CONTROLS, 1.9f);   // 掲示板 ×0.8（幅 3.6m）
            for (int i = 0; i < StepSigns.Length; i++) Add("StepSign_" + (i + 1), StepSigns[i], STEP_SIGN_RADIUS);   // ui-r3-11
            Add("StepsBoard", STEPS_BOARD, 1.4f);   // 掲示板 ×0.6（幅 2.7m）
            return l;
        }

        public const float FENCE_PIECE_M = 2.74f;        // FarmProps_03 の長さ（両端に柱）
        public const float FENCE_SCALE_Y = 1.6f;         // 0.98m × 1.6 ≈ 1.57m
        public const float FENCE_HEIGHT_M = 0.98f * FENCE_SCALE_Y;
        /// <summary>art-r4-09：見晴らしの切れ目の中の柵は低く（0.98m、景色を塞がない。当たりの見えない箱は 3m のまま）。</summary>
        public const float FENCE_GAP_SCALE_Y = 1.0f;

        /// <summary>柵の 1 区間（中点 mid、ロビー中心からの相対）の縦の拡大：見晴らしの切れ目の中は低い柵。</summary>
        public static float FenceScaleYAt(Vector3 mid)
        {
            float az = WorldLayout.YawDeg(new Vector3(mid.x - SPAWN.x, 0f, mid.z - SPAWN.z).normalized);
            foreach (var g in VistaGaps) if (Mathf.Abs(Mathf.DeltaAngle(az, g.x)) <= g.y + 2f) return FENCE_GAP_SCALE_Y;
            return FENCE_SCALE_Y;
        }
        // 柵の外の地面：ゆるい盛り上がり（20m で 6m）と低い尾根（4〜12m）。北の辺（町の奥の岩山の裏）は高いまま（12m・15〜30m）
        public const float OUT_SLOPE = 0.3f, OUT_RISE_MAX = 6f;
        public const float RIM_WIDTH_M = 30f, RIM_MIN_M = 4f, RIM_MAX_M = 12f;
        public const float OUT_SLOPE_N = 0.6f, OUT_RISE_MAX_N = 12f, RIM_MIN_N = 15f, RIM_MAX_N = 30f;
        public const float NORTH_BLEND_M = 40f;          // 北の高い尾根へ移る幅（柵の北の辺の 20m 南から）
        public const float GAP_KEEP = 0.15f;
        public const float BERM_VISIBLE_M = 12f;         // 柵の外のこの距離までは盛り上がりの途中の地面（見えてよい）             // 見晴らしの切れ目では盛り上がり・尾根をこの割合に下げる
        public const float BELT_FIRST_M = 3f, BELT_ROW_GAP_M = 3.5f, BELT_SPACING_M = 12.8f;   // art-r5-11：8 → 12.8m（×1.6、まばらな低木の原で遠くのメサが見える）
        public const int BELT_ROWS = 3;
        public const int BELT_ROWS_SOUTH = 2;            // 南（広場の前）は 2 列
        public const int MAX_BELT = 600;

        /// <summary>見晴らしの切れ目：スポーンから見た方位（北から東へ °）と半幅（°）。南と南東。</summary>
        public static readonly Vector2[] VistaGaps = { new Vector2(180f, 11f), new Vector2(138f, 9f) };
        const float GAP_SOFT_DEG = 4f;

        /// <summary>柵の外の点 p（ロビー中心からの相対）が見晴らしの切れ目にどれだけ入っているか（0〜1、端は GAP_SOFT_DEG でなめらか）。柵の中は 0。</summary>
        public static float GapFactor(Vector4 fence, Vector3 p)
        {
            if (DistOutside(fence, p) <= 0f) return 0f;
            float az = WorldLayout.YawDeg(new Vector3(p.x - SPAWN.x, 0f, p.z - SPAWN.z).normalized);
            float best = 0f;
            foreach (var g in VistaGaps)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(az, g.x));
                float t = Mathf.Clamp01((g.y + GAP_SOFT_DEG - d) / GAP_SOFT_DEG);
                best = Mathf.Max(best, t * t * (3f - 2f * t));
            }
            return best;
        }

        /// <summary>北の高い尾根の割合（0〜1）：柵の北の辺の NORTH_BLEND_M/2 南から NORTH_BLEND_M かけて 1 へ。</summary>
        public static float NorthFactor(Vector4 fence, Vector3 p)
        {
            float t = Mathf.Clamp01((p.z - (fence.w - NORTH_BLEND_M * 0.5f)) / NORTH_BLEND_M);
            return t * t * (3f - 2f * t);
        }

        /// <summary>柵の面積（m²）。</summary>
        public static float Area(Vector4 r) => (r.z - r.x) * (r.w - r.y);

        /// <summary>木の帯の最低本数の目安（南は 2 列・切れ目は無しとして、外周 ÷ 間隔 の 8 割）。Validator が使う。</summary>
        public static int MinBelt(Vector4 rect)
        {
            float w = rect.z - rect.x, d = rect.w - rect.y;
            float rows = BELT_ROWS * (w + 2f * d) + BELT_ROWS_SOUTH * w;
            // art-r6-09：まとまり（平均 29m）と空き（平均 45m）の繰り返しなので、帯の約 4 割
            float fill = (BELT_CLUSTER_MIN_M + BELT_CLUSTER_MAX_M) / (BELT_CLUSTER_MIN_M + BELT_CLUSTER_MAX_M + BELT_OPEN_MIN_M + BELT_OPEN_MAX_M);
            return Mathf.FloorToInt(0.7f * fill * (rows - BELT_ROWS_SOUTH * 40f) / BELT_SPACING_M);
        }

        public struct Gate
        {
            public int side;         // 0 = 南（z = minZ）、1 = 東、2 = 北、3 = 西
            public float center;     // その辺に沿った座標（南北の辺は x、東西の辺は z）
            public float halfWidth;
        }

        /// <summary>出入口。練習場も柵の中なので無し（外へ出る切れ目を作らない）。</summary>
        public static readonly Gate[] Gates = new Gate[0];

        /// <summary>柵の区間（各 [a, b]、長さ ≤ maxPiece）。辺ごとに等分し、出入口と重なる区間は除く。南→東→北→西の順に一周。</summary>
        public static List<Vector3[]> FenceSegments(Vector4 rect, Gate[] gates, float maxPiece)
        {
            var r = new List<Vector3[]>();
            Vector3[] c =
            {
                new Vector3(rect.x, 0f, rect.y), new Vector3(rect.z, 0f, rect.y), new Vector3(rect.z, 0f, rect.w), new Vector3(rect.x, 0f, rect.w),
            };
            for (int side = 0; side < 4; side++)
            {
                Vector3 a = c[side], b = c[(side + 1) % 4];
                float len = WorldLayout.DistXZ(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / maxPiece));
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n), q = Vector3.Lerp(a, b, (i + 1) / (float)n);
                    bool gap = false;
                    if (gates != null)
                        foreach (var g in gates)
                        {
                            if (g.side != side) continue;
                            float s0 = side % 2 == 0 ? Mathf.Min(p.x, q.x) : Mathf.Min(p.z, q.z);
                            float s1 = side % 2 == 0 ? Mathf.Max(p.x, q.x) : Mathf.Max(p.z, q.z);
                            if (s1 > g.center - g.halfWidth && s0 < g.center + g.halfWidth) gap = true;
                        }
                    if (!gap) r.Add(new[] { p, q });
                }
            }
            return r;
        }

        // ---- art-r6-09：柵は 3〜4 本の区間（6〜15m の切れ目）、北の土手には立てない。外へは柵の 6m 外の見えない壁（LobbyBuilder の LobbyBorder）が止める ----
        /// <summary>北の辺を除いた柵の線（南 → 東 → 西の順の長さ）の割合と切れ目の幅（m）。</summary>
        public static readonly Vector2[] FENCE_BREAKS = { new Vector2(0.18f, 8f), new Vector2(0.46f, 15f), new Vector2(0.74f, 6f) };
        public const float FENCE_YAW_JITTER = 4f, FENCE_SAG_M = 0.3f;

        /// <summary>柵の 1 区間（中点 mid）が見える柵か（北の辺は無し、FENCE_BREAKS の切れ目は無し）。</summary>
        public static bool FenceVisible(Vector4 rect, Vector3 mid)
        {
            float w = rect.z - rect.x, d = rect.w - rect.y;
            float e = 0.5f;
            if (Mathf.Abs(mid.z - rect.w) < e) return false;   // 北の辺
            float s;
            if (Mathf.Abs(mid.z - rect.y) < e) s = mid.x - rect.x;                 // 南（西 → 東）
            else if (Mathf.Abs(mid.x - rect.z) < e) s = w + (mid.z - rect.y);      // 東（南 → 北）
            else s = w + d + (rect.w - mid.z);                                     // 西（北 → 南）
            float total = w + 2f * d;
            foreach (var b in FENCE_BREAKS)
                if (Mathf.Abs(s - b.x * total) < b.y * 0.5f) return false;
            return true;
        }

        /// <summary>見える柵の区間の数（Validator）。</summary>
        public static int VisibleFenceCount(Vector4 rect)
        {
            int n = 0;
            foreach (var sg in FenceSegments(rect, Gates, FENCE_PIECE_M)) if (FenceVisible(rect, (sg[0] + sg[1]) * 0.5f)) n++;
            return n;
        }

        /// <summary>柵の板 i の向きの揺れ（±FENCE_YAW_JITTER）と垂れ（0〜FENCE_SAG_M 沈める）。</summary>
        public static void FenceJitter(int i, out float yaw, out float sag)
        {
            yaw = Mathf.Sin(i * 2.3f + 0.7f) * FENCE_YAW_JITTER;
            sag = Mathf.Abs(Mathf.Sin(i * 0.9f)) * FENCE_SAG_M;
        }

        // ---- art-r6-09：町の地面の色を外の土へ 25m でにじませる（8m のゆらぎ）----
        public const float TOWN_FEATHER_M = 25f, TOWN_FEATHER_NOISE_M = 8f;
        /// <summary>町の地面の板の外 plateOut m の、町の地面の色の重み（1 → 0）。</summary>
        public static float TownGroundWeight(float plateOut, Vector3 rel)
        {
            float n = (2f * HeightmapGen.Noise01(rel.x / 30f + 7.1f, rel.z / 30f - 3.3f, 77, 5) - 1f) * TOWN_FEATHER_NOISE_M;
            float u = Mathf.Clamp01((plateOut + n) / TOWN_FEATHER_M);
            return 1f - u * u * (3f - 2f * u);
        }
        /// <summary>ロビーの地面の土の色（町の地面の色 → 外の土 DirtOuter）。</summary>
        public static Color LobbyGroundTint(float plateOut, Vector3 rel)
        {
            float t = TownGroundWeight(plateOut, rel);
            Color a = ArtPalette.DirtOuter, b = ArtPalette.TownGroundTint;
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);
        }

        /// <summary>柵を広げてよい最大（ロビーの地面の端から 14m：木の帯 3 列が尾根の斜面に乗り、地面の端より内側に収まる）。
        /// 地面は柵＋38m なので、町は各辺 24m までなら広げた柵に収まる。</summary>
        public const float FENCE_MAX_INSET_M = 14f;
        public static readonly Vector4 FENCE_MAX_RECT = new Vector4(GROUND_RECT.x + FENCE_MAX_INSET_M, GROUND_RECT.y + FENCE_MAX_INSET_M,
            GROUND_RECT.z - FENCE_MAX_INSET_M, GROUND_RECT.w - FENCE_MAX_INSET_M);

        /// <summary>
        /// 実際の町の外形 town（ロビー中心からの相対）が柵の長方形 baseRect の内側に margin m 以上の余白で入るよう、足りない辺だけ外へ広げる。
        /// 広げた結果は FENCE_MAX_RECT を超えない（超える分は fits = false）。grown = 広げたか。
        /// </summary>
        public static Vector4 FitFenceRect(Vector4 baseRect, Vector4 town, float margin, out bool grown, out bool fits)
        {
            // 0.1m までの差（測った外形の丸め・浮動小数の誤差）では広げない
            const float TOL = 0.1f;
            var r = new Vector4(town.x - margin < baseRect.x - TOL ? town.x - margin : baseRect.x, town.y - margin < baseRect.y - TOL ? town.y - margin : baseRect.y,
                town.z + margin > baseRect.z + TOL ? town.z + margin : baseRect.z, town.w + margin > baseRect.w + TOL ? town.w + margin : baseRect.w);
            grown = r.x != baseRect.x || r.y != baseRect.y || r.z != baseRect.z || r.w != baseRect.w;
            var m = FENCE_MAX_RECT;
            fits = r.x >= m.x && r.y >= m.y && r.z <= m.z && r.w <= m.w;
            r = new Vector4(Mathf.Max(r.x, m.x), Mathf.Max(r.y, m.y), Mathf.Min(r.z, m.z), Mathf.Min(r.w, m.w));
            return r;
        }

        /// <summary>長方形の外への距離（中なら 0）。</summary>
        public static float DistOutside(Vector4 rect, Vector3 p) => WorldBounds.DistToRect(p, rect);

        /// <summary>柵の外の地面の盛り上がり（m、南・東・西の低い値）。外周の尾根は別に足される。</summary>
        public static float OutsideRise(float dOut) => Mathf.Min(OUT_RISE_MAX, OUT_SLOPE * dOut);

        /// <summary>外周の尾根の重み（地面の端で 1、内側 RIM_WIDTH_M で 0）。</summary>
        public static float RimWeight(Vector3 p)
        {
            float d = RimEdgeDistance(p);
            if (d >= RIM_WIDTH_M) return 0f;
            float t = Mathf.Clamp01(1f - d / RIM_WIDTH_M);
            return t * t * (3f - 2f * t);
        }

        // ---- art-r4-13：四角い穴にしない（縁の距離を揺らし、角を丸め、段を 1 つのなめらかな上りに） ----
        public const float RIM_JITTER_M = 14f, RIM_JITTER_SCALE_M = 90f, RIM_CORNER_M = 45f, RIM_JITTER_FADE_M = 30f;

        /// <summary>縁の距離の揺れ（m、±RIM_JITTER_M、周期 RIM_JITTER_SCALE_M の値ノイズ）。</summary>
        public static float RimJitter(Vector3 p) => RIM_JITTER_M * (2f * HeightmapGen.Noise01(p.x / RIM_JITTER_SCALE_M + 31.7f, p.z / RIM_JITTER_SCALE_M - 12.3f, 99, 37) - 1f);

        /// <summary>地面の端（GROUND_RECT、角を RIM_CORNER_M で丸めた）から内側への距離 + 揺れ（art-r4-13）。</summary>
        public static float RimEdgeDistance(Vector3 p)
        {
            Vector4 g = GROUND_RECT;
            float r = RIM_CORNER_M;
            float cx = Mathf.Clamp(p.x, g.x + r, g.z - r), cz = Mathf.Clamp(p.z, g.y + r, g.w - r);
            float dx = p.x - cx, dz = p.z - cz;
            float d = r - Mathf.Sqrt(dx * dx + dz * dz);
            return d + RimJitter(p);
        }

        /// <summary>柵の外の距離に揺れを足す（柵のすぐ外 RIM_JITTER_FADE_M は揺らさない：柵の足元は平らなまま）。</summary>
        public static float JitteredOutside(Vector4 fence, Vector3 p)
        {
            float d = DistOutside(fence, p);
            if (d <= 0f) return d;
            float f = Mathf.Clamp01(d / RIM_JITTER_FADE_M);
            return Mathf.Max(0f, d + RimJitter(p) * f * f * (3f - 2f * f));
        }

        /// <summary>なめらかに上限へ近づく上り（傾き slope で始まり max で止まる。折れ目の段を作らない：tanh）。</summary>
        public static float SoftRise(float d, float slope, float max)
        {
            if (d <= 0f || max <= 0f) return 0f;
            float x = slope * d / max;
            float e = Mathf.Exp(-2f * x);
            return max * (1f - e) / (1f + e);
        }

        /// <summary>外周の尾根の最低の高さ（ノイズの下限、南・東・西の低い値）。</summary>
        public static float RimMin(Vector3 p) => RIM_MIN_M * RimWeight(p);

        /// <summary>
        /// ロビーの地面の高さ（柵の中 0）。外は盛り上がり＋尾根（rimNoise 0〜1 で下限〜上限）。北は高い尾根へなめらかに移り、
        /// 見晴らしの切れ目では GAP_KEEP まで下げる。LobbyBuilder（rimNoise = ノイズ）とテスト（rimNoise = 0：下限）が同じ式を使う。
        /// </summary>
        public static float GroundHeight(Vector4 fence, Vector3 p, float rimNoise)
        {
            if (DistOutside(fence, p) <= 0f) return 0f;
            float d = JitteredOutside(fence, p);   // art-r4-13：揺らした距離、なめらかな上り（前は min(傾き × 距離, 上限) の折れ目が段に見えた）
            float nf = NorthFactor(fence, p);
            float rise = Mathf.Lerp(SoftRise(d, OUT_SLOPE, OUT_RISE_MAX), SoftRise(d, OUT_SLOPE_N, OUT_RISE_MAX_N), nf);
            float rmin = Mathf.Lerp(RIM_MIN_M, RIM_MIN_N, nf), rmax = Mathf.Lerp(RIM_MAX_M, RIM_MAX_N, nf);
            float rim = RimWeight(p) * (rmin + (rmax - rmin) * Mathf.Clamp01(rimNoise));
            float keep = 1f - (1f - GAP_KEEP) * GapFactor(fence, p);
            return (rise + rim) * keep;
        }

        /// <summary>ロビーの地面の高さの下限（尾根のノイズ 0）。</summary>
        public static float GroundHeight(Vector4 fence, Vector3 p) => GroundHeight(fence, p, 0f);

        static readonly string[] TreeParts = { "Trees_01", "Trees_02" };

        public const float BELT_BIG_ROCK_SCALE = 0.27f;
        static readonly string[] BeltCacti = { "Cacti_01", "Cacti_02", "Cacti_03" };   // 背の高いサワロ（4.1m）
        static readonly string[] BeltRocks = { "RockSetStylized_01_Rock", "DesertRocks_01", "DesertRocks_02", "DesertRocks_03" };

        /// <summary>art-r5-11：木の帯の 1 本の素材（k = 0〜19：0〜6 枯れ木、7〜12 サボテン（サワロ・サボテンの群れ）、13〜15 木、16〜17 アカシア、18〜19 岩）。</summary>
        public static void BeltPick(int k, System.Random rng, out string asset, out string part)
        {
            part = null;
            if (k < 7) asset = DressingPlan.DEADTREE;
            else if (k < 13)
            {
                if (k % 3 == 0) asset = DressingPlan.CGROUP;
                else { asset = DressingPlan.CACTI; part = BeltCacti[rng.Next(BeltCacti.Length)]; }
            }
            else if (k < 16) { asset = DressingPlan.TREES; part = TreeParts[rng.Next(2)]; }
            else if (k < 18) asset = DressingPlan.ACACIA;
            else
            {
                string rp = BeltRocks[rng.Next(BeltRocks.Length)];
                if (rp.StartsWith("RockSet")) { asset = DressingPlan.RSTYL; part = rp; }
                else { asset = DressingPlan.DROCKS; part = rp; }
            }
        }

        /// <summary>art-r6-09：木の帯のまとまりの長さ（外周に沿って、3〜7 本 × 3 列）と空き（30〜60m）の繰り返し。</summary>
        public const float BELT_CLUSTER_MIN_M = 18f, BELT_CLUSTER_MAX_M = 40f, BELT_OPEN_MIN_M = 30f, BELT_OPEN_MAX_M = 60f;
        /// <summary>外周の位置 s（m）が空き（木を置かない）か。まとまりと空きの長さは決まった値で揺らす。</summary>
        public static bool BeltOpen(float s)
        {
            float pos = 0f;
            for (int i = 0; i < 64; i++)
            {
                float u = Mathf.Abs(Mathf.Sin(i * 12.9898f + 4.1f)), v = Mathf.Abs(Mathf.Sin(i * 78.233f + 1.3f));
                float cl = Mathf.Lerp(BELT_CLUSTER_MIN_M, BELT_CLUSTER_MAX_M, u), op = Mathf.Lerp(BELT_OPEN_MIN_M, BELT_OPEN_MAX_M, v);
                if (s < pos + cl) return false;
                if (s < pos + cl + op) return true;
                pos += cl + op;
            }
            return false;
        }

        /// <summary>柵の外の木の帯（3 列・互い違い、拡大 1.0〜1.5、Collider なし＝柵の外で届かない）。</summary>
        public static List<Placement> TreeBelt(Vector4 rect, int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            for (int row = 0; row < BELT_ROWS; row++)
            {
                float off = BELT_FIRST_M + row * BELT_ROW_GAP_M;
                var outer = new Vector4(rect.x - off, rect.y - off, rect.z + off, rect.w + off);
                float w = outer.z - outer.x, d = outer.w - outer.y, perim = 2f * (w + d);
                for (float s = row * BELT_SPACING_M / BELT_ROWS; s < perim && list.Count < MAX_BELT; s += BELT_SPACING_M)
                {
                    // art-r6-09：一重の輪ではなく 3〜7 本のまとまり（BELT_CLUSTER_M）と 30〜60m の空き（BeltOpen）。外周の位置はどの列も同じ（まとまりは列をまたいで厚い）
                    if (BeltOpen(s)) continue;
                    float t = s + (float)rng.NextDouble() * 2f - 1f;
                    Vector3 p;
                    bool south = false;
                    if (t < w) { p = new Vector3(outer.x + t, 0f, outer.y); south = true; }
                    else if (t < w + d) p = new Vector3(outer.z, 0f, outer.y + (t - w));
                    else if (t < 2f * w + d) p = new Vector3(outer.z - (t - w - d), 0f, outer.w);
                    else p = new Vector3(outer.x, 0f, outer.w - (t - 2f * w - d));
                    int k = rng.Next(10);
                    if (south && row >= BELT_ROWS_SOUTH) continue;          // 南（広場の前）は 2 列
                    if (GapFactor(rect, p) > 0.05f) continue;                // 見晴らしの切れ目は空ける
                    // art-r5-11：西部の町のまわりの低木の原（枯れ木 35%・背の高いサボテン 30%・木 15%・アカシア 10%・岩 10%）。
                    // 前の緑の木とアカシアの帯はサバンナに見え、遠くの景色をふさいでいた
                    string asset, part;
                    BeltPick(rng.Next(20), rng, out asset, out part);
                    float scale = 1f + (float)rng.NextDouble() * 0.5f;
                    if (asset == DressingPlan.DROCKS) scale *= BELT_BIG_ROCK_SCALE;   // 大岩（高さ 11〜13m）は 3〜5m の岩の塊に
                    var sz = DressingPlan.SizeOf(asset, part);
                    list.Add(new Placement
                    {
                        group = "TreeBelt", asset = asset, part = part, position = new Vector3(p.x, GroundHeight(rect, p) - 0.3f, p.z),
                        yawDeg = (float)rng.NextDouble() * 360f, scale = scale, radius = sz.r * scale, height = sz.h * scale,
                        collider = false, shape = DressingPlan.SHAPE_NONE,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 見え方の近似：柵の中の目 eye から dir（正規化）へ進み、最初に当たるもの。柵の外の「平らな地面」（盛り上がり 1m 未満）か
        /// ロビーの地面の外（虚空）に先に届いたら true（＝外が見えてしまう）。柵の中の地面・柵・木（幹の円柱と樹冠の球）・
        /// 盛り上がった地面（尾根）に当たれば false。
        /// </summary>
        public static bool SeesOutside(Vector4 fence, Vector3 eye, Vector3 dir, List<Placement> belt, float maxDist, out string what)
        {
            what = "none";
            Vector4 f = fence, g = GROUND_RECT;
            for (float t = 0.25f; t < maxDist; t += 0.5f)
            {
                Vector3 p = eye + dir * t;
                // ロビーの地面の外：見晴らしの切れ目からなら遠くの景色（外の地面と山並み）、それ以外は地面の端が見えてしまう
                if (p.x < g.x || p.x > g.z || p.z < g.y || p.z > g.w)
                {
                    if (GapFactor(f, p) > 0.5f) { what = "vista"; return false; }
                    what = "void"; return true;
                }
                float dOut = DistOutside(f, p);
                if (dOut <= 0f)
                {
                    if (p.y <= 0f) { what = "inside ground"; return false; }
                    // 柵の線のすぐ内側
                    float dEdge = Mathf.Min(Mathf.Min(p.x - f.x, f.z - p.x), Mathf.Min(p.z - f.y, f.w - p.z));
                    if (dEdge < 0.3f && p.y <= FENCE_HEIGHT_M) { what = "fence"; return false; }
                    continue;
                }
                if (dOut < 0.3f && p.y <= FENCE_HEIGHT_M) { what = "fence"; return false; }
                foreach (var tr in belt)
                {
                    float dx = p.x - tr.position.x, dz = p.z - tr.position.z;
                    float h2 = dx * dx + dz * dz;
                    if (h2 > tr.radius * tr.radius * 1.2f) continue;
                    float y = p.y - tr.position.y;
                    if (y < tr.height * 0.45f && h2 < 0.35f * tr.scale * 0.35f * tr.scale) { what = "trunk"; return false; }
                    float cy = y - tr.height * 0.62f, cr = tr.radius * 0.8f;
                    if (h2 + cy * cy < cr * cr) { what = "canopy"; return false; }
                }
                float gh = GroundHeight(f, p);
                if (p.y <= gh)
                {
                    if (gh < 1f && GapFactor(f, p) > 0.5f) { what = "vista ground"; return false; }
                    if (dOut < BERM_VISIBLE_M) { what = "berm"; return false; }   // 柵のすぐ外のゆるい盛り上がり（ロビーの地面）
                    what = gh < 1f ? "outside flat ground" : "rim"; return gh < 1f;
                }
            }
            return false;
        }

        /// <summary>スポーンの目の高さ（1.6m）から方位 azDeg を見たときの地面の地平線の仰角（°、尾根の下限で数える）。</summary>
        public static float HorizonElevationDeg(Vector4 fence, Vector3 eye, float azDeg)
        {
            float a = azDeg * Mathf.Deg2Rad, best = -90f;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector4 g = GROUND_RECT;
            for (float t = 1f; t < 600f; t += 1f)
            {
                Vector3 p = eye + dir * t;
                if (p.x < g.x || p.x > g.z || p.z < g.y || p.z > g.w) break;
                float e = Mathf.Atan2(GroundHeight(fence, p) - eye.y, t) * Mathf.Rad2Deg;
                if (e > best) best = e;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // ロビーからステージが見えないか（art-r1-04）
        // ------------------------------------------------------------------

        /// <summary>ステージの地形の高さの上限（m、外周の尾根を含む。控えめに大きく）。</summary>
        public static float StageMaxHeight(int kind)
            => kind == HeightmapGen.KIND_DUNES ? 0.40f * HeightmapGen.HEIGHT_M + HeightmapGen.RIM_MAX_M
             : (kind == HeightmapGen.KIND_CANYON ? 0.95f * HeightmapGen.HEIGHT_M + HeightmapGen.RIM_MAX_M : 0.90f * HeightmapGen.HEIGHT_M + HeightmapGen.RIM_MAX_M);

        /// <summary>
        /// ロビーの目 eyeRel（ロビー中心からの相対、y = 目の高さ）から方位 azDeg に、霧で隠れない距離（LightingPlan.FogHiddenDistance）の内側で
        /// ステージの地形（上限の高さ）が地面の地平線より上に見えるか。見えるなら true と、そのステージの番号・距離。
        /// 町の建物・木・岩の冠は数えない（控えめ）。
        /// </summary>
        public static bool StageVisibleAbove(Vector4 fence, Vector3 eyeRel, float azDeg, out int stage, out float dist)
        {
            stage = -1; dist = 0f;
            float horizon = HorizonElevationDeg(fence, eyeRel, azDeg);
            float a = azDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 eyeW = WorldBounds.LOBBY_CENTER + eyeRel;
            float maxD = LightingPlan.FogHiddenDistance;
            foreach (var sd in StageDefinitions.All)
            {
                Vector3 o = sd.Origin;
                var r = new Vector4(o.x, o.z, o.x + StageDefinitions.SIZE, o.z + StageDefinitions.SIZE);
                float hMax = StageMaxHeight(sd.kind);
                for (float t = 5f; t < maxD; t += 5f)
                {
                    Vector3 p = eyeW + dir * t;
                    if (p.x < r.x || p.x > r.z || p.z < r.y || p.z > r.w) continue;
                    float e = Mathf.Atan2(hMax - eyeW.y, t) * Mathf.Rad2Deg;
                    if (e > horizon) { stage = sd.index; dist = t; return true; }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 尾根の岩の冠（art-r1-05）と町の裏の飾り（art-r1-16）
        // ------------------------------------------------------------------

        /// <summary>
        /// 尾根の上の岩の冠（切れ目あり）：柵の外 CROWN_OUT_M に沿って CROWN_STEP_M ごと（35% は空ける）。東西・南は岩の群れ（×2.5〜3）と
        /// 砂漠の大岩（DesertRocks_01〜03 ×2〜2.6、長い辺を柵に沿わせる）、北の 2 つの角はメサ B（×1.5〜2）。見晴らしの切れ目には置かない。
        /// y はロビー中心の高さ 0 からの地面の下限 − 高さの 25%（LobbyBuilder は実際の地形の高さで置き直す）。
        /// </summary>
        public const float CROWN_SINK = 0.15f;      // art-r3-13：足元の 4 隅の一番低い地面から高さの 15% 埋める
        public const float CROWN_FOOT = 0.7f;       // 4 隅 = 中心から外形の半径 × 0.7

        /// <summary>
        /// 尾根の岩を地面に下ろす（art-r3-13）：足元の 4 隅（±半径 × CROWN_FOOT）の一番低い地面から高さの CROWN_SINK だけ下げる
        /// （前は中心の地面 − 25% で、斜面の下側で宙に浮いた岩が見えた）。ground(rel) = ロビー中心からの相対の地面の高さ。
        /// </summary>
        public static void GroundCrown(List<Placement> crown, System.Func<Vector3, float> ground)
        {
            foreach (var c in crown)
            {
                float r = c.radius * CROWN_FOOT;
                float y = ground(c.position);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    y = Mathf.Min(y, ground(new Vector3(c.position.x + Mathf.Sin(a) * r, 0f, c.position.z + Mathf.Cos(a) * r)));
                }
                // art-r4-08：足元の高さの差 + 0.1m も沈める（高さの 60% まで）。斜面の下側で浮かない
                float hi = ground(c.position);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    hi = Mathf.Max(hi, ground(new Vector3(c.position.x + Mathf.Sin(a) * r, 0f, c.position.z + Mathf.Cos(a) * r)));
                }
                float sink = Mathf.Max(CROWN_SINK * c.height, Mathf.Min(hi - y + 0.1f, 0.6f * c.height));
                c.position = new Vector3(c.position.x, y - sink, c.position.z);
            }
        }

        /// <summary>町のいちばん高い 2 階建ての建物（SaloonFacade 10.9m）と、尾根の岩の高さ・拡大の上限（art-r4-13）。</summary>
        public const float TALLEST_TOWN_BUILDING_M = 10.9f, RIM_ROCK_MAX_H = 2f * TALLEST_TOWN_BUILDING_M, RIM_ROCK_MAX_SCALE = 2.5f;

        public static List<Placement> RimCrown(Vector4 rect, int seed)
        {
            const float CROWN_OUT_M = 24f, CROWN_STEP_M = 22f;
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            var outer = new Vector4(rect.x - CROWN_OUT_M, rect.y - CROWN_OUT_M, rect.z + CROWN_OUT_M, rect.w + CROWN_OUT_M);
            float w = outer.z - outer.x, d = outer.w - outer.y, perim = 2f * (w + d);
            string[] big = { "DesertRocks_01", "DesertRocks_02", "DesertRocks_03" };
            int k = 0;
            for (float s = CROWN_STEP_M * 0.5f; s < perim; s += CROWN_STEP_M, k++)
            {
                if (rng.NextDouble() < 0.35) continue;   // 切れ目のある冠
                float t = s + (float)rng.NextDouble() * 6f - 3f;
                Vector3 p; float sideYaw;
                if (t < w) { p = new Vector3(outer.x + t, 0f, outer.y); sideYaw = 0f; }
                else if (t < w + d) { p = new Vector3(outer.z, 0f, outer.y + (t - w)); sideYaw = 90f; }
                else if (t < 2f * w + d) { p = new Vector3(outer.z - (t - w - d), 0f, outer.w); sideYaw = 0f; }
                else { p = new Vector3(outer.x, 0f, outer.w - (t - 2f * w - d)); sideYaw = 90f; }
                if (GapFactor(rect, p) > 0.01f) continue;
                bool northCorner = p.z > rect.w + 10f && (p.x < rect.x + 10f || p.x > rect.z - 10f);
                string asset, part = null; float scale, yaw;
                if (northCorner) { asset = DressingPlan.MESAB; scale = 0.5f + (float)rng.NextDouble() * 0.15f; yaw = (float)rng.NextDouble() * 360f; }
                else if (k % 3 == 0) { asset = DressingPlan.DROCKS; part = big[rng.Next(big.Length)]; scale = 1.2f + (float)rng.NextDouble() * 0.4f; yaw = sideYaw + (rng.Next(2) == 0 ? 0f : 180f); }
                else { asset = DressingPlan.RSTYL; scale = 2.0f + (float)rng.NextDouble() * 0.5f; yaw = (float)rng.NextDouble() * 360f; }
                var sz = DressingPlan.SizeOf(asset, part);
                // art-r4-13：尾根の岩は町のいちばん高い建物の 2 倍まで（前は角の岩が 3〜4 階建てで 2 階建ての町を小さく見せた）
                if (sz.h * scale > RIM_ROCK_MAX_H) scale = RIM_ROCK_MAX_H / sz.h;
                scale = Mathf.Min(scale, RIM_ROCK_MAX_SCALE);
                float h = sz.h * scale;
                list.Add(new Placement
                {
                    group = "RimCrown", asset = asset, part = part, position = new Vector3(p.x, GroundHeight(rect, p) - CROWN_SINK * h, p.z),
                    yawDeg = yaw, scale = scale, radius = sz.r * scale, height = h, collider = false, shape = DressingPlan.SHAPE_NONE, background = true,
                });
            }
            GroundCrown(list, q => GroundHeight(rect, q));
            return list;
        }

        /// <summary>
        /// 町の裏（東の建物の列の裏と線路の間の狭い帯、x = BACKLOT_X）に 6m ごとに置く物の場所（ロビー中心からの相対）。練習場（z ≥ 86）の手前まで。
        /// 線路（x ≈ 19.2〜21.9）と、線路の東の通り道（x ≥ 23.4 の建物・小物の箱の手前）の間に収まる小さな物だけ。
        /// </summary>
        // ---- 見晴らしの切れ目の目印（art-r2-15）----
        public const float VISTA_HERO_DIST_M = 640f;     // art-r4-09：切れ目の真ん中の方位、スポーンから 450〜700m（前は 320m）
        public const float VISTA_HERO_MESA_SCALE = 2.5f, VISTA_HERO_MESAB_SCALE = 1.8f, VISTA_TOWER_DIST_M = 190f;   // art-r6-16：5 → 2.5、メサ B 3 → 1.8、塔は手前の 1/3
        public const float VISTA_HERO_TURN_DEG = 22f, VISTA_MESAB_TURN_DEG = 18f, VISTA_MESAB_DIST_M = 700f, VISTA_HERO_SINK = 0.45f, VISTA_ROCK_EXCLUDE_DEG = 12f;
        /// <summary>art-r6-16：切れ目の扇の中の物の地平線からの高さ（°）の上限（塔は大きさの物差しなので除く）。</summary>
        public const float VISTA_MAX_ELEV_DEG = 3f;
        public const float VISTA_SCATTER_FROM = 40f, VISTA_SCATTER_TO = 200f, VISTA_SCATTER_STEP = 15f;

        /// <summary>
        /// 見晴らしの切れ目ごとに、真ん中の方位の VISTA_HERO_DIST_M m にメサ ×2.5 を 2 つと給水塔（RanchCompound_11、煙の柱）、
        /// 切れ目の扇（柵の外 40〜200m）に 15m ごとに小石・砂漠の小岩・サボテン。位置はロビー中心からの相対（y = 0、置くときに外の地面の高さに合わせる）。
        /// </summary>
        public static List<Placement> VistaHeroes(int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            void Add(string group, string asset, string part, Vector3 p, float scale, float yaw)
            {
                var sz = DressingPlan.SizeOf(asset, part);
                list.Add(new Placement
                {
                    group = group, asset = asset, part = part, position = new Vector3(p.x, -0.25f * sz.h * scale, p.z), yawDeg = yaw, scale = scale,
                    radius = sz.r * scale, height = sz.h * scale, collider = false, shape = DressingPlan.SHAPE_NONE, background = true,
                });
            }
            void AddSunk(string group, string asset, string part, Vector3 p, float scale, float yaw, float sink)
            {
                Add(group, asset, part, p, scale, yaw);
                var q = list[list.Count - 1];
                q.position = new Vector3(q.position.x, -sink * q.height, q.position.z);
            }
            string[] small = { "DesertRocks_10", "DesertRocks_11", "DesertRocks_12", "DesertRocks_13", "DesertRocks_14", "DesertRocks_15" };
            foreach (var g in VistaGaps)
            {
                float a = g.x * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), right = new Vector3(dir.z, 0f, -dir.x);
                // art-r4-09：切れ目の真ん中の方位 450〜700m に 1 つの組んだ目印（メサ ×5 と両脇のメサ B ×3）、手前 250m に大きさの物差しの風車の塔
                // art-r6-16：主役のメサは ×2.5 で切れ目の真ん中から +22° へ（切れ目は低い遠くの地平線を見せる：空を背に脇に立つ）、
                // メサ B ×3 は反対側の 700m、給水塔は手前の 1/3（大きさの物差し）。切れ目の扇の中は地平線から 3° 以下
                float ha = (g.x + VISTA_HERO_TURN_DEG) * Mathf.Deg2Rad;
                Vector3 hdir = new Vector3(Mathf.Sin(ha), 0f, Mathf.Cos(ha));
                Vector3 hero = SPAWN + hdir * VISTA_HERO_DIST_M;
                AddSunk("VistaHero", DressingPlan.MESA, null, hero, VISTA_HERO_MESA_SCALE, (float)rng.NextDouble() * 360f, VISTA_HERO_SINK);
                for (int m = 0; m < 3; m++)
                {
                    float ba = (g.x - VISTA_MESAB_TURN_DEG - m * 6f) * Mathf.Deg2Rad;
                    AddSunk("VistaHero", DressingPlan.MESAB, null, SPAWN + new Vector3(Mathf.Sin(ba), 0f, Mathf.Cos(ba)) * (VISTA_MESAB_DIST_M + m * 40f),
                        VISTA_HERO_MESAB_SCALE * (1f - 0.12f * m), (float)rng.NextDouble() * 360f, VISTA_HERO_SINK);
                }
                Add("VistaHero", DressingPlan.RANCH, DressingPlan.RANCH_TOWER, SPAWN + dir * VISTA_TOWER_DIST_M + right * 25f, 1.2f, g.x + 180f);
                int k = 0;
                for (float d = VISTA_SCATTER_FROM; d <= VISTA_SCATTER_TO; d += VISTA_SCATTER_STEP, k++)
                {
                    float off = ((float)rng.NextDouble() * 2f - 1f) * g.y * 0.8f * Mathf.Deg2Rad;
                    float aa = a + off;
                    // 柵の外 d m（スポーンから柵までの距離 + d）
                    float toFence = DistToFenceAlong(FENCE_RECT, SPAWN, new Vector3(Mathf.Sin(aa), 0f, Mathf.Cos(aa)));
                    Vector3 p = SPAWN + new Vector3(Mathf.Sin(aa), 0f, Mathf.Cos(aa)) * (toFence + d);
                    if (Mathf.Abs(p.x - SPAWN.x) < APPROACH_HALF_M + 3f && p.z < FENCE_RECT.y) continue;   // art-r4-13：近づく道の上は空ける
                    int kind = k % 3;
                    if (kind == 0) Add("VistaScatter", DressingPlan.RCOLL, "RockCollection_" + (8 * (1 + rng.Next(23))), p, 2.5f, (float)rng.NextDouble() * 360f);
                    // art-r6-16：切れ目の扇（±12°）の中に橙の砂漠の岩の山は置かない（岩のセットの小石に）
                    else if (kind == 1 && Mathf.Abs(off) * Mathf.Rad2Deg > VISTA_ROCK_EXCLUDE_DEG) Add("VistaScatter", DressingPlan.DROCKS, small[rng.Next(small.Length)], p, 1.4f, (float)rng.NextDouble() * 360f);
                    else if (kind == 1) Add("VistaScatter", DressingPlan.RCOLL, "RockCollection_" + (8 * (1 + rng.Next(23))), p, 1.8f, (float)rng.NextDouble() * 360f);
                    else Add("VistaScatter", DressingPlan.CACTI, "Cacti_0" + (1 + rng.Next(9)), p, 1f, (float)rng.NextDouble() * 360f);
                }
            }
            // art-r4-13：南の見晴らしの切れ目を抜けて外へ続く近づく道の縁：15m ごとに左右交互の立てた丸太の杭、3 つに 1 つ壊れかけの柵（FarmProps_03）
            int j = 0;
            for (float z = FENCE_RECT.y - APPROACH_FROM_OUT_M; z > SPAWN.z - APPROACH_LEN_M; z -= APPROACH_POST_STEP_M, j++)
            {
                float sd = j % 2 == 0 ? 1f : -1f;
                Vector3 p = new Vector3(SPAWN.x + sd * (APPROACH_HALF_M + 1.5f), 0f, z + (float)rng.NextDouble() * 2f);
                Vector3 sc = CourseMarks.PostScale(DressingPlan.POST_H, DressingPlan.POST_THICK);
                list.Add(new Placement
                {
                    group = "ApproachRoad", asset = DressingPlan.LOGS, part = DressingPlan.POST_PART, position = new Vector3(p.x, DressingPlan.POST_H * 0.5f - 0.15f, p.z),
                    yawDeg = (float)rng.NextDouble() * 20f - 10f, rollDeg = 90f, scale = sc.x, scaleY = sc.y / sc.x, scaleZ = sc.z / sc.x, radius = 0.3f, height = DressingPlan.POST_H,
                    collider = false, shape = DressingPlan.SHAPE_NONE, background = true,
                });
                if (j % 3 == 1)
                    Add("ApproachRoad", DressingPlan.FARM, "FarmProps_03", p + new Vector3(sd * 0.3f, 0f, -1.6f), 1f, 90f + (float)rng.NextDouble() * 16f - 8f);
            }
            return list;
        }

        /// <summary>art-r4-13：町へ近づく道（南の切れ目を抜けて外の地面へ 300m、道のマスクで轍を塗る）。半幅・柵の外から始める距離・長さ・杭の間隔。</summary>
        public const float APPROACH_HALF_M = 4f, APPROACH_FROM_OUT_M = 40f, APPROACH_LEN_M = 330f, APPROACH_POST_STEP_M = 15f;

        /// <summary>町の大通りと近づく道の中心線（ロビー中心からの相対）：通り（x = SPAWN.x、z 20〜150）と南へ 300m。</summary>
        public static Vector3[] StreetAndApproach() => new[]
        {
            new Vector3(SPAWN.x, 0f, SPAWN.z - APPROACH_LEN_M), new Vector3(SPAWN.x, 0f, 20f), new Vector3(SPAWN.x, 0f, 150f),
        };

        /// <summary>柵の中の点 from から向き dir に進んで柵（長方形）を出るまでの距離。</summary>
        static float DistToFenceAlong(Vector4 rect, Vector3 from, Vector3 dir)
        {
            float t = float.MaxValue;
            if (dir.x > 1e-5f) t = Mathf.Min(t, (rect.z - from.x) / dir.x);
            if (dir.x < -1e-5f) t = Mathf.Min(t, (rect.x - from.x) / dir.x);
            if (dir.z > 1e-5f) t = Mathf.Min(t, (rect.w - from.z) / dir.z);
            if (dir.z < -1e-5f) t = Mathf.Min(t, (rect.y - from.z) / dir.z);
            return t;
        }

        // ---- 町の地面の板の縁（art-r2-11）----
        public const float PLATE_LIP_M = 2f, PLATE_BLEND_M = 12f, PLATE_TOP_OFFSET_M = 0.02f;   // art-r6-09：8 → 12m（板の縁の段を見せない）

        /// <summary>
        /// 町の模型の地面の板（WesternTown_001_Ground）の外側 dOut m の地形の高さ：板の外 PLATE_LIP_M までは板の上面 + 0.02m、
        /// その先 PLATE_BLEND_M かけて元の高さ level へ（板の縁・暗い側面を見せない）。板の上（dOut ≤ 0）と遠くは level のまま。
        /// </summary>
        public static float PlateBlend(float dOut, float level, float plateTop)
        {
            if (dOut <= 0f) return level;
            float top = plateTop + PLATE_TOP_OFFSET_M;
            if (dOut <= PLATE_LIP_M) return Mathf.Max(level, top);
            float u = Mathf.Clamp01((dOut - PLATE_LIP_M) / PLATE_BLEND_M);
            float s = u * u * (3f - 2f * u);
            return Mathf.Max(level, Mathf.Lerp(top, level, s));
        }

        /// <summary>板のまわりの砂の塗りの重み（板の外 PLATE_LIP_M + PLATE_BLEND_M まで 1 → 0）。</summary>
        public static float PlateSand(float dOut)
        {
            if (dOut <= 0f) return 0f;
            return 1f - Mathf.Clamp01((dOut - PLATE_LIP_M) / PLATE_BLEND_M);
        }

        // ---- art-r5-11：空いた敷地を埋める（建物の区画・東の裏の暮らし・囲いの馬・線路の終わりの荷の台）----
        /// <summary>線路（WesternTown_049_Track）の南の端（parts.json の外形から、町の板 TOWN_PLATE と同じ置き方）。</summary>
        public static readonly Vector3 RAIL_END = new Vector3(20.5f, 0f, 25.3f);

        static Placement Lot(string group, string asset, string part, Vector3 p, float yaw, float scale)
        {
            var sz = DressingPlan.SizeOf(asset, part);
            return new Placement
            {
                group = group, asset = asset, part = part, position = new Vector3(p.x, -0.05f, p.z), yawDeg = yaw, scale = scale,
                radius = sz.r * scale, height = sz.h * scale, collider = sz.h * scale >= DressingPlan.COLLIDER_MIN_H, shape = DressingPlan.SHAPE_BOX,
            };
        }

        /// <summary>
        /// art-r5-11：空いた敷地の埋め（ロビー中心からの相対、y = 通りの高さ − 0.05）。
        ///   建物の区画：町の東と西の縁に TownBlock（2 軒続きの建物）を 1 つずつ（町を向く）＝ 大通りの並びの外にもう 2 区画ずつ。
        ///   東の裏（大通りの東の並びの裏）：厩舎（×0.8）・テント 2 張り（×0.7）・薪の山・焚き火、その北に柵の囲いとつないだ馬 2 頭。
        ///   線路の南の端（RAIL_END）に木箱の荷の台（FarmProps_11/12 と丸太の床）。
        /// </summary>
        public static List<Placement> LotFill()
        {
            var l = new List<Placement>();
            const string G = "TownLots";
            l.Add(Lot(G, DressingPlan.TOWNBLOCK, null, new Vector3(39f, 0f, 50f), -90f, 1f));   // 東の縁の 2 区画（西＝町を向く）
            l.Add(Lot(G, DressingPlan.TOWNBLOCK, null, new Vector3(-40f, 0f, 108f), 90f, 1f));  // 西の縁の 2 区画（東を向く）
            const string E = "EastYard";
            l.Add(Lot(E, DressingPlan.STABLE, null, new Vector3(41.5f, 0f, 75f), -90f, 0.8f));
            l.Add(Lot(E, DressingPlan.TENT, null, new Vector3(37.8f, 0f, 63.5f), -70f, 0.7f));
            l.Add(Lot(E, DressingPlan.TENT, null, new Vector3(44.2f, 0f, 62f), -110f, 0.7f));
            l.Add(Lot(E, DressingPlan.CAMPFIRE, "Campfire_01", new Vector3(40.8f, 0f, 66.8f), 0f, 1f));
            string[] logs = { "WoodLogs_02", "WoodLogs_03", "WoodLogs_06", "WoodLogs_07" };
            for (int i = 0; i < logs.Length; i++) l.Add(Lot(E, DressingPlan.LOGS, logs[i], new Vector3(36.4f + (i % 2) * 1.1f, 0f, 84f + (i / 2) * 0.9f), 90f, 1.4f));
            // 柵の囲い（8.2m 四方、FarmProps_03 を 3 枚ずつ）とつないだ馬 2 頭
            const string C = "EastCorral";
            Vector3 c0 = new Vector3(38.6f, 0f, 92f);
            for (int i = 0; i < 3; i++)
            {
                l.Add(Lot(C, DressingPlan.FARM, "FarmProps_03", c0 + new Vector3(1.37f + i * FENCE_PIECE_M, 0f, 0f), 0f, 1f));                 // 南
                l.Add(Lot(C, DressingPlan.FARM, "FarmProps_03", c0 + new Vector3(1.37f + i * FENCE_PIECE_M, 0f, 3f * FENCE_PIECE_M), 0f, 1f));  // 北
                l.Add(Lot(C, DressingPlan.FARM, "FarmProps_03", c0 + new Vector3(3f * FENCE_PIECE_M, 0f, 1.37f + i * FENCE_PIECE_M), 90f, 1f)); // 東
                if (i != 1) l.Add(Lot(C, DressingPlan.FARM, "FarmProps_03", c0 + new Vector3(0f, 0f, 1.37f + i * FENCE_PIECE_M), 90f, 1f));    // 西（真ん中は門）
            }
            l.Add(Lot(C, DressingPlan.HORSE, null, c0 + new Vector3(3.2f, 0f, 3.4f), 20f, 1f));
            l.Add(Lot(C, DressingPlan.HORSE, null, c0 + new Vector3(5.6f, 0f, 5.4f), 200f, 1f));
            l.Add(Lot(C, DressingPlan.HAY, "HayBales_01", c0 + new Vector3(6.6f, 0f, 1.8f), 0f, 1f));
            // 線路の終わりの荷の台
            const string R = "RailPlatform";
            for (int i = 0; i < 3; i++) l.Add(Lot(R, DressingPlan.LOGS, "WoodLogs_01", RAIL_END + new Vector3(2.4f, 0f, -1.2f + i * 1.0f), 0f, 2f));
            l.Add(Lot(R, DressingPlan.FARM, "FarmProps_11", RAIL_END + new Vector3(2.0f, 0f, -0.6f), 10f, 1f));
            l.Add(Lot(R, DressingPlan.FARM, "FarmProps_12", RAIL_END + new Vector3(3.2f, 0f, 0.4f), -15f, 1f));
            l.Add(Lot(R, DressingPlan.FARM, "FarmProps_11", RAIL_END + new Vector3(2.6f, 0f, 1.4f), 40f, 1f));
            foreach (var q in l) if (q.part != null && q.part.StartsWith("WoodLogs")) { q.collider = false; q.shape = DressingPlan.SHAPE_NONE; }
            foreach (var q in l) if (q.asset == DressingPlan.HORSE || q.asset == DressingPlan.CAMPFIRE) { q.collider = false; q.shape = DressingPlan.SHAPE_NONE; }
            l.AddRange(Outskirts());
            return l;
        }

        // ---- art-r6-09：町の外れ（柵の外 60〜200m）：テントの野営地・囲い・南東へ延びる線路・牧場の建物・低い丘 ----
        public static readonly Vector3 OUT_CAMP = new Vector3(-118f, 0f, 70f), OUT_CORRAL = new Vector3(112f, 0f, 150f), OUT_RANCH = new Vector3(-160f, 0f, 175f),
            OUT_HILL = new Vector3(175f, 0f, 330f);
        public const float OUT_RAIL_FROM_M = 70f, OUT_RAIL_SIDE_M = 18f;
        public const int OUT_RAIL_PIECES = 2;   // 2 本 ≈ 190m（その先はロビーの地面の外）
        public const float OUT_HILL_SINK = 0.45f;

        /// <summary>
        /// 町の外れ（ロビー中心からの相対、y = 0 → LobbyBuilder が地形の高さへ）：西のテントの野営地（Tent ×3・焚き火 Campfire_01 の煙）、
        /// 東の囲い（厩舎 Stable と FarmProps_03 の柵 4 辺）、南東の切れ目へ延びる線路（WesternTown_049_Track ×3 ≈ 290m、切れ目の線の 18m 東）、
        /// 北西の牧場の建物（RanchCompound_08）、北東の低い丘（WesternTown_002_Hill、半分沈める）。柵の外で届かないので当たりなし。
        /// </summary>
        public static List<Placement> Outskirts()
        {
            var l = new List<Placement>();
            void O(string g, string asset, string part, Vector3 p, float yaw, float scale, float sinkH = 0.05f)
            {
                var q = Lot(g, asset, part, p, yaw, scale);
                q.position = new Vector3(q.position.x, -sinkH * q.height - 0.05f, q.position.z);
                q.collider = false; q.shape = DressingPlan.SHAPE_NONE;
                l.Add(q);
            }
            const string CAMP = "OutCamp";
            O(CAMP, DressingPlan.CAMPFIRE, "Campfire_01", OUT_CAMP, 0f, 1.1f);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 20f) * Mathf.Deg2Rad;
                O(CAMP, DressingPlan.TENT, null, OUT_CAMP + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 7f, i * 120f + 200f, 0.9f);
            }
            O(CAMP, DressingPlan.LOGS, "WoodLogs_02", OUT_CAMP + new Vector3(2.2f, 0f, -1.5f), 30f, 1.4f);
            O(CAMP, DressingPlan.LOGS, "WoodLogs_03", OUT_CAMP + new Vector3(-2.0f, 0f, 1.8f), 110f, 1.4f);
            O(CAMP, DressingPlan.HAY, "HayBales_02", OUT_CAMP + new Vector3(9f, 0f, -6f), 15f, 1f);
            const string COR = "OutCorral";
            O(COR, DressingPlan.STABLE, null, OUT_CORRAL + new Vector3(0f, 0f, 16f), 180f, 0.9f);
            for (int i = 0; i < 5; i++)
            {
                O(COR, DressingPlan.FARM, "FarmProps_03", OUT_CORRAL + new Vector3(-6.85f + 1.37f + i * FENCE_PIECE_M, 0f, -6.85f), 0f, 1f);
                if (i != 2) O(COR, DressingPlan.FARM, "FarmProps_03", OUT_CORRAL + new Vector3(-6.85f + 1.37f + i * FENCE_PIECE_M, 0f, 6.85f), 0f, 1f);
                O(COR, DressingPlan.FARM, "FarmProps_03", OUT_CORRAL + new Vector3(-6.85f, 0f, -6.85f + 1.37f + i * FENCE_PIECE_M), 90f, 1f);
                O(COR, DressingPlan.FARM, "FarmProps_03", OUT_CORRAL + new Vector3(6.85f, 0f, -6.85f + 1.37f + i * FENCE_PIECE_M), 90f, 1f);
            }
            O(COR, DressingPlan.HORSE, null, OUT_CORRAL + new Vector3(-1.5f, 0f, 0.5f), 60f, 1f);
            O(COR, DressingPlan.HORSE, null, OUT_CORRAL + new Vector3(2.5f, 0f, -2f), 250f, 1f);
            O(COR, DressingPlan.HAY, "HayBales_04", OUT_CORRAL + new Vector3(4f, 0f, 4f), 0f, 1f);
            // 南東の切れ目（VistaGaps[1]）へ延びる線路
            {
                float a = VistaGaps[1].x * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), right = new Vector3(dir.z, 0f, -dir.x);
                for (int i = 0; i < OUT_RAIL_PIECES; i++)
                {
                    Vector3 c = SPAWN + dir * (OUT_RAIL_FROM_M + (i + 0.5f) * SetPieces.RAIL_LENGTH) + right * OUT_RAIL_SIDE_M;
                    O("OutRail", DressingPlan.WTOWN, DressingPlan.RAIL_TRACK, c, VistaGaps[1].x, 1f, 0.3f);
                }
            }
            O("OutRanch", DressingPlan.RANCH, OUT_RANCH_PART, OUT_RANCH, 35f, 1f);
            O("OutHill", DressingPlan.WTOWN, OUT_HILL_PART, OUT_HILL, 200f, 1f, OUT_HILL_SINK);
            return l;
        }
        public const string OUT_RANCH_PART = "RanchCompound_08_Building", OUT_HILL_PART = "WesternTown_002_Hill";

        public const float BACKLOT_X = 22.6f;
        public static List<Vector3> BacklotSpots()
        {
            var l = new List<Vector3>();
            for (float z = 48f; z <= 84f; z += 6f) l.Add(new Vector3(BACKLOT_X, 0f, z));
            return l;
        }
    }
}
