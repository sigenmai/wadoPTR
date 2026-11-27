using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 乗り物 1 頭ぶんのリボルバー（spec 5.2b）。Revolver.fbx（部品は組んだまま）のルートに VRC_Pickup（Auto Hold = Yes）と一緒に置く。
    /// 普段は鞍の脇のホルスター（乗り物の子）にあり、手を離すとそこへ戻る。VRCObjectSync は付けない。
    /// ほかの人から見た銃: 乗っている本人（＝この GameObject のオーナー。乗ったら SetOwner）が構え（syncAiming）・左右の手・狙う向き（syncAimDir）を
    /// Manual 同期で送る（構えの開始・終了で 1 回、構え中は 10Hz）。受け取った側は、構え中は乗り手の手のボーン（GetBonePosition）に銃を置いて
    /// 銃口を狙う向きへなめらかに向け、構えていなければ右腰（Hips ボーン＋holsterHipOffset、ボーンがなければ鞍のホルスター）に出す。
    /// 撃鉄・シリンダー・銃口の火と煙・銃声は FxShot イベントで全員に出す。
    ///
    /// 操作:
    ///  - VR：手でつかんで構える（持っている間ずっと「構え中」）、トリガー（OnPickupUseDown）で撃つ。照準は銃口（muzzle）の向き。
    ///  - デスクトップ：右クリックを押している間構える（銃を視線の前に出す）、左クリックで撃つ。照準は頭の向き。
    ///  - 構え中は自分に AddSpeedMod(AIM_FACTOR, 0.2) を切れ目なく掛け直す（重ならないよう、前の分が切れたら次を掛ける）。
    /// 発射（撃った人のクライアントで判定）:
    ///  - AbilityCaster.FindTarget（球キャスト＋地形の遮り）で相手の乗り物・騎手の当たり判定に当たったか。
    ///  - 命中：相手の AbilityCaster.HitRevolver(向き, 自分の casterId, 何発目か) を相手のオーナーへ。×0.6 1 秒とよろけ、3 発目の遺物は相手側で判定。
    ///  - 外れ：自分に AddSpeedMod(MISS_FACTOR, MISS_SECONDS)。
    ///  - 見た目（全員）：FxShot で残弾（Bullet1..6 の表示）・撃鉄・シリンダー 60°・銃口の火と煙・銃声。
    /// 残弾は 1 ステージ 6 発（Refill で補充）。練習場（PracticeArea.infiniteAmmo）では減らない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RevolverHolder : UdonSharpBehaviour
    {
        public const float AIM_REFRESH_SECONDS = 0.2f;
        public const float CYLINDER_STEP_DEG = 60f;
        public const float AIM_SEND_INTERVAL = 0.1f;   // 構え中の同期 10Hz

        [Tooltip("同じ乗り物の AbilityCaster（mount・casterId・判定レイヤー・練習フラグを使う）")]
        public AbilityCaster caster;
        public VRC_Pickup pickup;

        [Header("Revolver.fbx の部品")]
        public Transform cylinder;           // Revolving_Cylinder
        public Vector3 cylinderAxis = new Vector3(0f, 0f, 1f);
        public Transform hammer;             // Hammer
        public Vector3 hammerAxis = new Vector3(1f, 0f, 0f);
        public float hammerAngle = 35f;
        public Transform triggerPart;        // Trigger
        public Vector3 triggerAxis = new Vector3(1f, 0f, 0f);
        public float triggerAngle = 15f;
        [Tooltip("Bullet1..Bullet6（残弾 = 表示されている数）")]
        public GameObject[] bullets;
        [Tooltip("銃口（z+ が弾の向き）")]
        public Transform muzzle;

        [Header("演出")]
        [Tooltip("銃口の火・煙をまとめた親。発射位置へ動かす（Start で親から外す）")]
        public Transform fxRoot;
        public ParticleSystem muzzleFlash;
        public ParticleSystem smoke;
        public AudioSource gunshot;
        public AudioSource dryFire;
        [Tooltip("任意。弾道を一瞬だけ出す線")]
        public LineRenderer tracer;
        public float tracerSeconds = 0.06f;

        [Header("調整")]
        public float range = 150f;
        public float hitRadius = 0.25f;
        public float fireInterval = 0.35f;
        public float hammerTime = 0.18f;
        [Tooltip("デスクトップで構えたときの銃の位置（頭基準）")]
        public Vector3 desktopAimOffset = new Vector3(0.18f, -0.18f, 0.45f);
        [Tooltip("手のボーンから見たグリップ位置（銃の向き基準）")]
        public Vector3 handGripOffset = new Vector3(0f, 0f, 0f);
        [Tooltip("腰のボーン（Hips）から見たホルスター位置（ボーンの向き基準。右腰）")]
        public Vector3 holsterHipOffset = new Vector3(0.18f, -0.05f, 0f);
        public Vector3 holsterHipEuler = new Vector3(90f, 0f, 0f);
        [Tooltip("ほかの人から見た銃の向きの追従の速さ")]
        public float aimSmoothing = 15f;

        // ---- 同期（乗っている本人＝オーナーが書く）----
        [UdonSynced] public bool syncAiming;
        [UdonSynced] public bool syncLeftHand;
        [UdonSynced] public Vector3 syncAimDir;

        /// <summary>残弾（表示用。撃った人の値が Fx で全員に配られる）。</summary>
        public int ammo = RevolverMath.AMMO_PER_STAGE;

        private int[] hitsOnTarget;
        private float aimModUntil;
        private float nextFire;
        private float shotTime = -10f;
        private float tracerOffAt;
        private int cylinderSteps;
        private float cylinderShown;
        private bool desktopAiming;
        private bool hammerHomed = true;
        private int lastResetSeq = -1;
        private Transform homeParent;
        private Vector3 homeLocalPos;
        private Quaternion homeLocalRot;
        private Quaternion cylinderHome;
        private Quaternion hammerHome;
        private Quaternion triggerHome;
        private Quaternion muzzleLocalRot = Quaternion.identity;
        private Quaternion shownRot = Quaternion.identity;
        private bool shownRotValid;
        private bool remotePosed;
        private float nextAimSend;
        private float nextOwnerTry;
        private bool started;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            hitsOnTarget = new int[AbilityCaster.MAX_CASTERS];
            homeParent = transform.parent;
            homeLocalPos = transform.localPosition;
            homeLocalRot = transform.localRotation;
            if (muzzle != null) muzzleLocalRot = Quaternion.Inverse(transform.rotation) * muzzle.rotation;
            if (cylinder != null) cylinderHome = cylinder.localRotation;
            if (hammer != null) hammerHome = hammer.localRotation;
            if (triggerPart != null) triggerHome = triggerPart.localRotation;
            if (fxRoot != null) fxRoot.parent = null;
            if (tracer != null) { tracer.positionCount = 2; tracer.enabled = false; }
            if (caster == null) Debug.LogWarning("[HorseRace] RevolverHolder: caster（AbilityCaster）が未設定です " + gameObject.name);
            if (bullets == null || bullets.Length != RevolverMath.AMMO_PER_STAGE) Debug.LogWarning("[HorseRace] RevolverHolder: bullets に Bullet1..6 を設定してください");
            UpdateBullets();
        }

        private MountController Mount()
        {
            return caster != null ? caster.mount : null;
        }

        /// <summary>ローカルのプレイヤーが hand（AbilityCaster.AIM_LEFT / AIM_RIGHT）の手でこの銃を持っているか。</summary>
        public bool IsHeldInLocalHand(int hand)
        {
            if (!IsLocalHeld()) return false;
            VRC_Pickup.PickupHand h = pickup.currentHand;
            if (hand == AbilityCaster.AIM_LEFT) return h == VRC_Pickup.PickupHand.Left;
            if (hand == AbilityCaster.AIM_RIGHT) return h == VRC_Pickup.PickupHand.Right;
            return false;
        }

        private bool IsLocalHeld()
        {
            if (pickup == null || !pickup.IsHeld) return false;
            VRCPlayerApi p = pickup.currentPlayer;
            return p != null && p.isLocal;
        }

        private bool IsPractice()
        {
            return caster != null && caster.practiceMount;
        }

        private bool InfiniteAmmo()
        {
            if (!IsPractice()) return false;
            PracticeArea pa = caster.practiceArea;
            return pa == null || pa.infiniteAmmo;
        }

        /// <summary>今撃てるか（弾・レース状態）。練習用の馬は練習場の中ならいつでも。</summary>
        public bool CanFire()
        {
            MountController m = Mount();
            if (m == null || !m.IsLocalRider()) return false;
            if (IsPractice())
            {
                PracticeArea pa = caster.practiceArea;
                if (pa != null && !pa.inPractice) return false;
                return InfiniteAmmo() || ammo > 0;
            }
            if (!m.raceActive || m.InputLocked || m.syncFinishAt >= 0.0) return false;
            int st = caster.raceManager != null ? caster.raceManager.state : RaceMath.STATE_RACING;
            return RevolverMath.CanShoot(ammo, st);
        }

        // =====================================================================
        // 更新
        // =====================================================================

        private void Update()
        {
            EnsureInit();
            MountController m = Mount();
            VRCPlayerApi local = Networking.LocalPlayer;
            bool rider = m != null && m.IsLocalRider();
            bool vr = local != null && local.IsUserInVR();

            if (pickup != null)
            {
                bool can = rider && vr;
                if (pickup.pickupable != can) pickup.pickupable = can;
                if (!rider && IsLocalHeld()) pickup.Drop();
            }

            bool aiming = false;
            if (rider)
            {
                if (vr) aiming = IsLocalHeld();
                else aiming = Input.GetMouseButton(1);
            }

            if (aiming && Time.time >= aimModUntil)
            {
                m.AddSpeedMod(RevolverMath.AIM_FACTOR, AIM_REFRESH_SECONDS);
                aimModUntil = Time.time + AIM_REFRESH_SECONDS;
            }

            Vector3 dir = Vector3.forward;
            bool leftHand = false;
            if (rider && local != null)
            {
                if (vr)
                {
                    Transform t = muzzle != null ? muzzle : transform;
                    dir = t.forward;
                    leftHand = IsLocalHeld() && pickup.currentHand == VRC_Pickup.PickupHand.Left;
                }
                else
                {
                    VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    dir = head.rotation * Vector3.forward;
                }
            }

            // 構えの同期（乗っている本人＝オーナーが書く）
            if (rider) SyncAimLocal(local, aiming, dir, leftHand);
            else if (syncAiming && Networking.IsOwner(gameObject))
            {
                // 降りた・乗り手が替わった：構えを解いて伝える
                syncAiming = false;
                RequestSerialization();
            }

            if (rider && !vr && aiming && local != null)
            {
                // デスクトップ：自分の右手（ボーンがなければ視線の前）に銃を出す
                if (!PlaceInHand(local, false, dir))
                {
                    VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    transform.SetPositionAndRotation(head.position + head.rotation * desktopAimOffset, head.rotation * InvMuzzle());
                }
                desktopAiming = true;
                if (Input.GetMouseButtonDown(0))
                {
                    VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                    Fire(head.position, dir);
                }
            }
            else if (desktopAiming)
            {
                desktopAiming = false;
                ReturnHome();
            }

            if (!rider) UpdateRemotePose(m);

            Animate();
        }

        // =====================================================================
        // 構えの同期と、ほかの人から見た銃の位置
        // =====================================================================

        private void SyncAimLocal(VRCPlayerApi local, bool aiming, Vector3 dir, bool leftHand)
        {
            if (local == null) return;
            if (!Networking.IsOwner(gameObject))
            {
                if (Time.time >= nextOwnerTry)
                {
                    nextOwnerTry = Time.time + 1f;
                    Networking.SetOwner(local, gameObject);
                }
                return;
            }
            bool changed = aiming != syncAiming || leftHand != syncLeftHand;
            if (changed || (aiming && Time.time >= nextAimSend))
            {
                syncAiming = aiming;
                syncLeftHand = leftHand;
                syncAimDir = dir;
                nextAimSend = Time.time + AIM_SEND_INTERVAL;
                RequestSerialization();
            }
        }

        /// <summary>乗っていないクライアント：構え中は乗り手の手、構えていなければ右腰（ボーンがなければ鞍のホルスター）。</summary>
        private void UpdateRemotePose(MountController m)
        {
            if (IsLocalHeld()) return;
            VRCPlayerApi rp = (m != null && m.riderPlayerId >= 0) ? VRCPlayerApi.GetPlayerById(m.riderPlayerId) : null;
            if (rp == null || !rp.IsValid())
            {
                if (remotePosed) { remotePosed = false; ReturnHome(); }
                return;
            }
            if (syncAiming)
            {
                Vector3 d = syncAimDir;
                if (d.sqrMagnitude < 0.0001f) d = m.transform.forward;
                if (PlaceInHand(rp, syncLeftHand, d)) { remotePosed = true; return; }
            }
            else
            {
                Vector3 hip = rp.GetBonePosition(HumanBodyBones.Hips);
                if (hip.sqrMagnitude > 0.0001f)
                {
                    Quaternion hr = rp.GetBoneRotation(HumanBodyBones.Hips);
                    transform.SetPositionAndRotation(hip + hr * holsterHipOffset, hr * Quaternion.Euler(holsterHipEuler));
                    shownRotValid = false;
                    remotePosed = true;
                    return;
                }
            }
            if (remotePosed) { remotePosed = false; ReturnHome(); }
        }

        /// <summary>player の手のボーンに銃を置き、銃口を dir へ（なめらかに）向ける。ボーンが取れなければ false。</summary>
        private bool PlaceInHand(VRCPlayerApi player, bool leftHand, Vector3 dir)
        {
            HumanBodyBones bone = leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Vector3 hand = player.GetBonePosition(bone);
            if (hand.sqrMagnitude < 0.0001f) return false;
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up) * InvMuzzle();
            float k = 1f - Mathf.Exp(-aimSmoothing * Time.deltaTime);
            shownRot = shownRotValid ? Quaternion.Slerp(shownRot, target, k) : target;
            shownRotValid = true;
            transform.SetPositionAndRotation(hand + shownRot * handGripOffset, shownRot);
            return true;
        }

        /// <summary>銃のルートから見た銃口の向きの逆（ルートの回転 = 銃口の向き × これ）。</summary>
        private Quaternion InvMuzzle()
        {
            return Quaternion.Inverse(muzzleLocalRot);
        }

        public override void OnPickupUseDown()
        {
            MountController m = Mount();
            if (m == null || !m.IsLocalRider()) return;
            Transform t = muzzle != null ? muzzle : transform;
            Fire(t.position, t.forward);
        }

        public override void OnDrop()
        {
            ReturnHome();
        }

        private void ReturnHome()
        {
            shownRotValid = false;
            if (homeParent != null && transform.parent != homeParent) transform.parent = homeParent;
            transform.localPosition = homeLocalPos;
            transform.localRotation = homeLocalRot;
        }

        // =====================================================================
        // 発射（撃った人のクライアント）
        // =====================================================================

        private void Fire(Vector3 origin, Vector3 dir)
        {
            if (Time.time < nextFire) return;
            nextFire = Time.time + fireInterval;
            if (!CanFire())
            {
                if (dryFire != null) dryFire.Play();
                return;
            }
            MountController m = Mount();
            dir = dir.normalized;
            bool hit = caster.FindTarget(origin, dir, range, hitRadius);
            Vector3 end = caster.foundPoint;
            if (!InfiniteAmmo()) ammo = RevolverMath.AmmoAfterShot(ammo);

            if (hit)
            {
                if (caster.foundMount != null)
                {
                    AbilityCaster tc = caster.foundMount.GetComponent<AbilityCaster>();
                    if (tc != null && tc.casterId >= 0 && tc.casterId < AbilityCaster.MAX_CASTERS)
                    {
                        int before = hitsOnTarget[tc.casterId];
                        hitsOnTarget[tc.casterId] = RevolverMath.RegisterHit(before);
                        tc.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AbilityCaster.HitRevolver), dir, caster.casterId, before + 1);
                    }
                }
                else if (caster.foundDummy != null)
                {
                    caster.foundDummy.Wobble();
                }
            }
            else
            {
                m.AddSpeedMod(RevolverMath.MISS_FACTOR, RevolverMath.MISS_SECONDS);
            }

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null && IsLocalHeld()) local.PlayHapticEventInHand(pickup.currentHand, 0.12f, 1f, 200f);
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxShot), origin, end, ammo);
        }

        // =====================================================================
        // 見た目（全員）
        // =====================================================================

        [NetworkCallable(10)]
        public void FxShot(Vector3 origin, Vector3 end, int ammoLeft)
        {
            EnsureInit();
            ammo = Mathf.Clamp(ammoLeft, 0, RevolverMath.AMMO_PER_STAGE);
            UpdateBullets();
            cylinderSteps = (cylinderSteps + 1) % 6;
            shotTime = Time.time;
            hammerHomed = false;
            if (fxRoot != null)
            {
                Vector3 d = end - origin;
                fxRoot.position = origin;
                if (d.sqrMagnitude > 0.0001f) fxRoot.rotation = Quaternion.LookRotation(d);
            }
            if (muzzleFlash != null) muzzleFlash.Play();
            if (smoke != null) smoke.Play();
            if (gunshot != null) gunshot.Play();
            // 中継カメラ（Task 10）：着弾点のすぐ近くに乗り物がいれば命中の場面とみなす（ローカルのみ）
            if (caster != null && caster.spectator != null) caster.spectator.NotifyHitNear(end, caster.mount);
            if (tracer != null)
            {
                tracer.SetPosition(0, origin);
                tracer.SetPosition(1, end);
                tracer.enabled = true;
                tracerOffAt = Time.time + tracerSeconds;
            }
        }

        private void Animate()
        {
            if (tracer != null && tracer.enabled && Time.time >= tracerOffAt) tracer.enabled = false;

            // シリンダー：1 発ごとに 60° 回す（0.1 秒で追いつく）
            if (cylinder != null)
            {
                float target = cylinderSteps * CYLINDER_STEP_DEG;
                if (cylinderShown > target + 1f) cylinderShown -= 360f; // 一周したとき
                float next = Mathf.MoveTowards(cylinderShown, target, CYLINDER_STEP_DEG * 10f * Time.deltaTime);
                if (next != cylinderShown)
                {
                    cylinderShown = next;
                    cylinder.localRotation = cylinderHome * Quaternion.AngleAxis(cylinderShown, cylinderAxis);
                }
            }

            // 撃鉄と引き金：起きて（前半）落ちる（後半）
            float t = (Time.time - shotTime) / hammerTime;
            if (t > 1f && !hammerHomed)
            {
                hammerHomed = true;
                if (hammer != null) hammer.localRotation = hammerHome;
                if (triggerPart != null) triggerPart.localRotation = triggerHome;
            }
            if (t >= 0f && t <= 1f)
            {
                float k = t < 0.5f ? t * 2f : Mathf.Max(0f, 2f - t * 2f);
                if (hammer != null) hammer.localRotation = hammerHome * Quaternion.AngleAxis(-hammerAngle * k, hammerAxis);
                if (triggerPart != null) triggerPart.localRotation = triggerHome * Quaternion.AngleAxis(triggerAngle * k, triggerAxis);
            }
        }

        private void UpdateBullets()
        {
            if (bullets == null) return;
            int show = InfiniteAmmo() ? bullets.Length : ammo;
            for (int i = 0; i < bullets.Length; i++)
                if (bullets[i] != null) bullets[i].SetActive(i < show);
        }

        /// <summary>
        /// RaceManager からステージ開始前に全クライアントで（何度でも）呼ばれる。弾 6 発・命中数クリア。
        /// レース中は同じステージで 2 回目以降を無視する。
        /// </summary>
        public void Refill()
        {
            EnsureInit();
            RaceManager rm = caster != null ? caster.raceManager : null;
            if (rm != null && rm.state == RaceMath.STATE_RACING)
            {
                if (lastResetSeq == rm.stageSeq) return;
            }
            if (rm != null) lastResetSeq = rm.stageSeq;
            ammo = RevolverMath.AMMO_PER_STAGE;
            for (int i = 0; i < hitsOnTarget.Length; i++) hitsOnTarget[i] = 0;
            UpdateBullets();
        }

        /// <summary>stageResetTargets に入れても同じ動作をするように。</summary>
        public void ResetForStage()
        {
            Refill();
        }
    }
}
