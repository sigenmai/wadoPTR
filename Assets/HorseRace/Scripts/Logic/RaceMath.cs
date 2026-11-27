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

        public static bool NeedsReset(float heightAboveTerrain, float distFromCourse, float secondsOffCourse)
        {
            if (heightAboveTerrain < -2f) return true;
            return distFromCourse > 60f && secondsOffCourse >= 3f;
        }
    }
}
