using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 沿道・チェックポイントの黒板。チョーク風の文字で順位を書く（RaceManager が同期を受けるたびに Refresh）。
    /// レース中 = 現在ステージの順位、ステージ結果 = 着順と獲得点、総合結果 = 合計点の順。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RankBoard : UdonSharpBehaviour
    {
        public RaceManager raceManager;
        [Tooltip("黒板の文字（TextMeshPro、チョーク風フォント）")]
        public TMP_Text boardText;
        [Tooltip("表示する人数の上限")]
        public int maxLines = 8;

        private void Start()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (boardText == null || raceManager == null) return;
            RaceManager rm = raceManager;
            int st = rm.state;
            string s;
            if (st == RaceMath.STATE_LOBBY)
            {
                s = "出走前";
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
            boardText.text = s;
        }
    }
}
