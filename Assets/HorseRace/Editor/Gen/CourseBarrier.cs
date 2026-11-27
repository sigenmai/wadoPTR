using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コースの見えない柵（近づくと見える）の純粋な計画（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    ///   通ってよい範囲 = 「通り道（本コース・崖下りの近道の弦）から OFFSET_M 以内」の和集合。柵はその境界線（等距離線）。
    ///   距離場を GRID_M のマス目に作り、マーチングスクエアで等値線を取る → 2 回の角切りでならす → SEGMENT_M ごとに打ち直す。
    ///   和集合の境界なので、急な曲がりの内側でも線が交差・ループしない（内側の角は外向きにしか削れない）。
    ///   本コース・近道・スタート・遺物・CP は通り道から OFFSET_M 未満なので、柵が横切ることはない。
    ///   切れ目：岩橋の溝の上（溝の縁の岩壁 TrenchWalls が代わりに止める。溝に落ちれば落とし穴で CP へ戻る）、
    ///           ゴールの GOAL_GAP_BEFORE_M 手前から先（ゴールゲート・ゴール後の囲い・ゴールの町。走り終えた人しか通らない）、
    ///           外周の尾根の上（Terrain の端から EDGE_MARGIN_M 以内。実際のコースでは起きない）。
    /// 座標は Terrain ローカル（m、y = 0）。高さは BarrierBuilder が地形から取る。
    /// </summary>
    public static class CourseBarrier
    {
        public const float OFFSET_M = 28f;            // 通り道の中心線から柵まで（平らな道 ±12m、飾りの帯 15〜60m の中）
        public const float SEGMENT_M = 5f;            // 柵 1 区間の長さ（4〜6m）
        public const float GRID_M = 1f;               // 距離場のマス目
        public const float WALL_HEIGHT_M = 8f;        // 両端の地面の高い方から上へ
        public const float WALL_THICK_M = 0.5f;
        public const float WALL_SINK_M = 1f;          // 低い方の地面より下へ埋める
        public const float CP_CLEAR_M = 4f;           // CP の箱・遺物・スタート・ゴール後の囲いから柵までの最小距離
        public const float GOAL_GAP_BEFORE_M = 45f;   // ゴールのこれだけ手前から先は柵を張らない
        public const float EDGE_MARGIN_M = HeightmapGen.RIM_WIDTH_M;   // 外周の尾根の上には張らない
        public const float MIN_RUN_M = 2f;            // これより短い切れ端は捨てる
        public const float CHUNK_M = 200f;            // 見た目のメッシュ 1 つぶんの長さ（BarrierBuilder）
        /// <summary>CP の箱の幅・奥行き（CourseBuilder.CP_WIDTH / CP_DEPTH と同じ。Validate が一致を確かめる）。</summary>
        public const float CP_WIDTH = 36f, CP_DEPTH = 4f;

        /// <summary>計画：柵の線（開いた線と閉じた輪）。点は Terrain ローカル（y = 0）。</summary>
        public sealed class Plan
        {
            public readonly List<Vector3[]> runs = new List<Vector3[]>();
            public readonly List<bool> closed = new List<bool>();
            /// <summary>通り道（距離の基準）。[0] = 本コース、以降は近道など。</summary>
            public readonly List<Vector3[]> corridors = new List<Vector3[]>();

            public int SegmentCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < runs.Count; i++) n += closed[i] ? runs[i].Length : runs[i].Length - 1;
                    return n;
                }
            }

            /// <summary>柵の区間（a, b）をすべて列挙する。</summary>
            public List<Vector3[]> Segments()
            {
                var r = new List<Vector3[]>();
                for (int i = 0; i < runs.Count; i++)
                {
                    Vector3[] p = runs[i];
                    int n = closed[i] ? p.Length : p.Length - 1;
                    for (int k = 0; k < n; k++) r.Add(new[] { p[k], p[(k + 1) % p.Length] });
                }
                return r;
            }

            public float TotalLength
            {
                get
                {
                    float l = 0f;
                    foreach (var s in Segments()) l += WorldLayout.DistXZ(s[0], s[1]);
                    return l;
                }
            }
        }

        /// <summary>柵を張らない範囲：向きのある長方形（中心・進行方向・コース方向の半幅・横方向の半幅）。</summary>
        public struct GapRect
        {
            public Vector3 center, tangent;
            public float halfAlong, halfLat;

            public bool Contains(Vector3 p)
            {
                float dx = p.x - center.x, dz = p.z - center.z;
                float along = dx * tangent.x + dz * tangent.z;
                float lat = dx * tangent.z - dz * tangent.x;
                return Mathf.Abs(along) <= halfAlong && Mathf.Abs(lat) <= halfLat;
            }
        }

        // ------------------------------------------------------------------
        // ステージ定義からの計画（Build World・テストで同じ結果）
        // ------------------------------------------------------------------

        /// <summary>通り道：本コース全体（流しを含む）と、崖下りの近道の弦。</summary>
        public static List<Vector3[]> Corridors(Vector3[] courseLocal, SetPieces.ShortcutPlan shortcut)
        {
            var r = new List<Vector3[]> { Flat(courseLocal) };
            if (shortcut != null) r.Add(new[] { new Vector3(shortcut.a.x, 0f, shortcut.a.z), new Vector3(shortcut.b.x, 0f, shortcut.b.z) });
            return r;
        }

        /// <summary>岩橋の溝の上の切れ目（無ければ null）。溝の縁の岩壁（SetPieceBuilder の TrenchWalls）の線まで。</summary>
        public static GapRect? TrenchGap(Vector3[] courseLocal, float bridgeArc)
        {
            if (bridgeArc < 0f) return null;
            return new GapRect
            {
                center = Flat(WorldLayout.PointAtArc(courseLocal, bridgeArc)),
                tangent = WorldLayout.TangentAtArc(courseLocal, bridgeArc),
                halfAlong = SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND + 0.6f,
                halfLat = SetPieces.TRENCH_HALF_LENGTH + 4f,
            };
        }

        /// <summary>ゴールの GOAL_GAP_BEFORE_M 手前から流しの終点までのコース（この通り道に属する柵は張らない）。</summary>
        public static Vector3[] GoalTail(Vector3[] courseLocal, float goalArc)
        {
            float from = goalArc - GOAL_GAP_BEFORE_M;
            var r = new List<Vector3> { Flat(WorldLayout.PointAtArc(courseLocal, from)) };
            float acc = 0f;
            for (int i = 0; i < courseLocal.Length; i++)
            {
                if (i > 0) acc += WorldLayout.DistXZ(courseLocal[i - 1], courseLocal[i]);
                if (acc > from) r.Add(Flat(courseLocal[i]));
            }
            return r.ToArray();
        }

        /// <summary>このステージの柵の計画。shortcut は崖下りの近道（無ければ null）。</summary>
        public static Plan For(StageDef def, Vector3[] courseLocal, float goalArc, SetPieces.ShortcutPlan shortcut)
        {
            var gaps = new List<GapRect>();
            GapRect? trench = TrenchGap(courseLocal, SetPieces.BridgeArc(def, goalArc));
            if (trench.HasValue) gaps.Add(trench.Value);
            return Make(Corridors(courseLocal, shortcut), OFFSET_M, gaps, GoalTail(courseLocal, goalArc), StageDefinitions.SIZE);
        }

        // ------------------------------------------------------------------
        // CP の箱（柵との距離の確認用。CourseBuilder / SetPieceBuilder と同じ式）
        // ------------------------------------------------------------------

        /// <summary>CP の箱の x/z の 4 隅（Terrain ローカル）。近道が横切る CP は近道まで広げた箱。</summary>
        public static List<Vector3[]> CheckpointBoxes(Vector3[] courseLocal, float goalArc, SetPieces.ShortcutPlan shortcut)
        {
            float[] arcs = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            var r = new List<Vector3[]>();
            for (int k = 0; k < arcs.Length; k++)
            {
                float lo = -CP_WIDTH * 0.5f, hi = CP_WIDTH * 0.5f;
                if (shortcut != null)
                    for (int j = 0; j < shortcut.crossCps.Length; j++)
                        if (shortcut.crossCps[j] == k)
                        {
                            float o = shortcut.crossOffsets[j];
                            lo = Mathf.Min(lo, o - 8f); hi = Mathf.Max(hi, o + 8f);
                        }
                Vector3 c = Flat(WorldLayout.PointAtArc(courseLocal, arcs[k]));
                Vector3 t = WorldLayout.TangentAtArc(courseLocal, arcs[k]);
                Vector3 rt = WorldLayout.RightOf(t);
                float hd = CP_DEPTH * 0.5f;
                r.Add(new[] { c + rt * lo - t * hd, c + rt * hi - t * hd, c + rt * hi + t * hd, c + rt * lo + t * hd });
            }
            return r;
        }

        /// <summary>柵（区間の列）と凸四角形（4 隅）の x/z の最短距離。重なれば 0。</summary>
        public static float DistToQuad(List<Vector3[]> segments, Vector3[] quad)
        {
            float best = float.MaxValue;
            foreach (var s in segments)
            {
                if (InsideQuad(s[0], quad) || InsideQuad(s[1], quad)) return 0f;
                for (int e = 0; e < 4; e++)
                {
                    Vector3 q0 = quad[e], q1 = quad[(e + 1) % 4];
                    if (SegmentsIntersect(s[0], s[1], q0, q1)) return 0f;
                    best = Mathf.Min(best, Mathf.Min(
                        Mathf.Min(WorldLayout.DistToSegmentXZ(s[0], q0, q1), WorldLayout.DistToSegmentXZ(s[1], q0, q1)),
                        Mathf.Min(WorldLayout.DistToSegmentXZ(q0, s[0], s[1]), WorldLayout.DistToSegmentXZ(q1, s[0], s[1]))));
                }
            }
            return best;
        }

        /// <summary>柵（区間の列）から点までの x/z の最短距離。</summary>
        public static float DistToPoint(List<Vector3[]> segments, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var s in segments) best = Mathf.Min(best, WorldLayout.DistToSegmentXZ(p, s[0], s[1]));
            return best;
        }

        static bool InsideQuad(Vector3 p, Vector3[] q)
        {
            int sign = 0;
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = q[e], b = q[(e + 1) % 4];
                float cr = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
                int s = cr > 0f ? 1 : (cr < 0f ? -1 : 0);
                if (s == 0) continue;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>x/z の線分 ab と cd が交わるか（端の接触を含む）。</summary>
        public static bool SegmentsIntersect(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
            return false;
        }

        static float Cross(Vector3 o, Vector3 a, Vector3 b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);

        // ------------------------------------------------------------------
        // 等距離線
        // ------------------------------------------------------------------

        /// <summary>
        /// 通り道 corridors から d m の等距離線を柵にする。gaps の長方形の中と、goalTail（null 可）から d + 1.5m 以内の点、
        /// Terrain（0..sizeM）の端から EDGE_MARGIN_M 以内の点には張らない。
        /// </summary>
        public static Plan Make(List<Vector3[]> corridors, float d, List<GapRect> gaps, Vector3[] goalTail, float sizeM)
        {
            var plan = new Plan();
            foreach (var c in corridors) plan.corridors.Add(c);
            if (corridors.Count == 0) return plan;

            // ---- 距離場（通り道からの距離 − d。負 = 通ってよい側） ----
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var c in corridors)
                foreach (var p in c)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
            float g = GRID_M, pad = d + 4f * g;
            float x0 = Mathf.Floor((minX - pad) / g) * g, z0 = Mathf.Floor((minZ - pad) / g) * g;
            int nx = Mathf.CeilToInt((maxX + pad - x0) / g) + 1, nz = Mathf.CeilToInt((maxZ + pad - z0) / g) + 1;
            var f = new float[nz, nx];
            float far = d + 2f * g;
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) f[j, i] = far;
            float reach = d + 2f * g;
            foreach (var c in corridors)
            {
                int segs = c.Length == 1 ? 1 : c.Length - 1;
                for (int s = 0; s < segs; s++)
                {
                    Vector3 a = c[s], b = c[Mathf.Min(s + 1, c.Length - 1)];
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - x0) / g));
                    int i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - x0) / g));
                    int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach - z0) / g));
                    int j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach - z0) / g));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            float dist = WorldLayout.DistToSegmentXZ(new Vector3(x0 + i * g, 0f, z0 + j * g), a, b);
                            if (dist < f[j, i]) f[j, i] = dist;
                        }
                }
            }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) f[j, i] -= d;

            // ---- マーチングスクエア（辺の id で線をつなぐ） ----
            var adj = new Dictionary<long, List<long>>();
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    float v00 = f[j, i], v10 = f[j, i + 1], v11 = f[j + 1, i + 1], v01 = f[j + 1, i];
                    int idx = (v00 < 0f ? 1 : 0) | (v10 < 0f ? 2 : 0) | (v11 < 0f ? 4 : 0) | (v01 < 0f ? 8 : 0);
                    if (idx == 0 || idx == 15) continue;
                    long eB = HKey(i, j, nx), eT = HKey(i, j + 1, nx), eL = VKey(i, j, nx), eR = VKey(i + 1, j, nx);
                    switch (idx)
                    {
                        case 1: case 14: Link(adj, eB, eL); break;
                        case 2: case 13: Link(adj, eB, eR); break;
                        case 3: case 12: Link(adj, eL, eR); break;
                        case 4: case 11: Link(adj, eR, eT); break;
                        case 6: case 9: Link(adj, eB, eT); break;
                        case 7: case 8: Link(adj, eL, eT); break;
                        case 5:
                        case 10:
                        {
                            // 鞍点：中央の値で決める（中央が内側 = 内側どうしがつながる）
                            bool centerIn = (v00 + v10 + v11 + v01) * 0.25f < 0f;
                            bool diag00 = idx == 5;   // 5 = 00 と 11 が内側
                            if (centerIn == diag00) { Link(adj, eB, eR); Link(adj, eT, eL); }
                            else { Link(adj, eB, eL); Link(adj, eR, eT); }
                            break;
                        }
                    }
                }

            // ---- 辺 id の連鎖 → 点列 ----
            var visited = new HashSet<long>();
            var chains = new List<List<Vector3>>();
            var chainClosed = new List<bool>();
            // 開いた連鎖（端は次数 1）を先に、残りは輪
            foreach (bool openPass in new[] { true, false })
                foreach (var kv in adj)
                {
                    if (visited.Contains(kv.Key)) continue;
                    if (openPass && kv.Value.Count != 1) continue;
                    var pts = new List<Vector3>();
                    long cur = kv.Key, prev = long.MinValue;
                    bool isClosed = false;
                    while (true)
                    {
                        visited.Add(cur);
                        pts.Add(EdgePoint(cur, f, nx, x0, z0, g));
                        long next = long.MinValue;
                        foreach (long n in adj[cur]) if (n != prev && !visited.Contains(n)) { next = n; break; }
                        if (next == long.MinValue)
                        {
                            foreach (long n in adj[cur]) if (n == kv.Key && pts.Count > 2) isClosed = true;
                            break;
                        }
                        prev = cur; cur = next;
                    }
                    if (pts.Count >= 2) { chains.Add(pts); chainClosed.Add(isClosed && !openPass); }
                }

            // ---- ならす → 切れ目で分ける → 打ち直す ----
            for (int c = 0; c < chains.Count; c++)
            {
                List<Vector3> pts = Chaikin(Chaikin(chains[c], chainClosed[c]), chainClosed[c]);
                var keep = new bool[pts.Count];
                bool any = false, all = true;
                for (int k = 0; k < pts.Count; k++)
                {
                    keep[k] = !InGap(pts[k], gaps, goalTail, d, sizeM);
                    any |= keep[k]; all &= keep[k];
                }
                if (!any) continue;
                if (all && chainClosed[c])
                {
                    Emit(plan, pts, true);
                    continue;
                }
                // 開いた区間に分ける（閉じた輪は切れ目から始め直す）
                int n = pts.Count, start = 0;
                if (chainClosed[c]) { while (keep[start]) start++; }
                var run = new List<Vector3>();
                int count = chainClosed[c] ? n + 1 : n;
                for (int k = 0; k < count; k++)
                {
                    int idx = (start + k) % n;
                    if (keep[idx]) run.Add(pts[idx]);
                    else { if (run.Count >= 2) Emit(plan, run, false); run = new List<Vector3>(); }
                }
                if (run.Count >= 2) Emit(plan, run, false);
            }
            return plan;
        }

        static bool InGap(Vector3 p, List<GapRect> gaps, Vector3[] goalTail, float d, float sizeM)
        {
            if (p.x < EDGE_MARGIN_M || p.z < EDGE_MARGIN_M || p.x > sizeM - EDGE_MARGIN_M || p.z > sizeM - EDGE_MARGIN_M) return true;
            if (gaps != null) foreach (var gr in gaps) if (gr.Contains(p)) return true;
            if (goalTail != null && goalTail.Length > 0 && WorldLayout.DistToPolylineXZ(p, goalTail) <= d + 1.5f) return true;
            return false;
        }

        /// <summary>点列を SEGMENT_M 前後の等間隔に打ち直して計画に加える（短すぎる切れ端は捨てる）。</summary>
        static void Emit(Plan plan, List<Vector3> pts, bool closed)
        {
            float len = 0f;
            int segs = closed ? pts.Count : pts.Count - 1;
            for (int k = 0; k < segs; k++) len += WorldLayout.DistXZ(pts[k], pts[(k + 1) % pts.Count]);
            if (len < MIN_RUN_M) return;
            int n = Mathf.Max(closed ? 3 : 1, Mathf.RoundToInt(len / SEGMENT_M));
            float step = len / n;
            var r = new List<Vector3>();
            int count = closed ? n : n + 1;
            float acc = 0f;
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                float target = k * step;
                while (seg < segs - 1 && acc + WorldLayout.DistXZ(pts[seg], pts[(seg + 1) % pts.Count]) < target)
                {
                    acc += WorldLayout.DistXZ(pts[seg], pts[(seg + 1) % pts.Count]);
                    seg++;
                }
                Vector3 a = pts[seg], b = pts[(seg + 1) % pts.Count];
                float sl = WorldLayout.DistXZ(a, b);
                r.Add(sl > 1e-5f ? Vector3.Lerp(a, b, (target - acc) / sl) : a);
            }
            if (!closed) r[r.Count - 1] = pts[pts.Count - 1];
            plan.runs.Add(r.ToArray());
            plan.closed.Add(closed);
        }

        /// <summary>角切り 1 回（開いた線は両端を残す）。</summary>
        static List<Vector3> Chaikin(List<Vector3> p, bool closed)
        {
            if (p.Count < 3) return new List<Vector3>(p);
            var r = new List<Vector3>();
            int n = p.Count;
            if (!closed) r.Add(p[0]);
            int segs = closed ? n : n - 1;
            for (int k = 0; k < segs; k++)
            {
                Vector3 a = p[k], b = p[(k + 1) % n];
                r.Add(a * 0.75f + b * 0.25f);
                r.Add(a * 0.25f + b * 0.75f);
            }
            if (!closed) r.Add(p[n - 1]);
            return r;
        }

        static long HKey(int i, int j, int nx) => ((long)j * nx + i) * 2;
        static long VKey(int i, int j, int nx) => ((long)j * nx + i) * 2 + 1;

        static void Link(Dictionary<long, List<long>> adj, long a, long b)
        {
            List<long> l;
            if (!adj.TryGetValue(a, out l)) adj[a] = l = new List<long>(2);
            l.Add(b);
            if (!adj.TryGetValue(b, out l)) adj[b] = l = new List<long>(2);
            l.Add(a);
        }

        /// <summary>辺 id の上で値が 0 になる点（線形補間）。</summary>
        static Vector3 EdgePoint(long key, float[,] f, int nx, float x0, float z0, float g)
        {
            bool vertical = (key & 1) == 1;
            long cell = key >> 1;
            int j = (int)(cell / nx), i = (int)(cell % nx);
            float a = f[j, i], b = vertical ? f[j + 1, i] : f[j, i + 1];
            float t = Mathf.Abs(a - b) > 1e-6f ? Mathf.Clamp01(a / (a - b)) : 0.5f;
            return vertical ? new Vector3(x0 + i * g, 0f, z0 + (j + t) * g) : new Vector3(x0 + (i + t) * g, 0f, z0 + j * g);
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        static Vector3[] Flat(Vector3[] p)
        {
            var r = new Vector3[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = Flat(p[i]);
            return r;
        }
    }
}
