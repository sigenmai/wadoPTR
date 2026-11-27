using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 火薬樽（system-r3-11）。1 つの AbilityCaster に 2 個。見た目は素材の樽（FarmProps_15 ×0.6）、導火線は小さな火花のパーティクル、
    /// 爆発は砂ぼこりのパーティクルと SHOT・THUD の音（新しいモデルは作らない）。
    /// AbilityCaster.FxBomb から全員の画面で Arm（地面に置く）。導火線 3 秒か、置いた本人以外の乗り物が 3m 以内に入ったら爆発。
    /// 当たり（system-r3-03 と同じく被害者が決める）：爆発の瞬間、このクライアントの乗り手の乗り物が半径の内なら AbilityCaster.HitBombLocal。
    /// 練習のカカシは置いた本人の画面（detect）だけ揺らす。Start で親から外す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PowderKeg : UdonSharpBehaviour
    {
        public GameObject visual;
        public ParticleSystem fuse;
        public ParticleSystem blast;
        public AudioSource blastShot;
        public AudioSource blastThud;
        public LayerMask groundMask = -1;
        [Tooltip("乗り物の体（近づいたら火が回る）")]
        public LayerMask mountMask = 0;
        [Tooltip("練習のカカシ（targetMask）")]
        public LayerMask targetMask = 0;

        private bool armed;
        private float armedAt;
        private float radius;
        private bool detect;
        private AbilityCaster owner;
        private float nextProbe;
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
            if (!armed && visual != null) visual.SetActive(false);
        }

        public void Arm(Vector3 pos, float r, AbilityCaster caster, bool detectHits)
        {
            EnsureInit();
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out hit, 40f, groundMask, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
            transform.position = pos;
            radius = r;
            owner = caster;
            detect = detectHits;
            armedAt = Time.time;
            armed = true;
            nextProbe = 0f;
            if (visual != null) visual.SetActive(true);
            if (fuse != null) fuse.Play();
        }

        private void Update()
        {
            if (!armed) return;
            float age = Time.time - armedAt;
            float nearest = 1000f;
            if (Time.time >= nextProbe && mountMask.value != 0)
            {
                nextProbe = Time.time + 0.1f;
                Collider[] cs = Physics.OverlapSphere(transform.position, AbilityMath.BOMB_PROXIMITY, mountMask.value, QueryTriggerInteraction.Ignore);
                if (cs != null)
                {
                    for (int i = 0; i < cs.Length; i++)
                    {
                        if (cs[i] == null) continue;
                        MountController m = cs[i].GetComponentInParent<MountController>();
                        if (m == null || m.riderPlayerId < 0 || m.IsMirage()) continue;
                        if (owner != null && m == owner.mount && age < AbilityMath.BOMB_OWNER_SAFE) continue;
                        Vector3 d = m.transform.position - transform.position;
                        d.y = 0f;
                        if (d.magnitude < nearest) nearest = d.magnitude;
                    }
                }
            }
            if (AbilityMath.BombDetonates(age, nearest)) Explode();
        }

        private void Explode()
        {
            armed = false;
            Vector3 c = transform.position;
            if (visual != null) visual.SetActive(false);
            if (fuse != null) fuse.Stop();
            if (blast != null) blast.Emit(48);
            if (blastShot != null) blastShot.Play();
            if (blastThud != null) blastThud.Play();
            if (owner == null) return;
            // 被害者が決める：このクライアントの乗り手の乗り物だけ
            if (mountMask.value != 0)
            {
                Collider[] cs = Physics.OverlapSphere(c, radius + 1.5f, mountMask.value, QueryTriggerInteraction.Ignore);
                if (cs != null)
                {
                    for (int i = 0; i < cs.Length; i++)
                    {
                        if (cs[i] == null) continue;
                        MountController m = cs[i].GetComponentInParent<MountController>();
                        if (m == null || !m.IsLocalRider() || m == owner.mount || m.IsMirage()) continue;
                        if (!AbilityMath.InBlast(c, m.transform.position, radius)) continue;
                        AbilityCaster tc = m.GetComponent<AbilityCaster>();
                        if (tc != null) tc.HitBombLocal(c, owner);
                        break;
                    }
                }
            }
            if (detect && targetMask.value != 0)
            {
                Collider[] ds = Physics.OverlapSphere(c, radius, targetMask.value, QueryTriggerInteraction.Collide);
                if (ds != null)
                    for (int i = 0; i < ds.Length; i++)
                    {
                        if (ds[i] == null) continue;
                        PracticeDummy pd = ds[i].GetComponentInParent<PracticeDummy>();
                        if (pd != null) pd.Wobble();
                    }
            }
        }
    }
}
