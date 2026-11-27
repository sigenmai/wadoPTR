using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class SeatMathTests
    {
        [Test] public void SeatType_PerMountAndCoat()
        {
            Assert.AreEqual(SeatMath.SEAT_HORSE, SeatMath.SeatType(MountMath.MOUNT_HORSE, 0));
            Assert.AreEqual(SeatMath.SEAT_HORSE, SeatMath.SeatType(MountMath.MOUNT_HORSE, 5));
            Assert.AreEqual(SeatMath.SEAT_SKELETON, SeatMath.SeatType(MountMath.MOUNT_HORSE, SeatMath.COAT_SKELETON));
            Assert.AreEqual(SeatMath.SEAT_CAMEL, SeatMath.SeatType(MountMath.MOUNT_CAMEL, SeatMath.COAT_SKELETON));
            Assert.AreEqual(SeatMath.SEAT_RAPTOR, SeatMath.SeatType(SeatMath.MOUNT_RAPTOR, 0));
        }

        [Test] public void Saddles_FromModels()
        {
            Assert.AreEqual(1.60f, SeatMath.DefaultSaddleY(SeatMath.SEAT_HORSE), 1e-4);
            Assert.Greater(SeatMath.DefaultSaddleY(SeatMath.SEAT_CAMEL), 2.5f);   // ラクダのこぶの前は馬より 1m 高い
            Assert.AreEqual(1.64f, SeatMath.DefaultSaddleY(SeatMath.SEAT_SKELETON), 1e-4);
            for (int t = 0; t < SeatMath.SEAT_TYPES; t++) Assert.LessOrEqual(System.Math.Abs(SeatMath.DefaultSaddleZ(t)), 0.3f);
        }

        [Test] public void PersistKeys_DistinctPerTypeAndAxis()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int t = 0; t < SeatMath.SEAT_TYPES; t++)
                for (int a = 0; a < SeatMath.AXES; a++) Assert.IsTrue(seen.Add(SeatMath.PersistKey(t, a)));
            Assert.AreEqual("hr.seat.camel.height", SeatMath.PersistKey(SeatMath.SEAT_CAMEL, SeatMath.AXIS_HEIGHT));
            Assert.AreEqual("hr.seat.horse.tilt", SeatMath.PersistKey(SeatMath.SEAT_HORSE, SeatMath.AXIS_TILT));
        }

        [Test] public void Step_OneCentimetreOrDegree()
        {
            Assert.AreEqual(0.01f, SeatMath.StepAxis(SeatMath.AXIS_HEIGHT, 0f, 1), 1e-6);
            Assert.AreEqual(-0.01f, SeatMath.StepAxis(SeatMath.AXIS_FORWARD, 0f, -1), 1e-6);
            Assert.AreEqual(1f, SeatMath.StepAxis(SeatMath.AXIS_TILT, 0f, 1), 1e-6);
            Assert.AreEqual(0f, SeatMath.StepAxis(SeatMath.AXIS_TILT, 0f, 0), 1e-6);
        }

        [Test] public void Step_NoDriftAfterManySteps()
        {
            float v = 0f;
            for (int i = 0; i < 37; i++) v = SeatMath.StepAxis(SeatMath.AXIS_HEIGHT, v, 1);
            for (int i = 0; i < 12; i++) v = SeatMath.StepAxis(SeatMath.AXIS_HEIGHT, v, -1);
            Assert.AreEqual(0.25f, v, 1e-5);
        }

        [Test] public void Clamp_Limits()
        {
            Assert.AreEqual(SeatMath.HEIGHT_LIMIT, SeatMath.ClampAxis(SeatMath.AXIS_HEIGHT, 5f), 1e-5);
            Assert.AreEqual(-SeatMath.FORWARD_LIMIT, SeatMath.ClampAxis(SeatMath.AXIS_FORWARD, -5f), 1e-5);
            Assert.AreEqual(SeatMath.TILT_LIMIT, SeatMath.ClampAxis(SeatMath.AXIS_TILT, 90f), 1e-5);
            Assert.AreEqual(SeatMath.HEIGHT_LIMIT, SeatMath.StepAxis(SeatMath.AXIS_HEIGHT, SeatMath.HEIGHT_LIMIT, 1), 1e-5);
            Assert.AreEqual(0f, SeatMath.ClampAxis(SeatMath.AXIS_HEIGHT, float.NaN));
            Assert.AreEqual(0.03f, SeatMath.ClampAxis(SeatMath.AXIS_HEIGHT, 0.0304f), 1e-6);   // 刻みに丸める
        }

        [Test] public void Repeat_HoldToRepeat()
        {
            Assert.AreEqual(0, SeatMath.RepeatCount(-1f));
            Assert.AreEqual(1, SeatMath.RepeatCount(0f));
            Assert.AreEqual(1, SeatMath.RepeatCount(SeatMath.REPEAT_DELAY - 0.01f));
            Assert.AreEqual(2, SeatMath.RepeatCount(SeatMath.REPEAT_DELAY));
            int oneSecond = SeatMath.RepeatCount(SeatMath.REPEAT_DELAY + 1f) - SeatMath.RepeatCount(SeatMath.REPEAT_DELAY);
            Assert.That(oneSecond, Is.InRange(12, 16));   // 押し続けると 1 秒で約 14cm
        }

        [Test] public void Hips_Plausibility()
        {
            Assert.IsTrue(SeatMath.IsPlausibleHips(0.1f, 0.05f));
            Assert.IsFalse(SeatMath.IsPlausibleHips(-1500f, 3f));   // ボーンが無い（ワールド原点）→ 遠い
            Assert.IsFalse(SeatMath.IsPlausibleHips(0.1f, 2f));
            Assert.IsFalse(SeatMath.IsPlausibleHips(float.NaN, 0f));
        }

        [Test] public void Anchor_PutsHipsOnSaddle()
        {
            // 腰が Anchor から 0.15m 上・0.04m 前に来るアバター：Anchor を下げて後ろへ
            float y = SeatMath.AnchorY(1.6f, 0.15f, 0f);
            float z = SeatMath.AnchorZ(-0.05f, 0.04f, 0f);
            Assert.AreEqual(1.45f, y, 1e-5);
            Assert.AreEqual(-0.09f, z, 1e-5);
            // Anchor + hips = 鞍（＋微調整）
            Assert.AreEqual(1.6f + 0.02f, SeatMath.AnchorY(1.6f, 0.15f, 0.02f) + 0.15f, 1e-5);
        }

        [Test] public void Fallback_FromEyeHeight()
        {
            Assert.AreEqual(1.6f * SeatMath.FALLBACK_HIPS_PER_EYE, SeatMath.FallbackHipsY(0f), 1e-5);
            Assert.AreEqual(3f * SeatMath.FALLBACK_HIPS_PER_EYE, SeatMath.FallbackHipsY(3f), 1e-5);
        }

        [Test] public void Calib_Blend()
        {
            Assert.AreEqual(0.4f, SeatMath.BlendCalib(0f, 0.4f, false), 1e-5);
            Assert.AreEqual(0.2f, SeatMath.BlendCalib(0f, 0.4f, true), 1e-5);
        }

        [Test] public void Labels()
        {
            Assert.AreEqual("高さ +3cm", SeatMath.AxisLabel(SeatMath.AXIS_HEIGHT, 0.03f));
            Assert.AreEqual("前後 -12cm", SeatMath.AxisLabel(SeatMath.AXIS_FORWARD, -0.12f));
            Assert.AreEqual("傾き ±0°", SeatMath.AxisLabel(SeatMath.AXIS_TILT, 0f));
        }
    }
}
