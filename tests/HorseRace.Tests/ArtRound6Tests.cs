using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 6 巡目（review-log の Round 6 — art）の単体テスト。</summary>
    public class ArtRound6Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        static float SlopeDeg(ArtRound2Tests.Built b, Vector3 p)
        {
            const float e = 4f;
            float gx = (b.Ground(p.x + e, p.z) - b.Ground(p.x - e, p.z)) / (2f * e), gz = (b.Ground(p.x, p.z + e) - b.Ground(p.x, p.z - e)) / (2f * e);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        // ---- art-r6-02 → art-r7-02：切り通しの段の岩は地形に埋まらない（前の面が見え、壁から薄いかけらだけが出ない）----
        [Test] public void PassTierRocksAreExposed()
        {
            var b = Stage(2);
            var tiers = b.plan.Where(p => p.group == "WallFace" && p.tint != Color.white).ToList();
            Assert.Greater(tiers.Count, 20, "段の岩");
            GroundFit.Ground g = b.Ground;
            foreach (var p in tiers)
            {
                GroundFit.WallExposure(p, DressingPlan.TowardRoad(b.local, p.position), g, out float front, out float depth);
                Assert.GreaterOrEqual(front, DressingPlan.WALL_SEAT_MIN_FRONT, p.asset + " " + p.part + " " + p.position);
                Assert.GreaterOrEqual(depth, DressingPlan.WALL_SEAT_MIN_DEPTH_M, p.asset + " " + p.part + " " + p.position);
            }
            Assert.AreEqual(7f, SetPieces.PASS_BENCH1_H, 1e-4f);
            Assert.AreEqual(7f, SetPieces.PASS_BENCH2_H, 1e-4f);
        }

        // ---- art-r6-03：道の目印・路肩・道ばたは 25° より急な地面に置かない、切り通しは灯りの列と小石の線 ----
        [TestCaseSource(nameof(Stages))]
        public void RoadsideStaysOnGentleGround(int i)
        {
            var b = Stage(i);
            foreach (var p in b.plan)
            {
                if (!DressingPlan.RoadsideGroups.Contains(p.group)) continue;
                Assert.LessOrEqual(SlopeDeg(b, p.position), DressingPlan.ROADSIDE_MAX_SLOPE_DEG + 0.01f, p.group + " " + p.part + " " + p.position);
            }
            float pass = SetPieces.PassArc(b.def, b.goal);
            if (pass < 0f) return;
            var cum = DressingPlan.CourseArcs(b.local);
            foreach (var p in b.plan.Where(q => q.group == "GuideLine"))
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out float _);
                Assert.GreaterOrEqual(Mathf.Abs(a - pass), DressingPlan.PASS_GUIDE_SKIP_M - 3f, "切り通しの中に柵・杭を立てない " + p.part);
            }
            Assert.Greater(b.plan.Count(q => q.group == "PassLamp"), 10, "切り通しの道の線は灯りの列");
        }

        // ---- art-r6-04：轍は途切れない 2 本の線（しみにしない） ----
        [Test] public void RutDarkeningIsContinuousAlongTheRoad()
        {
            Assert.LessOrEqual(1f - ArtPalette.RUT_SHADE, ArtPalette.RUT_SHADE_MAX_DARKEN + 1e-5f);
            Assert.AreEqual(0.9f, HeightmapGen.RUT_OFFSET_M, 1e-4f);
            Assert.AreEqual(0.35f, HeightmapGen.RUT_WIDTH_M, 1e-4f);
            var b = Stage(0);
            const int res = 2049;
            HeightmapGen.TrailMask(res, b.def, StageDefinitions.Normalize(b.local), out float[,] _, out float[,] _, out float[,] sd);
            float cell = StageDefinitions.SIZE / (res - 1);
            int windows = 0;
            for (float a0 = 200f; a0 < b.goal - 200f; a0 += 150f)
            {
                foreach (float lat in new[] { -HeightmapGen.RUT_OFFSET_M, HeightmapGen.RUT_OFFSET_M })
                {
                    float mn = float.MaxValue, mx = float.MinValue;
                    for (float a = a0; a <= a0 + 20f; a += 0.5f)
                    {
                        Vector3 c = WorldLayout.PointAtArc(b.local, a), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(b.local, a));
                        Vector3 q = c + r * lat;
                        float g = HeightmapGen.EncodeSignedDist(HeightmapGen.SampleBilinear(sd, q.x / cell, q.z / cell));
                        g = Mathf.Round(g * 255f) / 255f;
                        float dk = HeightmapGen.RutDarken(HeightmapGen.DecodeSignedDist(g));
                        mn = Mathf.Min(mn, dk); mx = Mathf.Max(mx, dk);
                    }
                    Assert.LessOrEqual(mx - mn, 0.02f, $"rut at {a0:F0} m lat {lat}");
                    windows++;
                }
            }
            Assert.Greater(windows, 10);
        }

        // ---- art-r6-05：外の地面は縁から尾根で続く（平らな台の縁にしない） ----
        [Test] public void FillerContinuesAsRidgesNearStages()
        {
            Assert.Greater(WorldBounds.FILLER_RIDGE_M, 10f);
            Assert.Greater(WorldBounds.FILLER_EDGE_WOBBLE_M, 30f);
            Assert.Greater(HeightmapGen.RIM_EDGE_WOBBLE_M, 10f);
            // 平らな縁（高さ 10m）から外へ：縁のすぐ外は縁の高さ、100〜200m の帯は場所で 2m 以上ちがう（まっすぐな坂ではない）
            var rects = new[] { new Vector4(0f, 0f, 100f, 100f) };
            System.Func<int, float, float, float> flat = (i, x, z) => 10f;
            Assert.AreEqual(10f - WorldBounds.FILLER_SEAM_M, WorldBounds.FillerHeight(104f, 50f, 8f, 8f, rects, flat), 1e-3f);
            float mn = float.MaxValue, mx = float.MinValue;
            for (float z = -400f; z <= 500f; z += 20f)
            {
                float y = WorldBounds.FillerHeight(250f, z, 8f, 8f, rects, flat);
                mn = Mathf.Min(mn, y); mx = Mathf.Max(mx, y);
            }
            Assert.Greater(mx - mn, 2f, "縁に沿って同じ高さの坂にしない");
            // 縁のマークのまとまりに崖の板（Cliffs）がある（道から 260m 以上）
            var back = DressingPlan.Backdrop(7);
            var cliffs = back.Where(p => p.asset == DressingPlan.CLIFFS).ToList();
            TestContext.WriteLine($"cliffs {cliffs.Count}, at seam {cliffs.Count(p => p.group == "BackdropNear")}");
            Assert.GreaterOrEqual(cliffs.Select(p => p.part).Distinct().Count(), 12, "Cliffs_01〜13 を順に（art-r6-14）");
            foreach (var p in cliffs) Assert.GreaterOrEqual(DressingPlan.CourseDistWorld(p.position) - p.radius * DressingPlan.CLIFF_HALF_DEPTH, DressingPlan.CLIFF_FAR_M - 0.01f);
        }

        // ---- art-r6-06：60° より急な段が残らない ----
        [TestCaseSource(nameof(Stages))]
        public void NoStairStepsAfterCarving(int i)
        {
            var b = Stage(i);
            var keep = SetPieces.KeepMask(b.def, b.local, b.h.GetLength(0), b.goal, b.sc);
            float step = HeightmapGen.MaxNeighbourStep(b.h, HeightmapGen.HEIGHT_M, keep, 2);
            TestContext.WriteLine($"stage {i}: max neighbour step {step:F2} m (all non-kept {HeightmapGen.MaxNeighbourStep(b.h, HeightmapGen.HEIGHT_M, keep, 0):F2} m)");
            Assert.LessOrEqual(step, HeightmapGen.RELAX_MAX_STEP_M + 0.05f);
        }

        // ---- art-r6-07：どのステージも道の縁から 6m に 100m あたり 25 個以上 ----
        [TestCaseSource(nameof(Stages))]
        public void NearFieldDensityAllStages(int i)
        {
            var b = Stage(i);
            var cum = DressingPlan.CourseArcs(b.local);
            int windows = Mathf.CeilToInt(b.goal / DressingPlan.BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in b.plan)
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out float d);
                float edge = d - StageDefinitions.HalfWidthAt(b.def, a);
                if (edge < 0f || edge - p.radius > DressingPlan.NEAR_EDGE_M) continue;
                int k = Mathf.FloorToInt(a / DressingPlan.BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            float br = SetPieces.BridgeArc(b.def, b.goal), jp = SetPieces.JumpArc(b.def, b.goal);
            int checkedW = 0;
            for (int k = 1; k < windows - 1; k++)
            {
                float c = (k + 0.5f) * DressingPlan.BAND_WINDOW_M;
                if (c > b.goal - 60f) continue;
                if (br >= 0f && Mathf.Abs(c - br) < 80f) continue;   // 岩の橋の溝（道の脇は溝）
                if (jp >= 0f && Mathf.Abs(c - jp) < 80f) continue;   // 踏み切りの砂の窪み
                Assert.GreaterOrEqual(counts[k], DressingPlan.NEAR_EDGE_PER_100M, $"stage {i} window {k * 100}-{k * 100 + 100} m");
                checkedW++;
            }
            Assert.Greater(checkedW, 15);
            Assert.Greater(Stage(0).plan.Count(p => p.group == "CornerInside") + Stage(1).plan.Count(p => p.group == "CornerInside" && p.asset == DressingPlan.HAY)
                + Stage(2).plan.Count(p => p.group == "CornerInside" && p.asset == DressingPlan.HAY), 4, "頂点の内側の小さな岩・干し草");
        }

        // ---- art-r6-08：スポーンの足元から通りが始まる ----
        [Test] public void SpawnHasNearProps()
        {
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            var l = DressingPlan.ForLobby(new Vector2(t.x, t.y), new Vector2(t.z, t.w), LobbyLayout.LobbyDressingExclusions(), 4242);
            int n = DressingPlan.SpawnNearCount(l);
            TestContext.WriteLine($"near spawn {n}");
            Assert.GreaterOrEqual(n, DressingPlan.SPAWN_NEAR_MIN);
            Assert.Greater(l.Count(p => p.group == "SpawnBoardwalk"), 6, "板張り");
            Assert.AreEqual(1, l.Count(p => p.group == "SpawnStreet" && p.asset == DressingPlan.HORSE));
            Assert.Greater(l.Count(p => p.group == "SpawnStreet" && p.asset == DressingPlan.WTOWN), 2, "町の小物");
            // 視線の通り道は空ける
            foreach (var p in l.Where(q => q.group == "SpawnStreet" || q.group == "SpawnBoardwalk"))
                Assert.GreaterOrEqual(Mathf.Abs(p.position.x - DressingPlan.SpawnAxisX(p.position.z)) - Mathf.Min(p.radius, 1.5f), DressingPlan.APRON_LANE_HALF_M - 0.01f, p.group + " " + p.part);
        }

        // ---- art-r6-09：町の外れ・区切った柵・まとまった木 ----
        [Test] public void LobbyHasOutskirtsAndBrokenFence()
        {
            var f = LobbyLayout.FENCE_RECT;
            var outs = LobbyLayout.Outskirts();
            Assert.AreEqual(3, outs.Count(q => q.asset == DressingPlan.TENT));
            Assert.AreEqual(1, outs.Count(q => q.asset == DressingPlan.STABLE));
            Assert.AreEqual(1, outs.Count(q => q.part == LobbyLayout.OUT_RANCH_PART));
            Assert.AreEqual(1, outs.Count(q => q.part == LobbyLayout.OUT_HILL_PART));
            Assert.GreaterOrEqual(outs.Count(q => q.part == DressingPlan.RAIL_TRACK), 2);
            var tr = LobbyLayout.TERRAIN_RECT;
            foreach (var q in outs)
            {
                float d = LobbyLayout.DistOutside(f, q.position);
                Assert.Greater(d, 20f, q.group + " は柵の外");
                Assert.IsFalse(q.collider);
                Assert.That(q.position.x, Is.InRange(tr.x, tr.z), q.group);
                Assert.That(q.position.z, Is.InRange(tr.y, tr.w), q.group);
                Assert.Less(LobbyLayout.ApproachCorridorWeight(q.position), 0.01f, "近づく道の上に置かない " + q.group);
            }
            // 柵：北は無し、区間は 3〜4 本
            var segs = LobbyLayout.FenceSegments(f, LobbyLayout.Gates, LobbyLayout.FENCE_PIECE_M);
            var vis = segs.Select(sg => LobbyLayout.FenceVisible(f, (sg[0] + sg[1]) * 0.5f)).ToList();
            Assert.IsFalse(segs.Where((sg, k) => vis[k] && Mathf.Abs((sg[0].z + sg[1].z) * 0.5f - f.w) < 0.5f).Any(), "北の土手に柵");
            int runs = 0;
            for (int k = 0; k < vis.Count; k++) if (vis[k] && !vis[(k - 1 + vis.Count) % vis.Count]) runs++;
            Assert.That(runs, Is.InRange(3, 4));
            Assert.AreEqual(vis.Count(v => v), LobbyLayout.VisibleFenceCount(f));
            // 木の帯は 30〜60m の空きのあるまとまり
            Assert.IsTrue(LobbyLayout.BeltOpen(LobbyLayout.BELT_CLUSTER_MAX_M + 1f) || LobbyLayout.BeltOpen(LobbyLayout.BELT_CLUSTER_MAX_M + LobbyLayout.BELT_OPEN_MIN_M - 1f));
            Assert.IsFalse(LobbyLayout.BeltOpen(1f));
            // 町の地面の色は 25m で外の土へ
            Assert.AreEqual(1f, LobbyLayout.TownGroundWeight(-20f, Vector3.zero), 1e-4f);
            Assert.AreEqual(0f, LobbyLayout.TownGroundWeight(LobbyLayout.TOWN_FEATHER_M + LobbyLayout.TOWN_FEATHER_NOISE_M + 1f, Vector3.zero), 1e-4f);
            Assert.AreEqual(12f, LobbyLayout.PLATE_BLEND_M, 1e-4f);
        }

        // ---- art-r6-10：砂漠の岩はプラスチックの橙にしない ----
        [Test] public void DesertRocksAreNotOverSaturated()
        {
            float raw = ArtPalette.Saturation(ArtPalette.DesertRocksMean);
            for (int k = 1; k <= 9; k++)
            {
                var st = ArtPalette.StyleFor("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_0" + k);
                float s = ArtPalette.Saturation(ArtPalette.Multiply(ArtPalette.DesertRocksMean, st.tint));
                Assert.LessOrEqual(s, ArtPalette.ROCK_MAX_SATURATION, "DesertRocks_0" + k);
                Assert.LessOrEqual(s, raw + 1e-4f, "掛け色で彩度を上げない");
                Assert.AreEqual(ArtPalette.ROCK_FLATTEN, st.flatten, 1e-5f);
            }
            Assert.AreEqual(0.95f, ArtPalette.StyleFor("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_12").tint.r, 1e-4f);
            Assert.AreEqual(7, ArtPalette.PartNumber("DesertRocks_07"));
            Assert.AreEqual(17, ArtPalette.PartNumber("WesternTown_017_Prop"));
        }

        // ---- art-r6-11：砂漠の道は明るいが白く飛ばない ----
        [Test] public void DunesTrailDoesNotClip()
        {
            for (int i = 0; i < 3; i++)
                Assert.LessOrEqual(LightingPlan.LitLuminance(ArtPalette.Trail(i), i), LightingPlan.MAX_LIT_TRAIL_LUMINANCE, "stage " + i);
            Assert.AreEqual(LightingPlan.DUNES_SUN_INTENSITY, LightingPlan.ForStage(1).sunIntensity, 1e-4f);
            Assert.AreEqual(LightingPlan.SUN_INTENSITY, LightingPlan.ForStage(0).sunIntensity, 1e-4f);
            Assert.GreaterOrEqual(LightingPlan.Luminance(ArtPalette.TrailDunes) - LightingPlan.Luminance(ArtPalette.SandLayer), ArtPalette.TRAIL_DUNES_ABOVE_SAND);
            Assert.AreEqual(0.015f, ArtPalette.SAND_NOISE_AMOUNT, 1e-5f);
        }

        // ---- art-r6-12：手配書は 5 種類を順に（1 種類が 40% を超えない） ----
        [TestCaseSource(nameof(Stages))]
        public void PostersAreVaried(int i)
        {
            var b = Stage(i);
            var parts = new List<string>();
            int k = 0;
            foreach (var cp in CourseMarks.FinishChute(b.def, b.local, b.goal)) if (cp.kind == CourseMarks.CHUTE_FLAG) parts.Add(CourseMarks.PosterPart(k++, cp.side));
            int ck = 0;
            foreach (var c in StageDefinitions.TightCorners(b.def, b.local))
            {
                int ci = 0;
                foreach (var ch in CourseMarks.CornerWallChevrons(b.def, b.local, c)) parts.Add(CourseMarks.PosterPart(ci++ + ck * 7, ch.side));
                ck++;
            }
            int gk = 0;
            foreach (var p in b.plan.Where(q => q.group == "GuideLine" && q.part == DressingPlan.POST_PART && Mathf.Abs(q.height - DressingPlan.POST_H) < 0.01f)) parts.Add(CourseMarks.PosterPart(gk, gk++ & 1));
            Assert.Greater(parts.Count, 10);
            foreach (var g in parts.GroupBy(x => x))
                Assert.LessOrEqual(g.Count(), 0.4f * parts.Count, g.Key);
            Assert.GreaterOrEqual(parts.Distinct().Count(), 4);
            for (int j = 0; j < 50; j++)
            {
                CourseMarks.PosterJitter(j, out float yaw, out float roll, out float sc);
                Assert.That(yaw, Is.InRange(-CourseMarks.POSTER_YAW_JITTER, CourseMarks.POSTER_YAW_JITTER));
                Assert.That(roll, Is.InRange(-CourseMarks.POSTER_ROLL_JITTER, CourseMarks.POSTER_ROLL_JITTER));
                Assert.That(sc, Is.InRange(CourseMarks.POSTER_SCALE_MIN, CourseMarks.POSTER_SCALE_MAX));
            }
        }

        // ---- art-r6-13：岩の橋は石の床と板の床、灯りは橋台で弱く ----
        [Test] public void RockBridgeDeckIsStoneNotGlow()
        {
            Assert.AreEqual(0f, SetPieces.SPINE_LINE_W, 1e-5f);
            Assert.LessOrEqual(SetPieces.BRIDGE_LAMP_INTENSITY, 0.8f + 1e-5f);
            Assert.LessOrEqual(SetPieces.BRIDGE_LAMP_RANGE, 8f + 1e-5f);
            Assert.GreaterOrEqual(SetPieces.BRIDGE_LAMP_MIN_H, 3.5f);
            float half = SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND;
            var tiles = SetPieces.BridgeDeckTiles();
            foreach (var t in tiles)
            {
                Assert.Less(Mathf.Abs(t.x), half);
                Assert.Less(Mathf.Abs(t.y), SetPieces.SPINE_HALF - 1f, "背の上");
                Assert.LessOrEqual(Mathf.Abs(t.z), SetPieces.DECK_TILE_YAW_JITTER + 1e-4f);
            }
            Assert.IsTrue(tiles.Any(t => t.y < 0f) && tiles.Any(t => t.y > 0f), "2 列");
            float top = SetPieces.DECK_TILE_H * SetPieces.DECK_TILE_SCALE * SetPieces.DECK_TILE_SCALE_Y * (1f - SetPieces.DECK_TILE_SINK);
            Assert.That(top, Is.InRange(0.05f, 0.35f), "上の面だけが背の上に出る");
        }

        // ---- art-r6-15：メサ B は散らさず、まとまりの目印に ----
        [TestCaseSource(nameof(Stages))]
        public void MesaRockBIsClusteredNotConfetti(int i)
        {
            var b = Stage(i);
            var free = b.plan.Where(p => p.asset == DressingPlan.MESAB && !DressingPlan.MesabExempt.Contains(p.group)).ToList();
            Assert.LessOrEqual(free.Count, DressingPlan.MESAB_FREE_MAX);
            var all = b.plan.Where(p => p.asset == DressingPlan.MESAB && p.group != "WallFace" && p.group != "MesaGate").Select(p => p.position).ToList();
            float idx = DressingPlan.ClusterIndex(all, StageDefinitions.SIZE * StageDefinitions.SIZE);
            TestContext.WriteLine($"stage {i}: MesaRockB free {free.Count}, all {all.Count}, cluster index {idx:F2}");
            Assert.GreaterOrEqual(idx, DressingPlan.MESAB_CLUSTER_INDEX_MIN);
        }

        // ---- art-r6-16：見晴らしの切れ目は低い地平線（主役は脇に） ----
        [Test] public void VistaGapStaysLow()
        {
            var eye = LobbyLayout.SPAWN + new Vector3(0f, 1.7f, 0f);
            var v = LobbyLayout.VistaHeroes(5);
            foreach (var g in LobbyLayout.VistaGaps)
            {
                foreach (var p in v.Where(q => q.group == "VistaHero" && q.asset != DressingPlan.RANCH))
                {
                    Vector3 d = p.position - LobbyLayout.SPAWN; d.y = 0f;
                    float az = WorldLayout.YawDeg(d.normalized);
                    if (Mathf.Abs(Mathf.DeltaAngle(az, g.x)) > g.y) continue;   // 切れ目の扇の外
                    float elev = Mathf.Atan2(p.position.y + p.height - eye.y, d.magnitude) * Mathf.Rad2Deg;
                    Assert.LessOrEqual(elev, LobbyLayout.VISTA_MAX_ELEV_DEG, p.asset + " in gap " + g.x);
                }
                var mesa = v.First(q => q.group == "VistaHero" && q.asset == DressingPlan.MESA && Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg((q.position - LobbyLayout.SPAWN).normalized), g.x + LobbyLayout.VISTA_HERO_TURN_DEG)) < 1f);
                Vector3 dm = mesa.position - LobbyLayout.SPAWN; dm.y = 0f;
                Assert.LessOrEqual(Mathf.Atan2(mesa.position.y + mesa.height - eye.y, dm.magnitude) * Mathf.Rad2Deg, LobbyLayout.VISTA_MAX_ELEV_DEG + 0.05f, "主役のメサの見かけの高さ");
                // 切れ目の扇（±12°）に橙の砂漠の岩は置かない
                foreach (var p in v.Where(q => q.group == "VistaScatter" && q.asset == DressingPlan.DROCKS))
                {
                    Vector3 d = p.position - LobbyLayout.SPAWN; d.y = 0f;
                    Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg(d.normalized), g.x)), LobbyLayout.VISTA_ROCK_EXCLUDE_DEG - 0.01f);
                }
            }
        }

        // ---- art-r6-17：境目の町の灯りと焚き火 ----
        [Test] public void BorderTownsHaveWarmLampsAndFires()
        {
            var b = Stage(2);
            Assert.Greater(b.plan.Count(p => p.group == "TownLamp" && p.part == "FarmProps_02"), 10);
            Assert.GreaterOrEqual(b.plan.Count(p => p.group == "TownFire" && p.asset == DressingPlan.CAMPFIRE), 2);
            Assert.AreEqual(0, Stage(0).plan.Count(p => p.group == "TownLamp"));
            Assert.AreEqual(6f, ArtPalette.BORDER_BAND_M, 1e-4f);
            Assert.LessOrEqual(ArtPalette.BORDER_GRAIN_AMP, 0.03f + 1e-5f);
            Assert.Greater(b.plan.Count(p => p.part == DressingPlan.TOWN_HERO), 0, "ゴールの町の正面の大きな建物（art-r6-14）");
        }
    }
}
