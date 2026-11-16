using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

/// <summary>
/// 原体の一部位、または取り出された内容物。
/// 台に付いている間は切取線を持ち、分離後は手で運べる部材になる。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoPart : UdonSharpBehaviour
{
    public const int Hidden = 0;
    public const int Attached = 1;
    public const int Loose = 2;
    public const int OnBelt = 3;

    public int kind;
    public float hpMax = 5f;
    public WadoGame game;
    public WadoBody owner;
    public Transform looseParent;
    public Transform cutPoint;
    public GameObject cutMark;
    public Transform gash;
    public GameObject visual;
    public Collider solid;
    public Rigidbody rb;
    public VRCPickup pickup;

    [HideInInspector] public int state;
    [HideInInspector] public bool cuttable;

    private float hp;
    private bool homeSaved;
    private Vector3 homePos;
    private Quaternion homeRot;
    private Vector3 gashScale;
    private const float BeltSpeed = 0.9f;

    private void SaveHome()
    {
        if (homeSaved) return;
        homeSaved = true;
        homePos = transform.localPosition;
        homeRot = transform.localRotation;
        if (Utilities.IsValid(gash)) gashScale = gash.localScale;
    }

    public void Hide()
    {
        SaveHome();
        state = Hidden;
        cuttable = false;
        if (Utilities.IsValid(pickup))
        {
            if (pickup.IsHeld) pickup.Drop();
            pickup.pickupable = false;
        }
        rb.isKinematic = true;
        solid.enabled = false;
        visual.SetActive(false);
        ShowMarks(false);
    }

    /// <summary>原体の定位置に付け直す(新しい原体の搬入)。</summary>
    public void Attach(Transform root)
    {
        SaveHome();
        state = Attached;
        hp = hpMax;
        rb.isKinematic = true;
        solid.enabled = false;
        pickup.pickupable = false;
        transform.SetParent(root);
        transform.localPosition = homePos;
        transform.localRotation = homeRot;
        visual.SetActive(true);
        if (Utilities.IsValid(gash)) gash.localScale = new Vector3(gashScale.x, gashScale.y, 0.001f);
    }

    public void SetCuttable(bool value)
    {
        cuttable = value && state == Attached;
        ShowMarks(cuttable);
    }

    private void ShowMarks(bool value)
    {
        if (Utilities.IsValid(cutMark)) cutMark.SetActive(value);
        if (Utilities.IsValid(gash)) gash.gameObject.SetActive(value);
    }

    /// <summary>刃が入った。切り離されたら true。</summary>
    public bool Damage(float amount)
    {
        if (state != Attached) return false;
        hp -= amount;
        float k = Mathf.Clamp01(1f - hp / hpMax);
        if (Utilities.IsValid(gash)) gash.localScale = new Vector3(gashScale.x, gashScale.y, Mathf.Max(0.001f, gashScale.z * k));
        return hp <= 0f;
    }

    /// <summary>台から切り離す。belt = 搬送ベルトが計量口まで運ぶ。</summary>
    public void Release(bool belt)
    {
        cuttable = false;
        ShowMarks(false);
        transform.SetParent(looseParent);
        solid.enabled = true;
        if (belt)
        {
            state = OnBelt;
            rb.isKinematic = true;
            pickup.pickupable = false;
        }
        else
        {
            state = Loose;
            rb.isKinematic = false;
            pickup.pickupable = true;
            rb.velocity = new Vector3(Random.Range(-0.2f, 0.2f), 0.7f, Random.Range(-0.5f, -0.2f));
            rb.angularVelocity = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
        }
    }

    /// <summary>内容物として取り出される。</summary>
    public void Spawn(Vector3 position, bool belt)
    {
        SaveHome();
        hp = 0f;
        transform.SetParent(looseParent);
        transform.position = position;
        visual.SetActive(true);
        Release(belt);
    }

    public void MoveTo(Vector3 position)
    {
        if (state != Loose) return;
        if (pickup.IsHeld) pickup.Drop();
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = position;
    }

    /// <summary>計量口に入った。</summary>
    public void Weigh()
    {
        if (state != Loose && state != OnBelt) return;
        float fresh = Utilities.IsValid(owner) ? owner.Freshness() : 1f;
        int k = kind;
        Hide();
        game.Deposit(k, fresh);
    }

    void Update()
    {
        if (state == OnBelt)
        {
            Vector3 goal = owner.chutePoint.position;
            transform.position = Vector3.MoveTowards(transform.position, goal, BeltSpeed * Time.deltaTime);
            if (Vector3.Distance(transform.position, goal) < 0.03f) Weigh();
        }
        else if (state == Loose)
        {
            // 部屋の外へ落ちたものは受け皿へ戻す
            if (transform.position.y < -3f) MoveTo(owner.trayPoint.position + Vector3.up * 0.3f);
        }
    }
}
