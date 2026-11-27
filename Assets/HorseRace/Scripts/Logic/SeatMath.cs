using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 鞍の位置（SeatAnchor）の計算。Static only (UdonSharp-safe).
    ///
    /// VRCStation は「入る位置」（SeatAnchor）にプレイヤーの原点を置き、騎乗ポーズ（RidePoseBuilder の Station 用 Animator）で
    /// 腰（Hips ボーン）は原点からアバターごとに違う高さに来る。そこで着席後に Hips を測り、
    ///   SeatAnchor（乗り物ローカル）= 鞍の点 − 測った Hips（SeatAnchor ローカル）＋ プレイヤーの微調整
    /// として、どのアバターでも腰が鞍に乗るようにする。Hips は SeatAnchor に対する相対なので、SeatAnchor を動かしても値は変わらない（発散しない）。
    /// </summary>
    public class SeatMath : UdonSharp.UdonSharpBehaviour
    {
        // ---- 鞍の種類（乗り物の見た目ごと）----
        public const int SEAT_HORSE = 0;
        public const int SEAT_CAMEL = 1;
        public const int SEAT_SKELETON = 2;
        public const int SEAT_RAPTOR = 3;
        public const int SEAT_TYPES = 4;
        /// <summary>MountVisual.COAT_SKELETON と同じ値（Logic は Runtime を参照しないので複製）。</summary>
        public const int COAT_SKELETON = 6;
        /// <summary>今の乗り物の種類（MountMath.MOUNT_*）。ラプトルはまだ乗り物として選べない（Phase 2 用に鞍の値だけ持つ）。</summary>
        public const int MOUNT_RAPTOR = 2;

        // ---- 鞍の点（腰＝Hips を置く位置、乗り物ローカル。高さは地面＝乗り物の原点から）----
        // Blender（blender -b）で FBX を読み、背骨の真上 ±12cm の頂点の一番高い所を前後 20 区間で測った背中の高さ ＋ 12cm（鞍と骨盤の厚み）。
        // 前後は Unity の +Z ＝ 頭の向き（MountBuilder が頭を +Z に回す）。
        //   Horse.fbx         背中のくぼみ（前後 -0.10〜+0.05m）の上面 1.46〜1.48m → 腰 1.60m、前後 -0.05m
        //   Camel.fbx         こぶの前のくぼみ（前後 -0.28m）の上面 2.55〜2.60m → 腰 2.70m、前後 -0.25m
        //   SkeletonHorse.fbx 背骨（前後 -0.12〜0.00m）の上面 1.51〜1.53m → 腰 1.64m、前後 -0.05m
        //   Raptor.fbx        元の大きさで背中 0.49m（シーンでの拡大率が決まっていないので ×2.6 を仮定）→ 腰 1.40m、前後 0.00m
        public const float SADDLE_HORSE_Y = 1.60f, SADDLE_HORSE_Z = -0.05f;
        public const float SADDLE_CAMEL_Y = 2.70f, SADDLE_CAMEL_Z = -0.25f;
        public const float SADDLE_SKELETON_Y = 1.64f, SADDLE_SKELETON_Z = -0.05f;
        public const float SADDLE_RAPTOR_Y = 1.40f, SADDLE_RAPTOR_Z = 0.00f;

        // ---- プレイヤーの微調整（1cm / 1° 刻み）----
        public const int AXIS_HEIGHT = 0;
        public const int AXIS_FORWARD = 1;
        public const int AXIS_TILT = 2;
        public const int AXES = 3;
        public const float STEP_M = 0.01f;
        public const float STEP_DEG = 1f;
        public const float HEIGHT_LIMIT = 0.40f;
        public const float FORWARD_LIMIT = 0.40f;
        public const float TILT_LIMIT = 25f;

        // ---- 押し続けたときの繰り返し ----
        /// <summary>押してからこの秒数は 1 回だけ。その後 REPEAT_RATE 秒ごとに 1 回。</summary>
        public const float REPEAT_DELAY = 0.4f;
        public const float REPEAT_RATE = 0.07f;

        // ---- 腰の測定 ----
        /// <summary>SeatAnchor ローカルで測った Hips として受け付ける範囲（これ外はボーンが取れていないとみなす）。</summary>
        public const float HIPS_Y_MIN = -0.8f;
        public const float HIPS_Y_MAX = 2.5f;
        public const float HIPS_Z_LIMIT = 0.6f;
        /// <summary>Hips が取れないとき（ボーンが無い・測る前）：腰の高さ ≒ 目の高さ × これ（騎乗ポーズの RootT.y = 0.08 に合わせた目安）。</summary>
        public const float FALLBACK_HIPS_PER_EYE = 0.06f;
        /// <summary>2 回目以降の測定は前の値とこの割合で混ぜる（一瞬のずれで跳ねない）。</summary>
        public const float CALIB_BLEND = 0.5f;

        public static int SeatType(int mount, int coat)
        {
            if (mount == MountMath.MOUNT_CAMEL) return SEAT_CAMEL;
            if (mount == MOUNT_RAPTOR) return SEAT_RAPTOR;
            if (coat == COAT_SKELETON) return SEAT_SKELETON;
            return SEAT_HORSE;
        }

        public static float DefaultSaddleY(int type)
        {
            if (type == SEAT_CAMEL) return SADDLE_CAMEL_Y;
            if (type == SEAT_SKELETON) return SADDLE_SKELETON_Y;
            if (type == SEAT_RAPTOR) return SADDLE_RAPTOR_Y;
            return SADDLE_HORSE_Y;
        }

        public static float DefaultSaddleZ(int type)
        {
            if (type == SEAT_CAMEL) return SADDLE_CAMEL_Z;
            if (type == SEAT_SKELETON) return SADDLE_SKELETON_Z;
            if (type == SEAT_RAPTOR) return SADDLE_RAPTOR_Z;
            return SADDLE_HORSE_Z;
        }

        public static string TypeKey(int type)
        {
            if (type == SEAT_CAMEL) return "camel";
            if (type == SEAT_SKELETON) return "skeleton";
            if (type == SEAT_RAPTOR) return "raptor";
            return "horse";
        }

        public static string AxisKey(int axis)
        {
            if (axis == AXIS_FORWARD) return "fwd";
            if (axis == AXIS_TILT) return "tilt";
            return "height";
        }

        /// <summary>VRChat Persistence（PlayerData）のキー。例 "hr.seat.camel.height"。</summary>
        public static string PersistKey(int type, int axis)
        {
            return "hr.seat." + TypeKey(type) + "." + AxisKey(axis);
        }

        public static float AxisLimit(int axis)
        {
            if (axis == AXIS_FORWARD) return FORWARD_LIMIT;
            if (axis == AXIS_TILT) return TILT_LIMIT;
            return HEIGHT_LIMIT;
        }

        public static float AxisStep(int axis)
        {
            return axis == AXIS_TILT ? STEP_DEG : STEP_M;
        }

        /// <summary>範囲に収め、刻み（1cm / 1°）に丸める（保存値の誤差が積もらない）。NaN は 0。</summary>
        public static float ClampAxis(int axis, float value)
        {
            if (value != value) return 0f;   // NaN（Udon で確実に使える比較だけ）
            float lim = AxisLimit(axis);
            float step = AxisStep(axis);
            float v = Mathf.Clamp(value, -lim, lim);
            return Mathf.Round(v / step) * step;
        }

        /// <summary>dir = +1 / -1 で 1 刻み動かす。</summary>
        public static float StepAxis(int axis, float value, int dir)
        {
            float d = dir > 0 ? 1f : (dir < 0 ? -1f : 0f);
            return ClampAxis(axis, value + d * AxisStep(axis));
        }

        /// <summary>押し続けて heldSeconds たったときに、合計で何回動いているべきか（押した瞬間 1 回）。</summary>
        public static int RepeatCount(float heldSeconds)
        {
            if (heldSeconds < 0f) return 0;
            if (heldSeconds < REPEAT_DELAY) return 1;
            return 1 + 1 + Mathf.FloorToInt((heldSeconds - REPEAT_DELAY) / REPEAT_RATE);
        }

        /// <summary>SeatAnchor ローカルで測った Hips が本物らしいか（ボーンが無いと 0 ベクトルのワールド座標 → 遠い値になる）。</summary>
        public static bool IsPlausibleHips(float hipsY, float hipsZ)
        {
            if (hipsY != hipsY || hipsZ != hipsZ) return false;
            return hipsY >= HIPS_Y_MIN && hipsY <= HIPS_Y_MAX && Mathf.Abs(hipsZ) <= HIPS_Z_LIMIT;
        }

        public static float FallbackHipsY(float eyeHeight)
        {
            if (!(eyeHeight > 0f)) eyeHeight = 1.6f;
            return eyeHeight * FALLBACK_HIPS_PER_EYE;
        }

        /// <summary>新しい測定値を混ぜる（最初の 1 回はそのまま）。</summary>
        public static float BlendCalib(float old, float measured, bool hasOld)
        {
            if (!hasOld) return measured;
            return old + (measured - old) * CALIB_BLEND;
        }

        /// <summary>SeatAnchor の乗り物ローカルの高さ：腰（Hips）が鞍の高さ saddleY に来る位置 ＋ 微調整。</summary>
        public static float AnchorY(float saddleY, float hipsY, float userHeight)
        {
            return saddleY - hipsY + userHeight;
        }

        /// <summary>SeatAnchor の乗り物ローカルの前後：腰が鞍の前後 saddleZ に来る位置 ＋ 微調整。</summary>
        public static float AnchorZ(float saddleZ, float hipsZ, float userForward)
        {
            return saddleZ - hipsZ + userForward;
        }

        /// <summary>微調整の表示（「高さ +3cm」「前後 -1cm」「傾き +2°」）。</summary>
        public static string AxisLabel(int axis, float value)
        {
            string sign = value > 0.0001f ? "+" : (value < -0.0001f ? "-" : "±");
            if (axis == AXIS_TILT)
                return "傾き " + sign + Mathf.RoundToInt(Mathf.Abs(value)).ToString() + "°";
            int cm = Mathf.RoundToInt(Mathf.Abs(value) * 100f);
            return (axis == AXIS_FORWARD ? "前後 " : "高さ ") + sign + cm.ToString() + "cm";
        }
    }
}
