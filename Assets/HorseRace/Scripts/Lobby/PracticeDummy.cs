using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace HorseRace
{
    /// <summary>
    /// 練習場のカカシ。Collider（AbilityCaster.targetMask に含まれるレイヤー）と同じか親の GameObject に置く。
    /// リボルバー・投げ縄・鉄球が当たると揺れる（全員に見える）。リボルバーは命中扱い（外しの減速なし）。効果（減速など）はない。
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

        private float wobbleStart = -100f;
        private Quaternion home;
        private bool wobbling;
        private bool started;

        private void Start()
        {
            if (pivot == null) pivot = transform;
            home = pivot.localRotation;
            started = true;
        }

        /// <summary>当てた人のクライアントから呼ぶ。全員で揺らす。</summary>
        public void Wobble()
        {
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
