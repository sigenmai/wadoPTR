using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// VR の手綱入力。座席（seatAnchor）基準で両手の位置を見て、
    ///  - 片手を手前に引く → その側へ旋回（steer -1..1）
    ///  - 両手を引く → 減速（brake）
    ///  - 両手を同時に振り下ろす（リボルバーを持っているときは空いている片手を強めに）→ 疾走 ON のまま（ばてるか手綱を引くまで。MountMath.SprintLatch）
    ///    腕を振り続けなくてよい（system-r1-11：デスクトップの Shift 押しっぱなしと同じ手間）
    /// を出す。挙動の計算は MountController が行い、ここは入力層だけ。
    /// 低スタミナ時は手綱の振動（ハプティクス）で知らせる。
    /// system-r3-08：リボルバーを持っている手は手綱に数えない（構えても曲がらない・止まらない）。片手のときは空いている手の
    ///   横のずれでハンドル（MountMath.ReinSteer）、引き 0.7 超えでブレーキ。休める位置は、ハンドルを切らず振ってもいない間に
    ///   0.15/秒でゆっくり今の手へ寄せる（座り直しで曲がり癖・ブレーキ癖が残らない）。号砲（MountController.BeginRace）でも測り直す。
    /// system-r3-16：PulseHands で出来事（スタートダッシュ・立ち上がり・きれいな着地・カーブの立ち上がり・疾走の蹄）を手に伝える。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MountInputVR : UdonSharpBehaviour
    {
        public MountController mount;
        public Transform seatAnchor;

        [Header("Reins")]
        public float pullDeadzone = 0.05f;
        // 旋回の不感帯・幅は MountMath.REIN_*（ReinSteer、system-r3-08）
        public float pullRange = 0.2f;
        public float brakeThreshold = 0.5f;
        public float shakeSpeed = 1.2f;
        [Tooltip("片手で振るとき（もう片方の手でリボルバーを持っている）の速さ（m/s）")]
        public float oneHandShakeSpeed = 1.4f;
        [Tooltip("振り下ろしの後、この秒数の間の次の振りは何もしない（腕を休める）")]
        public float swingCooldown = 0.5f;

        // ---- system-r3-16：出来事ごとの振動（秒・強さ）----
        public const float PULSE_START_S = 0.15f;
        public const float PULSE_START_AMP = 0.6f;
        public const float PULSE_STALL_S = 0.3f;
        public const float PULSE_STALL_AMP = 0.9f;
        public const float PULSE_BOOST_S = 0.1f;
        public const float PULSE_BOOST_AMP = 0.5f;
        public const float PULSE_HIT_S = 0.25f;
        public const float PULSE_HIT_AMP = 0.8f;
        public const float PULSE_HOOF_S = 0.03f;
        public const float PULSE_HOOF_AMP = 0.15f;

        [Header("Haptics")]
        public float lowStamina = 25f;
        public float hapticInterval = 0.9f;

        // 出力（MountController が読む）
        public float steer;
        public bool brake;
        public bool sprint;

        private Vector3 restL;
        private Vector3 restR;
        private bool calibrated;
        private Vector3 lastL;
        private Vector3 lastR;
        private bool hasLast;
        private float lastShakeTime = -10f;
        private RevolverHolder revolver;
        private bool revolverLooked;
        private float nextHapticTime;

        /// <summary>両手を seconds 秒・強さ amp で振るわせる（VR で乗っている本人だけ。デスクトップでは何もしない）。</summary>
        public void PulseHands(float seconds, float amp)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsUserInVR()) return;
            if (mount != null && !mount.IsLocalRider()) return;
            float s = Mathf.Clamp(seconds, 0f, 0.5f);
            float a = Mathf.Clamp01(amp);
            local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, s, a, 120f);
            local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, s, a, 120f);
        }

        /// <summary>着席後に MountController から呼ばれる。今の手の位置を「手綱を緩めた位置」とする。</summary>
        public void Calibrate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || seatAnchor == null || !local.IsUserInVR()) return;
            restL = HandLocal(local, VRCPlayerApi.TrackingDataType.LeftHand);
            restR = HandLocal(local, VRCPlayerApi.TrackingDataType.RightHand);
            calibrated = true;
            hasLast = false;
        }

        private RevolverHolder Revolver()
        {
            if (!revolverLooked && mount != null)
            {
                revolverLooked = true;
                AbilityCaster ac = mount.GetComponent<AbilityCaster>();
                if (ac != null) revolver = ac.revolver;
            }
            return revolver;
        }

        private Vector3 HandLocal(VRCPlayerApi local, VRCPlayerApi.TrackingDataType type)
        {
            VRCPlayerApi.TrackingData td = local.GetTrackingData(type);
            return seatAnchor.InverseTransformPoint(td.position);
        }

        private void Update()
        {
            steer = 0f;
            brake = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || mount == null || seatAnchor == null) { sprint = false; return; }
            if (!local.IsUserInVR() || !mount.IsLocalRider()) { sprint = false; hasLast = false; return; }
            if (!calibrated) Calibrate();

            Vector3 l = HandLocal(local, VRCPlayerApi.TrackingDataType.LeftHand);
            Vector3 r = HandLocal(local, VRCPlayerApi.TrackingDataType.RightHand);

            // リボルバーを持っている手は手綱に数えない（system-r3-08）
            RevolverHolder rvh = Revolver();
            int mode = MountMath.REIN_BOTH;
            if (rvh != null)
            {
                if (rvh.IsHeldInLocalHand(AbilityCaster.AIM_RIGHT)) mode = MountMath.REIN_LEFT_ONLY;
                else if (rvh.IsHeldInLocalHand(AbilityCaster.AIM_LEFT)) mode = MountMath.REIN_RIGHT_ONLY;
            }
            float rawL = restL.z - l.z;
            float rawR = restR.z - r.z;
            float pullL = Mathf.Clamp01((rawL - pullDeadzone) / pullRange);
            float pullR = Mathf.Clamp01((rawR - pullDeadzone) / pullRange);
            float lateral = mode == MountMath.REIN_LEFT_ONLY ? l.x - restL.x : (mode == MountMath.REIN_RIGHT_ONLY ? r.x - restR.x : 0f);
            brake = MountMath.ReinBrake(pullL, pullR, mode, brakeThreshold);
            if (!brake) steer = MountMath.ReinSteer(rawL, rawR, mode, lateral);

            float dt = Time.deltaTime;
            bool swing = false;
            if (hasLast && dt > 0f && Time.time - lastShakeTime >= swingCooldown)
            {
                float vyL = (l.y - lastL.y) / dt;
                float vyR = (r.y - lastR.y) / dt;
                if (vyL < -shakeSpeed && vyR < -shakeSpeed) swing = true;
                else
                {
                    // リボルバーを持っている手の反対の手だけで振ってもよい
                    RevolverHolder rv = Revolver();
                    if (rv != null)
                    {
                        if (rv.IsHeldInLocalHand(AbilityCaster.AIM_RIGHT) && vyL < -oneHandShakeSpeed) swing = true;
                        else if (rv.IsHeldInLocalHand(AbilityCaster.AIM_LEFT) && vyR < -oneHandShakeSpeed) swing = true;
                    }
                }
                if (swing) lastShakeTime = Time.time;
            }
            // 休める位置をゆっくり今の手へ（ハンドルを切らず・ブレーキなし・1 秒振っていない間）。リボルバーの手は寄せない
            if (dt > 0f && MountMath.ReinMayRecenter(steer, brake, Time.time - lastShakeTime))
            {
                float k = Mathf.Clamp01(MountMath.REIN_RECENTER_RATE * dt);
                if (mode != MountMath.REIN_RIGHT_ONLY) restL = Vector3.Lerp(restL, l, k);
                if (mode != MountMath.REIN_LEFT_ONLY) restR = Vector3.Lerp(restR, r, k);
            }
            lastL = l;
            lastR = r;
            hasLast = true;
            sprint = MountMath.SprintLatch(sprint, swing, brake, mount.Exhausted);

            if (mount.Stamina < lowStamina && Time.time >= nextHapticTime)
            {
                nextHapticTime = Time.time + hapticInterval;
                float amp = mount.Exhausted ? 0.5f : 0.25f;
                local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, 0.12f, amp, 60f);
                local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 0.12f, amp, 60f);
            }
        }
    }
}
