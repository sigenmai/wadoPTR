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
    ///  - デスクトップ：頭の前・左下の端に ×0.55 で小さく（ui-r2-03：60° の縦の視野・16:9 の内側）。向きだけ 0.15 秒遅れて追う（位置は頭に固定）。
    ///  - 命中の知らせ（ui-r2-06）：撃った側「命中！ 名前」（金）、撃たれた側「投げ縄に掛かった」など（赤）。
    ///  - ステージの合間（ui-r2-08）：総合順位・合計点・首位との差・次のステージ。総合順位が上下したら跳ねて金／赤。
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
        public Vector3 headOffset = new Vector3(-0.40f, -0.19f, 0.55f);
        [Tooltip("デスクトップでの札の倍率（VR は 1）。UiStyle.DESKTOP_HUD_SCALE")]
        public float desktopScale = 0.55f;
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

        [Header("練習（ui-r3-07）")]
        [Tooltip("練習場（練習用の馬に乗っている間も札を出す）")]
        public PracticeArea practiceArea;
        [Tooltip("練習場の往復タイム（計測中の秒数・自己ベスト）")]
        public PracticeLap practiceLap;

        /// <summary>このセッションで自分が能力を使った回数（ローカル）。能力の札のボタンの説明は最初の 3 回だけ（ui-r2-07）。</summary>
        [HideInInspector] public int abilityCastCount;

        /// <summary>オプション：補助表示を出すか（ローカル、既定 ON）。</summary>
        public bool hudEnabled = true;

        private Color Ink = new Color(0.23f, 0.15f, 0.08f);
        private Color Gold = new Color(1f, 0.78f, 0.27f);
        private Color Red = new Color(0.85f, 0.25f, 0.15f);
        private Color Teal = new Color(0.2f, 0.75f, 0.7f);

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
        // ui-r3-09：知らせの優先度（0 = 青緑の情報、1 = 金のごほうび、2 = 赤の危険）と待ちの 1 枠
        private int noteRank;
        private string queuedText = "";
        private Color queuedColor;
        private float queuedSeconds;
        private int queuedRank;

        // ui-r3-02：先頭のゴール（ステージごとに 1 回）
        private int lastFirstFinishSeq = -1;
        // ui-r3-03：時間切れになったステージ
        private int timeUpSeq = -1;
        private bool practiceShown;

        private int lastLit = -1;
        private int lastMood = -1;
        private bool lastPulseOn;
        private MountController alarmMount;

        private bool rotInit;
        private Quaternion smoothRot;
        private Vector3 rankBaseScale = Vector3.one;
        private bool scaleCaptured;
        private Vector3 rootBaseScale = Vector3.one;
        private bool rootScaleCaptured;
        private bool lastVr;
        private bool vrKnown;

        // ステージの合間の総合順位（ui-r2-08）
        private int overallSeen;
        private int overallBaseline;
        private int resultSeq = -1;
        private int overallFlashSeq = -1;

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

        /// <summary>
        /// VR の手の振動（ui-r3-05、spec 5.14「VR は手綱の振動も」）。VR のときだけ両手（rightOnly なら右手だけ）。
        /// 値は HudMath.ClampHaptic* で安全な範囲へ。デスクトップでは何もしない。
        /// </summary>
        public void Buzz(float seconds, float amp, float freq)
        {
            Buzz(seconds, amp, freq, false);
        }

        public void Buzz(float seconds, float amp, float freq, bool rightOnly)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || !local.IsUserInVR()) return;
            float s = HudMath.ClampHapticSeconds(seconds);
            float a = HudMath.ClampHapticAmp(amp);
            float f = HudMath.ClampHapticHz(freq);
            local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, s, a, f);
            if (!rightOnly) local.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, s, a, f);
        }

        /// <summary>
        /// 時間切れ（ui-r3-03）：RaceManager が STAGE_RESULT に入ったとき、まだゴールしていなかった本人に。
        /// 赤い「時間切れ」の判を 2.5 秒、合間の進み具合の行に「0点（時間切れ）」。
        /// </summary>
        public void ShowTimeUp(int stageSeq)
        {
            timeUpSeq = stageSeq;
            ShowBanner(HudMath.TimeUpText(), Red, 2.5f, 1.6f);
            Buzz(HudMath.HAPTIC_HIT_TAKEN_S, HudMath.HAPTIC_HIT_TAKEN_AMP, HudMath.HAPTIC_HIT_TAKEN_HZ);
        }

        /// <summary>練習の往復が終わった（ui-r3-07、PracticeLap から）：タイムを 2 秒、自己ベストなら金と「自己ベスト！」、ほかは青緑。</summary>
        public void PracticeLapDone(float lap, bool newBest)
        {
            if (newBest) ShowBanner(HudMath.FormatClockTenths(lap) + "\n<size=45%>自己ベスト！</size>", Gold, 2f, 1.4f);
            else ShowBanner(HudMath.FormatClockTenths(lap), Teal, 2f, 1.4f);
            Buzz(HudMath.HAPTIC_SHORTCUT_S, HudMath.HAPTIC_SHORTCUT_AMP, HudMath.HAPTIC_SHORTCUT_HZ);
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
            Buzz(HudMath.HAPTIC_CP_S, HudMath.HAPTIC_CP_AMP, HudMath.HAPTIC_CP_HZ);
        }

        /// <summary>順番を飛ばして先の CP に入った：赤い知らせを 2 秒（危険、ui-r3-09）。</summary>
        public void MissedCheckpoint()
        {
            ShowNote("◀ 関門を通り過ぎた", Red, 2f, HudMath.NOTE_DANGER);
        }

        /// <summary>自分のゴール：着順・点・タイムを 4 秒、ゴールの音。</summary>
        public void ShowFinish(int place, int points, float seconds, int stage)
        {
            ShowBanner(HudMath.FinishText(place, points, seconds, stage), Gold, 4f, 1.6f);
            Buzz(HudMath.HAPTIC_FINISH_S, HudMath.HAPTIC_FINISH_AMP, HudMath.HAPTIC_FINISH_HZ);
            if (finishAudio != null)
            {
                if (finishClip != null) finishAudio.PlayOneShot(finishClip);
                else finishAudio.Play();
            }
        }

        /// <summary>撃った側の命中の知らせ（ui-r2-06）："命中！ 名前"（金）と跳ねる拡大。</summary>
        public void ShowHitConfirm(string targetName)
        {
            ShowNote(HudMath.HitConfirmText(targetName), Gold, 1.2f, HudMath.NOTE_REWARD);
            Punch(1.2f);
            Buzz(HudMath.HAPTIC_HIT_CONFIRM_S, HudMath.HAPTIC_HIT_CONFIRM_AMP, HudMath.HAPTIC_HIT_CONFIRM_HZ, true);   // 狙った手（右）
        }

        /// <summary>撃たれた側の知らせ（ui-r2-06）：kind = 能力の番号か HudMath.HIT_REVOLVER。</summary>
        public void ShowHitTaken(int kind)
        {
            ShowNote(HudMath.HitTakenText(kind), Red, 1.5f, HudMath.NOTE_DANGER);
            Punch(1.15f);
            Buzz(HudMath.HAPTIC_HIT_TAKEN_S, HudMath.HAPTIC_HIT_TAKEN_AMP, HudMath.HAPTIC_HIT_TAKEN_HZ);   // 重い一発
        }

        /// <summary>近道の CP を通った（ui-r2-17）：青緑の「近道！」と跳ねる拡大。</summary>
        public void ShowShortcut()
        {
            ShowNote(HudMath.ShortcutText(), Teal, 1.5f, HudMath.NOTE_INFO);
            Punch(1.25f);
            Buzz(HudMath.HAPTIC_SHORTCUT_S, HudMath.HAPTIC_SHORTCUT_AMP, HudMath.HAPTIC_SHORTCUT_HZ);
        }

        /// <summary>情報の知らせ（優先度 0）。</summary>
        public void ShowNote(string text, Color color, float seconds)
        {
            ShowNote(text, color, seconds, HudMath.NOTE_INFO);
        }

        /// <summary>
        /// 知らせ（ui-r3-09）：priority 0 = 情報（青緑）、1 = ごほうび（金）、2 = 危険（赤）。
        /// 今の知らせより低い優先度は待ちの 1 枠へ入り、今の知らせが消えたら出る。同じか高い優先度は上書き。
        /// </summary>
        public void ShowNote(string text, Color color, float seconds, int priority)
        {
            float now = Time.time;
            if (!HudMath.NoteWins(priority, noteRank, noteUntil - now))
            {
                if (queuedText.Length == 0 || priority >= queuedRank)
                {
                    queuedText = text;
                    queuedColor = color;
                    queuedSeconds = seconds;
                    queuedRank = priority;
                }
                return;
            }
            // 上書きされる知らせがまだ残っていて、上書きする側より低くなければ待ちへ回す（危険を消さない）
            if (noteUntil > now && noteText.Length > 0 && priority > noteRank && (queuedText.Length == 0 || noteRank >= queuedRank))
            {
                queuedText = noteText;
                queuedColor = noteColor;
                queuedSeconds = noteUntil - now;
                queuedRank = noteRank;
            }
            noteText = text;
            noteColor = color;
            noteUntil = now + seconds;
            noteRank = priority;
            lastProgress = "";
        }

        /// <summary>今の知らせが消えたら待ちの知らせを出す。</summary>
        private void PromoteQueuedNote(float now)
        {
            if (now < noteUntil || queuedText.Length == 0) return;
            noteText = queuedText;
            noteColor = queuedColor;
            noteUntil = now + queuedSeconds;
            noteRank = queuedRank;
            queuedText = "";
            lastProgress = "";
        }

        public void _LeaderTick()
        {
            if (cpTick == null) return;
            cpTick.pitch = 0.7f;
            cpTick.Play();
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
            // 練習（ui-r3-07）：ロビーで練習用の馬に乗っている間も同じ札（持久力の印・知らせ・往復タイム）
            MountController pm = st == RaceMath.STATE_LOBBY ? PracticeMount() : null;
            bool practice = hudEnabled && pm != null;
            bool show = practice || (hudEnabled && st != RaceMath.STATE_LOBBY && rm.IsLocalEntered());
            SetShown(show);
            if (!show)
            {
                SetAlarm(null);
                practiceShown = false;
                return;
            }
            if (practice)
            {
                if (!practiceShown) { practiceShown = true; lastHeader = ""; lastRank = ""; lastTime = ""; lastProgress = ""; }
                Place(local, pm);
                UpdatePracticeTexts();
                UpdatePips(pm, true);
                UpdatePunch();
                return;
            }
            if (practiceShown) { practiceShown = false; lastHeader = ""; lastRank = ""; lastTime = ""; lastProgress = ""; }
            MountController lm = rm.LocalMount();
            Place(local, lm);
            UpdateTexts(local, rm, st);
            bool finished = lm != null && lm.syncFinishAt >= 0.0 && lm.syncFinishAt >= rm.stageStartServer;
            UpdatePips(lm, st == RaceMath.STATE_RACING && !finished);   // ゴールした後は警報を止める（ui-r3-10）
            UpdatePunch();
        }

        /// <summary>練習用の馬に乗っていればその馬（PracticeArea.posts から、ComfortVignette と同じ）。</summary>
        private MountController PracticeMount()
        {
            if (practiceArea == null || practiceArea.posts == null) return null;
            for (int i = 0; i < practiceArea.posts.Length; i++)
            {
                PracticeHorsePost p = practiceArea.posts[i];
                if (p != null && p.mount != null && p.IsLocalRiding()) return p.mount;
            }
            return null;
        }

        /// <summary>練習の札（ui-r3-07）：見出し「練習」、計測中の秒数、自己ベスト、南の門→北の門→南の門。</summary>
        private void UpdatePracticeTexts()
        {
            float now = Time.time;
            PromoteQueuedNote(now);
            string h = "練習";
            if (h != lastHeader) { lastHeader = h; if (headerText != null) headerText.text = h; }
            float live = practiceLap != null ? practiceLap.LiveSeconds() : -1f;
            string r;
            Color rc = Ink;
            if (now < bannerUntil) { r = bannerText; rc = bannerColor; }
            else r = live >= 0f ? HudMath.FormatClockTenths(live) : "―";
            string key = r + HudMath.ColorHex(rc);
            if (key != lastRank)
            {
                lastRank = key;
                if (rankText != null) { rankText.text = r; rankText.color = rc; }
            }
            string t = HudMath.PracticeBestText(practiceLap != null ? practiceLap.BestSeconds() : -1f);
            if (t != lastTime) { lastTime = t; if (timeText != null) timeText.text = t; }
            string p;
            if (now < noteUntil) p = "<color=" + HudMath.ColorHex(noteColor) + ">" + noteText + "</color>";
            else p = "<size=80%>" + HudMath.PracticeRouteLine(practiceLap != null ? practiceLap.Leg() : 0) + "</size>";
            if (p != lastProgress) { lastProgress = p; if (progressText != null) progressText.text = p; }
        }

        private void Place(VRCPlayerApi local, MountController lm)
        {
            Transform root = hudRoot.transform;
            float dt = Time.deltaTime;
            bool vr = local.IsUserInVR();
            if (!rootScaleCaptured) { rootBaseScale = root.localScale; rootScaleCaptured = true; }
            if (!vrKnown || vr != lastVr)
            {
                vrKnown = true;
                lastVr = vr;
                root.localScale = rootBaseScale * (vr ? 1f : desktopScale);   // ui-r2-03：デスクトップは小さく端に
            }
            if (vr && lm != null)
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
            PromoteQueuedNote(now);
            MountController lmp = rm.LocalMount();
            bool finished = st == RaceMath.STATE_RACING && lmp != null && lmp.syncFinishAt >= 0.0 && lmp.syncFinishAt >= rm.stageStartServer;
            // 先頭がゴールした（ui-r3-02）：残りが 1:00 に縮むことを、まだ走っている人に知らせる（ステージごとに 1 回）
            if (st == RaceMath.STATE_RACING && rm.firstFinishServer >= 0.0 && rm.firstFinishServer >= rm.stageStartServer && lastFirstFinishSeq != rm.stageSeq)
            {
                lastFirstFinishSeq = rm.stageSeq;
                bool fresh = Networking.GetServerTimeInSeconds() - rm.firstFinishServer < 6.0;
                if (fresh && !finished)
                {
                    ShowBanner(HudMath.LeaderFinishedText(), Red, 2.5f, 1.5f);
                    _LeaderTick();
                    SendCustomEventDelayedSeconds(nameof(_LeaderTick), 0.22f);
                }
            }
            // 見出し：ステージ名（ステージの色）
            string h = "<color=" + HudMath.ColorHex(RaceManager.StageColor(rm.stage)) + ">" + RaceManager.StageLabel(rm.stage) + "</color>";
            if (st == RaceMath.STATE_FINAL_RESULT) h = "総合結果";
            bool between = st == RaceMath.STATE_STAGE_RESULT;
            if (between) h = HudMath.BetweenStagesHeader(rm.stage + 1, RaceManager.STAGE_COUNT);   // ui-r2-08
            if (h != lastHeader)
            {
                lastHeader = h;
                if (headerText != null) headerText.text = h;
            }

            // 順位（知らせが出ている間は知らせ）
            int rank = st == RaceMath.STATE_FINAL_RESULT || between ? rm.GetOverallRankOf(local.playerId) : rm.GetRankOf(local.playerId);
            if (between)
            {
                // 総合順位が前のステージの合間から上下したら 1 回だけ跳ねて金／赤（ui-r2-08）
                if (resultSeq != rm.stageSeq) { resultSeq = rm.stageSeq; overallBaseline = overallSeen; }
                if (overallBaseline > 0 && rank > 0 && rank != overallBaseline && overallFlashSeq != rm.stageSeq)
                {
                    overallFlashSeq = rm.stageSeq;
                    bool up = rank < overallBaseline;
                    rankArrow = up ? "↑" : "↓";
                    rankColor = up ? Gold : Red;
                    rankFlashUntil = now + 2.5f;
                    Punch(1.35f);
                }
                if (rank > 0) overallSeen = rank;
            }
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
            int localSlot = rm.LocalSlot();
            if (now < bannerUntil) { r = bannerText; rc = bannerColor; }
            else if (st == RaceMath.STATE_COUNTDOWN) { r = "まもなく"; rc = Ink; }
            else if (finished) { r = HudMath.FinishedRank(rank); rc = Gold; }   // ui-r3-10（ゴール済みは順位表でゴール順に並ぶ＝着順）
            else { r = HudMath.RankMarkup(rank, rm.ActiveCount()) + rankArrow; rc = rankColor; }   // ui-r3-14：何人中か
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
            string t = rem >= 0f && st == RaceMath.STATE_RACING ? HudMath.RemainingMarkup(rem) : "";   // ≤ 60 秒は赤インク（ui-r3-02）
            if (finished) t = HudMath.FinishedTime((float)(lmp.syncFinishAt - rm.stageStartServer));   // ui-r3-10：時計はもう進めない
            if (between && localSlot >= 0) t = "計\n" + rm.slotTotalPoints[localSlot].ToString() + "点";
            if (t != lastTime)
            {
                lastTime = t;
                if (timeText != null) timeText.text = t;
            }

            // 進み具合（CP の知らせが出ている間は知らせ）
            string p;
            bool noteLive = now < noteUntil;
            bool wrongWay = st == RaceMath.STATE_RACING && !finished && lmp != null && lmp.WrongWay;
            if (st == RaceMath.STATE_RACING && lmp != null && lmp.ResetHoldProgress > 0f)
                p = "<color=" + HudMath.ColorHex(Red) + ">" + HudMath.ResetHoldText(lmp.ResetHoldProgress) + "</color>";   // system-r2-03
            else if (noteLive && noteRank >= HudMath.NOTE_DANGER) p = "<color=" + HudMath.ColorHex(noteColor) + ">" + noteText + "</color>";
            // 逆走は危険（ui-r3-09）：ごほうび・情報の知らせより上
            else if (wrongWay) p = "<color=" + HudMath.ColorHex(Red) + "><b>◀ 逆走</b></color>";   // system-r1-17（U+21A9 の矢印はフォントに無い：ui-r2-04）
            else if (noteLive) p = "<color=" + HudMath.ColorHex(noteColor) + ">" + noteText + "</color>";
            else if (finished) p = HudMath.FinishedLine();   // ui-r3-10：ゴールした人は観戦へ
            else if (st == RaceMath.STATE_FINAL_RESULT) p = "<size=70%>" + HudMath.RematchLine() + "</size>";   // system-r2-06
            else if (between && localSlot >= 0 && timeUpSeq == rm.stageSeq)   // ui-r3-03：時間切れの 0 点を理由と一緒に
                p = "<size=85%><color=" + HudMath.ColorHex(Red) + ">" + HudMath.TimeUpLine() + "</color>  "
                    + HudMath.BetweenStagesLine(rm.slotTotalPoints[localSlot], LeaderPoints(rm), rm.stage + 1, RaceManager.STAGE_COUNT) + "</size>";
            else if (between && localSlot >= 0) p = HudMath.BetweenStagesLine(rm.slotTotalPoints[localSlot], LeaderPoints(rm), rm.stage + 1, RaceManager.STAGE_COUNT);
            else if (st == RaceMath.STATE_RACING && rm.InStartGrace()) p = "撃ち合いまで " + Mathf.CeilToInt(rm.StartGraceLeft()).ToString() + "<size=70%>（順に解禁）</size>";   // system-r1-07 / r2-11
            else
            {
                p = st == RaceMath.STATE_RACING && lmp != null ? HudMath.ProgressBar(lmp.syncCp, rm.CheckpointCount(rm.stage), HudMath.PROGRESS_SEGMENTS) : "";
                // 風よけ（ドラフト：system-r1-13）・ひと息（疾走を早めにやめた回復：system-r1-01）を進み具合の横に ★ 1 つずつ
                // （文字だと札からあふれて縮み、VR で読めない大きさになる：ui-r2-05）
                if (p.Length > 0 && lmp.Draft > 0f) p += "<color=" + HudMath.ColorHex(Gold) + ">★</color>";
                if (p.Length > 0 && lmp.SecondWindActive) p += "<color=" + HudMath.ColorHex(Teal) + ">★</color>";
                // 前の騎手までの距離（ui-r3-14）：60m 以内だけ、8m 未満（風よけの届く距離）は金
                if (p.Length > 0 && localSlot >= 0)
                {
                    string gap = HudMath.GapAheadText(rm.GapAheadMeters(localSlot));
                    if (gap.Length > 0) p += " " + gap;
                }
            }
            if (p != lastProgress)
            {
                lastProgress = p;
                if (progressText != null) progressText.text = p;
            }
        }

        private int LeaderPoints(RaceManager rm)
        {
            int best = 0;
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                if (rm.slotPlayerId[i] >= 0 && rm.slotTotalPoints[i] > best) best = rm.slotTotalPoints[i];
            return best;
        }

        /// <summary>持久力の印。racing = 警報（赤い脈・馬の息）を出してよいか（レース中でまだゴールしていない・練習中）。</summary>
        private void UpdatePips(MountController lm, bool racingNow)
        {
            if (staminaPips == null) return;
            bool racing = lm != null && racingNow;
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
            if (!show) { rotInit = false; vrKnown = false; }
        }
    }
}
