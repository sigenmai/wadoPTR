// Hand-written stubs mirroring the Unity 2022.3 UnityEngine API surface used by Assets/HorseRace/Scripts.
// Signatures follow the real API; bodies are minimal and NOT behaviour-accurate (type-check only).
using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 back => new Vector3(0f, 0f, -1f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public float magnitude => Mathf.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && this == v;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * Dot(v, n);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);
        public Vector3 eulerAngles { get => Vector3.zero; set { } }
        public static Quaternion Euler(float x, float y, float z) => identity;
        public static Quaternion Euler(Vector3 euler) => identity;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => identity;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion Inverse(Quaternion q) => q;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 360f / (PI * 2f);
        public const float Deg2Rad = (PI * 2f) / 360f;
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpAngle(float a, float b, float t) => a + Repeat(b - a + 180f, 360f) - 180f;
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float MoveTowards(float current, float target, float maxDelta) =>
            Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
        public static float Exp(float p) => (float)Math.Exp(p);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float PerlinNoise(float x, float y) => 0.5f;
    }

    public struct LayerMask
    {
        private int m_Mask;
        public int value { get => m_Mask; set => m_Mask = value; }
        public static implicit operator int(LayerMask mask) => mask.m_Mask;
        public static implicit operator LayerMask(int intVal) { LayerMask m; m.m_Mask = intVal; return m; }
        public static int GetMask(params string[] layerNames) => 0;
        public static int NameToLayer(string layerName) => 0;
    }

    public enum QueryTriggerInteraction { UseGlobal = 0, Ignore = 1, Collide = 2 }

    public enum KeyCode { None = 0, Space = 32, E = 101, LeftShift = 304, RightShift = 303 }

    public class Object
    {
        public string name { get; set; }
        public static implicit operator bool(Object exists) => exists != null;
    }

    public class GameObject : Object
    {
        public Transform transform => null;
        public bool activeSelf => true;
        public bool activeInHierarchy => true;
        public int layer { get; set; }
        public void SetActive(bool value) { }
        public T GetComponent<T>() => default(T);
        public T GetComponentInParent<T>() => default(T);
        public T GetComponentInChildren<T>() => default(T);
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default(T);
        public T GetComponentInParent<T>() => default(T);
        public T GetComponentInParent<T>(bool includeInactive) => default(T);
        public T GetComponentInChildren<T>() => default(T);
        public T[] GetComponentsInChildren<T>() => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        public Transform parent { get; set; }
        public Vector3 InverseTransformPoint(Vector3 position) => position;
        public Vector3 TransformPoint(Vector3 position) => position;
        public Vector3 InverseTransformDirection(Vector3 direction) => direction;
        public Vector3 TransformDirection(Vector3 direction) => direction;
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => true;
    }

    public class MonoBehaviour : Behaviour { }

    public class Collider : Component
    {
        public bool enabled { get; set; }
        public bool isTrigger { get; set; }
        public Rigidbody attachedRigidbody => null;
    }

    public class Rigidbody : Component
    {
        public bool isKinematic { get; set; }
        public Vector3 velocity { get; set; }
    }

    public struct RaycastHit
    {
        public Vector3 point { get; set; }
        public Vector3 normal { get; set; }
        public float distance { get; set; }
        public Collider collider => null;
        public Transform transform => null;
    }

    public static class Physics
    {
        public static Vector3 gravity { get; set; }
        public static bool Raycast(Vector3 origin, Vector3 direction) => false;
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) => false;
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => false;
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => false;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo) { hitInfo = default; return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance) { hitInfo = default; return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) { hitInfo = default; return false; }
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) { hitInfo = default; return false; }
    }

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float time => 0f;
        public static float fixedDeltaTime => 0f;
        public static float timeSinceLevelLoad => 0f;
        public static int frameCount => 0;
    }

    public static class Input
    {
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKeyUp(KeyCode key) => false;
        public static bool GetButton(string buttonName) => false;
        public static bool GetButtonDown(string buttonName) => false;
        public static float GetAxis(string axisName) => 0f;
        public static float GetAxisRaw(string axisName) => 0f;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public class Material : Object { }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Material material { get; set; }
        public Material[] sharedMaterials { get; set; }
    }

    public class RuntimeAnimatorController : Object
    {
        public AnimationClip[] animationClips => null;
    }

    public class Motion : Object { }

    public class AnimationClip : Motion
    {
        public float length => 0f;
    }

    public class AnimatorOverrideController : RuntimeAnimatorController
    {
        public AnimatorOverrideController() { }
        public AnimatorOverrideController(RuntimeAnimatorController controller) { }
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }
        public AnimationClip this[string name] { get => null; set { } }
        public AnimationClip this[AnimationClip clip] { get => null; set { } }
        public int overridesCount => 0;
        public void GetOverrides(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>> overrides) { }
        public void ApplyOverrides(System.Collections.Generic.IList<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>> overrides) { }
    }

    public class Animator : Behaviour
    {
        public float speed { get; set; }
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }
        public void SetInteger(string name, int value) { }
        public void SetInteger(int id, int value) { }
        public void SetFloat(string name, float value) { }
        public void SetFloat(int id, float value) { }
        public void SetBool(string name, bool value) { }
        public void SetBool(int id, bool value) { }
        public void SetTrigger(string name) { }
        public void SetTrigger(int id) { }
        public void ResetTrigger(string name) { }
        public int GetInteger(string name) => 0;
        public static int StringToHash(string name) => 0;
    }

    public class AudioClip : Object { }

    public class AudioSource : Behaviour
    {
        public float pitch { get; set; }
        public float volume { get; set; }
        public bool loop { get; set; }
        public bool isPlaying => false;
        public AudioClip clip { get; set; }
        public void Play() { }
        public void Stop() { }
        public void PlayOneShot(AudioClip clip) { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
}
