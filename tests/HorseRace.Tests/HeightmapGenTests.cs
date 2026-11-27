using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class HeightmapGenTests
    {
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static float Mean(float[,] m)
        {
            double s = 0; int n = 0;
            foreach (var v in m) { s += v; n++; }
            return (float)(s / n);
        }

        // Winding course in [0,1]^2 (x/z), densely sampled (~4 m at 1000 m terrain).
        static Vector3[] Course()
        {
            var ctrl = new[]
            {
                V(0.10f,0,0.15f), V(0.30f,0,0.10f), V(0.50f,0,0.25f), V(0.70f,0,0.20f), V(0.85f,0,0.40f),
                V(0.70f,0,0.55f), V(0.45f,0,0.50f), V(0.25f,0,0.65f), V(0.40f,0,0.85f), V(0.70f,0,0.90f)
            };
            return CoursePath.Sample(ctrl, 0.004f);
        }

        [Test] public void Deterministic() => CollectionAssert.AreEqual(HeightmapGen.Generate(129, 0, 7, Course()), HeightmapGen.Generate(129, 0, 7, Course()));

        [Test] public void InRange([Values(0, 1, 2)] int k)
        {
            foreach (var v in HeightmapGen.Generate(129, k, 1, Course())) Assert.That(v, Is.InRange(0f, 1f));
        }

        [Test] public void Rideable([Values(0, 1, 2)] int k)
        {
            var h = HeightmapGen.Generate(513, k, 3, Course());
            Assert.LessOrEqual(HeightmapGen.MaxSlopeDegAlong(h, 1000, 200, Course()), 25f);
        }

        [Test] public void DunesMostlySand()
        {
            var m = HeightmapGen.SandMask(129, HeightmapGen.KIND_DUNES, 1);
            Assert.Greater(Mean(m), 0.8f);
        }

        [Test] public void CanyonPartlySand()
        {
            float a = Mean(HeightmapGen.SandMask(129, HeightmapGen.KIND_CANYON, 1));
            Assert.That(a, Is.InRange(0.05f, 0.4f));
        }
    }
}
