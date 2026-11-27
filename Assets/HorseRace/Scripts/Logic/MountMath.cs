using UnityEngine;

namespace HorseRace
{
    /// <summary>Pure movement math for mounts. Static methods only (UdonSharp-safe).</summary>
    public class MountMath : UdonSharp.UdonSharpBehaviour
    {
        public const int MOUNT_HORSE = 0;
        public const int MOUNT_CAMEL = 1;
        public const int GAIT_WALK = 0;
        public const int GAIT_TROT = 1;
        public const int GAIT_CANTER = 2;
        public const int GAIT_SPRINT = 3;

        /// <summary>馬の疾走（全体の最高速度）。旋回の速さの基準にも使う。</summary>
        public const float TOP_SPEED = 18f;
        /// <summary>加速・減速（m/s²）。0→18 m/s が約 3.3 秒、18→0 が約 1.6 秒。</summary>
        public const float ACCEL = 5.5f;
        public const float DECEL = 11f;
        /// <summary>旋回の速さ：止まっているとき／最高速のとき（度/秒）。</summary>
        public const float TURN_RATE_SLOW = 90f;
        public const float TURN_RATE_FAST = 50f;

        /// <summary>
        /// 歩様の速さ（m/s）。馬 4 / 8 / 14 / 18、ラクダ 4 / 7.5 / 13 / 15.5（system-r2-01：ラクダは遅いが持久力で疾走を長く続けられる）。
        /// 疾走と駆歩をくり返す平均：馬 15.6 m/s（疾走 40%）、ラクダ 14.4 m/s（疾走 57%）＝ 固い地面は馬が約 8% 速く、
        /// 深い砂（馬 ×0.82）はラクダが約 13% 速い（MountMathTests.CamelHasAStage）。
        /// 2.5km のコースを駆歩＋ときどき疾走で、坂・カーブ込みで平均約 12〜13 m/s ＝ 1 ステージ約 3 分 20 秒〜3 分 30 秒。
        /// </summary>
        public static float GaitSpeed(int mount, int gait)
        {
            if (mount == MOUNT_CAMEL)
            {
                if (gait == GAIT_WALK) return 4f;
                if (gait == GAIT_TROT) return 7.5f;
                if (gait == GAIT_CANTER) return 13f;
                return 15.5f;
            }
            if (gait == GAIT_WALK) return 4f;
            if (gait == GAIT_TROT) return 8f;
            if (gait == GAIT_CANTER) return 14f;
            return TOP_SPEED;
        }

        /// <summary>
        /// アニメーションを等倍で再生するときの速さ（クリップを作ったときの歩様の速さ：馬 3/6/10/13、ラクダ 3/6/9/11）。
        /// 実速度 / これ ＝ 再生速度なので、速くなった分だけ脚の回転も速くなる（足が滑って見えない）。
        /// </summary>
        public static float AnimRefSpeed(int mount, int gait)
        {
            if (mount == MOUNT_CAMEL)
            {
                if (gait == GAIT_WALK) return 3f;
                if (gait == GAIT_TROT) return 6f;
                if (gait == GAIT_CANTER) return 9f;
                return 11f;
            }
            if (gait == GAIT_WALK) return 3f;
            if (gait == GAIT_TROT) return 6f;
            if (gait == GAIT_CANTER) return 10f;
            return 13f;
        }

        /// <summary>
        /// 砂の上の馬の速さの倍率（ラクダは 1）。疾走 18×0.82 = 14.8 m/s ＜ ラクダ 15.5 m/s。
        /// 砂は道の外と、ステージごとの「深い砂」の区間（StageDef.sandTrailArcs：道の上も砂）にある（system-r2-01）。
        /// </summary>
        public const float HORSE_SAND_FACTOR = 0.82f;

