using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// スタート台の号砲（机に置いた拳銃。クラス名は以前の鐘のまま）。マスターが Interact で撃つ＝レース開始（RaceManager.RequestStart）。
    /// マスター以外が触ると撃鉄が少し動いてカチッ（tapSound）と鳴るだけで、紙の札（noteText）に「号砲を撃てるのは主催者だけ」と出す。
    /// 開始が通ると RaceManager が全員の手元で PlayRing() を呼ぶ：撃鉄が落ち、弾倉が 1 発分回り、銃口から煙、空砲の音（ringSound）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StartBell : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("号砲の音（空砲。全員に聞こえるよう 2D 推奨）")]
        public AudioSource ringSound;
        [Tooltip("マスター以外が触ったときの乾いたカチッという音（任意）")]
        public AudioSource tapSound;
        [Tooltip("札の文字（任意）")]
        public TextMeshPro noteText;
        [Tooltip("任意。拳銃の撃鉄（ローカル X 軸まわりに起こして落とす）")]
        public Transform hammer;
        [Tooltip("任意。拳銃の弾倉（ローカル Z 軸まわりに 60° 回す）")]
        public Transform cylinder;
        public ParticleSystem smoke;
        public float hammerCockDeg = -35f;

        private float fireAt = -10f;
        private float tapAt = -10f;
        private bool restCaptured;
        private Quaternion hammerRest;
        private Quaternion cylRest;
        private float cylAngle;
        private bool animating;

        private void CaptureRest()
        {
            if (restCaptured) return;
            restCaptured = true;
            if (hammer != null) hammerRest = hammer.localRotation;
            if (cylinder != null) cylRest = cylinder.localRotation;
        }

        public override void Interact()
        {
            if (raceManager == null) return;
            if (raceManager.IsLocalMaster())
            {
                if (raceManager.ActiveCount() == 0)
                {
                    ShowNote("台帳に誰もサインしていない");
                    Tap();
                    return;
                }
                raceManager.RingBell();
            }
            else
            {
                Tap();
                ShowNote("号砲を撃てるのは主催者（マスター）だけ");
            }
        }

        private void Tap()
        {
            CaptureRest();
            if (tapSound != null) tapSound.Play();
            tapAt = Time.time;
            animating = true;
        }

        public void PlayRing()
        {
            CaptureRest();
            if (ringSound != null) ringSound.Play();
            if (smoke != null) smoke.Play();
            fireAt = Time.time;
            cylAngle += 60f;
            animating = true;
            ShowNote("");
        }

        private void Update()
        {
            if (!animating) return;
            float tf = Time.time - fireAt;
            float tt = Time.time - tapAt;
            float cock = 0f;
            // 撃つ：0.12 秒で起こして落とす。触るだけ：半分起こして戻す
            if (tf < 0.25f) cock = tf < 0.12f ? tf / 0.12f : Mathf.Max(0f, 1f - (tf - 0.12f) / 0.05f);
            else if (tt < 0.3f) cock = 0.5f * Mathf.Sin(tt / 0.3f * Mathf.PI);
            if (hammer != null) hammer.localRotation = hammerRest * Quaternion.Euler(hammerCockDeg * cock, 0f, 0f);
            if (cylinder != null)
            {
                float a = cylAngle - 60f * (1f - Mathf.Clamp01(tf / 0.12f));
                cylinder.localRotation = cylRest * Quaternion.Euler(0f, 0f, a);
            }
            if (tf >= 0.25f && tt >= 0.3f) animating = false;
        }

        private void ShowNote(string msg)
        {
            if (noteText == null) return;
            noteText.text = msg.Length > 0 ? msg : DefaultNote();
            if (msg.Length > 0) SendCustomEventDelayedSeconds(nameof(_ClearNote), 4f);
        }

        public static string DefaultNote()
        {
            return "主催者が号砲を撃つとレース開始";
        }

        public void _ClearNote()
        {
            if (noteText != null) noteText.text = DefaultNote();
        }
    }
}
