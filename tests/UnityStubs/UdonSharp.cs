using System;

namespace UdonSharp
{
    public class UdonSharpBehaviour { }

    public enum BehaviourSyncMode { Any, Continuous, Manual, None, NoVariableSync }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class UdonSyncedAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class UdonBehaviourSyncModeAttribute : Attribute
    {
        public UdonBehaviourSyncModeAttribute(BehaviourSyncMode mode) { }
    }
}
