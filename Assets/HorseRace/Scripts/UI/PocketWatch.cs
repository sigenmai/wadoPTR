using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 懐中時計（ステージの残り時間）。エントリーしていてカウントダウン〜レース中だけ見える。
    /// VR：毎 LateUpdate で左手のトラッキング位置に付ける（手首の内側）。デスクトップ：頭の前・視界の下のほうに小さく固定。
    /// このスクリプトの GameObject は常に有効にしておき、見た目は子の watchRoot に置く（watchRoot を出し入れする）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PocketWatch : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("時計の見た目（このオブジェクトの子）")]
        public GameObject watchRoot;
        [Tooltip("文字盤の文字（「残り 3:12」）")]
        public TMP_Text faceText;
        [Tooltip("分針・秒針（任意。ローカル Z 軸まわりに回す）")]
        public Transform minuteHand;
        public Transform secondHand;

        [Header("VR（左手からの相対）")]
        public Vector3 vrLocalOffset = new Vector3(0f, 0.03f, -0.06f);
        public Vector3 vrLocalEuler = new Vector3(0f, 90f, 0f);

        [Header("デスクトップ（頭からの相対）")]
        public Vector3 desktopOffset = new Vector3(0f, -0.2f, 0.45f);
        public Vector3 desktopEuler = new Vector3(-20f, 180f, 0f);

        private bool shown = true;
        private string lastText = "";

        private void Start()
        {
            SetShown(false);
        }

        private void LateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || raceManager == null) { SetShown(false); return; }
            float rem = raceManager.GetRemainingSeconds();
            bool show = rem >= 0f && raceManager.IsLocalEntered();
            SetShown(show);
            if (!show) return;

            if (local.IsUserInVR())
            {
                VRCPlayerApi.TrackingData hand = local.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
                transform.SetPositionAndRotation(hand.position + hand.rotation * vrLocalOffset, hand.rotation * Quaternion.Euler(vrLocalEuler));
            }
            else
            {
                VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                transform.SetPositionAndRotation(head.position + head.rotation * desktopOffset, head.rotation * Quaternion.Euler(desktopEuler));
            }

            string t = "残り " + RaceMath.FormatClock(rem);
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
