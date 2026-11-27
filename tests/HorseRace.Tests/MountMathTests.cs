using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class MountMathTests
    {
        [Test] public void GaitSpeeds() { Assert.AreEqual(13f, MountMath.GaitSpeed(MountMath.MOUNT_HORSE, MountMath.GAIT_SPRINT)); Assert.AreEqual(11f, MountMath.GaitSpeed(MountMath.MOUNT_CAMEL, MountMath.GAIT_SPRINT)); }
        [Test] public void HorseSlowsOnSand_CamelDoesNot() { Assert.AreEqual(8.5f, MountMath.TargetSpeed(0, 2, 0f, false, true), 1e-4); Assert.AreEqual(9f, MountMath.TargetSpeed(1, 2, 0f, false, true), 1e-4); }
        [Test] public void CamelWorseUphill() => Assert.AreEqual(9f * (1 - 0.195f), MountMath.TargetSpeed(1, 2, 10f, false, false), 1e-4);
        [Test] public void Exhausted_CapsAtCanter() => Assert.AreEqual(10f, MountMath.TargetSpeed(0, 3, 0f, true, false), 1e-4);
        [Test] public void SlopeClamp() => Assert.AreEqual(5f, MountMath.TargetSpeed(0, 2, 60f, false, false), 1e-4);
        [Test] public void Approach() { Assert.AreEqual(4f, MountMath.Approach(0, 13, 1), 1e-4); Assert.AreEqual(5f, MountMath.Approach(13, 0, 1), 1e-4); }
        [Test] public void Stamina() { Assert.AreEqual(88f, MountMath.StepStamina(0, 100, true, 1), 1e-4); Assert.AreEqual(92.5f, MountMath.StepStamina(1, 100, true, 1), 1e-4); Assert.AreEqual(0f, MountMath.StepStamina(0, 5, true, 1)); Assert.AreEqual(100f, MountMath.StepStamina(0, 98, false, 1)); }
        [Test] public void Exhaustion_Hysteresis() { Assert.IsTrue(MountMath.IsExhausted(0, false)); Assert.IsTrue(MountMath.IsExhausted(20, true)); Assert.IsFalse(MountMath.IsExhausted(25, true)); }
        [Test] public void TurnRate() { Assert.AreEqual(90f, MountMath.TurnRateDeg(0, 0), 1e-4); Assert.AreEqual(45f, MountMath.TurnRateDeg(0, 13), 1e-4); Assert.AreEqual(72f, MountMath.TurnRateDeg(1, 0), 1e-4); }
        [Test] public void LandingStun() { Assert.AreEqual(0f, MountMath.LandingStun(2.9f)); Assert.AreEqual(0.75f, MountMath.LandingStun(8f), 1e-4); Assert.AreEqual(1.5f, MountMath.LandingStun(50f)); }
        [Test] public void SmoothAngle_NoSnap() { float a = MountMath.SmoothAngle(0, 30, 1f / 60); Assert.Greater(a, 0); Assert.Less(a, 3); }
    }
}
