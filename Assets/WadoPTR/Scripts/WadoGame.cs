using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

/// <summary>
/// 和堂PTR 第七加工センター 夜間処理室 — 進行管理。
/// 経済・発注(強化)・相場・保存/復元・掲示類の更新をすべてここで行う。
/// 進行はプレイヤーごとにローカルで完結し、保存は PlayerData(各自のアカウント)に行う。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoGame : UdonSharpBehaviour
{
    // ---- 行動ID (WadoButton.action) ----
    public const int ActBuy = 0;        // arg = 発注行
    public const int ActCall = 1;       // 搬入呼出
    public const int ActSweep = 2;      // 床清掃(散らかった部材を受け皿へ)
    public const int ActTransfer = 3;   // 異動願
    public const int ActTransferConfirm = 4;

    public const int UpgradeCount = 10;
    public const int UpBlade = 0;
    public const int UpPrice = 1;
    public const int UpOrgans = 2;
    public const int UpBelt = 3;
    public const int UpCold = 4;
    public const int UpGenFirst = 5;    // 5..9 は常時収益

    [Header("連携")]
    public WadoBody body;

    [Header("表示")]
    public TextMeshPro txtMoney;
    public TextMeshPro txtRate;
    public TextMeshPro txtRank;
    public TextMeshPro txtScale;
    public TextMeshPro txtMarket;
    public TextMeshPro txtSlip;
    public TextMeshPro txtRoster;
    public TextMeshPro txtSchedule;
    public TextMeshPro txtHatch;
    public TextMeshPro txtTransfer;
    public TextMeshPro[] rowName;
    public TextMeshPro[] rowInfo;

    [Header("段階的に現れるもの")]
    public GameObject[] memos;          // 前任者の走り書きなど。累計出荷額で順に現れる
    public GameObject[] stains;         // 処理数に応じて増える汚れ
    public GameObject[] lineLamps;      // 常時収益 5 系統の稼働灯
    public GameObject beltVisual;
    public GameObject organShelfVisual;
    public GameObject scheduleBoard;    // 搬入予定表(終盤)
    public GameObject transferConfirmButton;

    [Header("音")]
    public AudioSource sfx;
    public AudioSource sfxScale;
    public AudioClip[] clipCoin;
    public AudioClip clipBeep;
    public AudioClip clipBeepOk;
    public AudioClip clipBuzzer;
    public AudioClip clipPrinter;
    public AudioClip clipChimeUp;
    public AudioClip clipChimeDown;
    public AudioClip clipPlop;

    // ---- 保存される状態 ----
    private double money;
    private double lifetime;        // 累計出荷額(異動しても残る)
    private double runEarned;       // 現在の配属での出荷額
    private int transfers;          // 異動回数
    private double merit;           // 評価点(異動で得る。収益倍率)
    private int bodiesDone;
    private int[] level;

    // ---- 定義 ----
    private bool defined;
    private string[] upName;
    private string[] upNote;
    private double[] upBase;
    private double[] upGrowth;
    private int[] upMax;
    private double[] genRate;
    private double[] partBase;
    private string[] partName;
    private float[] partWeight;
    private int[] partCat;
    private string[] catName;
    private double[] memoAt;
    private int[] stainAt;
    private string[] rankName;
    private double[] rankAt;
    private string[] unitName;

    // ---- 実行時 ----
    private bool restored;
    private bool dirty;
    private float saveTimer;
    private float uiTimer;
    private float rowTimer;
    private float marketTimer;
    private float rosterTimer;
    private float[] market;
    private float[] marketPrev;
    private double rate;
    private double priceMult;
    private string[] slip;
    private int lastRank = -1;
    private int lastMemo = -1;
    private float transferArm;
    private const float MarketPeriod = 90f;
    private const string K = "wptr_";

    void Start()
    {
        Define();
        Recalc();
        RollMarket(true);
        Print("管理室: 打刻を確認しました。");
        Print("管理室: 作業標準書に従ってください。");
        RefreshAll();
    }

    private void Define()
    {
        if (defined) return;
        defined = true;

        level = new int[UpgradeCount];
        upName = new string[] {
            "刃物研磨 申請", "卸単価 改定交渉", "特定部材 取扱許可", "搬送ベルト 設置", "保冷設備 増強",
            "夜間補助員 配置", "自動分割機 導入", "第二処理ライン 増設", "搬入元 提携拡大", "海外販路 開拓" };
        upNote = new string[] {
            "一振りの切断深度", "全部材の買取単価 +15%", "胸腹部内容物の採取と出荷", "分離部材を計量口へ自動搬送", "鮮度等級の低下を遅らせる",
            "常時出荷", "常時出荷", "常時出荷", "常時出荷", "常時出荷" };
        upBase = new double[] { 3000, 5000, 60000, 25000, 8000, 15000, 200000, 3000000, 50000000, 1000000000 };
        upGrowth = new double[] { 4.0, 1.8, 1.0, 1.0, 3.0, 1.15, 1.15, 1.15, 1.15, 1.15 };
        upMax = new int[] { 4, 60, 1, 1, 5, 9999, 9999, 9999, 9999, 9999 };
        genRate = new double[] { 0, 0, 0, 0, 0, 20, 260, 3500, 48000, 700000 };

        // 部材: 0 頭部 / 1,2 上肢 / 3,4 下肢 / 5 体幹 / 6 肝 / 7,8 腎 / 9 心
        partBase = new double[] { 800, 500, 500, 700, 700, 1500, 4000, 3000, 3000, 6000 };
        partName = new string[] { "頭部", "上肢(左)", "上肢(右)", "下肢(左)", "下肢(右)", "体幹", "内容物 イ", "内容物 ロ", "内容物 ロ", "内容物 ハ" };
        partWeight = new float[] { 4.6f, 3.4f, 3.4f, 11.2f, 11.2f, 29.5f, 1.4f, 0.15f, 0.15f, 0.3f };
        partCat = new int[] { 0, 1, 1, 2, 2, 3, 4, 4, 4, 4 };
        catName = new string[] { "頭部", "上肢", "下肢", "体幹", "内容物" };

        memoAt = new double[] { 5000, 30000, 150000, 1000000, 8000000, 50000000, 400000000, 3000000000 };
        stainAt = new int[] { 1, 3, 6, 10, 16, 25, 40, 60 };
        rankName = new string[] { "研修中", "処理員", "主任処理員", "班長", "係長", "課長代理", "センター長", "役員待遇", "次期原体候補" };
        rankAt = new double[] { 0, 20000, 300000, 3000000, 30000000, 300000000, 3000000000, 30000000000, 300000000000 };
        unitName = new string[] { "万", "億", "兆", "京", "垓" };

        market = new float[5];
        marketPrev = new float[5];
        slip = new string[6];
        for (int i = 0; i < slip.Length; i++) slip[i] = "";
    }

    // ------------------------------------------------------------------ 周期処理

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 1f) dt = 1f;

        if (rate > 0)
        {
            double gain = rate * dt;
            money += gain;
            lifetime += gain;
            runEarned += gain;
            dirty = true;
        }

        uiTimer -= dt;
        if (uiTimer <= 0f)
        {
            uiTimer = 0.25f;
            RefreshMoney();
        }

        rowTimer -= dt;
        if (rowTimer <= 0f)
        {
            rowTimer = 1f;
            RefreshRows();
        }

        marketTimer -= dt;
        if (marketTimer <= 0f) RollMarket(false);

        rosterTimer -= dt;
        if (rosterTimer <= 0f)
        {
            rosterTimer = 10f;
            RefreshRoster();
        }

        if (transferArm > 0f)
        {
            transferArm -= dt;
            if (transferArm <= 0f) RefreshTransfer();
        }

        saveTimer -= dt;
        if (saveTimer <= 0f && dirty && restored)
        {
            Save();
        }
    }

    // ------------------------------------------------------------------ 保存 / 復元

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player)) return;
        if (!player.isLocal)
        {
            RefreshRoster();
            return;
        }
        Define();

        double v;
        int n;
        if (PlayerData.TryGetDouble(player, K + "money", out v)) money = v;
        if (PlayerData.TryGetDouble(player, K + "life", out v)) lifetime = v;
        if (PlayerData.TryGetDouble(player, K + "run", out v)) runEarned = v;
        if (PlayerData.TryGetDouble(player, K + "merit", out v)) merit = v;
        if (PlayerData.TryGetInt(player, K + "transfers", out n)) transfers = n;
        if (PlayerData.TryGetInt(player, K + "bodies", out n)) bodiesDone = n;
        for (int i = 0; i < UpgradeCount; i++)
        {
            if (PlayerData.TryGetInt(player, K + "u" + i.ToString(), out n)) level[i] = Mathf.Clamp(n, 0, upMax[i]);
        }
        Recalc();

        // 不在の間も補助員とラインは動いている(上限 8 時間・効率 5 割)
        double then;
        if (PlayerData.TryGetDouble(player, K + "time", out then) && then > 0 && rate > 0)
        {
            double away = NowSeconds() - then;
            if (away > 28800) away = 28800;
            if (away > 60)
            {
                double gain = rate * away * 0.5;
                money += gain;
                lifetime += gain;
                runEarned += gain;
                int hours = (int)(away / 3600);
                int mins = (int)((away - hours * 3600) / 60);
                Print("不在時稼働報告 " + hours.ToString() + "時間" + mins.ToString("00") + "分");
                Print("  振込 " + Yen(gain));
                Chime(true);
            }
        }
        else if (lifetime > 0)
        {
            Print("管理室: おかえりなさい。");
        }

        restored = true;
        dirty = true;
        saveTimer = 5f;
        if (Utilities.IsValid(body)) body.OnGameRestored();
        RefreshAll();
    }

    private void Save()
    {
        dirty = false;
        saveTimer = 12f;
        PlayerData.SetDouble(K + "money", money);
        PlayerData.SetDouble(K + "life", lifetime);
        PlayerData.SetDouble(K + "run", runEarned);
        PlayerData.SetDouble(K + "merit", merit);
        PlayerData.SetInt(K + "transfers", transfers);
        PlayerData.SetInt(K + "bodies", bodiesDone);
        for (int i = 0; i < UpgradeCount; i++) PlayerData.SetInt(K + "u" + i.ToString(), level[i]);
        PlayerData.SetDouble(K + "time", NowSeconds());
        PlayerData.SetInt(K + "rank", RankIndex());
    }

    private double NowSeconds()
    {
        return System.DateTime.UtcNow.Ticks / 10000000.0;
    }

    // ------------------------------------------------------------------ 計算

    private void Recalc()
    {
        double r = 0;
        for (int i = UpGenFirst; i < UpgradeCount; i++) r += genRate[i] * level[i];
        rate = r * MeritMult();
        priceMult = 1.0 + 0.15 * level[UpPrice];
    }

    private double MeritMult()
    {
        return 1.0 + merit * 0.1;
    }

    public double CostOf(int row)
    {
        return System.Math.Floor(upBase[row] * System.Math.Pow(upGrowth[row], level[row]));
    }

    public int LevelOf(int row)
    {
        Define();
        return level[row];
    }

    /// <summary>刃物の一振りの切断力。</summary>
    public float BladeDamage()
    {
        Define();
        int l = level[UpBlade];
        if (l <= 0) return 1f;
        if (l == 1) return 2f;
        if (l == 2) return 3f;
        if (l == 3) return 5f;
        return 8f;
    }

    /// <summary>鮮度が「特」から「B」まで落ちるのにかかる秒数。</summary>
    public float FreshWindow()
    {
        Define();
        return 60f * (1f + 0.5f * level[UpCold]);
    }

    public bool HasOrgans() { Define(); return level[UpOrgans] > 0; }
    public bool HasBelt() { Define(); return level[UpBelt] > 0; }

    private int RankIndex()
    {
        int r = 0;
        for (int i = 0; i < rankAt.Length; i++) if (lifetime >= rankAt[i]) r = i;
        return r;
    }

    private double MeritOnTransfer()
    {
        return System.Math.Floor(System.Math.Sqrt(runEarned / 1000000.0));
    }

    // ------------------------------------------------------------------ 入力

    public void Press(int action, int arg)
    {
        Define();
        if (action == ActBuy) Buy(arg);
        else if (action == ActCall) { if (Utilities.IsValid(body)) body.Call(); }
        else if (action == ActSweep) { if (Utilities.IsValid(body)) body.Sweep(); }
        else if (action == ActTransfer) ArmTransfer();
        else if (action == ActTransferConfirm) DoTransfer();
    }

    private void Buy(int row)
    {
        if (row < 0 || row >= UpgradeCount) return;
        if (level[row] >= upMax[row])
        {
            Deny();
            return;
        }
        double cost = CostOf(row);
        if (money < cost)
        {
            Deny();
            Print("発注却下: 残高不足 (" + upName[row] + ")");
            return;
        }
        money -= cost;
        level[row]++;
        Recalc();
        dirty = true;
        saveTimer = 0.5f;
        Play(sfx, clipBeepOk, 0.8f);
        Play(sfx, clipPrinter, 0.5f);
        Print("承認: " + upName[row]);
        if (row == UpOrgans) Print("管理室: 内容物は専用の保冷箱へ。");
        if (row == UpBelt) Print("設備: 搬送ベルト 稼働開始");
        if (Utilities.IsValid(body)) body.OnUpgradesChanged();
        RefreshAll();
    }

    private void ArmTransfer()
    {
        if (MeritOnTransfer() < 1)
        {
            Deny();
            Print("人事: 異動の要件を満たしていません。");
            return;
        }
        transferArm = 12f;
        Play(sfx, clipBeep, 0.8f);
        RefreshTransfer();
    }

    private void DoTransfer()
    {
        if (transferArm <= 0f) return;
        double gain = MeritOnTransfer();
        if (gain < 1) return;
        transferArm = 0f;
        merit += gain;
        transfers++;
        money = 0;
        runEarned = 0;
        for (int i = 0; i < UpgradeCount; i++) level[i] = 0;
        Recalc();
        dirty = true;
        saveTimer = 0.5f;
        Chime(true);
        Print("人事: 異動を発令します。評価 +" + gain.ToString("N0"));
        Print("人事: 新しい処理室でも、同じ手順で。");
        if (Utilities.IsValid(body)) body.OnUpgradesChanged();
        RefreshAll();
    }

    // ------------------------------------------------------------------ 計量 / 出荷

    /// <summary>計量口に部材が入った。freshness は 0.8〜1.5。</summary>
    public void Deposit(int kind, float freshness)
    {
        Define();
        if (kind < 0 || kind >= partBase.Length) return;
        int cat = partCat[kind];
        double value = partBase[kind] * market[cat] * priceMult * freshness * MeritMult();
        value += rate * 3.0;    // 手作業分の歩合
        value = System.Math.Floor(value);
        money += value;
        lifetime += value;
        runEarned += value;
        dirty = true;

        string grade = "C";
        if (freshness >= 1.35f) grade = "特";
        else if (freshness >= 1.15f) grade = "A";
        else if (freshness >= 1.0f) grade = "B";
        float w = partWeight[kind] * Random.Range(0.92f, 1.08f);
        if (Utilities.IsValid(txtScale))
        {
            txtScale.text = partName[kind] + "\n" + w.ToString("0.00") + " kg  鮮度 " + grade + "\n<size=140%>" + Yen(value) + "</size>";
        }
        Print("計量 " + partName[kind] + " " + grade + " " + Yen(value));
        Play(sfxScale, clipPlop, 0.7f);
        Play(sfxScale, clipBeep, 0.6f);
        if (Utilities.IsValid(clipCoin) && clipCoin.Length > 0)
        {
            Play(sfxScale, clipCoin[Random.Range(0, clipCoin.Length)], 0.7f);
        }
        RefreshMoney();
        RefreshRows();
        CheckProgress();
    }

    /// <summary>原体 1 体の分割が終わった。</summary>
    public void BodyFinished()
    {
        bodiesDone++;
        dirty = true;
        CheckProgress();
    }

    public int BodiesDone() { return bodiesDone; }

    // ------------------------------------------------------------------ 相場

    private void RollMarket(bool first)
    {
        marketTimer = MarketPeriod;
        for (int i = 0; i < market.Length; i++)
        {
            marketPrev[i] = first ? 1f : market[i];
            market[i] = Mathf.Round(Random.Range(0.7f, 1.4f) * 20f) / 20f;
        }
        if (!first)
        {
            Chime(false);
            Print("相場改定のお知らせ");
        }
        RefreshMarket();
    }

    // ------------------------------------------------------------------ 表示

    private void RefreshAll()
    {
        RefreshMoney();
        RefreshRows();
        RefreshMarket();
        RefreshRoster();
        RefreshTransfer();
        CheckProgress();
    }

    private void RefreshMoney()
    {
        if (Utilities.IsValid(txtMoney)) txtMoney.text = Yen(money);
        if (Utilities.IsValid(txtRate))
        {
            txtRate.text = "常時出荷 " + Yen(rate) + " /秒\n本日処理数 " + bodiesDone.ToString("N0") + " 体";
        }
    }

    private void RefreshRows()
    {
        if (!Utilities.IsValid(rowName) || !Utilities.IsValid(rowInfo)) return;
        for (int i = 0; i < UpgradeCount; i++)
        {
            if (i >= rowName.Length || i >= rowInfo.Length) break;
            bool maxed = level[i] >= upMax[i];
            double cost = CostOf(i);
            string head = upName[i];
            if (upMax[i] == 1) head += level[i] > 0 ? "  <color=#7CFF9B>済</color>" : "";
            else if (i >= UpGenFirst) head += "  ×" + level[i].ToString("N0");
            else head += "  Lv." + level[i].ToString();
            if (Utilities.IsValid(rowName[i])) rowName[i].text = head;

            string info;
            if (maxed) info = "<color=#7CFF9B>承認済</color>  " + upNote[i];
            else
            {
                string c = money >= cost ? "<color=#FFE27A>" : "<color=#FF6A5E>";
                info = c + Yen(cost) + "</color>  ";
                if (i >= UpGenFirst) info += "+" + Yen(genRate[i] * MeritMult()) + "/秒";
                else info += upNote[i];
            }
            if (Utilities.IsValid(rowInfo[i])) rowInfo[i].text = info;
        }
        if (Utilities.IsValid(lineLamps))
        {
            for (int i = 0; i < lineLamps.Length; i++)
            {
                int row = UpGenFirst + i;
                if (row < UpgradeCount && Utilities.IsValid(lineLamps[i])) lineLamps[i].SetActive(level[row] > 0);
            }
        }
        if (Utilities.IsValid(beltVisual)) beltVisual.SetActive(level[UpBelt] > 0);
        if (Utilities.IsValid(organShelfVisual)) organShelfVisual.SetActive(level[UpOrgans] > 0);
    }

    private void RefreshMarket()
    {
        if (!Utilities.IsValid(txtMarket)) return;
        string s = "";
        for (int i = 0; i < catName.Length; i++)
        {
            if (i == 4 && level[UpOrgans] <= 0)
            {
                s += "―――  取扱不可\n";
                continue;
            }
            string arrow = "<color=#AAAAAA>→</color>";
            if (market[i] > marketPrev[i] + 0.01f) arrow = "<color=#FF6A5E>▲</color>";
            else if (market[i] < marketPrev[i] - 0.01f) arrow = "<color=#6AB8FF>▼</color>";
            s += catName[i] + "  " + Mathf.RoundToInt(market[i] * 100f).ToString() + "%  " + arrow + "\n";
        }
        txtMarket.text = s;
    }

    private void RefreshTransfer()
    {
        bool armed = transferArm > 0f;
        if (Utilities.IsValid(transferConfirmButton)) transferConfirmButton.SetActive(armed);
        if (!Utilities.IsValid(txtTransfer)) return;
        double gain = MeritOnTransfer();
        string s = "現在の評価  " + merit.ToString("N0") + " 点  (出荷額 +" + (merit * 10).ToString("N0") + "%)\n";
        s += "異動回数  " + transfers.ToString() + " 回\n";
        if (armed)
        {
            s += "<color=#FF6A5E>残高・設備・許可はすべて現処理室に残ります。\n右の「確認」を押すと発令されます。</color>";
        }
        else if (gain >= 1)
        {
            s += "<color=#7CFF9B>いま提出すると 評価 +" + gain.ToString("N0") + " 点</color>";
        }
        else
        {
            s += "要件: 現配属での出荷額 " + Yen(1000000) + " 以上";
        }
        txtTransfer.text = s;
    }

    private void RefreshRoster()
    {
        Define();
        int count = VRCPlayerApi.GetPlayerCount();
        if (count <= 0) return;
        VRCPlayerApi[] players = new VRCPlayerApi[count];
        VRCPlayerApi.GetPlayers(players);
        string roster = "";
        string schedule = "";
        int shown = 0;
        for (int i = 0; i < players.Length; i++)
        {
            VRCPlayerApi p = players[i];
            if (!Utilities.IsValid(p)) continue;
            if (shown >= 10) break;
            shown++;
            int r = 0;
            if (p.isLocal) r = RankIndex();
            else r = Mathf.Clamp(PlayerData.GetInt(p, K + "rank"), 0, rankName.Length - 1);
            roster += p.displayName + "  <color=#AAAAAA>" + rankName[r] + "</color>\n";
            schedule += "原体 No.7-" + (4100 + p.playerId * 37).ToString("00000") + "  " + p.displayName + " 様\n";
        }
        if (Utilities.IsValid(txtRoster)) txtRoster.text = roster;
        if (Utilities.IsValid(txtSchedule)) txtSchedule.text = schedule;
    }

    public override void OnPlayerJoined(VRCPlayerApi player) { rosterTimer = 1f; }
    public override void OnPlayerLeft(VRCPlayerApi player) { rosterTimer = 1f; }

    /// <summary>累計出荷額・処理数に応じて、部屋の様子と肩書きを進める。</summary>
    private void CheckProgress()
    {
        int rank = RankIndex();
        if (rank != lastRank)
        {
            if (lastRank >= 0 && rank > lastRank)
            {
                Chime(true);
                Print("辞令: " + rankName[rank] + " を命ずる");
            }
            lastRank = rank;
            if (Utilities.IsValid(txtRank)) txtRank.text = rankName[rank];
        }

        int memo = -1;
        for (int i = 0; i < memoAt.Length; i++) if (lifetime >= memoAt[i]) memo = i;
        if (memo != lastMemo)
        {
            lastMemo = memo;
            if (Utilities.IsValid(memos))
            {
                for (int i = 0; i < memos.Length; i++)
                {
                    if (Utilities.IsValid(memos[i])) memos[i].SetActive(i <= memo);
                }
            }
        }
        if (Utilities.IsValid(stains))
        {
            for (int i = 0; i < stains.Length; i++)
            {
                if (i < stainAt.Length && Utilities.IsValid(stains[i])) stains[i].SetActive(bodiesDone >= stainAt[i]);
            }
        }
        if (Utilities.IsValid(scheduleBoard)) scheduleBoard.SetActive(rank >= 5);
    }

    /// <summary>伝票プリンタに 1 行出す。</summary>
    public void Print(string line)
    {
        Define();
        for (int i = 0; i < slip.Length - 1; i++) slip[i] = slip[i + 1];
        slip[slip.Length - 1] = line;
        if (!Utilities.IsValid(txtSlip)) return;
        string s = "";
        for (int i = 0; i < slip.Length; i++) s += slip[i] + "\n";
        txtSlip.text = s;
    }

    public void SetHatchText(string s)
    {
        if (Utilities.IsValid(txtHatch)) txtHatch.text = s;
    }

    public void Deny()
    {
        Play(sfx, clipBuzzer, 0.6f);
    }

    private void Chime(bool up)
    {
        Play(sfx, up ? clipChimeUp : clipChimeDown, 0.6f);
    }

    private void Play(AudioSource src, AudioClip clip, float volume)
    {
        if (Utilities.IsValid(src) && Utilities.IsValid(clip)) src.PlayOneShot(clip, volume);
    }

    /// <summary>¥1億2,345万 のような和式表記。</summary>
    public string Yen(double v)
    {
        Define();
        if (v < 0) v = 0;
        v = System.Math.Floor(v);
        if (v < 10000) return "¥" + v.ToString("N0");
        double unit = 10000;
        int idx = 0;
        while (idx < unitName.Length - 1 && v >= unit * 10000)
        {
            unit *= 10000;
            idx++;
        }
        double major = System.Math.Floor(v / unit);
        if (major >= 100000000) return "¥" + v.ToString("0.00E+0");
        double minor = System.Math.Floor((v - major * unit) / (unit / 10000));
        string s = "¥" + major.ToString("N0") + unitName[idx];
        if (minor > 0 && major < 1000)
        {
            if (idx > 0) s += minor.ToString("N0") + unitName[idx - 1];
            else s += minor.ToString("N0");
        }
        return s;
    }
}
