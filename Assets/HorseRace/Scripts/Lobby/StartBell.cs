using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// スタート台の鐘。マスターが Interact で鳴らす＝レース開始（RaceManager.RequestStart）。
    /// マスター以外が触ると小さく鳴るだけで、札（noteText、任意）に「鐘を鳴らせるのは主催者だけ」と出す。
    /// 開始が通ると RaceManager が全員の手元で PlayRing() を呼ぶ。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StartBell : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        public AudioSource ringSound;
        [Tooltip("マスター以外が触ったときの小さな音（任意）")]
        public AudioSource tapSound;
        [Tooltip("札の文字（任意）")]
        public TextMeshPro noteText;

        public override void Interact()
        {
            if (raceManager == null) return;
            if (raceManager.IsLocalMaster())
            {
                if (raceManager.ActiveCount() == 0)
                {
                    ShowNote("台帳に誰もサインしていない");
                    if (tapSound != null) tapSound.Play();
                    return;
                }
                raceManager.RingBell();
            }
            else
            {
                if (tapSound != null) tapSound.Play();
                ShowNote("鐘を鳴らせるのは主催者（マスター）だけ");
            }
        }

        public void PlayRing()
        {
            if (ringSound != null) ringSound.Play();
            ShowNote("");
        }

        private void ShowNote(string msg)
        {
            if (noteText == null) return;
            noteText.text = msg;
            if (msg.Length > 0) SendCustomEventDelayedSeconds(nameof(_ClearNote), 4f);
        }

        public void _ClearNote()
        {
            if (noteText != null) noteText.text = "";
        }
    }
}
