using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 壁に貼られた手配書風のカード。Interact で 1 枚「取る」＝乗り物（kind 0）か能力（kind 1）を選ぶ。
    /// 選択はこのクライアントだけ（RaceManager の local* フィールド）。取ったカードは自分にだけ壁から消えて見える
    /// （posterVisual を隠し、takenVisual＝ピンだけ残った跡を出す）。台帳にサインするとこの選択でエントリーする。
    /// 取るとき：紙のこすれる音（takeSound）と、紙が 0.25 秒で手前へ 0.15m 引き出されながら縮んで消える動き。そのあと選択が決まる。
    /// カードの下の札（label）：「手配　砂塵」と効果・クールダウン（HudMath.AbilityDescription / MountDescription）。
    /// 文言: 乗り物 = 馬／ラクダ、能力 = 砂塵／投げ縄／岩壁／鉄球。
    /// KIND_PICK（system-r2-15）：電報の掲示板の下の予想の札。value = スロット、札の文字は RaceManager が乗り手の名前に書き換える。
    /// 取ると RaceManager.PickWinner（このクライアントだけの予想）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PosterCard : UdonSharpBehaviour
    {
        public const int KIND_MOUNT = 0;
        public const int KIND_ABILITY = 1;
        public const int KIND_PICK = 2;
        public const float TAKE_SECONDS = 0.25f;
        public const float TAKE_PULL_M = 0.15f;

        public RaceManager raceManager;
        [Tooltip("0 = 乗り物（value: 0 馬 / 1 ラクダ）、1 = 能力（value: 0 砂塵 / 1 投げ縄 / 2 岩壁 / 3 鉄球）")]
        public int kind;
        public int value;
        [Tooltip("壁に貼られている紙（取ったら自分にだけ隠す）")]
        public GameObject posterVisual;
        [Tooltip("取った跡（ピンと紙の切れ端など、任意）")]
        public GameObject takenVisual;
        [Tooltip("カードの下の札（Start で名前と説明を入れる）")]
        public TextMeshPro label;
        [Tooltip("紙のこすれる音（任意）")]
        public AudioSource takeSound;

        private bool taking;
        private float takeAt;
        private Vector3 restPos;
        private Vector3 restScale;
        private bool restCaptured;

        private void Start()
        {
            CaptureRest();
            if (label != null && kind != KIND_PICK) label.text = "<b><size=130%>手配　" + Caption() + "</size></b>\n" + Description();
        }

        private void CaptureRest()
        {
            if (restCaptured || posterVisual == null) return;
            restCaptured = true;
            restPos = posterVisual.transform.localPosition;
            restScale = posterVisual.transform.localScale;
        }

        public string Caption()
        {
            if (kind == KIND_MOUNT) return RaceManager.MountKindLabel(value);
            return RaceManager.AbilityLabel(value);
        }

        public string Description()
        {
            if (kind == KIND_MOUNT) return HudMath.MountDescription(value);
            return HudMath.AbilityDescription(value);
        }

        public override void Interact()
        {
            if (raceManager == null || taking) return;
            CaptureRest();
            if (takeSound != null) takeSound.Play();
            bool visible = posterVisual != null && posterVisual.activeSelf;
            if (!visible) { _Commit(); return; }
            taking = true;
            takeAt = Time.time;
            SendCustomEventDelayedSeconds(nameof(_Commit), TAKE_SECONDS);
        }

        private void Update()
        {
            if (!taking || posterVisual == null) return;
            float t = Mathf.Clamp01((Time.time - takeAt) / TAKE_SECONDS);
            float e = t * t * (3f - 2f * t);
            Transform pv = posterVisual.transform;
            pv.localPosition = restPos + new Vector3(0f, 0f, -TAKE_PULL_M * e);
            pv.localScale = restScale * (1f - e);
        }

        public void _Commit()
        {
            taking = false;
            if (raceManager == null) return;
            if (kind == KIND_MOUNT) raceManager.SelectMount(value);
            else if (kind == KIND_PICK) raceManager.PickWinner(value);
            else raceManager.SelectAbility(value);
        }

        /// <summary>予想の札の文字（RaceManager から、ローカル）。</summary>
        public void SetPickLabel(string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        /// <summary>RaceManager から呼ばれる（ローカル表示のみ）。</summary>
        public void SetTaken(bool taken)
        {
            if (posterVisual != null)
            {
                if (restCaptured && !taking)
                {
                    posterVisual.transform.localPosition = restPos;
                    posterVisual.transform.localScale = restScale;
                }
                posterVisual.SetActive(!taken);
            }
            if (takenVisual != null) takenVisual.SetActive(taken);
        }
    }
}
