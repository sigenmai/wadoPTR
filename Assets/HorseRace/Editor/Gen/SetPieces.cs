using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5）の純粋な幾何（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    ///   岩橋：コースを横切る溝（長さ 90m）を高さマップに掘り、道幅いっぱい（BRIDGE_WIDTH 26m ≥ 平らな道 24m）の橋の板を渡す。
    ///         溝の縁（道の外）には壁（SetPieceBuilder）、溝の底には落とし穴（ResetVolume → 最後の CP へ戻す）。
    ///   崖下りの近道：CP SHORTCUT_SPAN（3）区間ぶんのコースの曲がりを弦で切る枝道。途中に高さ SHORTCUT_DROP（≤ 8m）の段（崖）があり、
    ///                 落ちると着地の硬直（MountMath.LandingStun は 3m 以上で発生）。近道用の CP は増やさず、
    ///                 弦が横切る途中の CP 2 つの箱を近道まで広げる（どちらの道でも CP を順番どおりに通る）。
    ///   ジャンプ台：コース上の板の坂（JUMP_HEIGHT / JUMP_LENGTH、25° 以下）。
    /// 座標は Terrain ローカル（m）。高さマップは [z,x]、0..1（× heightM）。
    /// </summary>
    public static class SetPieces
    {
        // 岩橋
        public const float TRENCH_HALF_WIDTH = 7f;     // コース方向の溝の半幅（底が平らな部分）
        public const float TRENCH_BLEND = 3f;          // 溝の縁の傾斜
        public const float TRENCH_HALF_LENGTH = 45f;   // コースを横切る方向の半分の長さ
        public const float TRENCH_END_TAPER = 12f;     // 溝の両端で浅くなる長さ
        public const float TRENCH_DEPTH = 12f;
        public const float BRIDGE_HALF_SPAN = 11f;     // 橋の板の半分の長さ（溝の縁 10m より長い）
        public const float BRIDGE_WIDTH = 26f;          // 道幅（平らな道 ±12m）いっぱい＋余白

        // ジャンプ台（砂丘の盛り上がり：地形に坂を作り、見えない板の Collider で正確に跳ばせる）
        public const float JUMP_LENGTH = 8f, JUMP_HEIGHT = 1.5f, JUMP_WIDTH = 16f;
        public const float KICKER_LIP_M = 6f;          // 縁の後ろ（着地側）で元の高さへ戻る長さ
        public const float KICKER_SIDE_M = 6f;         // 横の裾
        public const float KICKER_UNDER_M = 0.15f;     // 地形は見えない板の上面よりこれだけ低く（板の上を走る）

        // 境目のメサの切り通し
        public const float PASS_DEPTH = 6f;            // 道を下げる深さ
        public const float PASS_HALF_CUT = 15f;        // 深さいっぱいの区間の半分（全長 30m）
        public const float PASS_RAMP = 26f;            // 前後の下り・上り（6m / 26m、最大 20° 未満）
        public const float PASS_ROAD_HALF = 13f;       // ここまで（道 ±12m ＋余白）は下げる
        public const float PASS_WALL_RISE = 22f;       // 両脇（道から 22〜40m）の盛り上げ
        public const float PASS_WALL_FROM = 22f, PASS_WALL_TO = 40f, PASS_WALL_FADE = 30f;

        // 砂漠の廃線（WesternTown_049_Track、長さ 95.6m）
        public const float RAIL_LENGTH = 95.6f;
        public const float RAIL_LATERAL = 35f;         // コースから（30〜40m）
        public const int RAIL_RUNS = 3;
        public const float RAIL_BED_HALF = 3.5f, RAIL_BED_BLEND = 4f;

        // 崖下りの近道
        public const float SHORTCUT_DROP = 6f;
        public const float MAX_DROP = 8f;
        public const float SHORTCUT_HALF_WIDTH = 4f;
        public const float SHORTCUT_BLEND = 4f;
        public const float SHORTCUT_RAMP_DEG = 12f;
        public const float RUNWAY_KEEP = 14f;          // 本コースの平らな道（±12m）＋余白は触らない
        public const float SHORTCUT_MIN_DEV = 24f;     // 弦がコースから最低これだけ離れる区間を選ぶ
        public const float SHORTCUT_MAX_DEV = 50f;     // コース外リセット（60m）より十分内側
        public const float LANDING_RUN = 12f;          // 段の下からコースに戻るまでの平らな長さ
        public const float END_INSET = 6f;             // CP から内側へ

        public static float JumpAngleDeg => Mathf.Atan(JUMP_HEIGHT / JUMP_LENGTH) * Mathf.Rad2Deg;

        /// <summary>target に一番近い「CP と CP の中間」の弧長（spacing*k + spacing/2、k ≥ 1、ゴールの 1.5 区間手前まで）。</summary>
        public static float MidCpArc(float target, float spacing, float goalArc)
        {
            int kMax = Mathf.Max(1, Mathf.FloorToInt((goalArc - spacing * 1.5f) / spacing));
            int k = Mathf.Clamp(Mathf.RoundToInt((target - spacing * 0.5f) / spacing), 1, kMax);
            return k * spacing + spacing * 0.5f;
        }

        /// <summary>弧長が [a0, a1] に入る点を除いた点列（橋の上は板なので地形の勾配の検査から外す）。</summary>
        public static Vector3[] ExcludeArcRange(Vector3[] pts, float a0, float a1)
        {
            var r = new System.Collections.Generic.List<Vector3>();
            float acc = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) acc += WorldLayout.DistXZ(pts[i - 1], pts[i]);
                if (acc < a0 || acc > a1) r.Add(pts[i]);
            }
            return r.ToArray();
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>コースを横切る溝を掘る（h を書き換える）。center/tangent は橋の中心とコースの向き（x/z）。</summary>
        public static void CarveTrench(float[,] h, float sizeM, float heightM, Vector3 center, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float reach = TRENCH_HALF_LENGTH + TRENCH_HALF_WIDTH + TRENCH_BLEND;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - center.x, dz = z * cell - center.z;
                    float along = Mathf.Abs(dx * t.x + dz * t.z);
                    float lat = Mathf.Abs(dx * r.x + dz * r.z);
                    float wa = 1f - Smooth01((along - TRENCH_HALF_WIDTH) / TRENCH_BLEND);
                    float wl = 1f - Smooth01((lat - (TRENCH_HALF_LENGTH - TRENCH_END_TAPER)) / TRENCH_END_TAPER);
                    float w = wa * wl;
                    if (w <= 0f) continue;
                    h[z, x] = Mathf.Max(0f, h[z, x] - TRENCH_DEPTH / heightM * w);
                }
        }

        // ------------------------------------------------------------------
        // ステージ定義からの配置（Build World・Validate・テストで同じ結果）
        // ------------------------------------------------------------------

        public static float BridgeArc(StageDef def, float goalArc)
            => def.bridgeFraction < 0f ? -1f : MidCpArc(def.bridgeFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        public static float JumpArc(StageDef def, float goalArc)
            => def.jumpFraction < 0f ? -1f : MidCpArc(def.jumpFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        public static float PassArc(StageDef def, float goalArc)
            => def.passFraction < 0f ? -1f : MidCpArc(def.passFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        /// <summary>点列の y に高さマップの高さ（m）を入れたコピー。</summary>
        public static Vector3[] WithHeights(Vector3[] local, float[,] h)
        {
            var r = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
                r[i] = new Vector3(local[i].x, WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, local[i].x, local[i].z), local[i].z);
            return r;
        }

        /// <summary>このステージの近道の計画（無ければ null）。heights は HeightmapGen の生成直後（掘る前）の高さ。</summary>
        public static ShortcutPlan PlanFor(StageDef def, Vector3[] local, float[,] heights, float goalArc)
        {
            if (!def.cliffShortcut) return null;
            float bridge = BridgeArc(def, goalArc);
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            float avoid0 = bridge < 0f ? -1f : bridge - 80f, avoid1 = bridge < 0f ? -2f : bridge + 80f;
            return PlanShortcut(WithHeights(local, heights), cps, avoid0, avoid1);
        }

        /// <summary>見どころの地形の加工（溝・近道・ジャンプの盛り上がり・切り通し・廃線の路盤）を heights に適用する。</summary>
        public static ShortcutPlan ApplyTerrain(StageDef def, Vector3[] local, float[,] heights, float goalArc)
        {
            ShortcutPlan plan = PlanFor(def, local, heights, goalArc);
            float bridge = BridgeArc(def, goalArc);
            if (bridge >= 0f)
                CarveTrench(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, bridge), WorldLayout.TangentAtArc(local, bridge));
            if (plan != null) ApplyShortcut(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, plan, local);
            float jump = JumpArc(def, goalArc);
            if (jump >= 0f)
                RaiseKicker(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, jump), WorldLayout.TangentAtArc(local, jump));
            float pass = PassArc(def, goalArc);
            if (pass >= 0f)
                CarvePass(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, pass), WorldLayout.TangentAtArc(local, pass));
            foreach (var run in RailRuns(def, local, goalArc))
                FlattenBed(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, run[0], run[1]);
            return plan;
        }

        // ------------------------------------------------------------------
        // ジャンプの盛り上がり・切り通し・廃線
        // ------------------------------------------------------------------

        /// <summary>
        /// ジャンプの盛り上がり（地形の高さに足す m）。along = コース方向の位置（中心 0、+ が進む向き）、lat = 横の距離。
        /// 坂（JUMP_LENGTH）で JUMP_HEIGHT − KICKER_UNDER_M まで上がり、縁の後ろ KICKER_LIP_M で元へ戻る。横は JUMP_WIDTH/2 から KICKER_SIDE_M で裾。
        /// </summary>
        public static float KickerRise(float along, float lat)
        {
            float half = JUMP_LENGTH * 0.5f, top = JUMP_HEIGHT - KICKER_UNDER_M;
            float a;
            if (along < -half || along > half + KICKER_LIP_M) return 0f;
            if (along <= half) a = top * (along + half) / JUMP_LENGTH;
            else a = top * (1f - Smooth01((along - half) / KICKER_LIP_M));
            float w = 1f - Smooth01((Mathf.Abs(lat) - JUMP_WIDTH * 0.5f) / KICKER_SIDE_M);
            return a * w;
        }

        public static void RaiseKicker(float[,] h, float sizeM, float heightM, Vector3 center, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float reach = JUMP_LENGTH + KICKER_LIP_M + JUMP_WIDTH * 0.5f + KICKER_SIDE_M;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - center.x, dz = z * cell - center.z;
                    float rise = KickerRise(dx * t.x + dz * t.z, dx * r.x + dz * r.z);
                    if (rise > 0f) h[z, x] = Mathf.Clamp01(h[z, x] + rise / heightM);
                }
        }

        /// <summary>
        /// 切り通しの高さの変化（m、地形に足す）。道（|lat| ≤ PASS_ROAD_HALF）は PASS_DEPTH 下げ（前後 PASS_RAMP でなめらかに）、
        /// 両脇（PASS_WALL_FROM〜PASS_WALL_TO）は PASS_WALL_RISE 盛り上げて、遠くから三角の切れ込みに見える壁にする。
        /// </summary>
        public static float PassDelta(float along, float lat)
        {
            float al = Mathf.Abs(along), la = Mathf.Abs(lat);
            float aw = 1f - Smooth01((al - PASS_HALF_CUT) / PASS_RAMP);
            float awWall = 1f - Smooth01((al - PASS_HALF_CUT - 10f) / 30f);
            float lw = Smooth01((la - PASS_ROAD_HALF) / (PASS_WALL_FROM - PASS_ROAD_HALF));
            float outer = 1f - Smooth01((la - PASS_WALL_TO) / PASS_WALL_FADE);
            return -PASS_DEPTH * aw * (1f - lw) + PASS_WALL_RISE * awWall * lw * outer;
        }

        public static void CarvePass(float[,] h, float sizeM, float heightM, Vector3 center, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float reach = PASS_HALF_CUT + 40f + PASS_WALL_TO + PASS_WALL_FADE;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - center.x, dz = z * cell - center.z;
                    float d = PassDelta(dx * t.x + dz * t.z, dx * r.x + dz * r.z);
                    if (d != 0f) h[z, x] = Mathf.Clamp01(h[z, x] + d / heightM);
                }
        }

        /// <summary>
        /// 砂漠の廃線（コースと平行、横 RAIL_LATERAL m）：コースが RAIL_LENGTH m ほぼまっすぐな所を RAIL_RUNS か所（300m 以上離して）。
        /// 各 [a, b]（Terrain ローカル、y = 0）。コースの全点から 30m 以上・Terrain の端から 80m 以上離れる。砂漠以外は空。
        /// </summary>
        public static System.Collections.Generic.List<Vector3[]> RailRuns(StageDef def, Vector3[] local, float goalArc)
        {
            var runs = new System.Collections.Generic.List<Vector3[]>();
            if (def.kind != HeightmapGen.KIND_DUNES || local == null || local.Length < 2) return runs;
            var used = new System.Collections.Generic.List<float>();
            float half = RAIL_LENGTH * 0.5f;
            int side = 1;
            for (float a = 160f; a < goalArc - 160f && runs.Count < RAIL_RUNS; a += 10f)
            {
                bool far = true;
                foreach (float u in used) if (Mathf.Abs(u - a) < 300f) far = false;
                if (!far) continue;
                Vector3 p0 = WorldLayout.PointAtArc(local, a - half), p1 = WorldLayout.PointAtArc(local, a + half);
                // ほぼまっすぐ：途中の点が弦から 3m 以内
                bool straight = true;
                for (float k = a - half; k <= a + half && straight; k += 8f)
                    if (WorldLayout.DistToSegmentXZ(WorldLayout.PointAtArc(local, k), p0, p1) > 3f) straight = false;
                if (!straight) continue;
                for (int tryS = 0; tryS < 2; tryS++, side = -side)
                {
                    Vector3 t = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z).normalized;
                    Vector3 r = WorldLayout.RightOf(t) * (side * RAIL_LATERAL);
                    Vector3 a0 = p0 + r, b0 = p1 + r;
                    bool ok = true;
                    for (int k = 0; k <= 12 && ok; k++)
                    {
                        Vector3 q = Vector3.Lerp(a0, b0, k / 12f);
                        if (q.x < 80f || q.z < 80f || q.x > StageDefinitions.SIZE - 80f || q.z > StageDefinitions.SIZE - 80f) ok = false;
                        else if (WorldLayout.DistToPolylineXZ(q, local) < 30f) ok = false;
                    }
                    if (!ok) continue;
                    runs.Add(new[] { new Vector3(a0.x, 0f, a0.z), new Vector3(b0.x, 0f, b0.z) });
                    used.Add(a);
                    side = -side;
                    break;
                }
            }
            return runs;
        }

        /// <summary>廃線の路盤：a〜b の両端の高さを結ぶ直線に、幅 ±RAIL_BED_HALF をならす（RAIL_BED_BLEND で元へ）。</summary>
        public static void FlattenBed(float[,] h, float sizeM, float heightM, Vector3 a, Vector3 b)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            float ya = WorldLayout.SampleHeight(h, sizeM, heightM, a.x, a.z), yb = WorldLayout.SampleHeight(h, sizeM, heightM, b.x, b.z);
            Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float L = ab.magnitude;
            if (L < 1e-3f) return;
            Vector3 t = ab / L;
            float reach = RAIL_BED_HALF + RAIL_BED_BLEND;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - a.x, dz = z * cell - a.z;
                    float along = dx * t.x + dz * t.z;
                    if (along < -RAIL_BED_BLEND || along > L + RAIL_BED_BLEND) continue;
                    float lat = Mathf.Abs(dx * t.z - dz * t.x);
                    if (lat >= reach) continue;
                    float wl = lat <= RAIL_BED_HALF ? 1f : 1f - (lat - RAIL_BED_HALF) / RAIL_BED_BLEND;
                    float wa = along < 0f ? 1f + along / RAIL_BED_BLEND : (along > L ? 1f - (along - L) / RAIL_BED_BLEND : 1f);
                    float target = Mathf.Lerp(ya, yb, Mathf.Clamp01(along / L)) / heightM;
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, Mathf.Clamp01(wl * wa)));
                }
        }

        // ------------------------------------------------------------------
        // 崖下りの近道
        // ------------------------------------------------------------------

        public const int SHORTCUT_SPAN = 3;            // 近道が渡る CP 区間の数（途中の CP 2 つの箱を近道まで広げる）

        public sealed class ShortcutPlan
        {
            public int cpInterval;      // CP[cpInterval] → CP[cpInterval+SPAN]（0 始まりの配列添字）
            public int[] crossCps;      // 途中で横切る CP の配列添字。その CP の箱を近道まで広げる（近道用の CP は増やさない）
            public float[] crossOffsets;// 各 CP の位置での近道の横オフセット（m、+ = 進行方向右）
            public float startArc, endArc;
            public Vector3 a, b;        // 弦の両端（Terrain ローカル、y = 地面の高さ m）
            public float length;
            public float f0;            // 本コースの平らな道を出る位置（0..1）
            public float rampEnd;       // 坂を上りきる位置
            public float fDrop;         // 段（崖）の位置
            public float drop;          // 段の高さ（m）
            public float maxDevFromCourse;
            public float maxDevFromCpChord;   // MountController のコース外判定（直前の CP → 次の CP の線分）からの最大距離
        }

        /// <summary>
        /// 近道の計画。courseH は Terrain ローカルの点列（y = コースの高さ m）、cpArcs は CP の弧長。
        /// CP SHORTCUT_SPAN 区間ごとに弦（両端から END_INSET 内側）を引き、コースから一番離れる候補を選ぶ。
        /// 弦は途中の CP の横の線を crossOffsets で横切るので、その CP の箱を近道まで広げれば順番どおりに進める。
        /// [avoid0, avoid1] と重なる区間（橋など）とゴールの区間は使わない。無ければ null。
        /// </summary>
        public static ShortcutPlan PlanShortcut(Vector3[] courseH, float[] cpArcs, float avoid0, float avoid1)
        {
            const int N = SHORTCUT_SPAN;
            if (courseH == null || cpArcs == null || cpArcs.Length < N + 2) return null;
            ShortcutPlan best = null;
            float rampLen = SHORTCUT_DROP / Mathf.Tan(SHORTCUT_RAMP_DEG * Mathf.Deg2Rad);
            for (int i = 0; i + N + 1 < cpArcs.Length; i++)
            {
                float s0 = cpArcs[i] + END_INSET, s1 = cpArcs[i + N] - END_INSET;
                if (s1 >= avoid0 && s0 <= avoid1) continue;
                Vector3 a = WorldLayout.PointAtArc(courseH, s0), b = WorldLayout.PointAtArc(courseH, s1);
                float L = WorldLayout.DistXZ(a, b);
                if (L < 40f) continue;
                var cpPos = new Vector3[N + 1];
                for (int k = 0; k <= N; k++) cpPos[k] = WorldLayout.PointAtArc(courseH, cpArcs[i + k]);
                // 途中の CP の横の線（CP を通りコースに垂直）と弦の交点
                var fc = new float[N + 1];
                var offs = new float[N - 1];
                bool ok = true;
                fc[0] = 0f; fc[N] = 2f;
                for (int k = 1; k < N && ok; k++)
                {
                    Vector3 t = WorldLayout.TangentAtArc(courseH, cpArcs[i + k]);
                    Vector3 r = WorldLayout.RightOf(t);
                    Vector3 c = cpPos[k];
                    float da = (a.x - c.x) * t.x + (a.z - c.z) * t.z, db = (b.x - c.x) * t.x + (b.z - c.z) * t.z;
                    if (da * db > 0f) { ok = false; break; }
                    fc[k] = da / (da - db);
                    Vector3 q = Vector3.Lerp(a, b, fc[k]);
                    offs[k - 1] = (q.x - c.x) * r.x + (q.z - c.z) * r.z;
                    if (fc[k] < fc[k - 1]) ok = false;
                }
                if (!ok) continue;
                int steps = Mathf.CeilToInt(L);
                float dev = 0f, devCp = 0f, f0 = -1f, f1 = -1f;
                int seg = 0;
                for (int k = 0; k <= steps; k++)
                {
                    float f = k / (float)steps;
                    while (seg < N - 1 && f > fc[seg + 1]) seg++;
                    Vector3 p = Vector3.Lerp(a, b, f);
                    float d = WorldLayout.DistToPolylineXZ(p, courseH);
                    if (d > dev) dev = d;
                    float dc = WorldLayout.DistToSegmentXZ(p, cpPos[seg], cpPos[seg + 1]);
                    if (dc > devCp) devCp = dc;
                    if (d >= RUNWAY_KEEP + 2f) { if (f0 < 0f) f0 = f; f1 = f; }
                }
                if (dev < SHORTCUT_MIN_DEV || dev > SHORTCUT_MAX_DEV || f0 < 0f || devCp > SHORTCUT_MAX_DEV) continue;
                float fDrop = f1 - LANDING_RUN / L;
                float rampEnd = f0 + rampLen / L;
                if (fDrop - rampEnd < 4f / L) continue;
                if (best != null && dev <= best.maxDevFromCourse) continue;
                var cross = new int[N - 1];
                for (int k = 1; k < N; k++) cross[k - 1] = i + k;
                best = new ShortcutPlan
                {
                    cpInterval = i, crossCps = cross, crossOffsets = offs, startArc = s0, endArc = s1, a = a, b = b, length = L,
                    f0 = f0, rampEnd = rampEnd, fDrop = fDrop, drop = SHORTCUT_DROP,
                    maxDevFromCourse = dev, maxDevFromCpChord = devCp,
                };
            }
            return best;
        }

        /// <summary>近道の高さ（m）：両端の高さを結ぶ直線＋（坂で上がって）段の手前まで drop m 高い。</summary>
        public static float ShortcutHeight(ShortcutPlan p, float f)
        {
            float baseH = Mathf.Lerp(p.a.y, p.b.y, f);
            float bump;
            if (f <= p.f0 || f > p.fDrop) bump = 0f;
            else if (f < p.rampEnd) bump = p.drop * (f - p.f0) / Mathf.Max(1e-4f, p.rampEnd - p.f0);
            else bump = p.drop;
            return baseH + bump;
        }

        /// <summary>近道の通路を高さマップに作る。本コースから RUNWAY_KEEP m 以内のマスは触らない（本コースの勾配は変わらない）。</summary>
        public static void ApplyShortcut(float[,] h, float sizeM, float heightM, ShortcutPlan p, Vector3[] courseLocal)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 ab = new Vector3(p.b.x - p.a.x, 0f, p.b.z - p.a.z);
            float L = ab.magnitude;
            if (L < 1e-3f) return;
            Vector3 t = ab / L;
            float reach = SHORTCUT_HALF_WIDTH + SHORTCUT_BLEND;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.a.x, p.b.x) - reach) / cell));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(p.a.x, p.b.x) + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.a.z, p.b.z) - reach) / cell));
            int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(p.a.z, p.b.z) + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - p.a.x, dz = z * cell - p.a.z;
                    float along = dx * t.x + dz * t.z;
                    if (along < 0f || along > L) continue;
                    float lat = Mathf.Abs(dx * t.z - dz * t.x);
                    if (lat >= reach) continue;
                    var cp = new Vector3(x * cell, 0f, z * cell);
                    float dc = WorldLayout.DistToPolylineXZ(cp, courseLocal);
                    float wr = Mathf.Clamp01((dc - RUNWAY_KEEP) / 2f);
                    if (wr <= 0f) continue;
                    float wl = lat <= SHORTCUT_HALF_WIDTH ? 1f : 1f - (lat - SHORTCUT_HALF_WIDTH) / SHORTCUT_BLEND;
                    float target = ShortcutHeight(p, along / L) / heightM;
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, wl * wr));
                }
        }

        /// <summary>弦に沿って高さマップを stepM ごとに読み、最大の下り段差（m、stepM 間の差）を返す。</summary>
        public static float MaxDropAlong(float[,] h, float sizeM, float heightM, Vector3 a, Vector3 b, float stepM)
        {
            float L = WorldLayout.DistXZ(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(L / stepM));
            float prev = WorldLayout.SampleHeight(h, sizeM, heightM, a.x, a.z), max = 0f;
            for (int k = 1; k <= steps; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
                float y = WorldLayout.SampleHeight(h, sizeM, heightM, p.x, p.z);
                if (prev - y > max) max = prev - y;
                prev = y;
            }
            return max;
        }
    }
}
