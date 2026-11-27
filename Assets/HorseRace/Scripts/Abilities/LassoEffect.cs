using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 投げ縄の見た目（LineRenderer）。全クライアントで AbilityCaster.FxLasso から Show。
    /// 当たったとき：縄の先を end の近くの乗り物（OverlapSphere で探す）に結び、hold 秒ぴんと張ったまま追いかける。
    /// 外れたとき：end まで伸びて戻る。当たり判定・効果は AbilityCaster 側（発動者のクライアント）。
    /// Start で親から外す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LassoEffect : UdonSharpBehaviour
    {
        public LineRenderer line;
        public int segments = 12;
        public float sag = 1.2f;
        public float throwTime = 0.25f;
        public float retractTime = 0.35f;
        public float handHeight = 1.8f;
        [Tooltip("当たった相手を探すレイヤー（乗り物）")]
        public LayerMask mountMask = -1;
        public float attachSearchRadius = 4f;
        public AudioSource throwSound;

        private bool active;
        private float startTime;
        private float holdSeconds;
        private bool hit;
        private MountController fromMount;
        private Transform target;
        private Vector3 targetOffset;
        private Vector3 endPos;
        private bool started;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            transform.parent = null;
            if (line != null)
            {
                if (segments < 2) segments = 2;
                line.positionCount = segments;
                if (!active) line.enabled = false;
            }
        }

        public void Show(MountController from, Vector3 end, bool didHit, float hold)
        {
            EnsureInit();
            if (from == null || line == null) return;
            fromMount = from;
            endPos = end;
            hit = didHit;
            holdSeconds = hold;
            startTime = Time.time;
            target = null;
            if (hit)
            {
                Collider[] cs = Physics.OverlapSphere(end, attachSearchRadius, mountMask, QueryTriggerInteraction.Collide);
                float best = attachSearchRadius * attachSearchRadius + 1f;
                if (cs != null)
                {
                    for (int i = 0; i < cs.Length; i++)
                    {
                        if (cs[i] == null) continue;
                        MountController m = cs[i].GetComponentInParent<MountController>();
                        if (m == null || m == from) continue;
                        float d = (m.transform.position - end).sqrMagnitude;
                        if (d < best) { best = d; target = m.transform; }
                    }
                }
                if (target != null) targetOffset = end - target.position;
            }
            active = true;
            line.enabled = true;
            if (throwSound != null) throwSound.Play();
        }

        private void Update()
        {
            if (!active) return;
            float t = Time.time - startTime;
            float extend = Mathf.Clamp01(t / throwTime);
            float tension = 0f;
            bool done;
            if (hit)
            {
                if (t > throwTime) tension = 1f;
                done = t > throwTime + holdSeconds;
            }
            else
            {
                if (t > throwTime) extend = 1f - Mathf.Clamp01((t - throwTime) / retractTime);
                done = t > throwTime + retractTime;
            }
            if (done || fromMount == null)
            {
                active = false;
                line.enabled = false;
                return;
            }
            Vector3 a = fromMount.transform.position + Vector3.up * handHeight;
            Vector3 b = target != null ? target.position + targetOffset : endPos;
            Vector3 tip = Vector3.Lerp(a, b, extend);
            float droop = sag * (1f - 0.85f * tension);
            for (int i = 0; i < segments; i++)
            {
                float s = (float)i / (segments - 1);
                Vector3 p = Vector3.Lerp(a, tip, s) - Vector3.up * (droop * 4f * s * (1f - s));
                line.SetPosition(i, p);
            }
        }
    }
}
