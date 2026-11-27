using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// レース中の順位（order）と最初のゴールの時刻だけを運ぶ小さな同期（system-r2-13）。
    /// RaceManager は状態・枠が変わったときだけ（まれ・大きい）同期し、順位が変わるたび（最大 4 Hz・小さい）はこちらだけを送る。
    /// マスター（RaceManager のオーナー）が持つ。RaceManager の子の GameObject に置く（SceneAssembler）。
    /// 受け取ったら RaceManager.OnOrderReceived で RaceManager.order / firstFinishServer（同期しないフィールド）へ写す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RaceOrder : UdonSharpBehaviour
    {
        public RaceManager raceManager;

        [UdonSynced] public int[] order = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        [UdonSynced] public double firstFinishServer = -1.0;
        /// <summary>この順位がどのステージ（RaceManager.stageSeq）のものか。</summary>
        [UdonSynced] public int stageSeq;

        /// <summary>マスターから。変わっていなければ送らない。</summary>
        public void Push(int[] src, double first, int seq)
        {
            if (src == null) return;
            bool changed = first != firstFinishServer || seq != stageSeq;
            for (int i = 0; i < order.Length; i++)
            {
                int v = i < src.Length ? src[i] : -1;
                if (order[i] != v) { order[i] = v; changed = true; }
            }
            firstFinishServer = first;
            stageSeq = seq;
            if (!changed) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null && !Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            RequestSerialization();
        }

        public override void OnDeserialization()
        {
            if (raceManager != null) raceManager.OnOrderReceived();
        }
    }
}
