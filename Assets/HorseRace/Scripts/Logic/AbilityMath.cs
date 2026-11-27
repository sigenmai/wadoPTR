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
        // Phase 2（DINO はまだ番号だけ。BOMB / MIRAGE は system-r3-10/11 で実装）
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
        /// <summary>ui-r3-08：鉄球の狙いの印が赤くなる距離（m、26 m/s の初速で約 1.3 秒・はねる前に届く）。</summary>
        public const float BALL_AIM_RANGE = 35f;
        /// <summary>ui-r3-08：狙いの印の当たり判定を飛ばす間隔（秒、10 Hz）。</summary>
        public const float AIM_PROBE_INTERVAL = 0.1f;
        /// <summary>ui-r3-08：届く距離のこの倍まで探して、外なら灰色で見せる。</summary>
        public const float AIM_PROBE_REACH = 1.6f;
        /// <summary>ui-r3-08：狙いの印の高さ（m、GoalPresenter の着順の札と同じ）と回る速さ（度/秒）。</summary>
        public const float AIM_MARKER_HEIGHT = 2.9f;
        public const float AIM_MARKER_SPIN_DEG = 180f;

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
            if (a == ABILITY_BOMB) return 20f;
            if (a == ABILITY_MIRAGE) return 20f;
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

        // =====================================================================
        // round 3（system-r3-04/05/10/11/12）
        // =====================================================================

        /// <summary>実装済みの能力か（手配書・台帳・AbilityCaster が受け付ける番号）。DINO はまだ。</summary>
        public static bool IsImplemented(int a)
        {
            return a == ABILITY_DUST || a == ABILITY_LASSO || a == ABILITY_WALL || a == ABILITY_BALL || a == ABILITY_BOMB || a == ABILITY_MIRAGE;
        }

        /// <summary>手配書の壁・台帳に並べる順。</summary>
        public static int ImplementedCount() { return 6; }

        public static int ImplementedAt(int i)
        {
            if (i == 0) return ABILITY_DUST;
            if (i == 1) return ABILITY_LASSO;
            if (i == 2) return ABILITY_WALL;
            if (i == 3) return ABILITY_BALL;
            if (i == 4) return ABILITY_BOMB;
            if (i == 5) return ABILITY_MIRAGE;
            return ABILITY_DUST;
        }

        /// <summary>能力の番号を実装済みのものに（それ以外は砂塵）。</summary>
        public static int SanitizeAbility(int a)
        {
            return IsImplemented(a) ? a : ABILITY_DUST;
        }

        // ---- r3-04：Lv3 で拾った遺物は「溢れ」になる ----
        /// <summary>溢れ 1 つあたり：クールダウンをこの割合だけ縮める・持久力を足す・加速。</summary>
        public const float SURGE_COOLDOWN_FRACTION = 0.5f;
        public const float SURGE_STAMINA = 35f;
        public const float SURGE_SPEED_FACTOR = 1.08f;
        public const float SURGE_SPEED_SECONDS = 1.5f;

        /// <summary>Lv の上限で余った遺物の数（gain 0 なら 0）。</summary>
        public static int RelicOverflow(int levelBefore, int gain)
        {
            if (gain <= 0) return 0;
            int lv = Clamp(levelBefore);
            int room = 3 - lv;
            int over = gain - room;
            return over > 0 ? over : 0;
        }

        /// <summary>溢れ overflow 個で縮めた「使えるようになる時刻」（now より前にはしない）。cooldown は実際のクールダウン（倍率込み）。</summary>
        public static float SurgeReadyAt(float readyAt, float now, float cooldown, int overflow)
        {
            if (overflow <= 0) return readyAt;
            float r = readyAt - SURGE_COOLDOWN_FRACTION * cooldown * overflow;
            return r < now ? now : r;
        }

        /// <summary>溢れ overflow 個で足した持久力（上限 MountMath.STAMINA_MAX）。</summary>
        public static float SurgeStamina(float stamina, int overflow)
        {
            if (overflow <= 0) return stamina;
            return Mathf.Min(MountMath.STAMINA_MAX, stamina + SURGE_STAMINA * overflow);
        }

        // ---- r3-05：投げ縄は相手を選んで投げる（狙いの補助）。外したら安く ----
        /// <summary>照準からこの角度（度）以内の、いちばん近い相手に投げる。</summary>
        public const float LASSO_ASSIST_DEG = 10f;
        /// <summary>誰もいない（届かない）とき：投げずに 1 秒だけ待つ。</summary>
        public const float LASSO_FIZZLE_COOLDOWN = 1f;
        /// <summary>遮られた・外れたとき：クールダウンのこの割合。</summary>
        public const float LASSO_MISS_FRACTION = 0.35f;

        public const int CAST_HIT = 0;
        public const int CAST_MISS = 1;
        public const int CAST_FIZZLE = 2;

        /// <summary>照準（水平の向き aim）と相手への向き to の角度が LASSO_ASSIST_DEG 以内か。</summary>
        public static bool InLassoCone(Vector3 aim, Vector3 to)
        {
            Vector3 a = new Vector3(aim.x, 0f, aim.z);
            Vector3 b = new Vector3(to.x, 0f, to.z);
            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) return false;
            float dot = Vector3.Dot(a.normalized, b.normalized);
            return dot >= Mathf.Cos(LASSO_ASSIST_DEG * Mathf.Deg2Rad);
        }

        /// <summary>投げ縄の結果ごとのクールダウン（秒、倍率 scale 込み）：当たり = 全部、外れ・遮られた = 35%、相手なし = 1 秒。</summary>
        public static float LassoCooldown(int outcome, float scale)
        {
            if (outcome == CAST_FIZZLE) return LASSO_FIZZLE_COOLDOWN;
            if (outcome == CAST_MISS) return Cooldown(ABILITY_LASSO) * LASSO_MISS_FRACTION * scale;
            return Cooldown(ABILITY_LASSO) * scale;
        }

        // ---- r3-10：蜃気楼（身代わりを走らせて、狙われなくなる）----
        public static float MirageSeconds(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 2.5f;
            if (lv == 2) return 3.5f;
            return 4.5f;
        }

        public static int MirageDecoys(int lv)
        {
            lv = Clamp(lv);
            return lv == 3 ? 2 : 1;
        }

        /// <summary>身代わりが横へずれる幅（±m）と、自分の体のちらつき（Hz）。</summary>
        public const float MIRAGE_DRIFT_M = 3f;
        public const float MIRAGE_FLICKER_HZ = 8f;

        /// <summary>狙えるか：蜃気楼の間（now &lt; mirageUntil）は狙われない。</summary>
        public static bool Targetable(float mirageUntil, float now)
        {
            return now >= mirageUntil;
        }

        /// <summary>ちらつき：見えている（true）か。8 Hz で点いたり消えたり。蜃気楼の外は常に見える。</summary>
        public static bool MirageVisible(float now, float mirageStart, float mirageUntil)
        {
            if (now < mirageStart || now >= mirageUntil) return true;
            int k = Mathf.FloorToInt((now - mirageStart) * MIRAGE_FLICKER_HZ * 2f);
            return (k & 1) == 0;
        }

        /// <summary>身代わり i（0..）の横のずれ（m）：t 秒で ±MIRAGE_DRIFT_M へゆっくり（1 体目は右、2 体目は左）。</summary>
        public static float MirageDrift(int i, float t, float seconds)
        {
            float side = (i % 2 == 0) ? 1f : -1f;
            float k = seconds > 0f ? Mathf.Clamp01(t / seconds) : 1f;
            return side * MIRAGE_DRIFT_M * Mathf.Sin(k * Mathf.PI * 0.5f);
        }

        // ---- r3-11：火薬樽（後ろに落とす罠）----
        public static int BombCount(int lv)
        {
            lv = Clamp(lv);
            return lv == 1 ? 1 : 2;
        }

        public static float BombRadius(int lv)
        {
            lv = Clamp(lv);
            if (lv == 1) return 4f;
            if (lv == 2) return 5f;
            return 6f;
        }

        public const float BOMB_FUSE = 3f;
        /// <summary>これより近づいた相手で火が回る（m、自分の判定）。</summary>
        public const float BOMB_PROXIMITY = 3f;
        /// <summary>置く場所：後ろへ（m）、2 個目は横へ（m）。</summary>
        public const float BOMB_BEHIND = 3f;
        public const float BOMB_SPREAD = 2.5f;
        public const float BOMB_UP_KNOCK = 5f;
        public const float BOMB_STUN = 0.6f;
        /// <summary>置いた本人が自分の樽で爆発しないまでの秒数（置いてすぐ通り過ぎる）。</summary>
        public const float BOMB_OWNER_SAFE = 1.5f;

        /// <summary>爆発の時刻か：導火線が尽きた、または相手が BOMB_PROXIMITY 以内。</summary>
        public static bool BombDetonates(float age, float nearestDist)
        {
            return age >= BOMB_FUSE || nearestDist <= BOMB_PROXIMITY;
        }

        /// <summary>爆発の半径の内か（水平距離）。</summary>
        public static bool InBlast(Vector3 center, Vector3 p, float radius)
        {
            float dx = p.x - center.x, dz = p.z - center.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        // ---- r3-12：集中攻撃の上限 ----
        public const int PILEON_HITS = 3;
        public const float PILEON_WINDOW = 6f;
        public const float PILEON_GUARD = 3f;

        /// <summary>直近 PILEON_WINDOW 秒の被弾（hitTimes、未使用は負の値）が PILEON_HITS 回以上なら守りを出す。</summary>
        public static bool PileOnGuard(float[] hitTimes, float now)
        {
            if (hitTimes == null) return false;
            int n = 0;
            for (int i = 0; i < hitTimes.Length; i++)
            {
                float t = hitTimes[i];
                if (t < 0f) continue;
                if (now - t <= PILEON_WINDOW && now >= t) n++;
            }
            return n >= PILEON_HITS;
        }

        /// <summary>First claim wins: returns holder if already held (>=0), else claimant.</summary>
        public static int RelicArbiter(int holder, int claimant)
        {
            if (holder >= 0) return holder;
            return claimant;
        }
    }
}
