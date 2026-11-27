using UnityEngine;

namespace HorseRace
{
    /// <summary>Timed speed multipliers in fixed-size slot arrays (typically 8). Slot is empty when remaining &lt;= 0.
    /// Add/Tick mutate the arrays in place.</summary>
    public class SpeedMod : UdonSharp.UdonSharpBehaviour
    {
        public const int SLOTS = 8;
        /// <summary>減速の合計の下限（system-r1-06：集中攻撃されても ×0.55 より遅くならない）。</summary>
        public const float MIN_COMBINED = 0.55f;
        /// <summary>2 つ目以降の減速 1 つにつき掛ける倍率（いちばん強い減速 × 0.85^(残りの数)）。</summary>
        public const float EXTRA_DEBUFF = 0.85f;

        /// <summary>
        /// 有効な倍率の合計。加速（&gt; 1）は掛け算、減速（&lt; 1）は「いちばん強いもの × 0.85^(ほかの減速の数)」。結果の下限 MIN_COMBINED。
        /// </summary>
        public static float Combined(float[] factors, float[] remaining)
        {
            float buff = 1f;
            float weakest = 1f;
            int debuffs = 0;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] <= 0f) continue;
                float f = factors[i];
                if (f >= 1f) { buff *= f; continue; }
                debuffs++;
                if (f < weakest) weakest = f;
            }
            float p = weakest;
            for (int k = 1; k < debuffs; k++) p *= EXTRA_DEBUFF;
            p *= buff;
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
