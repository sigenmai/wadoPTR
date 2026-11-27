using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// VR の手綱入力。座席（seatAnchor）基準で両手の位置を見て、
    ///  - 片手を手前に引く → その側へ旋回（steer -1..1）
    ///  - 両手を引く → 減速（brake）
    ///  - 両手を同時に振り下ろす → 疾走（sprint、最後に振ってから 0.8 秒持続）
    /// を出す。挙動の計算は MountController が行い、ここは入力層だけ。
    /// 低スタミナ時は手綱の振動（ハプティクス）で知らせる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MountInputVR : UdonSharpBehaviour
    {
        public MountController mount;
        public Transform seatAnchor;

        [Header("Reins")]
        public float pullDeadzone = 0.05f;
        /// <summary>左右の引き量の差がこれ（m）未満なら旋回しない。</summary>
        public float steerDiffDeadzone = 0.1f;
        public float steerDiffRange = 0.15f;
        public float pullRange = 0.2f;
        public float brakeThreshold = 0.5f;
        public float shakeSpeed = 1.2f;
        public float sprintHold = 0.8f;

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
        private float nextHapticTime;

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

            float pullL = Mathf.Clamp01((restL.z - l.z - pullDeadzone) / pullRange);
            float pullR = Mathf.Clamp01((restR.z - r.z - pullDeadzone) / pullRange);

            if (pullL > brakeThreshold && pullR > brakeThreshold)
            {
                brake = true;
            }
            else
            {
                // 右が多く引かれていれば右旋回。差が 10cm 未満は不感帯
                float diff = (restR.z - r.z) - (restL.z - l.z);
                float mag = Mathf.Abs(diff) - steerDiffDeadzone;
                if (mag > 0f)
                {
                    float s = Mathf.Clamp01(mag / steerDiffRange);
                    steer = diff > 0f ? s : -s;
                }
            }

            float dt = Time.deltaTime;
            if (hasLast && dt > 0f)
            {
                float vyL = (l.y - lastL.y) / dt;
                float vyR = (r.y - lastR.y) / dt;
                if (vyL < -shakeSpeed && vyR < -shakeSpeed) lastShakeTime = Time.time;
            }
            lastL = l;
            lastR = r;
            hasLast = true;
            sprint = Time.time - lastShakeTime < sprintHold;

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
