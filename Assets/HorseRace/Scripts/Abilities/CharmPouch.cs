using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 腰（鞍の脇）のお守り袋。能力のクールダウンと Lv を数字なしで見せる（spec 5.14）。
    ///  - 使えるとき：袋が光る（pouchRenderer のマテリアルを readyMaterial に替え、glow を出す）。使えるようになった瞬間に readySound。
    ///  - Lv：levelMarks（最大 3 個の刻印）のうち Lv 個を点ける（levelMarksUnlit があれば残りはそちらを出す）。
    /// AbilityCaster が毎フレーム Show を呼ぶ（変化したときだけ反映）。乗っていない人の袋は消灯。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CharmPouch : UdonSharpBehaviour
    {
        public Renderer pouchRenderer;
        public Material readyMaterial;
        public Material idleMaterial;
        public GameObject glow;
        [Tooltip("光る刻印（Lv1..3 の順）")]
        public GameObject[] levelMarks;
        [Tooltip("任意。消えている刻印（levelMarks と同じ並び）")]
        public GameObject[] levelMarksUnlit;
        public AudioSource readySound;

        private int shownReady = -1;
        private int shownLevel = -1;

        public void Show(bool ready, int level)
        {
            int r = ready ? 1 : 0;
            if (r != shownReady)
            {
                if (shownReady == 0 && ready && readySound != null) readySound.Play();
                shownReady = r;
                if (pouchRenderer != null)
                {
                    Material m = ready ? readyMaterial : idleMaterial;
                    if (m != null) pouchRenderer.sharedMaterial = m;
                }
                if (glow != null) glow.SetActive(ready);
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
    }
}
