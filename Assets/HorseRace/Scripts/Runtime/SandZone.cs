using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 砂地の範囲（Is Trigger の Collider と同じ GameObject に置く）。乗り物の Collider が入ると
    /// MountController.sandZoneCount を増減する。乗り物側には Rigidbody(isKinematic) が必要。
    /// Terrain の砂レイヤー比率は Task 11 が MountController.terrainSandWeight に入れる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SandZone : UdonSharpBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            MountController mc = other.GetComponentInParent<MountController>();
            if (mc != null) mc.sandZoneCount++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            MountController mc = other.GetComponentInParent<MountController>();
            if (mc != null && mc.sandZoneCount > 0) mc.sandZoneCount--;
        }
    }
}
