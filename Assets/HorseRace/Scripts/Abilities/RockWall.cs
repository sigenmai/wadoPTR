using UdonSharp;
using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 岩壁（1 つの AbilityCaster に 1 つ、壁は最大 3 枚）。全クライアントで AbilityCaster.FxWall から Show。
    /// 各クライアントが自分で地面を下向きレイキャストして高さを合わせ、riseTime で地面からせり上がり、seconds 後に沈んで消える。
    /// 壁の幅は Lv で変わる（AbilityMath.WallWidth: 6/7.5/9m）。segments の各壁は幅 AbilityMath.WALL_WIDTH（6m）で作っておけば、
    /// 幅に合わせて localScale.x を拡大する。各壁には Collider を付けて MountController の Obstacle Mask に含まれるレイヤーに置く
    /// （Ground Mask には含めない）。見た目は Packs/Cliffs か Packs/DesertRocks の岩。Start で親から外す。
    /// 道をふさぎきらない（system-r2-10）：
    ///  - 狙った点から左右へ obstacleMask（岩・建物・見えない柵）までの幅を測り、AbilityMath.WallSpanFor で全長を決める（隙間 6m 以上）。
    ///    隙間がひと続きで片側に残るよう AbilityMath.WallOffsetFor で寄せ、入らない壁は外側から外す。
    ///  - 乗り物の足元（mountMask の箱）には立てない。地面が狙った点より 3m 以上低い（橋の下・溝）所にも立てない。
    ///  - 発動者は最初の 2 秒、自分の壁に当たらない（IgnoresFor）。
    /// 崩れる（system-r3-06）：乗り物が 1 枚に当たると、当たった本人のクライアントがその 1 枚を崩し（Crumble：隠して砂ぼこり）、
    ///   AbilityCaster.FxCrumble で全員の画面でも崩す。1 枚で遅れるのは 1 人・約 10m まで。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RockWall : UdonSharpBehaviour
    {
        public const float PROBE_M = 40f;
        public const float BRIDGE_DROP_M = 3f;
        public const float OWNER_PASS_SECONDS = 2f;
        public const float SEGMENT_DEPTH = 1.8f;

        public GameObject[] segments;
        [Tooltip("壁の高さ（せり上がる量）")]
        public float height = 4f;
        public float riseTime = 0.4f;
        public float sinkTime = 0.6f;
        public LayerMask groundMask = -1;
        [Tooltip("道の空いている幅を測る相手（岩・建物・見えない柵。WorldLayers.ObstacleMask）")]
        public LayerMask obstacleMask = 0;
        [Tooltip("乗り物の体（足元に壁を立てない）")]
        public LayerMask mountMask = 0;
        public float groundProbe = 40f;
        public AudioSource riseSound;
        [Tooltip("この壁を出す AbilityCaster（FxCrumble を全員に送る）。MountBuilder が入れる")]
        public AbilityCaster caster;
        [Tooltip("崩れたときの砂ぼこり（ワールド空間のパーティクル）")]
        public ParticleSystem crumbleDust;
        public AudioSource crumbleSound;

        /// <summary>この壁を出した乗り物（Show で入れる）。</summary>
        [HideInInspector] public MountController OwnerMount;

        private bool active;
        private float startTime;
        private float endTime;
        private int count;
        private Vector3[] basePos;
        private Vector3[] baseScale;
        private bool[] used;
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
            int n = segments != null ? segments.Length : 0;
            basePos = new Vector3[n];
            baseScale = new Vector3[n];
            used = new bool[n];
            for (int i = 0; i < n; i++) if (segments[i] != null) baseScale[i] = segments[i].transform.localScale;
            if (!active) for (int i = 0; i < n; i++) if (segments[i] != null) segments[i].SetActive(false);
        }

        public void Show(Vector3 center, float yaw, int wallCount, float width, float seconds)
        {
            ShowFor(center, yaw, wallCount, width, seconds, null, 1);
        }

        /// <summary>owner = 発動者の乗り物、lv = 能力の Lv（壁の全長の上限）。</summary>
        public void ShowFor(Vector3 center, float yaw, int wallCount, float width, float seconds, MountController owner, int lv)
        {
            EnsureInit();
            if (segments == null) return;
            OwnerMount = owner;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = rot * Vector3.right;
            if (width <= 0f) width = AbilityMath.WALL_WIDTH;

            // 狙った点の地面
            RaycastHit hit;
            float centerGround = center.y;
            if (Physics.Raycast(center + Vector3.up * groundProbe, Vector3.down, out hit, groundProbe * 2f, groundMask, QueryTriggerInteraction.Ignore))
                centerGround = hit.point.y;
            Vector3 c = new Vector3(center.x, centerGround, center.z);

            // 左右の空いている幅（胸の高さで障害物まで）
            float left = PROBE_M, right2 = PROBE_M;
            if (obstacleMask.value != 0)
            {
                Vector3 o = c + Vector3.up * 1.2f;
                if (Physics.Raycast(o, -right, out hit, PROBE_M, obstacleMask.value, QueryTriggerInteraction.Ignore)) left = hit.distance;
                if (Physics.Raycast(o, right, out hit, PROBE_M, obstacleMask.value, QueryTriggerInteraction.Ignore)) right2 = hit.distance;
            }
            float span = Mathf.Min(AbilityMath.WallSpanFor(left + right2, lv), Mathf.Min(wallCount, segments.Length) * width);
            int n = Mathf.Min(AbilityMath.WallSegmentsFor(span, lv), Mathf.Min(wallCount, segments.Length));
            if (n == 1 && span < width) width = span;
            float total = n * width;
            float shift = AbilityMath.WallOffsetFor(left, right2, total);
            float sx = width / AbilityMath.WALL_WIDTH;
            count = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                used[i] = false;
                if (segments[i] == null) continue;
                if (i >= n) { segments[i].SetActive(false); continue; }
                float off = shift + (i - (n - 1) * 0.5f) * width;
                Vector3 p = c + right * off;
                bool ok = true;
                if (Physics.Raycast(p + Vector3.up * groundProbe, Vector3.down, out hit, groundProbe * 2f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    p.y = hit.point.y;
                    if (p.y < centerGround - BRIDGE_DROP_M) ok = false;   // 橋の脇の溝・崖の下には立てない
                }
                else ok = false;
                // 乗り物の足元には立てない（挟まって動けなくなる）
                if (ok && mountMask.value != 0
                    && Physics.CheckBox(p + Vector3.up * (height * 0.5f), new Vector3(width * 0.5f, height * 0.5f, SEGMENT_DEPTH * 0.5f + 0.5f), rot,
                        mountMask.value, QueryTriggerInteraction.Ignore)) ok = false;
                if (!ok) { segments[i].SetActive(false); continue; }
                basePos[i] = p;
                used[i] = true;
                segments[i].transform.SetPositionAndRotation(p - Vector3.up * height, rot);
                Vector3 bs = baseScale[i];
                segments[i].transform.localScale = new Vector3(bs.x * sx, bs.y, bs.z);
                segments[i].SetActive(true);
                count = i + 1;
            }
            startTime = Time.time;
            endTime = Time.time + seconds;
            active = true;
            if (riseSound != null) riseSound.Play();
        }

        /// <summary>c が今出ている壁の何枚目か（-1 = この壁ではない）。</summary>
        public int SegmentOf(Collider c)
        {
            if (!active || c == null || segments == null) return -1;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null || !used[i]) continue;
                if (c.transform == segments[i].transform || c.transform.IsChildOf(segments[i].transform)) return i;
            }
            return -1;
        }

        /// <summary>壁の 1 枚を崩す（隠して砂ぼこり）。何度呼んでもよい（全員に届く FxCrumble と、当たった本人の即時の呼び出し）。</summary>
        public void Crumble(int seg)
        {
            EnsureInit();
            if (segments == null || seg < 0 || seg >= segments.Length || segments[seg] == null) return;
            if (!used[seg]) return;
            used[seg] = false;
            Vector3 p = segments[seg].transform.position + Vector3.up * (height * 0.5f);
            segments[seg].SetActive(false);
            if (crumbleDust != null)
            {
                crumbleDust.transform.position = p;
                crumbleDust.Emit(40);
            }
            if (crumbleSound != null) crumbleSound.Play();
        }

        /// <summary>発動者の乗り物が出してから 2 秒の間、自分の壁の当たりを無視するか（MountController の正面の障害物の判定）。</summary>
        public bool IgnoresFor(Collider c, MountController m)
        {
            if (!active || c == null || m == null || m != OwnerMount) return false;
            if (Time.time - startTime > OWNER_PASS_SECONDS) return false;
            if (segments == null) return false;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null || !used[i]) continue;
                if (c.transform.IsChildOf(segments[i].transform)) return true;
            }
            return false;
        }

        private void Update()
        {
            if (!active) return;
            float now = Time.time;
            if (now >= endTime)
            {
                active = false;
                for (int i = 0; i < segments.Length; i++) if (segments[i] != null) segments[i].SetActive(false);
                return;
            }
            float rise = riseTime > 0f ? Mathf.Clamp01((now - startTime) / riseTime) : 1f;
            float sink = (sinkTime > 0f && now > endTime - sinkTime) ? Mathf.Clamp01((endTime - now) / sinkTime) : 1f;
            float f = Mathf.Min(rise, sink);
            for (int i = 0; i < count; i++)
            {
                if (segments[i] == null || !used[i]) continue;
                segments[i].transform.position = basePos[i] - Vector3.up * (height * (1f - f));
            }
        }
    }
}
