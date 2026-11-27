using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 蜃気楼の身代わり（system-r3-10）。1 つの AbilityCaster に 2 体。見た目は乗り物と同じモデルの写し（MountBuilder.BuildVisual：
    /// Horse / Camel / SkeletonHorse と毛色のマテリアル、同じ Mount.controller）で、乗り手はいない。新しいモデルは作らない。
    /// AbilityCaster.FxMirage から全員の画面で Show：発動者の向き・速さでまっすぐ走り（駆歩か襲歩のアニメ）、横へ ±3m ずれていく。
    /// 当たり判定は targetMask のレイヤーの Is Trigger の箱（投げ縄・リボルバー・鉄球が当たると Pop：ばてたときの砂ぼこりで消える）。
    /// 地面には下向きのレイで沿わせる。Start で親から外す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MirageDouble : UdonSharpBehaviour
    {
        [Tooltip("身代わりを出した AbilityCaster")]
        public AbilityCaster caster;
        [Tooltip("caster.doubles の中の番号（FxPopDouble の引数）")]
        public int index;
        public MountVisual visual;
        [Tooltip("見た目の親（隠す・出す）")]
        public GameObject body;
        public Collider hitBox;
        [Tooltip("消えるときの砂ぼこり（MountVisual.exhaustPuff と同じ作り）")]
        public ParticleSystem popDust;
        public LayerMask groundMask = -1;

        private bool shown;
        private float startTime;
        private float until;
        private float seconds;
        private Vector3 origin;
        private Vector3 fwd;
        private Vector3 right;
        private float speed;
        private int mountKind;
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
            if (!shown) SetShown(false);
        }

        public bool IsShown()
        {
            return shown;
        }

        /// <summary>pos から yaw の向きに speed（m/s）で secs 秒走らせる。i = 何体目（ずれる向き）。</summary>
        public void Show(Vector3 pos, float yaw, float spd, int mount, int coat, float secs, int i)
        {
            EnsureInit();
            fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            right = new Vector3(fwd.z, 0f, -fwd.x);
            origin = pos;
            // 止まっていても駆歩で走り出す（身代わりが立ち止まっていると一目で分かる）
            float canter = MountMath.GaitSpeed(mount, MountMath.GAIT_CANTER);
            speed = Mathf.Max(spd, canter);
            mountKind = mount;
            seconds = secs;
            startTime = Time.time;
            until = Time.time + secs;
            index = i;
            if (visual != null) visual.Apply(mount, coat);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            SetShown(true);
        }

        /// <summary>当たった・時間切れ：砂ぼこりで消える。</summary>
        public void Pop()
        {
            if (!shown) return;
            if (popDust != null)
            {
                popDust.transform.position = transform.position + Vector3.up * 0.6f;
                popDust.Emit(36);
            }
            SetShown(false);
        }

        private void SetShown(bool v)
        {
            shown = v;
            if (body != null && body.activeSelf != v) body.SetActive(v);
            if (hitBox != null) hitBox.enabled = v;
        }

        private void Update()
        {
            if (!shown) return;
            float now = Time.time;
            if (now >= until) { Pop(); return; }
            float t = now - startTime;
            Vector3 p = origin + fwd * (speed * t) + right * AbilityMath.MirageDrift(index, t, seconds);
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out hit, 40f, groundMask, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            transform.position = p;
            if (visual != null)
            {
                int gait = MountController.ComputeAnimGait(mountKind, speed);
                if (gait < MountController.ANIM_CANTER) gait = MountController.ANIM_CANTER;
                visual.SetMotion(gait, speed, mountKind, 100f);
            }
        }
    }
}
