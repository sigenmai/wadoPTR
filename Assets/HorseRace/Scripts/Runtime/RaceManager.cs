using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// レース進行。オーナー（＝インスタンスマスター）だけが状態を書き換え、Manual 同期で全員に配る。
    ///
    /// 状態: LOBBY →(号砲 or 出走時計)→ COUNTDOWN 3 s（Starter があれば旗＋空砲、最大 6 s）→ RACING（毎秒 ShouldEndStage）
    ///       → STAGE_RESULT 20 s（ポイント加算・能力/弾/遺物のリセット・途中参加の受付・乗り物と能力の乗り換え）→ 次の COUNTDOWN
    ///       （RACING は 1 位ゴール 60 秒後・5:30・全員ゴールのいちばん早いもので終わる）
    ///       ステージ中（COUNTDOWN / RACING）のサインは pending* に預かり、STAGE_RESULT に入るときに空き枠へ入れる（次のステージから走る）
    ///       … 3 ステージ後 FINAL_RESULT 20 s → LOBBY（もう一戦：同じ顔ぶれのまま点だけ消し、30 秒で次のレース。system-r2-06）
    ///       台帳でいつでも取り消せる（RequestWithdraw、system-r2-05）。号砲から 20 秒たっても最初の CP に届かず止まっている人は「不在」で、次の合間で枠を空ける。
    ///       ひとりでも 30 秒で出走する（タイムアタック、system-r2-04）。ステージの最速タイムはコース記録（stageBest*）に残る。
    ///       順位（order / firstFinishServer）は子の RaceOrder が小さく同期する（system-r2-13）。RaceManager は状態・枠が変わったときだけ同期する。
    ///       観客の予想（system-r2-15）：電報の掲示板の札で勝つと思う人を選び、ステージ結果で 1 着 3 点・3 着まで 1 点（PlayerData に通算を保存）。
    ///
    /// 依頼（クライアント → マスター）は引数つきネットワークイベント（[NetworkCallable]）。マスター側で必ず検証する。
    /// 順位は各 MountController の同期値（syncCp / syncDistToCp / syncFinishAt）からマスターが RaceMath.ComputeOrder で計算する。
    /// 乗り物の着席・テレポート・進行リセットは、同期状態を受け取った各クライアントが自分の乗り物にだけ行う（オーナー側処理）。
    /// 声の距離は各クライアントのローカル設定なので、OnPlayerJoined とレース開始時に全員ぶん自分で設定する。
    ///
    /// Task 9/10 の型には依存しない：ステージ間のリセットは UdonSharpBehaviour 配列へ SendCustomEvent で送る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RaceManager : UdonSharpBehaviour
    {
        public const int MAX_SLOTS = 8;
        public const int STAGE_COUNT = 3;
        public const float COUNTDOWN_SECONDS = 3f;
        public const float COUNTDOWN_MAX_SECONDS = 6f;
        /// <summary>ステージの合間（system-r1-08：囲いの手配書で乗り物・能力を選び直す時間）。</summary>
        public const float STAGE_RESULT_SECONDS = 20f;
        public const float FINAL_RESULT_SECONDS = 20f;
        public const float FINISH_PEN_DELAY = 3f;
        public const float VOICE_FAR = 10000f;
        public const float VOICE_NEAR = 0f;
        /// <summary>Phase 1 の能力は 0..3（砂塵・投げ縄・岩壁・鉄球）。</summary>
        public const int MAX_ABILITY_PHASE1 = 3;
        public const float MASTER_TICK = 0.25f;
        /// <summary>順位の計算の間隔（8 枠なので軽い。同期は変わったときだけ）。system-r1-17</summary>
        public const double RACE_EVAL_INTERVAL = 0.25;

        // ---- 同期（マスターだけが書く）----
        [UdonSynced] public int state;
        [UdonSynced] public int stage;
        [UdonSynced] public double stageStartServer;
        /// <summary>最初のゴールのサーバー時刻（-1 = まだ）。RaceOrder が運ぶ（同期しない、system-r2-13）。</summary>
        public double firstFinishServer = -1.0;
        /// <summary>現在の状態に入ったサーバー時刻（COUNTDOWN / 結果表示のタイマー）。</summary>
        [UdonSynced] public double stateStartServer;
        /// <summary>COUNTDOWN に入るたびに +1。クライアントはこれでステージ開始処理を 1 回だけ行う。</summary>
        [UdonSynced] public int stageSeq;
        [UdonSynced] public int[] slotPlayerId = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        [UdonSynced] public int[] slotMount = new int[MAX_SLOTS];
        [UdonSynced] public int[] slotCoat = new int[MAX_SLOTS];
        [UdonSynced] public int[] slotAbility = new int[MAX_SLOTS];
        [UdonSynced] public int[] slotTotalPoints = new int[MAX_SLOTS];
        /// <summary>直近のステージの着順（1 始まり、0 = なし）。</summary>
        [UdonSynced] public int[] slotStagePlace = new int[MAX_SLOTS];
        /// <summary>現在のステージ順位（スロット番号、先頭から。空きは -1）。RaceOrder が運ぶ（同期しない、system-r2-13）。</summary>
        public int[] order = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        /// <summary>今のレースのステージ別着順。index = stage * 8 + slot（1 始まり、0 = なし）。</summary>
        [UdonSynced] public int[] stagePlace = new int[STAGE_COUNT * MAX_SLOTS];
        /// <summary>ステージ中にサインした人（次のステージから走る）。-1 = 空き。STAGE_RESULT に入るときに枠へ移す。</summary>
        [UdonSynced] public int[] pendingPlayerId = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        [UdonSynced] public int[] pendingMount = new int[MAX_SLOTS];
        [UdonSynced] public int[] pendingCoat = new int[MAX_SLOTS];
        [UdonSynced] public int[] pendingAbility = new int[MAX_SLOTS];
        /// <summary>ロビーの出走の締め切り（サーバー時刻、-1 = なし）。RaceMath.LobbyDeadline（system-r1-09）。</summary>
        [UdonSynced] public double lobbyDeadlineServer = -1.0;
        /// <summary>スロットごとのスタートのレーン（COUNTDOWN に入るときにマスターが RaceMath.GridOrder で決める。system-r1-14）。</summary>
        [UdonSynced] public int[] gridLane = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        /// <summary>出走しなかった（号砲から 20 秒、最初の CP に届かず止まっている）スロットのビット（system-r2-05）。次の STAGE_RESULT で枠を空ける。</summary>
        [UdonSynced] public int noStartMask;
        /// <summary>総合結果の後、同じ顔ぶれでロビーに戻った（もう一戦、system-r2-06）。号砲で false。</summary>
        [UdonSynced] public bool rematch;
        /// <summary>コース記録（system-r2-04）：ステージごとの最速のゴールのタイム（秒、-1 = なし）と名前。このインスタンスの間だけ。</summary>
        [UdonSynced] public float[] stageBestTime = new float[] { -1f, -1f, -1f };
        [UdonSynced] public string[] stageBestName = new string[] { "", "", "" };

        // ---- 前回のレース（総合結果に入った時点の写し。ロビーに戻っても消さない。たまり場の黒板用）----
        /// <summary>true なら last* が有効。</summary>
        [UdonSynced] public bool lastValid;
        /// <summary>前回の着順。index = stage * 8 + 行（行 = 総合順位 0..7）。0 = なし。</summary>
        [UdonSynced] public int[] lastStagePlace = new int[STAGE_COUNT * MAX_SLOTS];
        /// <summary>前回の合計ポイント（行 = 総合順位）。</summary>
        [UdonSynced] public int[] lastTotal = new int[MAX_SLOTS];
        /// <summary>前回の名前（行 = 総合順位、空文字 = 行なし）。抜けた人の id は引けないので名前で持つ。</summary>
        [UdonSynced] public string[] lastName = new string[] { "", "", "", "", "", "", "", "" };

        [Header("Mounts (slot i = mounts[i], 8 頭)")]
        public MountController[] mounts;
        [Tooltip("スタート位置。index = stage * 8 + slot（24 個）")]
        public Transform[] startPoints;
        [Tooltip("各ステージの最初の CP（index 1）")]
        public Checkpoint[] firstCheckpoints;
        [Tooltip("各ステージのゴール後の囲い。ゴール 3 秒後にここへ移す")]
        public Transform[] finishPens;
        [Tooltip("レース後に乗り物を戻す場所（slot ごと、任意）")]
        public Transform[] mountParking;
        [Tooltip("レース後にプレイヤーを戻す場所")]
        public Transform lobbySpawn;

        [Header("Hooks (Task 9/10, 型に依存しない)")]
        [Tooltip("任意。設定すると COUNTDOWN で \"BeginCountdown\" を送り、StarterFinished() を待つ（最大 6 秒）")]
        public UdonSharpBehaviour starter;
        [Tooltip("ステージ開始前（STAGE_RESULT と第 1 ステージの COUNTDOWN）に \"ResetForStage\" を送る（能力・遺物）")]
        public UdonSharpBehaviour[] stageResetTargets;
        [Tooltip("同じタイミングで \"Refill\" を送る（リボルバー）")]
        public UdonSharpBehaviour[] refillTargets;
        [Tooltip("任意（Task 10）。中継カメラ。AbilityCaster / RevolverHolder が命中の場面を知らせる先（乗り物ごとの設定がなければこれ）")]
        public SpectatorDirector spectator;

        [Tooltip("順位を運ぶ小さな同期（子の GameObject、system-r2-13）")]
        public RaceOrder raceOrder;

        [Header("Lobby / UI")]
        public EntryLedger[] ledgers;
        public RankBoard[] rankBoards;
        public PosterCard[] posterCards;
        public MountPicker[] mountPickers;
        public StartBell startBell;
        [Tooltip("首の札（補助表示）。カウントダウン・ゴール・CP の合図を出す（任意）")]
        public MinimalHud hud;
        public PracticeArea practiceArea;
        [Tooltip("新聞売り（観客の予想の当たり・はずれの音、ui-r3-16）")]
        public TownCrier crier;
        [Tooltip("予想の札（system-r2-15）：value = スロット。電報の掲示板の下")]
        public PosterCard[] pickCards;
        [Tooltip("予想の札の上の紙：自分の予想・前回の結果・通算")]
        public TextMeshPro pickStatusText;
        [Tooltip("サルーンの掲示板「あなたの戦績」（system-r3-15、ローカルの PlayerData）")]
        public TextMeshPro recordText;

        [Header("Local selection (このクライアントだけ、同期しない)")]
        public int localMount = MountMath.MOUNT_HORSE;
        public int localCoat = 0;
        public int localAbility = AbilityMath.ABILITY_DUST;

        // ---- ローカル適用済みの状態 ----
        private int appliedState = -1;
        private int appliedSeq;
        private int[] appliedSlotPlayer;
        private int localSlot = -1;
        private bool localFinishHandled;
        private bool localWasRider;
        private int lastLocalCpStage = -1;
        private int lastLocalCpIndex = -1;
        private bool started;

        // ---- マスターのみ（同期しない。マスター交代時は既定値から再開してよいもの）----
        private float nextMasterTick;
        private double nextRaceEval;
        private bool starterDone;
        /// <summary>直近の UpdateOrderMaster で、エントリー者全員がゴール済みだったか。</summary>
        private bool allFinishedMaster;
        private bool orderDirty;
        private int lastLobbyEntrants = -1;
        private bool recordDirty;

        // ---- 戦績（ローカル、system-r3-15：VRChat Persistence の PlayerData、自分の分だけ書ける）----
        public const string WINS_KEY = "hr_wins";
        public const string PODIUMS_KEY = "hr_podiums";
        public const string RACES_KEY = "hr_races";
        public const string BEST_KEY_PREFIX = "hr_best_s";
        private int recWins, recPodiums, recRaces;
        private float[] recBest;
        private bool recLoaded;
        private int recordedStageSeq = -1;
        private int recordedFinalSeq = -1;

        // ---- 予想（ローカル、system-r2-15）----
        public const string PICK_KEY = "hr_pick_total";
        private int localPickSlot = -1;
        private int localPickPlayer = -1;
        private int localPickSeq = -1;
        private int pickTotal;
        private bool pickLoaded;
        private string pickLastText = "";

        /// <summary>ロビーの手順（ui-r3-11、StepSigns が読む。ローカル）：手配書を取った・乗り物を選んだ。</summary>
        [HideInInspector] public bool localTookCard;
        [HideInInspector] public bool localChoseMount;
        private int pendingPenStage = -1;

        private void Start()
        {
            EnsureInit();
            RefreshLobbyLocal();
            ApplyLocal();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            appliedSlotPlayer = new int[MAX_SLOTS];
            for (int i = 0; i < MAX_SLOTS; i++) appliedSlotPlayer[i] = -1;
            if (mounts == null || mounts.Length < MAX_SLOTS)
                Debug.LogWarning("[HorseRace] RaceManager: mounts は 8 頭ぶん設定してください");
            if (startPoints == null || startPoints.Length < MAX_SLOTS * STAGE_COUNT)
                Debug.LogWarning("[HorseRace] RaceManager: startPoints は 24 個（stage*8+slot）設定してください");
        }

        // =====================================================================
        // 公開 API（読み取り）
        // =====================================================================

        public int SlotCount()
        {
            if (mounts == null) return 0;
            return mounts.Length < MAX_SLOTS ? mounts.Length : MAX_SLOTS;
        }

        public int FindSlot(int playerId)
        {
            if (playerId < 0) return -1;
            for (int i = 0; i < MAX_SLOTS; i++) if (slotPlayerId[i] == playerId) return i;
            return -1;
        }

        public int ActiveCount()
        {
            int n = 0;
            for (int i = 0; i < MAX_SLOTS; i++) if (slotPlayerId[i] >= 0) n++;
            return n;
        }

        public int LocalSlot()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return -1;
            return FindSlot(local.playerId);
        }

        public bool IsLocalEntered()
        {
            return LocalSlot() >= 0;
        }

        /// <summary>playerId がステージ中にサインして、次のステージを待っているか。</summary>
        public bool IsPending(int playerId)
        {
            if (playerId < 0) return false;
            for (int i = 0; i < MAX_SLOTS; i++) if (pendingPlayerId[i] == playerId) return true;
            return false;
        }

        /// <summary>エントリーしていて、レース（カウントダウン〜総合結果）の最中。</summary>
        public bool IsLocalRacing()
        {
            return state != RaceMath.STATE_LOBBY && IsLocalEntered();
        }

        public bool IsLocalMaster()
        {
            return Networking.IsOwner(gameObject);
        }

        /// <summary>現在ステージの順位（1 始まり）。エントリーしていない・順位未計算は 0。</summary>
        public int GetRankOf(int playerId)
        {
            int s = FindSlot(playerId);
            if (s < 0) return 0;
            int rank = 0;
            for (int p = 0; p < MAX_SLOTS; p++)
            {
                int o = order[p];
                if (o < 0 || slotPlayerId[o] < 0) continue;
                rank++;
                if (o == s) return rank;
            }
            return 0;
        }

        /// <summary>総合順位（合計ポイント順、同点は RaceMath.OverallAhead、1 始まり）。エントリーしていなければ 0。</summary>
        public int GetOverallRankOf(int playerId)
        {
            int s = FindSlot(playerId);
            if (s < 0) return 0;
            int rank = 1;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (i == s || slotPlayerId[i] < 0) continue;
                if (RaceMath.OverallAhead(i, s, slotTotalPoints, stagePlace, STAGE_COUNT)) rank++;
                else if (i < s && !RaceMath.OverallAhead(s, i, slotTotalPoints, stagePlace, STAGE_COUNT)) rank++;   // 完全に同じならスロット順（GetOverallOrder と合わせる）
            }
            return rank;
        }

        /// <summary>有効なスロットを総合順に（合計点 → 最後に一緒に走ったステージの着順 → 1 着の数。system-r1-12）。</summary>
        public int[] GetOverallOrder()
        {
            bool[] act = new bool[MAX_SLOTS];
            for (int i = 0; i < MAX_SLOTS; i++) act[i] = slotPlayerId[i] >= 0;
            return RaceMath.OverallOrder(slotTotalPoints, stagePlace, act, STAGE_COUNT);
        }

        /// <summary>ロビーの出走の締め切りまでの秒数（なければ -1）。</summary>
        public float GetLobbySecondsLeft()
        {
            if (state != RaceMath.STATE_LOBBY || lobbyDeadlineServer < 0.0) return -1f;
            double left = lobbyDeadlineServer - Networking.GetServerTimeInSeconds();
            return left > 0.0 ? (float)left : 0f;
        }

        /// <summary>今ローカルのプレイヤーが号砲を撃てるか（マスター、または締め切り 30 秒前からのエントリー者）。</summary>
        public bool LocalCanStart()
        {
            return RaceMath.CanRingStart(IsLocalMaster(), IsLocalEntered(), Networking.GetServerTimeInSeconds(), lobbyDeadlineServer);
        }

        /// <summary>開幕の猶予（号砲から 8 秒、能力・リボルバーを使えない）の間か。RACING 以外は false。</summary>
        public bool InStartGrace()
        {
            if (state != RaceMath.STATE_RACING) return false;
            return RaceMath.InStartGrace(Networking.GetServerTimeInSeconds(), stageStartServer);
        }

        public float StartGraceLeft()
        {
            if (state != RaceMath.STATE_RACING) return 0f;
            return RaceMath.StartGraceLeft(Networking.GetServerTimeInSeconds(), stageStartServer);
        }

        /// <summary>
        /// スロットの能力が開幕の解禁まであと何秒か（system-r2-11）：号砲から AbilityMath.OpeningReady(レーン) 秒（8〜13.25 秒、前のレーンから）。
        /// RACING 以外は 0（COUNTDOWN 中は CanCast が別に止める）。
        /// </summary>
        public float OpeningLeft(int slot)
        {
            if (state != RaceMath.STATE_RACING) return 0f;
            double left = stageStartServer + AbilityMath.OpeningReady(LaneOf(slot), MAX_SLOTS) - Networking.GetServerTimeInSeconds();
            return left > 0.0 ? (float)left : 0f;
        }

        /// <summary>号砲からの秒数（RACING 以外は -1）。</summary>
        public float SecondsSinceGun()
        {
            if (state != RaceMath.STATE_RACING) return -1f;
            return (float)(Networking.GetServerTimeInSeconds() - stageStartServer);
        }

        /// <summary>ステージの残り秒数。RACING 中は終了条件の早いほうまで、COUNTDOWN 中は 300。それ以外は -1。</summary>
        public float GetRemainingSeconds()
        {
            if (state == RaceMath.STATE_RACING)
                return RaceMath.RemainingSeconds(Networking.GetServerTimeInSeconds(), stageStartServer, firstFinishServer);
            if (state == RaceMath.STATE_COUNTDOWN) return RaceMath.STAGE_HARD_LIMIT;
            return -1f;
        }

        /// <summary>結果表示などの今の状態の残り秒数（なければ -1）。</summary>
        public float GetStateRemainingSeconds()
        {
            double elapsed = Networking.GetServerTimeInSeconds() - stateStartServer;
            float total = -1f;
            if (state == RaceMath.STATE_STAGE_RESULT) total = STAGE_RESULT_SECONDS;
            else if (state == RaceMath.STATE_FINAL_RESULT) total = FINAL_RESULT_SECONDS;
            else if (state == RaceMath.STATE_COUNTDOWN) total = COUNTDOWN_SECONDS;
            if (total < 0f) return -1f;
            float left = total - (float)elapsed;
            return left > 0f ? left : 0f;
        }

        /// <summary>このクライアントのプレイヤーが今使う能力。レース中はエントリーした能力、それ以外（練習場など）は手配書で選んだもの。</summary>
        public int LocalActiveAbility()
        {
            int s = LocalSlot();
            if (s >= 0 && state != RaceMath.STATE_LOBBY) return slotAbility[s];
            return localAbility;
        }

        /// <summary>自分の乗り物（エントリーしていなければ null）。</summary>
        public MountController LocalMount()
        {
            int s = LocalSlot();
            if (s < 0 || s >= SlotCount()) return null;
            return mounts[s];
        }

        /// <summary>Checkpoint 側からの任意の通知（ローカル）。進行の判定は MountController が持つので表示更新だけ。</summary>
        public void LocalReachedCheckpoint(int cpStage, int index)
        {
            lastLocalCpStage = cpStage;
            lastLocalCpIndex = index;
            RefreshDisplays();
        }

        /// <summary>RaceOrder が順位を受け取った（マスター以外）。同期しないフィールドへ写して表示を更新する。</summary>
        public void OnOrderReceived()
        {
            if (raceOrder == null || Networking.IsOwner(gameObject)) return;
            for (int p = 0; p < MAX_SLOTS && p < raceOrder.order.Length; p++) order[p] = raceOrder.order[p];
            firstFinishServer = raceOrder.firstFinishServer;
            RefreshDisplays();
        }

        /// <summary>マスター：順位を RaceOrder で送る（変わっていなければ送らない）。</summary>
        private void PushOrderMaster()
        {
            if (raceOrder != null) raceOrder.Push(order, firstFinishServer, stageSeq);
        }

        /// <summary>出走しなかったスロットか（system-r2-05、台帳に「（不在）」）。</summary>
        public bool IsNoStart(int slot)
        {
            if (slot < 0 || slot >= MAX_SLOTS) return false;
            return (noStartMask & (1 << slot)) != 0;
        }

        /// <summary>ステージ st のコース記録の 1 行（「記録 3:21.4 名前」、なければ空）。</summary>
        public string CourseRecordLine(int st)
        {
            if (stageBestTime == null || st < 0 || st >= stageBestTime.Length || stageBestTime[st] < 0f) return "";
            return StageLabel(st) + " 記録 " + HudMath.FormatClockTenths(stageBestTime[st]) + " " + HudMath.DisplayName(stageBestName[st], HudMath.SHORT_NAME_MAX);
        }

        // =====================================================================
        // ローカル選択（手配書・柵の馬・台帳・鐘から呼ぶ）
        // =====================================================================

        public void SelectMount(int mount)
        {
            if (mount != MountMath.MOUNT_HORSE && mount != MountMath.MOUNT_CAMEL) return;
            localMount = mount;
            localTookCard = true;
            localChoseMount = true;   // 乗り物の手配書（馬・ラクダ）を取った＝乗り物も選んだ
            RefreshLobbyLocal();
            ResignBetweenStages();
        }

        /// <summary>柵の馬を選ぶ＝乗り物は馬になる。coat 0..5、6 = 骨の馬。</summary>
        public void SelectCoat(int coat)
        {
            if (coat < 0 || coat > MountVisual.COAT_SKELETON) return;
            localCoat = coat;
            localMount = MountMath.MOUNT_HORSE;
            localChoseMount = true;
            RefreshLobbyLocal();
            ResignBetweenStages();
        }

        public void SelectAbility(int ability)
        {
            if (!AbilityMath.IsImplemented(ability)) return;
            localAbility = ability;
            localTookCard = true;
            RefreshLobbyLocal();
            ResignBetweenStages();
        }

        /// <summary>ステージの合間に囲いの手配書を取ったら、そのまま次のステージの選択として送る（台帳にサインし直さなくてよい）。</summary>
        private void ResignBetweenStages()
        {
            if (state == RaceMath.STATE_STAGE_RESULT && stage + 1 < STAGE_COUNT && IsLocalEntered()) SignLedger();
        }

        /// <summary>台帳にサイン：今の選択でマスターにエントリーを依頼する。</summary>
        public void SignLedger()
        {
            int coat = localMount == MountMath.MOUNT_CAMEL ? 0 : localCoat;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestEntry), localMount, coat, localAbility);
        }

        /// <summary>台帳の取り消し（system-r2-05）：エントリー（またはステージ中の預かり）をやめる。</summary>
        public void WithdrawLedger()
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestWithdraw));
        }

        /// <summary>号砲を撃つ：マスター、または締め切り 30 秒前からのエントリー者が開始を依頼する。</summary>
        public void RingBell()
        {
            if (!LocalCanStart()) return;
            if (Networking.IsOwner(gameObject)) RequestStart();
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStart));
        }

        // =====================================================================
        // 依頼（マスターで実行・検証）
        // =====================================================================

        [NetworkCallable]
        public void RequestEntry(int mount, int coat, int ability)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = GetCaller();
            if (caller == null || !caller.IsValid()) return;
            if (mount != MountMath.MOUNT_HORSE && mount != MountMath.MOUNT_CAMEL) return;
            if (!AbilityMath.IsImplemented(ability)) return;
            if (mount == MountMath.MOUNT_CAMEL) coat = 0;
            else if (coat < 0 || coat > MountVisual.COAT_SKELETON) return;

            int s = FindSlot(caller.playerId);
            if (s >= 0)
            {
                // 乗り物・毛色・能力を変えられるのはロビーとステージの合間（system-r1-08：地形に合わせて乗り換える）。
                // 合間に変えたものは次の StageBeginLocal の AssignRider で反映される。カウントダウン・レース中は変えない
                if (state != RaceMath.STATE_LOBBY && state != RaceMath.STATE_STAGE_RESULT) return;
            }
            else
            {
                // LOBBY / 次がある STAGE_RESULT はすぐ枠へ。ステージ中は預かって、STAGE_RESULT に入るときに枠へ。最終ステージ中・後は受け付けない
                int mode = RaceMath.EntryMode(state, stage, STAGE_COUNT);
                if (mode == RaceMath.ENTRY_REJECT) return;
                if (mode == RaceMath.ENTRY_QUEUE)
                {
                    int q = RaceMath.PendingIndexFor(pendingPlayerId, caller.playerId);
                    if (q < 0) return;
                    pendingPlayerId[q] = caller.playerId;
                    pendingMount[q] = mount;
                    pendingCoat[q] = coat;
                    pendingAbility[q] = ability;
                    RequestSerialization();
                    ApplyLocal();
                    return;
                }
                RemovePending(caller.playerId);
                s = FindFreeSlot();
                if (s < 0) return; // 満員
                ClearSlot(s);
                slotPlayerId[s] = caller.playerId; // 途中参加は 0 点から（ClearSlot 済み）
            }
            slotMount[s] = mount;
            slotCoat[s] = coat;
            slotAbility[s] = ability;
            RequestSerialization();
            ApplyLocal();
        }

        /// <summary>
        /// エントリーの取り消し（system-r2-05）。ロビーとステージの合間だけ枠を空ける（レース中は預かりの取り消しだけ）。
        /// 観る側に回る・席を外す人が、出走時計や「全員乗ったら 15 秒」を止めないように。
        /// </summary>
        [NetworkCallable]
        public void RequestWithdraw()
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = GetCaller();
            if (caller == null || !caller.IsValid()) return;
            bool changed = IsPending(caller.playerId);
            RemovePending(caller.playerId);
            if (state == RaceMath.STATE_LOBBY || state == RaceMath.STATE_STAGE_RESULT)
            {
                int s = FindSlot(caller.playerId);
                if (s >= 0)
                {
                    ClearSlot(s);
                    changed = true;
                }
            }
            if (!changed) return;
            RequestSerialization();
            ApplyLocal();
        }

        [NetworkCallable]
        public void RequestStart()
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = GetCaller();
            if (caller == null || !caller.IsValid()) return;
            if (state != RaceMath.STATE_LOBBY) return;
            if (ActiveCount() == 0) return;
            // マスター、または締め切りまで 30 秒未満のときのエントリー者
            double now = Networking.GetServerTimeInSeconds();
            if (!RaceMath.CanRingStart(Networking.IsOwner(caller, gameObject), FindSlot(caller.playerId) >= 0, now, lobbyDeadlineServer)) return;
            StartRaceMaster(now);
            RequestSerialization();
            ApplyLocal();
        }

        /// <summary>ロビーからレースを始める（号砲・出走時計の締め切り）。</summary>
        private void StartRaceMaster(double now)
        {
            stage = 0;
            lobbyDeadlineServer = -1.0;
            rematch = false;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                slotTotalPoints[i] = 0;
                slotStagePlace[i] = 0;
            }
            for (int k = 0; k < stagePlace.Length; k++) stagePlace[k] = 0;
            BeginCountdownMaster(now);
        }

        /// <summary>Starter（Task 10）が号砲を撃ったら呼ぶ。マスター以外では何もしない。</summary>
        public void StarterFinished()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (state == RaceMath.STATE_COUNTDOWN) starterDone = true;
        }

        private VRCPlayerApi GetCaller()
        {
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null && !NetworkCalling.InNetworkCall) caller = Networking.LocalPlayer; // 直接呼ばれた（オーナー自身）
            return caller;
        }

        private void RemovePending(int playerId)
        {
            for (int i = 0; i < MAX_SLOTS; i++) if (pendingPlayerId[i] == playerId) pendingPlayerId[i] = -1;
        }

        private void ClearPending()
        {
            for (int i = 0; i < MAX_SLOTS; i++) pendingPlayerId[i] = -1;
        }

        /// <summary>預かったサインを空き枠へ（0 点から）。次のステージがあるときだけ。満員なら次の合間まで待つ。</summary>
        private bool PromotePendingMaster()
        {
            if (stage + 1 >= STAGE_COUNT) return false;
            bool changed = false;
            for (int q = 0; q < MAX_SLOTS; q++)
            {
                int pid = pendingPlayerId[q];
                if (pid < 0) continue;
                if (FindSlot(pid) >= 0) { pendingPlayerId[q] = -1; changed = true; continue; }
                int s = FindFreeSlot();
                if (s < 0) break;
                ClearSlot(s);
                slotPlayerId[s] = pid;
                slotMount[s] = pendingMount[q];
                slotCoat[s] = pendingCoat[q];
                slotAbility[s] = pendingAbility[q];
                pendingPlayerId[q] = -1;
                changed = true;
            }
            return changed;
        }

        private int FindFreeSlot()
        {
            int n = SlotCount();
            for (int i = 0; i < n; i++)
            {
                if (mounts[i] == null) continue;
                if (slotPlayerId[i] < 0) return i;
            }
            return -1;
        }

        // =====================================================================
        // マスターの進行
        // =====================================================================

        private void Update()
        {
            if (!started) EnsureInit();
            UpdateLocalFinisher();
            if (!Networking.IsOwner(gameObject)) return;
            if (Time.time < nextMasterTick) return;
            nextMasterTick = Time.time + MASTER_TICK;
            MasterTick();
        }

        private void MasterTick()
        {
            double now = Networking.GetServerTimeInSeconds();
            bool changed = PruneLeftPlayers();
            ParkOrphanMountsMaster();
            double elapsed = now - stateStartServer;

            if (state == RaceMath.STATE_COUNTDOWN)
            {
                bool done;
                if (starter == null) done = elapsed >= COUNTDOWN_SECONDS;
                else done = starterDone || elapsed >= COUNTDOWN_MAX_SECONDS;
                if (done)
                {
                    state = RaceMath.STATE_RACING;
                    stageStartServer = now;
                    stateStartServer = now;
                    firstFinishServer = -1.0;
                    orderDirty = true;
                    nextRaceEval = now + RACE_EVAL_INTERVAL;
                    changed = true;
                }
            }
            else if (state == RaceMath.STATE_RACING)
            {
                if (ActiveCount() == 0)
                {
                    GoLobbyMaster(now, false);
                    changed = true;
                }
                else if (now >= nextRaceEval)
                {
                    nextRaceEval = now + RACE_EVAL_INTERVAL;
                    // 順位は RaceOrder だけで送る（system-r2-13）。RaceManager はコース記録・不在の印が変わったときだけ
                    if (UpdateOrderMaster()) orderDirty = true;
                    if (recordDirty) { recordDirty = false; changed = true; }
                    if (MarkNoStartsMaster(now)) changed = true;
                    if (RaceMath.ShouldEndStage(now, stageStartServer, firstFinishServer) || allFinishedMaster)
                    {
                        EndStageMaster(now);
                        changed = true;
                    }
                }
            }
            else if (state == RaceMath.STATE_STAGE_RESULT)
            {
                if (ActiveCount() == 0)
                {
                    GoLobbyMaster(now, false);
                    changed = true;
                }
                else if (PromotePendingMaster())
                {
                    // 結果表示中に届いた・マスター交代で残っていた預かりを枠へ
                    changed = true;
                }
                else if (elapsed >= STAGE_RESULT_SECONDS)
                {
                    if (stage + 1 < STAGE_COUNT)
                    {
                        stage++;
                        BeginCountdownMaster(now);
                    }
                    else
                    {
                        state = RaceMath.STATE_FINAL_RESULT;
                        stateStartServer = now;
                        ClearPending();
                        SnapshotLastRaceMaster();
                    }
                    changed = true;
                }
            }
            else if (state == RaceMath.STATE_FINAL_RESULT)
            {
                if (elapsed >= FINAL_RESULT_SECONDS)
                {
                    GoLobbyMaster(now, true);   // もう一戦（system-r2-06）
                    changed = true;
                }
            }
            else if (state == RaceMath.STATE_LOBBY)
            {
                // 出走時計（system-r1-09）：2 人以上で 60 秒、全員がスタートで待っている・ワールドの全員がエントリーで 15 秒。締め切りで自動で出走。
                // ひとりなら 30 秒（乗って待てば 10 秒）でタイムアタック（system-r2-04）
                int entrants = ActiveCount();
                int prev = lastLobbyEntrants;
                lastLobbyEntrants = entrants;
                if (rematch) prev = entrants;   // もう一戦の 30 秒は決め直さない
                double d = RaceMath.LobbyDeadline(now, entrants, CountReadyMaster(), VRCPlayerApi.GetPlayerCount(), lobbyDeadlineServer, prev);
                if (d != lobbyDeadlineServer)
                {
                    lobbyDeadlineServer = d;
                    changed = true;
                }
                if (lobbyDeadlineServer >= 0.0 && now >= lobbyDeadlineServer && entrants > 0)
                {
                    StartRaceMaster(now);
                    changed = true;
                }
            }

            if (changed)
            {
                RequestSerialization();
                ApplyLocal();
            }
            if (orderDirty)
            {
                orderDirty = false;
                PushOrderMaster();
                if (!changed) RefreshDisplays();
            }
        }

        /// <summary>号砲から 20 秒、最初の CP に届かず止まっている人に「不在」の印（system-r2-05）。印が増えたら true。</summary>
        private bool MarkNoStartsMaster(double now)
        {
            double since = now - stageStartServer;
            if (since < RaceMath.NO_START_SECONDS) return false;
            bool changed = false;
            int n = SlotCount();
            for (int i = 0; i < n; i++)
            {
                if (slotPlayerId[i] < 0 || mounts[i] == null || IsNoStart(i)) continue;
                MountController m = mounts[i];
                if (m.syncFinishAt >= 0.0 && m.syncFinishAt >= stageStartServer) continue;
                if (!RaceMath.IsNoStart(m.syncCp, m.syncSpeed, since)) continue;
                noStartMask |= 1 << i;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// スタートで待っている人の数：自分の乗り物に座っていて（プレイヤーが乗り物から 2.5m 以内）、乗り物が待機場所から 8m 以内。
        /// 町を乗り回している人は数えない（「スタート台まで集まる」）。
        /// </summary>
        private int CountReadyMaster()
        {
            int n = SlotCount();
            int ready = 0;
            for (int i = 0; i < n; i++)
            {
                int pid = slotPlayerId[i];
                MountController m = mounts[i];
                if (pid < 0 || m == null || m.riderPlayerId != pid) continue;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                if (p == null || !p.IsValid()) continue;
                if (Vector3.Distance(p.GetPosition(), m.transform.position) > 2.5f) continue;
                if (mountParking != null && i < mountParking.Length && mountParking[i] != null
                    && Vector3.Distance(m.transform.position, mountParking[i].position) > 8f) continue;
                ready++;
            }
            return ready;
        }

        private void BeginCountdownMaster(double now)
        {
            state = RaceMath.STATE_COUNTDOWN;
            stateStartServer = now;
            stageStartServer = now;
            firstFinishServer = -1.0;
            stageSeq++;
            // スタートの並び：第 1 ステージは混ぜる、第 2・3 は総合の逆順（首位がいちばん外）
            int[] lanes = RaceMath.GridOrder(GetOverallOrder(), stage, stageSeq * 7919 + (int)(now % 10000.0), MAX_SLOTS);
            for (int i = 0; i < MAX_SLOTS; i++) gridLane[i] = i < lanes.Length ? lanes[i] : i;
            starterDone = false;
            allFinishedMaster = false;
            noStartMask = 0;
            for (int p = 0; p < MAX_SLOTS; p++) order[p] = -1;
            orderDirty = true;
        }

        /// <summary>同期された乗り物の進行から順位と最初のゴール時刻を更新する。変化があれば true。</summary>
        private bool UpdateOrderMaster()
        {
            int n = SlotCount();
            int[] cp = new int[MAX_SLOTS];
            float[] dist = new float[MAX_SLOTS];
            double[] fin = new double[MAX_SLOTS];
            bool[] act = new bool[MAX_SLOTS];
            double first = -1.0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                fin[i] = -1.0;
                if (i >= n || slotPlayerId[i] < 0 || mounts[i] == null) continue;
                MountController m = mounts[i];
                act[i] = true;
                cp[i] = m.syncCp;
                dist[i] = m.syncDistToCp;
                double f = m.syncFinishAt;
                // 前のステージのゴール時刻（リセットの同期待ち）は無視する
                if (f >= 0.0 && f >= stageStartServer)
                {
                    fin[i] = f;
                    if (first < 0.0 || f < first) first = f;
                }
            }
            int[] o = RaceMath.ComputeOrder(cp, dist, fin, act);
            // 出走しなかった人（不在）はゴールを待たない（system-r2-05）
            bool[] racing = new bool[MAX_SLOTS];
            for (int i = 0; i < MAX_SLOTS; i++) racing[i] = act[i] && !IsNoStart(i);
            allFinishedMaster = RaceMath.AllFinished(fin, racing);
            // コース記録（system-r2-04）：ゴールしたタイムがステージの最速なら書き換える
            if (stage >= 0 && stage < stageBestTime.Length)
            {
                for (int i = 0; i < MAX_SLOTS; i++)
                {
                    if (fin[i] < 0.0) continue;
                    float t = (float)(fin[i] - stageStartServer);
                    if (t <= 0f) continue;
                    if (stageBestTime[stage] >= 0f && t >= stageBestTime[stage]) continue;
                    stageBestTime[stage] = t;
                    stageBestName[stage] = PlayerName(slotPlayerId[i]);
                    recordDirty = true;
                }
            }
            bool changed = first != firstFinishServer;
            firstFinishServer = first;
            for (int p = 0; p < MAX_SLOTS; p++)
            {
                int v = p < o.Length ? o[p] : -1;
                if (order[p] != v) changed = true;
                order[p] = v;
            }
            return changed;
        }

        private void EndStageMaster(double now)
        {
            // 出走しなかった人（不在）は点をもらわずに枠を空ける（system-r2-05）
            for (int i = 0; i < MAX_SLOTS; i++) if (IsNoStart(i)) ClearSlot(i);
            noStartMask = 0;
            UpdateOrderMaster();
            orderDirty = true;
            for (int i = 0; i < MAX_SLOTS; i++) slotStagePlace[i] = 0;
            int place = 0;
            for (int p = 0; p < MAX_SLOTS; p++)
            {
                int s = order[p];
                if (s < 0 || slotPlayerId[s] < 0) continue;
                slotTotalPoints[s] += RaceMath.PointsForPlace(place, stage);   // 最終ステージは 1.5 倍
                slotStagePlace[s] = place + 1;
                int idx = stage * MAX_SLOTS + s;
                if (idx >= 0 && idx < stagePlace.Length) stagePlace[idx] = place + 1;
                place++;
            }
            state = RaceMath.STATE_STAGE_RESULT;
            stateStartServer = now;
            allFinishedMaster = false;
            // ステージ中にサインした人を次のステージの枠へ（0 点から）
            PromotePendingMaster();
        }

        /// <summary>総合結果に入るときに、ロビーに戻っても残す前回の結果を写す（総合順位の順に並べる）。</summary>
        private void SnapshotLastRaceMaster()
        {
            int[] overall = GetOverallOrder();
            for (int row = 0; row < MAX_SLOTS; row++)
            {
                bool has = row < overall.Length;
                int s = has ? overall[row] : -1;
                lastName[row] = has ? PlayerName(slotPlayerId[s]) : "";
                lastTotal[row] = has ? slotTotalPoints[s] : 0;
                for (int st = 0; st < STAGE_COUNT; st++)
                    lastStagePlace[st * MAX_SLOTS + row] = has ? stagePlace[st * MAX_SLOTS + s] : 0;
            }
            lastValid = overall.Length > 0;
        }

        /// <summary>
        /// ロビーへ。keepEntrants（総合結果の後、system-r2-06）なら、まだいる人の枠・乗り物・毛色・能力を残し、点・着順・順位だけ消して
        /// 30 秒後（RaceMath.RematchDeadline）に次のレース（もう一戦）。台帳で取り消せる・新しい人もサインできる。
        /// </summary>
        private void GoLobbyMaster(double now, bool keepEntrants)
        {
            state = RaceMath.STATE_LOBBY;
            stage = 0;
            stateStartServer = now;
            firstFinishServer = -1.0;
            noStartMask = 0;
            int kept = 0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                int pid = slotPlayerId[i];
                VRCPlayerApi p = pid >= 0 ? VRCPlayerApi.GetPlayerById(pid) : null;
                if (keepEntrants && p != null && p.IsValid())
                {
                    slotTotalPoints[i] = 0;
                    slotStagePlace[i] = 0;
                    for (int st = 0; st < STAGE_COUNT; st++) stagePlace[st * MAX_SLOTS + i] = 0;
                    kept++;
                }
                else ClearSlot(i);
            }
            for (int p = 0; p < MAX_SLOTS; p++) order[p] = -1;
            orderDirty = true;
            ClearPending();
            rematch = kept > 0;
            lobbyDeadlineServer = rematch ? RaceMath.RematchDeadline(now) : -1.0;
        }

        private void ClearSlot(int i)
        {
            slotPlayerId[i] = -1;
            slotMount[i] = 0;
            slotCoat[i] = 0;
            slotAbility[i] = 0;
            slotTotalPoints[i] = 0;
            slotStagePlace[i] = 0;
            noStartMask &= ~(1 << i);
            // 途中離脱はその時点で集計から外す／空いた枠を次の人が使うときに前の人の着順を持ち越さない
            for (int st = 0; st < STAGE_COUNT; st++) stagePlace[st * MAX_SLOTS + i] = 0;
        }

        /// <summary>
        /// 抜けた人の乗り物は、枠が空いた後で所有権がマスターに来るので ReleaseMountLocal では片付かない。
        /// 空き枠で誰も乗っていない、自分が持っている乗り物が置き場から離れていたら置き場へ戻す。
        /// </summary>
        private void ParkOrphanMountsMaster()
        {
            if (mountParking == null) return;
            int n = SlotCount();
            for (int i = 0; i < n; i++)
            {
                if (slotPlayerId[i] >= 0) continue;
                MountController m = mounts[i];
                if (m == null || m.riderPlayerId >= 0) continue;
                if (i >= mountParking.Length || mountParking[i] == null) continue;
                if (!Networking.IsOwner(m.gameObject)) continue;
                Transform park = mountParking[i];
                if (Vector3.Distance(m.transform.position, park.position) < 2f) continue;
                m.TeleportTo(park.position, park.eulerAngles.y);
            }
        }

        /// <summary>もういないプレイヤーの枠を空ける（OnPlayerLeft の取りこぼし・マスター交代の保険）。</summary>
        private bool PruneLeftPlayers()
        {
            bool changed = false;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                int pid = slotPlayerId[i];
                if (pid < 0) continue;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                if (p == null || !p.IsValid())
                {
                    ClearSlot(i);
                    changed = true;
                }
            }
            for (int q = 0; q < MAX_SLOTS; q++)
            {
                int pid = pendingPlayerId[q];
                if (pid < 0) continue;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                if (p == null || !p.IsValid())
                {
                    pendingPlayerId[q] = -1;
                    changed = true;
                }
            }
            return changed;
        }

        // =====================================================================
        // ネットワーク・プレイヤーイベント
        // =====================================================================

        public override void OnDeserialization()
        {
            ApplyLocal();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player != null && player.isLocal)
            {
                // 同期済みの状態からそのまま続ける（リセットしない）。タイマーは stateStartServer / stageStartServer が持っている。
                Debug.Log("[HorseRace] RaceManager: マスターを引き継ぎました state=" + state + " stage=" + stage);
                nextMasterTick = 0f;
                nextRaceEval = 0.0;
                starterDone = false;
                if (raceOrder != null && !Networking.IsOwner(raceOrder.gameObject)) Networking.SetOwner(player, raceOrder.gameObject);
                RequestSerialization();
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            ApplyVoice(player);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null) return;
            if (!Networking.IsOwner(gameObject)) return; // オーナーが空けて配る（マスター自身が抜けたときは新マスターの PruneLeftPlayers）
            bool wasPending = IsPending(player.playerId);
            RemovePending(player.playerId);
            int s = FindSlot(player.playerId);
            if (s < 0)
            {
                if (wasPending) { RequestSerialization(); ApplyLocal(); }
                return;
            }
            ClearSlot(s);
            RequestSerialization();
            ApplyLocal();
        }

        // =====================================================================
        // ローカル適用（全クライアント。オーナーは変更直後に自分で呼ぶ）
        // =====================================================================

        private void ApplyLocal()
        {
            EnsureInit();
            VRCPlayerApi local = Networking.LocalPlayer;
            int localId = local != null ? local.playerId : -1;
            int n = SlotCount();

            // 抜けた・空いた枠の乗り物を手放す
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                int was = appliedSlotPlayer[i];
                int now = slotPlayerId[i];
                if (was == now) continue;
                appliedSlotPlayer[i] = now;
                if (was >= 0 && i < n && mounts[i] != null) ReleaseMountLocal(i);
            }
            localSlot = FindSlot(localId);

            if (stageSeq != appliedSeq)
            {
                appliedSeq = stageSeq;
                if (state == RaceMath.STATE_COUNTDOWN || state == RaceMath.STATE_RACING) StageBeginLocal(localId);
            }

            if (state != appliedState)
            {
                int prev = appliedState;
                appliedState = state;
                StateEnterLocal(prev);
            }

            if (state == RaceMath.STATE_LOBBY) LobbySeatLocal(localId);

            RefreshDisplays();
        }

        private void ReleaseMountLocal(int i)
        {
            MountController m = mounts[i];
            m.SetRaceActive(false);
            m.InputLocked = false;
            if (Networking.IsOwner(m.gameObject) && mountParking != null && i < mountParking.Length && mountParking[i] != null)
                m.TeleportTo(mountParking[i].position, mountParking[i].eulerAngles.y);
            m.AssignRider(-1, slotMount[i], slotCoat[i]);
        }

        /// <summary>
        /// ロビーで台帳にサインした人は、自分の乗り物（置き場）に座って待つ（立って待たない）。
        /// 新しく枠に入った・乗り物や毛色を変えた（サインし直し）ときだけ座らせる。ロビーでは Jump で自由に降りられ、
        /// 降りた後は自分の乗り物を Interact でまた座れる（MountController.Interact）。カウントダウンでは StageBeginLocal が必ず座らせる。
        /// ロビーでは乗ったまま町の中を歩ける（InputLocked=false、レース外なので能力・降車禁止なし）。
        /// </summary>
        private void LobbySeatLocal(int localId)
        {
            int n = SlotCount();
            for (int i = 0; i < n; i++)
            {
                MountController m = mounts[i];
                int pid = slotPlayerId[i];
                if (m == null || pid < 0) continue;
                if (pid != localId)
                {
                    if (m.riderPlayerId != pid)
                    {
                        m.SetRaceActive(false);
                        m.AssignRider(pid, slotMount[i], slotCoat[i]);
                    }
                    continue;
                }
                bool fresh = m.riderPlayerId != pid;
                bool changed = Networking.IsOwner(m.gameObject) && (m.syncMount != slotMount[i] || m.syncCoat != slotCoat[i]);
                if (!fresh && !changed) continue;
                if (practiceArea != null) practiceArea.ReleaseLocal();
                m.SetRaceActive(false);
                m.InputLocked = false;
                m.AssignRider(pid, slotMount[i], slotCoat[i]);   // 所有権を取って座る
                if (fresh && mountParking != null && i < mountParking.Length && mountParking[i] != null)
                    m.TeleportTo(mountParking[i].position, mountParking[i].eulerAngles.y);
                // 練習用の馬から降りた同じフレームの UseStation は無視されることがあるので少し後でもう一度
                SendCustomEventDelayedFrames(nameof(_ReseatLobbyLocal), 3);
            }
        }

        public void _ReseatLobbyLocal()
        {
            if (state != RaceMath.STATE_LOBBY) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            int s = FindSlot(local.playerId);
            if (s < 0 || s >= SlotCount() || mounts[s] == null) return;
            MountController m = mounts[s];
            if (m.IsLocalRider()) return;
            if (practiceArea != null && practiceArea.IsRidingPracticeHorse()) return;
            m.AssignRider(local.playerId, slotMount[s], slotCoat[s]);
        }

        /// <summary>練習用の馬に乗る前に、ロビーで座っている自分の乗り物から降りる（Station から Station へは乗り換えられない）。</summary>
        public void LeaveLobbyMountLocal()
        {
            if (state != RaceMath.STATE_LOBBY) return;
            MountController m = LocalMount();
            if (m != null && m.IsLocalRider()) m.ExitLocal();
        }

        /// <summary>ステージ開始（COUNTDOWN 突入）：全員の乗り物を割り当て直し、自分の乗り物だけ着席・スタート位置・進行リセット。</summary>
        private void StageBeginLocal(int localId)
        {
            int n = SlotCount();
            for (int i = 0; i < n; i++)
            {
                MountController m = mounts[i];
                if (m == null) continue;
                m.currentStage = stage;
                int pid = slotPlayerId[i];
                if (pid < 0)
                {
                    if (m.riderPlayerId >= 0) m.AssignRider(-1, 0, 0);
                    m.SetRaceActive(false);
                    continue;
                }
                if (pid == localId)
                {
                    if (practiceArea != null) practiceArea.ReleaseLocal();
                    // 降りていた人も含めて座らせ直す（AssignRider は所有権を取る）
                    m.AssignRider(pid, slotMount[i], slotCoat[i]);
                    Transform sp = GetStartPoint(stage, LaneOf(i));
                    Vector3 pos = sp != null ? sp.position : m.transform.position;
                    float yaw = sp != null ? sp.eulerAngles.y : m.transform.eulerAngles.y;
                    m.TeleportTo(pos, yaw);
                    m.ResetProgress(pos, yaw, GetFirstCheckpoint(stage));
                    m.ArmStart();   // 号砲の前後の前進でスタートダッシュ（system-r1-15）
                    localFinishHandled = false;
                    localWasRider = true;
                    // 練習用の馬から降りた同じフレームの UseStation は無視されることがあるので少し後でもう一度
                    SendCustomEventDelayedFrames(nameof(_ReseatLocal), 3);
                }
                else
                {
                    m.AssignRider(pid, slotMount[i], slotCoat[i]);
                }
                m.SetRaceActive(true);
                m.InputLocked = true;
            }
            // 毎ステージの COUNTDOWN（STAGE_RESULT の同期を取りこぼして RACING → 次の COUNTDOWN に飛んだクライアントでも Lv・弾を戻す）。
            // 対象はどれもレース外では何度呼ばれてもよいので、STAGE_RESULT で既に呼んでいても問題ない。
            // RACING から始まったクライアント（COUNTDOWN も取りこぼし）は第 1 ステージだけ（拾われた遺物を戻さないように）
            if (state == RaceMath.STATE_COUNTDOWN || stage == 0) InvokeStageResetHooks();
            if (state == RaceMath.STATE_COUNTDOWN)
            {
                if (stage == 0 && startBell != null) startBell.PlayRing();
                if (starter != null) starter.SendCustomEvent("BeginCountdown");
            }
        }

        /// <summary>COUNTDOWN か RACING（COUNTDOWN の同期を取りこぼしたクライアント）で、まだ座れていなければ座らせ直す。</summary>
        public void _ReseatLocal()
        {
            if (state != RaceMath.STATE_COUNTDOWN && state != RaceMath.STATE_RACING) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            int s = FindSlot(local.playerId);
            if (s < 0 || s >= SlotCount() || mounts[s] == null) return;
            MountController m = mounts[s];
            if (m.IsLocalRider()) return;
            m.AssignRider(local.playerId, slotMount[s], slotCoat[s]);
            m.SetRaceActive(true);
            m.InputLocked = state != RaceMath.STATE_RACING;
        }

        private void StateEnterLocal(int prev)
        {
            MountController lm = (localSlot >= 0 && localSlot < SlotCount()) ? mounts[localSlot] : null;
            if (state == RaceMath.STATE_RACING)
            {
                if (lm != null)
                {
                    lm.SetRaceActive(true);
                    lm.InputLocked = false;
                    lm.BeginRace(stageStartServer);
                }
                ApplyVoiceAll();
            }
            else if (state == RaceMath.STATE_STAGE_RESULT)
            {
                if (lm != null)
                {
                    lm.SetRaceActive(false);
                    // 合間は囲いの中を歩ける：囲いの手配書で次のステージの乗り物・能力を選び直す（system-r1-08）。
                    // まだゴールしていなかった人も囲いへ（手配書・中継の板に届くように）
                    if (Networking.IsOwner(lm.gameObject) && !localFinishHandled)
                    {
                        localFinishHandled = true;
                        bool timedOut = prev == RaceMath.STATE_RACING && lm.syncFinishAt < stageStartServer;
                        if (timedOut)
                        {
                            // 時間切れ（ui-r3-03）：その場で赤い判と空砲、1.2 秒読ませてから囲いへ（黙って移すとバグに見える）
                            if (hud != null) hud.ShowTimeUp(stageSeq);
                            if (starter != null) starter.SendCustomEvent("PlayTimeUp");
                            pendingPenStage = stage;
                            SendCustomEventDelayedSeconds(nameof(_PenTeleport), 1.2f);
                        }
                        else
                        {
                            Transform pen = (finishPens != null && stage < finishPens.Length) ? finishPens[stage] : null;
                            if (pen != null) lm.TeleportTo(pen.position, pen.eulerAngles.y);
                        }
                    }
                    lm.InputLocked = stage + 1 >= STAGE_COUNT;
                }
                InvokeStageResetHooks();
                ScorePickLocal();
                if (lm != null && prev == RaceMath.STATE_RACING) RecordStageLocal(lm);
            }
            else if (state == RaceMath.STATE_FINAL_RESULT)
            {
                if (lm != null)
                {
                    lm.InputLocked = true;
                    lm.SetRaceActive(false);
                }
                if (localSlot >= 0 && prev >= 0 && prev != RaceMath.STATE_FINAL_RESULT) RecordFinalLocal();
            }
            else if (state == RaceMath.STATE_LOBBY)
            {
                if (prev >= 0 && prev != RaceMath.STATE_LOBBY)
                {
                    int n = SlotCount();
                    for (int i = 0; i < n; i++)
                    {
                        if (mounts[i] == null) continue;
                        mounts[i].SetRaceActive(false);
                        mounts[i].InputLocked = false;
                    }
                    if (rematch && lm != null)
                    {
                        // もう一戦（system-r2-06）：乗ったまま待機場所へ（台帳・手配書・乗り直しは要らない）
                        localWasRider = false;
                        if (Networking.IsOwner(lm.gameObject) && mountParking != null && localSlot < mountParking.Length && mountParking[localSlot] != null)
                            lm.TeleportTo(mountParking[localSlot].position, mountParking[localSlot].eulerAngles.y);
                        SendCustomEventDelayedFrames(nameof(_ReseatLobbyLocal), 3);
                    }
                    else if (localWasRider) SendCustomEventDelayedFrames(nameof(_TeleportLocalToLobby), 3);
                }
            }
        }

        /// <summary>時間切れの 1.2 秒後（ui-r3-03）：まだ同じステージの合間なら自分の乗り物を囲いへ。</summary>
        public void _PenTeleport()
        {
            int st = pendingPenStage;
            pendingPenStage = -1;
            if (st < 0 || state != RaceMath.STATE_STAGE_RESULT || stage != st) return;
            if (localSlot < 0 || localSlot >= SlotCount()) return;
            MountController m = mounts[localSlot];
            if (m == null || !Networking.IsOwner(m.gameObject)) return;
            Transform pen = (finishPens != null && stage < finishPens.Length) ? finishPens[stage] : null;
            if (pen != null) m.TeleportTo(pen.position, pen.eulerAngles.y);
        }

        /// <summary>
        /// 前の騎手までの距離（m、ui-r3-14）：順位表（order、CP と距離の並び）でひとつ前の騎手と比べる。
        /// 同じ CP を目指していれば「次の CP までの距離」の差、違えば水平の直線距離。前がいない・前がもうゴールした・自分がゴールした は -1。
        /// </summary>
        public float GapAheadMeters(int slot)
        {
            if (mounts == null || slot < 0 || slot >= SlotCount() || mounts[slot] == null) return -1f;
            int prev = -1;
            bool found = false;
            for (int p = 0; p < MAX_SLOTS; p++)
            {
                int o = order[p];
                if (o < 0 || o >= SlotCount() || slotPlayerId[o] < 0) continue;
                if (o == slot) { found = true; break; }
                prev = o;
            }
            if (!found || prev < 0) return -1f;
            MountController me = mounts[slot];
            MountController a = mounts[prev];
            if (a == null) return -1f;
            if (me.syncFinishAt >= 0.0 && me.syncFinishAt >= stageStartServer) return -1f;
            int aheadCp = a.syncCp;
            if (a.syncFinishAt >= 0.0 && a.syncFinishAt >= stageStartServer) aheadCp = -1;
            Vector3 d = a.transform.position - me.transform.position;
            d.y = 0f;
            return HudMath.GapMeters(me.syncCp, me.syncDistToCp, aheadCp, a.syncDistToCp, d.magnitude);
        }

        public void _TeleportLocalToLobby()
        {
            localWasRider = false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || lobbySpawn == null) return;
            if (state != RaceMath.STATE_LOBBY) return;
            local.TeleportTo(lobbySpawn.position, lobbySpawn.rotation);
        }

        /// <summary>ゴール 3 秒後、自分の乗り物をそのステージの囲いへ移して操作を戻す（惰性でコース外へ行き続けないように）。</summary>
        private void UpdateLocalFinisher()
        {
            if (localFinishHandled || state != RaceMath.STATE_RACING) return;
            if (localSlot < 0 || localSlot >= SlotCount()) return;
            MountController m = mounts[localSlot];
            if (m == null || !Networking.IsOwner(m.gameObject)) return;
            double fin = m.syncFinishAt;
            if (fin < 0.0) return;
            if (Networking.GetServerTimeInSeconds() - fin < FINISH_PEN_DELAY) return;
            localFinishHandled = true;
            Transform pen = (finishPens != null && stage < finishPens.Length) ? finishPens[stage] : null;
            if (pen != null) m.TeleportTo(pen.position, pen.eulerAngles.y);
            m.InputLocked = false;
        }

        private void InvokeStageResetHooks()
        {
            if (stageResetTargets != null)
            {
                for (int i = 0; i < stageResetTargets.Length; i++)
                    if (stageResetTargets[i] != null) stageResetTargets[i].SendCustomEvent("ResetForStage");
            }
            if (refillTargets != null)
            {
                for (int i = 0; i < refillTargets.Length; i++)
                    if (refillTargets[i] != null) refillTargets[i].SendCustomEvent("Refill");
            }
        }

        /// <summary>スロットのスタートのレーン（gridLane。壊れていればスロット番号）。</summary>
        public int LaneOf(int slot)
        {
            if (gridLane == null || slot < 0 || slot >= gridLane.Length) return slot;
            int l = gridLane[slot];
            return (l >= 0 && l < MAX_SLOTS) ? l : slot;
        }

        private Transform GetStartPoint(int st, int slot)
        {
            int idx = st * MAX_SLOTS + slot;
            if (startPoints == null || idx < 0 || idx >= startPoints.Length) return null;
            return startPoints[idx];
        }

        private Checkpoint GetFirstCheckpoint(int st)
        {
            if (firstCheckpoints == null || st < 0 || st >= firstCheckpoints.Length) return null;
            return firstCheckpoints[st];
        }

        // =====================================================================
        // ボイス（ローカル設定）
        // =====================================================================

        private void ApplyVoice(VRCPlayerApi p)
        {
            if (p == null || !p.IsValid()) return;
            p.SetVoiceDistanceFar(VOICE_FAR);
            p.SetVoiceDistanceNear(VOICE_NEAR);
            p.SetVoiceLowpass(false);
        }

        public void ApplyVoiceAll()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count <= 0) return;
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            players = VRCPlayerApi.GetPlayers(players);
            for (int i = 0; i < players.Length; i++) ApplyVoice(players[i]);
        }

        // =====================================================================
        // 表示
        // =====================================================================

        private void RefreshDisplays()
        {
            RefreshPicksLocal();
            if (ledgers != null)
                for (int i = 0; i < ledgers.Length; i++) if (ledgers[i] != null) ledgers[i].Refresh();
            if (rankBoards != null)
                for (int i = 0; i < rankBoards.Length; i++) if (rankBoards[i] != null) rankBoards[i].Refresh();
        }

        private void RefreshLobbyLocal()
        {
            if (posterCards != null)
            {
                for (int i = 0; i < posterCards.Length; i++)
                {
                    PosterCard c = posterCards[i];
                    if (c == null) continue;
                    bool taken = c.kind == PosterCard.KIND_MOUNT ? c.value == localMount : c.value == localAbility;
                    c.SetTaken(taken);
                }
            }
            if (mountPickers != null)
            {
                for (int i = 0; i < mountPickers.Length; i++)
                {
                    MountPicker mp = mountPickers[i];
                    if (mp == null) continue;
                    mp.SetChosen(localMount == MountMath.MOUNT_HORSE && mp.coat == localCoat);
                }
            }
            // 台帳の右のページ（あなたの選択）も書き直す
            if (ledgers != null)
                for (int i = 0; i < ledgers.Length; i++) if (ledgers[i] != null) ledgers[i].Refresh();
        }

        // ---- 文言（プレイヤーに見せるので日本語。原作の名称は使わない）----

        public static string AbilityLabel(int ability)
        {
            if (ability == AbilityMath.ABILITY_DUST) return "砂塵";
            if (ability == AbilityMath.ABILITY_LASSO) return "投げ縄";
            if (ability == AbilityMath.ABILITY_WALL) return "岩壁";
            if (ability == AbilityMath.ABILITY_BALL) return "鉄球";
            if (ability == AbilityMath.ABILITY_BOMB) return "火薬樽";
            if (ability == AbilityMath.ABILITY_MIRAGE) return "蜃気楼";
            return "？";
        }

        public static string MountKindLabel(int mount)
        {
            return mount == MountMath.MOUNT_CAMEL ? "ラクダ" : "馬";
        }

        public static string CoatLabel(int coat)
        {
            if (coat == 0) return "鹿毛";
            if (coat == 1) return "栗毛";
            if (coat == 2) return "青毛";
            if (coat == 3) return "芦毛";
            if (coat == 4) return "月毛";
            if (coat == 5) return "河原毛";
            if (coat == MountVisual.COAT_SKELETON) return "骨の馬";
            return "？";
        }

        public static string MountLabel(int mount, int coat)
        {
            if (mount == MountMath.MOUNT_CAMEL) return "ラクダ";
            if (coat == MountVisual.COAT_SKELETON) return "骨の馬";
            return "馬（" + CoatLabel(coat) + "）";
        }

        /// <summary>黒板・台帳・電報用の短い名前（HudMath.SHORT_NAME_MAX 文字まで、超えたら「…」）。</summary>
        public static string ShortName(int playerId)
        {
            return HudMath.DisplayName(PlayerName(playerId), HudMath.SHORT_NAME_MAX);   // ui-r3-15：フォントに無い字は「・」
        }

        public static string PlayerName(int playerId)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
            if (p == null || !p.IsValid()) return "（不在）";
            return p.displayName;
        }

        /// <summary>"第2幕 砂漠" のような表記（stage は 0 始まり。名前は HudMath.STAGE_NAMES = StageDefinitions の label）。</summary>
        public static string StageLabel(int st)
        {
            return HudMath.StageLabel(st);
        }

        /// <summary>ステージの色（荒野 = 赤錆、砂漠 = 砂金、境目 = 紫）。</summary>
        public static Color StageColor(int st)
        {
            return HudMath.StageColor(st);
        }

        /// <summary>順位表の進み具合の列（ui-r2-15）：スロットのゴールのタイム、まだなら CP の四角 6 つ。</summary>
        public string StandingCell(int slot)
        {
            if (mounts == null || slot < 0 || slot >= mounts.Length || mounts[slot] == null) return "";
            MountController m = mounts[slot];
            bool done = m.syncFinishAt >= 0.0 && m.syncFinishAt >= stageStartServer;
            float secs = done ? (float)(m.syncFinishAt - stageStartServer) : 0f;
            return HudMath.StandingCell(m.syncCp, CheckpointCount(stage), done, secs);
        }

        /// <summary>ステージ st の CP の数（ゴールを含む。firstCheckpoints から next をたどって数え、覚えておく）。</summary>
        public int CheckpointCount(int st)
        {
            if (firstCheckpoints == null || st < 0 || st >= firstCheckpoints.Length) return 0;
            if (cpCountCache == null) cpCountCache = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
            if (st < cpCountCache.Length && cpCountCache[st] >= 0) return cpCountCache[st];
            int n = 0;
            Checkpoint c = firstCheckpoints[st];
            while (c != null && n < 1000) { n++; c = c.next; }
            if (st < cpCountCache.Length) cpCountCache[st] = n;
            return n;
        }

        private int[] cpCountCache;

        // =====================================================================
        // 予想（system-r2-15）：観客がステージの勝者を当てる（ローカル、通算は PlayerData）
        // =====================================================================

        /// <summary>
        /// 予想の札を取った（PosterCard KIND_PICK から）。走る人は選べない。号砲の後（RACING）と総合結果の間は変えられない。
        /// ロビー・合間に選んだら次のステージ、カウントダウン中ならそのステージの予想。
        /// </summary>
        public void PickWinner(int slot)
        {
            if (slot < 0 || slot >= MAX_SLOTS || slotPlayerId[slot] < 0) return;
            if (IsLocalEntered()) { pickLastText = "走る人は予想できない"; RefreshPicksLocal(); return; }
            if (state == RaceMath.STATE_RACING || state == RaceMath.STATE_FINAL_RESULT) { pickLastText = "号砲の後は変えられない"; RefreshPicksLocal(); return; }
            if (state == RaceMath.STATE_STAGE_RESULT && stage + 1 >= STAGE_COUNT) return;
            localPickSlot = slot;
            localPickPlayer = slotPlayerId[slot];
            localPickSeq = state == RaceMath.STATE_COUNTDOWN ? stageSeq : stageSeq + 1;
            RefreshPicksLocal();
        }

        private void EnsurePickLoaded()
        {
            if (pickLoaded) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            int v;
            if (PlayerData.TryGetInt(local, PICK_KEY, out v)) { pickTotal = v; pickLoaded = true; }
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            pickLoaded = false;
            EnsurePickLoaded();
            pickLoaded = true;
            RefreshPicksLocal();
            recLoaded = false;
            EnsureRecordLoaded();
            RefreshRecordLocal();
        }

        // =====================================================================
        // 戦績（system-r3-15）：優勝・表彰台・出走の回数とステージの自己ベスト（PlayerData）。サルーンの掲示板に自分の分、黒板に勝ち頭
        // =====================================================================

        private void EnsureRecordLoaded()
        {
            if (recBest == null) recBest = new float[STAGE_COUNT];
            if (recLoaded) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            int v;
            float f;
            if (PlayerData.TryGetInt(local, WINS_KEY, out v)) recWins = v;
            if (PlayerData.TryGetInt(local, PODIUMS_KEY, out v)) recPodiums = v;
            if (PlayerData.TryGetInt(local, RACES_KEY, out v)) recRaces = v;
            for (int i = 0; i < STAGE_COUNT; i++)
                if (PlayerData.TryGetFloat(local, BEST_KEY_PREFIX + i.ToString(), out f)) recBest[i] = f;
            recLoaded = true;
        }

        /// <summary>STAGE_RESULT に入ったとき：自分がゴールしていれば、そのステージのタイムで自己ベストを更新する。</summary>
        private void RecordStageLocal(MountController lm)
        {
            if (recordedStageSeq == stageSeq) return;
            recordedStageSeq = stageSeq;
            if (lm.syncFinishAt < 0.0 || lm.syncFinishAt < stageStartServer) return;
            if (stage < 0 || stage >= STAGE_COUNT) return;
            EnsureRecordLoaded();
            float secs = (float)(lm.syncFinishAt - stageStartServer);
            if (!HudMath.IsNewBest(recBest[stage], secs)) return;
            recBest[stage] = secs;
            PlayerData.SetFloat(BEST_KEY_PREFIX + stage.ToString(), secs);
            RefreshRecordLocal();
        }

        /// <summary>FINAL_RESULT に入ったとき（走った人だけ）：出走 +1、総合 1 位なら優勝 +1、3 位までなら表彰台 +1。</summary>
        private void RecordFinalLocal()
        {
            if (recordedFinalSeq == stageSeq) return;
            recordedFinalSeq = stageSeq;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            EnsureRecordLoaded();
            int rank = GetOverallRankOf(local.playerId);
            recRaces++;
            if (rank == 1) recWins++;
            if (rank >= 1 && rank <= 3) recPodiums++;
            PlayerData.SetInt(RACES_KEY, recRaces);
            PlayerData.SetInt(WINS_KEY, recWins);
            PlayerData.SetInt(PODIUMS_KEY, recPodiums);
            RefreshRecordLocal();
        }

        private void RefreshRecordLocal()
        {
            if (recordText == null) return;
            EnsureRecordLoaded();
            if (recBest == null) return;
            recordText.text = HudMath.RecordPage(recRaces, recWins, recPodiums, recBest[0], STAGE_COUNT > 1 ? recBest[1] : 0f, STAGE_COUNT > 2 ? recBest[2] : 0f);
        }

        /// <summary>前回レースの黒板の「勝ち頭」（system-r3-15）：このインスタンスにいる人の優勝の回数（PlayerData）の上位 3 人。</summary>
        public string WinMastersText()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count <= 0) return "";
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            players = VRCPlayerApi.GetPlayers(players);
            int[] best = new int[] { 0, 0, 0 };
            string[] names = new string[] { "", "", "" };
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi p = players[i];
                if (p == null || !p.IsValid()) continue;
                int v;
                if (!PlayerData.TryGetInt(p, WINS_KEY, out v) || v <= 0) continue;
                string n = p.displayName;
                for (int k = 0; k < 3; k++)
                {
                    if (v <= best[k]) continue;
                    for (int j = 2; j > k; j--) { best[j] = best[j - 1]; names[j] = names[j - 1]; }
                    best[k] = v;
                    names[k] = n;
                    break;
                }
            }
            if (best[0] <= 0) return "";
            string s = "<b>勝ち頭</b>\n";
            for (int k = 0; k < 3; k++)
                if (best[k] > 0) s += HudMath.Row2((k + 1).ToString() + "位", HudMath.DisplayName(names[k], HudMath.SHORT_NAME_MAX) + "  " + best[k].ToString() + "勝") + "\n";
            return s;
        }

        /// <summary>STAGE_RESULT に入ったとき：このステージの予想を着順で採点（1 着 3 点・2〜3 着 1 点）して通算に足す。</summary>
        private void ScorePickLocal()
        {
            if (localPickSeq != stageSeq || localPickSlot < 0) return;
            int slot = localPickSlot;
            localPickSeq = -1;
            localPickSlot = -1;
            if (slotPlayerId[slot] != localPickPlayer) { pickLastText = "予想した人が抜けた"; return; }
            int place = slotStagePlace[slot];
            int pts = DirectorMath.PickPoints(place);
            EnsurePickLoaded();
            pickTotal += pts;
            pickLoaded = true;
            PlayerData.SetInt(PICK_KEY, pickTotal);
            string who = ShortName(localPickPlayer);
            pickLastText = pts > 0 ? "的中！ " + who + " " + place.ToString() + "着  +" + pts.ToString() + "点"
                : "はずれ " + who + (place > 0 ? " " + place.ToString() + "着" : "");
            // 観客への小さなごほうび（ui-r3-16）：当たりは鈴・はずれは鈍い音（新聞売りの 2D の声）、予想の紙が跳ね、中継の札に 4 秒
            if (!IsLocalEntered())
            {
                if (crier != null) crier.PickResult(pts > 0, pickLastText);
                if (spectator != null) spectator.ShowPickCaption(pickLastText);
            }
        }

        /// <summary>予想の札と札の上の紙を書き直す（ローカル）。札は枠に人がいるスロットだけ出す。</summary>
        private void RefreshPicksLocal()
        {
            bool open = state != RaceMath.STATE_FINAL_RESULT && !(state == RaceMath.STATE_STAGE_RESULT && stage + 1 >= STAGE_COUNT);
            bool pickedValid = localPickSlot >= 0 && localPickSlot < MAX_SLOTS && slotPlayerId[localPickSlot] == localPickPlayer;
            if (pickCards != null)
            {
                for (int i = 0; i < pickCards.Length; i++)
                {
                    PosterCard c = pickCards[i];
                    if (c == null) continue;
                    int slot = c.value;
                    bool has = slot >= 0 && slot < MAX_SLOTS && slotPlayerId[slot] >= 0 && open;
                    if (c.gameObject.activeSelf != has) c.gameObject.SetActive(has);
                    if (!has) continue;
                    bool mine = pickedValid && localPickSlot == slot;
                    c.SetPickLabel("<b>" + ShortName(slotPlayerId[slot]) + "</b>\n<size=70%>" + MountKindLabel(slotMount[slot]) + "・" + AbilityLabel(slotAbility[slot]) + "</size>");
                    c.SetTaken(mine);
                }
            }
            if (pickStatusText == null) return;
            EnsurePickLoaded();
            string s = "<b>予想</b>（1着 3点・3着まで 1点）　通算 " + pickTotal.ToString() + "点\n";
            string line = "";
            if (pickedValid) line = "あなたの予想：" + ShortName(localPickPlayer);
            else if (IsLocalEntered()) line = "走る人は予想できない";
            else line = "勝つと思う人の札を取る";
            if (pickLastText.Length > 0) line += "　／　" + pickLastText;
            pickStatusText.text = s + line;
        }

        /// <summary>たまり場の黒板の「予想の名人」：このインスタンスにいる人の通算（PlayerData）の上位 3 人。誰も点が無ければ空。</summary>
        public string PickMastersText()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            if (count <= 0) return "";
            VRCPlayerApi[] players = new VRCPlayerApi[count];
            players = VRCPlayerApi.GetPlayers(players);
            int[] best = new int[] { 0, 0, 0 };
            string[] names = new string[] { "", "", "" };
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi p = players[i];
                if (p == null || !p.IsValid()) continue;
                int v;
                if (!PlayerData.TryGetInt(p, PICK_KEY, out v) || v <= 0) continue;
                string n = p.displayName;
                for (int k = 0; k < 3; k++)
                {
                    if (v <= best[k]) continue;
                    for (int j = 2; j > k; j--) { best[j] = best[j - 1]; names[j] = names[j - 1]; }
                    best[k] = v;
                    names[k] = n;
                    break;
                }
            }
            if (best[0] <= 0) return "";
            string s = "<b>予想の名人</b>\n";
            for (int k = 0; k < 3; k++)
                if (best[k] > 0) s += HudMath.Row2((k + 1).ToString() + "位", HudMath.DisplayName(names[k], HudMath.SHORT_NAME_MAX) + "  " + best[k].ToString() + "点") + "\n";
            return s;
        }
    }
}
