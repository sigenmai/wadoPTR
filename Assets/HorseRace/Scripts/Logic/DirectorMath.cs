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

        /// <summary>
        /// 着順（1 始まり）。finishAt が stageStart 以上の有効スロットのうち何番目にゴールしたか（同時刻はスロット番号の小さい順）。
        /// slot がまだゴールしていなければ 0。
        /// </summary>
        public static int FinishPlace(double[] finishAt, bool[] active, int slot, double stageStart)
        {
            if (slot < 0 || slot >= finishAt.Length || slot >= active.Length) return 0;
            if (!active[slot]) return 0;
            double mine = finishAt[slot];
            if (mine < 0.0 || mine < stageStart) return 0;
            int place = 1;
            for (int i = 0; i < finishAt.Length && i < active.Length; i++)
            {
                if (i == slot || !active[i]) continue;
                double f = finishAt[i];
                if (f < 0.0 || f < stageStart) continue;
                if (f < mine || (f == mine && i < slot)) place++;
            }
            return place;
        }

        /// <summary>order の各位置について前の馬との水平距離（m）。index 0 は 0。x / z はスロット番号で引く。</summary>
        public static float[] GapsXZ(int[] order, float[] x, float[] z)
        {
            int n = order.Length;
            float[] gaps = new float[n];
            for (int k = 1; k < n; k++)
            {
                int a = order[k];
                int b = order[k - 1];
                float dx = x[a] - x[b];
                float dz = z[a] - z[b];
                gaps[k] = UnityEngine.Mathf.Sqrt(dx * dx + dz * dz);
            }
            return gaps;
        }

        /// <summary>rank（1 始まり）位のスロット。いなければ -1。</summary>
        public static int SlotAtRank(int[] order, int rank)
        {
            if (rank < 1 || rank > order.Length) return -1;
            return order[rank - 1];
        }
    }
}
