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
            // 中央：22m で +5m（13°）、縁で 4m に 6m 落ちて −1m の窪み、窪みは縁の 20m 先まで、6m で道へ戻る（course-r1-08）
            Assert.AreEqual(0f, SetPieces.KickerDelta(-SetPieces.JUMP_LENGTH, 0f), 1e-4);
            Assert.AreEqual(SetPieces.JUMP_HEIGHT, SetPieces.KickerDelta(0f, 0f), 1e-4);
            Assert.AreEqual(SetPieces.JUMP_HEIGHT - SetPieces.KICKER_DROP_M, SetPieces.KickerDelta(SetPieces.KICKER_LIP_M, 0f), 1e-4);
            Assert.AreEqual(-1f, SetPieces.KickerDelta(12f, 0f), 1e-4);
            Assert.AreEqual(0f, SetPieces.KickerDelta(SetPieces.KICKER_TROUGH_END + SetPieces.KICKER_TROUGH_EXIT + 0.1f, 0f), 1e-4);
            Assert.That(SetPieces.JumpAngleDeg, Is.InRange(12f, 14f));
            // 横（±7m の外）は低いゆるい坂：縁の後ろも 25° 以下で窪みへ転がれる
            Assert.LessOrEqual(SetPieces.KickerDelta(0f, 14f), SetPieces.KICKER_SIDE_HEIGHT + 0.01f);
            for (float a2 = -SetPieces.JUMP_LENGTH; a2 < SetPieces.KICKER_TROUGH_END + SetPieces.KICKER_TROUGH_EXIT; a2 += 0.25f)
            {
                float g = Mathf.Abs(SetPieces.KickerDelta(a2 + 0.25f, 12.5f) - SetPieces.KickerDelta(a2, 12.5f)) / 0.25f;
                Assert.LessOrEqual(Mathf.Atan(g) * Mathf.Rad2Deg, StageDefinitions.MAX_SLOPE_DEG);
            }
            // 道の外は 0
            Assert.AreEqual(0f, SetPieces.KickerDelta(0f, SetPieces.KICKER_HALF_WIDTH + SetPieces.KICKER_SHOULDER + 0.1f), 1e-4);
        }

        [Test] public void DunesKickerRaisesTerrainAtLip()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h0 = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var h = (float[,])h0.Clone();
            SetPieces.ApplyTerrain(s, local, h, goal);
            float jump = SetPieces.JumpArc(s, goal);
            Vector3 lip = WorldLayout.PointAtArc(local, jump - 0.5f), trough = WorldLayout.PointAtArc(local, jump + 12f);
            float H(float[,] a, Vector3 p) => WorldLayout.SampleHeight(a, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
            Assert.That(H(h, lip) - H(h0, lip), Is.InRange(3.5f, SetPieces.JUMP_HEIGHT + 0.5f));
            Assert.That(H(h, trough) - H(h0, trough), Is.InRange(-1.5f, -0.5f));
            var pts = SetPieces.ExcludeArcRange(local, jump - 2f, jump + 20f);
            float slope = HeightmapGen.MaxSlopeDegAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, StageDefinitions.Normalize(pts));
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
            // course-r2-09：2 つ目の遺物は切り通しの壁際の危ない線（他は離す）
            var relics = SetPieces.RelicSpots(s, local, goal, null);
            Assert.AreEqual(pass, relics[1].arc, 0.01f);
            for (int k = 0; k < relics.Length; k++) if (k != 1) Assert.Greater(Mathf.Abs(relics[k].arc - pass), 60f);
            Assert.Less(pass, goal - 300f);   // 切り通しを抜けてから最後の直線
            var h0 = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
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

        /// <summary>最後の下り（course-r1-04 → course-r2-07）：ステージごとの長さと高さで下り（StageDef.dashLenM/dashDropM）、その先ゴールまでほぼ平ら（砂漠は 8m の丘を越える）。</summary>
        [Test] public void EveryStageEndsWithDownhillThenFlatSprint([Values(0, 1, 2)] int i)
        {
            var s = StageDefinitions.All[i];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            float Y(float a) { var p = WorldLayout.PointAtArc(local, a); return WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z); }
            float dash = StageDefinitions.DashArc(s, goal), end = StageDefinitions.DashEndArc(s, goal);
            float drop = Y(dash) - Y(end);
            TestContext.WriteLine($"stage {i}: descent {drop:F1} m from {dash:F0} to {end:F0}, sprint {Y(end) - Y(goal):F1}");
            Assert.AreEqual(s.dashDropM, drop, 3.5f);
            Assert.Less(Mathf.Abs(Y(end) - Y(goal)), 4f);
            Assert.AreEqual(s.dashDropM, StageDefinitions.ElevationAt(s, dash) - StageDefinitions.ElevationAt(s, end), 0.2f);
            // 3 ステージの締めくくりはちがう（course-r2-07）
            Assert.AreEqual(i, s.finishKind);
            // 下りの速さの上乗せが体で分かる（9° で +18%、12m/100m で +14%）
            float deg = Mathf.Atan(s.dashDropM / s.dashLenM) * Mathf.Rad2Deg;
            Assert.Greater(MountMath.TargetSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT, -deg, false, false), MountMath.TOP_SPEED * 1.1f);
        }

        /// <summary>砂漠の踏切（course-r2-04）：序盤の道に沿って線路 2 本（190m）、路盤は道の内側、端と端がつながる、深い砂の区間の中。</summary>
        [Test] public void RailRunsAreStraightAndClear()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var runs = SetPieces.RailRuns(s, local, goal);
            Assert.AreEqual(SetPieces.RAIL_RUNS, runs.Count);
            foreach (var r in runs)
            {
                Assert.AreEqual(SetPieces.RAIL_LENGTH, WorldLayout.DistXZ(r[0], r[1]), 0.5f);
                for (int k = 0; k <= 10; k++)
                {
                    float d = WorldLayout.DistToPolylineXZ(Vector3.Lerp(r[0], r[1], k / 10f), local);
                    Assert.That(d, Is.InRange(SetPieces.RAIL_LATERAL - 2f, HeightmapGen.RUNWAY_M - SetPieces.RAIL_BED_HALF), "路盤は道の内側");
                }
            }
            Assert.Less(WorldLayout.DistXZ(runs[0][1], runs[1][0]), 2f, "線路がつながる");
            float open = SetPieces.OpeningArc(s, goal);
            Assert.That(open, Is.InRange(150f, 500f));
            Assert.IsTrue(StageDefinitions.InDeepSand(s, open), "踏切の道は深い砂（路盤だけ固い）");
            Assert.IsTrue(SetPieces.OnRailBed(runs, Vector3.Lerp(runs[0][0], runs[0][1], 0.5f)));
            Assert.AreEqual(0, SetPieces.RailRuns(StageDefinitions.All[0], StageDefinitions.SampleLocal(StageDefinitions.All[0]), 2000f).Count);
        }
    }
}
