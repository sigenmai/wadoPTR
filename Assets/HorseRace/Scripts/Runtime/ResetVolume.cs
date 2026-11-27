using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 落とし穴（岩橋の溝の底など）。Is Trigger の Collider と同じ GameObject に置く。
    /// ローカルの騎手（かつオーナー）の乗り物が入ったら、その MountController を最後に通った CP へ戻す。
    /// 地形の上に落ちた場合はコース外判定（60m）が働かないことがあるため、その保険。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ResetVolume : UdonSharpBehaviour
    {
        public void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            MountController mount = other.GetComponentInParent<MountController>();
            if (mount == null) return;
            if (!mount.IsLocalRider()) return;
            if (!Networking.IsOwner(mount.gameObject)) return;
            mount.ForceResetToLastCp();
        }
    }
}
