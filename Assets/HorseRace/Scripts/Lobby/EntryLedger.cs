using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 受付カウンターの台帳。Interact ＝ サイン ＝ 手配書・柵の馬で選んだ内容でマスターへエントリーを依頼する。
    /// ページ（pageText、手書き風フォントの TextMeshPro）にエントリー者の名前・乗り物・能力を書く。
    /// ページの更新は RaceManager が同期を受け取るたびに Refresh() で行う。
    /// ステージ中のサインは「次のステージから」の欄に載り、ステージ結果に入ると枠へ移る（RaceManager.pendingPlayerId）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class EntryLedger : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("台帳のページ（TextMeshPro（3D）、手書き風フォント）")]
        public TextMeshPro pageText;
        [Tooltip("ペンの音（任意）")]
        public AudioSource signSound;

        private void Start()
        {
            Refresh();
        }

        public override void Interact()
        {
            if (raceManager == null) return;
            if (signSound != null) signSound.Play();
            raceManager.SignLedger();
        }

        public void Refresh()
        {
            if (pageText == null || raceManager == null) return;
            RaceManager rm = raceManager;
            int st = rm.state;
            string s = "エントリー台帳\n\n";
            int count = 0;
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
            {
                int pid = rm.slotPlayerId[i];
                if (pid < 0) continue;
                count++;
                s += count.ToString() + ". " + RaceManager.PlayerName(pid) + "\n    "
                    + RaceManager.MountLabel(rm.slotMount[i], rm.slotCoat[i]) + "・" + RaceManager.AbilityLabel(rm.slotAbility[i]);
                if (st != RaceMath.STATE_LOBBY) s += "  " + rm.slotTotalPoints[i].ToString() + "点";
                s += "\n";
            }
            if (count == 0) s += "（まだ誰もサインしていない）\n";
            string pend = "";
            for (int q = 0; q < RaceManager.MAX_SLOTS; q++)
            {
                int pid = rm.pendingPlayerId[q];
                if (pid < 0) continue;
                pend += "・" + RaceManager.PlayerName(pid) + "\n";
            }
            if (pend.Length > 0) s += "\n次のステージから：\n" + pend;
            s += "\n";
            if (count >= rm.SlotCount() && rm.SlotCount() > 0) s += "定員いっぱい\n";
            if (st == RaceMath.STATE_LOBBY) s += "手配書と柵の馬で選んでサイン。鐘が鳴れば出走。";
            else if (st == RaceMath.STATE_STAGE_RESULT && rm.stage + 1 < RaceManager.STAGE_COUNT) s += "ステージの合間：今サインすれば次のステージから走れる。";
            else if (st == RaceMath.STATE_STAGE_RESULT) s += "最終ステージの結果発表中。次のレースはロビーで。";
            else if (st == RaceMath.STATE_FINAL_RESULT) s += "総合結果の発表中。";
            else if (rm.stage + 1 < RaceManager.STAGE_COUNT) s += RaceManager.StageLabel(rm.stage) + " 出走中。今サインすれば次のステージから走れる。";
            else s += RaceManager.StageLabel(rm.stage) + "（最終）出走中。次のレースはロビーで。";
            pageText.text = s;
        }
    }
}
