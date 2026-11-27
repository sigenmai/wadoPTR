using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目の決まり（光・色・見どころの地形）の単体テスト（review-log の Round 1 — art）。</summary>
    public class ArtPassTests
    {
        // ---- 光（art-r1-01）----

        [Test] public void CanyonFloorShadowIsNotCrushed()
        {
            float lum = LightingPlan.MinCanyonFloorShadowLuminance(ArtPalette.DirtCanyon, ArtPalette.SandLayer);
            TestContext.WriteLine($"canyon floor shadow luminance {lum:F3}");
            Assert.GreaterOrEqual(lum, LightingPlan.MIN_SHADOW_LUMINANCE);
            // 前の値（影色 0.55,0.42,0.48・影の段の太陽 0.35・地面側の環境光 0.45,0.33,0.25）より明るい
            var old = new Color(0.62f * 0.55f * (0.45f + 0.35f * 1.15f), 0.36f * 0.42f * (0.33f + 0.35f * 1.07f), 0.22f * 0.48f * (0.25f + 0.35f * 0.92f));
            Assert.Greater(lum, LightingPlan.Luminance(old) * 1.4f);
        }

        [Test] public void SunIsHighAndAmbientWarm()
        {
            Assert.AreEqual(62f, LightingPlan.SUN_EULER.x, 1e-4);
            Assert.GreaterOrEqual(LightingPlan.SUN_INTENSITY, 1.25f);
            Assert.Greater(LightingPlan.AmbientGround.r, 0.55f);
            Assert.AreEqual(0.5f, LightingPlan.SHADOW_LIGHT_FLOOR, 1e-4);
        }

        [Test] public void FogHiddenDistanceBetweenStartAndEnd()
        {
            Assert.That(LightingPlan.FogHiddenDistance, Is.InRange(LightingPlan.FOG_START_M, LightingPlan.FOG_END_M));
            Assert.AreEqual(0f, LightingPlan.FogFraction(100f), 1e-5);
            Assert.AreEqual(1f, LightingPlan.FogFraction(2000f), 1e-5);
        }

        // ---- 色（art-r1-03 / art-r1-13）----

        [Test] public void BigRocksKeepDetailAndCliffsAreRust()
        {
            var c = ArtPalette.StyleFor("Packs/Cliffs/Cliffs.fbx", "Cliffs_03");
            Assert.AreEqual(ArtPalette.ROCK_FLATTEN, c.flatten, 1e-5);
            Assert.AreEqual(ArtPalette.ROCK_FLATTEN_MIP, c.flattenMip, 1e-5);
            Assert.AreEqual(0.82f, c.tint.g, 1e-5);
            Assert.AreEqual(ArtPalette.ROCK_FLATTEN, ArtPalette.StyleFor("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_02").flatten, 1e-5);
            Assert.AreEqual(ArtPalette.DEFAULT_FLATTEN, ArtPalette.StyleFor("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_09").flatten, 1e-5);
            Assert.AreEqual(ArtPalette.ROCK_FLATTEN, ArtPalette.StyleFor("ThirdParty/MesaRockB/MesaRockB.fbx", null).flatten, 1e-5);
            Assert.AreEqual(ArtPalette.DEFAULT_FLATTEN, ArtPalette.StyleFor("Packs/Cacti/Cacti.fbx", "Cacti_01").flatten, 1e-5);
            Assert.AreNotEqual(c.key, ArtPalette.StyleFor("Packs/Cacti/Cacti.fbx", "Cacti_01").key);
        }

        [Test] public void BlueGrayRocksBecomeWarm()
        {
            var blueGray = new Color(0.55f, 0.58f, 0.63f);
            Assert.IsFalse(ArtPalette.IsWarmRock(blueGray));
            var t = ArtPalette.StyleFor("ThirdParty/RockGray/RockGrayC.fbx", null).tint;
            Assert.IsTrue(ArtPalette.IsWarmRock(ArtPalette.Multiply(blueGray, t)));
            Assert.IsTrue(ArtPalette.IsRockAsset("ThirdParty/RockGray/RockGrayC.fbx"));
            Assert.IsFalse(ArtPalette.IsRockAsset("Packs/Cacti/Cacti.fbx"));
        }

        [Test] public void TownGreyBlueGreenPulledToPalette()
        {
            const string town = "Packs/WesternTown/WesternTown.fbx";
            float h, s, v;
            ArtPalette.ToHSV(ArtPalette.RemapBaseColor(town, new Color(0.35f, 0.45f, 0.70f)), out h, out s, out v);   // 青
            Assert.That(h, Is.InRange(0f, 60f));
            ArtPalette.ToHSV(ArtPalette.RemapBaseColor(town, new Color(0.25f, 0.55f, 0.25f)), out h, out s, out v);   // 緑
            Assert.That(h, Is.InRange(60f, 110f)); Assert.Less(s, 0.4f);
            ArtPalette.ToHSV(ArtPalette.RemapBaseColor(town, new Color(0.6f, 0.6f, 0.6f)), out h, out s, out v);      // 灰色
            Assert.That(h, Is.InRange(0f, 60f)); Assert.Greater(s, 0.1f);
            var red = new Color(0.7f, 0.2f, 0.15f);
            Assert.AreEqual(red, ArtPalette.RemapBaseColor(town, red));
            var blue = new Color(0.2f, 0.3f, 0.8f);
            Assert.AreEqual(blue, ArtPalette.RemapBaseColor("Packs/FarmProps/FarmProps.fbx", blue));   // 町以外は変えない
        }

        [Test] public void StageDirtColorsAreDistinct()
        {
            float h0, h2, s, v;
            ArtPalette.ToHSV(ArtPalette.Dirt(0), out h0, out s, out v);
            ArtPalette.ToHSV(ArtPalette.Dirt(2), out h2, out s, out v);
            Assert.That(h0, Is.InRange(15f, 35f));            // 赤錆
            Assert.That(h2, Is.InRange(330f, 360f).Or.InRange(0f, 8f));   // 紫寄り
            Assert.Greater(LightingPlan.ShadowColorDusk.b, LightingPlan.ShadowColorDusk.r);
        }

        // ---- 見どころの地形（art-r1-08 / art-r1-09）----

        [Test] public void KickerProfile()
        {
            float half = SetPieces.JUMP_LENGTH * 0.5f;
            Assert.AreEqual(0f, SetPieces.KickerRise(-half, 0f), 1e-4);
            Assert.AreEqual(SetPieces.JUMP_HEIGHT - SetPieces.KICKER_UNDER_M, SetPieces.KickerRise(half, 0f), 1e-4);
            Assert.AreEqual(0f, SetPieces.KickerRise(half + SetPieces.KICKER_LIP_M + 0.1f, 0f), 1e-4);
            Assert.AreEqual(0f, SetPieces.KickerRise(0f, SetPieces.JUMP_WIDTH * 0.5f + SetPieces.KICKER_SIDE_M + 0.1f), 1e-4);
            // 坂も縁の後ろも 25° 以下
            for (float a = -half; a < half + SetPieces.KICKER_LIP_M; a += 0.25f)
            {
                float g = Mathf.Abs(SetPieces.KickerRise(a + 0.25f, 0f) - SetPieces.KickerRise(a, 0f)) / 0.25f;
                Assert.LessOrEqual(Mathf.Atan(g) * Mathf.Rad2Deg, StageDefinitions.MAX_SLOPE_DEG);
            }
        }

        [Test] public void DunesKickerRaisesTerrainUnderRamp()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h0 = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
            var h = (float[,])h0.Clone();
            SetPieces.ApplyTerrain(s, local, h, goal);
            float jump = SetPieces.JumpArc(s, goal);
            Vector3 lip = WorldLayout.PointAtArc(local, jump + SetPieces.JUMP_LENGTH * 0.5f - 0.5f);
            float before = WorldLayout.SampleHeight(h0, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, lip.x, lip.z);
            float after = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, lip.x, lip.z);
            Assert.That(after - before, Is.InRange(0.8f, SetPieces.JUMP_HEIGHT));
            float slope = HeightmapGen.MaxSlopeDegAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, StageDefinitions.Normalize(local));
            Assert.LessOrEqual(slope, StageDefinitions.MAX_SLOPE_DEG);
        }

        [Test] public void BorderPassIsCarvedBetweenCheckpoints()
        {
            var s = StageDefinitions.All[2];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            float pass = SetPieces.PassArc(s, goal);
            float[] cps = WorldLayout.CheckpointArcs(goal, StageDefinitions.CP_SPACING_M);
            foreach (float c in cps) Assert.Greater(Mathf.Abs(c - pass), SetPieces.PASS_HALF_CUT + SetPieces.PASS_RAMP - 2f, "CP " + c);
            foreach (float f in s.relicFractions) Assert.Greater(Mathf.Abs(f * goal - pass), 60f);
            Assert.Less(pass, goal - 300f);   // 切り通しを抜けてから最後の直線
            var h0 = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
            var h = (float[,])h0.Clone();
            SetPieces.ApplyTerrain(s, local, h, goal);
            Vector3 c0 = WorldLayout.PointAtArc(local, pass);
            Vector3 r = WorldLayout.RightOf(WorldLayout.TangentAtArc(local, pass));
            float H(float[,] a, Vector3 p) => WorldLayout.SampleHeight(a, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
            Assert.AreEqual(-SetPieces.PASS_DEPTH, H(h, c0) - H(h0, c0), 0.6f);
            Assert.Greater(H(h, c0 + r * 30f) - H(h0, c0 + r * 30f), 15f);
            Assert.Greater(H(h, c0 - r * 30f) - H(h0, c0 - r * 30f), 15f);
            float slope = HeightmapGen.MaxSlopeDegAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, StageDefinitions.Normalize(local));
            TestContext.WriteLine($"border max slope after pass {slope:F1}");
            Assert.LessOrEqual(slope, StageDefinitions.MAX_SLOPE_DEG);
        }

        [Test] public void BorderEndsWithStraightDownhill()
        {
            var s = StageDefinitions.All[2];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
            Vector3 a = WorldLayout.PointAtArc(local, goal - 300f), b = WorldLayout.PointAtArc(local, goal + 100f);
            // 一直線：途中の点が弦から 2m 以内
            for (float k = goal - 300f; k <= goal + 100f; k += 10f)
                Assert.Less(WorldLayout.DistToSegmentXZ(WorldLayout.PointAtArc(local, k), a, b), 2f);
            float ya = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, a.x, a.z);
            float yg = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, goal).x, WorldLayout.PointAtArc(local, goal).z);
            TestContext.WriteLine($"descent {ya - yg:F1} m");
            Assert.Greater(ya - yg, HeightmapGen.FINAL_DROP_M * 0.5f);
        }

        [Test] public void RailRunsAreStraightAndClear()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            var runs = SetPieces.RailRuns(s, local, StageDefinitions.GoalArc(s, local));
            Assert.AreEqual(SetPieces.RAIL_RUNS, runs.Count);
            foreach (var r in runs)
            {
                Assert.AreEqual(SetPieces.RAIL_LENGTH, WorldLayout.DistXZ(r[0], r[1]), 0.5f);
                for (int k = 0; k <= 10; k++) Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(Vector3.Lerp(r[0], r[1], k / 10f), local), 30f);
            }
            Assert.AreEqual(0, SetPieces.RailRuns(StageDefinitions.All[0], StageDefinitions.SampleLocal(StageDefinitions.All[0]), 2000f).Count);
        }
    }
}
