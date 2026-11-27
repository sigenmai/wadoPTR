using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 5 巡目（review-log の Round 5 — art）の単体テスト。</summary>
    public class ArtRound5Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);

        // ---- art-r5-01：轍は 2 本のきれいな線（点線・丸いしみにしない）----

        /// <summary>斜めのまっすぐな道で、テクスチャと同じ手順（符号付きの距離 → 双線形 → 8bit の G → 復号 → 轍の重み）を 0.25m ごとに調べる。</summary>
        [Test] public void RutIsMonotonicAcrossEachTrack()
        {
            const int res = 256;   // 1 マス ≈ 3.9m（実際の 2m より粗い：補間の悪さが出やすい）
            // 斜め 30° のまっすぐな道
            var pts = new List<Vector3>();
            Vector3 a = new Vector3(0.1f, 0f, 0.15f), dir = new Vector3(Mathf.Sin(30f * Mathf.Deg2Rad), 0f, Mathf.Cos(30f * Mathf.Deg2Rad));
            for (int i = 0; i <= 40; i++) pts.Add(a + dir * (i * 0.018f));
            HeightmapGen.TrailMask(res, null, pts.ToArray(), out float[,] dist, out float[,] hw, out float[,] sd);
            float cell = HeightmapGen.TERRAIN_SIZE_M / (res - 1);
            Vector3 right = WorldLayout.RightOf(dir);
            int checkedLines = 0;
            for (float along = 150f; along <= 550f; along += 37f)
            {
                Vector3 c = a * HeightmapGen.TERRAIN_SIZE_M + dir * along;
                var w = new List<float>();
                for (float lat = -5f; lat <= 5f; lat += 0.25f)
                {
                    Vector3 q = c + right * lat;
                    float g = HeightmapGen.EncodeSignedDist(HeightmapGen.SampleBilinear(sd, q.x / cell, q.z / cell));
                    g = Mathf.Round(g * 255f) / 255f;   // 8bit のテクスチャ
                    w.Add(HeightmapGen.RutWeightSigned(HeightmapGen.DecodeSignedDist(g)));
                }
                // 増減の向きが変わる回数：上り → 下り（1 本目）→ 上り → 下り（2 本目）= 3 回だけ（途中の小さなくぼみ・しみが無い）
                int turns = 0, last = 0;
                for (int i = 1; i < w.Count; i++)
                {
                    float d = w[i] - w[i - 1];
                    int s = d > 1e-4f ? 1 : (d < -1e-4f ? -1 : 0);
                    if (s == 0) continue;
                    if (last != 0 && s != last) turns++;
                    last = s;
                }
                float peak = 0f; foreach (float v in w) peak = Mathf.Max(peak, v);
                Assert.AreEqual(3, turns, $"rut profile at {along:F0} m has {turns} turns");
                Assert.Greater(peak, 0.9f);
                checkedLines++;
            }
            Assert.Greater(checkedLines, 5);
            Assert.AreEqual(0.86f, ArtPalette.RUT_SHADE, 1e-4f);
        }

        [Test] public void SignedDistanceHasOppositeSignsOnEachSide()
        {
            var path = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 100f) };
            float l = WorldLayout.SignedDistToPolylineXZ(new Vector3(-3f, 0f, 50f), path);
            float r = WorldLayout.SignedDistToPolylineXZ(new Vector3(3f, 0f, 50f), path);
            Assert.AreEqual(3f, Mathf.Abs(l), 1e-3f);
            Assert.AreEqual(-l, r, 1e-3f);
            Assert.AreEqual(0.5f, HeightmapGen.EncodeSignedDist(0f), 1e-5f);
            Assert.AreEqual(2.5f, HeightmapGen.DecodeSignedDist(HeightmapGen.EncodeSignedDist(2.5f)), 1e-4f);
        }

        // ---- art-r5-03：浮かない ----
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void NothingFloats(int i)
        {
            var b = Stage(i);
            var all = new List<Placement>(b.plan);
            all.AddRange(DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc)));
            int bad = 0; string worst = null; float wg = 0f;
            foreach (var p in all)
            {
                float g = DressingPlan.FloatGap(p, b.h, StageDefinitions.SIZE);
                if (g > DressingPlan.GROUNDING_FLOAT_TOL_M) { bad++; if (g > wg) { wg = g; worst = p.group + " " + p.asset + " " + p.part; } }
            }
            TestContext.WriteLine($"stage {i}: {bad} floating, worst {wg:F2} m {worst}");
            Assert.AreEqual(0, bad, worst);
        }

        [Test] public void GroundingRules()
        {
            // 斜面（足元の差 > 0.45 × 高さ）・縁のはみ出し（いちばん低い所が中心より 0.3 × 高さ低い）は置かない。埋める岩・背景・柵の線は沈めるだけ
            Assert.IsTrue(DressingPlan.RejectGrounding("Roadside", false, 4f, 2f, 0f, 0.5f));
            Assert.IsTrue(DressingPlan.RejectGrounding("Roadside", false, 4f, 1.5f, 0f, 1.4f));
            Assert.IsFalse(DressingPlan.RejectGrounding("Roadside", false, 4f, 1f, 0f, 0.5f));
            Assert.IsFalse(DressingPlan.RejectGrounding("WallFace", false, 4f, 3f, 0f, 2f));
            Assert.IsFalse(DressingPlan.RejectGrounding("Roadside", true, 4f, 3f, 0f, 2f));
            Assert.IsFalse(DressingPlan.RejectGrounding("Roadside", false, 12f, 8f, 0f, 2f));
            Assert.IsTrue(DressingPlan.IsTowerPart(DressingPlan.RANCH_TOWER));
            // 縁に立つ塔：外側 8m が 2m 以上低いと置かない
            var h = new float[65, 65];
            for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++) h[z, x] = x < 32 ? 0.1f : 0.05f;   // x = 500m に 10m の段
            Assert.IsTrue(DressingPlan.TowerSiteOk(h, 1000f, new Vector3(300f, 0f, 500f), 3f));
            Assert.IsFalse(DressingPlan.TowerSiteOk(h, 1000f, new Vector3(492f, 0f, 500f), 3f));
        }

        // ---- art-r5-06：白い箱の町にしない ----
        [Test] public void TownColoursAreNeverWhite()
        {
            string[] paths = { "Packs/WesternTown/WesternTown.fbx", "Buildings/TownBlock.fbx", "Buildings/RanchCompound.fbx" };
            var used = new HashSet<Color>();
            foreach (string path in paths)
                for (float r = 0f; r <= 1.001f; r += 0.1f)
                    for (float g = 0f; g <= 1.001f; g += 0.1f)
                        for (float b = 0f; b <= 1.001f; b += 0.1f)
                            for (int n = 0; n < 6; n++)
                            {
                                Color c = ArtPalette.RemapBaseColor(path, new Color(r, g, b), "WesternTown_0" + n + "0_Building");
                                ArtPalette.ToHSV(c, out _, out _, out float v);
                                Assert.LessOrEqual(v, ArtPalette.TOWN_MAX_V + 1e-4f, $"{path} ({r:F1},{g:F1},{b:F1})");
                                if (r > 0.95f && g > 0.95f && b > 0.95f) used.Add(c);
                            }
            Assert.GreaterOrEqual(used.Count, 3, "白い壁は建物ごとに違う板壁の色");
            // 暗い縁取り・窓はそのまま
            var dark = new Color(0.08f, 0.07f, 0.06f);
            Assert.AreEqual(dark, ArtPalette.RemapBaseColor(paths[0], dark, "x"));
            // 画像付きの建物の掛け色
            Assert.LessOrEqual(Mathf.Max(ArtPalette.BuildingTextureTint.r, Mathf.Max(ArtPalette.BuildingTextureTint.g, ArtPalette.BuildingTextureTint.b)), ArtPalette.BUILDING_TEX_MAX + 1e-4f);
            Assert.AreEqual(ArtPalette.StableHash("abc"), ArtPalette.StableHash("abc"));
            foreach (var c in ArtPalette.Sidings)
            {
                ArtPalette.ToHSV(c, out float h, out _, out _);
                Assert.IsFalse(h > 150f && h < 200f, "青緑はブースト専用");
            }
        }

        // ---- art-r5-07：砂漠の道は砂より明るい ----
        [Test] public void DunesRoadIsBrighterThanSand()
        {
            float t = LightingPlan.Luminance(ArtPalette.TrailDunes), sand = LightingPlan.Luminance(ArtPalette.SandLayer);
            TestContext.WriteLine($"trail {t:F3} sand {sand:F3}");
            Assert.GreaterOrEqual(t - sand, ArtPalette.TRAIL_DUNES_ABOVE_SAND);
        }

        // ---- art-r5-02：杭は暗い古い木 ----
        [Test] public void PostsAreDarkWood()
        {
            var st = ArtPalette.StyleFor(DressingPlan.LOGS, DressingPlan.POST_PART);
            Assert.AreEqual(ArtPalette.PostWood, st.tint);
            StringAssert.Contains("_Post", st.key);
            Assert.Less(LightingPlan.Luminance(ArtPalette.PostWood), LightingPlan.Luminance(ArtPalette.Dirt(0)) + 0.02f, "地面より暗い");
            Assert.AreEqual(8, DressingPlan.BARRIER_POST_EVERY);
            for (int i = 0; i < 3; i++)
            {
                var b = Stage(i);
                var line = DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc));
                var all = new List<Placement>(b.plan); all.AddRange(line);
                foreach (var p in all)
                {
                    if (p.part != DressingPlan.POST_PART || p.rollDeg != 90f || p.lamp) continue;
                    float e = 4f, x = p.position.x, z = p.position.z;
                    float gx = (b.Ground(x + e, z) - b.Ground(x - e, z)) / (2f * e), gz = (b.Ground(x, z + e) - b.Ground(x, z - e)) / (2f * e);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    Assert.LessOrEqual(slope, DressingPlan.POST_MAX_SLOPE_DEG + 0.01f, p.group);
                }
            }
        }
    }
}
