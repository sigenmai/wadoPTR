using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 懐中時計（ステージの残り時間）。エントリーしていてレース中だけ。文字盤は紙の円盤に TMP の数字 12 個と針 2 本（字形「│」）、真ん中に「残り 3:12」。
    /// VR：左手首の内側。手首を顔に向けたとき（手の上向き・手から頭への向きの内積 > 0.55）だけ 0.15 秒でふくらんで出る。
    ///     手綱を握っている間（手首が下）は出ない＝残り時間は首の札（MinimalHud）にいつも出ている。
    /// デスクトップ：頭の前・視界の下のほうに小さく固定。
    /// 残り 10 秒から 1 秒ごとにカチ、カチ（tickAudio、任意）。
    /// このスクリプトの GameObject は常に有効にしておき、見た目は子の watchRoot に置く（watchRoot を出し入れする）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PocketWatch : UdonSharpBehaviour
    {
        public const float FADE_SECONDS = 0.15f;
        public const float TICK_FROM_SECONDS = 10f;

        public RaceManager raceManager;
        [Tooltip("時計の見た目（このオブジェクトの子）")]
        public GameObject watchRoot;
        [Tooltip("文字盤の文字（「残り 3:12」）")]
        public TextMeshPro faceText;
        [Tooltip("分針・秒針（ローカル Z 軸まわりに回す）")]
        public Transform minuteHand;
        public Transform secondHand;
        [Tooltip("残り 10 秒の時計の音（任意。1 秒ごとに Play）")]
        public AudioSource tickAudio;

        [Header("VR（左手からの相対）")]
        public Vector3 vrLocalOffset = new Vector3(0f, 0.03f, -0.06f);
        public Vector3 vrLocalEuler = new Vector3(0f, 90f, 0f);

        [Header("デスクトップ（頭からの相対）")]
        public Vector3 desktopOffset = new Vector3(0f, -0.2f, 0.45f);
        public Vector3 desktopEuler = new Vector3(-20f, 180f, 0f);

        private bool shown = true;
        private string lastText = "";
        private float reveal;
        private int lastTickSecond = -1;
        private Vector3 baseScale = Vector3.one;
        private bool scaleCaptured;

        private void Start()
        {
            if (watchRoot != null) { baseScale = watchRoot.transform.localScale; scaleCaptured = true; }
            SetShown(false);
        }

        private void LateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || raceManager == null) { SetShown(false); return; }
            float rem = raceManager.GetRemainingSeconds();
            bool lobby = raceManager.state == RaceMath.STATE_LOBBY;
            if (lobby) rem = raceManager.GetLobbySecondsLeft();   // ロビー：出走時計（system-r1-09）
            bool active = rem >= 0f && raceManager.IsLocalEntered() && (lobby || raceManager.state == RaceMath.STATE_RACING);
            if (!active) { reveal = 0f; lastTickSecond = -1; SetShown(false); return; }

            bool want;
            if (local.IsUserInVR())
            {
                VRCPlayerApi.TrackingData hand = local.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                transform.SetPositionAndRotation(hand.position + hand.rotation * vrLocalOffset, hand.rotation * Quaternion.Euler(vrLocalEuler));
                want = HudMath.WristFacing(hand.rotation * Vector3.up, (head.position - hand.position).normalized);
            }
            else
            {
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                transform.SetPositionAndRotation(head.position + head.rotation * desktopOffset, head.rotation * Quaternion.Euler(desktopEuler));
                want = true;
            }
            reveal = Mathf.MoveTowards(reveal, want ? 1f : 0f, Time.deltaTime / FADE_SECONDS);
            SetShown(reveal > 0.001f);

            // 残り 10 秒からは見えていなくても時計の音（手綱を握ったままでも分かる）
            int sec = Mathf.CeilToInt(rem);
            if (!lobby && rem <= TICK_FROM_SECONDS && rem > 0f && sec != lastTickSecond)
            {
                lastTickSecond = sec;
                if (tickAudio != null) tickAudio.Play();
            }
            if (!shown) return;

            if (watchRoot != null && scaleCaptured)
            {
                float e = reveal * reveal * (3f - 2f * reveal);
                watchRoot.transform.localScale = baseScale * e;
            }
            MountController lm = raceManager.LocalMount();
            string t;
            if (lobby) t = "出走まで\n" + RaceMath.FormatClock(rem);
            else if (lm != null && lm.WrongWay) t = "<color=#D9402A>逆走</color>\n" + RaceMath.FormatClock(rem);
            else t = "残り\n" + RaceMath.FormatClock(rem);
            if (t != lastText)
            {
                lastText = t;
                if (faceText != null) faceText.text = t;
            }
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -rem * 0.1f);
            if (secondHand != null) secondHand.localRotation = Quaternion.Euler(0f, 0f, -rem * 6f);
        }

        private void SetShown(bool show)
        {
            if (show == shown) return;
            shown = show;
            if (watchRoot != null) watchRoot.SetActive(show);
        }
    }
}
