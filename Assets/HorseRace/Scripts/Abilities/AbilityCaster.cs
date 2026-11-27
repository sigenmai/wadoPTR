using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 乗り物 1 頭ぶんの能力（砂塵・投げ縄・岩壁・鉄球・火薬樽・蜃気楼）と、被弾の受け口。
    /// MountController と同じ GameObject に置く（同期モードも同じ Manual。同期変数は持たない）。
    /// こうすると NetworkEventTarget.Owner ＝ その乗り物のオーナー（＝乗っているプレイヤー）になる。
    ///
    /// 流れ（spec 5.2）:
    ///  - 発動は「乗っているローカルのプレイヤー」のクライアントだけ（CanCast）。
    ///  - 見た目（砂煙・縄・岩壁・鉄球・火薬樽・身代わり）は NetworkEventTarget.All の引数つきイベント（Fx*）で全員に出す。
    ///  - 投げ縄・リボルバー（一瞬で決まる）は発動者の画面で当て、相手の AbilityCaster に NetworkEventTarget.Owner で Hit* を送る。
    ///  - 続く当たり（砂煙・鉄球・火薬樽、system-r3-03）は被害者の画面で決める：どのクライアントも同じ引数で見た目を動かし、
    ///    自分が乗っている乗り物だけを調べて Hit*Local を自分に掛け、全員に FxHitConfirm（中継のカット・発動者の「命中！」）を送る。
    ///    相手は 0.22 秒＋遅延だけ遅れて描かれるので、発動者の画面で決めると被害者には「抜けたのに減速」「当たったのに何もない」になった。
    ///  - 6 秒に 3 回当たったら 3 秒の守り（system-r3-12：先頭への集中攻撃の上限）。
    ///  - 蜃気楼（system-r3-10）の間は狙われない（IsMirage、全員の画面で同じ時刻）。
    ///  - リボルバーの命中（HitRevolver）もここで受け、撃った人ごとの累計命中数で 3 発ごとに DropRelic（最終判定は被弾側）。
    ///    別の 2 人の 3 発目がほぼ同時に来ても、落とすのは 1 回だけ（RevolverMath.DROP_COOLDOWN 秒以内の 2 回目は Lv を下げない）。
    ///
    /// 入力: デスクトップ = E（照準は視線）／VR = トリガー（照準は押した手の向き。リボルバーを持っている手のトリガーは射撃）。
    /// Lv・クールダウン・命中数はローカル（オーナー側）だけが意味を持つ。ResetForStage は全クライアントで何度呼ばれてもよい。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AbilityCaster : UdonSharpBehaviour
    {
        public const int MAX_CASTERS = 16;
        public const int AIM_HEAD = 0;
        public const int AIM_LEFT = 1;
        public const int AIM_RIGHT = 2;
        /// <summary>よろけ・はじかれを受けた後の守り（秒）。この間は減速が半分の長さ、はじき・よろけなし（system-r1-06）。</summary>
        public const float GUARD_SECONDS = 1.5f;

        [Header("この乗り物")]
        public MountController mount;
        [Tooltip("全 AbilityCaster で重複しない番号（レース用 = slot 0..7、練習用 = 8, 9 …、16 未満）")]
        public int casterId;
        [Tooltip("練習用の馬なら ON（能力は手配書の選択、クールダウン ×PracticeArea.cooldownScale）")]
        public bool practiceMount;
        public RaceManager raceManager;
        public PracticeArea practiceArea;
        public CharmPouch pouch;
        [Tooltip("同じ乗り物のリボルバー（VR でリボルバーを持った手のトリガーを能力に使わないため）")]
        public RevolverHolder revolver;

        [Header("見た目（ワールド空間。Start で親から外す）")]
        public DustCloud dustCloud;
        public LassoEffect lasso;
        public RockWall rockWall;
        [Tooltip("鉄球 3 個（BallCount の最大）")]
        public IronBall[] balls;
        [Tooltip("落とした遺物用の予備 RelicPickup（[0] に出現を依頼する。全員で同じ配列）")]
        public RelicPickup[] relicPool;
        [Tooltip("砂煙を浴びたときに自分の頭の周りに出す半透明の砂（ローカルだけ）")]
        public GameObject haze;
        public AudioSource castSound;
        public AudioSource relicSound;
        [Tooltip("任意（Task 10）。未設定なら RaceManager.spectator を使う。命中の場面を中継カメラに知らせる（全員向けの見た目イベントからローカルに呼ぶだけ）")]
        public SpectatorDirector spectator;

        [Header("判定")]
        [Tooltip("乗り物の体・騎手の当たり判定のレイヤー")]
        public LayerMask targetMask = -1;
        [Tooltip("視線を遮るもの（地形・岩）")]
        public LayerMask blockMask = -1;
        public LayerMask groundMask = -1;
        public float lassoRadius = 1.5f;
        public float aimHeight = 1.8f;
        [Tooltip("VR の手の向き（手のトラッキング回転に掛けるローカル軸）")]
        public Vector3 vrHandAimAxis = new Vector3(0f, 0f, 1f);

        [Header("調整値（AbilityMath にない細部）")]
        public float dustHitSeconds = AbilityMath.DUST_HIT_SECONDS;
        public float dustBehind = 4f;
        public float ballSpeed = 26f;   // 最高速 18 m/s の馬が自分の鉄球に追いつかない
        public float ballLoft = 4f;
        public float ballSpreadDeg = 6f;
        [Tooltip("鉄球のよろけ秒数（Lv1..3）")]
        public float[] ballStunByLevel = new float[] { 0.4f, 0.6f, 0.8f };
        [Tooltip("カーブ量の倍率（Lv1..3）。カーブ（度/m）= ロール角 × AbilityMath.BALL_CURVE_DEG_PER_M_PER_ROLL_DEG × これ")]
        public float[] ballCurveScaleByLevel = new float[] { 0.1f, 0.15f, 0.2f };
        public float maxRollDeg = 45f;
        public float desktopRollDeg = 30f;
        [Tooltip("デスクトップの鉄球のカーブ：乗り物の正面から見た視線の左右のずれ（度）がこの範囲で 0 → desktopRollDeg")]
        public float desktopCurveMinDeg = 10f;
        public float desktopCurveMaxDeg = 40f;
        public float ballKnock = 3f;
        public float revolverKnock = 2f;
        public float revolverStun = 0.35f;
        public float relicDropBehind = 4f;

        [Header("round 3（system-r3-05/10/11）")]
        [Tooltip("投げ縄の相手がいない（届かない）ときのカチッという音（SfxMath.CLICK）")]
        public AudioSource fizzleSound;
        [Tooltip("蜃気楼の身代わり（同じ乗り物のモデルの写し、2 体）。MountBuilder が作る")]
        public MirageDouble[] doubles;
        [Tooltip("火薬樽（FarmProps_15 ×0.6、2 個）。MountBuilder が作る")]
        public PowderKeg[] kegs;

        /// <summary>能力 Lv（1..3）。ローカル（オーナー側）の値。</summary>
        public int Level = 1;

        // FindTarget の結果（out 引数の代わり）
        [Header("狙いの印（ui-r3-08、ローカルのみ）")]
        [Tooltip("投げ縄・鉄球を狙っている相手の上に出す宝石（Gems_01_01）。MountBuilder が作る")]
        public Transform aimMarker;
        public Renderer[] aimMarkerRenderers;
        [Tooltip("届く距離の内（赤）")]
        public Material aimInRange;
        [Tooltip("届く距離の外（灰色）")]
        public Material aimOutRange;

        [HideInInspector] public MountController foundMount;
        [HideInInspector] public PracticeDummy foundDummy;
        [HideInInspector] public Vector3 foundPoint;

        private float readyAt;
        /// <summary>撃った人（casterId）ごとの、このステージの累計命中数。</summary>
        private int[] hitsFromShooter;
        /// <summary>撃った人ごとの、もう処理した「3 発ごと」の回数（遺物を落とした・落とさず流した、どちらも含む）。</summary>
        private int[] dropsForShooter;
        private float lastDropAt = -1000f;
        private float hazeUntil;
        private float guardUntil;
        private bool pouchShownLocal;
        private int lastResetSeq = -1;
        private Vector3 aimDir;
        private float nextAimProbe;
        private Transform aimTarget;
        private int aimShownState = -1;   // -1 = 隠す、0 = 灰、1 = 赤
        private float aimRoll;
        private bool started;
        // ---- round 3 ----
        private float mirageStart = -1000f;
        private float mirageUntil = -1000f;
        private bool flickerHidden;
        private float[] hitTimes;
        private int hitTimeIdx;
        [HideInInspector] public MirageDouble foundDouble;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            hitsFromShooter = new int[MAX_CASTERS];
            hitTimes = new float[8];
            for (int i = 0; i < hitTimes.Length; i++) hitTimes[i] = -1000f;
            dropsForShooter = new int[MAX_CASTERS];
            if (mount == null) mount = GetComponent<MountController>();
            if (mount == null) Debug.LogWarning("[HorseRace] AbilityCaster: MountController がありません " + gameObject.name);
            if (casterId < 0 || casterId >= MAX_CASTERS) Debug.LogWarning("[HorseRace] AbilityCaster: casterId は 0..15 " + gameObject.name);
            if (haze != null) haze.SetActive(false);
        }

        // =====================================================================
        // 入力
        // =====================================================================

        private void Update()
        {
            EnsureInit();
            bool local = mount != null && mount.IsLocalRider();
            if (local)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (lp != null && !lp.IsUserInVR() && Input.GetKeyDown(KeyCode.E)) TryCast(AIM_HEAD);
            }
            UpdatePouch(local);
            UpdateHaze(local);
            UpdateAimProbe(local);
            UpdateMirageFlicker();
        }

        /// <summary>蜃気楼の間（system-r3-10）：自分の乗り物の見た目を 8 Hz でちらつかせる（全員の画面。renderer.enabled だけ、シェーダーは変えない）。</summary>
        private void UpdateMirageFlicker()
        {
            if (mount == null || mount.visual == null) return;
            bool hide = !AbilityMath.MirageVisible(Time.time, mirageStart, mirageUntil);
            if (hide == flickerHidden) return;
            flickerHidden = hide;
            mount.visual.SetFlickerHidden(hide);
        }

        /// <summary>蜃気楼の間か（全員の画面で同じ：FxMirage で始まる）。</summary>
        public bool IsMirage()
        {
            return !AbilityMath.Targetable(mirageUntil, Time.time);
        }

        /// <summary>
        /// 狙いの印（ui-r3-08）：投げ縄・鉄球が使えるとき、発動と同じ球の当たり判定（FindTarget）を 10 Hz で飛ばし、
        /// 見つかった相手（練習のカカシも）の上 2.9m（ゴールの着順の札と同じ高さ）に金の宝石を回す。届く距離の内は赤、外は灰。
        /// ローカルだけ（同期しない）。
        /// </summary>
        private void UpdateAimProbe(bool local)
        {
            if (aimMarker == null) return;
            int id = AbilityId();
            bool want = local && (id == AbilityMath.ABILITY_LASSO || id == AbilityMath.ABILITY_BALL) && CanCast();
            if (!want)
            {
                aimTarget = null;
                SetAimState(-1);
                return;
            }
            if (Time.time < nextAimProbe) return;
            nextAimProbe = Time.time + AbilityMath.AIM_PROBE_INTERVAL;
            VRCPlayerApi lp = Networking.LocalPlayer;
            ComputeAim(lp != null && lp.IsUserInVR() ? AIM_RIGHT : AIM_HEAD);
            Vector3 origin = mount.transform.position + Vector3.up * aimHeight;
            float eff = id == AbilityMath.ABILITY_LASSO ? AbilityMath.LassoRange(Level) + lassoRadius : AbilityMath.BALL_AIM_RANGE;
            // 投げ縄は発動と同じ「照準の 10° 以内のいちばん近い相手」（system-r3-05）
            bool hit = id == AbilityMath.ABILITY_LASSO ? FindLassoTarget(origin, FlatAim(), eff * AbilityMath.AIM_PROBE_REACH)
                : FindTarget(origin, FlatAim(), eff * AbilityMath.AIM_PROBE_REACH, lassoRadius);
            Transform t = null;
            if (hit && foundMount != null) t = foundMount.transform;
            else if (hit && foundDummy != null) t = foundDummy.transform;
            else if (hit && foundDouble != null) t = foundDouble.transform;
            foundMount = null;
            foundDummy = null;
            foundDouble = null;
            aimTarget = t;
            if (t == null) { SetAimState(-1); return; }
            Vector3 d = t.position - mount.transform.position;
            d.y = 0f;
            SetAimState(d.magnitude <= eff ? 1 : 0);
        }

        private void SetAimState(int state)
        {
            if (aimMarker == null || state == aimShownState) return;
            aimShownState = state;
            bool on = state >= 0;
            if (aimMarker.gameObject.activeSelf != on) aimMarker.gameObject.SetActive(on);
            if (!on || aimMarkerRenderers == null) return;
            Material m = state == 1 ? aimInRange : aimOutRange;
            if (m == null) return;
            for (int i = 0; i < aimMarkerRenderers.Length; i++) if (aimMarkerRenderers[i] != null) aimMarkerRenderers[i].sharedMaterial = m;
        }

        private void LateUpdate()
        {
            if (aimMarker == null || aimTarget == null || aimShownState < 0) return;
            aimMarker.SetPositionAndRotation(aimTarget.position + Vector3.up * AbilityMath.AIM_MARKER_HEIGHT,
                Quaternion.Euler(0f, Time.time * AbilityMath.AIM_MARKER_SPIN_DEG, 0f));
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!value) return;
            if (mount == null || !mount.IsLocalRider()) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !lp.IsUserInVR()) return; // デスクトップの左クリックはリボルバー
            int hand = args.handType == HandType.LEFT ? AIM_LEFT : AIM_RIGHT;
            if (revolver != null && revolver.IsHeldInLocalHand(hand)) return;
            TryCast(hand);
        }

        /// <summary>今の照準（デスクトップ = 視線、VR = 右手）で発動を試みる。</summary>
        public void Cast()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            TryCast(lp != null && lp.IsUserInVR() ? AIM_RIGHT : AIM_HEAD);
        }

        public bool IsReady()
        {
            return Time.time >= readyAt;
        }

        public bool CanCast()
        {
            if (mount == null || !mount.IsLocalRider()) return false;
            if (!IsReady()) return false;
            if (practiceMount) return practiceArea == null || practiceArea.inPractice;
            if (!mount.raceActive || mount.InputLocked) return false;
            if (mount.syncFinishAt >= 0.0) return false;
            if (raceManager != null && raceManager.state != RaceMath.STATE_RACING) return false;
            // 開幕は使えない（system-r1-07）。解禁はレーンの順に 8〜13.25 秒（system-r2-11：猶予明けの一斉発動を防ぐ）
            if (OpeningLeft() > 0f) return false;
            return true;
        }

        /// <summary>この乗り物の能力が開幕の解禁まであと何秒か（0 = 使える。練習用・レース外は 0）。AbilityMath.OpeningReady（レーン順）。</summary>
        public float OpeningLeft()
        {
            if (practiceMount || raceManager == null || mount == null) return 0f;
            return raceManager.OpeningLeft(mount.slotIndex);
        }

        /// <summary>今、開幕の猶予で使えない（お守り袋を消し、リボルバーは「封」）。</summary>
        public bool LockedByStartGrace()
        {
            if (practiceMount || raceManager == null) return false;
            return raceManager.InStartGrace();
        }

        /// <summary>被弾後の守りの間か（オーナー側の値）。</summary>
        public bool IsGuarded()
        {
            return Time.time < guardUntil;
        }

        public int AbilityId()
        {
            int id = AbilityMath.ABILITY_DUST;
            if (raceManager != null)
            {
                if (practiceMount) id = raceManager.localAbility;
                else if (mount != null && raceManager.slotAbility != null && mount.slotIndex >= 0 && mount.slotIndex < raceManager.slotAbility.Length)
                    id = raceManager.slotAbility[mount.slotIndex];
            }
            return AbilityMath.SanitizeAbility(id);   // 実装済みの 6 つ（system-r3-10）
        }

        public float CooldownScale()
        {
            if (practiceMount && practiceArea != null && practiceArea.inPractice) return practiceArea.cooldownScale;
            return 1f;
        }

        private void TryCast(int hand)
        {
            if (!CanCast()) return;
            int id = AbilityId();
            ComputeAim(hand);
            // 各 Cast* は実際に使ったクールダウン（秒）を返す（投げ縄は外れ・相手なしで短い：system-r3-05）。負 = 投げなかった
            float cd;
            if (id == AbilityMath.ABILITY_DUST) cd = CastDust();
            else if (id == AbilityMath.ABILITY_LASSO) cd = CastLasso();
            else if (id == AbilityMath.ABILITY_WALL) cd = CastWall();
            else if (id == AbilityMath.ABILITY_BOMB) cd = CastBomb();
            else if (id == AbilityMath.ABILITY_MIRAGE) cd = CastMirage();
            else cd = CastBall();
            if (cd < 0f)
            {
                readyAt = Time.time + AbilityMath.LASSO_FIZZLE_COOLDOWN;
                if (fizzleSound != null) fizzleSound.Play();
                if (raceManager != null && raceManager.hud != null) raceManager.hud.ShowNote(HudMath.LassoFizzleNote(), new Color(0.75f, 0.75f, 0.75f), 1f);
                return;
            }
            readyAt = Time.time + cd;
            if (castSound != null) castSound.Play();
            if (raceManager != null && raceManager.hud != null) raceManager.hud.abilityCastCount++;   // ボタンの説明は最初の 3 回だけ（ui-r2-07）
        }

        private void ComputeAim(int hand)
        {
            aimDir = mount.transform.forward;
            aimRoll = 0f;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            if (hand == AIM_HEAD)
            {
                VRCPlayerApi.TrackingData td = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                aimDir = td.rotation * Vector3.forward;
                // 鉄球のカーブは視線の左右のずれから（A/D は旋回なので使わない：system-r1-16）。10〜40° で 0 → desktopRollDeg
                Vector3 f = FlatForward();
                Vector3 a = new Vector3(aimDir.x, 0f, aimDir.z);
                if (a.sqrMagnitude > 0.0001f)
                {
                    a = a.normalized;
                    float off = Mathf.Atan2(f.x * a.z - f.z * a.x, f.x * a.x + f.z * a.z) * Mathf.Rad2Deg;   // 左が正
                    float mag = Mathf.Abs(off);
                    float k = Mathf.Clamp01((mag - desktopCurveMinDeg) / Mathf.Max(1f, desktopCurveMaxDeg - desktopCurveMinDeg));
                    aimRoll = (off > 0f ? -1f : 1f) * k * desktopRollDeg;   // 右を見る = 右へカーブ
                }
            }
            else
            {
                VRCPlayerApi.TrackingData td = lp.GetTrackingData(hand == AIM_LEFT
                    ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
                aimDir = td.rotation * vrHandAimAxis;
                float z = td.rotation.eulerAngles.z;
                if (z > 180f) z -= 360f;
                aimRoll = Mathf.Clamp(-z, -maxRollDeg, maxRollDeg); // 右に傾ける = 右へカーブ
            }
        }

        private Vector3 FlatAim()
        {
            Vector3 d = new Vector3(aimDir.x, 0f, aimDir.z);
            if (d.sqrMagnitude < 0.01f)
            {
                Vector3 f = mount.transform.forward;
                d = new Vector3(f.x, 0f, f.z);
            }
            return d.normalized;
        }

        private Vector3 FlatForward()
        {
            Vector3 f = mount.transform.forward;
            Vector3 d = new Vector3(f.x, 0f, f.z);
            if (d.sqrMagnitude < 0.01f) return Vector3.forward;
            return d.normalized;
        }

        private int LevelIndex()
        {
            return Mathf.Clamp(Level, 1, 3) - 1;
        }

        /// <summary>Lv ごとのインスペクター配列から値を取る。配列が短ければ最後の要素、空なら fallback。</summary>
        private float LevelValue(float[] arr, float fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            int i = LevelIndex();
            if (i >= arr.Length) i = arr.Length - 1;
            return arr[i];
        }

        // =====================================================================
        // 発動（発動者のクライアント）
        // =====================================================================

        private float FullCooldown(int id)
        {
            return AbilityMath.Cooldown(id) * CooldownScale();
        }

        private float CastDust()
        {
            Vector3 pos = mount.transform.position - FlatForward() * dustBehind;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxDust), pos, AbilityMath.DustRadius(Level), AbilityMath.DustDuration(Level));
            return FullCooldown(AbilityMath.ABILITY_DUST);
        }

        /// <summary>
        /// 投げ縄（system-r3-05）：照準の 10° 以内・届く距離のいちばん近い相手（見通しのあるもの）に投げる（VR の揺れる手でも狙える）。
        /// 誰もいなければ投げない（-1：1 秒だけ待つ・「届かない」）。遮られた・届かなかったらクールダウン 35%。
        /// </summary>
        private float CastLasso()
        {
            Vector3 origin = mount.transform.position + Vector3.up * aimHeight;
            Vector3 dir = FlatAim();
            float range = AbilityMath.LassoRange(Level);
            bool hit = FindLassoTarget(origin, dir, range + lassoRadius);
            if (!hit && foundMount == null && foundDummy == null && foundDouble == null && !lassoBlocked) return -1f;
            // 相手の今の位置（描いている 0.22 秒前の位置ではない、system-r2-07）で届くかを確かめ直す
            if (hit && foundMount != null)
            {
                Vector3 pp = foundMount.PredictedPosition() - mount.transform.position;
                pp.y = 0f;
                if (pp.magnitude > range + lassoRadius) { hit = false; foundMount = null; }
            }
            if (hit)
            {
                Vector3 to = foundMount != null ? foundMount.PredictedPosition() : foundPoint;
                mount.ApplyPull(to, AbilityMath.LassoPullBonus(Level), AbilityMath.LASSO_SELF_SECONDS);
                if (foundMount != null)
                {
                    AbilityCaster tc = foundMount.GetComponent<AbilityCaster>();
                    if (tc != null) tc.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(HitLasso));
                }
                else if (foundDummy != null) foundDummy.Wobble();
                else if (foundDouble != null) PopDoubleForAll(foundDouble);
            }
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxLasso), foundPoint, hit, hit ? RaceSlotOf(foundMount) : -1);
            return AbilityMath.LassoCooldown(hit ? AbilityMath.CAST_HIT : AbilityMath.CAST_MISS, CooldownScale());
        }

        private bool lassoBlocked;

        /// <summary>
        /// 投げ縄の相手（system-r3-05）：origin の周り reach m の乗っている乗り物（蜃気楼の間は除く）・カカシ・身代わりのうち、
        /// 今の位置（PredictedPosition）が照準 dir の 10° 以内で、いちばん近いもの。見通し（blockMask のレイ）が無ければ外れ（lassoBlocked = true）。
        /// 結果は foundMount / foundDummy / foundDouble / foundPoint。
        /// </summary>
        public bool FindLassoTarget(Vector3 origin, Vector3 dir, float reach)
        {
            foundMount = null;
            foundDummy = null;
            foundDouble = null;
            lassoBlocked = false;
            foundPoint = origin + dir * reach;
            Collider[] cs = Physics.OverlapSphere(origin, reach, targetMask, QueryTriggerInteraction.Collide);
            if (cs == null) return false;
            float best = reach + 1f;
            Vector3 bestPoint = foundPoint;
            MountController bm = null;
            PracticeDummy bd = null;
            MirageDouble bx = null;
            for (int i = 0; i < cs.Length; i++)
            {
                Collider c = cs[i];
                if (c == null) continue;
                Vector3 p;
                MountController m = null;
                PracticeDummy pd = null;
                MirageDouble md = c.GetComponentInParent<MirageDouble>();
                if (md != null)
                {
                    if (md.caster == this || !md.IsShown()) continue;
                    p = md.transform.position + Vector3.up * aimHeight;
                }
                else
                {
                    m = c.GetComponentInParent<MountController>();
                    if (m != null)
                    {
                        if (m == mount || m.riderPlayerId < 0 || m.IsMirage()) continue;
                        p = m.PredictedPosition() + Vector3.up * aimHeight;
                    }
                    else
                    {
                        pd = c.GetComponentInParent<PracticeDummy>();
                        if (pd == null) continue;
                        p = pd.transform.position + Vector3.up * aimHeight;
                    }
                }
                Vector3 to = p - origin;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                float d = flat.magnitude;
                if (d >= best || d > reach) continue;
                if (!AbilityMath.InLassoCone(dir, to)) continue;
                best = d; bm = m; bd = pd; bx = md; bestPoint = p;
            }
            if (bm == null && bd == null && bx == null) return false;
            foundMount = bm; foundDummy = bd; foundDouble = bx; foundPoint = bestPoint;
            Vector3 v = bestPoint - origin;
            float len = v.magnitude;
            RaycastHit block;
            if (len > 0.01f && Physics.Raycast(origin, v / len, out block, len, blockMask, QueryTriggerInteraction.Ignore)
                && block.collider != null && block.collider.GetComponentInParent<MountController>() == null
                && block.distance < len - 1f)
            {
                lassoBlocked = true;
                foundPoint = block.point;
                return false;
            }
            return true;
        }

        private float CastWall()
        {
            Vector3 dir = FlatAim();
            Vector3 center = mount.transform.position + dir * AbilityMath.WALL_AHEAD;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxWall), center, yaw, AbilityMath.WallCount(Level), AbilityMath.WallWidth(Level));
            return FullCooldown(AbilityMath.ABILITY_WALL);
        }

        private float CastBall()
        {
            Vector3 origin = mount.transform.position + Vector3.up * aimHeight + FlatForward() * 1.0f;
            float curve = aimRoll * AbilityMath.BALL_CURVE_DEG_PER_M_PER_ROLL_DEG * LevelValue(ballCurveScaleByLevel, 0.1f);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxBall), origin, FlatAim(), curve, AbilityMath.BallCount(Level));
            return FullCooldown(AbilityMath.ABILITY_BALL);
        }

        /// <summary>火薬樽（system-r3-11）：3m 後ろに 1〜2 個（2 個目は横へ）。導火線 3 秒か、相手が 3m 以内で爆発。</summary>
        private float CastBomb()
        {
            Vector3 f = FlatForward();
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            Vector3 back = mount.transform.position - f * AbilityMath.BOMB_BEHIND;
            int n = AbilityMath.BombCount(Level);
            Vector3 a = n >= 2 ? back - right * AbilityMath.BOMB_SPREAD : back;
            Vector3 b = back + right * AbilityMath.BOMB_SPREAD;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxBomb), a, b, n, Level);
            return FullCooldown(AbilityMath.ABILITY_BOMB);
        }

        /// <summary>蜃気楼（system-r3-10）：身代わりを今の向き・速さで走らせ、2.5/3.5/4.5 秒狙われない。</summary>
        private float CastMirage()
        {
            float yaw = mount.transform.eulerAngles.y;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxMirage), mount.transform.position, yaw, mount.Speed, Level);
            return FullCooldown(AbilityMath.ABILITY_MIRAGE);
        }

        /// <summary>身代わりを全員の画面で消す（投げ縄・リボルバーが当てた：発動者の画面で決まる当たり）。</summary>
        public void PopDoubleForAll(MirageDouble md)
        {
            if (md == null || md.caster == null) return;
            md.Pop();
            md.caster.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxPopDouble), md.index);
        }

        /// <summary>
        /// origin から dir へ半径 radius の球を range まで飛ばし、いちばん近い「自分以外の乗っている乗り物」か練習用のカカシを探す。
        /// 結果は foundMount / foundDummy / foundPoint。地形などに遮られていたら外れ（foundPoint は遮った点）。
        /// </summary>
        public bool FindTarget(Vector3 origin, Vector3 dir, float range, float radius)
        {
            foundMount = null;
            foundDummy = null;
            foundDouble = null;
            foundPoint = origin + dir * range;
            float best = range + 1f;
            RaycastHit[] hits = Physics.SphereCastAll(origin, radius, dir, range, targetMask, QueryTriggerInteraction.Collide);
            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider c = hits[i].collider;
                    if (c == null) continue;
                    float d = hits[i].distance;
                    if (d >= best) continue;
                    Vector3 p = d > 0f ? hits[i].point : c.transform.position;
                    MirageDouble md = c.GetComponentInParent<MirageDouble>();
                    if (md != null)
                    {
                        if (md.caster == this || !md.IsShown()) continue;
                        best = d; foundDouble = md; foundMount = null; foundDummy = null; foundPoint = p;
                        continue;
                    }
                    MountController m = c.GetComponentInParent<MountController>();
                    if (m != null)
                    {
                        if (m == mount || m.riderPlayerId < 0 || m.IsMirage()) continue;   // 蜃気楼の間は狙えない（system-r3-10）
                        best = d; foundMount = m; foundDummy = null; foundDouble = null; foundPoint = p;
                        continue;
                    }
                    PracticeDummy pd = c.GetComponentInParent<PracticeDummy>();
                    if (pd != null)
                    {
                        best = d; foundDummy = pd; foundMount = null; foundDouble = null; foundPoint = p;
                    }
                }
            }

            RaycastHit block;
            bool blocked = Physics.Raycast(origin, dir, out block, range, blockMask, QueryTriggerInteraction.Ignore);
            if (blocked && block.collider != null && block.collider.GetComponentInParent<MountController>() != null) blocked = false;

            if (foundMount == null && foundDummy == null && foundDouble == null)
            {
                if (blocked) foundPoint = block.point;
                return false;
            }
            if (blocked && block.distance < best - 0.05f)
            {
                foundMount = null;
                foundDummy = null;
                foundDouble = null;
                foundPoint = block.point;
                return false;
            }
            return true;
        }

        // ---- 見た目オブジェクトから（発動者のクライアントだけ）----

        /// <summary>中継カメラ（Task 10）：この乗り物専用の設定、なければ RaceManager の共通の設定。</summary>
        public SpectatorDirector Spectator()
        {
            if (spectator != null) return spectator;
            return raceManager != null ? raceManager.spectator : null;
        }

        /// <summary>m がレース用の乗り物ならそのスロット番号、そうでなければ（練習用・カカシ・null）-1。見た目イベントの引数用。</summary>
        public int RaceSlotOf(MountController m)
        {
            if (m == null || raceManager == null || raceManager.mounts == null) return -1;
            int s = m.slotIndex;
            if (s < 0 || s >= raceManager.mounts.Length || raceManager.mounts[s] != m) return -1;
            return s;
        }

        /// <summary>
        /// 撃った側の命中の知らせ（ui-r2-06）：自分が乗っている乗り物の発動・射撃が当たったら首の札に「命中！ 名前」。
        /// playerId &lt; 0（カカシ・練習用）は名前なし。
        /// </summary>
        public void ConfirmHit(int playerId)
        {
            EnsureInit();
            if (mount == null || !mount.IsLocalRider()) return;
            if (raceManager == null || raceManager.hud == null) return;
            raceManager.hud.ShowHitConfirm(playerId >= 0 ? RaceManager.ShortName(playerId) : "");
        }

        /// <summary>撃たれた側の知らせ（ui-r2-06）：首の札に赤で「投げ縄に掛かった」など、能力の札から赤い火花。</summary>
        private void NotifyHitTaken(int kind)
        {
            if (mount == null || !mount.IsLocalRider()) return;
            if (raceManager != null && raceManager.hud != null) raceManager.hud.ShowHitTaken(kind);   // 首の札が手も振るわせる（ui-r3-05）
            else mount.Pulse(MountInputVR.PULSE_HIT_S, MountInputVR.PULSE_HIT_AMP);                  // 首の札が無い（練習用など）とき（system-r3-16）
            if (pouch != null) pouch.FlashHit();
        }

        /// <summary>
        /// 命中の確認（system-r3-03）：被害者が自分の画面で当たりを決めたら、発動者の AbilityCaster のこのイベントを全員に送る。
        /// 中継のカット（slot = 被害者のレース用スロット、-1 なら練習用）と、発動者本人の「命中！ 名前」。
        /// </summary>
        [NetworkCallable(10)]
        public void FxHitConfirm(int slot)
        {
            EnsureInit();
            SpectatorDirector sd = Spectator();
            if (slot >= 0 && sd != null) sd.NotifyHitSlot(slot);
            if (mount != null && mount.IsLocalRider())
                ConfirmHit(slot >= 0 && raceManager != null && slot < raceManager.slotPlayerId.Length ? raceManager.slotPlayerId[slot] : -1);
        }

        // =====================================================================
        // 見た目（全員）
        // =====================================================================

        [NetworkCallable(10)]
        public void FxDust(Vector3 pos, float radius, float duration)
        {
            EnsureInit();
            if (dustCloud != null) dustCloud.Show(pos, radius, duration, this, mount != null && mount.IsLocalRider());
        }

        [NetworkCallable(10)]
        public void FxLasso(Vector3 end, bool hit, int hitSlot)
        {
            EnsureInit();
            if (lasso != null) lasso.Show(mount, end, hit, hit ? AbilityMath.LASSO_SELF_SECONDS : 0f);
            SpectatorDirector sd = Spectator();
            if (hitSlot >= 0 && sd != null) sd.NotifyHitSlot(hitSlot);
            if (hit && mount != null && mount.IsLocalRider())
                ConfirmHit(hitSlot >= 0 && raceManager != null ? raceManager.slotPlayerId[hitSlot] : -1);
        }

        [NetworkCallable(10)]
        public void FxWall(Vector3 center, float yaw, int count, float width)
        {
            EnsureInit();
            // 壁の枚数 = Lv（AbilityMath.WallCount）。道の幅に合わせて減らす・寄せる・乗り物の足元に立てない（system-r2-10）
            if (rockWall != null) rockWall.ShowFor(center, yaw, count, width, AbilityMath.WALL_SECONDS, mount, count);
        }

        /// <summary>岩壁の 1 枚が崩れた（system-r3-06：当たった本人のクライアントから）。</summary>
        [NetworkCallable(10)]
        public void FxCrumble(int segment)
        {
            EnsureInit();
            if (rockWall != null) rockWall.Crumble(segment);
        }

        /// <summary>蜃気楼（system-r3-10）：全員の画面で身代わりを走らせ、mirageUntil まで狙えなくする。</summary>
        [NetworkCallable(10)]
        public void FxMirage(Vector3 pos, float yaw, float speed, int level)
        {
            EnsureInit();
            float secs = AbilityMath.MirageSeconds(level);
            mirageStart = Time.time;
            mirageUntil = Time.time + secs;
            if (castSound != null) castSound.Play();
            if (doubles == null || mount == null) return;
            int n = Mathf.Min(AbilityMath.MirageDecoys(level), doubles.Length);
            for (int i = 0; i < n; i++)
                if (doubles[i] != null) doubles[i].Show(pos, yaw, speed, mount.syncMount, mount.syncCoat, secs, i);
        }

        [NetworkCallable(10)]
        public void FxPopDouble(int index)
        {
            EnsureInit();
            if (doubles == null || index < 0 || index >= doubles.Length || doubles[index] == null) return;
            doubles[index].Pop();
        }

        /// <summary>火薬樽（system-r3-11）：全員の画面で置く。爆発の当たりは各自の画面（自分の乗り物だけ）。</summary>
        [NetworkCallable(10)]
        public void FxBomb(Vector3 a, Vector3 b, int count, int level)
        {
            EnsureInit();
            if (kegs == null) return;
            bool detect = mount != null && mount.IsLocalRider();
            float r = AbilityMath.BombRadius(level);
            int n = Mathf.Min(count, kegs.Length);
            for (int i = 0; i < n; i++)
                if (kegs[i] != null) kegs[i].Arm(i == 0 ? a : b, r, this, detect);
        }

        [NetworkCallable(10)]
        public void FxBall(Vector3 origin, Vector3 flatDir, float curveDegPerM, int count)
        {
            EnsureInit();
            if (balls == null) return;
            bool detect = mount != null && mount.IsLocalRider();
            int n = Mathf.Min(count, balls.Length);
            for (int i = 0; i < n; i++)
            {
                if (balls[i] == null) continue;
                float ang = (i - (n - 1) * 0.5f) * ballSpreadDeg;
                Vector3 d = Quaternion.AngleAxis(ang, Vector3.up) * flatDir;
                balls[i].Launch(origin, d * ballSpeed + Vector3.up * ballLoft, curveDegPerM, detect, this, count);   // 個数 = Lv（AbilityMath.BallCount）
            }
        }

        // =====================================================================
        // 被弾（この乗り物のオーナーで実行）
        // =====================================================================

        private bool AcceptHit()
        {
            EnsureInit();
            if (mount == null) return false;
            if (!Networking.IsOwner(gameObject)) return false;
            if (mount.riderPlayerId < 0) return false;
            // レース中か練習場だけ（ロビーでの悪用・ステージ結果中に遅れて届いた命中で Lv や遺物が動かないように）
            if (practiceMount) return practiceArea == null || practiceArea.inPractice;
            if (!mount.raceActive) return false;
            return raceManager == null || raceManager.state == RaceMath.STATE_RACING;
        }

        private Vector3 Flat(Vector3 v)
        {
            Vector3 d = new Vector3(v.x, 0f, v.z);
            if (d.sqrMagnitude < 0.0001f) return Vector3.zero;
            return d.normalized;
        }

        /// <summary>守りの間は減速の長さを半分に（見た目・音はそのまま）。</summary>
        private float GuardedSeconds(float seconds)
        {
            return IsGuarded() ? seconds * 0.5f : seconds;
        }

        /// <summary>はじき・よろけを掛ける（守りの間は掛けない）。掛けたら守りを始める。</summary>
        private void KnockWithGuard(Vector3 impulse, float stunSeconds)
        {
            if (IsGuarded()) return;
            mount.ApplyKnock(impulse, stunSeconds);
            StartGuard();
        }

        private void StartGuard()
        {
            if (guardUntil > Time.time + GUARD_SECONDS) return;   // もっと長い守り（集中攻撃）を縮めない
            guardUntil = Time.time + GUARD_SECONDS;
            if (pouch != null) pouch.FlashGuard(GUARD_SECONDS);
        }

        /// <summary>
        /// 被弾を数える（system-r3-12）：6 秒に 3 回で 3 秒の守り（金の火花と「守り」）。先頭が後ろの全員から順番に撃たれ続けない。
        /// </summary>
        private void RegisterHit()
        {
            if (hitTimes == null) return;
            hitTimes[hitTimeIdx] = Time.time;
            hitTimeIdx = (hitTimeIdx + 1) % hitTimes.Length;
            if (!AbilityMath.PileOnGuard(hitTimes, Time.time)) return;
            guardUntil = Time.time + AbilityMath.PILEON_GUARD;
            if (pouch != null) pouch.FlashGuard(AbilityMath.PILEON_GUARD);
            for (int i = 0; i < hitTimes.Length; i++) hitTimes[i] = -1000f;
        }

        /// <summary>被害者の画面で決めた当たりを、発動者（src）の AbilityCaster から全員に知らせる。</summary>
        private void ConfirmToAll(AbilityCaster src)
        {
            if (src == null) return;
            src.SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxHitConfirm), RaceSlotOf(mount));
        }

        /// <summary>砂煙の当たり（system-r3-03、被害者＝このクライアントの乗り手。MountController.ScanMounts から）。</summary>
        public void HitDustLocal(AbilityCaster src)
        {
            if (!AcceptHit()) return;
            mount.AddSpeedMod(AbilityMath.DUST_SLOW_FACTOR, GuardedSeconds(dustHitSeconds));
            hazeUntil = Time.time + dustHitSeconds;
            NotifyHitTaken(AbilityMath.ABILITY_DUST);
            RegisterHit();
            ConfirmToAll(src);
        }

        /// <summary>鉄球の当たり（system-r3-03、被害者のクライアントの IronBall から）。lv = 発動者の Lv（よろけの秒数）。</summary>
        public void HitBallLocal(Vector3 velocity, int lv, AbilityCaster src)
        {
            if (!AcceptHit()) return;
            float stun = 0.5f;
            if (src != null && src.ballStunByLevel != null && src.ballStunByLevel.Length > 0)
                stun = src.ballStunByLevel[Mathf.Clamp(lv - 1, 0, src.ballStunByLevel.Length - 1)];
            mount.AddSpeedMod(AbilityMath.BALL_HIT_FACTOR, GuardedSeconds(AbilityMath.BALL_HIT_SECONDS));
            KnockWithGuard(Flat(velocity) * ballKnock, Mathf.Clamp(stun, 0f, 1.5f));
            NotifyHitTaken(AbilityMath.ABILITY_BALL);
            RegisterHit();
            ConfirmToAll(src);
        }

        /// <summary>火薬樽の爆発（system-r3-11、被害者のクライアントの PowderKeg から）：上へ 5 m/s・よろけ 0.6 秒（守りの決まりどおり）。</summary>
        public void HitBombLocal(Vector3 center, AbilityCaster src)
        {
            if (!AcceptHit()) return;
            Vector3 away = Flat(mount.transform.position - center);
            KnockWithGuard(away * 2f + Vector3.up * AbilityMath.BOMB_UP_KNOCK, AbilityMath.BOMB_STUN);
            NotifyHitTaken(AbilityMath.ABILITY_BOMB);
            RegisterHit();
            ConfirmToAll(src);
        }


        [NetworkCallable(20)]
        public void HitLasso()
        {
            if (!AcceptHit()) return;
            bool guarded = IsGuarded();
            mount.AddSpeedMod(AbilityMath.LASSO_TARGET_FACTOR, GuardedSeconds(AbilityMath.LASSO_TARGET_SECONDS));
            if (!guarded) StartGuard();
            NotifyHitTaken(AbilityMath.ABILITY_LASSO);
            RegisterHit();
        }

        [NetworkCallable(20)]
        public void HitBall(Vector3 velocity, float stunSeconds)
        {
            if (!AcceptHit()) return;
            mount.AddSpeedMod(AbilityMath.BALL_HIT_FACTOR, GuardedSeconds(AbilityMath.BALL_HIT_SECONDS));
            KnockWithGuard(Flat(velocity) * ballKnock, Mathf.Clamp(stunSeconds, 0f, 1.5f));
            NotifyHitTaken(AbilityMath.ABILITY_BALL);
            RegisterHit();
        }

        /// <summary>
        /// リボルバー命中。shooterId = 撃った人の casterId、hitNumber = 撃った側で数えた「このステージでその相手への累計何発目か」（1, 2, 3, 4 …）。
        /// 最終判定はここ（被弾側のオーナー）。累計は撃った側の番号で上げるだけなので、順番が入れ替わって届いた命中（3 発目の後の 2 発目）は数に足さない。
        /// 3 発ごとに 1 回 DropRelic。ただし直前の DropRelic から DROP_COOLDOWN 秒以内（別の人の 3 発目が同時に来た）なら Lv は下げず、その人の 3 発は使い切った扱い。
        /// </summary>
        [NetworkCallable(20)]
        public void HitRevolver(Vector3 direction, int shooterId, int hitNumber)
        {
            if (!AcceptHit()) return;
            mount.AddSpeedMod(RevolverMath.HIT_FACTOR, GuardedSeconds(RevolverMath.HIT_SECONDS));
            KnockWithGuard(Flat(direction) * revolverKnock, revolverStun);
            NotifyHitTaken(HudMath.HIT_REVOLVER);
            RegisterHit();
            if (shooterId < 0 || shooterId >= MAX_CASTERS || shooterId == casterId) return;
            int total = RevolverMath.TotalAfterHit(hitsFromShooter[shooterId], hitNumber);
            hitsFromShooter[shooterId] = total;
            if (!RevolverMath.DropDue(total, dropsForShooter[shooterId])) return;
            dropsForShooter[shooterId] = RevolverMath.DropsFor(total);
            if (!RevolverMath.DropAllowed(Time.time, lastDropAt)) return;
            lastDropAt = Time.time;
            DropRelic();
        }

        // =====================================================================
        // Lv・遺物
        // =====================================================================

        /// <summary>この乗り物が今遺物を拾ったら何 Lv 上がるか（system-r1-04：1 位 0、上半分 +1、下半分 +2。練習用・順位なしは +1）。</summary>
        public int RelicGainNow()
        {
            if (practiceMount || raceManager == null || mount == null) return 1;
            int pid = mount.riderPlayerId;
            return AbilityMath.RelicGain(raceManager.GetRankOf(pid), raceManager.ActiveCount());
        }

        /// <summary>遺物を拾った（RelicPickup から、拾った本人のクライアントで）。gain = 上がる Lv（RelicGainNow）。</summary>
        public void GrantRelic(int gain)
        {
            EnsureInit();
            if (gain <= 0) return;
            int before = Level;
            Level = AbilityMath.LevelAfterRelicGain(Level, gain);
            if (relicSound != null) relicSound.Play();
            // 溢れ（system-r3-04）：Lv3 で余った遺物は、1 つにつきクールダウン半分・持久力 +35・×1.08 1.5 秒
            int over = AbilityMath.RelicOverflow(before, gain);
            if (over <= 0) return;
            readyAt = AbilityMath.SurgeReadyAt(readyAt, Time.time, FullCooldown(AbilityId()), over);
            if (mount != null)
            {
                mount.Stamina = AbilityMath.SurgeStamina(mount.Stamina, over);
                mount.AddSpeedMod(AbilityMath.SURGE_SPEED_FACTOR, AbilityMath.SURGE_SPEED_SECONDS);
                if (mount.visual != null) mount.visual.PlayBoostSparkle();
            }
            if (pouch != null) pouch.FlashSurge();
        }

        /// <summary>オーナー側。Lv −1。元の Lv が 2 以上なら足元（少し後ろ）に遺物を出すよう予備の RelicPickup に依頼する。</summary>
        public void DropRelic()
        {
            EnsureInit();
            int before = Level;
            Level = RevolverMath.LevelAfterDrop(before);
            if (!RevolverMath.SpawnsDroppedRelic(before)) return;
            if (relicPool == null || relicPool.Length == 0 || relicPool[0] == null)
            {
                Debug.LogWarning("[HorseRace] AbilityCaster: relicPool が未設定なので遺物を出せません");
                return;
            }
            Vector3 pos = mount != null ? mount.transform.position - FlatForward() * relicDropBehind : transform.position;
            // 自分が予備のオーナー（マスターが落とした等）なら直接呼ぶ
            if (Networking.IsOwner(relicPool[0].gameObject)) relicPool[0].RequestSpawn(pos);
            else relicPool[0].SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RelicPickup.RequestSpawn), pos);
        }

        /// <summary>
        /// RaceManager からステージ開始前に全クライアントで（何度でも）呼ばれる。Lv1・クールダウン解除・命中数クリア。
        /// レース中は同じステージ（stageSeq）で 2 回目以降を無視する（遅れて届いたリセットで拾った遺物を消さない）。
        /// </summary>
        public void ResetForStage()
        {
            EnsureInit();
            if (raceManager != null && raceManager.state == RaceMath.STATE_RACING)
            {
                if (lastResetSeq == raceManager.stageSeq) return;
            }
            if (raceManager != null) lastResetSeq = raceManager.stageSeq;
            Level = 1;
            readyAt = 0f;
            hazeUntil = 0f;
            guardUntil = 0f;
            mirageStart = -1000f;
            mirageUntil = -1000f;
            for (int i = 0; i < hitTimes.Length; i++) hitTimes[i] = -1000f;
            for (int i = 0; i < MAX_CASTERS; i++)
            {
                hitsFromShooter[i] = 0;
                dropsForShooter[i] = 0;
            }
            lastDropAt = -1000f;
        }

        // =====================================================================
        // 腰のお守り袋・砂のかすみ（ローカル）
        // =====================================================================

        private void UpdatePouch(bool local)
        {
            if (pouch == null) return;
            if (local)
            {
                // 開幕の猶予の間は「封」（使えない札）
                // 開幕の解禁（レーン順、system-r2-11）までは「封」とその秒数（各自の札に自分の番が出る）
                float open = OpeningLeft();
                bool locked = open > 0f;
                float rem = locked ? open : readyAt - Time.time;
                MinimalHud hud = raceManager != null ? raceManager.hud : null;
                if (pouch.hud == null) pouch.hud = hud;
                VRCPlayerApi lp = Networking.LocalPlayer;
                bool vr = lp != null && lp.IsUserInVR();
                pouch.hint = hud != null && hud.abilityCastCount < HudMath.ABILITY_HINT_CASTS ? HudMath.AbilityHint(vr) : "";
                pouch.Show(IsReady() && !locked, Level, rem, AbilityId(), mount != null ? mount.SeatType() : 0);
                pouchShownLocal = true;
            }
            else if (pouchShownLocal)
            {
                pouch.Hide();
                pouchShownLocal = false;
            }
        }

        private void UpdateHaze(bool local)
        {
            if (haze == null) return;
            bool on = local && Time.time < hazeUntil;
            if (haze.activeSelf != on) haze.SetActive(on);
            if (!on) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            haze.transform.position = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        }
    }
}
