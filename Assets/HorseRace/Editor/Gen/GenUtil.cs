using System.Collections.Generic;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// Build World の共通処理：素材の読み込み・部品の取り出し・トゥーンマテリアル・プリミティブ・UdonSharp コンポーネント。
    /// 素材が見つからないときはプリミティブで代用し、警告を Missing に記録する（Validate が一覧にする）。
    /// </summary>
    public static class GenUtil
    {
        public const string ROOT_NAME = "HorseRace_Generated";
        public const string MODELS = "Assets/HorseRace/Models";
        public const string GENERATED = "Assets/HorseRace/Generated";
        public const string GEN_MATERIALS = GENERATED + "/Materials";
        public const string GEN_TERRAIN = GENERATED + "/Terrain";
        public const string TOON_SHADER = "HorseRace/Toon";
        public const float FLATTEN = 0.7f;

        // ---- 素材のパス ----
        public const string HORSE_FBX = MODELS + "/Horse.fbx";
        public const string CAMEL_FBX = MODELS + "/Camel.fbx";
        public const string SKELETON_FBX = MODELS + "/SkeletonHorse.fbx";
        public static string Pack(string name) => MODELS + "/Packs/" + name + "/" + name + ".fbx";
        public static string Prop(string name) => MODELS + "/Props/" + name + "/" + name + ".fbx";
        public static string Third(string folder, string name) => MODELS + "/ThirdParty/" + folder + "/" + name + ".fbx";
        public static string Building(string name) => MODELS + "/Buildings/" + name + ".fbx";
        public const string WANTED_CARDS = MODELS + "/Packs/WantedPosters/Cards";
        public const string WANTED_PAPER = MODELS + "/Packs/WantedPosters/WantedPosters.fbm/WantedTexture.png";

        /// <summary>見つからなかった素材など（重複なし）。</summary>
        public static readonly List<string> Missing = new List<string>();
        /// <summary>このビルドで作った UdonSharpBehaviour（最後に CopyProxyToUdon する）。</summary>
        public static readonly List<UdonSharpBehaviour> Behaviours = new List<UdonSharpBehaviour>();

        static readonly Dictionary<string, Material> toonCache = new Dictionary<string, Material>();
        static Shader toonShader;

        public static void Reset()
        {
            Missing.Clear();
            Behaviours.Clear();
            toonCache.Clear();
            toonShader = null;
        }

        /// <summary>負の数でも 0..n-1 に入る剰余。</summary>
        public static int Mod(int a, int n) => n <= 0 ? 0 : ((a % n) + n) % n;

        public static void Warn(string msg)
        {
            if (!Missing.Contains(msg)) Missing.Add(msg);
            Debug.LogWarning("[HorseRace] " + msg);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------
        // GameObject
        // ------------------------------------------------------------------

        public static GameObject Child(Transform parent, string name, Vector3 localPos, float yawDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            return go;
        }

        public static GameObject World(Transform parent, string name, Vector3 worldPos, float yawDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            return go;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>プリミティブ（トゥーン・単色）。keepCollider=false なら Collider を消す。</summary>
        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale,
            Color color, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = ToonColor(color);
            return go;
        }

        public static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ------------------------------------------------------------------
        // 素材
        // ------------------------------------------------------------------

        public static GameObject LoadModel(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Warn("素材が見つかりません: " + path);
            return go;
        }

        /// <summary>モデル全体を置く（プレハブ接続は外す）。マテリアルはトゥーンに差し替え。無ければ null。</summary>
        public static GameObject InstantiateModel(string path, Transform parent, string name)
        {
            var prefab = LoadModel(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            Toonify(go);
            return go;
        }

        /// <summary>
        /// パック（複数の部品 &lt;Pack&gt;_NN が並んだ FBX）から部品 1 つを取り出す。部品の原点は底の中心。
        /// 戻り値は「holder（位置・向き・拡大）→ 部品」。部品が無ければプリミティブ（fallbackSize の箱）で代用。
        /// </summary>
        public static GameObject InstantiatePart(string packPath, string partName, Transform parent, Vector3 worldPos,
            float yawDeg, float scale, Vector3 fallbackSize, Color fallbackColor)
        {
            var holder = World(parent, partName, worldPos, yawDeg);
            holder.transform.localScale = Vector3.one * scale;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(packPath);
            Transform part = null;
            GameObject inst = null;
            if (prefab != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                part = FindDeep(inst.transform, partName);
            }
            if (part == null)
            {
                if (inst != null) Object.DestroyImmediate(inst);
                Warn(prefab == null ? "素材が見つかりません: " + packPath : "部品が見つかりません: " + packPath + " / " + partName);
                Prim(PrimitiveType.Cube, partName + "_Fallback", holder.transform,
                    new Vector3(0f, fallbackSize.y * 0.5f, 0f), fallbackSize, fallbackColor);
                return holder;
            }
            // FBX のルートの回転・拡大（軸変換）を含めた部品のワールドの向き・大きさを保ったまま holder の下へ移す
            inst.transform.position = Vector3.zero;
            Quaternion wr = part.rotation;
            Vector3 ws = part.lossyScale;
            part.SetParent(holder.transform, false);
            part.localPosition = Vector3.zero;
            part.localRotation = wr;
            part.localScale = ws;
            Object.DestroyImmediate(inst);
            Toonify(holder);
            return holder;
        }

        /// <summary>FBX 全体（1 つのモデル）を holder の子に置く。無ければプリミティブ。</summary>
        public static GameObject PlaceModel(string path, Transform parent, string name, Vector3 worldPos, float yawDeg,
            float scale, Vector3 fallbackSize, Color fallbackColor)
        {
            var holder = World(parent, name, worldPos, yawDeg);
            holder.transform.localScale = Vector3.one * scale;
            var m = InstantiateModel(path, holder.transform, name + "_Model");
            if (m == null)
                Prim(PrimitiveType.Cube, name + "_Fallback", holder.transform, new Vector3(0f, fallbackSize.y * 0.5f, 0f),
                    fallbackSize, fallbackColor);
            return holder;
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>名前に part を含む最初の子孫（大文字小文字を区別しない）。</summary>
        public static Transform FindContains(Transform t, string part)
        {
            if (t.name.ToLowerInvariant().Contains(part.ToLowerInvariant())) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindContains(t.GetChild(i), part);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>メッシュの最初の法線（ワールド）。平らな板（掲示板のパネル・手配書）の向きを知るのに使う。</summary>
        public static Vector3 FirstNormalWorld(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return t.forward;
            var n = mf.sharedMesh.normals;
            if (n == null || n.Length == 0) return t.forward;
            return t.TransformDirection(n[0]).normalized;
        }

        /// <summary>root を Y 軸まわりに回して、ワールド方向 current（の水平成分）を desired に向ける。</summary>
        public static void YawAlign(Transform root, Vector3 current, Vector3 desired)
        {
            current.y = 0f; desired.y = 0f;
            if (current.sqrMagnitude < 1e-6f || desired.sqrMagnitude < 1e-6f) return;
            float a = WorldLayout.YawDeg(desired.normalized) - WorldLayout.YawDeg(current.normalized);
            root.rotation = Quaternion.Euler(0f, a, 0f) * root.rotation;
        }

        // ------------------------------------------------------------------
        // マテリアル（すべて HorseRace/Toon、_Flatten 0.7）
        // ------------------------------------------------------------------

        static Shader Toon()
        {
            if (toonShader == null) toonShader = Shader.Find(TOON_SHADER);
            if (toonShader == null) Warn("シェーダー " + TOON_SHADER + " が見つかりません");
            return toonShader;
        }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').Replace('#', '_').Replace('|', '_');
        }

        static Material SaveOrUpdate(string key, Texture tex, Color tint)
        {
            Material cached;
            if (toonCache.TryGetValue(key, out cached)) return cached;
            EnsureFolder(GEN_MATERIALS);
            string path = GEN_MATERIALS + "/" + Safe(key) + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Toon();
            if (mat == null)
            {
                mat = new Material(sh != null ? sh : Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (sh != null) mat.shader = sh;
            mat.SetTexture("_MainTex", tex);
            mat.SetColor("_Tint", tint);
            mat.SetFloat("_Flatten", FLATTEN);
            mat.enableInstancing = true;   // 同じ木・岩が多いので GPU インスタンシング（静的バッチングされないものに効く）
            EditorUtility.SetDirty(mat);
            toonCache[key] = mat;
            return mat;
        }

        public static Material ToonColor(Color c)
        {
            string key = "Toon_Color_" + ColorUtility.ToHtmlStringRGB(c);
            return SaveOrUpdate(key, null, c);
        }

        public static Material ToonTexture(Texture tex, string key)
        {
            return SaveOrUpdate("Toon_" + key, tex, Color.white);
        }

        /// <summary>モデル内のすべての Renderer のマテリアルを、元のテクスチャを引き継いだトゥーンに差し替える。</summary>
        public static void Toonify(GameObject root, Texture overrideTex = null)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Material s = src[i];
                    if (s != null && s.shader != null && s.shader.name == TOON_SHADER && overrideTex == null) { dst[i] = s; continue; }
                    Texture tex = overrideTex != null ? overrideTex : MainTextureOf(s, r);
                    string key = (s != null ? s.name : "none") + "_" + (tex != null ? tex.name : "notex");
                    dst[i] = SaveOrUpdate("Toon_" + key, tex, Color.white);
                }
                r.sharedMaterials = dst;
            }
        }

        static Texture MainTextureOf(Material m, Renderer r)
        {
            if (m != null)
            {
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return m.GetTexture("_MainTex");
                if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
                if (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) return m.GetTexture("_BaseColorMap");
            }
            // FBX のマテリアルにテクスチャが付いていない：同じフォルダにテクスチャが 1 枚だけならそれを使う
            var mf = r.GetComponent<MeshFilter>();
            Object meshAsset = mf != null ? (Object)mf.sharedMesh : null;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) meshAsset = smr.sharedMesh;
            if (meshAsset == null) return null;
            string p = AssetDatabase.GetAssetPath(meshAsset);
            if (string.IsNullOrEmpty(p)) return null;
            string dir = Path.GetDirectoryName(p).Replace('\\', '/');
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
            var top = new List<string>();
            foreach (string g in guids)
            {
                string tp = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(tp).Replace('\\', '/') == dir) top.Add(tp);
            }
            if (top.Count == 1) return AssetDatabase.LoadAssetAtPath<Texture2D>(top[0]);
            return null;
        }

        /// <summary>効果用（パーティクル）のマテリアル。トゥーン対象外（パーティクルはトゥーンで描けないため）。</summary>
        public static Material ParticleMaterial()
        {
            var m = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
            if (m == null) m = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            return m;
        }

        // ------------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------------

        public static ParticleSystem Particles(Transform parent, string name, Vector3 localPos, Color color, float size,
            float speed, float lifetime, bool loop, float rate, float shapeRadius)
        {
            var go = Child(parent, name, localPos);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = loop;
            main.loop = loop;
            main.duration = loop ? 1f : 2f;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = shapeRadius;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var mat = ParticleMaterial();
            if (pr != null && mat != null) pr.sharedMaterial = mat;
            return ps;
        }

        public static AudioSource Audio(GameObject go, bool is2D, bool loop = false)
        {
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = is2D ? 0f : 1f;
            return a;
        }

        public static TMPro.TextMeshPro Text(Transform parent, string name, Vector3 localPos, Quaternion localRot, string text,
            float fontSize, Color color, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TMPro.TextMeshPro>();
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            var font = Font();
            if (font != null) t.font = font;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = size;
            return t;
        }

        static TMPro.TMP_FontAsset font;
        static bool fontSearched;

        public const string FONT_TTF = "Assets/HorseRace/Fonts/ipag.ttf";   // IPA ゴシック（IPA フォントライセンス v1.0、同じフォルダに全文）
        public const string GEN_FONTS = GENERATED + "/Fonts";
        public const string GEN_FONT_ASSET = GEN_FONTS + "/JPGothic SDF.asset";   // IPA-1 3.1(4) に配慮し、生成物に「IPA」の名前を付けない

        /// <summary>
        /// 日本語の TMP フォント。Assets/HorseRace 以下に TMP_FontAsset があればそれ（Generated のものを含む）、
        /// 無ければ Fonts/ipag.ttf から TMP_FontAsset.CreateFontAsset で動的フォントアセット（必要な文字を実行時に追加）を作って
        /// Generated/Fonts に保存する。どちらも無ければ null（Validate が警告）。
        /// </summary>
        public static TMPro.TMP_FontAsset Font()
        {
            if (fontSearched) return font;
            fontSearched = true;
            string[] g = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/HorseRace" });
            if (g.Length > 0) font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g[0]));
            if (font == null) font = CreateFontAsset();
            if (font == null)
                Warn("日本語の TMP フォントがありません（" + FONT_TTF + " も見つかりません）。TMP の既定フォントでは日本語が□になります");
            return font;
        }

        /// <summary>読むだけの検索（アセットを作らない。Validate 用）：TMP_FontAsset があるか、無ければ元の ttf があるか。</summary>
        public static bool FontAvailable()
        {
            if (AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/HorseRace" }).Length > 0) return true;
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(FONT_TTF) != null;
        }

        static TMPro.TMP_FontAsset CreateFontAsset()
        {
            var ttf = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(FONT_TTF);
            if (ttf == null) return null;
            var fa = TMPro.TMP_FontAsset.CreateFontAsset(ttf);
            if (fa == null) { Warn("TMP_FontAsset.CreateFontAsset が失敗しました: " + FONT_TTF); return null; }
            EnsureFolder(GEN_FONTS);
            fa.name = "JPGothic SDF";
            AssetDatabase.CreateAsset(fa, GEN_FONT_ASSET);
            // アトラスとマテリアルはフォントアセットの中に保存する（別ファイルにしないと参照が切れる）
            if (fa.atlasTextures != null)
                foreach (var t in fa.atlasTextures) if (t != null) { t.name = "JPGothic Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
            if (fa.material != null) { fa.material.name = "JPGothic Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[HorseRace] 日本語フォントアセットを作りました（動的）: " + GEN_FONT_ASSET);
            return fa;
        }

        public static void ResetFontCache()
        {
            font = null;
            fontSearched = false;
        }

        // ------------------------------------------------------------------
        // UdonSharp
        // ------------------------------------------------------------------

        /// <summary>
        /// UdonSharpBehaviour を追加する（UdonSharp 1.1 の UdonSharpUndo.AddComponent：裏の UdonBehaviour も作られる）。
        /// フィールドはこの戻り値（プロキシ）に普通に代入し、ビルドの最後に CopyProxyToUdon でまとめて UdonBehaviour へ写す。
        /// </summary>
        public static T AddU<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T b = UdonSharpUndo.AddComponent<T>(go);
            if (b != null) Behaviours.Add(b);
            return b;
        }

        public static void SetInteractText(UdonSharpBehaviour b, string text)
        {
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (ub != null) ub.interactText = text;
        }

        public static void FlushBehaviours()
        {
            foreach (var b in Behaviours)
            {
                if (b == null) continue;
                UdonSharpEditorUtility.CopyProxyToUdon(b);
                EditorUtility.SetDirty(b);
                var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
                if (ub != null) EditorUtility.SetDirty(ub);
            }
        }
    }
}
