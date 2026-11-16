// 和堂PTR 第七加工センター 夜間処理室 — シーン生成
// メニュー: WadoPTR > 処理室を建てる (Build World Scene)
//
// layout.json(Tools/gen_layout.py が出力)を読み、部屋・什器・原体・端末・掲示・音・VRChat 設定を
// すべて組み立てて Assets/WadoPTR/Scenes/WadoPTR.unity に保存する。何度実行しても同じ結果になる。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace WadoPTR.EditorTools
{
    public static class WadoWorldBuilder
    {
        const string Root = "Assets/WadoPTR";
        const string Gen = Root + "/Generated";
        const string ScenePath = Root + "/Scenes/WadoPTR.unity";
        // TextMeshPro(3D) は fontSize 1 でおよそ 0.1m。文字が全体に大きい/小さいときはここを変えて建て直す
        const float TextScale = 10f;
        const int LayerPickup = 13;
        const int LayerIgnoreRaycast = 2;

        // ---------------------------------------------------------------- layout.json
        [Serializable] class Layout { public RoomDef room; public BoxDef[] boxes; public PropDef[] props; public LightDef[] lights; public LabelDef[] labels; public AnchorDef[] anchors; public BodyDef[] body; }
        [Serializable] class RoomDef { public float w, d, h, tableY, tableZ; }
        [Serializable] class BoxDef { public string name; public float[] pos; public float[] size; public float yaw; public string mat; public bool collider; public float tile; public string group; }
        [Serializable] class PropDef { public string name; public string model; public float[] pos; public float yaw, pitch, roll, scale; public bool collider; public string group; }
        [Serializable] class LightDef { public string name; public float[] pos; public float[] color; public float intensity; public float range; public string kind; }
        [Serializable] class LabelDef { public string name; public string text; public float[] pos; public float yaw, pitch, roll, size, width, height; public string font; public string color; public string align; public string group; }
        [Serializable] class AnchorDef { public string name; public float[] pos; public float yaw; }
        [Serializable] class BodyDef { public string name; public float[] offset; public float[] size; public float[] cut; }

        // Tools/polyhaven_manifest.json の必要部分
        [Serializable] class PhManifest { public PhModel[] list; }
        [Serializable] class PhModel { public string key; public PhMat[] materials; }
        [Serializable] class PhMat { public string name; public string diff; public string nor; public float smoothness; public float metallic; }

        static Layout L;
        static Dictionary<string, AnchorDef> anchors;
        static Dictionary<string, Material> mats;
        static Dictionary<string, Transform> groups;
        static Dictionary<string, PhModel> phModels;
        static Dictionary<string, GameObject> propObjects;
        static TMP_FontAsset fontGothic, fontDot, fontHand;

        // ---------------------------------------------------------------- 入口
        [MenuItem("WadoPTR/処理室を建てる (Build World Scene)", false, 0)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("WadoPTR", "再生を止めてから実行してください。", "OK");
                return;
            }
            if (EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("WadoPTR", "スクリプトのコンパイルが終わってから、もう一度実行してください。", "OK");
                return;
            }
            if (!EnsureTmpEssentials()) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                EditorUtility.DisplayProgressBar("WadoPTR", "準備中", 0.05f);
                EnsureFolders();
                EnsureProgramAssets();
                LoadData();

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                groups = new Dictionary<string, Transform>();
                propObjects = new Dictionary<string, GameObject>();

                EditorUtility.DisplayProgressBar("WadoPTR", "素材", 0.15f);
                BuildMaterials();
                BuildFonts();

                EditorUtility.DisplayProgressBar("WadoPTR", "部屋", 0.35f);
                foreach (var b in L.boxes) MakeBox(b);
                foreach (var p in L.props) MakeProp(p);
                foreach (var l in L.lights) MakeLight(l);
                foreach (var l in L.labels) MakeLabel(l);

                EditorUtility.DisplayProgressBar("WadoPTR", "処理ライン", 0.6f);
                BuildGameplay();

                EditorUtility.DisplayProgressBar("WadoPTR", "環境", 0.85f);
                BuildEnvironment();

                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[WadoPTR] 処理室を建てました: " + ScenePath);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("WadoPTR",
                    "処理室を建てました。\n\n" + ScenePath + "\n\n再生ボタン(ClientSim)で試すか、VRChat SDK > Show Control Panel から Build & Test してください。", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                EditorUtility.DisplayDialog("WadoPTR", "生成中にエラーが出ました。Console を確認してください。\n\n" + e.Message, "OK");
            }
        }

        // ---------------------------------------------------------------- 前提
        static bool EnsureTmpEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return true;
            bool go = EditorUtility.DisplayDialog("WadoPTR",
                "TextMesh Pro の基本リソース (TMP Essentials) が未導入です。\n導入してから、もう一度このメニューを実行してください。", "導入する", "やめる");
            if (go)
            {
                // TMP のバージョン差で名前が変わっても壊れないよう、メニュー経由 → リフレクションの順に試す
                bool ok = false;
                try
                {
                    ok = EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                if (!ok)
                {
                    try
                    {
                        Type importer = typeof(TMP_Settings).Assembly.GetType("TMPro.TMP_PackageResourceImporter");
                        var method = importer != null ? importer.GetMethod("ImportResources", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
                        if (method != null)
                        {
                            method.Invoke(null, new object[] { true, false, false });
                            ok = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                if (!ok)
                {
                    EditorUtility.DisplayDialog("WadoPTR", "自動導入に失敗しました。\nWindow > TextMeshPro > Import TMP Essential Resources を実行してください。", "OK");
                }
            }
            return false;
        }

        static void EnsureFolders()
        {
            foreach (var d in new[] { Gen, Gen + "/Materials", Gen + "/Fonts", Root + "/Scenes" })
            {
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>U# スクリプトに対応する Program Asset が無ければ作る(同梱の .asset が壊れた場合の保険)。</summary>
        static void EnsureProgramAssets()
        {
            var types = new[] { typeof(WadoGame), typeof(WadoBody), typeof(WadoPart), typeof(WadoBlade), typeof(WadoButton), typeof(WadoChute), typeof(WadoAmbience) };
            bool created = false;
            foreach (var t in types)
            {
                if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(t) != null) continue;
                string csPath = Root + "/Scripts/" + t.Name + ".cs";
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
                if (script == null) throw new Exception("スクリプトが見つかりません: " + csPath);
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                string assetPath = Root + "/Scripts/" + t.Name + ".asset";
                if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(asset, assetPath);
                created = true;
                Debug.Log("[WadoPTR] Program Asset を作成: " + assetPath);
            }
            if (created)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                UdonSharpProgramAsset.CompileAllCsPrograms();
            }
        }

        static void LoadData()
        {
            L = JsonUtility.FromJson<Layout>(File.ReadAllText(Root + "/Editor/layout.json"));
            anchors = L.anchors.ToDictionary(a => a.name, a => a);
            phModels = new Dictionary<string, PhModel>();
            var man = JsonUtility.FromJson<PhManifest>(File.ReadAllText(Root + "/Editor/models.json"));
            foreach (var m in man.list) phModels[m.key] = m;
        }

        // ---------------------------------------------------------------- 素材
        static Material NewMat(string name, Color color, float smooth, float metal)
        {
            string path = Gen + "/Materials/" + Safe(name) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Standard");
            m.color = color;
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            m.mainTexture = null;
            m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.mainTextureScale = Vector2.one;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetTextures(Material m, string diffPath, string norPath)
        {
            if (!string.IsNullOrEmpty(diffPath))
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath);
                if (t != null) m.mainTexture = t;
            }
            if (!string.IsNullOrEmpty(norPath))
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(norPath);
                if (n != null)
                {
                    m.SetTexture("_BumpMap", n);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
            EditorUtility.SetDirty(m);
        }

        static void SetEmission(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
        }

        static Material Textured(string name, string texFolder, float smooth, float metal)
        {
            var m = NewMat(name, Color.white, smooth, metal);
            string b = Root + "/Textures/" + texFolder + "/" + texFolder;
            SetTextures(m, b + "_diff.jpg", b + "_nor.jpg");
            return m;
        }

        static Material Unlit(string name, Color c)
        {
            string path = Gen + "/Materials/" + Safe(name) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Unlit/Color");
            m.color = c;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material UnlitTransparent(string name, string texPath)
        {
            string path = Gen + "/Materials/" + Safe(name) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Unlit/Transparent"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Unlit/Transparent");
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildMaterials()
        {
            mats = new Dictionary<string, Material>();
            mats["floor"] = Textured("floor", "worn_tile_floor", 0.5f, 0f);
            mats["ceiling"] = Textured("ceiling", "dirty_concrete", 0.1f, 0f);
            mats["tile_wall"] = Textured("tile_wall", "long_white_tiles", 0.62f, 0f);
            mats["tile_wall"].color = new Color(0.86f, 0.9f, 0.9f);
            mats["steel"] = NewMat("steel", new Color(0.66f, 0.69f, 0.7f), 0.68f, 0.9f);
            mats["door"] = Textured("door", "painted_metal_shutter", 0.35f, 0.5f);
            mats["black"] = NewMat("black", new Color(0.01f, 0.01f, 0.012f), 0.1f, 0f);
            mats["rubber"] = NewMat("rubber", new Color(0.03f, 0.03f, 0.035f), 0.25f, 0f);
            mats["led_black"] = NewMat("led_black", new Color(0.015f, 0.02f, 0.022f), 0.55f, 0f);
            mats["paper"] = NewMat("paper", new Color(0.78f, 0.76f, 0.68f), 0.05f, 0f);
            mats["note"] = NewMat("note", new Color(0.93f, 0.84f, 0.36f), 0.05f, 0f);
            mats["plastic_grey"] = NewMat("plastic_grey", new Color(0.42f, 0.44f, 0.44f), 0.4f, 0f);
            mats["button_green"] = NewMat("button_green", new Color(0.10f, 0.55f, 0.22f), 0.6f, 0f);
            SetEmission(mats["button_green"], new Color(0.02f, 0.25f, 0.06f));
            mats["button_red"] = NewMat("button_red", new Color(0.65f, 0.08f, 0.06f), 0.6f, 0f);
            SetEmission(mats["button_red"], new Color(0.3f, 0.02f, 0.01f));
            mats["button_amber"] = NewMat("button_amber", new Color(0.8f, 0.5f, 0.08f), 0.6f, 0f);
            SetEmission(mats["button_amber"], new Color(0.3f, 0.16f, 0.0f));
            mats["button_grey"] = NewMat("button_grey", new Color(0.55f, 0.57f, 0.58f), 0.5f, 0f);
            mats["lamp_off"] = NewMat("lamp_off", new Color(0.08f, 0.1f, 0.08f), 0.5f, 0f);
            mats["lamp_on"] = Unlit("lamp_on", new Color(0.35f, 1f, 0.5f));
            mats["mark"] = Unlit("mark", new Color(1f, 0.32f, 0.08f));
            mats["gash"] = NewMat("gash", new Color(0.16f, 0.01f, 0.02f), 0.7f, 0f);
            mats["cut"] = NewMat("cut", new Color(0.30f, 0.03f, 0.04f), 0.72f, 0f);
            mats["organ"] = NewMat("organ", new Color(0.36f, 0.05f, 0.07f), 0.85f, 0f);
            mats["skin"] = NewMat("skin", new Color(0.86f, 0.88f, 0.92f), 0.28f, 0f);
            SetTextures(mats["skin"], Root + "/Models/body/body_skin.jpg", null);
            for (int i = 0; i < 4; i++) mats["stain" + i] = UnlitTransparent("stain" + i, Root + "/Textures/stains/stain" + i + ".png");
            var spray = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/spray.mat");
            if (spray == null)
            {
                spray = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(spray, Gen + "/Materials/spray.mat");
            }
            spray.color = new Color(0.45f, 0.02f, 0.03f, 1f);
            EditorUtility.SetDirty(spray);
            mats["spray"] = spray;
        }

        /// <summary>タイル貼りの面は、箱の寸法に合わせて繰り返し回数を変えた複製を使う。</summary>
        static Material Tiled(string baseName, float u, float v)
        {
            string key = baseName + "_" + Mathf.RoundToInt(u * 100) + "x" + Mathf.RoundToInt(v * 100);
            if (mats.TryGetValue(key, out var hit)) return hit;
            string path = Gen + "/Materials/" + Safe(key) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(mats[baseName]);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(mats[baseName]);
            m.shaderKeywords = mats[baseName].shaderKeywords;
            m.mainTextureScale = new Vector2(u, v);
            EditorUtility.SetDirty(m);
            mats[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- 書体
        static void BuildFonts()
        {
            string chars = CollectCharacters();
            fontGothic = MakeFont("MPLUS1p-Bold", chars);
            fontDot = MakeFont("DotGothic16-Regular", chars);
            fontHand = MakeFont("Yomogi-Regular", chars);
        }

        /// <summary>世界の中に出てくる文字をすべて集める(スクリプト・レイアウト・このファイル)。</summary>
        static string CollectCharacters()
        {
            var set = new HashSet<char>();
            for (char c = ' '; c <= '~'; c++) set.Add(c);
            for (char c = 'ぁ'; c <= 'ゖ'; c++) set.Add(c);     // ひらがな
            for (char c = 'ァ'; c <= 'ー'; c++) set.Add(c);     // カタカナ
            foreach (char c in "、。・「」『』（）ー―…〜！？：；％＋－×÷＝￥¥■□▲▼→←↑↓●○◎※〒々〆①②③④⑤⑥⑦⑧⑨⑩") set.Add(c);
            var files = new List<string>();
            files.AddRange(Directory.GetFiles(Root + "/Scripts", "*.cs"));
            files.AddRange(Directory.GetFiles(Root + "/Editor", "*.cs"));
            files.Add(Root + "/Editor/layout.json");
            foreach (var f in files)
            {
                foreach (char c in File.ReadAllText(f))
                {
                    if (c > 0x7F && !char.IsControl(c) && !char.IsSurrogate(c)) set.Add(c);
                }
            }
            var arr = set.ToArray();
            Array.Sort(arr);
            return new string(arr);
        }

        static TMP_FontAsset MakeFont(string ttfName, string chars)
        {
            string outPath = Gen + "/Fonts/" + ttfName + " SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (fa == null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/" + ttfName + ".ttf");
                if (font == null) throw new Exception("フォントが見つかりません: " + ttfName);
                fa = TMP_FontAsset.CreateFontAsset(font, 56, 6, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
                fa.name = ttfName + " SDF";
                AssetDatabase.CreateAsset(fa, outPath);
                fa.material.name = ttfName + " SDF Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                fa.atlasTexture.name = ttfName + " SDF Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            }
            fa.TryAddCharacters(chars, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[WadoPTR] " + ttfName + " に無い文字: " + missing);
            EditorUtility.SetDirty(fa);
            if (fa.atlasTexture != null) EditorUtility.SetDirty(fa.atlasTexture);
            if (fa.material != null) EditorUtility.SetDirty(fa.material);
            return fa;
        }

        static TMP_FontAsset FontOf(string key)
        {
            if (key == "dot") return fontDot;
            if (key == "hand") return fontHand;
            return fontGothic;
        }

        // ---------------------------------------------------------------- 基本部品
        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        static Vector3 V(float[] a) { return new Vector3(a[0], a[1], a[2]); }

        static Transform Group(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "Misc";
            if (groups.TryGetValue(name, out var t)) return t;
            var go = new GameObject(name);
            groups[name] = go.transform;
            return go.transform;
        }

        static Vector3 AnchorPos(string name)
        {
            if (!anchors.TryGetValue(name, out var a)) throw new Exception("layout.json に基準点がありません: " + name);
            return V(a.pos);
        }

        static Transform AnchorObject(string name, Transform parent)
        {
            var a = anchors[name];
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = V(a.pos);
            go.transform.rotation = Quaternion.Euler(0, a.yaw, 0);
            return go.transform;
        }

        static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void MakeBox(BoxDef b)
        {
            Vector3 size = V(b.size);
            Material m = mats.TryGetValue(b.mat, out var found) ? found : mats["plastic_grey"];
            if (b.tile > 0f)
            {
                // 面積の大きい 2 辺で繰り返す
                float[] s = { size.x, size.y, size.z };
                Array.Sort(s);
                bool horizontal = size.y <= s[0] + 0.0001f;
                float u = horizontal ? size.x : Mathf.Max(size.x, size.z);
                float v = horizontal ? size.z : size.y;
                m = Tiled(b.mat, u / b.tile, v / b.tile);
            }
            var go = Cube(b.name, Group(b.group), Vector3.zero, size, m, b.collider);
            go.transform.position = V(b.pos);
            go.transform.rotation = Quaternion.Euler(0, b.yaw, 0);
            go.isStatic = true;
        }

        static GameObject LoadModel(string folder, string file)
        {
            string path = Root + "/Models/" + folder + "/" + file + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new Exception("モデルが見つかりません: " + path);
            return prefab;
        }

        /// <summary>FBX を親の下に置く。読み込み時の回転・縮尺は保つ。</summary>
        static GameObject PlaceModel(string folder, string file, Transform parent, string name)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel(folder, file));
            Vector3 lp = inst.transform.localPosition;
            Quaternion lr = inst.transform.localRotation;
            Vector3 ls = inst.transform.localScale;
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = lp;
            inst.transform.localRotation = lr;
            inst.transform.localScale = ls;
            inst.name = name;
            return inst;
        }

        static Material PropMaterial(string model, string matName, int index)
        {
            if (!phModels.TryGetValue(model, out var pm) || pm.materials == null || pm.materials.Length == 0) return mats["plastic_grey"];
            PhMat def = pm.materials.FirstOrDefault(x => x.name == matName);
            if (def == null) def = pm.materials[Mathf.Clamp(index, 0, pm.materials.Length - 1)];
            string key = "ph_" + model + "_" + def.name;
            if (mats.TryGetValue(key, out var hit)) return hit;
            var m = NewMat(key, Color.white, def.smoothness, def.metallic);
            string dir = Root + "/Models/" + model + "/";
            SetTextures(m, string.IsNullOrEmpty(def.diff) ? null : dir + def.diff, string.IsNullOrEmpty(def.nor) ? null : dir + def.nor);
            if (def.name.Contains("glass") && model.Contains("fluorescent")) SetEmission(m, new Color(0.85f, 0.95f, 1f) * 2.2f);
            mats[key] = m;
            return m;
        }

        static void ApplyPropMaterials(GameObject inst, string model)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = PropMaterial(model, src[i] != null ? src[i].name : "", i);
                r.sharedMaterials = dst;
            }
        }

        static void MakeProp(PropDef p)
        {
            var holder = new GameObject(p.name);
            holder.transform.SetParent(Group(p.group), false);
            holder.transform.position = V(p.pos);
            holder.transform.rotation = Quaternion.Euler(p.pitch, p.yaw, p.roll);
            holder.transform.localScale = Vector3.one * p.scale;
            var inst = PlaceModel(p.model, p.model, holder.transform, "model");
            ApplyPropMaterials(inst, p.model);
            if (p.collider)
            {
                var mf = inst.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var bc = mf.gameObject.AddComponent<BoxCollider>();
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = mf.sharedMesh.bounds.size;
                }
            }
            foreach (var t in holder.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            propObjects[p.name] = holder;
        }

        static void MakeLight(LightDef l)
        {
            var go = new GameObject(l.name);
            go.transform.SetParent(Group("Lights"), false);
            go.transform.position = V(l.pos);
            var lt = go.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(l.color[0], l.color[1], l.color[2]);
            lt.intensity = l.intensity;
            lt.range = l.range;
            lt.shadows = LightShadows.None;
            lt.renderMode = LightRenderMode.ForcePixel;
            lt.lightmapBakeType = LightmapBakeType.Realtime;
        }

        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }

        /// <summary>壁面などの文字。pos は読む人から見た左上(align=center なら上辺中央, right なら右上)。size は一文字の高さ(m)。</summary>
        static TextMeshPro Text(string name, Transform parent, Vector3 localPos, float size, float width, TMP_FontAsset font, Color color, string align, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.fontSize = size * TextScale;
            t.color = color;
            t.richText = true;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.lineSpacing = 8f;
            var rt = t.rectTransform;
            if (align == "center")
            {
                rt.pivot = new Vector2(0.5f, 1f);
                t.alignment = TextAlignmentOptions.Top;
            }
            else if (align == "right")
            {
                rt.pivot = new Vector2(1f, 1f);
                t.alignment = TextAlignmentOptions.TopRight;
            }
            else
            {
                rt.pivot = new Vector2(0f, 1f);
                t.alignment = TextAlignmentOptions.TopLeft;
            }
            rt.sizeDelta = new Vector2(width, 2f);
            go.transform.localPosition = localPos;
            t.text = text;
            return t;
        }

        static void MakeLabel(LabelDef l)
        {
            var t = Text(l.name, Group(l.group), Vector3.zero, l.size, l.width, FontOf(l.font), Hex(l.color), l.align, l.text);
            t.transform.position = V(l.pos);
            t.transform.rotation = Quaternion.Euler(l.pitch, l.yaw, l.roll);
        }

        static AudioClip Clip(string name)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".ogg");
            if (c == null) Debug.LogWarning("[WadoPTR] 音が見つかりません: " + name);
            return c;
        }

        static AudioSource Audio(string name, Transform parent, Vector3 pos, bool spatial, float far, AudioClip loop = null, float volume = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = loop != null;
            a.loop = loop != null;
            a.clip = loop;
            a.volume = volume;
            a.spatialBlend = spatial ? 1f : 0f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 0.5f;
            a.maxDistance = far;
            a.dopplerLevel = 0f;
            var v = go.AddComponent<VRCSpatialAudioSource>();
            v.EnableSpatialization = spatial;
            v.Gain = spatial ? 6f : 0f;
            v.Near = 0.3f;
            v.Far = far;
            v.VolumetricRadius = 0f;
            v.UseAudioSourceVolumeCurve = false;
            return a;
        }

        static T AddUdon<T>(GameObject go) where T : UdonSharpBehaviour
        {
            var c = go.AddUdonSharpComponent<T>();
            if (c == null) throw new Exception("UdonSharp コンポーネントを追加できません: " + typeof(T).Name);
            return c;
        }

        static void SetInteract(UdonSharpBehaviour proxy, string text, float proximity)
        {
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (ub == null) return;
            ub.InteractionText = text;
            ub.proximity = proximity;
            EditorUtility.SetDirty(ub);
        }

        static WadoButton Button(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, WadoGame game, int action, int arg, string verb)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x * 1.25f, size.y * 1.25f, size.z * 2.2f);
            col.center = new Vector3(0, 0, -size.z * 0.4f);
            Cube("base", go.transform, new Vector3(0, 0, 0.004f), new Vector3(size.x * 1.25f, size.y * 1.25f, 0.012f), mats["plastic_grey"], false);
            var cap = Cube("cap", go.transform, new Vector3(0, 0, -size.z * 0.5f), size, mat, false);
            var b = AddUdon<WadoButton>(go);
            b.game = game;
            b.action = action;
            b.arg = arg;
            b.cap = cap.transform;
            b.travel = size.z * 0.6f;
            SetInteract(b, verb, 2.5f);
            EditorUtility.SetDirty(b);
            return b;
        }

        // ---------------------------------------------------------------- 処理ライン
        static void BuildGameplay()
        {
            var sys = Group("System");
            var gameGo = new GameObject("WadoGame");
            gameGo.transform.SetParent(sys, false);
            var game = AddUdon<WadoGame>(gameGo);

            var bodyGo = new GameObject("WadoBody");
            bodyGo.transform.SetParent(sys, false);
            var body = AddUdon<WadoBody>(bodyGo);
            game.body = body;
            body.game = game;

            // --- 基準点
            var points = new GameObject("Points").transform;
            points.SetParent(sys, false);
            body.tablePoint = AnchorObject("Table", points);
            body.hatchPoint = AnchorObject("Hatch", points);
            body.trayPoint = AnchorObject("Tray", points);
            body.chutePoint = AnchorObject("Chute", points);

            // --- 音
            var sfxGame = Audio("Sfx_Terminal", sys, new Vector3(0, 2.4f, 0), false, 20f);
            var sfxScale = Audio("Sfx_Scale", sys, AnchorPos("Chute") + Vector3.up * 0.5f, true, 14f);
            var sfxBody = Audio("Sfx_Hatch", sys, AnchorPos("HatchDoor"), true, 14f);
            var sfxBlade = Audio("Sfx_Blade", sys, AnchorPos("Table") + Vector3.up * 0.2f, true, 12f);
            game.sfx = sfxGame;
            game.sfxScale = sfxScale;
            game.clipCoin = new[] { Clip("coin1"), Clip("coin2"), Clip("coin3") };
            game.clipBeep = Clip("beep");
            game.clipBeepOk = Clip("beep_ok");
            game.clipBuzzer = Clip("buzzer");
            game.clipPrinter = Clip("printer");
            game.clipChimeUp = Clip("chime_up");
            game.clipChimeDown = Clip("chime_down");
            game.clipPlop = Clip("plop");
            body.sfx = sfxBody;
            body.clipMotor = Clip("motor");
            body.clipHatch = Clip("hatch");
            body.clipCloth = Clip("cloth");
            body.clipChime = Clip("chime_up");

            // --- 搬入口の扉
            var door = Cube("HatchDoor", Group("Line"), Vector3.zero, new Vector3(0.05f, 0.74f, 0.94f), mats["steel"], false);
            door.transform.position = AnchorPos("HatchDoor");
            body.hatchDoor = door.transform;

            // --- 原体
            var loose = new GameObject("Loose").transform;
            loose.SetParent(sys, false);
            var root = new GameObject("Gentai").transform;
            root.SetParent(sys, false);
            root.position = AnchorPos("Table");
            body.root = root;

            float[] hp = { 6f, 5f, 5f, 6f, 6f, 9f };
            string[] order = { "head", "arm_l", "arm_r", "leg_l", "leg_r", "torso" };
            var parts = new List<WadoPart>();
            for (int i = 0; i < order.Length; i++)
            {
                BodyDef def = L.body.First(x => x.name == order[i]);
                parts.Add(MakeBodyPart(i, def, hp[i], root, loose, game, body));
            }
            // 内容物 (取扱許可が下りてから)
            parts.Add(MakeOrgan(6, "Naiyobutsu_I", new Vector3(0.24f, 0.09f, 0.15f), loose, game, body));
            parts.Add(MakeOrgan(7, "Naiyobutsu_Ro_A", new Vector3(0.11f, 0.06f, 0.07f), loose, game, body));
            parts.Add(MakeOrgan(8, "Naiyobutsu_Ro_B", new Vector3(0.11f, 0.06f, 0.07f), loose, game, body));
            parts.Add(MakeOrgan(9, "Naiyobutsu_Ha", new Vector3(0.12f, 0.11f, 0.1f), loose, game, body));
            body.parts = parts.ToArray();

            // --- 飛沫
            var sprayGo = new GameObject("Spray");
            sprayGo.transform.SetParent(sys, false);
            sprayGo.transform.position = AnchorPos("Table");
            sprayGo.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var ps = sprayGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new Color(0.5f, 0.02f, 0.03f, 1f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 38f;
            sh.radius = 0.03f;
            var pr = sprayGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mats["spray"];
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            body.spray = ps;

            // --- 刃物
            var rest = AnchorObject("BladeRest", points);
            var bladeGo = new GameObject("Hamono");
            bladeGo.transform.SetParent(sys, false);
            bladeGo.transform.position = rest.position;
            bladeGo.layer = LayerPickup;
            var brb = bladeGo.AddComponent<Rigidbody>();
            brb.isKinematic = true;
            brb.useGravity = false;
            var bcol = bladeGo.AddComponent<BoxCollider>();
            bcol.center = new Vector3(0f, 0.164f, 0.011f);
            bcol.size = new Vector3(0.06f, 0.76f, 0.08f);
            var bpick = bladeGo.AddComponent<VRCPickup>();
            bpick.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            bpick.orientation = VRC_Pickup.PickupOrientation.Any;
            bpick.InteractionText = "刃物";
            bpick.UseText = "切る";
            bpick.proximity = 2.5f;
            bpick.allowManipulationWhenEquipped = false;
            var bvis = PlaceModel("machete", "machete", bladeGo.transform, "model");
            ApplyPropMaterials(bvis, "machete");
            SetLayer(bladeGo, LayerPickup);
            var edge = new GameObject("edge").transform;
            edge.SetParent(bladeGo.transform, false);
            edge.localPosition = new Vector3(0f, 0.34f, 0.01f);
            var blade = AddUdon<WadoBlade>(bladeGo);
            blade.game = game;
            blade.body = body;
            blade.pickup = bpick;
            blade.edge = edge;
            blade.restPoint = rest;
            blade.sfx = sfxBlade;
            blade.clipChop = new[] { Clip("chop1"), Clip("chop2"), Clip("chop3"), Clip("chop4") };
            blade.clipSever = new[] { Clip("sever1"), Clip("sever2") };
            blade.clipSwing = new[] { Clip("swing1"), Clip("swing2") };
            blade.clipCrack = Clip("crack");
            blade.clipTake = Clip("blade_take");
            EditorUtility.SetDirty(blade);

            // --- 計量口
            var chuteGo = new GameObject("ChuteTrigger");
            chuteGo.transform.SetParent(sys, false);
            chuteGo.transform.position = AnchorPos("Chute");
            chuteGo.layer = LayerIgnoreRaycast;
            var ccol = chuteGo.AddComponent<BoxCollider>();
            ccol.isTrigger = true;
            ccol.center = new Vector3(0f, 0.15f, 0f);
            ccol.size = new Vector3(0.9f, 0.75f, 0.9f);
            AddUdon<WadoChute>(chuteGo);

            // --- 搬送ベルト (設置後に現れる)
            var belt = new GameObject("Belt");
            belt.transform.SetParent(Group("Line"), false);
            Vector3 from = AnchorPos("BeltFrom");
            Vector3 to = AnchorPos("Chute");
            float x0 = from.x, x1 = to.x - 0.55f;
            float tz = L.room.tableZ, ty = L.room.tableY;
            Cube("Belt_Bed", belt.transform, new Vector3((x0 + x1) / 2, ty - 0.045f, tz), new Vector3(x1 - x0, 0.05f, 0.62f), mats["rubber"], true);
            Cube("Belt_Rail_F", belt.transform, new Vector3((x0 + x1) / 2, ty - 0.02f, tz - 0.34f), new Vector3(x1 - x0, 0.1f, 0.04f), mats["steel"], false);
            Cube("Belt_Rail_B", belt.transform, new Vector3((x0 + x1) / 2, ty - 0.02f, tz + 0.34f), new Vector3(x1 - x0, 0.1f, 0.04f), mats["steel"], false);
            Cube("Belt_Leg_A", belt.transform, new Vector3(x0 + 0.3f, (ty - 0.07f) / 2, tz), new Vector3(0.06f, ty - 0.07f, 0.5f), mats["steel"], false);
            Cube("Belt_Leg_B", belt.transform, new Vector3(x1 - 0.3f, (ty - 0.07f) / 2, tz), new Vector3(0.06f, ty - 0.07f, 0.5f), mats["steel"], false);
            Audio("Belt_Hum", belt.transform, new Vector3((x0 + x1) / 2, ty, tz), true, 7f, Clip("amb_conveyor"), 0.22f);
            game.beltVisual = belt;
            if (groups.TryGetValue("OrganShelf", out var shelf)) game.organShelfVisual = shelf.gameObject;

            BuildDisplays(game, body);

            EditorUtility.SetDirty(game);
            EditorUtility.SetDirty(body);
        }

        static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void ConfigurePart(WadoPart part, GameObject go, GameObject visual, int kind, float hp, Vector3 colliderSize, Transform loose, WadoGame game, WadoBody body, string interact)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.drag = 0.3f;
            rb.angularDrag = 1.5f;
            rb.isKinematic = true;
            var col = go.AddComponent<BoxCollider>();
            col.size = colliderSize;
            var pick = go.AddComponent<VRCPickup>();
            pick.AutoHold = VRC_Pickup.AutoHoldMode.No;
            pick.orientation = VRC_Pickup.PickupOrientation.Any;
            pick.InteractionText = interact;
            pick.proximity = 2.5f;
            pick.pickupable = false;
            part.kind = kind;
            part.hpMax = hp;
            part.game = game;
            part.owner = body;
            part.looseParent = loose;
            part.visual = visual;
            part.solid = col;
            part.rb = rb;
            part.pickup = pick;
            SetLayer(go, LayerPickup);
            EditorUtility.SetDirty(part);
        }

        static WadoPart MakeBodyPart(int kind, BodyDef def, float hp, Transform root, Transform loose, WadoGame game, WadoBody body)
        {
            var go = new GameObject("Buzai_" + def.name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = V(def.offset);
            var visual = PlaceModel("body", def.name, go.transform, "visual");
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    bool isCut = (src[i] != null && src[i].name.ToLowerInvariant().Contains("cut")) || (src[i] == null && i > 0);
                    dst[i] = isCut ? mats["cut"] : mats["skin"];
                }
                r.sharedMaterials = dst;
            }

            Vector3 size = V(def.size);
            Vector3 cutLocal = V(def.cut) - V(def.offset);
            var cp = new GameObject("cutPoint").transform;
            cp.SetParent(go.transform, false);
            cp.localPosition = cutLocal;

            // 切取線: 関節を貫く薄い板。肌と交わるところが線として見え、上端がわずかに肌から出る。
            // 腕と体幹は体の軸に沿い、頭と脚は体を横切る。top は原体ルート基準の肌の高さ(Tools で実測)
            bool alongBody = def.name.StartsWith("arm") || def.name == "torso";
            float len = 0.21f, top = 0.218f;
            Vector3 c = V(def.cut);
            if (def.name == "head") { len = 0.16f; top = 0.186f; }
            else if (def.name.StartsWith("arm")) { len = 0.15f; top = 0.176f; c.x += 0.025f; }
            else if (def.name == "torso") { len = 0.44f; top = 0.256f; }
            float bottom = 0.04f;
            Vector3 fin = new Vector3(c.x, (top + bottom) / 2f, c.z) - V(def.offset);
            Quaternion rot = Quaternion.Euler(0, alongBody ? 90f : 0f, 0);
            var mark = Cube("cutMark", go.transform, fin, new Vector3(0.006f, top - bottom, len), mats["mark"], false);
            mark.transform.localRotation = rot;
            var gash = Cube("gash", go.transform, fin - new Vector3(0, 0.002f, 0), new Vector3(0.036f, top - bottom, len), mats["gash"], false);
            gash.transform.localRotation = rot;

            var part = AddUdon<WadoPart>(go);
            part.cutPoint = cp;
            part.cutMark = mark;
            part.gash = gash.transform;
            ConfigurePart(part, go, visual, kind, hp, new Vector3(size.x * 0.9f, size.y * 0.8f, size.z * 0.9f), loose, game, body, "部材");
            return part;
        }

        static WadoPart MakeOrgan(int kind, string name, Vector3 size, Transform loose, WadoGame game, WadoBody body)
        {
            var go = new GameObject(name);
            go.transform.SetParent(loose, false);
            go.transform.position = AnchorPos("Tray") + new Vector3(0, -5f, 0);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "visual";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = size;
            visual.GetComponent<MeshRenderer>().sharedMaterial = mats["organ"];
            var part = AddUdon<WadoPart>(go);
            ConfigurePart(part, go, visual, kind, 1f, size * 0.85f, loose, game, body, "内容物");
            return part;
        }

        // ---------------------------------------------------------------- 表示・端末・掲示
        static readonly Color Amber = new Color(1f, 0.72f, 0.26f);
        static readonly Color LedWhite = new Color(0.91f, 0.92f, 0.9f);
        static readonly Color LedDim = new Color(0.48f, 0.55f, 0.56f);
        static readonly Color LedGreen = new Color(0.49f, 1f, 0.61f);
        static readonly Color LedRed = new Color(1f, 0.42f, 0.37f);
        static readonly Color Ink = new Color(0.1f, 0.1f, 0.1f);

        static Transform Panel(string anchorName)
        {
            return AnchorObject(anchorName, Group("Displays"));
        }

        static void BuildDisplays(WadoGame game, WadoBody body)
        {
            // 残高
            var money = Panel("Money");
            game.txtMoney = Text("txtMoney", money, new Vector3(0, -0.03f, 0), 0.3f, 2.8f, fontDot, Amber, "left", "¥0");
            game.txtRate = Text("txtRate", money, new Vector3(0, -0.385f, 0), 0.052f, 2.0f, fontDot, LedDim, "left", "");
            game.txtRank = Text("txtRank", money, new Vector3(2.76f, 0.1f, 0), 0.08f, 1.4f, fontDot, LedGreen, "right", "研修中");

            // 相場
            var market = Panel("Market");
            game.txtMarket = Text("txtMarket", market, Vector3.zero, 0.105f, 1.9f, fontDot, LedWhite, "left", "");
            game.txtMarket.lineSpacing = 22f;

            // 稼働状況
            var lines = Panel("Lines");
            string[] lineNames = { "夜間補助員", "自動分割機", "第二処理ライン", "搬入元 提携", "海外販路" };
            var lamps = new GameObject[lineNames.Length];
            for (int i = 0; i < lineNames.Length; i++)
            {
                float y = -i * 0.21f;
                Cube("lampOff" + i, lines, new Vector3(0.07f, y - 0.05f, -0.005f), new Vector3(0.09f, 0.09f, 0.02f), mats["lamp_off"], false);
                lamps[i] = Cube("lampOn" + i, lines, new Vector3(0.07f, y - 0.05f, -0.012f), new Vector3(0.075f, 0.075f, 0.02f), mats["lamp_on"], false);
                Text("lineName" + i, lines, new Vector3(0.2f, y, 0), 0.085f, 1.7f, fontDot, LedWhite, "left", lineNames[i]);
            }
            game.lineLamps = lamps;

            // 計量表示
            var scale = Panel("Scale");
            game.txtScale = Text("txtScale", scale, Vector3.zero, 0.085f, 1.15f, fontDot, LedGreen, "left", "計量待機");

            // 伝票
            var slip = Panel("Slip");
            game.txtSlip = Text("txtSlip", slip, Vector3.zero, 0.04f, 1.18f, fontDot, Ink, "left", "");
            game.txtSlip.lineSpacing = 30f;

            // 搬入表示と呼出・清掃
            var hatch = Panel("HatchText");
            game.txtHatch = Text("txtHatch", hatch, Vector3.zero, 0.105f, 1.42f, fontDot, LedRed, "left", "搬入待機");
            var call = Panel("CallButton");
            Button("Button_Call", call, Vector3.zero, new Vector3(0.16f, 0.16f, 0.03f), mats["button_green"], game, WadoGame.ActCall, 0, "搬入呼出");
            var sweep = Panel("SweepButton");
            Button("Button_Sweep", sweep, Vector3.zero, new Vector3(0.16f, 0.16f, 0.03f), mats["button_grey"], game, WadoGame.ActSweep, 0, "床清掃");

            // 発注端末
            var term = Panel("Terminal");
            var names = new TextMeshPro[WadoGame.UpgradeCount];
            var infos = new TextMeshPro[WadoGame.UpgradeCount];
            for (int i = 0; i < WadoGame.UpgradeCount; i++)
            {
                float y = -i * 0.175f;
                Button("Button_Order" + i, term, new Vector3(0.1f, y - 0.07f, 0), new Vector3(0.11f, 0.11f, 0.025f),
                    i >= WadoGame.UpGenFirst ? mats["button_amber"] : mats["button_green"], game, WadoGame.ActBuy, i, "発注");
                names[i] = Text("rowName" + i, term, new Vector3(0.24f, y, 0), 0.075f, 2.5f, fontDot, LedWhite, "left", "");
                infos[i] = Text("rowInfo" + i, term, new Vector3(0.24f, y - 0.092f, 0), 0.05f, 2.5f, fontDot, LedDim, "left", "");
            }
            game.rowName = names;
            game.rowInfo = infos;

            // 名簿
            var roster = Panel("Roster");
            game.txtRoster = Text("txtRoster", roster, Vector3.zero, 0.05f, 1.3f, fontGothic, Ink, "left", "");

            // 異動願
            var transfer = Panel("Transfer");
            game.txtTransfer = Text("txtTransfer", transfer, Vector3.zero, 0.036f, 1.12f, fontGothic, Ink, "left", "");
            Text("lblSubmit", transfer, new Vector3(0.3f, -0.72f, 0), 0.045f, 0.5f, fontGothic, Ink, "center", "提出");
            Button("Button_Transfer", transfer, new Vector3(0.3f, -0.9f, 0), new Vector3(0.14f, 0.14f, 0.03f), mats["button_grey"], game, WadoGame.ActTransfer, 0, "異動願 提出");
            var confirmHolder = new GameObject("TransferConfirm");
            confirmHolder.transform.SetParent(transfer, false);
            Text("lblConfirm", confirmHolder.transform, new Vector3(0.8f, -0.72f, 0), 0.045f, 0.5f, fontGothic, new Color(0.7f, 0.1f, 0.08f), "center", "確認");
            Button("Button_TransferConfirm", confirmHolder.transform, new Vector3(0.8f, -0.9f, 0), new Vector3(0.14f, 0.14f, 0.03f), mats["button_red"], game, WadoGame.ActTransferConfirm, 0, "確認");
            game.transferConfirmButton = confirmHolder;

            // 搬入予定表 (課長代理から見えるようになる)
            var sched = Panel("Schedule");
            sched.name = "ScheduleBoard";
            Cube("paper", sched, new Vector3(0.62f, -0.5f, 0.012f), new Vector3(1.36f, 1.1f, 0.02f), mats["paper"], false);
            Text("title", sched, new Vector3(0.03f, -0.03f, 0), 0.075f, 1.3f, fontGothic, Ink, "left", "搬入予定表");
            Text("sub", sched, new Vector3(0.03f, -0.14f, 0), 0.032f, 1.3f, fontGothic, new Color(0.35f, 0.35f, 0.35f), "left", "当処理室 在室者より順次");
            game.txtSchedule = Text("txtSchedule", sched, new Vector3(0.03f, -0.22f, 0), 0.042f, 1.3f, fontGothic, Ink, "left", "");
            game.scheduleBoard = sched.gameObject;

            // 前任者の走り書き
            string[] memo = {
                "刃は線に沿って。\n迷うと手が止まる。\n手が止まると鮮度が落ちる。\n　　　　　　――前の人",
                "補助員は申請すれば来る。\nどこから来るのかは\n聞かないほうがいい。",
                "搬入元の欄、\nたまに「社内」って出る。\n人事部って書いてあった。",
                "名簿、先週より\n一人減ってる。\n「異動した」って聞いた。",
                "鮮度「特」は褒められる。\n早く切るほど、\nまだあたたかい。",
                "異動した人から\n連絡が来たこと、ある？\nわたしは、ない。",
                "この扉、内側からは\n開かない。\n退勤って何だっけ。",
                "搬入予定表に\n自分の名前を見つけた。\n番号も合っている。\n次の人へ。\n刃は線に沿って。",
            };
            var memoObjs = new GameObject[memo.Length];
            for (int i = 0; i < memo.Length; i++)
            {
                var a = anchors["Memo" + i];
                var go = new GameObject("Memo" + i);
                go.transform.SetParent(Group("Memos"), false);
                go.transform.position = V(a.pos);
                go.transform.rotation = Quaternion.Euler(i == 0 ? 90f : 0f, a.yaw, i % 2 == 0 ? 3f : -4f);
                Cube("paper", go.transform, new Vector3(0.15f, -0.15f, 0.004f), new Vector3(0.32f, 0.32f, 0.004f), mats["note"], false);
                Text("text", go.transform, new Vector3(0.02f, -0.025f, 0), 0.024f, 0.29f, fontHand, new Color(0.08f, 0.08f, 0.2f), "left", memo[i]);
                memoObjs[i] = go;
            }
            game.memos = memoObjs;

            // 汚れ (処理数で増える)
            float ty = L.room.tableY, tz = L.room.tableZ;
            var stainDefs = new[] {
                new float[] { 0.2f, ty + 0.002f, tz, 0.9f, 0.6f, 20f, 0 },
                new float[] { 0.5f, 0.004f, tz - 0.75f, 1.2f, 0.9f, 60f, 1 },
                new float[] { -0.6f, ty + 0.003f, tz + 0.1f, 0.8f, 0.5f, 140f, 2 },
                new float[] { 2.3f, 0.005f, tz - 0.4f, 1.8f, 0.7f, 5f, 3 },
                new float[] { -1.0f, 0.006f, tz + 0.9f, 1.4f, 1.1f, 200f, 0 },
                new float[] { 3.3f, 0.007f, 0.1f, 1.2f, 1.0f, 80f, 2 },
                new float[] { -2.6f, 0.008f, tz - 0.7f, 2.4f, 0.8f, 185f, 3 },
                new float[] { 0.0f, 0.009f, -1.4f, 2.2f, 1.6f, 300f, 1 },
            };
            var stains = new GameObject[stainDefs.Length];
            for (int i = 0; i < stainDefs.Length; i++)
            {
                var d = stainDefs[i];
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Stain" + i;
                UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(Group("Stains"), false);
                q.transform.position = new Vector3(d[0], d[1], d[2]);
                q.transform.rotation = Quaternion.Euler(90f, d[5], 0);
                q.transform.localScale = new Vector3(d[3], d[4], 1f);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mats["stain" + (int)d[6]];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                stains[i] = q;
            }
            game.stains = stains;
        }

        // ---------------------------------------------------------------- 環境 / VRChat
        static void BuildEnvironment()
        {
            var sys = Group("System");

            // 空調と蛍光灯
            Audio("Amb_Cooler", sys, new Vector3(0, 2.6f, 0), false, 30f, Clip("amb_cooler"), 0.3f);
            Vector3 tube = propObjects.TryGetValue("Tube_Dock", out var tubeGo) ? tubeGo.transform.position : new Vector3(3.2f, 3.1f, -1.6f);
            Audio("Amb_Buzz", sys, tube, true, 6f, Clip("amb_buzz"), 0.5f);

            var ambGo = new GameObject("WadoAmbience");
            ambGo.transform.SetParent(sys, false);
            var amb = AddUdon<WadoAmbience>(ambGo);
            var dockLight = GameObject.Find("Light_Dock");
            if (dockLight != null) amb.flickerLight = dockLight.GetComponent<Light>();
            if (tubeGo != null)
            {
                foreach (var r in tubeGo.GetComponentsInChildren<Renderer>(true))
                {
                    r.gameObject.isStatic = false;
                    amb.flickerTube = r;
                }
            }
            amb.dripSource = Audio("Sfx_Drip", sys, new Vector3(0, 0.1f, 0), true, 10f);
            amb.clipDrip = Clip("drip");
            var drips = new List<Transform>();
            foreach (var p in new[] { new Vector3(-3.6f, 0.05f, 2.6f), new Vector3(2.9f, 0.05f, 2.3f), new Vector3(-0.4f, 0.05f, -0.1f), new Vector3(-4.2f, 0.9f, 0.9f) })
            {
                var t = new GameObject("DripPoint").transform;
                t.SetParent(ambGo.transform, false);
                t.position = p;
                drips.Add(t);
            }
            amb.dripPoints = drips.ToArray();
            EditorUtility.SetDirty(amb);

            // 描画設定: 外光なし・冷えた薄暗さ
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.2f, 0.215f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = new Color(0.035f, 0.045f, 0.05f);
            RenderSettings.reflectionIntensity = 0.4f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(sys, false);
            probeGo.transform.position = new Vector3(0, 1.6f, 0);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.size = new Vector3(L.room.w, L.room.h + 0.4f, L.room.d);
            probe.center = Vector3.zero;
            probe.resolution = 128;
            probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = Color.black;

            // カメラ (VRChat の参照カメラを兼ねる)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 1.6f, -2.2f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();

            // VRChat
            var world = new GameObject("VRCWorld");
            var spawn = AnchorObject("Spawn", world.transform);
            var desc = world.AddComponent<VRCSceneDescriptor>();
            desc.spawns = new[] { spawn };
            desc.ReferenceCamera = camGo;
            desc.RespawnHeightY = -10f;
            EditorUtility.SetDirty(desc);
        }

        // ---------------------------------------------------------------- 初回の案内
        [InitializeOnLoadMethod]
        static void OfferOnFirstLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool("WadoPTR.Offered", false)) return;
                SessionState.SetBool("WadoPTR.Offered", true);
                if (File.Exists(ScenePath)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
                if (EditorUtility.DisplayDialog("WadoPTR",
                    "処理室のシーンがまだありません。いま建てますか?\n(あとで メニュー WadoPTR > 処理室を建てる からも実行できます)", "建てる", "あとで"))
                {
                    Build();
                }
            };
        }
    }

    /// <summary>このフォルダの素材の読み込み設定。法線マップと、汚れ用の透過 PNG。</summary>
    public class WadoAssetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/WadoPTR/")) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            if (n.EndsWith("_nor"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (assetPath.Contains("/stains/"))
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
            }
            ti.maxTextureSize = 1024;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/WadoPTR/")) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = true;
        }
    }
}
