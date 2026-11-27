using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 練習場の往復タイム（system-r1-18：待ち時間に目標を）。練習場の南と北に見えない門（Checkpoint、stageIndex = -1、index 0 = 南の門、1 = 北の門）。
    /// 南の門を通ると計測開始 → 北の門を回って南の門に戻るとタイム。自分のいちばん速いタイムはローカルに持ち、
    /// 上位 5 位に入ればオーナー（既定はマスター）へ SubmitLap を送る。オーナーが名前とタイムの表（同期）を直して全員の掲示板に出す。
    /// カカシに当てた数（PracticeDummy から AddTargetHit）も自分の画面だけで数えて板に出す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PracticeLap : UdonSharpBehaviour
    {
        public const int TOP = 5;
        public const float MIN_LAP_SECONDS = 3f;

        [Tooltip("練習場の掲示板の文字（往復タイムの上位 5 人と自分の記録）")]
        public TextMeshPro boardText;
        public AudioSource lapSound;

        [UdonSynced] public string[] topNames = new string[] { "", "", "", "", "" };
        [UdonSynced] public float[] topTimes = new float[] { -1f, -1f, -1f, -1f, -1f };

        private float lapStart = -1f;
        private bool turned;
        private float best = -1f;
        private float last = -1f;
        private int targetHits;
        private float nextRefresh;

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            // 計測中は 0.2 秒ごとに経過時間を書き直す（自分の画面だけ）
            if (lapStart < 0f || Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.2f;
            Refresh();
        }

        /// <summary>Checkpoint（stageIndex &lt; 0）から、乗っている本人のクライアントで呼ばれる。</summary>
        public void GatePassed(int index)
        {
            if (index == 1)
            {
                if (lapStart >= 0f) turned = true;
                return;
            }
            if (index != 0) return;
            float now = Time.time;
            if (lapStart >= 0f && turned)
            {
                float lap = now - lapStart;
                if (lap >= MIN_LAP_SECONDS)
                {
                    last = lap;
                    if (lapSound != null) lapSound.Play();
                    if (best < 0f || lap < best)
                    {
                        best = lap;
                        if (Qualifies(lap)) Submit(lap);
                    }
                }
            }
            lapStart = now;
            turned = false;
            Refresh();
        }

        /// <summary>カカシに当てた（当てた人のクライアントで PracticeDummy.Wobble から）。</summary>
        public void AddTargetHit()
        {
            targetHits++;
            Refresh();
        }

        private bool Qualifies(float lap)
        {
            for (int i = 0; i < TOP; i++)
                if (topTimes[i] < 0f || lap < topTimes[i]) return true;
            return false;
        }

        private void Submit(float lap)
        {
            if (Networking.IsOwner(gameObject))
            {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (local != null) Insert(local.displayName, lap);
            }
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(SubmitLap), lap);
        }

        [NetworkCallable(4)]
        public void SubmitLap(float lap)
        {
            if (!Networking.IsOwner(gameObject)) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            if (lap < MIN_LAP_SECONDS || lap > 600f) return;
            Insert(caller.displayName, lap);
        }

        /// <summary>オーナー側：同じ名前の古い記録を消して、速い順に入れる。</summary>
        private void Insert(string name, float lap)
        {
            for (int i = 0; i < TOP; i++)
            {
                if (topNames[i] != name || topTimes[i] < 0f) continue;
                if (topTimes[i] <= lap) return;   // 前の記録のほうが速い
                for (int k = i; k < TOP - 1; k++) { topNames[k] = topNames[k + 1]; topTimes[k] = topTimes[k + 1]; }
                topNames[TOP - 1] = "";
                topTimes[TOP - 1] = -1f;
                break;
            }
            int at = -1;
            for (int i = 0; i < TOP; i++)
                if (topTimes[i] < 0f || lap < topTimes[i]) { at = i; break; }
            if (at < 0) return;
            for (int k = TOP - 1; k > at; k--) { topNames[k] = topNames[k - 1]; topTimes[k] = topTimes[k - 1]; }
            topNames[at] = name;
            topTimes[at] = lap;
            RequestSerialization();
            Refresh();
        }

        public override void OnDeserialization()
        {
            Refresh();
        }

        public static string FormatLap(float t)
        {
            if (t < 0f) return "―";
            int cs = Mathf.RoundToInt(t * 100f);
            int s = cs / 100;
            int r = cs % 100;
            return s.ToString() + "." + (r < 10 ? "0" : "") + r.ToString() + "秒";
        }

        private void Refresh()
        {
            if (boardText == null) return;
            string s = "<b><size=130%>往復タイム</size></b>\n<size=80%>南の門 → 北の門 → 南の門</size>\n";
            for (int i = 0; i < TOP; i++)
            {
                if (topTimes[i] < 0f) continue;
                s += HudMath.Row2((i + 1).ToString() + "位", HudMath.ShortName(topNames[i], HudMath.SHORT_NAME_MAX) + "  " + FormatLap(topTimes[i])) + "\n";
            }
            s += "<size=85%>自分：最高 " + FormatLap(best) + "　前回 " + FormatLap(last);
            if (lapStart >= 0f) s += "　計測中 " + FormatLap(Time.time - lapStart);
            s += "\n的に当てた数 " + targetHits.ToString() + "</size>";
            if (boardText.text != s) boardText.text = s;
        }
    }
}
