using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 乗り物の見た目。syncMount / syncCoat で Horse / Camel / SkeletonHorse のモデルを切り替え、
    /// 馬は HorseMaterialBuilder の毛色マテリアル（coat 0..5）を貼る。coat 6 は骨の馬。
    /// Animator（全モデル共通の Mount.controller）に歩様 "Gait"、"Jump"/"Stagger" トリガー、"Stamp" を渡し、
    /// speed = 実速度 / アニメの基準速度（MountMath.AnimRefSpeed）。クロスフェード 0.2 秒はコントローラ側（MountAnimatorBuilder）。
    /// スタミナは息づかい音（低スタミナで荒く）と耳を伏せる動きで見せる。
    /// 蹄（system-r3-09）：Animator の 1 周（normalizedTime % 1）の中の足音の位置（SfxMath.HoofBeats：常歩 4・速歩 2・駆歩 3・襲歩 4 拍）を
    ///   通るたびに hooves で蹄の音（高さ 0.9〜1.1）。蹄の砂ぼこり（hoofDust）は歩様ごとに 0/4/10/22 粒/秒、砂の上 ×1.6、空中 0。
    ///   ほかの人の乗り物も同期した歩様（syncGait）で鳴るので、後ろから迫る相手が聞こえる。自分が疾走中は拍ごとに手も軽く振るわせる（r3-16）。
    /// 蜃気楼（system-r3-10）：SetFlickerHidden で体の Renderer を消す／戻す（AbilityCaster が 8 Hz で切り替える）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MountVisual : UdonSharpBehaviour
    {
        public const int COAT_SKELETON = 6;

        [Header("Models (children of this object)")]
        public GameObject horseModel;
        public GameObject camelModel;
        public GameObject skeletonModel;
        public Animator horseAnimator;
        public Animator camelAnimator;
        public Animator skeletonAnimator;
        /// <summary>Mount.controller / Mount_Camel.overrideController / Mount_SkeletonHorse.overrideController。</summary>
        public RuntimeAnimatorController horseController;
        public RuntimeAnimatorController camelController;
        public RuntimeAnimatorController skeletonController;
        /// <summary>毛色を貼る馬モデルのレンダラー。</summary>
        public Renderer[] horseCoatRenderers;
        /// <summary>Horse_Bay, Horse_Chestnut, Horse_Black, Horse_Gray, Horse_Palomino, Horse_Dun の順（coat 0..5）。</summary>
        public Material[] coatMaterials;
        /// <summary>坂でモデルを前後に傾ける軸（モデル群の親）。</summary>
        public Transform bodyPivot;
        /// <summary>モデルが無いときに出すプリミティブ（任意）。</summary>
        public GameObject fallbackPrimitive;

        [Header("Stamina feedback")]
        public AudioSource breathAudio;
        public float breathPitchCalm = 0.9f;
        public float breathPitchTired = 1.5f;
        public float breathVolumeCalm = 0.25f;
        public float breathVolumeTired = 0.75f;
        [Tooltip("首の札の持久力の印が赤く脈打つ間（残り 1 つ以下・ばてた）、MinimalHud が true にする：息をいちばん大きく・高く")]
        public bool breathAlarm;
        public Transform earLeft;
        public Transform earRight;
        public Vector3 earsBackEuler = new Vector3(-50f, 0f, 0f);
        /// <summary>耳のボーンがクリップでキーされている（毎フレーム Animator が上書きする）なら true。</summary>
        public bool earsAnimated = true;
        public float tiredStamina = 40f;

        [Header("相手から見える持久力（system-r2-08）・加速の合図（r2-14）")]
        [Tooltip("体から出る汗（持久力が少ないほど多い。ほかの人の乗り物は同期した帯で）")]
        public ParticleSystem sweat;
        [Tooltip("ばてた瞬間の砂ぼこりのひと吹き（誰の画面でも）")]
        public ParticleSystem exhaustPuff;
        [Tooltip("金の火花（きれいな着地・カーブの立ち上がり）")]
        public ParticleSystem boostSparkle;
        [Tooltip("ほかの人の乗り物の上の札「ばてている」（25m 以内でばてている間だけ）")]
        public GameObject spentTag;
        public float spentTagRange = 25f;
        /// <summary>MountController が毎フレーム入れる：ほかの人が乗っている乗り物（自分の画面では相手）。</summary>
        [HideInInspector] public bool remoteRider;

        [Header("蹄（system-r3-09）")]
        [Tooltip("蹄の音（SfxMath.HOOF、空間音 2〜35m）")]
        public AudioSource hooves;
        [Tooltip("蹄の砂ぼこり（ワールド空間・後ろ向きの円錐）")]
        public ParticleSystem hoofDust;
        [Tooltip("自分が乗っているときの手の振動（疾走の拍ごと、system-r3-16）")]
        public MountInputVR hapticReins;
        /// <summary>MountController が毎フレーム入れる：このクライアントの乗り手が乗っている。</summary>
        [HideInInspector] public bool localRider;
        [Tooltip("蜃気楼でちらつかせる体の Renderer（3 つのモデルすべて）")]
        public Renderer[] flickerRenderers;
        private float[] beats;
        private int beatsGait = -1;
        private float lastPhase = -1f;
        private float lastDustRate = -1f;
        private bool lastSpent;
        private bool tagShown;
        private bool sweatOn;

        private int appliedMount = -1;
        private int appliedCoat = -1;
        private Animator activeAnim;
        private int lastFlags;
        private int lastGait = -1;
        private bool lastStamp;
        private Quaternion earLRest;
        private Quaternion earRRest;
        private bool earsInit;
        private float earBlend;
        private bool warned;

        private void Start()
        {
            if (earLeft != null) earLRest = earLeft.localRotation;
            if (earRight != null) earRRest = earRight.localRotation;
            earsInit = true;
        }

        public void Apply(int mount, int coat)
        {
            if (mount == appliedMount && coat == appliedCoat) return;
            appliedMount = mount;
            appliedCoat = coat;

            bool camel = mount == MountMath.MOUNT_CAMEL;
            bool skeleton = !camel && coat == COAT_SKELETON;
            bool horse = !camel && !skeleton;

            if (horseModel != null) horseModel.SetActive(horse);
            if (camelModel != null) camelModel.SetActive(camel);
            if (skeletonModel != null) skeletonModel.SetActive(skeleton);

            GameObject wanted = camel ? camelModel : (skeleton ? skeletonModel : horseModel);
            if (wanted == null)
            {
                if (!warned)
                {
                    Debug.LogWarning("[HorseRace] MountVisual: モデルが未設定のためプリミティブで代用します " + gameObject.name);
                    warned = true;
                }
                if (fallbackPrimitive != null) fallbackPrimitive.SetActive(true);
            }
            else if (fallbackPrimitive != null)
            {
                fallbackPrimitive.SetActive(false);
            }

            activeAnim = camel ? camelAnimator : (skeleton ? skeletonAnimator : horseAnimator);
            RuntimeAnimatorController ctrl = camel ? camelController : (skeleton ? skeletonController : horseController);
            if (activeAnim != null && ctrl != null && activeAnim.runtimeAnimatorController != ctrl)
            {
                activeAnim.runtimeAnimatorController = ctrl;
            }
            lastGait = -1;
            lastFlags = 0;
            lastStamp = false;

            if (horse && coatMaterials != null && coat >= 0 && coat < coatMaterials.Length && coatMaterials[coat] != null
                && horseCoatRenderers != null)
            {
                for (int i = 0; i < horseCoatRenderers.Length; i++)
                {
                    if (horseCoatRenderers[i] != null) horseCoatRenderers[i].sharedMaterial = coatMaterials[coat];
                }
            }
        }

        public void SetPitch(float pitchDeg)
        {
            if (bodyPivot == null) return;
            bodyPivot.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
        }

        /// <summary>gaitWithFlags は MountController.syncGait（下位 3 ビット＝歩様、上位＝AIR/STUN/STAMP）。
        /// stamina &lt; 0 はリモート（不明）。</summary>
        public void SetMotion(int gaitWithFlags, float speed, int mount, float stamina)
        {
            int gait = gaitWithFlags & MountController.GAIT_MASK;
            int flags = gaitWithFlags - gait;

            if (activeAnim != null)
            {
                if (gait != lastGait)
                {
                    activeAnim.SetInteger("Gait", gait);
                    lastGait = gait;
                }
                bool air = (flags & MountController.GAIT_FLAG_AIR) != 0;
                bool stun = (flags & MountController.GAIT_FLAG_STUN) != 0;
                if (air && (lastFlags & MountController.GAIT_FLAG_AIR) == 0) activeAnim.SetTrigger("Jump");
                if (stun && (lastFlags & MountController.GAIT_FLAG_STUN) == 0) activeAnim.SetTrigger("Stagger");
                bool stamp = (flags & MountController.GAIT_FLAG_STAMP) != 0;
                if (stamp != lastStamp)
                {
                    activeAnim.SetBool("Stamp", stamp);
                    lastStamp = stamp;
                }

                float animSpeed = 1f;
                if (gait > 0 && !air && !stun)
                {
                    float baseSpeed = MountMath.AnimRefSpeed(mount, gait - 1);
                    animSpeed = Mathf.Clamp(speed / baseSpeed, 0.5f, 1.8f);
                }
                activeAnim.speed = animSpeed;
            }
            lastFlags = flags;
            UpdateHooves(gait, flags);

            bool spent = MountMath.IsSpent(gaitWithFlags);
            if (spent && !lastSpent && exhaustPuff != null) exhaustPuff.Emit(36);   // ばてた瞬間：みんなに見える砂ぼこり
            lastSpent = spent;
            UpdateSpentTag(spent);
            UpdateStaminaFeedback(gait, stamina);
        }

        /// <summary>
        /// 蹄の音と砂ぼこり（system-r3-09）。gait = アニメの歩様（ANIM_IDLE..ANIM_GALLOP）、flags = AIR/STUN/SAND。
        /// 周期の位置は Animator の今のステートの normalizedTime % 1（再生速度は速さに合わせてある）。
        /// </summary>
        private void UpdateHooves(int gait, int flags)
        {
            bool air = (flags & MountController.GAIT_FLAG_AIR) != 0;
            bool sand = (flags & MountController.GAIT_FLAG_SAND) != 0;
            int g = gait - 1;   // MountMath.GAIT_*（-1 = 止まっている）
            if (hoofDust != null)
            {
                float rate = g < 0 ? 0f : SfxMath.HoofDustRate(g, sand, air);
                if (rate != lastDustRate)
                {
                    lastDustRate = rate;
                    ParticleSystem.EmissionModule em = hoofDust.emission;
                    em.rateOverTime = rate;
                    if (rate > 0f && !hoofDust.isPlaying) hoofDust.Play();
                }
            }
            if (g < 0 || air || activeAnim == null) { lastPhase = -1f; return; }
            if (g != beatsGait) { beats = SfxMath.HoofBeats(g); beatsGait = g; lastPhase = -1f; }
            float phase = activeAnim.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
            if (phase < 0f) phase += 1f;
            if (lastPhase >= 0f && beats != null)
            {
                for (int i = 0; i < beats.Length; i++)
                {
                    if (!SfxMath.BeatCrossed(lastPhase, phase, beats[i])) continue;
                    if (hooves != null && hooves.clip != null && hooves.gameObject.activeInHierarchy)
                    {
                        hooves.pitch = SfxMath.HoofPitch(i, beats.Length);
                        hooves.PlayOneShot(hooves.clip);
                    }
                    if (localRider && g == MountMath.GAIT_SPRINT && hapticReins != null)
                        hapticReins.PulseHands(MountInputVR.PULSE_HOOF_S, MountInputVR.PULSE_HOOF_AMP);
                }
            }
            lastPhase = phase;
        }

        /// <summary>蜃気楼のちらつき（system-r3-10）：hide = true で体の Renderer を消す（シェーダーは変えない）。</summary>
        public void SetFlickerHidden(bool hide)
        {
            if (flickerRenderers == null) return;
            for (int i = 0; i < flickerRenderers.Length; i++)
                if (flickerRenderers[i] != null) flickerRenderers[i].enabled = !hide;
        }

        /// <summary>金の火花（きれいな着地・カーブの立ち上がり：system-r2-14）。</summary>
        public void PlayBoostSparkle()
        {
            if (boostSparkle != null) boostSparkle.Emit(24);
        }

        private void UpdateSpentTag(bool spent)
        {
            if (spentTag == null) return;
            bool show = false;
            if (spent && remoteRider)
            {
                VRC.SDKBase.VRCPlayerApi lp = VRC.SDKBase.Networking.LocalPlayer;
                if (lp != null) show = Vector3.Distance(lp.GetPosition(), transform.position) <= spentTagRange;
            }
            if (show == tagShown) return;
            tagShown = show;
            spentTag.SetActive(show);
        }

        private void UpdateStaminaFeedback(int gait, float stamina)
        {
            float fatigue;
            if (stamina >= 0f) fatigue = 1f - Mathf.Clamp01(stamina / 100f);
            else fatigue = gait >= MountController.ANIM_GALLOP ? 0.5f : 0.1f * gait;

            if (breathAudio != null)
            {
                breathAudio.pitch = Mathf.Lerp(breathPitchCalm, breathPitchTired, fatigue);
                breathAudio.volume = breathAlarm ? 1f : Mathf.Lerp(breathVolumeCalm, breathVolumeTired, fatigue);
                if (breathAlarm) breathAudio.pitch = breathPitchTired + 0.15f;
                if (!breathAudio.isPlaying && breathAudio.gameObject.activeInHierarchy) breathAudio.Play();
            }

            if (sweat != null)
            {
                // 汗：持久力 40 未満（相手の疲れが見える。ほかの人の乗り物は同期した帯で）
                bool on = stamina >= 0f && stamina < tiredStamina;
                if (on != sweatOn)
                {
                    sweatOn = on;
                    if (on) sweat.Play();
                    else sweat.Stop();
                }
            }

            float earTarget = (stamina >= 0f && stamina < tiredStamina) ? 1f : 0f;
            earBlend = Mathf.MoveTowards(earBlend, earTarget, Time.deltaTime * 3f);
        }

        /// <summary>Animator が耳を書いた後に上書きする（Update だと Animator に消される）。</summary>
        private void LateUpdate()
        {
            if (!earsInit) return;
            Quaternion offset = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(earsBackEuler), earBlend);
            if (earLeft != null) earLeft.localRotation = (earsAnimated ? earLeft.localRotation : earLRest) * offset;
            if (earRight != null) earRight.localRotation = (earsAnimated ? earRight.localRotation : earRRest) * offset;
        }
    }
}
