// OneClickSetup.cs 用のスタブ（実際の Unity Editor API と同じ名前・引数）
using System;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static CallbackFunction update;
        public static bool isCompiling => false;
        public static bool isUpdating => false;
        public static bool isPlayingOrWillChangePlaymode => false;
        public static double timeSinceStartup => 0;
    }

    public static class SessionState
    {
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetInt(string key, int value) { }
        public static void EraseInt(string key) { }
    }

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool defaultValue) => defaultValue;
        public static void SetBool(string key, bool value) { }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }
    public enum OpenSceneMode { Single = 0, Additive = 1, AdditiveWithoutLoading = 2 }
}

namespace UnityEngine
{
    public static class Application
    {
        public static bool isBatchMode => false;
        public static string dataPath => "";
    }
}
