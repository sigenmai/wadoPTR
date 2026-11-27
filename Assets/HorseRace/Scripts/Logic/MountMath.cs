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
        /// 歩様の速さ（m/s）。馬 4 / 8 / 14 / 18、ラクダ 4 / 7.5 / 12 / 15（ラクダの最高速は馬の約 0.83 倍）。
        /// 2.5km のコースを駆歩＋ときどき疾走で、坂・カーブ込みで平均約 12〜13 m/s ＝ 1 ステージ約 3 分 20 秒〜3 分 30 秒。
        /// </summary>
        public static float GaitSpeed(int mount, int gait)
        {
            if (mount == MOUNT_CAMEL)
            {
                if (gait == GAIT_WALK) return 4f;
                if (gait == GAIT_TROT) return 7.5f;
                if (gait == GAIT_CANTER) return 12f;
                return 15f;
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

        /// <summary>砂の上の馬の速さの倍率（ラクダは 1）。疾走 18×0.78 = 14.0 m/s ＜ ラクダ 15 m/s（砂漠はラクダが約 7% 速い）。</summary>
        public const float HORSE_SAND_FACTOR = 0.78f;

        /// <summary>
        /// 目標速度。ばてている（exhausted）間は速歩（馬 8 / ラクダ 7.5 m/s）まで：走り切ってしまうと大きく遅れる（system-r1-01）。
        /// </summary>
        public static float TargetSpeed(int mount, int gait, float slopeDeg, bool exhausted, bool onSand)
        {
            if (exhausted && gait > GAIT_TROT) gait = GAIT_TROT;
            float speed = GaitSpeed(mount, gait);
            float uphillPerDeg = mount == MOUNT_CAMEL ? 0.0195f : 0.015f;
            float factor = slopeDeg >= 0f ? 1f - slopeDeg * uphillPerDeg : 1f - slopeDeg * 0.01f;
            factor = Mathf.Clamp(factor, 0.5f, 1.25f);
            speed *= factor;
            if (onSand && mount == MOUNT_HORSE) speed *= HORSE_SAND_FACTOR;
            return speed;
        }

        public static float Approach(float current, float target, float dt)
        {
            if (target > current) return Mathf.Min(target, current + ACCEL * dt);
            return Mathf.Max(target, current - DECEL * dt);
        }

        // ---- スタミナ（system-r1-01：いつ使うかを選ぶ資源にする）----
        public const float STAMINA_MAX = 100f;
        public const float DRAIN_HORSE = 12f;
        public const float DRAIN_CAMEL = 7.5f;
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
        public const float BUMP_RADIUS = 1.6f;

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

        public static float SmoothAngle(float current, float target, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            return current + (target - current) * k;
        }
    }
}
