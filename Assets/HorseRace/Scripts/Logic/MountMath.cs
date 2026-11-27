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

        // =====================================================================
        // round 3（system-r3-01/02/06/07/08）
        // =====================================================================

        // ---- r3-01：跳ねても速さの決まりを抜けられない ----
        /// <summary>跳ぶたびに払う持久力。</summary>
        public const float JUMP_STAMINA = 8f;
        /// <summary>着地してから次に跳べるまでの秒数。</summary>
        public const float JUMP_COOLDOWN = 0.9f;
        /// <summary>空中でも疾走を押していれば持久力を減らす（回復はしない）。</summary>
        public const bool AIR_DRAIN_WHEN_SPRINT = true;
        /// <summary>空中のカーブの上限は地上より少しゆるい（+2 m/s）。</summary>
        public const float AIR_CORNER_SLACK = 2f;

        /// <summary>跳べるか：着地から JUMP_COOLDOWN 秒たち、持久力が JUMP_STAMINA 以上。</summary>
        public static bool CanJump(float secondsSinceLand, float stamina)
        {
            return secondsSinceLand >= JUMP_COOLDOWN && stamina >= JUMP_STAMINA;
        }

        /// <summary>空中の速さ：入ったときの速さを保つが、カーブの上限 + AIR_CORNER_SLACK は超えない。</summary>
        public static float AirSpeed(float speed, float yawRateDeg)
        {
            return Mathf.Min(speed, CornerSpeedCap(yawRateDeg) + AIR_CORNER_SLACK);
        }

        /// <summary>着地の瞬間：地上で出せる速さ（groundTarget = 目標 × 倍率、カーブの上限込み）より速ければそこまで落とす。</summary>
        public static float LandingSpeed(float speed, float groundTarget)
        {
            return Mathf.Min(speed, Mathf.Max(groundTarget, 0f));
        }

        /// <summary>空中の持久力の回復の倍率（空中は回復しない）。</summary>
        public static float AirRefillMul(bool grounded, float refillMul)
        {
            return grounded ? refillMul : 0f;
        }

        /// <summary>
        /// 疾走を押したまま、hopEvery 秒ごとに跳ぼうとして distance m を走る（hop = false なら地上だけ）。MountController.SimulateOwner と同じ決まり：
        /// 跳ぶと JUMP_STAMINA、着地から JUMP_COOLDOWN は跳べない、空中は速さを保つが回復しない、着地で地上の目標まで落とす。平均の速さ（m/s）。
        /// </summary>
        public static float HopRunAverage(int mount, bool onSand, float distance, float hopEvery, bool hop, float jumpSpeed, float gravity)
        {
            const float dt = 0.01f;
            float stamina = STAMINA_MAX;
            bool exhausted = false;
            float speed = TargetSpeed(mount, GAIT_SPRINT, 0f, false, onSand);
            float dist = 0f, t = 0f, nextHop = 0f, lastLand = -10f;
            bool grounded = true;
            float y = 0f, vy = 0f;
            int guard = 0;
            while (dist < distance && guard++ < 1000000)
            {
                float target = TargetSpeed(mount, GAIT_SPRINT, 0f, exhausted, onSand);
                if (grounded && hop && t >= nextHop && CanJump(t - lastLand, stamina))
                {
                    stamina -= JUMP_STAMINA;
                    grounded = false;
                    vy = jumpSpeed;
                    nextHop = t + hopEvery;
                }
                bool sprinting = !exhausted && speed > 0.5f;
                stamina = StepStamina(mount, stamina, sprinting, exhausted, GAIT_SPRINT, dt, AirRefillMul(grounded, 1f));
                exhausted = IsExhausted(stamina, exhausted);
                if (grounded) speed = Approach(speed, target, dt);
                else
                {
                    vy -= gravity * dt;
                    y += vy * dt;
                    if (vy <= 0f && y <= 0f)
                    {
                        y = 0f;
                        vy = 0f;
                        grounded = true;
                        lastLand = t;
                        speed = LandingSpeed(speed, target);
                    }
                }
                dist += speed * dt;
                t += dt;
            }
            return t > 0f ? dist / t : 0f;
        }

        // ---- r3-02：跳んだ弧そのものはきれいな着地の高さに数えない ----
        /// <summary>きれいな着地に要る高さ（m）。踏み切った所より 2m 以上下（本物の段・崖）。</summary>
        public const float CLEAN_LANDING_MIN_M = 2f;

        /// <summary>きれいな着地の高さ：跳んだなら踏み切りの高さ − 着地点、崖から落ちたなら落ち始め（頂点）− 着地点。</summary>
        public static float BoostHeight(bool jumpedThisAir, float takeoffY, float fallStartY, float groundY)
        {
            return jumpedThisAir ? takeoffY - groundY : fallStartY - groundY;
        }

        /// <summary>きれいな着地の加速を出すか（高さ hBoost が 2〜10m、着地の 0.25 秒前までに疾走）。</summary>
        public static bool GrantsCleanLanding(float hBoost, float secondsSinceSprintPress)
        {
            return hBoost >= CLEAN_LANDING_MIN_M && CleanLanding(hBoost, secondsSinceSprintPress);
        }

        /// <summary>平らな坂（slopeDeg、下りが正）を speed で走りながら跳んだときの、踏み切りから着地点までの落差（m）。</summary>
        public static float HopDropOnSlope(float speed, float downSlopeDeg, float jumpSpeed, float gravity)
        {
            float k = Mathf.Tan(downSlopeDeg * Mathf.Deg2Rad) * speed;   // 地面が 1 秒に下がる量
            float tLand = 2f * (jumpSpeed + k) / gravity;                 // jumpSpeed·t − g t²/2 = −k t
            return k * tLand;
        }

        // ---- r3-06：正面から壁に当たっても止まらない ----
        /// <summary>
        /// 正面から当たったときに残す速さ。レビューの案は WallScrapeFactor(1) = 0.25 だったが、18 m/s で 16.6m 遅れ「12m 以下」を満たさないので 0.4
        /// （18 → 7.2 m/s、ACCEL で戻るまで 10.6m）。
        /// </summary>
        public const float WALL_HEAD_ON_KEEP = 0.4f;
        /// <summary>壁に当たった後、この秒数は当たっても削らない。</summary>
        public const float WALL_GUARD_SECONDS = 1f;
        /// <summary>はじかれて空いている側へ回す最大の角度（度）と、回す速さ（度/秒、視点がいきなり回らない）。</summary>
        public const float WALL_DEFLECT_DEG = 35f;
        public const float WALL_DEFLECT_RATE = 90f;
        /// <summary>空いている側を探すレイの角度（±度）と、はじく速さ（m/s）。</summary>
        public const float WALL_PROBE_DEG = 60f;
        public const float WALL_KNOCK = 2f;
        /// <summary>段差で滑れなかったとき（TrySlide の失敗）に残す速さ。</summary>
        public const float STEP_BLOCK_KEEP = 0.4f;

        /// <summary>速さ speed で正面から当たって WALL_HEAD_ON_KEEP まで落ち、ACCEL で戻るまでに遅れる距離（m）。</summary>
        public static float WallHeadOnLossMeters(float speed)
        {
            float dv = speed * (1f - WALL_HEAD_ON_KEEP);
            return dv * dv / (2f * ACCEL);
        }

        /// <summary>空いている側（左右のレイの届いた距離）：右が広ければ +1、左なら −1。</summary>
        public static float DeflectSide(float freeLeft, float freeRight)
        {
            return freeRight >= freeLeft ? 1f : -1f;
        }

        // ---- r3-07：キーボードは「この速さで曲がれるいっぱい」まで ----
        /// <summary>キーボードのハンドルを入れる速さ・戻す速さ（/秒）。</summary>
        public const float KB_STEER_IN = 4f;
        public const float KB_STEER_OUT = 8f;
        /// <summary>上限ぴったりで曲がると丸めで少し落ちるので 2% 手前。</summary>
        public const float KB_STEER_MARGIN = 0.98f;

        /// <summary>キーボードのハンドル（-1..1）を input へ近づける（入れるとき 4/秒、0 へ戻すとき 8/秒）。</summary>
        public static float SlewSteer(float current, float input, float dt)
        {
            bool releasing = Mathf.Abs(input) < Mathf.Abs(current) || input * current < 0f;
            float rate = releasing ? KB_STEER_OUT : KB_STEER_IN;
            return Mathf.MoveTowards(current, input, rate * dt);
        }

        /// <summary>
        /// キーボードのハンドルの上限：曲がる速さが CornerSpeedCap ≥ target を保てるいっぱい（0..1）。
        /// steerMax = (LAT_ACCEL_MAX / max(target, 1) × Rad2Deg) / TurnRateDeg(mount, speed)。ブレーキ（S）を押している間は呼ばない（1）。
        /// </summary>
        public static float KeyboardSteerLimit(int mount, float speed, float target)
        {
            float rate = TurnRateDeg(mount, speed);
            if (rate <= 0.001f) return 1f;
            float maxDeg = LAT_ACCEL_MAX / Mathf.Max(target, 1f) * Mathf.Rad2Deg * KB_STEER_MARGIN;
            return Mathf.Clamp01(maxDeg / rate);
        }

        // ---- r3-08：VR の手綱（リボルバーの手は数えない）----
        public const int REIN_BOTH = 0;
        /// <summary>右手でリボルバーを持っている：左手だけで手綱。</summary>
        public const int REIN_LEFT_ONLY = 1;
        /// <summary>左手でリボルバーを持っている：右手だけで手綱。</summary>
        public const int REIN_RIGHT_ONLY = 2;
        public const float REIN_DIFF_DEADZONE = 0.1f;
        public const float REIN_DIFF_RANGE = 0.15f;
        public const float REIN_LATERAL_DEADZONE = 0.06f;
        public const float REIN_LATERAL_RANGE = 0.15f;
        /// <summary>片手のときのブレーキ（引き量 0..1）。</summary>
        public const float REIN_ONE_HAND_BRAKE = 0.7f;
        /// <summary>手綱の休める位置をゆっくり今の手に寄せる速さ（/秒）。</summary>
        public const float REIN_RECENTER_RATE = 0.15f;

        /// <summary>
        /// 手綱のハンドル（-1..1、右が正）。両手：pullL / pullR（m、手前に引いた量）の差、不感帯 0.1m・幅 0.15m。
        /// 片手：その手の横のずれ lateral（m、右が正）、不感帯 0.06m・幅 0.15m。
        /// </summary>
        public static float ReinSteer(float pullL, float pullR, int oneHandMode, float lateral)
        {
            float diff, dead, range;
            if (oneHandMode == REIN_BOTH) { diff = pullR - pullL; dead = REIN_DIFF_DEADZONE; range = REIN_DIFF_RANGE; }
            else { diff = lateral; dead = REIN_LATERAL_DEADZONE; range = REIN_LATERAL_RANGE; }
            float mag = Mathf.Abs(diff) - dead;
            if (mag <= 0f) return 0f;
            float s = Mathf.Clamp01(mag / range);
            return diff > 0f ? s : -s;
        }

        /// <summary>手綱のブレーキ。両手：両方 threshold 超え。片手：空いている手の引き（0..1）が 0.7 超え。</summary>
        public static bool ReinBrake(float pullLNorm, float pullRNorm, int oneHandMode, float threshold)
        {
            if (oneHandMode == REIN_LEFT_ONLY) return pullLNorm > REIN_ONE_HAND_BRAKE;
            if (oneHandMode == REIN_RIGHT_ONLY) return pullRNorm > REIN_ONE_HAND_BRAKE;
            return pullLNorm > threshold && pullRNorm > threshold;
        }

        /// <summary>ゆっくり休める位置を直してよいか（ハンドルほぼ 0・ブレーキなし・1 秒振っていない）。</summary>
        public static bool ReinMayRecenter(float steer, bool brake, float secondsSinceSwing)
        {
            return Mathf.Abs(steer) < 0.1f && !brake && secondsSinceSwing >= 1f;
        }

        public static float SmoothAngle(float current, float target, float dt)
        {
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            return current + (target - current) * k;
        }
    }
}
