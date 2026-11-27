using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class WorldLayoutTests
    {
        static Vector3 V(float x, float z) => new Vector3(x, 0f, z);

        [Test] public void CheckpointArcsEvery80EndAtGoal()
        {
            var a = WorldLayout.CheckpointArcs(500f, 80f);
            CollectionAssert.AreEqual(new[] { 80f, 160f, 240f, 320f, 400f, 500f }, a);
        }

        [Test] public void CheckpointArcsSkipCrowdedBeforeGoal()
        {
            // 480 は ゴール 510 の 30m 手前（< 40m）なので省く
            var a = WorldLayout.CheckpointArcs(510f, 80f);
            Assert.AreEqual(510f, a[a.Length - 1]);
            Assert.AreEqual(400f, a[a.Length - 2]);
        }

        [Test] public void CheckpointArcsGapsNeverExceed120()
        {
            for (float g = 100f; g < 3000f; g += 7f)
            {
                var a = WorldLayout.CheckpointArcs(g, 80f);
                float prev = 0f;
                foreach (float x in a) { Assert.LessOrEqual(x - prev, 120.001f); Assert.Greater(x - prev, 39.99f); prev = x; }
            }
        }

        [Test] public void LaneOffsetsCentered()
        {
            var o = WorldLayout.LaneOffsets(8, 2f);
            Assert.AreEqual(8, o.Length);
            Assert.AreEqual(-7f, o[0], 1e-5);
            Assert.AreEqual(7f, o[7], 1e-5);
            Assert.AreEqual(0f, o[3] + o[4], 1e-5);
        }

        [Test] public void PointAndTangentAtArc()
        {
            var pts = new[] { V(0, 0), V(10, 0), V(10, 10) };
            var p = WorldLayout.PointAtArc(pts, 15f);
            Assert.AreEqual(10f, p.x, 1e-4); Assert.AreEqual(5f, p.z, 1e-4);
            var t = WorldLayout.TangentAtArc(pts, 3f, 1f);
            Assert.AreEqual(1f, t.x, 1e-4);
            Assert.AreEqual(90f, WorldLayout.YawDeg(t), 1e-3);
            var r = WorldLayout.RightOf(new Vector3(0, 0, 1));
            Assert.AreEqual(1f, r.x, 1e-5);
        }

        [Test] public void SampleHeightBilinear()
        {
            var h = new float[,] { { 0f, 1f }, { 0f, 1f } };
            Assert.AreEqual(50f, WorldLayout.SampleHeight(h, 10f, 100f, 5f, 3f), 1e-3);
            Assert.AreEqual(100f, WorldLayout.SampleHeight(h, 10f, 100f, 20f, 3f), 1e-3);
        }

        [Test] public void StreetHeightIsAreaWeightedMode()
        {
            // 通り（13.75m）が面積で最大、屋根（18m）は面の数が多くても面積が小さい
            var hs = new List<float>(); var ar = new List<float>();
            for (int i = 0; i < 3; i++) { hs.Add(13.76f); ar.Add(100f); }
            for (int i = 0; i < 20; i++) { hs.Add(18.1f); ar.Add(2f); }
            hs.Add(0.2f); ar.Add(50f);
            Assert.AreEqual(13.75f, WorldLayout.EstimateStreetHeight(hs.ToArray(), ar.ToArray(), 0.25f, -1f), 1e-4);
            Assert.AreEqual(-1f, WorldLayout.EstimateStreetHeight(new float[0], null, 0.25f, -1f));
        }

        [Test] public void SandRunsMergeAlongX()
        {
            var m = new float[4, 4];
            m[0, 0] = m[0, 1] = m[0, 3] = 1f;
            m[2, 1] = m[2, 2] = 1f;
            var runs = WorldLayout.SandRuns(m, 4, 0.5f);
            Assert.AreEqual(3, runs.Count);
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, runs[0]);
            CollectionAssert.AreEqual(new[] { 0, 3, 3 }, runs[1]);
            CollectionAssert.AreEqual(new[] { 2, 1, 2 }, runs[2]);
            Assert.AreEqual(5f / 16f, WorldLayout.RunsCoverage(runs, 4), 1e-5);
        }

        [Test] public void DeviationFromChordsOfStraightCourseIsZero()
        {
            var pts = CoursePath.Sample(new[] { V(0, 0), V(0, 1000) }, 2f);
            Assert.Less(WorldLayout.MaxDeviationFromChords(pts, 6f, WorldLayout.CheckpointArcs(1000f, 80f)), 0.01f);
        }

        [Test] public void ScatterRespectsBand()
        {
            var course = CoursePath.Sample(new[] { V(100, 500), V(900, 500) }, 2f);
            var pts = WorldLayout.ScatterNearCourse(5, 20, 1000f, 50f, course, 30f, 120f, 15f);
            Assert.AreEqual(20, pts.Count);
            foreach (var p in pts)
            {
                float d = WorldLayout.DistToPolylineXZ(p, course);
                Assert.That(d, Is.InRange(30f, 120f));
            }
            CollectionAssert.AreEqual(pts, WorldLayout.ScatterNearCourse(5, 20, 1000f, 50f, course, 30f, 120f, 15f));
        }
    }

    public class StageDefinitionsTests
    {
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        [Test] public void ThreeStagesExact()
        {
            var s = StageDefinitions.All;
            Assert.AreEqual(3, s.Length);
            CollectionAssert.AreEqual(new[] { HeightmapGen.KIND_CANYON, HeightmapGen.KIND_DUNES, HeightmapGen.KIND_MESA },
                new[] { s[0].kind, s[1].kind, s[2].kind });
            Assert.AreEqual(-1200f, s[0].center.x); Assert.AreEqual(0f, s[1].center.x); Assert.AreEqual(1200f, s[2].center.x);
            Assert.AreEqual(0f, s[0].center.z + s[1].center.z + s[2].center.z);
        }

        [TestCaseSource(nameof(Stages))]
        public void CourseLengthInRange(int i)
        {
            var s = StageDefinitions.All[i];
            var pts = StageDefinitions.SampleLocal(s);
            float len = StageDefinitions.GoalArc(s, pts);
            TestContext.WriteLine($"stage {i}: {len:F0} m");
            Assert.That(len, Is.InRange(StageDefinitions.MIN_COURSE_M, StageDefinitions.MAX_COURSE_M));
        }

        [TestCaseSource(nameof(Stages))]
        public void CourseInsideTerrainWithMargin(int i)
        {
            foreach (var p in StageDefinitions.SampleLocal(StageDefinitions.All[i]))
            {
                Assert.That(p.x, Is.InRange(60f, 940f));
                Assert.That(p.z, Is.InRange(60f, 940f));
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void CourseDoesNotTouchItself(int i)
        {
            // 平らな道（±12m）と路肩のぼかし（90m）が別の区間と重ならないように 150m 以上離す
            var pts = StageDefinitions.SampleLocal(StageDefinitions.All[i]);
            Assert.Greater(WorldLayout.MinSelfDistance(pts, 500f), 150f);
        }

        [TestCaseSource(nameof(Stages))]
        public void FourRelicsOnCourse(int i)
        {
            var s = StageDefinitions.All[i];
            Assert.AreEqual(StageDefinitions.RELICS_PER_STAGE, s.relicFractions.Length);
            Assert.AreEqual(StageDefinitions.RELICS_PER_STAGE, s.relicSide.Length);
            foreach (float f in s.relicFractions) Assert.That(f, Is.InRange(0.05f, 0.95f));
        }

        [TestCaseSource(nameof(Stages))]
        public void CheckpointChordsStayWithin60m(int i)
        {
            var s = StageDefinitions.All[i];
            var pts = StageDefinitions.SampleLocal(s);
            var cps = WorldLayout.CheckpointArcs(StageDefinitions.GoalArc(s, pts), StageDefinitions.CP_SPACING_M);
            Assert.Less(WorldLayout.MaxDeviationFromChords(pts, StageDefinitions.START_ARC_M, cps), 30f);
            Assert.That(cps.Length, Is.InRange(25, 36));
        }

        [TestCaseSource(nameof(Stages))]
        public void GeneratedTerrainRideable(int i)
        {
            var s = StageDefinitions.All[i];
            var pts = StageDefinitions.SampleLocal(s);
            var n = StageDefinitions.Normalize(pts);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, n);
            float slope = HeightmapGen.MaxSlopeDegAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, n);
            TestContext.WriteLine($"stage {i}: max slope {slope:F1} deg");
            Assert.LessOrEqual(slope, StageDefinitions.MAX_SLOPE_DEG);
        }

        [Test] public void StageTerrainsDoNotOverlap()
        {
            var s = StageDefinitions.All;
            for (int i = 1; i < s.Length; i++)
                Assert.GreaterOrEqual(s[i].Origin.x - (s[i - 1].Origin.x + StageDefinitions.SIZE), 100f);
        }
    }
}
