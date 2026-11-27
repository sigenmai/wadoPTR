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
    /// デスクトップ：頭の前・右上の隅（右下は能力の札：ui-r3-04）に ×0.6、文字盤を目のほうへ 15° 傾ける（裏返さない：ui-r2-02）。
    ///     首の札と同じ残り時間を重ねないよう、ロビーの「出走まで」と残り 30 秒（文字盤が赤くなる）のときだけ出す。
    /// 文字盤（ui-r2-16）：ステージの全体（330 秒、ロビーは 60 秒）を 1 周に。過ぎた数字は薄く、最後の 2 つ（11・12）は赤インク、
    ///     残り 30 秒で文字盤の字が全部赤インク。秒針は 1 秒ずつカチッと進む。
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
        [Tooltip("文字盤の数字 1..12（順に。LobbyBuilder.Watch が入れる）")]
        public TextMeshPro[] numerals;
        [Tooltip("残り 10 秒の時計の音（任意。1 秒ごとに Play）")]
        public AudioSource tickAudio;

        [Header("VR（左手からの相対）")]
        public Vector3 vrLocalOffset = new Vector3(0f, 0.03f, -0.06f);
        public Vector3 vrLocalEuler = new Vector3(0f, 90f, 0f);

        [Header("デスクトップ（頭からの相対）")]
        public Vector3 desktopOffset = new Vector3(0.40f, 0.15f, 0.55f);
        public Vector3 desktopEuler = new Vector3(-15f, 0f, 0f);
        [Tooltip("デスクトップでの倍率（VR は 1）。UiStyle.DESKTOP_WATCH_SCALE")]
        public float desktopScale = 0.6f;
        [Tooltip("デスクトップで出す残り秒数（これ以下・ロビーのときだけ）")]
        public float desktopShowBelow = 30f;

        private bool shown = true;
        private string lastText = "";
        private float reveal;
        private int lastTickSecond = -1;
        private Vector3 baseScale = Vector3.one;
        private bool scaleCaptured;
        private int lastNumeralKey = -1;
        private Color InkC = new Color(0.23f, 0.15f, 0.08f);
        private Color RedC = new Color(0.72f, 0.12f, 0.08f);

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
            // ゴールした人には時計を出さない（ui-r3-10：カチカチ・赤い文字盤は終わったレースのもの）
            if (active && !lobby)
            {
                MountController fm = raceManager.LocalMount();
                if (fm != null && fm.syncFinishAt >= 0.0 && fm.syncFinishAt >= raceManager.stageStartServer) active = false;
            }
            if (!active) { reveal = 0f; lastTickSecond = -1; SetShown(false); return; }

            bool want;
            bool vr = local.IsUserInVR();
            if (vr)
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
                want = lobby || rem <= desktopShowBelow;   // 首の札と同じ時間を重ねない（ui-r2-02）
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
                watchRoot.transform.localScale = baseScale * (e * (vr ? 1f : desktopScale));
            }
            MountController lm = raceManager.LocalMount();
            string t;
            bool late = !lobby && rem <= desktopShowBelow;
            if (lobby) t = "出走まで\n" + RaceMath.FormatClock(rem);
            else if (lm != null && lm.WrongWay) t = "<color=#D9402A>逆走</color>\n" + RaceMath.FormatClock(rem);
            else t = "残り\n" + RaceMath.FormatClock(rem);
            if (late) t = "<color=#B81F14>" + t + "</color>";
            if (t != lastText)
            {
                lastText = t;
                if (faceText != null) faceText.text = t;
            }
            // 文字盤 = ステージの全体（ui-r2-16）。長い針が 12 に来たら時間切れ。
            // 先頭がゴールしたら 60 秒の新しい 1 周に（ui-r3-02：針が 12 の近くへ飛ばない）
            bool leader = !lobby && raceManager.firstFinishServer >= 0.0 && raceManager.firstFinishServer >= raceManager.stageStartServer;
            float total = HudMath.DialTotal(lobby, leader);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, HudMath.DialAngle(rem, total));
            if (secondHand != null) secondHand.localRotation = Quaternion.Euler(0f, 0f, HudMath.SecondHandAngle(rem));
            UpdateNumerals(rem, total, late);
        }

        private void UpdateNumerals(float rem, float total, bool late)
        {
            if (numerals == null) return;
            int key = late ? 1 : 0;
            for (int k = 1; k <= 12; k++) key = key * 3 + HudMath.NumeralState(k, rem, total);
            if (key == lastNumeralKey) return;
            lastNumeralKey = key;
            for (int i = 0; i < numerals.Length && i < 12; i++)
            {
                TextMeshPro n = numerals[i];
                if (n == null) continue;
                int stt = HudMath.NumeralState(i + 1, rem, total);
                Color c = late || stt == 2 ? RedC : InkC;
                c.a = stt == 0 ? 0.25f : 1f;
                n.color = c;
            }
        }

        private void SetShown(bool show)
        {
            if (show == shown) return;
            shown = show;
            if (watchRoot != null) watchRoot.SetActive(show);
        }
    }
}
