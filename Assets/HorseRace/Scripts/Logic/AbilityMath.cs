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
        public const float DUST_SLOW_FACTOR = 0.85f;   // system-r2-09：後ろについてくる砂煙で何度も当たるので 1 回は弱く
        /// <summary>同じ相手に砂煙が当たり直すまでの秒数（＝ 1 回の減速の長さ）。</summary>
        public const float DUST_HIT_SECONDS = 1.5f;
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

        /// <summary>砂煙の長さ（秒）：3/4/5（system-r2-09：発動者の後ろについてくるので短く）。</summary>
        public static float DustDuration(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 3f;
            if (lv == 2) return 4f;
            return 5f;
        }

        /// <summary>砂煙に当たる回数：後ろにつき続けた相手は DUST_HIT_SECONDS ごと（0 秒・1.5 秒・3 秒…）。</summary>
        public static int DustHitsWhileTailing(int lv)
        {
            float d = DustDuration(lv);
            int n = 0;
            for (float t = 0f; t < d; t += DUST_HIT_SECONDS) n++;
            return n;
        }

        /// <summary>
        /// 速さ speed で後ろについてくる相手が砂煙で失う距離（m）：当たるたびに ×0.85 を 1.5 秒、さらに砂煙の中は風よけ（ドラフト）が消える。
        /// </summary>
        public static float DustTrailLossMeters(int lv, float speed)
        {
            float perHit = speed * (1f - DUST_SLOW_FACTOR) * DUST_HIT_SECONDS;
            return DustHitsWhileTailing(lv) * perHit + MountMath.DRAFT_SPEED_BONUS * DustDuration(lv);
        }

        /// <summary>鉄球が当たった相手が失う距離（m、よろけは含めない）。</summary>
        public static float BallHitCostMeters(float speed)
        {
            return speed * (1f - BALL_HIT_FACTOR) * BALL_HIT_SECONDS;
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

        // ---- 岩壁で道をふさぎきらない（system-r2-10）----
        /// <summary>壁の横に必ず残す隙間（m）：6m か、道の空いている幅の 30% の大きいほう。</summary>
        public const float WALL_MIN_GAP = 6f;
        public const float WALL_GAP_FRACTION = 0.3f;
        /// <summary>これより狭い壁は立てない（細すぎる壁は読めない）。</summary>
        public const float WALL_MIN_SEGMENT = 3f;

        public static float WallGap(float freeWidth)
        {
            return Mathf.Max(WALL_MIN_GAP, WALL_GAP_FRACTION * freeWidth);
        }

        /// <summary>空いている幅 freeWidth（左右の障害物・見えない柵まで）で立ててよい壁の全長（m）。Lv の全長より長くはしない。</summary>
        public static float WallSpanFor(float freeWidth, int lv)
        {
            float full = WallCount(lv) * WallWidth(lv);
            float room = freeWidth - WallGap(freeWidth);
            if (room < 0f) room = 0f;
            return Mathf.Min(full, room);
        }

        /// <summary>全長 span に入る壁の枚数（外側から減らす）。1 枚も入らないが WALL_MIN_SEGMENT 以上あれば 1 枚（幅は span）。</summary>
        public static int WallSegmentsFor(float span, int lv)
        {
            float w = WallWidth(lv);
            int n = Mathf.Min(WallCount(lv), Mathf.FloorToInt(span / w + 0.001f));
            if (n <= 0 && span >= WALL_MIN_SEGMENT) n = 1;
            return n;
        }

        /// <summary>
        /// 壁の中心の横のずれ（m、右が正）。left / right = 狙った点から左右の障害物までの距離、span = 壁の全長。
        /// どちらかの横に WallGap 以上の隙間がひと続きで残るように寄せる（壁は道の外へはみ出さない）。
        /// </summary>
        public static float WallOffsetFor(float left, float right, float span)
        {
            float gap = WallGap(left + right);
            float o = 0f;
            float half = span * 0.5f;
            float lg = left - half, rg = right - half;
            if (lg < gap && rg < gap)
            {
                if (lg >= rg) o = gap - left + half;     // 左に隙間を作る：壁を右へ
                else o = right - half - gap;             // 右に隙間を作る：壁を左へ
            }
            float maxO = right - half, minO = half - left;
            if (minO <= maxO) o = Mathf.Clamp(o, minO, maxO);
            return o;
        }

        // ---- 開幕の解禁を順番に（system-r2-11）----
        /// <summary>能力が初めて使えるようになる号砲からの秒数：8 秒 + レーン × 0.75 秒（前のレーンから）、最大 14 秒。</summary>
        public const float OPENING_STEP = 0.75f;
        public const float OPENING_MAX = 14f;

        public static float OpeningReady(int lane, int slots)
        {
            if (lane < 0) lane = 0;
            if (slots > 0 && lane >= slots) lane = slots - 1;
            float t = RaceMath.START_GRACE_SECONDS + OPENING_STEP * lane;
            return t > OPENING_MAX ? OPENING_MAX : t;
        }

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
