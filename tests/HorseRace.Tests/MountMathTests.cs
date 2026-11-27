using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class MountMathTests
    {
        [Test] public void GaitSpeeds()
        {
            float[] horse = { 4f, 8f, 14f, 18f };
            float[] camel = { 4f, 7.5f, 12f, 15f };
            for (int g = 0; g < 4; g++)
            {
                Assert.AreEqual(horse[g], MountMath.GaitSpeed(MountMath.MOUNT_HORSE, g));
                Assert.AreEqual(camel[g], MountMath.GaitSpeed(MountMath.MOUNT_CAMEL, g));
            }
            Assert.AreEqual(MountMath.TOP_SPEED, MountMath.GaitSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT));
        }
        [Test] public void CamelTopSpeed_BelowHorse() => Assert.Less(MountMath.GaitSpeed(1, 3), MountMath.GaitSpeed(0, 3));
        [Test] public void AnimRefSpeed_PlaybackWithinClamp()
        {
            // 実速度 / 基準 が MountVisual の上限 1.8 を超えない（疾走の上り坂ボーナス ×1.25 込みでも 1.8 付近まで）
            for (int m = 0; m < 2; m++)
                for (int g = 0; g < 4; g++)
                {
                    float r = MountMath.GaitSpeed(m, g) / MountMath.AnimRefSpeed(m, g);
                    Assert.Greater(r, 1f); Assert.LessOrEqual(r, 1.45f);
                }
        }
        [Test] public void ExpectedStageTime_ThreeToFourMinutes()
        {
            // 2.5km を駆歩 75%・疾走 25%（スタミナ 100 で疾走約 8 秒、回復は 1/1.5 の速さ）、坂・カーブで 10% 減とした目安
            float avg = (0.75f * MountMath.GaitSpeed(0, 2) + 0.25f * MountMath.GaitSpeed(0, 3)) * 0.9f;
            float sec = 2500f / avg;
            Assert.Greater(sec, 180f); Assert.Less(sec, 240f);
            float camelAvg = (0.75f * MountMath.GaitSpeed(1, 2) + 0.25f * MountMath.GaitSpeed(1, 3)) * 0.9f;
            Assert.Less(2500f / camelAvg, 270f);
        }
        [Test] public void HorseSlowsOnSand_CamelDoesNot() { Assert.AreEqual(14f * 0.85f, MountMath.TargetSpeed(0, 2, 0f, false, true), 1e-4); Assert.AreEqual(12f, MountMath.TargetSpeed(1, 2, 0f, false, true), 1e-4); }
        [Test] public void CamelWorseUphill() => Assert.AreEqual(12f * (1 - 0.195f), MountMath.TargetSpeed(1, 2, 10f, false, false), 1e-4);
        [Test] public void Exhausted_CapsAtCanter() => Assert.AreEqual(14f, MountMath.TargetSpeed(0, 3, 0f, true, false), 1e-4);
        [Test] public void SlopeClamp() => Assert.AreEqual(7f, MountMath.TargetSpeed(0, 2, 60f, false, false), 1e-4);
        [Test] public void Approach() { Assert.AreEqual(5.5f, MountMath.Approach(0, 18, 1), 1e-4); Assert.AreEqual(7f, MountMath.Approach(18, 0, 1), 1e-4); Assert.AreEqual(18f, MountMath.Approach(15, 18, 1), 1e-4); }
        [Test] public void Stamina() { Assert.AreEqual(88f, MountMath.StepStamina(0, 100, true, 1), 1e-4); Assert.AreEqual(92.5f, MountMath.StepStamina(1, 100, true, 1), 1e-4); Assert.AreEqual(0f, MountMath.StepStamina(0, 5, true, 1)); Assert.AreEqual(100f, MountMath.StepStamina(0, 98, false, 1)); }
        [Test] public void Exhaustion_Hysteresis() { Assert.IsTrue(MountMath.IsExhausted(0, false)); Assert.IsTrue(MountMath.IsExhausted(20, true)); Assert.IsFalse(MountMath.IsExhausted(25, true)); }
        [Test] public void TurnRate() { Assert.AreEqual(90f, MountMath.TurnRateDeg(0, 0), 1e-4); Assert.AreEqual(50f, MountMath.TurnRateDeg(0, 18), 1e-4); Assert.AreEqual(70f, MountMath.TurnRateDeg(0, 9), 1e-4); Assert.AreEqual(40f, MountMath.TurnRateDeg(1, 30), 1e-4); Assert.AreEqual(72f, MountMath.TurnRateDeg(1, 0), 1e-4); }
        [Test] public void LandingStun() { Assert.AreEqual(0f, MountMath.LandingStun(2.9f)); Assert.AreEqual(0.75f, MountMath.LandingStun(8f), 1e-4); Assert.AreEqual(1.5f, MountMath.LandingStun(50f)); }
        [Test] public void SmoothAngle_NoSnap() { float a = MountMath.SmoothAngle(0, 30, 1f / 60); Assert.Greater(a, 0); Assert.Less(a, 3); }
    }
}
