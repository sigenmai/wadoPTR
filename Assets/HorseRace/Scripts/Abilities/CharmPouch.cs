using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 能力の札（首の札の右隣、乗り物の首の上）。その能力の手配書（Wanted_砂塵 など）を小さな紙に貼り、使えるかどうかを見せる（spec 5.14）。
    ///  - 使えるとき：明るい手配書（readyMaterials[能力]）。使えるようになった瞬間に金の火花（sparks.Emit(12)）と readySound。
    ///  - クールダウン中：35% の灰色の手配書（idleMaterials[能力]）と「あと 7」（cooldownText）。
    ///  - Lv：宝石 3 つ（levelMarks）のうち Lv 個を点ける（levelMarksUnlit があれば残りはそちら）。
    ///  - 開幕の猶予（8 秒）の間は灰色の手配書と「あと 5」（使えない）。
    ///  - 被弾後の守り（1.5 秒）：金の火花と「守り」の文字（FlashGuard、新しい形は作らない）。
    ///  - 被弾（ui-r2-06）：赤い火花 6 つ（hitSparks、FlashHit）。
    ///  - 使えるとき、セッションの最初の 3 回の発動まではボタン（「E」/「トリガー」）を真鍮の字で出す（hint、ui-r2-07）。
    ///  - Lv が上がった（ui-r2-12）：金の火花 16、札が 1.4 → 1 に跳ね、新しい宝石が 2 倍にふくらんで戻り、首の札に「遺物 Lv2 砂煙 半径6m・6秒」。
    /// 乗っている本人だけに出す（AbilityCaster が毎フレーム Show、降りたら Hide）。位置は乗り物の種類（鞍の高さ）に合わせて動かす。
    /// デスクトップ（ui-r3-04）：首の上は画面の外（視野の 30° より 40° 下）なので、首の札（MinimalHud.Place）と同じく頭の前・右下に ×0.55、
    ///   向きだけ 0.15 秒遅れて追う。使えるようになった瞬間は VR の手にも小さく響かせる（ui-r3-05）。
    /// 溢れ（system-r3-04）：Lv3 で遺物を拾ったら金の火花と「溢れ：再装填」を 1.5 秒（FlashSurge）。
    /// 絵の無い能力（火薬樽・蜃気楼、system-r3-10/11）は Wanted_Original の紙に名前（caption）を書く。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CharmPouch : UdonSharpBehaviour
    {
        [Tooltip("札の見た目の親（Hide で隠す）")]
        public GameObject cardRoot;
        [Tooltip("手配書の紙の Renderer")]
        public Renderer[] cardRenderers;
        [Tooltip("能力ごとの明るい手配書（砂塵・投げ縄・岩壁・鉄球の順）")]
        public Material[] readyMaterials;
        [Tooltip("能力ごとの灰色の手配書（クールダウン中）")]
        public Material[] idleMaterials;
        public ParticleSystem sparks;
        [Tooltip("被弾の赤い火花（ui-r2-06）")]
        public ParticleSystem hitSparks;
        [Tooltip("首の札（Lv が上がった知らせ。AbilityCaster が入れる）")]
        public MinimalHud hud;
        /// <summary>使えるときに出すボタンの説明（AbilityCaster が毎フレーム入れる。空なら出さない）。</summary>
        [HideInInspector] public string hint = "";
        public TextMeshPro cooldownText;
        [Tooltip("光る宝石（Lv1..3 の順）")]
        public GameObject[] levelMarks;
        [Tooltip("任意。消えている宝石（levelMarks と同じ並び）")]
        public GameObject[] levelMarksUnlit;
        public AudioSource readySound;
        [Tooltip("絵の無い能力の手配書の上の名前（HudMath.AbilityCardCaption）")]
        public TextMeshPro caption;
        [Tooltip("鞍の点からの位置（乗り物ローカル）。首の札（MinimalHud.neckOffset）の 0.09m 右")]
        public Vector3 neckOffset = new Vector3(0.15f, 0.25f, 0.55f);
        public float neckPitch = 35f;

        [Header("デスクトップ：頭（ui-r3-04）")]
        [Tooltip("頭からの相対位置（右下の端、首の札の反対側）。UiStyle.DesktopAbilityOffset")]
        public Vector3 desktopOffset = new Vector3(0.40f, -0.19f, 0.55f);
        [Tooltip("デスクトップでの倍率。UiStyle.DESKTOP_HUD_SCALE")]
        public float desktopScale = 0.55f;
        public float desktopLag = 0.15f;

        private int shownReady = -1;
        private bool rotInit;
        private Quaternion smoothRot;
        private Vector3 baseScale = Vector3.one;
        private bool baseScaleCaptured;
        private bool onHead;
        private int shownLevel = -1;
        private int shownAbility = -1;
        private int shownType = -1;
        private int shownSeconds = -1;
        private bool visible = true;
        private float guardUntil;
        private bool guardShown;
        private string shownHint = "";
        private float punchAt = -10f;
        private float gemPopAt = -10f;
        private int gemPopIndex = -1;
        private Vector3 cardBaseScale = Vector3.one;
        private bool cardScaleCaptured;
        private Vector3 gemBaseScale = Vector3.one;
        private float surgeUntil;
        private bool surgeShown;

        private void Start()
        {
            SetVisible(false);
        }

        public void Show(bool ready, int level, float remaining, int ability, int seatType)
        {
            SetVisible(true);
            if (seatType != shownType && !onHead)
            {
                shownType = seatType;
                transform.localPosition = new Vector3(neckOffset.x, SeatMath.DefaultSaddleY(seatType) + neckOffset.y, SeatMath.DefaultSaddleZ(seatType) + neckOffset.z);
                transform.localRotation = Quaternion.Euler(neckPitch, 0f, 0f);
            }
            int r = ready ? 1 : 0;
            if (r != shownReady || ability != shownAbility)
            {
                if (shownReady == 0 && ready && ability == shownAbility)
                {
                    if (readySound != null) readySound.Play();
                    if (sparks != null) sparks.Emit(12);
                    if (hud != null) hud.Buzz(HudMath.HAPTIC_READY_S, HudMath.HAPTIC_READY_AMP, HudMath.HAPTIC_READY_HZ);
                }
                shownReady = r;
                shownAbility = ability;
                Material m = PickMaterial(ready ? readyMaterials : idleMaterials, ability);
                if (m != null && cardRenderers != null)
                    for (int i = 0; i < cardRenderers.Length; i++) if (cardRenderers[i] != null) cardRenderers[i].sharedMaterial = m;
                if (caption != null) caption.text = HudMath.AbilityCardCaption(ability);
            }
            bool guard = Time.time < guardUntil;
            bool surge = !guard && Time.time < surgeUntil;
            int secs = guard ? -2 : (ready ? 0 : Mathf.CeilToInt(remaining));
            string h = hint != null ? hint : "";
            if (secs != shownSeconds || guard != guardShown || h != shownHint || surge != surgeShown)
            {
                shownSeconds = secs;
                guardShown = guard;
                surgeShown = surge;
                shownHint = h;
                if (cooldownText != null)
                {
                    if (guard) cooldownText.text = "<color=#FFC745>守り</color>";
                    else if (surge) cooldownText.text = "<color=#FFC745>" + HudMath.SurgeNote() + "</color>";
                    else cooldownText.text = ready ? h : HudMath.CooldownLabel(remaining);
                }
            }
            if (level != shownLevel)
            {
                if (level > shownLevel && shownLevel >= 0) LevelUp(ability, level);
                shownLevel = level;
                if (levelMarks != null)
                    for (int i = 0; i < levelMarks.Length; i++)
                        if (levelMarks[i] != null) levelMarks[i].SetActive(i < level);
                if (levelMarksUnlit != null)
                    for (int i = 0; i < levelMarksUnlit.Length; i++)
                        if (levelMarksUnlit[i] != null) levelMarksUnlit[i].SetActive(i >= level);
            }
        }

        /// <summary>Lv が上がった瞬間（ui-r2-12）：金の火花・札の跳ね・新しい宝石のふくらみ・首の札の知らせ。</summary>
        private void LevelUp(int ability, int level)
        {
            if (sparks != null) sparks.Emit(16);
            punchAt = Time.time;
            int gi = level - 1;
            if (levelMarks != null && gi >= 0 && gi < levelMarks.Length && levelMarks[gi] != null)
            {
                if (gemPopIndex >= 0 && gemPopIndex < levelMarks.Length && levelMarks[gemPopIndex] != null)
                    levelMarks[gemPopIndex].transform.localScale = gemBaseScale;
                gemPopIndex = gi;
                gemBaseScale = levelMarks[gi].transform.localScale;
                gemPopAt = Time.time;
            }
            if (hud != null) hud.ShowNote(HudMath.RelicNote(ability, level), new Color(1f, 0.78f, 0.27f), 2f, HudMath.NOTE_REWARD);
        }

        private void Update()
        {
            if (!visible) return;
            float t = Time.time - punchAt;
            if (cardRoot != null && t < 1f)
            {
                if (!cardScaleCaptured) { cardBaseScale = cardRoot.transform.localScale; cardScaleCaptured = true; }
                cardRoot.transform.localScale = cardBaseScale * HudMath.PunchScale(t, 1.4f);
            }
            float g = Time.time - gemPopAt;
            if (gemPopIndex >= 0 && levelMarks != null && gemPopIndex < levelMarks.Length && levelMarks[gemPopIndex] != null)
            {
                if (g < 0.6f) levelMarks[gemPopIndex].transform.localScale = gemBaseScale * HudMath.PunchScale(g * 0.5f, 2f);
                else
                {
                    levelMarks[gemPopIndex].transform.localScale = gemBaseScale;
                    gemPopIndex = -1;
                }
            }
        }

        /// <summary>
        /// デスクトップの置き場所（ui-r3-04）：頭の前・右下に ×0.55、向きだけ desktopLag 秒遅れて追う（MinimalHud.Place と同じ）。
        /// VR に戻ったら乗り物の首の上（mount ローカル）に戻す。
        /// </summary>
        private void LateUpdate()
        {
            if (!visible) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            if (!baseScaleCaptured) { baseScale = transform.localScale; baseScaleCaptured = true; }
            bool desk = !local.IsUserInVR();
            if (!desk)
            {
                if (onHead)
                {
                    onHead = false;
                    rotInit = false;
                    transform.localScale = baseScale;
                    shownType = -1;   // 次の Show で首の上へ戻す
                }
                return;
            }
            if (!onHead)
            {
                onHead = true;
                transform.localScale = baseScale * desktopScale;
            }
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (!rotInit) { smoothRot = head.rotation; rotInit = true; }
            float k = desktopLag > 0.001f ? 1f - Mathf.Exp(-Time.deltaTime / desktopLag) : 1f;
            smoothRot = Quaternion.Slerp(smoothRot, head.rotation, k);
            transform.SetPositionAndRotation(head.position + smoothRot * desktopOffset, smoothRot);
        }

        /// <summary>被弾した（ui-r2-06）：赤い火花 6 つ。</summary>
        public void FlashHit()
        {
            if (visible && hitSparks != null) hitSparks.Emit(6);
        }

        /// <summary>被弾後の守りを seconds 秒見せる（金の火花＋「守り」）。</summary>
        public void FlashGuard(float seconds)
        {
            guardUntil = Time.time + seconds;
            if (visible && sparks != null) sparks.Emit(8);
        }

        /// <summary>溢れ（system-r3-04）：金の火花と「溢れ：再装填」を 1.5 秒、首の札にも。</summary>
        public void FlashSurge()
        {
            surgeUntil = Time.time + 1.5f;
            if (visible && sparks != null) sparks.Emit(16);
            if (hud != null) hud.ShowNote(HudMath.SurgeNote(), new Color(1f, 0.78f, 0.27f), 1.5f, HudMath.NOTE_REWARD);
        }

        public void Hide()
        {
            SetVisible(false);
            shownReady = -1;
            shownSeconds = -1;
            shownLevel = -1;   // 乗り直しで Lv の跳ねを出さない
            rotInit = false;
        }

        private Material PickMaterial(Material[] set, int ability)
        {
            if (set == null || set.Length == 0) return null;
            if (ability < 0 || ability >= set.Length) ability = 0;
            return set[ability];
        }

        private void SetVisible(bool v)
        {
            if (v == visible) return;
            visible = v;
            if (cardRoot != null) cardRoot.SetActive(v);
        }
    }
}
