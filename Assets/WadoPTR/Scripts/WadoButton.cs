using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 押しボタン。発注端末・搬入呼出・床清掃・異動願などすべての「押す」操作はこれ。
/// デスクトップでも VR でも同じ Interact で動く。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoButton : UdonSharpBehaviour
{
    public WadoGame game;
    public int action;
    public int arg;
    public Transform cap;           // 押し込まれる部分
    public float travel = 0.012f;

    private float pressed;
    private Vector3 capHome;
    private bool capSaved;

    public override void Interact()
    {
        if (Utilities.IsValid(cap))
        {
            if (!capSaved)
            {
                capSaved = true;
                capHome = cap.localPosition;
            }
            pressed = 0.18f;
            cap.localPosition = capHome + Vector3.forward * travel;
        }
        if (Utilities.IsValid(game)) game.Press(action, arg);
    }

    void Update()
    {
        if (pressed <= 0f) return;
        pressed -= Time.deltaTime;
        if (pressed <= 0f && Utilities.IsValid(cap)) cap.localPosition = capHome;
    }
}
