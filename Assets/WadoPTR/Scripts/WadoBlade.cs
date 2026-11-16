using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

/// <summary>
/// 刃物。
/// VR: 切取線に向けて実際に振る(刃の速度で判定)。引き金でも刃先の近くの線を打てる。
/// デスクトップ: 持った状態で切取線を見て「使用」(左クリック)。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoBlade : UdonSharpBehaviour
{
    public WadoGame game;
    public WadoBody body;
    public VRCPickup pickup;
    public Transform edge;              // 刃の中ほど
    public Transform restPoint;         // 刃物立て。手を離すと巻き取り索でここへ戻る
    public AudioSource sfx;
    public AudioClip[] clipChop;
    public AudioClip[] clipSever;
    public AudioClip[] clipSwing;
    public AudioClip clipCrack;
    public AudioClip clipTake;

    public float swingSpeed = 1.3f;     // これ以上の速さで線を通ると刃が入る (m/s)
    public float swingRadius = 0.16f;
    public float useRadiusVR = 0.24f;
    public float lookRadius = 0.22f;
    public float lookRange = 2.4f;

    private bool held;
    private bool inVR;
    private Vector3 lastPos;
    private float cooldown;
    private float returnDelay;

    public override void OnPickup()
    {
        held = true;
        VRCPlayerApi lp = Networking.LocalPlayer;
        inVR = Utilities.IsValid(lp) && lp.IsUserInVR();
        lastPos = edge.position;
        cooldown = 0.3f;
        Play(clipTake, 0.5f);
    }

    public override void OnDrop()
    {
        held = false;
        returnDelay = 0.7f;
    }

    public override void OnPickupUseDown()
    {
        if (!held) return;
        int target = -1;
        if (inVR)
        {
            target = body.FindCut(edge.position, useRadiusVR);
        }
        else
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp))
            {
                VRCPlayerApi.TrackingData head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                Vector3 dir = head.rotation * Vector3.forward;
                target = body.FindCutByRay(head.position, dir, lookRange, lookRadius);
            }
        }
        if (target >= 0 && cooldown <= 0f) Strike(target);
        else PlayRandom(clipSwing, 0.5f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (cooldown > 0f) cooldown -= dt;

        if (!held)
        {
            // 手を離れた刃物は刃物立てへ巻き取られる
            if (!Utilities.IsValid(restPoint)) return;
            if (returnDelay > 0f)
            {
                returnDelay -= dt;
                return;
            }
            Vector3 home = restPoint.position;
            if ((transform.position - home).sqrMagnitude > 0.000001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, home, 6f * dt);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, restPoint.rotation, 720f * dt);
            }
            return;
        }

        Vector3 p = edge.position;
        float speed = (p - lastPos).magnitude / Mathf.Max(dt, 0.001f);
        lastPos = p;
        if (inVR && cooldown <= 0f && speed >= swingSpeed)
        {
            int target = body.FindCut(p, swingRadius);
            if (target >= 0) Strike(target);
        }
    }

    private void Strike(int target)
    {
        cooldown = 0.22f;
        if (Utilities.IsValid(sfx)) sfx.transform.position = body.CutPosition(target);
        bool severed = body.Hit(target, game.BladeDamage());
        if (severed)
        {
            PlayRandom(clipSever, 0.9f);
            Play(clipCrack, 0.6f);
            Haptic(0.25f, 1.0f);
        }
        else
        {
            PlayRandom(clipChop, 0.8f);
            Haptic(0.08f, 0.7f);
        }
    }

    private void Haptic(float duration, float amplitude)
    {
        if (!inVR) return;
        VRCPlayerApi lp = Networking.LocalPlayer;
        if (!Utilities.IsValid(lp)) return;
        lp.PlayHapticEventInHand(pickup.currentHand, duration, amplitude, 120f);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (Utilities.IsValid(sfx) && Utilities.IsValid(clip)) sfx.PlayOneShot(clip, volume);
    }

    private void PlayRandom(AudioClip[] clips, float volume)
    {
        if (!Utilities.IsValid(clips) || clips.Length == 0) return;
        Play(clips[Random.Range(0, clips.Length)], volume);
    }
}
