using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace HorseRace
{
    /// <summary>
    /// 鞍の位置合わせ（乗り物 1 頭に 1 つ。MountController.seatFit から使う）。
    ///
    /// 1. 自動の位置合わせ：着席後 0.4 / 1.2 / 2.0 / 3.5 秒に自分のアバターの Hips（GetBonePosition）を SeatAnchor ローカルで測り、
    ///    腰が鞍の点（乗り物の種類ごとの saddleY / saddleZ。MountBuilder が SeatMath の値を入れる）に来るよう SeatAnchor をずらす。
    ///    アバターを替えた・大きさを変えた（OnAvatarChanged / OnAvatarEyeHeightChanged）ら測り直す。
    ///    Hips が取れない（ボーン無し）ときは目の高さ × SeatMath.FALLBACK_HIPS_PER_EYE。
    /// 2. 手での微調整：高さ・前後 1cm、傾き 1°（押し続けると連続）。乗り物の種類ごとに VRChat Persistence（PlayerData）へ保存し、次に来たときも同じ。
    ///    - 鞍の右前の真鍮の札（文字だけ・当たり判定は見えない箱。モデルは作らない）：VR は指先で触れる（押し続けで連続）、デスクトップ・VR のレーザーは Interact で 1 刻み
    ///    - デスクトップのキー：PageUp / PageDown ＝ 高さ、Home / End ＝ 前後、Insert / Delete ＝ 傾き（押し続けで連続）
    ///    札は座っている本人にだけ見え、レース中（走っている間）は隠れる（手綱の手で誤って触らない）。キーはいつでも効く。
    /// 計算した SeatAnchor の位置は MountController が syncSeat で他の人へ送る（他の人の画面の Station も同じ位置になる）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SeatFit : UdonSharpBehaviour
    {
        public const int BTN_HEIGHT_UP = 0;
        public const int BTN_HEIGHT_DOWN = 1;
        public const int BTN_FORWARD = 2;
        public const int BTN_BACK = 3;
        public const int BTN_TILT_FORWARD = 4;
        public const int BTN_TILT_BACK = 5;
        public const int BTN_RESET = 6;
        public const int BUTTONS = 7;

        public MountController mount;
        public Transform seatAnchor;

        [Header("鞍の点（乗り物ローカル、馬・ラクダ・骨の馬・ラプトルの順）")]
        public float[] saddleY;
        public float[] saddleZ;

        [Header("札（このオブジェクトの子。乗り物の子）")]
        public GameObject panelRoot;
        [Tooltip("鞍の点からの札の位置（右前・腿の上）")]
        public Vector3 panelOffset = new Vector3(0.28f, 0.10f, 0.32f);
        [Tooltip("ボタン（高さ＋, 高さ−, 前, 後, 前傾, 後傾, 元に戻す の順）")]
        public Transform[] buttons;
        public TextMeshPro readout;
        [Tooltip("ボタンの文字（buttons と同じ並び）。押すと 0.85 → 1 に 0.12 秒で戻る")]
        public Transform[] buttonLabels;
        [Tooltip("押したときのカチッという音（任意）")]
        public AudioSource clickAudio;
        public float pokeRadius = 0.04f;

        [Header("測定")]
        public float[] calibTimes = new float[] { 0.4f, 1.2f, 2.0f, 3.5f };
        [Tooltip("この速さ（m/s）以上では測らない")]
        public float calibMaxSpeed = 0.5f;

        // ---- 状態 ----
        private float[] user;        // SEAT_TYPES * AXES
        private bool restored;
        private float hipsY;
        private float hipsZ;
        private bool hasHips;
        private bool seatedLocal;
        private float seatedAt;
        private int calibIndex;
        private bool panelShown = true;
        private int shownType = -1;
        private bool dirty;
        private int dirtyType;
        private float saveAt;
        private int pokeHeld = -1;
        private float pokeSince;
        private int pokeFired;
        private int keyHeld = -1;
        private float keySince;
        private int keyFired;
        private bool started;
        private int flashButton = -1;
        private float flashAt;

        private void Start()
        {
            EnsureInit();
            SetPanel(false);
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            user = new float[SeatMath.SEAT_TYPES * SeatMath.AXES];
            if (saddleY == null || saddleY.Length < SeatMath.SEAT_TYPES)
            {
                saddleY = new float[SeatMath.SEAT_TYPES];
                for (int t = 0; t < SeatMath.SEAT_TYPES; t++) saddleY[t] = SeatMath.DefaultSaddleY(t);
            }
            if (saddleZ == null || saddleZ.Length < SeatMath.SEAT_TYPES)
            {
                saddleZ = new float[SeatMath.SEAT_TYPES];
                for (int t = 0; t < SeatMath.SEAT_TYPES; t++) saddleZ[t] = SeatMath.DefaultSaddleZ(t);
            }
            hipsY = SeatMath.FallbackHipsY(1.6f);
        }

        // =====================================================================
        // MountController から
        // =====================================================================

        /// <summary>今の乗り手（自分）の SeatAnchor の乗り物ローカルの高さ。</summary>
        public float AnchorY(int type)
        {
            EnsureInit();
            int t = ClampType(type);
            return SeatMath.AnchorY(saddleY[t], hipsY, user[t * SeatMath.AXES + SeatMath.AXIS_HEIGHT]);
        }

        public float AnchorZ(int type)
        {
            EnsureInit();
            int t = ClampType(type);
            return SeatMath.AnchorZ(saddleZ[t], hipsZ, user[t * SeatMath.AXES + SeatMath.AXIS_FORWARD]);
        }

        public float Tilt(int type)
        {
            EnsureInit();
            return user[ClampType(type) * SeatMath.AXES + SeatMath.AXIS_TILT];
        }

        /// <summary>乗り手がいない・まだ測っていないときの SeatAnchor（目の高さ 1.6m のアバターの目安）。</summary>
        public float DefaultAnchorY(int type)
        {
            EnsureInit();
            return SeatMath.AnchorY(saddleY[ClampType(type)], SeatMath.FallbackHipsY(1.6f), 0f);
        }

        public float DefaultAnchorZ(int type)
        {
            EnsureInit();
            return SeatMath.AnchorZ(saddleZ[ClampType(type)], 0f, 0f);
        }

        public void OnSeated()
        {
            EnsureInit();
            seatedLocal = true;
            StartCalibration();
            // 別の乗り物で変えた値も PlayerData（ローカルにすぐ反映される）から読み直す
            if (restored) Load();
        }

        public void OnUnseated()
        {
            seatedLocal = false;
            pokeHeld = -1;
            keyHeld = -1;
            SetPanel(false);
            if (dirty) Save();
        }

        private void StartCalibration()
        {
            seatedAt = Time.time;
            calibIndex = 0;
        }

        private int ClampType(int type)
        {
            if (type < 0 || type >= SeatMath.SEAT_TYPES) return SeatMath.SEAT_HORSE;
            return type;
        }

        // =====================================================================
        // VRChat のイベント
        // =====================================================================

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            restored = true;
            Load();
        }

        public override void OnAvatarChanged(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal || !seatedLocal) return;
            hasHips = false;
            StartCalibration();
        }

        public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters)
        {
            if (player == null || !player.isLocal || !seatedLocal) return;
            hasHips = false;
            StartCalibration();
        }

        // =====================================================================
        // 更新（自分が乗っているときだけ）
        // =====================================================================

        private void Update()
        {
            if (!started) EnsureInit();
            if (dirty && Time.time >= saveAt) Save();
            if (mount == null || !seatedLocal) return;
            if (!mount.IsLocalRider()) { OnUnseated(); return; }
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            // 走っている間は測らない（アバターが Station に 1 フレーム遅れて付いてくると、18 m/s で 30cm ずれて測れてしまう）。止まったら測る
            if (calibTimes != null && calibIndex < calibTimes.Length && Time.time - seatedAt >= calibTimes[calibIndex]
                && Mathf.Abs(mount.Speed) < calibMaxSpeed)
            {
                calibIndex++;
                MeasureHips(local);
            }

            int type = mount.SeatType();
            bool vr = local.IsUserInVR();
            bool show = !(mount.raceActive && !mount.InputLocked);
            SetPanel(show);
            if (show) PlacePanel(type);
            if (type != shownType) { shownType = type; UpdateReadout(); }

            if (show && vr) UpdatePoke(local, type);
            else pokeHeld = -1;
            if (!vr) UpdateKeys(type);
            AnimateFlash();
        }

        private void FlashButton(int button)
        {
            if (flashButton >= 0 && flashButton != button) SetLabelScale(flashButton, 1f);
            flashButton = button;
            flashAt = Time.time;
        }

        private void AnimateFlash()
        {
            if (flashButton < 0) return;
            float t = (Time.time - flashAt) / 0.12f;
            if (t >= 1f) { SetLabelScale(flashButton, 1f); flashButton = -1; return; }
            SetLabelScale(flashButton, Mathf.Lerp(0.85f, 1f, t));
        }

        private void SetLabelScale(int button, float s)
        {
            if (buttonLabels == null || button < 0 || button >= buttonLabels.Length || buttonLabels[button] == null) return;
            buttonLabels[button].localScale = new Vector3(s, s, s);
        }

        private void MeasureHips(VRCPlayerApi local)
        {
            if (seatAnchor == null) return;
            Vector3 w = local.GetBonePosition(HumanBodyBones.Hips);
            bool ok = w.sqrMagnitude > 0.000001f;
            float y = 0f;
            float z = 0f;
            if (ok)
            {
                Vector3 l = seatAnchor.InverseTransformPoint(w);
                y = l.y;
                z = l.z;
                ok = SeatMath.IsPlausibleHips(y, z);
            }
            if (!ok)
            {
                if (hasHips) return;   // 一瞬取れなかっただけ：前の値のまま
                hipsY = SeatMath.FallbackHipsY(local.GetAvatarEyeHeightAsMeters());
                hipsZ = 0f;
                return;
            }
            hipsY = SeatMath.BlendCalib(hipsY, y, hasHips);
            hipsZ = SeatMath.BlendCalib(hipsZ, z, hasHips);
            hasHips = true;
        }

        private void SetPanel(bool show)
        {
            if (show == panelShown) return;
            panelShown = show;
            if (panelRoot != null) panelRoot.SetActive(show);
        }

        private void PlacePanel(int type)
        {
            if (panelRoot == null) return;
            int t = ClampType(type);
            float h = user[t * SeatMath.AXES + SeatMath.AXIS_HEIGHT];
            float f = user[t * SeatMath.AXES + SeatMath.AXIS_FORWARD];
            panelRoot.transform.localPosition = new Vector3(panelOffset.x, saddleY[t] + h + panelOffset.y, saddleZ[t] + f + panelOffset.z);
        }

        // ---- VR：指先で触る ----
        private void UpdatePoke(VRCPlayerApi local, int type)
        {
            if (buttons == null) return;
            Vector3 l = Fingertip(local, HumanBodyBones.LeftIndexDistal, VRCPlayerApi.TrackingDataType.LeftHand);
            Vector3 r = Fingertip(local, HumanBodyBones.RightIndexDistal, VRCPlayerApi.TrackingDataType.RightHand);
            int best = -1;
            float bestD = pokeRadius;
            for (int i = 0; i < buttons.Length && i < BUTTONS; i++)
            {
                if (buttons[i] == null) continue;
                Vector3 p = buttons[i].position;
                float d = Mathf.Min(Vector3.Distance(p, l), Vector3.Distance(p, r));
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { pokeHeld = -1; return; }
            if (best != pokeHeld) { pokeHeld = best; pokeSince = Time.time; pokeFired = 0; }
            pokeFired = FireHeld(best, type, Time.time - pokeSince, pokeFired);
        }

        private Vector3 Fingertip(VRCPlayerApi local, HumanBodyBones bone, VRCPlayerApi.TrackingDataType hand)
        {
            Vector3 p = local.GetBonePosition(bone);
            if (p.sqrMagnitude > 0.000001f) return p;
            return local.GetTrackingData(hand).position;
        }

        // ---- デスクトップ：キー ----
        private void UpdateKeys(int type)
        {
            int k = -1;
            if (Input.GetKey(KeyCode.PageUp)) k = BTN_HEIGHT_UP;
            else if (Input.GetKey(KeyCode.PageDown)) k = BTN_HEIGHT_DOWN;
            else if (Input.GetKey(KeyCode.Home)) k = BTN_FORWARD;
            else if (Input.GetKey(KeyCode.End)) k = BTN_BACK;
            else if (Input.GetKey(KeyCode.Insert)) k = BTN_TILT_FORWARD;
            else if (Input.GetKey(KeyCode.Delete)) k = BTN_TILT_BACK;
            if (k < 0) { keyHeld = -1; return; }
            if (k != keyHeld) { keyHeld = k; keySince = Time.time; keyFired = 0; }
            keyFired = FireHeld(k, type, Time.time - keySince, keyFired);
        }

        /// <summary>押し続けた時間に応じて足りない回数だけ動かす。元に戻すは 1 回だけ。戻り値は動かした合計回数。</summary>
        private int FireHeld(int button, int type, float held, int fired)
        {
            int want = SeatMath.RepeatCount(held);
            if (button == BTN_RESET) want = Mathf.Min(want, 1);
            while (fired < want)
            {
                Apply(button, type);
                fired++;
            }
            return fired;
        }

        // =====================================================================
        // ボタン（SeatAdjustButton の Interact から。1 刻み）
        // =====================================================================

        public void Press(int button)
        {
            if (mount == null || !mount.IsLocalRider()) return;
            Apply(button, mount.SeatType());
        }

        private void Apply(int button, int type)
        {
            EnsureInit();
            int t = ClampType(type);
            int b = t * SeatMath.AXES;
            if (button == BTN_RESET)
            {
                user[b + SeatMath.AXIS_HEIGHT] = 0f;
                user[b + SeatMath.AXIS_FORWARD] = 0f;
                user[b + SeatMath.AXIS_TILT] = 0f;
                hasHips = false;
                StartCalibration();   // アバターの腰も測り直す
            }
            else
            {
                int axis = SeatMath.AXIS_HEIGHT;
                if (button == BTN_FORWARD || button == BTN_BACK) axis = SeatMath.AXIS_FORWARD;
                else if (button == BTN_TILT_FORWARD || button == BTN_TILT_BACK) axis = SeatMath.AXIS_TILT;
                int dir = (button == BTN_HEIGHT_UP || button == BTN_FORWARD || button == BTN_TILT_FORWARD) ? 1 : -1;
                user[b + axis] = SeatMath.StepAxis(axis, user[b + axis], dir);
            }
            if (clickAudio != null) clickAudio.Play();
            FlashButton(button);
            dirty = true;
            dirtyType = t;
            saveAt = Time.time + 1f;   // 押し終わって 1 秒後にまとめて保存（PlayerData の送信を減らす）
            UpdateReadout();
        }

        private void UpdateReadout()
        {
            if (readout == null || user == null) return;
            int t = ClampType(shownType);
            int b = t * SeatMath.AXES;
            readout.text = SeatMath.AxisLabel(SeatMath.AXIS_HEIGHT, user[b + SeatMath.AXIS_HEIGHT]) + "\n"
                + SeatMath.AxisLabel(SeatMath.AXIS_FORWARD, user[b + SeatMath.AXIS_FORWARD]) + "\n"
                + SeatMath.AxisLabel(SeatMath.AXIS_TILT, user[b + SeatMath.AXIS_TILT]);
        }

        // =====================================================================
        // 保存（VRChat Persistence の PlayerData。自分の分だけ書ける）
        // =====================================================================

        private void Load()
        {
            EnsureInit();
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            for (int t = 0; t < SeatMath.SEAT_TYPES; t++)
            {
                for (int a = 0; a < SeatMath.AXES; a++)
                {
                    float v;
                    if (PlayerData.TryGetFloat(local, SeatMath.PersistKey(t, a), out v))
                        user[t * SeatMath.AXES + a] = SeatMath.ClampAxis(a, v);
                }
            }
            UpdateReadout();
        }

        private void Save()
        {
            dirty = false;
            if (!restored) return;   // 復元前に書くと保存済みの値を消してしまう
            // 変えた種類の分だけ書く（他の乗り物の SeatFit が持つ古い値で上書きしない）
            int t = ClampType(dirtyType);
            for (int a = 0; a < SeatMath.AXES; a++)
                PlayerData.SetFloat(SeatMath.PersistKey(t, a), user[t * SeatMath.AXES + a]);
        }
    }
}
