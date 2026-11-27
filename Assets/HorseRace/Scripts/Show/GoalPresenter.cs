using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// ゴールゲートの演出（ステージごとに 1 つ、stageIndex で区別）。同期しない・見た目はローカル。
    ///
    /// - 乗り物がゴールしたら（＝ゲートのゴール用 Checkpoint が立てる同期値 MountController.syncFinishAt が今のステージの値になったら）
    ///   紙吹雪を出し、その乗り物の上に着順の木札（「1着！」「2着」…）を跳ねさせる。着順は全員の syncFinishAt の順（DirectorMath.FinishPlace）。
    ///   ゲートの Trigger そのもの（Checkpoint, isGoal）が syncFinishAt を立てるので、ここでは同期値を見るだけで全クライアントが同じ着順を出せる。
    /// - そのステージで最初のゴール（RaceManager の同期値 firstFinishServer が今のステージの値になった）で教会の鐘を全員が 1 回鳴らす。
    ///   stageSeq ごとに 1 回。途中参加などで古い出来事を受け取ったときは鳴らさない。
    /// - 自分のゴールは首の札（RaceManager.hud）に「3着  +6点」とステージのタイム（10 分の 1 秒）を 4 秒、跳ねる拡大とゴールの音。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GoalPresenter : UdonSharpBehaviour
    {
        /// <summary>この秒数より古いゴール・最初のゴールは演出しない（途中参加・同期の遅れ）。</summary>
        public const float FRESH_SECONDS = 6f;

        public RaceManager raceManager;
        [Tooltip("このゲートのステージ（0 始まり）")]
        public int stageIndex;

        [Header("効果")]
        [Tooltip("紙吹雪（ゲートの上に置く）")]
        public ParticleSystem confetti;
        [Tooltip("1 回のゴールで出す紙吹雪の数（0 なら Play）")]
        public int confettiBurst = 120;
        [Tooltip("教会の鐘（全員に聞こえるよう 2D 推奨）")]
        public AudioSource bellAudio;
        public AudioClip bellClip;
        [Tooltip("任意。ゴールごとの小さな歓声・ラッパ")]
        public AudioSource finishAudio;

        [Header("着順の木札（8 枚、最初は非表示）")]
        public Transform[] placeTags;
        public TextMeshPro[] placeTagTexts;
        public float tagHeight = 2.8f;
        public float tagHopHeight = 0.8f;
        public float tagHopPeriod = 0.5f;
        public float tagHopSeconds = 1.6f;
        [Tooltip("ゴール 3 秒後に乗り物は囲いへ移されるので、それより短く")]
        public float tagShowSeconds = RaceManager.FINISH_PEN_DELAY - 0.2f;

        private int celebratedSeq = -1;
        private bool[] celebrated;
        private int ringSeq = -1;
        private double[] fin;
        private bool[] act;

        private int[] tagSlot;
        private float[] tagStart;
        private int nextTag;

        private void Start()
        {
            int n = RaceManager.MAX_SLOTS;
            celebrated = new bool[n];
            fin = new double[n];
            act = new bool[n];
            int t = placeTags != null ? placeTags.Length : 0;
            tagSlot = new int[t];
            tagStart = new float[t];
            for (int i = 0; i < t; i++)
            {
                tagSlot[i] = -1;
                if (placeTags[i] != null) placeTags[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (celebrated == null) return;
            AnimateTags();
            RaceManager rm = raceManager;
            if (rm == null || rm.stage != stageIndex) return;
            int st = rm.state;
            if (st != RaceMath.STATE_RACING && st != RaceMath.STATE_STAGE_RESULT) return;

            if (rm.stageSeq != celebratedSeq)
            {
                celebratedSeq = rm.stageSeq;
                for (int i = 0; i < celebrated.Length; i++) celebrated[i] = false;
            }

            double now = Networking.GetServerTimeInSeconds();
            double stageStart = rm.stageStartServer;
            int n = rm.SlotCount();
            bool any = false;
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
            {
                act[i] = false;
                fin[i] = -1.0;
                if (i >= n || rm.slotPlayerId[i] < 0 || rm.mounts[i] == null) continue;
                act[i] = true;
                double f = rm.mounts[i].syncFinishAt;
                if (f >= 0.0 && f >= stageStart)
                {
                    fin[i] = f;
                    if (!celebrated[i]) any = true;
                }
            }
            if (any)
            {
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
                {
                    if (!act[i] || celebrated[i] || fin[i] < 0.0) continue;
                    celebrated[i] = true;
                    if (now - fin[i] > FRESH_SECONDS) continue;
                    Celebrate(i, DirectorMath.FinishPlace(fin, act, i, stageStart));
                }
            }

            // 鐘：最初のゴール（マスターが同期する firstFinishServer）を全員が見て 1 回
            double first = rm.firstFinishServer;
            if (ringSeq != rm.stageSeq && first >= 0.0 && first >= stageStart)
            {
                ringSeq = rm.stageSeq;
                if (now - first <= FRESH_SECONDS) RingBell();
            }
        }

        private void Celebrate(int slot, int place)
        {
            if (confetti != null)
            {
                if (confettiBurst > 0) confetti.Emit(confettiBurst);
                else confetti.Play();
            }
            RaceManager rm = raceManager;
            if (rm != null && slot == rm.LocalSlot() && rm.hud != null && place > 0)
            {
                // 自分のゴール：首の札に「3着  +6点」とタイム（2D のゴールの音は札が鳴らす）
                // 点はステージ込み（最終ステージ ×1.5）：RaceManager・黒板・電報と同じ表（ui-r2-01）
                rm.hud.ShowFinish(place, RaceMath.PointsForPlace(place - 1, rm.stage), (float)(fin[slot] - rm.stageStartServer), rm.stage);
            }
            else if (finishAudio != null) finishAudio.Play();
            if (placeTags == null || placeTags.Length == 0 || place <= 0) return;
            int k = nextTag % placeTags.Length;
            nextTag++;
            if (placeTags[k] == null) return;
            tagSlot[k] = slot;
            tagStart[k] = Time.time;
            if (placeTagTexts != null && k < placeTagTexts.Length && placeTagTexts[k] != null)
            {
                // 着順と名前（ui-r2-14：中継・囲いで見ている人にも誰か分かる）
                string who = rm != null && slot < rm.slotPlayerId.Length && rm.slotPlayerId[slot] >= 0 ? RaceManager.ShortName(rm.slotPlayerId[slot]) : "";
                placeTagTexts[k].text = (place == 1 ? "1着！" : place.ToString() + "着") + (who.Length > 0 ? "\n<size=55%>" + who + "</size>" : "");
            }
            placeTags[k].gameObject.SetActive(true);
        }

        private void RingBell()
        {
            if (bellAudio == null) return;
            if (bellClip != null) bellAudio.PlayOneShot(bellClip);
            else bellAudio.Play();
        }

        private void AnimateTags()
        {
            if (placeTags == null || raceManager == null || raceManager.mounts == null) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            Vector3 head = local != null ? local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position : Vector3.zero;
            for (int k = 0; k < placeTags.Length; k++)
            {
                int slot = tagSlot[k];
                if (slot < 0 || placeTags[k] == null) continue;
                float t = Time.time - tagStart[k];
                MountController m = slot < raceManager.SlotCount() ? raceManager.mounts[slot] : null;
                if (t > tagShowSeconds || m == null)
                {
                    tagSlot[k] = -1;
                    placeTags[k].gameObject.SetActive(false);
                    continue;
                }
                // 跳ねる：減衰する |sin|
                float decay = Mathf.Max(0f, 1f - t / tagHopSeconds);
                float hop = tagHopHeight * Mathf.Abs(Mathf.Sin(t * Mathf.PI / tagHopPeriod)) * decay;
                Vector3 pos = m.transform.position + Vector3.up * (tagHeight + hop);
                // 文字をローカルプレイヤーのほうへ向ける（水平回転のみ）
                Vector3 away = pos - head;
                away.y = 0f;
                Quaternion rot = away.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(away) : placeTags[k].rotation;
                placeTags[k].SetPositionAndRotation(pos, rot);
            }
        }
    }
}
