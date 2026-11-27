using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// ゴールの町の表彰台（Podium.fbx）。総合結果（FINAL_RESULT）に入ったら：
    ///   - 各クライアントが自分のプレイヤーだけ見る：総合 3 位以内なら乗り物から降りて、その順位の段（steps[0..2]）へ TeleportTo
    ///   - 花火（ParticleSystem、3 ステージの色：赤錆・砂金・紫）と任意のファンファーレは全員
    ///   - 段の上の名札（手配書の紙、看板の文字）に「総合1位\n名前  24点」（1 位は金）。総合結果の間だけ書き、終わったら消す
    /// 発表は順に（ui-r3-06）：t = 0 で上位 3 人を段へ（名札は伏せたまま）・太鼓の連打（時計の音 TICK を 0.12 秒ごとに 2 秒）、
    ///   t = 2.0 で 3 位、t = 3.5 で 2 位（名札が 1.4 → 1 に跳ね、ペンの音）、t = 5.5 で 1 位（1.8 → 1、金の紙吹雪 300、教会の鐘）、そこで初めて花火とファンファーレ。
    ///   4〜8 位は後ろの「総合結果」の掲示板に小さく。途中から来た人（総合結果に入って 6 秒より後）には全部をすぐ出す。
    /// 総合結果以外の状態になったら花火を止めるだけ（プレイヤーは RaceManager がロビーへ戻す／歩いて降りる）。
    /// RaceManager の同期状態を毎フレーム比べて、stageSeq ごとに 1 回だけ（ローカル）。同期しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Podium : UdonSharpBehaviour
    {
        public const int PODIUM_PLACES = 3;
        /// <summary>総合結果に入ってからこの秒数を過ぎていたらテレポートしない（同期の遅れ・途中参加）。</summary>
        public const float TELEPORT_FRESH_SECONDS = 10f;

        public RaceManager raceManager;
        [Tooltip("段の立ち位置（0 = 1 位、1 = 2 位、2 = 3 位）。向き = 観客側")]
        public Transform[] steps;
        public ParticleSystem[] fireworks;
        [Tooltip("任意")]
        public AudioSource fanfare;
        [Tooltip("段の上の名札（0 = 1 位、1 = 2 位、2 = 3 位）")]
        public TextMeshPro[] namePlates;
        [Tooltip("名札の紙（名前が無い段は隠す。namePlates と同じ並び）")]
        public GameObject[] plateRoots;

        [Header("発表の順（ui-r3-06）")]
        [Tooltip("太鼓の連打（fanfare から 0.12 秒ごとに。SfxMath.TICK）")]
        public AudioClip drumClip;
        [Tooltip("名札をめくる音（SfxMath.PEN）")]
        public AudioClip revealClip;
        [Tooltip("1 位の鐘（SfxMath.BELL）")]
        public AudioClip bellClip;
        [Tooltip("最後のファンファーレ（SfxMath.STING）")]
        public AudioClip stingClip;
        [Tooltip("1 位の段の上の金の紙吹雪（Emit 300）")]
        public ParticleSystem goldBurst;
        [Tooltip("後ろの「総合結果」の掲示板の文字（4〜8 位を小さく）")]
        public TextMeshPro resultsBoard;
        [Tooltip("「総合結果」の掲示板の見出し（4〜8 位の上）")]
        public string resultsHeading = HudMath.RESULTS_HEADING;

        public const float DRUM_SECONDS = DirectorMath.PODIUM_DRUM_SECONDS;
        public const float DRUM_STEP = DirectorMath.PODIUM_DRUM_STEP;
        public const float REVEAL_3RD = DirectorMath.PODIUM_REVEAL_3RD;
        public const float REVEAL_2ND = DirectorMath.PODIUM_REVEAL_2ND;
        public const float REVEAL_1ST = DirectorMath.PODIUM_REVEAL_1ST;
        /// <summary>この秒数より後に総合結果を受け取ったら（途中参加）順を追わずに全部出す。</summary>
        public const float SEQUENCE_FRESH_SECONDS = 6f;

        private int appliedState = -1;
        private int doneSeq = -1;
        private int pendingStep = -1;
        private float drumUntil;
        private float[] punchAt = new float[] { -10f, -10f, -10f };
        private float[] punchFrom = new float[] { 1f, 1f, 1f };
        private Vector3[] plateBase;
        private bool plateBaseCaptured;

        private void Update()
        {
            RaceManager rm = raceManager;
            if (rm == null) return;
            int st = rm.state;
            AnimatePlates();
            if (st == appliedState) return;
            appliedState = st;
            if (st == RaceMath.STATE_FINAL_RESULT && doneSeq != rm.stageSeq)
            {
                doneSeq = rm.stageSeq;
                EnterFinal(rm);
            }
            else if (st != RaceMath.STATE_FINAL_RESULT)
            {
                StopFireworks();
                ClearPlates();
            }
        }

        /// <summary>名札の文字を書く（reveal = false なら紙は伏せたまま＝隠す）。</summary>
        private void FillPlates(RaceManager rm, bool reveal)
        {
            if (namePlates == null) return;
            int[] overall = rm.GetOverallOrder();
            for (int k = 0; k < namePlates.Length; k++)
            {
                bool has = k < overall.Length && k < PODIUM_PLACES;
                if (plateRoots != null && k < plateRoots.Length && plateRoots[k] != null) plateRoots[k].SetActive(has && reveal);
                if (namePlates[k] == null) continue;
                if (!has) { namePlates[k].text = ""; continue; }
                int slot = overall[k];
                // 紙の名札は濃いインク、1 位は赤インクの太字（ui-r2-09：明るい字を明るい紙に書かない）
                string name = RaceManager.ShortName(rm.slotPlayerId[slot]);
                if (k == 0) name = "<b>" + name + "</b>";
                namePlates[k].text = "<size=70%>総合" + (k + 1).ToString() + "位</size>\n" + name
                    + "  " + rm.slotTotalPoints[slot].ToString() + "点";
                namePlates[k].color = k == 0 ? new Color(0.72f, 0.12f, 0.08f) : new Color(0.23f, 0.15f, 0.08f);
            }
        }

        /// <summary>4〜8 位（ui-r3-06）：「総合結果」の掲示板の見出しの下に小さく。</summary>
        private void FillBoard(RaceManager rm)
        {
            if (resultsBoard == null) return;
            int[] overall = rm.GetOverallOrder();
            string s = resultsHeading;
            if (overall.Length > PODIUM_PLACES)
            {
                s += "\n<size=55%><color=#F5E6C2>";   // 暗い板の上は明るいクリームの字（紙ではないので焦げ茶は沈む）
                for (int k = PODIUM_PLACES; k < overall.Length; k++)
                {
                    int slot = overall[k];
                    s += (k > PODIUM_PLACES ? "\n" : "") + HudMath.Row4((k + 1).ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot]), "", "計" + rm.slotTotalPoints[slot].ToString() + "点");
                }
                s += "</color></size>";
            }
            resultsBoard.text = s;
        }

        private void ClearPlates()
        {
            if (resultsBoard != null) resultsBoard.text = resultsHeading;
            if (namePlates != null)
                for (int k = 0; k < namePlates.Length; k++) if (namePlates[k] != null) namePlates[k].text = "";
            if (plateRoots != null)
                for (int k = 0; k < plateRoots.Length; k++) if (plateRoots[k] != null) plateRoots[k].SetActive(false);
        }

        private void Start()
        {
            ClearPlates();
        }

        private void EnterFinal(RaceManager rm)
        {
            double elapsed = Networking.GetServerTimeInSeconds() - rm.stateStartServer;
            FillBoard(rm);
            if (elapsed > SEQUENCE_FRESH_SECONDS)
            {
                // 途中から：順は追わずに全部出す
                FillPlates(rm, true);
                StartFireworks();
            }
            else
            {
                // 伏せた名札の下に上位 3 人が立つ → 太鼓 → 3 位 → 2 位 → 間をおいて 1 位
                FillPlates(rm, false);
                float t0 = (float)elapsed;
                drumUntil = Time.time + Mathf.Max(0f, DRUM_SECONDS - t0);
                if (drumUntil > Time.time) _DrumTick();
                SendCustomEventDelayedSeconds(nameof(_Reveal3), Mathf.Max(0.05f, REVEAL_3RD - t0));
                SendCustomEventDelayedSeconds(nameof(_Reveal2), Mathf.Max(0.1f, REVEAL_2ND - t0));
                SendCustomEventDelayedSeconds(nameof(_Reveal1), Mathf.Max(0.15f, REVEAL_1ST - t0));
            }

            if (elapsed > TELEPORT_FRESH_SECONDS) return;
            int s = rm.LocalSlot();
            if (s < 0) return;
            int[] overall = rm.GetOverallOrder();
            int place = -1;
            for (int k = 0; k < overall.Length && k < PODIUM_PLACES; k++) if (overall[k] == s) { place = k; break; }
            if (place < 0 || steps == null || place >= steps.Length || steps[place] == null) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            MountController lm = rm.LocalMount();
            if (lm != null && lm.station != null && lm.IsLocalRider()) lm.station.ExitStation(local);
            pendingStep = place;
            // 降車と同じフレームの TeleportTo は効かないことがあるので少し後
            SendCustomEventDelayedFrames(nameof(_TeleportToStep), 3);
        }

        public void _TeleportToStep()
        {
            int k = pendingStep;
            pendingStep = -1;
            if (raceManager == null || raceManager.state != RaceMath.STATE_FINAL_RESULT) return;
            if (steps == null || k < 0 || k >= steps.Length || steps[k] == null) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            local.TeleportTo(steps[k].position, steps[k].rotation);
        }

        private bool SequenceValid()
        {
            return raceManager != null && raceManager.state == RaceMath.STATE_FINAL_RESULT && raceManager.stageSeq == doneSeq;
        }

        public void _DrumTick()
        {
            if (!SequenceValid() || Time.time >= drumUntil) return;
            if (fanfare != null && drumClip != null) fanfare.PlayOneShot(drumClip, 0.8f);
            SendCustomEventDelayedSeconds(nameof(_DrumTick), DRUM_STEP);
        }

        public void _Reveal3() { Reveal(2, 1.4f); }
        public void _Reveal2() { Reveal(1, 1.4f); }

        public void _Reveal1()
        {
            if (!Reveal(0, 1.8f)) return;
            if (goldBurst != null) goldBurst.Emit(300);
            if (fanfare != null && bellClip != null) fanfare.PlayOneShot(bellClip);
            StartFireworks();
        }

        /// <summary>名札 k（0 = 1 位）をめくる：紙を出して from → 1 に跳ね、ペンの音。その段に人がいなければ何もしない（false）。</summary>
        private bool Reveal(int k, float from)
        {
            if (!SequenceValid()) return false;
            int[] overall = raceManager.GetOverallOrder();
            if (k >= overall.Length) return false;
            if (plateRoots != null && k < plateRoots.Length && plateRoots[k] != null)
            {
                CapturePlateBase();
                plateRoots[k].SetActive(true);
                punchAt[k] = Time.time;
                punchFrom[k] = from;
            }
            if (fanfare != null && revealClip != null) fanfare.PlayOneShot(revealClip);
            return true;
        }

        private void CapturePlateBase()
        {
            if (plateBaseCaptured || plateRoots == null) return;
            plateBaseCaptured = true;
            plateBase = new Vector3[plateRoots.Length];
            for (int i = 0; i < plateRoots.Length; i++) plateBase[i] = plateRoots[i] != null ? plateRoots[i].transform.localScale : Vector3.one;
        }

        private void AnimatePlates()
        {
            if (!plateBaseCaptured || plateRoots == null) return;
            for (int k = 0; k < plateRoots.Length && k < punchAt.Length; k++)
            {
                if (plateRoots[k] == null) continue;
                float t = Time.time - punchAt[k];
                if (t < 0f || t > 1.2f) continue;
                plateRoots[k].transform.localScale = plateBase[k] * HudMath.PunchScale(t, punchFrom[k]);
            }
        }

        private void StartFireworks()
        {
            if (fireworks != null)
                for (int i = 0; i < fireworks.Length; i++) if (fireworks[i] != null) fireworks[i].Play();
            if (fanfare != null)
            {
                if (stingClip != null) fanfare.PlayOneShot(stingClip);
                else fanfare.Play();
            }
        }

        private void StopFireworks()
        {
            if (fireworks == null) return;
            for (int i = 0; i < fireworks.Length; i++) if (fireworks[i] != null) fireworks[i].Stop();
        }
    }
}
