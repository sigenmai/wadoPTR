using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// チェックポイント。Is Trigger の Collider と同じ GameObject に置く。
    /// ローカルの騎手（かつオーナー）の乗り物が入ったときだけ MountController.ReachCheckpoint を呼ぶ。
    /// 進行の判定（順番通りか）は MountController 側。
    /// 表示（ローカル）：次の CP を通ったら首の札（RaceManager.hud）でピッチの上がる音と札の跳ね、
    /// 順番を飛ばして先の CP に入ったら「◀ 関門を通り過ぎた」を 2 秒。
    /// 近道の入口の双子の CP（isShortcut、ui-r2-17）：自分なら首の札に青緑の「近道！」、誰が入っても（各自の画面で）札の横に青緑の火花。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Checkpoint : UdonSharpBehaviour
    {
        /// <summary>ステージ内の通し番号。スタートライン通過 = 0、最初の CP = 1、以降 +1。</summary>
        public int index;
        public int stageIndex;
        public bool isGoal;
        /// <summary>次の CP。ゴールは null。</summary>
        public Checkpoint next;
        [Tooltip("首の札の合図用（ビルドで自動設定）")]
        public RaceManager raceManager;
        [Tooltip("練習場の往復タイムの門（stageIndex = -1）のとき：計測する PracticeLap")]
        public PracticeLap practiceLap;
        [Tooltip("近道の入口の CP（ui-r2-17）")]
        public bool isShortcut;
        [Tooltip("近道の札の横の青緑の火花（誰が入っても各自の画面で出す）")]
        public ParticleSystem shortcutBurst;
        private float lastBurst = -10f;

        public void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            MountController mount = other.GetComponentInParent<MountController>();
            if (mount == null) return;
            if (other.gameObject != mount.gameObject) return; // 体の Collider だけ（リボルバー・騎手の当たり判定の子は数えない）
            if (isShortcut && shortcutBurst != null && mount.raceActive && Time.time - lastBurst > 0.3f)
            {
                lastBurst = Time.time;
                shortcutBurst.Emit(40);   // 中継・観客にも近道が見える
            }
            if (!mount.IsLocalRider()) return;
            if (!Networking.IsOwner(mount.gameObject)) return;
            if (stageIndex < 0)
            {
                // 練習場の門（system-r1-18）：レースの進行には数えない
                if (practiceLap != null) practiceLap.GatePassed(index);
                return;
            }
            int before = mount.syncCp;
            mount.ReachCheckpoint(this);
            Notify(mount, before);
        }

        private void Notify(MountController mount, int before)
        {
            if (raceManager == null || raceManager.hud == null) return;
            if (stageIndex != mount.currentStage || !mount.raceActive || mount.InputLocked) return;
            if (mount.syncCp == index && before == index - 1)
            {
                raceManager.hud.CheckpointPassed(index, raceManager.CheckpointCount(stageIndex));
                if (isShortcut) raceManager.hud.ShowShortcut();
            }
            else if (index > before + 1)
                raceManager.hud.MissedCheckpoint();
        }
    }
}
