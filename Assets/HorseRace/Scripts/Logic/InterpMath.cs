using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// Snapshot interpolation for remote mounts. Static only (UdonSharp-safe).
    /// Samples are stamped with the sender's server time and kept in a small time-ordered buffer
    /// (parallel arrays). The receiver renders a fixed delay behind server time on a smoothed clock.
    /// </summary>
    public class InterpMath : UdonSharp.UdonSharpBehaviour
    {
        public const int BUFFER = 4;
        /// <summary>Render this far behind the sender (about 2.2 send intervals at 10 Hz).</summary>
        public const float DELAY = 0.22f;
        /// <summary>A new sample farther than this from the newest one is a teleport: snap.</summary>
        public const float SNAP_DISTANCE = 10f;
        /// <summary>Render clock jumps instead of easing when it is off by more than this.</summary>
        public const double CLOCK_SNAP = 0.5;
        /// <summary>How fast the render clock eases toward its target (per second).</summary>
        public const float CLOCK_GAIN = 2f;
        /// <summary>Time constant for blending out the displayed-vs-target error after a correction.</summary>
        public const float ERROR_TAU = 0.15f;

        /// <summary>
        /// Inserts a sample keeping times ascending and returns the new count. A sample with the same time replaces
        /// the old one. When the buffer is full the oldest sample is dropped; a sample older than all kept ones is ignored.
        /// </summary>
        public static int Insert(double[] times, Vector3[] pos, float[] yaw, float[] speed, int count, double t, Vector3 p, float y, float s)
        {
            int cap = times.Length;
            if (count > cap) count = cap;
            int idx = count;
            while (idx > 0 && times[idx - 1] > t) idx--;
            if (idx > 0 && times[idx - 1] == t)
            {
                pos[idx - 1] = p; yaw[idx - 1] = y; speed[idx - 1] = s;
                return count;
            }
            if (count == cap)
            {
                if (idx == 0) return count;
                for (int i = 1; i < idx; i++)
                {
                    times[i - 1] = times[i]; pos[i - 1] = pos[i]; yaw[i - 1] = yaw[i]; speed[i - 1] = speed[i];
                }
                idx--;
                times[idx] = t; pos[idx] = p; yaw[idx] = y; speed[idx] = s;
                return count;
            }
            for (int i = count; i > idx; i--)
            {
                times[i] = times[i - 1]; pos[i] = pos[i - 1]; yaw[i] = yaw[i - 1]; speed[i] = speed[i - 1];
            }
            times[idx] = t; pos[idx] = p; yaw[idx] = y; speed[idx] = s;
            return count + 1;
        }

        /// <summary>
        /// Index of the newest sample at or before renderTime. -1 when renderTime is before the oldest sample
        /// (or the buffer is empty); count-1 means "past the newest sample" (extrapolate).
        /// </summary>
        public static int SpanStart(double[] times, int count, double renderTime)
        {
            for (int i = count - 1; i >= 0; i--)
                if (times[i] <= renderTime) return i;
            return -1;
        }

        /// <summary>Interpolation factor of renderTime between a and b, clamped to 0..1.</summary>
        public static float SpanT(double a, double b, double renderTime)
        {
            double span = b - a;
            if (span < 0.0001) return 1f;
            return Mathf.Clamp01((float)((renderTime - a) / span));
        }

        /// <summary>Shortest signed difference b - a in degrees (-180..180].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        public static float LerpAngle(float a, float b, float t)
        {
            return a + DeltaAngle(a, b) * Mathf.Clamp01(t);
        }

        /// <summary>Advances a render clock by dt and eases it toward target; jumps when far off.</summary>
        public static double AdvanceClock(double clock, float dt, double target)
        {
            double c = clock + dt;
            double err = target - c;
            if (err > CLOCK_SNAP || err < -CLOCK_SNAP) return target;
            return c + err * Mathf.Clamp01(dt * CLOCK_GAIN);
        }

        /// <summary>Exponential decay multiplier for dt with time constant tau (0 = immediate).</summary>
        public static float DecayFactor(float dt, float tau)
        {
            if (tau <= 0f) return 0f;
            return Mathf.Exp(-dt / tau);
        }
    }
}
