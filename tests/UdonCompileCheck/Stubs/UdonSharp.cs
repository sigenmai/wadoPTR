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
    public partial class UdonSyncedAttribute : Attribute
    {
        public UdonSyncedAttribute(UdonSyncMode networkSyncTypeIn = UdonSyncMode.None) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public partial class UdonBehaviourSyncModeAttribute : Attribute
    {
        public UdonBehaviourSyncModeAttribute(BehaviourSyncMode behaviourSyncMode = BehaviourSyncMode.Any) { }
    }

    public abstract partial class UdonSharpBehaviour : MonoBehaviour
    {
        public void RequestSerialization() { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName) { }
        // UdonSharp 1.2 / VRChat SDK 3.8+: network events with parameters (receiver must be [NetworkCallable]).
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName, object parameter0) { }
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName, object parameter0, object parameter1) { }
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName, object parameter0, object parameter1, object parameter2) { }
        public void SendCustomNetworkEvent(NetworkEventTarget target, string eventName, object parameter0, object parameter1, object parameter2, object parameter3) { }
        public void SendCustomEventDelayedSeconds(string eventName, float delaySeconds, EventTiming eventTiming = EventTiming.Update) { }
        public void SendCustomEventDelayedFrames(string eventName, int delayFrames, EventTiming eventTiming = EventTiming.Update) { }

        /// <summary>UdonSharp 1.1+ / SDK 3.1+: hides the interact prompt (UdonBehaviour.DisableInteractive).</summary>
        public bool DisableInteractive { get; set; }
        public virtual void Interact() { }
        public virtual void OnDeserialization() { }
        public virtual void OnPreSerialization() { }
        public virtual void OnPostSerialization(SerializationResult result) { }
        public virtual void OnOwnershipTransferred(VRCPlayerApi player) { }
        public virtual void OnPlayerJoined(VRCPlayerApi player) { }
        public virtual void OnPlayerLeft(VRCPlayerApi player) { }
        public virtual void OnPlayerTriggerEnter(VRCPlayerApi player) { }
        public virtual void OnPlayerTriggerExit(VRCPlayerApi player) { }
        public virtual void OnStationEntered(VRCPlayerApi player) { }
        // SDK 3.4+ avatar events, SDK 3.7+ persistence event
        public virtual void OnAvatarChanged(VRCPlayerApi player) { }
        public virtual void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters) { }
        public virtual void OnPlayerRestored(VRCPlayerApi player) { }
        public virtual void OnStationExited(VRCPlayerApi player) { }
        public virtual void OnPickup() { }
        public virtual void OnDrop() { }
        public virtual void OnPickupUseDown() { }
        public virtual void OnPickupUseUp() { }
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
