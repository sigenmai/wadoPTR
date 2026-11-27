using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// VR の酔い止め（ui-r2-18）：乗っている間、速さと曲がる速さに合わせて視界の縁を暗くする（トンネリング）。
    /// 見た目は砂塵のかすみ（MountBuilder の内向きの球、新しい形は作らない）をもう 1 つ頭に付け、
    /// Shaders/ComfortVignette.shader が「視線の向きとの角度」で縁だけを暗くする。強さ _Strength をここで毎フレーム入れる。
    /// 頭の向きそのものは数えない（自分で首を振っても暗くしない）：乗り物の速さ（MountController.Speed）と乗り物の向きの変わる速さだけ。
    /// このスクリプトは補助表示のレバーの横の紙（Collider 付き）に置き、Interact で なし → 弱 → 強（ローカル、既定 弱）。
    /// デスクトップでは出さない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ComfortVignette : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        public PracticeArea practiceArea;
        [Tooltip("頭に付ける内向きの球（このオブジェクトの子にしない）")]
        public GameObject vignette;
        public Renderer vignetteRenderer;
        [Tooltip("紙の文字（「視界の縁：弱」）")]
        public TextMeshPro label;
        [Tooltip("0 = なし、1 = 弱、2 = 強")]
        public int level = 1;
        [Tooltip("強さの追いつく速さ（1/秒）")]
        public float smoothing = 6f;

        private float strength;
        private float lastYaw;
        private bool yawInit;
        private float lastSet = -1f;
        private MountController lastMount;

        private void Start()
        {
            ApplyLabel();
            if (vignette != null) vignette.SetActive(false);
        }

        public override void Interact()
        {
            level = (level + 1) % HudMath.VIGNETTE_LEVELS;
            ApplyLabel();
        }

        private void ApplyLabel()
        {
            if (label != null) label.text = HudMath.VignetteLabel(level);
        }

        private MountController FindLocalMount()
        {
            if (raceManager != null)
            {
                MountController m = raceManager.LocalMount();
                if (m != null && m.IsLocalRider()) return m;
            }
            if (practiceArea != null && practiceArea.posts != null)
            {
                for (int i = 0; i < practiceArea.posts.Length; i++)
                {
                    PracticeHorsePost p = practiceArea.posts[i];
                    if (p != null && p.mount != null && p.mount.IsLocalRider()) return p.mount;
                }
            }
            return null;
        }

        private void LateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            MountController m = local != null && local.IsUserInVR() && level > 0 ? FindLocalMount() : null;
            float dt = Time.deltaTime;
            float target = 0f;
            if (m != null)
            {
                float yaw = m.transform.eulerAngles.y;
                if (!yawInit || m != lastMount) { lastYaw = yaw; yawInit = true; lastMount = m; }
                float yawRate = dt > 0.0001f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
                lastYaw = yaw;
                target = HudMath.VignetteStrength(m.Speed, yawRate, level);
            }
            else yawInit = false;
            strength = Mathf.MoveTowards(strength, target, smoothing * dt);
            bool on = strength > 0.01f && m != null;
            if (vignette != null)
            {
                if (vignette.activeSelf != on) vignette.SetActive(on);
                if (on) vignette.transform.position = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            }
            if (on && vignetteRenderer != null && Mathf.Abs(strength - lastSet) > 0.005f)
            {
                lastSet = strength;
                vignetteRenderer.sharedMaterial.SetFloat("_Strength", strength);
            }
        }
    }
}
