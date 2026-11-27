namespace HorseRace.EditorTools
{
    /// <summary>
    /// 騎乗ポーズ（ヒューマノイドのマッスル値）。UnityEditor に依存しない（tests/HorseRace.Tests で名前と範囲をテストする）。
    /// RidePoseBuilder がこれを AnimationClip のカーブ（Animator の float プロパティ）にして、VRCStation 用の AnimatorController を作る。
    ///
    /// 名前は UnityEngine.HumanTrait.MuscleName と同じ綴り（体のマッスルはそのままクリップのプロパティ名になる）。
    /// 指だけはクリップのプロパティ名が違う（"Left Index 1 Stretched" → "LeftHand.Index.1 Stretched"）ので ClipProperty で変換する。
    /// 値は -1..1（0 = 可動域の真ん中。脚の Front-Back は + で前へ、In-Out は + で外へ開く、Stretch は + で伸ばす／- で曲げる）。
    /// 値は Unity を使わずに決めた初期値。Unity の Avatar の Muscles &amp; Settings で見ながら、ここの数字を直して Build Ride Pose を作り直す。
    ///
    /// ポーズ：鞍にまたがる（太ももを前・外へ開いて胴を挟む、膝を曲げて鐙の高さ、かかとを下げる）、背中は少し前傾、
    /// 両腕は前へ出して肘を曲げ、両手で手綱を握る（指を曲げる）。VR では頭と手はトラッキングが優先される。
    /// </summary>
    public static class RidePose
    {
        /// <summary>体の中心（Body）の高さ。Humanoid のクリップでは人の大きさ（humanScale）で割った値。
        /// 低くして腰がほぼ Station の入る位置（＝原点）に来るようにする。最後は実行時の Hips の測定（SeatFit）で鞍に合わせる。</summary>
        public const float ROOT_T_Y = 0.08f;

        /// <summary>くつろいだ乗り方（ロビー・待機・常歩〜駆歩）。Station の Animator はこれを使う。</summary>
        public static readonly string[] RelaxedNames =
        {
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right",
            "Chest Front-Back", "UpperChest Front-Back",
            "Neck Nod Down-Up", "Head Nod Down-Up",
            "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
            "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out", "Left Foot Up-Down", "Left Foot Twist In-Out", "Left Toes Up-Down",
            "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out",
            "Right Lower Leg Stretch", "Right Lower Leg Twist In-Out", "Right Foot Up-Down", "Right Foot Twist In-Out", "Right Toes Up-Down",
            "Left Shoulder Down-Up", "Left Shoulder Front-Back", "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out",
            "Left Forearm Stretch", "Left Forearm Twist In-Out", "Left Hand Down-Up", "Left Hand In-Out",
            "Right Shoulder Down-Up", "Right Shoulder Front-Back", "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out",
            "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out",
            "Left Thumb 1 Stretched", "Left Thumb Spread", "Left Thumb 2 Stretched", "Left Thumb 3 Stretched",
            "Left Index 1 Stretched", "Left Index Spread", "Left Index 2 Stretched", "Left Index 3 Stretched",
            "Left Middle 1 Stretched", "Left Middle Spread", "Left Middle 2 Stretched", "Left Middle 3 Stretched",
            "Left Ring 1 Stretched", "Left Ring Spread", "Left Ring 2 Stretched", "Left Ring 3 Stretched",
            "Left Little 1 Stretched", "Left Little Spread", "Left Little 2 Stretched", "Left Little 3 Stretched",
            "Right Thumb 1 Stretched", "Right Thumb Spread", "Right Thumb 2 Stretched", "Right Thumb 3 Stretched",
            "Right Index 1 Stretched", "Right Index Spread", "Right Index 2 Stretched", "Right Index 3 Stretched",
            "Right Middle 1 Stretched", "Right Middle Spread", "Right Middle 2 Stretched", "Right Middle 3 Stretched",
            "Right Ring 1 Stretched", "Right Ring Spread", "Right Ring 2 Stretched", "Right Ring 3 Stretched",
            "Right Little 1 Stretched", "Right Little Spread", "Right Little 2 Stretched", "Right Little 3 Stretched",
        };

        public static readonly float[] RelaxedValues =
        {
            0.15f, 0f, 0f,
            0.05f, 0f,
            0f, 0f,
            // 左脚：太もも前へ 0.6・外へ 0.5（胴をまたぐ）、膝は可動域の真ん中（約 70〜80° 曲げ）、かかとを下げる
            0.6f, 0.5f, 0f,
            0f, 0f, 0.2f, 0f, 0f,
            // 右脚（左右対称）
            0.6f, 0.5f, 0f,
            0f, 0f, 0.2f, 0f, 0f,
            // 左腕：腕を下ろして前へ、肘を曲げて手綱の位置（へその前、鞍の前橋の上）
            0f, 0f, -0.6f, 0.4f, 0f,
            0.1f, 0.3f, 0f, 0f,
            0f, 0f, -0.6f, 0.4f, 0f,
            0.1f, 0.3f, 0f, 0f,
            // 指：手綱を握る（Stretched を負＝曲げる、親指は軽く）
            -0.3f, 0f, -0.2f, -0.2f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.3f, 0f, -0.2f, -0.2f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
            -0.6f, 0f, -0.7f, -0.6f,
        };

        /// <summary>レースの前傾（疾走の構え）。Relaxed との差分だけ持つ（同じ名前の値を置き換える）。
        /// VRChat は Udon から Station の Animator のパラメーターを動かせないので、今は同じアセットに別クリップとして作るだけ（Station は Relaxed を使う）。</summary>
        public static readonly string[] CrouchNames =
        {
            "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back", "Neck Nod Down-Up",
            "Left Upper Leg Front-Back", "Right Upper Leg Front-Back", "Left Lower Leg Stretch", "Right Lower Leg Stretch",
            "Left Arm Front-Back", "Right Arm Front-Back", "Left Forearm Stretch", "Right Forearm Stretch",
        };

        public static readonly float[] CrouchValues =
        {
            0.45f, 0.3f, 0.15f, -0.25f,
            0.8f, 0.8f, -0.25f, -0.25f,
            0.6f, 0.6f, -0.1f, -0.1f,
        };

        /// <summary>HumanTrait.MuscleName の名前 → AnimationClip の Animator プロパティ名（指だけ "LeftHand.Index.1 Stretched" 形式）。</summary>
        public static string ClipProperty(string muscle)
        {
            string side = null;
            if (muscle.StartsWith("Left ")) side = "LeftHand";
            else if (muscle.StartsWith("Right ")) side = "RightHand";
            if (side == null) return muscle;
            string rest = muscle.Substring(muscle.IndexOf(' ') + 1);   // "Index 1 Stretched" / "Index Spread"
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (string f in fingers)
            {
                if (!rest.StartsWith(f + " ")) continue;
                string tail = rest.Substring(f.Length + 1);   // "1 Stretched" / "Spread"
                if (tail == "Spread") return side + "." + f + ".Spread";
                return side + "." + f + "." + tail;
            }
            return muscle;
        }

        /// <summary>Relaxed に Crouch の差分を重ねた値（名前は RelaxedNames の順）。</summary>
        public static float[] CrouchFull()
        {
            var v = (float[])RelaxedValues.Clone();
            for (int i = 0; i < CrouchNames.Length; i++)
            {
                int k = System.Array.IndexOf(RelaxedNames, CrouchNames[i]);
                if (k >= 0) v[k] = CrouchValues[i];
            }
            return v;
        }
    }
}
