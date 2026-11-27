using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 砂塵の砂煙（1 つの AbilityCaster に 1 つ）。見た目は全クライアントで出す（AbilityCaster.FxDust から Show）。
    /// 発動者のクライアントだけ detect = true で、checkInterval ごとに OverlapSphere で中にいる相手を探し、
    /// AbilityCaster.ReportDustHit で相手のオーナーへ知らせる（同じ相手は dustHitSeconds 経つまで再送しない）。
    /// visual は半径 baseRadius で作っておく（球状のパーティクルなど）。Start で親から外す（乗り物と一緒に動かない）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DustCloud : UdonSharpBehaviour
    {
        public const int MAX_TRACKED = 16;

        public GameObject visual;
        public ParticleSystem particles;
        [Tooltip("visual を作ったときの半径（m）。Show の半径に合わせて拡大する")]
        public float baseRadius = 1f;
        public float checkInterval = 0.25f;
        public AudioSource whoosh;

        private bool active;
        private float until;
        private float nextCheck;
        private float radius;
        private Vector3 center;
        private bool detect;
        private AbilityCaster owner;
        private MountController[] hitMounts;
        private float[] hitTimes;
        private bool started;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            hitMounts = new MountController[MAX_TRACKED];
            hitTimes = new float[MAX_TRACKED];
            transform.parent = null;
            if (visual != null && !active) visual.SetActive(false);
        }

        public void Show(Vector3 pos, float r, float duration, AbilityCaster caster, bool detectHits)
        {
            EnsureInit();
            center = pos;
            radius = r;
            owner = caster;
            detect = detectHits;
            until = Time.time + duration;
            nextCheck = 0f;
            active = true;
            for (int i = 0; i < MAX_TRACKED; i++) { hitMounts[i] = null; hitTimes[i] = 0f; }
            transform.position = pos;
            float s = baseRadius > 0.01f ? r / baseRadius : r;
            transform.localScale = new Vector3(s, s, s);
            if (visual != null) visual.SetActive(true);
            if (particles != null) particles.Play();
            if (whoosh != null) whoosh.Play();
        }

        private void Update()
        {
            if (!active) return;
            if (Time.time >= until)
            {
                active = false;
                if (particles != null) particles.Stop();
                if (visual != null) visual.SetActive(false);
                return;
            }
            if (!detect || owner == null || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            Collider[] cs = Physics.OverlapSphere(center, radius, owner.targetMask, QueryTriggerInteraction.Collide);
            if (cs == null) return;
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null) continue;
                MountController m = cs[i].GetComponentInParent<MountController>();
                if (m == null || m == owner.mount || m.riderPlayerId < 0) continue;
                if (RecentlyHit(m)) continue;
                owner.ReportDustHit(m);
            }
        }

        /// <summary>直近 dustHitSeconds 以内に当てた相手なら true。そうでなければ今の時刻を記録して false。</summary>
        private bool RecentlyHit(MountController m)
        {
            int free = -1;
            int oldest = 0;
            for (int i = 0; i < MAX_TRACKED; i++)
            {
                if (hitMounts[i] == m)
                {
                    if (Time.time - hitTimes[i] < owner.dustHitSeconds) return true;
                    hitTimes[i] = Time.time;
                    return false;
                }
                if (hitMounts[i] == null && free < 0) free = i;
                if (hitTimes[i] < hitTimes[oldest]) oldest = i;
            }
            int k = free >= 0 ? free : oldest;
            hitMounts[k] = m;
            hitTimes[k] = Time.time;
            return false;
        }
    }
}
