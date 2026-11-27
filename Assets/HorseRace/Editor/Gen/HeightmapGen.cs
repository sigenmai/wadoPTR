using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// Editor-only procedural heightmap generator. Arrays are indexed [z, x]
    /// (Unity TerrainData.SetHeights convention). Heights are in [0,1] of HEIGHT_M.
    /// </summary>
    public static class HeightmapGen
    {
        public const int KIND_CANYON = 0, KIND_DUNES = 1, KIND_MESA = 2;
        public const float TERRAIN_SIZE_M = 1000f;
        public const float HEIGHT_M = 200f;
        public const float RUNWAY_M = 12f;     // fully flattened distance from the course
        public const float FLOOR_H = 0.28f;    // canyon height (0-1) at/below which sand may lie
        public const float WALL_H = 0.40f;     // canyon height at/above which sand is 0
        public const float FALLOFF_M = 60f;    // blend back to raw terrain by this distance beyond RUNWAY_M（course-r1-03：砂丘を道の脇から見せる）
        public const float FALLOFF_EXTRA_M = FALLOFF_M - RUNWAY_M;   // 道の縁からのぼかしの幅（道幅が変わっても同じ）
        public const float CANYON_BENCH_M = 30f;      // 荒野：道の縁からここまでは谷底の段（ゆるい斜面）、その先で壁へ（course-r1-13）
        public const float CANYON_BENCH_SLOPE = 0.25f; // 段の傾き（14°、≤ 20°）

        public static float[,] Generate(int resolution, int kind, int seed, Vector3[] coursePointsNormalized)
            => Generate(resolution, kind, seed, coursePointsNormalized, null);

        /// <summary>ステージの地形：道の高さの形（StageDef.elevArc/elevM）と道幅（StageDef.widthArc/halfWidthM）を使う。</summary>
        public static float[,] Generate(int resolution, StageDef def, Vector3[] coursePointsNormalized)
            => Generate(resolution, def.kind, def.seed, coursePointsNormalized, def);

        static float[,] Generate(int resolution, int kind, int seed, Vector3[] coursePointsNormalized, StageDef def)
        {
            int n = resolution;
            var h = new float[n, n];
            float cellM = TERRAIN_SIZE_M / (n - 1);

            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    h[z, x] = Mathf.Clamp01(Raw(kind, seed, x / (float)(n - 1), z / (float)(n - 1)));

            if (coursePointsNormalized == null || coursePointsNormalized.Length < 2) return h;

            // Distance (m) to the course and arc length (m) of the nearest course point.
            var dist = new float[n, n];
            var arc = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) dist[z, x] = float.MaxValue;

            float maxRunway = RUNWAY_M;
            if (def != null && def.halfWidthM != null) foreach (float hw in def.halfWidthM) maxRunway = Mathf.Max(maxRunway, hw);
            float reachM = maxRunway + FALLOFF_EXTRA_M;
            int radius = Mathf.CeilToInt(reachM / cellM) + 1;
            float s0 = 0f;
            for (int i = 1; i < coursePointsNormalized.Length; i++)
            {
                Vector3 a = coursePointsNormalized[i - 1] * TERRAIN_SIZE_M, b = coursePointsNormalized[i] * TERRAIN_SIZE_M;
                float segLen = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                float tx = segLen > 1e-5f ? (b.x - a.x) / segLen : 1f, tz = segLen > 1e-5f ? (b.z - a.z) / segLen : 0f;
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / (cellM * 0.5f)));
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float px = a.x + (b.x - a.x) * t, pz = a.z + (b.z - a.z) * t;
                    float s = s0 + segLen * t;
                    int cx = Mathf.FloorToInt(px / cellM + 0.5f), cz = Mathf.FloorToInt(pz / cellM + 0.5f);
                    for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(n - 1, cz + radius); z++)
                        for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(n - 1, cx + radius); x++)
                        {
                            float dx = x * cellM - px, dz = z * cellM - pz;
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d < dist[z, x]) { dist[z, x] = d; arc[z, x] = s; }
                        }
                }
                s0 += segLen;
            }

            float total = s0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float d = dist[z, x];
                    if (d >= reachM) continue;
                    float runway = def != null ? StageDefinitions.HalfWidthAt(def, arc[z, x]) : RUNWAY_M;
                    float target = def != null ? PathHeight(def, arc[z, x]) : PathHeight(kind, seed, arc[z, x], total);
                    if (kind == KIND_CANYON && def != null)
                    {
                        // 谷底の段：道の縁から CANYON_BENCH_M までゆるく上がり（元の地形が低ければそれに従う）、その先で元の壁へ
                        float bench = target + (Mathf.Clamp(d, runway, CANYON_BENCH_M) - runway) * CANYON_BENCH_SLOPE / HEIGHT_M;
                        bench = Mathf.Min(bench, Mathf.Max(h[z, x], target));
                        float wb = 1f - Smooth(CANYON_BENCH_M, CANYON_BENCH_M + FALLOFF_EXTRA_M, d);
                        h[z, x] = Mathf.Clamp01(d <= runway ? target : Mathf.Lerp(h[z, x], bench, wb));
                        continue;
                    }
                    float w = 1f - Smooth(runway, runway + FALLOFF_EXTRA_M, d);
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, w));
                }
            return h;
        }

        // ---- 道の踏み固めた土（course-r1-09）----
        public const float TRAIL_FULL_INSET_M = 2f;   // （前の値）平らな道の縁の 2m 内側まで
        public const float TRAIL_FADE_OUT_M = 4f;     // （前の値）縁の 4m 外で 0。マスクを計算する範囲に使う
        /// <summary>道の縁の幅（art-r3-06）：平らな道の縁 ±0.6m（1.2m）の smoothstep で切る。前の 6m のぼかしは見えない物の影のように見えた。</summary>
        public const float TRAIL_EDGE_M = 1.2f;
        /// <summary>轍（わだち、art-r3-06）：道の中心から ±1.8m に幅 0.6m の 2 本の線（ToonTerrain が道の色 ×0.85 で描く）。</summary>
        public const float RUT_OFFSET_M = 1.8f, RUT_WIDTH_M = 0.6f;
        /// <summary>道の中心からの距離を道のマスクの G に入れるときの最大（m）：G = 距離 / TRAIL_DIST_MAX_M。</summary>
        public const float TRAIL_DIST_MAX_M = 16f;

        /// <summary>道の中心からの距離 d（m）、平らな道の半幅 hw での踏み固めた土の重み（0〜1）。縁は TRAIL_EDGE_M の smoothstep（art-r3-06）。</summary>
        public static float TrailWeight(float d, float hw) => TrailWeight(d, hw, TRAIL_EDGE_M);

        /// <summary>縁の幅 edge（m）を指定した重み。砂漠は TRAIL_EDGE_DUNES_M（art-r4-05：のこぎりの歯の縁にしない）。</summary>
        public static float TrailWeight(float d, float hw, float edge) => 1f - Smooth(hw - edge * 0.5f, hw + edge * 0.5f, d);

        /// <summary>art-r4-05：砂漠の道の縁は 3m のぼかし＋縁の位置を ±0.8m 揺らす（直線の帯・ぎざぎざの縁にしない）。</summary>
        public const float TRAIL_EDGE_DUNES_M = 3.0f, TRAIL_EDGE_NOISE_M = 0.8f, TRAIL_EDGE_NOISE_SCALE_M = 7f;
        /// <summary>art-r4-05：砂漠は道の縁（重み 0.5）から外を砂 1 のまま、SAND_KEEP_TO_M から SAND_KEEP_FADE_M でもとの砂へ戻す（土の帯を見せない）。</summary>
        public const float SAND_KEEP_TO_M = 10f, SAND_KEEP_FADE_M = 10f;

        /// <summary>ステージの種類ごとの道の縁の幅（m）。</summary>
        public static float TrailEdge(int kind) => kind == KIND_DUNES ? TRAIL_EDGE_DUNES_M : TRAIL_EDGE_M;

        /// <summary>道の縁の揺れ（m、砂漠だけ）：Terrain ローカル (x, z) m の値ノイズ ±TRAIL_EDGE_NOISE_M。</summary>
        public static float TrailEdgeJitter(int kind, float x, float z)
            => kind == KIND_DUNES ? TRAIL_EDGE_NOISE_M * (2f * Noise(x / TRAIL_EDGE_NOISE_SCALE_M, z / TRAIL_EDGE_NOISE_SCALE_M, 3, 41) - 1f) : 0f;

        /// <summary>art-r5-01：符号付きの距離 d（m）をテクスチャの G（0〜1）へ：G = 0.5 + d / (2·TRAIL_DIST_MAX_M)。</summary>
        public static float EncodeSignedDist(float d) => Mathf.Clamp01(0.5f + Mathf.Clamp(d, -TRAIL_DIST_MAX_M, TRAIL_DIST_MAX_M) / (2f * TRAIL_DIST_MAX_M));

        /// <summary>G（0〜1）→ 符号付きの距離（m）。ToonTerrain の d = (G − 0.5)·2（× TRAIL_DIST_MAX_M）と同じ。</summary>
        public static float DecodeSignedDist(float g) => (g - 0.5f) * 2f * TRAIL_DIST_MAX_M;

        /// <summary>art-r5-01：符号付きの距離 sd（m）の轍の重み（ToonTerrain と同じ式：1 − smoothstep(w·0.35, w·0.65, ||d| − offset|)）。</summary>
        public static float RutWeightSigned(float sd) => RutWeight(Mathf.Abs(sd));

        /// <summary>格子 a（[z, x]）の (x, z)（格子の番号）の双線形補間（TerrainBuilder の道のテクスチャとテスト用）。</summary>
        public static float SampleBilinear(float[,] a, float x, float z)
        {
            int n = a.GetLength(0), mm = a.GetLength(1);
            int x0 = Mathf.Clamp((int)Mathf.Floor(x), 0, mm - 2), z0 = Mathf.Clamp((int)Mathf.Floor(z), 0, n - 2);
            float fx = Mathf.Clamp01(x - x0), fz = Mathf.Clamp01(z - z0);
            float top = a[z0, x0] + (a[z0, x0 + 1] - a[z0, x0]) * fx, bot = a[z0 + 1, x0] + (a[z0 + 1, x0 + 1] - a[z0 + 1, x0]) * fx;
            return top + (bot - top) * fz;
        }

        /// <summary>轍の重み（0〜1）：中心からの距離 d が ±RUT_OFFSET_M の幅 RUT_WIDTH_M の線の上で 1。</summary>
        public static float RutWeight(float d)
        {
            float e = Mathf.Abs(d - RUT_OFFSET_M);
            return 1f - Smooth(RUT_WIDTH_M * 0.35f, RUT_WIDTH_M * 0.65f, e);
        }

        /// <summary>踏み固めた土の重み（[z,x]、resolution × resolution、Terrain 全体）。道幅は StageDef の幅の区間に従う。</summary>
        public static float[,] TrailMask(int resolution, StageDef def, Vector3[] coursePointsNormalized)
        {
            float[,] dist;
            return TrailMask(resolution, def, coursePointsNormalized, out dist);
        }

        /// <summary>踏み固めた土の重みと、各点の道の中心からの距離（m、範囲の外は float.MaxValue）。距離は轍（RutWeight）に使う。</summary>
        public static float[,] TrailMask(int resolution, StageDef def, Vector3[] coursePointsNormalized, out float[,] dist)
        {
            float[,] hwMap;
            return TrailMask(resolution, def, coursePointsNormalized, out dist, out hwMap);
        }

        /// <summary>重み・道の中心からの距離・その点のいちばん近い所の平らな道の半幅（m、art-r4-05：砂漠の縁の砂に使う）。</summary>
        public static float[,] TrailMask(int resolution, StageDef def, Vector3[] coursePointsNormalized, out float[,] dist, out float[,] hwMap)
        {
            float[,] signedDist;
            return TrailMask(resolution, def, coursePointsNormalized, out dist, out hwMap, out signedDist);
        }

        /// <summary>
        /// art-r5-01：さらに「符号付きの横の距離」（m、+ = 進む向きの左、− = 右。いちばん近い区間の接線 × (p − 最寄りの点) の外積）。
        /// 符号なしの距離は中心の線で折れ曲がるので、双線形で補間すると轍が途切れた点線・丸いしみになった。符号付きなら中心をまっすぐ通る（直線の道で正確に線形）。
        /// 範囲の外は ±TRAIL_DIST_MAX_M。TerrainBuilder が G = 0.5 + d / (2·TRAIL_DIST_MAX_M) に入れる（EncodeSignedDist）。
        /// </summary>
        public static float[,] TrailMask(int resolution, StageDef def, Vector3[] coursePointsNormalized, out float[,] dist, out float[,] hwMap, out float[,] signedDist)
        {
            hwMap = null;
            int n = resolution;
            var m = new float[n, n];
            dist = new float[n, n];
            signedDist = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) { dist[z, x] = float.MaxValue; signedDist[z, x] = TRAIL_DIST_MAX_M; }
            if (coursePointsNormalized == null || coursePointsNormalized.Length < 2) return m;
            float cellM = TERRAIN_SIZE_M / (n - 1);
            float maxHw = RUNWAY_M;
            if (def != null && def.halfWidthM != null) foreach (float hw in def.halfWidthM) maxHw = Mathf.Max(maxHw, hw);
            float reach = maxHw + TRAIL_FADE_OUT_M + 1f + (def != null && def.kind == KIND_DUNES ? SAND_KEEP_TO_M + SAND_KEEP_FADE_M : 0f);
            var arc = new float[n, n];
            int radius = Mathf.CeilToInt(reach / cellM) + 1;
            float s0 = 0f;
            for (int i = 1; i < coursePointsNormalized.Length; i++)
            {
                Vector3 a = coursePointsNormalized[i - 1] * TERRAIN_SIZE_M, b = coursePointsNormalized[i] * TERRAIN_SIZE_M;
                float segLen = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                float tx = segLen > 1e-5f ? (b.x - a.x) / segLen : 1f, tz = segLen > 1e-5f ? (b.z - a.z) / segLen : 0f;
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / (cellM * 0.5f)));
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float px = a.x + (b.x - a.x) * t, pz = a.z + (b.z - a.z) * t;
                    int cx = Mathf.FloorToInt(px / cellM + 0.5f), cz = Mathf.FloorToInt(pz / cellM + 0.5f);
                    for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(n - 1, cz + radius); z++)
                        for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(n - 1, cx + radius); x++)
                        {
                            float dx = x * cellM - px, dz = z * cellM - pz;
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d < dist[z, x])
                            {
                                dist[z, x] = d; arc[z, x] = s0 + segLen * t;
                                // 接線 × (p − 最寄りの点)：区間の線からのまっすぐな横の距離（サンプル点の間隔で波打たない）
                                float lat = tz * dx - tx * dz;
                                signedDist[z, x] = Mathf.Clamp(-lat, -TRAIL_DIST_MAX_M, TRAIL_DIST_MAX_M);
                            }
                        }
                }
                s0 += segLen;
            }
            int kind = def != null ? def.kind : KIND_CANYON;
            float edge = TrailEdge(kind);
            hwMap = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float hw = def != null ? StageDefinitions.HalfWidthAt(def, arc[z, x]) : RUNWAY_M;
                    hwMap[z, x] = hw;
                    if (dist[z, x] < reach)
                        m[z, x] = TrailWeight(dist[z, x] + TrailEdgeJitter(kind, x * cellM, z * cellM), hw, edge);
                }
            return m;
        }

        /// <summary>スタート・ゴールの線の幅（m、course-r3-14）：道を横切る明るい土の帯（地形の塗りだけ、模型なし）。</summary>
        public const float LINE_BAND_M = 2f, LINE_BAND_FADE_M = 1.2f;

        /// <summary>
        /// スタート・ゴールの線（course-r3-14）：弧長 START_ARC_M + 3（スタートの門）と goalArc に、道幅いっぱい・幅 LINE_BAND_M の帯の重み（0〜1）。
        /// TerrainBuilder が道のマスクの B に入れ、ToonTerrain が道の色 × _LineBoost（明るい土）で塗る。local = Terrain ローカルのコース点列（2m 間隔）。
        /// </summary>
        public static float[,] LineMask(int resolution, StageDef def, Vector3[] local, float goalArc)
        {
            int n = resolution;
            var m = new float[n, n];
            if (local == null || local.Length < 2) return m;
            float cellM = TERRAIN_SIZE_M / (n - 1);
            float[] arcs = { StageDefinitions.START_ARC_M + 3f, goalArc };
            // art-r4-12：岩の橋の背（溝の上の道）は明るい土（SPINE_LINE_W × _LineBoost）で、溝の底より明るく
            float bridge = def != null ? SetPieces.BridgeArc(def, goalArc) : -1f;
            if (bridge >= 0f)
            {
                Vector3 c = WorldLayout.PointAtArc(local, bridge), t = WorldLayout.TangentAtArc(local, bridge);
                t = new Vector3(t.x, 0f, t.z).normalized;
                Vector3 r = WorldLayout.RightOf(t);
                float half = SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND + 2f, reach = half + SetPieces.SPINE_HALF + 2f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - reach) / cellM)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.x + reach) / cellM));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - reach) / cellM)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.z + reach) / cellM));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * cellM - c.x, dz = z * cellM - c.z;
                        float along = Mathf.Abs(dx * t.x + dz * t.z), lat = Mathf.Abs(dx * r.x + dz * r.z);
                        float w = SetPieces.SPINE_LINE_W * (1f - Smooth(half - 2f, half, along)) * (1f - Smooth(SetPieces.SPINE_HALF - 1f, SetPieces.SPINE_HALF, lat));
                        if (w > m[z, x]) m[z, x] = w;
                    }
            }
            foreach (float a in arcs)
            {
                Vector3 c = WorldLayout.PointAtArc(local, a), t = WorldLayout.TangentAtArc(local, a);
                t = new Vector3(t.x, 0f, t.z).normalized;
                Vector3 r = WorldLayout.RightOf(t);
                float hw = def != null ? StageDefinitions.HalfWidthAt(def, a) : RUNWAY_M;
                float reach = hw + LINE_BAND_FADE_M + 2f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - reach) / cellM)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.x + reach) / cellM));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - reach) / cellM)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.z + reach) / cellM));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * cellM - c.x, dz = z * cellM - c.z;
                        float along = Mathf.Abs(dx * t.x + dz * t.z), lat = Mathf.Abs(dx * r.x + dz * r.z);
                        float w = (1f - Smooth(LINE_BAND_M * 0.5f, LINE_BAND_M * 0.5f + LINE_BAND_FADE_M, along)) * (1f - Smooth(hw - 0.5f, hw + 0.5f, lat));
                        if (w > m[z, x]) m[z, x] = w;
                    }
            }
            return m;
        }

        /// <summary>道の上の砂を消す（踏み固めた土が 0.5 を超える所は 0、その外は 1 − 重み を掛ける）。sand と trail は同じ解像度。</summary>
        /// <summary>
        /// art-r4-05：砂漠の道の上の砂を消す。踏み固めた土が 0.5 を超える所だけ 0、その外（縁の半分の外）は砂 1 のまま
        /// （道の中心から 半幅 + SAND_KEEP_TO_M まで、そこから SAND_KEEP_FADE_M でもとの砂へ）。土（DirtDunes）の帯を道の脇に見せない。
        /// dist・hwMap は TrailMask の出力。砂漠以外は前の ClearSandOnTrail と同じ。
        /// </summary>
        public static void ClearSandOnTrail(float[,] sand, float[,] trail, int kind, float[,] dist, float[,] hwMap)
        {
            if (kind != KIND_DUNES || dist == null || hwMap == null) { ClearSandOnTrail(sand, trail); return; }
            if (sand == null || trail == null) return;
            int n = Mathf.Min(sand.GetLength(0), trail.GetLength(0)), m = Mathf.Min(sand.GetLength(1), trail.GetLength(1));
            for (int z = 0; z < n; z++)
                for (int x = 0; x < m; x++)
                {
                    if (trail[z, x] > 0.5f) { sand[z, x] = 0f; continue; }
                    float d = dist[z, x];
                    if (d == float.MaxValue) continue;
                    float hw = hwMap[z, x];
                    float keep = 1f - Smooth(hw + SAND_KEEP_TO_M, hw + SAND_KEEP_TO_M + SAND_KEEP_FADE_M, d);
                    if (keep > sand[z, x]) sand[z, x] = keep;
                }
        }

        /// <summary>道の縁で見えてしまう土の重み（地形の塗りの土 (1 − 砂) × (1 − 道)、art-r4-05 のテスト・Validator 用）。</summary>
        public static float ExposedDirt(float sand, float trail) => Mathf.Clamp01(1f - sand) * Mathf.Clamp01(1f - trail);

        public static void ClearSandOnTrail(float[,] sand, float[,] trail)
        {
            if (sand == null || trail == null) return;
            int n = Mathf.Min(sand.GetLength(0), trail.GetLength(0)), m = Mathf.Min(sand.GetLength(1), trail.GetLength(1));
            for (int z = 0; z < n; z++)
                for (int x = 0; x < m; x++)
                    sand[z, x] = trail[z, x] > 0.5f ? 0f : sand[z, x] * (1f - trail[z, x]);
        }

        /// <summary>深い砂（system-r2-01）が道の中心線から何 m まで覆うか（柵 28m の外まで）。端の 6m でなめらかに戻す。</summary>
        public const float DEEP_SAND_REACH_M = 30f, DEEP_SAND_FADE_M = 6f;
        /// <summary>深い砂の区間に残す踏み跡の濃さ（踏み固めた道の重みに掛ける。0.5 未満なので砂の上の轍に見える）。</summary>
        public const float DEEP_SAND_TRAIL_KEEP = 0.25f;

        /// <summary>
        /// 深い砂の区間（StageDef.sandTrailArcs）だけ、道の上も砂にする（ClearSandOnTrail の後で呼ぶ）。sand と trail は同じ解像度（Terrain 全体）、
        /// courseLocal は Terrain ローカル（m）のサンプル点。区間の中の点から DEEP_SAND_REACH_M 以内を砂 1（端はなめらかに）、踏み跡は薄く。
        /// </summary>
        public static void ApplyDeepSand(float[,] sand, float[,] trail, StageDef def, Vector3[] courseLocal, float sizeM)
        {
            if (sand == null || def == null || def.sandTrailArcs == null || courseLocal == null || courseLocal.Length < 2) return;
            int n = sand.GetLength(0), m = sand.GetLength(1);
            float cell = sizeM / (n - 1);
            int r = Mathf.CeilToInt(DEEP_SAND_REACH_M / cell) + 1;
            float acc = 0f;
            for (int i = 0; i < courseLocal.Length; i++)
            {
                if (i > 0) acc += WorldLayout.DistXZ(courseLocal[i - 1], courseLocal[i]);
                if (!StageDefinitions.InDeepSand(def, acc)) continue;
                Vector3 p = courseLocal[i];
                int cx = Mathf.RoundToInt(p.x / cell), cz = Mathf.RoundToInt(p.z / cell);
                for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(n - 1, cz + r); z++)
                    for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(m - 1, cx + r); x++)
                    {
                        float dx = x * cell - p.x, dz = z * cell - p.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > DEEP_SAND_REACH_M) continue;
                        float w = 1f - Smooth(DEEP_SAND_REACH_M - DEEP_SAND_FADE_M, DEEP_SAND_REACH_M, d);
                        if (w > sand[z, x]) sand[z, x] = w;
                        if (trail != null && z < trail.GetLength(0) && x < trail.GetLength(1))
                            trail[z, x] *= Mathf.Lerp(1f, DEEP_SAND_TRAIL_KEEP, w);
                    }
            }
        }

        public static float MaxSlopeDegAlong(float[,] h, float terrainSizeM, float heightM, Vector3[] coursePointsNormalized)
        {
            int n = h.GetLength(0);
            float cellM = terrainSizeM / (n - 1);
            float max = 0f;
            if (coursePointsNormalized == null) return max;
            for (int i = 0; i < coursePointsNormalized.Length; i++)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt(coursePointsNormalized[i].x * (n - 1) + 0.5f), 0, n - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(coursePointsNormalized[i].z * (n - 1) + 0.5f), 0, n - 1);
                int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(n - 1, x + 1);
                int z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(n - 1, z + 1);
                float gx = (h[z, x1] - h[z, x0]) * heightM / ((x1 - x0) * cellM);
                float gz = (h[z1, x] - h[z0, x]) * heightM / ((z1 - z0) * cellM);
                float deg = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                if (deg > max) max = deg;
            }
            return max;
        }

        /// <summary>Sand coverage [0,1]. For CANYON, pass the generated heights to confine sand to the valley floor.</summary>
        public const float MESA_SAND_RADIUS_M = 30f;    // まわり 60m（半径 30m）
        public const float MESA_SAND_LOW_SHARE = 0.35f, MESA_SAND_MAX_SLOPE = 12f;

        /// <summary>
        /// 境目の砂（art-r3-07）：点 (u, v)（0〜1）の地面が、まわり半径 MESA_SAND_RADIUS_M の 16 点のうち何割より低いか（q）。
        /// 低い方 35%（q ≥ 0.65）を smoothstep(0.45, 0.55, q − 0.15) で砂にし、斜面 12° 以上は砂にしない。
        /// </summary>
        public static float MesaBasinSand(float[,] h, float u, float v)
        {
            int n = h.GetLength(0);
            float S = TERRAIN_SIZE_M;
            float px = u * S, pz = v * S;
            float h0 = SampleM(h, px, pz);
            int higher = 0;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f, rr = (i % 2 == 0) ? MESA_SAND_RADIUS_M : MESA_SAND_RADIUS_M * 0.5f;
                if (SampleM(h, px + Mathf.Sin(a) * rr, pz + Mathf.Cos(a) * rr) > h0 + 0.05f) higher++;
            }
            float q = higher / 16f;
            float e = 4f;
            float gx = (SampleM(h, px + e, pz) - SampleM(h, px - e, pz)) / (2f * e);
            float gz = (SampleM(h, px, pz + e) - SampleM(h, px, pz - e)) / (2f * e);
            float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            return Smooth(0.45f, 0.55f, q - (1f - MESA_SAND_LOW_SHARE) + 0.5f) * (1f - Smooth(MESA_SAND_MAX_SLOPE - 2f, MESA_SAND_MAX_SLOPE + 2f, slope));
        }

        // ---- art-r4-06：砂の形 ----
        public const float CANYON_SAND_FROM = 0.45f, CANYON_SAND_TO = 0.80f;
        // 案の 0.70〜0.85・斜面 2〜12° では境目の地面の 1.7% しか砂にならず、帯も短い（主成分の比 2.1）。帯を 0.62〜0.88、斜面 1.5〜16° に広げた（比 5.5、4.4%）
        public const float MESA_BAND_M = 6f, MESA_BAND_FROM = 0.62f, MESA_BAND_TO = 0.88f, MESA_BAND_SOFT = 0.04f;
        public const float MESA_BAND_MIN_SLOPE = 1.5f, MESA_BAND_MAX_SLOPE = 16f;   // 平ら（1.5° 未満）は帯にならず大きな塊になるので砂にしない
        public const int MESA_BAND_BLUR_PASSES = 2;      // 3×3 を 2 回（約 4m）

        /// <summary>境目の砂（art-r4-06）：高さ / 6m の小数部が 0.62〜0.88 の帯、斜面 1.5〜16°。等高線に沿って細長い帯になる。</summary>
        public static float MesaContourSand(float[,] h, float u, float v)
        {
            float px = u * TERRAIN_SIZE_M, pz = v * TERRAIN_SIZE_M;
            float hm = SampleM(h, px, pz);
            float f = hm / MESA_BAND_M - Mathf.Floor(hm / MESA_BAND_M);
            float band = Smooth(MESA_BAND_FROM - MESA_BAND_SOFT, MESA_BAND_FROM + MESA_BAND_SOFT, f) * (1f - Smooth(MESA_BAND_TO - MESA_BAND_SOFT, MESA_BAND_TO + MESA_BAND_SOFT, f));
            float e = 4f;
            float gx = (SampleM(h, px + e, pz) - SampleM(h, px - e, pz)) / (2f * e);
            float gz = (SampleM(h, px, pz + e) - SampleM(h, px, pz - e)) / (2f * e);
            float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            return band * Smooth(MESA_BAND_MIN_SLOPE - 0.5f, MESA_BAND_MIN_SLOPE + 0.5f, slope) * (1f - Smooth(MESA_BAND_MAX_SLOPE - 2f, MESA_BAND_MAX_SLOPE + 2f, slope));
        }

        /// <summary>3×3 の平均（端は内側だけ）。</summary>
        public static float[,] Blur3(float[,] a)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            var r = new float[n, m];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < m; x++)
                {
                    float s = 0f; int c = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int zz = z + dz, xx = x + dx;
                            if (zz < 0 || xx < 0 || zz >= n || xx >= m) continue;
                            s += a[zz, xx]; c++;
                        }
                    r[z, x] = s / c;
                }
            return r;
        }

        /// <summary>高さマップ（0〜1）の点 (x, z)（m）の高さ（m、双線形）。</summary>
        static float SampleM(float[,] h, float x, float z)
        {
            int n = h.GetLength(0);
            float cell = TERRAIN_SIZE_M / (n - 1);
            float fx = Mathf.Clamp(x / cell, 0f, n - 1.001f), fz = Mathf.Clamp(z / cell, 0f, n - 1.001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float a = h[z0, x0] + (h[z0, x0 + 1] - h[z0, x0]) * tx;
            float b = h[z0 + 1, x0] + (h[z0 + 1, x0 + 1] - h[z0 + 1, x0]) * tx;
            return (a + (b - a) * tz) * HEIGHT_M;
        }

        public static float[,] SandMask(int resolution, int kind, int seed, float[,] heights = null)
        {
            int n = resolution;
            var m = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1), v = z / (float)(n - 1);
                    float val;
                    if (kind == KIND_DUNES)
                    {
                        float q = Noise(u * 5f, v * 5f, seed, 7);
                        val = 1f - 0.4f * Smooth(0.65f, 0.85f, q);
                    }
                    else if (kind == KIND_CANYON)
                    {
                        float q = Noise(u * 6f, v * 6f, seed, 3);
                        val = Smooth(CANYON_SAND_FROM, CANYON_SAND_TO, q);   // art-r4-06：やわらかい縁（前の 0.55〜0.68 は硬いしみ）
                        if (heights != null)
                        {
                            int hz = Mathf.Clamp(Mathf.FloorToInt(v * (heights.GetLength(0) - 1) + 0.5f), 0, heights.GetLength(0) - 1);
                            int hx = Mathf.Clamp(Mathf.FloorToInt(u * (heights.GetLength(1) - 1) + 0.5f), 0, heights.GetLength(1) - 1);
                            val *= 1f - Smooth(FLOOR_H, WALL_H, heights[hz, hx]);
                        }
                    }
                    else if (heights != null)
                    {
                        // art-r4-06：等高線の帯（6m ごとの段の下の帯 70〜85%、斜面 2〜12°）。くぼ地の塊（MesaBasinSand）はしみに見えた
                        val = MesaContourSand(heights, u, v);
                    }
                    else
                    {
                        // band just below each terrace edge
                        float frac = MesaFrac(u, v, seed);
                        val = Smooth(0.45f, 0.7f, frac) * (1f - Smooth(0.9f, 1f, frac));
                    }
                    m[z, x] = Mathf.Clamp01(val);
                }
            if (kind == KIND_MESA && heights != null)
                for (int pass = 0; pass < MESA_BAND_BLUR_PASSES; pass++) m = Blur3(m);
            return m;
        }

        // ---- 柵の線の土手（course-r2-06）：見えない壁の足元を 1.2m 盛り上げる（壁がどこにあるか地面で分かる）----
        public const float BERM_M = 1.2f, BERM_HALF_M = 1.5f, BERM_FADE_M = 2.5f;

        /// <summary>土手の高さ（m）：柵の線からの距離 d。BERM_HALF_M までは BERM_M、その外 BERM_FADE_M でなめらかに 0。</summary>
        public static float BermAt(float d) => BERM_M * (1f - Smooth(BERM_HALF_M, BERM_HALF_M + BERM_FADE_M, d));

        /// <summary>柵の区間（Terrain ローカル、[a, b]）に沿って土手を盛る（h を書き換える。下げはしない）。</summary>
        public static void RaiseBerm(float[,] h, float sizeM, float heightM, System.Collections.Generic.List<Vector3[]> segments)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            float reach = BERM_HALF_M + BERM_FADE_M;
            var add = new float[n, n];
            foreach (var sg in segments)
            {
                Vector3 a = sg[0], b = sg[1];
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach) / cell));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = WorldLayout.DistToSegmentXZ(new Vector3(x * cell, 0f, z * cell), a, b);
                        if (d >= reach) continue;
                        float v = BermAt(d);
                        if (v > add[z, x]) add[z, x] = v;
                    }
            }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (add[z, x] > 0f) h[z, x] = Mathf.Clamp01(h[z, x] + add[z, x] / heightM);
        }

        // ---- 外周の尾根（地形の端を見せない）----
        public const float RIM_WIDTH_M = 55f;        // 外周から内側へこの幅で盛り上げる（コースは端から 60m 以上内側）
        public const float RIM_MIN_M = 25f;          // 端での盛り上げの最小（m）
        public const float RIM_MAX_M = 60f;          // 端での盛り上げの最大（m、ノイズで 25〜60m）
        public const float RIM_COURSE_KEEP_M = 25f;  // コースからこの距離までは触らない（2 倍の距離までなめらかに戻す）

        /// <summary>
        /// 外周 rimWidthM の帯を盛り上げて尾根・崖の壁にする（h を書き換える）。端では元の地形より rimMinM〜rimMaxM 高く
        /// （ノイズで高さが変わるので一直線の壁に見えない）、内側へなめらかに 0 へ戻る。coursePointsNormalized を渡すと
        /// コースから RIM_COURSE_KEEP_M m 以内は変えない（本コースの勾配は変わらない）。
        /// </summary>
        public static void ApplyRim(float[,] h, float terrainSizeM, float heightM, float rimWidthM, float rimMinM, float rimMaxM,
            int seed, Vector3[] coursePointsNormalized)
        {
            int n = h.GetLength(0);
            float cell = terrainSizeM / (n - 1);
            Vector3[] course = null;
            if (coursePointsNormalized != null && coursePointsNormalized.Length > 0)
            {
                int step = Mathf.Max(1, coursePointsNormalized.Length / 400);
                course = new Vector3[(coursePointsNormalized.Length + step - 1) / step];
                for (int i = 0, k = 0; i < coursePointsNormalized.Length; i += step, k++)
                    course[k] = coursePointsNormalized[i] * terrainSizeM;
            }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float px = x * cell, pz = z * cell;
                    float d = RimDistance(px, pz, terrainSizeM);
                    if (d >= rimWidthM) continue;
                    float w = Smooth(0f, 1f, 1f - d / rimWidthM);
                    if (course != null)
                    {
                        float dc = float.MaxValue;
                        for (int i = 0; i < course.Length; i++)
                        {
                            float dx = course[i].x - px, dz = course[i].z - pz;
                            float dd = dx * dx + dz * dz;
                            if (dd < dc) dc = dd;
                        }
                        dc = Mathf.Sqrt(dc);
                        w *= Smooth(RIM_COURSE_KEEP_M, RIM_COURSE_KEEP_M * 2f, dc);
                        if (w <= 0f) continue;
                    }
                    float big = Noise(px / 140f, pz / 140f, seed, 21);
                    float fine = Noise(px / 35f, pz / 35f, seed, 22);
                    float rise = rimMinM + (rimMaxM - rimMinM) * Mathf.Clamp01(0.8f * big + 0.2f * fine);
                    rise *= RimHeightFactor(px, pz, seed);
                    h[z, x] = Mathf.Clamp01(h[z, x] + w * rise / heightM);
                }
            SmoothSteep(h, terrainSizeM, heightM, course);
        }

        // ---- art-r4-17：急な壁ののこぎりの歯（高さマップの段）をならす ----
        public const float STEEP_BLUR_SLOPE_DEG = 45f;
        public const int STEEP_BLUR_PASSES = 2;

        /// <summary>
        /// 斜面 45° 以上のマスだけ 3×3 の平均を 2 回（art-r4-17）。コース（course、Terrain ローカル m の間引いた点）から RIM_COURSE_KEEP_M 以内は触らない。
        /// 峡谷の壁・外周の尾根の頂の段の付いた歯を丸める（道の高さ・勾配は変わらない）。
        /// </summary>
        public static void SmoothSteep(float[,] h, float terrainSizeM, float heightM, Vector3[] course)
        {
            int n = h.GetLength(0);
            float cell = terrainSizeM / (n - 1);
            float tan = Mathf.Tan(STEEP_BLUR_SLOPE_DEG * Mathf.Deg2Rad);
            var mask = new bool[n, n];
            for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    float gx = (h[z, x + 1] - h[z, x - 1]) * heightM / (2f * cell);
                    float gz = (h[z + 1, x] - h[z - 1, x]) * heightM / (2f * cell);
                    if (gx * gx + gz * gz < tan * tan) continue;
                    if (course != null)
                    {
                        float px = x * cell, pz = z * cell, dc = float.MaxValue;
                        for (int i = 0; i < course.Length; i++)
                        {
                            float dx = course[i].x - px, dz = course[i].z - pz;
                            float dd = dx * dx + dz * dz;
                            if (dd < dc) dc = dd;
                        }
                        if (dc < RIM_COURSE_KEEP_M * RIM_COURSE_KEEP_M) continue;
                    }
                    mask[z, x] = true;
                }
            for (int pass = 0; pass < STEEP_BLUR_PASSES; pass++)
            {
                var src = (float[,])h.Clone();
                for (int z = 1; z < n - 1; z++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        if (!mask[z, x]) continue;
                        float s = 0f;
                        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) s += src[z + dz, x + dx];
                        h[z, x] = s / 9f;
                    }
            }
        }

        // ---- 外周の尾根の形（art-r2-09）：角を丸め、高さを大きくうねらせる（四角いお盆に見せない）----
        public const float RIM_CORNER_RADIUS_M = 180f;   // 角の丸み（角の近くは尾根が内側へ回り込む）
        public const float RIM_HEIGHT_PERIOD_M = 250f;   // 尾根の高さのうねりの周期
        public const float RIM_HEIGHT_VAR = 0.4f;        // ±40%（鞍部と峰）

        /// <summary>角を丸めた正方形 [0, size] の縁から内側への距離（m）。角の丸みの外（角の三角）は負。</summary>
        public static float RimDistance(float px, float pz, float sizeM)
        {
            float r = Mathf.Min(RIM_CORNER_RADIUS_M, sizeM * 0.5f);
            float cx = Mathf.Clamp(px, r, sizeM - r), cz = Mathf.Clamp(pz, r, sizeM - r);
            float dx = px - cx, dz = pz - cz;
            float e = Mathf.Sqrt(dx * dx + dz * dz);
            return r - e;
        }

        /// <summary>尾根の高さの倍率（1 ± RIM_HEIGHT_VAR、周期 RIM_HEIGHT_PERIOD_M のノイズ）：尾根の線に鞍部と峰を作る。</summary>
        public static float RimHeightFactor(float px, float pz, int seed)
            => 1f + RIM_HEIGHT_VAR * (2f * Noise(px / RIM_HEIGHT_PERIOD_M, pz / RIM_HEIGHT_PERIOD_M, seed, 23) - 1f);

        /// <summary>
        /// ApplyRim の長方形の Terrain 版（ロビー用。TerrainData.size の x = sizeXM、z = sizeZM、高さマップは n × n）。
        /// 4 辺それぞれから rimWidthM m の帯を rimMinM〜rimMaxM 持ち上げる（コースの除外なし）。
        /// </summary>
        public static void ApplyRimRect(float[,] h, float sizeXM, float sizeZM, float heightM, float rimWidthM, float rimMinM, float rimMaxM, int seed)
        {
            int n = h.GetLength(0);
            float cellX = sizeXM / (n - 1), cellZ = sizeZM / (n - 1);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float px = x * cellX, pz = z * cellZ;
                    float d = Mathf.Min(Mathf.Min(px, pz), Mathf.Min(sizeXM - px, sizeZM - pz));
                    if (d >= rimWidthM) continue;
                    float w = Smooth(0f, 1f, 1f - d / rimWidthM);
                    float big = Noise(px / 140f, pz / 140f, seed, 21);
                    float fine = Noise(px / 35f, pz / 35f, seed, 22);
                    float rise = rimMinM + (rimMaxM - rimMinM) * Mathf.Clamp01(0.8f * big + 0.2f * fine);
                    h[z, x] = Mathf.Clamp01(h[z, x] + w * rise / heightM);
                }
        }

        // ---- internals ----

        static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Noise(float x, float y, int seed, int salt)
        {
            float o = seed * 17.31f + salt * 5.7f;
            return ValueNoise(x + o, y - o * 0.61f + salt);
        }

        // Private deterministic value noise in [0,1]; identical in Unity and the test project.
        static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            float top = a + (b - a) * u, bot = c + (d - c) * u;
            return Mathf.Clamp01(top + (bot - top) * v);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static float Fbm(float u, float v, int seed, float freq)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * Noise(u * freq, v * freq, seed, o);
                norm += amp; amp *= 0.5f; freq *= 2f;
            }
            return sum / norm;
        }

        static float MesaFrac(float u, float v, int seed)
        {
            float t = Fbm(u, v, seed, 2f) * 4f;
            return t - Mathf.Floor(t);
        }

        static float Raw(int kind, int seed, float u, float v)
        {
            if (kind == KIND_CANYON)
            {
                float n = Fbm(u, v, seed, 3f);
                return 0.30f + 0.25f * n;              // walls 60–110 m (course-r1-13: lower so the sky shows); path is carved down to the valley floor
            }
            if (kind == KIND_DUNES)
            {
                // 稜線はコースの行に対して斜め（course-r1-03）：コースは稜線を越えて走る
                float w = Mathf.Sin((u * 0.6f + v * 0.8f) * 38f + Noise(u * 2f, v * 2f, seed, 1) * 6f) * 0.5f + 0.5f;
                return 0.22f + 0.12f * w + 0.06f * Fbm(u, v, seed, 6f);
            }
            {
                float t = Fbm(u, v, seed, 2f) * 4f;
                float f = Mathf.Floor(t), frac = t - f;
                return 0.2f + 0.7f * (f + Smooth(0.75f, 1f, frac)) / 4f;
            }
        }

        /// <summary>コースの弧長 s（m）での道の高さ（0〜1）：基準の高さ＋小さなうねり（プロファイル無しの地形用）。</summary>
        public static float PathHeight(int kind, int seed, float s, float total) => PathHeight(kind, seed, s);

        /// <summary>ステージの道の高さ（0〜1）：基準の高さ＋道の高さの形（StageDef.elevArc/elevM、m）＋小さなうねり。</summary>
        public static float PathHeight(StageDef def, float s)
            => PathHeight(def.kind, def.seed, s) + StageDefinitions.ElevationAt(def, s) / HEIGHT_M;

        /// <summary>ステージの基準の高さ（0〜1）。荒野は 40m 下る分だけ低く（壁が空を隠しすぎない）。</summary>
        public static float BaseHeight(int kind) => kind == KIND_CANYON ? 0.12f : (kind == KIND_DUNES ? 0.28f : 0.35f);

        /// <summary>決まった値ノイズ（0〜1）。外の地面（WorldBounds.FillerBaseHeight）など他の生成でも同じ値を使う。</summary>
        public static float Noise01(float x, float y, int seed, int salt) => Noise(x, y, seed, salt);

        static float PathHeight(int kind, int seed, float s)
        {
            // course-r3-09：スタートの 200m はうねりをスタートの値にそろえる（広く平らなスタート、START_OPEN_M の先 250m でもとのうねりへ）
            float u0 = Undulation(kind, seed, 0f);
            float w = Mathf.Clamp01((s - StageDefinitions.START_OPEN_M * 0.5f) / 300f);
            return BaseHeight(kind) + u0 + (Undulation(kind, seed, s) - u0) * w * w * (3f - 2f * w);
        }

        static float Undulation(int kind, int seed, float s)
        {
            float p1 = seed * 0.37f, p2 = seed * 0.91f + 1.3f;
            float h = 0.015f * (0.6f * Mathf.Sin(s / 300f + p1) + 0.4f * Mathf.Sin(s / 130f + p2));
            if (kind == KIND_DUNES) h += 0.004f * Mathf.Sin(s / 25f + p1);
            return h;
        }
    }
}
