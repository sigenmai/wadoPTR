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

        private int appliedState = -1;
        private int doneSeq = -1;
        private int pendingStep = -1;

        private void Update()
        {
            RaceManager rm = raceManager;
            if (rm == null) return;
            int st = rm.state;
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

        private void FillPlates(RaceManager rm)
        {
            if (namePlates == null) return;
            int[] overall = rm.GetOverallOrder();
            for (int k = 0; k < namePlates.Length; k++)
            {
                bool has = k < overall.Length && k < PODIUM_PLACES;
                if (plateRoots != null && k < plateRoots.Length && plateRoots[k] != null) plateRoots[k].SetActive(has);
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

        private void ClearPlates()
        {
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
            if (fireworks != null)
                for (int i = 0; i < fireworks.Length; i++) if (fireworks[i] != null) fireworks[i].Play();
            if (fanfare != null) fanfare.Play();
            FillPlates(rm);

            double elapsed = Networking.GetServerTimeInSeconds() - rm.stateStartServer;
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

        private void StopFireworks()
        {
            if (fireworks == null) return;
            for (int i = 0; i < fireworks.Length; i++) if (fireworks[i] != null) fireworks[i].Stop();
        }
    }
}
