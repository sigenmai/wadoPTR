using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>計量口。入ってきた部材を量って出荷に回す。</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoChute : UdonSharpBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (!Utilities.IsValid(other)) return;
        WadoPart part = other.GetComponent<WadoPart>();
        if (!Utilities.IsValid(part)) return;
        if (part.state != WadoPart.Loose) return;
        part.Weigh();
    }
}
