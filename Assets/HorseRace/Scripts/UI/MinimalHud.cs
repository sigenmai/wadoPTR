using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 首の札（補助表示、古い紙と真鍮の文字）。手配書の紙 1 枚に「第1幕 荒野」「3位」「残り 3:12」「■■■□□□ ゴール」と持久力の印 5 つ。
    ///  - VR：自分の乗り物の鞍の前・たてがみの上（鞍の点 + neckOffset）に置き、乗り手のほうへ 35° 起こす（顔には付けない：参考 13）。
    ///        位置は乗り物に固定（速く走っても遅れない）、向きだけなめらかに追う。
    ///  - デスクトップ：頭の前・左下の端。向きだけ 0.15 秒遅れて追う（位置は頭に固定）。
    ///  - カウントダウンの数字・ゴールの着順と点・CP の通過・CP の飛ばし・順位の上下を、跳ねる拡大と色で知らせる（Starter・GoalPresenter・Checkpoint から呼ぶ）。
    ///  - 持久力の印（宝石 5 つ）：ceil(持久力/20) 個を金、残りを革の色。満タンは小さく暗く、残り 1 つ以下・ばてたら赤く 3Hz で脈打ち、馬の息を大きくする。
    /// このスクリプトはロビーのレバー（Collider 付き）に置き、Interact で表示の ON/OFF（既定 ON、ローカルのみ）。hudRoot はこのオブジェクトの子にしない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MinimalHud : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("札（手配書の紙と文字・印の親）。このオブジェクトの子にしない")]
        public GameObject hudRoot;
        [Tooltip("順位（大きな数字）。カウントダウン・ゴールの知らせもここ")]
        public TextMeshPro rankText;
        public TextMeshPro timeText;
        [Tooltip("札の上の見出し（ステージ名、ステージの色）")]
        public TextMeshPro headerText;
        [Tooltip("札の下の進み具合（■■■□□□ ゴール）と CP の知らせ")]
        public TextMeshPro progressText;

        [Header("VR：首（鞍の前）")]
        [Tooltip("実行中に自分の乗り物（RaceManager.LocalMount）が入る")]
        public Transform neckAnchor;
        [Tooltip("鞍の点からの札の位置（乗り物ローカル）")]
        public Vector3 neckOffset = new Vector3(0f, 0.25f, 0.55f);
        public float neckPitch = 35f;
        public float neckFollow = 10f;

        [Header("デスクトップ：頭")]
        [Tooltip("頭からの相対位置（左下の端）")]
        public Vector3 headOffset = new Vector3(-0.17f, -0.12f, 0.4f);
        public float desktopLag = 0.15f;

        [Header("持久力の印（宝石 5 つ）")]
        public Renderer[] staminaPips;
        public Transform pipRow;
        public Material pipLit;
        public Material pipUnlit;
        public Material pipDanger;
        [Tooltip("満タンのときの控えめな印（紙の色に 30% だけ金を混ぜた色）")]
        public Material pipFaint;

        [Header("音（2D、任意）")]
        [Tooltip("CP を通った音（ピッチを 1.0 → 1.4 に上げる）")]
        public AudioSource cpTick;
        [Tooltip("ゴールの音（PlayOneShot、clip が無ければ Play）")]
        public AudioSource finishAudio;
        public AudioClip finishClip;
        [Tooltip("抜かれたときの小さな風切り音（任意）")]
        public AudioSource whoosh;

        [Header("レバー")]
        [Tooltip("レバーの取っ手（任意。ON/OFF で傾ける）")]
        public Transform leverHandle;
        public Vector3 leverOnEuler = new Vector3(-30f, 0f, 0f);
        public Vector3 leverOffEuler = new Vector3(30f, 0f, 0f);
        [Tooltip("レバー横の札（任意）")]
        public TextMeshPro leverLabel;

        /// <summary>オプション：補助表示を出すか（ローカル、既定 ON）。</summary>
        public bool hudEnabled = true;

        private Color Ink = new Color(0.23f, 0.15f, 0.08f);
        private Color Gold = new Color(1f, 0.78f, 0.27f);
        private Color Red = new Color(0.85f, 0.25f, 0.15f);

        private bool shown = true;
        private string lastRank = "";
        private string lastTime = "";
        private string lastHeader = "";
        private string lastProgress = "";
        private int lastRankInt;
        private int lastRankStage = -1;
        private float rankFlashUntil;
        private string rankArrow = "";
        private Color rankColor;

        private string bannerText = "";
        private Color bannerColor;
        private float bannerUntil;
        private float punchAt = -10f;
        private float punchFrom = 1f;

        private string noteText = "";
        private Color noteColor;
        private float noteUntil;

        private int lastLit = -1;
        private int lastMood = -1;
        private bool lastPulseOn;
        private MountController alarmMount;

        private bool rotInit;
        private Quaternion smoothRot;
        private Vector3 rankBaseScale = Vector3.one;
        private bool scaleCaptured;

        private void Start()
        {
            rankColor = Ink;
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
            if (leverLabel != null) leverLabel.text = hudEnabled ? "札：出す" : "札：しまう";
        }

        // =====================================================================
        // 他から呼ぶ知らせ（ローカル）
        // =====================================================================

        /// <summary>大きな知らせ（カウントダウンの数字・ゴール）。順位の代わりに seconds 秒出して、from → 1 で跳ねる。</summary>
        public void ShowBanner(string text, Color color, float seconds, float from)
        {
            bannerText = text;
            bannerColor = color;
            bannerUntil = Time.time + seconds;
            Punch(from);
            lastRank = "";
        }

        /// <summary>CP を通った（index = 1 始まり、count = ゴールを含む数）。音のピッチを上げ、札を跳ねさせる。</summary>
        public void CheckpointPassed(int index, int count)
        {
            if (cpTick != null)
            {
                float f = count > 1 ? Mathf.Clamp01((index - 1f) / (count - 1f)) : 1f;
                cpTick.pitch = 1f + 0.4f * f;
                cpTick.Play();
            }
            Punch(1.15f);
        }

        /// <summary>順番を飛ばして先の CP に入った：赤い知らせを 2 秒。</summary>
        public void MissedCheckpoint()
        {
            ShowNote("関門を通り過ぎた ↩", Red, 2f);
        }

        /// <summary>自分のゴール：着順・点・タイムを 4 秒、ゴールの音。</summary>
        public void ShowFinish(int place, int points, float seconds)
        {
            ShowBanner(HudMath.FinishText(place, points, seconds), Gold, 4f, 1.6f);
            if (finishAudio != null)
            {
                if (finishClip != null) finishAudio.PlayOneShot(finishClip);
                else finishAudio.Play();
            }
        }

        public void ShowNote(string text, Color color, float seconds)
        {
            noteText = text;
            noteColor = color;
            noteUntil = Time.time + seconds;
            lastProgress = "";
        }

        private void Punch(float from)
        {
            punchAt = Time.time;
            punchFrom = from;
        }

        // =====================================================================

        private void LateUpdate()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || raceManager == null || hudRoot == null) { SetShown(false); return; }
            RaceManager rm = raceManager;
            int st = rm.state;
            bool show = hudEnabled && st != RaceMath.STATE_LOBBY && rm.IsLocalEntered();
            SetShown(show);
            if (!show)
            {
                SetAlarm(null);
                return;
            }
            MountController lm = rm.LocalMount();
            Place(local, lm);
            UpdateTexts(local, rm, st);
            UpdatePips(lm, st);
            UpdatePunch();
        }

        private void Place(VRCPlayerApi local, MountController lm)
        {
            Transform root = hudRoot.transform;
            float dt = Time.deltaTime;
            if (local.IsUserInVR() && lm != null)
            {
                neckAnchor = lm.transform;
                int type = lm.SeatType();
                Vector3 lp = new Vector3(neckOffset.x, SeatMath.DefaultSaddleY(type) + neckOffset.y, SeatMath.DefaultSaddleZ(type) + neckOffset.z);
                Vector3 pos = neckAnchor.TransformPoint(lp);
                Quaternion goal = neckAnchor.rotation * Quaternion.Euler(neckPitch, 0f, 0f);
                if (!rotInit) { smoothRot = goal; rotInit = true; }
                smoothRot = Quaternion.Slerp(smoothRot, goal, 1f - Mathf.Exp(-neckFollow * dt));
                root.SetPositionAndRotation(pos, smoothRot);
                return;
            }
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            if (!rotInit) { smoothRot = head.rotation; rotInit = true; }
            float k = desktopLag > 0.001f ? 1f - Mathf.Exp(-dt / desktopLag) : 1f;
            smoothRot = Quaternion.Slerp(smoothRot, head.rotation, k);
            root.SetPositionAndRotation(head.position + smoothRot * headOffset, smoothRot);
        }

        private void UpdateTexts(VRCPlayerApi local, RaceManager rm, int st)
        {
            float now = Time.time;
            // 見出し：ステージ名（ステージの色）
            string h = "<color=" + HudMath.ColorHex(RaceManager.StageColor(rm.stage)) + ">" + RaceManager.StageLabel(rm.stage) + "</color>";
            if (st == RaceMath.STATE_FINAL_RESULT) h = "総合結果";
            if (h != lastHeader)
            {
                lastHeader = h;
                if (headerText != null) headerText.text = h;
            }

            // 順位（知らせが出ている間は知らせ）
            int rank = st == RaceMath.STATE_FINAL_RESULT ? rm.GetOverallRankOf(local.playerId) : rm.GetRankOf(local.playerId);
            if (st == RaceMath.STATE_RACING)
            {
                if (lastRankStage != rm.stageSeq) { lastRankStage = rm.stageSeq; lastRankInt = rank; }
                if (rank > 0 && lastRankInt > 0 && rank != lastRankInt)
                {
                    bool up = rank < lastRankInt;
                    rankArrow = up ? "↑" : "↓";
                    rankColor = up ? Gold : Red;
                    rankFlashUntil = now + 1f;
                    if (up) Punch(1.35f);
                    else if (whoosh != null) whoosh.Play();
                }
                if (rank > 0) lastRankInt = rank;
            }
            if (now >= rankFlashUntil) { rankArrow = ""; rankColor = Ink; }

            string r;
            Color rc;
            if (now < bannerUntil) { r = bannerText; rc = bannerColor; }
            else if (st == RaceMath.STATE_COUNTDOWN) { r = "まもなく"; rc = Ink; }
            else { r = HudMath.RankMarkup(rank) + rankArrow; rc = rankColor; }
            string key = r + HudMath.ColorHex(rc);
            if (key != lastRank)
            {
                lastRank = key;
                if (rankText != null)
                {
                    rankText.text = r;
                    rankText.color = rc;
                }
            }

            float rem = rm.GetRemainingSeconds();
            string t = rem >= 0f && st == RaceMath.STATE_RACING ? "残り\n" + RaceMath.FormatClock(rem) : "";
            if (t != lastTime)
            {
                lastTime = t;
                if (timeText != null) timeText.text = t;
            }

            // 進み具合（CP の知らせが出ている間は知らせ）
            string p;
            if (now < noteUntil) p = "<color=" + HudMath.ColorHex(noteColor) + ">" + noteText + "</color>";
            else
            {
                MountController lm = rm.LocalMount();
                p = st == RaceMath.STATE_RACING && lm != null ? HudMath.ProgressBar(lm.syncCp, rm.CheckpointCount(rm.stage), HudMath.PROGRESS_SEGMENTS) : "";
            }
            if (p != lastProgress)
            {
                lastProgress = p;
                if (progressText != null) progressText.text = p;
            }
        }

        private void UpdatePips(MountController lm, int st)
        {
            if (staminaPips == null) return;
            bool racing = lm != null && st == RaceMath.STATE_RACING;
            float stamina = lm != null ? lm.Stamina : 100f;
            bool exhausted = lm != null && lm.Exhausted;
            int lit = HudMath.StaminaPips(stamina, staminaPips.Length);
            int mood = racing ? HudMath.StaminaMood(stamina, exhausted, staminaPips.Length) : 0;
            bool pulseOn = mood == 2 && Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
            if (lit != lastLit || mood != lastMood || pulseOn != lastPulseOn)
            {
                lastLit = lit;
                lastMood = mood;
                lastPulseOn = pulseOn;
                for (int i = 0; i < staminaPips.Length; i++)
                {
                    Renderer pr = staminaPips[i];
                    if (pr == null) continue;
                    Material m = i < lit ? (mood == 2 && pulseOn ? pipDanger : (mood == 0 && pipFaint != null ? pipFaint : pipLit)) : pipUnlit;
                    if (mood == 2 && lit == 0 && i == 0 && pulseOn) m = pipDanger;   // 空でも 1 つ目を赤く脈打たせる
                    if (m != null) pr.sharedMaterial = m;
                }
            }
            if (pipRow != null)
            {
                float s = mood == 0 ? 0.8f : (mood == 2 ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * 3f)) : 1f);
                pipRow.localScale = new Vector3(s, s, s);
            }
            SetAlarm(racing && mood == 2 ? lm : null);
        }

        private void SetAlarm(MountController m)
        {
            if (m == alarmMount) return;
            if (alarmMount != null && alarmMount.visual != null) alarmMount.visual.breathAlarm = false;
            alarmMount = m;
            if (alarmMount != null && alarmMount.visual != null) alarmMount.visual.breathAlarm = true;
        }

        private void UpdatePunch()
        {
            if (rankText == null) return;
            Transform rt = rankText.transform;
            if (!scaleCaptured) { rankBaseScale = rt.localScale; scaleCaptured = true; }
            float s = HudMath.PunchScale(Time.time - punchAt, punchFrom);
            rt.localScale = rankBaseScale * s;
        }

        private void SetShown(bool show)
        {
            if (show == shown) return;
            shown = show;
            if (hudRoot != null) hudRoot.SetActive(show);
            if (!show) rotInit = false;
        }
    }
}
