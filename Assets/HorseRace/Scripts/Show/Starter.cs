using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// スタートラインのスターター（人物はプリミティブでよい）。RaceManager.starter に設定する。
    ///
    /// RaceManager が COUNTDOWN の適用時に全クライアントで "BeginCountdown" を送ってくる（stageSeq ごとに 1 回）。
    ///   0 s / 1 s / 2 s：旗を振りながら空砲 3 発（空に「3」「2」「1」）→ 3 s：号砲（砂煙、空に「出走！」）
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
        [Tooltip("空の大きな数字（任意、TextMeshPro）")]
        public TextMeshPro skyCountText;

        private bool running;
        private float beginTime;
        private int step;          // 0..BLANK_SHOTS = 次に撃つ空砲、BLANK_SHOTS+1 = 号砲済み
        private float gunTime;
        private bool restCaptured;
        private Quaternion flagRest;
        private Quaternion armRest;

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
            running = true;
            beginTime = Time.time;
            step = 0;
            gunTime = -1f;
        }

        private void Update()
        {
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
                SetSky("");
                return;
            }
            float t = Time.time - beginTime;
            bool raceStarted = st == RaceMath.STATE_RACING;

            if (step <= BLANK_SHOTS)
            {
                if (raceStarted) step = BLANK_SHOTS; // 遅れて届いた：残りの空砲は飛ばす
                while (step < BLANK_SHOTS && t >= step * SHOT_INTERVAL)
                {
                    FireBlank(step);
                    step++;
                }
                if (step == BLANK_SHOTS && (raceStarted || t >= BLANK_SHOTS * SHOT_INTERVAL))
                {
                    FireGun();
                    step = BLANK_SHOTS + 1;
                }
            }

            Pose(t);

            if (gunTime >= 0f && Time.time - gunTime >= AFTER_GUN_SECONDS)
            {
                running = false;
                SetSky("");
            }
        }

        private void FireBlank(int i)
        {
            PlayClip(blankClip);
            if (pistolSmoke != null) pistolSmoke.Play();
            SetSky((BLANK_SHOTS - i).ToString());
        }

        private void FireGun()
        {
            gunTime = Time.time;
            PlayClip(startClip != null ? startClip : blankClip);
            if (pistolSmoke != null) pistolSmoke.Play();
            if (startDust != null)
                for (int i = 0; i < startDust.Length; i++) if (startDust[i] != null) startDust[i].Play();
            SetSky("出走！");
            // マスターのインスタンスだけ。RaceManager 側でもオーナー確認する
            if (raceManager != null && raceManager.IsLocalMaster()) raceManager.StarterFinished();
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
            if (skyCountText != null) skyCountText.text = s;
        }
    }
}