        /// <summary>
        /// 目標速度。ばてている（exhausted）間は速歩（馬 8 / ラクダ 7.5 m/s）まで：走り切ってしまうと大きく遅れる（system-r1-01）。
        /// </summary>
        public static float TargetSpeed(int mount, int gait, float slopeDeg, bool exhausted, bool onSand)
        {
            if (exhausted && gait > GAIT_TROT) gait = GAIT_TROT;
            float speed = GaitSpeed(mount, gait);
            float uphillPerDeg = mount == MOUNT_CAMEL ? 0.0195f : 0.015f;
            float factor = slopeDeg >= 0f ? 1f - slopeDeg * uphillPerDeg : 1f - slopeDeg * DOWNHILL_PER_DEG;
            factor = Mathf.Clamp(factor, 0.5f, DOWNHILL_MAX);
            speed *= factor;
            if (onSand && mount == MOUNT_HORSE) speed *= HORSE_SAND_FACTOR;
            return speed;
        }

        /// <summary>下りの速さの上乗せ（course-r2-07）：1° ごとに 2%、最大 ×1.2（9° の最後の下りで 18 → 21.6 m/s）。</summary>
        public const float DOWNHILL_PER_DEG = 0.02f;
        public const float DOWNHILL_MAX = 1.2f;

        // ---- 曲がるときの速さの上限（course-r2-01）----
        /// <summary>横の加速度の上限（m/s²）。速さ v で角速度 ω（rad/s）なら v·ω ≤ これ：半径 46m 以上なら 18 m/s、28m で駈歩 14 m/s。</summary>
        public const float LAT_ACCEL_MAX = 7f;
        /// <summary>角速度をならす時間（秒）。キーボードの短い操作では落とさず、曲がり続けたときだけ効く。</summary>
        public const float YAW_RATE_SMOOTH_SECONDS = 0.35f;

        /// <summary>今の向きの変わる速さ yawRateDeg（度/秒）で出せる速さの上限（m/s）= LAT_ACCEL_MAX / ω。</summary>
        public static float CornerSpeedCap(float yawRateDeg)
        {
            float w = Mathf.Max(Mathf.Abs(yawRateDeg) * Mathf.Deg2Rad, 0.01f);
            return LAT_ACCEL_MAX / w;
        }

        /// <summary>角速度（度/秒、絶対値）を YAW_RATE_SMOOTH_SECONDS でならす。</summary>
        public static float SmoothYawRate(float smoothed, float instantDegPerSec, float dt)
        {
            float k = Mathf.Clamp01(dt / YAW_RATE_SMOOTH_SECONDS);
            return smoothed + (Mathf.Abs(instantDegPerSec) - smoothed) * k;
        }

        public static float Approach(float current, float target, float dt)
        {
            if (target > current) return Mathf.Min(target, current + ACCEL * dt);
            return Mathf.Max(target, current - DECEL * dt);
        }

        // ---- スタミナ（system-r1-01：いつ使うかを選ぶ資源にする）----
        public const float STAMINA_MAX = 100f;
        public const float DRAIN_HORSE = 12f;
        public const float DRAIN_CAMEL = 6f;
        /// <summary>回復（/秒）：常歩・速歩 12、駆歩 8、ばてている間 4。</summary>
        public const float REFILL_EASY = 12f;
        public const float REFILL_CANTER = 8f;
        public const float REFILL_EXHAUSTED = 4f;
        /// <summary>ばてから戻るスタミナ（これ未満の間はばてたまま）。</summary>
        public const float EXHAUST_RECOVER = 40f;
        /// <summary>疾走をやめたときにまだこれ以上残っていれば「いい手綱さばき」：SECOND_WIND_SECONDS 秒間 回復 ×SECOND_WIND_MUL。</summary>
        public const float SECOND_WIND = 15f;
        public const float SECOND_WIND_MUL = 1.25f;
        public const float SECOND_WIND_SECONDS = 2f;

        /// <summary>1 フレームのスタミナ。gait = 今の歩様（0 常歩〜3 疾走。止まっているときは常歩）。</summary>
        public static float StepStamina(int mount, float stamina, bool sprinting, bool exhausted, int gait, float dt)
        {
            return StepStamina(mount, stamina, sprinting, exhausted, gait, dt, 1f);
        }

