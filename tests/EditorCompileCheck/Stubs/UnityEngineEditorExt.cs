// Extensions of the runtime stubs (tests/UdonCompileCheck/Stubs) with the Unity 2022.3 UnityEngine API surface
// used by Assets/HorseRace/Editor. Signatures follow the real API; bodies are placeholders (type-check only).
using System;
using System.Collections;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public partial struct Color
    {
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);
        public static Color red => new Color(1f, 0f, 0f, 1f);
    }

    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color) => "";
        public static string ToHtmlStringRGBA(Color color) => "";
    }

    public struct Matrix4x4
    {
        public Vector3 MultiplyPoint(Vector3 point) => point;
        public Vector3 MultiplyPoint3x4(Vector3 point) => point;
        public Vector3 MultiplyVector(Vector3 vector) => vector;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
        public Vector3 extents { get => size * 0.5f; set => size = value * 2f; }
        public Vector3 min { get => center - extents; set { } }
        public Vector3 max { get => center + extents; set { } }
        public void Encapsulate(Bounds bounds) { }
        public void Encapsulate(Vector3 point) { }
        public bool Contains(Vector3 point) => false;
    }

    public enum PrimitiveType { Sphere = 0, Capsule = 1, Cylinder = 2, Cube = 3, Plane = 4, Quad = 5 }

    public partial class Object
    {
        public static void DestroyImmediate(Object obj) { }
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets) { }
        public static void Destroy(Object obj) { }
        public static T FindObjectOfType<T>() where T : Object => null;
        public static T[] FindObjectsOfType<T>() where T : Object => new T[0];
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object => new T[0];
        public static T Instantiate<T>(T original) where T : Object => original;
        public int GetInstanceID() => 0;
        public HideFlags hideFlags { get; set; }
    }

    [Flags]
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public partial class GameObject
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public bool isStatic { get; set; }
        public string tag { get; set; }
        public T AddComponent<T>() where T : Component => default(T);
        public Component AddComponent(Type componentType) => null;
        public T GetComponentInChildren<T>(bool includeInactive) => default(T);
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public T[] GetComponents<T>() => new T[0];
        public static GameObject CreatePrimitive(PrimitiveType type) => new GameObject();
        public static GameObject Find(string name) => null;
    }

    public partial class Component
    {
        public T GetComponentInChildren<T>(bool includeInactive) => default(T);
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public T[] GetComponents<T>() => new T[0];
        public string tag { get; set; }
    }

    public partial class Transform : IEnumerable
    {
        public int childCount => 0;
        public Transform GetChild(int index) => null;
        public Transform Find(string n) => null;
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public bool IsChildOf(Transform parent) => false;
        public Vector3 lossyScale => Vector3.one;
        public Matrix4x4 localToWorldMatrix => default(Matrix4x4);
        public Matrix4x4 worldToLocalMatrix => default(Matrix4x4);
        public Vector3 InverseTransformVector(Vector3 vector) => vector;
        public Vector3 TransformVector(Vector3 vector) => vector;
        public Transform root => this;
        public void SetSiblingIndex(int index) { }
        public IEnumerator GetEnumerator() { yield break; }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
    }

    public partial class Collider
    {
        public Bounds bounds => default(Bounds);
    }

    public class BoxCollider : Collider
    {
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public class SphereCollider : Collider
    {
        public Vector3 center { get; set; }
        public float radius { get; set; }
    }

    public class CapsuleCollider : Collider
    {
        public Vector3 center { get; set; }
        public float radius { get; set; }
        public float height { get; set; }
        public int direction { get; set; }
    }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
        public bool convex { get; set; }
    }

    public partial class Rigidbody
    {
        public bool useGravity { get; set; }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Vector2[] uv { get; set; }
        public int[] triangles { get; set; }
        public Bounds bounds { get; set; }
        public bool isReadable => true;
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }
    }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
        public Mesh mesh { get; set; }
    }

    public partial class Renderer
    {
        public Bounds bounds => default(Bounds);
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh { get; set; }
    }

    public partial class LineRenderer
    {
        public float startWidth { get; set; }
        public float endWidth { get; set; }
        public bool useWorldSpace { get; set; }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) => null;
    }

    public partial class Material
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Shader shader { get; set; }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
        public bool enableInstancing { get; set; }
        public bool HasProperty(string name) => false;
        public void SetTexture(string name, Texture value) { }
        public Texture GetTexture(string name) => null;
        public void SetFloat(string name, float value) { }
        public float GetFloat(string name) => 0f;
        public void SetColor(string name, Color value) { }
        public Color GetColor(string name) => default(Color);
    }

    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    public partial class Texture
    {
        public TextureWrapMode wrapMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels(Color[] colors) { }
        public void Apply() { }
    }

    public partial class RenderTexture
    {
        public int depth { get; set; }
        public int antiAliasing { get; set; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; }
        public void Release() { }
    }

    public partial class Camera
    {
        public int cullingMask { get; set; }
    }

    public sealed class AudioListener : Behaviour { }

    public partial class AudioSource
    {
        public bool playOnAwake { get; set; }
        public float spatialBlend { get; set; }
    }

    public enum AnimatorCullingMode { AlwaysAnimate = 0, CullUpdateTransforms = 1, CullCompletely = 2 }

    public enum AnimatorControllerParameterType { Float = 1, Int = 3, Bool = 4, Trigger = 9 }

    public partial class Animator
    {
        public bool applyRootMotion { get; set; }
        public AnimatorCullingMode cullingMode { get; set; }
    }

    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }

    public enum LightType { Spot = 0, Directional = 1, Point = 2 }
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public Color color { get; set; }
        public float intensity { get; set; }
        public LightShadows shadows { get; set; }
    }

    public sealed class RenderSettings : Object
    {
        public static Light sun { get; set; }
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static Color ambientSkyColor { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static bool fog { get; set; }
        public static FogMode fogMode { get; set; }
        public static Color fogColor { get; set; }
        public static float fogStartDistance { get; set; }
        public static float fogEndDistance { get; set; }
        public static float fogDensity { get; set; }
        public static Material skybox { get; set; }
    }

    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => default(T);
    }

    public partial struct LayerMask
    {
        public static string LayerToName(int layer) => "";
    }

    public static partial class Physics
    {
        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore) { }
        public static void IgnoreLayerCollision(int layer1, int layer2) { }
        public static void SyncTransforms() { }
    }

    // ---- ParticleSystem modules (real API: struct modules whose setters write through) ----
    public enum ParticleSystemSimulationSpace { Local = 0, World = 1, Custom = 2 }
    public enum ParticleSystemShapeType { Sphere = 0, Hemisphere = 2, Cone = 4, Box = 5, Circle = 10 }

    public partial class ParticleSystem
    {
        public struct MinMaxCurve
        {
            public MinMaxCurve(float constant) { }
            public static implicit operator MinMaxCurve(float constant) => new MinMaxCurve(constant);
        }

        public struct MinMaxGradient
        {
            public MinMaxGradient(Color color) { }
            public static implicit operator MinMaxGradient(Color color) => new MinMaxGradient(color);
        }

        public struct MainModule
        {
            public float duration { get; set; }
            public bool loop { get; set; }
            public bool playOnAwake { get; set; }
            public MinMaxCurve startLifetime { get; set; }
            public MinMaxCurve startSpeed { get; set; }
            public MinMaxCurve startSize { get; set; }
            public MinMaxGradient startColor { get; set; }
            public int maxParticles { get; set; }
            public ParticleSystemSimulationSpace simulationSpace { get; set; }
        }

        public struct EmissionModule
        {
            public bool enabled { get; set; }
            public MinMaxCurve rateOverTime { get; set; }
        }

        public struct ShapeModule
        {
            public bool enabled { get; set; }
            public ParticleSystemShapeType shapeType { get; set; }
            public float radius { get; set; }
        }

        public MainModule main => default(MainModule);
        public EmissionModule emission => default(EmissionModule);
        public ShapeModule shape => default(ShapeModule);
    }

    public sealed class ParticleSystemRenderer : Renderer { }

    // ---- Terrain ----
    public sealed class TerrainLayer : Object
    {
        public Texture2D diffuseTexture { get; set; }
        public Texture2D normalMapTexture { get; set; }
        public Vector2 tileSize { get; set; }
        public Vector2 tileOffset { get; set; }
    }

    public sealed class TerrainData : Object
    {
        public int heightmapResolution { get; set; }
        public Vector3 size { get; set; }
        public int alphamapResolution { get; set; }
        public int alphamapLayers => 0;
        public int baseMapResolution { get; set; }
        public TerrainLayer[] terrainLayers { get; set; }
        public float[,] GetHeights(int xBase, int yBase, int width, int height) => new float[height, width];
        public void SetHeights(int xBase, int yBase, float[,] heights) { }
        public float[,,] GetAlphamaps(int x, int y, int width, int height) => new float[height, width, 0];
        public void SetAlphamaps(int x, int y, float[,,] map) { }
        public float GetInterpolatedHeight(float x, float y) => 0f;
    }

    public sealed class Terrain : Behaviour
    {
        public TerrainData terrainData { get; set; }
        public float basemapDistance { get; set; }
        public float heightmapPixelError { get; set; }
        public bool drawInstanced { get; set; }
        public Material materialTemplate { get; set; }
        public float SampleHeight(Vector3 worldPosition) => 0f;
        public static GameObject CreateTerrainGameObject(TerrainData assignTerrain) => new GameObject();
    }

    public sealed class TerrainCollider : Collider
    {
        public TerrainData terrainData { get; set; }
    }

    public static partial class Mathf
    {
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float DeltaAngle(float current, float target) => target - current;
    }
}

namespace UnityEngine.Rendering
{
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name => "";
        public string path => "";
        public bool IsValid() => true;
        public GameObject[] GetRootGameObjects() => new GameObject[0];
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() => default(Scene);
    }
}

namespace UnityEngine.UI
{
    public partial class Image : MaskableGraphic
    {
        public Sprite sprite { get; set; }
    }
}

namespace UnityEngine
{
    public sealed class Sprite : Object { }

    public sealed class Font : Object { }
}
