namespace HorseRace
{
    /// <summary>Spectator camera target selection. Static only (UdonSharp-safe).</summary>
    public class DirectorMath : UdonSharp.UdonSharpBehaviour
    {
        public const float MIN_SHOT = 5f;
        public const float MAX_SHOT = 15f;
        public const float BATTLE_GAP = 8f;
        public const float HIT_SHOW = 3f;

        /// <summary>order: slots leader first. gapToAhead[k] = metres between order[k] and order[k-1] (index 0 unused).</summary>
        public static int ChooseTarget(int current, float shotTime, int[] order, float[] gapToAhead, int lastHitSlot, float sinceHit)
        {
            int n = order.Length;
            if (n == 0) return -1;
            if (shotTime < MIN_SHOT) return current;

            int target = -1;
            int battleK = -1;

            if (lastHitSlot >= 0 && sinceHit < HIT_SHOW)
            {
                for (int i = 0; i < n; i++)
                {
                    if (order[i] == lastHitSlot) { target = lastHitSlot; break; }
                }
            }

            if (target < 0)
            {
                for (int k = 1; k < n; k++)
                {
                    if (gapToAhead[k] <= BATTLE_GAP) { battleK = k; target = order[k]; break; }
                }
            }

            if (target < 0) target = order[0];

            if (shotTime >= MAX_SHOT && target == current)
            {
                if (battleK >= 1) target = order[battleK - 1];
                else if (n > 1) target = order[1];
            }
            return target;
        }
    }
}
