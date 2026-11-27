using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// メニュー「HorseRace/Build Mount Animator」で Assets/HorseRace/Animations/Mount.controller を作る。
    /// パラメータ: int "Gait"（0 Idle, 1 Walk, 2 Trot, 3 Canter, 4 Gallop）、trigger "Jump" / "Stagger"、bool "Stamp"。
    /// クリップは Horse.fbx から取る（Blender 出力の "Armature|Walk" 形式は '|' の後ろで照合）。
    /// Camel / SkeletonHorse はボーン名・クリップ名が同じなので、同じコントローラを MountVisual の各 Animator に割り当てる。
    /// 3 つの FBX のループ設定（歩様・Idle・Stamp をループ）もここで直す。
    /// </summary>
    public static class MountAnimatorBuilder
    {
        const string OutDir = "Assets/HorseRace/Animations";
        const string OutPath = OutDir + "/Mount.controller";
        const string HorseFbx = "Assets/HorseRace/Models/Horse.fbx";
        static readonly string[] AllFbx =
        {
            "Assets/HorseRace/Models/Horse.fbx",
            "Assets/HorseRace/Models/Camel.fbx",
            "Assets/HorseRace/Models/SkeletonHorse.fbx",
        };
        static readonly string[] GaitClips = { "Idle", "Walk", "Trot", "Canter", "Gallop" };
        static readonly string[] LoopClips = { "Idle", "Walk", "Trot", "Canter", "Gallop", "Stamp" };
        const float Fade = 0.2f;

        [MenuItem("HorseRace/Build Mount Animator")]
        public static void Build()
        {
            foreach (string fbx in AllFbx) FixLoopSettings(fbx);

            Dictionary<string, AnimationClip> clips = LoadClips(HorseFbx);
            if (clips.Count == 0)
            {
                Debug.LogError("[HorseRace] Horse.fbx にアニメーションクリップが見つかりません: " + HorseFbx);
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/HorseRace", "Animations");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(OutPath) != null)
                AssetDatabase.DeleteAsset(OutPath);

            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(OutPath);
            ctrl.AddParameter("Gait", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Stagger", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Stamp", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            var gaitStates = new AnimatorState[GaitClips.Length];
            for (int i = 0; i < GaitClips.Length; i++)
            {
                gaitStates[i] = sm.AddState(GaitClips[i], new Vector3(300f, 60f * i, 0f));
                gaitStates[i].motion = Clip(clips, GaitClips[i]);
            }
            sm.defaultState = gaitStates[0];

            AnimatorState jump = sm.AddState("Jump", new Vector3(600f, 0f, 0f));
            jump.motion = Clip(clips, "Jump");
            AnimatorState stagger = sm.AddState("Stagger", new Vector3(600f, 80f, 0f));
            stagger.motion = Clip(clips, "Stagger");
            AnimatorState stamp = sm.AddState("Stamp", new Vector3(0f, 0f, 0f));
            stamp.motion = Clip(clips, "Stamp");

            // 歩様 ↔ 歩様（Gait の値で直接遷移、0.2 秒クロスフェード）
            for (int from = 0; from < gaitStates.Length; from++)
            {
                for (int to = 0; to < gaitStates.Length; to++)
                {
                    if (from == to) continue;
                    AnimatorStateTransition t = gaitStates[from].AddTransition(gaitStates[to]);
                    Setup(t, false);
                    t.AddCondition(AnimatorConditionMode.Equals, to, "Gait");
                }
            }

            // 足踏み（Idle 中のみ）
            AnimatorStateTransition toStamp = gaitStates[0].AddTransition(stamp);
            Setup(toStamp, false);
            toStamp.AddCondition(AnimatorConditionMode.If, 0, "Stamp");
            toStamp.AddCondition(AnimatorConditionMode.Equals, 0, "Gait");
            AnimatorStateTransition stampBack = stamp.AddTransition(gaitStates[0]);
            Setup(stampBack, false);
            stampBack.AddCondition(AnimatorConditionMode.IfNot, 0, "Stamp");
            for (int to = 1; to < gaitStates.Length; to++)
            {
                AnimatorStateTransition t = stamp.AddTransition(gaitStates[to]);
                Setup(t, false);
                t.AddCondition(AnimatorConditionMode.Equals, to, "Gait");
            }

            // Jump / Stagger は Any State から。終わったら現在の Gait に戻る
            AnimatorStateTransition anyJump = sm.AddAnyStateTransition(jump);
            Setup(anyJump, false);
            anyJump.canTransitionToSelf = false;
            anyJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            AnimatorStateTransition anyStagger = sm.AddAnyStateTransition(stagger);
            Setup(anyStagger, false);
            anyStagger.canTransitionToSelf = false;
            anyStagger.AddCondition(AnimatorConditionMode.If, 0, "Stagger");

            AnimatorState[] oneShots = { jump, stagger };
            foreach (AnimatorState s in oneShots)
            {
                for (int to = 0; to < gaitStates.Length; to++)
                {
                    AnimatorStateTransition t = s.AddTransition(gaitStates[to]);
                    Setup(t, true);
                    t.AddCondition(AnimatorConditionMode.Equals, to, "Gait");
                }
            }

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log("[HorseRace] Mount.controller を作成しました: " + OutPath);
        }

        static void Setup(AnimatorStateTransition t, bool exitTime)
        {
            t.hasFixedDuration = true;
            t.duration = Fade;
            t.hasExitTime = exitTime;
            t.exitTime = exitTime ? 0.9f : 0f;
            t.offset = 0f;
        }

        static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name)
        {
            AnimationClip c;
            if (clips.TryGetValue(name, out c)) return c;
            Debug.LogWarning("[HorseRace] クリップ '" + name + "' が Horse.fbx にありません。空のステートになります。");
            return null;
        }

        static string ShortName(string clipName)
        {
            int bar = clipName.LastIndexOf('|');
            return bar >= 0 ? clipName.Substring(bar + 1) : clipName;
        }

        static Dictionary<string, AnimationClip> LoadClips(string fbxPath)
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                AnimationClip clip = o as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                string key = ShortName(clip.name);
                if (!result.ContainsKey(key)) result.Add(key, clip);
            }
            return result;
        }

        static void FixLoopSettings(string fbxPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[HorseRace] FBX が見つかりません: " + fbxPath);
                return;
            }
            ModelImporterClipAnimation[] src = importer.clipAnimations;
            if (src == null || src.Length == 0) src = importer.defaultClipAnimations;
            bool changed = false;
            foreach (ModelImporterClipAnimation c in src)
            {
                string shortName = ShortName(c.name);
                bool loop = System.Array.IndexOf(LoopClips, shortName) >= 0;
                if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
            }
            if (changed || importer.clipAnimations == null || importer.clipAnimations.Length == 0)
            {
                importer.clipAnimations = src;
                importer.SaveAndReimport();
            }
        }
    }
}
