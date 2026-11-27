using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 壁に貼られた手配書風のカード。Interact で 1 枚「取る」＝乗り物（kind 0）か能力（kind 1）を選ぶ。
    /// 選択はこのクライアントだけ（RaceManager の local* フィールド）。取ったカードは自分にだけ壁から消えて見える
    /// （posterVisual を隠し、takenVisual＝ピンだけ残った跡を出す）。台帳にサインするとこの選択でエントリーする。
    /// 文言: 乗り物 = 馬／ラクダ、能力 = 砂塵／投げ縄／岩壁／鉄球。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PosterCard : UdonSharpBehaviour
    {
        public const int KIND_MOUNT = 0;
        public const int KIND_ABILITY = 1;

        public RaceManager raceManager;
        [Tooltip("0 = 乗り物（value: 0 馬 / 1 ラクダ）、1 = 能力（value: 0 砂塵 / 1 投げ縄 / 2 岩壁 / 3 鉄球）")]
        public int kind;
        public int value;
        [Tooltip("壁に貼られている紙（取ったら自分にだけ隠す）")]
        public GameObject posterVisual;
        [Tooltip("取った跡（ピンと紙の切れ端など、任意）")]
        public GameObject takenVisual;
        [Tooltip("カードの文字（任意。Start で文言を入れる）")]
        public TextMeshPro label;

        private void Start()
        {
            if (label != null) label.text = "手配\n" + Caption();
        }

        public string Caption()
        {
            if (kind == KIND_MOUNT) return RaceManager.MountKindLabel(value);
            return RaceManager.AbilityLabel(value);
        }

        public override void Interact()
        {
            if (raceManager == null) return;
            if (kind == KIND_MOUNT) raceManager.SelectMount(value);
            else raceManager.SelectAbility(value);
        }

        /// <summary>RaceManager から呼ばれる（ローカル表示のみ）。</summary>
        public void SetTaken(bool taken)
        {
            if (posterVisual != null) posterVisual.SetActive(!taken);
            if (takenVisual != null) takenVisual.SetActive(taken);
        }
    }
}
