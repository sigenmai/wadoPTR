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
        public const float TRAIL_FULL_INSET_M = 2f;   // 平らな道の縁の 2m 内側まで踏み固めた土
        public const float TRAIL_FADE_OUT_M = 4f;     // 縁の 4m 外で 0

        /// <summary>道の中心からの距離 d（m）、平らな道の半幅 hw での踏み固めた土の重み（0〜1）。</summary>
        public static float TrailWeight(float d, float hw) => 1f - Smooth(hw - TRAIL_FULL_INSET_M, hw + TRAIL_FADE_OUT_M, d);

        /// <summary>踏み固めた土の重み（[z,x]、resolution × resolution、Terrain 全体）。道幅は StageDef の幅の区間に従う。</summary>
        public static float[,] TrailMask(int resolution, StageDef def, Vector3[] coursePointsNormalized)
        {
            int n = resolution;
            var m = new float[n, n];
            if (coursePointsNormalized == null || coursePointsNormalized.Length < 2) return m;
            float cellM = TERRAIN_SIZE_M / (n - 1);
            float maxHw = RUNWAY_M;
            if (def != null && def.halfWidthM != null) foreach (float hw in def.halfWidthM) maxHw = Mathf.Max(maxHw, hw);
            float reach = maxHw + TRAIL_FADE_OUT_M + 1f;
            var dist = new float[n, n];
            var arc = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) dist[z, x] = float.MaxValue;
            int radius = Mathf.CeilToInt(reach / cellM) + 1;
            float s0 = 0f;
            for (int i = 1; i < coursePointsNormalized.Length; i++)
            {
                Vector3 a = coursePointsNormalized[i - 1] * TERRAIN_SIZE_M, b = coursePointsNormalized[i] * TERRAIN_SIZE_M;
                float segLen = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
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
                            if (d < dist[z, x]) { dist[z, x] = d; arc[z, x] = s0 + segLen * t; }
                        }
                }
                s0 += segLen;
            }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (dist[z, x] < reach)
                        m[z, x] = TrailWeight(dist[z, x], def != null ? StageDefinitions.HalfWidthAt(def, arc[z, x]) : RUNWAY_M);
            return m;
        }

        /// <summary>道の上の砂を消す（踏み固めた土が 0.5 を超える所は 0、その外は 1 − 重み を掛ける）。sand と trail は同じ解像度。</summary>
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
                        val = Smooth(0.55f, 0.68f, q);
                        if (heights != null)
                        {
                            int hz = Mathf.Clamp(Mathf.FloorToInt(v * (heights.GetLength(0) - 1) + 0.5f), 0, heights.GetLength(0) - 1);
                            int hx = Mathf.Clamp(Mathf.FloorToInt(u * (heights.GetLength(1) - 1) + 0.5f), 0, heights.GetLength(1) - 1);
                            val *= 1f - Smooth(FLOOR_H, WALL_H, heights[hz, hx]);
                        }
                    }
                    else
                    {
                        // band just below each terrace edge
                        float frac = MesaFrac(u, v, seed);
                        val = Smooth(0.45f, 0.7f, frac) * (1f - Smooth(0.9f, 1f, frac));
                    }
                    m[z, x] = Mathf.Clamp01(val);
                }
            return m;
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
            float p1 = seed * 0.37f, p2 = seed * 0.91f + 1.3f;
            float h = BaseHeight(kind) + 0.015f * (0.6f * Mathf.Sin(s / 300f + p1) + 0.4f * Mathf.Sin(s / 130f + p2));
            if (kind == KIND_DUNES) h += 0.004f * Mathf.Sin(s / 25f + p1);
            return h;
        }
    }
}
