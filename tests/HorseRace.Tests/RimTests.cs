using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class RimTests
    {
        static float[,] Rimmed(int i, out float[,] raw, out Vector3[] norm)
        {
            var s = StageDefinitions.All[i];
            var local = StageDefinitions.SampleLocal(s);
            norm = StageDefinitions.Normalize(local);
            raw = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, norm);
            var h = (float[,])raw.Clone();
            HeightmapGen.ApplyRim(h, 1000f, 200f, HeightmapGen.RIM_WIDTH_M, HeightmapGen.RIM_MIN_M, HeightmapGen.RIM_MAX_M, s.seed, norm);
            return h;
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void EdgesRaisedByRimHeight(int i)
        {
            var h = Rimmed(i, out var raw, out _);
            int n = h.GetLength(0);
            for (int k = 0; k < n; k++)
                foreach (var (z, x) in new[] { (0, k), (n - 1, k), (k, 0), (k, n - 1) })
                {
                    float want = Mathf.Min(1f, raw[z, x] + HeightmapGen.RIM_MIN_M * (1f - HeightmapGen.RIM_HEIGHT_VAR) / 200f);
                    Assert.GreaterOrEqual(h[z, x], want - 1e-4f, $"edge ({x},{z})");
                }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void CourseCorridorUnchanged(int i)
        {
            var h = Rimmed(i, out var raw, out var norm);
            foreach (var p in norm)
            {
                float a = WorldLayout.SampleHeight(raw, 1000f, 200f, p.x * 1000f, p.z * 1000f);
                float b = WorldLayout.SampleHeight(h, 1000f, 200f, p.x * 1000f, p.z * 1000f);
                Assert.AreEqual(a, b, 0.01f);
            }
            Assert.LessOrEqual(HeightmapGen.MaxSlopeDegAlong(h, 1000f, 200f, norm), 25f);
        }

        [Test] public void InteriorUntouchedAndDeterministic()
        {
            var h1 = Rimmed(1, out var raw, out _);
            var h2 = Rimmed(1, out _, out _);
            CollectionAssert.AreEqual(h1, h2);
            Assert.AreEqual(raw[256, 256], h1[256, 256]);
            Assert.AreEqual(raw[100, 100], h1[100, 100]);   // 端から 195m
        }

        [Test] public void RimIsNotAFlatWall()
        {
            var h = Rimmed(1, out var raw, out _);
            float min = float.MaxValue, max = float.MinValue;
            for (int x = 0; x < 513; x++) { float r = (h[0, x] - raw[0, x]) * 200f; min = Mathf.Min(min, r); max = Mathf.Max(max, r); }
            Assert.Greater(max - min, 8f);
        }
    }
}
