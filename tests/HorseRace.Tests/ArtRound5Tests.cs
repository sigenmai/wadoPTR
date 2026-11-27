using System.Collections.Generic;
using System.Linq;
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

        // ---- art-r5-04：切り通しの壁は 3 段の岩の重なり ----
        [Test] public void PassWallIsStackedRockTiers()
        {
            var b = Stage(2);
            var wall = b.plan.Where(p => p.group == "WallFace").ToList();
            int dark = wall.Count(p => p.tint == ArtPalette.WallTierDark), light = wall.Count(p => p.tint == ArtPalette.WallTierLight);
            TestContext.WriteLine($"wall {wall.Count}: light tiers {light}, dark tiers {dark}");
            Assert.Greater(dark, 8, "中の段（暗い縞）");
            Assert.Greater(light, 16, "下と上の段（明るい縞）");
            Assert.IsFalse(wall.Any(p => p.asset == DressingPlan.DROCKS && p.height < 2f), "2m 未満の岩のかけらは置かない");
            float pass = SetPieces.PassArc(b.def, b.goal);
            var lights = DressingPlan.PassWallLights(b.def, b.local, b.goal);
            Assert.AreEqual(2, lights.Count);
            int side = DressingPlan.SunFacingWallSide(b.def, b.local, pass);
            foreach (var l in lights)
            {
                float a = DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), l, out _);
                Vector3 c = WorldLayout.PointAtArc(b.local, a), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(b.local, a));
                Assert.Greater(((l.x - c.x) * r.x + (l.z - c.z) * r.z) * side, 0f, "日の当たる側の壁");
            }
            Assert.AreEqual(25f, DressingPlan.PASS_WALL_LIGHT_RANGE);
        }

        // ---- art-r5-05：ステージの色の境目はまっすぐな線にならない ----
        [Test] public void StageColourSeamIsIrregular()
        {
            for (int si = 0; si < 3; si++)
            {
                Vector3 o = StageDefinitions.All[si].Origin;
                float S = StageDefinitions.SIZE;
                var dists = new List<float>();
                for (int edge = 0; edge < 4; edge++)
                    for (float t = 25f; t < S; t += 50f)
                    {
                        Vector3 c, n;
                        if (edge == 0) { c = new Vector3(o.x + t, 0f, o.z); n = new Vector3(0f, 0f, -1f); }
                        else if (edge == 1) { c = new Vector3(o.x + S, 0f, o.z + t); n = new Vector3(1f, 0f, 0f); }
                        else if (edge == 2) { c = new Vector3(o.x + t, 0f, o.z + S); n = new Vector3(0f, 0f, 1f); }
                        else { c = new Vector3(o.x, 0f, o.z + t); n = new Vector3(-1f, 0f, 0f); }
                        // 縁の内 160m から外 500m へ歩いて、重みが 0.5 を下回る所
                        for (float d = -160f; d <= 500f; d += 5f)
                        {
                            Vector3 q = c + n * d;
                            if (WorldBounds.StageColorWeight(si, q.x, q.z) < 0.5f) { dists.Add(d); break; }
                        }
                    }
                float mean = 0f; foreach (float d in dists) mean += d; mean /= dists.Count;
                float v = 0f; foreach (float d in dists) v += (d - mean) * (d - mean);
                float sd = Mathf.Sqrt(v / dists.Count);
                TestContext.WriteLine($"stage {si}: seam transition mean {mean:F0} m, stddev {sd:F1} m ({dists.Count} samples)");
                Assert.Greater(dists.Count, 60);
                Assert.Greater(sd, 25f, "色の境目は不規則");
                // 縁の両側で同じ色（ステージと外の地面が同じ式で塗る）＝ 段にならない。奥はステージの土の色
                Color inner = WorldBounds.GroundTint(o.x + S * 0.5f, o.z + S * 0.5f);
                Assert.AreEqual(ArtPalette.Dirt(si).r, inner.r, 0.02f);
            }
            // 遠くは外の地面の色
            Color far = WorldBounds.GroundTint(-20000f, -20000f);
            Assert.AreEqual(ArtPalette.DirtOuter.g, far.g, 1e-3f);
        }

        // ---- art-r5-13 / art-r5-07：騎手の目の手前の細かい物・砂漠の道の縁の点線 ----
        [TestCase(2)] [TestCase(0)] [TestCase(1)]
        public void DenseNearFieldAtPassAndCorners(int i)
        {
            var b = Stage(i);
            var cum = DressingPlan.CourseArcs(b.local);
            var corners = StageDefinitions.TightCorners(b.def, b.local);
            float pass = SetPieces.PassArc(b.def, b.goal);
            int windows = Mathf.CeilToInt(b.goal / DressingPlan.BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in b.plan)
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out _);
                if (DressingPlan.RoadClearance(b.def, b.local, cum, p.position) > DressingPlan.NEAR_FIELD_M) continue;
                int k = Mathf.FloorToInt(a / DressingPlan.BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            int dense = 0;
            for (int k = 0; k < windows; k++)
            {
                float c = (k + 0.5f) * DressingPlan.BAND_WINDOW_M;
                if (c > b.goal - 60f || c < 80f) continue;
                bool isDense = (pass >= 0f && Mathf.Abs(c - pass) < SetPieces.PASS_HALF_CUT) || corners.Exists(q => c >= q.fromArc && c <= q.toArc);
                if (!isDense) continue;
                dense++;
                Assert.GreaterOrEqual(counts[k], DressingPlan.NEAR_PER_100M_DENSE, $"stage {i} window {k * 100}-{k * 100 + 100} m");
            }
            TestContext.WriteLine($"stage {i}: {dense} dense windows");
            Assert.Greater(b.plan.Count(p => p.group == "Shoulder" && WorldLayout.DistToPolylineXZ(p.position, b.local) - StageDefinitions.HalfWidthAt(b.def, DressingPlan.NearestArc(b.local, cum, p.position, out _)) > DressingPlan.SHOULDER_BAND2_MIN - 0.5f), 20, "2 本目の帯");
            if (i == 1)
            {
                int dots = b.plan.Count(p => p.group == "Shoulder" && p.asset == DressingPlan.RCOLL);
                Assert.Greater(dots, (int)(b.goal / DressingPlan.SHOULDER_STEP_M * 0.5f), "砂漠の道の縁の点線");
            }
            Assert.AreEqual(0.13f, ArtPalette.GRAIN_AMP, 1e-4f);
            Assert.AreEqual(0.11f, ArtPalette.SCUFF_AMP, 1e-4f);
        }

        // ---- art-r5-08 / 09：岩の橋の門・踏み切りの漏斗と旗・近道の入口 ----
        [Test] public void SetPieceEntriesAreFramed()
        {
            Assert.LessOrEqual(SetPieces.TRENCH_LIGHT_RANGE, 8f);
            Assert.LessOrEqual(SetPieces.TRENCH_LIGHT_INTENSITY, 1f);
            Assert.Greater(SetPieces.TrenchLightColor.r, SetPieces.TrenchLightColor.g * 2f, "危険は赤（黄白くしない）");
            Assert.AreEqual(0.8f, SetPieces.BRIDGE_GATE_SCALE, 1e-4f);
            var f = SetPieces.JumpFunnel();
            Assert.Greater(f.Count, 10);
            float first = f.Min(v => v.x), last = f.Max(v => v.x);
            Assert.AreEqual(-SetPieces.JUMP_FUNNEL_END_BEFORE_M, last, 0.01f, "縁の 25m 手前で漏斗が閉じる");
            float wideLat = f.Where(v => v.x == first).Max(v => Mathf.Abs(v.y)), narrowLat = f.Where(v => v.x == last).Max(v => Mathf.Abs(v.y));
            Assert.Greater(wideLat, narrowLat + 3f, "2 列が寄る");
            Assert.LessOrEqual(narrowLat, SetPieces.KICKER_CORE_M, "跳ぶ線の幅へ");
            Assert.AreEqual(15f, SetPieces.SHORTCUT_SMOKE_M, 1e-4f);
            ArtPalette.ToHSV(new Color(0.2f, 0.9f, 0.85f), out float h, out _, out _);
            Assert.That(h, Is.InRange(160f, 190f), "加速 = 青緑");
        }

        // ---- art-r5-15：遠くのごま塩を減らし、カーブの外に大きなシルエット ----
        [TestCase(0)] [TestCase(2)]
        public void FewerFarPebblesMoreCornerMasses(int i)
        {
            var b = Stage(i);
            int farSmall = b.plan.Count(p => DressingPlan.IsSmallRock(p) && !DressingPlan.LineGroups.Contains(p.group) && WorldLayout.DistToPolylineXZ(p.position, b.local) >= DressingPlan.FAR_PEBBLE_ROAD_M);
            int masses = b.plan.Count(p => p.group == "CornerSilhouette" && p.height >= DressingPlan.CORNER_CLUSTER_H_MIN - 0.01f && p.height <= DressingPlan.CORNER_CLUSTER_H_MAX + 0.01f);
            TestContext.WriteLine($"stage {i}: far small rocks {farSmall}, corner masses {masses}");
            Assert.Greater(masses, 3);
            Assert.AreEqual(1.4f, CourseMarks.SECTOR_POST_THICK_K, 1e-4f);
        }

        // ---- art-r5-14：補色の空と地平線の帯 ----
        [Test] public void SkiesHaveHorizonBands()
        {
            for (int i = 0; i < 2; i++)
            {
                var l = LightingPlan.ForStage(i);
                ArtPalette.ToHSV(l.skyTint, out float h, out float sat, out _);
                Assert.Greater(sat, 0.5f, "昼の空は彩度の高い青");
                Assert.That(h, Is.InRange(200f, 230f));
                Assert.AreEqual(0.6f, l.atmosphere, 1e-4f);
            }
            var d = LightingPlan.ForStage(2);
            Assert.AreEqual(1.6f, d.atmosphere, 1e-4f);
            Assert.AreEqual(0.12f, d.sunSize, 1e-4f);
            Assert.Greater(d.fogColor.r, d.fogColor.b + 0.2f, "夕景の地平線は暖かい");
            Assert.Greater(d.skyTint.b, d.skyTint.g, "天頂は紫");
            Assert.AreEqual(d.fogColor, LightingPlan.SkyHorizon(2));
        }
    }
}
