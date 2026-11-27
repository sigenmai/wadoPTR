using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class BackdropTests
    {
        [Test] public void BackdropRingOutsidePlayableAreas()
        {
            var all = DressingPlan.Backdrop(7);
            var b = all.Where(p => p.group != "BackdropNear").ToList();   // art-r4-01：継ぎ目の輪は縁をまたぐ（StageSeamHidden で確かめる）
            TestContext.WriteLine($"backdrop: {b.Count} + seam {all.Count - b.Count}");
            Assert.That(b.Count, Is.InRange(70, 220));
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

        // ---- art-r4-01：ステージの縁の継ぎ目を隠す ----
        [Test] public void StageSeamHidden()
        {
            var seam = DressingPlan.Backdrop(7).Where(p => p.group == "BackdropNear").ToList();
            var rects = WorldBounds.PlayableRects();
            var built = new[] { ArtRound2Tests.Stage(0), ArtRound2Tests.Stage(1), ArtRound2Tests.Stage(2) };
            System.Func<int, float, float, float> ground = (i, x, z) =>
            {
                if (i >= 3) return 0f;
                var o = built[i].def.Origin;
                return o.y + built[i].Ground(x - o.x, z - o.z);
            };
            float[,] grid = WorldBounds.FillerGrid(WorldBounds.FILLER_RES, rects, ground);
            for (int si = 0; si < 3; si++)
            {
                Vector4 r = rects[si];
                float per = 2f * ((r.z - r.x) + (r.w - r.y));
                int bare = 0, skipped = 0, measured = 0; float worstGap = 0f;
                for (float t0 = 0f; t0 < per; t0 += 100f)
                {
                    bool covered = false;
                    for (float t = t0; t < t0 + 100f && !covered; t += 10f)
                    {
                        Vector3 c = PerimeterPoint(r, t);
                        foreach (var q in seam)
                        {
                            float foot = (q.asset == DressingPlan.MESA || q.asset == DressingPlan.MESAB) ? q.radius * DressingPlan.MESA_GATE_FOOTPRINT : q.radius * 0.85f;
                            if (WorldLayout.DistXZ(q.position, c) <= foot + 30f) { covered = true; break; }
                        }
                    }
                    if (!covered) bare++;
                }
                // 縁の 10m 外の外の地面と、縁の地面の差
                var o = built[si].def.Origin;
                for (float t = 25f; t < per; t += 50f)
                {
                    Vector3 c = PerimeterPoint(r, t), n = OutwardNormal(r, t);
                    Vector3 inPt = c - n * 0.5f, outPt = c + n * 5f;
                    float edge = o.y + built[si].Ground(inPt.x - o.x, inPt.z - o.z);
                    // 縁に沿って半マス（約 5m）で 1m 以上変わる所（ゴールの先の切れ込みの壁・尾根の細かい起伏）は外の地面の格子（10.5m）では追えないので数えない
                    Vector3 tan = new Vector3(-n.z, 0f, n.x);
                    float e1 = o.y + built[si].Ground(inPt.x + tan.x * 5.5f - o.x, inPt.z + tan.z * 5.5f - o.z), e2 = o.y + built[si].Ground(inPt.x - tan.x * 5.5f - o.x, inPt.z - tan.z * 5.5f - o.z);
                    if (Mathf.Abs(e1 - edge) > 1f || Mathf.Abs(e2 - edge) > 1f) { skipped++; continue; }
                    measured++;
                    float filler = WorldBounds.SampleFiller(grid, outPt.x, outPt.z);
                    worstGap = Mathf.Max(worstGap, Mathf.Abs(filler - edge));
                }
                TestContext.WriteLine($"stage {si}: perimeter windows without a seam rock {bare}, worst |filler - edge| {worstGap:F2} m ({measured} measured, {skipped} on steep edges)");
                Assert.GreaterOrEqual(measured, 15, "なめらかな縁で確かめる");
                Assert.AreEqual(0, bare, "100m ごとに縁をまたぐ岩");
                Assert.Less(worstGap, 2f, "外の地面は縁の高さから始まる");
            }
            foreach (var q in seam)
            {
                Assert.IsFalse(q.asset == DressingPlan.CLIFFS, "継ぎ目の輪に崖の板は使わない");
                Assert.IsFalse(q.collider);
            }
        }

        static Vector3 PerimeterPoint(Vector4 u, float t)
        {
            float w = u.z - u.x, d = u.w - u.y, perim = 2f * (w + d);
            t = (t % perim + perim) % perim;
            if (t < w) return new Vector3(u.x + t, 0f, u.y);
            if (t < w + d) return new Vector3(u.z, 0f, u.y + (t - w));
            if (t < 2f * w + d) return new Vector3(u.z - (t - w - d), 0f, u.w);
            return new Vector3(u.x, 0f, u.w - (t - 2f * w - d));
        }

        static Vector3 OutwardNormal(Vector4 u, float t)
        {
            float w = u.z - u.x, d = u.w - u.y, perim = 2f * (w + d);
            t = (t % perim + perim) % perim;
            if (t < w) return new Vector3(0f, 0f, -1f);
            if (t < w + d) return new Vector3(1f, 0f, 0f);
            if (t < 2f * w + d) return new Vector3(0f, 0f, 1f);
            return new Vector3(-1f, 0f, 0f);
        }

        // ---- art-r4-09：崖の板はスポーン・コースから遠い一番外の輪だけ ----
        [Test] public void NoCliffsNearSpawn()
        {
            var cliffs = DressingPlan.Backdrop(7).Where(p => p.asset == DressingPlan.CLIFFS).ToList();
            TestContext.WriteLine($"far cliffs: {cliffs.Count}");
            Assert.Greater(cliffs.Count, 0, "素材は使う（霧の奥だけ）");
            foreach (var p in cliffs) Assert.IsTrue(DressingPlan.CliffFarEnough(p.position), p.part + " " + p.position);
            foreach (var p in LobbyLayout.VistaHeroes(5)) Assert.AreNotEqual(DressingPlan.CLIFFS, p.asset, "見晴らしの切れ目に崖の板");
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
