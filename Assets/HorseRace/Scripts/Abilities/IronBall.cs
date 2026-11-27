using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 鉄球 1 個（1 つの AbilityCaster に 3 個）。全クライアントで同じ初速から同じように動かす（Rigidbody は使わない）。
    /// 水平の速度は 1m 進むごとに curveDegPerM 度だけ曲がる（照準のロール角から AbilityCaster が計算）。
    /// 地面（groundMask）で AbilityMath.BALL_BOUNCES 回はねて、その次に着地したら消える。maxLife 秒でも消える。
    /// 当たり（system-r3-03）：被害者が決める。どのクライアントも OverlapSphere を見るが、乗り物は「自分が乗っている乗り物」だけを調べ、
    /// 触れたら自分の AbilityCaster.HitBallLocal（減速・はじき・全員に FxHitConfirm）。ほかの人の乗り物は描かれた位置が遅れているので素通り
    /// （当たったかはその人の画面が決める）。練習のカカシは発動者（detect = true）だけが揺らす。蜃気楼の間の乗り物は素通り、身代わりに当たったら消す。
    /// Start で親から外す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class IronBall : UdonSharpBehaviour
    {
        public GameObject visual;
        public float radius = 0.35f;
        public float hitRadius = 1.0f;
        public float gravity = 20f;
        [Tooltip("はねたときに残る法線方向の速度の割合")]
        public float bounciness = 0.55f;
        [Tooltip("はねたときに残る接線方向の速度の割合")]
        public float friction = 0.85f;
        public float maxLife = 6f;
        public LayerMask groundMask = -1;
        public AudioSource bounceSound;
        public AudioSource hitSound;

        private bool active;
        private Vector3 pos;
        private Vector3 vel;
        private float curve;
        private int bounces;
        private float life;
        private bool detect;
        private int level = 1;
        private AbilityCaster owner;
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
            if (visual != null && !active) visual.SetActive(false);
        }

        public void Launch(Vector3 origin, Vector3 velocity, float curveDegPerM, bool detectHits, AbilityCaster caster)
        {
            Launch(origin, velocity, curveDegPerM, detectHits, caster, 1);
        }

        /// <summary>lv = 発動者の能力 Lv（よろけの秒数。FxBall の個数 = Lv から全員が同じ値を知る）。</summary>
        public void Launch(Vector3 origin, Vector3 velocity, float curveDegPerM, bool detectHits, AbilityCaster caster, int lv)
        {
            level = lv;
            EnsureInit();
            pos = origin;
            vel = velocity;
            curve = curveDegPerM;
            bounces = 0;
            life = 0f;
            detect = detectHits;
            owner = caster;
            active = true;
            transform.position = pos;
            if (visual != null) visual.SetActive(true);
        }

        private void Stop()
        {
            active = false;
            if (visual != null) visual.SetActive(false);
        }

        private void Update()
        {
            if (!active) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            life += dt;
            if (life > maxLife) { Stop(); return; }

            Vector3 h = new Vector3(vel.x, 0f, vel.z);
            float dist = h.magnitude * dt;
            if (curve != 0f && dist > 0f)
            {
                Vector3 hr = Quaternion.AngleAxis(curve * dist, Vector3.up) * h;
                vel = new Vector3(hr.x, vel.y, hr.z);
            }
            vel = vel + Vector3.down * (gravity * dt);

            Vector3 step = vel * dt;
            float len = step.magnitude;
            RaycastHit hit;
            if (len > 0.0001f && Physics.Raycast(pos, step / len, out hit, len + radius, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (bounces >= AbilityMath.BALL_BOUNCES) { Stop(); return; }
                bounces++;
                Vector3 n = hit.normal;
                pos = hit.point + n * radius;
                float vn = Vector3.Dot(vel, n);
                Vector3 normalPart = n * vn;
                Vector3 tangent = vel - normalPart;
                vel = tangent * friction - normalPart * bounciness;
                if (bounceSound != null) bounceSound.Play();
            }
            else
            {
                pos = pos + step;
            }
            transform.position = pos;
            CheckHits();
        }

        private void CheckHits()
        {
            if (owner == null) return;
            Collider[] cs = Physics.OverlapSphere(pos, hitRadius, owner.targetMask, QueryTriggerInteraction.Collide);
            if (cs == null) return;
            for (int i = 0; i < cs.Length; i++)
            {
                if (cs[i] == null) continue;
                MirageDouble md = cs[i].GetComponentInParent<MirageDouble>();
                if (md != null)
                {
                    if (md.caster == owner || !md.IsShown()) continue;
                    md.Pop();
                    if (hitSound != null) hitSound.Play();
                    Stop();
                    return;
                }
                MountController m = cs[i].GetComponentInParent<MountController>();
                if (m != null)
                {
                    if (m == owner.mount || m.riderPlayerId < 0) continue;
                    if (!m.IsLocalRider()) continue;   // ほかの人の乗り物はその人の画面が決める
                    if (m.IsMirage()) continue;        // 蜃気楼の間は素通り
                    AbilityCaster tc = m.GetComponent<AbilityCaster>();
                    if (tc != null) tc.HitBallLocal(vel, level, owner);
                    if (hitSound != null) hitSound.Play();
                    Stop();
                    return;
                }
                PracticeDummy pd = cs[i].GetComponentInParent<PracticeDummy>();
                if (pd != null)
                {
                    if (detect) pd.Wobble();
                    if (hitSound != null) hitSound.Play();
                    Stop();
                    return;
                }
            }
        }
    }
}
