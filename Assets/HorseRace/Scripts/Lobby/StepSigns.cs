using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// ロビーの手順の札（ui-r3-11）：「一 手配書を取る」「二 馬を選ぶ」「三 台帳にサイン」「四 スタート台へ」を、手配書の壁・柵の馬・受付・スタート台の手前に 1 枚ずつ。
    /// 0.5 秒ごとに RaceManager のローカルの選択（手配書を取った・乗り物を選んだ）・エントリー・自分の位置（スタート台の近く）を読み、
    /// 済んだ手順は赤インクの ✓、次の手順の札は暖かい灯り（LampWarm、range 4）を点けて 3 秒ごとに文字が跳ねる。ローカルのみ・同期しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StepSigns : UdonSharpBehaviour
    {
        public const float REFRESH_SECONDS = 0.5f;
        public const float PUNCH_EVERY = 3f;

        public RaceManager raceManager;
        [Tooltip("札の文字（一〜四の順）")]
        public TextMeshPro[] stepTexts;
        [Tooltip("札の灯り（一〜四の順。次の手順だけ点ける）")]
        public Light[] stepLights;
        [Tooltip("スタート台（ここから startRadius m 以内なら四は済み）")]
        public Transform startPoint;
        public float startRadius = 4f;

        private int lastMask = -1;
        private int nextStep = -1;
        private float nextRefresh;
        private float punchAt = -10f;
        private Vector3[] baseScale;

        private void Start()
        {
            int n = stepTexts != null ? stepTexts.Length : 0;
            baseScale = new Vector3[n];
            for (int i = 0; i < n; i++) baseScale[i] = stepTexts[i] != null ? stepTexts[i].transform.localScale : Vector3.one;
        }

        private void Update()
        {
            if (raceManager == null || stepTexts == null || baseScale == null) return;
            float now = Time.time;
            if (now >= nextRefresh)
            {
                nextRefresh = now + REFRESH_SECONDS;
                Refresh();
            }
            // 次の手順の札の文字を 3 秒ごとに跳ねさせる
            if (nextStep >= 0 && nextStep < stepTexts.Length && stepTexts[nextStep] != null)
            {
                if (now - punchAt >= PUNCH_EVERY) punchAt = now;
                stepTexts[nextStep].transform.localScale = baseScale[nextStep] * HudMath.PunchScale(now - punchAt, 1.25f);
            }
        }

        private void Refresh()
        {
            RaceManager rm = raceManager;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            bool signed = rm.IsLocalEntered() || rm.IsPending(local.playerId);
            bool atStart = false;
            if (startPoint != null)
            {
                Vector3 d = local.GetPosition() - startPoint.position;
                d.y = 0f;
                atStart = d.magnitude <= startRadius;
            }
            if (rm.state != RaceMath.STATE_LOBBY && rm.IsLocalEntered()) atStart = true;
            int mask = HudMath.LobbyStepMask(rm.localTookCard, rm.localChoseMount, signed, atStart);
            if (mask == lastMask) return;
            lastMask = mask;
            int nx = HudMath.LobbyNextStep(mask);
            if (nextStep >= 0 && nextStep < stepTexts.Length && stepTexts[nextStep] != null) stepTexts[nextStep].transform.localScale = baseScale[nextStep];
            nextStep = nx < HudMath.LOBBY_STEPS ? nx : -1;
            punchAt = Time.time;
            for (int i = 0; i < stepTexts.Length; i++)
                if (stepTexts[i] != null) stepTexts[i].text = HudMath.LobbyStepText(i, mask);
            if (stepLights != null)
                for (int i = 0; i < stepLights.Length; i++)
                    if (stepLights[i] != null) stepLights[i].enabled = i == nextStep;
        }
    }
}
