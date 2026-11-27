using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class BackdropTests
    {
        [Test] public void BackdropRingOutsidePlayableAreas()
        {
            var b = DressingPlan.Backdrop(7);
            TestContext.WriteLine($"backdrop: {b.Count}");
            Assert.That(b.Count, Is.InRange(40, 120));
            var rects = WorldBounds.PlayableRects();
            Vector4 f = WorldBounds.FillerRect();
            foreach (var p in b)
            {
                foreach (var r in rects)
                    Assert.GreaterOrEqual(WorldBounds.DistToRect(p.position, r), p.radius + 50f, p.asset);
                Assert.IsFalse(p.collider);
                Assert.That(p.scale, Is.InRange(4f, 8f));
                // 地面の板の上に乗っている（山の外側も空白にならない）
                Assert.That(p.position.x - p.radius, Is.GreaterThan(f.x));
                Assert.That(p.position.x + p.radius, Is.LessThan(f.z));
                Assert.That(p.position.z - p.radius, Is.GreaterThan(f.y));
                Assert.That(p.position.z + p.radius, Is.LessThan(f.w));
            }
        }

        [Test] public void BackdropDeterministic()
        {
            var a = DressingPlan.Backdrop(7);
            var c = DressingPlan.Backdrop(7);
            Assert.AreEqual(a.Count, c.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i].position.x, c[i].position.x);
        }

        [Test] public void FillerCoversEveryPlayableRect()
        {
            Vector4 f = WorldBounds.FillerRect();
            foreach (var r in WorldBounds.PlayableRects())
            {
                Assert.Less(f.x, r.x); Assert.Less(f.y, r.y); Assert.Greater(f.z, r.z); Assert.Greater(f.w, r.w);
            }
        }

        [Test] public void StagesAndLobbyDoNotOverlap()
        {
            var rs = WorldBounds.PlayableRects();
            for (int i = 0; i < rs.Length; i++)
                for (int j = i + 1; j < rs.Length; j++)
                {
                    bool overlap = rs[i].x < rs[j].z && rs[j].x < rs[i].z && rs[i].y < rs[j].w && rs[j].y < rs[i].w;
                    Assert.IsFalse(overlap, $"{i} {j}");
                }
        }
    }
}
