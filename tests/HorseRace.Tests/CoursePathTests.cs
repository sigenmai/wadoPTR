using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class CoursePathTests
    {
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        [Test] public void EvenSpacing()
        {
            var p = CoursePath.Sample(new[] { V(0, 0, 0), V(100, 0, 0), V(100, 0, 100) }, 10);
            for (int i = 1; i < p.Length - 1; i++) Assert.AreEqual(10f, Vector3.Distance(p[i - 1], p[i]), 0.5f);
        }

        [Test] public void HitsEndpoints()
        {
            var c = new[] { V(0, 0, 0), V(50, 0, 0), V(80, 0, 40) };
            var p = CoursePath.Sample(c, 5);
            Assert.AreEqual(0f, Vector3.Distance(p[0], c[0]), 1e-3);
            Assert.AreEqual(0f, Vector3.Distance(p[p.Length - 1], c[2]), 1e-3);
        }

        [Test] public void LengthOfStraightLine()
        {
            Assert.AreEqual(30f, CoursePath.Length(new[] { V(0, 0, 0), V(10, 0, 0), V(10, 0, 20) }), 1e-3);
        }
    }
}