        /// <summary>refillMul = 回復の倍率（SECOND_WIND・ドラフトで 1 より大きい）。消費には掛けない。</summary>
        public static float StepStamina(int mount, float stamina, bool sprinting, bool exhausted, int gait, float dt, float refillMul)
        {
            float rate;
            if (sprinting) rate = mount == MOUNT_CAMEL ? -DRAIN_CAMEL : -DRAIN_HORSE;
            else if (exhausted) rate = REFILL_EXHAUSTED * refillMul;
            else if (gait >= GAIT_CANTER) rate = REFILL_CANTER * refillMul;
            else rate = REFILL_EASY * refillMul;
            return Mathf.Clamp(stamina + rate * dt, 0f, STAMINA_MAX);
        }

        public static bool IsExhausted(float stamina, bool wasExhausted)
        {
            if (stamina <= 0f) return true;
            if (stamina >= EXHAUST_RECOVER) return false;
            return wasExhausted;
        }

        /// <summary>疾走をやめた瞬間のスタミナで「ひと息（回復 ×1.25）」がもらえるか。</summary>
        public static bool EarnsSecondWind(float staminaAtRelease)
        {
            return staminaAtRelease >= SECOND_WIND;
        }

        /// <summary>
        /// VR の疾走のラッチ（system-r1-11）：両手（またはリボルバーを持っていない片手）を 1 回振り下ろすと ON のまま、
        /// ばてるか手綱を引く（brake）まで続く。振り直しても ON のまま（腕を休めてよい）。
        /// </summary>
        public static bool SprintLatch(bool prevLatched, bool swing, bool brake, bool exhausted)
        {
            if (brake || exhausted) return false;
            if (swing) return true;
            return prevLatched;
        }

        // ---- 壁・段差に当たったとき（system-r1-02：止まらずに滑る）----
        /// <summary>head（0 = かすった、1 = 正面）がこれより大きいときだけ止まる。</summary>
        public const float WALL_HARD_STOP_HEAD = 0.85f;
        /// <summary>壁に沿って滑る向きへ鼻先を回す速さ（度/秒）。</summary>
        public const float WALL_SLIDE_TURN_DEG = 20f;

        /// <summary>壁に当たったときに残す速さの割合：かすった 0.9 〜 正面 0.25。</summary>
        public static float WallScrapeFactor(float head)
        {
            return Mathf.Lerp(0.9f, 0.25f, Mathf.Clamp01(head));
        }

        /// <summary>進む向きと壁の面の角度（度、0 = 平行にかすった、90 = 正面）から head を出す。</summary>
        public static float WallHead(float angleDeg)
        {
            return Mathf.Clamp01(Mathf.Sin(Mathf.Clamp(angleDeg, 0f, 90f) * Mathf.Deg2Rad));
        }

        // ---- ドラフト（system-r1-13）----
        public const float DRAFT_MIN_M = 3f;
        public const float DRAFT_MAX_M = 14f;
        public const float DRAFT_HALF_ANGLE = 12f;
        public const float DRAFT_REFILL_MUL = 1.6f;
        public const float DRAFT_SPEED_BONUS = 0.6f;
        /// <summary>他の乗り物と横に押し合うときの速さ（m/s、止めない）。</summary>
        public const float BUMP_SPEED = 2f;
        public const float BUMP_RADIUS = 1.4f;   // system-r2-07：相手の位置の予測の誤差で幻の押し合いをしない

        /// <summary>前の乗り物の後ろについているか（距離 3〜14m、自分の向きから ±12° 以内）。1 = ついている、0 = いない。</summary>
        public static float DraftBonus(float dist, float angleDeg)
        {
            if (dist < DRAFT_MIN_M || dist > DRAFT_MAX_M) return 0f;
            if (Mathf.Abs(angleDeg) > DRAFT_HALF_ANGLE) return 0f;
            return 1f;
        }

        // ---- スタートダッシュ（system-r1-15）----
        public const int START_NONE = 0;
        public const int START_BOOST = 1;
        public const int START_STALL = -1;
        public const float START_WINDOW = 0.35f;
        public const float START_EARLY = 0.6f;
        public const float START_BOOST_FACTOR = 1.2f;
        public const float START_BOOST_SECONDS = 1.5f;
        public const float START_STALL_SECONDS = 0.5f;

