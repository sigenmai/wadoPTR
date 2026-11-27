using UnityEngine;

namespace HorseRace
{
    /// <summary>Revolver rules. Static methods only (UdonSharp-safe).</summary>
    public class RevolverMath : UdonSharp.UdonSharpBehaviour
    {
        public const int AMMO_PER_STAGE = 6;
        public const float AIM_FACTOR = 0.9f;
        public const float MISS_FACTOR = 0.8f;
        public const float MISS_SECONDS = 3f;
        public const float HIT_FACTOR = 0.6f;
        public const float HIT_SECONDS = 1f;
        public const int HITS_TO_DROP = 3;

        public static int AmmoAfterShot(int ammo)
        {
            if (ammo <= 0) return 0;
            return ammo - 1;
        }

        public static bool CanShoot(int ammo, int raceState)
        {
            return raceState == RaceMath.STATE_RACING && ammo > 0;
        }

        /// <summary>Returns next hit count; wraps to 0 on reaching HITS_TO_DROP.</summary>
        public static int RegisterHit(int hitsOnTarget)
        {
            int n = hitsOnTarget + 1;
            if (n >= HITS_TO_DROP) return 0;
            return n;
        }

        public static bool DropsRelic(int hitsBefore)
        {
            return hitsBefore + 1 >= HITS_TO_DROP;
        }

        public static int LevelAfterDrop(int lv)
        {
            return lv - 1 > 1 ? lv - 1 : 1;
        }

        public static bool SpawnsDroppedRelic(int lv)
        {
            return lv > 1;
        }
    }
}
