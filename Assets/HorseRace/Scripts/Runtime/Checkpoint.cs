using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// チェックポイント。Is Trigger の Collider と同じ GameObject に置く。
    /// ローカルの騎手（かつオーナー）の乗り物が入ったときだけ MountController.ReachCheckpoint を呼ぶ。
    /// 進行の判定（順番通りか）は MountController 側。
    /// </summary>
    public class Checkpoint : UdonSharpBehaviour
    {
        /// <summary>ステージ内の通し番号。スタートライン通過 = 0、最初の CP = 1、以降 +1。</summary>
        public int index;
        public int stageIndex;
        public bool isGoal;
        /// <summary>次の CP。ゴールは null。</summary>
        public Checkpoint next;

        public void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            MountController mount = other.GetComponentInParent<MountController>();
            if (mount == null) return;
            if (!mount.IsLocalRider()) return;
            if (!Networking.IsOwner(mount.gameObject)) return;
            mount.ReachCheckpoint(this);
        }
    }
}