        /// <summary>deltaSeconds = 前進を押した時刻 − 号砲（負 = 早い）。±0.35 秒ならダッシュ、0.6 秒より早ければ立ち上がって硬直。</summary>
        public static int StartBoost(float deltaSeconds)
        {
            if (deltaSeconds < -START_EARLY) return START_STALL;
            if (Mathf.Abs(deltaSeconds) <= START_WINDOW) return START_BOOST;
            return START_NONE;
        }

        public static float TurnRateDeg(int mount, float speed)
        {
            float t = Mathf.Clamp01(speed / TOP_SPEED);
            float rate = TURN_RATE_SLOW + (TURN_RATE_FAST - TURN_RATE_SLOW) * t;
            return mount == MOUNT_CAMEL ? rate * 0.8f : rate;
        }

        // ---- 着地（system-r1-03：近道・ジャンプを損にしない）----
        public const float LANDING_SAFE_M = 6f;
        public const float LANDING_HARD_M = 20f;
        public const float LANDING_MIN_KEEP = 0.55f;
        public const float CLEAN_LANDING_MAX_M = 10f;
        public const float CLEAN_LANDING_WINDOW = 0.25f;
        public const float CLEAN_LANDING_FACTOR = 1.15f;
        public const float CLEAN_LANDING_SECONDS = 1.5f;

        /// <summary>落ちた高さ（m）からの硬直秒数：6m 未満は 0、以降 0.08 秒/m、最大 0.8 秒。</summary>
        public static float LandingStun(float fallHeight)
        {
            if (fallHeight < LANDING_SAFE_M) return 0f;
            return Mathf.Min((fallHeight - LANDING_SAFE_M) * 0.08f, 0.8f);
        }

        /// <summary>着地で残す速さの割合：6m 未満は 1、20m で 0.55 まで直線で下がる（それより高くても 0.55）。</summary>
        public static float LandingSpeedKeep(float fallHeight)
        {
            if (fallHeight <= LANDING_SAFE_M) return 1f;
            float t = Mathf.Clamp01((fallHeight - LANDING_SAFE_M) / (LANDING_HARD_M - LANDING_SAFE_M));
            return Mathf.Lerp(1f, LANDING_MIN_KEEP, t);
        }

        /// <summary>きれいな着地：10m 未満の落下で、着地の 0.25 秒前までに疾走を押していた（secondsSinceSprintPress ≥ 0）。</summary>
        public static bool CleanLanding(float fallHeight, float secondsSinceSprintPress)
        {
            if (fallHeight >= CLEAN_LANDING_MAX_M) return false;
            return secondsSinceSprintPress >= 0f && secondsSinceSprintPress <= CLEAN_LANDING_WINDOW;
        }

        // ---- よろけ（system-r2-12：止めずに勢いを残す）----
        /// <summary>よろけの間の目標速度 = よろけ始めの速さ × これ（0 にしてブレーキを掛けない。操作だけ利かない）。</summary>
        public const float STUN_KEEP = 0.7f;

        /// <summary>speed で走っていてよろけ stunSeconds を受けたとき、何 m 遅れるか（よろけ中は STUN_KEEP まで減速、その後 ACCEL で戻る）。</summary>
        public static float StunLossMeters(float speed, float stunSeconds)
        {
            const float dt = 0.01f;
            float v = speed;
            float lost = 0f;
            float t = 0f;
            float keep = speed * STUN_KEEP;
            int guard = 0;
            while (guard++ < 100000)
            {
                bool stunned = t < stunSeconds;
                if (!stunned && v >= speed - 0.0001f) break;
                v = Approach(v, stunned ? keep : speed, dt);
                lost += (speed - v) * dt;
                t += dt;
            }
            return lost;
        }

