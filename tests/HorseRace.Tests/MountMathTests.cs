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
        [Test] public void HorseSlowsOnSand_CamelDoesNot() { Assert.AreEqual(14f * 0.78f, MountMath.TargetSpeed(0, 2, 0f, false, true), 1e-4); Assert.AreEqual(12f, MountMath.TargetSpeed(1, 2, 0f, false, true), 1e-4); }
        [Test] public void CamelFasterOnSand_ByAboutSevenPercent()
        {
            float horse = MountMath.TargetSpeed(0, 3, 0f, false, true);
            float camel = MountMath.TargetSpeed(1, 3, 0f, false, true);
            Assert.Greater(camel / horse, 1.05f); Assert.Less(camel / horse, 1.10f);
            // 砂でなければ馬が速い（荒野・境目は馬、砂漠はラクダ）
            Assert.Greater(MountMath.TargetSpeed(0, 3, 0f, false, false), MountMath.TargetSpeed(1, 3, 0f, false, false));
        }
        [Test] public void CamelWorseUphill() => Assert.AreEqual(12f * (1 - 0.195f), MountMath.TargetSpeed(1, 2, 10f, false, false), 1e-4);
        [Test] public void Exhausted_CapsAtTrot() { Assert.AreEqual(8f, MountMath.TargetSpeed(0, 3, 0f, true, false), 1e-4); Assert.AreEqual(7.5f, MountMath.TargetSpeed(1, 2, 0f, true, false), 1e-4); }
        [Test] public void SlopeClamp() => Assert.AreEqual(7f, MountMath.TargetSpeed(0, 2, 60f, false, false), 1e-4);
        [Test] public void Approach() { Assert.AreEqual(5.5f, MountMath.Approach(0, 18, 1), 1e-4); Assert.AreEqual(7f, MountMath.Approach(18, 0, 1), 1e-4); Assert.AreEqual(18f, MountMath.Approach(15, 18, 1), 1e-4); }
        [Test] public void Stamina()
        {
            Assert.AreEqual(88f, MountMath.StepStamina(0, 100, true, false, 3, 1), 1e-4);
            Assert.AreEqual(92.5f, MountMath.StepStamina(1, 100, true, false, 3, 1), 1e-4);
            Assert.AreEqual(0f, MountMath.StepStamina(0, 5, true, false, 3, 1));
            Assert.AreEqual(100f, MountMath.StepStamina(0, 98, false, false, 2, 1));
            Assert.AreEqual(62f, MountMath.StepStamina(0, 50, false, false, 1, 1), 1e-4);   // 速歩 +12
            Assert.AreEqual(58f, MountMath.StepStamina(0, 50, false, false, 2, 1), 1e-4);   // 駆歩 +8
            Assert.AreEqual(54f, MountMath.StepStamina(0, 50, false, true, 1, 1), 1e-4);    // ばて +4
            Assert.AreEqual(60f, MountMath.StepStamina(0, 50, false, false, 2, 1, 1.25f), 1e-4);   // ひと息 ×1.25
        }
        [Test] public void Exhaustion_Hysteresis() { Assert.IsTrue(MountMath.IsExhausted(0, false)); Assert.IsTrue(MountMath.IsExhausted(39, true)); Assert.IsFalse(MountMath.IsExhausted(40, true)); Assert.IsFalse(MountMath.IsExhausted(20, false)); }
        [Test] public void SecondWind() { Assert.IsTrue(MountMath.EarnsSecondWind(15f)); Assert.IsFalse(MountMath.EarnsSecondWind(14.9f)); }

        /// <summary>200 秒の平地で 2 つの乗り方を比べる（加速・減速込み）。</summary>
        static float SimulateStage(bool holdForever, float releaseAt, float resumeAt)
        {
            float dt = 0.02f, speed = 14f, stamina = 100f, dist = 0f, windUntil = -1f;
            bool exhausted = false, sprintHeld = true, wasSprinting = false;
            for (float t = 0f; t < 200f; t += dt)
            {
                if (!holdForever)
                {
                    if (sprintHeld && stamina <= releaseAt) sprintHeld = false;
                    else if (!sprintHeld && stamina >= resumeAt) sprintHeld = true;
                }
                bool sprinting = sprintHeld && !exhausted;
                if (wasSprinting && !sprinting && MountMath.EarnsSecondWind(stamina)) windUntil = t + MountMath.SECOND_WIND_SECONDS;
                wasSprinting = sprinting;
                int gait = sprintHeld ? MountMath.GAIT_SPRINT : MountMath.GAIT_CANTER;
                float mul = t < windUntil ? MountMath.SECOND_WIND_MUL : 1f;
                stamina = MountMath.StepStamina(0, stamina, sprinting, exhausted, exhausted ? MountMath.GAIT_TROT : gait, dt, mul);
                exhausted = MountMath.IsExhausted(stamina, exhausted);
                speed = MountMath.Approach(speed, MountMath.TargetSpeed(0, gait, 0f, exhausted, false), dt);
                dist += speed * dt;
            }
            return dist;
        }

        [Test] public void Stamina_ReleasingAt15_BeatsHoldingShiftForever()
        {
            float hold = SimulateStage(true, 0f, 0f);
            float smart = SimulateStage(false, MountMath.SECOND_WIND, 90f);
            float canterOnly = 200f * 14f;
            TestContext.WriteLine("hold " + hold + " m, release@15 " + smart + " m, canter only " + canterOnly + " m");
            Assert.Greater(smart, hold * 1.15f);       // 走り切ると大きく遅れる
            Assert.Greater(smart, canterOnly);         // 上手に使えば疾走は得
            Assert.Less(hold, canterOnly);             // ずっと押しっぱなしは駆歩だけより遅い
        }
        [Test] public void SprintLatch()
        {
            Assert.IsTrue(MountMath.SprintLatch(false, true, false, false));
            Assert.IsTrue(MountMath.SprintLatch(true, false, false, false));    // 振らなくても続く
            Assert.IsTrue(MountMath.SprintLatch(true, true, false, false));     // 振り直しても ON のまま
            Assert.IsFalse(MountMath.SprintLatch(true, false, true, false));    // 手綱を引くと切れる
            Assert.IsFalse(MountMath.SprintLatch(true, false, false, true));    // ばてると切れる
            Assert.IsFalse(MountMath.SprintLatch(false, true, false, true));
            Assert.IsFalse(MountMath.SprintLatch(false, false, false, false));
        }
        [Test] public void WallScrape()
        {
            Assert.AreEqual(0.9f, MountMath.WallScrapeFactor(MountMath.WallHead(0f)), 1e-4);
            Assert.AreEqual(0.25f, MountMath.WallScrapeFactor(MountMath.WallHead(90f)), 1e-4);
            Assert.Greater(MountMath.WallScrapeFactor(MountMath.WallHead(20f)), 0.65f);   // 斜めにかすっても大半の速さが残る
            Assert.Less(MountMath.WallHead(45f), MountMath.WALL_HARD_STOP_HEAD);
            Assert.Greater(MountMath.WallHead(80f), MountMath.WALL_HARD_STOP_HEAD);
        }
        [Test] public void Landing()
        {
            Assert.AreEqual(1f, MountMath.LandingSpeedKeep(5.9f));
            Assert.AreEqual(0.55f, MountMath.LandingSpeedKeep(20f), 1e-4);
            Assert.AreEqual(0.55f, MountMath.LandingSpeedKeep(60f), 1e-4);
            Assert.AreEqual(0.775f, MountMath.LandingSpeedKeep(13f), 1e-4);
            Assert.IsTrue(MountMath.CleanLanding(8f, 0.1f));
            Assert.IsFalse(MountMath.CleanLanding(8f, 0.4f));
            Assert.IsFalse(MountMath.CleanLanding(12f, 0f));
            Assert.IsFalse(MountMath.CleanLanding(8f, -1f));
        }
        [Test] public void CliffShortcut_CostsLittle()
        {
            // 12m の崖下り：硬直 0.48 秒以内、速さは 8 割以上残る（旧：1.35 秒硬直＋速さ 0）
            Assert.LessOrEqual(MountMath.LandingStun(12f), 0.5f);
            Assert.Greater(MountMath.LandingSpeedKeep(12f), 0.8f);
        }
        [Test] public void Draft()
        {
            Assert.AreEqual(1f, MountMath.DraftBonus(8f, 5f));
            Assert.AreEqual(1f, MountMath.DraftBonus(8f, -12f));
            Assert.AreEqual(0f, MountMath.DraftBonus(2f, 0f));
            Assert.AreEqual(0f, MountMath.DraftBonus(15f, 0f));
            Assert.AreEqual(0f, MountMath.DraftBonus(8f, 13f));
        }
        [Test] public void StartBoost()
        {
            Assert.AreEqual(MountMath.START_BOOST, MountMath.StartBoost(0f));
            Assert.AreEqual(MountMath.START_BOOST, MountMath.StartBoost(-0.3f));
            Assert.AreEqual(MountMath.START_BOOST, MountMath.StartBoost(0.35f));
            Assert.AreEqual(MountMath.START_NONE, MountMath.StartBoost(-0.5f));
            Assert.AreEqual(MountMath.START_NONE, MountMath.StartBoost(1f));
            Assert.AreEqual(MountMath.START_STALL, MountMath.StartBoost(-0.7f));
        }
        [Test] public void TurnRate() { Assert.AreEqual(90f, MountMath.TurnRateDeg(0, 0), 1e-4); Assert.AreEqual(50f, MountMath.TurnRateDeg(0, 18), 1e-4); Assert.AreEqual(70f, MountMath.TurnRateDeg(0, 9), 1e-4); Assert.AreEqual(40f, MountMath.TurnRateDeg(1, 30), 1e-4); Assert.AreEqual(72f, MountMath.TurnRateDeg(1, 0), 1e-4); }
        [Test] public void LandingStun() { Assert.AreEqual(0f, MountMath.LandingStun(5.9f)); Assert.AreEqual(0.32f, MountMath.LandingStun(10f), 1e-4); Assert.AreEqual(0.8f, MountMath.LandingStun(50f)); }
        [Test] public void SmoothAngle_NoSnap() { float a = MountMath.SmoothAngle(0, 30, 1f / 60); Assert.Greater(a, 0); Assert.Less(a, 3); }
    }
}
