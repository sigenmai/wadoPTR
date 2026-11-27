using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 能力の札（首の札の右隣、乗り物の首の上）。その能力の手配書（Wanted_砂塵 など）を小さな紙に貼り、使えるかどうかを見せる（spec 5.14）。
    ///  - 使えるとき：明るい手配書（readyMaterials[能力]）。使えるようになった瞬間に金の火花（sparks.Emit(12)）と readySound。
    ///  - クールダウン中：35% の灰色の手配書（idleMaterials[能力]）と「あと 7」（cooldownText）。
    ///  - Lv：宝石 3 つ（levelMarks）のうち Lv 個を点ける（levelMarksUnlit があれば残りはそちら）。
    /// 乗っている本人だけに出す（AbilityCaster が毎フレーム Show、降りたら Hide）。位置は乗り物の種類（鞍の高さ）に合わせて動かす。
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
        public TextMeshPro cooldownText;
        [Tooltip("光る宝石（Lv1..3 の順）")]
        public GameObject[] levelMarks;
        [Tooltip("任意。消えている宝石（levelMarks と同じ並び）")]
        public GameObject[] levelMarksUnlit;
        public AudioSource readySound;
        [Tooltip("鞍の点からの位置（乗り物ローカル）。首の札（MinimalHud.neckOffset）の 0.09m 右")]
        public Vector3 neckOffset = new Vector3(0.15f, 0.25f, 0.55f);
        public float neckPitch = 35f;

        private int shownReady = -1;
        private int shownLevel = -1;
        private int shownAbility = -1;
        private int shownType = -1;
        private int shownSeconds = -1;
        private bool visible = true;

        private void Start()
        {
            SetVisible(false);
        }

        public void Show(bool ready, int level, float remaining, int ability, int seatType)
        {
            SetVisible(true);
            if (seatType != shownType)
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
                }
                shownReady = r;
                shownAbility = ability;
                Material m = PickMaterial(ready ? readyMaterials : idleMaterials, ability);
                if (m != null && cardRenderers != null)
                    for (int i = 0; i < cardRenderers.Length; i++) if (cardRenderers[i] != null) cardRenderers[i].sharedMaterial = m;
            }
            int secs = ready ? 0 : Mathf.CeilToInt(remaining);
            if (secs != shownSeconds)
            {
                shownSeconds = secs;
                if (cooldownText != null) cooldownText.text = ready ? "" : HudMath.CooldownLabel(remaining);
            }
            if (level != shownLevel)
            {
                shownLevel = level;
                if (levelMarks != null)
                    for (int i = 0; i < levelMarks.Length; i++)
                        if (levelMarks[i] != null) levelMarks[i].SetActive(i < level);
                if (levelMarksUnlit != null)
                    for (int i = 0; i < levelMarksUnlit.Length; i++)
                        if (levelMarksUnlit[i] != null) levelMarksUnlit[i].SetActive(i >= level);
            }
        }

        public void Hide()
        {
            SetVisible(false);
            shownReady = -1;
            shownSeconds = -1;
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
