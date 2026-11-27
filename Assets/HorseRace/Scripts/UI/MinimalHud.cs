using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 最小限の補助表示（古い紙・真鍮風）。視界の端に「3位」「残り 3:12」だけを小さく出す。デスクトップ・VR 共通。
    /// VRChat には画面スペースの UI が無いので、頭の前の端（既定は左下）に毎 LateUpdate で置くワールド空間の板で表現する。
    /// このスクリプトはロビーの真鍮のレバー（Collider 付き）に置き、Interact で表示の ON/OFF を切り替える（既定 ON、ローカルのみ）。
    /// hudRoot はこのオブジェクトの子にしない（レバーと一緒に動かないように）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MinimalHud : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("古い紙の板（ワールド空間 Canvas など）。このオブジェクトの子にしない")]
        public GameObject hudRoot;
        public TextMeshProUGUI rankText;
        public TextMeshProUGUI timeText;
        [Tooltip("頭からの相対位置（左下の端）")]
        public Vector3 headOffset = new Vector3(-0.17f, -0.12f, 0.4f);
        [Tooltip("レバーの取っ手（任意。ON/OFF で傾ける）")]
        public Transform leverHandle;
        public Vector3 leverOnEuler = new Vector3(-30f, 0f, 0f);
        public Vector3 leverOffEuler = new Vector3(30f, 0f, 0f);
        [Tooltip("レバー横の札（任意）")]
        public TextMeshPro leverLabel;

        /// <summary>オプション：補助表示を出すか（ローカル、既定 ON）。</summary>
        public bool hudEnabled = true;

        private bool shown = true;
        private string lastRank = "";
        private string lastTime = "";

        private void Start()
        {
            ApplyLever();
            SetShown(false);
        }

        public override void Interact()
        {
            hudEnabled = !hudEnabled;
            ApplyLever();
        }

        private void ApplyLever()
        {
            if (leverHandle != null) leverHandle.localRotation = Quaternion.Euler(hudEnabled ? leverOnEuler : leverOffEuler);
            if (leverLabel != null) leverLabel.text = hudEnabled ? "順位・残り時間の札：出す" : "順位・残り時間の札：しまう";
        }

        private void LateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || raceManager == null || hudRoot == null) { SetShown(false); return; }
            int st = raceManager.state;
            bool show = hudEnabled && st != RaceMath.STATE_LOBBY && raceManager.IsLocalEntered();
            SetShown(show);
            if (!show) return;

            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            hudRoot.transform.SetPositionAndRotation(head.position + head.rotation * headOffset, head.rotation);

            int rank = st == RaceMath.STATE_FINAL_RESULT ? raceManager.GetOverallRankOf(local.playerId) : raceManager.GetRankOf(local.playerId);
            string r = rank > 0 ? rank.ToString() + "位" : "―";
            float rem = raceManager.GetRemainingSeconds();
            string t = rem >= 0f ? "残り " + RaceMath.FormatClock(rem) : "";
            if (r != lastRank)
            {
                lastRank = r;
                if (rankText != null) rankText.text = r;
            }
            if (t != lastTime)
            {
                lastTime = t;
                if (timeText != null) timeText.text = t;
            }
        }

        private void SetShown(bool show)
        {
            if (show == shown) return;
            shown = show;
            if (hudRoot != null) hudRoot.SetActive(show);
        }
    }
}
