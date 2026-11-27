using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 騎乗ポーズの Humanoid AnimationClip と、VRCStation 用の AnimatorController（Station_Ride.controller）を作る。
    /// VRCStation.animatorController に入れると、座っている間アバターの Sitting レイヤーがこのコントローラーに置き換わり、
    /// 既定の「椅子に座る」ポーズの代わりに鞍にまたがるポーズになる（歩き・移動は Station の Immobilize で止まっている）。
    /// マッスル値は RidePose.cs（モデルは作らない。アニメーションだけ）。
    /// </summary>
    public static class RidePoseBuilder
    {
        public const string DIR = "Assets/HorseRace/Animations";
        public const string RELAXED_CLIP = DIR + "/RidePose_Relaxed.anim";
        public const string CROUCH_CLIP = DIR + "/RidePose_Crouch.anim";
        public const string STATION_CONTROLLER = DIR + "/Station_Ride.controller";

        /// <summary>クリップとコントローラーを作り直して返す（失敗したら null。Station は既定の座りポーズのまま）。</summary>
        public static RuntimeAnimatorController Build()
        {
            if (!AssetDatabase.IsValidFolder(DIR)) AssetDatabase.CreateFolder("Assets/HorseRace", "Animations");
            CheckMuscleNames(RidePose.RelaxedNames);
            CheckMuscleNames(RidePose.CrouchNames);
            if (RidePose.RelaxedNames.Length != RidePose.RelaxedValues.Length || RidePose.CrouchNames.Length != RidePose.CrouchValues.Length)
            {
                GenUtil.Warn("RidePose の名前と値の数が合いません（騎乗ポーズを作りません）");
                return null;
            }

            AnimationClip relaxed = MakeClip(RELAXED_CLIP, "RidePose_Relaxed", RidePose.RelaxedNames, RidePose.RelaxedValues);
            MakeClip(CROUCH_CLIP, "RidePose_Crouch", RidePose.RelaxedNames, RidePose.CrouchFull());

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(STATION_CONTROLLER) != null)
                AssetDatabase.DeleteAsset(STATION_CONTROLLER);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(STATION_CONTROLLER);
            if (ctrl == null) { GenUtil.Warn("Station_Ride.controller を作れませんでした"); return null; }
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState ride = sm.AddState("Ride", new Vector3(300f, 0f, 0f));
            ride.motion = relaxed;
            ride.writeDefaultValues = true;
            sm.defaultState = ride;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        /// <summary>Unity の HumanTrait.MuscleName に無い名前があれば警告（綴り違いのカーブは無視されてポーズが崩れる）。</summary>
        static void CheckMuscleNames(string[] names)
        {
            var known = new HashSet<string>(HumanTrait.MuscleName);
            foreach (string n in names)
                if (!known.Contains(n)) GenUtil.Warn("騎乗ポーズ: Unity のマッスル名にありません: " + n);
        }

        static AnimationClip MakeClip(string path, string name, string[] names, float[] values)
        {
            var clip = new AnimationClip();
            clip.name = name;
            clip.frameRate = 30f;
            for (int i = 0; i < names.Length; i++)
                SetConst(clip, RidePose.ClipProperty(names[i]), values[i]);
            // 体の中心（Body）：低め（腰 ≒ Station の入る位置）。向きはそのまま
            SetConst(clip, "RootT.x", 0f);
            SetConst(clip, "RootT.y", RidePose.ROOT_T_Y);
            SetConst(clip, "RootT.z", 0f);
            SetConst(clip, "RootQ.x", 0f);
            SetConst(clip, "RootQ.y", 0f);
            SetConst(clip, "RootQ.z", 0f);
            SetConst(clip, "RootQ.w", 1f);

            AnimationClipSettings st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = true;
            st.keepOriginalPositionY = true;
            st.keepOriginalOrientation = true;
            st.keepOriginalPositionXZ = true;
            st.loopBlendOrientation = true;
            st.loopBlendPositionY = true;
            st.loopBlendPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, st);

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static void SetConst(AnimationClip clip, string property, float value)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f, value));
        }
    }
}
