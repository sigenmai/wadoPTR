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
                var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
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
            float D = CourseBarrier.OFFSET_M;
            foreach (var st in Stages())
                foreach (var s in st.segs)
                {
                    float da = DistToCorridors(s[0], st.plan.corridors);
                    Assert.AreEqual(D, da, 1.0f, st.def.label + " at " + s[0].x + "," + s[0].z);
                    // 区間の途中も通り道から D−1 より近づかない（曲がりの内側の角で入り込まない）
                    for (int k = 1; k < 5; k++)
                    {
                        Vector3 m = Vector3.Lerp(s[0], s[1], k / 5f);
                        Assert.GreaterOrEqual(DistToCorridors(m, st.plan.corridors), D - 1f, st.def.label);
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

        [Test] public void CheckpointsAreClearOfWalls()
        {
            foreach (var st in Stages())
            {
                var boxes = CourseBarrier.CheckpointBoxes(st.local, st.goal, st.shortcut);
                Assert.AreEqual(WorldLayout.CheckpointArcs(st.goal, StageDefinitions.CP_SPACING_M).Length, boxes.Count);
                float min = float.MaxValue;
                foreach (var b in boxes) min = Mathf.Min(min, CourseBarrier.DistToQuad(st.segs, b));
                TestContext.WriteLine(st.def.label + ": min wall–CP box " + min.ToString("F1") + " m");
                Assert.GreaterOrEqual(min, CourseBarrier.CP_CLEAR_M, st.def.label);
            }
        }

        [Test] public void RelicsStartAndFinishAreReachableAndClear()
        {
            float D = CourseBarrier.OFFSET_M;
            foreach (var st in Stages())
            {
                var pts = new List<Vector3>();
                for (int i = 0; i < st.def.relicFractions.Length; i++)
                {
                    float a = st.def.relicFractions[i] * st.goal;
                    Vector3 t = WorldLayout.TangentAtArc(st.local, a);
                    pts.Add(WorldLayout.PointAtArc(st.local, a) + WorldLayout.RightOf(t) * st.def.relicSide[i]);
                }
                Vector3 t0 = WorldLayout.TangentAtArc(st.local, StageDefinitions.START_ARC_M);
                Vector3 p0 = WorldLayout.PointAtArc(st.local, StageDefinitions.START_ARC_M);
                foreach (float o in WorldLayout.LaneOffsets(StageDefinitions.LANES, StageDefinitions.LANE_SPACING_M))
                    pts.Add(p0 + WorldLayout.RightOf(t0) * o);
                float total = CoursePath.Length(st.local);
                pts.Add(WorldLayout.PointAtArc(st.local, total - CourseGeometry.PEN_BACK));
                foreach (var p in pts)
                {
                    // 通ってよい側（通り道から D 未満）にあり、柵から 4m 以上離れている
                    Assert.Less(DistToCorridors(p, st.plan.corridors), D - CourseBarrier.CP_CLEAR_M, st.def.label);
                    Assert.GreaterOrEqual(CourseBarrier.DistToPoint(st.segs, p), CourseBarrier.CP_CLEAR_M, st.def.label);
                }
                // CP（ゴールを含む）は本コース上 = 通ってよい側
                foreach (float a in WorldLayout.CheckpointArcs(st.goal, StageDefinitions.CP_SPACING_M))
                    Assert.Greater(CourseBarrier.DistToPoint(st.segs, WorldLayout.PointAtArc(st.local, a)), D - 1f, st.def.label);
            }
        }

        [Test] public void GapOverTrenchAndAfterGoal()
        {
            foreach (var st in Stages())
            {
                float bridge = SetPieces.BridgeArc(st.def, st.goal);
                var trench = CourseBarrier.TrenchGap(st.local, bridge);
                Assert.AreEqual(bridge >= 0f, trench.HasValue);
                var tail = CourseBarrier.GoalTail(st.local, st.goal);
                foreach (var s in st.segs)
                    for (int k = 0; k <= 4; k++)
                    {
                        Vector3 m = Vector3.Lerp(s[0], s[1], k / 4f);
                        if (trench.HasValue)
                        {
                            // 溝の中（縁の岩壁の線より内側を 1m 余らせて）には柵がない
                            var inner = trench.Value;
                            inner.halfAlong -= 1f; inner.halfLat -= 1f;
                            Assert.IsFalse(inner.Contains(m), st.def.label + " wall over trench");
                        }
                        Assert.Greater(WorldLayout.DistToPolylineXZ(m, tail), CourseBarrier.OFFSET_M + 0.5f, st.def.label + " wall in goal area");
                    }
                if (trench.HasValue)
                {
                    // 切れ目の両側で柵は溝の縁の壁の線まで来ている（すき間から外へ出られない）
                    int near = 0;
                    var t = trench.Value;
                    foreach (var p in st.plan.runs)
                        foreach (var end in new[] { p[0], p[p.Length - 1] })
                        {
                            float dx = end.x - t.center.x, dz = end.z - t.center.z;
                            float along = Mathf.Abs(dx * t.tangent.x + dz * t.tangent.z);
                            float lat = Mathf.Abs(dx * t.tangent.z - dz * t.tangent.x);
                            if (lat < t.halfLat && Mathf.Abs(along - t.halfAlong) < 2f) near++;
                        }
                    Assert.AreEqual(4, near, st.def.label + ": both sides of both trench lips");
                }
            }
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
                    Assert.GreaterOrEqual(CourseBarrier.DistToPoint(st.segs, p), CourseBarrier.OFFSET_M - 1f, st.def.label);
                }
            }
            Assert.AreEqual(1, n, "the canyon has the cliff shortcut");
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
