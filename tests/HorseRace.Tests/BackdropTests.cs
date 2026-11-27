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
            Assert.That(b.Count, Is.InRange(70, 200));
            // 崖の板は遊べる範囲から 260m 以上だけ（art-r3-10：霧で柔らかくなる距離）。2 列（内側 320m・外側 520m 付近）
            foreach (var p in b)
                if (p.asset == DressingPlan.CLIFFS)
                    foreach (var r in WorldBounds.PlayableRects()) Assert.GreaterOrEqual(WorldBounds.DistToRect(p.position, r), DressingPlan.CLIFF_FAR_M, p.part);
            var u = WorldBounds.Union();
            int outer = 0;
            foreach (var p in b) if (WorldBounds.DistToRect(p.position, u) > 470f) outer++;
            Assert.Greater(outer, b.Count / 4, "外側の列");
            var rects = WorldBounds.PlayableRects();
            Vector4 f = WorldBounds.FillerRect();
            foreach (var p in b)
            {
                foreach (var r in rects)
                    Assert.GreaterOrEqual(WorldBounds.DistToRect(p.position, r), p.radius + 50f, p.asset);
                Assert.IsFalse(p.collider);
                Assert.That(p.scale, Is.InRange(1.2f, 10f));
                // 足元は外の地面に埋まる（高さの 25%）
                Assert.AreEqual(WorldBounds.FILLER_Y + WorldBounds.FillerBaseHeight(p.position.x, p.position.z) - WorldBounds.BACKDROP_SINK * p.height, p.position.y, 0.01f);
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

        /// <summary>外の地面：遊べる範囲のすぐ外はほぼ平ら、山並みの方へ 20m 以上上る（無限の平らな板にしない）。</summary>
        [Test] public void FillerTerrainRisesTowardBackdrop()
        {
            var u = WorldBounds.Union();
            float near = WorldBounds.FillerBaseHeight(u.x - 10f, (u.y + u.w) * 0.5f);
            Assert.Less(Mathf.Abs(near), 1f);
            float far = WorldBounds.FillerBaseHeight(u.x - 600f, (u.y + u.w) * 0.5f);
            Assert.Greater(far, 15f);
            for (float x = u.x - 700f; x < u.z + 700f; x += 97f)
                Assert.That(WorldBounds.FillerBaseHeight(x, u.y - 300f), Is.InRange(-7f, 70f));
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
