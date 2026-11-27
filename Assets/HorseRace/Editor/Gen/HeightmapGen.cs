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
        public const float FALLOFF_M = 90f;    // blend back to raw terrain by this distance

        public static float[,] Generate(int resolution, int kind, int seed, Vector3[] coursePointsNormalized)
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

            int radius = Mathf.CeilToInt(FALLOFF_M / cellM) + 1;
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

            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float d = dist[z, x];
                    if (d >= FALLOFF_M) continue;
                    float w = 1f - Smooth(RUNWAY_M, FALLOFF_M, d);
                    float target = PathHeight(kind, seed, arc[z, x]);
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, w));
                }
            return h;
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

        public static float[,] SandMask(int resolution, int kind, int seed)
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

        // ---- internals ----

        static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Noise(float x, float y, int seed, int salt)
        {
            float o = seed * 17.31f + salt * 5.7f;
            return Mathf.PerlinNoise(x + o, y - o * 0.61f + salt);
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
                return 0.45f + 0.5f * n;               // high walls; path is carved down to the valley floor
            }
            if (kind == KIND_DUNES)
            {
                float w = Mathf.Sin(u * 38f + Noise(u * 2f, v * 2f, seed, 1) * 6f) * 0.5f + 0.5f;
                return 0.22f + 0.12f * w + 0.06f * Fbm(u, v, seed, 6f);
            }
            {
                float t = Fbm(u, v, seed, 2f) * 4f;
                float f = Mathf.Floor(t), frac = t - f;
                return 0.2f + 0.7f * (f + Smooth(0.75f, 1f, frac)) / 4f;
            }
        }

        static float PathHeight(int kind, int seed, float s)
        {
            float p1 = seed * 0.37f, p2 = seed * 0.91f + 1.3f;
            float baseH = kind == KIND_CANYON ? 0.18f : (kind == KIND_DUNES ? 0.28f : 0.35f);
            float h = baseH + 0.03f * (0.6f * Mathf.Sin(s / 300f + p1) + 0.4f * Mathf.Sin(s / 130f + p2));
            if (kind == KIND_DUNES) h += 0.004f * Mathf.Sin(s / 25f + p1);
            return h;
        }
    }
}
