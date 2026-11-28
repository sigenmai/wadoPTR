using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 3 巡目（review-log の Round 3 — art）の単体テスト。</summary>
    public class ArtRound3Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        // ---- art-r3-01：ゴールの横木は細い枠（板にしない）----
        [Test] public void GoalGateBeamIsThin()
        {
            var parts = CourseMarks.GoalGateParts(Vector3.zero, new Vector3(0.6f, 0f, 0.8f), p => new Vector3(p.x, 0f, p.z));
            var beams = parts.Where(p => p.part == "WoodLogs_01" && Mathf.Abs(p.euler.z) < 1e-3f).ToList();
            Assert.AreEqual(CourseMarks.GATE_BEAM_PIECES * 2, beams.Count, "2 段");
            foreach (var b in beams)
            {
                float y = b.scale.y * CourseMarks.WOODLOG_01_H, z = b.scale.z * CourseMarks.WOODLOG_01_W, len = b.scale.x * CourseMarks.WOODLOG_01_LEN;
                Assert.LessOrEqual(y, 1.0f, "縦の太さ");
                Assert.LessOrEqual(z, 1.0f, "奥行き");
                Assert.That(len, Is.InRange(1.0f, 1.6f), "course-r3-01：引き伸ばさない丸太の束");
                Assert.GreaterOrEqual(b.position.y + y * 0.5f, CourseMarks.GATE_BEAM_MIN_Y, "高さ 5.5m 以上");
            }
            var tiers = beams.Select(b => Mathf.Round(b.position.y * 10f)).Distinct().Count();
            Assert.AreEqual(2, tiers);
        }

        // ---- art-r3-02：骨（DesertProps_03/05/06/07/08）は骨の原・巨大な骨だけ ----
        [TestCaseSource(nameof(Stages))]
        public void NoBonesOutsideBoneGroups(int i)
        {
            var b = Stage(i);
            var all = new List<Placement>(b.plan);
            all.AddRange(DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc)));
            foreach (var p in all)
                if (DressingPlan.IsBonePart(p.part)) Assert.IsTrue(DressingPlan.BoneGroups.Contains(p.group), p.group + " " + p.part);
            Assert.LessOrEqual(b.plan.Count(p => DressingPlan.IsBonePart(p.part)), DressingPlan.BONE_MAX);
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            foreach (var p in DressingPlan.ForLobby(new Vector2(t.x, t.y), new Vector2(t.z, t.w), LobbyLayout.LobbyDressingExclusions(), 4242))
                Assert.IsFalse(DressingPlan.IsBonePart(p.part), "ロビー " + p.group + " " + p.part);
            Assert.IsTrue(DressingPlan.IsBonePart("DesertProps_03") && DressingPlan.IsBonePart("DesertProps_05"));
            Assert.AreNotEqual("DesertProps_05", DressingPlan.FencePiece(2));
        }

        [Test] public void PostIsUprightThinLog()
        {
            Vector3 s = CourseMarks.PostScale(DressingPlan.POST_H, DressingPlan.POST_THICK);
            Assert.AreEqual(DressingPlan.POST_H, s.x * CourseMarks.WOODLOG_01_LEN, 1e-4f);
            Assert.AreEqual(DressingPlan.POST_THICK, s.y * CourseMarks.WOODLOG_01_H, 1e-4f);
            Assert.AreEqual(DressingPlan.POST_THICK, s.z * CourseMarks.WOODLOG_01_W, 1e-4f);
            var posts = Stage(0).plan.Where(p => p.group == "GuideLine" && p.part == DressingPlan.POST_PART).ToList();
            Assert.Greater(posts.Count, 10);
            foreach (var p in posts)
            {
                Assert.AreEqual(90f, p.rollDeg);
                Assert.LessOrEqual(p.scale * p.scaleZ * CourseMarks.WOODLOG_01_W, 0.35f, "細い杭");
            }
        }

        // ---- art-r3-03：カーブの外側の柵は途切れない ----
        [TestCaseSource(nameof(Stages))]
        public void GuideFenceIsContinuous(int i)
        {
            var b = Stage(i);
            var runs = DressingPlan.GuideFenceRuns(b.def, b.local, b.goal);
            Assert.GreaterOrEqual(runs.Count, 3);
            int pieces = 0;
            foreach (var run in runs)
            {
                float prevYaw = float.NaN;
                Vector3 prevEnd = Vector3.zero;
                for (int k = 0; k + 1 < run.joints.Count; k++, pieces++)
                {
                    DressingPlan.RailPiece(run.joints[k], run.joints[k + 1], k, out string part, out Vector3 c, out float yaw);
                    Assert.That(part, Is.EqualTo("FarmProps_03").Or.EqualTo("FarmProps_04"));
                    Vector3 d = run.joints[k + 1] - run.joints[k];
                    Assert.AreEqual(DressingPlan.RAIL_LEN, new Vector3(d.x, 0f, d.z).magnitude, 0.05f, "柵 1 枚の長さ");
                    float lineYaw = WorldLayout.YawDeg(new Vector3(d.x, 0f, d.z).normalized);
                    Vector3 start = c - new Vector3(d.x, 0f, d.z) * 0.5f, end = c + new Vector3(d.x, 0f, d.z) * 0.5f;
                    if (k > 0)
                    {
                        Assert.LessOrEqual(WorldLayout.DistXZ(prevEnd, start), 0.4f, "端と端");
                        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(prevYaw, lineYaw)), 15f, "向きの変化");
                    }
                    prevYaw = lineYaw; prevEnd = end;
                    // 長い辺を線に沿わせる：03 は yaw = 線 + 90（X が長い）、04 は yaw = 線（Z が長い）
                    Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, part == "FarmProps_04" ? lineYaw : lineYaw + 90f), 1e-3f);
                }
            }
            TestContext.WriteLine($"stage {i}: {runs.Count} runs, {pieces} rails");
        }

        // ---- art-r3-04：遺物は遠くから見える ----
        [Test] public void RelicStyleGlows()
        {
            var st = ArtPalette.StyleFor("Packs/Gems/Gems.fbx", "Gems_01_01");
            Assert.Greater(st.glow, 0.5f);
            Assert.AreEqual(0f, ArtPalette.StyleFor("Packs/Cacti/Cacti.fbx", "Cacti_01").glow);
        }

        // ---- art-r3-05：崖の板は道から 260m より遠くだけ、砂漠には置かない ----
        [TestCaseSource(nameof(Stages))]
        public void CliffsOnlyFar(int i)
        {
            var b = Stage(i);
            foreach (var p in b.plan.Where(q => q.asset == DressingPlan.CLIFFS))
            {
                Assert.AreNotEqual(1, i, "砂漠に崖の板");
                Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(p.position, b.local) - DressingPlan.FootprintRadius(p), DressingPlan.CLIFF_FAR_M - 0.01f, p.group + " " + p.part);
            }
            Assert.AreEqual(0.1f, ArtPalette.StyleFor(DressingPlan.CLIFFS, "Cliffs_01").flatten, 1e-5f);
        }

        // ---- art-r3-06：砂漠の道は明るい踏み固めた土、縁は硬く、轍 ----
        [Test] public void DunesTrailIsLightEarthWithHardEdge()
        {
            float t = LightingPlan.Luminance(ArtPalette.TrailDunes), sand = LightingPlan.Luminance(ArtPalette.SandLayer);
            Assert.GreaterOrEqual(t, 0.85f * sand, "art-r4-05：砂の 85% 以上の明るさ");
            Assert.Greater(t, 0.45f, "アスファルトの暗さにしない");
            Assert.AreEqual(1f, HeightmapGen.TrailWeight(11.4f, 12f), 1e-3f);
            Assert.AreEqual(0f, HeightmapGen.TrailWeight(12.6f, 12f), 1e-3f);
            Assert.Greater(HeightmapGen.RutWeight(HeightmapGen.RUT_OFFSET_M), 0.99f);   // art-r6-04：±0.9m・幅 0.35m
            Assert.Greater(HeightmapGen.RutWeight(HeightmapGen.RUT_OFFSET_M + 0.08f), 0.5f);
            Assert.Less(HeightmapGen.RutWeight(0.4f), 0.01f);
            Assert.Less(HeightmapGen.RutWeight(3.0f), 0.01f);
        }

        // ---- art-r3-07：境目の色は地形に従い、近景がある ----
        /// <summary>art-r4-06 で等高線の帯（ArtRound4Tests.BorderSandIsBanded）に置き換えた。尾根・台地の上に砂の塊が無いことだけ残す。</summary>
        [Test] public void MesaSandFollowsBasins() => MesaCrestHasLittleSand();

        static void MesaCrestHasLittleSand()
        {
            var b = Stage(2);
            var sand = HeightmapGen.SandMask(129, HeightmapGen.KIND_MESA, b.def.seed, b.h);
            float total = 0f; int n = sand.GetLength(0);
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) total += sand[z, x];
            float share = total / (n * n);
            TestContext.WriteLine($"border sand share {share:F3}");
            Assert.That(share, Is.InRange(0.02f, 0.35f), "帯の砂は地面の一部だけ");
        }

        [Test] public void MesaNearFieldDensity()
        {
            var b = Stage(2);
            var cum = DressingPlan.CourseArcs(b.local);
            float pass = SetPieces.PassArc(b.def, b.goal);
            int windows = Mathf.CeilToInt(b.goal / DressingPlan.BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in b.plan)
            {
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out float d);
                if (DressingPlan.RoadClearance(b.def, b.local, cum, p.position) > DressingPlan.NEAR_FIELD_M) continue;
                int k = Mathf.FloorToInt(a / DressingPlan.BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            for (int k = 0; k < windows; k++)
            {
                float c = (k + 0.5f) * DressingPlan.BAND_WINDOW_M;
                if (c > b.goal - 60f) continue;
                Assert.GreaterOrEqual(counts[k], DressingPlan.NEAR_PER_100M, $"window {k * 100}-{k * 100 + 100} m");
            }
            Assert.Greater(b.plan.Count(p => p.group == "MesaFloor"), 80);
        }

        // ---- art-r3-08：切り通しの手前に背（こぶ）が無い、門の目印 ----
        [Test] public void PassHasNoHump()
        {
            var b = Stage(2);
            float pass = SetPieces.PassArc(b.def, b.goal);
            Assert.GreaterOrEqual(pass, 0f);
            var hs = new List<float>();
            for (float a = pass - 120f; a <= pass + 60f + 0.01f; a += 2f)
            {
                Vector3 p = WorldLayout.PointAtArc(b.local, a);
                hs.Add(b.Ground(p.x, p.z));
            }
            float h0 = hs[0], h1 = hs[hs.Count - 1];
            float worst = -99f;   // 局所の最大が無い（単調）なら −99
            for (int i = 1; i + 1 < hs.Count; i++)
            {
                if (hs[i] < hs[i - 1] || hs[i] < hs[i + 1]) continue;   // 局所の最大だけ
                float chord = h0 + (h1 - h0) * i / (hs.Count - 1f);
                worst = Mathf.Max(worst, hs[i] - chord);
            }
            TestContext.WriteLine($"pass hump above chord: {worst:F2} m");
            Assert.LessOrEqual(worst, 0.4f);
            // 門：メサ ×2.4 が 2 つ、尾根の焚き火 2 つ、底の道ばたは岩（杭なし）
            Assert.AreEqual(2, b.plan.Count(p => p.group == "MesaGate" && (p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB) && Mathf.Abs(p.scale - DressingPlan.MESA_GATE_SCALE) < 1e-3f), "メサ ×2.4");
            Assert.AreEqual(2, b.plan.Count(p => p.group == "MesaGate" && p.asset == DressingPlan.CAMPFIRE), "尾根の焚き火: " + string.Join(",", b.plan.Where(p => p.group == "MesaGate").Select(p => p.asset)));
            Assert.Greater(b.plan.Count(p => p.group == "PassFloor"), 10);
            var cum = DressingPlan.CourseArcs(b.local);
            foreach (var p in b.plan.Where(q => q.part == DressingPlan.POST_PART && q.rollDeg == 90f && !q.lamp))   // art-r4-14：切り通しの灯りの柱は別
                Assert.Greater(Mathf.Abs(DressingPlan.NearestArc(b.local, cum, p.position, out float _) - pass), SetPieces.PASS_HALF_CUT, "切り通しの中に杭");
        }

        // ---- art-r3-09：町の大通りの飾り ----
        [Test] public void LobbyStreetDensity()
        {
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            var mn = new Vector2(t.x, t.y); var mx = new Vector2(t.z, t.w);
            var l = DressingPlan.ForLobby(mn, mx, LobbyLayout.LobbyDressingExclusions(), 4242);
            Assert.LessOrEqual(l.Count, DressingPlan.MAX_LOBBY);
            foreach (var line in DressingPlan.BoardwalkLines(mn, mx))
            {
                float len = WorldLayout.DistXZ(line[0], line[1]);
                int n = l.Count(p => p.group == "StreetDressing" && WorldLayout.DistToSegmentXZ(p.position, line[0], line[1]) < 4f);
                TestContext.WriteLine($"boardwalk {len:F0} m: {n} props");
                Assert.GreaterOrEqual(n, len / 8f);
            }
            Assert.Greater(l.Count(p => p.group == "StreetDressing" && p.part == "FarmProps_02"), 4, "街灯");
            Assert.Greater(l.Count(p => p.group == "PlateEdge"), 60, "地面の板の縁");
        }

        // ---- art-r3-10：外の地面の山並みは判押しにしない ----
        [Test] public void BackdropNotRegular()
        {
            var b = DressingPlan.Backdrop(7);
            Vector4 u = WorldBounds.Union();
            var c = new Vector3((u.x + u.z) * 0.5f, 0f, (u.y + u.w) * 0.5f);
            float cv = DressingPlan.SpacingVariation(b, "Backdrop", c);
            TestContext.WriteLine($"backdrop spacing CV {cv:F2}");
            Assert.Greater(cv, 0.3f);
            var scales = b.Where(p => p.group == "Backdrop").Select(p => p.scale).ToList();
            Assert.Greater(scales.Max() / scales.Min(), 2f, "大きさのばらつき");
        }

        // ---- art-r3-11：砂漠のスタートに停車場、最初の 120m は平ら ----
        [Test] public void DunesStartIsAStagedPlace()
        {
            var b = Stage(1);
            var halt = b.plan.Where(p => p.group == "RailHalt").ToList();
            Assert.IsTrue(halt.Any(p => p.part == DressingPlan.RAIL_TRACK), "線路");
            Assert.IsTrue(halt.Any(p => p.part == DressingPlan.TOWN_TALL_A), "給水塔");
            Assert.GreaterOrEqual(halt.Count(p => p.asset == DressingPlan.TENT), 2);
            Assert.IsTrue(halt.Any(p => p.asset == DressingPlan.CAMEL));
            Vector3 s0 = WorldLayout.PointAtArc(b.local, StageDefinitions.START_ARC_M);
            float y0 = b.Ground(s0.x, s0.z);
            for (float a = StageDefinitions.START_ARC_M; a <= StageDefinitions.START_ARC_M + SetPieces.START_FLAT_M; a += 10f)
            {
                Vector3 p = WorldLayout.PointAtArc(b.local, a);
                Assert.AreEqual(y0, b.Ground(p.x, p.z), 0.6f, "スタートの直線が平ら " + a);
            }
        }

        // ---- art-r3-13：尾根の岩は地面に下りている ----
        [Test] public void RimCrownGrounded()
        {
            var f = LobbyLayout.FENCE_RECT;
            var crown = LobbyLayout.RimCrown(f, 11);
            Assert.Greater(crown.Count, 5);
            foreach (var c in crown)
            {
                float r = c.radius * LobbyLayout.CROWN_FOOT;
                float lowest = LobbyLayout.GroundHeight(f, c.position);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    lowest = Mathf.Min(lowest, LobbyLayout.GroundHeight(f, new Vector3(c.position.x + Mathf.Sin(a) * r, 0f, c.position.z + Mathf.Cos(a) * r)));
                }
                Assert.LessOrEqual(c.position.y, lowest - 0.1f * c.height, c.asset);
            }
            var rs = ArtPalette.StyleFor(DressingPlan.RSTYL, "RockSetStylized_01_Rock");
            Assert.IsTrue(ArtPalette.IsWarmRock(rs.tint), "岩のセットは暖色");
        }

        // ---- art-r3-15：ゴールの町の通りの飾りと建物の灯り ----
        [Test] public void GoalTownStreetIsDressed()
        {
            var b = Stage(2);
            var street = b.plan.Where(p => p.group == "StreetDressing").ToList();
            Assert.Greater(street.Count, 40);
            Assert.Greater(street.Count(p => p.part == "FarmProps_02"), 8, "街灯 16m ごと");
            Assert.Greater(street.Count(p => p.part == DressingPlan.TOWN_CART || p.part == DressingPlan.TOWN_BARRELS), 4, "荷車 30m ごと");
        }
    }
}
