using UnityEngine;

namespace HorseRace
{
    /// <summary>Relic ability rules. Static methods only (UdonSharp-safe). Levels are 1..3; out-of-range is clamped.</summary>
    public class AbilityMath : UdonSharp.UdonSharpBehaviour
    {
        public const int ABILITY_DUST = 0;
        public const int ABILITY_LASSO = 1;
        public const int ABILITY_WALL = 2;
        public const int ABILITY_BALL = 3;
        // Phase 2 (ids only; no rules yet)
        public const int ABILITY_DINO = 4;
        public const int ABILITY_BOMB = 5;
        public const int ABILITY_MIRAGE = 6;

        // Secondary tuning values
        public const float DUST_SLOW_FACTOR = 0.8f;
        public const float LASSO_SELF_BONUS_MPS = 5f;   // 最高速 18 m/s に合わせて 4 → 5（Lv2 6.5、Lv3 8）
        public const float LASSO_SELF_SECONDS = 2f;
        public const float LASSO_TARGET_FACTOR = 0.7f;
        public const float LASSO_TARGET_SECONDS = 1f;
        public const float WALL_WIDTH = 6f;
        public const float WALL_AHEAD = 28f;   // 18 m/s で約 1.5 秒先（旧 20m ＝ 13 m/s で約 1.5 秒先）
        public const float WALL_SECONDS = 6f;
        public const float BALL_HIT_FACTOR = 0.6f;
        public const float BALL_HIT_SECONDS = 1.2f;
        public const int BALL_BOUNCES = 3;
        public const float BALL_CURVE_DEG_PER_M_PER_ROLL_DEG = 0.5f;

        static int Clamp(int lv)
        {
            if (lv < 1) return 1;
            if (lv > 3) return 3;
            return lv;
        }

        public static int LevelAfterRelic(int lv)
        {
            return Clamp(lv + 1);
        }

        /// <summary>Cooldown seconds. Unknown ids (incl. Phase 2) return 20f.</summary>
        public static float Cooldown(int a)
        {
            if (a == ABILITY_DUST) return 18f;
            if (a == ABILITY_LASSO) return 22f;
            if (a == ABILITY_WALL) return 25f;
            if (a == ABILITY_BALL) return 15f;
            return 20f;
        }

        public static float DustRadius(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 4f;
            if (lv == 2) return 6f;
            return 8f;
        }

        public static float DustDuration(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 4f;
            if (lv == 2) return 6f;
            return 8f;
        }

        public static float LassoRange(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 32f;
            if (lv == 2) return 45f;
            return 58f;
        }

        /// <summary>Lasso self pull bonus (m/s): 4/5/6. Lv1 equals LASSO_SELF_BONUS_MPS.</summary>
        public static float LassoPullBonus(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return LASSO_SELF_BONUS_MPS;
            if (lv == 2) return 6.5f;
            return 8f;
        }

        public static int WallCount(int lv) { return Clamp(lv); }

        /// <summary>Width of each wall segment (m): 6/7.5/9. Lv1 equals WALL_WIDTH.</summary>
        public static float WallWidth(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return WALL_WIDTH;
            if (lv == 2) return 7.5f;
            return 9f;
        }

        public static int BallCount(int lv) { return Clamp(lv); }

        /// <summary>
        /// 順位で決まる遺物の価値（system-r1-04、順位ベースの逆転）：1 位 0（拾えない・暗く見える）、上半分 +1、下半分 +2。
        /// 1 人だけ・順位がまだ無い（rank &lt;= 0）ときは +1。
        /// </summary>
        public static int RelicGain(int rank, int activeCount)
        {
            if (activeCount <= 1 || rank <= 0) return 1;
            if (rank == 1) return 0;
            if (rank * 2 <= activeCount) return 1;
            return 2;
        }

        /// <summary>遺物 gain 個ぶん Lv を上げる（上限 3、gain 0 なら変えない）。</summary>
        public static int LevelAfterRelicGain(int lv, int gain)
        {
            if (gain <= 0) return Clamp(lv);
            return Clamp(lv + gain);
        }

        /// <summary>First claim wins: returns holder if already held (>=0), else claimant.</summary>
        public static int RelicArbiter(int holder, int claimant)
        {
            if (holder >= 0) return holder;
            return claimant;
        }
    }
}
