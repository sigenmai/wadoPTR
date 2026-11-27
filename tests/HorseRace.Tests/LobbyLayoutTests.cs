using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class LobbyLayoutTests
    {
        [Test] public void FenceIsClosedLoopWithShortPieces()
        {
            var r = LobbyLayout.FENCE_RECT;
            var segs = LobbyLayout.FenceSegments(r, LobbyLayout.Gates, LobbyLayout.FENCE_PIECE_M);
            float total = 0f;
            for (int i = 0; i < segs.Count; i++)
            {
                float len = WorldLayout.DistXZ(segs[i][0], segs[i][1]);
                Assert.LessOrEqual(len, LobbyLayout.FENCE_PIECE_M + 1e-3f);   // 柱の間隔 ≤ 3m
                total += len;
                var next = segs[(i + 1) % segs.Count];
                Assert.Less(WorldLayout.DistXZ(segs[i][1], next[0]), 1e-3f, "切れ目 " + i);
            }
            Assert.AreEqual(2f * ((r.z - r.x) + (r.w - r.y)), total, 0.1f);
            Assert.AreEqual(0, LobbyLayout.Gates.Length);   // 練習場も柵の中：外への出入口なし
        }

        [Test] public void GateLeavesOnlyItsGap()
        {
            var r = new Vector4(0, 0, 30, 30);
            var gate = new LobbyLayout.Gate { side = 0, center = 15f, halfWidth = 3f };
            var segs = LobbyLayout.FenceSegments(r, new[] { gate }, 2.5f);
            int gaps = 0;
            for (int i = 0; i < segs.Count; i++)
                if (WorldLayout.DistXZ(segs[i][1], segs[(i + 1) % segs.Count][0]) > 1e-3f) gaps++;
            Assert.AreEqual(1, gaps);
            foreach (var s in segs) Assert.IsFalse(s[0].z == 0f && s[1].z == 0f && Mathf.Max(s[0].x, s[1].x) > 12f && Mathf.Min(s[0].x, s[1].x) < 18f);
        }

        [Test] public void FenceClearOfLobbyFeatures()
        {
            var segs = LobbyLayout.FenceSegments(LobbyLayout.FENCE_RECT, LobbyLayout.Gates, LobbyLayout.FENCE_PIECE_M);
            var practice = new Vector3(120f, 0f, -40f);             // 練習場の中心、干し草の輪 42m
            var features = new[] { new Vector3(0, 0, -5), new Vector3(-8, 0, 12), new Vector3(8, 0, 14), new Vector3(0, 0, -18),
                new Vector3(30, 0, -40), new Vector3(-38.5f, 0, -26), new Vector3(-62, 0, -10), new Vector3(-150, 0, -100), new Vector3(-30, 0, -45) };
            foreach (var s in segs)
            {
                Vector3 m = (s[0] + s[1]) * 0.5f;
                Assert.Greater(WorldLayout.DistXZ(m, practice), 42f + 5f);
                foreach (var f in features) Assert.Greater(WorldLayout.DistXZ(m, f), 10f);
            }
        }

        /// <summary>柵の長方形を町に合わせて広げる：入っていれば変えない、はみ出す辺だけ 8m の余白まで広げる、最大を超えない。</summary>
        [Test] public void FitFenceRectGrowsOnlyWhenNeeded()
        {
            var b = LobbyLayout.FENCE_RECT;
            var r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 189f), 8f, out bool grown, out bool fits);
            Assert.IsFalse(grown); Assert.IsTrue(fits); Assert.AreEqual(b, r);
            r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 230f), 8f, out grown, out fits);
            Assert.IsTrue(grown); Assert.IsTrue(fits);
            Assert.AreEqual(238f, r.w, 1e-4); Assert.AreEqual(b.x, r.x); Assert.AreEqual(b.y, r.y); Assert.AreEqual(b.z, r.z);
            r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 400f), 8f, out grown, out fits);
            Assert.IsTrue(grown); Assert.IsFalse(fits);
            Assert.LessOrEqual(r.w, LobbyLayout.FENCE_MAX_RECT.w);
            Assert.That(LobbyLayout.FENCE_MAX_RECT.z, Is.GreaterThan(b.z));   // 既定の柵は最大の内側
        }

        [Test] public void GrownFenceKeepsBeltOutsideAndOutsideHidden()
        {
            var f = LobbyLayout.FitFenceRect(LobbyLayout.FENCE_RECT, new Vector4(-41f, 25f, 41f, 230f), 8f, out _, out _);
            var belt = LobbyLayout.TreeBelt(f, 3);
            foreach (var t in belt) Assert.Greater(LobbyLayout.DistOutside(f, t.position), 2f);
            foreach (float az in new[] { 0f, 90f, 180f, 270f })
            {
                float a = az * Mathf.Deg2Rad;
                Assert.IsFalse(LobbyLayout.SeesOutside(f, new Vector3(0f, 3.2f, 200f), new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), belt, 700f, out string what), what);
            }
        }

        [Test] public void TreeBeltIsDenseStaggeredAndOutsideFence()
        {
            var belt = LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT, 3);
            TestContext.WriteLine($"belt {belt.Count}");
            Assert.That(belt.Count, Is.InRange(300, LobbyLayout.MAX_BELT));
            foreach (var t in belt)
            {
                Assert.Greater(LobbyLayout.DistOutside(LobbyLayout.FENCE_RECT, t.position), 2f);
                Assert.That(t.scale, Is.InRange(1f, 1.5f));
                Assert.IsFalse(t.collider);
            }
        }

        /// <summary>柵の中の目の高さ（1.6〜3.2m）から水平〜10° 下向きに見て、柵の外の平らな地面・虚空が見えない。</summary>
        [Test] public void OutsideNotVisibleFromInsideFence()
        {
            var belt = LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT, 3);
            var f = LobbyLayout.FENCE_RECT;
            int rays = 0;
            var hits = new Dictionary<string, int>();
            for (float x = f.x + 4f; x <= f.z - 4f; x += 56f)
                for (float z = f.y + 4f; z <= f.w - 4f; z += 56f)
                    foreach (float eyeH in new[] { 1.6f, 2.4f, 3.2f })
                        for (int az = 0; az < 24; az++)
                            foreach (float dip in new[] { 0f, 2f, 5f, 10f })
                            {
                                float a = az * 15f * Mathf.Deg2Rad, d = dip * Mathf.Deg2Rad;
                                var dir = new Vector3(Mathf.Sin(a) * Mathf.Cos(d), -Mathf.Sin(d), Mathf.Cos(a) * Mathf.Cos(d));
                                bool seen = LobbyLayout.SeesOutside(f, new Vector3(x, eyeH, z), dir, belt, 700f, out string what);
                                hits.TryGetValue(what, out int n); hits[what] = n + 1;
                                rays++;
                                Assert.IsFalse(seen, $"eye ({x},{eyeH},{z}) az {az * 15} dip {dip}: {what}");
                            }
            TestContext.WriteLine($"{rays} rays: " + string.Join(", ", hits));
            // 盛り上がった地面だけでなく、柵と木にも当たっている
            int Hit(string k) => hits.TryGetValue(k, out int n) ? n : 0;
            Assert.Greater(Hit("canopy") + Hit("trunk"), 0);
            Assert.Greater(Hit("fence"), 0);
        }
    }
}
