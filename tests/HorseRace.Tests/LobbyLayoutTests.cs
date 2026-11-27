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
            // 触れる物・人が立つ所はすべて町の中：柵から 10m 以上
            foreach (var s in segs)
            {
                Vector3 m = (s[0] + s[1]) * 0.5f;
                foreach (var f in LobbyLayout.FunctionalSpots())
                    Assert.Greater(WorldLayout.DistXZ(m, new Vector3(f.Value.x, 0f, f.Value.z)), 10f, f.Key);
            }
        }

        /// <summary>柵の長方形を町に合わせて広げる：入っていれば変えない、はみ出す辺だけ 8m の余白まで広げる、最大を超えない。</summary>
        [Test] public void FitFenceRectGrowsOnlyWhenNeeded()
        {
            var b = LobbyLayout.FENCE_RECT;
            var r = LobbyLayout.FitFenceRect(b, TOWN_MEASURED, 8f, out bool grown, out bool fits);
            Assert.IsFalse(grown); Assert.IsTrue(fits); Assert.AreEqual(b, r);
            r = LobbyLayout.FitFenceRect(b, new Vector4(TOWN_MEASURED.x, TOWN_MEASURED.y, TOWN_MEASURED.z, 205f), 8f, out grown, out fits);
            Assert.IsTrue(grown); Assert.IsTrue(fits);
            Assert.AreEqual(213f, r.w, 1e-4); Assert.AreEqual(b.x, r.x); Assert.AreEqual(b.y, r.y); Assert.AreEqual(b.z, r.z);
            r = LobbyLayout.FitFenceRect(b, new Vector4(TOWN_MEASURED.x, TOWN_MEASURED.y, TOWN_MEASURED.z, 400f), 8f, out grown, out fits);
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
            Assert.GreaterOrEqual(LobbyLayout.MinBelt(LobbyLayout.FENCE_RECT), 150);
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
    
        // ------------------------------------------------------------------
        // Blender で WesternTown.fbx を測った値（tools/preview と同じ置き方：南端 z = 25、東西の中心 x = 0、通りの高さ 13.71m を沈める）。
        // 町の中の「通りより 0.35m 以上高いメッシュ」の 1m 格子の占有を連結成分に分け、その外接の箱（minX, minZ, maxX, maxZ）。
        // 線路（高さ 0.35m 未満）は乗り越えられるので入れていない。
        // ------------------------------------------------------------------
        static readonly Vector4 TOWN_MEASURED = new Vector4(-40.587f, 25f, 40.587f, 189.378f);
        static readonly Vector4[] TOWN_FOOTPRINTS =
        {
            // 高さ 0.8m 超（建物・岩・サボテン・柵・岩山）
            new Vector4(4.4f, 32f, 8.4f, 37f), new Vector4(14.4f, 35f, 17.4f, 39f), new Vector4(3.4f, 36f, 4.4f, 37f),
            new Vector4(7.4f, 37f, 12.4f, 42f), new Vector4(-26.6f, 38f, -25.6f, 41f), new Vector4(-21.6f, 38f, -15.6f, 45f),
            new Vector4(5.4f, 38f, 6.4f, 39f), new Vector4(23.4f, 38f, 28.4f, 46f), new Vector4(-29.6f, 41f, -26.6f, 45f),
            new Vector4(6.4f, 43f, 17.4f, 116f), new Vector4(-14.6f, 49f, -4.6f, 68f), new Vector4(26.4f, 54f, 30.4f, 57f),
            new Vector4(31.4f, 60f, 33.4f, 63f), new Vector4(-25.6f, 62f, -23.6f, 65f), new Vector4(23.4f, 62f, 31.4f, 68f),
            new Vector4(-28.6f, 63f, -26.6f, 65f), new Vector4(-15.6f, 70f, -4.6f, 82f), new Vector4(26.4f, 75f, 29.4f, 79f),
            new Vector4(-25.6f, 83f, -20.6f, 88f), new Vector4(-14.6f, 83f, -4.6f, 91f), new Vector4(28.4f, 83f, 31.4f, 86f),
            new Vector4(30.4f, 86f, 32.4f, 88f), new Vector4(-28.6f, 88f, -22.6f, 95f), new Vector4(-16.6f, 93f, -6.6f, 115f),
            new Vector4(-21.6f, 94f, -17.6f, 99f), new Vector4(-31.6f, 98f, -24.6f, 104f), new Vector4(-2.6f, 115f, 5.4f, 133f),
            new Vector4(17.4f, 118f, 23.4f, 119f), new Vector4(26.4f, 118f, 31.4f, 123f), new Vector4(-27.6f, 119f, -20.6f, 127f),
            new Vector4(-15.6f, 123f, -3.6f, 135f), new Vector4(8.4f, 125f, 15.4f, 131f), new Vector4(-35.6f, 140f, 34.4f, 145f),
            new Vector4(7.4f, 148f, 12.4f, 154f), new Vector4(-15.6f, 149f, -12.6f, 152f), new Vector4(23.4f, 149f, 30.4f, 155f),
            new Vector4(-21.6f, 151f, -16.6f, 155f), new Vector4(-37.6f, 156f, 36.4f, 190f), new Vector4(8.4f, 157f, 12.4f, 161f),
            // 0.35〜0.8m の起伏・小物（3 セル以上）
            new Vector4(-35.6f, 27f, -29.6f, 34f), new Vector4(9.4f, 27f, 14.4f, 32f), new Vector4(13.4f, 31f, 19.4f, 46f),
            new Vector4(-6.6f, 32f, 2.4f, 42f), new Vector4(2.4f, 35f, 5.4f, 37f), new Vector4(5.4f, 37f, 7.4f, 40f),
            new Vector4(-21.6f, 42f, -19.6f, 44f), new Vector4(25.4f, 52f, 29.4f, 55f), new Vector4(-30.6f, 54f, -26.6f, 64f),
            new Vector4(25.4f, 56f, 29.4f, 58f), new Vector4(25.4f, 75f, 27.4f, 79f), new Vector4(28.4f, 75f, 30.4f, 78f),
            new Vector4(-25.6f, 98f, -23.6f, 100f), new Vector4(-30.6f, 99f, -26.6f, 102f), new Vector4(-10.6f, 108f, -7.6f, 111f),
            new Vector4(-0.6f, 114f, 3.4f, 116f), new Vector4(11.4f, 148f, 13.4f, 152f), new Vector4(21.4f, 155f, 24.4f, 159f),
            new Vector4(-14.6f, 156f, -10.6f, 157f), new Vector4(7.4f, 157f, 11.4f, 160f), new Vector4(10.4f, 160f, 14.4f, 165f),
            new Vector4(3.4f, 161f, 6.4f, 162f), new Vector4(-35.6f, 162f, -31.6f, 166f), new Vector4(-3.6f, 162f, -0.6f, 163f),
            new Vector4(18.4f, 162f, 22.4f, 165f), new Vector4(31.4f, 165f, 34.4f, 166f),
        };

        /// <summary>ロビー全体＝町：柵の面積 ≤ (町の幅 + 16m) × (町の奥行き + 16m)、町が 8m の余白で入る、地面 = 柵 + 38m。</summary>
        [Test] public void LobbyIsJustTheTown()
        {
            var f = LobbyLayout.FENCE_RECT;
            var t = TOWN_MEASURED;
            float limit = (t.z - t.x + 16f) * (t.w - t.y + 16f);
            TestContext.WriteLine($"fence {f.z - f.x} x {f.w - f.y} = {LobbyLayout.Area(f)} m2 (limit {limit})");
            Assert.LessOrEqual(LobbyLayout.Area(f), limit + 2f);   // 外形を cm に丸めた分
            Assert.AreEqual(t.x, LobbyLayout.TOWN_NOMINAL_RECT.x, 0.01f); Assert.AreEqual(t.w, LobbyLayout.TOWN_NOMINAL_RECT.w, 0.01f);
            LobbyLayout.FitFenceRect(f, t, LobbyLayout.TOWN_MARGIN_M, out bool grown, out bool fits);
            Assert.IsFalse(grown); Assert.IsTrue(fits);
            Assert.GreaterOrEqual(t.x - f.x, LobbyLayout.TOWN_MARGIN_M - 0.01f); Assert.GreaterOrEqual(f.z - t.z, LobbyLayout.TOWN_MARGIN_M - 0.01f);
            Assert.GreaterOrEqual(t.y - f.y, LobbyLayout.TOWN_MARGIN_M - 0.01f); Assert.GreaterOrEqual(f.w - t.w, LobbyLayout.TOWN_MARGIN_M - 0.01f);
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

        /// <summary>
        /// 遊びに使う物（スポーン・台帳・手配書・鐘・レバー・待機場所・柵の馬・たまり場・掲示板・新聞売り・予備の遺物・練習場の物）は
        /// すべて柵の中、町の外形の中（町の端 4m より内側）、奥の見えない壁より南、測った建物・岩・サボテン・起伏の箱の外（半径分）。
        /// </summary>
        [Test] public void FunctionalSpotsOnFreeTownGround()
        {
            var f = LobbyLayout.FENCE_RECT;
            var town = TOWN_MEASURED;
            var spots = LobbyLayout.FunctionalSpots();
            Assert.Greater(spots.Count, 30);
            foreach (var kv in spots)
            {
                var p = new Vector3(kv.Value.x, 0f, kv.Value.z);
                float r = kv.Value.w;
                Assert.That(p.x - r, Is.GreaterThanOrEqualTo(f.x).And.GreaterThanOrEqualTo(town.x + 4f), kv.Key);
                Assert.That(p.x + r, Is.LessThanOrEqualTo(f.z).And.LessThanOrEqualTo(town.z - 4f), kv.Key);
                Assert.That(p.z - r, Is.GreaterThanOrEqualTo(f.y).And.GreaterThanOrEqualTo(town.y + 2f), kv.Key);
                Assert.Less(p.z + r, LobbyLayout.TOWN_BACK_Z - 1f, kv.Key);
                foreach (var b in TOWN_FOOTPRINTS)
                    Assert.Greater(WorldBounds.DistToRect(p, b), r, kv.Key + " が町の " + b + " に重なる");
            }
            // スポーン（VRCWorld のスポーンも同じ）は柵の中
            Assert.AreEqual(0f, LobbyLayout.DistOutside(f, LobbyLayout.SPAWN), 1e-4);
            // 予備の遺物・厩舎も柵の中（厩舎は奥の見えない壁の北＝背景、町の建物と重ならない）
            Assert.AreEqual(0f, LobbyLayout.DistOutside(f, LobbyLayout.STABLE), 1e-4);
            Assert.Greater(LobbyLayout.STABLE.z - 2.8f, LobbyLayout.TOWN_BACK_Z + 2f);
            var stable = new Vector4(LobbyLayout.STABLE.x - 7f, LobbyLayout.STABLE.z - 2.8f, LobbyLayout.STABLE.x + 7f, LobbyLayout.STABLE.z + 2.8f);
            foreach (var b in TOWN_FOOTPRINTS)
                Assert.IsFalse(stable.x < b.z && b.x < stable.z && stable.y < b.w && b.y < stable.w, "厩舎が " + b + " に重なる");
        }

        /// <summary>練習場（トリガーの箱）は町の中の線路の東：柵の中、測った建物の箱と重なるのは給水塔・車止めだけ（乗り越えない障害物として残す）。
        /// releaseRadius は箱の角より外。</summary>
        [Test] public void PracticeLaneInsideTownEastOfTrack()
        {
            var f = LobbyLayout.FENCE_RECT;
            var pr = LobbyLayout.PRACTICE_RECT;
            Assert.GreaterOrEqual(pr.x, f.x); Assert.LessOrEqual(pr.z, f.z); Assert.GreaterOrEqual(pr.y, f.y);
            Assert.LessOrEqual(pr.z, TOWN_MEASURED.z - 4f);
            Assert.LessOrEqual(pr.w, LobbyLayout.TOWN_BACK_Z);
            Assert.GreaterOrEqual((pr.z - pr.x) * (pr.w - pr.y), 800f);   // 17m × 53m
            Vector3 c = LobbyLayout.PRACTICE_CENTER;
            float corner = WorldLayout.DistXZ(c, new Vector3(pr.x, 0f, pr.y));
            Assert.Greater(LobbyLayout.PracticeReleaseRadius, corner);
            Assert.Less(LobbyLayout.PracticeReleaseRadius, corner + 6f);
            foreach (var kv in LobbyLayout.FunctionalSpots())
            {
                if (!kv.Key.StartsWith("Practice")) continue;
                var p = new Vector3(kv.Value.x, 0f, kv.Value.z);
                Assert.AreEqual(0f, WorldBounds.DistToRect(p, pr), 1e-4, kv.Key);
            }
            // 練習場に食い込む町の物は給水塔（高さ 10m）と線路の車止め・サボテンの端だけ
            int inside = 0;
            foreach (var b in TOWN_FOOTPRINTS)
                if (pr.x < b.z && b.x < pr.z && pr.y < b.w && b.y < pr.w) inside++;
            Assert.LessOrEqual(inside, 4);
        }
    }
}
