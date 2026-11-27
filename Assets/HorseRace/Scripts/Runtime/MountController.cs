using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace HorseRace
{
    /// <summary>
    /// 乗り物（馬・ラクダ）1頭ぶんの制御。オーナー（＝乗っているプレイヤー）がレイキャスト接地で移動を計算し、
    /// transform を直接更新して 10Hz で手動同期する。非オーナーは送信側のサーバー時刻（syncTime）付きの
    /// サンプルを 4 個までためて、サーバー時刻の 0.22 秒前（InterpMath.DELAY、送信間隔の約 2 倍）を補間で描く。最大 0.3 秒外挿。
    /// 新しいサンプルで描く位置が変わるときは、今見えている位置から 0.15 秒かけて寄せる（逆戻り・瞬間移動しない）。10m 以上はテレポートとしてスナップ。
    ///
    /// シーン構成（このスクリプトと同じ GameObject に VRCStation を置く。Udon の駅イベントは
    /// 同じ GameObject の UdonBehaviour にしか届かないため）:
    ///   MountRoot  [MountController, VRCStation(Immobilize, stationEnterPlayerLocation = SeatAnchor), Rigidbody(isKinematic), 体の Collider]
    ///     SeatAnchor   … 座席。ピッチと上下動を平滑化して動かす（酔い対策）
    ///     Visual       [MountVisual] … Horse / Camel / SkeletonHorse のモデル
    ///     ReinsVR      [MountInputVR]
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MountController : UdonSharpBehaviour
    {
        // ---- syncGait のビット（下位 3 ビットが歩様 0..4）----
        public const int GAIT_MASK = 7;
        public const int GAIT_FLAG_AIR = 8;
        public const int GAIT_FLAG_STUN = 16;
        public const int GAIT_FLAG_STAMP = 32;

        public const int ANIM_IDLE = 0;
        public const int ANIM_WALK = 1;
        public const int ANIM_TROT = 2;
        public const int ANIM_CANTER = 3;
        public const int ANIM_GALLOP = 4;

        public const float GRAVITY = 20f;
        public const float SEND_INTERVAL = 0.1f;   // 10Hz
        public const float INTERP_DELAY = InterpMath.DELAY;
        public const float MAX_EXTRAPOLATION = 0.3f;

        // ---- 同期（brief の名前そのまま）----
        [UdonSynced] public Vector3 syncPos;
        [UdonSynced] public float syncYaw;
        [UdonSynced] public float syncSpeed;
        [UdonSynced] public int syncGait;
        [UdonSynced] public int syncMount;
        [UdonSynced] public int syncCoat;
        [UdonSynced] public int syncCp;
        [UdonSynced] public float syncDistToCp;
        [UdonSynced] public double syncFinishAt = -1.0;
        /// <summary>送った時のサーバー時刻（OnPreSerialization で入れる）。受け手の補間の時間軸。</summary>
        [UdonSynced] public double syncTime;
        /// <summary>乗り手が合わせた SeatAnchor（x = 乗り物ローカルの高さ, y = 前後, z = 前傾の角度）。他の人の画面の Station も同じ位置に置く。</summary>
        [UdonSynced] public Vector3 syncSeat;

        // ---- 公開状態 ----
        [Header("Slot")]
        public int slotIndex;
        public int riderPlayerId = -1;
        /// <summary>RaceManager がレース中に true にする（SetRaceActive で変えること）。true かつ InputLocked=false の間は降車しても再着席させる。
        /// true の間だけ Jump での降車を禁止する（ロビー・練習場では自由に降りられる）。</summary>
        public bool raceActive;
        public bool InputLocked;
        public float Stamina = 100f;
        public bool OnSand;
        public bool Exhausted;
        public float Speed;
        public bool Grounded = true;
        /// <summary>逆走している（次の CP と反対へ 1.5 秒以上）。首の札・懐中時計が「逆走」と出す（ローカル）。</summary>
        public bool WrongWay;
        /// <summary>前の乗り物の後ろについている（ドラフト、0..1）。ローカル。</summary>
        public float Draft;
        /// <summary>今「ひと息」（回復 ×1.25）中か。ローカル。</summary>
        public bool SecondWindActive;
        /// <summary>Task 11 が Terrain の砂レイヤー比率（0..1）を入れる。</summary>
        public float terrainSandWeight;
        /// <summary>SandZone が増減する。</summary>
        public int sandZoneCount;

        [Header("References")]
        public VRC.SDK3.Components.VRCStation station;
        public Transform seatAnchor;
        public MountVisual visual;
        public MountInputVR vrInput;
        /// <summary>鞍の位置合わせ（アバターの腰を測る・微調整・保存）。無ければ seatHeight / seatForward の固定位置。</summary>
        public SeatFit seatFit;

        [Header("Ground")]
        public LayerMask groundMask = -1;
        public LayerMask obstacleMask = -1;
        [Tooltip("ほかの乗り物の体のレイヤー（押し合い・ドラフト）。0 なら調べない")]
        public LayerMask mountMask = 0;
        public float rayStartHeight = 1.5f;
        public float groundSnap = 0.6f;
        public float maxStepUp = 0.6f;
        public float bodyRadius = 0.8f;
        public float chestHeight = 1.2f;
        public float jumpSpeed = 6f;

        [Header("Seat (anti-motion-sickness)")]
        public float seatHeight = 1.6f;
        public float seatForward = -0.05f;
        public float seatMaxLag = 0.5f;
        public float seatPitchScale = 0.5f;

        [Header("Progress")]
        /// <summary>RaceManager がステージ開始時に設定する。</summary>
        public int currentStage;
        public float offCourseDistance = 60f;
        public float terrainProbeRange = 200f;
        public float underTerrainProbeRange = 100f;
        public float airNoGroundGrace = 2f;

        // ---- チェックポイント進行・コース外リセット（オーナー側）----
        private Checkpoint nextCp;
        private Vector3 respawnPos;
        private float respawnYaw;
        private bool hasRespawn;
        private float secondsOffCourse;
        private float airTime;

        // ---- オーナー側の内部状態 ----
        private int cruiseLevel;          // 0 停止, 1 常歩, 2 速歩, 3 駆歩
        private float inputSteer;         // A/D・左スティック
        public const float STICK_DEADZONE = 0.15f;
        private float inputVertical;
        private bool verticalArmed = true;
        private bool jumpQueued;
        private bool brakeWasHeld;
        private float brakeRepeatAt;
        private float yaw;
        private float vy;
        private float fallStartY;
        private float stunTimer;
        private Vector3 knockVel;
        // ---- スタミナの判断（system-r1-01）・着地（r1-03）・壁（r1-02）・押し合い（r1-13）・逆走（r1-17）・スタート（r1-15）----
        private bool wasSprinting;
        private float secondWindUntil;
        private float lastSprintHeldAt = -10f;
        private bool prevSprintReq;
        private bool stickSprintLatched;
        private float lastWallAt = -10f;
        private float nextMountScan;
        private float wrongWayTimer;
        private bool startArmed;
        private double startPressServer = -1.0;
        private double raceGunServer = -1.0;
        // TrySlide の結果（out 引数の代わり）
        private Vector3 slideNext;
        private float slideGroundY;
        private Vector3 pullTarget;
        private float pullBonus;
        private float pullRemaining;
        private float[] modFactors;
        private float[] modRemaining;
        private float nextSendTime;
        private bool seated;
        private bool started;

        // ---- 見た目・座席 ----
        private float bodyPitch;
        private float seatPitch;
        private float seatY;
        private float seatLocalY;
        private bool interactDisabled;

        // ---- 非オーナー補間（送信時刻つきのサンプルを InterpMath.BUFFER 個）----
        private double[] bufTime;
        private Vector3[] bufPos;
        private float[] bufYaw;
        private float[] bufSpeed;
        private int bufCount;
        private double renderClock;
        private Vector3 errPos;
        private float errYaw;
        private float lastSpeed;
        // SampleBuffer の結果（out 引数の代わり）
        private Vector3 samplePos;
        private float sampleYaw;

        private void Start()
        {
            EnsureInit();
        }

        private void EnsureInit()
        {
            if (started) return;
            started = true;
            modFactors = new float[SpeedMod.SLOTS];
            modRemaining = new float[SpeedMod.SLOTS];
            bufTime = new double[InterpMath.BUFFER];
            bufPos = new Vector3[InterpMath.BUFFER];
            bufYaw = new float[InterpMath.BUFFER];
            bufSpeed = new float[InterpMath.BUFFER];
            yaw = transform.eulerAngles.y;
            syncPos = transform.position;
            syncYaw = yaw;
            seatLocalY = seatHeight;
            seatY = transform.position.y + seatLocalY;
            if (station == null) Debug.LogWarning("[HorseRace] MountController: VRCStation が未設定です " + gameObject.name);
            if (seatAnchor == null) Debug.LogWarning("[HorseRace] MountController: seatAnchor が未設定です " + gameObject.name);
            if (visual != null) visual.Apply(syncMount, syncCoat);
        }

        // =====================================================================
        // 公開 API
        // =====================================================================

        /// <summary>全クライアントで呼んでよい。対象がローカルなら所有権を取って乗せる。playerId &lt; 0 で解除。</summary>
        public void AssignRider(int playerId, int mount, int coat)
        {
            EnsureInit();
            VRCPlayerApi local = Networking.LocalPlayer;
            bool wasLocal = local != null && riderPlayerId == local.playerId;
            riderPlayerId = playerId;
            bool isLocal = local != null && playerId == local.playerId;

            if (wasLocal && !isLocal && seated && station != null)
            {
                station.ExitStation(local);
            }
            // レース中だけ Jump で降車させない（VRChat は disableStationExit=false だと Jump で駅から降ろす）。
            // ロビー・練習場では自由に降りられる。raceActive が変わったら SetRaceActive が更新する。
            ApplyStationExitLock();
            if (!isLocal) return;

            Networking.SetOwner(local, gameObject);
            syncMount = mount;
            syncCoat = coat;
            cruiseLevel = 0;
            Speed = 0f;
            Stamina = 100f;
            Exhausted = false;
            if (visual != null) visual.Apply(syncMount, syncCoat);
            if (station != null) station.UseStation(local);
            WriteSync();
            RequestSerialization();
        }

        /// <summary>全クライアントで呼んでよい。raceActive を変え、降車禁止（disableStationExit）を合わせる。</summary>
        public void SetRaceActive(bool active)
        {
            raceActive = active;
            ApplyStationExitLock();
        }

        private void ApplyStationExitLock()
        {
            if (station != null) station.disableStationExit = riderPlayerId >= 0 && raceActive;
        }

        /// <summary>オーナー側で呼ぶ。位置と向きを即座に設定して速度をリセットする。</summary>
        public void TeleportTo(Vector3 pos, float newYaw)
        {
            EnsureInit();
            transform.position = pos;
            yaw = newYaw;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Speed = 0f;
            vy = 0f;
            knockVel = Vector3.zero;
            pullRemaining = 0f;
            stunTimer = 0f;
            cruiseLevel = 0;
            Grounded = true;
            fallStartY = pos.y;
            wrongWayTimer = 0f;
            WrongWay = false;
            seatY = pos.y + seatLocalY;   // 座席はすぐ新しい位置へ（平滑化で遅れて付いてこない）
            bodyPitch = 0f;
            seatPitch = 0f;
            bufCount = 0;
            if (Networking.IsOwner(gameObject))
            {
                WriteSync();
                RequestSerialization();
            }
        }

        /// <summary>オーナー側。速度倍率を seconds 秒（SpeedMod の 8 スロットで掛け算、下限 ×0.4）。</summary>
        public void AddSpeedMod(float factor, float seconds)
        {
            EnsureInit();
            SpeedMod.Add(modFactors, modRemaining, factor, seconds);
        }

        /// <summary>オーナー側。impulse は速度変化（m/s）。視点（yaw）は回さない。</summary>
        public void ApplyKnock(Vector3 impulse, float stunSeconds)
        {
            EnsureInit();
            knockVel += new Vector3(impulse.x, 0f, impulse.z);
            if (impulse.y > 0f)
            {
                vy = Mathf.Max(vy, impulse.y);
                if (Grounded) { Grounded = false; fallStartY = transform.position.y; }
            }
            if (stunSeconds > stunTimer) stunTimer = stunSeconds;
        }

        /// <summary>ステージ開始（COUNTDOWN）で RaceManager が自分の乗り物に呼ぶ。号砲の前後の前進入力を見てスタートダッシュ／立ち上がりを決める（system-r1-15）。</summary>
        public void ArmStart()
        {
            startArmed = true;
            startPressServer = -1.0;
            raceGunServer = -1.0;
        }

        /// <summary>RACING に入ったとき RaceManager が呼ぶ（gunServer = stageStartServer = 号砲のサーバー時刻）。</summary>
        public void BeginRace(double gunServer)
        {
            raceGunServer = gunServer;
        }

        private void NoteStartPress()
        {
            if (!startArmed || startPressServer >= 0.0) return;
            startPressServer = Networking.GetServerTimeInSeconds();
        }

        /// <summary>号砲の後、押した時刻と号砲の差でダッシュ（×1.2 1.5 秒＋すぐ速歩の速さ）か立ち上がり（0.5 秒の硬直）。</summary>
        private void ResolveStart()
        {
            if (!startArmed || raceGunServer < 0.0 || InputLocked) return;
            double now = Networking.GetServerTimeInSeconds();
            if (startPressServer < 0.0)
            {
                if (now - raceGunServer > MountMath.START_WINDOW) startArmed = false;
                return;
            }
            startArmed = false;
            int r = MountMath.StartBoost((float)(startPressServer - raceGunServer));
            if (r == MountMath.START_BOOST)
            {
                AddSpeedMod(MountMath.START_BOOST_FACTOR, MountMath.START_BOOST_SECONDS);
                if (cruiseLevel < 3) cruiseLevel = 3;
                float trot = MountMath.GaitSpeed(syncMount, MountMath.GAIT_TROT);
                if (Speed < trot) Speed = trot;
            }
            else if (r == MountMath.START_STALL)
            {
                if (stunTimer < MountMath.START_STALL_SECONDS) stunTimer = MountMath.START_STALL_SECONDS;   // 立ち上がる（Stagger）
            }
        }

        /// <summary>オーナー側。target へ bonusMps の追加速度で seconds 秒引き寄せる。</summary>
        public void ApplyPull(Vector3 target, float bonusMps, float seconds)
        {
            EnsureInit();
            pullTarget = target;
            pullBonus = bonusMps;
            pullRemaining = seconds;
        }

        /// <summary>オーナー側。ステージ開始時に RaceManager が呼ぶ。syncCp = 0 はスタートライン通過済みの意味。</summary>
        public void ResetProgress(Vector3 startPos, float startYaw, Checkpoint firstCp)
        {
            EnsureInit();
            syncCp = 0;
            syncFinishAt = -1.0;
            syncDistToCp = 0f;
            nextCp = firstCp;
            respawnPos = startPos + Vector3.up * 0.5f;
            respawnYaw = startYaw;
            hasRespawn = true;
            secondsOffCourse = 0f;
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }

        /// <summary>Checkpoint から呼ばれる。現在のステージで、直前の CP の次の番号のときだけ進む。</summary>
        public void ReachCheckpoint(Checkpoint cp)
        {
            if (cp == null) return;
            if (!Networking.IsOwner(gameObject)) return;
            if (!raceActive || InputLocked) return;
            if (cp.stageIndex != currentStage) return;
            if (cp.index != syncCp + 1) return;
            syncCp = cp.index;
            respawnPos = cp.transform.position + Vector3.up * 0.5f;
            respawnYaw = cp.transform.eulerAngles.y;
            hasRespawn = true;
            nextCp = cp.next;
            secondsOffCourse = 0f;
            if (cp.isGoal && syncFinishAt < 0.0) syncFinishAt = Networking.GetServerTimeInSeconds();
            RequestSerialization();
        }

        /// <summary>XZ 平面での点 p と線分 a-b の距離。</summary>
        public static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x;
            float abz = b.z - a.z;
            float len2 = abx * abx + abz * abz;
            float t = 0f;
            if (len2 > 0.0001f) t = Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / len2);
            float dx = p.x - (a.x + abx * t);
            float dz = p.z - (a.z + abz * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void UpdateProgress(float dt)
        {
            Vector3 pos = transform.position;
            syncDistToCp = nextCp != null ? Vector3.Distance(pos, nextCp.transform.position) : 0f;
            if (Grounded) airTime = 0f; else airTime += dt;
            // ゴール後は惰性でどこへ行ってもよい
            if (!raceActive || !hasRespawn || syncFinishAt >= 0.0) { secondsOffCourse = 0f; wrongWayTimer = 0f; WrongWay = false; return; }

            // 逆走（system-r1-17）：次の CP と反対へ速く走り続けたら 1.5 秒で知らせる
            if (nextCp != null && !InputLocked)
            {
                Vector3 to = nextCp.transform.position - pos;
                to.y = 0f;
                Vector3 f = transform.forward;
                f.y = 0f;
                float dot = (to.sqrMagnitude > 1f && f.sqrMagnitude > 0.01f) ? Vector3.Dot(f.normalized, to.normalized) : 1f;
                if (RaceMath.WrongWayNow(dot, Speed)) wrongWayTimer += dt;
                else if (dot > 0f) wrongWayTimer = 0f;
                WrongWay = wrongWayTimer >= RaceMath.WRONG_WAY_SECONDS;
            }
            else { wrongWayTimer = 0f; WrongWay = false; }

            RaycastHit hit;
            float height;
            if (Physics.Raycast(pos + Vector3.up * rayStartHeight, Vector3.down, out hit,
                rayStartHeight + terrainProbeRange, groundMask, QueryTriggerInteraction.Ignore))
            {
                height = pos.y - hit.point.y;
            }
            else if (Physics.Raycast(pos + Vector3.up * rayStartHeight, Vector3.up, out hit,
                underTerrainProbeRange, groundMask, QueryTriggerInteraction.Ignore))
            {
                // 下に地面が無く上にある：地形の下に抜けた
                height = -100f;
            }
            else if (Grounded || airTime >= airNoGroundGrace)
            {
                height = -100f;
            }
            else
            {
                // 深い谷を跳び越え中：猶予時間の間は地形の下とみなさない
                height = 0f;
            }

            float dist;
            if (nextCp != null) dist = DistToSegmentXZ(pos, respawnPos, nextCp.transform.position);
            else dist = DistToSegmentXZ(pos, respawnPos, respawnPos);

            if (dist > offCourseDistance) secondsOffCourse += dt;
            else secondsOffCourse = 0f;

            if (RaceMath.NeedsReset(height, dist, secondsOffCourse))
            {
                secondsOffCourse = 0f;
                TeleportTo(respawnPos, respawnYaw);
            }
        }

        /// <summary>
        /// 落とし穴（ResetVolume、岩橋の溝の底など）から呼ばれる。オーナーがレース中・ゴール前なら最後に通った CP（またはスタート）へ戻す。
        /// </summary>
        public void ForceResetToLastCp()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (!raceActive || !hasRespawn || syncFinishAt >= 0.0) return;
            secondsOffCourse = 0f;
            TeleportTo(respawnPos, respawnYaw);
        }

        public bool IsLocalRider()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return local != null && seated && riderPlayerId == local.playerId;
        }

        public float SpeedModCombined()
        {
            EnsureInit();
            return SpeedMod.Combined(modFactors, modRemaining);
        }

        // =====================================================================
        // 入力（デスクトップ／スティック。VR の手綱は MountInputVR）
        // =====================================================================

        public override void InputMoveVertical(float value, UdonInputEventArgs args)
        {
            inputVertical = value;
            if (!IsLocalRider()) return;
            if (verticalArmed && value > 0.6f) NoteStartPress();
            if (verticalArmed && value > 0.6f) { cruiseLevel = Mathf.Min(cruiseLevel + 1, 3); verticalArmed = false; }
            else if (verticalArmed && value < -0.6f) { cruiseLevel = Mathf.Max(cruiseLevel - 1, 0); verticalArmed = false; }
            else if (Mathf.Abs(value) < 0.3f) verticalArmed = true;
        }

        public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
        {
            inputSteer = value;
        }

        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            if (value && IsLocalRider()) jumpQueued = true;
        }

        private bool ReadSprint(VRCPlayerApi local)
        {
            if (local.IsUserInVR())
            {
                // スティック押し込みは切り替え（押しっぱなしにしない：system-r1-11）。ばて・手綱で切れる
                if (Input.GetButtonDown("Oculus_CrossPlatform_PrimaryThumbstick")) stickSprintLatched = !stickSprintLatched;
                if (Exhausted || (vrInput != null && vrInput.brake)) stickSprintLatched = false;
                if (vrInput != null && vrInput.sprint) return true;
                return stickSprintLatched;
            }
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        // =====================================================================
        // 駅（座席）
        // =====================================================================

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            if (player.playerId != riderPlayerId)
            {
                if (station != null) station.ExitStation(player);
                return;
            }
            seated = true;
            if (seatFit != null) seatFit.OnSeated();
            if (vrInput != null) vrInput.SendCustomEventDelayedSeconds("Calibrate", 0.5f);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;
            seated = false;
            if (seatFit != null) seatFit.OnUnseated();
            if (player.playerId == riderPlayerId && raceActive && !InputLocked)
            {
                // 降車した瞬間に戻す（同じフレーム内の UseStation は無視されることがあるので 1 フレーム後）
                SendCustomEventDelayedFrames(nameof(_Reseat), 1);
            }
        }

        /// <summary>
        /// 自分の乗り物（割り当て済み）から降りていたら、体をクリック／Interact でまた座る（ロビーで降りた後など）。
        /// 他の人の乗り物・座っている間は Interact できない（DisableInteractive）。
        /// </summary>
        public override void Interact()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || station == null || seated) return;
            if (riderPlayerId != local.playerId) return;
            station.UseStation(local);
        }

        /// <summary>自分が乗っていれば降りる（練習用の馬に乗り換えるときなど）。</summary>
        public void ExitLocal()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || station == null || !seated) return;
            station.ExitStation(local);
        }

        /// <summary>今の見た目の鞍の種類（SeatMath.SEAT_*）。</summary>
        public int SeatType()
        {
            return SeatMath.SeatType(syncMount, syncCoat);
        }

        private void UpdateInteractable()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            bool disable = local == null || seated || riderPlayerId != local.playerId;
            if (disable == interactDisabled) return;
            interactDisabled = disable;
            DisableInteractive = disable;
        }

        public void _Reseat()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || station == null) return;
            if (riderPlayerId != local.playerId || seated) return;
            if (!raceActive || InputLocked) return;
            station.UseStation(local);
        }

        // =====================================================================
        // 更新
        // =====================================================================

        private void Update()
        {
            if (!started) EnsureInit();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (Networking.IsOwner(gameObject))
            {
                if (IsLocalRider())
                {
                    SimulateOwner(dt);
                    if (Time.time >= nextSendTime)
                    {
                        nextSendTime = Time.time + SEND_INTERVAL;
                        RequestSerialization();
                    }
                }
                else
                {
                    WriteSync();
                }
            }
            else
            {
                UpdateRemote(dt);
            }

            UpdateInteractable();
            float targetPitch = SampleGroundPitch();
            bodyPitch = MountMath.SmoothAngle(bodyPitch, targetPitch, dt * 3f);
            seatPitch = MountMath.SmoothAngle(seatPitch, targetPitch * seatPitchScale, dt);
            UpdateSeat(dt);

            if (visual != null)
            {
                visual.Apply(syncMount, syncCoat);
                visual.SetPitch(bodyPitch);
                float stam = Networking.IsOwner(gameObject) ? Stamina : -1f;
                float spd = Networking.IsOwner(gameObject) ? Speed : lastSpeed;
                visual.SetMotion(syncGait, spd, syncMount, stam);
            }
        }

        private void SimulateOwner(float dt)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            int mount = syncMount;

            SpeedMod.Tick(modRemaining, dt);
            if (stunTimer > 0f) stunTimer -= dt;
            if (pullRemaining > 0f) pullRemaining -= dt;
            ResolveStart();

            bool canControl = !InputLocked && stunTimer <= 0f;

            // ---- 入力 ----
            float steer = inputSteer;
            bool sprintReq = false;
            bool brakeHeld = false;
            bool vr = local.IsUserInVR();
            if (canControl)
            {
                sprintReq = ReadSprint(local);
                if (vr && vrInput != null)
                {
                    // スティックに入力があればスティック優先、無ければ手綱
                    if (Mathf.Abs(inputSteer) <= STICK_DEADZONE) steer = vrInput.steer;
                    brakeHeld = vrInput.brake;
                }
                if (brakeHeld && (!brakeWasHeld || Time.time >= brakeRepeatAt))
                {
                    // 疾走中の最初のひと引きは疾走をやめるだけ（歩様は下げない）
                    if (brakeWasHeld || !prevSprintReq) cruiseLevel = Mathf.Max(cruiseLevel - 1, 0);
                    brakeRepeatAt = Time.time + 0.8f;
                }
                if (sprintReq && cruiseLevel < 1) cruiseLevel = 1;
            }
            else
            {
                // 硬直中は操作不可だが歩様の設定は残す（解けたら元の歩様へ戻る）
                steer = 0f;
                jumpQueued = false;
                // カウントダウン中の VR の振り下ろしもスタートの入力として数える
                if (InputLocked && vr && vrInput != null && vrInput.sprint) NoteStartPress();
            }
            if (sprintReq && !prevSprintReq) NoteStartPress();
            prevSprintReq = sprintReq;
            if (sprintReq) lastSprintHeldAt = Time.time;
            brakeWasHeld = brakeHeld;
            if (InputLocked) cruiseLevel = 0;

            // ---- 地面 ----
            Vector3 pos = transform.position;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            RaycastHit hit;
            bool hasGround = Physics.Raycast(pos + Vector3.up * rayStartHeight, Vector3.down, out hit,
                rayStartHeight + groundSnap + 50f, groundMask, QueryTriggerInteraction.Ignore);
            float groundY = hasGround ? hit.point.y : -100000f;
            float slopeDeg = 0f;
            if (hasGround) slopeDeg = SlopeAlong(hit.normal, fwd);

            // ---- ほかの乗り物：押し合い・ドラフト（0.1 秒ごと）----
            if (Time.time >= nextMountScan)
            {
                nextMountScan = Time.time + 0.1f;
                ScanMounts(pos, fwd);
            }

            // ---- スタミナ・砂（system-r1-01）----
            OnSand = sandZoneCount > 0 || terrainSandWeight > 0.5f;
            bool sprinting = sprintReq && !Exhausted && Speed > 0.5f && Grounded;
            // まだ 15 以上残して疾走をやめた＝いい手綱さばき：2 秒間 回復 ×1.25
            if (wasSprinting && !sprinting && MountMath.EarnsSecondWind(Stamina)) secondWindUntil = Time.time + MountMath.SECOND_WIND_SECONDS;
            wasSprinting = sprinting;
            SecondWindActive = Time.time < secondWindUntil;
            float refillMul = SecondWindActive ? MountMath.SECOND_WIND_MUL : 1f;
            if (Draft > 0f) refillMul *= MountMath.DRAFT_REFILL_MUL;
            int gaitNow = cruiseLevel > 0 ? cruiseLevel - 1 : MountMath.GAIT_WALK;
            Stamina = MountMath.StepStamina(mount, Stamina, sprinting, Exhausted, gaitNow, dt, refillMul);
            Exhausted = MountMath.IsExhausted(Stamina, Exhausted);

            // ---- 目標速度 ----
            float target = 0f;
            if (canControl && cruiseLevel > 0)
            {
                int gait = cruiseLevel - 1;
                if (sprintReq) gait = MountMath.GAIT_SPRINT;
                float baseSpeed = MountMath.TargetSpeed(mount, gait, slopeDeg, Exhausted, OnSand) + MountMath.DRAFT_SPEED_BONUS * Draft;
                target = baseSpeed * SpeedMod.Combined(modFactors, modRemaining);
            }
            if (Grounded) Speed = MountMath.Approach(Speed, target, dt);

            // ---- 旋回 ----
            yaw += steer * MountMath.TurnRateDeg(mount, Speed) * dt;
            fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // ---- 水平移動 ----
            Vector3 move = fwd * Speed;
            move += knockVel;
            knockVel = knockVel * Mathf.Exp(-dt * 3f);
            if (knockVel.magnitude < 0.05f) knockVel = Vector3.zero;
            if (pullRemaining > 0f)
            {
                Vector3 toT = pullTarget - pos;
                toT.y = 0f;
                if (toT.magnitude > 1f) move += toT.normalized * pullBonus;
                else pullRemaining = 0f;
            }
            Vector3 step = move * dt;
            float stepLen = step.magnitude;
            if (stepLen > 0.0001f)
            {
                // 正面の障害物（胸の高さ）：かすったら沿って滑る、正面だけ止まる（system-r1-02）
                Vector3 dir = step / stepLen;
                RaycastHit ohit;
                if (Physics.Raycast(pos + Vector3.up * chestHeight, dir, out ohit, stepLen + bodyRadius,
                    obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 n = Vector3.ProjectOnPlane(ohit.normal, Vector3.up);
                    if (n.sqrMagnitude < 0.0001f) n = -dir; else n = n.normalized;
                    step = WallContact(pos, step, dir, n, dt);
                }
            }

            Vector3 next = pos + step;

            // ---- 新しい位置での接地 ----
            RaycastHit nhit;
            bool nGround = Physics.Raycast(next + Vector3.up * rayStartHeight, Vector3.down, out nhit,
                rayStartHeight + 200f, groundMask, QueryTriggerInteraction.Ignore);
            float nGroundY = nGround ? nhit.point.y : -100000f;

            if (Grounded && nGround && nGroundY - pos.y > maxStepUp)
            {
                // 壁・急な上り：地面の法線で沿う向きへ滑る（滑れなければ止まる）
                if (TrySlide(pos, step, nhit.normal, dt))
                {
                    next = slideNext;
                    nGroundY = slideGroundY;
                }
                else
                {
                    next = pos;
                    Speed = 0f;
                    nGroundY = groundY;
                }
            }

            if (Grounded)
            {
                if (jumpQueued && canControl)
                {
                    vy = jumpSpeed;
                    Grounded = false;
                    fallStartY = next.y;
                }
                else if (nGround && next.y - nGroundY <= groundSnap)
                {
                    next.y = nGroundY;
                    vy = 0f;
                }
                else
                {
                    // 崖から落ちた
                    Grounded = false;
                    vy = 0f;
                    fallStartY = next.y;
                }
            }
            else
            {
                vy -= GRAVITY * dt;
                next.y += vy * dt;
                if (nGround && nGroundY - next.y > maxStepUp)
                {
                    // 空中で崖の側面に当たった：水平移動を止め、当たり方に応じて速さを削る（0 にはしない）
                    Vector3 hn = Vector3.ProjectOnPlane(nhit.normal, Vector3.up);
                    float head = 1f;
                    if (hn.sqrMagnitude > 0.0001f && stepLen > 0.0001f) head = Mathf.Clamp01(-Vector3.Dot(step / stepLen, hn.normalized));
                    next.x = pos.x;
                    next.z = pos.z;
                    Speed *= MountMath.WallScrapeFactor(head);
                    knockVel = Vector3.zero;
                    nGround = Physics.Raycast(next + Vector3.up * rayStartHeight, Vector3.down, out nhit,
                        rayStartHeight + 200f, groundMask, QueryTriggerInteraction.Ignore);
                    nGroundY = nGround ? nhit.point.y : -100000f;
                }
                if (next.y > fallStartY) fallStartY = next.y;
                if (vy <= 0f && nGround && next.y <= nGroundY)
                {
                    next.y = nGroundY;
                    vy = 0f;
                    Grounded = true;
                    // 着地（system-r1-03）：6m 未満は何もなし、高いほど少し硬直・少し減速（止まらない）
                    float h = fallStartY - nGroundY;
                    float stun = MountMath.LandingStun(h);
                    if (stun > stunTimer) stunTimer = stun;
                    Speed *= MountMath.LandingSpeedKeep(h);
                    // きれいな着地：10m 未満の落下（2m 以上）で、着地の直前まで疾走を押していた → ×1.15 1.5 秒
                    if (h >= 2f && MountMath.CleanLanding(h, Time.time - lastSprintHeldAt))
                        AddSpeedMod(MountMath.CLEAN_LANDING_FACTOR, MountMath.CLEAN_LANDING_SECONDS);
                }
            }
            jumpQueued = false;

            transform.position = next;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            UpdateProgress(dt);

            syncGait = ComputeAnimGait(mount, Speed);
            if (!Grounded) syncGait |= GAIT_FLAG_AIR;
            if (stunTimer > 0f) syncGait |= GAIT_FLAG_STUN;
            if (InputLocked && Speed < 0.3f) syncGait |= GAIT_FLAG_STAMP;
            WriteSync();
        }

        /// <summary>
        /// 胸の高さの障害物に当たった。n = 水平の法線、head = 0 かすった〜1 正面。正面（head &gt; 0.85）だけ止まる。
        /// それ以外は壁に沿う成分だけ進み、当たった瞬間に速さを WallScrapeFactor(head) 倍、鼻先を滑る向きへ 20°/秒まで回す。
        /// </summary>
        private Vector3 WallContact(Vector3 pos, Vector3 step, Vector3 dir, Vector3 n, float dt)
        {
            float head = Mathf.Clamp01(-Vector3.Dot(dir, n));
            bool fresh = Time.time - lastWallAt > 0.25f;
            lastWallAt = Time.time;
            if (head > MountMath.WALL_HARD_STOP_HEAD)
            {
                Speed = 0f;
                knockVel = Vector3.zero;
                return Vector3.zero;
            }
            if (fresh) Speed *= MountMath.WallScrapeFactor(head);
            Vector3 slid = Vector3.ProjectOnPlane(step, n);
            slid.y = 0f;
            float len = slid.magnitude;
            if (len < 0.0001f) return Vector3.zero;
            // 滑った先にも壁（角）なら止める
            if (Physics.Raycast(pos + Vector3.up * chestHeight, slid / len, len + bodyRadius * 0.5f, obstacleMask, QueryTriggerInteraction.Ignore))
                return Vector3.zero;
            TurnToward(slid / len, dt);
            return slid;
        }

        /// <summary>
        /// 段差（maxStepUp より高い地面）に当たった：地面の法線の水平成分に沿って滑る。法線が上向きすぎる（崖の上の面）ときは ±45° を試す。
        /// 結果は slideNext / slideGroundY。滑れなければ false。
        /// </summary>
        private bool TrySlide(Vector3 pos, Vector3 step, Vector3 groundNormal, float dt)
        {
            float stepLen = step.magnitude;
            if (stepLen < 0.0001f) return false;
            Vector3 dir = step / stepLen;
            Vector3 n = Vector3.ProjectOnPlane(groundNormal, Vector3.up);
            if (n.sqrMagnitude > 0.04f)
            {
                n = n.normalized;
                float head = Mathf.Clamp01(-Vector3.Dot(dir, n));
                if (head <= MountMath.WALL_HARD_STOP_HEAD)
                {
                    Vector3 slid = Vector3.ProjectOnPlane(step, n);
                    slid.y = 0f;
                    if (TrySlideTo(pos, slid))
                    {
                        if (Time.time - lastWallAt > 0.25f) Speed *= MountMath.WallScrapeFactor(head);
                        lastWallAt = Time.time;
                        if (slid.sqrMagnitude > 0.000001f) TurnToward(slid.normalized, dt);
                        return true;
                    }
                }
            }
            // 法線から向きが分からない：斜め 45° の左右を試す（速さは cos45 ぶん）
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 d2 = Quaternion.Euler(0f, 45f * side, 0f) * step * 0.7071f;
                if (TrySlideTo(pos, d2))
                {
                    if (Time.time - lastWallAt > 0.25f) Speed *= MountMath.WallScrapeFactor(0.7071f);
                    lastWallAt = Time.time;
                    TurnToward(d2.normalized, dt);
                    return true;
                }
            }
            return false;
        }

        private bool TrySlideTo(Vector3 pos, Vector3 slid)
        {
            if (slid.sqrMagnitude < 0.000001f) return false;
            Vector3 cand = pos + slid;
            RaycastHit h;
            if (!Physics.Raycast(cand + Vector3.up * rayStartHeight, Vector3.down, out h, rayStartHeight + 200f, groundMask, QueryTriggerInteraction.Ignore))
                return false;
            if (h.point.y - pos.y > maxStepUp) return false;
            slideNext = cand;
            slideGroundY = h.point.y;
            return true;
        }

        /// <summary>鼻先（yaw）を dir の向きへ最大 WALL_SLIDE_TURN_DEG 度/秒で回す。</summary>
        private void TurnToward(Vector3 dir, float dt)
        {
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float d = InterpMath.DeltaAngle(yaw, targetYaw);
            if (d > 90f || d < -90f) return;   // 後ろ向きへは回さない（押し戻されたとき）
            float maxStep = MountMath.WALL_SLIDE_TURN_DEG * dt;
            yaw += Mathf.Clamp(d, -maxStep, maxStep);
        }

        /// <summary>
        /// 近くの乗り物（system-r1-13）：1.6m 以内は横へ押し合う（knockVel に最大 2 m/s、止めない）。
        /// 3〜14m 前・±12° 以内に走っている乗り物がいればドラフト（Draft = 1：回復 ×1.6、最高速 +0.6 m/s）。
        /// </summary>
        private void ScanMounts(Vector3 pos, Vector3 fwd)
        {
            Draft = 0f;
            if (mountMask.value == 0) return;
            Collider[] cols = Physics.OverlapSphere(pos, MountMath.DRAFT_MAX_M + 1f, mountMask.value, QueryTriggerInteraction.Ignore);
            if (cols == null) return;
            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null) continue;
                MountController m = c.GetComponentInParent<MountController>();
                if (m == null || m == this || m.riderPlayerId < 0) continue;
                if (c.gameObject != m.gameObject) continue;   // 体の Collider だけ（子は数えない）
                Vector3 d = m.transform.position - pos;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.01f) continue;
                if (dist < MountMath.BUMP_RADIUS && Grounded)
                {
                    Vector3 away = -d / dist;
                    float along = Vector3.Dot(knockVel, away);
                    if (along < MountMath.BUMP_SPEED) knockVel += away * (MountMath.BUMP_SPEED - Mathf.Max(0f, along));
                }
                if (Draft <= 0f && Speed > 6f)
                {
                    float ang = Mathf.Atan2(fwd.x * d.z - fwd.z * d.x, fwd.x * d.x + fwd.z * d.z) * Mathf.Rad2Deg;
                    Draft = MountMath.DraftBonus(dist, ang);
                }
            }
        }

        /// <summary>前方向の坂角度（度、上りが正）。</summary>
        private float SlopeAlong(Vector3 normal, Vector3 fwd)
        {
            if (normal.y < 0.05f) return 60f;
            float dy = -(normal.x * fwd.x + normal.z * fwd.z) / normal.y;
            return Mathf.Atan(dy) * Mathf.Rad2Deg;
        }

        private float SampleGroundPitch()
        {
            if ((syncGait & GAIT_FLAG_AIR) != 0) return 0f;
            RaycastHit hit;
            Vector3 pos = transform.position;
            if (!Physics.Raycast(pos + Vector3.up * rayStartHeight, Vector3.down, out hit,
                rayStartHeight + groundSnap, groundMask, QueryTriggerInteraction.Ignore)) return 0f;
            Vector3 fwd = transform.forward;
            // Unity のピッチ（X 回転）は正で前下がり
            return -SlopeAlong(hit.normal, fwd);
        }

        public static int ComputeAnimGait(int mount, float speed)
        {
            if (speed < 0.3f) return ANIM_IDLE;
            float w = MountMath.GaitSpeed(mount, MountMath.GAIT_WALK);
            float t = MountMath.GaitSpeed(mount, MountMath.GAIT_TROT);
            float c = MountMath.GaitSpeed(mount, MountMath.GAIT_CANTER);
            float s = MountMath.GaitSpeed(mount, MountMath.GAIT_SPRINT);
            if (speed < (w + t) * 0.5f) return ANIM_WALK;
            if (speed < (t + c) * 0.5f) return ANIM_TROT;
            if (speed < (c + s) * 0.5f) return ANIM_CANTER;
            return ANIM_GALLOP;
        }

        /// <summary>
        /// SeatAnchor（Station の入る位置）を置く。高さ・前後・前傾は
        ///   自分が乗っている：SeatFit（腰の測定＋微調整）→ syncSeat に入れて送る
        ///   他の人が乗っている：受け取った syncSeat
        ///   誰も乗っていない：鞍の種類ごとの既定値
        /// 高さは平滑化して上下動を伝えない（酔い対策）。
        /// </summary>
        private void UpdateSeat(float dt)
        {
            if (seatAnchor == null) return;
            int type = SeatType();
            float ly = seatHeight;
            float lz = seatForward;
            float tilt = 0f;
            if (IsLocalRider())
            {
                if (seatFit != null)
                {
                    ly = seatFit.AnchorY(type);
                    lz = seatFit.AnchorZ(type);
                    tilt = seatFit.Tilt(type);
                }
                syncSeat = new Vector3(ly, lz, tilt);
            }
            else if (riderPlayerId >= 0 && !Networking.IsOwner(gameObject) && syncSeat.sqrMagnitude > 0.0001f)
            {
                // （まだ 1 度も受け取っていない＝0 ベクトルのときは下の既定値）
                ly = syncSeat.x;
                lz = syncSeat.y;
                tilt = syncSeat.z;
            }
            else if (seatFit != null)
            {
                ly = seatFit.DefaultAnchorY(type);
                lz = seatFit.DefaultAnchorZ(type);
            }
            // 受け取った値が壊れていても座席が遠くへ飛ばない
            ly = Mathf.Clamp(ly, -1f, 4f);
            lz = Mathf.Clamp(lz, -1.5f, 1.5f);
            tilt = Mathf.Clamp(tilt, -SeatMath.TILT_LIMIT, SeatMath.TILT_LIMIT);
            seatLocalY = ly;

            Vector3 root = transform.position;
            float yawDeg = transform.eulerAngles.y;
            float targetY = root.y + ly;
            seatY = MountMath.SmoothAngle(seatY, targetY, dt);
            seatY = Mathf.Clamp(seatY, targetY - seatMaxLag, targetY + seatMaxLag);
            Vector3 p = root + Quaternion.Euler(0f, yawDeg, 0f) * new Vector3(0f, 0f, lz);
            p.y = seatY;
            seatAnchor.position = p;
            seatAnchor.rotation = Quaternion.Euler(seatPitch + tilt, yawDeg, 0f);
        }

        // =====================================================================
        // ネットワーク
        // =====================================================================

        private void WriteSync()
        {
            syncPos = transform.position;
            syncYaw = yaw;
            syncSpeed = Speed;
        }

        public override void OnPreSerialization()
        {
            WriteSync();
            syncTime = Networking.GetServerTimeInSeconds();
        }

        public override void OnDeserialization()
        {
            EnsureInit();
            lastSpeed = syncSpeed;
            // 初回・テレポート（最新サンプルから 10m 以上）：ためたサンプルを捨ててスナップ
            bool snap = bufCount == 0 || Vector3.Distance(syncPos, bufPos[bufCount - 1]) > InterpMath.SNAP_DISTANCE;
            if (snap) bufCount = 0;
            bufCount = InterpMath.Insert(bufTime, bufPos, bufYaw, bufSpeed, bufCount, syncTime, syncPos, syncYaw, syncSpeed);
            if (snap)
            {
                renderClock = Networking.GetServerTimeInSeconds() - InterpMath.DELAY;
                errPos = Vector3.zero;
                errYaw = 0f;
                transform.position = syncPos;
                yaw = syncYaw;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                return;
            }
            // 描く位置が新しいサンプルで変わっても、今見えている位置から続ける（差は UpdateRemote で 0.15 秒かけて消す）
            SampleBuffer(renderClock);
            errPos = transform.position - samplePos;
            errYaw = InterpMath.DeltaAngle(sampleYaw, yaw);
            if (errPos.magnitude > InterpMath.SNAP_DISTANCE) errPos = Vector3.zero;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player != null && player.isLocal)
            {
                yaw = transform.eulerAngles.y;
                Speed = 0f;
                vy = 0f;
                Grounded = true;
            }
            else
            {
                bufCount = 0;
            }
        }

        /// <summary>サンプルの時間軸で renderTime の位置・向きを samplePos / sampleYaw に入れる。古すぎれば最古、新しすぎれば外挿（最大 0.3 秒）。</summary>
        private void SampleBuffer(double renderTime)
        {
            int i = InterpMath.SpanStart(bufTime, bufCount, renderTime);
            if (i < 0)
            {
                samplePos = bufPos[0];
                sampleYaw = bufYaw[0];
            }
            else if (i >= bufCount - 1)
            {
                int last = bufCount - 1;
                float ex = Mathf.Min((float)(renderTime - bufTime[last]), MAX_EXTRAPOLATION);
                Vector3 fwd = Quaternion.Euler(0f, bufYaw[last], 0f) * Vector3.forward;
                samplePos = bufPos[last] + fwd * (bufSpeed[last] * ex);
                sampleYaw = bufYaw[last];
            }
            else
            {
                float t = InterpMath.SpanT(bufTime[i], bufTime[i + 1], renderTime);
                samplePos = Vector3.Lerp(bufPos[i], bufPos[i + 1], t);
                sampleYaw = InterpMath.LerpAngle(bufYaw[i], bufYaw[i + 1], t);
            }
        }

        private void UpdateRemote(float dt)
        {
            if (bufCount == 0) return;
            renderClock = InterpMath.AdvanceClock(renderClock, dt, Networking.GetServerTimeInSeconds() - InterpMath.DELAY);
            SampleBuffer(renderClock);
            float k = InterpMath.DecayFactor(dt, InterpMath.ERROR_TAU);
            errPos = errPos * k;
            errYaw = errYaw * k;
            float ry = sampleYaw + errYaw;
            transform.position = samplePos + errPos;
            transform.rotation = Quaternion.Euler(0f, ry, 0f);
            yaw = ry;
        }
    }
}
