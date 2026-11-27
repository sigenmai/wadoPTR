using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ロビーの柵と外を隠す木の帯（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。座標はロビー中心からの相対（m）。
    ///   柵：FENCE_RECT（町の外形 + 8m。広場・たまり場・掲示板・練習場はすべて町の中）を一周する木の柵（FarmProps_03 を高さ ×1.6、柱の間隔 ≤ 2.74m）。
    ///       練習場も柵の中なので出入口（Gates）は無し＝外へ出る切れ目は無い（必要になったら Gates に足す）。
    ///   外：柵のすぐ外の地面は OUT_SLOPE で盛り上がり（OUT_RISE_MAX まで）、その先は外周の尾根。柵の外 3〜10m に木の帯（3 列・互い違い）。
    ///   → 柵の中の目の高さ（1.6〜3.2m）から水平〜少し下向きに見ると、見えるのは柵・木・盛り上がった地面（尾根）・空だけ（VisibilityRay でテスト）。
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
        /// 町の奥の見えない壁（z、町の南端 25 のとき）。町の模型には z ≈ 140〜141 に東西いっぱいの木の柵（高さ約 2m）があり、
        /// その北（教会の裏の空き地・岩山）は背景専用：柵のすぐ北に見えない壁を置いて、柵の両端の隙間と岩山へ行けないようにする。
        /// </summary>
        public const float TOWN_BACK_Z = 141.5f;

        // ---- 町の中の配置（ロビー中心からの相対 m、町の南端 z = 25・通りの高さ y = 0）。Blender で測った町の空き地（建物・岩・サボテン・起伏の無い所）----
        // 広場＝町の南の入口（x -29〜-7、z 25〜48 の空き地）
        public static readonly Vector3 SPAWN = new Vector3(-11.5f, 0f, 30f);            // 北（受付）を向く
        public static readonly Vector3 LEDGER_DESK = new Vector3(-11.5f, 0f, 40.5f);    // 受付カウンター（南＝スポーンを向く）
        public static readonly Vector3 WANTED_WALL = new Vector3(-22f, 0f, 36.5f);      // 手配書の壁（幅 6m、南を向く。背中は岩）
        public static readonly Vector3 START_BELL = new Vector3(-20f, 0f, 29.5f);       // スタート台（3m 四方）と鐘
        public static readonly Vector3 HUD_LEVER = new Vector3(-8f, 0f, 29f);           // 補助表示のレバー
        // 西の空き地（西の建物の列 x ≤ -14.6 と町の西の端 x -36 の間）
        public static readonly Vector3 PARKING_FIRST = new Vector3(-33.5f, 0f, 37f);   // 乗り物の待機場所 ×8（北へ PARKING_STEP ずつ、東を向く）
        public const float PARKING_STEP = 2.3f;
        public static readonly Vector3 CORRAL_FIRST = new Vector3(-21f, 0f, 47.5f);    // 柵の馬 ×7（北へ CORRAL_STEP ずつ、東を向く）
        public const float CORRAL_STEP = 2.2f;
        public static readonly Vector3 HANGOUT_FIRE = new Vector3(-25f, 0f, 72.5f);    // たまり場の焚き火（ベンチは半径 3.2m）
        public static readonly Vector3 NOTICE_BOARDS = new Vector3(-25f, 0f, 80f);     // 掲示板 3 枚（東西に 6m ずつ、南＝焚き火を向く）
        public static readonly Vector3 LAST_RACE_BOARD = new Vector3(-33.5f, 0f, 72.5f); // 前回レースの黒板（焚き火から西を見て読む）
        public static readonly Vector3 TOWN_CRIER = new Vector3(-17.5f, 0f, 71.5f);    // 新聞売り（西＝焚き火を向く）
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
        public static readonly Vector3 PRACTICE_JUMP = new Vector3(29f, 0f, 101f);           // ジャンプ台（6m × 6.2m、15°、北へ上る。着地は z 104〜117）
        public static readonly Vector3 PRACTICE_BUMP = new Vector3(25.5f, 0f, 132f);         // 小さな丘（7m 四方を 45° 回した低い台、上面 0.45m）
        public static readonly Vector3 PRACTICE_DUMMY_1 = new Vector3(23.5f, 0f, 111f);
        public static readonly Vector3 PRACTICE_DUMMY_2 = new Vector3(33f, 0f, 129f);
        public static readonly Vector3 PRACTICE_RELIC = new Vector3(33f, 0f, 108f);

        /// <summary>ロビーの飾り（DressingPlan.ForLobby）を置かない円（x, _, z, 半径）：南の入口の前（町の南の辺と柵の間＝広場への入口）。
        /// 遊びに使う物はすべて町の中（飾りは町の外形の外だけ）なので、それ以外の円は要らない。</summary>
        public static List<Vector4> LobbyDressingExclusions() => new List<Vector4> { new Vector4(0f, 0f, TOWN_NOMINAL_RECT.y - 4f, 44f) };

        /// <summary>遊びに使う物の位置と半径（テストで「柵の中・町の建物の外」を確かめる）。</summary>
        public static List<KeyValuePair<string, Vector4>> FunctionalSpots()
        {
            var l = new List<KeyValuePair<string, Vector4>>();
            void Add(string n, Vector3 p, float r) => l.Add(new KeyValuePair<string, Vector4>(n, new Vector4(p.x, 0f, p.z, r)));
            Add("Spawn", SPAWN, 1f);
            Add("LedgerDesk", LEDGER_DESK, 2.5f);
            for (int i = -1; i <= 1; i++) Add("WantedWall_" + (i + 1), WANTED_WALL + new Vector3(i * 2.1f, 0f, 0f), 1.2f);   // 幅 6.2m・厚さ 0.2m の板
            Add("StartBell", START_BELL, 2.2f);
            Add("HudLever", HUD_LEVER, 0.6f);
            for (int i = 0; i < 8; i++) Add("Parking_" + i, PARKING_FIRST + new Vector3(0f, 0f, i * PARKING_STEP), 1.4f);
            for (int i = 0; i < 7; i++) Add("Picker_" + i, CORRAL_FIRST + new Vector3(0f, 0f, i * CORRAL_STEP), 1.4f);
            Add("CorralFenceS", CORRAL_FIRST + new Vector3(-3.5f, 0f, -1.5f), 0.3f);
            Add("CorralFenceN", CORRAL_FIRST + new Vector3(-3.5f, 0f, 6 * CORRAL_STEP + 0.8f), 0.3f);   // すぐ北にサボテン
            Add("Campfire+Benches", HANGOUT_FIRE, 4f);
            for (int i = -1; i <= 1; i++) Add("NoticeBoard_" + (i + 1), NOTICE_BOARDS + new Vector3(i * 6f, 0f, 0f), 2.3f);
            Add("LastRaceBoard", LAST_RACE_BOARD, 1.5f);
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
            return l;
        }

        public const float FENCE_PIECE_M = 2.74f;        // FarmProps_03 の長さ（両端に柱）
        public const float FENCE_SCALE_Y = 1.6f;         // 0.98m × 1.6 ≈ 1.57m
        public const float FENCE_HEIGHT_M = 0.98f * FENCE_SCALE_Y;
        public const float OUT_SLOPE = 0.6f, OUT_RISE_MAX = 12f;
        public const float RIM_WIDTH_M = 30f, RIM_MIN_M = 15f, RIM_MAX_M = 30f;
        public const float BELT_FIRST_M = 3f, BELT_ROW_GAP_M = 3.5f, BELT_SPACING_M = 8f;
        public const int BELT_ROWS = 3;
        public const int MAX_BELT = 600;

        /// <summary>柵の面積（m²）。</summary>
        public static float Area(Vector4 r) => (r.z - r.x) * (r.w - r.y);

        /// <summary>木の帯の最低本数の目安（3 列の外周 ÷ 間隔 の 9 割）。Validator が使う。</summary>
        public static int MinBelt(Vector4 rect)
        {
            float perim = 2f * ((rect.z - rect.x) + (rect.w - rect.y));
            return Mathf.FloorToInt(0.9f * BELT_ROWS * perim / BELT_SPACING_M);
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

        /// <summary>柵の外の地面の盛り上がり（m）。外周の尾根（ApplyRim）は別に足される。</summary>
        public static float OutsideRise(float dOut) => Mathf.Min(OUT_RISE_MAX, OUT_SLOPE * dOut);

        /// <summary>外周の尾根の最低の高さ（ApplyRim のノイズの下限）。</summary>
        public static float RimMin(Vector3 p)
        {
            Vector4 g = GROUND_RECT;
            float d = Mathf.Min(Mathf.Min(p.x - g.x, g.z - p.x), Mathf.Min(p.z - g.y, g.w - p.z));
            if (d >= RIM_WIDTH_M) return 0f;
            float t = Mathf.Clamp01(1f - d / RIM_WIDTH_M);
            return RIM_MIN_M * t * t * (3f - 2f * t);
        }

        /// <summary>ロビーの地面の高さ（柵の中 0、外は盛り上がり＋尾根の下限）。</summary>
        public static float GroundHeight(Vector4 fence, Vector3 p)
        {
            float d = DistOutside(fence, p);
            return d <= 0f ? 0f : OutsideRise(d) + RimMin(p);
        }

        static readonly string[] TreeParts = { "Trees_01", "Trees_02" };

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
                    float t = s + (float)rng.NextDouble() * 2f - 1f;
                    Vector3 p;
                    if (t < w) p = new Vector3(outer.x + t, 0f, outer.y);
                    else if (t < w + d) p = new Vector3(outer.z, 0f, outer.y + (t - w));
                    else if (t < 2f * w + d) p = new Vector3(outer.z - (t - w - d), 0f, outer.w);
                    else p = new Vector3(outer.x, 0f, outer.w - (t - 2f * w - d));
                    int k = rng.Next(10);
                    string asset = k < 6 ? DressingPlan.TREES : (k < 9 ? DressingPlan.ACACIA : DressingPlan.DEADTREE);
                    string part = asset == DressingPlan.TREES ? TreeParts[rng.Next(2)] : null;
                    float scale = 1f + (float)rng.NextDouble() * 0.5f;
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
                if (p.x < g.x || p.x > g.z || p.z < g.y || p.z > g.w) { what = "void"; return true; }
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
                if (p.y <= gh) { what = gh < 1f ? "outside flat ground" : "rim"; return gh < 1f; }
            }
            return false;
        }
    }
}
