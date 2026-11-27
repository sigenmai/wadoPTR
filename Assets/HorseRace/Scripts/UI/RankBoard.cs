using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 沿道・チェックポイントの黒板。チョーク風の文字で順位を書く（RaceManager が同期を受けるたびに Refresh）。
    /// レース中 = 現在ステージの順位、ステージ結果 = 着順と獲得点、総合結果 = 合計点の順。
    /// showLastRace（たまり場の黒板）ならロビー中は前回のレースの表。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RankBoard : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("黒板の文字（TextMeshPro、チョーク風フォント）")]
        public TextMeshPro boardText;
        [Tooltip("表示する人数の上限")]
        public int maxLines = 8;
        [Tooltip("たまり場の黒板用：ロビー中は前回のレースの順位表（ステージ別の着順と総合ポイント）を書く")]
        public bool showLastRace;
        [Tooltip("コースの黒板：このステージ番号（0..2）。カウントダウン〜ステージ結果の間は、別のステージの黒板は書き直さない（負荷対策）。-1 = 常に書く")]
        public int stageIndex = -1;

        private void Start()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (boardText == null || raceManager == null) return;
            RaceManager rm = raceManager;
            int st = rm.state;
            if (stageIndex >= 0 && rm.stage != stageIndex
                && (st == RaceMath.STATE_COUNTDOWN || st == RaceMath.STATE_RACING || st == RaceMath.STATE_STAGE_RESULT)) return;
            string s;
            if (st == RaceMath.STATE_LOBBY)
            {
                s = showLastRace ? LastRaceText(rm) : "出走前";
            }
            else if (st == RaceMath.STATE_COUNTDOWN)
            {
                s = RaceManager.StageLabel(rm.stage) + "\nまもなく出走";
            }
            else if (st == RaceMath.STATE_RACING)
            {
                s = RaceManager.StageLabel(rm.stage) + " 順位\n";
                int line = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS && line < maxLines; p++)
                {
                    int slot = rm.order[p];
                    if (slot < 0 || rm.slotPlayerId[slot] < 0) continue;
                    line++;
                    s += line.ToString() + "位 " + RaceManager.PlayerName(rm.slotPlayerId[slot]) + "\n";
                }
            }
            else if (st == RaceMath.STATE_STAGE_RESULT)
            {
                s = RaceManager.StageLabel(rm.stage) + " 結果\n";
                for (int place = 1; place <= RaceManager.MAX_SLOTS && place <= maxLines; place++)
                {
                    for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                    {
                        if (rm.slotPlayerId[i] < 0 || rm.slotStagePlace[i] != place) continue;
                        s += place.ToString() + "位 " + RaceManager.PlayerName(rm.slotPlayerId[i])
                            + "  +" + RaceMath.PointsForPlace(place - 1).ToString() + "点（計" + rm.slotTotalPoints[i].ToString() + "点）\n";
                    }
                }
            }
            else
            {
                s = "総合結果\n";
                int[] overall = rm.GetOverallOrder();
                for (int k = 0; k < overall.Length && k < maxLines; k++)
                {
                    int slot = overall[k];
                    s += (k + 1).ToString() + "位 " + RaceManager.PlayerName(rm.slotPlayerId[slot])
                        + "  " + rm.slotTotalPoints[slot].ToString() + "点\n";
                }
            }
            if (boardText.text != s) boardText.text = s;
        }

        /// <summary>前回のレース：「1位 名前  第1 1位／第2 3位／第3 2位  計24点」。</summary>
        private string LastRaceText(RaceManager rm)
        {
            if (!rm.lastValid) return "前回のレース結果\n\nまだ記録がない";
            string s = "前回のレース結果\n";
            for (int row = 0; row < RaceManager.MAX_SLOTS && row < maxLines; row++)
            {
                string name = rm.lastName[row];
                if (name == null || name.Length == 0) continue;
                s += (row + 1).ToString() + "位 " + name + "  ";
                for (int st = 0; st < RaceManager.STAGE_COUNT; st++)
                {
                    int place = rm.lastStagePlace[st * RaceManager.MAX_SLOTS + row];
                    if (st > 0) s += "／";
                    s += "第" + (st + 1).ToString() + " " + (place > 0 ? place.ToString() + "位" : "―");
                }
                s += "  計" + rm.lastTotal[row].ToString() + "点\n";
            }
            return s;
        }
    }
}
