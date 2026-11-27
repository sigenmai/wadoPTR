using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 中継カメラ（1 台、RenderTexture 960×540 へ描画）。たまり場の掲示板（NoticeBoardDisplay mode 0）に映す。
    ///
    /// - 描画するのは COUNTDOWN / RACING の間で、ローカルプレイヤーがレース参加者でないか、もうゴールした参加者のクライアントだけ
    ///   （走っている参加者は camera.enabled = false。ゴールした人は囲いの掲示板で残りの走りを見る：system-r1-10）。
    /// - 毎 0.5 秒、各 MountController の同期値（RaceManager と同じ syncCp / syncDistToCp / syncFinishAt）から順位と前の馬との距離を出し、
    ///   DirectorMath.ChooseTarget で追う馬を決める（基本は先頭／8m 以内の競り合い／能力命中の直後、1 カット 5〜15 秒）。
    ///   ゴール済みの馬は中継の対象から外す（全員ゴールしたらゴール済みも含める）。
    /// - 追従は後方斜め上からなめらかに。切り替え時だけ 0.3 秒で素早くブレンドする。
    /// - 掲示板下の木札（DirectorTag）で「○位を追う」「おまかせ」をローカルに切り替える（押した本人の画面だけ）。
    /// - ゴールした馬はゴール後 (FINISH_PEN_DELAY - 0.5) 秒だけ映し続けてから切り替える（囲いへ移る瞬間は映さない）。
    /// - 「○位を追う」は順位の入れ替わりに 1.5 秒の猶予を置いてから切り替える。その順位の人がいなければ「おまかせ」と同じ。
    /// - 「命中」は撃った側が判定した相手のスロットを全員向けの見た目イベント（FxShot / FxLasso）の引数で受け取り、
    ///   鉄球は全クライアントで動く当たり判定から、NotifyHitSlot でローカルに知らされる。
    /// - RenderTexture（Target Texture）が無いカメラは描画しない（観戦者の画面に直接映らないように）。
    /// 同期しない。プレイヤーのカメラ（視点）には一切触れない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SpectatorDirector : UdonSharpBehaviour
    {
        public const float DECIDE_INTERVAL = 0.5f;
        public const float CUT_BLEND_SECONDS = 0.3f;
        public const float OVERRIDE_HYSTERESIS = 1.5f;
        /// <summary>ゴールした馬を映し続ける秒数（囲いへ移されるより前に切る）。</summary>
        public const float FINISH_HOLD_SECONDS = RaceManager.FINISH_PEN_DELAY - 0.5f;
        public const int MAX_RANK = 8;

        public RaceManager raceManager;
        [Tooltip("中継カメラ。Target Texture は下の RenderTexture（960×540）。初期状態は無効でよい")]
        public Camera broadcastCamera;
        [Tooltip("任意。設定すると起動時にカメラの Target Texture に入れる")]
        public RenderTexture renderTexture;

        [Header("追従（後方斜め上）")]
        public float followDistance = 11f;   // 18 m/s でも後ろから全身が入る
        public float followHeight = 4.5f;
        public float lookHeight = 1.2f;
        [Tooltip("位置の追従の速さ（大きいほどきびきび）")]
        public float positionSharpness = 4f;
        [Tooltip("向き（馬の進行方向）の追従の速さ")]
        public float yawSharpness = 2f;

        [Header("カメラの範囲（ステージごと、x/z。Build World が見えない壁の内側に設定）")]
        [Tooltip("stage 番号ごとの最小 x/z（y は使わない）。空ならはみ出しを制限しない")]
        public Vector3[] stageBoundsMin;
        [Tooltip("stage 番号ごとの最大 x/z")]
        public Vector3[] stageBoundsMax;

        [Header("任意")]
        [Tooltip("掲示板の下の札に「中継：名前（2位）」と書く")]
        public TextMeshPro captionText;
        [Tooltip("木札。選ばれている札の印を光らせる")]
        public DirectorTag[] tags;

        // ---- ローカル状態 ----
        private bool live;
        private int overrideRank;          // 0 = おまかせ、1..8 = その順位を追う
        private bool overrideFresh;        // 木札を押した直後はすぐ切り替える
        private bool overrideActive;       // 今「○位を追う」で動いている（その順位の人がいる）
        private int overrideCandidate = -1;
        private float overrideCandidateSince;
        private bool warnedNoTexture;
        private double serverNow;
        private int target = -1;
        private float shotStart;
        private float nextDecide;
        private int lastHitSlot = -1;
        private float lastHitTime = -100f;

        private bool hasFollow;
        private Vector3 followPos;
        private float followYaw;
        private float blendT = 1f;
        private Vector3 blendFromPos;
        private Quaternion blendFromRot;

        private int[] fullOrder = new int[0];
        private int[] cp;
        private float[] dist;
        private double[] fin;
        private bool[] act;
        private bool[] actRunning;
        private float[] px;
        private float[] pz;

        private void Start()
        {
            int n = RaceManager.MAX_SLOTS;
            cp = new int[n];
            dist = new float[n];
            fin = new double[n];
            act = new bool[n];
            actRunning = new bool[n];
            px = new float[n];
            pz = new float[n];
            if (broadcastCamera != null)
            {
                if (renderTexture != null) broadcastCamera.targetTexture = renderTexture;
                broadcastCamera.enabled = false;
            }
            else Debug.LogWarning("[HorseRace] SpectatorDirector: broadcastCamera が未設定です");
            RefreshTags();
        }

        // =====================================================================
        // 公開 API
        // =====================================================================

        /// <summary>このクライアントで中継を描画しているか（掲示板の表示切り替え用）。</summary>
        public bool IsLive()
        {
            return live;
        }

        /// <summary>今追っているスロット（いなければ -1）。</summary>
        public int CurrentTarget()
        {
            return live ? target : -1;
        }

        public int OverrideRank()
        {
            return overrideRank;
        }

        /// <summary>木札から（ローカル）。0 = おまかせ、1..8 = その順位を追う。</summary>
        public void SetOverride(int rank)
        {
            if (rank < 0 || rank > MAX_RANK) return;
            overrideRank = rank;
            overrideFresh = true;
            overrideCandidate = -1;
            nextDecide = 0f;
            RefreshTags();
        }

        /// <summary>
        /// 能力・弾が当たったレース用の乗り物のスロット（撃った側が判定し、全員向けの見た目イベントの引数で届く）。
        /// -1・範囲外・空きスロットは無視。ローカルのみ。
        /// </summary>
        public void NotifyHitSlot(int slot)
        {
            if (raceManager == null || slot < 0 || slot >= raceManager.SlotCount()) return;
            if (raceManager.slotPlayerId[slot] < 0) return;
            lastHitSlot = slot;
            lastHitTime = Time.time;
        }

        // =====================================================================
        // 更新
        // =====================================================================

        private float nextTagRefresh;

        /// <summary>
        /// 木札（ui-r2-14）：レース中はエントリー数より大きい順位の札を隠し、残りを「2位 名前」に書き換える（0.5 秒ごと）。
        /// レース外は「1位」〜「8位」をすべて出す。隠した札の選択の印も一緒に隠れる。
        /// </summary>
        private void RefreshTagLabels()
        {
            if (tags == null || raceManager == null) return;
            RaceManager rm = raceManager;
            int st = rm.state;
            bool racing = st == RaceMath.STATE_COUNTDOWN || st == RaceMath.STATE_RACING;
            int count = rm.ActiveCount();
            for (int i = 0; i < tags.Length; i++)
            {
                DirectorTag t = tags[i];
                if (t == null) continue;
                int r = t.followRank;
                if (r <= 0 || !racing) { t.SetVisible(true); t.SetLabel(""); continue; }
                bool show = r <= count;
                t.SetVisible(show);
                if (!show) continue;
                int slot = -1, seen = 0;
                for (int p = 0; p < RaceManager.MAX_SLOTS; p++)
                {
                    int o = rm.order[p];
                    if (o < 0 || rm.slotPlayerId[o] < 0) continue;
                    seen++;
                    if (seen == r) { slot = o; break; }
                }
                t.SetLabel(slot >= 0 ? r.ToString() + "位 " + RaceManager.ShortName(rm.slotPlayerId[slot]) : "");
            }
        }

        private void Update()
        {
            if (Time.time >= nextTagRefresh)
            {
                nextTagRefresh = Time.time + DECIDE_INTERVAL;
                RefreshTagLabels();
            }
            bool want = ShouldRender();
            if (want != live)
            {
                live = want;
                if (broadcastCamera != null) broadcastCamera.enabled = live;
                if (live)
                {
                    target = -1;
                    hasFollow = false;
                    nextDecide = 0f;
                }
                else if (captionText != null) captionText.text = "";
            }
            if (!live) return;
            if (Time.time >= nextDecide)
            {
                nextDecide = Time.time + DECIDE_INTERVAL;
                Decide();
            }
        }

        private bool ShouldRender()
        {
            if (raceManager == null || broadcastCamera == null) return false;
            int st = raceManager.state;
            if (st != RaceMath.STATE_COUNTDOWN && st != RaceMath.STATE_RACING) return false;
            if (raceManager.IsLocalEntered())
            {
                MountController lm = raceManager.LocalMount();
                if (lm == null || lm.syncFinishAt < 0.0 || st != RaceMath.STATE_RACING) return false;
            }
            if (broadcastCamera.targetTexture == null)
            {
                // Target Texture が無いカメラは観戦者の画面に直接描画してしまうので使わない
                if (!warnedNoTexture)
                {
                    warnedNoTexture = true;
                    Debug.LogWarning("[HorseRace] SpectatorDirector: 中継カメラに RenderTexture（Target Texture）がないので描画しません");
                }
                return false;
            }
            return true;
        }

        private void Decide()
        {
            if (cp == null) return;
            RaceManager rm = raceManager;
            int n = rm.SlotCount();
            int running = 0;
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
            {
                act[i] = false;
                actRunning[i] = false;
                fin[i] = -1.0;
                cp[i] = 0;
                dist[i] = 0f;
                if (i >= n || rm.slotPlayerId[i] < 0) continue;
                MountController m = rm.mounts[i];
                if (m == null) continue;
                act[i] = true;
                cp[i] = m.syncCp;
                dist[i] = m.syncDistToCp;
                double f = m.syncFinishAt;
                bool finished = f >= 0.0 && f >= rm.stageStartServer;
                if (finished) fin[i] = f;
                else
                {
                    actRunning[i] = true;
                    running++;
                }
                Vector3 p = m.transform.position;
                px[i] = p.x;
                pz[i] = p.z;
            }
            fullOrder = RaceMath.ComputeOrder(cp, dist, fin, act);
            int[] order = running > 0 ? RaceMath.ComputeOrder(cp, dist, fin, actRunning) : fullOrder;

            serverNow = Networking.GetServerTimeInSeconds();
            int next = -1;
            bool usedOverride = false;
            if (overrideRank > 0)
            {
                // その順位の人がいない（エントリー数を超える）・ゴールして囲いへ向かう馬なら「おまかせ」と同じ
                int want = DirectorMath.SlotAtRank(fullOrder, overrideRank);
                if (want >= 0 && Usable(want))
                {
                    usedOverride = true;
                    if (want == target)
                    {
                        overrideCandidate = -1;
                        next = target;
                    }
                    else if (overrideFresh || !Usable(target))
                    {
                        next = want;
                    }
                    else
                    {
                        // 順位の入れ替わりは少し待ってから（抜きつ抜かれつで切り替えが連発しないように）
                        if (overrideCandidate != want)
                        {
                            overrideCandidate = want;
                            overrideCandidateSince = Time.time;
                        }
                        next = Time.time - overrideCandidateSince >= OVERRIDE_HYSTERESIS ? want : target;
                    }
                }
            }
            overrideFresh = false;
            overrideActive = usedOverride;
            if (!usedOverride)
            {
                overrideCandidate = -1;
                if (target >= 0 && act[target] && fin[target] >= 0.0 && Usable(target))
                {
                    next = target; // ゴールした直後の馬はもう少し映す
                }
                else
                {
                    bool currentValid = target >= 0 && Contains(order, target);
                    float shotTime = Time.time - shotStart;
                    // 追っていた馬がいなくなった・ゴールして映す時間が過ぎた → すぐに選び直す
                    if (!currentValid && shotTime < DirectorMath.MIN_SHOT) shotTime = DirectorMath.MIN_SHOT;
                    float[] gaps = DirectorMath.GapsXZ(order, px, pz);
                    next = DirectorMath.ChooseTarget(currentValid ? target : -1, shotTime, order, gaps, lastHitSlot, Time.time - lastHitTime);
                }
            }
            if (next != target) CutTo(next);
            UpdateCaption();
        }

        /// <summary>映してよい馬か：エントリー中で、まだゴールしていないか、ゴールして FINISH_HOLD_SECONDS 以内。</summary>
        private bool Usable(int slot)
        {
            if (slot < 0 || slot >= RaceManager.MAX_SLOTS || !act[slot]) return false;
            if (fin[slot] < 0.0) return true;
            return serverNow - fin[slot] < FINISH_HOLD_SECONDS;
        }

        private static bool Contains(int[] arr, int v)
        {
            for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return true;
            return false;
        }

        private void CutTo(int slot)
        {
            target = slot;
            shotStart = Time.time;
            if (broadcastCamera == null) return;
            if (hasFollow)
            {
                Transform ct = broadcastCamera.transform;
                blendFromPos = ct.position;
                blendFromRot = ct.rotation;
                blendT = 0f;
            }
            else blendT = 1f; // 最初のカットはそのまま置く
            hasFollow = false; // 新しい馬の後ろに追従点を置き直す
        }

        /// <summary>カメラがステージの外（地形の端・尾根の向こう）を映さないよう、位置を今のステージの範囲に収める。</summary>
        private Vector3 ClampToStage(Vector3 p)
        {
            if (stageBoundsMin == null || stageBoundsMax == null) return p;
            int st = raceManager.stage;
            if (st < 0 || st >= stageBoundsMin.Length || st >= stageBoundsMax.Length) return p;
            Vector3 mn = stageBoundsMin[st];
            Vector3 mx = stageBoundsMax[st];
            p.x = Mathf.Clamp(p.x, mn.x, mx.x);
            p.z = Mathf.Clamp(p.z, mn.z, mx.z);
            return p;
        }

        private void LateUpdate()
        {
            if (!live || broadcastCamera == null || raceManager == null) return;
            if (target < 0 || target >= raceManager.SlotCount()) return;
            MountController m = raceManager.mounts[target];
            if (m == null) return;
            float dt = Time.deltaTime;
            Vector3 mp = m.transform.position;
            float mYaw = m.transform.eulerAngles.y;
            if (!hasFollow)
            {
                followYaw = mYaw;
                followPos = DesiredPos(mp, followYaw);
                hasFollow = true;
            }
            else
            {
                followYaw = Mathf.LerpAngle(followYaw, mYaw, 1f - Mathf.Exp(-yawSharpness * dt));
                followPos = Vector3.Lerp(followPos, DesiredPos(mp, followYaw), 1f - Mathf.Exp(-positionSharpness * dt));
            }
            followPos = ClampToStage(followPos);
            Vector3 look = mp + Vector3.up * lookHeight - followPos;
            Quaternion rot = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look) : broadcastCamera.transform.rotation;
            Vector3 pos = followPos;
            if (blendT < 1f)
            {
                blendT = Mathf.Min(1f, blendT + dt / CUT_BLEND_SECONDS);
                float s = blendT * blendT * (3f - 2f * blendT);
                pos = Vector3.Lerp(blendFromPos, pos, s);
                rot = Quaternion.Slerp(blendFromRot, rot, s);
            }
            broadcastCamera.transform.SetPositionAndRotation(pos, rot);
        }

        private Vector3 DesiredPos(Vector3 mountPos, float yawDeg)
        {
            Vector3 fwd = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            return mountPos - fwd * followDistance + Vector3.up * followHeight;
        }

        private void UpdateCaption()
        {
            if (captionText == null) return;
            if (target < 0)
            {
                captionText.text = "";
                return;
            }
            int rank = 0;
            for (int k = 0; k < fullOrder.Length; k++) if (fullOrder[k] == target) { rank = k + 1; break; }
            string who = RaceManager.ShortName(raceManager.slotPlayerId[target]);
            float rem = raceManager.GetRemainingSeconds();
            string mode = overrideActive ? "" : "（おまかせ）";
            string c = RaceManager.StageLabel(raceManager.stage) + "｜" + rank.ToString() + "位 " + who + " を追跡" + mode;
            if (rem >= 0f && raceManager.state == RaceMath.STATE_RACING) c += "｜残り " + RaceMath.FormatClock(rem);
            if (captionText.text != c) captionText.text = c;
        }

        private void RefreshTags()
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] != null) tags[i].SetSelected(tags[i].followRank == overrideRank);
        }
    }
}
