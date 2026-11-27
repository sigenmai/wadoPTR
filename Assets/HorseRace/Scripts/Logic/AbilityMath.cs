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
            if (lv == 1) return 25f;
            if (lv == 2) return 35f;
            return 45f;
        }

        public static int WallCount(int lv) { return Clamp(lv); }

        public static int BallCount(int lv) { return Clamp(lv); }

        /// <summary>First claim wins: returns holder if already held (>=0), else claimant.</summary>
        public static int RelicArbiter(int holder, int claimant)
        {
            if (holder >= 0) return holder;
            return claimant;
        }
    }
}
