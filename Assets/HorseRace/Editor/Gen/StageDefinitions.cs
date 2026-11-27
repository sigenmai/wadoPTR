using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 3 ステージの定義（エディタ専用・純粋データ。UnityEditor に依存しないので単体テストできる）。
    /// 座標はすべて「Terrain ローカル（m）」：Terrain の角 = (0,0,0)、x/z は 0..1000。
    /// ワールド座標 = Origin + ローカル。Terrain の中心がステージの中心 (-1200,0,0)/(0,0,0)/(1200,0,0)。
    /// 制御点の最後の runoutM はゴール後の流し（ゴール後の囲いを置く平らな道。ゴールの町もここに並ぶ）で、
    /// 公式のコース長（スタート〜ゴール）には含めない。
    /// 徒歩用の近道は Phase 2。
    /// </summary>
    public sealed class StageDef
    {
        public int index;
        public string label;
        public int kind;
        public int seed;
        public Vector3 center;
        public Vector3[] controlLocal;
        public float runoutM;
        /// <summary>
        /// 道の高さの形（course-r1-03/04）：弧長 elevArc[k]（m、スタートから）での高さ elevM[k]（m、ステージの基準の高さから）。間は直線補間、
        /// 範囲の外は端の値。HeightmapGen.PathHeight がこれに小さなうねりを足す。最後の下り（DASH_DROP）は ゴールの 350m 手前で終わる。
        /// </summary>
        public float[] elevArc, elevM;
        /// <summary>
        /// 道幅（course-r1-10）：区間 [widthArc[2k], widthArc[2k+1]]（m）の平らな道の半幅 halfWidthM[k]（m）。区間の外は RUNWAY_M（12m）。
        /// 7m = 絞り（3〜4 頭）、18m = 追い抜きの広場。区間の端は WIDTH_BLEND_M でなめらかに変わる。
        /// </summary>
        public float[] widthArc, halfWidthM;
        /// <summary>見どころ（spec 5.5）。負の値 = なし。割合は公式コース長に対して（実際は CP の中間に寄せる）。</summary>
        public float bridgeFraction = -1f;     // 荒野：溝の上に残した地形の背（岩の橋）と、溝の両端を回る遠回り
        public bool hasShortcut;               // 近道（CP の連鎖には入らない枝道、途中に 6m の段）
        public float shortcutFrom = 0f, shortcutTo = 1f;   // 近道を探す範囲（公式コース長に対する割合）
        public float jumpFraction = -1f;       // 砂漠：地形の踏み切り（+5m の坂と 6m の段、その先に砂の窪み）
        public float passFraction = -1f;       // 境目：メサの切り通し（道を下げて両脇の地形を盛り、メサの門で挟む）
        public float mesaClimbFraction = -1f;  // 境目：台地の上（mesaClimbFraction〜mesaTopEndFraction、この間は道幅 70m の帯ごと持ち上がる）
        public float mesaTopEndFraction = -1f;

        public Vector3 Origin => new Vector3(center.x - StageDefinitions.HALF, center.y, center.z - StageDefinitions.HALF);
    }

    public static class StageDefinitions
    {
        public const float SIZE = HeightmapGen.TERRAIN_SIZE_M;   // 1000
        public const float HALF = SIZE * 0.5f;
        public const int HEIGHTMAP_RES = 513;
        public const int ALPHAMAP_RES = 512;
        /// <summary>コースのサンプリング間隔（m）。地形の平らな道・CP・検証すべてこの点列を使う。</summary>
        public const float SAMPLE_SPACING_M = 2f;
        public const float CP_SPACING_M = 80f;
        public const int LANES = 8;
        public const float LANE_SPACING_M = 2.6f;
        public const float START_ARC_M = 6f;        // スタート位置（8 頭の横一列）の弧長
        public const float MIN_COURSE_M = 2200f, MAX_COURSE_M = 2800f;
        public const float MAX_SLOPE_DEG = 25f;
        public const int RELICS_PER_STAGE = 4;
        // 道幅
        public const float CHOKE_HALF_M = 7f, WIDE_HALF_M = 18f, WIDTH_BLEND_M = 15f;
        // 最後の下り（course-r1-04）：ゴールの DASH_END_BEFORE_GOAL_M 手前で終わる DASH_DROP_M の下り（DASH_LEN_M）、その先はゴールまで平ら
        public const float DASH_END_BEFORE_GOAL_M = 350f, DASH_LEN_M = 240f;
        public const float DASH_DROP_MIN_M = 18f, DASH_DROP_MAX_M = 25f;

        static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        public static readonly StageDef[] All =
        {
            new StageDef
            {
                index = 0, label = "荒野", kind = HeightmapGen.KIND_CANYON, seed = 11,
                center = new Vector3(-1200f, 0f, 0f),
                // 峡谷を下る技巧のコース（course-r1-01）：北東から西へ、急カーブ 3 つ（左・右・左、半径 30m 前後）と中くらいのカーブ 3 つで
                // 左右に 6 回向きを変え、南の縁を東へ 680m の最後の直線。スタートからゴールまで 3 段で 40m 下る
                controlLocal = new[]
                {
                    P(910, 890), P(625, 886), P(593, 878), P(567, 859), P(413, 687), P(404, 672), P(403, 654), P(410, 637), P(423, 626),
                    P(627, 510), P(641, 497), P(647, 478), P(643, 459), P(631, 443), P(481, 331), P(454, 318), P(424, 315), P(395, 324),
                    P(278, 388), P(248, 396), P(217, 391), P(192, 372), P(177, 345), P(129, 165), P(128, 147), P(135, 130), P(149, 119),
                    P(167, 115), P(910, 110),
                },
                runoutM = 70f,
                // 3 段の下り（40m）：急カーブ 1 の手前、中くらいのカーブの後、最後の下り（ゴールの 590〜350m 手前、22m）
                elevArc = new[] { 0f, 420f, 560f, 1250f, 1380f, 1847f, 2087f },
                elevM = new[] { 40f, 40f, 30f, 30f, 22f, 22f, 0f },
                // 絞り：岩の橋（1040〜1120）と急カーブ 3 の手前の細い谷（1600〜1670）。広場：最初のカーブの後・急カーブ 1 の後・最後の直線
                widthArc = new[] { 340f, 540f, 1040f, 1120f, 1600f, 1670f, 660f, 860f, 1770f, 1970f },
                halfWidthM = new[] { 18f, 7f, 7f, 18f, 18f },
                bridgeFraction = 1080f / 2437f,
                hasShortcut = true, shortcutFrom = 0.5f, shortcutTo = 0.75f,
            },
            new StageDef
            {
                index = 1, label = "砂漠", kind = HeightmapGen.KIND_DUNES, seed = 23,
                center = new Vector3(0f, 0f, 0f),
                // 大きな輪（course-r1-01）：南西から北へ速い弧で砂丘の稜線を斜めに越え、中央へ切れ込む急カーブ 2 つ、東の縁を南へ、
                // 南東のゴールへ急カーブ 1 つ。稜線を 6 回越える（10〜14m）
                controlLocal = new[]
                {
                    P(150, 100), P(120, 465), P(122, 519), P(139, 569), P(240, 776), P(268, 809), P(308, 824), P(350, 817), P(384, 791),
                    P(500, 639), P(513, 628), P(530, 624), P(547, 628), P(560, 639), P(724, 853), P(738, 864), P(757, 867), P(775, 861),
                    P(788, 847), P(869, 689), P(878, 649), P(867, 610), P(838, 582), P(626, 463), P(613, 452), P(607, 436), P(608, 420),
                    P(615, 405), P(890, 88),
                },
                runoutM = 70f,
                // 稜線 6 つ（160m ごと、高さ 10〜14m、上り・下り 80m = 7〜10°）、急カーブ 2 の後から 22m 上ってゴールの 590〜350m 手前で下る
                elevArc = new[] { 0f, 80f, 160f, 240f, 320f, 400f, 480f, 560f, 640f, 720f, 800f, 880f, 960f, 1040f, 1500f, 1853f, 2093f },
                elevM = new[] { 0f, 0f, 12f, 0f, 10f, 0f, 14f, 0f, 11f, 0f, 12f, 0f, 13f, 0f, 0f, 22f, 0f },
                // 絞り：踏み切りの手前（1240〜1320）と急カーブ 3 の手前（1980〜2050）。広場：急カーブ 1・2 の後
                widthArc = new[] { 1240f, 1316f, 1980f, 2050f, 1140f, 1230f, 1480f, 1680f },
                halfWidthM = new[] { 7f, 7f, 18f, 18f },
                jumpFraction = 1320f / 2443f,
                hasShortcut = true, shortcutFrom = 0.25f, shortcutTo = 0.75f,
            },
            new StageDef
            {
                index = 2, label = "境目", kind = HeightmapGen.KIND_MESA, seed = 37,
                center = new Vector3(1200f, 0f, 0f),
                // 台地を回ってから登る（course-r1-01/05）：南から西・北の縁を大きく回り、北から中央の台地へ 7° の坂で 32m 登る。
                // 台地の上の鞍部（道幅 14m）を抜け、急カーブで東へ下りて（近道 = 台地の縁から 6m の崖）、北東の角のゴールの町へ
                controlLocal = new[]
                {
                    P(720, 130), P(216, 147), P(178, 159), P(150, 187), P(139, 226), P(131, 775), P(140, 814), P(167, 843), P(205, 856),
                    P(470, 876), P(487, 873), P(502, 863), P(511, 848), P(512, 830), P(473, 576), P(473, 561), P(480, 547), P(569, 423),
                    P(583, 411), P(601, 406), P(618, 410), P(633, 422), P(915, 780),
                },
                runoutM = 110f,
                // 登り 1112〜1372（32m、7°）、台地 〜1796、下り 1 段目（10m）、最後の下り（22m、ゴールの 590〜350m 手前）
                elevArc = new[] { 0f, 1112f, 1372f, 1796f, 1900f, 1904f, 2144f },
                elevM = new[] { 0f, 0f, 32f, 32f, 22f, 22f, 0f },
                // 絞り：台地の上の鞍部（1480〜1560）と台地の縁の門（1820〜1890）。広場：最初のカーブの後・急カーブ 1 の後・最後の直線
                widthArc = new[] { 1480f, 1560f, 1820f, 1890f, 600f, 800f, 1630f, 1790f, 2150f, 2350f },
                halfWidthM = new[] { 7f, 7f, 18f, 18f, 18f },
                passFraction = 1000f / 2494f,   // 台地へ登る手前のメサの門（切り通し）
                mesaClimbFraction = 1372f / 2494f, mesaTopEndFraction = 1796f / 2494f,
                hasShortcut = true, shortcutFrom = 1700f / 2494f, shortcutTo = 2300f / 2494f,
            },
        };

        /// <summary>道の高さの形（m、ステージの基準から）。キーが無ければ 0。</summary>
        public static float ElevationAt(StageDef s, float arc) => Keyed(s.elevArc, s.elevM, arc);

        public static float Keyed(float[] keys, float[] vals, float x)
        {
            if (keys == null || vals == null || keys.Length == 0) return 0f;
            if (x <= keys[0]) return vals[0];
            for (int k = 1; k < keys.Length; k++)
                if (x <= keys[k])
                {
                    float span = keys[k] - keys[k - 1];
                    return span <= 1e-4f ? vals[k] : Mathf.Lerp(vals[k - 1], vals[k], (x - keys[k - 1]) / span);
                }
            return vals[vals.Length - 1];
        }

        /// <summary>平らな道の半幅（m）。区間の中は halfWidthM、区間の端から WIDTH_BLEND_M で RUNWAY_M へ戻る。</summary>
        public static float HalfWidthAt(StageDef s, float arc)
        {
            float hw = HeightmapGen.RUNWAY_M;
            if (s == null || s.widthArc == null || s.halfWidthM == null) return hw;
            float best = hw, bestW = 0f;
            for (int k = 0; k < s.halfWidthM.Length && 2 * k + 1 < s.widthArc.Length; k++)
            {
                float a0 = s.widthArc[2 * k], a1 = s.widthArc[2 * k + 1];
                float d = arc < a0 ? a0 - arc : (arc > a1 ? arc - a1 : 0f);
                float w = 1f - Mathf.Clamp01(d / WIDTH_BLEND_M);
                if (w > bestW) { bestW = w; best = Mathf.Lerp(HeightmapGen.RUNWAY_M, s.halfWidthM[k], w); }
            }
            return best;
        }

        /// <summary>最後の下りの始まり（全力の合図の位置）の弧長。</summary>
        public static float DashArc(float goalArc) => goalArc - DASH_END_BEFORE_GOAL_M - DASH_LEN_M;

        /// <summary>Terrain ローカル（m）の等間隔サンプル（流しを含む）。</summary>
        public static Vector3[] SampleLocal(StageDef s) => CoursePath.Sample(s.controlLocal, SAMPLE_SPACING_M);

        /// <summary>HeightmapGen 用に 0..1 へ正規化（x/z）。</summary>
        public static Vector3[] Normalize(Vector3[] local)
        {
            var r = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) r[i] = new Vector3(local[i].x / SIZE, 0f, local[i].z / SIZE);
            return r;
        }

        /// <summary>公式のコース長（スタート〜ゴール、m）。</summary>
        public static float GoalArc(StageDef s, Vector3[] sampledLocal) => CoursePath.Length(sampledLocal) - s.runoutM;
        // ------------------------------------------------------------------
        // コースの形の検査（course-r1-01/02）
        // ------------------------------------------------------------------

        /// <summary>カーブ半径を測る窓の半分（サンプル数）。2m 間隔で ±3 点 = 12m の窓（約 10m）。</summary>
        public const int CORNER_WINDOW_HALF = 3;
        public const float TIGHT_CORNER_MAX_R = 45f;      // これ未満 = 急カーブ（駈歩に落とすか外へふくらむ）
        public const float TIGHT_CORNER_MIN_R = 22f;      // これ未満は無し（駈歩なら必ず曲がれる）
        public const float MEDIUM_CORNER_MAX_R = 75f;     // 45〜75m = 中くらいのカーブ
        public const int MIN_TIGHT_CORNERS = 3;
        public const float MIN_STAGE_SEPARATION_M = 120f; // 2 つのステージの形の平均の差

        /// <summary>サンプル i の前後 half 点を通る円の半径（m、まっすぐなら float.MaxValue）。</summary>
        public static float RadiusAt(Vector3[] pts, int i, int half = CORNER_WINDOW_HALF)
        {
            if (pts == null || i - half < 0 || i + half >= pts.Length) return float.MaxValue;
            Vector3 a = pts[i - half], b = pts[i], c = pts[i + half];
            float ab = WorldLayout.DistXZ(a, b), bc = WorldLayout.DistXZ(b, c), ca = WorldLayout.DistXZ(c, a);
            float cross = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
            if (cross < 1e-5f) return float.MaxValue;
            return ab * bc * ca / (2f * cross);
        }

        /// <summary>点列（スタート〜ゴール、2m 間隔）の最小のカーブ半径（m）。</summary>
        public static float MinCornerRadius(Vector3[] sampled) => MinCornerRadius(sampled, 0f, float.MaxValue);

        public static float MinCornerRadius(Vector3[] sampled, float fromArc, float toArc)
        {
            float best = float.MaxValue;
            for (int i = 0; i < sampled.Length; i++)
            {
                float a = i * SAMPLE_SPACING_M;
                if (a < fromArc || a > toArc) continue;
                float r = RadiusAt(sampled, i);
                if (r < best) best = r;
            }
            return best;
        }

        /// <summary>カーブ 1 つ（半径が maxR 未満の連続した区間）：頂点の弧長・最小半径・曲がる向き（+1 = 右、-1 = 左）・入口と出口の弧長。</summary>
        public struct Corner
        {
            public float apexArc, radius, fromArc, toArc;
            public int side;
        }

        /// <summary>半径 maxR 未満の区間をカーブとしてまとめる（gapM 以内の切れ目はつなぐ）。toArc より先は見ない。</summary>
        public static System.Collections.Generic.List<Corner> Corners(Vector3[] sampled, float maxR, float toArc, float gapM = 16f)
        {
            var list = new System.Collections.Generic.List<Corner>();
            bool open = false;
            Corner cur = new Corner();
            float lastIn = -1000f;
            for (int i = 0; i < sampled.Length; i++)
            {
                float a = i * SAMPLE_SPACING_M;
                if (a > toArc) break;
                float r = RadiusAt(sampled, i);
                if (r < maxR)
                {
                    if (!open || a - lastIn > gapM)
                    {
                        if (open) list.Add(cur);
                        cur = new Corner { apexArc = a, radius = r, fromArc = a, toArc = a, side = TurnSide(sampled, i) };
                        open = true;
                    }
                    cur.toArc = a;
                    if (r < cur.radius) { cur.radius = r; cur.apexArc = a; cur.side = TurnSide(sampled, i); }
                    lastIn = a;
                }
            }
            if (open) list.Add(cur);
            return list;
        }

        /// <summary>サンプル i で右へ曲がっていれば +1、左なら -1（y 上・x 右・z 前）。</summary>
        public static int TurnSide(Vector3[] pts, int i, int half = CORNER_WINDOW_HALF)
        {
            int i0 = Mathf.Max(0, i - half), i1 = Mathf.Min(pts.Length - 1, i + half);
            Vector3 a = pts[i0], b = pts[i], c = pts[i1];
            float cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
            return cross < 0f ? 1 : -1;
        }

        /// <summary>急カーブ（TIGHT_CORNER_MAX_R 未満）の一覧（スタート〜ゴール）。</summary>
        public static System.Collections.Generic.List<Corner> TightCorners(StageDef s, Vector3[] sampled)
            => Corners(sampled, TIGHT_CORNER_MAX_R, GoalArc(s, sampled));

        /// <summary>2 つのコースを弧長の割合で n 点に打ち直し、同じ割合の点どうしの距離の平均（m）。形がちがうほど大きい。</summary>
        public static float MeanPathDistance(Vector3[] a, Vector3[] b, int n = 200)
        {
            float la = CoursePath.Length(a), lb = CoursePath.Length(b), sum = 0f;
            for (int k = 0; k < n; k++)
            {
                float f = k / (float)(n - 1);
                sum += WorldLayout.DistXZ(WorldLayout.PointAtArc(a, f * la), WorldLayout.PointAtArc(b, f * lb));
            }
            return sum / n;
        }
    }
}
