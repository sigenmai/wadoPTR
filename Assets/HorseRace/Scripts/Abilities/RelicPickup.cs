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
    /// 先着の確定（spec 5.3 / ruling 3）:
    ///  1. ローカルの騎手の乗り物が触れたクライアントが、オーナー（既定はマスター）へ Claim(epoch) を送る（NetworkEventTarget.Owner）。
    ///  2. オーナーは AbilityMath.RelicArbiter(holder, 送ってきた人の playerId) で holder を決め、同期（Manual）する。
    ///     2 人が同時に触れても、オーナーに先に届いた 1 人だけが holder になる。
    ///  3. holder == 自分 のクライアントだけが、触れた乗り物の AbilityCaster.GrantRelic を 1 回（epoch ごとに 1 回）呼ぶ。
    ///  拾われた遺物はそのステージ中は消える。ResetForStage（オーナーだけが実行）で元に戻る。
    ///
    /// 予備（isSpare = true）は「落とした遺物」用。普段は出ていない。AbilityCaster.DropRelic が pool[0] のオーナーへ RequestSpawn(pos) を送り、
    /// pool の中で空いているものをそのオーナーが所有して pos に出す。ステージのリセットで全部しまう。
    /// practiceRespawnSeconds &gt; 0 なら（練習場用）拾われてからその秒数で同じ場所に戻る。
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
            if (visual == null) Debug.LogWarning("[HorseRace] RelicPickup: visual（Packs/Gems の宝石）が未設定です " + gameObject.name);
        }

        public bool IsAvailable()
        {
            if (holder >= 0) return false;
            if (isSpare) return spareActive;
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
            AbilityCaster caster = m.GetComponent<AbilityCaster>();
            if (caster == null) return;
            if (!m.raceActive && !caster.practiceMount) return;
            if (claimEpoch == epoch && Time.time - claimTime < 2f) return;
            claimEpoch = epoch;
            claimTime = Time.time;
            pendingCaster = caster;
            // 見た目だけ先に消す（負けたら次の同期で戻る）
            localHidden = true;
            SendCustomEventDelayedSeconds(nameof(_ClearLocalHidden), 3f);
            ApplyVisual();
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(Claim), epoch);
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
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            if (forEpoch != epoch) return;
            if (isSpare && !spareActive) return;
            int newHolder = AbilityMath.RelicArbiter(holder, caller.playerId);
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
                visual.transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
            if (practiceRespawnSeconds > 0f && holder >= 0 && Networking.IsOwner(gameObject)
                && Time.time - pickedAt > practiceRespawnSeconds)
            {
                holder = -1;
                epoch++;
                RequestSerialization();
                Apply();
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
            if (local != null && holder >= 0 && holder == local.playerId && grantedEpoch != epoch)
            {
                grantedEpoch = epoch;
                localHidden = false;
                AbilityCaster c = pendingCaster;
                if (c == null || c.mount == null || !c.mount.IsLocalRider()) c = FindLocalCaster();
                if (c != null) c.GrantRelic();
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

        private AbilityCaster FindLocalCaster()
        {
            if (raceManager == null) return null;
            MountController m = raceManager.LocalMount();
            if (m == null) return null;
            return m.GetComponent<AbilityCaster>();
        }
    }
}
