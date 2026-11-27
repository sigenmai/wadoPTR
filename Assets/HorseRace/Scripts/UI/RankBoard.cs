using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 沿道・チェックポイントの黒板。チョーク風の文字で順位を書く（RaceManager が同期を受けるたびに Refresh）。
    /// レース中 = 現在ステージの順位、ステージ結果 = 着順と獲得点、総合結果 = 合計点の順。
    /// showLastRace（たまり場の黒板）ならロビー中は前回のレースの表。
    /// レース中のコースの黒板（showLastRace = false）は走りながら読めるよう上位 3 人だけを大きく（「1 ○○」）、その下に自分の順位
    /// （「あなた 5位」、3 位以内は金）。黒板は同期しないので自分の順位はクライアントごとに書ける。
    /// 名前は 10 文字まで（RaceManager.ShortName）、列は TMP の pos タグでそろえる。文字は自動で縮み、入らなければ「…」。
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
        [Tooltip("ゴール後の囲いの黒板：レース中は全員の順位と残り時間（1 秒ごとに書き直す。system-r1-10）")]
        public bool penBoard;

        private float nextPenTick;

        /// <summary>レース中のコースの黒板：上位何人を書くか・大きさ（黒板の基本の文字に対する %）。</summary>
        public const int TOP_LINES = 3;
        public const int TOP_SIZE_PCT = 100;
        public const int LOCAL_SIZE_PCT = 118;

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (!penBoard || raceManager == null || raceManager.state != RaceMath.STATE_RACING) return;
            if (Time.time < nextPenTick) return;
            nextPenTick = Time.time + 1f;
            Refresh();
        }

        /// <summary>見出し：ステージ名（ステージの色・太字・140%）＋ 後ろの言葉。</summary>
        private static string Header(int stage, string tail)
        {
            return "<b><size=140%><color=" + HudMath.ColorHex(RaceManager.StageColor(stage)) + ">" + RaceManager.StageLabel(stage) + "</color>" + tail + "</size></b>";
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
                s = Header(rm.stage, "") + "\nまもなく出走";
            }
            else if (st == RaceMath.STATE_RACING && penBoard)
            {
                s = Header(rm.stage, " 残り " + RaceMath.FormatClock(rm.GetRemainingSeconds())) + "\n";
                int line = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS && line < maxLines; p++)
                {
                    int slot = rm.order[p];
                    if (slot < 0 || rm.slotPlayerId[slot] < 0) continue;
                    line++;
                    MountController m = (rm.mounts != null && slot < rm.mounts.Length) ? rm.mounts[slot] : null;
                    bool done = m != null && m.syncFinishAt >= rm.stageStartServer;
                    s += HudMath.Row2(line.ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot]) + (done ? " ゴール" : "")) + "\n";
                }
                s += "<size=80%>板の札で追う人を選べる</size>";
            }
            else if (st == RaceMath.STATE_RACING && !showLastRace)
            {
                s = Header(rm.stage, " 順位") + "\n";
                int line = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS && line < TOP_LINES; p++)
                {
                    int slot = rm.order[p];
                    if (slot < 0 || rm.slotPlayerId[slot] < 0) continue;
                    line++;
                    s += "<size=" + TOP_SIZE_PCT.ToString() + "%>" + line.ToString() + " " + RaceManager.ShortName(rm.slotPlayerId[slot]) + "</size>\n";
                }
                VRCPlayerApi local = Networking.LocalPlayer;
                int mine = local != null ? rm.GetRankOf(local.playerId) : 0;
                if (mine > 0)
                    s += "<size=" + LOCAL_SIZE_PCT.ToString() + "%>" + (mine <= 3 ? "<color=#FFC745>" : "") + "あなた " + mine.ToString() + "位" + (mine <= 3 ? "</color>" : "") + "</size>";
            }
            else if (st == RaceMath.STATE_RACING)
            {
                s = Header(rm.stage, " 順位") + "\n";
                int line = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS && line < maxLines; p++)
                {
                    int slot = rm.order[p];
                    if (slot < 0 || rm.slotPlayerId[slot] < 0) continue;
                    line++;
                    s += HudMath.Row2(line.ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot])) + "\n";
                }
            }
            else if (st == RaceMath.STATE_STAGE_RESULT)
            {
                s = Header(rm.stage, " 結果") + "\n";
                for (int place = 1; place <= RaceManager.MAX_SLOTS && place <= maxLines; place++)
                {
                    for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                    {
                        if (rm.slotPlayerId[i] < 0 || rm.slotStagePlace[i] != place) continue;
                        s += HudMath.Row4(place.ToString() + "着", RaceManager.ShortName(rm.slotPlayerId[i]),
                            "+" + RaceMath.PointsForPlace(place - 1, rm.stage).ToString(), "計" + rm.slotTotalPoints[i].ToString()) + "\n";
                    }
                }
            }
            else
            {
                s = "<b><size=140%>総合結果</size></b>\n";
                int[] overall = rm.GetOverallOrder();
                for (int k = 0; k < overall.Length && k < maxLines; k++)
                {
                    int slot = overall[k];
                    s += HudMath.Row4((k + 1).ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot]), "", rm.slotTotalPoints[slot].ToString() + "点") + "\n";
                }
            }
            if (boardText.text != s) boardText.text = s;
        }

        /// <summary>前回のレース：「1位 名前  第1 1位／第2 3位／第3 2位  計24点」。</summary>
        private string LastRaceText(RaceManager rm)
        {
            if (!rm.lastValid) return "<b><size=140%>前回のレース結果</size></b>\n\nまだ記録がない";
            string s = "<b><size=140%>前回のレース結果</size></b>\n";
            for (int row = 0; row < RaceManager.MAX_SLOTS && row < maxLines; row++)
            {
                string name = rm.lastName[row];
                if (name == null || name.Length == 0) continue;
                s += "<pos=0%>" + (row + 1).ToString() + "位<pos=12%>" + HudMath.ShortName(name, HudMath.SHORT_NAME_MAX) + "<pos=45%>";
                for (int st = 0; st < RaceManager.STAGE_COUNT; st++)
                {
                    int place = rm.lastStagePlace[st * RaceManager.MAX_SLOTS + row];
                    if (st > 0) s += "／";
                    s += HudMath.StageName(st) + " " + (place > 0 ? place.ToString() + "位" : "―");
                }
                s += "<pos=88%>計" + rm.lastTotal[row].ToString() + "\n";
            }
            return s;
        }
    }
}
