using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 遺物（拾うと能力 Lv +1、上限 3）。見た目は Packs/Gems の宝石（色違いの 1 つを遺物として使う）を visual に。
    /// Is Trigger の Collider と同じ GameObject に置く。乗り物（MountController の Collider ＋ kinematic Rigidbody）が入ると OnTriggerEnter。
    ///
    /// コースの遺物（isSpare = false）は一人ずつ（system-r1-04、順位ベースの逆転）：
    ///  - どのクライアントも自分の「拾った」印を epoch ごとに持ち、拾ったら自分の画面でだけ消える。誰でも各遺物を 1 回ずつ拾える（先着の取り合いなし）。
    ///  - 上がる Lv は順位で決まる（AbilityCaster.RelicGainNow：1 位 0、上半分 +1、下半分 +2）。1 位には遺物が小さく暗く見え、拾えない。
    ///  - practiceRespawnSeconds &gt; 0（練習場）なら、拾ってからその秒数で自分の画面に戻る。ResetForStage（オーナー）で epoch が進むと全員ぶん戻る。
    ///
    /// 予備（落とした遺物）だけは先着で取り合う（spec 5.3 / ruling 3）:
    ///  1. ローカルの騎手の乗り物が触れたクライアントが、オーナーへ Claim(epoch) を送る（NetworkEventTarget.Owner）。
    ///  2. オーナーは AbilityMath.RelicArbiter(holder, 送ってきた人の playerId) で holder を決め、同期（Manual）する。
    ///  3. holder == 自分 のクライアントだけが、触れた乗り物の AbilityCaster.GrantRelic を 1 回（epoch ごとに 1 回）呼ぶ。
    ///
    /// 予備（isSpare = true）は「落とした遺物」用。普段は出ていない。AbilityCaster.DropRelic が pool[0] のオーナーへ RequestSpawn(pos) を送り、
    /// pool の中で空いているものをそのオーナーが所有して pos に出す。ステージのリセットで全部しまう。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RelicPickup : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("落とした遺物用の予備なら ON（普段は出ていない）")]
        public bool isSpare;
        [Tooltip("予備のとき：予備全部（自分を含む、全予備で同じ配列）")]
        public RelicPickup[] pool;
        public GameObject visual;
        public AudioSource pickupSound;
        public LayerMask groundMask = -1;
        public float hoverHeight = 0.8f;
        public float spinSpeed = 60f;
        [Tooltip("落とした遺物を拾えるようになるまでの秒数（落とした本人がすぐ拾い直さないように）")]
        public float armDelay = 1.0f;
        [Tooltip("0 より大きければ、拾われてからこの秒数で戻る（練習場用）")]
        public float practiceRespawnSeconds = 0f;
        [Tooltip("1 位（拾えない）のときの見た目の大きさ")]
        public float dimScale = 0.55f;

        /// <summary>拾った人の playerId（-1 = まだ誰も拾っていない）。</summary>
        [UdonSynced] public int holder = -1;
        /// <summary>リセット・出現のたびに +1。拾った報酬は epoch ごとに 1 回だけ。</summary>
        [UdonSynced] public int epoch;
        /// <summary>予備のとき：出ているか。</summary>
        [UdonSynced] public bool spareActive;
        /// <summary>予備のとき：出ている位置。</summary>
        [UdonSynced] public Vector3 spawnPos;

        private Vector3 homePos;
        private int grantedEpoch = -1;
        private int appliedEpoch = -1;
        private int claimEpoch = -1;
        private float claimTime;
        private float armAt;
        private float pickedAt;
        private bool localHidden;
        private int lastResetSeq = -1;
        private AbilityCaster pendingCaster;
        private bool started;
        // コースの遺物（一人ずつ）
        private int localTakenEpoch = -1;
        private float localTakenAt;
        private bool dimmed;
        private float nextDimCheck;
        private Vector3 visualScale = Vector3.one;

        private void Start()
        {
            EnsureInit();
            Apply();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            homePos = transform.position;
            if (visual != null) visualScale = visual.transform.localScale;
            if (visual == null) Debug.LogWarning("[HorseRace] RelicPickup: visual（Packs/Gems の宝石）が未設定です " + gameObject.name);
        }

        public bool IsAvailable()
        {
            if (!isSpare) return !LocallyTaken();
            if (holder >= 0) return false;
            return spareActive;
        }

        /// <summary>コースの遺物：自分がこの epoch で拾った（練習場は戻るまでの間）。</summary>
        private bool LocallyTaken()
        {
            if (localTakenEpoch != epoch) return false;
            if (practiceRespawnSeconds > 0f && Time.time - localTakenAt > practiceRespawnSeconds) return false;
            return true;
        }

        // =====================================================================
        // 拾う（ローカルの騎手のクライアント）
        // =====================================================================

        public void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            EnsureInit();
            if (!IsAvailable() || localHidden) return;
            if (Time.time < armAt) return;
            MountController m = other.GetComponentInParent<MountController>();
            if (m == null || !m.IsLocalRider()) return;
            if (other.gameObject != m.gameObject) return; // 体の Collider だけ（子の Collider で二重に拾いに行かない）
            AbilityCaster caster = m.GetComponent<AbilityCaster>();
            if (caster == null) return;
            if (!m.raceActive && !caster.practiceMount) return;
            int gain = caster.RelicGainNow();
            if (gain <= 0) return;   // 1 位は拾えない（暗く見えている）
            if (!isSpare)
            {
                // コースの遺物：自分の画面だけで拾う（オーナーに聞かない）
                localTakenEpoch = epoch;
                localTakenAt = Time.time;
                caster.GrantRelic(gain);
                if (pickupSound != null) pickupSound.Play();
                ApplyVisual();
                return;
            }
            if (claimEpoch == epoch && Time.time - claimTime < 2f) return;
            claimEpoch = epoch;
            claimTime = Time.time;
            pendingCaster = caster;
            // 見た目だけ先に消す（負けたら次の同期で戻る）
            localHidden = true;
            SendCustomEventDelayedSeconds(nameof(_ClearLocalHidden), 3f);
            ApplyVisual();
            // 自分がオーナー（マスターが拾った等）なら直接判定する
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Networking.IsOwner(gameObject) && local != null) ClaimBy(local.playerId, epoch);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Claim), epoch);
        }

        public void _ClearLocalHidden()
        {
            if (!localHidden) return;
            localHidden = false;
            ApplyVisual();
        }

        // =====================================================================
        // オーナー側
        // =====================================================================

        [NetworkCallable(10)]
        public void Claim(int forEpoch)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!isSpare) return;   // コースの遺物は取り合わない
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            ClaimBy(caller.playerId, forEpoch);
        }

        /// <summary>オーナー側の先着判定（Claim と、オーナー自身が触れたとき）。</summary>
        private void ClaimBy(int playerId, int forEpoch)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (forEpoch != epoch) return;
            if (!isSpare || !spareActive) return;
            int newHolder = AbilityMath.RelicArbiter(holder, playerId);
            if (newHolder == holder) return;
            holder = newHolder;
            pickedAt = Time.time;
            RequestSerialization();
            Apply();
        }

        /// <summary>予備の遺物を pos（の地面）に出す。pool[0] のオーナーへ送られる。</summary>
        [NetworkCallable(10)]
        public void RequestSpawn(Vector3 pos)
        {
            if (!Networking.IsOwner(gameObject)) return;
            RelicPickup[] list = pool;
            if (list == null || list.Length == 0) { SpawnAt(pos); return; }
            RelicPickup pick = null;
            for (int i = 0; i < list.Length; i++)
            {
                RelicPickup r = list[i];
                if (r == null || !r.isSpare) continue;
                if (!r.spareActive) { pick = r; break; }
            }
            if (pick == null)
            {
                for (int i = 0; i < list.Length; i++)
                {
                    RelicPickup r = list[i];
                    if (r == null || !r.isSpare) continue;
                    if (r.holder >= 0) { pick = r; break; } // 拾われた後のものを使い回す
                }
            }
            if (pick == null)
            {
                Debug.LogWarning("[HorseRace] RelicPickup: 予備の遺物が足りません");
                return;
            }
            pick.SpawnAt(pos);
        }

        /// <summary>ローカルで所有権を取り、pos の地面に出す（RequestSpawn から）。</summary>
        public void SpawnAt(Vector3 pos)
        {
            EnsureInit();
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            RaycastHit hit;
            Vector3 p = pos;
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out hit, 50f, groundMask, QueryTriggerInteraction.Ignore))
                p = hit.point;
            spawnPos = p + Vector3.up * hoverHeight;
            spareActive = true;
            holder = -1;
            epoch++;
            RequestSerialization();
            Apply();
        }

        /// <summary>
        /// RaceManager からステージ開始前に全クライアントで（何度でも）呼ばれる。オーナーだけが元に戻して同期する。
        /// レース中は同じステージで 2 回目以降を無視する（遅れたリセットで拾われた遺物を復活させない）。
        /// </summary>
        public void ResetForStage()
        {
            EnsureInit();
            if (raceManager != null && raceManager.state == RaceMath.STATE_RACING)
            {
                if (lastResetSeq == raceManager.stageSeq) return;
            }
            if (raceManager != null) lastResetSeq = raceManager.stageSeq;
            if (!Networking.IsOwner(gameObject)) return;
            holder = -1;
            spareActive = false;
            epoch++;
            RequestSerialization();
            Apply();
        }

        private void Update()
        {
            if (visual != null && visual.activeSelf && spinSpeed != 0f)
                visual.transform.Rotate(0f, (dimmed ? 0.3f : 1f) * spinSpeed * Time.deltaTime, 0f);
            if (Time.time >= nextDimCheck)
            {
                nextDimCheck = Time.time + 0.5f;
                UpdateDim();
                ApplyVisual();   // 練習場の遺物が戻る
            }
        }

        // =====================================================================
        // 反映（全員）
        // =====================================================================

        public override void OnDeserialization()
        {
            Apply();
        }

        private void Apply()
        {
            EnsureInit();
            if (epoch != appliedEpoch)
            {
                appliedEpoch = epoch;
                localHidden = false;
                armAt = isSpare ? Time.time + armDelay : 0f;
            }
            if (isSpare && spareActive) transform.position = spawnPos;
            else if (!isSpare) transform.position = homePos;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (isSpare && local != null && holder >= 0 && holder == local.playerId && grantedEpoch != epoch)
            {
                grantedEpoch = epoch;
                localHidden = false;
                AbilityCaster c = pendingCaster;
                if (c == null || c.mount == null || !c.mount.IsLocalRider()) c = FindLocalCaster();
                if (c != null)
                {
                    int g = c.RelicGainNow();
                    c.GrantRelic(g > 0 ? g : 1);   // 触れたときは拾えた順位だった
                }
                if (pickupSound != null) pickupSound.Play();
            }
            if (holder >= 0 && holder != (local != null ? local.playerId : -2)) localHidden = false;
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (visual == null) return;
            bool show = IsAvailable() && !localHidden;
            if (visual.activeSelf != show) visual.SetActive(show);
        }

        /// <summary>ローカルの騎手が 1 位（拾っても Lv が上がらない）なら遺物を小さく・ゆっくり回して見せる。</summary>
        private void UpdateDim()
        {
            bool dim = false;
            AbilityCaster c = FindLocalCaster();
            if (c != null && c.mount != null && c.mount.raceActive && c.RelicGainNow() <= 0) dim = true;
            if (dim == dimmed) return;
            dimmed = dim;
            if (visual != null) visual.transform.localScale = dim ? visualScale * dimScale : visualScale;
        }

        private AbilityCaster FindLocalCaster()
        {
            if (raceManager == null) return null;
            MountController m = raceManager.LocalMount();
            if (m == null) return null;
            return m.GetComponent<AbilityCaster>();
        }
    }
}
