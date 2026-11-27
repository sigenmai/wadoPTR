using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>コース構成（course-r1）：ステージごとに形・カーブ・高さ・道幅・遺物がちがい、走り方が変わること。</summary>
    public partial class StageDefinitionsTests
    {

        static Vector3[] Local(int i) => StageDefinitions.SampleLocal(StageDefinitions.All[i]);
        static float Goal(int i) => StageDefinitions.GoalArc(StageDefinitions.All[i], Local(i));

        /// <summary>course-r1-01：3 ステージの形は互いに平均 120m 以上ちがう（同じコースを 3 回走らない）。</summary>
        [Test] public void StagesHaveDifferentShapes()
        {
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                {
                    float d = StageDefinitions.MeanPathDistance(Local(i), Local(j));
                    TestContext.WriteLine($"stage {i}-{j}: mean distance {d:F0} m");
                    Assert.GreaterOrEqual(d, StageDefinitions.MIN_STAGE_SEPARATION_M);
                }
        }

        [Test] public void MeanPathDistanceOfSameCourseIsZero()
        {
            var a = Local(0);
            Assert.AreEqual(0f, StageDefinitions.MeanPathDistance(a, a), 1e-3);
        }

        /// <summary>course-r1-02：急カーブ（10m 前後の窓で半径 45m 未満）が 3 つ以上、どれも 22m 以上（駈歩なら曲がれる）、中くらいのカーブも 2 つ以上。</summary>
        [TestCaseSource(nameof(Stages))]
        public void TightCornersNeedALine(int i)
        {
            var s = StageDefinitions.All[i];
            var local = Local(i);
            float goal = Goal(i);
            var tight = StageDefinitions.TightCorners(s, local);
            var all = StageDefinitions.Corners(local, StageDefinitions.MEDIUM_CORNER_MAX_R, goal);
            float minR = StageDefinitions.MinCornerRadius(local, 0f, goal);
            TestContext.WriteLine($"stage {i}: tight {tight.Count}, corners {all.Count}, min radius {minR:F1} m");
            Assert.GreaterOrEqual(tight.Count, StageDefinitions.MIN_TIGHT_CORNERS);
            Assert.GreaterOrEqual(minR, StageDefinitions.TIGHT_CORNER_MIN_R);
            Assert.GreaterOrEqual(all.Count - tight.Count, 2, "medium corners");
            // 急カーブの手前は速く走れる区間（他のカーブから 160m 以上、うち 2 つは 250m 以上）
            int longApproach = 0;
            foreach (var c in tight)
            {
                float prev = 0f;
                foreach (var o in all) if (o.toArc < c.fromArc - 1f) prev = Mathf.Max(prev, o.toArc);
                float approach = c.fromArc - prev;
                TestContext.WriteLine($"  tight apex {c.apexArc:F0} r {c.radius:F0} approach {approach:F0}");
                Assert.GreaterOrEqual(approach, 160f);
                if (approach >= 250f) longApproach++;
            }
            Assert.GreaterOrEqual(longApproach, 2);
        }

        [Test] public void RadiusOfACircleIsMeasured()
        {
            var pts = new List<Vector3>();
            for (int k = 0; k < 90; k++) { float a = k * 2f / 30f; pts.Add(new Vector3(Mathf.Sin(a) * 30f, 0f, Mathf.Cos(a) * 30f)); }
            Assert.AreEqual(30f, StageDefinitions.MinCornerRadius(pts.ToArray()), 0.5f);
            var c = StageDefinitions.Corners(pts.ToArray(), 45f, 1000f);
            Assert.AreEqual(1, c.Count);
        }

        /// <summary>course-r1-03：砂漠は 10〜14m の稜線を 6〜8 回越える（上り ≤12°、下り ≤15°）。荒野はゴールまでに 40m 下る。境目は 32m の台地に登る。</summary>
        [Test] public void ElevationGivesEachStageItsCharacter()
        {
            var dunes = StageDefinitions.All[1];
            int crests = 0;
            for (int k = 1; k + 1 < dunes.elevM.Length; k++)
                if (dunes.elevM[k] > dunes.elevM[k - 1] && dunes.elevM[k] > dunes.elevM[k + 1] && dunes.elevM[k] - dunes.elevM[k + 1] >= 10f && dunes.elevM[k] <= 14f) crests++;
            Assert.That(crests, Is.InRange(6, 8));
            for (int s = 0; s < 3; s++)
            {
                var d = StageDefinitions.All[s];
                for (int k = 1; k < d.elevArc.Length; k++)
                {
                    float run = d.elevArc[k] - d.elevArc[k - 1], rise = d.elevM[k] - d.elevM[k - 1];
                    if (run < 1f) { Assert.Less(Mathf.Abs(rise), 0.01f); continue; }
                    float deg = Mathf.Atan(Mathf.Abs(rise) / run) * Mathf.Rad2Deg;
                    Assert.LessOrEqual(deg, rise > 0f ? 12f : 15f, "stage " + s + " key " + k);
                }
            }
            var canyon = StageDefinitions.All[0];
            Assert.AreEqual(40f, StageDefinitions.ElevationAt(canyon, 0f) - StageDefinitions.ElevationAt(canyon, Goal(0)), 0.01f);
            var mesa = StageDefinitions.All[2];
            Assert.AreEqual(32f, StageDefinitions.ElevationAt(mesa, mesa.mesaClimbFraction * Goal(2) + 1f), 0.5f);
        }

        /// <summary>course-r1-03/04：道の高さは地形に出る（荒野は下る、砂漠は稜線、境目は台地）。</summary>
        [TestCaseSource(nameof(Stages))]
        public void PathFollowsElevationProfile(int i)
        {
            var s = StageDefinitions.All[i];
            var local = Local(i);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            float baseM = HeightmapGen.BaseHeight(s.kind) * HeightmapGen.HEIGHT_M;
            for (float a = 100f; a < Goal(i); a += 97f)
            {
                var p = WorldLayout.PointAtArc(local, a);
                float y = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
                Assert.AreEqual(baseM + StageDefinitions.ElevationAt(s, a), y, 4f, "arc " + a);
            }
        }

        /// <summary>course-r1-10：絞り（半幅 7m、60〜90m）が 2 つ、追い抜きの広場（半幅 18m）が 2 つ以上。</summary>
        [TestCaseSource(nameof(Stages))]
        public void WidthHasChokesAndOvertakingZones(int i)
        {
            var s = StageDefinitions.All[i];
            int chokes = 0, wide = 0;
            for (int k = 0; k < s.halfWidthM.Length; k++)
            {
                float len = s.widthArc[2 * k + 1] - s.widthArc[2 * k];
                if (s.halfWidthM[k] <= StageDefinitions.CHOKE_HALF_M) { chokes++; Assert.That(len, Is.InRange(60f, 90f)); }
                if (s.halfWidthM[k] >= StageDefinitions.WIDE_HALF_M) { wide++; Assert.GreaterOrEqual(len, 85f); }
            }
            Assert.That(chokes, Is.InRange(2, 3));
            Assert.GreaterOrEqual(wide, 2);
            Assert.AreEqual(StageDefinitions.START_HALF_M, StageDefinitions.HalfWidthAt(s, 5f), 0.01f);   // course-r3-09：広いスタート
            Assert.AreEqual(HeightmapGen.RUNWAY_M, StageDefinitions.HalfWidthAt(s, 215f), 0.01f);
            for (int k = 0; k < s.halfWidthM.Length; k++)
                Assert.AreEqual(s.halfWidthM[k], StageDefinitions.HalfWidthAt(s, 0.5f * (s.widthArc[2 * k] + s.widthArc[2 * k + 1])), 0.01f);
        }

        /// <summary>course-r1-10：8 頭（18.2m）が横に並べるのは広い所だけ。絞りは 3〜4 頭。</summary>
        [Test] public void ChokeFitsThreeToFourHorses()
        {
            float lanes = 2f * StageDefinitions.CHOKE_HALF_M / StageDefinitions.LANE_SPACING_M;
            Assert.That(lanes, Is.InRange(3f, 6f));
            Assert.Greater(2f * StageDefinitions.WIDE_HALF_M, StageDefinitions.LANES * StageDefinitions.LANE_SPACING_M);
        }

        /// <summary>course-r1-05：境目の台地の上に鞍部（絞り）がある。荒野の岩の橋・砂漠の踏み切りの手前も絞り。</summary>
        [Test] public void ChokesSitOnSetPieces()
        {
            var c = StageDefinitions.All[0];
            Assert.AreEqual(SetPieces.SPINE_HALF, StageDefinitions.HalfWidthAt(c, SetPieces.BridgeArc(c, Goal(0))), 0.01f);
            var d = StageDefinitions.All[1];
            Assert.AreEqual(StageDefinitions.CHOKE_HALF_M, StageDefinitions.HalfWidthAt(d, SetPieces.JumpArc(d, Goal(1)) - 30f), 0.01f);
            var m = StageDefinitions.All[2];
            float top0 = m.mesaClimbFraction * Goal(2), top1 = m.mesaTopEndFraction * Goal(2);
            bool saddle = false;
            for (float a = top0; a <= top1; a += 5f) if (StageDefinitions.HalfWidthAt(m, a) <= StageDefinitions.CHOKE_HALF_M) saddle = true;
            Assert.IsTrue(saddle);
        }

        /// <summary>course-r1-09：コースのどの点でも踏み固めた土の重みが 0.9 以上（道が読める）。道の外 8m では 0。</summary>
        [TestCaseSource(nameof(Stages))]
        public void TrailCoversTheWholeCourse(int i)
        {
            var s = StageDefinitions.All[i];
            var local = Local(i);
            var m = HeightmapGen.TrailMask(StageDefinitions.ALPHAMAP_RES, s, StageDefinitions.Normalize(local));
            int n = m.GetLength(0);
            float min = 1f;
            foreach (var p in local)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(p.x / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt(p.z / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
                min = Mathf.Min(min, m[z, x]);
            }
            TestContext.WriteLine($"stage {i}: min trail weight {min:F2}");
            Assert.GreaterOrEqual(min, 0.9f);
            Assert.AreEqual(1f, HeightmapGen.TrailWeight(9f, 12f), 1e-4);
            Assert.AreEqual(0f, HeightmapGen.TrailWeight(16.1f, 12f), 1e-4);
            // 道の上の砂は消える
            var sand = HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES, s.kind, s.seed);
            HeightmapGen.ClearSandOnTrail(sand, m);
            foreach (var p in local)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(p.x / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt(p.z / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
                Assert.AreEqual(0f, sand[z, x], 1e-4);
            }
        }

        /// <summary>course-r1-11：遺物は危ない線・遠回りの線に。コースの中心から 6m 以内は 1 つまで（先頭がまっすぐ走るだけでは全部拾えない）。</summary>
        [TestCaseSource(nameof(Stages))]
        public void RelicsSitOnRiskyLines(int i)
        {
            var s = StageDefinitions.All[i];
            var local = Local(i);
            float goal = Goal(i);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var plan = SetPieces.ApplyTerrain(s, local, h, goal);
            var spots = SetPieces.RelicSpots(s, local, goal, plan);
            Assert.AreEqual(StageDefinitions.RELICS_PER_STAGE, spots.Length);
            int onLine = 0;
            foreach (var r in spots)
            {
                float d = WorldLayout.DistToPolylineXZ(r.local, local);
                TestContext.WriteLine($"stage {i}: relic arc {r.arc:F0} lateral {d:F1} up {r.up}");
                if (d < 6f) onLine++;
            }
            Assert.LessOrEqual(onLine, 1);
            // 3 つ目は近道の上
            Assert.Greater(WorldLayout.DistToPolylineXZ(spots[2].local, local), SetPieces.RUNWAY_KEEP);
            Assert.Less(WorldLayout.DistToSegmentXZ(spots[2].local, plan.a, plan.b), 0.5f);
            // 4 つ目は最後の下りの入口の内側
            Assert.AreEqual(StageDefinitions.DashArc(s, goal), spots[3].arc, 0.01f);
            Assert.AreEqual(-SetPieces.RELIC_WIDE_SIDE, spots[3].side, 0.01f);
            if (s.kind == HeightmapGen.KIND_DUNES) Assert.AreEqual(SetPieces.JUMP_RELIC_UP, spots[1].up, 1e-4);
        }

        /// <summary>course-r1-14：CP の箱の大きさは 1 か所（CourseGeometry）で決まり、柵の計算も同じ値を使う。</summary>
        [Test] public void CheckpointBoxSizeHasOneSource()
        {
            Assert.AreEqual(6f, CourseBarrier.CP_DEPTH);
            Assert.AreEqual(36f, CourseBarrier.CP_WIDTH);
            Assert.AreEqual(CourseGeometry.CP_DEPTH, CourseBarrier.CP_DEPTH);
            Assert.AreEqual(CourseGeometry.CP_WIDTH, CourseBarrier.CP_WIDTH);
        }

        /// <summary>course-r1-10 / system-r2-02：CP の箱は道幅より広く、柵の 2m 内側まで届く。</summary>
        [TestCaseSource(nameof(Stages))]
        public void CheckpointBoxesFollowWidth(int i)
        {
            var s = StageDefinitions.All[i];
            var local = Local(i);
            float goal = Goal(i);
            foreach (float a in WorldLayout.CheckpointArcs(goal, StageDefinitions.CP_SPACING_M))
            {
                float half = CourseBarrier.CpHalfWidth(s, local, goal, a);
                float hw = StageDefinitions.HalfWidthAt(s, a);
                Assert.GreaterOrEqual(half, hw + 2f, "arc " + a);
                // system-r2-02：箱は柵の 2m 内側まで（絞りでも柵との間に抜け道を残さない）
                Assert.AreEqual(CourseBarrier.OffsetAtArc(s, local, goal, a) - CourseBarrier.CP_SIDE_CLEAR_M, half, 1e-3f);
            }
        }

        /// <summary>course-r1-04：黒板は最後の下りの始まりの直前の CP にも立つ（順位を読んでから全力）。</summary>
        [TestCaseSource(nameof(Stages))]
        public void BoardBeforeTheFinalPush(int i)
        {
            float goal = Goal(i);
            var cps = WorldLayout.CheckpointArcs(goal, StageDefinitions.CP_SPACING_M);
            var s = StageDefinitions.All[i];
            int k = CourseMarks.DashBoardCp(s, cps, goal);
            Assert.GreaterOrEqual(k, 0);
            Assert.IsTrue(CourseMarks.HasBoard(s, cps, k, goal));
            Assert.That(StageDefinitions.DashArc(s, goal) - cps[k], Is.InRange(0f, StageDefinitions.CP_SPACING_M));
            int chokes = 0;
            for (int j = 0; j < s.halfWidthM.Length; j++) if (s.halfWidthM[j] <= StageDefinitions.CHOKE_HALF_M && s.widthArc[2 * j] - CourseMarks.CHOKE_WARN_M > 40f) chokes++;
            Assert.AreEqual(chokes, CourseMarks.ChokeWarnArcs(s).Count);
        }
    }
}
