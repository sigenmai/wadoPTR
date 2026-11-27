using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ワールド生成の純粋な計算（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    /// 点列は x/z 平面で扱い、y は無視する（高さは SampleHeight で別に求める）。
    /// </summary>
    public static class WorldLayout
    {
        // ------------------------------------------------------------------
        // コース上の位置
        // ------------------------------------------------------------------

        /// <summary>CP の弧長。spacing ごと（spacing, 2*spacing, ...）に置き、最後は必ず goalArc（ゴール）。
        /// ゴールの直前が spacing/2 未満になるときはその CP を省く（CP が詰まりすぎない）。</summary>
        public static float[] CheckpointArcs(float goalArc, float spacing)
        {
            var r = new List<float>();
            if (goalArc <= 0f || spacing <= 0f) return new[] { Mathf.Max(0f, goalArc) };
            for (float a = spacing; a < goalArc - spacing * 0.5f; a += spacing) r.Add(a);
            r.Add(goalArc);
            return r.ToArray();
        }

        /// <summary>弧長 arc の位置（点列の線形補間、範囲外は端に丸める）。</summary>
        public static Vector3 PointAtArc(Vector3[] pts, float arc)
        {
            if (pts == null || pts.Length == 0) return Vector3.zero;
            if (arc <= 0f) return pts[0];
            float acc = 0f;
            for (int i = 1; i < pts.Length; i++)
            {
                float seg = DistXZ(pts[i - 1], pts[i]);
                if (acc + seg >= arc && seg > 0f) return Vector3.Lerp(pts[i - 1], pts[i], (arc - acc) / seg);
                acc += seg;
            }
            return pts[pts.Length - 1];
        }

        /// <summary>
        /// 弧長 a0 → a1 のコースの中心を step m ごとに取った折れ線（両端を含む。system-r3-13：Checkpoint.pathIn、順位を道のりで決める）。
        /// </summary>
        public static Vector3[] ArcPolyline(Vector3[] pts, float a0, float a1, float step)
        {
            if (step <= 0.01f) step = 10f;
            if (a1 < a0) { float t = a0; a0 = a1; a1 = t; }
            int n = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / step));
            var r = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) r[i] = PointAtArc(pts, i == n ? a1 : a0 + i * step);
            return r;
        }

        /// <summary>弧長 arc での進行方向（x/z、正規化、y = 0）。前後 window m の差分。</summary>
        public static Vector3 TangentAtArc(Vector3[] pts, float arc, float window = 4f)
        {
            Vector3 a = PointAtArc(pts, arc - window), b = PointAtArc(pts, arc + window);
            var d = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float m = d.magnitude;
            return m > 1e-5f ? d / m : new Vector3(0f, 0f, 1f);
        }

        /// <summary>進行方向 tangent の右（x/z）。</summary>
        public static Vector3 RightOf(Vector3 tangent) => new Vector3(tangent.z, 0f, -tangent.x);

        /// <summary>Y 回転（度）。Unity の Quaternion.Euler(0, yaw, 0) * forward = dir。</summary>
        public static float YawDeg(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        /// <summary>lanes 本のレーンの横オフセット（中央揃え、左から右）。</summary>
        public static float[] LaneOffsets(int lanes, float spacing)
        {
            var r = new float[Mathf.Max(0, lanes)];
            for (int i = 0; i < r.Length; i++) r[i] = (i - (lanes - 1) * 0.5f) * spacing;
            return r;
        }

        // ------------------------------------------------------------------
        // 距離
        // ------------------------------------------------------------------

        public static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float len2 = abx * abx + abz * abz;
            float t = 0f;
            if (len2 > 1e-6f) t = Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len2);
            float dx = p.x - (a.x + abx * t), dz = p.z - (a.z + abz * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// art-r5-01：折れ線への符号付きの距離（m、+ = 進む向きの左、− = 右）。大きさは DistToPolylineXZ と同じ、符号はいちばん近い区間の接線 × (p − 最寄りの点)。
        /// 轍のテクスチャ（ToonTerrain の G）は符号付きにする（符号なしは中心で折れ曲がり、補間すると点線・しみになる）。
        /// </summary>
        public static float SignedDistToPolylineXZ(Vector3 p, Vector3[] pts)
        {
            if (pts == null || pts.Length < 2) return DistToPolylineXZ(p, pts);
            float best = float.MaxValue, sign = 1f;
            for (int i = 1; i < pts.Length; i++)
            {
                Vector3 a = pts[i - 1], b = pts[i];
                float abx = b.x - a.x, abz = b.z - a.z, len2 = abx * abx + abz * abz;
                float t = len2 > 1e-6f ? Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len2) : 0f;
                float dx = p.x - (a.x + abx * t), dz = p.z - (a.z + abz * t);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < best) { best = d; sign = (abz * dx - abx * dz) > 0f ? -1f : 1f; }
            }
            return best * sign;
        }

        public static float DistToPolylineXZ(Vector3 p, Vector3[] pts)
        {
            if (pts == null || pts.Length == 0) return float.MaxValue;
            if (pts.Length == 1) return DistXZ(p, pts[0]);
            float best = float.MaxValue;
            for (int i = 1; i < pts.Length; i++)
            {
                float d = DistToSegmentXZ(p, pts[i - 1], pts[i]);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// コース外判定の確認用：スタート〜ゴールの各コース点について、その点を含む区間（直前の CP → 次の CP の線分、
        /// 最初の区間はスタート点 → CP1）からの距離の最大（m）。MountController は 60m を超えて 3 秒でリセットする。
        /// </summary>
        public static float MaxDeviationFromChords(Vector3[] course, float startArc, float[] cpArcs)
        {
            if (course == null || course.Length < 2 || cpArcs == null || cpArcs.Length == 0) return 0f;
            float max = 0f, acc = 0f;
            int seg = 0;
            Vector3 a = PointAtArc(course, startArc), b = PointAtArc(course, cpArcs[0]);
            for (int i = 0; i < course.Length; i++)
            {
                if (i > 0) acc += DistXZ(course[i - 1], course[i]);
                if (acc < startArc) continue;
                if (acc > cpArcs[cpArcs.Length - 1]) break;
                while (seg < cpArcs.Length - 1 && acc > cpArcs[seg])
                {
                    a = b;
                    seg++;
                    b = PointAtArc(course, cpArcs[seg]);
                }
                float d = DistToSegmentXZ(course[i], a, b);
                if (d > max) max = d;
            }
            return max;
        }

        /// <summary>コースが自分自身に近づきすぎていないか：弧長で minArcGap 以上離れた 2 点の最短距離（m）。</summary>
        public static float MinSelfDistance(Vector3[] pts, float minArcGap)
        {
            if (pts == null || pts.Length < 2) return float.MaxValue;
            var arcs = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) arcs[i] = arcs[i - 1] + DistXZ(pts[i - 1], pts[i]);
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length; i += 2)
                for (int j = i + 1; j < pts.Length; j += 2)
                {
                    if (arcs[j] - arcs[i] < minArcGap) continue;
                    float d = DistXZ(pts[i], pts[j]);
                    if (d < best) best = d;
                }
            return best;
        }

        // ------------------------------------------------------------------
        // 高さ
        // ------------------------------------------------------------------

        /// <summary>高さマップ（[z,x]、0..1）の双線形補間。lx/lz は Terrain ローカル（m）。戻り値は m。</summary>
        public static float SampleHeight(float[,] h, float sizeM, float heightM, float lx, float lz)
        {
            int n = h.GetLength(0), m = h.GetLength(1);
            float fx = Mathf.Clamp(lx / sizeM, 0f, 1f) * (m - 1);
            float fz = Mathf.Clamp(lz / sizeM, 0f, 1f) * (n - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, m - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, n - 1);
            int x1 = Mathf.Min(x0 + 1, m - 1), z1 = Mathf.Min(z0 + 1, n - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = h[z0, x0] + (h[z0, x1] - h[z0, x0]) * tx;
            float b = h[z1, x0] + (h[z1, x1] - h[z1, x0]) * tx;
            return (a + (b - a) * tz) * heightM;
        }

        /// <summary>
        /// 町の模型の「通りの高さ」の推定：上向きの面の高さを bin m ごとのヒストグラムにし（重み = 面積）、
        /// いちばん多い bin の中央値を返す。WesternTown は浮いた土台の模型なので、この高さだけ沈めると通りが地面に合う。
        /// 面が 1 つも無いときは fallback。
        /// </summary>
        public static float EstimateStreetHeight(float[] faceHeights, float[] faceAreas, float bin, float fallback)
        {
            if (faceHeights == null || faceHeights.Length == 0 || bin <= 0f) return fallback;
            var hist = new Dictionary<int, double>();
            for (int i = 0; i < faceHeights.Length; i++)
            {
                float w = faceAreas != null && i < faceAreas.Length ? faceAreas[i] : 1f;
                if (w <= 0f) continue;
                int k = Mathf.FloorToInt(faceHeights[i] / bin + 0.5f);
                hist.TryGetValue(k, out double v);
                hist[k] = v + w;
            }
            if (hist.Count == 0) return fallback;
            int bestK = 0; double bestW = -1;
            foreach (var kv in hist)
                if (kv.Value > bestW || (kv.Value == bestW && kv.Key < bestK)) { bestW = kv.Value; bestK = kv.Key; }
            return bestK * bin;
        }

        // ------------------------------------------------------------------
        // 砂地（SandZone のトリガー箱）
        // ------------------------------------------------------------------

        /// <summary>
        /// 砂マスク（[z,x]、0..1）を cells×cells のマス目に分け、平均が threshold 以上のマスを x 方向に連結した区間にする。
        /// 戻り値は {行 z, 開始列 x0, 終了列 x1（含む）} の列。SandZone の BoxCollider 1 つ = 1 区間。
        /// </summary>
        public static List<int[]> SandRuns(float[,] mask, int cells, float threshold)
        {
            var runs = new List<int[]>();
            if (mask == null || cells <= 0) return runs;
            bool[,] cell = SandCells(mask, cells, threshold);
            for (int cz = 0; cz < cells; cz++)
            {
                int start = -1;
                for (int cx = 0; cx <= cells; cx++)
                {
                    bool on = cx < cells && cell[cz, cx];
                    if (on && start < 0) start = cx;
                    if (!on && start >= 0) { runs.Add(new[] { cz, start, cx - 1 }); start = -1; }
                }
            }
            return runs;
        }

        /// <summary>マス目ごとの「砂地か」（平均 ≥ threshold）。</summary>
        public static bool[,] SandCells(float[,] mask, int cells, float threshold)
        {
            int n = mask.GetLength(0), m = mask.GetLength(1);
            var r = new bool[cells, cells];
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    int z0 = cz * n / cells, z1 = Mathf.Max(z0 + 1, (cz + 1) * n / cells);
                    int x0 = cx * m / cells, x1 = Mathf.Max(x0 + 1, (cx + 1) * m / cells);
                    double s = 0; int c = 0;
                    for (int z = z0; z < z1 && z < n; z++)
                        for (int x = x0; x < x1 && x < m; x++) { s += mask[z, x]; c++; }
                    r[cz, cx] = c > 0 && s / c >= threshold;
                }
            return r;
        }

        /// <summary>区間の合計面積の割合（0..1）。</summary>
        public static float RunsCoverage(List<int[]> runs, int cells)
        {
            if (runs == null || cells <= 0) return 0f;
            long n = 0;
            foreach (var r in runs) n += r[2] - r[1] + 1;
            return n / (float)(cells * cells);
        }

        public static float Mean(float[,] m)
        {
            if (m == null || m.Length == 0) return 0f;
            double s = 0;
            foreach (float v in m) s += v;
            return (float)(s / m.Length);
        }

        // ------------------------------------------------------------------
        // 飾りのばらまき
        // ------------------------------------------------------------------

        /// <summary>
        /// 決まった乱数（seed）で count 個の点（Terrain ローカル m）を選ぶ。各点はコースから minDist〜maxDist m、
        /// Terrain の端から margin m 以上、点どうしは spacing m 以上離す。条件を満たす点が見つからなければ少なく返す。
        /// </summary>
        public static List<Vector3> ScatterNearCourse(int seed, int count, float sizeM, float margin,
            Vector3[] course, float minDist, float maxDist, float spacing)
        {
            var rng = new System.Random(seed);
            var r = new List<Vector3>();
            int tries = 0;
            while (r.Count < count && tries < count * 400)
            {
                tries++;
                var p = new Vector3(margin + (float)rng.NextDouble() * (sizeM - 2f * margin), 0f,
                                    margin + (float)rng.NextDouble() * (sizeM - 2f * margin));
                float d = DistToPolylineXZ(p, course);
                if (d < minDist || d > maxDist) continue;
                bool ok = true;
                foreach (var q in r) if (DistXZ(p, q) < spacing) { ok = false; break; }
                if (ok) r.Add(p);
            }
            return r;
        }

        /// <summary>決まった乱数で 0..360 の角度列を作る。</summary>
        public static float[] Angles(int seed, int count)
        {
            var rng = new System.Random(seed);
            var r = new float[count];
            for (int i = 0; i < count; i++) r[i] = (float)(rng.NextDouble() * 360.0);
            return r;
        }
    }
}
