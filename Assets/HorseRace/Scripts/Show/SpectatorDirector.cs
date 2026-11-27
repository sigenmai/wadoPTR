using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 中継カメラ（1 台、RenderTexture 960×540 へ描画）。たまり場の掲示板（NoticeBoardDisplay mode 0）に映す。
    ///
    /// - 描画するのは COUNTDOWN / RACING の間で、ローカルプレイヤーがレース参加者でないクライアントだけ（参加者は camera.enabled = false）。
    /// - 毎 0.5 秒、各 MountController の同期値（RaceManager と同じ syncCp / syncDistToCp / syncFinishAt）から順位と前の馬との距離を出し、
    ///   DirectorMath.ChooseTarget で追う馬を決める（基本は先頭／8m 以内の競り合い／能力命中の直後、1 カット 5〜15 秒）。
    ///   ゴール済みの馬は中継の対象から外す（全員ゴールしたらゴール済みも含める）。
    /// - 追従は後方斜め上からなめらかに。切り替え時だけ 0.3 秒で素早くブレンドする。
    /// - 掲示板下の木札（DirectorTag）で「○位を追う」「おまかせ」をローカルに切り替える（押した本人の画面だけ）。
    /// - 「命中」は AbilityCaster / IronBall / RevolverHolder の全員向けの見た目イベントから NotifyHitMount / NotifyHitNear で受け取る（ローカルのみ）。
    /// 同期しない。プレイヤーのカメラ（視点）には一切触れない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SpectatorDirector : UdonSharpBehaviour
    {
        public const float DECIDE_INTERVAL = 0.5f;
        public const float CUT_BLEND_SECONDS = 0.3f;
        public const float HIT_NEAR_RADIUS = 2f;
        public const int MAX_RANK = 8;

        public RaceManager raceManager;
        [Tooltip("中継カメラ。Target Texture は下の RenderTexture（960×540）。初期状態は無効でよい")]
        public Camera broadcastCamera;
        [Tooltip("任意。設定すると起動時にカメラの Target Texture に入れる")]
        public RenderTexture renderTexture;

        [Header("追従（後方斜め上）")]
        public float followDistance = 9f;
        public float followHeight = 4.5f;
        public float lookHeight = 1.2f;
        [Tooltip("位置の追従の速さ（大きいほどきびきび）")]
        public float positionSharpness = 3f;
        [Tooltip("向き（馬の進行方向）の追従の速さ")]
        public float yawSharpness = 2f;

        [Header("任意")]
        [Tooltip("掲示板の下の札に「中継：名前（2位）」と書く")]
        public TextMeshPro captionText;
        [Tooltip("木札。選ばれている札の印を光らせる")]
        public DirectorTag[] tags;

        // ---- ローカル状態 ----
        private bool live;
        private int overrideRank;          // 0 = おまかせ、1..8 = その順位を追う
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
            nextDecide = 0f;
            RefreshTags();
        }

        /// <summary>能力・弾が当たった乗り物（ローカルの見た目イベントから）。レース用の乗り物でなければ無視。</summary>
        public void NotifyHitMount(MountController m)
        {
            if (m == null || raceManager == null || raceManager.mounts == null) return;
            int s = m.slotIndex;
            if (s < 0 || s >= raceManager.SlotCount() || raceManager.mounts[s] != m) return;
            if (raceManager.slotPlayerId[s] < 0) return;
            lastHitSlot = s;
            lastHitTime = Time.time;
        }

        /// <summary>当たった地点しかわからないとき（弾・縄）。exclude（撃った側）以外で最も近いレース用の乗り物を命中とみなす。</summary>
        public void NotifyHitNear(Vector3 point, MountController exclude)
        {
            if (raceManager == null || raceManager.mounts == null) return;
            int n = raceManager.SlotCount();
            int best = -1;
            float bestSq = HIT_NEAR_RADIUS * HIT_NEAR_RADIUS;
            for (int i = 0; i < n; i++)
            {
                MountController m = raceManager.mounts[i];
                if (m == null || m == exclude || raceManager.slotPlayerId[i] < 0) continue;
                float d = (m.transform.position - point).sqrMagnitude;
                if (d <= bestSq)
                {
                    bestSq = d;
                    best = i;
                }
            }
            if (best < 0) return;
            lastHitSlot = best;
            lastHitTime = Time.time;
        }

        // =====================================================================
        // 更新
        // =====================================================================

        private void Update()
        {
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
            return !raceManager.IsLocalEntered();
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

            int next = -1;
            if (overrideRank > 0) next = DirectorMath.SlotAtRank(fullOrder, overrideRank);
            if (next < 0)
            {
                bool currentValid = target >= 0 && Contains(order, target);
                float shotTime = Time.time - shotStart;
                // 追っていた馬がいなくなった・ゴールした → すぐに選び直す
                if (!currentValid && shotTime < DirectorMath.MIN_SHOT) shotTime = DirectorMath.MIN_SHOT;
                float[] gaps = DirectorMath.GapsXZ(order, px, pz);
                next = DirectorMath.ChooseTarget(currentValid ? target : -1, shotTime, order, gaps, lastHitSlot, Time.time - lastHitTime);
            }
            if (next != target) CutTo(next);
            UpdateCaption();
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
            string who = RaceManager.PlayerName(raceManager.slotPlayerId[target]);
            string mode = overrideRank > 0 ? overrideRank.ToString() + "位を追う" : "おまかせ";
            captionText.text = "中継：" + who + "（" + rank.ToString() + "位）［" + mode + "］";
        }

        private void RefreshTags()
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] != null) tags[i].SetSelected(tags[i].followRank == overrideRank);
        }
    }
}
