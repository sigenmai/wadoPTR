using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// スタート台の号砲（机に置いた拳銃。クラス名は以前の鐘のまま）。Interact で撃つ＝レース開始（RaceManager.RequestStart）。
    /// 撃てるのはマスター、または出走時計の締め切りまで 30 秒未満のときのエントリー者（RaceMath.CanRingStart、system-r1-09）。
    /// 撃てない人が触ると撃鉄が少し動いてカチッ（tapSound）と鳴るだけ。紙の札（noteText）には出走時計「出走まで 0:42」を書く。
    /// ひとりのエントリーは「ひとりで走る（タイムアタック）」とコース記録（system-r2-04）、総合結果の後は「もう一戦」（system-r2-06）。
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
            if (raceManager.ActiveCount() == 0)
            {
                ShowNote("台帳に誰もサインしていない");
                Tap();
                return;
            }
            if (raceManager.LocalCanStart())
            {
                raceManager.RingBell();
            }
            else
            {
                Tap();
                ShowNote(raceManager.IsLocalEntered() ? "出走 30 秒前から撃てる（主催者はいつでも）" : "台帳にサインした人が出走 30 秒前から撃てる");
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

        private float noteHoldUntil;
        private float nextClock;
        private string lastClock = "";

        private void Update()
        {
            UpdateClock();
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
            lastClock = "";
            if (msg.Length > 0)
            {
                noteText.text = msg;
                noteHoldUntil = Time.time + 4f;
            }
            else noteHoldUntil = 0f;
        }

        /// <summary>出走時計（0.25 秒ごと）：「出走まで 0:42」／2 人目を待つ／レース中。</summary>
        private void UpdateClock()
        {
            if (noteText == null || raceManager == null) return;
            if (Time.time < nextClock || Time.time < noteHoldUntil) return;
            nextClock = Time.time + 0.25f;
            string t = ClockNote(raceManager);
            if (t == lastClock) return;
            lastClock = t;
            noteText.text = t;
        }

        public static string ClockNote(RaceManager rm)
        {
            if (rm.state != RaceMath.STATE_LOBBY) return "レース中";
            float left = rm.GetLobbySecondsLeft();
            bool solo = rm.ActiveCount() == 1;
            if (left >= 0f)
            {
                string s = "";
                if (solo) s += "<b>ひとりで走る（タイムアタック）</b>\n";   // system-r2-04
                else if (rm.rematch) s += "<b>もう一戦</b>（台帳で取り消せる）\n";   // system-r2-06
                s += "<b><size=150%>出走まで " + RaceMath.FormatClock(left) + "</size></b>";
                if (left < RaceMath.LOBBY_ANYONE_SECONDS) s += "\n号砲：出走者なら誰でも";
                if (solo)
                {
                    string rec = rm.CourseRecordLine(0);
                    if (rec.Length > 0) s += "\n<size=80%>" + rec + "</size>";
                }
                return s;
            }
            return DefaultNote();
        }

        public static string DefaultNote()
        {
            return "台帳にサインで出走時計が動く\n（ひとりなら 30 秒でタイムアタック）";
        }

        public void _ClearNote()
        {
            noteHoldUntil = 0f;
        }
    }
}
