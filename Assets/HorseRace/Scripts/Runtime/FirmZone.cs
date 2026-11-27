using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 固い地面の範囲（course-r2-04：深い砂の道の中の線路の路盤）。Is Trigger の Collider と同じ GameObject に置く。
    /// 乗り物の Collider が入ると MountController.firmZoneCount を増減する。1 以上の間は SandZone・Terrain の砂の中でも砂にしない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class FirmZone : UdonSharpBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            MountController mc = other.GetComponentInParent<MountController>();
            // 体の Collider だけ数える（SandZone と同じ）
            if (mc != null && other.gameObject == mc.gameObject) mc.firmZoneCount++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            MountController mc = other.GetComponentInParent<MountController>();
            if (mc != null && other.gameObject == mc.gameObject && mc.firmZoneCount > 0) mc.firmZoneCount--;
        }
    }
}
