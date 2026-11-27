using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 乗り物の見た目。syncMount / syncCoat で Horse / Camel / SkeletonHorse のモデルを切り替え、
    /// 馬は HorseMaterialBuilder の毛色マテリアル（coat 0..5）を貼る。coat 6 は骨の馬。
    /// Animator（全モデル共通の Mount.controller）に歩様 "Gait"、"Jump"/"Stagger" トリガー、"Stamp" を渡し、
    /// speed = 実速度 / 歩様の基準速度。クロスフェード 0.2 秒はコントローラ側（MountAnimatorBuilder）。
    /// スタミナは息づかい音（低スタミナで荒く）と耳を伏せる動きで見せる。
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
        public float breathVolumeTired = 1f;
        public Transform earLeft;
        public Transform earRight;
        public Vector3 earsBackEuler = new Vector3(-50f, 0f, 0f);
        public float tiredStamina = 40f;

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
            lastGait = -1;
            lastFlags = 0;

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
                    float baseSpeed = MountMath.GaitSpeed(mount, gait - 1);
                    animSpeed = Mathf.Clamp(speed / baseSpeed, 0.5f, 1.6f);
                }
                activeAnim.speed = animSpeed;
            }
            lastFlags = flags;

            UpdateStaminaFeedback(gait, stamina);
        }

        private void UpdateStaminaFeedback(int gait, float stamina)
        {
            float fatigue;
            if (stamina >= 0f) fatigue = 1f - Mathf.Clamp01(stamina / 100f);
            else fatigue = gait >= MountController.ANIM_GALLOP ? 0.5f : 0.1f * gait;

            if (breathAudio != null)
            {
                breathAudio.pitch = Mathf.Lerp(breathPitchCalm, breathPitchTired, fatigue);
                breathAudio.volume = Mathf.Lerp(breathVolumeCalm, breathVolumeTired, fatigue);
                if (!breathAudio.isPlaying && breathAudio.gameObject.activeInHierarchy) breathAudio.Play();
            }

            if (!earsInit) return;
            float earTarget = (stamina >= 0f && stamina < tiredStamina) ? 1f : 0f;
            earBlend = Mathf.MoveTowards(earBlend, earTarget, Time.deltaTime * 3f);
            Quaternion back = Quaternion.Euler(earsBackEuler);
            if (earLeft != null) earLeft.localRotation = Quaternion.Slerp(earLRest, earLRest * back, earBlend);
            if (earRight != null) earRight.localRotation = Quaternion.Slerp(earRRest, earRRest * back, earBlend);
        }
    }
}
