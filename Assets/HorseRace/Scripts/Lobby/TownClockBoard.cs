using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 町の大時計（ui-r2-13）：大通りのスタート台の奥に立てた大きな掲示板（NoticeBoard ×1.4）に「出走まで 0:42」（3pt）。
    /// 町を歩いている人みんなが同じ時計を見て集まれるように（参考調査 §11：Riders Republic の決まった開始時刻と見える秒読み）。
    ///  - 30 秒未満は琥珀、10 秒未満は赤。残り 10 秒からは 1 秒ごとに 1.3 → 1 に跳ねる。
    ///  - 時計が動いていないときは「あと 1 人で出走時計が動く」、レース中は「レース中 第2幕 砂漠」。
    /// 文字は HudMath.TownClockText（単体テストあり）。同期しない（RaceManager の同期値を読むだけ）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TownClockBoard : UdonSharpBehaviour
    {
        public const float PUNCH_FROM = 1.3f;
        public const float PUNCH_LAST_SECONDS = 10f;

        public RaceManager raceManager;
        public TextMeshPro clockText;
        [Tooltip("跳ねる拡大をかける Transform（文字）")]
        public Transform punchRoot;

        private string lastText = "";
        private int lastSecond = -1;
        private float punchAt = -10f;
        private Vector3 baseScale = Vector3.one;
        private bool scaleCaptured;
        private float nextRefresh;

        private void Update()
        {
            if (raceManager == null || clockText == null) return;
            if (punchRoot != null)
            {
                if (!scaleCaptured) { baseScale = punchRoot.localScale; scaleCaptured = true; }
                punchRoot.localScale = baseScale * HudMath.PunchScale(Time.time - punchAt, PUNCH_FROM);
            }
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.1f;
            RaceManager rm = raceManager;
            bool racing = rm.state != RaceMath.STATE_LOBBY;
            float left = racing ? -1f : rm.GetLobbySecondsLeft();
            if (left >= 0f && left <= PUNCH_LAST_SECONDS)
            {
                int sec = Mathf.CeilToInt(left);
                if (sec != lastSecond)
                {
                    lastSecond = sec;
                    punchAt = Time.time;
                }
            }
            else lastSecond = -1;
            string t = HudMath.TownClockText(left, rm.ActiveCount(), racing, rm.stage);
            if (t == lastText) return;
            lastText = t;
            clockText.text = t;
        }
    }
}
