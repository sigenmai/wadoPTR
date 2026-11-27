using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの飾り（素材配置）。位置は WorldLayout.ScatterNearCourse（決まった乱数）でコースから離して選ぶ。
    ///   荒野：Packs/Cliffs（峡谷の壁）、Packs/DesertRocks・Packs/RockCollection（合わせて 30 個）、Packs/DesertProps、Props/DeadTree、Props/AcaciaGrove
    ///   砂漠：Packs/Cacti、Props/CactusGroup、Props/Tent（オアシス）、Packs/DesertProps の骨（低い部品）
    ///   境目：Props/MesaRock・ThirdParty/MesaRockB（背景・見えない壁）、ThirdParty/RockGray・RockSetStylized、
    ///         ゴールの町（Buildings/TownBlock・SaloonFacade・RanchCompound）と Props/Podium
    ///   背景専用（Props/Stable・メサ）は BackgroundWalls で周りを見えない BoxCollider で囲む（spec 5.12b）。
    /// 飾りはコース中心から 18m 以上離す（平らな道 ±12m と CP の黒板 15m を避ける）。
    /// Collider：高さ 1.2m 以上の岩・建物は Block レイヤー（MountController の obstacleMask）。崖・メサ・建物は MeshCollider、
    /// それ以外は BoxCollider。低い小物は Collider なし。
    /// </summary>
    public static class DressingBuilder
    {
        static readonly Color RockFallback = new Color(0.6f, 0.45f, 0.35f);
        static readonly Color PlantFallback = new Color(0.35f, 0.5f, 0.25f);
        static readonly Color WoodFallback = new Color(0.45f, 0.30f, 0.18f);

        // DesertProps のうち低い部品（高さ 0.4〜1.2m：骨・頭骨と思われるもの）。素材名が "palette" だけで判別できないため寸法で選んだ
        static readonly string[] DesertBones = { "DesertProps_08", "DesertProps_14", "DesertProps_27", "DesertProps_28", "DesertProps_30" };
        static readonly string[] DesertMisc = { "DesertProps_01", "DesertProps_02", "DesertProps_06", "DesertProps_07", "DesertProps_10", "DesertProps_12" };

        public static void BuildStage(StageBuild sb, WorldContext ctx)
        {
            var root = GenUtil.Child(sb.root, "Dressing", Vector3.zero).transform;
            root.position = Vector3.zero;
            switch (sb.def.kind)
            {
                case HeightmapGen.KIND_CANYON: Canyon(sb, root); break;
                case HeightmapGen.KIND_DUNES: Dunes(sb, root); break;
                default: Mesa(sb, root, ctx); break;
            }
        }

        // ------------------------------------------------------------------

        static void Canyon(StageBuild sb, Transform root)
        {
            int seed = sb.def.seed * 100;
            // 峡谷の壁：コースの両脇 55〜110m に大きな崖
            var cliffs = Scatter(sb, seed + 1, 26, 55f, 110f, 45f);
            for (int i = 0; i < cliffs.Count; i++)
            {
                string part = "Cliffs_" + (i % 13 + 1).ToString("00");
                var go = Place(sb, root, GenUtil.Pack("Cliffs"), part, cliffs[i], seed + i, 1f, 2f, new Vector3(20f, 18f, 20f));
                AddCollider(go, true);
            }
            // 岩：大 3 / 中 6 / 小 6 種を DesertRocks から、RockCollection から 12 個（合わせて 30）
            var rocks = Scatter(sb, seed + 2, 18, 18f, 70f, 14f);
            for (int i = 0; i < rocks.Count; i++)
            {
                int k = i % 15 + 1;
                float minD = k <= 3 ? 35f : 18f;
                if (WorldLayout.DistToPolylineXZ(rocks[i], sb.courseLocal) < minD) continue;
                var go = Place(sb, root, GenUtil.Pack("DesertRocks"), "DesertRocks_" + k.ToString("00"), rocks[i], seed + 50 + i, 1f, 0.4f, new Vector3(3f, 2f, 3f));
                AddCollider(go, k <= 3);
            }
            var small = Scatter(sb, seed + 3, 12, 18f, 45f, 8f);
            for (int i = 0; i < small.Count; i++)
            {
                // RockCollection は 184 個の小石。大きめ（後半の番号）を飛び飛びに選ぶ
                string part = "RockCollection_" + (40 + i * 11).ToString();
                Place(sb, root, GenUtil.Pack("RockCollection"), part, small[i], seed + 80 + i, 2.5f, 0.2f, new Vector3(1f, 1f, 1f));
            }
            var props = Scatter(sb, seed + 4, 6, 18f, 40f, 20f);
            for (int i = 0; i < props.Count; i++)
                AddCollider(Place(sb, root, GenUtil.Pack("DesertProps"), DesertMisc[i % DesertMisc.Length], props[i], seed + 90 + i, 1f, 0.1f, new Vector3(2f, 2f, 2f)), false);
            var trees = Scatter(sb, seed + 5, 10, 18f, 80f, 20f);
            for (int i = 0; i < trees.Count; i++)
            {
                bool acacia = i % 3 == 2;
                var go = PlaceModel(sb, root, GenUtil.Prop(acacia ? "AcaciaGrove" : "DeadTree"), acacia ? "AcaciaGrove" : "DeadTree",
                    trees[i], seed + 120 + i, 1f, 0.2f, new Vector3(2f, 4f, 2f), PlantFallback);
                AddTrunkCollider(go, acacia ? 3f : 0.6f);
            }
        }

        static void Dunes(StageBuild sb, Transform root)
        {
            int seed = sb.def.seed * 100;
            var cacti = Scatter(sb, seed + 1, 34, 18f, 120f, 18f);
            for (int i = 0; i < cacti.Count; i++)
            {
                var go = Place(sb, root, GenUtil.Pack("Cacti"), "Cacti_0" + (i % 9 + 1), cacti[i], seed + i, 1f, 0.2f, new Vector3(1f, 3f, 1f));
                AddTrunkCollider(go, 0.6f);
            }
            var groups = Scatter(sb, seed + 2, 10, 20f, 100f, 30f);
            for (int i = 0; i < groups.Count; i++)
                AddTrunkCollider(PlaceModel(sb, root, GenUtil.Prop("CactusGroup"), "CactusGroup", groups[i], seed + 40 + i, 1f, 0.2f,
                    new Vector3(5f, 3f, 1.5f), PlantFallback), 2.5f);
            // オアシス：コースの中ほど、右へ 40m
            float a = sb.goalArc * 0.5f;
            Vector3 t = WorldLayout.TangentAtArc(sb.courseLocal, a);
            Vector3 oasis = WorldLayout.PointAtArc(sb.courseLocal, a) + WorldLayout.RightOf(t) * 40f;
            var oRoot = GenUtil.World(root, "Oasis", sb.Ground(oasis.x, oasis.z)).transform;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = oasis + Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0f, 11f);
                var tent = PlaceModel(sb, oRoot, GenUtil.Prop("Tent"), "Tent", p, seed + 60 + i, 1f, 0.1f, new Vector3(6f, 3f, 6f), new Color(0.85f, 0.8f, 0.7f));
                tent.transform.rotation = Quaternion.Euler(0f, i * 120f + 180f, 0f);
                AddCollider(tent, false);
            }
            for (int i = 0; i < 2; i++)
                PlaceModel(sb, oRoot, GenUtil.Prop("AcaciaGrove"), "Palm", oasis + new Vector3(i * 8f - 4f, 0f, -3f), seed + 70 + i, 1f, 0.2f, new Vector3(2f, 4f, 2f), PlantFallback);
            var bones = Scatter(sb, seed + 3, 10, 18f, 50f, 25f);
            for (int i = 0; i < bones.Count; i++)
                Place(sb, root, GenUtil.Pack("DesertProps"), DesertBones[i % DesertBones.Length], bones[i], seed + 80 + i, 1f, 0.1f, new Vector3(1f, 0.5f, 1f));
        }

        static void Mesa(StageBuild sb, Transform root, WorldContext ctx)
        {
            int seed = sb.def.seed * 100;
            // ゴールの町のまわり（流しの終点から 160m）には飾りを置かない
            Vector3 end = sb.courseLocal[sb.courseLocal.Length - 1];
            var avoid = new[] { end };

            // メサ（背景）：コースから 160〜330m、見えない壁で囲む
            var mesas = Filter(Scatter(sb, seed + 1, 12, 160f, 330f, 110f), avoid, 200f);
            for (int i = 0; i < mesas.Count; i++)
            {
                bool b = i % 2 == 1;
                float scale = 1.4f + (i % 3) * 0.3f;
                var go = PlaceModel(sb, root, b ? GenUtil.Third("MesaRockB", "MesaRockB") : GenUtil.Prop("MesaRock"), b ? "MesaRockB" : "MesaRock",
                    mesas[i], seed + i, scale, 3f, new Vector3(30f, 25f, 30f), RockFallback);
                AddCollider(go, true);
                BackgroundWalls(go, 6f);
            }
            var rocks = Filter(Scatter(sb, seed + 2, 24, 18f, 70f, 14f), avoid, 160f);
            string[] gray = { "RockGrayA", "RockGrayB", "RockGrayC", "RockGrayD", "RockGrayE", "RockGrayF", "RockGrayG", "RockGrayH" };
            for (int i = 0; i < rocks.Count; i++)
            {
                bool styl = i % 4 == 3;
                string name = styl ? "RockSetStylized" : gray[i % gray.Length];
                float s = styl ? 1.5f : 1.2f + (i % 3) * 0.6f;
                var go = PlaceModel(sb, root, GenUtil.Third(styl ? "RockSetStylized" : "RockGray", name), name, rocks[i], seed + 40 + i, s, 0.2f,
                    new Vector3(2.5f, 2f, 2.5f), RockFallback);
                AddCollider(go, false);
            }
            var dt = Filter(Scatter(sb, seed + 3, 6, 18f, 60f, 30f), avoid, 160f);
            for (int i = 0; i < dt.Count; i++)
                AddTrunkCollider(PlaceModel(sb, root, GenUtil.Prop("DeadTree"), "DeadTree", dt[i], seed + 70 + i, 1f, 0.2f, new Vector3(2f, 4f, 2f), PlantFallback), 0.6f);

            GoalTown(sb, root, ctx);
        }

        /// <summary>ゴールの町：流し（ゴール後の通り）の両脇に建物、通りの先に牧場、脇に表彰台。</summary>
        static void GoalTown(StageBuild sb, Transform root, WorldContext ctx)
        {
            var town = GenUtil.Child(root, "GoalTown", Vector3.zero).transform;
            town.position = Vector3.zero;
            Vector3[] c = sb.courseLocal;
            string[] left = { "TownBlock", "SaloonFacade", "TownBlock" };
            string[] right = { "SaloonFacade", "TownBlock", "TownBlock" };
            for (int i = 0; i < 3; i++)
            {
                float a = sb.goalArc - 30f + i * 22f;
                Vector3 t = WorldLayout.TangentAtArc(c, a);
                Vector3 r = WorldLayout.RightOf(t);
                Vector3 p = WorldLayout.PointAtArc(c, a);
                float yaw = WorldLayout.YawDeg(t);
                Building(sb, town, left[i], p - r * 22f, yaw + 90f);
                Building(sb, town, right[i], p + r * 22f, yaw - 90f);
            }
            // 通りの先（囲いの向こう）に牧場の建物群
            float total = CoursePath.Length(c);
            Vector3 te = WorldLayout.TangentAtArc(c, total);
            Vector3 pe = WorldLayout.PointAtArc(c, total) + te * 45f;
            if (pe.x < 960f && pe.z < 960f) Building(sb, town, "RanchCompound", pe, WorldLayout.YawDeg(te) + 180f);

            // 表彰台：ゴールから 50m 先、通りの右脇（平らな道の外）
            float pa = sb.goalArc + 45f;
            Vector3 pt = WorldLayout.TangentAtArc(c, pa);
            Vector3 pr = WorldLayout.RightOf(pt);
            Vector3 pp = WorldLayout.PointAtArc(c, pa) + pr * 15f;
            sb.goalTownCenter = sb.Ground(pp.x, pp.z);
            ctx.podium = ShowBuilder.BuildPodium(town, sb.goalTownCenter, WorldLayout.YawDeg(-pr));
        }

        static void Building(StageBuild sb, Transform parent, string name, Vector3 local, float yaw)
        {
            Vector3 w = MinGround(sb, local, 8f);
            var go = GenUtil.PlaceModel(GenUtil.Building(name), parent, name, w - Vector3.up * 0.3f, yaw, 1f, new Vector3(12f, 8f, 8f), WoodFallback);
            AddCollider(go, true);
        }

        // ------------------------------------------------------------------
        // 共通
        // ------------------------------------------------------------------

        /// <summary>決まった乱数でコースから minD〜maxD m の点。スターター・橋・近道など sb.exclusions の範囲は除く。</summary>
        static List<Vector3> Scatter(StageBuild sb, int seed, int count, float minD, float maxD, float spacing)
        {
            var pts = WorldLayout.ScatterNearCourse(seed, count, StageDefinitions.SIZE, 40f, sb.courseLocal, minD, maxD, spacing);
            var r = new List<Vector3>();
            foreach (var p in pts)
            {
                bool ok = true;
                foreach (var e in sb.exclusions)
                    if (WorldLayout.DistXZ(p, new Vector3(e.x, 0f, e.z)) < e.w + spacing * 0.5f) { ok = false; break; }
                if (ok) r.Add(p);
            }
            return r;
        }

        static List<Vector3> Filter(List<Vector3> pts, Vector3[] avoid, float radius)
        {
            var r = new List<Vector3>();
            foreach (var p in pts)
            {
                bool ok = true;
                foreach (var a in avoid) if (WorldLayout.DistXZ(p, a) < radius) { ok = false; break; }
                if (ok) r.Add(p);
            }
            return r;
        }

        /// <summary>足元の 4 隅と中心のうち一番低い地面（浮かないように）。</summary>
        static Vector3 MinGround(StageBuild sb, Vector3 local, float radius)
        {
            Vector3 best = sb.Ground(local.x, local.z);
            for (int i = 0; i < 4; i++)
            {
                Vector3 o = Quaternion.Euler(0f, 45f + i * 90f, 0f) * new Vector3(0f, 0f, radius);
                Vector3 g = sb.Ground(local.x + o.x, local.z + o.z);
                if (g.y < best.y) best.y = g.y;
            }
            return best;
        }

        static GameObject Place(StageBuild sb, Transform root, string pack, string part, Vector3 local, int seed, float scale, float sink, Vector3 fb)
        {
            Vector3 w = MinGround(sb, local, 2f * scale) - Vector3.up * sink;
            float yaw = WorldLayout.Angles(seed, 1)[0];
            return GenUtil.InstantiatePart(pack, part, root, w, yaw, scale, fb, RockFallback);
        }

        static GameObject PlaceModel(StageBuild sb, Transform root, string path, string name, Vector3 local, int seed, float scale, float sink, Vector3 fb, Color fc)
        {
            Vector3 w = MinGround(sb, local, 3f * scale) - Vector3.up * sink;
            float yaw = WorldLayout.Angles(seed, 1)[0];
            return GenUtil.PlaceModel(path, root, name, w, yaw, scale, fb, fc);
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
        static void AddTrunkCollider(GameObject go, float width)
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
