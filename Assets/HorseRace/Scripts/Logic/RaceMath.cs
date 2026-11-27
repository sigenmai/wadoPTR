namespace HorseRace
{
    /// <summary>Pure race rules: ranking, points, stage end, late join, reset. Static only (UdonSharp-safe).</summary>
    public class RaceMath : UdonSharp.UdonSharpBehaviour
    {
        public const float STAGE_HARD_LIMIT = 330f;
        public const float AFTER_FIRST_FINISH = 60f;

        public const int STATE_LOBBY = 0;
        public const int STATE_COUNTDOWN = 1;
        public const int STATE_RACING = 2;
        public const int STATE_STAGE_RESULT = 3;
        public const int STATE_FINAL_RESULT = 4;

        /// <summary>firstFinishAt is -1 when nobody has finished.</summary>
        public static bool ShouldEndStage(double serverNow, double stageStart, double firstFinishAt)
        {
            if (serverNow - stageStart >= STAGE_HARD_LIMIT) return true;
            if (firstFinishAt >= 0.0 && serverNow - firstFinishAt >= AFTER_FIRST_FINISH) return true;
            return false;
        }

        // true if slot a ranks strictly ahead of slot b
        private static bool Ahead(int a, int b, int[] cp, float[] dist, double[] finishAt)
        {
            bool fa = finishAt[a] >= 0.0;
            bool fb = finishAt[b] >= 0.0;
            if (fa != fb) return fa;
            if (fa) return finishAt[a] < finishAt[b];
            if (cp[a] != cp[b]) return cp[a] > cp[b];
            return dist[a] < dist[b];
        }

        /// <summary>Returns active slot indices, best first. Ties keep lower slot first.</summary>
        public static int[] ComputeOrder(int[] cpReached, float[] distToNextCp, double[] finishAt, bool[] active)
        {
            int n = 0;
            for (int i = 0; i < active.Length; i++) if (active[i]) n++;
            int[] order = new int[n];
            int count = 0;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i]) continue;
                int j = count;
                while (j > 0 && Ahead(i, order[j - 1], cpReached, distToNextCp, finishAt))
                {
                    order[j] = order[j - 1];
                    j--;
                }
                order[j] = i;
                count++;
            }
            return order;
        }

        public static int PointsForPlace(int place)
        {
            if (place == 0) return 10;
            if (place == 1) return 8;
            if (place == 2) return 6;
            if (place == 3) return 5;
            if (place == 4) return 4;
            if (place == 5) return 3;
            if (place == 6) return 2;
            if (place == 7) return 1;
            return 0;
        }

        /// <summary>最終ステージ（0 始まりで 2）は点が 1.5 倍（端数は切り上げ）：15/12/9/8/6/5/3/2（system-r1-21）。</summary>
        public const int FINAL_STAGE = 2;

        public static int PointsForPlace(int place, int stage)
        {
            int b = PointsForPlace(place);
            if (stage != FINAL_STAGE) return b;
            return (b * 3 + 1) / 2;
        }

        // ---- 開幕の猶予（system-r1-07）----
        /// <summary>号砲から何秒間、能力とリボルバーを使えないか。</summary>
        public const float START_GRACE_SECONDS = 8f;

        public static bool InStartGrace(double serverNow, double stageStartServer)
        {
            return serverNow - stageStartServer < START_GRACE_SECONDS;
        }

        /// <summary>猶予の残り秒数（0 = もう使える）。</summary>
        public static float StartGraceLeft(double serverNow, double stageStartServer)
        {
            double left = stageStartServer + START_GRACE_SECONDS - serverNow;
            if (left < 0.0) return 0f;
            return (float)left;
        }

        // ---- ロビーの出走時計（system-r1-09）----
        public const float LOBBY_WAIT_SECONDS = 60f;
        public const float LOBBY_READY_SECONDS = 15f;
        /// <summary>締め切りまでこれ未満になったら、エントリーした人なら誰でも号砲を撃てる。</summary>
        public const float LOBBY_ANYONE_SECONDS = 30f;

        /// <summary>
        /// ロビーの出走の締め切り（サーバー時刻、-1 = なし）。エントリー 2 人以上で 60 秒後に決め、全員が乗った・ワールドの全員がエントリーしたら 15 秒後まで縮める。
        /// 2 人未満なら -1。縮めるだけで延ばさない。
        /// </summary>
        public static double LobbyDeadline(double now, int entrants, int seated, int players, double current)
        {
            if (entrants < 2) return -1.0;
            double d = current;
            if (d < 0.0) d = now + LOBBY_WAIT_SECONDS;
            if (seated >= entrants || entrants >= players)
            {
                double ready = now + LOBBY_READY_SECONDS;
                if (ready < d) d = ready;
            }
            return d;
        }

        /// <summary>号砲（スタート台の拳銃）を撃てるか：マスター、または締め切りまで 30 秒未満のときのエントリー者。</summary>
        public static bool CanRingStart(bool isMaster, bool isEntrant, double now, double deadline)
        {
            if (isMaster) return true;
            return isEntrant && deadline >= 0.0 && deadline - now < LOBBY_ANYONE_SECONDS;
        }

        // ---- 総合順位の同点（system-r1-12）----
        /// <summary>
        /// 総合で a が b より上か：合計点 → 2 人とも走った最後のステージの着順（小さいほうが上）→ 1 着の数。
        /// stagePlace は index = stage * slots + slot（1 始まり、0 = 走っていない）、slots = totals.Length。完全に同じなら false。
        /// </summary>
        public static bool OverallAhead(int a, int b, int[] totals, int[] stagePlace, int stageCount)
        {
            if (totals[a] != totals[b]) return totals[a] > totals[b];
            int slots = totals.Length;
            for (int st = stageCount - 1; st >= 0; st--)
            {
                int pa = stagePlace[st * slots + a];
                int pb = stagePlace[st * slots + b];
                if (pa > 0 && pb > 0)
                {
                    if (pa != pb) return pa < pb;
                    break;
                }
            }
            int wa = 0;
            int wb = 0;
            for (int st = 0; st < stageCount; st++)
            {
                if (stagePlace[st * slots + a] == 1) wa++;
                if (stagePlace[st * slots + b] == 1) wb++;
            }
            return wa > wb;
        }

        /// <summary>active なスロットの総合順（上から）。同点の決め方は OverallAhead、完全に同じならスロット番号の小さい順。</summary>
        public static int[] OverallOrder(int[] totals, int[] stagePlace, bool[] active, int stageCount)
        {
            int n = 0;
            for (int i = 0; i < active.Length; i++) if (active[i]) n++;
            int[] result = new int[n];
            int count = 0;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i]) continue;
                int j = count;
                while (j > 0 && OverallAhead(i, result[j - 1], totals, stagePlace, stageCount))
                {
                    result[j] = result[j - 1];
                    j--;
                }
                result[j] = i;
                count++;
            }
            return result;
        }

        // ---- スタートの並び（system-r1-14）----
        /// <summary>
        /// スロットごとのスタートのレーン（長さ slots）。overallOrder = 走るスロット（総合順、上から）。
        /// 第 1 ステージ（stage 0）は seed で混ぜる、第 2・3 ステージは総合の逆順（最下位がレーン 0、首位がいちばん外）。
        /// 走らないスロットには残りのレーンをスロット番号の順に入れる（重ならない）。
        /// </summary>
        public static int[] GridOrder(int[] overallOrder, int stage, int seed, int slots)
        {
            int n = overallOrder.Length;
            int[] seq = new int[n];
            for (int i = 0; i < n; i++) seq[i] = overallOrder[n - 1 - i];
            if (stage == 0)
            {
                for (int i = 0; i < n; i++) seq[i] = overallOrder[i];
                int s = (seed < 0 ? -seed : seed) % 65536;
                for (int i = n - 1; i > 0; i--)
                {
                    s = (s * 1103 + 12345) % 65536;
                    int j = s % (i + 1);
                    int t = seq[i];
                    seq[i] = seq[j];
                    seq[j] = t;
                }
            }
            int[] lane = new int[slots];
            bool[] used = new bool[slots];
            for (int i = 0; i < slots; i++) lane[i] = -1;
            for (int i = 0; i < n; i++)
            {
                int slot = seq[i];
                if (slot < 0 || slot >= slots) continue;
                lane[slot] = i;
                used[i] = true;
            }
            int next = 0;
            for (int slot = 0; slot < slots; slot++)
            {
                if (lane[slot] >= 0) continue;
                while (next < slots && used[next]) next++;
                lane[slot] = next < slots ? next : slot;
                if (next < slots) used[next] = true;
            }
            return lane;
        }

        // ---- 逆走（system-r1-17）----
        public const float WRONG_WAY_DOT = -0.3f;
        public const float WRONG_WAY_MIN_SPEED = 4f;
        public const float WRONG_WAY_SECONDS = 1.5f;

        /// <summary>次の CP への向きと進む向きの内積・速さから、逆走の兆し（このフレーム）か。</summary>
        public static bool WrongWayNow(float dotFwdToCp, float speed)
        {
            return dotFwdToCp < WRONG_WAY_DOT && speed > WRONG_WAY_MIN_SPEED;
        }

        /// <summary>Seconds until the stage ends (the earlier of the hard limit and 60 s after the first finish). Never negative.</summary>
        public static float RemainingSeconds(double serverNow, double stageStart, double firstFinishAt)
        {
            double end = stageStart + STAGE_HARD_LIMIT;
            if (firstFinishAt >= 0.0)
            {
                double afterFirst = firstFinishAt + AFTER_FIRST_FINISH;
                if (afterFirst < end) end = afterFirst;
            }
            double left = end - serverNow;
            if (left < 0.0) return 0f;
            return (float)left;
        }

        /// <summary>"m:ss", rounding up so the clock reads 0:00 only when time is over. Negative shows 0:00.</summary>
        public static string FormatClock(float seconds)
        {
            int total = seconds > 0f ? UnityEngine.Mathf.CeilToInt(seconds) : 0;
            int m = total / 60;
            int s = total % 60;
            return m.ToString() + ":" + (s < 10 ? "0" : "") + s.ToString();
        }

        public static bool CanEnterStage(int raceState)
        {
            return raceState == STATE_LOBBY || raceState == STATE_STAGE_RESULT;
        }

        public const int ENTRY_REJECT = 0;
        /// <summary>Take a slot right away (lobby, or between stages when another stage follows).</summary>
        public const int ENTRY_NOW = 1;
        /// <summary>Mid-stage sign-up: remember it and give a slot when the stage ends (spectate until then).</summary>
        public const int ENTRY_QUEUE = 2;

        /// <summary>How a sign-up by a player without a slot is handled. stage is 0-based.</summary>
        public static int EntryMode(int raceState, int stage, int stageCount)
        {
            if (raceState == STATE_LOBBY) return ENTRY_NOW;
            bool hasNext = stage + 1 < stageCount;
            if (!hasNext) return ENTRY_REJECT;
            if (raceState == STATE_STAGE_RESULT) return ENTRY_NOW;
            if (raceState == STATE_COUNTDOWN || raceState == STATE_RACING) return ENTRY_QUEUE;
            return ENTRY_REJECT;
        }

        /// <summary>Index in the pending list for playerId: its existing entry, else the first free (-1) one, else -1.</summary>
        public static int PendingIndexFor(int[] pendingPlayerId, int playerId)
        {
            if (playerId < 0) return -1;
            for (int i = 0; i < pendingPlayerId.Length; i++) if (pendingPlayerId[i] == playerId) return i;
            for (int i = 0; i < pendingPlayerId.Length; i++) if (pendingPlayerId[i] < 0) return i;
            return -1;
        }

        /// <summary>True when at least one slot is active and every active slot has finished (finishAt >= 0).</summary>
        public static bool AllFinished(double[] finishAt, bool[] active)
        {
            int n = 0;
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i]) continue;
                n++;
                if (finishAt[i] < 0.0) return false;
            }
            return n > 0;
        }

        public static bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse)
        {
            if (heightAboveTerrain < -2f) return true;
            return distFromCourse > 60f && secondsOffCourse >= 3f;
        }
    }
}
