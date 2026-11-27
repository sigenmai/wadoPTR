using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ロビー（spec 5.6 / 5.14、PLAYTEST Task 8〜10 のロビー側の配置をすべて自動化）。
    ///   地面：平らな Terrain（420m 四方、地面の高さ y = 0）。町の足元だけ 1m 下げて通りの板と地面がちらつかないようにする。
    ///   町：Packs/WesternTown を「通りの高さ」だけ沈めて置く（通りの高さはメッシュの上向きの面の面積ヒストグラムの最頻値、
    ///       取れなければ STREET_HEIGHT_FALLBACK = 13.75m。Blender で同じ方法で測った値）。町は広場の北側。
    ///   広場：受付カウンター（FarmProps の机＋台帳 EntryLedger）、手配書の壁（PosterCard ×6）、スタート台の鐘（StartBell）、
    ///         補助表示のレバー（MinimalHud）、スポーン位置。
    ///   柵の馬：MountPicker ×7（毛色 0〜5＋骨の馬 6）と柵、その奥に厩舎（背景専用・見えない壁）。
    ///   たまり場：焚き火・丸太のベンチ・前回レースの黒板（RankBoard showLastRace）・掲示板 3 枚と中継（ShowBuilder）。
    ///   練習場：坂・崖下り・ジャンプ台・干し草・カカシ ×2・練習用の馬の杭 ×2・練習場の遺物、全体を覆うトリガー（PracticeArea）。
    ///   乗り物の待機場所 ×8、予備の遺物 ×4 の置き場、懐中時計、補助表示の紙。
    /// </summary>
    public static class LobbyBuilder
    {
        public static readonly Vector3 CENTER = WorldBounds.LOBBY_CENTER;
        public const float GROUND_SIZE = WorldBounds.LOBBY_SIZE;
        public const float GROUND_HEIGHT_RANGE = 80f;   // 外周の尾根（〜25m）が入る高さ
        public const float RIM_WIDTH_M = LobbyLayout.RIM_WIDTH_M, RIM_MIN_M = LobbyLayout.RIM_MIN_M, RIM_MAX_M = LobbyLayout.RIM_MAX_M;   // ロビーの外周の尾根
        public const float BORDER_INSET_M = 32f;       // 見えない壁：外周から 32m 内側（尾根の手前）
        public const float BERM_M = 8f, BERM_HEIGHT_M = 0.6f;   // 町の足元のまわりを少し盛って土台の縁を隠す
        public const float TOWN_ENTRANCE_HALF = 12f;   // 町の南側の入口（見えない壁・岩を置かない）
        public const float GROUND_LEVEL = 2f;            // 地形の高さ 2m（Terrain は y = -2 に置く）→ 地面 y = 0
        public const float STREET_HEIGHT_FALLBACK = 13.75f;
        public static readonly Vector3 PRACTICE_OFFSET = new Vector3(120f, 0f, -40f);

        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        static readonly Color DarkWood = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Brass = new Color(0.80f, 0.62f, 0.25f);
        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);
        static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);
        static readonly Color Ramp = new Color(0.66f, 0.45f, 0.30f);

        public static float lastStreetHeight;

        public static void Build(WorldContext ctx)
        {
            var root = GenUtil.World(ctx.root, "Lobby", CENTER).transform;
            ctx.lobbyRoot = root;
            ctx.lobbyCenter = CENTER;

            GameObject town = Town(root);
            FitFence(town);
            Ground(root, town);
            Plaza(root, ctx);
            Corral(root, ctx);
            Hangout(root, ctx);
            Practice(root, ctx);
            Parking(root, ctx);
            SpareRelics(root, ctx);
            Watch(root, ctx);
            Dressing(root, town);
            FenceAndBelt(root);
        }

        static Vector3 L(float x, float z) => CENTER + new Vector3(x, 0f, z);

        // ------------------------------------------------------------------
        // 町のまわりの飾り（DressingPlan.ForLobby）と町の壁の手配書
        // ------------------------------------------------------------------

        public const int WALL_POSTERS_MAX = 18;
        static readonly string[] CardNames = { "Wanted_Original", "Wanted_Horse", "Wanted_Camel", "Wanted_Dust", "Wanted_Lasso", "Wanted_Wall", "Wanted_Ball" };

        /// <summary>置かない円（ロビー中心からの相対 x/z と半径）：広場・柵の馬と厩舎・たまり場・待機場所・練習場・予備の遺物・町の入口。</summary>
        public static List<Vector4> LobbyExclusions()
        {
            return new List<Vector4>
            {
                new Vector4(0f, 0f, -2f, 24f), new Vector4(-50f, 0f, -10f, 28f), new Vector4(30f, 0f, -30f, 20f),
                new Vector4(-11f, 0f, -45f, 23f), new Vector4(PRACTICE_OFFSET.x, 0f, PRACTICE_OFFSET.z, 82f),
                new Vector4(-150f, 0f, -100f, 10f), new Vector4(0f, 0f, 22f, 14f),
            };
        }

        public const float FENCE_COLLIDER_H = 3.0f;
        /// <summary>このビルドの柵の長方形（ロビー中心からの相対）。実際の町が入らなければ広げる（FitFence）。</summary>
        static Vector4 fenceRect = LobbyLayout.FENCE_RECT;

        /// <summary>実際の WesternTown の外形（回転した後）が柵の中に 8m 以上の余白で入るか確かめ、入らなければ柵を広げる。</summary>
        static void FitFence(GameObject town)
        {
            Bounds tb = GenUtil.RendererBounds(town);
            var townRel = new Vector4(tb.min.x - CENTER.x, tb.min.z - CENTER.z, tb.max.x - CENTER.x, tb.max.z - CENTER.z);
            bool grown, fits;
            fenceRect = LobbyLayout.FitFenceRect(LobbyLayout.FENCE_RECT, townRel, LobbyLayout.TOWN_MARGIN_M, out grown, out fits);
            if (grown)
                Debug.LogError("[HorseRace] WesternTown（" + townRel + "）が柵の長方形に 8m の余白で入らないため、柵を " + fenceRect
                    + " に広げました（木の帯・柵の外の盛り上がりも広げた柵に合わせます）");
            if (!fits)
                Debug.LogError("[HorseRace] WesternTown が大きすぎて、柵を最大まで広げても 8m の余白が取れません。町の配置を確認してください");
        }

        /// <summary>ロビー全体を囲む木の柵（切れ目なし、柱 ≤ 2.74m、高さ約 1.57m、Block）と、柵の外の木の帯（3 列）。</summary>
        static void FenceAndBelt(Transform root)
        {
            var fence = GenUtil.World(root, "LobbyFence", CENTER).transform;
            var segs = LobbyLayout.FenceSegments(fenceRect, LobbyLayout.Gates, LobbyLayout.FENCE_PIECE_M);
            foreach (var sg in segs)
            {
                Vector3 a = CENTER + sg[0], b = CENTER + sg[1];
                Vector3 d = b - a;
                float len = d.magnitude;
                var piece = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), Fence.PART, fence, (a + b) * 0.5f, WorldLayout.YawDeg(d / len) - 90f, 1f,
                    new Vector3(Fence.PIECE, 1f, 0.15f), Wood);
                piece.transform.localScale = new Vector3(len / Fence.PIECE, LobbyLayout.FENCE_SCALE_Y, 1f);
                var box = piece.AddComponent<BoxCollider>();
                // 当たり判定は見た目より高い 3.0m（乗り物のジャンプで越えられない）。ローカルは縦 ×1.6 されるので割る
                float colH = FENCE_COLLIDER_H / LobbyLayout.FENCE_SCALE_Y;
                box.size = new Vector3(Fence.PIECE, colH, 0.4f);
                box.center = new Vector3(0f, colH * 0.5f, 0f);
                GenUtil.SetLayerRecursive(piece, WorldLayers.Block);
                DressingBuilder.SetStatic(piece);
            }
            var belt = GenUtil.World(root, "LobbyTreeBelt", CENTER).transform;
            var trees = LobbyLayout.TreeBelt(fenceRect, 3);
            DressingBuilder.Instantiate(trees, belt, CENTER);
            Debug.Log("[HorseRace] ロビーの柵 " + segs.Count + " 区間（切れ目 " + LobbyLayout.Gates.Length + "）、柵の外の木の帯 " + trees.Count + " 本");
        }

        static void Dressing(Transform root, GameObject town)
        {
            var dr = GenUtil.Child(root, "TownDressing", Vector3.zero).transform;
            dr.position = CENTER;
            Bounds tb = GenUtil.RendererBounds(town);
            var plan = DressingPlan.ForLobby(new Vector2(tb.min.x - CENTER.x, tb.min.z - CENTER.z), new Vector2(tb.max.x - CENTER.x, tb.max.z - CENTER.z),
                LobbyExclusions(), 4242);
            DressingBuilder.Instantiate(plan, dr, CENTER);
            DressingBuilder.SetStatic(town);
            int posters = WallPosters(dr, town, tb);
            Debug.Log("[HorseRace] ロビーの飾り: " + plan.Count + " 個（上限 " + DressingPlan.MAX_LOBBY + "）、町の壁の手配書 " + posters + " 枚");
        }

        /// <summary>町の南・東・西の外から水平にレイを飛ばし、当たった壁（ほぼ垂直の面）に手配書を貼る。</summary>
        static int WallPosters(Transform parent, GameObject town, Bounds tb)
        {
            Physics.SyncTransforms();
            int mask = 1 << WorldLayers.Block, n = 0;
            float y = CENTER.y + 1.7f;
            var rays = new List<Vector3[]>();
            for (float x = tb.min.x + 6f; x < tb.max.x - 6f; x += 12f)
                rays.Add(new[] { new Vector3(x, y, tb.min.z - 4f), Vector3.forward });
            for (float z = tb.min.z + 6f; z < tb.max.z - 6f; z += 14f)
            {
                rays.Add(new[] { new Vector3(tb.min.x - 4f, y, z), Vector3.right });
                rays.Add(new[] { new Vector3(tb.max.x + 4f, y, z), Vector3.left });
            }
            foreach (var r in rays)
            {
                if (n >= WALL_POSTERS_MAX) break;
                RaycastHit hit;
                if (!Physics.Raycast(r[0], r[1], out hit, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider == null || !hit.collider.transform.IsChildOf(town.transform)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.3f) continue;
                var holder = new GameObject("WallPoster_" + n);
                holder.transform.SetParent(parent, false);
                holder.transform.position = hit.point + hit.normal * 0.03f - Vector3.up * 0.3f;
                holder.transform.rotation = Quaternion.LookRotation(-new Vector3(hit.normal.x, 0f, hit.normal.z).normalized, Vector3.up);
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/" + CardNames[n % CardNames.Length] + ".png");
                DressingBuilder.SetStatic(Poster(holder.transform, n % 3 == 2 ? "WantedPosters_02" : "WantedPosters_01", tex, "Poster"));
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------
        // 町と地面
        // ------------------------------------------------------------------

        static GameObject Town(Transform root)
        {
            var holder = GenUtil.World(root, "WesternTown", CENTER);
            var m = GenUtil.InstantiateModel(GenUtil.Pack("WesternTown"), holder.transform, "Model");
            if (m == null)
            {
                GenUtil.Prim(PrimitiveType.Cube, "TownFallback", holder.transform, new Vector3(0f, 4f, 0f), new Vector3(40f, 8f, 80f), Wood, true);
                holder.transform.position = L(0f, 25f + 40f);
                DressingBuilder.AddCollider(holder, false);
                return holder;
            }
            lastStreetHeight = StreetHeight(m);
            Vector3 p = holder.transform.position;
            p.y = CENTER.y - lastStreetHeight + 0.02f;
            holder.transform.position = p;
            // 丘のある端を北（広場の反対）へ：両端 35m の帯で高い頂点（通り +10m 以上）の数を比べ、南が多ければ 180° 回す
            {
                Bounds b0 = GenUtil.RendererBounds(holder);
                int south = 0, north = 0;
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Matrix4x4 m4 = mf.transform.localToWorldMatrix;
                    foreach (Vector3 v in mf.sharedMesh.vertices)
                    {
                        Vector3 wv = m4.MultiplyPoint3x4(v);
                        if (wv.y < CENTER.y + 10f) continue;
                        if (wv.z < b0.min.z + 35f) south++;
                        else if (wv.z > b0.max.z - 35f) north++;
                    }
                }
                if (south > north * 3 / 2)
                {
                    holder.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * holder.transform.rotation;
                    Debug.Log("[HorseRace] WesternTown の丘が南側にあったので 180° 回しました（丘を広場の反対側へ）");
                }
            }
            Bounds b = GenUtil.RendererBounds(holder);
            // 町を広場の北側へ：南端が z = +25
            holder.transform.position += new Vector3(CENTER.x - b.center.x, 0f, CENTER.z + 25f - b.min.z);
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            GenUtil.SetLayerRecursive(holder, WorldLayers.Block);
            return holder;
        }

        /// <summary>WesternTown の通りの高さ（町のルートからの高さ、m）。上向きの面の面積で最頻の高さ。</summary>
        public static float StreetHeight(GameObject model)
        {
            var hs = new List<float>();
            var areas = new List<float>();
            float baseY = model.transform.position.y;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Vector3[] v = mesh.vertices;
                int[] tri = mesh.triangles;
                Matrix4x4 w = mf.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    Vector3 a = w.MultiplyPoint3x4(v[tri[i]]), b = w.MultiplyPoint3x4(v[tri[i + 1]]), c = w.MultiplyPoint3x4(v[tri[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float len = n.magnitude;
                    if (len < 1e-6f) continue;
                    // 法線の向き（巻き順）は Unity の左手系で反転しうるので、上下どちらでも「水平な面」を数える
                    if (Mathf.Abs(n.y) / len < 0.95f) continue;
                    hs.Add((a.y + b.y + c.y) / 3f - baseY);
                    areas.Add(len * 0.5f);
                }
            }
            float h = WorldLayout.EstimateStreetHeight(hs.ToArray(), areas.ToArray(), 0.25f, STREET_HEIGHT_FALLBACK);
            // 模型の底（0m）や屋根に当たったときは既知の値に戻す
            if (h < 5f || h > 25f)
            {
                GenUtil.Warn("WesternTown の通りの高さを推定できませんでした（" + h.ToString("F2") + "m）。既定値 " + STREET_HEIGHT_FALLBACK + "m を使います");
                h = STREET_HEIGHT_FALLBACK;
            }
            Debug.Log("[HorseRace] WesternTown の通りの高さ: " + h.ToString("F2") + "m（この分だけ沈めて置きました）");
            return h;
        }

        static void Ground(Transform root, GameObject town)
        {
            int res = 129;
            float cell = GROUND_SIZE / (res - 1);
            Vector3 origin = new Vector3(CENTER.x - GROUND_SIZE * 0.5f, CENTER.y - GROUND_LEVEL, CENTER.z - WorldBounds.LOBBY_SOUTH);
            Bounds tb = GenUtil.RendererBounds(town);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = origin.x + x * cell, wz = origin.z + z * cell;
                    bool under = wx > tb.min.x + 1f && wx < tb.max.x - 1f && wz > tb.min.z + 1f && wz < tb.max.z - 1f;
                    float level = under ? GROUND_LEVEL - 1f : GROUND_LEVEL;
                    if (!under)
                    {
                        // 町の外周の小さな土手（入口は除く）：土台の縁・裏を見せない
                        float dx = Mathf.Max(0f, Mathf.Max(tb.min.x - wx, wx - tb.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(tb.min.z - wz, wz - tb.max.z));
                        float dd = Mathf.Sqrt(dx * dx + dz * dz);
                        bool entrance = wz < tb.min.z && Mathf.Abs(wx - tb.center.x) < TOWN_ENTRANCE_HALF + 4f;
                        if (dd < BERM_M && !entrance) level += BERM_HEIGHT_M * (1f - dd / BERM_M);
                    }
                    // 柵の外はすぐ盛り上がる（柵の中から外の平らな地面が見えない）
                    float dOut = LobbyLayout.DistOutside(fenceRect, new Vector3(wx - CENTER.x, 0f, wz - CENTER.z));
                    if (dOut > 0f) level += LobbyLayout.OutsideRise(dOut);
                    h[z, x] = level / GROUND_HEIGHT_RANGE;
                }
            HeightmapGen.ApplyRim(h, GROUND_SIZE, GROUND_HEIGHT_RANGE, RIM_WIDTH_M, RIM_MIN_M, RIM_MAX_M, 99, null);
            var td = TerrainBuilder.CreateTerrainData("Lobby", h, GROUND_SIZE, GROUND_HEIGHT_RANGE);
            td.terrainLayers = TerrainBuilder.Layers();
            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "LobbyGround";
            tgo.transform.SetParent(root, false);
            tgo.transform.position = origin;
            GenUtil.SetLayerRecursive(tgo, WorldLayers.Ground);

            // 外周の見えない壁（尾根の手前。練習場もこの内側）
            // 見えない壁は柵の 6m 外（柵が広がっても柵の外側。尾根の手前〜斜面）
            {
                var bw = GenUtil.World(root, "LobbyBorder", CENTER).transform;
                Vector4 r = new Vector4(fenceRect.x - 6f, fenceRect.y - 6f, fenceRect.z + 6f, fenceRect.w + 6f);
                float cx = (r.x + r.z) * 0.5f, cz = (r.y + r.w) * 0.5f, w = r.z - r.x, d = r.w - r.y, hgt = TerrainBuilder.WALL_HEIGHT_M;
                AddBox(bw, "N", new Vector3(cx, hgt * 0.5f - 20f, r.w), new Vector3(w + 2f, hgt, 2f), false);
                AddBox(bw, "S", new Vector3(cx, hgt * 0.5f - 20f, r.y), new Vector3(w + 2f, hgt, 2f), false);
                AddBox(bw, "E", new Vector3(r.z, hgt * 0.5f - 20f, cz), new Vector3(2f, hgt, d + 2f), false);
                AddBox(bw, "W", new Vector3(r.x, hgt * 0.5f - 20f, cz), new Vector3(2f, hgt, d + 2f), false);
                GenUtil.SetLayerRecursive(bw.gameObject, WorldLayers.Block);
            }
            TownWallsAndSkirt(root, town, tb);
        }

        /// <summary>
        /// 町（ほぼ見た目だけの建物の模型）の外周：見えない壁（南の入口 24m だけ開ける）で建物の裏へ回り込めないようにし、
        /// 足元に岩を並べて沈めた土台の縁を隠す。
        /// </summary>
        static void TownWallsAndSkirt(Transform root, GameObject town, Bounds tb)
        {
            var walls = GenUtil.World(root, "TownWalls", new Vector3(tb.center.x, CENTER.y, tb.center.z)).transform;
            float w = tb.size.x + 4f, d = tb.size.z + 4f, hgt = 30f;
            AddBox(walls, "N", new Vector3(0f, hgt * 0.5f, d * 0.5f), new Vector3(w, hgt, 1f), false);
            AddBox(walls, "E", new Vector3(w * 0.5f, hgt * 0.5f, 0f), new Vector3(1f, hgt, d), false);
            AddBox(walls, "W", new Vector3(-w * 0.5f, hgt * 0.5f, 0f), new Vector3(1f, hgt, d), false);
            float seg = (w - TOWN_ENTRANCE_HALF * 2f) * 0.5f;
            AddBox(walls, "S_L", new Vector3(-(TOWN_ENTRANCE_HALF + seg * 0.5f), hgt * 0.5f, -d * 0.5f), new Vector3(seg, hgt, 1f), false);
            AddBox(walls, "S_R", new Vector3(TOWN_ENTRANCE_HALF + seg * 0.5f, hgt * 0.5f, -d * 0.5f), new Vector3(seg, hgt, 1f), false);
            GenUtil.SetLayerRecursive(walls.gameObject, WorldLayers.Block);

            var skirt = GenUtil.World(root, "TownSkirt", CENTER).transform;
            string[] rocks = { "DesertRocks_10", "DesertRocks_11", "DesertRocks_12", "DesertRocks_13", "DesertRocks_14", "DesertRocks_15" };
            int k = 0;
            float perim = 2f * (tb.size.x + tb.size.z);
            for (float s = 0f; s < perim; s += 6f, k++)
            {
                Vector3 p;
                if (s < tb.size.x) p = new Vector3(tb.min.x + s, CENTER.y, tb.min.z - 1.5f);
                else if (s < tb.size.x + tb.size.z) p = new Vector3(tb.max.x + 1.5f, CENTER.y, tb.min.z + (s - tb.size.x));
                else if (s < 2f * tb.size.x + tb.size.z) p = new Vector3(tb.max.x - (s - tb.size.x - tb.size.z), CENTER.y, tb.max.z + 1.5f);
                else p = new Vector3(tb.min.x - 1.5f, CENTER.y, tb.max.z - (s - 2f * tb.size.x - tb.size.z));
                if (p.z < tb.min.z && Mathf.Abs(p.x - tb.center.x) < TOWN_ENTRANCE_HALF + 2f) continue;   // 入口
                var r = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), rocks[k % rocks.Length], skirt, p - Vector3.up * 0.6f,
                    k * 47f, 1.3f, new Vector3(2.5f, 1.5f, 2.5f), new Color(0.6f, 0.45f, 0.35f));
                DressingBuilder.SetStatic(r);
            }
        }

        static BoxCollider AddBox(Transform parent, string name, Vector3 local, Vector3 size, bool trigger)
        {
            var go = GenUtil.Child(parent, name, local);
            var b = go.AddComponent<BoxCollider>();
            b.size = size;
            b.isTrigger = trigger;
            return b;
        }

        // ------------------------------------------------------------------
        // 広場
        // ------------------------------------------------------------------

        static void Plaza(Transform root, WorldContext ctx)
        {
            ctx.lobbySpawn = GenUtil.World(root, "LobbySpawn", L(0f, -5f) + Vector3.up * 0.05f, 0f).transform;

            // 受付カウンター（FarmProps_05 = 机）と台帳。カウンターは南（広場）を向く
            var counter = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_05", root, L(-8f, 12f), 180f, 1f,
                new Vector3(2.5f, 1f, 1.2f), Wood);
            counter.name = "ReceptionCounter";
            DressingBuilder.AddCollider(counter, false);
            float top = GenUtil.RendererBounds(counter).max.y;
            if (top < 0.5f) top = 1f;
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_15", root, L(-10.5f, 13f), 0f, 1f, new Vector3(0.7f, 0.8f, 0.7f), Wood).name = "Barrel";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_12", root, L(-5.5f, 13.5f), 20f, 1f, new Vector3(1f, 1f, 1f), Wood).name = "Crate";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", root, L(-12f, 10f), 0f, 1f, new Vector3(0.3f, 3f, 0.3f), DarkWood).name = "StreetLamp";

            var book = GenUtil.World(root, "EntryLedger", new Vector3(L(-8f, 12f).x, CENTER.y + top + 0.03f, L(-8f, 12f).z), 180f);
            GenUtil.Prim(PrimitiveType.Cube, "Cover", book.transform, Vector3.zero, new Vector3(0.7f, 0.05f, 0.5f), new Color(0.35f, 0.12f, 0.08f));
            GenUtil.Prim(PrimitiveType.Cube, "Pages", book.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.66f, 0.02f, 0.46f), Paper);
            var bc = book.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.8f, 0.3f, 0.6f);
            bc.center = new Vector3(0f, 0.1f, 0f);
            var page = GenUtil.Text(book.transform, "PageText", new Vector3(0f, 0.045f, 0f), Quaternion.Euler(90f, 180f, 0f), "エントリー台帳", 0.35f, Ink, new Vector2(0.62f, 0.42f));
            var ledger = GenUtil.AddU<EntryLedger>(book);
            ledger.pageText = page;
            ledger.signSound = GenUtil.Audio(book, false);
            GenUtil.SetInteractText(ledger, "台帳にサインする");
            ctx.ledgers.Add(ledger);
            // 台帳の上の札
            GenUtil.Text(root, "LedgerSign", L(-8f, 12.6f) + Vector3.up * (top + 0.9f), Quaternion.identity, "受付\n台帳にサインでエントリー", 1.0f, Ink, new Vector2(2.4f, 0.8f));

            PosterWall(root, ctx);

            // スタート台と鐘
            var stage = GenUtil.Prim(PrimitiveType.Cube, "StartPlatform", root, Vector3.zero, new Vector3(3f, 0.4f, 3f), Wood, true);
            stage.transform.position = L(0f, -18f) + Vector3.up * 0.2f;
            stage.layer = WorldLayers.Block;
            var post = GenUtil.Prim(PrimitiveType.Cube, "BellPost", root, Vector3.zero, new Vector3(0.2f, 2.6f, 0.2f), DarkWood);
            post.transform.position = L(0f, -18f) + Vector3.up * 1.7f;
            var bellGo = GenUtil.World(root, "StartBell", L(0f, -18f) + Vector3.up * 2.6f, 180f);
            GenUtil.Prim(PrimitiveType.Cylinder, "Arm", bellGo.transform, new Vector3(0f, 0.35f, 0.25f), new Vector3(0.06f, 0.3f, 0.06f), DarkWood)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GenUtil.Prim(PrimitiveType.Sphere, "Bell", bellGo.transform, new Vector3(0f, 0.1f, 0.5f), new Vector3(0.45f, 0.5f, 0.45f), Brass);
            var bellCol = bellGo.AddComponent<SphereCollider>();
            bellCol.center = new Vector3(0f, 0.1f, 0.5f);
            bellCol.radius = 0.4f;
            var bell = GenUtil.AddU<StartBell>(bellGo);
            bell.ringSound = GenUtil.Audio(bellGo, true);
            bell.tapSound = GenUtil.Audio(bellGo, false);
            bell.noteText = GenUtil.Text(root, "BellNote", L(0f, -16.4f) + Vector3.up * 1.6f, Quaternion.Euler(0f, 180f, 0f),
                "主催者が鐘を鳴らすとレース開始", 0.8f, Ink, new Vector2(2.4f, 0.6f));
            GenUtil.SetInteractText(bell, "鐘を鳴らす");
            ctx.startBell = bell;

            Hud(root, ctx);
        }

        static void PosterWall(Transform root, WorldContext ctx)
        {
            var wall = GenUtil.World(root, "WantedWall", L(8f, 14f), 0f);
            var board = GenUtil.Prim(PrimitiveType.Cube, "Board", wall.transform, new Vector3(0f, 1.6f, 0f), new Vector3(6f, 2.4f, 0.2f), DarkWood, true);
            board.layer = WorldLayers.Block;
            GenUtil.Prim(PrimitiveType.Cube, "PostL", wall.transform, new Vector3(-3.1f, 1.4f, 0f), new Vector3(0.2f, 2.8f, 0.2f), Wood);
            GenUtil.Prim(PrimitiveType.Cube, "PostR", wall.transform, new Vector3(3.1f, 1.4f, 0f), new Vector3(0.2f, 2.8f, 0.2f), Wood);
            GenUtil.Text(wall.transform, "Title", new Vector3(0f, 3.05f, -0.12f), Quaternion.identity, "手配書　―一枚取って乗り物と能力を選ぶ―", 2.2f, Paper, new Vector2(6f, 0.5f));

            // kind 0 = 乗り物（0 馬 / 1 ラクダ）、kind 1 = 能力（0 砂塵 / 1 投げ縄 / 2 岩壁 / 3 鉄球）
            var cards = new (string tex, int kind, int value, string label)[]
            {
                ("Wanted_Horse", 0, 0, "馬"), ("Wanted_Camel", 0, 1, "ラクダ"),
                ("Wanted_Dust", 1, 0, "砂塵"), ("Wanted_Lasso", 1, 1, "投げ縄"), ("Wanted_Wall", 1, 2, "岩壁"), ("Wanted_Ball", 1, 3, "鉄球"),
            };
            for (int i = 0; i < cards.Length; i++)
            {
                // 上段：乗り物 2 枚、下段：能力 4 枚
                float x = i < 2 ? -1.0f + i * 2.0f : -1.5f + (i - 2) * 1.0f;
                float y = i < 2 ? 2.05f : 1.15f;
                var go = GenUtil.Child(wall.transform, "Card_" + cards[i].label, new Vector3(x, y, -0.13f));
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(0.5f, 0.7f, 0.06f);
                col.center = new Vector3(0f, 0.32f, 0f);
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/" + cards[i].tex + ".png");
                if (tex == null) GenUtil.Warn("手配書のカードが見つかりません: " + cards[i].tex);
                var poster = Poster(go.transform, "WantedPosters_01", tex, "Poster");
                var taken = Poster(go.transform, "WantedPosters_05", OriginalCard(), "Taken");
                taken.SetActive(false);
                var pc = GenUtil.AddU<PosterCard>(go);
                pc.kind = cards[i].kind;
                pc.value = cards[i].value;
                pc.posterVisual = poster;
                pc.takenVisual = taken;
                GenUtil.SetInteractText(pc, "手配書を取る：" + cards[i].label);
                ctx.posterCards.Add(pc);
            }
            // 飾りの手配書（取れない）
            Poster(wall.transform, "WantedPosters_01", OriginalCard(), "Decor_L").transform.localPosition = new Vector3(-2.6f, 1.0f, -0.13f);
            Poster(wall.transform, "WantedPosters_02", OriginalCard(), "Decor_R").transform.localPosition = new Vector3(2.6f, 1.9f, -0.13f);
        }

        static Texture2D OriginalCard() => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/Wanted_Original.png");

        /// <summary>手配書 1 枚（部品の紙の面を壁の手前 = -Z に向ける）。テクスチャはカードごと。</summary>
        internal static GameObject Poster(Transform parent, string part, Texture tex, string name)
        {
            var holder = GenUtil.InstantiatePart(GenUtil.Pack("WantedPosters"), part, parent, parent.position, 0f, 1f,
                new Vector3(0.44f, 0.63f, 0.01f), Paper);
            holder.name = name;
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            if (holder.transform.childCount > 0)
            {
                Transform p = holder.transform.GetChild(0);
                Vector3 n = GenUtil.FirstNormalWorld(p);
                GenUtil.YawAlign(holder.transform, n, -parent.forward);
            }
            if (tex != null) GenUtil.Toonify(holder, tex);
            return holder;
        }

        static void Hud(Transform root, WorldContext ctx)
        {
            // 真鍮のレバー（ロビー）
            var lever = GenUtil.World(root, "HudLever", L(4f, -14f), 180f);
            GenUtil.Prim(PrimitiveType.Cube, "Base", lever.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.4f, 1f, 0.3f), DarkWood);
            var handle = GenUtil.Child(lever.transform, "Handle", new Vector3(0f, 1.0f, 0f)).transform;
            GenUtil.Prim(PrimitiveType.Cylinder, "Rod", handle, new Vector3(0f, 0.25f, 0f), new Vector3(0.05f, 0.25f, 0.05f), Brass);
            GenUtil.Prim(PrimitiveType.Sphere, "Knob", handle, new Vector3(0f, 0.52f, 0f), Vector3.one * 0.1f, Brass);
            var col = lever.AddComponent<BoxCollider>();
            col.size = new Vector3(0.5f, 1.6f, 0.5f);
            col.center = new Vector3(0f, 0.8f, 0f);
            var label = GenUtil.Text(lever.transform, "Label", new Vector3(0f, 0.7f, -0.16f), Quaternion.identity, "補助表示", 0.6f, Paper, new Vector2(0.5f, 0.2f));

            // 補助表示の紙（ワールド空間 Canvas、レバーの子にしない）
            var canvasGo = new GameObject("MinimalHudPaper");
            canvasGo.transform.SetParent(root, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = canvasGo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(220f, 110f);
            canvasGo.transform.position = L(0f, 0f) + Vector3.up * 1.5f;
            canvasGo.transform.localScale = Vector3.one * 0.001f;
            var bgGo = new GameObject("Paper");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var img = bgGo.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.93f, 0.86f, 0.70f, 0.85f);
            Stretch(bgGo.GetComponent<RectTransform>(), 0f, 1f);
            var rank = UguiText(canvasGo.transform, "RankText", 0.5f, 1f, 44f);
            var time = UguiText(canvasGo.transform, "TimeText", 0f, 0.5f, 36f);

            var hud = GenUtil.AddU<MinimalHud>(lever);
            hud.hudRoot = canvasGo;
            hud.rankText = rank;
            hud.timeText = time;
            hud.leverHandle = handle;
            hud.leverLabel = label;
            GenUtil.SetInteractText(hud, "補助表示を出す／消す");
            ctx.hud = hud;
        }

        static void Stretch(RectTransform rt, float yMin, float yMax)
        {
            rt.anchorMin = new Vector2(0f, yMin);
            rt.anchorMax = new Vector2(1f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static TMPro.TextMeshProUGUI UguiText(Transform parent, string name, float yMin, float yMax, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.text = "";
            t.fontSize = size;
            t.color = Ink;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            var f = GenUtil.Font();
            if (f != null) t.font = f;
            Stretch(go.GetComponent<RectTransform>(), yMin, yMax);
            return t;
        }

        // ------------------------------------------------------------------
        // 柵の馬と厩舎
        // ------------------------------------------------------------------

        static void Corral(Transform root, WorldContext ctx)
        {
            var corral = GenUtil.World(root, "Corral", L(-40f, -10f)).transform;
            Fence.Line(corral, L(-42f, -32f), L(-42f, 12f));
            for (int coat = 0; coat < 7; coat++)
            {
                Vector3 p = L(-38.5f, -26f + coat * 5.5f);
                var go = GenUtil.World(corral, coat == 6 ? "Picker_Skeleton" : "Picker_Coat" + coat, p, 90f);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(1.2f, 2.2f, 2.6f);
                col.center = new Vector3(0f, 1.1f, 0f);
                var vis = GenUtil.Child(go.transform, "Visual", Vector3.zero);
                var mv = MountBuilder.BuildVisual(vis, coat);
                var marker = GenUtil.Prim(PrimitiveType.Cube, "ChosenRibbon", go.transform, new Vector3(0f, 2.3f, 0f), new Vector3(0.6f, 0.1f, 0.1f), new Color(0.8f, 0.1f, 0.1f));
                marker.SetActive(false);
                var picker = GenUtil.AddU<MountPicker>(go);
                picker.coat = coat;
                picker.visual = mv;
                picker.chosenMarker = marker;
                GenUtil.SetInteractText(picker, coat == 6 ? "骨の馬にする" : "この毛色の馬にする");
                ctx.mountPickers.Add(picker);
                // つなぐ杭
                GenUtil.Prim(PrimitiveType.Cylinder, "HitchPost", corral, Vector3.zero, new Vector3(0.15f, 0.6f, 0.15f), DarkWood)
                    .transform.position = L(-41.5f, -26f + coat * 5.5f) + Vector3.up * 0.6f;
            }
            // 厩舎（背景専用：柵の奥、見えない壁で囲む）
            var stable = GenUtil.PlaceModel(GenUtil.Prop("Stable"), corral, "Stable", L(-62f, -10f), 90f, 1f, new Vector3(14f, 6f, 6f), Wood);
            DressingBuilder.AddCollider(stable, true);
            DressingBuilder.BackgroundWalls(stable, 4f);
        }

        // ------------------------------------------------------------------
        // たまり場
        // ------------------------------------------------------------------

        static void Hangout(Transform root, WorldContext ctx)
        {
            var h = GenUtil.World(root, "Hangout", L(30f, -25f)).transform;
            var fire = GenUtil.InstantiatePart(GenUtil.Pack("Campfire"), "Campfire_01", h, L(30f, -25f), 0f, 1f, new Vector3(1.5f, 0.6f, 1.5f), DarkWood);
            fire.name = "Campfire";
            GenUtil.Particles(fire.transform, "Flames", new Vector3(0f, 0.4f, 0f), new Color(1f, 0.55f, 0.15f, 0.9f), 0.35f, 1.2f, 0.8f, true, 25f, 0.3f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 p = L(30f, -25f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 3.2f);
                var bench = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", h, p, a + 90f, 1.4f, new Vector3(1.4f, 0.35f, 0.4f), Wood);
                bench.name = "Bench_" + i;
            }
            // 前回のレースの黒板（焚き火の側＝東から読める。文字は yaw の方向を見ている人に読める）
            ctx.rankBoards.Add(CourseBuilder.ChalkBoardAt(h, L(18f, -26f), -90f, "LastRaceBoard", true));
            ShowBuilder.BuildBoards(h, ctx, L(30f, -40f));
            // 薪の山
            for (int i = 0; i < 6; i++)
            {
                var log = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_0" + (2 + i), h, L(41f + (i % 3) * 0.9f, -17f) + Vector3.up * (i / 3) * 0.3f,
                    90f + i * 7f, 1.2f, new Vector3(0.3f, 0.3f, 0.8f), Wood);
                DressingBuilder.SetStatic(log);
            }
        }

        // ------------------------------------------------------------------
        // 練習場
        // ------------------------------------------------------------------

        static void Practice(Transform root, WorldContext ctx)
        {
            Vector3 c = CENTER + PRACTICE_OFFSET;
            var pr = GenUtil.World(root, "PracticeArea", c).transform;
            var trig = pr.gameObject.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(110f, 24f, 110f);
            trig.center = new Vector3(0f, 10f, 0f);
            pr.gameObject.layer = WorldLayers.TRIGGERS;
            var area = GenUtil.AddU<PracticeArea>(pr.gameObject);
            area.releaseRadius = 85f;   // 外周（中心から最大 78m）より少し大きく
            ctx.practiceArea = area;

            var feats = GenUtil.Child(pr, "Features", Vector3.zero).transform;
            // 坂（12°、16m）→ 台（高さ 3.3m）→ 反対側は崖下り
            Solid(feats, "Slope", new Vector3(-20f, 1.6f, 0f), new Vector3(8f, 0.6f, 16.4f), Quaternion.Euler(-11.7f, 0f, 0f));
            Solid(feats, "CliffTop", new Vector3(-20f, 1.65f, 13f), new Vector3(8f, 3.3f, 10f), Quaternion.identity);
            // ジャンプ台（15°、6m）
            Solid(feats, "JumpRamp", new Vector3(18f, 0.6f, 10f), new Vector3(6f, 0.5f, 6.2f), Quaternion.Euler(-15f, 0f, 0f));
            // 小さな丘
            Solid(feats, "Bump", new Vector3(0f, -0.75f, -25f), new Vector3(12f, 2.4f, 12f), Quaternion.Euler(0f, 45f, 0f));
            // 周回の目印の干し草
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * 360f;
                Vector3 p = c + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 42f);
                var hay = GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), i % 3 == 0 ? "HayBales_01" : "HayBales_0" + (2 + i % 5), feats, p, a, 1f,
                    new Vector3(1f, 0.6f, 0.7f), new Color(0.85f, 0.75f, 0.4f));
                DressingBuilder.AddCollider(hay, false);
            }
            // カカシ
            for (int i = 0; i < 2; i++)
            {
                var d = GenUtil.World(pr, "Scarecrow_" + (i + 1), c + new Vector3(10f + i * 12f, 0f, -8f - i * 10f), 180f);
                var pivot = GenUtil.Child(d.transform, "Pivot", Vector3.zero).transform;
                GenUtil.Prim(PrimitiveType.Cylinder, "Pole", pivot, new Vector3(0f, 1f, 0f), new Vector3(0.12f, 1f, 0.12f), DarkWood);
                GenUtil.Prim(PrimitiveType.Cube, "Arms", pivot, new Vector3(0f, 1.5f, 0f), new Vector3(1.4f, 0.1f, 0.1f), DarkWood);
                GenUtil.Prim(PrimitiveType.Sphere, "Head", pivot, new Vector3(0f, 2.0f, 0f), Vector3.one * 0.4f, new Color(0.85f, 0.75f, 0.4f));
                GenUtil.Prim(PrimitiveType.Cube, "Shirt", pivot, new Vector3(0f, 1.35f, 0f), new Vector3(0.6f, 0.6f, 0.25f), new Color(0.6f, 0.2f, 0.15f));
                var cap = d.AddComponent<CapsuleCollider>();
                cap.radius = 0.45f;
                cap.height = 2.2f;
                cap.center = new Vector3(0f, 1.1f, 0f);
                d.layer = WorldLayers.Rider;
                var dummy = GenUtil.AddU<PracticeDummy>(d);
                dummy.pivot = pivot;
                dummy.hitSound = GenUtil.Audio(d, false);
                ctx.dummies.Add(dummy);
            }
            // 練習用の馬の杭（馬は SceneAssembler が作って mount をつなぐ）
            for (int i = 0; i < 2; i++)
            {
                Vector3 pp = c + new Vector3(-45f, 0f, -45f + i * 7f);
                var post = GenUtil.World(pr, "PracticePost_" + (i + 1), pp, 90f);
                GenUtil.Prim(PrimitiveType.Cylinder, "Post", post.transform, new Vector3(0f, 0.65f, 0f), new Vector3(0.2f, 0.65f, 0.2f), DarkWood);
                var col = post.AddComponent<BoxCollider>();
                col.size = new Vector3(0.5f, 1.4f, 0.5f);
                col.center = new Vector3(0f, 0.7f, 0f);
                var home = GenUtil.World(pr, "PracticeHome_" + (i + 1), pp + new Vector3(2.5f, 0.05f, 0f), 90f).transform;
                var hp = GenUtil.AddU<PracticeHorsePost>(post);
                hp.home = home;
                hp.coat = i;
                GenUtil.SetInteractText(hp, "練習用の馬に乗る");
                ctx.practicePosts.Add(hp);
                ctx.practiceHomes.Add(home);
            }
            GenUtil.Text(pr, "Sign", c + new Vector3(-48f, 2.2f, -38f), Quaternion.Euler(0f, 90f, 0f), "練習場\n杭に触れると練習用の馬に乗れます", 3f, Paper, new Vector2(4f, 1.5f));
            // 練習場の遺物（拾われて 10 秒で戻る）
            var relic = CourseBuilder.Relic(pr, "PracticeRelic", c + new Vector3(0f, 0f, 30f), 3, false);
            relic.practiceRespawnSeconds = 10f;
            ctx.practiceRelics.Add(relic);
        }

        /// <summary>乗り物が立てる面（Ground）の箱。</summary>
        static GameObject Solid(Transform parent, string name, Vector3 local, Vector3 size, Quaternion rot)
        {
            var go = GenUtil.Prim(PrimitiveType.Cube, name, parent, local, size, Ramp, true);
            go.transform.localRotation = rot;
            go.layer = WorldLayers.Ground;
            return go;
        }

        // ------------------------------------------------------------------

        static void Parking(Transform root, WorldContext ctx)
        {
            var p = GenUtil.World(root, "MountParking", L(-12f, -45f)).transform;
            for (int i = 0; i < 8; i++)
                ctx.parking.Add(GenUtil.World(p, "Parking_" + i, L(-30f + i * 5f, -45f) + Vector3.up * 0.05f, 0f).transform);
            Fence.Line(p, L(-33f, -49f), L(8f, -49f));
        }

        static void SpareRelics(Transform root, WorldContext ctx)
        {
            var p = GenUtil.World(root, "SpareRelicPool", L(-150f, -100f)).transform;
            for (int i = 0; i < 4; i++)
                ctx.spareRelics.Add(CourseBuilder.Relic(p, "SpareRelic_" + (i + 1), L(-156f + i * 4f, -100f), i, true));
        }

        static void Watch(Transform root, WorldContext ctx)
        {
            var go = GenUtil.World(root, "PocketWatch", L(0f, 0f) + Vector3.up * 1.2f);
            var w = GenUtil.Child(go.transform, "Watch", Vector3.zero).transform;
            GenUtil.Prim(PrimitiveType.Cylinder, "Case", w, Vector3.zero, new Vector3(0.05f, 0.004f, 0.05f), Brass)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GenUtil.Prim(PrimitiveType.Cylinder, "Face", w, new Vector3(0f, 0f, -0.0045f), new Vector3(0.044f, 0.0005f, 0.044f), Paper)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var face = GenUtil.Text(w, "FaceText", new Vector3(0f, -0.008f, -0.006f), Quaternion.identity, "", 0.08f, Ink, new Vector2(0.04f, 0.015f));
            var min = GenUtil.Child(w, "MinuteHand", new Vector3(0f, 0f, -0.0055f)).transform;
            GenUtil.Prim(PrimitiveType.Cube, "Hand", min, new Vector3(0f, 0.008f, 0f), new Vector3(0.0015f, 0.016f, 0.0005f), Ink);
            var sec = GenUtil.Child(w, "SecondHand", new Vector3(0f, 0f, -0.006f)).transform;
            GenUtil.Prim(PrimitiveType.Cube, "Hand", sec, new Vector3(0f, 0.01f, 0f), new Vector3(0.0008f, 0.02f, 0.0005f), new Color(0.6f, 0.1f, 0.1f));
            var pw = GenUtil.AddU<PocketWatch>(go);
            pw.watchRoot = w.gameObject;
            pw.faceText = face;
            pw.minuteHand = min;
            pw.secondHand = sec;
            ctx.watch = pw;
        }
    }
}
