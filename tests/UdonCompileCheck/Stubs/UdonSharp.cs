// Hand-written stubs mirroring the UdonSharp 1.1 API surface used by Assets/HorseRace/Scripts.
using System;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Enums;
using VRC.Udon.Common.Interfaces;

namespace UdonSharp
{
    public enum BehaviourSyncMode { Any, None, Continuous, Manual, NoVariableSync }

    public enum UdonSyncMode { NotSynced, None, Linear, Smooth }

    [AttributeUsage(AttributeTargets.Field)]
    public class UdonSyncedAttribute : Attribute
    {
        public UdonSyncedAttribute(UdonSyncMode networkSyncTypeIn = UdonSyncMode.None) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UdonBehaviourSyncModeAttribute : Attribute
    {
        public UdonBehaviourSyncModeAttribute(BehaviourSyncMode behaviourSyncMode = BehaviourSyncMode.Any) { }
    }

    public abstract class UdonSharpBehaviour : MonoBehaviour
    {
        public void RequestSerialization() { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName) { }
        public void SendCustomEventDelayedSeconds(string eventName, float delaySeconds, EventTiming eventTiming = EventTiming.Update) { }
        public void SendCustomEventDelayedFrames(string eventName, int delayFrames, EventTiming eventTiming = EventTiming.Update) { }

        public virtual void Interact() { }
        public virtual void OnDeserialization() { }
        public virtual void OnPreSerialization() { }
        public virtual void OnPostSerialization(SerializationResult result) { }
        public virtual void OnOwnershipTransferred(VRCPlayerApi player) { }
        public virtual void OnPlayerJoined(VRCPlayerApi player) { }
        public virtual void OnPlayerLeft(VRCPlayerApi player) { }
        public virtual void OnStationEntered(VRCPlayerApi player) { }
        public virtual void OnStationExited(VRCPlayerApi player) { }
        public virtual void InputJump(bool value, UdonInputEventArgs args) { }
        public virtual void InputUse(bool value, UdonInputEventArgs args) { }
        public virtual void InputGrab(bool value, UdonInputEventArgs args) { }
        public virtual void InputDrop(bool value, UdonInputEventArgs args) { }
        public virtual void InputMoveHorizontal(float value, UdonInputEventArgs args) { }
        public virtual void InputMoveVertical(float value, UdonInputEventArgs args) { }
        public virtual void InputLookHorizontal(float value, UdonInputEventArgs args) { }
        public virtual void InputLookVertical(float value, UdonInputEventArgs args) { }
    }
}
