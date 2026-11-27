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
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RockWall : UdonSharpBehaviour
    {
        public GameObject[] segments;
        [Tooltip("壁の高さ（せり上がる量）")]
        public float height = 4f;
        public float riseTime = 0.4f;
        public float sinkTime = 0.6f;
        public LayerMask groundMask = -1;
        public float groundProbe = 40f;
        public AudioSource riseSound;

        private bool active;
        private float startTime;
        private float endTime;
        private int count;
        private Vector3[] basePos;
        private Vector3[] baseScale;
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
            for (int i = 0; i < n; i++) if (segments[i] != null) baseScale[i] = segments[i].transform.localScale;
            if (!active) for (int i = 0; i < n; i++) if (segments[i] != null) segments[i].SetActive(false);
        }

        public void Show(Vector3 center, float yaw, int wallCount, float width, float seconds)
        {
            EnsureInit();
            if (segments == null) return;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = rot * Vector3.right;
            count = Mathf.Min(wallCount, segments.Length);
            if (width <= 0f) width = AbilityMath.WALL_WIDTH;
            float sx = width / AbilityMath.WALL_WIDTH;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                if (i >= count) { segments[i].SetActive(false); continue; }
                float off = (i - (count - 1) * 0.5f) * width;
                Vector3 p = center + right * off;
                RaycastHit hit;
                if (Physics.Raycast(p + Vector3.up * groundProbe, Vector3.down, out hit, groundProbe * 2f, groundMask, QueryTriggerInteraction.Ignore))
                    p.y = hit.point.y;
                basePos[i] = p;
                segments[i].transform.SetPositionAndRotation(p - Vector3.up * height, rot);
                Vector3 bs = baseScale[i];
                segments[i].transform.localScale = new Vector3(bs.x * sx, bs.y, bs.z);
                segments[i].SetActive(true);
            }
            startTime = Time.time;
            endTime = Time.time + seconds;
            active = true;
            if (riseSound != null) riseSound.Play();
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
                if (segments[i] == null) continue;
                segments[i].transform.position = basePos[i] - Vector3.up * (height * (1f - f));
            }
        }
    }
}
