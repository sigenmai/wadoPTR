using UnityEngine;

namespace HorseRace
{
    /// <summary>Revolver rules. Static methods only (UdonSharp-safe).</summary>
    public class RevolverMath : UdonSharp.UdonSharpBehaviour
    {
        public const int AMMO_PER_STAGE = 6;
        /// <summary>開幕（号砲から OPENING_SECONDS 秒まで）に撃てる弾の数（system-r2-11：猶予が明けた瞬間の一斉射撃を防ぐ）。</summary>
        public const int OPENING_AMMO = 1;
        public const float OPENING_SECONDS = 15f;

        /// <summary>号砲から sinceGun 秒、残り ammo 発で、開幕の 1 発の制限の中で撃てるか（弾の有無は CanShoot）。</summary>
        public static bool OpeningAllows(int ammo, float sinceGun)
        {
            if (sinceGun >= OPENING_SECONDS) return true;
            return AMMO_PER_STAGE - ammo < OPENING_AMMO;
        }
        // system-r1-05：撃つ方が得（外しの損 ≒ 1.7m、当てた相手の損 ≒ 7.8m @14 m/s）
        public const float AIM_FACTOR = 0.95f;
        public const float MISS_FACTOR = 0.92f;
        public const float MISS_SECONDS = 1.5f;
        public const float HIT_FACTOR = 0.6f;
        public const float HIT_SECONDS = 1.4f;
        /// <summary>馬上の照準補助：撃つ人が乗っていて AIM_ASSIST_MIN_SPEED より速いとき、当たり判定の半径に足す（m）。</summary>
        public const float AIM_ASSIST_RADIUS = 0.3f;
        public const float AIM_ASSIST_MIN_SPEED = 10f;
        public const int HITS_TO_DROP = 3;

        /// <summary>外したときに自分が失う距離（m）。</summary>
        public static float MissCostMeters(float speed)
        {
            return speed * (1f - MISS_FACTOR) * MISS_SECONDS;
        }

        /// <summary>当てたときに相手が失う距離（m、よろけは含めない）。</summary>
        public static float HitCostMeters(float speed)
        {
            return speed * (1f - HIT_FACTOR) * HIT_SECONDS;
        }

        /// <summary>当たり判定の半径（照準補助込み）。</summary>
        public static float HitRadius(float baseRadius, bool mounted, float shooterSpeed)
        {
            if (mounted && shooterSpeed > AIM_ASSIST_MIN_SPEED) return baseRadius + AIM_ASSIST_RADIUS;
            return baseRadius;
        }

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
