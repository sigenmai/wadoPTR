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

        /// <summary>Seconds after a relic drop during which further 3rd hits (from other shooters) cost no level.</summary>
        public const float DROP_COOLDOWN = 1f;

        /// <summary>
        /// Target-side running hit total from one shooter this stage. hint = the shooter's own cumulative count
        /// (1, 2, 3, 4 …; 0 = none). A hint only ever raises the total, so a late (reordered) hit adds nothing.
        /// </summary>
        public static int TotalAfterHit(int total, int hint)
        {
            if (hint <= 0) return total + 1;
            return hint > total ? hint : total;
        }

        /// <summary>Relic drops earned by a hit total (one per HITS_TO_DROP hits).</summary>
        public static int DropsFor(int total)
        {
            return total / HITS_TO_DROP;
        }

        /// <summary>True when the total earns a drop that has not been given yet.</summary>
        public static bool DropDue(int total, int dropsGiven)
        {
            return DropsFor(total) > dropsGiven;
        }

        /// <summary>False within DROP_COOLDOWN of the previous drop on the same target (two shooters' 3rd hits at once).</summary>
        public static bool DropAllowed(float now, float lastDropAt)
        {
            return now - lastDropAt >= DROP_COOLDOWN;
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
