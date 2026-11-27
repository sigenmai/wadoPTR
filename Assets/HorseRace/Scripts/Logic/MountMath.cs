using UnityEngine;

namespace HorseRace
{
    /// <summary>Pure movement math for mounts. Static methods only (UdonSharp-safe).</summary>
    public class MountMath : UdonSharp.UdonSharpBehaviour
    {
        public const int MOUNT_HORSE = 0;
        public const int MOUNT_CAMEL = 1;
        public const int GAIT_WALK = 0;
        public const int GAIT_TROT = 1;
        public const int GAIT_CANTER = 2;
        public const int GAIT_SPRINT = 3;

        /// <summary>馬の疾走（全体の最高速度）。旋回の速さの基準にも使う。</summary>
        public const float TOP_SPEED = 18f;
        /// <summary>加速・減速（m/s²）。0→18 m/s が約 3.3 秒、18→0 が約 1.6 秒。</summary>
        public const float ACCEL = 5.5f;
        public const float DECEL = 11f;
        /// <summary>旋回の速さ：止まっているとき／最高速のとき（度/秒）。</summary>
        public const float TURN_RATE_SLOW = 90f;
        public const float TURN_RATE_FAST = 50f;

        /// <summary>
        /// 歩様の速さ（m/s）。馬 4 / 8 / 14 / 18、ラクダ 4 / 7.5 / 12 / 15（ラクダの最高速は馬の約 0.83 倍）。
        /// 2.5km のコースを駆歩＋ときどき疾走で、坂・カーブ込みで平均約 12〜13 m/s ＝ 1 ステージ約 3 分 20 秒〜3 分 30 秒。
        /// </summary>
        public static float GaitSpeed(int mount, int gait)
        {
            if (mount == MOUNT_CAMEL)
            {
                if (gait == GAIT_WALK) return 4f;
                if (gait == GAIT_TROT) return 7.5f;
                if (gait == GAIT_CANTER) return 12f;
                return 15f;
            }
            if (gait == GAIT_WALK) return 4f;
            if (gait == GAIT_TROT) return 8f;
            if (gait == GAIT_CANTER) return 14f;
            return TOP_SPEED;
        }

        /// <summary>
        /// アニメーションを等倍で再生するときの速さ（クリップを作ったときの歩様の速さ：馬 3/6/10/13、ラクダ 3/6/9/11）。
        /// 実速度 / これ ＝ 再生速度なので、速くなった分だけ脚の回転も速くなる（足が滑って見えない）。
        /// </summary>
        public static float AnimRefSpeed(int mount, int gait)
        {
            if (mount == MOUNT_CAMEL)
            {
                if (gait == GAIT_WALK) return 3f;
                if (gait == GAIT_TROT) return 6f;
                if (gait == GAIT_CANTER) return 9f;
                return 11f;
            }
            if (gait == GAIT_WALK) return 3f;
            if (gait == GAIT_TROT) return 6f;
            if (gait == GAIT_CANTER) return 10f;
            return 13f;
        }

        public static float TargetSpeed(int mount, int gait, float slopeDeg, bool exhausted, bool onSand)
        {
            if (exhausted && gait > GAIT_CANTER) gait = GAIT_CANTER;
            float speed = GaitSpeed(mount, gait);
            float uphillPerDeg = mount == MOUNT_CAMEL ? 0.0195f : 0.015f;
            float factor = slopeDeg >= 0f ? 1f - slopeDeg * uphillPerDeg : 1f - slopeDeg * 0.01f;
            factor = Mathf.Clamp(factor, 0.5f, 1.25f);
            speed *= factor;
            if (onSand && mount == MOUNT_HORSE) speed *= 0.85f;
            return speed;
        }

        public static float Approach(float current, float target, float dt)
        {
            if (target > current) return Mathf.Min(target, current + ACCEL * dt);
            return Mathf.Max(target, current - DECEL * dt);
        }

        public static float StepStamina(int mount, float stamina, bool sprinting, float dt)
        {
            float rate = 8f;
            if (sprinting) rate = mount == MOUNT_CAMEL ? -7.5f : -12f;
            return Mathf.Clamp(stamina + rate * dt, 0f, 100f);
        }

        public static bool IsExhausted(float stamina, bool wasExhausted)
        {
            if (stamina <= 0f) return true;
            if (stamina >= 25f) return false;
            return wasExhausted;
        }

        public static float TurnRateDeg(int mount, float speed)
        {
            float t = Mathf.Clamp01(speed / TOP_SPEED);
            float rate = TURN_RATE_SLOW + (TURN_RATE_FAST - TURN_RATE_SLOW) * t;
            return mount == MOUNT_CAMEL ? rate * 0.8f : rate;
        }

        public static float LandingStun(float fallHeight)
        {
            if (fallHeight < 3f) return 0f;
            return Mathf.Min((fallHeight - 3f) * 0.15f, 1.5f);
        }

        public static float SmoothAngle(float current, float target, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            return current + (target - current) * k;
        }
    }
}
