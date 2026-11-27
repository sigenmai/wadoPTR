using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// レース進行。オーナー（＝インスタンスマスター）だけが状態を書き換え、Manual 同期で全員に配る。
    ///
    /// 状態: LOBBY →(鐘)→ COUNTDOWN 3 s（Starter があれば旗＋空砲、最大 6 s）→ RACING（毎秒 ShouldEndStage）
    ///       → STAGE_RESULT 10 s（ポイント加算・能力/弾/遺物のリセット・途中参加の受付）→ 次の COUNTDOWN
    ///       （RACING は 1 位ゴール 60 秒後・5:30・全員ゴールのいちばん早いもので終わる）
    ///       ステージ中（COUNTDOWN / RACING）のサインは pending* に預かり、STAGE_RESULT に入るときに空き枠へ入れる（次のステージから走る）
    ///       … 3 ステージ後 FINAL_RESULT 20 s → LOBBY（エントリーとポイントを消す）
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
        public const float STAGE_RESULT_SECONDS = 10f;
        public const float FINAL_RESULT_SECONDS = 20f;
        public const float FINISH_PEN_DELAY = 3f;
        public const float VOICE_FAR = 10000f;
        public const float VOICE_NEAR = 0f;
        /// <summary>Phase 1 の能力は 0..3（砂塵・投げ縄・岩壁・鉄球）。</summary>
        public const int MAX_ABILITY_PHASE1 = 3;
        public const float MASTER_TICK = 0.25f;
        public const double RACE_EVAL_INTERVAL = 1.0;

        // ---- 同期（マスターだけが書く）----
        [UdonSynced] public int state;
        [UdonSynced] public int stage;
        [UdonSynced] public double stageStartServer;
        [UdonSynced] public double firstFinishServer = -1.0;
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
        /// <summary>現在のステージ順位（スロット番号、先頭から。空きは -1）。</summary>
        [UdonSynced] public int[] order = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        /// <summary>今のレースのステージ別着順。index = stage * 8 + slot（1 始まり、0 = なし）。</summary>
        [UdonSynced] public int[] stagePlace = new int[STAGE_COUNT * MAX_SLOTS];
        /// <summary>ステージ中にサインした人（次のステージから走る）。-1 = 空き。STAGE_RESULT に入るときに枠へ移す。</summary>
        [UdonSynced] public int[] pendingPlayerId = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        [UdonSynced] public int[] pendingMount = new int[MAX_SLOTS];
        [UdonSynced] public int[] pendingCoat = new int[MAX_SLOTS];
        [UdonSynced] public int[] pendingAbility = new int[MAX_SLOTS];

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

        [Header("Lobby / UI")]
        public EntryLedger[] ledgers;
        public RankBoard[] rankBoards;
        public PosterCard[] posterCards;
        public MountPicker[] mountPickers;
        public StartBell startBell;
        public PracticeArea practiceArea;

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

        /// <summary>総合順位（合計ポイント順、1 始まり）。エントリーしていなければ 0。</summary>
        public int GetOverallRankOf(int playerId)
        {
            int s = FindSlot(playerId);
            if (s < 0) return 0;
            int rank = 1;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (i == s || slotPlayerId[i] < 0) continue;
                if (slotTotalPoints[i] > slotTotalPoints[s] || (slotTotalPoints[i] == slotTotalPoints[s] && i < s)) rank++;
            }
            return rank;
        }

        /// <summary>有効なスロットを合計ポイントの多い順に（同点はスロット番号の小さい順）。</summary>
        public int[] GetOverallOrder()
        {
            int n = ActiveCount();
            int[] result = new int[n];
            int count = 0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (slotPlayerId[i] < 0) continue;
                int j = count;
                while (j > 0 && slotTotalPoints[i] > slotTotalPoints[result[j - 1]])
                {
                    result[j] = result[j - 1];
                    j--;
                }
                result[j] = i;
                count++;
            }
            return result;
        }

        /// <summary>ステージの残り秒数。RACING 中は終了条件の早いほうまで、COUNTDOWN 中は 330。それ以外は -1。</summary>
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

        // =====================================================================
        // ローカル選択（手配書・柵の馬・台帳・鐘から呼ぶ）
        // =====================================================================

        public void SelectMount(int mount)
        {
            if (mount != MountMath.MOUNT_HORSE && mount != MountMath.MOUNT_CAMEL) return;
            localMount = mount;
            RefreshLobbyLocal();
        }

        /// <summary>柵の馬を選ぶ＝乗り物は馬になる。coat 0..5、6 = 骨の馬。</summary>
        public void SelectCoat(int coat)
        {
            if (coat < 0 || coat > MountVisual.COAT_SKELETON) return;
            localCoat = coat;
            localMount = MountMath.MOUNT_HORSE;
            RefreshLobbyLocal();
        }

        public void SelectAbility(int ability)
        {
            if (ability < 0 || ability > MAX_ABILITY_PHASE1) return;
            localAbility = ability;
            RefreshLobbyLocal();
        }

        /// <summary>台帳にサイン：今の選択でマスターにエントリーを依頼する。</summary>
        public void SignLedger()
        {
            int coat = localMount == MountMath.MOUNT_CAMEL ? 0 : localCoat;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestEntry), localMount, coat, localAbility);
        }

        /// <summary>鐘を鳴らす：マスターのときだけ開始を依頼する。</summary>
        public void RingBell()
        {
            if (!Networking.IsOwner(gameObject)) return;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStart));
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
            if (ability < 0 || ability > MAX_ABILITY_PHASE1) return;
            if (mount == MountMath.MOUNT_CAMEL) coat = 0;
            else if (coat < 0 || coat > MountVisual.COAT_SKELETON) return;

            int s = FindSlot(caller.playerId);
            if (s >= 0)
            {
                // 乗り物・毛色・能力を変えられるのはロビーだけ（ステージの合間のサインし直しは何も変えない）
                if (state != RaceMath.STATE_LOBBY) return;
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

        [NetworkCallable]
        public void RequestStart()
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = GetCaller();
            if (caller == null || !caller.IsValid()) return;
            if (!Networking.IsOwner(caller, gameObject)) return; // マスターのみ
            if (state != RaceMath.STATE_LOBBY) return;
            if (ActiveCount() == 0) return;
            stage = 0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                slotTotalPoints[i] = 0;
                slotStagePlace[i] = 0;
            }
            for (int k = 0; k < stagePlace.Length; k++) stagePlace[k] = 0;
            BeginCountdownMaster(Networking.GetServerTimeInSeconds());
            RequestSerialization();
            ApplyLocal();
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
                    nextRaceEval = now + RACE_EVAL_INTERVAL;
                    changed = true;
                }
            }
            else if (state == RaceMath.STATE_RACING)
            {
                if (ActiveCount() == 0)
                {
                    GoLobbyMaster(now);
                    changed = true;
                }
                else if (now >= nextRaceEval)
                {
                    nextRaceEval = now + RACE_EVAL_INTERVAL;
                    if (UpdateOrderMaster()) changed = true;
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
                    GoLobbyMaster(now);
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
                    GoLobbyMaster(now);
                    changed = true;
                }
            }
            // LOBBY: 鐘（RequestStart）を待つだけ

            if (changed)
            {
                RequestSerialization();
                ApplyLocal();
            }
        }

        private void BeginCountdownMaster(double now)
        {
            state = RaceMath.STATE_COUNTDOWN;
            stateStartServer = now;
            stageStartServer = now;
            firstFinishServer = -1.0;
            stageSeq++;
            starterDone = false;
            allFinishedMaster = false;
            for (int p = 0; p < MAX_SLOTS; p++) order[p] = -1;
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
            allFinishedMaster = RaceMath.AllFinished(fin, act);
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
            UpdateOrderMaster();
            for (int i = 0; i < MAX_SLOTS; i++) slotStagePlace[i] = 0;
            int place = 0;
            for (int p = 0; p < MAX_SLOTS; p++)
            {
                int s = order[p];
                if (s < 0 || slotPlayerId[s] < 0) continue;
                slotTotalPoints[s] += RaceMath.PointsForPlace(place);
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

        private void GoLobbyMaster(double now)
        {
            state = RaceMath.STATE_LOBBY;
            stage = 0;
            stateStartServer = now;
            firstFinishServer = -1.0;
            for (int i = 0; i < MAX_SLOTS; i++) ClearSlot(i);
            for (int p = 0; p < MAX_SLOTS; p++) order[p] = -1;
            ClearPending();
        }

        private void ClearSlot(int i)
        {
            slotPlayerId[i] = -1;
            slotMount[i] = 0;
            slotCoat[i] = 0;
            slotAbility[i] = 0;
            slotTotalPoints[i] = 0;
            slotStagePlace[i] = 0;
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
                    Transform sp = GetStartPoint(stage, i);
                    Vector3 pos = sp != null ? sp.position : m.transform.position;
                    float yaw = sp != null ? sp.eulerAngles.y : m.transform.eulerAngles.y;
                    m.TeleportTo(pos, yaw);
                    m.ResetProgress(pos, yaw, GetFirstCheckpoint(stage));
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
                }
                ApplyVoiceAll();
            }
            else if (state == RaceMath.STATE_STAGE_RESULT)
            {
                if (lm != null)
                {
                    lm.InputLocked = true;
                    lm.SetRaceActive(false);
                }
                InvokeStageResetHooks();
            }
            else if (state == RaceMath.STATE_FINAL_RESULT)
            {
                if (lm != null)
                {
                    lm.InputLocked = true;
                    lm.SetRaceActive(false);
                }
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
                    if (localWasRider) SendCustomEventDelayedFrames(nameof(_TeleportLocalToLobby), 3);
                }
            }
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
        }

        // ---- 文言（プレイヤーに見せるので日本語。原作の名称は使わない）----

        public static string AbilityLabel(int ability)
        {
            if (ability == AbilityMath.ABILITY_DUST) return "砂塵";
            if (ability == AbilityMath.ABILITY_LASSO) return "投げ縄";
            if (ability == AbilityMath.ABILITY_WALL) return "岩壁";
            if (ability == AbilityMath.ABILITY_BALL) return "鉄球";
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

        public static string PlayerName(int playerId)
        {
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(playerId);
            if (p == null || !p.IsValid()) return "（不在）";
            return p.displayName;
        }

        /// <summary>"第2ステージ" のような表記（stage は 0 始まり）。</summary>
        public static string StageLabel(int st)
        {
            return "第" + (st + 1).ToString() + "ステージ";
        }
    }
}
