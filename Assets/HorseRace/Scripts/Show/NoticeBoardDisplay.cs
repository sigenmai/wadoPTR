using TMPro;
using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// たまり場の掲示板（NoticeBoard.fbx の NoticeBoard_Panel、16:9・UV 0〜1）に表示するもの。板ごとにインスペクターで mode を決める。
    ///   0 = 中継：SpectatorDirector が描画中なら RenderTexture のマテリアル、それ以外は羊皮紙のマテリアル＋案内文
    ///   1 = 順位表（電報風）：レース中は順位・名前・能力・残り時間、ステージ結果は新聞売り（TownCrier）の電報、総合結果は合計点
    ///   2 = エントリー一覧：台帳にサインした人・乗り物・能力・合計点
    /// 0.5 秒ごとに RaceManager の同期値を読んで書き直す（ローカルのみ・同期しない）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoticeBoardDisplay : UdonSharpBehaviour
    {
        public const int MODE_BROADCAST = 0;
        public const int MODE_STANDINGS = 1;
        public const int MODE_ENTRIES = 2;
        public const float REFRESH_INTERVAL = 0.5f;

        [Tooltip("0 = 中継、1 = 順位表（電報）、2 = エントリー一覧")]
        public int mode;
        public RaceManager raceManager;
        [Tooltip("mode 0 で使う")]
        public SpectatorDirector director;

        [Header("板")]
        [Tooltip("NoticeBoard_Panel の Renderer")]
        public Renderer panelRenderer;
        [Tooltip("中継用（Unlit、Main Texture = 中継の RenderTexture）")]
        public Material broadcastMaterial;
        [Tooltip("羊皮紙（WantedPosters の紙のマテリアルを流用してよい）。mode 1 / 2 と中継の休止中")]
        public Material parchmentMaterial;
        [Tooltip("板の上に重ねる文字（TextMeshPro、古い活字風フォント）")]
        public TextMeshPro boardText;
        [Tooltip("表示する人数の上限")]
        public int maxLines = 8;

        private string telegram = "";
        private int telegramSeq = -1;
        private float nextRefresh;
        private int lastMaterialState = -1;

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + REFRESH_INTERVAL;
            Refresh();
        }

        /// <summary>新聞売り（TownCrier）が電報を貼る。seq はそのステージの stageSeq。</summary>
        public void PostTelegram(string text, int seq)
        {
            telegram = text;
            telegramSeq = seq;
            if (mode == MODE_STANDINGS) Refresh();
        }

        public void Refresh()
        {
            if (raceManager == null) return;
            if (mode == MODE_BROADCAST) RefreshBroadcast();
            else if (mode == MODE_ENTRIES)
            {
                SetPanel(false);
                SetText(EntriesText());
            }
            else
            {
                SetPanel(false);
                SetText(StandingsText());
            }
        }

        private void RefreshBroadcast()
        {
            bool live = director != null && director.IsLive();
            SetPanel(live);
            if (live)
            {
                SetText("");
                return;
            }
            int st = raceManager.state;
            bool racing = st == RaceMath.STATE_COUNTDOWN || st == RaceMath.STATE_RACING;
            if (racing && raceManager.IsLocalEntered()) SetText("中継\n\nゴールすると映ります");
            else if (racing) SetText("中継\n\nまもなく映ります");
            else SetText("中継\n\nレースが始まると\nここに映ります");
        }

        private void SetPanel(bool broadcast)
        {
            int k = broadcast ? 1 : 0;
            if (k == lastMaterialState || panelRenderer == null) return;
            lastMaterialState = k;
            Material mat = broadcast ? broadcastMaterial : parchmentMaterial;
            if (mat != null) panelRenderer.sharedMaterial = mat;
        }

        private void SetText(string s)
        {
            if (boardText == null) return;
            if (boardText.text != s) boardText.text = s;
        }

        private string StandingsText()
        {
            RaceManager rm = raceManager;
            int st = rm.state;
            if (st == RaceMath.STATE_COUNTDOWN)
            {
                string c = "<b><size=140%>電報</size></b>\n" + RaceManager.StageLabel(rm.stage) + " まもなく出走\n";
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                {
                    if (rm.slotPlayerId[i] < 0) continue;
                    c += HudMath.Row2("・", RaceManager.ShortName(rm.slotPlayerId[i]) + "（" + RaceManager.AbilityLabel(rm.slotAbility[i]) + "）") + "\n";
                }
                return c;
            }
            if (st == RaceMath.STATE_RACING)
            {
                string s = "<b>" + RaceManager.StageLabel(rm.stage) + " 順位　残り " + RaceMath.FormatClock(rm.GetRemainingSeconds()) + "</b>\n";
                int line = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS && line < maxLines; p++)
                {
                    int slot = rm.order[p];
                    if (slot < 0 || rm.slotPlayerId[slot] < 0) continue;
                    line++;
                    // 進み具合（CP の四角）かゴールのタイム（ui-r2-15）。先頭の行はステージの色
                    string row = HudMath.RaceRow(line.ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot]), RaceManager.AbilityLabel(rm.slotAbility[slot]), rm.StandingCell(slot));
                    if (line == 1) row = "<color=" + HudMath.ColorHex(RaceManager.StageColor(rm.stage)) + ">" + row + "</color>";
                    s += row + "\n";
                }
                if (line == 0) s += "（集計中）\n";
                return s;
            }
            if (st == RaceMath.STATE_STAGE_RESULT)
            {
                if (telegramSeq == rm.stageSeq && telegram.Length > 0) return telegram;
                return TownCrier.StageTelegram(rm); // 新聞売りがいなくても電報は出す
            }
            if (st == RaceMath.STATE_FINAL_RESULT)
            {
                string f = "<b><size=140%>電報</size></b>\n総合結果\n";
                int[] overall = rm.GetOverallOrder();
                for (int k = 0; k < overall.Length && k < maxLines; k++)
                {
                    int slot = overall[k];
                    f += HudMath.Row4((k + 1).ToString() + "位", RaceManager.ShortName(rm.slotPlayerId[slot]), "", rm.slotTotalPoints[slot].ToString() + "点") + "\n";
                }
                f += "<b>" + HudMath.RematchLine() + "</b>";   // system-r2-06
                return f;
            }
            // ロビー：最後に貼られた電報を残す
            if (telegram.Length > 0) return telegram;
            return "<b><size=140%>電報</size></b>\n\n次のレースを待つ";
        }

        private string EntriesText()
        {
            RaceManager rm = raceManager;
            int count = rm.ActiveCount();
            string s = "<b><size=140%>出走表</size>（" + count.ToString() + "/" + RaceManager.MAX_SLOTS.ToString() + "）</b>\n";
            if (count == 0) return s + "\n受付の台帳にサインして出走登録";
            float left = rm.GetLobbySecondsLeft();
            if (left >= 0f) s += "<b>出走まで " + RaceMath.FormatClock(left) + "</b>\n";
            if (left >= 0f && rm.state == RaceMath.STATE_LOBBY && count == 1) s += "ひとりで走る（タイムアタック）\n";   // system-r2-04
            else if (left >= 0f && rm.state == RaceMath.STATE_LOBBY && rm.rematch) s += "もう一戦（台帳で取り消せる）\n";   // system-r2-06
            bool showPoints = rm.state != RaceMath.STATE_LOBBY;
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
            {
                if (rm.slotPlayerId[i] < 0) continue;
                s += "<pos=0%>" + RaceManager.ShortName(rm.slotPlayerId[i]) + "<pos=38%>"
                    + RaceManager.MountLabel(rm.slotMount[i], rm.slotCoat[i]) + "・"
                    + RaceManager.AbilityLabel(rm.slotAbility[i]);
                if (rm.IsNoStart(i)) s += "<pos=85%>（不在）";   // system-r2-05
                else if (showPoints) s += "<pos=85%>" + rm.slotTotalPoints[i].ToString() + "点";
                s += "\n";
            }
            return s;
        }
    }
}
