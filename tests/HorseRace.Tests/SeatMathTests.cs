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
        // ---- art-r7-03：騎乗時の視点（座った目の高さ）と首の札 ----
        [Test] public void SeatedEye_FromSaddleAndAvatar()
        {
            // 馬：鞍の点 1.60m + 0.43 × 立った目（1.2 / 1.65 / 1.9m）
            Assert.AreEqual(2.116f, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.2f, 0f), 0.01f);
            Assert.AreEqual(2.31f, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.65f, 0f), 0.01f);
            Assert.AreEqual(2.417f, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.9f, 0f), 0.01f);
            Assert.AreEqual(3.41f, SeatMath.SeatedEyeY(SeatMath.SEAT_CAMEL, 1.65f, 0f), 0.01f);
            Assert.AreEqual(2.31f, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 0f, 0f), 0.01f, "目の高さが取れない：1.65m");
            Assert.AreEqual(2.41f, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.65f, 0.10f), 0.01f, "微調整の高さ");
            Assert.AreEqual(0.049f, SeatMath.SeatedEyeZ(SeatMath.SEAT_HORSE, 1.65f, 0f), 0.01f);
            // Horse.fbx（拡大 1）の耳の先 2.05m・前後 0.9〜1.0m：目は耳より上（頭と首は道の下に見える）、目と耳の間は 1m 以上（カメラが頭・たてがみの中に入らない）
            const float EAR_TOP = 2.05f, EAR_FWD = 0.95f;
            Assert.Greater(SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.65f, 0f), EAR_TOP + 0.15f);
            float down = UnityEngine.Mathf.Atan2(SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.65f, 0f) - EAR_TOP, EAR_FWD - SeatMath.SeatedEyeZ(SeatMath.SEAT_HORSE, 1.65f, 0f)) * UnityEngine.Mathf.Rad2Deg;
            Assert.That(down, Is.InRange(8f, 25f), "耳は水平から 8〜25° 下（縦 60° の視野の下半分、道の先は隠さない）");
        }

        [Test] public void NeckCard_ClearsTheMane()
        {
            // Horse.fbx のたてがみ：前後 0.4m で 1.80m、0.5m で 1.87m、0.6m で 1.93m（鞍の点 1.60m・前後 -0.05m から）
            float y = SeatMath.SADDLE_HORSE_Y + HudMath.NeckCardRise(1.65f), z = SeatMath.SADDLE_HORSE_Z + HudMath.NECK_CARD_FWD_M;
            float tilt = 35f * UnityEngine.Mathf.Deg2Rad;
            // 札の下の持久力の印の列（札の中心から NECK_PIPS_BOTTOM 下、35° 後ろへ倒す）
            float drop = -HorseRace.EditorTools.UiStyle.NECK_PIPS_BOTTOM;
            float py = y - drop * UnityEngine.Mathf.Cos(tilt), pz = z + drop * UnityEngine.Mathf.Sin(tilt);
            float mane = pz <= 0.4f ? 1.80f : (pz <= 0.5f ? 1.80f + (pz - 0.4f) * 0.7f : 1.87f + (pz - 0.5f) * 0.6f);
            Assert.Greater(py, mane + 0.05f, "印の列はたてがみの 5cm 以上上");
            Assert.Less(y, SeatMath.SeatedEyeY(SeatMath.SEAT_HORSE, 1.65f, 0f) - 0.2f, "札は目より 20cm 以上下");
            Assert.AreEqual(HudMath.NECK_CARD_MIN_RISE_M, HudMath.NeckCardRise(1.2f), 1e-4f, "小さなアバターもたてがみの上");
            Assert.Greater(HudMath.NeckCardRise(1.9f), HudMath.NeckCardRise(1.65f), "背の高いアバターは上げる");
            Assert.LessOrEqual(HudMath.NeckCardRise(5f), HudMath.NECK_CARD_MAX_RISE_M);
        }
    }
}
