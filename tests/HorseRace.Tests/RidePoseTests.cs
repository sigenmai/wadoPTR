using System.Collections.Generic;
using NUnit.Framework;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class RidePoseTests
    {
        /// <summary>UnityEngine.HumanTrait.MuscleName（Unity 2022.3、95 個）をそのまま写したもの。</summary>
        public static readonly string[] UnityMuscleNames =
        {
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right",
            "Chest Front-Back", "Chest Left-Right", "Chest Twist Left-Right",
            "UpperChest Front-Back", "UpperChest Left-Right", "UpperChest Twist Left-Right",
            "Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right",
            "Head Nod Down-Up", "Head Tilt Left-Right", "Head Turn Left-Right",
            "Left Eye Down-Up", "Left Eye In-Out", "Right Eye Down-Up", "Right Eye In-Out",
            "Jaw Close", "Jaw Left-Right",
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

        [Test] public void UnityList_Has95() => Assert.AreEqual(95, UnityMuscleNames.Length);

        [Test] public void AllPoseNames_AreUnityMuscles()
        {
            var known = new HashSet<string>(UnityMuscleNames);
            foreach (string n in RidePose.RelaxedNames) Assert.IsTrue(known.Contains(n), n);
            foreach (string n in RidePose.CrouchNames) Assert.IsTrue(known.Contains(n), n);
        }

        [Test] public void NamesAndValues_Match_NoDuplicates()
        {
            Assert.AreEqual(RidePose.RelaxedNames.Length, RidePose.RelaxedValues.Length);
            Assert.AreEqual(RidePose.CrouchNames.Length, RidePose.CrouchValues.Length);
            Assert.AreEqual(RidePose.RelaxedNames.Length, new HashSet<string>(RidePose.RelaxedNames).Count);
            foreach (string n in RidePose.CrouchNames) CollectionAssert.Contains(RidePose.RelaxedNames, n);
        }

        [Test] public void Values_InMuscleRange()
        {
            foreach (float v in RidePose.RelaxedValues) { Assert.GreaterOrEqual(v, -1f); Assert.LessOrEqual(v, 1f); }
            foreach (float v in RidePose.CrouchFull()) { Assert.GreaterOrEqual(v, -1f); Assert.LessOrEqual(v, 1f); }
        }

        [Test] public void Pose_IsSymmetric_AndStraddles()
        {
            var d = new Dictionary<string, float>();
            for (int i = 0; i < RidePose.RelaxedNames.Length; i++) d[RidePose.RelaxedNames[i]] = RidePose.RelaxedValues[i];
            foreach (var kv in d)
                if (kv.Key.StartsWith("Left ")) Assert.AreEqual(kv.Value, d["Right " + kv.Key.Substring(5)], kv.Key);
            Assert.Greater(d["Left Upper Leg In-Out"], 0.3f);      // 脚を開いて胴をまたぐ
            Assert.Greater(d["Left Upper Leg Front-Back"], 0.3f);  // 太ももは前へ
            Assert.Greater(d["Left Arm Front-Back"], 0f);          // 手綱は前
            Assert.Less(d["Left Index 2 Stretched"], 0f);           // 握る
        }

        [Test] public void Crouch_LeansFurther()
        {
            float[] c = RidePose.CrouchFull();
            int k = System.Array.IndexOf(RidePose.RelaxedNames, "Spine Front-Back");
            Assert.Greater(c[k], RidePose.RelaxedValues[k]);
        }

        [Test] public void ClipProperty_FingersUseHandPrefix_BodyUnchanged()
        {
            Assert.AreEqual("LeftHand.Index.1 Stretched", RidePose.ClipProperty("Left Index 1 Stretched"));
            Assert.AreEqual("RightHand.Thumb.Spread", RidePose.ClipProperty("Right Thumb Spread"));
            Assert.AreEqual("RightHand.Little.3 Stretched", RidePose.ClipProperty("Right Little 3 Stretched"));
            Assert.AreEqual("Left Upper Leg In-Out", RidePose.ClipProperty("Left Upper Leg In-Out"));
            Assert.AreEqual("Spine Front-Back", RidePose.ClipProperty("Spine Front-Back"));
            Assert.AreEqual("Right Hand Down-Up", RidePose.ClipProperty("Right Hand Down-Up"));
        }
    }
}
