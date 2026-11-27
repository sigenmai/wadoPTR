// Hand-written stubs mirroring the VRChat SDK3 (3.5+) API surface used by Assets/HorseRace/Scripts.
// Type-check only; bodies are placeholders.
using UnityEngine;

namespace VRC.SDKBase
{
    public partial class VRCPlayerApi
    {
        public enum TrackingDataType { Head = 0, LeftHand = 1, RightHand = 2, Origin = 3, AvatarRoot = 4 }

        public partial struct TrackingData
        {
            public Vector3 position;
            public Quaternion rotation;
            public TrackingData(Vector3 position, Quaternion rotation) { this.position = position; this.rotation = rotation; }
        }

        public bool isLocal;
        public string displayName;
        public bool isMaster => false;
        public int playerId => 0;

        public static bool IsValid(VRCPlayerApi player) => player != null;
        public static VRCPlayerApi GetPlayerById(int playerId) => null;
        public static int GetPlayerCount() => 0;
        public static VRCPlayerApi[] GetPlayers(VRCPlayerApi[] players) => players;
        public bool IsValid() => true;
        public bool IsUserInVR() => false;
        public TrackingData GetTrackingData(TrackingDataType tt) => default;
        public Vector3 GetPosition() => Vector3.zero;
        public Vector3 GetBonePosition(HumanBodyBones tt) => Vector3.zero;
        public Quaternion GetBoneRotation(HumanBodyBones tt) => Quaternion.identity;
        public float GetAvatarEyeHeightAsMeters() => 1.6f;
        public Quaternion GetRotation() => Quaternion.identity;
        public void TeleportTo(Vector3 teleportPos, Quaternion teleportRot) { }
        public void SetVoiceDistanceNear(float near) { }
        public void SetVoiceDistanceFar(float far) { }
        public void SetVoiceGain(float gain) { }
        public void SetVoiceLowpass(bool enabled) { }
        public void SetVoiceVolumetricRadius(float radius) { }
        public void PlayHapticEventInHand(VRC_Pickup.PickupHand hand, float duration, float amplitude, float frequency) { }
    }

    public static partial class Networking
    {
        public static VRCPlayerApi LocalPlayer => null;
        public static bool IsMaster => false;
        public static bool IsOwner(GameObject obj) => false;
        public static bool IsOwner(VRCPlayerApi player, GameObject obj) => false;
        public static VRCPlayerApi GetOwner(GameObject obj) => null;
        public static void SetOwner(VRCPlayerApi player, GameObject obj) { }
        public static double GetServerTimeInSeconds() => 0.0;
        public static int GetServerTimeInMilliseconds() => 0;
    }

    public partial class VRC_Pickup : MonoBehaviour
    {
        public enum PickupHand { None = 0, Left = 1, Right = 2 }
        public bool pickupable = true;
        public bool IsHeld => false;
        public PickupHand currentHand => PickupHand.None;
        public VRCPlayerApi currentPlayer => null;
        public void Drop() { }
    }

    /// <summary>Mirrors VRC.SDKBase.VRCStation (abstract base of VRC.SDK3.Components.VRCStation).</summary>
    public abstract partial class VRCStation : MonoBehaviour
    {
        public enum Mobility { Mobile = 0, Immobilize = 1, ImmobilizeForVehicle = 2 }
        public Mobility PlayerMobility;
        public bool canUseStationFromStation;
        public RuntimeAnimatorController animatorController;
        public bool disableStationExit;
        public bool seated;
        public Transform stationEnterPlayerLocation;
        public Transform stationExitPlayerLocation;
        public abstract void UseStation(VRCPlayerApi player);
        public abstract void ExitStation(VRCPlayerApi player);
    }
}

namespace VRC.SDK3.Components
{
    public partial class VRCStation : VRC.SDKBase.VRCStation
    {
        public override void UseStation(VRC.SDKBase.VRCPlayerApi player) { }
        public override void ExitStation(VRC.SDKBase.VRCPlayerApi player) { }
    }
}

namespace VRC.Udon.Common
{
    public enum UdonInputEventType { BUTTON = 0, AXIS = 1 }
    public enum HandType { RIGHT = 0, LEFT = 1 }

    public partial struct UdonInputEventArgs
    {
        public UdonInputEventType eventType;
        public bool boolValue;
        public float floatValue;
        public HandType handType;
    }


    public partial struct SerializationResult
    {
        public bool success;
        public int byteCount;
    }
}

namespace VRC.Udon.Common.Enums
{
    public enum EventTiming { Update = 0, LateUpdate = 1 }
}

namespace VRC.Udon.Common.Interfaces
{
    public enum NetworkEventTarget { All = 0, Owner = 1, Others = 2, Self = 3 }
}

namespace VRC.SDK3.UdonNetworkCalling
{
    /// <summary>Mirrors VRC.SDK3.UdonNetworkCalling.NetworkCallableAttribute (SDK 3.8+): marks a method callable by network events with parameters.</summary>
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public partial class NetworkCallableAttribute : System.Attribute
    {
        public int MaxEventsPerSecond { get; }
        public NetworkCallableAttribute() { MaxEventsPerSecond = 5; }
        public NetworkCallableAttribute(int maxEventsPerSecond) { MaxEventsPerSecond = maxEventsPerSecond; }
    }

    /// <summary>Mirrors VRC.SDK3.UdonNetworkCalling.NetworkCalling (SDK 3.8+).</summary>
    public static partial class NetworkCalling
    {
        public static VRC.SDKBase.VRCPlayerApi CallingPlayer => null;
        public static bool InNetworkCall => false;
    }
}

namespace VRC.SDK3.Persistence
{
    /// <summary>Mirrors VRC.SDK3.Persistence.PlayerData (SDK 3.7+). Setters write the local player's data; getters read any player's.</summary>
    public static partial class PlayerData
    {
        public static void SetFloat(string key, float value) { }
        public static float GetFloat(VRC.SDKBase.VRCPlayerApi player, string key) => 0f;
        public static bool TryGetFloat(VRC.SDKBase.VRCPlayerApi player, string key, out float value) { value = 0f; return false; }
        public static bool HasKey(VRC.SDKBase.VRCPlayerApi player, string key) => false;
    }
}

namespace VRC.SDKBase
{
    /// <summary>VRChat の VRCShader（グローバルのシェーダー変数。名前は _Udon で始まる必要がある）。</summary>
    public static partial class VRCShader
    {
        public static int PropertyToID(string name) => 0;
        public static void SetGlobalColor(int id, Color value) { }
        public static void SetGlobalFloat(int id, float value) { }
    }
}
