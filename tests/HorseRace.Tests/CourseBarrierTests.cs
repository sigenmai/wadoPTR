using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class CourseBarrierTests
    {
        sealed class Stage
        {
            public StageDef def;
            public Vector3[] local;
            public float goal;
            public SetPieces.ShortcutPlan shortcut;
            public CourseBarrier.Plan plan;
            public List<Vector3[]> segs;
        }

        static Stage[] cache;

        static Stage[] Stages()
        {
            if (cache != null) return cache;
            var r = new List<Stage>();
            foreach (var s in StageDefinitions.All)
            {
                var local = StageDefinitions.SampleLocal(s);
                float goal = StageDefinitions.GoalArc(s, local);
                var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
                var sc = SetPieces.PlanFor(s, local, h, goal);
                var plan = CourseBarrier.For(s, local, goal, sc);
                r.Add(new Stage { def = s, local = local, goal = goal, shortcut = sc, plan = plan, segs = plan.Segments() });
            }
            cache = r.ToArray();
            return cache;
        }

        static float DistToCorridors(Vector3 p, List<Vector3[]> corridors)
        {
            float best = float.MaxValue;
            foreach (var c in corridors) best = Mathf.Min(best, WorldLayout.DistToPolylineXZ(p, c));
            return best;
        }

        [Test] public void EveryStageHasBarriersOnBothSides()
        {
            foreach (var st in Stages())
            {
                int n = st.plan.SegmentCount;
                TestContext.WriteLine(st.def.label + ": " + n + " segments, " + st.plan.runs.Count + " runs, " + st.plan.TotalLength.ToString("F0") + " m");
                // 両側でおおむね 2 × コース長（ゴールの手前から先は切れ目）
                Assert.Greater(st.plan.TotalLength, 1.7f * st.goal, st.def.label);
                Assert.Less(st.plan.TotalLength, 3.0f * st.goal, st.def.label);
            }
        }

        [Test] public void SegmentLengthsAreFourToSixMetres()
        {
            foreach (var st in Stages())
                foreach (var s in st.segs)
                {
                    float l = WorldLayout.DistXZ(s[0], s[1]);
                    Assert.That(l, Is.InRange(1f, 6.5f), st.def.label);
                }
            // 切れ端以外はほぼ SEGMENT_M
            foreach (var st in Stages())
                for (int i = 0; i < st.plan.runs.Count; i++)
                {
                    var p = st.plan.runs[i];
                    if (p.Length < 4) continue;
                    float l = WorldLayout.DistXZ(p[1], p[2]);
                    Assert.That(l, Is.InRange(4f, 6f), st.def.label);
                }
        }

        [Test] public void WallPointsSitAtOffsetFromCorridor()
        {
            // 柵は点ごとの柵の距離（道幅 + 16m、急カーブ + 10m、橋のまわり 58m）の等距離線の上
            foreach (var st in Stages())
                foreach (var s in st.segs)
                {
                    Assert.AreEqual(0f, st.plan.Field(s[0]), 1.0f, st.def.label + " at " + s[0].x + "," + s[0].z);
                    for (int k = 1; k < 5; k++)
                    {
                        Vector3 m = Vector3.Lerp(s[0], s[1], k / 5f);
                        Assert.GreaterOrEqual(st.plan.Field(m), -1f, st.def.label);
                    }
                }
        }

        [Test] public void WallsNeverCrossCourseOrShortcut()
        {
            foreach (var st in Stages())
                foreach (var c in st.plan.corridors)
                    for (int i = 1; i < c.Length; i++)
                        foreach (var s in st.segs)
                            Assert.IsFalse(CourseBarrier.SegmentsIntersect(s[0], s[1], c[i - 1], c[i]), st.def.label);
        }

        [Test] public void NoSelfIntersections()
        {
            foreach (var st in Stages())
            {
                var segs = st.segs;
                for (int i = 0; i < segs.Count; i++)
                    for (int j = i + 2; j < segs.Count; j++)
                    {
                        // 隣り合う区間（輪の最初と最後を含む）は端を共有するので除く
                        if (WorldLayout.DistXZ(segs[i][0], segs[j][1]) < 1e-3f || WorldLayout.DistXZ(segs[i][1], segs[j][0]) < 1e-3f) continue;
                        Assert.IsFalse(CourseBarrier.SegmentsIntersect(segs[i][0], segs[i][1], segs[j][0], segs[j][1]), st.def.label + " " + i + "/" + j);
                    }
            }
        }

        [Test] public void StaysInsideTerrainRim()
        {
            float m = CourseBarrier.EDGE_MARGIN_M, size = StageDefinitions.SIZE;
            foreach (var st in Stages())
                foreach (var p in st.plan.runs)
                    foreach (var q in p)
                    {
                        Assert.That(q.x, Is.InRange(m - 0.5f, size - m + 0.5f), st.def.label);
                        Assert.That(q.z, Is.InRange(m - 0.5f, size - m + 0.5f), st.def.label);
                    }
        }

        /// <summary>system-r2-02：CP の箱は柵から柵まで（柵から 2m 内側）。柵沿いの外の線でも CP を飛ばさない。</summary>
        [Test] public void CheckpointBoxesReachTheFence()
        {
            foreach (var st in Stages())
            {
                var boxes = CourseBarrier.CheckpointBoxes(st.def, st.local, st.goal, st.shortcut);
                int twins = st.shortcut != null ? st.shortcut.twinFractions.Length : 0;
                float[] arcs = WorldLayout.CheckpointArcs(st.goal, StageDefinitions.CP_SPACING_M);
                Assert.AreEqual(arcs.Length + twins, boxes.Count);
                foreach (float a in arcs)
                {
                    float off = CourseBarrier.OffsetAtArc(st.def, st.local, st.goal, a);
                    float half = CourseBarrier.CpHalfWidth(st.def, st.local, st.goal, a);
                    Assert.GreaterOrEqual(half, off - 2f - 1e-3f, st.def.label + " arc " + a);
                    Assert.Less(half, off, st.def.label + " arc " + a);
                }
            }
        }

        [Test] public void RelicsStartAndFinishAreReachableAndClear()
        {
            float D = CourseBarrier.OFFSET_M;
            foreach (var st in Stages())
            {
                var pts = new List<Vector3>();
                foreach (var r in SetPieces.RelicSpots(st.def, st.local, st.goal, st.shortcut)) pts.Add(r.local);
                Vector3 t0 = WorldLayout.TangentAtArc(st.local, StageDefinitions.START_ARC_M);
                Vector3 p0 = WorldLayout.PointAtArc(st.local, StageDefinitions.START_ARC_M);
                foreach (float o in WorldLayout.LaneOffsets(StageDefinitions.LANES, StageDefinitions.LANE_SPACING_M))
                    pts.Add(p0 + WorldLayout.RightOf(t0) * o);
                float total = CoursePath.Length(st.local);
                pts.Add(WorldLayout.PointAtArc(st.local, total - CourseGeometry.PEN_BACK));
                foreach (var p in pts)
                {
                    // 通ってよい側にあり、柵から 4m 以上離れている
                    Assert.Less(st.plan.Field(p), -CourseBarrier.CP_CLEAR_M, st.def.label);
                    Assert.GreaterOrEqual(CourseBarrier.DistToPoint(st.segs, p), CourseBarrier.CP_CLEAR_M, st.def.label);
                }
                // CP（ゴールを含む）は本コース上 = 通ってよい側
                foreach (float a in WorldLayout.CheckpointArcs(st.goal, StageDefinitions.CP_SPACING_M))
                    Assert.Greater(CourseBarrier.DistToPoint(st.segs, WorldLayout.PointAtArc(st.local, a)), StageDefinitions.CHOKE_HALF_M + CourseBarrier.CORNER_SIDE_M - 1f, st.def.label);
            }
        }

        [Test] public void DetoursAroundGorgeAreInsideAndGoalAreaOpen()
        {
            foreach (var st in Stages())
            {
                var tail = CourseBarrier.GoalTail(st.local, st.goal);
                foreach (var s in st.segs)
                    for (int k = 0; k <= 4; k++)
                        Assert.Greater(WorldLayout.DistToPolylineXZ(Vector3.Lerp(s[0], s[1], k / 4f), tail), CourseBarrier.OFFSET_M + 0.5f, st.def.label + " wall in goal area");
                float bridge = SetPieces.BridgeArc(st.def, st.goal);
                if (bridge < 0f) continue;
                // 溝の両端を回る遠回り（道から 38〜54m）は柵の内側（course-r1-07）
                Vector3 c = WorldLayout.PointAtArc(st.local, bridge), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(st.local, bridge));
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 detour = c + r * (sd * (SetPieces.DETOUR_FROM + SetPieces.DETOUR_TO) * 0.5f);
                    Assert.Less(st.plan.Field(detour), -CourseBarrier.CP_CLEAR_M, st.def.label + " detour " + sd);
                }
            }
        }

        [Test] public void ChokesAndCornersNarrowTheBarrier()
        {
            var st = Stages()[0];
            float bridge = SetPieces.BridgeArc(st.def, st.goal);
            var off = CourseBarrier.CourseOffsets(st.def, st.local, st.goal);
            float min = float.MaxValue, max = 0f;
            foreach (float o in off) { min = Mathf.Min(min, o); max = Mathf.Max(max, o); }
            Assert.AreEqual(StageDefinitions.CHOKE_HALF_M + CourseBarrier.SIDE_M, min, 2.5f);
            Assert.AreEqual(SetPieces.DETOUR_BARRIER_M, max, 0.01f);
            Assert.Greater(bridge, 0f);
        }

        [Test] public void ShortcutStaysInsideAllowedArea()
        {
            int n = 0;
            foreach (var st in Stages())
            {
                if (st.shortcut == null) continue;
                n++;
                Assert.AreEqual(2, st.plan.corridors.Count);
                for (int k = 0; k <= 20; k++)
                {
                    Vector3 p = Vector3.Lerp(st.shortcut.a, st.shortcut.b, k / 20f);
                    p.y = 0f;
                    Assert.Less(st.plan.Field(p), -CourseBarrier.OFFSET_M + 1f, st.def.label);
                }
            }
            Assert.AreEqual(3, n, "every stage has a shortcut (course-r1-06)");
        }

        [Test] public void TightHairpinHasCleanInnerCorner()
        {
            // 半径 10m の U ターン（D = 28m より小さい）：内側の等距離線がループせず、交差もしない
            var ctrl = new[] { new Vector3(400, 0, 300), new Vector3(400, 0, 500), new Vector3(410, 0, 515), new Vector3(420, 0, 500), new Vector3(420, 0, 300) };
            var course = CoursePath.Sample(ctrl, 2f);
            var plan = CourseBarrier.Make(new List<Vector3[]> { course }, CourseBarrier.OFFSET_M, new List<CourseBarrier.GapRect>(), null, 1000f);
            Assert.AreEqual(1, plan.runs.Count, "one closed loop around the hairpin");
            Assert.IsTrue(plan.closed[0]);
            var segs = plan.Segments();
            for (int i = 0; i < segs.Count; i++)
            {
                Assert.AreEqual(CourseBarrier.OFFSET_M, WorldLayout.DistToPolylineXZ(segs[i][0], course), 1f);
                for (int j = i + 2; j < segs.Count; j++)
                {
                    if (i == 0 && j == segs.Count - 1) continue;
                    Assert.IsFalse(CourseBarrier.SegmentsIntersect(segs[i][0], segs[i][1], segs[j][0], segs[j][1]));
                }
            }
        }

        [Test] public void GoalTailStartsBeforeGoal()
        {
            var st = Stages()[0];
            var tail = CourseBarrier.GoalTail(st.local, st.goal);
            Assert.AreEqual(0f, WorldLayout.DistXZ(tail[0], WorldLayout.PointAtArc(st.local, st.goal - CourseBarrier.GOAL_GAP_BEFORE_M)), 1e-3f);
            Assert.AreEqual(0f, WorldLayout.DistXZ(tail[tail.Length - 1], st.local[st.local.Length - 1]), 1e-3f);
        }

        [Test] public void Deterministic()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var a = CourseBarrier.For(s, local, goal, null);
            var b = CourseBarrier.For(s, local, goal, null);
            Assert.AreEqual(a.SegmentCount, b.SegmentCount);
            for (int i = 0; i < a.runs.Count; i++) CollectionAssert.AreEqual(a.runs[i], b.runs[i]);
        }
    }
}
