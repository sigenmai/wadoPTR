using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 広場の隣の練習場。Is Trigger の Collider（練習場全体を覆う）と同じ GameObject に置く。
    /// ローカルのプレイヤーが中にいる間 inPractice = true（練習用の馬に乗っている間は releaseRadius 以内なら true のまま）。
    /// 能力・リボルバー（Task 9）はこのフラグと設定値を読む：能力の切り替え自由、クールダウン ×cooldownScale、弾は無限。
    /// 練習用の馬（PracticeHorsePost）は乗り降り自由。releaseRadius より外へ出ると自動で降ろして馬を戻す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PracticeArea : UdonSharpBehaviour
    {
        /// <summary>この領域が練習場であることを示す（後続タスクが読む）。</summary>
        public bool isPractice = true;
        [Tooltip("練習場の中ではクールダウンにこれを掛ける")]
        public float cooldownScale = 1f / 3f;
        public bool infiniteAmmo = true;
        public bool freeAbilitySwitch = true;
        [Tooltip("練習用の馬に乗ったままこの距離（水平）より外へ出たら降ろす")]
        public float releaseRadius = 80f;
        public PracticeHorsePost[] posts;

        /// <summary>ローカルのプレイヤーが練習場にいる（ローカル専用、同期しない）。</summary>
        public bool inPractice;

        private bool inTrigger;
        private float nextCheck;

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            inTrigger = true;
            UpdateFlag();
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            inTrigger = false;
            UpdateFlag();
        }

        private void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            UpdateFlag();
            if (IsRidingPracticeHorse() && !WithinRadius()) ReleaseLocal();
        }

        private void UpdateFlag()
        {
            // 駅（座席）に座ると Trigger の出入りが当てにならないことがあるので、騎乗中は距離でも判定する
            inPractice = inTrigger || (IsRidingPracticeHorse() && WithinRadius());
        }

        private bool WithinRadius()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return false;
            Vector3 d = local.GetPosition() - transform.position;
            d.y = 0f;
            return d.magnitude <= releaseRadius;
        }

        public bool IsRidingPracticeHorse()
        {
            if (posts == null) return false;
            for (int i = 0; i < posts.Length; i++)
                if (posts[i] != null && posts[i].IsLocalRiding()) return true;
            return false;
        }

        /// <summary>ローカルのプレイヤーを練習用の馬から降ろす（レース開始時に RaceManager も呼ぶ）。</summary>
        public void ReleaseLocal()
        {
            if (posts == null) return;
            for (int i = 0; i < posts.Length; i++)
                if (posts[i] != null) posts[i].ReleaseIfLocal();
        }
    }
}
