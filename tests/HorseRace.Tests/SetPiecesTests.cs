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

        [Test] public void TrenchLeavesSpineAndDetours()
        {
            var h = Flat(257, 0.5f);
            var c = new Vector3(500f, 0f, 500f);
            var t = new Vector3(0f, 0f, 1f);   // コースは +z、横は x
            SetPieces.CarveTrench(h, 1000f, 200f, c, t);
            float H(float x, float z) => WorldLayout.SampleHeight(h, 1000f, 200f, x, z);
            // 背（中央 ±7m）は掘らない＝岩の橋、溝は 12m、両端（38m より外）は掘らない＝遠回り（course-r1-07）
            Assert.AreEqual(100f, H(500f, 500f), 0.01f);
            Assert.AreEqual(100f - SetPieces.TRENCH_DEPTH, H(500f + 20f, 500f), 0.3f);
            Assert.AreEqual(100f - SetPieces.TRENCH_DEPTH, H(500f - 20f, 500f), 0.3f);
            Assert.AreEqual(100f, H(500f + SetPieces.DETOUR_FROM + 4f, 500f), 0.01f);
            Assert.AreEqual(100f, H(500f + 20f, 500f + SetPieces.BRIDGE_HALF_SPAN + 2f), 0.01f);
            Assert.AreEqual(0f, SetPieces.TrenchDepthAt(0f, 3f), 1e-4);
            Assert.AreEqual(0f, SetPieces.TrenchDepthAt(0f, SetPieces.DETOUR_FROM + 0.1f), 1e-4);
        }

        [Test] public void SpineIsAChoke()
        {
            Assert.AreEqual(StageDefinitions.CHOKE_HALF_M, SetPieces.SPINE_HALF, 1e-4);
            Assert.Greater(SetPieces.DETOUR_BARRIER_M, SetPieces.DETOUR_TO);
            Assert.Greater(SetPieces.BRIDGE_HALF_SPAN, SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND);
        }

        [Test] public void JumpRampIs13Deg()
        {
            Assert.That(SetPieces.JumpAngleDeg, Is.InRange(12f, 14f));
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
            h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
        }

        /// <summary>近道（course-r1-06 → r2-02）：どのステージにも 1 つ、疾走で 1.5〜3.5 秒得、段 8m、双子の CP は順番どおり、ヘアピンは切らない。</summary>
        [Test] public void EveryStageShortcutSavesAtLeast30m([Values(0, 1, 2)] int i)
        {
            Stage(i, out var s, out var local, out var goal, out var h);
            SetPieces.RaiseMesa(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, s, local, goal);
            var p = SetPieces.PlanFor(s, local, h, goal);
            Assert.NotNull(p, "近道の区間が見つからない");
            TestContext.WriteLine($"stage {i}: shortcut {p.startArc:F0}-{p.endArc:F0} span {p.span} len {p.length:F0} ratio {p.ratio:F2} gain {p.gainSeconds:F2}s dev {p.maxDevFromCourse:F0} devCp {p.maxDevFromCpChord:F0}");
            Assert.That(p.gainSeconds, Is.InRange(SetPieces.SHORTCUT_MIN_GAIN_S, SetPieces.SHORTCUT_MAX_GAIN_S));
            Assert.AreEqual(p.gainSeconds * SetPieces.SHORTCUT_SPEED, p.saved, 1e-3);
            Assert.AreEqual(SetPieces.SHORTCUT_DROP, p.drop, 1e-4);
            Assert.That(p.drop, Is.InRange(3f, SetPieces.MAX_DROP));
            Assert.LessOrEqual(p.maxDevFromCpChord, SetPieces.SHORTCUT_MAX_CP_DEV);
            Assert.LessOrEqual(WorldLayout.DistToPolylineXZ(p.b, local), 1f);   // コースに戻る
            Assert.AreEqual(p.span - 1, p.twinFractions.Length);
            for (int k = 1; k < p.twinFractions.Length; k++) Assert.Greater(p.twinFractions[k], p.twinFractions[k - 1]);
            Assert.That(p.startArc, Is.GreaterThanOrEqualTo(s.shortcutFrom * goal - 1f));
            Assert.That(p.endArc, Is.LessThanOrEqualTo(s.shortcutTo * goal + 1f));
            float bridge = SetPieces.BridgeArc(s, goal);
            if (bridge >= 0f) Assert.That(p.endArc < bridge - 80f || p.startArc > bridge + 80f);
            foreach (var hp in StageDefinitions.Hairpins(s, local))
                Assert.That(p.endArc < hp.fromArc - SetPieces.HAIRPIN_AVOID_M || p.startArc > hp.toArc + SetPieces.HAIRPIN_AVOID_M, "ヘアピンを切らない");
        }

        /// <summary>ShortcutPaysOnlyAtSprint（course-r2-02）：3 ステージの近道は疾走なら 1.5 秒以上速く、駈歩なら本コースの疾走より遅い。着地は本当の危険。</summary>
        [Test] public void ShortcutPaysOnlyAtSprint([Values(0, 1, 2)] int i)
        {
            Stage(i, out var s, out var local, out var goal, out var h);
            SetPieces.RaiseMesa(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, s, local, goal);
            var p = SetPieces.PlanFor(s, local, h, goal);
            float course = p.endArc - p.startArc, ramp = SetPieces.ShortcutRampLen(p.drop);
            float main = course / MountMath.TOP_SPEED;
            float sprint = SetPieces.ShortcutSeconds(p.length, ramp, p.drop, SetPieces.LANDING_RUN, MountMath.TOP_SPEED);
            float canter = SetPieces.ShortcutSeconds(p.length, ramp, p.drop, SetPieces.LANDING_RUN, MountMath.GaitSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_CANTER));
            TestContext.WriteLine($"stage {i}: main {main:F2}s, shortcut sprint {sprint:F2}s, canter {canter:F2}s");
            Assert.GreaterOrEqual(main - sprint, SetPieces.SHORTCUT_MIN_GAIN_S);
            Assert.Greater(canter, main);
            Assert.Greater(MountMath.LandingStun(SetPieces.SHORTCUT_DROP), 0.1f);
            Assert.Less(MountMath.LandingSpeedKeep(SetPieces.SHORTCUT_DROP), 0.95f);
        }

        [Test] public void ShortcutGainCountsSandRampAndLanding()
        {
            float ramp = SetPieces.ShortcutRampLen(8f);
            Assert.AreEqual(8f / Mathf.Tan(12f * Mathf.Deg2Rad), ramp, 1e-3);
            float t = SetPieces.ShortcutSeconds(200f, ramp, 8f, 12f, 18f);
            float expect = (200f - ramp - 12f) / 18f + ramp / (18f * (1f - 12f * 0.015f)) + 12f / (18f * MountMath.HORSE_SAND_FACTOR) + MountMath.LandingStun(8f);
            Assert.AreEqual(expect, t, 1e-4);
            // 長さの比 1.1 の弦は損
            Assert.Less(SetPieces.ShortcutGainSeconds(220f, 200f, ramp, 8f, 12f), 0.5f);
            // 全面が砂だと比 1.25 でも損（以前の近道の罠）
            Assert.Less(SetPieces.ShortcutGainSeconds(250f, 200f, ramp, 8f, 200f), 0f);
        }

        [Test] public void MesaClimbsOntoAPlateau()
        {
            Stage(2, out var s, out var local, out var goal, out var h);
            var h0 = (float[,])h.Clone();
            SetPieces.RaiseMesa(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, s, local, goal);
            float top = 0.5f * (s.mesaClimbFraction + s.mesaTopEndFraction) * goal;
            Vector3 c = WorldLayout.PointAtArc(local, top), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(local, top));
            float H(float[,] a, Vector3 p) => WorldLayout.SampleHeight(a, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
            Assert.AreEqual(32f, StageDefinitions.ElevationAt(s, top), 0.01f);
            // 帯（±35m）は道と同じ高さ、ステージの基準より 25m 以上高い
            Assert.AreEqual(H(h, c), H(h, c + r * 30f), 4f);
            Assert.AreEqual(H(h, c), H(h, c - r * 30f), 4f);
            Assert.Greater(H(h, c) - HeightmapGen.BaseHeight(s.kind) * HeightmapGen.HEIGHT_M, 25f);
            // 坂（260m）は 25° 以下
            float slope = HeightmapGen.MaxSlopeDegAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, StageDefinitions.Normalize(local));
            Assert.LessOrEqual(slope, StageDefinitions.MAX_SLOPE_DEG);
        }

        [Test] public void CanyonAfterSetPiecesMainCourseStillRideable()
        {
            Stage(0, out var s, out var local, out var goal, out var h);
            var p = SetPieces.ApplyTerrain(s, local, h, goal);
            float bridge = SetPieces.BridgeArc(s, goal);
            var pts = local;
            float slope = HeightmapGen.MaxSlopeDegAlong(h, 1000f, 200f, StageDefinitions.Normalize(pts));
            TestContext.WriteLine($"slope {slope:F1}");
            Assert.LessOrEqual(slope, 25f);
            // 溝の底は岩の背より十分低い
            Vector3 c = WorldLayout.PointAtArc(local, bridge);
            Vector3 side = c + WorldLayout.RightOf(WorldLayout.TangentAtArc(local, bridge)) * 20f;
            Assert.Greater(WorldLayout.SampleHeight(h, 1000f, 200f, c.x, c.z) - WorldLayout.SampleHeight(h, 1000f, 200f, side.x, side.z), 8f);
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
            Assert.AreEqual(j + SetPieces.JUMP_RELIC_AHEAD, SetPieces.RelicSpots(s, local, goal, null)[1].arc, 0.01f);
        }
    }
}
