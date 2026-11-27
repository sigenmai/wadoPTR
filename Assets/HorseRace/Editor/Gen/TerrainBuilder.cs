using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの Terrain（1000m 四方、高さ 200m、解像度 513）を作る。
    /// TerrainLayer: 0 = 赤茶の土、1 = 岩（急な斜面）、2 = 砂（HeightmapGen.SandMask。CANYON は高さを渡して谷底だけ）。
    /// 実行時（Udon）からは Terrain の塗りを読めないので、砂の範囲には SandZone のトリガー箱も並べる（25m のマス目を x 方向に連結）。
    /// TerrainData・TerrainLayer・テクスチャは Assets/HorseRace/Generated/Terrain に上書き保存する。
    /// </summary>
    public static class TerrainBuilder
    {
        public const int LAYER_DIRT = 0, LAYER_ROCK = 1, LAYER_SAND = 2;
        public const int SAND_CELLS = 40;              // 1000m / 40 = 25m のマス
        public const float SAND_THRESHOLD = 0.5f;      // MountController は terrainSandWeight > 0.5 を砂地とみなす
        const float ROCK_SLOPE_FROM = 28f, ROCK_SLOPE_TO = 40f;

        static readonly Color DirtColor = new Color(0.62f, 0.36f, 0.22f);
        static readonly Color RockColor = new Color(0.55f, 0.45f, 0.38f);
        static readonly Color SandColor = new Color(0.90f, 0.76f, 0.52f);

        public static StageBuild Build(StageDef def, Transform parent)
        {
            var sb = new StageBuild { def = def };
            sb.root = GenUtil.World(parent, "Stage" + (def.index + 1) + "_" + def.label, def.center).transform;

            sb.courseLocal = StageDefinitions.SampleLocal(def);
            sb.goalArc = StageDefinitions.GoalArc(def, sb.courseLocal);
            Vector3[] norm = StageDefinitions.Normalize(sb.courseLocal);
            sb.heights = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, def.kind, def.seed, norm);
            sb.sand = HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES, def.kind, def.seed,
                def.kind == HeightmapGen.KIND_CANYON ? sb.heights : null);

            sb.courseWorld = new Vector3[sb.courseLocal.Length];
            for (int i = 0; i < sb.courseLocal.Length; i++) sb.courseWorld[i] = sb.Ground(sb.courseLocal[i].x, sb.courseLocal[i].z);

            TerrainData td = CreateTerrainData("Stage" + (def.index + 1), sb.heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M);
            PaintLayers(td, sb.heights, sb.sand);
            GameObject tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";
            tgo.transform.SetParent(sb.root, false);
            tgo.transform.position = def.Origin;
            GenUtil.SetLayerRecursive(tgo, WorldLayers.Ground);
            sb.terrain = tgo.GetComponent<Terrain>();
            sb.terrain.basemapDistance = 1200f;
            sb.terrain.heightmapPixelError = 4f;
            sb.terrain.drawInstanced = true;

            BuildSandZones(sb);
            return sb;
        }

        /// <summary>TerrainData を作って Generated/Terrain/&lt;name&gt;.asset に上書き保存する。</summary>
        public static TerrainData CreateTerrainData(string name, float[,] heights, float sizeM, float heightM)
        {
            GenUtil.EnsureFolder(GenUtil.GEN_TERRAIN);
            string path = GenUtil.GEN_TERRAIN + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null) AssetDatabase.DeleteAsset(path);
            var td = new TerrainData();
            td.heightmapResolution = heights.GetLength(0);
            td.size = new Vector3(sizeM, heightM, sizeM);
            td.SetHeights(0, 0, heights);
            AssetDatabase.CreateAsset(td, path);
            return td;
        }

        public static TerrainLayer[] Layers()
        {
            return new[]
            {
                Layer("TL_RedDirt", DirtColor, 0.10f, 11),
                Layer("TL_Rock", RockColor, 0.16f, 12),
                Layer("TL_Sand", SandColor, 0.06f, 13),
            };
        }

        static TerrainLayer Layer(string name, Color c, float noise, int seed)
        {
            GenUtil.EnsureFolder(GenUtil.GEN_TERRAIN);
            string texPath = GenUtil.GEN_TERRAIN + "/" + name + "_Tex.asset";
            string layerPath = GenUtil.GEN_TERRAIN + "/" + name + ".terrainlayer";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = NoiseTexture(c, noise, seed);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(12f, 12f);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>トゥーン調の地面テクスチャ：ほぼ単色に、大きめの斑を数段階でポスタライズして乗せる。</summary>
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
                    float n = Mathf.PerlinNoise(ox + x * 0.09f, oy + y * 0.09f);
                    n = Mathf.Floor(n * 3f) / 3f;
                    float k = 1f + (n - 0.5f) * amount * 2f;
                    px[y * N + x] = new Color(c.r * k, c.g * k, c.b * k, 1f);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        public static void PaintLayers(TerrainData td, float[,] heights, float[,] sand)
        {
            td.terrainLayers = Layers();
            int res = StageDefinitions.ALPHAMAP_RES;
            td.alphamapResolution = res;
            td.baseMapResolution = 512;
            int hn = heights.GetLength(0);
            float cellM = td.size.x / (hn - 1);
            var a = new float[res, res, 3];
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
                    a[z, x, LAYER_SAND] = s;
                    a[z, x, LAYER_ROCK] = rock;
                    a[z, x, LAYER_DIRT] = Mathf.Max(0f, 1f - s - rock);
                }
            td.SetAlphamaps(0, 0, a);
        }

        /// <summary>砂の範囲に SandZone（Is Trigger の BoxCollider）を並べる。乗り物は sandZoneCount で砂地を知る。</summary>
        static void BuildSandZones(StageBuild sb)
        {
            var parent = GenUtil.Child(sb.root, "SandZones", Vector3.zero).transform;
            parent.position = sb.def.Origin;
            var runs = WorldLayout.SandRuns(sb.sand, SAND_CELLS, SAND_THRESHOLD);
            float cell = StageDefinitions.SIZE / SAND_CELLS;
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
