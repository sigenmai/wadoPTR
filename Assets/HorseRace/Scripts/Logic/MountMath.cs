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

        public static float GaitSpeed(int mount, int gait)
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
            if (target > current) return Mathf.Min(target, current + 4f * dt);
            return Mathf.Max(target, current - 8f * dt);
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
            float t = Mathf.Clamp01(speed / 13f);
            float rate = 90f + (45f - 90f) * t;
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
