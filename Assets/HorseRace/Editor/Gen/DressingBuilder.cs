using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージ・ロビーの飾りの実体化。何をどこに置くかは DressingPlan（純粋・単体テスト済み）が決め、ここでは
    /// Placement を 1 つずつモデルにする：パックの部品（GenUtil.InstantiatePart）かモデル全体（GenUtil.PlaceModel）、
    /// 横倒し（骨の馬）、Collider（DressingPlan.SHAPE_*：MeshCollider / 外形の BoxCollider / 幹だけ、すべて Block）、
    /// 背景専用（メサ・厩舎）は BackgroundWalls、すべて Static（バッチング用）。骨の馬は Animator を外して動かない飾りにする。
    /// 境目ステージは表彰台（ShowBuilder.BuildPodium）もここで置く。
    /// </summary>
    public static class DressingBuilder
    {
        static readonly Color Fallback = new Color(0.6f, 0.45f, 0.35f);

        public static void BuildStage(StageBuild sb, WorldContext ctx)
        {
            var root = GenUtil.Child(sb.root, "Dressing", Vector3.zero).transform;
            root.position = Vector3.zero;
            List<Placement> plan = DressingPlan.For(sb.def, sb.courseLocal, sb.heights, sb.goalArc, sb.shortcut);
            Instantiate(plan, root, sb.def.Origin);
            if (sb.def.kind == HeightmapGen.KIND_MESA)
            {
                Vector3 pp = DressingPlan.PodiumLocal(sb.courseLocal, sb.goalArc);
                float pa = sb.goalArc + 45f;
                Vector3 pr = WorldLayout.RightOf(WorldLayout.TangentAtArc(sb.courseLocal, pa));
                sb.goalTownCenter = sb.Ground(pp.x, pp.z);
                var town = GenUtil.Child(root, "Podium", Vector3.zero).transform;
                town.position = Vector3.zero;
                ctx.podium = ShowBuilder.BuildPodium(town, sb.goalTownCenter, WorldLayout.YawDeg(-pr));
            }
            Debug.Log("[HorseRace] ステージ" + (sb.def.index + 1) + " の飾り: " + plan.Count + " 個（上限 " + DressingPlan.MAX_PER_STAGE + "）");
        }

        /// <summary>計画をモデルにする。origin = 計画の座標の原点（Terrain の角、ロビーなら中心）。グループごとに親を分ける。</summary>
        public static void Instantiate(List<Placement> plan, Transform root, Vector3 origin)
        {
            var groups = new Dictionary<string, Transform>();
            foreach (Placement p in plan)
            {
                Transform parent;
                if (!groups.TryGetValue(p.group ?? "Misc", out parent))
                {
                    parent = GenUtil.Child(root, p.group ?? "Misc", Vector3.zero).transform;
                    parent.position = Vector3.zero;
                    groups[p.group ?? "Misc"] = parent;
                }
                Vector3 w = origin + p.position;
                string path = GenUtil.MODELS + "/" + p.asset;
                float s = Mathf.Max(0.01f, p.scale);
                var fb = new Vector3(p.radius * 1.4f / s, Mathf.Max(0.2f, p.height / s), p.radius * 1.4f / s);
                GameObject go = p.part != null
                    ? GenUtil.InstantiatePart(path, p.part, parent, w, p.yawDeg, p.scale, fb, Fallback)
                    : GenUtil.PlaceModel(path, parent, System.IO.Path.GetFileNameWithoutExtension(p.asset), w, p.yawDeg, p.scale, fb, Fallback);
                if (p.rollDeg != 0f) go.transform.rotation = Quaternion.Euler(0f, p.yawDeg, p.rollDeg);
                if (p.asset == DressingPlan.SKELETON)
                    foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                if (p.collider)
                {
                    if (p.shape == DressingPlan.SHAPE_MESH) AddCollider(go, true);
                    else if (p.shape == DressingPlan.SHAPE_TRUNK) AddTrunkCollider(go, Mathf.Clamp(p.radius * 0.35f, 0.4f, 2.5f));
                    else AddCollider(go, false);
                }
                if (p.background && (p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB || p.asset == DressingPlan.STABLE))
                    BackgroundWalls(go, 6f);
                SetStatic(go);
            }
        }

        public static void SetStatic(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
        }

        /// <summary>高さ 1.2m 以上なら Block レイヤーの Collider（mesh=true なら各メッシュに MeshCollider、それ以外は外形の BoxCollider）。</summary>
        public static void AddCollider(GameObject go, bool mesh)
        {
            Bounds b = GenUtil.RendererBounds(go);
            if (b.size.y < 1.2f) return;
            if (mesh)
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(b.center);
                Vector3 s = go.transform.lossyScale;
                box.size = new Vector3(b.size.x / Mathf.Max(0.01f, s.x), b.size.y / Mathf.Max(0.01f, s.y), b.size.z / Mathf.Max(0.01f, s.z)) * 0.85f;
            }
            GenUtil.SetLayerRecursive(go, WorldLayers.Block);
        }

        /// <summary>木・サボテン：幹だけの細い BoxCollider（Block）。</summary>
        public static void AddTrunkCollider(GameObject go, float width)
        {
            Bounds b = GenUtil.RendererBounds(go);
            var box = go.AddComponent<BoxCollider>();
            Vector3 s = go.transform.lossyScale;
            box.center = new Vector3(0f, b.size.y * 0.5f / Mathf.Max(0.01f, s.y), 0f);
            box.size = new Vector3(width / Mathf.Max(0.01f, s.x), b.size.y / Mathf.Max(0.01f, s.y), width / Mathf.Max(0.01f, s.z));
            GenUtil.SetLayerRecursive(go, WorldLayers.Block);
        }

        /// <summary>
        /// 背景専用（spec 5.12b）：外形を margin m 広げた長方形の 4 辺に、見えない BoxCollider の壁（Block、高さ +20m）を立てる。
        /// 乗り物（obstacleMask）も徒歩のプレイヤーも中へ入れない。
        /// </summary>
        public static GameObject BackgroundWalls(GameObject target, float margin)
        {
            Bounds b = GenUtil.RendererBounds(target);
            var walls = new GameObject(target.name + "_InvisibleWalls");
            walls.transform.SetParent(target.transform.parent, false);
            walls.transform.position = new Vector3(b.center.x, b.min.y - 2f, b.center.z);
            walls.transform.rotation = Quaternion.identity;
            float w = b.size.x + margin * 2f, d = b.size.z + margin * 2f, h = b.size.y + 22f;
            const float T = 1f;
            AddWall(walls.transform, "N", new Vector3(0f, h * 0.5f, d * 0.5f), new Vector3(w + T, h, T));
            AddWall(walls.transform, "S", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w + T, h, T));
            AddWall(walls.transform, "E", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(T, h, d + T));
            AddWall(walls.transform, "W", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(T, h, d + T));
            GenUtil.SetLayerRecursive(walls, WorldLayers.Block);
            return walls;
        }

        static void AddWall(Transform parent, string name, Vector3 local, Vector3 size)
        {
            var go = GenUtil.Child(parent, name, local);
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
        }
    }
}
