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
            GenUtil.StageVariant = sb.def.index;   // 境目は紫の夕景の影色のマテリアル
            Instantiate(plan, root, sb.def.Origin, "S" + (sb.def.index + 1));
            GenUtil.StageVariant = -1;
            Atmosphere(sb, root, plan);
            GuidePostSheets(sb, root, plan);
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
        public static void Instantiate(List<Placement> plan, Transform root, Vector3 origin, string scope)
        {
            var groups = new Dictionary<string, Transform>();
            for (int index = 0; index < plan.Count; index++)
            {
                Placement p = plan[index];
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
                if (p.scaleY != 1f || p.scaleZ != 1f) go.transform.localScale = new Vector3(s, s * p.scaleY, s * p.scaleZ);   // 縦に伸ばした岩（art-r2-08）・立てた丸太の杭（art-r3-02）
                // art-r5-04：置き物ごとの色（切り通しの段の明暗）
                if (p.tint != Color.white) GenUtil.RetintToon(go, p.tint, "Tier");
                // 手直し用の安定した id（名前の末尾）。見えない壁の名前にも付くよう、壁を作る前に付ける
                GenUtil.Tag(go, LayoutOverrides.IdFor(scope, p, index));
                // 骨の馬・ラクダは Animator を外して動かない置き物にする（スターターの馬と同じ）
                if (p.asset == DressingPlan.SKELETON || p.asset == DressingPlan.CAMEL || p.asset == DressingPlan.HORSE)   // art-r4-07：広場のつないだ馬
                    foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
                if (p.collider)
                {
                    if (p.shape == DressingPlan.SHAPE_MESH) AddCollider(go, true);
                    else if (p.shape == DressingPlan.SHAPE_TRUNK) AddTrunkCollider(go, Mathf.Clamp(p.radius * 0.35f, 0.4f, 2.5f));
                    else AddCollider(go, false);
                }
                if (p.background && (p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB || p.asset == DressingPlan.STABLE))
                    BackgroundWalls(go, 6f);
                // 建物（正面だけの模型を含む）：裏へ回って面の裏側を見られないよう、すぐ外側を見えない壁で囲む
                else if (p.asset.StartsWith("Buildings/") && (p.collider || p.background))
                    BackgroundWalls(go, 1f);
                SetStatic(go);
            }
        }

        /// <summary>
        /// 近く・中くらいの空気（art-r1-15）と遠くの目印の合図（art-r1-10）：コース沿いに地面すれすれの砂ぼこり（25m の箱、8 か所、風に流れる）、
        /// 野営地の焚き火ごとに炎と煙の柱、遠くの目印（Landmark）の上に 40m 立ちのぼる暗い煙と灯り。
        /// </summary>
        static void Atmosphere(StageBuild sb, Transform root, List<Placement> plan)
        {
            var air = GenUtil.Child(root, "Atmosphere", Vector3.zero).transform;
            air.position = Vector3.zero;
            Vector3 o = sb.def.Origin;
            Color dust = new Color(0.88f, 0.74f, 0.55f, 0.10f);
            Vector3 wind = new Vector3(1.6f, 0.05f, 0.7f);
            const int N = 8;
            for (int i = 0; i < N; i++)
            {
                float a = sb.goalArc * (i + 0.5f) / N;
                Vector3 t = WorldLayout.TangentAtArc(sb.courseLocal, a);
                Vector3 lp = WorldLayout.PointAtArc(sb.courseLocal, a) + WorldLayout.RightOf(t) * ((i % 2 == 0 ? 1f : -1f) * 16f);
                Vector3 p = sb.Ground(lp.x, lp.z);
                GenUtil.DustBox(air, "GroundDust_" + i, p + Vector3.up * 0.8f, new Vector3(25f, 1.2f, 25f), dust, wind, 3f);
            }
            // art-r4-04：切り通しの壁の灯り（壁の真ん中の高さ、日の当たる段を分ける）
            foreach (var lp in DressingPlan.PassWallLights(sb.def, sb.courseLocal, sb.goalArc))
            {
                float a = DressingPlan.NearestArc(sb.courseLocal, DressingPlan.CourseArcs(sb.courseLocal), lp, out float dc);
                Vector3 road = sb.Ground(WorldLayout.PointAtArc(sb.courseLocal, a).x, WorldLayout.PointAtArc(sb.courseLocal, a).z);
                Vector3 g = sb.Ground(lp.x, lp.z);
                float top = Mathf.Max(g.y, road.y) + 8f;
                GenUtil.PointLight(air, "PassWallLight", new Vector3(g.x, road.y + (top - road.y) * lp.y + 2f, g.z), LightingPlan.ForStage(sb.def.index).sunColor,   // art-r5-04：太陽の色
                    DressingPlan.PASS_WALL_LIGHT_RANGE, DressingPlan.PASS_WALL_LIGHT_INTENSITY);
            }
            foreach (var q in plan)
            {
                Vector3 w = o + q.position;
                if (q.asset == DressingPlan.CAMPFIRE)
                {
                    GenUtil.Particles(air, "CampFlames", w + Vector3.up * 0.4f, new Color(1f, 0.55f, 0.15f, 0.9f), 0.35f, 1.2f, 0.8f, true, 20f, 0.3f);
                    GenUtil.SmokeColumn(air, "CampSmoke", w + Vector3.up * 1f, SetPieceBuilder.SmokeGrey, 20f, 1.6f);
                    GenUtil.PointLight(air, "CampLight", w + Vector3.up * 1.2f, new Color(1f, 0.6f, 0.3f), 9f, 1.2f);
                    // 切り通しの尾根の焚き火（art-r3-08）：300m 先から見える暖色の灯り（18m）
                    if (q.group == "MesaGate") GenUtil.PointLight(air, "PassBeacon", w + Vector3.up * 3f, SetPieceBuilder.LampWarm, 18f, 2.2f);
                    // art-r4-15：境目の先へ誘う焚き火の山は 25m の煙の柱と灯り（風車の塔の代わり）
                    if (q.group == "GuideTower")
                    {
                        GenUtil.SmokeColumn(air, "CairnSmoke", w + Vector3.up * 2f, new Color(0.24f, 0.22f, 0.22f, 0.65f), DressingPlan.CAIRN_SMOKE_M, 3.5f);
                        GenUtil.PointLight(air, "CairnLight", w + Vector3.up * 2.5f, SetPieceBuilder.LampWarm, 24f, 2f);
                    }
                }
                else if (q.lamp)
                    GenUtil.PointLight(air, "PostLamp", w + Vector3.up * (q.height * 0.5f + 0.2f), SetPieceBuilder.LampWarm, 12f, 1.5f);   // art-r4-14：野の灯りの柱
                else if (q.part == "FarmProps_02" && (q.group == "MinersCamp" || q.group == "WaterStop" || q.group == "GoalOutpost"
                    || q.group == "StreetDressing" || q.group == "RailHalt" || q.group == "GoalTown" || q.group == "PassLamp"))   // course-r3-02：切り通しの中の灯り
                    GenUtil.PointLight(air, "CampLamp", w + Vector3.up * (2.9f * q.scale), SetPieceBuilder.LampWarm, 12f, 1.5f);   // 見どころ・出張所の街灯（art-r2-05/06）
                else if (q.group == "GiantSkeleton")
                {
                    // 砂丘の上の巨大な骨（art-r2-16）：夕方も見える暖色の灯りと、風下へ流れる砂ぼこり
                    GenUtil.PointLight(air, "GiantSkeletonLight", w + Vector3.up * (q.height * 0.5f), SetPieceBuilder.LampWarm, 40f, 1.4f);
                    GenUtil.DustBox(air, "GiantSkeletonDust", w + Vector3.up * 2f, new Vector3(q.radius * 2f, 2f, q.radius * 2f),
                        new Color(0.92f, 0.78f, 0.56f, 0.12f), new Vector3(2.2f, 0.2f, 0.9f), 4f);
                }
                else if (IsTownBuilding(q))
                {
                    // 町の建物ごとに 1 つ、窓の暖かさ（art-r3-15）：正面（向き yaw）の 3m の高さに 6m の灯り
                    float a = q.yawDeg * Mathf.Deg2Rad;
                    Vector3 front = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (q.radius * 0.45f);
                    GenUtil.PointLight(air, "WindowGlow", w + front + Vector3.up * 3f, SetPieceBuilder.LampWarm, 6f, 1.1f);
                }
                else if (q.group == "Landmark" || q.part == DressingPlan.RANCH_TOWER || q.group == "FinishLandmark")
                {
                    Vector3 top = w + Vector3.up * (q.height + 1f);
                    GenUtil.SmokeColumn(air, "LandmarkSmoke", top, new Color(0.22f, 0.20f, 0.20f, 0.6f), 40f, 4f);
                    GenUtil.PointLight(air, "LandmarkLight", top - Vector3.up * 2f, SetPieceBuilder.LampWarm, 30f, 2f);
                }
            }
        }

        /// <summary>
        /// 直線の標柱の札（course-r3-06）：道の目印（GuideLine）の立てた丸太（高さ POST_H）に、手配書の紙 WantedPosters_02（0.8 × 1.0m）を
        /// ステージの道の色（ArtPalette.Trail を明るく）に塗って打ち付ける。目印は「杭と札」の形で、骨（飾り）と見分けられる。
        /// </summary>
        static void GuidePostSheets(StageBuild sb, Transform root, List<Placement> plan)
        {
            var g = GenUtil.Child(root, "GuidePostSheets", Vector3.zero).transform;
            g.position = Vector3.zero;
            Color tc = ArtPalette.Trail(sb.def.index);
            var mat = GenUtil.ToonColor(new Color(tc.r + (1f - tc.r) * 0.35f, tc.g + (1f - tc.g) * 0.35f, tc.b + (1f - tc.b) * 0.35f));   // 道の色を明るく
            Vector3 o = sb.def.Origin;
            int k = 0;
            foreach (var q in plan)
            {
                if (q.group != "GuideLine" || q.part != DressingPlan.POST_PART || Mathf.Abs(q.height - DressingPlan.POST_H) > 0.01f) continue;
                Vector3 top = o + new Vector3(q.position.x, q.position.y + q.height * 0.5f - 1.15f, q.position.z);
                var holder = GenUtil.World(g, "GuideSheet_" + (k++), top, q.yawDeg);
                var sheet = GenUtil.PaperSheet(holder.transform, "Paper", "WantedPosters_02", new Vector3(0f, 0f, -0.2f), null, 0.8f, 1.0f);
                foreach (var r in sheet.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                SetStatic(holder);
            }
        }

        /// <summary>灯りを付ける町の建物（ゴールの町・スタートの町・出張所の建物、art-r3-15）。</summary>
        public static bool IsTownBuilding(Placement q)
        {
            if (q.group != "GoalTown" && q.group != "StartTown" && q.group != "GoalOutpost" && q.group != "Settlement" && q.group != "RanchYard") return false;
            if (q.asset.StartsWith("Buildings/")) return true;
            return q.part != null && q.part.EndsWith("_Building") && q.part != DressingPlan.RANCH_TOWER;
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
