using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// 処理台と原体。搬入口からの搬入、切取線の管理、部材の分離、受け皿への回収を受け持つ。
/// parts[0..5] = 頭部, 上肢(左), 上肢(右), 下肢(左), 下肢(右), 体幹 / parts[6..9] = 内容物
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WadoBody : UdonSharpBehaviour
{
    public WadoGame game;
    public WadoPart[] parts;
    public Transform root;              // 原体のルート。parts[0..5] の定位置の親
    public Transform hatchPoint;        // 搬入口の内側
    public Transform tablePoint;        // 処理台の定位置
    public Transform trayPoint;         // 受け皿(散らかった部材の回収先)
    public Transform chutePoint;        // 計量口(ベルトの終点)
    public Transform hatchDoor;         // 搬入口の扉(上に開く)
    public ParticleSystem spray;
    public AudioSource sfx;
    public AudioClip clipMotor;
    public AudioClip clipHatch;
    public AudioClip clipCloth;
    public AudioClip clipChime;

    public const int BodyPartCount = 6;
    public const int Torso = 5;

    // 0 = 台は空 / 1 = 搬入中 / 2 = 処理中
    private int state;
    private float moveT;
    private float arrivedAt;
    private float doorOpen;
    private Vector3 doorClosedPos;
    private bool doorSaved;
    private int serial;
    private bool firstCallDone;

    private string[] origins;
    private string[] cls;

    void Start()
    {
        origins = new string[] { "提携医療法人 ■■会", "■■区 福祉課", "■■葬祭 委託分", "所有者不明", "社内 (人事部)", "■■物流 経由" };
        cls = new string[] { "成人・男", "成人・女", "成人・不詳" };
        SaveDoor();
        HideAll();
        if (Utilities.IsValid(game)) game.SetHatchText("搬入待機\n<size=60%>呼出ボタンを押してください</size>");
    }

    private void SaveDoor()
    {
        if (doorSaved) return;
        doorSaved = true;
        if (Utilities.IsValid(hatchDoor)) doorClosedPos = hatchDoor.localPosition;
    }

    private void HideAll()
    {
        if (!Utilities.IsValid(parts)) return;
        for (int i = 0; i < parts.Length; i++)
        {
            if (Utilities.IsValid(parts[i])) parts[i].Hide();
        }
    }

    public void OnGameRestored()
    {
        if (Utilities.IsValid(game)) serial = game.BodiesDone();
    }

    public void OnUpgradesChanged()
    {
        UpdateCuttable();
    }

    // ------------------------------------------------------------------ 搬入

    /// <summary>搬入呼出ボタン。</summary>
    public void Call()
    {
        if (state != 0)
        {
            game.Deny();
            return;
        }
        if (CountLoose() > 0)
        {
            game.Deny();
            game.Print("管理室: 未計量の部材があります。");
            return;
        }
        state = 1;
        moveT = 0f;
        serial++;
        firstCallDone = true;
        root.position = hatchPoint.position;
        root.rotation = hatchPoint.rotation;
        for (int i = 0; i < BodyPartCount; i++) parts[i].Attach(root);
        for (int i = BodyPartCount; i < parts.Length; i++) parts[i].Hide();
        UpdateCuttable();
        Play(clipMotor, 0.7f);
        Play(clipHatch, 0.6f);
        string label = "原体 No.7-" + (4100 + serial).ToString("00000") + "\n<size=60%>区分 " + cls[Random.Range(0, cls.Length)]
            + "\n搬入元 " + origins[Random.Range(0, origins.Length)] + "</size>";
        game.SetHatchText(label);
        game.Print("搬入 No.7-" + (4100 + serial).ToString("00000"));
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // 扉
        float target = state == 1 ? 1f : 0f;
        if (doorOpen != target && Utilities.IsValid(hatchDoor))
        {
            doorOpen = Mathf.MoveTowards(doorOpen, target, dt * 1.6f);
            hatchDoor.localPosition = doorClosedPos + Vector3.up * (0.62f * doorOpen);
        }

        if (state == 1)
        {
            moveT += dt / 3.2f;
            float k = Mathf.Clamp01(moveT);
            k = k * k * (3f - 2f * k);
            root.position = Vector3.Lerp(hatchPoint.position, tablePoint.position, k);
            if (moveT >= 1f)
            {
                state = 2;
                arrivedAt = Time.time;
                root.position = tablePoint.position;
                Play(clipCloth, 0.7f);
            }
        }
    }

    // ------------------------------------------------------------------ 切断

    private void UpdateCuttable()
    {
        if (!Utilities.IsValid(parts)) return;
        int attachedLimbs = 0;
        for (int i = 0; i < Torso; i++) if (parts[i].state == WadoPart.Attached) attachedLimbs++;
        for (int i = 0; i < Torso; i++) parts[i].SetCuttable(parts[i].state == WadoPart.Attached);
        // 体幹は四肢と頭部を外してから
        parts[Torso].SetCuttable(parts[Torso].state == WadoPart.Attached && attachedLimbs == 0);
    }

    /// <summary>点 p から半径 r 以内で最も近い切取線。無ければ -1。</summary>
    public int FindCut(Vector3 p, float r)
    {
        if (state != 2) return -1;
        int best = -1;
        float bestD = r;
        for (int i = 0; i < BodyPartCount; i++)
        {
            WadoPart part = parts[i];
            if (!part.cuttable) continue;
            float d = Vector3.Distance(p, part.cutPoint.position);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>視線(origin, dir)に最も近い切取線。デスクトップ操作用。</summary>
    public int FindCutByRay(Vector3 origin, Vector3 dir, float maxDist, float r)
    {
        if (state != 2) return -1;
        int best = -1;
        float bestD = r;
        for (int i = 0; i < BodyPartCount; i++)
        {
            WadoPart part = parts[i];
            if (!part.cuttable) continue;
            Vector3 v = part.cutPoint.position - origin;
            float t = Vector3.Dot(v, dir);
            if (t < 0f || t > maxDist) continue;
            float d = (v - dir * t).magnitude;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    public Vector3 CutPosition(int index)
    {
        return parts[index].cutPoint.position;
    }

    /// <summary>刃が入った。分離したら true。</summary>
    public bool Hit(int index, float damage)
    {
        if (state != 2) return false;
        WadoPart part = parts[index];
        if (!part.cuttable) return false;

        if (Utilities.IsValid(spray))
        {
            spray.transform.position = part.cutPoint.position;
            spray.Emit(index == Torso ? 26 : 14);
        }
        bool severed = part.Damage(damage);
        if (!severed) return false;

        bool belt = game.HasBelt();
        if (index == Torso)
        {
            // 体幹が開いた。許可があれば内容物を取り出す
            if (game.HasOrgans())
            {
                for (int i = BodyPartCount; i < parts.Length; i++)
                {
                    Vector3 p = part.transform.position + new Vector3((i - 7.5f) * 0.16f, 0.22f, 0.02f * (i % 2));
                    parts[i].Spawn(p, belt);
                }
            }
            part.Release(belt);
            state = 0;
            game.BodyFinished();
            game.SetHatchText("搬入待機\n<size=60%>次の原体を呼び出せます</size>");
        }
        else
        {
            part.Release(belt);
        }
        UpdateCuttable();
        return true;
    }

    // ------------------------------------------------------------------ 計量 / 回収

    /// <summary>搬入からの経過時間による鮮度係数 (1.5 → 1.0 → 0.8)。</summary>
    public float Freshness()
    {
        float window = game.FreshWindow();
        float t = Time.time - arrivedAt;
        if (t <= window) return Mathf.Lerp(1.5f, 1.0f, t / window);
        return Mathf.Lerp(1.0f, 0.8f, Mathf.Clamp01((t - window) / (window * 2f)));
    }

    private int CountLoose()
    {
        int n = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].state == WadoPart.Loose || parts[i].state == WadoPart.OnBelt) n++;
        }
        return n;
    }

    /// <summary>床清掃ボタン。散らかった部材を受け皿に戻す。</summary>
    public void Sweep()
    {
        int n = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].state == WadoPart.Loose)
            {
                parts[i].MoveTo(trayPoint.position + new Vector3(0f, 0.15f + 0.12f * n, 0f));
                n++;
            }
        }
        if (n > 0)
        {
            Play(clipCloth, 0.7f);
            game.Print("清掃: 部材 " + n.ToString() + " 点を受け皿へ");
        }
        else if (!firstCallDone)
        {
            game.Print("清掃: 異常なし");
        }
        else
        {
            game.Print("清掃: 床面 異常なし");
        }
    }

    private void Play(AudioClip clip, float volume)
    {
        if (Utilities.IsValid(sfx) && Utilities.IsValid(clip)) sfx.PlayOneShot(clip, volume);
    }
}