        // ---- カーブの立ち上がり（system-r2-14）----
        /// <summary>疾走せず 10 m/s 以上で 3 秒以内に 60° 以上曲がり、ハンドルを戻した（|steer| &lt; 0.2）ら ×1.08 を 1.2 秒。</summary>
        public const float CORNER_MIN_DEG = 60f;
        public const float CORNER_WINDOW = 3f;
        public const float CORNER_MIN_SPEED = 10f;
        public const float CORNER_STRAIGHT_STEER = 0.2f;
        public const float CORNER_BOOST_FACTOR = 1.08f;
        public const float CORNER_BOOST_SECONDS = 1.2f;

        /// <summary>このフレームでカーブの曲がりを数えるか（疾走していない・10 m/s 以上・ハンドルを切っている）。</summary>
        public static bool CornerCounts(bool sprinting, float speed, float steerAbs)
        {
            return !sprinting && speed >= CORNER_MIN_SPEED && steerAbs >= CORNER_STRAIGHT_STEER;
        }

        /// <summary>turnedDeg = 数えた曲がりの合計、seconds = 曲がり始めからの秒数、steerAbs = 今のハンドル。立ち上がりの加速を出すか。</summary>
        public static bool CornerExitBoost(float turnedDeg, float seconds, float steerAbs)
        {
            return turnedDeg >= CORNER_MIN_DEG && seconds <= CORNER_WINDOW && steerAbs < CORNER_STRAIGHT_STEER;
        }

        // ---- 相手の持久力が見える（system-r2-08）----
        /// <summary>MountController.syncGait の上位ビット：持久力 40 未満（TIRED）・ばてた（SPENT）。下位 3 ビット（歩様）と AIR/STUN/STAMP（8/16/32）とは重ならない。</summary>
        public const int GAIT_FLAG_TIRED = 64;
        public const int GAIT_FLAG_SPENT = 128;
        public const float TIRED_STAMINA = 40f;

        /// <summary>歩様（とほかのフラグ）に持久力の帯のビットを足す。</summary>
        public static int PackStamina(int gaitWithFlags, float stamina, bool exhausted)
        {
            int g = gaitWithFlags & ~(GAIT_FLAG_TIRED | GAIT_FLAG_SPENT);
            if (exhausted) return g | GAIT_FLAG_SPENT;
            if (stamina < TIRED_STAMINA) return g | GAIT_FLAG_TIRED;
            return g;
        }

        /// <summary>ほかの人の乗り物の見た目用の持久力：ばてた 0、40 未満 30、それ以外 100。</summary>
        public static float RemoteStamina(int gaitWithFlags)
        {
            if ((gaitWithFlags & GAIT_FLAG_SPENT) != 0) return 0f;
            if ((gaitWithFlags & GAIT_FLAG_TIRED) != 0) return 30f;
            return 100f;
        }

        public static bool IsSpent(int gaitWithFlags)
        {
            return (gaitWithFlags & GAIT_FLAG_SPENT) != 0;
        }

        /// <summary>疾走と駆歩をくり返したときの平均の速さ（m/s、StepStamina で dt ごとに。疾走は持久力 lowAt まで、駆歩で highAt まで戻す）。</summary>
        public static float CycleAverageSpeed(int mount, bool onSand, float seconds, float lowAt, float highAt)
        {
            const float dt = 0.02f;
            float stamina = STAMINA_MAX;
            bool exhausted = false;
            bool sprint = true;
            float dist = 0f;
            float t = 0f;
            while (t < seconds)
            {
                if (sprint && stamina <= lowAt) sprint = false;
                else if (!sprint && stamina >= highAt) sprint = true;
                int gait = sprint ? GAIT_SPRINT : GAIT_CANTER;
                stamina = StepStamina(mount, stamina, sprint, exhausted, sprint ? GAIT_SPRINT : GAIT_CANTER, dt);
                exhausted = IsExhausted(stamina, exhausted);
                dist += TargetSpeed(mount, gait, 0f, exhausted, onSand) * dt;
                t += dt;
            }
            return dist / seconds;
        }

        public static float SmoothAngle(float current, float target, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            return current + (target - current) * k;
        }
    }
}
