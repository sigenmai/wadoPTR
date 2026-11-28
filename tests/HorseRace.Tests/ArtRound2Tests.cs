using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 2 巡目（review-log の Round 2 — art）の単体テスト。</summary>
    public class ArtRound2Tests
    {
        /// <summary>Build World と同じ順のステージの地形（Generate → ApplyRim → SetPieces.ApplyTerrain）と飾りの計画。</summary>
        internal sealed class Built
        {
            public StageDef def;
            public Vector3[] local;
            public float goal;
            public float[,] h;
            public SetPieces.ShortcutPlan sc;
            public List<Placement> plan;
            public float Ground(float x, float z) => WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, x, z);
        }

        static readonly Dictionary<int, Built> cache = new Dictionary<int, Built>();

        internal static Built Stage(int i)
        {
            lock (cache)
            {
                if (cache.TryGetValue(i, out var b)) return b;
                var s = StageDefinitions.All[i];
                var local = StageDefinitions.SampleLocal(s);
                float goal = StageDefinitions.GoalArc(s, local);
                var norm = StageDefinitions.Normalize(local);
                var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, norm);
                HeightmapGen.ApplyRim(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, HeightmapGen.RIM_WIDTH_M, HeightmapGen.RIM_MIN_M, HeightmapGen.RIM_MAX_M, s.seed, norm);
                var sc = SetPieces.ApplyTerrain(s, local, h, goal);
                b = new Built { def = s, local = local, goal = goal, h = h, sc = sc, plan = DressingPlan.For(s, local, h, goal, sc) };
                cache[i] = b;
                return b;
            }
        }

        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        // ---- art-r2-01：外の地面がステージの地面の上に出ない ----

        [Test] public void FillerNeverAboveStageGround()
        {
            var rects = WorldBounds.PlayableRects();
            var built = new[] { Stage(0), Stage(1), Stage(2) };
            System.Func<int, float, float, float> ground = (i, x, z) =>
            {
                if (i >= 3) return 0f;   // ロビー（このテストでは見ない）
                var o = built[i].def.Origin;
                return o.y + built[i].Ground(x - o.x, z - o.z);
            };
            float[,] grid = WorldBounds.FillerGrid(WorldBounds.FILLER_RES, rects, ground);
            for (int i = 0; i < 3; i++)
            {
                var o = built[i].def.Origin;
                float worst = float.MaxValue; Vector3 at = Vector3.zero;
                for (float z = 0f; z <= StageDefinitions.SIZE; z += 2f)
                    for (float x = 0f; x <= StageDefinitions.SIZE; x += 2f)
                    {
                        float stage = o.y + built[i].Ground(x, z);
                        float filler = WorldBounds.SampleFiller(grid, o.x + x, o.z + z);
                        float gap = stage - filler;
                        if (gap < worst) { worst = gap; at = new Vector3(x, 0f, z); }
                    }
                TestContext.WriteLine($"stage {i}: min (stage - filler) {worst:F2} m at {at}");
                Assert.GreaterOrEqual(worst, 0.4f, $"stage {i} at {at}");   // art-r4-01：縁の外の頂点は縁の高さ − 0.6m（継ぎ目の段を見せない）。内側は 0.4m 以上下
            }
        }

        [Test] public void FillerHeightRules()
        {
            var rects = new[] { new Vector4(0f, 0f, 100f, 100f) };
            System.Func<int, float, float, float> flat = (i, x, z) => 10f;
            Assert.AreEqual(WorldBounds.FILLER_Y - WorldBounds.FILLER_DEPTH_M, WorldBounds.FillerHeight(50f, 50f, 8f, 8f, rects, flat), 1e-4);
            Assert.LessOrEqual(WorldBounds.FillerHeight(1f, 50f, 8f, 8f, rects, flat), 10f - WorldBounds.FILLER_UNDER_M + 1e-4);
            // 縁の 1 マスはまわり 3×3 の一番低い地面から
            // art-r4-01：縁に沿って ±1 マスのいちばん低い地面（内側の低い斜面は拾わない：縁の尾根から外の地面が崖のように落ちない）
            System.Func<int, float, float, float> pit = (i, x, z) => z > 45f && z < 47f ? -20f : 10f;
            Assert.LessOrEqual(WorldBounds.FillerHeight(-2f, 50f, 8f, 8f, rects, pit), -20f - WorldBounds.FILLER_SEAM_M + 1e-3);
            Assert.LessOrEqual(WorldBounds.FillerHeight(2f, 50f, 8f, 8f, rects, pit), -20f - WorldBounds.FILLER_UNDER_M + 1e-3, "内側の頂点はもっと下");
            System.Func<int, float, float, float> inward = (i, x, z) => x > 9f && x < 11f ? -20f : 10f;
            Assert.Greater(WorldBounds.FillerHeight(-2f, 50f, 8f, 8f, rects, inward), 5f);
            Assert.AreEqual(-3f, WorldBounds.SignedDistToRect(new Vector3(3f, 0f, 50f), rects[0]), 1e-4);
        }

        // ---- art-r2-02：道の色が土・砂から読める ----
        [Test] public void TrailContrast()
        {
            for (int i = 0; i < 3; i++)
            {
                float t = LightingPlan.Luminance(ArtPalette.Trail(i)), d = LightingPlan.Luminance(ArtPalette.Dirt(i)), sand = LightingPlan.Luminance(ArtPalette.SandLayer);
                TestContext.WriteLine($"stage {i}: trail {t:F3} dirt {d:F3} sand {sand:F3}");
                // art-r3-06：砂漠の道は砂より 0.08 以上暗い明るい踏み固めた土（前の暗い道はアスファルトに見えた）。砂漠の土とは色合いで分ける
                Assert.GreaterOrEqual(Mathf.Abs(t - d), i == 1 ? 0.05f : 0.15f, "土 " + i);
                // art-r4-05：砂漠の道は砂 × 約 0.92 の明るさの踏み固めた砂（暗い帯にしない：砂の 85% 以上、砂よりは暗い）
                // art-r5-07：砂漠の道は砂より明るさで 0.08 以上明るい（同じ明るさでは道が見えなかった）
                if (i == 1) Assert.GreaterOrEqual(t - sand, ArtPalette.TRAIL_DUNES_ABOVE_SAND, "砂漠の道は砂より明るい");
                // art-r6-11：砂漠の砂を暗くした（道を白く飛ばさない）ので、荒野・境目の道（砂漠の砂の上には無い）は 0.03 以上離れていればよい。主な読みは土との 0.15
                else Assert.GreaterOrEqual(Mathf.Abs(t - sand), 0.03f, "砂 " + i);
            }
            Assert.AreNotEqual(ArtPalette.Trail(0), ArtPalette.Trail(1));
        }

        // ---- art-r2-03：道の目印は柵・杭・街灯、骨は骨の原だけ ----
        [TestCaseSource(nameof(Stages))]
        public void GuideLineUsesFencePieces(int i)
        {
            var plan = Stage(i).plan;
            var guides = plan.Where(p => p.group == "GuideLine" || p.group == "CanyonRim").ToList();
            Assert.Greater(guides.Count, 30);
            Assert.IsFalse(guides.Any(p => p.part == "DesertProps_06" || p.part == "DesertProps_10"), "柵の列にあばらに見える杭");
            Assert.Greater(guides.Count(p => p.part == "FarmProps_03" || p.part == "FarmProps_04"), guides.Count / 3);
            Assert.IsFalse(guides.Any(p => p.part == "DesertProps_03" || p.part == "DesertProps_05"), "骨の杭（art-r3-02）");
            Assert.Greater(guides.Count(p => p.lamp && p.part == DressingPlan.POST_PART), 3, "直線の灯り（art-r4-14：丸太の柱の上の灯り）");
            Assert.IsFalse(guides.Any(p => p.part == "FarmProps_02"), "野に吊り下げの街灯（絞首台に見える）を並べない");
            var bones = plan.Where(p => DressingPlan.IsBonePart(p.part)).ToList();
            foreach (var b in bones) Assert.IsTrue(DressingPlan.BoneGroups.Contains(b.group), b.group + " " + b.part);
            Assert.LessOrEqual(bones.Count, DressingPlan.BONE_MAX);
        }

        // ---- art-r2-04：1 つの素材に偏らない ----
        [TestCaseSource(nameof(Stages))]
        public void AssetShareCap(int i)
        {
            var plan = Stage(i).plan;
            float key = DressingPlan.MaxShare(plan, false, out string k1), pack = DressingPlan.MaxShare(plan, true, out string k2);
            TestContext.WriteLine($"stage {i}: {plan.Count} props, max key {k1} {key:P1}, max pack {k2} {pack:P1}");
            Assert.LessOrEqual(key, DressingPlan.KEY_SHARE_MAX + 1e-4f, k1);
            Assert.LessOrEqual(pack, DressingPlan.PACK_SHARE_MAX + 1e-4f, k2);
            if (i == 2) Assert.LessOrEqual(plan.Count(p => p.asset == DressingPlan.TREES || p.asset == DressingPlan.ACACIA), DressingPlan.MESA_LUSH_MAX);
        }

        // ---- art-r2-05：素材をもっと使う ----
        [Test] public void AssetBreadth()
        {
            var town = new HashSet<string>();
            int camels = 0, gems = 0;
            for (int i = 0; i < 3; i++)
            {
                var plan = Stage(i).plan;
                int distinct = plan.Select(p => DressingPlan.ShareKey(p.asset, p.part)).Distinct().Count();
                TestContext.WriteLine($"stage {i}: {distinct} distinct props");
                Assert.GreaterOrEqual(distinct, 14);
                foreach (var p in plan) if (p.asset == DressingPlan.WTOWN) town.Add(p.part);
                camels += plan.Count(p => p.asset == DressingPlan.CAMEL);
                gems += plan.Count(p => p.asset == DressingPlan.GEMS);
                Assert.GreaterOrEqual(plan.Count(p => p.group == DressingPlan.VignetteGroup(Stage(i).def.kind)), 10, "見どころ " + i);
            }
            TestContext.WriteLine($"WesternTown parts {town.Count}, camels {camels}, gems {gems}");
            Assert.GreaterOrEqual(town.Count, 30);
            Assert.GreaterOrEqual(camels, 2);
            Assert.GreaterOrEqual(gems, 2);
        }

        [TestCaseSource(nameof(Stages))]
        public void VignettesSitBesideTheRoadEvery350To450m(int i)
        {
            var b = Stage(i);
            string g = DressingPlan.VignetteGroup(b.def.kind);
            var cum = DressingPlan.CourseArcs(b.local);
            var anchors = b.plan.Where(p => p.group == g && (p.asset == DressingPlan.CAMPFIRE || p.asset == DressingPlan.STABLE || (p.asset == DressingPlan.CAMEL && b.def.kind == HeightmapGen.KIND_DUNES))).ToList();
            var arcs = new List<float>();
            foreach (var p in anchors)
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out float d);
                Assert.That(d, Is.InRange(DressingPlan.VIGNETTE_LAT_MIN - 12f, DressingPlan.VIGNETTE_LAT_MAX + 12f), p.asset);
                if (arcs.All(x => Mathf.Abs(x - a) > 60f)) arcs.Add(a);
            }
            TestContext.WriteLine($"stage {i}: vignettes at " + string.Join(", ", arcs.Select(a => a.ToString("F0"))));
            Assert.GreaterOrEqual(arcs.Count, 4);
        }

        // ---- art-r2-06：ゴールの先に向かう目印 ----
        [TestCase(0)] [TestCase(1)]
        public void GoalFramesLandmark(int i)
        {
            var b = Stage(i);
            Vector3 g = WorldLayout.PointAtArc(b.local, b.goal), t = WorldLayout.TangentAtArc(b.local, b.goal);
            float eye = b.Ground(g.x, g.z) + 2.2f;
            bool found = false;
            foreach (var p in b.plan)
            {
                if (p.height <= 8f) continue;
                Vector3 d = new Vector3(p.position.x - g.x, 0f, p.position.z - g.z);
                float dist = d.magnitude;
                if (dist < 40f || dist > 150f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg(t), WorldLayout.YawDeg(d.normalized))) > 12f) continue;
                float top = p.position.y + p.height * 0.6f;
                bool clear = true;
                for (float s = 4f; s < dist - p.radius; s += 2f)
                {
                    Vector3 q = g + d.normalized * s;
                    float line = eye + (top - eye) * (s / dist);
                    if (b.Ground(q.x, q.z) > line) { clear = false; break; }
                }
                TestContext.WriteLine($"stage {i}: {p.group} {p.part ?? p.asset} at {dist:F0} m, h {p.height:F0}, clear {clear}");
                if (clear) found = true;
            }
            Assert.IsTrue(found, "ゴールの奥に見える高い目印が無い");
        }

        // ---- art-r2-09：外周の尾根 ----
        [Test] public void RimIsRoundedAtCorners()
        {
            Assert.Less(HeightmapGen.RimDistance(70f, 70f, 1000f), HeightmapGen.RIM_WIDTH_M);       // 角の近くは尾根
            Assert.GreaterOrEqual(HeightmapGen.RimDistance(70f, 500f, 1000f), HeightmapGen.RIM_WIDTH_M);   // 辺の真ん中の同じ深さは平ら
            Assert.AreEqual(10f, HeightmapGen.RimDistance(10f, 500f, 1000f), 1e-3f);
            float min = float.MaxValue, max = float.MinValue;
            for (float x = 0f; x < 1000f; x += 10f) { float f = HeightmapGen.RimHeightFactor(x, 0f, 7); min = Mathf.Min(min, f); max = Mathf.Max(max, f); }
            Assert.Greater(max - min, 0.3f, "尾根の高さに鞍部と峰");
            float per = DressingPlan.RimPathLength(28f, 1000f);
            for (float s = 0f; s < per; s += 37f)
                Assert.AreEqual(28f, HeightmapGen.RimDistance(DressingPlan.RimPathPoint(s, 28f, 1000f).x, DressingPlan.RimPathPoint(s, 28f, 1000f).z, 1000f), 0.05f);
        }

        [TestCaseSource(nameof(Stages))]
        public void HorizonSilhouettes(int i)
        {
            var b = Stage(i);
            var o = b.def.Origin;
            // 自分のステージと隣のステージ（尾根のシルエットは隣からも見える）の高い飾り、ワールドを囲む山並み（このステージのローカルへ）
            var tall = new List<Vector3>();
            for (int j = 0; j < 3; j++)
            {
                var oj = Stage(j).def.Origin;
                foreach (var p in Stage(j).plan) if (p.height > 15f) tall.Add(p.position + oj - o);
            }
            foreach (var p in DressingPlan.Backdrop(7)) if (p.height > 15f) tall.Add(new Vector3(p.position.x - o.x, p.position.y - o.y, p.position.z - o.z));
            var light = LightingPlan.ForStage(i);
            float hidden = light.fogStart + (light.fogEnd - light.fogStart) * LightingPlan.FOG_HIDDEN_FRACTION;
            int worst = int.MaxValue;
            foreach (float a in WorldLayout.CheckpointArcs(b.goal, StageDefinitions.CP_SPACING_M))
            {
                Vector3 c = WorldLayout.PointAtArc(b.local, a), t = WorldLayout.TangentAtArc(b.local, a);
                int n = 0;
                foreach (var p in tall)
                {
                    Vector3 d = new Vector3(p.x - c.x, 0f, p.z - c.z);
                    float dist = d.magnitude;
                    if (dist < 150f || dist > hidden) continue;
                    if (Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg(t), WorldLayout.YawDeg(d.normalized))) <= 30f) n++;
                }
                worst = Mathf.Min(worst, n);
                Assert.GreaterOrEqual(n, 3, $"stage {i} CP at {a:F0} m");
            }
            TestContext.WriteLine($"stage {i}: min silhouettes per CP {worst}, rim {b.plan.Count(p => p.group == "RimSilhouette")}");
            Assert.GreaterOrEqual(b.plan.Count(p => p.group == "RimSilhouette"), 15);
        }

        // ---- art-r2-10：ステージごとの光 ----
        [Test] public void StageLightsDiffer()
        {
            var dirt = new[] { ArtPalette.DirtCanyon, ArtPalette.DirtDunes, ArtPalette.DirtBorder };
            for (int i = 0; i < 3; i++)
            {
                float lum = LightingPlan.MinFloorShadowLuminance(i, dirt[i], ArtPalette.SandLayer);
                TestContext.WriteLine($"stage {i}: shadow floor luminance {lum:F3}");
                Assert.GreaterOrEqual(lum, LightingPlan.MIN_SHADOW_LUMINANCE);
                for (int j = i + 1; j < 3; j++)
                {
                    Color a = LightingPlan.ForStage(i).fogColor, c = LightingPlan.ForStage(j).fogColor;
                    float dist = Mathf.Sqrt((a.r - c.r) * (a.r - c.r) + (a.g - c.g) * (a.g - c.g) + (a.b - c.b) * (a.b - c.b));
                    Assert.GreaterOrEqual(dist, 0.08f, $"fog {i}-{j}");
                }
            }
            Assert.AreEqual(LightingPlan.ShadowColorDusk, LightingPlan.ForStage(2).shadow);
            Assert.AreEqual(LightingPlan.FogColor, LightingPlan.ForStage(LightingPlan.LOBBY_LIGHT).fogColor);
            Assert.Less(LightingPlan.ForStage(2).sunEuler.x, 25f, "夕日は低い");
        }

        // ---- art-r2-11 / 14：町の地面の板・建物の色 ----
        [Test] public void TownGroundAndBuildingsFollowPalette()
        {
            var g = ArtPalette.StyleFor("Packs/WesternTown/WesternTown.fbx", "WesternTown_001_Ground", "Ground");
            ArtPalette.ToHSV(g.tint, out float h, out float sat, out float v);
            TestContext.WriteLine($"town ground tint L {LightingPlan.Luminance(g.tint):F3} hue {h:F1}");
            Assert.LessOrEqual(LightingPlan.Luminance(g.tint), 0.70f);
            Assert.That(h, Is.InRange(25f, 45f));
            Assert.AreEqual("Ground", ArtPalette.PartKind("WesternTown_001_Ground"));
            Assert.AreEqual("Hill", ArtPalette.PartKind("WesternTown_002_Hill"));
            Assert.IsNull(ArtPalette.PartKind("Cacti_01"));
            var hill = ArtPalette.StyleFor("Packs/WesternTown/WesternTown.fbx", "WesternTown_002_Hill");
            Assert.AreEqual(ArtPalette.RockGrayTint, hill.tint);
            Assert.AreEqual(ArtPalette.ROCK_FLATTEN, hill.flatten, 1e-5);
            StringAssert.Contains("_Warm", hill.key);
            Assert.AreEqual(ArtPalette.RockGrayTint, ArtPalette.StyleFor("Packs/WesternTown/WesternTown.fbx", "WesternTown_045_Rock").tint);
            Assert.AreEqual(Color.white, ArtPalette.StyleFor("Packs/WesternTown/WesternTown.fbx", "WesternTown_084_Building").tint);
            // 建物（Buildings/・RanchCompound）の青 → 古い木
            var blue = new Color(0.6f, 0.7f, 0.85f);
            foreach (string path in new[] { "Buildings/TownBlock.fbx", "Buildings/SaloonFacade.fbx", "Buildings/RanchCompound.fbx" })
            {
                ArtPalette.ToHSV(ArtPalette.RemapBaseColor(path, blue), out float bh, out _, out _);
                Assert.That(bh, Is.InRange(20f, 45f), path);
                Assert.AreNotEqual(Color.white, ArtPalette.TextureTint(path));
            }
            Assert.AreEqual(Color.white, ArtPalette.TextureTint("Props/MesaRock/MesaRock.fbx"));
        }

        // ---- art-r2-16：砂漠の巨大な骨 ----
        [Test] public void GiantSkeletonOnDuneCrestAheadOfStart()
        {
            var b = Stage(1);
            var giants = b.plan.Where(p => p.group == "GiantSkeleton").ToList();
            Assert.AreEqual(2, giants.Count);
            Vector3 p0 = WorldLayout.PointAtArc(b.local, StageDefinitions.START_ARC_M), t0 = WorldLayout.TangentAtArc(b.local, StageDefinitions.START_ARC_M);
            var first = giants[0];
            Vector3 d = new Vector3(first.position.x - p0.x, 0f, first.position.z - p0.z);
            Assert.That(d.magnitude, Is.InRange(250f, 400f));
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg(t0), WorldLayout.YawDeg(d.normalized))), 30f);
            Assert.That(first.scale, Is.InRange(6f, 8f));
            Assert.IsFalse(first.collider);
        }

        // ---- art-r2-07：ゴールゲートは丸太 1 本ずつ（板に見えない）、プレビューと同じ置き方 ----
        [Test] public void GoalGateIsLogsOnPosts()
        {
            var parts = CourseMarks.GoalGateParts(Vector3.zero, new Vector3(0f, 0f, 1f), p => new Vector3(p.x, 0f, p.z));
            Assert.IsFalse(parts.Any(p => p.part == "WoodLogs_03"), "丸太の束を引き伸ばすと板に見える");
            var posts = parts.Where(p => p.part == "WoodLogs_01" && Mathf.Abs(p.euler.z - 90f) < 1e-3f).ToList();
            var beams = parts.Where(p => p.part == "WoodLogs_01" && p.euler.z == 0f).ToList();
            Assert.AreEqual(2, posts.Count);
            Assert.AreEqual(CourseMarks.GATE_BEAM_PIECES * CourseMarks.GATE_BEAM_TIERS, beams.Count);
            foreach (var p in posts)
            {
                Assert.AreEqual(CourseMarks.GATE_POST_H, p.scale.x * CourseMarks.WOODLOG_01_LEN, 1e-3f);
                Assert.AreEqual(CourseMarks.GATE_POST_THICK, p.scale.y * CourseMarks.WOODLOG_01_H, 1e-3f);
            }
            // course-r3-01：横木は丸太の束 ×0.9 を重ねてつなぐ 2 段。1 段の外の端から外の端まで柱から柱
            float top = beams.Max(b => b.position.y);
            var upper = beams.Where(b => Mathf.Abs(b.position.y - top) < 0.05f).ToList();
            float len = CourseMarks.WOODLOG_01_LEN * CourseMarks.GATE_BEAM_SCALE;
            float total = upper.Max(b => b.position.x) - upper.Min(b => b.position.x) + len;
            foreach (var b in beams) Assert.Less(b.position.y, CourseMarks.GATE_POST_H, "横木は柱の上の端より下（柱に載る）");
            Assert.GreaterOrEqual(total, CourseGeometry.GATE_HALF * 2f, "柱から柱まで");
            Assert.Less(CourseMarks.GATE_BANNER_Y + 0.8f, beams.Min(b => b.position.y + CourseMarks.GATE_BEAM_THICK * 0.5f), "幕は下の横木から吊る");
            Assert.AreEqual(0, parts.Count(p => DressingPlan.IsBonePart(p.part)), "骨の杭は使わない（art-r3-02）");
            Assert.GreaterOrEqual(parts.Count(p => p.part == "FarmProps_15" || p.part == "FarmProps_22"), 6, "柱の外の樽の山");
            Assert.AreEqual(2, parts.Count(p => p.light));
        }

        // ---- art-r2-11：町の地面の板の縁 ----
        [Test] public void PlateEdgeBlendsIntoGround()
        {
            Assert.AreEqual(-1f, LobbyLayout.PlateBlend(0f, -1f, 0f), 1e-5f);                       // 板の上は触らない
            Assert.AreEqual(LobbyLayout.PLATE_TOP_OFFSET_M, LobbyLayout.PlateBlend(1.5f, -1f, 0f), 1e-5f);   // 板の外 2m は板の上面
            Assert.AreEqual(-1f, LobbyLayout.PlateBlend(LobbyLayout.PLATE_LIP_M + LobbyLayout.PLATE_BLEND_M + 0.1f, -1f, 0f), 1e-5f);
            float prev = 1f;
            for (float d = 2f; d <= 10f; d += 0.5f) { float v = LobbyLayout.PlateBlend(d, -1f, 0f); Assert.LessOrEqual(v, prev + 1e-5f); prev = v; }
            Assert.AreEqual(3f, LobbyLayout.PlateBlend(1f, 3f, 0f), 1e-5f);                         // 高い所（土手）は下げない
            Assert.AreEqual(1f, LobbyLayout.PlateSand(1f), 1e-5f);
            Assert.AreEqual(0f, LobbyLayout.PlateSand(0f), 1e-5f);
        }

        // ---- art-r2-06 → course-r2-05：ゴールの先の切れ込み（3 ステージとも）----
        /// <summary>GoalViewOpen：ゴールの向きに 300m（Terrain の中）まで、地形が目の高さ + 10m より低い（外の世界が見える）。</summary>
        [Test] public void GoalViewOpen([Values(0, 1, 2)] int i)
        {
            var b = Stage(i);
            Vector3 g = WorldLayout.PointAtArc(b.local, b.goal), t = WorldLayout.TangentAtArc(b.local, b.goal);
            float gh = b.Ground(g.x, g.z), worst = -999f;
            for (float a = 0f; a <= SetPieces.NOTCH_VIEW_M; a += 5f)
            {
                Vector3 q = g + t * a;
                if (q.x < 0f || q.z < 0f || q.x > StageDefinitions.SIZE || q.z > StageDefinitions.SIZE) break;
                worst = Mathf.Max(worst, b.Ground(q.x, q.z) - gh);
            }
            TestContext.WriteLine($"stage {i}: highest ground ahead of the goal {worst:F1} m");
            Assert.Less(worst, SetPieces.NOTCH_EYE_M + 10f);
            Assert.AreEqual(0f, SetPieces.NotchWeight(SetPieces.NOTCH_FROM_M - 1f), 1e-5f);
            Assert.AreEqual(SetPieces.NOTCH_FLOOR_M, SetPieces.NotchFloor(0f), 1e-5f);
            Assert.AreEqual(SetPieces.NOTCH_FLOOR_M + 10f, SetPieces.NotchFloor(SetPieces.NOTCH_HALF_M + 50f), 1e-4f);
        }
    }
}
