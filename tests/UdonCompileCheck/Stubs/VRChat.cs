// Hand-written stubs mirroring the VRChat SDK3 (3.5+) API surface used by Assets/HorseRace/Scripts.
// Type-check only; bodies are placeholders.
using UnityEngine;

namespace VRC.SDKBase
{
    public class VRCPlayerApi
    {
        public enum TrackingDataType { Head = 0, LeftHand = 1, RightHand = 2, Origin = 3, AvatarRoot = 4 }

        public struct TrackingData
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
        public bool IsValid() => true;
        public bool IsUserInVR() => false;
        public TrackingData GetTrackingData(TrackingDataType tt) => default;
        public Vector3 GetPosition() => Vector3.zero;
        public Quaternion GetRotation() => Quaternion.identity;
        public void TeleportTo(Vector3 teleportPos, Quaternion teleportRot) { }
        public void PlayHapticEventInHand(VRC_Pickup.PickupHand hand, float duration, float amplitude, float frequency) { }
    }

    public static class Networking
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

    public class VRC_Pickup : MonoBehaviour
    {
        public enum PickupHand { None = 0, Left = 1, Right = 2 }
    }

    /// <summary>Mirrors VRC.SDKBase.VRCStation (abstract base of VRC.SDK3.Components.VRCStation).</summary>
    public abstract class VRCStation : MonoBehaviour
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
    public class VRCStation : VRC.SDKBase.VRCStation
    {
        public override void UseStation(VRC.SDKBase.VRCPlayerApi player) { }
        public override void ExitStation(VRC.SDKBase.VRCPlayerApi player) { }
    }
}

namespace VRC.Udon.Common
{
    public enum UdonInputEventType { BUTTON = 0, AXIS = 1 }
    public enum HandType { RIGHT = 0, LEFT = 1 }

    public struct UdonInputEventArgs
    {
        public UdonInputEventType eventType;
        public bool boolValue;
        public float floatValue;
        public HandType handType;
    }


    public struct SerializationResult
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
    public enum NetworkEventTarget { All = 0, Owner = 1 }
}
