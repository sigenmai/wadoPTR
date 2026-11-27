using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 4 巡目（review-log の Round 4 — art）の単体テスト。</summary>
    public class ArtRound4Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        // ---- art-r4-05：砂漠の道は暗い帯にしない ----
        [Test] public void DunesTrailIsNotADarkBand()
        {
            float t = LightingPlan.Luminance(ArtPalette.TrailDunes), sand = LightingPlan.Luminance(ArtPalette.Sand(1));
            TestContext.WriteLine($"trail {t:F3} sand {sand:F3}");
            Assert.GreaterOrEqual(t, 0.85f * sand);
            Assert.AreEqual(HeightmapGen.TRAIL_EDGE_DUNES_M, HeightmapGen.TrailEdge(HeightmapGen.KIND_DUNES));
            Assert.AreEqual(HeightmapGen.TRAIL_EDGE_M, HeightmapGen.TrailEdge(HeightmapGen.KIND_CANYON));
        }

        /// <summary>道の縁で土（DirtDunes）が見える幅 &lt; 1.5m：道の横断線ごとに (1 − 砂)(1 − 道) &gt; 0.5 が続く長さ。</summary>
        [Test] public void DunesExposedDirtAtRoadEdgeIsNarrow()
        {
            var b = Stage(1);
            var norm = StageDefinitions.Normalize(b.local);
            int res = StageDefinitions.ALPHAMAP_RES;
            var sand = HeightmapGen.SandMask(res, b.def.kind, b.def.seed, null);
            var trail = HeightmapGen.TrailMask(res, b.def, norm, out float[,] dist, out float[,] hw);
            HeightmapGen.ClearSandOnTrail(sand, trail, b.def.kind, dist, hw);
            HeightmapGen.ApplyDeepSand(sand, trail, b.def, b.local, StageDefinitions.SIZE);
            float cell = StageDefinitions.SIZE / (res - 1);
            float worst = 0f;
            for (float a = 300f; a < b.goal - 300f; a += 50f)
            {
                Vector3 p = WorldLayout.PointAtArc(b.local, a), t = WorldLayout.TangentAtArc(b.local, a), r = WorldLayout.RightOf(t);
                float h = StageDefinitions.HalfWidthAt(b.def, a);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float run = 0f, best = 0f;
                    for (float lat = h - 3f; lat <= h + 8f; lat += 0.25f)
                    {
                        Vector3 q = p + r * (sd * lat);
                        int x = Mathf.Clamp(Mathf.RoundToInt(q.x / cell), 0, res - 1), z = Mathf.Clamp(Mathf.RoundToInt(q.z / cell), 0, res - 1);
                        if (HeightmapGen.ExposedDirt(sand[z, x], trail[z, x]) > 0.5f) { run += 0.25f; best = Mathf.Max(best, run); }
                        else run = 0f;
                    }
                    worst = Mathf.Max(worst, best);
                }
            }
            TestContext.WriteLine($"widest exposed dirt at the dunes road edge: {worst:F2} m");
            Assert.Less(worst, 1.5f);
        }

        // ---- art-r4-06：ステージの主色の砂・境目は等高線の帯 ----
        [Test] public void SandHueFollowsStageDirt()
        {
            for (int i = 0; i < 3; i++)
            {
                ArtPalette.ToHSV(ArtPalette.Sand(i), out float hs, out _, out _);
                ArtPalette.ToHSV(ArtPalette.Dirt(i), out float hd, out _, out _);
                float d = Mathf.Abs(Mathf.DeltaAngle(hs, hd));
                TestContext.WriteLine($"stage {i}: sand hue {hs:F0} dirt hue {hd:F0} → {d:F0}°");
                Assert.LessOrEqual(d, 25f);
            }
            Assert.AreNotEqual(ArtPalette.Sand(0), ArtPalette.Sand(1));
            Assert.AreNotEqual(ArtPalette.Sand(2), ArtPalette.Sand(1));
        }

        [Test] public void BorderSandIsBanded()
        {
            var b = Stage(2);
            const int N = 256;
            var sand = HeightmapGen.SandMask(N, HeightmapGen.KIND_MESA, b.def.seed, b.h);
            var seen = new bool[N, N];
            var ratios = new List<float>();
            var stack = new Stack<int>();
            for (int z0 = 0; z0 < N; z0++)
                for (int x0 = 0; x0 < N; x0++)
                {
                    if (seen[z0, x0] || sand[z0, x0] < 0.5f) continue;
                    var cells = new List<Vector2>();
                    stack.Push(z0 * N + x0); seen[z0, x0] = true;
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop(), z = c / N, x = c % N;
                        cells.Add(new Vector2(x, z));
                        for (int k = 0; k < 4; k++)
                        {
                            int zz = z + (k == 0 ? 1 : k == 1 ? -1 : 0), xx = x + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (zz < 0 || xx < 0 || zz >= N || xx >= N || seen[zz, xx] || sand[zz, xx] < 0.5f) continue;
                            seen[zz, xx] = true; stack.Push(zz * N + xx);
                        }
                    }
                    if (cells.Count < 12) continue;
                    // 主成分の長さの比（細長いほど大きい）
                    float mx = cells.Average(c => c.x), mz = cells.Average(c => c.y);
                    float sxx = cells.Average(c => (c.x - mx) * (c.x - mx)), szz = cells.Average(c => (c.y - mz) * (c.y - mz)), sxz = cells.Average(c => (c.x - mx) * (c.y - mz));
                    float tr = sxx + szz, det = sxx * szz - sxz * sxz, disc = Mathf.Sqrt(Mathf.Max(0f, tr * tr / 4f - det));
                    float l1 = tr / 2f + disc, l2 = Mathf.Max(1e-3f, tr / 2f - disc);
                    ratios.Add(Mathf.Sqrt(l1 / l2));
                }
            float mean = ratios.Count == 0 ? 0f : ratios.Average();
            TestContext.WriteLine($"border sand components {ratios.Count}, mean aspect {mean:F2}");
            Assert.Greater(ratios.Count, 5);
            Assert.GreaterOrEqual(mean, 3f, "等高線に沿った細長い帯（丸いしみにしない）");
        }

        // ---- art-r4-10：空は霧と分ける ----
        [Test] public void SkyContrastsWithFog()
        {
            for (int i = 0; i <= LightingPlan.LOBBY_LIGHT; i++)
            {
                var l = LightingPlan.ForStage(i);
                float d = LightingPlan.Luminance(l.fogColor) - LightingPlan.Luminance(l.skyTint);
                TestContext.WriteLine($"light {i}: fog − sky {d:F3}, sun size {l.sunSize}");
                Assert.GreaterOrEqual(Mathf.Abs(d), LightingPlan.SKY_FOG_CONTRAST_MIN);
                Assert.Greater(l.skyTint.b, l.skyTint.g * (i == 2 ? 1.2f : 1.0f), "天頂は青（境目は紫）");
            }
            Assert.Greater(LightingPlan.ForStage(2).sunSize, LightingPlan.ForStage(0).sunSize, "夕日は大きく");
        }

        /// <summary>境目の夕日は最後の 300m の直線の正面（光の向き = 進む向きの反対、±10°）。</summary>
        [Test] public void DuskSunAheadOnFinalStraight()
        {
            var d = StageDefinitions.All[2];
            var local = StageDefinitions.SampleLocal(d);
            float goal = StageDefinitions.GoalArc(d, local);
            Vector3 t = WorldLayout.PointAtArc(local, goal) - WorldLayout.PointAtArc(local, goal - 300f);
            float travel = WorldLayout.YawDeg(new Vector3(t.x, 0f, t.z).normalized);
            float lightYaw = LightingPlan.ForStage(2).sunEuler.y;
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(lightYaw, travel + 180f)), 10f);
            Assert.That(LightingPlan.ForStage(2).sunEuler.x, Is.InRange(5f, 25f), "低い夕日");
        }
    }
}
