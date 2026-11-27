using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class SetPiecesTests
    {
        static float[,] Flat(int n, float v)
        {
            var h = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) h[z, x] = v;
            return h;
        }

        [Test] public void MidCpArcIsBetweenCheckpoints()
        {
            Assert.AreEqual(760f, SetPieces.MidCpArc(750f, 80f, 2600f));
            Assert.AreEqual(120f, SetPieces.MidCpArc(-50f, 80f, 2600f));
            float last = SetPieces.MidCpArc(9999f, 80f, 2600f);
            Assert.LessOrEqual(last, 2600f - 80f);
            Assert.AreEqual(40f, last % 80f, 1e-3);
        }

        [Test] public void ExcludeArcRangeDropsOnlyThatSpan()
        {
            var pts = CoursePath.Sample(new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 100) }, 2f);
            var r = SetPieces.ExcludeArcRange(pts, 40f, 60f);
            Assert.AreEqual(pts.Length - 11, r.Length);
        }

        [Test] public void TrenchIsDeepUnderBridgeAndUntouchedAtDeckEnds()
        {
            var h = Flat(257, 0.5f);
            var c = new Vector3(500f, 0f, 500f);
            var t = new Vector3(0f, 0f, 1f);
            SetPieces.CarveTrench(h, 1000f, 200f, c, t);
            Assert.AreEqual(100f - SetPieces.TRENCH_DEPTH, WorldLayout.SampleHeight(h, 1000f, 200f, 500f, 500f), 0.01f);
            // 橋の板の端（コース方向に BRIDGE_HALF_SPAN）は元の高さ
            Assert.AreEqual(100f, WorldLayout.SampleHeight(h, 1000f, 200f, 500f, 500f + SetPieces.BRIDGE_HALF_SPAN + 2f), 0.01f);
            Assert.AreEqual(100f, WorldLayout.SampleHeight(h, 1000f, 200f, 700f, 500f), 0.01f);
        }

        [Test] public void JumpRampWithin25Deg()
        {
            Assert.LessOrEqual(SetPieces.JumpAngleDeg, 25f);
            Assert.Greater(SetPieces.JumpAngleDeg, 5f);
        }

        [Test] public void ShortcutHeightProfile()
        {
            var p = new SetPieces.ShortcutPlan { a = new Vector3(0, 10, 0), b = new Vector3(100, 10, 0), f0 = 0.2f, rampEnd = 0.5f, fDrop = 0.8f, drop = 6f };
            Assert.AreEqual(10f, SetPieces.ShortcutHeight(p, 0.1f), 1e-4);
            Assert.AreEqual(13f, SetPieces.ShortcutHeight(p, 0.35f), 1e-4);
            Assert.AreEqual(16f, SetPieces.ShortcutHeight(p, 0.7f), 1e-4);
            Assert.AreEqual(10f, SetPieces.ShortcutHeight(p, 0.9f), 1e-4);
        }

        static void Stage(int i, out StageDef s, out Vector3[] local, out float goal, out float[,] h)
        {
            s = StageDefinitions.All[i];
            local = StageDefinitions.SampleLocal(s);
            goal = StageDefinitions.GoalArc(s, local);
            h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
        }

        [Test] public void CanyonShortcutPlanIsValid()
        {
            Stage(0, out var s, out var local, out var goal, out var h);
            var p = SetPieces.PlanFor(s, local, h, goal);
            Assert.NotNull(p, "近道の区間が見つからない");
            TestContext.WriteLine($"shortcut CP{p.cpInterval + 1}->CP{p.cpInterval + 1 + SetPieces.SHORTCUT_SPAN} offsets {string.Join(",", p.crossOffsets)} len {p.length:F0} m dev {p.maxDevFromCourse:F0} m devCp {p.maxDevFromCpChord:F0} m");
            Assert.LessOrEqual(p.drop, SetPieces.MAX_DROP);
            Assert.GreaterOrEqual(p.drop, 3f);   // 着地の硬直が起きる高さ
            Assert.LessOrEqual(p.maxDevFromCpChord, 60f);
            Assert.LessOrEqual(WorldLayout.DistToPolylineXZ(p.b, local), 1f);   // コースに戻る
            Assert.AreEqual(SetPieces.SHORTCUT_SPAN - 1, p.crossCps.Length);
            foreach (float o in p.crossOffsets) Assert.LessOrEqual(Mathf.Abs(o), 55f);   // 広げた CP の箱が届く
            float bridge = SetPieces.BridgeArc(s, goal);
            Assert.That(p.endArc < bridge - 80f || p.startArc > bridge + 80f);
        }

        [Test] public void CanyonAfterSetPiecesMainCourseStillRideable()
        {
            Stage(0, out var s, out var local, out var goal, out var h);
            var p = SetPieces.ApplyTerrain(s, local, h, goal);
            float bridge = SetPieces.BridgeArc(s, goal);
            var pts = SetPieces.ExcludeArcRange(local, bridge - SetPieces.BRIDGE_HALF_SPAN, bridge + SetPieces.BRIDGE_HALF_SPAN);
            float slope = HeightmapGen.MaxSlopeDegAlong(h, 1000f, 200f, StageDefinitions.Normalize(pts));
            TestContext.WriteLine($"slope {slope:F1}");
            Assert.LessOrEqual(slope, 25f);
            // 溝の底は橋の板より十分低い
            Vector3 c = WorldLayout.PointAtArc(local, bridge);
            float course = WorldLayout.SampleHeight(h, 1000f, 200f, WorldLayout.PointAtArc(local, bridge + 20f).x, WorldLayout.PointAtArc(local, bridge + 20f).z);
            Assert.Greater(course - WorldLayout.SampleHeight(h, 1000f, 200f, c.x, c.z), 8f);
            // 近道の段：3m 以上（硬直）8m 以下
            float drop = SetPieces.MaxDropAlong(h, 1000f, 200f, p.a, p.b, 4f);
            TestContext.WriteLine($"drop {drop:F1}");
            Assert.That(drop, Is.InRange(3f, SetPieces.MAX_DROP + 0.5f));
        }

        [Test] public void DunesJumpBetweenCheckpointsAndAwayFromRelics()
        {
            Stage(1, out var s, out var local, out var goal, out var h);
            float j = SetPieces.JumpArc(s, goal);
            Assert.Greater(j, 0f);
            Assert.AreEqual(40f, j % 80f, 1e-3);
            foreach (float f in s.relicFractions) Assert.Greater(Mathf.Abs(f * goal - j), 30f);
        }
    }
}
