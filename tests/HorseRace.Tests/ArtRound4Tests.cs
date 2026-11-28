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

        // ---- art-r4-07：スポーンの手前（60° の扇、4〜20m）に 10 個以上の物、視線の 2.5m の通り道は空ける ----
        [Test] public void LobbyForegroundFilled()
        {
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            var plan = DressingPlan.ForLobby(new Vector2(t.x, t.y), new Vector2(t.z, t.w), LobbyLayout.LobbyDressingExclusions(), 4242);
            Vector3 s = LobbyLayout.SPAWN;
            float yaw = LobbyLayout.SPAWN_YAW;
            int n = 0;
            foreach (var p in plan)
            {
                Vector3 d = new Vector3(p.position.x - s.x, 0f, p.position.z - s.z);
                float dist = d.magnitude;
                if (dist < 4f || dist > 20f) continue;
                float off = Mathf.DeltaAngle(yaw, WorldLayout.YawDeg(d.normalized));
                if (Mathf.Abs(off) > 30f) continue;
                float lateral = Mathf.Abs(dist * Mathf.Sin(off * Mathf.Deg2Rad));
                Assert.GreaterOrEqual(lateral, DressingPlan.APRON_LANE_HALF_M - 0.01f, "視線の通り道に " + p.group + " " + p.asset + " " + p.part);
                if (p.height >= 0.6f) n++;
            }
            TestContext.WriteLine($"props in the spawn cone: {n}");
            Assert.GreaterOrEqual(n, 10);
            Assert.GreaterOrEqual(plan.Count(p => p.group == "PlazaApron" && p.asset == DressingPlan.HORSE), 1, "つないだ馬");
        }

        // ---- art-r4-13：尾根の岩は町の建物の 2 倍まで・地面の段をなくす ----
        [Test] public void RimRocksScaled()
        {
            var crown = LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT, 11);
            float max = crown.Max(p => p.height);
            TestContext.WriteLine($"rim rocks {crown.Count}, tallest {max:F1} m (limit {LobbyLayout.RIM_ROCK_MAX_H:F1})");
            Assert.LessOrEqual(max, 2f * LobbyLayout.TALLEST_TOWN_BUILDING_M + 0.01f);
            Assert.IsTrue(crown.All(p => p.scale <= LobbyLayout.RIM_ROCK_MAX_SCALE + 1e-4f));
            Assert.GreaterOrEqual(LobbyLayout.TALLEST_TOWN_BUILDING_M, DressingPlan.SizeOf(DressingPlan.TOWNBLOCK, null).h);
        }

        /// <summary>柵の外の上りは折れ目（傾きの急な変化）が無い：1m ごとの傾きの変化が小さい。</summary>
        [Test] public void LobbyGroundHasNoTerraceStep()
        {
            var f = LobbyLayout.FENCE_RECT;
            float worst = 0f;
            for (float x = f.x + 10f; x < f.z - 10f; x += 23f)
            {
                float prevSlope = float.NaN;
                for (float d = 1f; d < LobbyLayout.GROUND_BORDER_M - 2f; d += 1f)
                {
                    Vector3 pa = new Vector3(x, 0f, f.y - d), pb = new Vector3(x, 0f, f.y - d - 1f);
                    if (LobbyLayout.GapFactor(f, pa) > 0f || LobbyLayout.GapFactor(f, pb) > 0f) { prevSlope = float.NaN; continue; }   // 見晴らしの切れ目の縁は別（わざと低く切る）
                    float a = LobbyLayout.GroundHeight(f, pa, 0.5f), b = LobbyLayout.GroundHeight(f, pb, 0.5f);
                    float slope = b - a;
                    if (!float.IsNaN(prevSlope)) worst = Mathf.Max(worst, Mathf.Abs(slope - prevSlope));
                    prevSlope = slope;
                }
            }
            TestContext.WriteLine($"largest change of slope per metre south of the fence: {worst:F3}");
            Assert.Less(worst, 0.12f, "段（傾きの折れ目）にしない");
            Assert.AreEqual(0f, LobbyLayout.GroundHeight(f, new Vector3(0f, 0f, 60f), 1f), 1e-5f, "柵の中は平ら");
        }

        /// <summary>見晴らしの切れ目：遠くの組んだ目印（メサ ×5、両脇のメサ B ×3）、崖の板なし、切れ目の柵は低い。南へ外の地面に続く道の杭。</summary>
        [Test] public void VistaGapsFrameAView()
        {
            var v = LobbyLayout.VistaHeroes(5);
            foreach (var g in LobbyLayout.VistaGaps)
            {
                float a = g.x * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var mesa = v.Where(p => p.group == "VistaHero" && p.asset == DressingPlan.MESA && Mathf.Abs(Mathf.DeltaAngle(g.x + LobbyLayout.VISTA_HERO_TURN_DEG, WorldLayout.YawDeg((p.position - LobbyLayout.SPAWN).normalized))) < 3f).ToList();   // art-r6-16：真ん中から +22°
                Assert.AreEqual(1, mesa.Count);
                float d = WorldLayout.DistXZ(mesa[0].position, LobbyLayout.SPAWN);
                Assert.That(d, Is.InRange(450f, 700f));
                Assert.AreEqual(LobbyLayout.VISTA_HERO_MESA_SCALE, mesa[0].scale, 1e-4f);
                var fr = LobbyLayout.FENCE_RECT;
                float tx = dir.x > 1e-4f ? (fr.z - LobbyLayout.SPAWN.x) / dir.x : (dir.x < -1e-4f ? (fr.x - LobbyLayout.SPAWN.x) / dir.x : float.MaxValue);
                float tz = dir.z < -1e-4f ? (fr.y - LobbyLayout.SPAWN.z) / dir.z : (dir.z > 1e-4f ? (fr.w - LobbyLayout.SPAWN.z) / dir.z : float.MaxValue);
                Vector3 fenceHit = LobbyLayout.SPAWN + dir * Mathf.Min(tx, tz);
                Assert.AreEqual(LobbyLayout.FENCE_GAP_SCALE_Y, LobbyLayout.FenceScaleYAt(fenceHit), 1e-4f, "切れ目の柵は低い");
            }
            Assert.AreEqual(6, v.Count(p => p.group == "VistaHero" && p.asset == DressingPlan.MESAB));   // art-r6-16：切れ目の反対側の 700m に 3 つずつ
            Assert.IsFalse(v.Any(p => p.asset == DressingPlan.CLIFFS));
            Assert.Greater(v.Count(p => p.group == "ApproachRoad"), 15);
            Assert.AreEqual(LobbyLayout.FENCE_SCALE_Y, LobbyLayout.FenceScaleYAt(new Vector3(LobbyLayout.FENCE_RECT.x, 0f, 100f)), 1e-4f);
        }

        // ---- art-r4-02：路肩 ----
        [TestCaseSource(nameof(Stages))]
        public void ShoulderDensity(int i)
        {
            var b = Stage(i);
            var cum = DressingPlan.CourseArcs(b.local);
            var sh = b.plan.Where(p => p.group == "Shoulder").ToList();
            int windows = Mathf.FloorToInt(b.goal / 100f), thin = 0;
            var counts = new int[windows + 1];
            foreach (var p in sh)
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out float d);
                float hw = StageDefinitions.HalfWidthAt(b.def, a);
                Assert.Greater(DressingPlan.RoadClearance(b.def, b.local, cum, p.position) - p.radius, HeightmapGen.RUNWAY_M - 0.01f, "道の半幅の中に置かない");
                Assert.IsFalse(p.collider);
                if (d <= hw + 6f) counts[Mathf.Clamp(Mathf.FloorToInt(a / 100f), 0, windows)]++;
            }
            int checkedW = 0;
            for (int k = 1; k < windows - 1; k++)
            {
                float a0 = k * 100f;
                // 見どころ（岩の橋・踏み切り）・スタートとゴールの門の窓は除く
                bool skip = false;
                float br = SetPieces.BridgeArc(b.def, b.goal), jp = SetPieces.JumpArc(b.def, b.goal);
                if (br >= 0f && br > a0 - 30f && br < a0 + 130f) skip = true;
                if (jp >= 0f && jp > a0 - 40f && jp < a0 + 140f) skip = true;
                if (skip) continue;
                checkedW++;
                if (counts[k] < 15) thin++;
            }
            TestContext.WriteLine($"stage {i}: {sh.Count} shoulder props, {thin}/{checkedW} windows under 15");
            Assert.LessOrEqual(thin, checkedW / 10, "100m ごとに 15 個以上（札・門の円のまわりだけ例外）");
        }

        // ---- art-r4-08：接地 ----
        [TestCaseSource(nameof(Stages))]
        public void AllPlacementsGrounded(int i)
        {
            var b = Stage(i);
            int bad = 0;
            foreach (var p in b.plan)
            {
                if (p.rollDeg == 90f && p.part == DressingPlan.POST_PART) continue;   // 立てた丸太の杭は中心が柱の真ん中
                if (p.group == "GiantSkeleton" || p.asset == DressingPlan.SKELETON) continue;   // 横倒しの骨は原点が腰
                if (p.group == "WallFace" && p.tint != Color.white) continue;   // art-r6-02：切り通しの段の岩は棚の上（中心の地面）に立てる。前の縁は下の段の岩が隠す
                // art-r5-03：足元は 8 点・半径 fr（木・サボテンは幹の半径：Add が記録した GroundRadius）
                DressingPlan.FootprintRange(b.h, StageDefinitions.SIZE, p.position, DressingPlan.GroundRadius(p), out float mn, out _, out float g0);
                if (p.position.y > mn + 0.05f || p.position.y > g0 + 0.3f) { bad++; if (bad < 5) TestContext.WriteLine($"  floating {p.group} {p.asset} {p.part} y {p.position.y:F2} ground {mn:F2}"); }
            }
            TestContext.WriteLine($"stage {i}: floating {bad} of {b.plan.Count}");
            Assert.AreEqual(0, bad);
            foreach (var p in b.plan.Where(q => !q.background && !DressingPlan.SlopeEmbedded.Contains(q.group) && q.height < DressingPlan.GROUND_REJECT_MAX_H))
            {
                if (DressingPlan.LineGroups.Contains(p.group) || DressingPlan.ShareExempt.Contains(p.group) || p.group == "MinersCamp" || p.group == "Caravan" || p.group == "WaterStop") continue;
                if (p.rollDeg == 90f && p.part == DressingPlan.POST_PART) continue;
                float fr = DressingPlan.GroundRadius(p), mn = float.MaxValue, mx = float.MinValue;
                for (int k = 0; k < 5; k++)
                {
                    float a = (45f + k * 90f) * Mathf.Deg2Rad;
                    float x = k == 4 ? p.position.x : p.position.x + Mathf.Sin(a) * fr * 0.7f, z = k == 4 ? p.position.z : p.position.z + Mathf.Cos(a) * fr * 0.7f;
                    float g = b.Ground(x, z); mn = Mathf.Min(mn, g); mx = Mathf.Max(mx, g);
                }
                Assert.LessOrEqual(mx - mn, DressingPlan.GROUND_REJECT_SPAN * p.height + 0.01f, "急な斜面の小物 " + p.group + " " + p.part);
            }
        }

        [Test] public void LobbyRimRocksGrounded()
        {
            var crown = LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT, 11);
            foreach (var c in crown)
            {
                float mn = LobbyLayout.GroundHeight(LobbyLayout.FENCE_RECT, c.position);
                for (int k = 0; k < 4; k++)
                {
                    float a = (45f + k * 90f) * Mathf.Deg2Rad, r = c.radius * LobbyLayout.CROWN_FOOT;
                    mn = Mathf.Min(mn, LobbyLayout.GroundHeight(LobbyLayout.FENCE_RECT, new Vector3(c.position.x + Mathf.Sin(a) * r, 0f, c.position.z + Mathf.Cos(a) * r)));
                }
                Assert.LessOrEqual(c.position.y, mn + 0.05f);
            }
        }

        // ---- art-r4-11：急カーブの干し草の壁は四角い干し草の続いた低い壁 ----
        [TestCaseSource(nameof(Stages))]
        public void CornerWallIsContinuousBlocks(int i)
        {
            var b = Stage(i);
            foreach (var c in StageDefinitions.TightCorners(b.def, b.local))
            {
                var wall = CourseMarks.CornerWall(b.def, b.local, c);
                Assert.IsTrue(wall.All(w => w.part == "HayBales_02" || w.part == "HayBales_03"), "四角い干し草");
                Assert.That(wall.Count(w => w.cap), Is.InRange(wall.Count / 3 - 1, wall.Count / 3 + 1), "3 つに 1 つ丸い干し草");
                foreach (var w in wall) Assert.That(w.scale * CourseMarks.HAY_BLOCK_LEN, Is.InRange(CourseMarks.CORNER_WALL_STEP_M * 0.94f, CourseMarks.CORNER_WALL_STEP_M * 1.06f), "端と端でつながる長さ");
                var cum = DressingPlan.CourseArcs(b.local);
                foreach (var w in wall)
                {
                    float a = DressingPlan.NearestArc(b.local, cum, w.position, out float _);
                    Assert.That(a - c.apexArc, Is.InRange(-CourseMarks.CORNER_WALL_FROM_M - 3f, CourseMarks.CORNER_WALL_TO_M + 3f));
                }
                var ch = CourseMarks.CornerWallChevrons(b.def, b.local, c);
                Assert.GreaterOrEqual(ch.Count, 3, "8m ごとの赤い矢印");
            }
        }

        // ---- art-r4-12：岩の橋は 60m 先から読める ----
        [Test] public void RockBridgeReads()
        {
            Assert.GreaterOrEqual(SetPieces.SpineFlankDeg, 65f);
            Assert.AreEqual(14f, SetPieces.TRENCH_DEPTH, 1e-4f);
            Assert.AreEqual(3, SetPieces.TRENCH_SKELETONS);
            // art-r6-13：背は道の色のまま（明るい線の塗りは黄色く光る板に見えた）。石の床と両端の板の床
            Assert.AreEqual(0f, SetPieces.SPINE_LINE_W, 1e-5f);
            Assert.Greater(SetPieces.BridgeDeckTiles().Count, 8);
            Assert.AreEqual(2 * SetPieces.DECK_PLANKS_PER_END, SetPieces.BridgeDeckPlanks().Count);
        }

        // ---- art-r4-14 / 15：野に吊り下げの街灯を並べない・目印は 1 ステージ 1 つ ----
        [TestCaseSource(nameof(Stages))]
        public void NoGallowsInOpenCountry(int i)
        {
            var b = Stage(i);
            foreach (var p in b.plan.Where(q => q.part == "FarmProps_02"))
                Assert.IsTrue(DressingPlan.FarmLampGroups.Contains(p.group), "野の吊り下げの街灯: " + p.group);
            Assert.Greater(b.plan.Count(p => p.lamp), 3, "丸太の柱の灯り");
            DressingPlan.GuideAsset(b.def.kind, out string asset, out string part, out float scale);
            var guide = b.plan.Where(p => p.group == "GuideTower" && p.part == part).ToList();
            Assert.LessOrEqual(guide.Count, 1);
            Assert.LessOrEqual(b.plan.Count(p => p.part == DressingPlan.RANCH_TOWER), 1);
        }

        // ---- art-r4-18：砂漠の道の近くの暮らしの跡 ----
        [Test] public void DunesNearRoadLife()
        {
            var b = Stage(1);
            var cum = DressingPlan.CourseArcs(b.local);
            var wrecks = b.plan.Where(p => p.group == "DuneWreck" && p.part == DressingPlan.TOWN_CART).ToList();
            TestContext.WriteLine($"half-buried carts {wrecks.Count}");
            Assert.GreaterOrEqual(wrecks.Count, (int)(b.goal / DressingPlan.DUNE_WRECK_STEP_M) - 3);
            foreach (var w in wrecks)
            {
                float a = DressingPlan.NearestArc(b.local, cum, w.position, out float d);
                Assert.That(d - StageDefinitions.HalfWidthAt(b.def, a), Is.InRange(DressingPlan.DUNE_WRECK_LAT_MIN - 1f, DressingPlan.DUNE_WRECK_LAT_MAX + 1f));
                Assert.AreEqual(DressingPlan.DUNE_WRECK_TILT, Mathf.Abs(w.rollDeg), 1e-3f);
            }
            var camels = b.plan.Where(p => p.group == "Caravan" && p.asset == DressingPlan.CAMEL && WorldLayout.DistToPolylineXZ(p.position, b.local) <= DressingPlan.CARAVAN_ROAD_MAX_M).ToList();
            Assert.GreaterOrEqual(camels.Count, 2, "道から 25m 以内のラクダの隊列");
            Assert.AreEqual(1, b.plan.Count(p => p.group == "GiantSkeleton" && Mathf.Abs(p.scale - DressingPlan.GIANT_SKELETON_HERO_SCALE) < 1e-3f), "砂漠の主役の巨大な骨");
        }

        [Test] public void TownBoardShowsRouteByDefault()
        {
            string r = HudMath.RouteMap(-1);
            StringAssert.Contains("次のレース", r);
            StringAssert.Contains("<b>荒野</b>", r);
            StringAssert.Contains("砂漠", r);
            StringAssert.Contains("境目", r);
            StringAssert.Contains("<b>砂漠</b>", HudMath.RouteMap(1));
        }

        // ---- art-r4-17：峡谷の壁の頂の岩・急な壁をならす ----
        [Test] public void CanyonCrestCapsAndSmoothWalls()
        {
            var b = Stage(0);
            int caps = b.plan.Count(p => p.group == "RimLandmark" && (p.asset == DressingPlan.RSTYL || p.asset == DressingPlan.DROCKS));
            TestContext.WriteLine($"canyon crest caps {caps}");
            Assert.GreaterOrEqual(caps, (int)(b.goal / DressingPlan.CREST_CAP_STEP_M) / 2);
            Assert.AreEqual(0, b.plan.Count(p => p.group == "RimLandmark" && p.part == DressingPlan.RANCH_TOWER), "頂は岩（風車の塔ではない）");
            // 急な壁のならし：45° 以上の段の隣どうしの差が小さくなる
            var h = new float[33, 33];
            for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) h[z, x] = (x / 4) % 2 == 0 ? 0.2f : 0.5f;   // 段の付いた壁
            HeightmapGen.SmoothSteep(h, 64f, 200f, null);
            float maxJump = 0f;
            for (int x = 1; x < 32; x++) maxJump = Mathf.Max(maxJump, Mathf.Abs(h[16, x] - h[16, x - 1]));
            Assert.Less(maxJump, 0.3f, "段がならされる");
        }
    }
}
