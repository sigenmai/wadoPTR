using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// ゴールの町の表彰台（Podium.fbx）。総合結果（FINAL_RESULT）に入ったら：
    ///   - 各クライアントが自分のプレイヤーだけ見る：総合 3 位以内なら乗り物から降りて、その順位の段（steps[0..2]）へ TeleportTo
    ///   - 花火（ParticleSystem）と任意のファンファーレは全員
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
            }
        }

        private void EnterFinal(RaceManager rm)
        {
            if (fireworks != null)
                for (int i = 0; i < fireworks.Length; i++) if (fireworks[i] != null) fireworks[i].Play();
            if (fanfare != null) fanfare.Play();

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
