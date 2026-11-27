using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ロビー（spec 5.6 / 5.14、PLAYTEST Task 8〜10 のロビー側の配置をすべて自動化）。
    ///   広さ：ロビー全体 = 町（柵 LobbyLayout.FENCE_RECT = 町の外形 + 8m ≈ 97m × 180m、地面はその外 38m まで＝長方形の Terrain）。
    ///   地面：平らな Terrain（LobbyLayout.GROUND_RECT、地面の高さ y = 0）。町の足元だけ 1m 下げて通りの板と地面がちらつかないようにする。
    ///         町の通りの高さに見えない Ground の板（TownStreetGround：乗り物・遺物・岩壁が立つ面。町の模型は Block）。
    ///   町：Packs/WesternTown を「通りの高さ」だけ沈めて置く。模型は建物・小物・地面・丘ごとに別のオブジェクトで、
    ///       1 つずつ手直し用の id（Lobby/Town/WesternTown|部品名#番号）が付く（Save Layout で建物を動かす・消す）。
    ///       （通りの高さはメッシュの上向きの面の面積ヒストグラムの最頻値、
    ///       取れなければ STREET_HEIGHT_FALLBACK = 13.75m。Blender で同じ方法で測った値）。岩山の端が北。
    ///       模型の z ≈ 140 の柵の北（教会の裏・岩山）は背景専用（見えない壁 TownWalls/Back）。
    ///   すべて町の中の空き地（位置は LobbyLayout、Blender で測った建物・岩・サボテンの無い平らな所）：
    ///   広場（町の南の入口）：受付カウンター（FarmProps の机＋台帳 EntryLedger）、手配書の壁（PosterCard ×6）、スタート台の鐘（StartBell）、
    ///         補助表示のレバー（MinimalHud）、スポーン位置。
    ///   西の空き地：乗り物の待機場所 ×8、柵の馬 MountPicker ×7（毛色 0〜5＋骨の馬 6）と柵、
    ///         たまり場（焚き火・丸太のベンチ・前回レースの黒板 RankBoard showLastRace・掲示板 3 枚と中継・新聞売り（ShowBuilder））、予備の遺物 ×4 の置き場。
    ///   練習場（町の東、線路の東の空き地 17m × 53m）：練習用の馬の杭 ×2・ジャンプ台・小さな丘・カカシ ×2・干し草・練習場の遺物、全体を覆うトリガー（PracticeArea）。
    ///   厩舎（背景専用・見えない壁）：町の奥の柵の北。懐中時計、補助表示の紙。
    /// </summary>
    public static class LobbyBuilder
    {
        public static readonly Vector3 CENTER = WorldBounds.LOBBY_CENTER;
        public static readonly Vector4 GROUND_RECT = LobbyLayout.GROUND_RECT;   // ロビー中心からの相対（長方形）
        public const float GROUND_HEIGHT_RANGE = 80f;   // 外周の尾根（〜25m）が入る高さ
        public const float RIM_WIDTH_M = LobbyLayout.RIM_WIDTH_M, RIM_MIN_M = LobbyLayout.RIM_MIN_M, RIM_MAX_M = LobbyLayout.RIM_MAX_M;   // ロビーの外周の尾根
        public const float BORDER_OUT_M = 6f;          // 見えない壁：柵の 6m 外（尾根の手前）
        public const float BERM_M = 8f, BERM_HEIGHT_M = 0.6f;   // 町の足元のまわりを少し盛って土台の縁を隠す
        public const float GROUND_LEVEL = 2f;            // 地形の高さ 2m（Terrain は y = -2 に置く）→ 地面 y = 0
        public const float STREET_HEIGHT_FALLBACK = 13.75f;
        public static readonly Vector3 PRACTICE_OFFSET = LobbyLayout.PRACTICE_CENTER;

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
            ctx.lobbyTerrain = ctxLobbyTerrain;
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
        /// <summary>ロビー中心からの相対位置（LobbyLayout の定数）→ ワールド座標。</summary>
        static Vector3 P(Vector3 rel) => CENTER + rel;

        // ------------------------------------------------------------------
        // 町のまわりの飾り（DressingPlan.ForLobby）と町の壁の手配書
        // ------------------------------------------------------------------

        public const int WALL_POSTERS_MAX = 18;
        static readonly string[] CardNames = { "Wanted_Original", "Wanted_Horse", "Wanted_Camel", "Wanted_Dust", "Wanted_Lasso", "Wanted_Wall", "Wanted_Ball" };

        /// <summary>
        /// 飾りを置かない円（ロビー中心からの相対 x/z と半径）。遊びに使う物はすべて町の中（町の箱は飾りの対象外）なので、
        /// 町の外で避けるのは南の入口の前（柵との間の 8m、広場への入口）と、町の東西の端から広場・練習場への出入りの口だけ。
        /// </summary>
        public static List<Vector4> LobbyExclusions() => LobbyLayout.LobbyDressingExclusions();

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
            int fi = 0;
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
                GenUtil.Tag(piece, "Fence/LobbyFence/FarmProps|" + Fence.PART + "#" + fi++);
            }
            var belt = GenUtil.World(root, "LobbyTreeBelt", CENTER).transform;
            var trees = LobbyLayout.TreeBelt(fenceRect, 3);
            // 幹の足元を実際の地形の高さ − 0.2m に置き直す（尾根のノイズ・土手の分。根元が斜面から浮かない：art-r1-05）
            foreach (var t in trees) t.position = new Vector3(t.position.x, TerrainY(t.position) - 0.2f, t.position.z);
            DressingBuilder.Instantiate(trees, belt, CENTER, "Belt");
            // 尾根の上の岩の冠（切れ目あり、見晴らしの切れ目には置かない）。高さの 25% を地形に埋める
            var crownRoot = GenUtil.World(root, "LobbyRimCrown", CENTER).transform;
            var crown = LobbyLayout.RimCrown(fenceRect, 11);
            foreach (var c in crown) c.position = new Vector3(c.position.x, TerrainY(c.position) - 0.25f * c.height, c.position.z);
            DressingBuilder.Instantiate(crown, crownRoot, CENTER, "Crown");
            Debug.Log("[HorseRace] ロビーの柵 " + segs.Count + " 区間（切れ目 " + LobbyLayout.Gates.Length + "）、柵の外の木の帯 " + trees.Count + " 本、尾根の岩 " + crown.Count + " 個");
        }

        static Terrain lobbyTerrain;

        /// <summary>ロビー中心からの相対位置 rel の地形の高さ（ロビー中心からの相対 y）。地形が無ければ LobbyLayout の式。</summary>
        static float TerrainY(Vector3 rel)
        {
            if (lobbyTerrain == null) return LobbyLayout.GroundHeight(fenceRect, rel);
            Vector3 w = CENTER + rel;
            return lobbyTerrain.SampleHeight(w) + lobbyTerrain.transform.position.y - CENTER.y;
        }

        static void Dressing(Transform root, GameObject town)
        {
            var dr = GenUtil.Child(root, "TownDressing", Vector3.zero).transform;
            dr.position = CENTER;
            Bounds tb = GenUtil.RendererBounds(town);
            var plan = DressingPlan.ForLobby(new Vector2(tb.min.x - CENTER.x, tb.min.z - CENTER.z), new Vector2(tb.max.x - CENTER.x, tb.max.z - CENTER.z),
                LobbyExclusions(), 4242);
            DressingBuilder.Instantiate(plan, dr, CENTER, "Lobby");
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
                GenUtil.Tag(holder, "Lobby/WallPoster/WantedPosters|#" + n);
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
            TagTownParts(m.transform);
            // 町の真ん中の一番高い建物（酒場の通り）の煙突の煙（art-r1-15）
            var tall = GenUtil.FindContains(m.transform, "WesternTown_084");
            if (tall != null)
            {
                Bounds tb2 = GenUtil.RendererBounds(tall.gameObject);
                GenUtil.SmokeColumn(holder.transform, "ChimneySmoke", holder.transform.InverseTransformPoint(new Vector3(tb2.center.x + tb2.extents.x * 0.4f, tb2.max.y, tb2.center.z)),
                    SetPieceBuilder.SmokeGrey, 18f, 1.2f);
            }
            return holder;
        }

        /// <summary>
        /// 町の模型は建物・小物ごとに別のオブジェクト（tools/blender/split_parts.py、名前は WesternTown_NNN_種類、
        /// 一覧は WesternTown.parts.json）。1 つずつ手直し用の id を付け、Save Layout で建物を動かす・消すことができるようにする。
        /// 当たり判定（MeshCollider）は部品自身に付いているので一緒に動く。
        /// </summary>
        public static int TagTownParts(Transform model)
        {
            var parts = new List<Transform>();
            for (int i = 0; i < model.childCount; i++) parts.Add(model.GetChild(i));
            parts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));   // FBX の子の順番に頼らない（同じ FBX なら同じ id）
            for (int i = 0; i < parts.Count; i++)
                GenUtil.Tag(parts[i].gameObject, LayoutOverrides.IdForPart("Lobby", "Town", "WesternTown", LayoutOverrides.StripId(parts[i].name), i));
            Debug.Log("[HorseRace] WesternTown の部品 " + parts.Count + " 個に手直し用の id を付けました（建物ごとに動かす・消すことができます）");
            return parts.Count;
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
            // 長方形の Terrain（TerrainData.size の x と z が別）：高さマップは正方形の格子のまま、セルが長方形になる
            int res = 257;   // 練習場の小さな丘を地形で作るので細かめ（約 0.7m × 1m）
            float sizeX = GROUND_RECT.z - GROUND_RECT.x, sizeZ = GROUND_RECT.w - GROUND_RECT.y;
            float cellX = sizeX / (res - 1), cellZ = sizeZ / (res - 1);
            Vector3 origin = new Vector3(CENTER.x + GROUND_RECT.x, CENTER.y - GROUND_LEVEL, CENTER.z + GROUND_RECT.y);
            Bounds tb = GenUtil.RendererBounds(town);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = origin.x + x * cellX, wz = origin.z + z * cellZ;
                    bool under = wx > tb.min.x + 1f && wx < tb.max.x - 1f && wz > tb.min.z + 1f && wz < tb.max.z - 1f;
                    float level = under ? GROUND_LEVEL - 1f : GROUND_LEVEL;
                    if (!under)
                    {
                        // 町の外周の小さな土手（入口は除く）：土台の縁・裏を見せない
                        float dx = Mathf.Max(0f, Mathf.Max(tb.min.x - wx, wx - tb.max.x));
                        float dz = Mathf.Max(0f, Mathf.Max(tb.min.z - wz, wz - tb.max.z));
                        float dd = Mathf.Sqrt(dx * dx + dz * dz);
                        bool entrance = wz < tb.min.z;   // 南の辺（広場の前）は土手なし：柵との間の地面から町へそのまま入れる
                        if (dd < BERM_M && !entrance) level += BERM_HEIGHT_M * (1f - dd / BERM_M);
                    }
                    // 柵の外：ゆるい盛り上がりと低い尾根（北は高いまま、見晴らしの切れ目は低く：LobbyLayout.GroundHeight、art-r1-05）
                    var rel = new Vector3(wx - CENTER.x, 0f, wz - CENTER.z);
                    float lx = x * cellX, lz = z * cellZ;
                    float noise = Mathf.Clamp01(0.8f * HeightmapGen.Noise01(lx / 140f, lz / 140f, 99, 21) + 0.2f * HeightmapGen.Noise01(lx / 35f, lz / 35f, 99, 22));
                    level += LobbyLayout.GroundHeight(fenceRect, rel, noise);
                    // 練習場の小さな丘（上面は通り +0.45m、半径 5m でなめらかに）：当たり判定は見えない台（Bump）
                    float db = WorldLayout.DistXZ(rel, LobbyLayout.PRACTICE_BUMP);
                    if (db < 6f) level = Mathf.Max(level, GROUND_LEVEL + 0.45f - 1.45f * Mathf.Clamp01((db - 1.5f) / 4.5f));
                    h[z, x] = level / GROUND_HEIGHT_RANGE;
                }
            var td = TerrainBuilder.CreateTerrainData("Lobby", h, sizeX, sizeZ, GROUND_HEIGHT_RANGE);
            PaintLobby(td, h, cellX, cellZ, origin);
            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "LobbyGround";
            tgo.transform.SetParent(root, false);
            tgo.transform.position = origin;
            GenUtil.SetLayerRecursive(tgo, WorldLayers.Ground);
            lobbyTerrain = tgo.GetComponent<Terrain>();
            TerrainBuilder.ApplyToonMaterial(lobbyTerrain);
            ctxLobbyTerrain = lobbyTerrain;

            // 外周の見えない壁は柵の 6m 外（柵が広がっても柵の外側。尾根の手前〜斜面）
            {
                var bw = GenUtil.World(root, "LobbyBorder", CENTER).transform;
                Vector4 r = new Vector4(fenceRect.x - BORDER_OUT_M, fenceRect.y - BORDER_OUT_M, fenceRect.z + BORDER_OUT_M, fenceRect.w + BORDER_OUT_M);
                float cx = (r.x + r.z) * 0.5f, cz = (r.y + r.w) * 0.5f, w = r.z - r.x, d = r.w - r.y, hgt = TerrainBuilder.WALL_HEIGHT_M;
                AddBox(bw, "N", new Vector3(cx, hgt * 0.5f - 20f, r.w), new Vector3(w + 2f, hgt, 2f), false);
                AddBox(bw, "S", new Vector3(cx, hgt * 0.5f - 20f, r.y), new Vector3(w + 2f, hgt, 2f), false);
                AddBox(bw, "E", new Vector3(r.z, hgt * 0.5f - 20f, cz), new Vector3(2f, hgt, d + 2f), false);
                AddBox(bw, "W", new Vector3(r.x, hgt * 0.5f - 20f, cz), new Vector3(2f, hgt, d + 2f), false);
                GenUtil.SetLayerRecursive(bw.gameObject, WorldLayers.Block);
            }
            TownWallsAndSkirt(root, town, tb);
        }

        static Terrain ctxLobbyTerrain;

        /// <summary>
        /// ロビーの地面の塗り（art-r1-05）：柵の中は土、柵の外の土手は砂（ゆるい所）と岩（急な所、18〜28°）、その間は土。
        /// 一色の赤い土の壁にしない。
        /// </summary>
        static void PaintLobby(TerrainData td, float[,] h, float cellX, float cellZ, Vector3 origin)
        {
            td.terrainLayers = TerrainBuilder.Layers(TerrainBuilder.VARIANT_LOBBY);
            int res = 256, n = h.GetLength(0);
            td.alphamapResolution = res;
            var a = new float[res, res, 4];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int hz = Mathf.FloorToInt(z / (float)(res - 1) * (n - 1) + 0.5f), hx = Mathf.FloorToInt(x / (float)(res - 1) * (n - 1) + 0.5f);
                    float slope = TerrainBuilder.SlopeDeg(h, hz, hx, GROUND_HEIGHT_RANGE, cellX, cellZ);
                    var rel = new Vector3(origin.x + hx * cellX - CENTER.x, 0f, origin.z + hz * cellZ - CENTER.z);
                    float outside = Mathf.Clamp01(LobbyLayout.DistOutside(fenceRect, rel) / 3f);
                    float rock = Mathf.Clamp01((slope - 18f) / 10f) * outside;
                    float sand = (1f - Mathf.Clamp01((slope - 6f) / 10f)) * outside * (1f - rock);
                    a[z, x, TerrainBuilder.LAYER_ROCK] = rock;
                    a[z, x, TerrainBuilder.LAYER_SAND] = sand;
                    a[z, x, TerrainBuilder.LAYER_STRATA] = 0f;
                    a[z, x, TerrainBuilder.LAYER_DIRT] = Mathf.Max(0f, 1f - rock - sand);
                }
            td.SetAlphamaps(0, 0, a);
        }

        /// <summary>
        /// 町＝ロビー全体なので、町の外周の見えない壁は無い（外へは柵と柵の外の見えない壁で止まる）。
        ///   奥の壁：模型の柵（z ≈ 140）のすぐ北に、ロビーの柵の東西いっぱいの見えない壁（教会の裏の空き地・岩山・厩舎へ行けない）。
        ///   通りの Ground：町の模型は Block レイヤーなので、乗り物（MountController の groundMask = Ground）・遺物・岩壁が立てるよう、
        ///         通りの高さに見えない Ground の板を町の外形いっぱいに敷く（建物の中は建物の当たり判定で入れない）。
        ///   足元の岩：東・西・北の辺に岩を並べて沈めた土台の縁を隠す（南の辺＝広場の前は開ける）。
        /// </summary>
        static void TownWallsAndSkirt(Transform root, GameObject town, Bounds tb)
        {
            var walls = GenUtil.World(root, "TownWalls", CENTER).transform;
            float backZ = TownBackZ(tb) - CENTER.z, hgt = 30f;
            float wx0 = fenceRect.x - BORDER_OUT_M, wx1 = fenceRect.z + BORDER_OUT_M;
            AddBox(walls, "Back", new Vector3((wx0 + wx1) * 0.5f, hgt * 0.5f - 5f, backZ), new Vector3(wx1 - wx0 + 2f, hgt, 1f), false);
            GenUtil.SetLayerRecursive(walls.gameObject, WorldLayers.Block);

            var street = GenUtil.World(root, "TownStreetGround", new Vector3(tb.center.x, CENTER.y + 0.02f - 0.5f, tb.center.z));
            var sb = street.AddComponent<BoxCollider>();
            sb.size = new Vector3(tb.size.x, 1f, tb.size.z);
            street.layer = WorldLayers.Ground;

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
                if (p.z < tb.min.z + 2f) continue;   // 南の辺（広場の前）は開ける
                var r = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), rocks[k % rocks.Length], skirt, p - Vector3.up * 0.6f,
                    k * 47f, 1.3f, new Vector3(2.5f, 1.5f, 2.5f), new Color(0.6f, 0.45f, 0.35f));
                DressingBuilder.SetStatic(r);
                GenUtil.Tag(r, "Lobby/TownSkirt/DesertRocks|" + rocks[k % rocks.Length] + "#" + k);
            }
        }

        /// <summary>奥の見えない壁の z（ワールド）：実際の町の南端からの距離で決める（町が少しずれても模型の柵に合う）。</summary>
        static float TownBackZ(Bounds tb) => tb.min.z + (LobbyLayout.TOWN_BACK_Z - LobbyLayout.TOWN_NOMINAL_RECT.y);

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
            // スポーン：町の南の入口の空き地、北北東（町の大通りが正面の真ん中）を向く。受付・手配書は視線の左
            ctx.lobbySpawn = GenUtil.World(root, "LobbySpawn", P(LobbyLayout.SPAWN) + Vector3.up * 0.05f, LobbyLayout.SPAWN_YAW).transform;

            // 受付カウンター（FarmProps_05 = 机）と台帳。カウンターは南（スポーン）を向く
            Vector3 desk = P(LobbyLayout.LEDGER_DESK);
            var counter = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_05", root, desk, 180f, 1f,
                new Vector3(2.5f, 1f, 1.2f), Wood);
            counter.name = "ReceptionCounter";
            DressingBuilder.AddCollider(counter, false);
            float top = GenUtil.RendererBounds(counter).max.y;
            if (top < 0.5f) top = 1f;
            // 机のまわり（西の町の物（x ≤ -15.6）に重ならないよう、机の後ろと東）
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_15", root, desk + new Vector3(2.4f, 0f, 1.2f), 0f, 1f, new Vector3(0.7f, 0.8f, 0.7f), Wood).name = "Barrel";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_12", root, desk + new Vector3(1.2f, 0f, 2.2f), 20f, 1f, new Vector3(1f, 1f, 1f), Wood).name = "Crate";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", root, desk + new Vector3(0f, 0f, 1.8f), 180f, 1f, new Vector3(0.3f, 3f, 0.3f), DarkWood).name = "StreetLamp";
            GenUtil.PointLight(root, "DeskLamp", desk + new Vector3(0f, 2.9f, 1.8f), SetPieceBuilder.LampWarm, 8f, 1.2f);

            LedgerBook(root, ctx, new Vector3(desk.x, CENTER.y + top + 0.005f, desk.z), 180f, "EntryLedger", "台帳にサインする");
            // 台帳の札：机の後ろの掲示板（NoticeBoard ×0.5）の板に書く（宙に浮かせない）
            NoticeSign(root, "LedgerSign", desk + new Vector3(0f, 0f, 1.0f), 0f, 0.5f, "<b><size=140%>受付</size></b>\n台帳にサインでエントリー", 1.0f,
                UiStyle.InkFace, TextStyle.Ink, Paper);

            PosterWall(root, ctx);

            // スタート台と鐘（広場の西、art-r1-12）：台 = 丸太の束（WoodLogs_01 ×2）を板のように並べ、四隅に干し草（HayBales_01）。
            // 当たり判定は見えない箱（3 × 0.6 × 3.5）。鐘の柱 = 杭（DesertProps_03）、鐘 = 真鍮に塗った街灯の頭（FarmProps_02）。触る判定は見えない球
            Vector3 sb = P(LobbyLayout.START_BELL);
            var stage = GenUtil.World(root, "StartPlatform", sb + Vector3.up * 0.3f);
            var stageBox = stage.AddComponent<BoxCollider>();
            stageBox.size = new Vector3(3.2f, 0.6f, 3.6f);
            stage.layer = WorldLayers.Block;
            for (int i = 0; i < 2; i++)
            {
                var plank = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", root, sb + new Vector3(0f, 0f, -0.9f + i * 1.8f), 90f * i, 2f,
                    new Vector3(1.5f, 0.3f, 1f), Wood);
                plank.name = "Plank_" + i;
                DressingBuilder.SetStatic(plank);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = sb + new Vector3((i % 2 == 0 ? -1f : 1f) * 2.3f, 0f, (i < 2 ? -1f : 1f) * 2.3f);
                var hay = GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", root, corner, i * 47f, 0.7f, new Vector3(1.5f, 1.6f, 1.5f), Wood);
                hay.name = "PlatformHay_" + i;
                DressingBuilder.SetStatic(hay);
            }
            // 号砲（ui-r1-19）：台の上の机（FarmProps_05 ×0.45）に拳銃（Packs/Revolver）を置く。撃鉄・弾倉は模型の部品を StartBell が動かす。
            // 触る判定は拳銃のまわりの見えない球。札は手配書の紙（WantedPosters_02）に書いて机の前に立てる
            Vector3 tableP = sb + Vector3.up * 0.6f;
            var table = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_05", root, tableP, 180f, 0.45f, new Vector3(1.1f, 0.45f, 0.55f), Wood);
            table.name = "PistolTable";
            DressingBuilder.SetStatic(table);
            float tableTop = GenUtil.RendererBounds(table).max.y;
            if (tableTop < tableP.y + 0.2f) tableTop = tableP.y + 0.45f;
            var bellGo = GenUtil.World(root, "StartBell", new Vector3(sb.x, tableTop + 0.02f, sb.z), 180f);
            var bell = GenUtil.AddU<StartBell>(bellGo);
            var gun = GenUtil.InstantiateModel(GenUtil.Pack("Revolver"), bellGo.transform, "StartersPistol");
            Vector3 muzzle = bellGo.transform.position + Vector3.up * 0.1f;
            if (gun != null)
            {
                GenUtil.FitMax(gun, 0.45f);   // 遠くからも拳銃と分かる大きさ（儀式用の大きな号砲）
                Bounds gb = GenUtil.RendererBounds(gun);
                gun.transform.position += new Vector3(bellGo.transform.position.x - gb.center.x, bellGo.transform.position.y - gb.min.y, bellGo.transform.position.z - gb.center.z);
                bell.hammer = GenUtil.FindDeep(gun.transform, "Hammer");
                bell.cylinder = GenUtil.FindDeep(gun.transform, "Revolving_Cylinder");
                var barrel = GenUtil.FindDeep(gun.transform, "Magnum_Barrel");
                if (barrel != null)
                {
                    Bounds bb = GenUtil.RendererBounds(barrel.gameObject);
                    muzzle = bb.center;
                }
            }
            var bellCol = bellGo.AddComponent<SphereCollider>();
            bellCol.center = new Vector3(0f, 0.12f, 0f);
            bellCol.radius = 0.4f;
            bell.smoke = GenUtil.Particles(bellGo.transform, "MuzzleSmoke", bellGo.transform.InverseTransformPoint(muzzle), new Color(0.85f, 0.85f, 0.85f, 0.7f),
                0.35f, 1.2f, 1.6f, false, 0f, 0.08f);
            var bm = bell.smoke.emission;
            bm.rateOverTime = 40f;
            var sm = bell.smoke.main;
            sm.duration = 0.3f;
            bell.ringSound = GenUtil.Audio(bellGo, true);
            bell.tapSound = GenUtil.Audio(bellGo, false);
            // 札：机の南（スポーン側）に立てた紙。読む人は北を見る
            var noteHolder = GenUtil.World(root, "BellNote", sb + new Vector3(0f, 0.6f, -0.85f), 0f);
            GenUtil.Stake(noteHolder.transform, "DesertProps_05", noteHolder.transform.position + noteHolder.transform.forward * 0.06f, 0f, 0.42f).name = "NotePost";
            GenUtil.PaperSheet(noteHolder.transform, "Paper", "WantedPosters_02", new Vector3(0f, 0.55f, 0.01f), null, 1.2f, 0.5f);
            bell.noteText = GenUtil.Text(noteHolder.transform, "Text", new Vector3(0f, 0.8f, -0.01f), Quaternion.identity,
                StartBell.DefaultNote(), 0.8f, UiStyle.InkFace, new Vector2(1.1f, 0.42f), TextStyle.Ink, true);
            GenUtil.SetInteractText(bell, "号砲を撃つ（レース開始）");
            ctx.startBell = bell;

            Hud(root, ctx);
        }

        static void PosterWall(Transform root, WorldContext ctx)
        {
            PosterWallAt(root, ctx, P(LobbyLayout.WANTED_WALL), 0f, "WantedWall", "<b><size=140%>手配書</size></b>　―一枚取って乗り物と能力を選ぶ―", 1.2f);
        }

        /// <summary>台帳（机の上の見開き）。yaw = 本の向き（読む人は yaw + 180 を見る）。ロビーの受付とゴール後の囲い（system-r1-08）で使う。</summary>
        internal static EntryLedger LedgerBook(Transform root, WorldContext ctx, Vector3 pos, float yaw, string name, string interact)
        {
            // 台帳（ui-r1-06）：机の上に手配書の紙（WantedPosters_03）2 枚を平らに並べた見開き。左 = エントリー一覧、右 = 自分の選択（ローカル）
            var book = GenUtil.World(root, name, pos, yaw);
            var pages = GenUtil.Child(book.transform, "Pages", Vector3.zero).transform;
            pages.localRotation = Quaternion.Euler(90f, 180f, 0f);   // 紙の面を上へ、文字の上を机の奥へ（スポーン側から読める）
            const float PW = 0.33f, PH = 0.45f;
            GenUtil.PaperSheet(pages, "LeftPage", "WantedPosters_03", new Vector3(-PW * 0.5f - 0.005f, -PH * 0.5f, 0f), null, PW, PH);
            GenUtil.PaperSheet(pages, "RightPage", "WantedPosters_03", new Vector3(PW * 0.5f + 0.005f, -PH * 0.5f, 0f), null, PW, PH);
            var bc = book.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.75f, 0.2f, 0.55f);
            bc.center = new Vector3(0f, 0.08f, 0f);
            var page = GenUtil.Text(pages, "PageText", new Vector3(-PW * 0.5f - 0.005f, 0f, -0.004f), Quaternion.identity, "エントリー台帳", 0.35f,
                UiStyle.InkFace, new Vector2(PW - 0.03f, PH - 0.04f), TextStyle.Ink, true);
            page.alignment = TMPro.TextAlignmentOptions.TopLeft;
            var choice = GenUtil.Text(pages, "ChoiceText", new Vector3(PW * 0.5f + 0.005f, 0f, -0.004f), Quaternion.identity, "", 0.35f,
                UiStyle.InkFace, new Vector2(PW - 0.04f, PH - 0.06f), TextStyle.Ink, true);
            var ledger = GenUtil.AddU<EntryLedger>(book);
            ledger.pageText = page;
            ledger.choiceText = choice;
            ledger.signSound = GenUtil.Audio(book, false);
            GenUtil.SetInteractText(ledger, interact);
            ctx.ledgers.Add(ledger);
            return ledger;
        }

        /// <summary>
        /// 手配書の壁（art-r1-12）：掲示板の模型（NoticeBoard ×scale、板は暗い木の色）に手配書（馬・ラクダ・能力 4 枚）を貼る。当たり判定は板の見えない箱。
        /// yaw = 読む人が見る向き。ロビーの広場とゴール後の囲い（ステージの合間の乗り換え、system-r1-08）で使う。
        /// </summary>
        internal static void PosterWallAt(Transform root, WorldContext ctx, Vector3 pos, float yaw, string name, string titleText, float WS)
        {
            var wall = GenUtil.World(root, name, pos, yaw);
            var model = GenUtil.PlaceModel(GenUtil.Prop("NoticeBoard"), wall.transform, "Board", wall.transform.position, yaw, WS, new Vector3(4.5f, 3.8f, 0.3f), DarkWood);
            var panel = GenUtil.FindDeep(model.transform, "NoticeBoard_Panel");
            float panelY = 2.45f, panelZ = 0f;
            if (panel != null)
            {
                GenUtil.YawAlign(model.transform, GenUtil.FirstNormalWorld(panel), -wall.transform.forward);
                var pr = panel.GetComponent<Renderer>();
                if (pr != null)
                {
                    pr.sharedMaterial = GenUtil.ToonColor(new Color(0.36f, 0.24f, 0.15f));   // コルク・暗い木
                    Vector3 lc = wall.transform.InverseTransformPoint(pr.bounds.center);
                    panelY = lc.y; panelZ = lc.z;
                }
            }
            var wallBox = model.AddComponent<BoxCollider>();
            wallBox.center = new Vector3(0f, panelY / WS, 0f);
            wallBox.size = new Vector3(4.6f, 2.6f / WS, 0.3f);
            GenUtil.SetLayerRecursive(model, WorldLayers.Block);
            DressingBuilder.SetStatic(model);
            float cz = panelZ - 0.06f;
            var title = GenUtil.Text(wall.transform, "Title", new Vector3(0f, panelY + 1.55f * WS / 1.2f, cz - 0.05f), Quaternion.identity, titleText, 2.2f * WS / 1.2f,
                UiStyle.SignFace, new Vector2(6f, 0.5f), TextStyle.Sign, true);

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
                float y = i < 2 ? panelY + 0.15f : panelY - 0.85f;   // 板の真ん中の上下 2 段（紙の原点は下の辺）
                var go = GenUtil.Child(wall.transform, "Card_" + cards[i].label, new Vector3(x, y, cz));
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
                pc.takeSound = GenUtil.Audio(go, false);
                // カードの下の札（ui-r1-07）：小さな紙（WantedPosters_05）にインクで名前・効果・クールダウン（PosterCard.Start が書く）
                var lab = GenUtil.Child(go.transform, "Label", new Vector3(0f, -0.15f, -0.005f)).transform;
                GenUtil.PaperSheet(lab, "Paper", "WantedPosters_05", new Vector3(0f, -0.13f, 0f), null, 0.52f, 0.26f);
                pc.label = GenUtil.Text(lab, "Text", new Vector3(0f, 0f, -0.006f), Quaternion.identity, cards[i].label, 0.35f, UiStyle.InkFace,
                    new Vector2(0.48f, 0.23f), TextStyle.Ink, true);
                GenUtil.SetInteractText(pc, "手配書を取る：" + cards[i].label);
                ctx.posterCards.Add(pc);
            }
            // 飾りの手配書（取れない）
            Poster(wall.transform, "WantedPosters_01", OriginalCard(), "Decor_L").transform.localPosition = new Vector3(-2.05f, panelY - 0.6f, cz);
            Poster(wall.transform, "WantedPosters_02", OriginalCard(), "Decor_R").transform.localPosition = new Vector3(2.05f, panelY - 0.1f, cz);
        }

        /// <summary>
        /// 掲示板の模型（Props/NoticeBoard × scale）の板に文字を書いた看板。yaw = 読む人が見る向き（文字は -Z 側）。
        /// 板の色を panelColor に塗り、文字は板の大きさに合わせて自動で縮む。模型が無ければ文字だけ。戻り値は文字。
        /// </summary>
        internal static TMPro.TextMeshPro NoticeSign(Transform parent, string name, Vector3 worldPos, float yaw, float scale, string text, float size,
            Color color, TextStyle style, Color panelColor, bool isStatic = true)
        {
            var go = GenUtil.World(parent, name, worldPos, yaw);
            var holder = GenUtil.PlaceModel(GenUtil.Prop("NoticeBoard"), go.transform, "Board", worldPos, yaw, scale, new Vector3(4.5f, 3.8f, 0.3f), DarkWood);
            var panel = GenUtil.FindDeep(holder.transform, "NoticeBoard_Panel");
            Vector3 textPos = new Vector3(0f, 2.15f * scale, -0.04f);
            Vector2 box = new Vector2(3.2f * scale, 1.8f * scale);
            if (panel != null)
            {
                GenUtil.YawAlign(holder.transform, GenUtil.FirstNormalWorld(panel), -go.transform.forward);
                var pr = panel.GetComponent<Renderer>();
                if (pr != null)
                {
                    pr.sharedMaterial = GenUtil.ToonColor(panelColor);
                    Vector3 local = go.transform.InverseTransformPoint(pr.bounds.center);
                    textPos = new Vector3(local.x, local.y, local.z - 0.03f);
                }
            }
            DressingBuilder.AddCollider(holder, false);
            if (isStatic) DressingBuilder.SetStatic(holder);
            return GenUtil.Text(go.transform, "Text", textPos, Quaternion.identity, text, size, color, new Vector2(box.x * 0.92f, box.y * 0.92f), style, true);
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
            // レバー（art-r1-12）：街灯の柱（FarmProps_02）に、真鍮に塗った杭（DesertProps_05 ×0.25）のレバー。字は紙の札
            var lever = GenUtil.World(root, "HudLever", P(LobbyLayout.HUD_LEVER), 180f);
            var leverPost = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", lever.transform, lever.transform.position, 180f, 0.6f,
                new Vector3(0.3f, 1.9f, 0.3f), DarkWood);
            leverPost.name = "Base";
            var handle = GenUtil.Child(lever.transform, "Handle", new Vector3(0f, 1.0f, -0.12f)).transform;
            var rod = GenUtil.Stake(handle, "DesertProps_05", handle.position, 180f, 0.25f);
            rod.name = "Rod";
            foreach (var r in rod.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(Brass);
            GenUtil.PaperSheet(lever.transform, "LabelPaper", "WantedPosters_05", new Vector3(0f, 0.55f, -0.14f), null, 0.6f, 0.3f);
            var col = lever.AddComponent<BoxCollider>();
            col.size = new Vector3(0.5f, 1.6f, 0.5f);
            col.center = new Vector3(0f, 0.8f, 0f);
            var label = GenUtil.Text(lever.transform, "Label", new Vector3(0f, 0.7f, -0.17f), Quaternion.identity, "札：出す", 0.6f, UiStyle.InkFace,
                new Vector2(0.52f, 0.24f), TextStyle.Ink, true);

            var hud = GenUtil.AddU<MinimalHud>(lever);
            BuildNeckCard(root, hud);
            hud.leverHandle = handle;
            hud.leverLabel = label;
            hud.cpTick = GenUtil.Audio(lever, true);
            hud.finishAudio = GenUtil.Audio(lever, true);
            hud.whoosh = GenUtil.Audio(lever, true);
            GenUtil.SetInteractText(hud, "首の札（順位・残り時間）を出す／しまう");
            ctx.hud = hud;
        }

        static readonly Color Leather = new Color(0.40f, 0.25f, 0.15f);
        static readonly Color PipGold = new Color(1.00f, 0.82f, 0.35f);
        static readonly Color PipRed = new Color(0.9f, 0.2f, 0.1f);

        /// <summary>
        /// 首の札（ui-r1-01/03/14）：手配書の紙（WantedPosters_05、0.20 × 0.13m）に TMP の文字 4 つ（見出し・順位・残り時間・進み具合）と、
        /// 紙の下の持久力の印 = 宝石（Gems_02_02〜06、0.025m）5 つ。実行中は MinimalHud が自分の乗り物の首（VR）か頭の前（デスクトップ）へ動かす。
        /// 原点は札の真ん中、文字は -Z 側（乗り手のほう）。
        /// </summary>
        static void BuildNeckCard(Transform root, MinimalHud hud)
        {
            const float W = UiStyle.NECK_CARD_W, H = UiStyle.NECK_CARD_H;
            var card = GenUtil.World(root, "NeckCard", P(LobbyLayout.SPAWN) + new Vector3(0f, 1.5f, 3f));   // 実行中は首・頭の前へ動く
            var paper = GenUtil.PaperSheet(card.transform, "Paper", "WantedPosters_05", new Vector3(0f, -H * 0.5f, 0.002f), null, W, H);
            foreach (var c in paper.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            hud.headerText = GenUtil.Text(card.transform, "Header", new Vector3(0f, H * 0.5f - 0.016f, -0.002f), Quaternion.identity, "", 0.25f,
                UiStyle.InkFace, new Vector2(W - 0.02f, 0.026f), TextStyle.Brass, true);
            hud.rankText = GenUtil.Text(card.transform, "RankText", new Vector3(-0.045f, 0.002f, -0.003f), Quaternion.identity, "", 0.9f,
                UiStyle.InkFace, new Vector2(0.105f, 0.07f), TextStyle.Brass, true);
            hud.timeText = GenUtil.Text(card.transform, "TimeText", new Vector3(0.05f, 0.002f, -0.003f), Quaternion.identity, "", 0.45f,
                UiStyle.InkFace, new Vector2(0.085f, 0.065f), TextStyle.Brass, true);
            hud.progressText = GenUtil.Text(card.transform, "ProgressText", new Vector3(0f, -H * 0.5f + 0.014f, -0.003f), Quaternion.identity, "", 0.22f,
                UiStyle.InkFace, new Vector2(W - 0.02f, 0.022f), TextStyle.Ink, true);
            foreach (var t in new[] { hud.headerText, hud.rankText, hud.timeText, hud.progressText }) t.fontStyle = TMPro.FontStyles.Bold;

            // 持久力の印：紙の下の縁に宝石 5 つ（点いている = 金、消えている = 革の色、危ない = 赤）
            var row = GenUtil.Child(card.transform, "StaminaPips", new Vector3(-0.03f, -H * 0.5f - 0.02f, -0.004f)).transform;
            var pips = new Renderer[HudMath.STAMINA_PIPS];
            for (int k = 0; k < pips.Length; k++)
            {
                var gem = GenUtil.InstantiatePart(GenUtil.Pack("Gems"), "Gems_02_0" + (k + 2), row, row.position, 0f, 1f,
                    new Vector3(0.02f, 0.025f, 0.02f), PipGold);
                gem.name = "Pip_" + k;
                GenUtil.FitMax(gem, UiStyle.PIP_SIZE_M);
                gem.transform.localPosition = new Vector3((k - 2) * 0.03f, -0.0125f, 0f);
                pips[k] = gem.GetComponentInChildren<Renderer>(true);
                foreach (var c in gem.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
            hud.staminaPips = pips;
            hud.pipRow = row;
            hud.pipLit = GenUtil.ToonColor(PipGold);
            hud.pipUnlit = GenUtil.ToonColor(Leather);
            hud.pipDanger = GenUtil.ToonColor(PipRed);
            hud.pipFaint = GenUtil.ToonColor(new Color(Paper.r * 0.7f + PipGold.r * 0.3f, Paper.g * 0.7f + PipGold.g * 0.3f, Paper.b * 0.7f + PipGold.b * 0.3f));   // 満タン：列ごと 30% に控えめ（＋ 0.8 倍）
            hud.hudRoot = card;
            GenUtil.SetLayerRecursive(card, WorldLayers.Rider);   // 当たり判定なし・CP を踏まない
        }

        // ------------------------------------------------------------------
        // 柵の馬と厩舎
        // ------------------------------------------------------------------

        static void Corral(Transform root, WorldContext ctx)
        {
            // 西の空き地：7 頭を南北に並べ（東＝通りの側を向く）、西に柵とつなぐ杭
            Vector3 first = P(LobbyLayout.CORRAL_FIRST);
            float step = LobbyLayout.CORRAL_STEP;
            var corral = GenUtil.World(root, "Corral", first + new Vector3(0f, 0f, 3f * step)).transform;
            Fence.Line(corral, first + new Vector3(-3.5f, 0f, -1.5f), first + new Vector3(-3.5f, 0f, 6f * step + 0.8f));
            for (int coat = 0; coat < 7; coat++)
            {
                Vector3 p = first + new Vector3(0f, 0f, coat * step);
                var go = GenUtil.World(corral, coat == 6 ? "Picker_Skeleton" : "Picker_Coat" + coat, p, 90f);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(1.2f, 2.2f, 2.6f);
                col.center = new Vector3(0f, 1.1f, 0f);
                var vis = GenUtil.Child(go.transform, "Visual", Vector3.zero);
                var mv = MountBuilder.BuildVisual(vis, coat);
                // 選んだ印：赤く塗った紙の旗（手配書の小さい紙 WantedPosters_05 を引き伸ばし、表裏）
                var marker = GenUtil.Child(go.transform, "ChosenRibbon", new Vector3(0f, 2.2f, 0f));
                foreach (float ry in new[] { 0f, 180f })
                {
                    var ribbon = GenUtil.PaperSheet(marker.transform, ry == 0f ? "Front" : "Back", "WantedPosters_05", Vector3.zero, null, 0.6f, 0.22f);
                    ribbon.transform.localRotation = Quaternion.Euler(0f, ry, 0f);
                    foreach (var r in ribbon.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(new Color(0.8f, 0.1f, 0.1f));
                }
                marker.SetActive(false);
                var picker = GenUtil.AddU<MountPicker>(go);
                picker.coat = coat;
                picker.visual = mv;
                picker.chosenMarker = marker;
                GenUtil.SetInteractText(picker, coat == 6 ? "骨の馬にする" : "この毛色の馬にする");
                ctx.mountPickers.Add(picker);
                // つなぐ杭
                GenUtil.Stake(corral, "DesertProps_05", p + new Vector3(-2.6f, 0f, 0f), 90f, 0.45f).name = "HitchPost";
            }
            // 厩舎（背景専用）：町の奥の柵の北＝見えない壁の向こう。正面は南（町）を向く。念のため見えない壁でも囲む
            var stable = GenUtil.PlaceModel(GenUtil.Prop("Stable"), root, "Stable", P(LobbyLayout.STABLE), 180f, 1f, new Vector3(14f, 6f, 6f), Wood);
            DressingBuilder.AddCollider(stable, true);
            DressingBuilder.BackgroundWalls(stable, 4f);
        }

        // ------------------------------------------------------------------
        // たまり場
        // ------------------------------------------------------------------

        static void Hangout(Transform root, WorldContext ctx)
        {
            Vector3 c = P(LobbyLayout.HANGOUT_FIRE);
            var h = GenUtil.World(root, "Hangout", c).transform;
            var fire = GenUtil.InstantiatePart(GenUtil.Pack("Campfire"), "Campfire_01", h, c, 0f, 1f, new Vector3(1.5f, 0.6f, 1.5f), DarkWood);
            fire.name = "Campfire";
            GenUtil.Particles(fire.transform, "Flames", new Vector3(0f, 0.4f, 0f), new Color(1f, 0.55f, 0.15f, 0.9f), 0.35f, 1.2f, 0.8f, true, 25f, 0.3f);
            GenUtil.SmokeColumn(fire.transform, "Smoke", new Vector3(0f, 1f, 0f), SetPieceBuilder.SmokeGrey, 16f, 1.4f);
            GenUtil.PointLight(h, "FireLight", c + Vector3.up * 1.2f, new Color(1f, 0.6f, 0.3f), 10f, 1.4f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 p = c + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 3.2f);
                var bench = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", h, p, a + 90f, 1.4f, new Vector3(1.4f, 0.35f, 0.4f), Wood);
                bench.name = "Bench_" + i;
            }
            // 前回のレースの黒板（焚き火の西。焚き火の側から西を見て読む。文字は yaw の方向を見ている人に読める）
            ctx.rankBoards.Add(CourseBuilder.ChalkBoardAt(h, P(LobbyLayout.LAST_RACE_BOARD), -90f, "LastRaceBoard", true));
            // 掲示板 3 枚は焚き火の北に東西に並べて南（焚き火）を向ける。新聞売りは焚き火の東で西を向く
            ShowBuilder.BuildBoards(h, ctx, P(LobbyLayout.NOTICE_BOARDS), Vector3.back, P(LobbyLayout.TOWN_CRIER), -90f);
            // 薪の山（焚き火の南西）
            for (int i = 0; i < 6; i++)
            {
                var log = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_0" + (2 + i), h, c + new Vector3(-7f + (i % 3) * 0.9f, 0f, -5f) + Vector3.up * (i / 3) * 0.3f,
                    90f + i * 7f, 1.2f, new Vector3(0.3f, 0.3f, 0.8f), Wood);
                DressingBuilder.SetStatic(log);
                GenUtil.Tag(log, "Lobby/Woodpile/WoodLogs|WoodLogs_0" + (2 + i) + "#" + i);
            }
        }

        // ------------------------------------------------------------------
        // 練習場
        // ------------------------------------------------------------------

        static void Practice(Transform root, WorldContext ctx)
        {
            Vector3 c = CENTER + PRACTICE_OFFSET;
            Vector4 pr4 = LobbyLayout.PRACTICE_RECT;
            var pr = GenUtil.World(root, "PracticeArea", c).transform;
            var trig = pr.gameObject.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            // 練習場＝町の東、線路の東の空き地（LobbyLayout.PRACTICE_RECT、17m × 53m）全体を覆うトリガー
            trig.size = new Vector3(pr4.z - pr4.x, 24f, pr4.w - pr4.y);
            trig.center = new Vector3(0f, 10f, 0f);
            pr.gameObject.layer = WorldLayers.TRIGGERS;
            var area = GenUtil.AddU<PracticeArea>(pr.gameObject);
            area.releaseRadius = LobbyLayout.PracticeReleaseRadius;   // トリガーの箱の角より少し外
            ctx.practiceArea = area;

            var feats = GenUtil.Child(pr, "Features", Vector3.zero).transform;
            Vector3 Loc(Vector3 rel) => rel - PRACTICE_OFFSET;
            // ジャンプ台（15°、6m、北へ上る。着地は給水塔の手前まで 13m）
            // 当たり判定は見えない板。見た目は板の面に丸太の束（WoodLogs_01 ×2）を 2 列 × 3 段に敷き、縁に丸太
            var ramp = Solid(feats, "JumpRamp", Loc(LobbyLayout.PRACTICE_JUMP) + new Vector3(0f, 0.6f, 0f), new Vector3(6f, 0.5f, 6.2f), Quaternion.Euler(-15f, 0f, 0f));
            GenUtil.HideRenderers(ramp);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                {
                    var log = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", ramp.transform, ramp.transform.position, 0f, 2f, new Vector3(1.5f, 0.3f, 1f), Wood);
                    log.transform.SetParent(ramp.transform, false);
                    log.transform.localRotation = Quaternion.identity;
                    // 丸太の束の原点は底：上面（高さ 0.6m）が板の上面（中心 +0.25m）にそろうよう、底を中心 −0.35m（ローカル −0.7）に
                    log.transform.localPosition = new Vector3((j - 0.5f) * 3f / 6f, -0.7f, (i - 1) * 2f / 6.2f);
                    log.transform.localScale = new Vector3(2f / 6f, 2f / 0.5f, 2f / 6.2f);
                }
            // 小さな丘（地形の盛り上がり、上面 0.45m）：北の端。当たり判定は見えない低い台、縁に丸太
            var bump = Solid(feats, "Bump", Loc(LobbyLayout.PRACTICE_BUMP) + new Vector3(0f, -0.75f, 0f), new Vector3(7f, 2.4f, 7f), Quaternion.Euler(0f, 45f, 0f));
            GenUtil.HideRenderers(bump);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                Vector3 lp = P(LobbyLayout.PRACTICE_BUMP) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 5.2f);
                var edge = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_0" + (2 + i % 3), feats, lp, a + 90f, 1.4f, new Vector3(1f, 0.3f, 0.8f), Wood);
                DressingBuilder.SetStatic(edge);
            }
            // 坂と崖下り（8m × 26m）は町の中の空き地に入らないので無し
            // 目印の干し草：東の端（町の端の手前）と北の端
            float[] hayZ = { 91f, 97f, 103f, 114f, 120f, 126f, 135f };
            for (int i = 0; i < hayZ.Length + 3; i++)
            {
                Vector3 rel = i < hayZ.Length ? new Vector3(34.8f, 0f, hayZ[i]) : new Vector3(21f + (i - hayZ.Length) * 6f, 0f, 138.5f);
                var hay = GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), i % 3 == 0 ? "HayBales_01" : "HayBales_0" + (2 + i % 5), feats, P(rel),
                    i < hayZ.Length ? 90f : 0f, 1f, new Vector3(1f, 0.6f, 0.7f), new Color(0.85f, 0.75f, 0.4f));
                DressingBuilder.AddCollider(hay, false);
            }
            // カカシ
            Vector3[] dummies = { LobbyLayout.PRACTICE_DUMMY_1, LobbyLayout.PRACTICE_DUMMY_2 };
            var dummies2 = new List<PracticeDummy>();
            for (int i = 0; i < 2; i++)
            {
                var d = GenUtil.World(pr, "Scarecrow_" + (i + 1), P(dummies[i]), 180f);
                // 的（art-r1-12）：干し草（HayBales_01）を 2 段に積み、手配書の紙を的として貼る。Pivot ごと揺れる
                var pivot = GenUtil.Child(d.transform, "Pivot", Vector3.zero).transform;
                GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", pivot, pivot.position, 180f, 0.7f, new Vector3(1.5f, 1.6f, 1.5f), Wood).name = "HayLow";
                GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", pivot, pivot.position + Vector3.up * 1.1f, 220f, 0.6f, new Vector3(1.5f, 1.6f, 1.5f), Wood).name = "HayHigh";
                var target = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/Wanted_Original.png");
                GenUtil.PaperSheet(pivot, "Target", "WantedPosters_01", new Vector3(0f, 0.9f, -0.56f), target, 0.6f, 0.9f);
                var cap = d.AddComponent<CapsuleCollider>();
                cap.radius = 0.45f;
                cap.height = 2.2f;
                cap.center = new Vector3(0f, 1.1f, 0f);
                d.layer = WorldLayers.Rider;
                var dummy = GenUtil.AddU<PracticeDummy>(d);
                dummy.pivot = pivot;
                dummy.hitSound = GenUtil.Audio(d, false);
                if (i == 1) dummy.moveAmplitude = 2.5f;   // 北のカカシは左右に動く的（system-r1-18）
                ctx.dummies.Add(dummy);
                dummies2.Add(dummy);
            }
            PracticeLapBoard(pr, ctx, dummies2);
            // 練習用の馬の杭（馬は SceneAssembler が作って mount をつなぐ）：南の端＝広場から線路沿いに北へ来て最初に着く所
            for (int i = 0; i < 2; i++)
            {
                Vector3 pp = P(LobbyLayout.PRACTICE_POST_FIRST) + new Vector3(0f, 0f, i * 4.5f);
                var post = GenUtil.World(pr, "PracticePost_" + (i + 1), pp, 90f);
                GenUtil.Stake(post.transform, "DesertProps_05", pp, 90f, 0.45f).name = "Post";
                var col = post.AddComponent<BoxCollider>();
                col.size = new Vector3(0.5f, 1.4f, 0.5f);
                col.center = new Vector3(0f, 0.7f, 0f);
                var home = GenUtil.World(pr, "PracticeHome_" + (i + 1), pp + new Vector3(2.5f, 0.05f, 0f), 0f).transform;
                var hp = GenUtil.AddU<PracticeHorsePost>(post);
                hp.home = home;
                hp.coat = i;
                GenUtil.SetInteractText(hp, "練習用の馬に乗る");
                ctx.practicePosts.Add(hp);
                ctx.practiceHomes.Add(home);
            }
            // 看板：杭の南。南（広場から来る側）から読める
            // 掲示板の模型（NoticeBoard ×0.6）の板に書く（ui-r1-12、宙に浮かせない）
            NoticeSign(pr, "Sign", P(LobbyLayout.PRACTICE_POST_FIRST) + new Vector3(1.5f, 0f, -2.5f), 0f, 0.6f,
                "<b><size=140%>練習場</size></b>\n杭に触れると練習用の馬に乗れます", 1.4f, UiStyle.InkFace, TextStyle.Ink, Paper);
            // 練習場の遺物（拾われて 10 秒で戻る）
            var relic = CourseBuilder.Relic(pr, "PracticeRelic", P(LobbyLayout.PRACTICE_RELIC), 3, false);
            relic.practiceRespawnSeconds = 10f;
            ctx.practiceRelics.Add(relic);
        }

        /// <summary>
        /// 練習場の往復タイム（system-r1-18）：南と北に見えない門（Is Trigger の箱 + Checkpoint stageIndex -1）、
        /// 掲示板の模型（NoticeBoard ×0.6）の板に上位 5 人・自分の記録・的に当てた数（PracticeLap）。
        /// </summary>
        static void PracticeLapBoard(Transform pr, WorldContext ctx, List<PracticeDummy> dummies)
        {
            Vector4 r = LobbyLayout.PRACTICE_RECT;
            float w = r.z - r.x;
            var go = GenUtil.World(pr, "PracticeLap", P(LobbyLayout.PRACTICE_LAP_BOARD));
            var text = NoticeSign(go.transform, "Board", P(LobbyLayout.PRACTICE_LAP_BOARD), 0f, 0.6f, "往復タイム", 1.1f, UiStyle.InkFace, TextStyle.Ink, Paper);
            var lap = GenUtil.AddU<PracticeLap>(go);
            lap.boardText = text;
            lap.lapSound = GenUtil.Audio(go, false);
            float[] gateZ = { LobbyLayout.PRACTICE_GATE_SOUTH_Z, LobbyLayout.PRACTICE_GATE_NORTH_Z };
            for (int i = 0; i < 2; i++)
            {
                var g = GenUtil.World(pr, i == 0 ? "LapGate_South" : "LapGate_North", P(new Vector3((r.x + r.z) * 0.5f, 0f, gateZ[i])));
                var box = g.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(w, 5f, 2f);
                box.center = new Vector3(0f, 2f, 0f);
                g.layer = WorldLayers.TRIGGERS;
                var cp = GenUtil.AddU<Checkpoint>(g);
                cp.index = i;
                cp.stageIndex = -1;
                cp.practiceLap = lap;
            }
            foreach (var d in dummies) d.scoreBoard = lap;
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
            // 西の空き地の西の端（町の端の手前）：南北に 8 か所、東を向く
            Vector3 first = P(LobbyLayout.PARKING_FIRST);
            var p = GenUtil.World(root, "MountParking", first + new Vector3(0f, 0f, 3.5f * LobbyLayout.PARKING_STEP)).transform;
            for (int i = 0; i < 8; i++)
                ctx.parking.Add(GenUtil.World(p, "Parking_" + i, first + new Vector3(0f, 0.05f, i * LobbyLayout.PARKING_STEP), 90f).transform);
        }

        /// <summary>予備の遺物の置き場（普段は出ていない）：町の西の空き地の北（風車の西）。</summary>
        public static readonly Vector3 SPARE_POOL = LobbyLayout.SPARE_POOL;

        static void SpareRelics(Transform root, WorldContext ctx)
        {
            var p = GenUtil.World(root, "SpareRelicPool", CENTER + SPARE_POOL).transform;
            for (int i = 0; i < 4; i++)
                ctx.spareRelics.Add(CourseBuilder.Relic(p, "SpareRelic_" + (i + 1), CENTER + SPARE_POOL + new Vector3(0f, 0f, -6f + i * 4f), i, true));
        }

        /// <summary>
        /// 懐中時計（ui-r1-02）：プリミティブを使わず、手配書の紙（WantedPosters_05、0.13m 四方）の文字盤に TMP の数字 12 個（0.18pt、半径 0.05m）と
        /// 字形「│」の針 2 本（分針 = インク、秒針 = 赤）、真ん中に「残り 3:12」（0.35pt、自動で縮む）。文字は -Z 側。
        /// </summary>
        static void Watch(Transform root, WorldContext ctx)
        {
            var go = GenUtil.World(root, "PocketWatch", P(LobbyLayout.SPAWN) + new Vector3(0f, 1.2f, 3f));   // 実行中は手元へ動く
            var w = GenUtil.Child(go.transform, "Watch", Vector3.zero).transform;
            const float D = 0.13f, R = 0.05f;
            var dial = GenUtil.PaperSheet(w, "Dial", "WantedPosters_05", new Vector3(0f, -D * 0.5f, 0.001f), null, D, D);
            foreach (var c in dial.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            for (int k = 1; k <= 12; k++)
            {
                Vector2 dp = UiStyle.DialPoint(k, R);
                GenUtil.Text(w, "Numeral_" + k, new Vector3(dp.x, dp.y, -0.002f), Quaternion.identity, k.ToString(), 0.18f, UiStyle.InkFace,
                    new Vector2(0.03f, 0.02f), TextStyle.Ink, false);
            }
            var face = GenUtil.Text(w, "FaceText", new Vector3(0f, -0.018f, -0.003f), Quaternion.identity, "", 0.35f, UiStyle.InkFace,
                new Vector2(0.06f, 0.035f), TextStyle.Ink, true);
            face.fontStyle = TMPro.FontStyles.Bold;
            var min = GenUtil.Child(w, "MinuteHand", new Vector3(0f, 0f, -0.004f)).transform;
            GenUtil.Text(min, "Glyph", new Vector3(0f, 0.017f, 0f), Quaternion.identity, "│", 0.5f, UiStyle.InkFace, new Vector2(0.02f, 0.05f), TextStyle.Ink, false);
            var sec = GenUtil.Child(w, "SecondHand", new Vector3(0f, 0f, -0.005f)).transform;
            GenUtil.Text(sec, "Glyph", new Vector3(0f, 0.022f, 0f), Quaternion.identity, "│", 0.6f, UiStyle.RedInk, new Vector2(0.02f, 0.06f), TextStyle.Plain, false);
            var pw = GenUtil.AddU<PocketWatch>(go);
            pw.watchRoot = w.gameObject;
            pw.faceText = face;
            pw.minuteHand = min;
            pw.secondHand = sec;
            pw.tickAudio = GenUtil.Audio(go, true);
            GenUtil.SetLayerRecursive(go, WorldLayers.Rider);
            ctx.watch = pw;
        }
    }
}
