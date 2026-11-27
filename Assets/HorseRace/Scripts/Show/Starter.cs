using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// スタートラインのスターター（人物はプリミティブでよい）。RaceManager.starter に設定する。
    ///
    /// RaceManager が COUNTDOWN の適用時に全クライアントで "BeginCountdown" を送ってくる（stageSeq ごとに 1 回）。
    ///   0〜1.2 s：空にステージ名（「第2幕 砂漠」、ステージの色）
    ///   1.2 s / 2.2 s / 3.2 s：旗を振りながら空砲 3 発（空に「3」「2」（紙の白）「1」（琥珀））→ 4.2 s：号砲（砂煙、空に金の「出走！」）
    ///   空の文字はステージのスタートの 30m 先・12m 上の中心線上（skySpots）。数字ごとに 1.6 → 1 に跳ね、首の札（MinimalHud）にも同じ文字。
    /// 号砲の時点でマスターのインスタンスだけ RaceManager.StarterFinished() を呼ぶ → マスターが RACING にして InputLocked = false が全員に届く。
    /// マスター以外は同じ演出をローカルで鳴らすだけ。号砲の前に RACING が届いたら（遅れたクライアント）残りを飛ばしてすぐ号砲。
    /// カウント中の馬の足踏み（Stamp）は MountController（InputLocked かつ停止中 → GAIT_FLAG_STAMP）と MountVisual が既に出す。
    /// 同期しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Starter : UdonSharpBehaviour
    {
        public const int BLANK_SHOTS = 3;
        public const float SHOT_INTERVAL = 1f;
        public const float AFTER_GUN_SECONDS = 1.5f;
        /// <summary>最初のこの秒数はステージ名（「第2幕 砂漠」、ステージの色）を空に出し、そのあと 3・2・1・出走！</summary>
        public const float TITLE_SECONDS = 1.2f;

        public RaceManager raceManager;
        [Tooltip("任意。ステージごとのスターターの立ち位置（index = stage）。BeginCountdown でこの GameObject ごと移す（旗・空の数字・砂煙は子に置く）")]
        public Transform[] stageSpots;

        [Header("人物（プリミティブ可）")]
        [Tooltip("旗の付け根（ここを回して振る）")]
        public Transform flagPivot;
        public Vector3 flagWaveAxis = new Vector3(0f, 0f, 1f);
        public float flagWaveAngle = 35f;
        public float flagWaveSpeed = 7f;
        [Tooltip("号砲で振り下ろした旗の角度（flagWaveAxis 回り）")]
        public float flagDropAngle = -80f;
        [Tooltip("ピストルを持つ腕の付け根（カウント中は空に向けて上げる）。Revolver モデルを持たせてよい")]
        public Transform pistolArm;
        public Vector3 pistolRaisedEuler = new Vector3(-150f, 0f, 0f);

        [Header("音・効果")]
        public AudioSource audioSource;
        [Tooltip("空砲（3 発）")]
        public AudioClip blankClip;
        [Tooltip("号砲（未設定なら空砲の音）")]
        public AudioClip startClip;
        [Tooltip("銃口の煙（任意）")]
        public ParticleSystem pistolSmoke;
        [Tooltip("号砲で上がる砂煙（スタートラインに並べる、任意）")]
        public ParticleSystem[] startDust;
        [Tooltip("空の大きな数字（任意、TextMeshPro）。騎手のほうを向く")]
        public TextMeshPro skyCountText;
        [Tooltip("任意。裏側（観客側）の同じ文字")]
        public TextMeshPro skyCountBack;
        [Tooltip("ステージごとの空の数字の位置（コースの中心線上、スタートラインの 30m 先・12m 上、騎手のほうを向く。index = stage）")]
        public Transform[] skySpots;
        [Tooltip("ステージごとのスタートの門（柵、Block）。カウントダウンの間だけ出し、号砲で消す（course-r1-15）")]
        public GameObject[] startGates;

        private bool running;
        private float beginTime;
        private int step;          // 0..BLANK_SHOTS = 次に撃つ空砲、BLANK_SHOTS+1 = 号砲済み
        private float gunTime;
        private bool restCaptured;
        private Quaternion flagRest;
        private Quaternion armRest;
        private float skyPunchAt = -10f;
        private bool titleShown;
        private Vector3 skyScale = Vector3.one;
        private int graceSeenSeq = -1;
        private int weaponsFreeSeq = -1;
        private bool skyScaleCaptured;

        private void Start()
        {
            CaptureRest();
            SetSky("");
        }

        private void CaptureRest()
        {
            if (restCaptured) return;
            restCaptured = true;
            if (flagPivot != null) flagRest = flagPivot.localRotation;
            if (pistolArm != null) armRest = pistolArm.localRotation;
        }

        /// <summary>RaceManager から（全クライアント・ローカル）。</summary>
        public void BeginCountdown()
        {
            CaptureRest();
            if (raceManager != null && stageSpots != null)
            {
                int st = raceManager.stage;
                if (st >= 0 && st < stageSpots.Length && stageSpots[st] != null)
                    transform.SetPositionAndRotation(stageSpots[st].position, stageSpots[st].rotation);
            }
            // 空の数字はスターターの子ではなく、ステージのスタートの前方の中心線上へ
            if (raceManager != null && skySpots != null)
            {
                int st = raceManager.stage;
                if (st >= 0 && st < skySpots.Length && skySpots[st] != null)
                {
                    if (skyCountText != null) skyCountText.transform.SetPositionAndRotation(skySpots[st].position, skySpots[st].rotation);
                    if (skyCountBack != null) skyCountBack.transform.SetPositionAndRotation(skySpots[st].position, skySpots[st].rotation * Quaternion.Euler(0f, 180f, 0f));
                }
            }
            SetGates(raceManager != null ? raceManager.stage : -1);
            titleShown = false;
            running = true;
            beginTime = Time.time;
            step = 0;
            gunTime = -1f;
        }

        private void Update()
        {
            UpdateWeaponsFree();
            if (!running)
            {
                RelaxPose();
                return;
            }
            RaceManager rm = raceManager;
            int st = rm != null ? rm.state : RaceMath.STATE_COUNTDOWN;
            if (st != RaceMath.STATE_COUNTDOWN && st != RaceMath.STATE_RACING)
            {
                // レースが打ち切られた（ロビーに戻った等）
                running = false;
                SetGates(-1);
                SetSky("");
                return;
            }
            float t = Time.time - beginTime;
            bool raceStarted = st == RaceMath.STATE_RACING;

            if (step <= BLANK_SHOTS)
            {
                if (raceStarted) step = BLANK_SHOTS; // 遅れて届いた：残りの空砲は飛ばす
                if (!titleShown && !raceStarted)
                {
                    titleShown = true;
                    int stg = rm != null ? rm.stage : 0;
                    SetSky(RaceManager.StageLabel(stg), RaceManager.StageColor(stg), 1.25f);
                }
                while (step < BLANK_SHOTS && t >= TITLE_SECONDS + step * SHOT_INTERVAL)
                {
                    FireBlank(step);
                    step++;
                }
                if (step == BLANK_SHOTS && (raceStarted || t >= TITLE_SECONDS + BLANK_SHOTS * SHOT_INTERVAL))
                {
                    FireGun();
                    step = BLANK_SHOTS + 1;
                }
            }

            Pose(t);
            PunchSky();

            if (gunTime >= 0f && Time.time - gunTime >= AFTER_GUN_SECONDS)
            {
                running = false;
                SetSky("");
            }
        }

        /// <summary>
        /// 開幕の猶予（RaceMath.START_GRACE_SECONDS）が終わったら、もう 1 発の空砲（2 発目の音）と「順に解禁」で知らせる（system-r1-07 / r2-11）。
        /// 猶予を見ていたステージ（途中から来た人の取りこぼしで鳴らさない）だけ、1 ステージ 1 回。
        /// </summary>
        private void UpdateWeaponsFree()
        {
            RaceManager rm = raceManager;
            if (rm == null || rm.state != RaceMath.STATE_RACING) return;
            if (rm.InStartGrace()) { graceSeenSeq = rm.stageSeq; return; }
            if (graceSeenSeq != rm.stageSeq || weaponsFreeSeq == rm.stageSeq) return;
            weaponsFreeSeq = rm.stageSeq;
            PlayClip(blankClip);
            if (pistolSmoke != null) pistolSmoke.Play();
            // 能力はレーンの順に 13 秒まで、リボルバーは 15 秒まで 1 発（system-r2-11）
            if (rm.hud != null && rm.IsLocalEntered()) rm.hud.ShowBanner("順に解禁", new Color(1f, 0.78f, 0.27f), 1.4f, 1.4f);
        }

        private void FireBlank(int i)
        {
            PlayClip(blankClip);
            if (pistolSmoke != null) pistolSmoke.Play();
            int n = BLANK_SHOTS - i;
            SetSky(n.ToString(), n == 1 ? new Color(1f, 0.6f, 0.2f) : new Color(0.96f, 0.90f, 0.76f), 1.6f);
        }

        /// <summary>スタートの門：stage の門だけ出す（-1 = すべて消す）。各クライアントがローカルで同じ時に切り替える。</summary>
        private void SetGates(int stage)
        {
            if (startGates == null) return;
            for (int i = 0; i < startGates.Length; i++)
                if (startGates[i] != null) startGates[i].SetActive(i == stage);
        }

        private void FireGun()
        {
            gunTime = Time.time;
            SetGates(-1);
            PlayClip(startClip != null ? startClip : blankClip);
            if (pistolSmoke != null) pistolSmoke.Play();
            if (startDust != null)
                for (int i = 0; i < startDust.Length; i++) if (startDust[i] != null) startDust[i].Play();
            SetSky("出走！", new Color(1f, 0.78f, 0.27f), 1.8f);
            // マスターのインスタンスだけ。RaceManager 側でもオーナー確認する
            if (raceManager != null && raceManager.IsLocalMaster()) raceManager.StarterFinished();
        }

        /// <summary>時間切れ（ui-r3-03）：RaceManager から、まだゴールしていなかった本人のクライアントで空砲を 1 発。</summary>
        public void PlayTimeUp()
        {
            PlayClip(blankClip);
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource == null) return;
            if (clip != null) audioSource.PlayOneShot(clip);
            else audioSource.Play();
        }

        private void Pose(float t)
        {
            if (flagPivot != null)
            {
                float a = gunTime >= 0f ? flagDropAngle : Mathf.Sin(t * flagWaveSpeed) * flagWaveAngle;
                flagPivot.localRotation = flagRest * Quaternion.AngleAxis(a, flagWaveAxis);
            }
            if (pistolArm != null)
            {
                Quaternion goal = armRest * Quaternion.Euler(pistolRaisedEuler);
                pistolArm.localRotation = Quaternion.Slerp(pistolArm.localRotation, goal, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
        }

        private void RelaxPose()
        {
            if (!restCaptured) return;
            float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
            if (flagPivot != null) flagPivot.localRotation = Quaternion.Slerp(flagPivot.localRotation, flagRest, k);
            if (pistolArm != null) pistolArm.localRotation = Quaternion.Slerp(pistolArm.localRotation, armRest, k);
        }

        private void SetSky(string s)
        {
            SetSky(s, new Color(0.96f, 0.90f, 0.76f), 1f);
        }

        /// <summary>空の文字（表と裏）を書き、from → 1 で跳ねさせる。首の札にも同じ文字を出す（カウントダウン中・号砲）。</summary>
        private void SetSky(string s, Color c, float from)
        {
            if (skyCountText != null)
            {
                if (!skyScaleCaptured) { skyScale = skyCountText.transform.localScale; skyScaleCaptured = true; }
                skyCountText.text = s;
                skyCountText.color = c;
            }
            if (skyCountBack != null) { skyCountBack.text = s; skyCountBack.color = c; }
            if (s.Length == 0) return;
            skyPunchAt = Time.time;
            skyPunchFrom = from;
            if (raceManager != null && raceManager.hud != null && raceManager.IsLocalEntered())
            {
                bool go = s == "出走！";
                raceManager.hud.ShowBanner(s, c, go ? 1.2f : 1.05f, from);
                // VR は数字ごとに軽く、号砲で強く手に響かせる（ui-r3-05）
                if (go) raceManager.hud.Buzz(HudMath.HAPTIC_GO_S, HudMath.HAPTIC_GO_AMP, HudMath.HAPTIC_GO_HZ);
                else raceManager.hud.Buzz(HudMath.HAPTIC_COUNTDOWN_S, HudMath.HAPTIC_COUNTDOWN_AMP, HudMath.HAPTIC_COUNTDOWN_HZ);
            }
        }

        private float skyPunchFrom = 1f;

        private void PunchSky()
        {
            if (!skyScaleCaptured) return;
            float k = HudMath.PunchScale(Time.time - skyPunchAt, skyPunchFrom);
            if (skyCountText != null) skyCountText.transform.localScale = skyScale * k;
            if (skyCountBack != null) skyCountBack.transform.localScale = skyScale * k;
        }
    }
}
