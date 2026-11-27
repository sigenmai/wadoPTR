using UnityEngine;

namespace HorseRace
{
    /// <summary>Timed speed multipliers in fixed-size slot arrays (typically 8). Slot is empty when remaining &lt;= 0.
    /// Add/Tick mutate the arrays in place.</summary>
    public class SpeedMod : UdonSharp.UdonSharpBehaviour
    {
        public const int SLOTS = 8;
        public const float MIN_COMBINED = 0.4f;

        public static float Combined(float[] factors, float[] remaining)
        {
            float p = 1f;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0f) p *= factors[i];
            }
            if (p < MIN_COMBINED) p = MIN_COMBINED;
            return p;
        }

        public static void Add(float[] factors, float[] remaining, float factor, float seconds)
        {
            int slot = -1;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] <= 0f) { slot = i; break; }
            }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < remaining.Length; i++)
                {
                    if (remaining[i] < remaining[slot]) slot = i;
                }
            }
            factors[slot] = factor;
            remaining[slot] = seconds;
        }

        public static void Tick(float[] remaining, float dt)
        {
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] > 0f) remaining[i] -= dt;
            }
        }
    }
}
