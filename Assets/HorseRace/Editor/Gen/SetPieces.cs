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

        // ジャンプ台
        public const float JUMP_LENGTH = 8f, JUMP_HEIGHT = 1.5f, JUMP_WIDTH = 16f;

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

        /// <summary>見どころの地形の加工（溝・近道）を heights に適用する。</summary>
        public static ShortcutPlan ApplyTerrain(StageDef def, Vector3[] local, float[,] heights, float goalArc)
        {
            ShortcutPlan plan = PlanFor(def, local, heights, goalArc);
            float bridge = BridgeArc(def, goalArc);
            if (bridge >= 0f)
                CarveTrench(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, bridge), WorldLayout.TangentAtArc(local, bridge));
            if (plan != null) ApplyShortcut(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, plan, local);
            return plan;
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
