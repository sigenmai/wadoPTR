using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>コース構成 round 2（course-r2-01〜15）。</summary>
    public class CourseRound2Tests
    {
        static StageDef Def(int i) => StageDefinitions.All[i];
        static Vector3[] Local(int i) => StageDefinitions.SampleLocal(Def(i));
        static float Goal(int i) => StageDefinitions.GoalArc(Def(i), Local(i));

        // ---- course-r2-01：曲がるときの速さの上限 ----
        [Test] public void CornerSpeedCapFollowsLateralGrip()
        {
            // 18 m/s で走れるのは半径 46m 以上、駈歩（14 m/s）は半径 28m
            float w18 = 18f / 46.3f * Mathf.Rad2Deg, w14 = 14f / 28f * Mathf.Rad2Deg;
            Assert.AreEqual(18f, MountMath.CornerSpeedCap(w18), 0.1f);
            Assert.AreEqual(14f, MountMath.CornerSpeedCap(w14), 0.1f);
            Assert.AreEqual(MountMath.CornerSpeedCap(30f), MountMath.CornerSpeedCap(-30f), 1e-5f);
            Assert.Greater(MountMath.CornerSpeedCap(0f), 100f, "まっすぐなら上限なし");
            // 疾走で目いっぱい曲がり続けると約 8 m/s まで落ちる
            Assert.AreEqual(MountMath.LAT_ACCEL_MAX / (MountMath.TURN_RATE_FAST * Mathf.Deg2Rad), MountMath.CornerSpeedCap(MountMath.TURN_RATE_FAST), 1e-4f);
        }

        [Test] public void YawRateIsSmoothedSoTapsDoNotBrake()
        {
            float s = 0f;
            for (int k = 0; k < 6; k++) s = MountMath.SmoothYawRate(s, 50f, 1f / 60f);   // 0.1 秒だけ目いっぱい
            Assert.Greater(MountMath.CornerSpeedCap(s), 16f, "短いキー操作では減速しない");
            for (int k = 0; k < 120; k++) s = MountMath.SmoothYawRate(s, 50f, 1f / 60f);  // 2 秒曲がり続ける
            Assert.AreEqual(50f, s, 1f);
            Assert.Less(MountMath.CornerSpeedCap(s), 9f);
        }

        [Test] public void DownhillBonusIsFelt()
        {
            Assert.AreEqual(18f * 1.1f, MountMath.TargetSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT, -5f, false, false), 1e-3f);
            Assert.AreEqual(18f * MountMath.DOWNHILL_MAX, MountMath.TargetSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT, -20f, false, false), 1e-3f);
        }

        /// <summary>各ステージにヘアピン（150° 以上、中心線の半径 16〜20m）が 1 つ。柵の中で取れる一番大きな線でも 0.85·最高速より遅い。</summary>
        [Test] public void TightCornerForcesLift([Values(0, 1, 2)] int i)
        {
            var s = Def(i);
            var local = Local(i);
            float goal = Goal(i);
            var hps = StageDefinitions.Hairpins(s, local);
            Assert.AreEqual(1, hps.Count, "ヘアピンは 1 つ");
            foreach (var c in StageDefinitions.TightCorners(s, local))
            {
                float turn = StageDefinitions.TurnDeg(local, c);
                float off = CourseBarrier.OffsetAtArc(s, local, goal, c.apexArc);
                float line = StageDefinitions.BestLineRadius(turn, c.radius, off);
                float cap = Mathf.Sqrt(MountMath.LAT_ACCEL_MAX * line);   // 一定半径の線での速さの上限（v = √(a·R)）
                bool hairpin = Mathf.Abs(turn) >= StageDefinitions.HAIRPIN_MIN_TURN_DEG;
                TestContext.WriteLine($"stage {i}: corner {c.apexArc:F0} R {c.radius:F1} turn {turn:F0} barrier {off:F1} best line R {line:F0} → {cap:F1} m/s {(hairpin ? "HAIRPIN" : "")}");
                if (!hairpin) continue;
                Assert.That(c.radius, Is.InRange(StageDefinitions.TIGHT_CORNER_MIN_R, 20f));
                Assert.Less(cap, 0.85f * MountMath.TOP_SPEED, "ヘアピンは疾走のままでは曲がれない");
                Assert.AreEqual(9f, StageDefinitions.HalfWidthAt(s, c.apexArc), 0.01f);
            }
            Assert.Greater(StageDefinitions.BestLineRadius(90f, 35f, 16f), 100f, "90° の急カーブは線を取れば速いまま");
        }

        /// <summary>course-r2-03：全力の合図より手前で、半径 250m より大きい区間が 320m を超えて続かない。</summary>
        [Test] public void MaxStraightM([Values(0, 1, 2)] int i)
        {
            float m = StageDefinitions.MaxStraightM(Def(i), Local(i));
            TestContext.WriteLine($"stage {i}: longest straight before the dash {m:F0} m");
            Assert.LessOrEqual(m, StageDefinitions.MAX_STRAIGHT_M);
        }

        /// <summary>course-r2-10：コースは外周から 180m 以上内側（ゴールの 120m 手前から先＝切れ込みへ向かう所を除く）。</summary>
        [Test] public void CourseKeepsOffRim([Values(0, 1, 2)] int i)
        {
            float m = StageDefinitions.MinRimClearance(Def(i), Local(i));
            TestContext.WriteLine($"stage {i}: min distance to the rim {m:F0} m");
            Assert.GreaterOrEqual(m, StageDefinitions.RIM_KEEP_M);
        }

        /// <summary>course-r2-09：遺物は 350m 以上離れ、最初の遺物はコースの 4 分の 1 より手前、4 分の 1 ごとに 1 つ。</summary>
        [Test] public void RelicsSpread([Values(0, 1, 2)] int i)
        {
            var s = Def(i);
            var local = Local(i);
            float goal = Goal(i);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var plan = SetPieces.ApplyTerrain(s, local, h, goal);
            var spots = SetPieces.RelicSpots(s, local, goal, plan);
            var arcs = spots.Select(r => r.arc).OrderBy(a => a).ToArray();
            TestContext.WriteLine($"stage {i}: relics at {string.Join(", ", arcs.Select(a => a.ToString("F0")))} of {goal:F0}");
            Assert.GreaterOrEqual(SetPieces.MinRelicGap(spots), SetPieces.RELIC_MIN_GAP_M);
            Assert.Less(arcs[0], 0.25f * goal);
            Assert.GreaterOrEqual(arcs[0], 300f);
            for (int q = 0; q < 4; q++)
                Assert.IsTrue(arcs.Any(a => a >= q * 0.25f * goal - 1f && a < (q + 1) * 0.25f * goal), "quarter " + q);
        }

        /// <summary>course-r2-12：3 ステージは長さがちがう（荒野 = 短い、砂漠 = 長い、境目 = その間）。荒野は急カーブ 4 つ。</summary>
        [Test] public void StagesHaveDifferentLengths()
        {
            float c = Goal(0), d = Goal(1), m = Goal(2);
            TestContext.WriteLine($"canyon {c:F0}, dunes {d:F0}, mesa {m:F0}");
            Assert.Less(c, m);
            Assert.Less(m, d);
            Assert.Greater(d - c, 300f);
            Assert.AreEqual(4, StageDefinitions.TightCorners(Def(0), Local(0)).Count);
            Assert.Less(StageDefinitions.TightCorners(Def(1), Local(1)).Count, StageDefinitions.TightCorners(Def(0), Local(0)).Count, "砂漠はカーブが少なく速い");
        }

        /// <summary>course-r2-04：序盤（弧長 150〜500m）に見どころ。境目は町の通りから始まる。</summary>
        [Test] public void OpeningSetPieceIsEarly([Values(0, 1, 2)] int i)
        {
            float a = SetPieces.OpeningArc(Def(i), Goal(i));
            Assert.That(a, Is.InRange(100f, 500f));
            Assert.Less(a / Goal(i), 0.25f);
        }

        /// <summary>course-r2-07：全力の合図はカーブを抜けた後（合図から先に急カーブが無い）。</summary>
        [Test] public void DashStartsAfterTheLastTightCorner([Values(0, 1, 2)] int i)
        {
            var s = Def(i);
            float dash = StageDefinitions.DashArc(s, Goal(i));
            foreach (var c in StageDefinitions.TightCorners(s, Local(i))) Assert.Less(c.toArc, dash + 1f);
        }

        // ---- 飾りの計画（DressingPlan）----
        static readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Placement>> plans = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Placement>>();
        static System.Collections.Generic.List<Placement> Plan(int i)
        {
            if (plans.TryGetValue(i, out var p)) return p;
            var s = Def(i); var local = Local(i); float goal = Goal(i);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var sc = SetPieces.ApplyTerrain(s, local, h, goal);
            p = DressingPlan.For(s, local, h, goal, sc);
            plans[i] = p;
            return p;
        }

        /// <summary>course-r2-11：先へ誘う塔（RanchCompound_11）が 4 本以上、塔どうしは離れ、足元に焚き火（煙の柱）。</summary>
        [Test] public void GuideTowersPullTheRider([Values(0, 1, 2)] int i)
        {
            var plan = Plan(i);
            var towers = plan.Where(p => p.part == DressingPlan.RANCH_TOWER).ToList();
            var guides = plan.Where(p => p.group == "GuideTower" && p.part == DressingPlan.RANCH_TOWER).ToList();
            TestContext.WriteLine($"stage {i}: {towers.Count} towers ({guides.Count} guide towers)");
            Assert.GreaterOrEqual(towers.Count, 4);
            Assert.GreaterOrEqual(guides.Count, 2);
            foreach (var g in guides)
            {
                Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(g.position, Local(i)), DressingPlan.TOWER_ROAD_M - 0.01f, "柵の外");
                Assert.IsTrue(plan.Any(p => p.asset == DressingPlan.CAMPFIRE && WorldLayout.DistXZ(p.position, g.position) < 10f), "足元に煙");
                foreach (var o in guides) if (o != g) Assert.GreaterOrEqual(WorldLayout.DistXZ(o.position, g.position), DressingPlan.TOWER_SPACING_M - 0.01f);
            }
        }

        /// <summary>course-r2-08：急カーブごとに 110・70・35m 手前の外側に ››› ›› › の札（外側の道の縁と柵の間）。</summary>
        [Test] public void CornerBoardsCountDown([Values(0, 1, 2)] int i)
        {
            var s = Def(i); var local = Local(i); float goal = Goal(i);
            var boards = CourseMarks.CornerBoards(s, local, goal);
            var tight = StageDefinitions.TightCorners(s, local);
            Assert.AreEqual(tight.Count * 3, boards.Count);
            foreach (var b in boards)
            {
                float d = WorldLayout.DistToPolylineXZ(b.position, local);
                Assert.Greater(d, HeightmapGen.RUNWAY_M - 4f, "道の外");
                Assert.Less(d, CourseBarrier.OFFSET_M, "柵の内側");
            }
            Assert.AreEqual("›››", CourseMarks.Chevrons(3, 1));
            Assert.AreEqual("‹", CourseMarks.Chevrons(1, -1));
        }

        /// <summary>course-r2-15：25・50・75% に区間の門、「残り x.x km」。</summary>
        [Test] public void SectorGatesTellDistanceLeft([Values(0, 1, 2)] int i)
        {
            float goal = Goal(i);
            var arcs = CourseMarks.SectorGateArcs(goal);
            Assert.AreEqual(3, arcs.Count);
            for (int k = 0; k < 3; k++) Assert.AreEqual((k + 1) * 0.25f * goal, arcs[k], 12f);
            Assert.AreEqual("残り 1.8 km", CourseMarks.RemainingText(2400f, 600f));
            Assert.AreEqual(6, CourseMarks.SectorGatePosts(Def(i), Local(i), goal).Count);
        }

        /// <summary>course-r2-03：S 字の真ん中の岩の島が 1 つ以上、道の内側寄り（内側の線は短く狭い、外側は広い）。</summary>
        [Test] public void RoadIslandsSplitTheLine([Values(0, 1, 2)] int i)
        {
            var spots = DressingPlan.IslandSpots(Def(i), Local(i), Goal(i));
            TestContext.WriteLine($"stage {i}: islands at {string.Join(", ", spots.Select(v => v.x.ToString("F0")))}");
            Assert.GreaterOrEqual(spots.Count, 1);
            var plan = Plan(i);
            Assert.GreaterOrEqual(plan.Count(p => p.group == "RoadIsland" && p.collider), spots.Count);
            foreach (var v in spots) Assert.AreEqual(DressingPlan.ISLAND_LATERAL, Mathf.Abs(v.y), 1e-4f);
        }

        /// <summary>course-r2-13：二股は広場の中（左 = 狭く固い、右 = 広く砂）。2 本とも 3 頭以上が並べる。</summary>
        [Test] public void ForkInAWideZone([Values(0, 1, 2)] int i)
        {
            var s = Def(i);
            var div = DressingPlan.ForkDivider(s, Goal(i));
            Assert.Greater(div.Count, 10);
            foreach (var d in div)
            {
                float hw = StageDefinitions.HalfWidthAt(s, d.x);
                Assert.AreEqual(StageDefinitions.WIDE_HALF_M, hw, 0.01f, "広場の中");
                float left = hw + d.y - DressingPlan.FORK_ROCK_R, right = hw - d.y - DressingPlan.FORK_ROCK_R;
                Assert.Greater(left, 3f * StageDefinitions.LANE_SPACING_M);
                Assert.Greater(right, left);
            }
            Assert.GreaterOrEqual(Plan(i).Count(p => p.group == "Fork" && p.collider), div.Count / 2);
        }

        /// <summary>course-r2-04：序盤の見どころの素材（荒野 = 牧場の門と庭・風車、境目 = 町の建物と岩の門、砂漠 = 線路 2 本）。</summary>
        [Test] public void OpeningUsesExistingAssets()
        {
            var c = Plan(0);
            Assert.AreEqual(2, c.Count(p => p.group == "RanchYard" && (p.part == "RanchCompound_07_Building" || p.part == "RanchCompound_09_Building")));
            Assert.GreaterOrEqual(c.Count(p => p.group == "RanchYard" && p.part == DressingPlan.RANCH_TOWER), 1);
            Assert.GreaterOrEqual(c.Count(p => p.group == "RanchYard" && (p.part == "FarmProps_03" || p.part == "FarmProps_04")), 20);
            var d = Plan(1);
            Assert.AreEqual(SetPieces.RAIL_RUNS, d.Count(p => p.group == "Railway" && p.part == DressingPlan.RAIL_TRACK));
            var m = Plan(2);
            Assert.GreaterOrEqual(m.Count(p => p.group == "StartTown" && p.height > 5f), 10);
            Assert.GreaterOrEqual(m.Count(p => p.group == "StartTown" && p.asset == DressingPlan.NOTICE), 1);
            foreach (var p in m.Where(q => q.group == "StartTown" && q.height > 5f))
            {
                float dc;
                float a = DressingPlan.NearestArc(Local(2), DressingPlan.CourseArcs(Local(2)), p.position, out dc);
                Assert.Less(a, DressingPlan.START_TOWN_LEN_M + 30f);
            }
        }

        /// <summary>course-r2-06：柵の足元の土手（地形）は 1.2m、柵から 4m で 0、下げはしない。</summary>
        [Test] public void BermRaisesTheBarrierLine()
        {
            Assert.AreEqual(HeightmapGen.BERM_M, HeightmapGen.BermAt(0f), 1e-5f);
            Assert.AreEqual(0f, HeightmapGen.BermAt(HeightmapGen.BERM_HALF_M + HeightmapGen.BERM_FADE_M + 0.01f), 1e-5f);
            var h = new float[101, 101];
            var segs = new System.Collections.Generic.List<Vector3[]> { new[] { new Vector3(20f, 0f, 50f), new Vector3(80f, 0f, 50f) } };
            HeightmapGen.RaiseBerm(h, 100f, 200f, segs);
            Assert.AreEqual(HeightmapGen.BERM_M, WorldLayout.SampleHeight(h, 100f, 200f, 50f, 50f), 0.01f);
            Assert.AreEqual(0f, WorldLayout.SampleHeight(h, 100f, 200f, 50f, 60f), 1e-5f);
        }
    }
}
