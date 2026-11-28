// Hand-written stubs mirroring the UdonSharp 1.1 editor API (UdonSharpEditor namespace / UdonSharpProgramAsset)
// and the VRChat SDK3 components used by Assets/HorseRace/Editor. Type-check only.
using System;
using UnityEngine;

namespace UdonSharp
{
    /// <summary>Real: UdonSharp.UdonSharpProgramAsset : UdonAssemblyProgramAsset (a ScriptableObject); sourceCsScript is a public MonoScript field.</summary>
    public class UdonSharpProgramAsset : ScriptableObject
    {
        public UnityEditor.MonoScript sourceCsScript;
        public static void CompileAllCsPrograms(bool forceCompile = false, bool editorBuild = true) { }
        public static bool AnyUdonSharpScriptHasError() => false;
    }
}

namespace UdonSharp.Compiler
{
    /// <summary>Real (U# 1.1.9): UdonSharp.Compiler.UdonSharpCompileOptions / UdonSharpCompilerV1.Compile (async) and CompileSync.</summary>
    public class UdonSharpCompileOptions
    {
        public bool IsEditorBuild { get; set; } = true;
    }

    public class UdonSharpCompilerV1
    {
        public static void Compile(UdonSharpCompileOptions options = null) { }
        public static void CompileSync(UdonSharpCompileOptions options = null) { }
    }
}

namespace UdonSharpEditor
{
    /// <summary>Real: UdonSharpEditor.UdonSharpUndo (U# 1.x) — adds the proxy behaviour and its backing UdonBehaviour.</summary>
    public static class UdonSharpUndo
    {
        public static T AddComponent<T>(GameObject gameObject) where T : UdonSharp.UdonSharpBehaviour => default(T);
        public static UdonSharp.UdonSharpBehaviour AddComponent(GameObject gameObject, Type type) => null;
        public static void DestroyImmediate(UdonSharp.UdonSharpBehaviour behaviour) { }
    }

    public static class UdonSharpEditorUtility
    {
        public static void CopyProxyToUdon(UdonSharp.UdonSharpBehaviour behaviour) { }
        public static void CopyUdonToProxy(UdonSharp.UdonSharpBehaviour behaviour) { }
        public static VRC.Udon.UdonBehaviour GetBackingUdonBehaviour(UdonSharp.UdonSharpBehaviour behaviour) => null;
        public static UdonSharp.UdonSharpBehaviour GetProxyBehaviour(VRC.Udon.UdonBehaviour udonBehaviour) => null;
    }
}

namespace VRC.Udon
{
    public class UdonBehaviour : MonoBehaviour
    {
        public string interactText = "Use";
        public float proximity = 2f;
    }
}

namespace VRC.SDKBase
{
    public partial class VRC_Pickup
    {
        public enum AutoHoldMode { AutoDetect = 0, Yes = 1, No = 2 }
        public enum PickupOrientation { Any = 0, Grip = 1, Gun = 2 }
        public AutoHoldMode AutoHold;
        public PickupOrientation orientation;
        public Transform ExactGun;
        public Transform ExactGrip;
        public bool allowManipulationWhenEquipped;
        public string InteractionText;
        public string UseText;
        public float proximity;
    }

    public abstract class VRC_SceneDescriptor : MonoBehaviour
    {
        public Transform[] spawns;
        public float RespawnHeightY = -100f;
        public GameObject ReferenceCamera;
    }
}

namespace VRC.SDK3.Components
{
    public class VRCPickup : VRC.SDKBase.VRC_Pickup { }

    public class VRCSceneDescriptor : VRC.SDKBase.VRC_SceneDescriptor { }
}
