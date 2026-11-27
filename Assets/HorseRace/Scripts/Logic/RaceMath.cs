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

        /// <summary>1 人だけのエントリー（タイムアタック、system-r2-04）：30 秒後に出走、その人が乗って待っていれば 10 秒後まで縮める。</summary>
        public const float LOBBY_SOLO_SECONDS = 30f;
        public const float LOBBY_SOLO_READY_SECONDS = 10f;

        /// <summary>
        /// ロビーの出走の締め切り（サーバー時刻、-1 = なし）。
        /// 2 人以上：60 秒後に決め、全員が乗った・ワールドの全員がエントリーしたら 15 秒後まで縮める。
        /// 1 人（system-r2-04：マスターが不在でもひとりで走れる）：30 秒後、乗って待っていれば 10 秒後まで縮める。
        /// 0 人なら -1。縮めるだけで延ばさない（もう一戦の 30 秒（RematchDeadline）もそのまま）。
        /// </summary>
        public static double LobbyDeadline(double now, int entrants, int seated, int players, double current)
        {
            if (entrants < 1) return -1.0;
            bool solo = entrants == 1;
            double d = current;
            if (d < 0.0) d = now + (solo ? LOBBY_SOLO_SECONDS : LOBBY_WAIT_SECONDS);
            if (seated >= entrants || entrants >= players)
            {
                double ready = now + (solo ? LOBBY_SOLO_READY_SECONDS : LOBBY_READY_SECONDS);
                if (ready < d) d = ready;
            }
            return d;
        }

        /// <summary>
        /// prevEntrants = 前の判定のときのエントリー数。ひとりの時計（30 秒・10 秒）が動いている間に 2 人目がサインしたら、
        /// 2 人以上の時計（60 秒・全員乗ったら 15 秒）で決め直す（友だちを待つ時間を残す）。それ以外は LobbyDeadline と同じ。
        /// </summary>
        public static double LobbyDeadline(double now, int entrants, int seated, int players, double current, int prevEntrants)
        {
            if (prevEntrants == 1 && entrants >= 2) current = -1.0;
            return LobbyDeadline(now, entrants, seated, players, current);
        }

        /// <summary>もう一戦（system-r2-06）：総合結果の後、同じ顔ぶれのまま 30 秒（LOBBY_READY_SECONDS × 2）で次のレースへ。</summary>
        public static double RematchDeadline(double now)
        {
            return now + LOBBY_READY_SECONDS * 2f;
        }

        // ---- 出走しない人（system-r2-05）----
        /// <summary>号砲からこの秒数たっても最初の CP に届かず止まっている人は「不在」（次の合間で枠を空ける）。</summary>
        public const float NO_START_SECONDS = 20f;
        public const float NO_START_SPEED = 1f;

        /// <summary>判定するのは号砲の 20 秒後から 2 秒だけ（後でスタートへ戻された人を「不在」にしない）。</summary>
        public const float NO_START_WINDOW = 2f;

        /// <summary>号砲から sinceGun 秒、CP（syncCp）と速さから「出走しなかった（AFK）」か（20〜22 秒の間だけ判定）。</summary>
        public static bool IsNoStart(int cp, float speed, double sinceGun)
        {
            if (sinceGun < NO_START_SECONDS || sinceGun >= NO_START_SECONDS + NO_START_WINDOW) return false;
            return cp <= 0 && speed < NO_START_SPEED;
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

        // ---- コース外・立ち往生・やり直し（system-r2-03）----
        /// <summary>コースの線からこれより離れて 3 秒で戻す（柵は 28m なので 60m では起きなかった）。</summary>
        public const float OFF_COURSE_M = 40f;
        public const float OFF_COURSE_SECONDS = 3f;
        /// <summary>駆歩以上を指示しているのに 1 m/s 未満のまま 4 秒（岩に挟まった・段の溝）で自動で戻す。</summary>
        public const float STUCK_SPEED = 1f;
        public const int STUCK_CRUISE = 2;
        public const float STUCK_SECONDS = 4f;
        /// <summary>やり直し（デスクトップ R 長押し・VR スティック長押し）の長押し秒数と、戻した後のよろけ秒数（自力で戻るより速くならない）。</summary>
        public const float MANUAL_RESET_HOLD = 1.2f;
        public const float MANUAL_RESET_PENALTY = 2f;

        public static bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse)
        {
            return NeedsReset(heightAboveTerrain, distFromCourse, secondsOffCourse, 0f);
        }

        /// <summary>地形の下・コース外 3 秒・立ち往生 4 秒（secondsStuck は StuckNow が続いた秒数）のどれかで戻す。</summary>
        public static bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse, float secondsStuck)
        {
            if (heightAboveTerrain < -2f) return true;
            if (secondsStuck >= STUCK_SECONDS) return true;
            return distFromCourse > OFF_COURSE_M && secondsOffCourse >= OFF_COURSE_SECONDS;
        }

        /// <summary>このフレーム立ち往生しているか：駆歩以上（cruiseLevel ≥ 2）を指示していて 1 m/s 未満。</summary>
        public static bool StuckNow(float speed, int cruiseLevel)
        {
            return speed < STUCK_SPEED && cruiseLevel >= STUCK_CRUISE;
        }

        // ---- CP を 1 つ飛ばしてもよい（system-r2-02）----
        /// <summary>柵沿いの外の線で CP の箱の横を抜けても戻らせない：次の CP か、その次（1 つ飛ばし）まで受け付ける。</summary>
        public const int MAX_CP_SKIP = 1;

        public static bool AcceptCheckpoint(int lastCp, int cpIndex)
        {
            return cpIndex > lastCp && cpIndex <= lastCp + 1 + MAX_CP_SKIP;
        }

        /// <summary>首の札の「関門を通り過ぎた」：受け付けられる範囲より先の CP に入った。</summary>
        public static bool MissedCheckpoint(int lastCp, int cpIndex)
        {
            return cpIndex > lastCp + 1 + MAX_CP_SKIP;
        }
    }
}
