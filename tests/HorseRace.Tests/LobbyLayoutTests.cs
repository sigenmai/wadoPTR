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
            var practice = LobbyLayout.PRACTICE_CENTER;             // 練習場の中心、半径 28m
            // 触れる物・人が立つ所（スポーン・台帳・手配書・鐘・掲示板・柵の馬・待機場所・新聞売り）は柵から 10m 以上
            var features = new[] { new Vector3(0, 0, -5), new Vector3(-8, 0, 12), new Vector3(8, 0, 14), new Vector3(0, 0, -18),
                new Vector3(30, 0, -40), new Vector3(-38.5f, 0, -26), new Vector3(-38.5f, 0, 7f), new Vector3(-30, 0, -45), new Vector3(42, 0, -36) };
            // 背景・普段は出ていない物（厩舎 x -53〜-47、予備の遺物の置き場）は柵の内側に 2m 以上
            var background = new[] { new Vector3(-53, 0, -17), new Vector3(-53, 0, -3), new Vector3(-50, 0, -53), new Vector3(-38, 0, -53) };
            foreach (var s in segs)
            {
                Vector3 m = (s[0] + s[1]) * 0.5f;
                Assert.Greater(WorldLayout.DistXZ(m, practice), LobbyLayout.PRACTICE_RADIUS_M + 5f);
                foreach (var f in features) Assert.Greater(WorldLayout.DistXZ(m, f), 10f);
            }
            foreach (var b in background) Assert.Less(LobbyLayout.DistOutside(new Vector4(LobbyLayout.FENCE_RECT.x + 2f, LobbyLayout.FENCE_RECT.y + 2f,
                LobbyLayout.FENCE_RECT.z - 2f, LobbyLayout.FENCE_RECT.w - 2f), b), 1e-4f, b.ToString());
        }

        /// <summary>柵の長方形を町に合わせて広げる：入っていれば変えない、はみ出す辺だけ 8m の余白まで広げる、最大を超えない。</summary>
        [Test] public void FitFenceRectGrowsOnlyWhenNeeded()
        {
            var b = LobbyLayout.FENCE_RECT;
            var r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 189f), 8f, out bool grown, out bool fits);
            Assert.IsFalse(grown); Assert.IsTrue(fits); Assert.AreEqual(b, r);
            r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 205f), 8f, out grown, out fits);
            Assert.IsTrue(grown); Assert.IsTrue(fits);
            Assert.AreEqual(213f, r.w, 1e-4); Assert.AreEqual(b.x, r.x); Assert.AreEqual(b.y, r.y); Assert.AreEqual(b.z, r.z);
            r = LobbyLayout.FitFenceRect(b, new Vector4(-41f, 25f, 41f, 400f), 8f, out grown, out fits);
            Assert.IsTrue(grown); Assert.IsFalse(fits);
            Assert.LessOrEqual(r.w, LobbyLayout.FENCE_MAX_RECT.w);
            Assert.That(LobbyLayout.FENCE_MAX_RECT.z, Is.GreaterThan(b.z));   // 既定の柵は最大の内側
        }

        [Test] public void GrownFenceKeepsBeltOutsideAndOutsideHidden()
        {
            var f = LobbyLayout.FitFenceRect(LobbyLayout.FENCE_RECT, new Vector4(-41f, 25f, 41f, 205f), 8f, out _, out _);
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
            Assert.That(belt.Count, Is.InRange(LobbyLayout.MinBelt(LobbyLayout.FENCE_RECT), LobbyLayout.MAX_BELT));
            Assert.GreaterOrEqual(LobbyLayout.MinBelt(LobbyLayout.FENCE_RECT), 250);
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
            for (float x = f.x + 4f; x <= f.z - 4f; x += 38f)
                for (float z = f.y + 4f; z <= f.w - 4f; z += 40f)
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
    
        /// <summary>ロビー全体は町くらい：柵の面積 ≤ 45,000 m²、町（想定の外形）が 8m の余白で入る、地面 = 柵 + 38m。</summary>
        [Test] public void LobbyIsAboutTownSized()
        {
            var f = LobbyLayout.FENCE_RECT;
            TestContext.WriteLine($"fence {f.z - f.x} x {f.w - f.y} = {LobbyLayout.Area(f)} m2");
            Assert.LessOrEqual(LobbyLayout.Area(f), 45000f);
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            LobbyLayout.FitFenceRect(f, t, LobbyLayout.TOWN_MARGIN_M, out bool grown, out bool fits);
            Assert.IsFalse(grown); Assert.IsTrue(fits);
            var g = LobbyLayout.GROUND_RECT;
            Assert.AreEqual(f.x - 38f, g.x, 1e-4); Assert.AreEqual(f.y - 38f, g.y, 1e-4);
            Assert.AreEqual(f.z + 38f, g.z, 1e-4); Assert.AreEqual(f.w + 38f, g.w, 1e-4);
            // ワールドの遊べる範囲（背景・地面の板の計算）もロビーの地面と同じ長方形
            var lr = WorldBounds.LobbyRect();
            Vector3 c = WorldBounds.LOBBY_CENTER;
            Assert.AreEqual(c.x + g.x, lr.x, 1e-3); Assert.AreEqual(c.z + g.y, lr.y, 1e-3);
            Assert.AreEqual(c.x + g.z, lr.z, 1e-3); Assert.AreEqual(c.z + g.w, lr.w, 1e-3);
            // 柵を最大まで広げても木の帯（柵の外 10m ＋樹冠）は地面の内側
            var m = LobbyLayout.FENCE_MAX_RECT;
            Assert.LessOrEqual(g.x, m.x - 10f); Assert.GreaterOrEqual(g.w, m.w + 10f);
        }

        /// <summary>練習場の円（中心 PRACTICE_CENTER、半径 28m）は柵の中、町の箱と広場の外。</summary>
        [Test] public void PracticeCircleInsideFenceOutsideTownAndPlaza()
        {
            var f = LobbyLayout.FENCE_RECT;
            var c = LobbyLayout.PRACTICE_CENTER;
            float r = LobbyLayout.PRACTICE_RADIUS_M;
            Assert.GreaterOrEqual(c.x - r, f.x); Assert.LessOrEqual(c.x + r, f.z);
            Assert.GreaterOrEqual(c.z - r, f.y); Assert.LessOrEqual(c.z + r, f.w);
            Assert.Greater(LobbyLayout.DistOutside(LobbyLayout.TOWN_NOMINAL_RECT, c), r);
            var plaza = new Vector4(-15f, -45f, 15f, 23f);   // 受付・手配書・鐘・レバー・待機場所
            Assert.Greater(LobbyLayout.DistOutside(plaza, c), r);
        }
    }
}
