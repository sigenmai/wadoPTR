using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 練習用の馬をつなぐ杭（レース用 8 頭とは別の MountController）。Interact で乗る。降りるのは Jump（練習中は降車禁止にしない）か、杭にもう一度 Interact。
    /// 誰が乗っているかを riderId で同期し、使用中の馬には乗れない。降りると馬は home に戻る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PracticeHorsePost : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        public MountController mount;
        [Tooltip("降りたときに馬を戻す場所")]
        public Transform home;
        public int coat;

        [UdonSynced] public int riderId = -1;
        private int appliedRiderId = -1;
        private float claimTime;
        private bool seenSeated;

        /// <summary>乗り手は Jump で自由に降りられる（raceActive=false）。降りたのを見つけたら杭の記録も外して馬を戻す。</summary>
        private void Update()
        {
            if (!IsLocalRiding() || mount == null) return;
            if (mount.IsLocalRider())
            {
                seenSeated = true;
                return;
            }
            // 着席は 1〜数フレーム遅れるので、座った後に降りたか、3 秒たっても座れなかったら解放
            if (seenSeated || Time.time - claimTime > 3f) Release();
        }

        public override void Interact()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || mount == null) return;
            if (riderId == local.playerId)
            {
                Release();
                return;
            }
            if (riderId >= 0)
            {
                VRCPlayerApi other = VRCPlayerApi.GetPlayerById(riderId);
                if (other != null && other.IsValid()) return; // 使用中
            }
            if (raceManager != null && raceManager.IsLocalRacing()) return;
            Networking.SetOwner(local, gameObject);
            riderId = local.playerId;
            claimTime = Time.time;
            seenSeated = false;
            RequestSerialization();
            Apply();
        }

        public bool IsLocalRiding()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return local != null && riderId >= 0 && riderId == local.playerId;
        }

        public void ReleaseIfLocal()
        {
            if (IsLocalRiding()) Release();
        }

        private void Release()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            Networking.SetOwner(local, gameObject);
            riderId = -1;
            RequestSerialization();
            if (mount != null && Networking.IsOwner(mount.gameObject) && home != null)
                mount.TeleportTo(home.position, home.eulerAngles.y);
            Apply();
        }

        public override void OnDeserialization()
        {
            Apply();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null || player.playerId != riderId) return;
            if (!Networking.IsOwner(gameObject)) return;
            riderId = -1;
            RequestSerialization();
            if (mount != null && home != null)
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (local != null) Networking.SetOwner(local, mount.gameObject);
                mount.TeleportTo(home.position, home.eulerAngles.y);
            }
            Apply();
        }

        private void Apply()
        {
            if (mount == null || appliedRiderId == riderId) return;
            appliedRiderId = riderId;
            mount.SetRaceActive(false);
            mount.InputLocked = false;
            mount.AssignRider(riderId, MountMath.MOUNT_HORSE, coat);
        }
    }
}
