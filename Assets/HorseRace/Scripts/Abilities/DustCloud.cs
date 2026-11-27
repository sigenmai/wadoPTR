using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 砂塵の砂煙（1 つの AbilityCaster に 1 つ）。見た目は全クライアントで出す（AbilityCaster.FxDust から Show）。
    /// 砂煙は発動者の乗り物の dustBehind m 後ろについて動く（system-r2-09：後ろにつく相手への対抗手段。各クライアントが
    /// 自分の画面の発動者の乗り物を追うので、新しい同期はいらない）。パーティクルはワールド空間なので、動くと後ろに尾を引く。
    /// 当たり（system-r3-03）：被害者が決める。各クライアントの MountController.ScanMounts（自分の乗り物、0.1 秒ごと）が、自分の画面に描かれた
    /// 相手の砂煙の中に自分がいるかを Contains で調べ、TakeVictimHit（dustHitSeconds ごとに当たり直す）が true なら AbilityCaster.HitDustLocal。
    /// 相手の乗り物は 0.22 秒＋遅延だけ遅れて描かれるので、発動者の画面で判定すると被害者の画面では砂煙を抜けても何も起きなかった。
    /// 中にいる間はドラフトしない（同じ ScanMounts）。
    /// visual は半径 baseRadius で作っておく（球状のパーティクルなど）。Start で親から外す。
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
        private AbilityCaster owner;
        /// <summary>このクライアントの乗り手（被害者）に最後に当たった時刻（砂煙ごと、Show で戻す）。</summary>
        private float victimHitAt = -1000f;
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

        public void Show(Vector3 pos, float r, float duration, AbilityCaster caster, bool detectHits)
        {
            EnsureInit();
            center = pos;
            radius = r;
            owner = caster;
            until = Time.time + duration;
            nextCheck = 0f;
            active = true;
            victimHitAt = -1000f;
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
            Follow();
        }

        /// <summary>発動者の乗り物の後ろへ（乗り物が無い・練習用でも同じ）。</summary>
        private void Follow()
        {
            if (owner == null || owner.mount == null) return;
            Transform mt = owner.mount.transform;
            Vector3 f = mt.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward; else f = f.normalized;
            center = mt.position - f * owner.dustBehind;
            transform.position = center;
        }

        /// <summary>点 p が今出ている砂煙の中か（水平距離）。</summary>
        public bool Contains(Vector3 p)
        {
            if (!active) return false;
            float dx = p.x - center.x, dz = p.z - center.z;
            return dx * dx + dz * dz < radius * radius;
        }

        /// <summary>
        /// 被害者のクライアントで：今当たりにしてよいか（前の当たりから dustHitSeconds たった）。true なら時刻を記録する。
        /// </summary>
        public bool TakeVictimHit()
        {
            if (!active) return false;
            float hs = owner != null ? owner.dustHitSeconds : AbilityMath.DUST_HIT_SECONDS;
            if (Time.time - victimHitAt < hs) return false;
            victimHitAt = Time.time;
            return true;
        }
    }
}
