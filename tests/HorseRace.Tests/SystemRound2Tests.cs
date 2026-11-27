using NUnit.Framework;
using UnityEngine;
using HorseRace;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>ゲームシステムの第 2 回のレビュー（system-r2-01〜15）の純粋な計算。</summary>
    public class SystemRound2Tests
    {
        // ---- r2-01 ラクダにも出番がある ----
        [Test] public void CamelHasAStage()
        {
            float horseFirm = MountMath.CycleAverageSpeed(MountMath.MOUNT_HORSE, false, 600f, 20f, 95f);
            float camelFirm = MountMath.CycleAverageSpeed(MountMath.MOUNT_CAMEL, false, 600f, 20f, 95f);
            float horseSand = MountMath.CycleAverageSpeed(MountMath.MOUNT_HORSE, true, 600f, 20f, 95f);
            float camelSand = MountMath.CycleAverageSpeed(MountMath.MOUNT_CAMEL, true, 600f, 20f, 95f);
            TestContext.WriteLine("firm horse " + horseFirm + " camel " + camelFirm + " / sand horse " + horseSand + " camel " + camelSand);
            Assert.That(horseFirm / camelFirm, Is.InRange(1.04f, 1.09f));
            Assert.GreaterOrEqual(camelSand / horseSand, 1.12f);
        }

        [Test] public void DeepSandStretches()
        {
            Assert.IsNull(StageDefinitions.All[0].sandTrailArcs);
            for (int i = 0; i < StageDefinitions.All.Length; i++)
            {
                var s = StageDefinitions.All[i];
                var local = StageDefinitions.SampleLocal(s);
                float goal = StageDefinitions.GoalArc(s, local);
                float share = StageDefinitions.DeepSandShare(s, goal);
                TestContext.WriteLine(s.label + ": deep sand " + (share * 100f).ToString("F1") + "% of " + goal.ToString("F0") + " m");
                // 電報の「深い砂 N%」はステージの定義と同じ（丸め）
                Assert.AreEqual(Mathf.RoundToInt(share * 100f), HudMath.DeepSandPercent(i), s.label);
                if (s.sandTrailArcs == null) continue;
                for (int k = 0; 2 * k + 1 < s.sandTrailArcs.Length; k++)
                {
                    float a0 = s.sandTrailArcs[2 * k], a1 = s.sandTrailArcs[2 * k + 1];
                    Assert.Less(a0, a1);
                    Assert.Less(a1, StageDefinitions.DashArc(s, goal), "深い砂は最後の下りの前で終わる");
                    Assert.IsTrue(StageDefinitions.InDeepSand(s, (a0 + a1) * 0.5f));
                }
                // 砂漠のほうが境目より深い砂が多い（乗り換えの理由になる）
            }
            Assert.Greater(HudMath.DeepSandPercent(1), HudMath.DeepSandPercent(2));
            Assert.Greater(HudMath.DeepSandPercent(2), 0);
            StringAssert.Contains("深い砂", HudMath.NextStageLine(0, 3));
        }

        [Test] public void DeepSandCoversTheRoad()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            var norm = StageDefinitions.Normalize(local);
            int res = 256;
            var sand = HeightmapGen.SandMask(res, s.kind, s.seed);
            var trail = HeightmapGen.TrailMask(res, s, norm);
            HeightmapGen.ClearSandOnTrail(sand, trail);
            HeightmapGen.ApplyDeepSand(sand, trail, s, local, StageDefinitions.SIZE);
            Vector3 inSand = WorldLayout.PointAtArc(local, 700f), firm = WorldLayout.PointAtArc(local, 1200f);
            Assert.Greater(Sample(sand, inSand), 0.5f, "深い砂の区間は道の上も砂");
            Assert.Less(Sample(trail, inSand), 0.5f, "深い砂の区間は踏み固めた道を薄く");
            Assert.Less(Sample(sand, firm), 0.5f, "それ以外の道の上は砂なし（course-r1-09）");
        }

        static float Sample(float[,] m, Vector3 local)
        {
            int n = m.GetLength(0);
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(local.z / StageDefinitions.SIZE * (n - 1)), 0, n - 1);
            return m[z, x];
        }

        // ---- r2-02 柵沿いで CP を飛ばしても戻らなくてよい ----
        [Test] public void AcceptCheckpoint()
        {
            Assert.IsTrue(RaceMath.AcceptCheckpoint(3, 4));
            Assert.IsTrue(RaceMath.AcceptCheckpoint(3, 5));
            Assert.IsFalse(RaceMath.AcceptCheckpoint(3, 6));
            Assert.IsFalse(RaceMath.AcceptCheckpoint(3, 3));
            Assert.IsFalse(RaceMath.AcceptCheckpoint(3, 2));
            Assert.IsFalse(RaceMath.MissedCheckpoint(3, 5));
            Assert.IsTrue(RaceMath.MissedCheckpoint(3, 6));
        }

        // ---- r2-03 立ち往生・やり直し ----
        [Test] public void StuckRiderIsReset()
        {
            Assert.IsTrue(RaceMath.StuckNow(0f, 3));
            Assert.IsFalse(RaceMath.StuckNow(0f, 1));   // 止まれと指示している
            Assert.IsFalse(RaceMath.StuckNow(5f, 3));
            Assert.IsTrue(RaceMath.NeedsReset(0f, 0f, 0f, 4f));
            Assert.IsFalse(RaceMath.NeedsReset(0f, 0f, 0f, 3.9f));
            Assert.AreEqual(40f, RaceMath.OFF_COURSE_M);
            Assert.AreEqual(1.2f, RaceMath.MANUAL_RESET_HOLD);
            Assert.AreEqual(2f, RaceMath.MANUAL_RESET_PENALTY);
        }

        // ---- r2-04 ひとりで走る ----
        [Test] public void SoloEntrantCanRace()
        {
            Assert.AreEqual(130.0, RaceMath.LobbyDeadline(100, 1, 0, 3, -1));
            Assert.AreEqual(110.0, RaceMath.LobbyDeadline(100, 1, 1, 3, 130));   // 乗って待っている：10 秒
            Assert.IsTrue(RaceMath.CanRingStart(false, true, 100, 129));
            Assert.AreEqual(160.0, RaceMath.LobbyDeadline(100, 2, 0, 3, -1));   // 2 人以上は 60 秒のまま
            Assert.AreEqual(160.0, RaceMath.LobbyDeadline(100, 2, 1, 3, 105, 1)); // ひとりの時計の途中で 2 人目：60 秒で決め直す
            Assert.AreEqual(105.0, RaceMath.LobbyDeadline(100, 2, 1, 3, 105, 2)); // 2 人のままなら延ばさない
        }

        // ---- r2-05 出走しない人 ----
        [Test] public void NoStart()
        {
            Assert.IsTrue(RaceMath.IsNoStart(0, 0f, 20.0));
            Assert.IsFalse(RaceMath.IsNoStart(0, 0f, 19.9));
            Assert.IsFalse(RaceMath.IsNoStart(0, 0f, 25.0));   // 後でスタートへ戻された人は不在にしない
            Assert.IsFalse(RaceMath.IsNoStart(1, 0f, 21.0));
            Assert.IsFalse(RaceMath.IsNoStart(0, 5f, 21.0));
        }

        // ---- r2-06 もう一戦 ----
        [Test] public void Rematch()
        {
            double d = RaceMath.RematchDeadline(100);
            Assert.AreEqual(130.0, d);
            Assert.AreEqual(130.0, RaceMath.LobbyDeadline(100, 4, 0, 6, d));    // 60 秒に延ばさない
            Assert.AreEqual(120.0, RaceMath.LobbyDeadline(105, 4, 4, 6, d));    // 全員乗ったら 15 秒
            Assert.AreEqual(130.0, RaceMath.LobbyDeadline(100, 1, 0, 6, d));    // ひとりのもう一戦も 30 秒
        }

        // ---- r2-07 相手の今の位置 ----
        [Test] public void LeadIsSpeedTimesDelay()
        {
            Vector3 p = InterpMath.Lead(Vector3.zero, 0f, 18f, InterpMath.DELAY);
            Assert.AreEqual(3.96f, p.z, 1e-3);
            Assert.AreEqual(0f, p.x, 1e-4);
            Vector3 e = InterpMath.Lead(new Vector3(1f, 2f, 3f), 90f, 10f, 0.5f);
            Assert.AreEqual(6f, e.x, 1e-3); Assert.AreEqual(2f, e.y, 1e-4); Assert.AreEqual(3f, e.z, 1e-3);
            Assert.AreEqual(InterpMath.SNAP_DISTANCE, InterpMath.Lead(Vector3.zero, 0f, 1000f, 1f).z, 1e-3);
            Assert.AreEqual(1.4f, MountMath.BUMP_RADIUS);
        }

        // ---- r2-08 相手の持久力 ----
        [Test] public void StaminaBitsDoNotTouchGait()
        {
            int[] flags = { 0, 8, 16, 32, 8 | 16 | 32 };
            for (int g = 0; g <= 4; g++)
                foreach (int f in flags)
                    foreach (float st in new[] { 100f, 30f, 0f })
                        foreach (bool ex in new[] { false, true })
                        {
                            int packed = MountMath.PackStamina(g | f, st, ex);
                            Assert.AreEqual(g, packed & 7);
                            Assert.AreEqual(f, packed & (8 | 16 | 32));
                        }
            Assert.AreEqual(0f, MountMath.RemoteStamina(MountMath.PackStamina(3, 0f, true)));
            Assert.AreEqual(30f, MountMath.RemoteStamina(MountMath.PackStamina(3, 30f, false)));
            Assert.AreEqual(100f, MountMath.RemoteStamina(MountMath.PackStamina(3, 90f, false)));
            Assert.IsTrue(MountMath.IsSpent(MountMath.PackStamina(4, 0f, true)));
            // 一度ばてて戻った帯から 100 に戻るとビットが消える
            Assert.AreEqual(3, MountMath.PackStamina(MountMath.PackStamina(3, 0f, true), 90f, false));
        }

        // ---- r2-09 砂煙は後ろにつく ----
        [Test] public void DustWorthIt()
        {
            float ball = AbilityMath.BallHitCostMeters(MountMath.TOP_SPEED);
            for (int lv = 1; lv <= 3; lv++)
            {
                float dust = AbilityMath.DustTrailLossMeters(lv, MountMath.TOP_SPEED);
                TestContext.WriteLine("Lv" + lv + " dust " + dust.ToString("F1") + " m vs ball " + ball.ToString("F1") + " m");
                Assert.GreaterOrEqual(dust, 0.6f * ball);
            }
            Assert.AreEqual(2, AbilityMath.DustHitsWhileTailing(1));
            Assert.AreEqual(4, AbilityMath.DustHitsWhileTailing(3));
        }

        // ---- r2-10 岩壁は道をふさぎきらない ----
        [Test] public void WallSpan()
        {
            for (int lv = 1; lv <= 3; lv++) Assert.LessOrEqual(AbilityMath.WallSpanFor(14f, lv), 8f);
            Assert.AreEqual(27f, AbilityMath.WallSpanFor(40f, 3));
            Assert.AreEqual(0f, AbilityMath.WallSpanFor(5f, 3));
            for (float w = 6f; w <= 60f; w += 0.5f)
                for (int lv = 1; lv <= 3; lv++)
                    Assert.GreaterOrEqual(w - AbilityMath.WallSpanFor(w, lv), AbilityMath.WALL_MIN_GAP - 1e-3f, "w " + w);
            Assert.AreEqual(3, AbilityMath.WallSegmentsFor(27f, 3));
            Assert.AreEqual(1, AbilityMath.WallSegmentsFor(8f, 3));   // 9m の壁 1 枚は入らないが、8m 幅の 1 枚
            Assert.AreEqual(0, AbilityMath.WallSegmentsFor(2f, 3));
            Assert.AreEqual(2, AbilityMath.WallSegmentsFor(15f, 2));
        }

        [Test] public void WallLeavesOneReadableGap()
        {
            float[] ls = { 7f, 3f, 20f, 1f, 12f };
            float[] rs = { 7f, 11f, 20f, 13f, 2f };
            for (int i = 0; i < ls.Length; i++)
            {
                float l = ls[i], r = rs[i];
                float w = l + r;
                for (int lv = 1; lv <= 3; lv++)
                {
                    float span = AbilityMath.WallSpanFor(w, lv);
                    float o = AbilityMath.WallOffsetFor(l, r, span);
                    float leftGap = l + o - span * 0.5f, rightGap = r - o - span * 0.5f;
                    Assert.GreaterOrEqual(leftGap, -1e-3f); Assert.GreaterOrEqual(rightGap, -1e-3f);
                    Assert.GreaterOrEqual(Mathf.Max(leftGap, rightGap), AbilityMath.WallGap(w) - 1e-3f, "l " + l + " r " + r + " lv " + lv);
                }
            }
        }

        // ---- r2-11 開幕は順番に解禁 ----
        [Test] public void OpeningReadyIsStaggered()
        {
            Assert.AreEqual(RaceMath.START_GRACE_SECONDS, AbilityMath.OpeningReady(0, 8));
            for (int l = 1; l < 8; l++) Assert.Greater(AbilityMath.OpeningReady(l, 8), AbilityMath.OpeningReady(l - 1, 8));
            Assert.AreEqual(13.25f, AbilityMath.OpeningReady(7, 8), 1e-4);
            for (int l = 0; l < 40; l++) Assert.LessOrEqual(AbilityMath.OpeningReady(l, 40), 14f);
            Assert.IsTrue(RevolverMath.OpeningAllows(6, 9f));
            Assert.IsFalse(RevolverMath.OpeningAllows(5, 9f));
            Assert.IsTrue(RevolverMath.OpeningAllows(5, 15f));
        }

        // ---- r2-12 よろけは止めない ----
        [Test] public void StunKeepsMomentum()
        {
            float lost = MountMath.StunLossMeters(18f, 0.8f);
            TestContext.WriteLine("0.8 s stun at 18 m/s: " + lost.ToString("F1") + " m");
            Assert.LessOrEqual(lost, 9f);
            Assert.Greater(lost, 0f);
            Assert.Less(MountMath.StunLossMeters(18f, 0.35f), lost);
        }

        // ---- r2-14 カーブの立ち上がり ----
        [Test] public void CornerExit()
        {
            Assert.IsTrue(MountMath.CornerCounts(false, 14f, 0.8f));
            Assert.IsFalse(MountMath.CornerCounts(true, 14f, 0.8f));
            Assert.IsFalse(MountMath.CornerCounts(false, 8f, 0.8f));
            Assert.IsTrue(MountMath.CornerExitBoost(65f, 2.5f, 0.1f));
            Assert.IsFalse(MountMath.CornerExitBoost(65f, 2.5f, 0.5f));   // まだ曲がっている
            Assert.IsFalse(MountMath.CornerExitBoost(50f, 2.5f, 0.1f));
            Assert.IsFalse(MountMath.CornerExitBoost(65f, 3.5f, 0.1f));
            Assert.Greater(MountMath.CORNER_BOOST_FACTOR, 1f);
        }

        // ---- r2-15 予想 ----
        [Test] public void PickPoints()
        {
            Assert.AreEqual(3, DirectorMath.PickPoints(1));
            Assert.AreEqual(1, DirectorMath.PickPoints(2));
            Assert.AreEqual(1, DirectorMath.PickPoints(3));
            Assert.AreEqual(0, DirectorMath.PickPoints(4));
            Assert.AreEqual(0, DirectorMath.PickPoints(0));
        }
    }
}
