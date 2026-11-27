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
            // 体の Collider だけ数える（リボルバー・騎手の当たり判定の子が出入りして数がずれないように）
            if (mc != null && other.gameObject == mc.gameObject) mc.sandZoneCount++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            MountController mc = other.GetComponentInParent<MountController>();
            if (mc != null && other.gameObject == mc.gameObject && mc.sandZoneCount > 0) mc.sandZoneCount--;
        }
    }
}
