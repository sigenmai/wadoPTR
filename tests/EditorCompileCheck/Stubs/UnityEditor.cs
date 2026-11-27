// Hand-written stubs mirroring the Unity 2022.3 UnityEditor API surface used by Assets/HorseRace/Editor.
// Signatures follow the real API; bodies are placeholders (type-check only).
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    public sealed class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => null;
        public static Object LoadAssetAtPath(string assetPath, Type type) => null;
        public static Object[] LoadAllAssetsAtPath(string assetPath) => new Object[0];
        public static void CreateAsset(Object asset, string path) { }
        public static void AddObjectToAsset(Object objectToAdd, Object assetObject) { }
        public static bool DeleteAsset(string path) => true;
        public static bool IsValidFolder(string path) => true;
        public static string CreateFolder(string parentFolder, string newFolderName) => "";
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static void ImportAsset(string path) { }
        public static void ImportPackage(string packagePath, bool interactive) { }
        public static string[] FindAssets(string filter) => new string[0];
        public static string[] FindAssets(string filter, string[] searchInFolders) => new string[0];
        public static string GUIDToAssetPath(string guid) => "";
        public static string GetAssetPath(Object assetObject) => "";
        public static T GetBuiltinExtraResource<T>(string path) where T : Object => null;
    }

    public sealed class EditorUtility
    {
        public static void SetDirty(Object target) { }
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
        public static bool DisplayDialog(string title, string message, string ok) => true;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 0;
    }

    public class Undo
    {
        public static void RegisterCreatedObjectUndo(Object objectToUndo, string name) { }
        public static void DestroyObjectImmediate(Object objectToUndo) { }
    }

    public enum PrefabUnpackMode { OutermostRoot = 0, Completely = 1 }
    public enum InteractionMode { AutomatedAction = 0, UserAction = 1 }

    public sealed class PrefabUtility
    {
        public static Object InstantiatePrefab(Object assetComponentOrGameObject) => null;
        public static Object InstantiatePrefab(Object assetComponentOrGameObject, Transform parent) => null;
        public static void UnpackPrefabInstance(GameObject instanceRoot, PrefabUnpackMode unpackMode, InteractionMode action) { }
        public static void RecordPrefabInstancePropertyModifications(Object targetObject) { }
    }

    public class SerializedProperty
    {
        public int arraySize { get; set; }
        public string stringValue { get; set; }
        public SerializedProperty GetArrayElementAtIndex(int index) => this;
    }

    public class SerializedObject : IDisposable
    {
        public SerializedObject(Object obj) { }
        public SerializedProperty FindProperty(string propertyPath) => null;
        public bool ApplyModifiedProperties() => true;
        public void Update() { }
        public void Dispose() { }
    }

    public class MonoScript : TextAsset
    {
        public Type GetClass() => null;
        public static MonoScript FromMonoBehaviour(MonoBehaviour behaviour) => null;
    }

    public class AssetImporter : Object
    {
        public string assetPath => "";
        public static AssetImporter GetAtPath(string path) => null;
        public void SaveAndReimport() { }
    }

    public sealed class ModelImporterClipAnimation
    {
        public string name { get; set; }
        public bool loopTime { get; set; }
    }

    public class ModelImporter : AssetImporter
    {
        public ModelImporterClipAnimation[] clipAnimations { get; set; }
        public ModelImporterClipAnimation[] defaultClipAnimations => new ModelImporterClipAnimation[0];
    }
}

namespace UnityEngine
{
    public class TextAsset : Object
    {
        public string text => "";
    }
}

namespace UnityEditor.SceneManagement
{
    public sealed class EditorSceneManager
    {
        public static bool MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) => true;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene) => true;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) => true;
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default(UnityEngine.SceneManagement.Scene);
        public static UnityEngine.SceneManagement.Scene OpenScene(string scenePath, OpenSceneMode mode) => default(UnityEngine.SceneManagement.Scene);
    }
}

namespace UnityEditor.Animations
{
    public enum AnimatorConditionMode { If = 1, IfNot = 2, Greater = 3, Less = 4, Equals = 6, NotEqual = 7 }

    public class AnimatorTransitionBase : Object
    {
        public bool canTransitionToSelf { get; set; }
        public void AddCondition(AnimatorConditionMode mode, float threshold, string parameter) { }
    }

    public class AnimatorStateTransition : AnimatorTransitionBase
    {
        public bool hasFixedDuration { get; set; }
        public float duration { get; set; }
        public bool hasExitTime { get; set; }
        public float exitTime { get; set; }
        public float offset { get; set; }
    }

    public sealed class AnimatorState : Object
    {
        public Motion motion { get; set; }
        public AnimatorStateTransition AddTransition(AnimatorState destinationState) => null;
    }

    public sealed class AnimatorStateMachine : Object
    {
        public AnimatorState defaultState { get; set; }
        public AnimatorState AddState(string name, Vector3 position) => null;
        public AnimatorStateTransition AddAnyStateTransition(AnimatorState destinationState) => null;
    }

    public class AnimatorControllerLayer
    {
        public string name { get; set; }
        public AnimatorStateMachine stateMachine { get; set; }
    }

    public sealed class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorControllerLayer[] layers { get; set; }
        public static AnimatorController CreateAnimatorControllerAtPath(string path) => null;
        public void AddParameter(string name, AnimatorControllerParameterType type) { }
    }
}
