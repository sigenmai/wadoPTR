using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 練習場のカカシ。Collider（AbilityCaster.targetMask に含まれるレイヤー）と同じか親の GameObject に置く。
    /// リボルバー・投げ縄・鉄球が当たると揺れる（全員に見える）。リボルバーは命中扱い（外しの減速なし）。効果（減速など）はない。
    /// 当てた人の画面で練習場の板（PracticeLap）の「的に当てた数」が +1（system-r1-18）。
    /// moveAmplitude &gt; 0 なら左右（ローカル X）に行き来する動く的（サーバー時刻で動かすので全員同じ位置）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class PracticeDummy : UdonSharpBehaviour
    {
        [Tooltip("揺らす部分（根元が回転の中心）")]
        public Transform pivot;
        public float wobbleDeg = 25f;
        public float wobbleTime = 1.2f;
        public float wobbleFrequency = 14f;
        public AudioSource hitSound;
        [Tooltip("当てた数を数える練習場の板（任意）")]
        public PracticeLap scoreBoard;
        [Tooltip("動く的：左右に行き来する幅（m、片側）。0 = 動かない")]
        public float moveAmplitude = 0f;
        public float movePeriod = 6f;
        private Vector3 basePos;

        private float wobbleStart = -100f;
        private Quaternion home;
        private bool wobbling;
        private bool started;

        private void Start()
        {
            if (pivot == null) pivot = transform;
            home = pivot.localRotation;
            basePos = transform.position;
            started = true;
        }

        /// <summary>当てた人のクライアントから呼ぶ。全員で揺らす。</summary>
        public void Wobble()
        {
            if (scoreBoard != null) scoreBoard.AddTargetHit();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxWobble));
        }

        [NetworkCallable(10)]
        public void FxWobble()
        {
            wobbleStart = Time.time;
            wobbling = true;
            if (hitSound != null) hitSound.Play();
        }

        private void Update()
        {
            if (started && moveAmplitude > 0f && movePeriod > 0.1f)
            {
                double st = Networking.GetServerTimeInSeconds();
                float ph = (float)(st % movePeriod) / movePeriod * Mathf.PI * 2f;
                transform.position = basePos + transform.right * (Mathf.Sin(ph) * moveAmplitude);
            }
            if (!wobbling || !started) return;
            float t = Time.time - wobbleStart;
            if (t >= wobbleTime)
            {
                wobbling = false;
                pivot.localRotation = home;
                return;
            }
            float a = wobbleDeg * Mathf.Sin(t * wobbleFrequency) * (1f - t / wobbleTime);
            pivot.localRotation = home * Quaternion.AngleAxis(a, Vector3.forward);
        }
    }
}
