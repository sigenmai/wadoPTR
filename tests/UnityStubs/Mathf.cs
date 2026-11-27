using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Exp(float p) => (float)Math.Exp(p);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 360f / (PI * 2f);
        public const float Deg2Rad = (PI * 2f) / 360f;
        public static float Tan(float v) => (float)Math.Tan(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Round(float f) => (float)Math.Round(f);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);

        // Deterministic value noise (hash-based, bilinear smoothstep); output in [0,1].
        public static float PerlinNoise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            float top = a + (b - a) * u, bot = c + (d - c) * u;
            return Clamp01(top + (bot - top) * v);
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
    }
}
