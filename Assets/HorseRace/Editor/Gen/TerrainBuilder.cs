using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの Terrain（1000m 四方、高さ 200m、解像度 513）を作る。
    /// 外周 55m は HeightmapGen.ApplyRim で 25〜60m 盛り上げた尾根（端が見えない）。その内側 40m に見えない壁（BoundaryWalls）。
    /// 道（course-r1-09）：HeightmapGen.TrailMask の踏み固めた土を ToonTerrain の _TrailTex で描く（TerrainLayer は 4 枚のまま）。道の上の砂は消す。
    /// TerrainLayer: 0 = 土（ステージの主色：ArtPalette.Dirt）、1 = 岩（急な斜面）、2 = 砂（HeightmapGen.SandMask。CANYON は高さを渡して谷底だけ）、
    ///   3 = 地層の縞（荒野の 30° 以上の斜面。ToonTerrain シェーダーがワールドの高さで横縞にする）。
    /// 地形のマテリアルは HorseRace/ToonTerrain（飾りと同じ 2 段のトゥーン・影色：地形の影と飾りの影が同じ色）。
    /// テクスチャは 45m ごとの大きなまだら（細かい点の繰り返しを見せない）。
    /// 実行時（Udon）からは Terrain の塗りを読めないので、砂の範囲には SandZone のトリガー箱も並べる（25m のマス目を x 方向に連結）。
    /// TerrainData・TerrainLayer・テクスチャは Assets/HorseRace/Generated/Terrain に上書き保存する。
    /// </summary>
    public static class TerrainBuilder
    {
        public const int LAYER_DIRT = 0, LAYER_ROCK = 1, LAYER_SAND = 2;
        public const int SAND_CELLS = 40;              // 1000m / 40 = 25m のマス
        public const float SAND_THRESHOLD = 0.5f;
        public const float SAND_ROAD_SKIP_M = 12f;      // MountController は terrainSandWeight > 0.5 を砂地とみなす
        public const int LAYER_STRATA = 3;
        const float ROCK_SLOPE_FROM = 28f, ROCK_SLOPE_TO = 40f;
        const float STRATA_SLOPE_FROM = 30f, STRATA_SLOPE_TO = 40f;
        public const float MESA_STRATA_FROM = 22f, MESA_STRATA_BLEND = 8f;   // art-r3-07：境目の地層の縞は 22° から
        public const float FILLER_STRATA_FROM = 18f;                          // art-r3-10：外の地面の斜面も地層の縞
        public const float LAYER_TILE_M = 45f;          // 12m だと斜面に細かい点の繰り返しが見えた（art-r1-14）
        public const float STRATA_BAND_M = 8f;
        /// <summary>art-r4-04：境目の縞の帯の高さ（ToonTerrain の _StrataScale）。</summary>
        public const float STRATA_BAND_BORDER_M = 4f;
        /// <summary>art-r4-05 → art-r5-01：道のマスクのテクスチャの解像度。ステージは 4096（1 texel ≈ 0.24m、轍 0.6m がなめらか）、ロビーは 2048。</summary>
        public const int TRAIL_TEX_RES = 4096, LOBBY_TRAIL_TEX_RES = 2048;
        /// <summary>art-r4-04：法線の y がこれより小さい（約 53° より急な）面だけ縞を濃く（ToonTerrain の _StrataSlope）。</summary>
        public const float STRATA_SLOPE_NY = 0.6f;
        public const string TERRAIN_SHADER = "HorseRace/ToonTerrain";
        public const string TERRAIN_MAT = GenUtil.GEN_MATERIALS + "/M_ToonTerrain.mat";
        /// <summary>地形のバリエーション：0〜2 = ステージ、LOBBY = ロビーと外の地面。</summary>
        public const int VARIANT_LOBBY = 3;
        /// <summary>外の地面（art-r3-10）：3 ステージの土を混ぜた色（ArtPalette.DirtOuter）。</summary>
        public const int VARIANT_OUTER = 4;

        public static StageBuild Build(StageDef def, Transform parent)
        {
            var sb = new StageBuild { def = def };
            sb.root = GenUtil.World(parent, "Stage" + (def.index + 1) + "_" + def.label, def.center).transform;

            sb.courseLocal = StageDefinitions.SampleLocal(def);
            sb.goalArc = StageDefinitions.GoalArc(def, sb.courseLocal);
            Vector3[] norm = StageDefinitions.Normalize(sb.courseLocal);
            sb.heights = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, def, norm);
            // 外周を尾根にして地形の端を見せない（コースの近くは触らない）
            HeightmapGen.ApplyRim(sb.heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, HeightmapGen.RIM_WIDTH_M,
                HeightmapGen.RIM_MIN_M, HeightmapGen.RIM_MAX_M, def.seed, norm);
            sb.sand = HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES, def.kind, def.seed,
                def.kind != HeightmapGen.KIND_DUNES ? sb.heights : null);   // 荒野は谷底だけ、境目はくぼ地（art-r3-07）
            // 道の踏み固めた土（course-r1-09）：道の上の砂は消す（道の外の砂 = 遅くなる危険）
            float[,] trailDist, trailHw, trailSigned;
            sb.trail = HeightmapGen.TrailMask(StageDefinitions.ALPHAMAP_RES, def, norm, out trailDist, out trailHw, out trailSigned);
            HeightmapGen.ClearSandOnTrail(sb.sand, sb.trail, def.kind, trailDist, trailHw);   // art-r4-05：砂漠は道の縁の外を砂のまま
            // 深い砂の区間（system-r2-01）：道の上も砂（ラクダの出番）。踏み跡は薄く残す
            HeightmapGen.ApplyDeepSand(sb.sand, sb.trail, def, sb.courseLocal, StageDefinitions.SIZE);

            sb.courseWorld = new Vector3[sb.courseLocal.Length];
            for (int i = 0; i < sb.courseLocal.Length; i++) sb.courseWorld[i] = sb.Ground(sb.courseLocal[i].x, sb.courseLocal[i].z);

            TerrainData td = CreateTerrainData("Stage" + (def.index + 1), sb.heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M);
            PaintLayers(td, sb.heights, sb.sand, def.index);
            GameObject tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";
            tgo.transform.SetParent(sb.root, false);
            tgo.transform.position = def.Origin;
            GenUtil.SetLayerRecursive(tgo, WorldLayers.Ground);
            sb.terrain = tgo.GetComponent<Terrain>();
            sb.terrain.heightmapPixelError = 4f;
            // course-r3-14：スタート・ゴールの線（道を横切る明るい土の帯、地形の塗りだけ）を道のマスクの B へ
            float[,] lines = HeightmapGen.LineMask(StageDefinitions.ALPHAMAP_RES, def, sb.courseLocal, sb.goalArc);
            ApplyToonMaterial(sb.terrain, TrailTexture("Stage" + (def.index + 1), sb.trail, trailSigned, lines, TRAIL_TEX_RES), "Stage" + (def.index + 1), def.index);

            BuildSandZones(sb);
            BoundaryWalls(sb.root, def.Origin, StageDefinitions.SIZE, WALL_INSET_M, "StageBoundary");
            return sb;
        }

        public const float WALL_INSET_M = 40f;     // 見えない壁：外周から 40m 内側（尾根の斜面の中。コースは 60m 以上内側）
        public const float WALL_HEIGHT_M = 320f;

        /// <summary>正方形 [origin, origin+size] の各辺から inset m 内側に、見えない壁（Block、描画なし）を 4 枚立てる。</summary>
        public static GameObject BoundaryWalls(Transform parent, Vector3 origin, float size, float inset, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(origin.x + size * 0.5f, origin.y + WALL_HEIGHT_M * 0.5f - 20f, origin.z + size * 0.5f);
            float s = size - inset * 2f;
            const float T = 2f;
            Wall(go.transform, "N", new Vector3(0f, 0f, s * 0.5f), new Vector3(s + T, WALL_HEIGHT_M, T));
            Wall(go.transform, "S", new Vector3(0f, 0f, -s * 0.5f), new Vector3(s + T, WALL_HEIGHT_M, T));
            Wall(go.transform, "E", new Vector3(s * 0.5f, 0f, 0f), new Vector3(T, WALL_HEIGHT_M, s + T));
            Wall(go.transform, "W", new Vector3(-s * 0.5f, 0f, 0f), new Vector3(T, WALL_HEIGHT_M, s + T));
            GenUtil.SetLayerRecursive(go, WorldLayers.Block);
            return go;
        }

        static void Wall(Transform parent, string name, Vector3 local, Vector3 size)
        {
            var w = GenUtil.Child(parent, name, local);
            var b = w.AddComponent<BoxCollider>();
            b.size = size;
        }

        /// <summary>TerrainData を作って Generated/Terrain/&lt;name&gt;.asset に上書き保存する。</summary>
        public static TerrainData CreateTerrainData(string name, float[,] heights, float sizeM, float heightM)
            => CreateTerrainData(name, heights, sizeM, sizeM, heightM);

        /// <summary>長方形の Terrain（x = sizeXM、z = sizeZM）。高さマップは正方形の格子のまま、セルが長方形になる。</summary>
        public static TerrainData CreateTerrainData(string name, float[,] heights, float sizeXM, float sizeZM, float heightM)
        {
            GenUtil.EnsureFolder(GenUtil.GEN_TERRAIN);
            string path = GenUtil.GEN_TERRAIN + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null) AssetDatabase.DeleteAsset(path);
            var td = new TerrainData();
            td.heightmapResolution = heights.GetLength(0);
            td.size = new Vector3(sizeXM, heightM, sizeZM);
            td.SetHeights(0, 0, heights);
            AssetDatabase.CreateAsset(td, path);
            return td;
        }

        /// <summary>地形のレイヤー 4 枚（土はバリエーションごとの色、岩・砂・地層は共通）。</summary>
        public static TerrainLayer[] Layers(int variant = VARIANT_LOBBY)
        {
            string dirtName = variant >= 0 && variant < VARIANT_LOBBY ? "TL_Dirt_S" + (variant + 1) : (variant == VARIANT_OUTER ? "TL_Dirt_Outer" : "TL_Dirt_Lobby");
            Color dirt = variant == VARIANT_OUTER ? ArtPalette.DirtOuter : ArtPalette.Dirt(variant);
            return new[]
            {
                Layer(dirtName, NoiseTexture(dirt, 0.05f, 11 + variant), new Vector2(LAYER_TILE_M, LAYER_TILE_M)),
                Layer("TL_Rock", NoiseTexture(ArtPalette.RockLayer, 0.08f, 12), new Vector2(LAYER_TILE_M, LAYER_TILE_M)),
                // art-r4-06：砂はステージの主色（荒野 = 赤錆の砂、境目 = ばら色）。ロビー・外の地面は荒野の砂
                Layer(variant == 1 ? "TL_Sand" : (variant == 2 ? "TL_Sand_S3" : "TL_Sand_Rust"), NoiseTexture(ArtPalette.Sand(variant), 0.03f, 13), new Vector2(LAYER_TILE_M, LAYER_TILE_M)),
                // art-r4-04：境目の縞は明暗を強く、帯の高さ 4m（切り通しの壁の段）
                variant == 2 ? Layer("TL_Strata_S3", StrataTexture(2), new Vector2(LAYER_TILE_M, STRATA_BAND_BORDER_M))
                             : Layer("TL_Strata", StrataTexture(0), new Vector2(LAYER_TILE_M, STRATA_BAND_M)),
            };
        }

        /// <summary>TerrainLayer とそのテクスチャを Generated/Terrain に上書き保存する（毎回テクスチャを作り直す：前の細かい点の模様を残さない）。</summary>
        static readonly System.Collections.Generic.Dictionary<string, TerrainLayer> layerCache = new System.Collections.Generic.Dictionary<string, TerrainLayer>();

        /// <summary>Build World の最初に呼ぶ（このビルドではレイヤーを 1 回だけ作り直す）。</summary>
        public static void ResetCache() => layerCache.Clear();

        static TerrainLayer Layer(string name, Texture2D fresh, Vector2 tile)
        {
            TerrainLayer cached;
            if (layerCache.TryGetValue(name, out cached) && cached != null) return cached;
            GenUtil.EnsureFolder(GenUtil.GEN_TERRAIN);
            string texPath = GenUtil.GEN_TERRAIN + "/" + name + "_Tex.asset";
            string layerPath = GenUtil.GEN_TERRAIN + "/" + name + ".terrainlayer";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) != null) AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(fresh, texPath);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = fresh;
            layer.tileSize = tile;
            EditorUtility.SetDirty(layer);
            layerCache[name] = layer;
            return layer;
        }

        /// <summary>トゥーン調の地面テクスチャ：ほぼ単色に、大きくてやわらかい斑（段の境目を作らない）を薄く乗せる。</summary>
        static Texture2D NoiseTexture(Color c, float amount, int seed)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color[N * N];
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // 周期 64 でつながるよう、円周上のノイズ（タイルの継ぎ目を出さない）
                    float ax = x / (float)N * Mathf.PI * 2f, ay = y / (float)N * Mathf.PI * 2f;
                    float n = Mathf.PerlinNoise(ox + Mathf.Cos(ax) * 1.6f, oy + Mathf.Sin(ax) * 1.6f) * 0.5f
                            + Mathf.PerlinNoise(ox + 40f + Mathf.Cos(ay) * 1.6f, oy + 40f + Mathf.Sin(ay) * 1.6f) * 0.5f;
                    float k = 1f + (n - 0.5f) * amount * 2f;
                    px[y * N + x] = new Color(c.r * k, c.g * k, c.b * k, 1f);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        /// <summary>地層の縞のテクスチャ：縦（V）に明るい帯と暗い帯が不規則な幅で並ぶ 2 色（メサの岩の縞と同じ見え方）。</summary>
        static Texture2D StrataTexture(int stage)
        {
            const int W = 16, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var px = new Color[W * H];
            int[] bands = { 9, 4, 7, 3, 12, 5, 8, 6, 10 };   // 合計 64
            int y0 = 0;
            for (int b = 0; b < bands.Length; b++)
            {
                Color c = b % 2 == 0 ? ArtPalette.StrataLightFor(stage) : ArtPalette.StrataDarkFor(stage);
                for (int y = y0; y < Mathf.Min(H, y0 + bands[b]); y++)
                    for (int x = 0; x < W; x++) px[y * W + x] = c;
                y0 += bands[b];
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        /// <summary>道の色はステージごと（art-r2-02：ArtPalette.Trail）。</summary>
        public static Color TrailColor(int stage) => ArtPalette.Trail(stage);

        /// <summary>
        /// 道の踏み固めた土の重み（R、Terrain 全体の UV）と道の中心からの符号付きの横の距離（art-r5-01：G = 0.5 + d / (2·TRAIL_DIST_MAX_M)、
        /// ToonTerrain が ±1.8m の 2 本の轍を描く）を Generated/Terrain/&lt;name&gt;_Trail.asset に保存する。
        /// 符号付きの距離は中心の線をまっすぐ通るので、双線形で補間しても途切れない（前の符号なしの距離は点線・しみになった）。
        /// </summary>
        public static Texture2D TrailTexture(string name, float[,] trail, float[,] signedDist = null, float[,] lines = null, int res = TRAIL_TEX_RES)
        {
            int n = trail.GetLength(0), m = trail.GetLength(1);
            // art-r4-05：双線形で引き伸ばす（512 の 1 マス 2m の段が道の縁ののこぎりの歯に見えた）。art-r5-01：ステージは 4096
            int R = res;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
            var px = new Color[R * R];
            for (int z = 0; z < R; z++)
                for (int x = 0; x < R; x++)
                {
                    float u = x / (float)(R - 1) * (m - 1), w = z / (float)(R - 1) * (n - 1);
                    float v = Bilinear(trail, u, w);
                    float g = signedDist != null ? HeightmapGen.EncodeSignedDist(HeightmapGen.SampleBilinear(signedDist, u, w)) : 1f;
                    float l = lines != null ? Bilinear(lines, u, w) : 0f;   // B = スタート・ゴールの線（course-r3-14）
                    px[z * R + x] = new Color(v, g, l, 1f);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            GenUtil.EnsureFolder(GenUtil.GEN_TERRAIN);
            string path = GenUtil.GEN_TERRAIN + "/" + name + "_Trail.asset";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        /// <summary>格子 a（[z, x]）の (x, z) の双線形補間。cap ≥ 0 なら値をその上限で切ってから混ぜる（範囲の外の float.MaxValue を混ぜない）。</summary>
        static float Bilinear(float[,] a, float x, float z, float cap = -1f)
        {
            int n = a.GetLength(0), m = a.GetLength(1);
            int x0 = Mathf.Clamp((int)x, 0, m - 2), z0 = Mathf.Clamp((int)z, 0, n - 2);
            float tx = Mathf.Clamp01(x - x0), tz = Mathf.Clamp01(z - z0);
            float p = a[z0, x0], q = a[z0, x0 + 1], r = a[z0 + 1, x0], s = a[z0 + 1, x0 + 1];
            if (cap >= 0f) { p = Mathf.Min(p, cap); q = Mathf.Min(q, cap); r = Mathf.Min(r, cap); s = Mathf.Min(s, cap); }
            float top = p + (q - p) * tx, bot = r + (s - r) * tx;
            return top + (bot - top) * tz;
        }

        /// <summary>
        /// 長方形の Terrain（ワールドの原点 origin、大きさ sizeX × sizeZ）に、ワールドの折れ線 path（半幅 halfWidth）の道のマスク（R = 重み、G = 中心からの距離）を
        /// res × res で作る（art-r4-07/13：ロビーの大通りと町へ近づく道の轍。ステージの道と同じ ToonTerrain の轍）。
        /// </summary>
        public static Texture2D PathTrailTexture(string name, Vector3 origin, float sizeX, float sizeZ, int res, Vector3[] path, float halfWidth)
        {
            var trail = new float[res, res];
            var dist = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var p = new Vector3(origin.x + x / (float)(res - 1) * sizeX, 0f, origin.z + z / (float)(res - 1) * sizeZ);
                    float sd = WorldLayout.SignedDistToPolylineXZ(p, path);   // art-r5-01：符号付き
                    dist[z, x] = sd;
                    trail[z, x] = HeightmapGen.TrailWeight(Mathf.Abs(sd), halfWidth, HeightmapGen.TRAIL_EDGE_DUNES_M);
                }
            return TrailTexture(name, trail, dist, null, Mathf.Max(res, LOBBY_TRAIL_TEX_RES));
        }

        /// <summary>ステージの地形のマテリアル（M_ToonTerrain_&lt;name&gt;.mat）：共通の設定＋道の踏み固めた土（_TrailTex）。</summary>
        public static void ApplyToonMaterial(Terrain t, Texture2D trail, string name, int stage)
        {
            ApplyToonMaterial(t);
            var baseMat = t.materialTemplate;
            if (baseMat == null || trail == null) return;
            string path = GenUtil.GEN_MATERIALS + "/M_ToonTerrain_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(baseMat); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(baseMat);
            m.shader = baseMat.shader;
            m.SetTexture("_TrailTex", trail);
            m.SetColor("_TrailColor", TrailColor(stage));
            m.SetFloat("_RutOffset", HeightmapGen.RUT_OFFSET_M / HeightmapGen.TRAIL_DIST_MAX_M);
            m.SetFloat("_RutWidth", HeightmapGen.RUT_WIDTH_M / HeightmapGen.TRAIL_DIST_MAX_M);
            m.SetFloat("_RutDarken", 1f - ArtPalette.RUT_SHADE);
            if (stage == 2) m.SetFloat("_StrataScale", STRATA_BAND_BORDER_M);   // art-r4-04
            m.SetFloat("_StrataSlope", stage == 1 ? 0f : STRATA_SLOPE_NY);       // art-r4-04：急な面（n.y < 0.6）は地層の縞を濃く（砂丘の崩れ面は除く）
            // art-r4-12：岩の橋の背は線の明るさ（道の色 × _LineBoost）で溝の底より明るく（SetPieceBuilder が B に塗る）
            EditorUtility.SetDirty(m);
            t.materialTemplate = m;
        }

        /// <summary>地形のトゥーンのマテリアル（Generated/Materials/M_ToonTerrain.mat）を作り、Terrain に付ける。見つからなければ既定の地形のまま。</summary>
        public static void ApplyToonMaterial(Terrain t)
        {
            var sh = Shader.Find(TERRAIN_SHADER);
            t.basemapDistance = 20000f;   // 遠くの低解像度の地形（ベースマップ）は使わない（トゥーンにならないため）
            if (sh == null)
            {
                GenUtil.Warn("シェーダー " + TERRAIN_SHADER + " が見つかりません（地形は既定のライティング）");
                t.drawInstanced = true;
                return;
            }
            GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
            var m = AssetDatabase.LoadAssetAtPath<Material>(TERRAIN_MAT);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, TERRAIN_MAT); }
            m.shader = sh;
            m.SetColor("_ShadowColor", LightingPlan.ShadowColor);
            m.SetFloat("_ShadowLightFloor", LightingPlan.SHADOW_LIGHT_FLOOR);
            m.SetFloat("_StrataScale", STRATA_BAND_M);
            m.SetFloat("_StrataTile", LAYER_TILE_M);
            // art-r4-02：道の外の粒（3.5m）とまだら（45m）、道の上の蹄の擦れ（1.2m）
            m.SetFloat("_GrainScale", ArtPalette.GRAIN_SCALE_M);
            m.SetFloat("_GrainAmp", ArtPalette.GRAIN_AMP);
            m.SetFloat("_MottleScale", ArtPalette.MOTTLE_SCALE_M);
            m.SetFloat("_MottleAmp", ArtPalette.MOTTLE_AMP);
            m.SetFloat("_ScuffScale", ArtPalette.SCUFF_SCALE_M);
            m.SetFloat("_ScuffAmp", ArtPalette.SCUFF_AMP);
            m.SetFloat("_StrataSlope", 0f);   // 縞の傾きの強調は荒野・境目のマテリアルだけ（下の ApplyToonMaterial(t, trail, …)）
            EditorUtility.SetDirty(m);
            t.materialTemplate = m;
            t.drawInstanced = false;      // 自前の地形シェーダーはインスタンス描画に対応していない
        }

        /// <summary>斜面の角度（°）。高さマップ [z,x]（0〜1 × heightM）、セル cellX × cellZ m。</summary>
        public static float SlopeDeg(float[,] heights, int hz, int hx, float heightM, float cellX, float cellZ)
        {
            int n = heights.GetLength(0);
            hz = Mathf.Clamp(hz, 1, n - 2); hx = Mathf.Clamp(hx, 1, heights.GetLength(1) - 2);
            float gx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * heightM / (2f * cellX);
            float gz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * heightM / (2f * cellZ);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        public static void PaintLayers(TerrainData td, float[,] heights, float[,] sand, int variant)
        {
            td.terrainLayers = Layers(variant);
            int res = StageDefinitions.ALPHAMAP_RES;
            td.alphamapResolution = res;
            td.baseMapResolution = 512;
            int hn = heights.GetLength(0);
            float cellM = td.size.x / (hn - 1);
            bool strata = variant == HeightmapGen.KIND_CANYON;
            bool mesa = variant == HeightmapGen.KIND_MESA;   // art-r3-07/08：境目は 22° 以上の斜面（切り通しの壁を含む）を地層の縞に
            var a = new float[res, res, 4];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int hz = Mathf.Clamp(Mathf.FloorToInt(z / (float)(res - 1) * (hn - 1) + 0.5f), 1, hn - 2);
                    int hx = Mathf.Clamp(Mathf.FloorToInt(x / (float)(res - 1) * (hn - 1) + 0.5f), 1, hn - 2);
                    float gx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * td.size.y / (2f * cellM);
                    float gz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * td.size.y / (2f * cellM);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    float s = sand != null ? Mathf.Clamp01(sand[Mathf.Min(z, sand.GetLength(0) - 1), Mathf.Min(x, sand.GetLength(1) - 1)]) : 0f;
                    float rock = Mathf.Clamp01((slope - ROCK_SLOPE_FROM) / (ROCK_SLOPE_TO - ROCK_SLOPE_FROM)) * (1f - s);
                    // 荒野の急な壁は地層の縞（岩の代わりに、30〜40° で移る）
                    float st = strata ? Mathf.Clamp01((slope - STRATA_SLOPE_FROM) / (STRATA_SLOPE_TO - STRATA_SLOPE_FROM)) * (1f - s)
                             : (mesa ? Mathf.Clamp01((slope - MESA_STRATA_FROM) / MESA_STRATA_BLEND) * (1f - s) : 0f);
                    rock *= 1f - st;
                    a[z, x, LAYER_SAND] = s;
                    a[z, x, LAYER_ROCK] = rock;
                    a[z, x, LAYER_STRATA] = st;
                    a[z, x, LAYER_DIRT] = Mathf.Max(0f, 1f - s - rock - st);
                }
            td.SetAlphamaps(0, 0, a);
        }

        // ------------------------------------------------------------------
        // 外の地面（art-r1-11）
        // ------------------------------------------------------------------

        public const int FILLER_RES = WorldBounds.FILLER_RES;
        public const float FILLER_DEPTH_M = WorldBounds.FILLER_DEPTH_M;
        public const float FILLER_RANGE_M = WorldBounds.FILLER_RANGE_M;
        public const float FILLER_BLEND_M = WorldBounds.FILLER_BLEND_M;

        /// <summary>
        /// ワールド全体の下の外の地面（低解像度の Terrain、WorldBounds.FillerRect）。高さは WorldBounds.FillerHeight（art-r2-01）：
        /// 遊べる範囲の 1 マスより奥は完全に埋め、縁の 1 マスはまわり 3×3 の一番低い地面の 3m 下、その外は縁の高さ − 0.8m から
        /// FILLER_BLEND_M かけて基本の高さ（±6m のうねり、山並みへ向かって 20〜60m 上る）へ。砂と土（斜面は岩）で塗る。
        /// </summary>
        public static Terrain BuildFiller(WorldContext ctx, Transform parent)
        {
            Vector4 f = WorldBounds.FillerRect();
            int n = FILLER_RES;
            float sizeX = f.z - f.x, sizeZ = f.w - f.y;
            float cellX = sizeX / (n - 1), cellZ = sizeZ / (n - 1);
            float baseY = WorldBounds.FILLER_Y - FILLER_DEPTH_M;
            var rects = new System.Collections.Generic.List<Vector4>();
            var terrains = new System.Collections.Generic.List<Terrain>();
            foreach (var sb in ctx.stages)
            {
                Vector3 o = sb.def.Origin;
                rects.Add(new Vector4(o.x, o.z, o.x + StageDefinitions.SIZE, o.z + StageDefinitions.SIZE));
                terrains.Add(sb.terrain);
            }
            if (ctx.lobbyTerrain != null) { rects.Add(WorldBounds.LobbyRect()); terrains.Add(ctx.lobbyTerrain); }
            System.Func<int, float, float, float> ground = (i, x, z) =>
            {
                Terrain t = terrains[i];
                return t == null ? WorldBounds.FILLER_Y : t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
            };
            float[,] world = WorldBounds.FillerGrid(n, rects.ToArray(), ground);
            var h = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    h[z, x] = Mathf.Clamp01((world[z, x] - baseY) / FILLER_RANGE_M);
            var td = CreateTerrainData("OuterGround", h, sizeX, sizeZ, FILLER_RANGE_M);
            // art-r3-10：外の地面はステージの土の色（3 ステージの土を混ぜた DirtOuter、クリーム色の砂の板にしない）＋斜面は地層の縞・岩
            td.terrainLayers = Layers(VARIANT_OUTER);
            int res = 512;
            td.alphamapResolution = res;
            td.baseMapResolution = 256;
            var a = new float[res, res, 4];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int hz = Mathf.FloorToInt(z / (float)(res - 1) * (n - 1) + 0.5f), hx = Mathf.FloorToInt(x / (float)(res - 1) * (n - 1) + 0.5f);
                    float slope = SlopeDeg(h, hz, hx, FILLER_RANGE_M, cellX, cellZ);
                    float st = Mathf.Clamp01((slope - FILLER_STRATA_FROM) / 10f);
                    float rock = Mathf.Clamp01((slope - 32f) / 10f) * (1f - st * 0.5f);
                    // 低い平らな所だけ少し砂（谷底の色の変化）、ほかは土
                    float low = 1f - Mathf.Clamp01((h[Mathf.Clamp(hz, 0, n - 1), Mathf.Clamp(hx, 0, n - 1)] * FILLER_RANGE_M + baseY - WorldBounds.FILLER_Y + 2f) / 6f);
                    float sand = Mathf.Clamp01(low * 0.6f) * (1f - st) * (1f - rock);
                    a[z, x, LAYER_STRATA] = st * (1f - rock);
                    a[z, x, LAYER_ROCK] = rock;
                    a[z, x, LAYER_SAND] = sand;
                    a[z, x, LAYER_DIRT] = Mathf.Max(0f, 1f - st * (1f - rock) - rock - sand);
                }
            td.SetAlphamaps(0, 0, a);
            GameObject tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "OuterGround";
            tgo.transform.SetParent(parent, false);
            tgo.transform.position = new Vector3(f.x, baseY, f.y);
            GenUtil.SetLayerRecursive(tgo, WorldLayers.Ground);
            var terrain = tgo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 8f;
            // art-r4-13：町へ近づく道（ロビーの南の切れ目から外の地面へ 300m）の轍も外の地面に塗る
            var road = LobbyLayout.StreetAndApproach();
            var roadW = new Vector3[road.Length];
            for (int i = 0; i < road.Length; i++) roadW[i] = WorldBounds.LOBBY_CENTER + road[i];
            ApplyToonMaterial(terrain, PathTrailTexture("OuterGround", new Vector3(f.x, 0f, f.y), sizeX, sizeZ, 2048, roadW, LobbyLayout.APPROACH_HALF_M), "OuterGround", 0);
            DressingBuilder.SetStatic(tgo);
            return terrain;
        }

        static int NearestIndex(Vector3[] pts, Vector3 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = WorldLayout.DistXZ(pts[i], p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>砂の範囲に SandZone（Is Trigger の BoxCollider）を並べる。乗り物は sandZoneCount で砂地を知る。</summary>
        static void BuildSandZones(StageBuild sb)
        {
            var parent = GenUtil.Child(sb.root, "SandZones", Vector3.zero).transform;
            parent.position = sb.def.Origin;
            float cell = StageDefinitions.SIZE / SAND_CELLS;
            // 道の上のマス（中心がコースから SAND_ROAD_SKIP_M 以内）は砂にしない（course-r1-09：遅くなるのは道の外だけ）。
            // ただし深い砂の区間（StageDef.sandTrailArcs、system-r2-01）は道の上も砂のまま
            var mask = (float[,])sb.sand.Clone();
            int mn = mask.GetLength(0);
            float[] acc = new float[sb.courseLocal.Length];
            for (int i = 1; i < acc.Length; i++) acc[i] = acc[i - 1] + WorldLayout.DistXZ(sb.courseLocal[i - 1], sb.courseLocal[i]);
            for (int cz = 0; cz < SAND_CELLS; cz++)
                for (int cx = 0; cx < SAND_CELLS; cx++)
                {
                    var c = new Vector3((cx + 0.5f) * cell, 0f, (cz + 0.5f) * cell);
                    if (WorldLayout.DistToPolylineXZ(c, sb.courseLocal) >= SAND_ROAD_SKIP_M) continue;
                    if (StageDefinitions.InDeepSand(sb.def, acc[NearestIndex(sb.courseLocal, c)])) continue;
                    int p0 = Mathf.FloorToInt(cz * cell / StageDefinitions.SIZE * mn), p1 = Mathf.Min(mn, Mathf.CeilToInt((cz + 1) * cell / StageDefinitions.SIZE * mn));
                    int q0 = Mathf.FloorToInt(cx * cell / StageDefinitions.SIZE * mn), q1 = Mathf.Min(mn, Mathf.CeilToInt((cx + 1) * cell / StageDefinitions.SIZE * mn));
                    for (int z = p0; z < p1; z++) for (int x = q0; x < q1; x++) mask[z, x] = 0f;
                }
            var runs = WorldLayout.SandRuns(mask, SAND_CELLS, SAND_THRESHOLD);
            foreach (int[] r in runs)
            {
                int cz = r[0], x0 = r[1], x1 = r[2];
                float minH = float.MaxValue, maxH = float.MinValue;
                for (float lz = cz * cell; lz <= (cz + 1) * cell; lz += cell * 0.25f)
                    for (float lx = x0 * cell; lx <= (x1 + 1) * cell; lx += cell * 0.25f)
                    {
                        float h = WorldLayout.SampleHeight(sb.heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, lx, lz);
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }
                var go = GenUtil.Child(parent, "Sand_" + cz + "_" + x0, new Vector3((x0 + x1 + 1) * 0.5f * cell, (minH + maxH) * 0.5f + 1f, (cz + 0.5f) * cell));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                go.layer = WorldLayers.TRIGGERS;
                box.size = new Vector3((x1 - x0 + 1) * cell, (maxH - minH) + 8f, cell);
                sb.sandZones.Add(GenUtil.AddU<SandZone>(go));
            }
        }
    }
}
