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

        /// <summary>このビルドで id を付けた飾り（手直しの対象）。id → GameObject。</summary>
        public static readonly Dictionary<string, GameObject> Tagged = new Dictionary<string, GameObject>();

        /// <summary>手直しできる飾りに安定した id を付ける（名前の末尾 " {id}"）。重複した id は "~2" などを足して警告。</summary>
        public static void Tag(GameObject go, string id)
        {
            string unique = LayoutOverrides.UniqueId(id, Tagged.Keys);
            if (unique != id) Debug.LogWarning("[HorseRace] 配置の id が重複したので " + unique + " にしました");
            go.name = LayoutOverrides.NameWithId(go.name, unique);
            Tagged[unique] = go;
        }

        static readonly Dictionary<string, Material> toonCache = new Dictionary<string, Material>();
        static Shader toonShader;

        /// <summary>
        /// いま作っているステージ（0 荒野 / 1 砂漠 / 2 境目、-1 = ステージ外）。境目の飾りのマテリアルは影色を紫の夕景にした別のもの
        /// （キーの末尾 "_S3"、ArtPalette / LightingPlan.ShadowColorDusk）。DressingBuilder が飾りを置く間だけ設定する。
        /// </summary>
        public static int StageVariant = -1;

        public static void Reset()
        {
            Missing.Clear();
            Behaviours.Clear();
            Tagged.Clear();
            toonCache.Clear();
            toonShader = null;
            StageVariant = -1;
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
            Toonify(go, null, path.StartsWith(MODELS + "/") ? path.Substring(MODELS.Length + 1) : path, null);
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
            Toonify(holder, null, packPath.StartsWith(MODELS + "/") ? packPath.Substring(MODELS.Length + 1) : packPath, partName);
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
            => SaveOrUpdate(key, tex, tint, ArtPalette.DEFAULT_FLATTEN, ArtPalette.DEFAULT_FLATTEN_MIP, LightingPlan.ShadowColor);

        /// <summary>トゥーンのマテリアルを作る（同じキーはこのビルドで 1 つ）。影色・影の段の太陽・平らにする強さは毎回書き直す（古い .mat の値を残さない）。</summary>
        static Material SaveOrUpdate(string key, Texture tex, Color tint, float flatten, float flattenMip, Color shadow)
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
            mat.SetFloat("_Flatten", flatten);
            mat.SetFloat("_FlattenMip", flattenMip);
            mat.SetColor("_ShadowColor", shadow);
            mat.SetFloat("_ShadowLightFloor", LightingPlan.SHADOW_LIGHT_FLOOR);
            mat.enableInstancing = true;   // 同じ木・岩が多いので GPU インスタンシング（静的バッチングされないものに効く）
            EditorUtility.SetDirty(mat);
            toonCache[key] = mat;
            return mat;
        }

        /// <summary>
        /// art-r5-04：置いた物のトゥーンのマテリアルに色を掛けた複製を付ける（同じ岩でも段ごとに明るい縞・暗い縞）。
        /// key はキャッシュのキーに足す文字（同じ元マテリアル × 同じ色は 1 つ）。トゥーン以外のマテリアルはそのまま。
        /// </summary>
        public static void RetintToon(GameObject go, Color mul, string key)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Material m = src[i];
                    if (m == null || m.shader == null || m.shader.name != TOON_SHADER) { dst[i] = m; continue; }
                    Color t = ArtPalette.Multiply(m.GetColor("_Tint"), mul);
                    dst[i] = SaveOrUpdate(m.name + "_" + key + "_" + ColorUtility.ToHtmlStringRGB(mul), m.GetTexture("_MainTex"), t,
                        m.GetFloat("_Flatten"), m.GetFloat("_FlattenMip"), m.GetColor("_ShadowColor"));
                    if (m.HasProperty("_Brightness") && dst[i].HasProperty("_Brightness")) dst[i].SetFloat("_Brightness", m.GetFloat("_Brightness"));
                }
                r.sharedMaterials = dst;
            }
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

        /// <summary>画像付きのトゥーンに色を掛けたもの（クールダウン中の灰色の手配書など）。</summary>
        public static Material ToonTextureTint(Texture tex, string key, Color tint)
        {
            return SaveOrUpdate("Toon_" + key + "_" + ColorUtility.ToHtmlStringRGB(tint), tex, tint);
        }

        /// <summary>
        /// モデル内のすべての Renderer のマテリアルを、元のテクスチャを引き継いだトゥーンに差し替える。
        /// assetPath（Models からの相対）・partName を渡すと素材ごとの決まり（ArtPalette：大きな岩は細部を残す・岩の色を暖色へ・町の灰色/青/緑を寄せる）を当てる。
        /// 境目のステージを作っている間（StageVariant == 2）は影色を紫の夕景にした別のマテリアル。
        /// </summary>
        public static void Toonify(GameObject root, Texture overrideTex = null, string assetPath = null, string partName = null)
        {
            ArtPalette.Style baseStyle = ArtPalette.StyleFor(assetPath, partName);
            bool dusk = StageVariant == 2;
            Color shadow = dusk ? LightingPlan.ShadowColorDusk : LightingPlan.ShadowColor;
            bool town = assetPath != null && assetPath.Contains("WesternTown");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                // 町の模型全体を置いたとき（ロビー）：部品（WesternTown_NNN_種類）ごとに色の決まり（地面の板・岩山、art-r2-11）
                ArtPalette.Style style = baseStyle;
                string sidingName = partName ?? assetPath;   // art-r5-06：白い壁の板壁の色を選ぶ名前
                if (town && partName == null)
                {
                    string pn = TownPartOf(r.transform, root.transform);
                    if (pn != null) { style = ArtPalette.StyleFor(assetPath, pn, ArtPalette.PartKind(pn)); sidingName = pn; }
                }
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Material s = src[i];
                    if (s != null && s.shader != null && s.shader.name == TOON_SHADER && overrideTex == null) { dst[i] = s; continue; }
                    Texture tex = overrideTex != null ? overrideTex : MainTextureOf(s, r);
                    // 画像を使わず「色だけ」で塗られた素材が多い（町・小物のパック）。元の色を Tint に引き継ぐ
                    // （画像があるときは白のまま。Blender から来た FBX は画像付きでも灰色の基本色を持っていて、掛けると暗くなる）
                    // 画像付きの建物（Buildings/・RanchCompound）は青白い壁を古い木の色へ寄せる掛け色（art-r2-14）
                    Color tint = tex != null ? ArtPalette.TextureTint(assetPath) : ArtPalette.RemapBaseColor(assetPath, BaseColorOf(s), sidingName);
                    tint = ArtPalette.Multiply(tint, style.tint);
                    string key = (s != null ? s.name : "none") + "_" + (tex != null ? tex.name : "notex")
                        + "_" + ColorUtility.ToHtmlStringRGB(tint) + style.key + (dusk ? "_S3" : "");
                    dst[i] = SaveOrUpdate("Toon_" + key, tex, tint, style.flatten, style.flattenMip, shadow);
                    // 遺物の宝石は明るく（art-r3-04：拾える物は一番鮮やかに。トゥーンの _Brightness）
                    if (style.glow > 0f && dst[i] != null && dst[i].HasProperty("_Brightness")) dst[i].SetFloat("_Brightness", 1f + style.glow);
                }
                r.sharedMaterials = dst;
            }
        }

        /// <summary>t から root までの親で、名前が「WesternTown_番号_種類」の最初のもの（無ければ null）。</summary>
        static string TownPartOf(Transform t, Transform root)
        {
            for (Transform c = t; c != null; c = c.parent)
            {
                if (c.name.StartsWith("WesternTown_") && ArtPalette.PartKind(c.name) != null) return c.name;
                if (c == root) break;
            }
            return null;
        }

        /// <summary>元のマテリアルの基本色（Standard の _Color、URP の _BaseColor、HDRP の _BaseColor）。無ければ白。</summary>
        static Color BaseColorOf(Material m)
        {
            if (m == null) return Color.white;
            Color c = Color.white;
            if (m.HasProperty("_Color")) c = m.GetColor("_Color");
            else if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
            c.a = 1f;
            return c;
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

        /// <summary>立ちのぼる煙の柱（暗い灰色、上へ riseM m）。野営地の焚き火・煙突・遠くの目印。</summary>
        public static ParticleSystem SmokeColumn(Transform parent, string name, Vector3 localPos, Color color, float riseM, float width)
        {
            float life = 6f;
            var ps = Particles(parent, name, localPos, color, width, riseM / life, life, true, 6f, width * 0.3f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = width * 0.3f;
            sh.rotation = new Vector3(-90f, 0f, 0f);   // 円錐を上へ
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = 0.6f; vel.y = 0f; vel.z = 0.3f;   // 弱い風
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.sizeMultiplier = 3f;
            var main = ps.main;
            main.maxParticles = 80;
            return ps;
        }

        /// <summary>地面すれすれの砂ぼこり（box の中に薄く、風に流れる）。近景・中景の空気。</summary>
        public static ParticleSystem DustBox(Transform parent, string name, Vector3 localPos, Vector3 box, Color color, Vector3 wind, float rate)
        {
            var ps = Particles(parent, name, localPos, color, 2.5f, 0.2f, 7f, true, rate, 1f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = wind.x; vel.y = wind.y; vel.z = wind.z;
            var main = ps.main;
            main.maxParticles = 120;
            return ps;
        }

        /// <summary>点光源（ランプ・焚き火・見どころの色の合図）。影は落とさない（VRChat の負荷）。</summary>
        public static Light PointLight(Transform parent, string name, Vector3 worldPos, Color color, float range, float intensity)
        {
            var go = World(parent, name, worldPos);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>部品をいちばん長い辺が maxM になるよう等倍で縮める（宝石の印など）。holder の親は拡大していないこと。</summary>
        public static void FitMax(GameObject holder, float maxM)
        {
            Bounds b = RendererBounds(holder);
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (m > 1e-4f) holder.transform.localScale = holder.transform.localScale * (maxM / m);
        }

        /// <summary>部品を size（ワールドの幅・高さ・奥行き、holder の向きで）に引き伸ばす（原点は底のまま）。</summary>
        public static void FitSize(GameObject holder, Vector3 size)
        {
            Quaternion keep = holder.transform.rotation;
            holder.transform.rotation = Quaternion.identity;
            Bounds b = RendererBounds(holder);
            holder.transform.rotation = keep;
            Vector3 s = holder.transform.localScale;
            holder.transform.localScale = new Vector3(
                b.size.x > 1e-4f ? s.x * size.x / b.size.x : s.x,
                b.size.y > 1e-4f ? s.y * size.y / b.size.y : s.y,
                b.size.z > 1e-4f ? s.z * size.z / b.size.z : s.z);
        }

        /// <summary>見た目を消す（当たり判定は残す）。プリミティブの板を見えない Collider として使うとき。</summary>
        public static void HideRenderers(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        /// <summary>DesertProps の杭・柱（03 = 杭 3.4m、05 = 杭 3.1m、10 = 杭の列）を 1 本。</summary>
        /// <summary>
        /// 立てた丸太の杭（art-r3-02）：WoodLogs_01 を roll 90° で立て、高さ heightM・断面 thickM（CourseMarks.PostScale）。足元 = ground（ワールド）。
        /// 骨の杭（DesertProps_03/05 は大きな大腿骨）の代わりに、杭・柱・札の柱に使う。
        /// </summary>
        public static GameObject WoodPost(Transform parent, Vector3 ground, float yaw, float heightM, float thickM)
        {
            var log = InstantiatePart(Pack("WoodLogs"), "WoodLogs_01", parent, ground + Vector3.up * (heightM * 0.5f), yaw, 1f,
                new Vector3(thickM, thickM, heightM), new Color(0.30f, 0.20f, 0.12f));
            log.transform.rotation = Quaternion.Euler(0f, yaw, 90f);
            Vector3 sc = CourseMarks.PostScale(heightM, thickM);
            Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
            log.transform.localScale = new Vector3(sc.x / Mathf.Max(1e-4f, ps.x), sc.y / Mathf.Max(1e-4f, ps.y), sc.z / Mathf.Max(1e-4f, ps.z));
            return log;
        }

        public static GameObject Stake(Transform parent, string part, Vector3 worldPos, float yaw, float scale)
            => InstantiatePart(Pack("DesertProps"), part, parent, worldPos, yaw, scale, new Vector3(0.3f, 3f, 0.3f), new Color(0.45f, 0.30f, 0.18f));

        /// <summary>
        /// 手配書の紙（WantedPosters の部品）を 1 枚、紙の面を parent の -Z（parent.forward の反対）に向けて置く。
        /// widthM・heightM を渡すと、その大きさに引き伸ばす（間に向きのそろった Scaler を挟むので歪まない）。原点は紙の下の辺の真ん中。
        /// </summary>
        public static GameObject PaperSheet(Transform parent, string name, string part, Vector3 localPos, Texture tex, float widthM = 0f, float heightM = 0f)
        {
            var scaler = Child(parent, name, localPos);
            var holder = InstantiatePart(Pack("WantedPosters"), part, scaler.transform, scaler.transform.position, 0f, 1f,
                new Vector3(0.44f, 0.63f, 0.01f), new Color(0.93f, 0.86f, 0.70f));
            holder.name = "Sheet";
            holder.transform.SetParent(scaler.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            if (holder.transform.childCount > 0)
            {
                Transform p = holder.transform.GetChild(0);
                YawAlign(holder.transform, FirstNormalWorld(p), -scaler.transform.forward);
            }
            if (tex != null) Toonify(holder, tex);
            if (widthM > 0f && heightM > 0f)
            {
                // 外形は Scaler の向きで測る（親が回っていると世界の軸の外形が太るため、いったん Scaler を世界の軸にそろえる）
                Quaternion keep = scaler.transform.rotation;
                scaler.transform.rotation = Quaternion.identity;
                Bounds b = RendererBounds(holder);
                scaler.transform.rotation = keep;
                float w = Mathf.Max(0.05f, b.size.x), h = Mathf.Max(0.05f, b.size.y);
                scaler.transform.localScale = new Vector3(widthM / w, heightM / h, 1f);
            }
            return scaler;
        }

        public static AudioSource Audio(GameObject go, bool is2D, bool loop = false)
            => Audio(go, is2D, loop, null);

        /// <summary>音源（clip = SfxBuilder.Clip の仮の合成音など。ui-r2-11：clip の無い AudioSource は Validate が警告する）。</summary>
        public static AudioSource Audio(GameObject go, bool is2D, bool loop, AudioClip clip)
        {
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = is2D ? 0f : 1f;
            if (clip != null) a.clip = clip;
            return a;
        }

        public static TMPro.TextMeshPro Text(Transform parent, string name, Vector3 localPos, Quaternion localRot, string text,
            float fontSize, Color color, Vector2 size)
            => Text(parent, name, localPos, localRot, text, fontSize, color, size, TextStyle.Plain, false);

        /// <summary>
        /// 3D の文字（TextMeshPro）。style = 素材に合わせた文字の見た目（Ink = 紙のインク、Chalk = 黒板のチョーク、Sign = 看板・空の文字（縁取りと影）、
        /// Brass = 真鍮の札）。autoSize = true なら fontSize を上限に 45% まで縮めて箱に収め、それでも入らなければ「…」で切る（あふれさせない）。
        /// </summary>
        public static TMPro.TextMeshPro Text(Transform parent, string name, Vector3 localPos, Quaternion localRot, string text,
            float fontSize, Color color, Vector2 size, TextStyle style, bool autoSize)
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
            if (autoSize) AutoSize(t, fontSize);
            var mat = StyleMaterial(style);
            if (mat != null) t.fontSharedMaterial = mat;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = size;
            return t;
        }

        /// <summary>あふれ対策：上限 max、下限 max × UiStyle.AUTO_MIN、それでも入らなければ「…」。</summary>
        public static void AutoSize(TMPro.TMP_Text t, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = max * UiStyle.AUTO_MIN;
            t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        }

        static readonly Dictionary<TextStyle, Material> styleCache = new Dictionary<TextStyle, Material>();

        /// <summary>
        /// 文字の見た目のプリセット（Generated/Fonts/TMP_&lt;Style&gt;.mat、フォントのマテリアルを写して値を書く）。
        /// 面の色は白（文字の色は頂点色＝ UiStyle.FaceColor）、縁取り・太さ・やわらかさ・影は UiStyle の値。Plain はフォントのまま（null）。
        /// </summary>
        public static Material StyleMaterial(TextStyle style)
        {
            if (style == TextStyle.Plain) return null;
            Material cached;
            if (styleCache.TryGetValue(style, out cached) && cached != null) return cached;
            var f = Font();
            if (f == null || f.material == null) return null;
            EnsureFolder(GEN_FONTS);
            string path = GEN_FONTS + "/TMP_" + style + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(f.material);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.CopyPropertiesFromMaterial(f.material);
            m.name = "TMP_" + style;
            UiStyle.Preset p = UiStyle.PresetFor(style);
            m.SetColor("_FaceColor", Color.white);
            m.SetFloat("_FaceDilate", p.dilate);
            m.SetFloat("_OutlineSoftness", p.softness);
            m.SetFloat("_OutlineWidth", p.outlineWidth);
            m.SetColor("_OutlineColor", p.outlineColor);
            if (p.underlay)
            {
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.6f));
                m.SetFloat("_UnderlayOffsetX", p.underlayOffset.x);
                m.SetFloat("_UnderlayOffsetY", p.underlayOffset.y);
                m.SetFloat("_UnderlaySoftness", p.underlaySoftness);
            }
            else m.DisableKeyword("UNDERLAY_ON");
            if (p.outlineWidth > 0f) m.EnableKeyword("OUTLINE_ON");
            EditorUtility.SetDirty(m);
            styleCache[style] = m;
            return m;
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
            // 決まった 1 つを選ぶ（ui-r2-04）：IPA ゴシックから作った "JPGothic SDF.asset" を優先、無ければパスの名前順で最初
            string pick = null;
            foreach (string guid in g)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("JPGothic SDF.asset")) { pick = path; break; }
                if (pick == null || string.CompareOrdinal(path, pick) < 0) pick = path;
            }
            if (pick != null) font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(pick);
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
            styleCache.Clear();
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
