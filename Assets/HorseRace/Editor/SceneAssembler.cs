using System.Collections.Generic;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// メニュー「HorseRace/Build World」：開いているシーンに HorseRace_Generated を作り直す（前のものは消す）。
    ///   1. UdonSharp のプログラムアセットが無いスクリプトに作る（作ったときはコンパイル待ちのため一度止まる → もう一度実行）
    ///   2. 毛色マテリアル（HorseMaterialBuilder）と Mount.controller（MountAnimatorBuilder）を作る
    ///   3. レイヤー Ground / Mount / Rider / Block を用意（WorldLayers）
    ///   4. ステージ ×3（Terrain・砂地・コース・飾り）、ロビー（町・受付・手配書・柵の馬・鐘・練習場・たまり場・掲示板・中継）
    ///   5. 乗り物（レース用 8 頭は待機場所、練習用 2 頭は杭の横）、スターター、RaceManager
    ///   6. PLAYTEST の「セットアップ」に書かれた参照をすべてつなぐ（Wire）→ VRCSceneDescriptor・ライト
    ///   7. 最後に Validate を実行して結果を Console に出す
    /// 手作業でつなぐものはない（日本語の TMP フォントだけは Assets/HorseRace 以下に置いておくと自動で使う）。
    /// </summary>
    public static class SceneAssembler
    {
        public const int RACE_MOUNTS = 8;
        public const int PRACTICE_MOUNTS = 2;

        [MenuItem("HorseRace/Build World")]
        public static void BuildWorld()
        {
            if (!ProgramAssets.Ensure()) return;
            GenUtil.Reset();
            GenUtil.ResetFontCache();

            HorseMaterialBuilder.Create();
            MountAnimatorBuilder.Build();
            if (!WorldLayers.Ensure()) return;

            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject g in scene.GetRootGameObjects())
                if (g.name == GenUtil.ROOT_NAME) Object.DestroyImmediate(g);

            var ctx = new WorldContext();
            ctx.root = new GameObject(GenUtil.ROOT_NAME).transform;
            try
            {
                Progress("光・中継の素材", 0.02f);
                Lights(ctx);
                ShowBuilder.CreateAssets(ctx);

                for (int i = 0; i < StageDefinitions.All.Length; i++)
                {
                    StageDef def = StageDefinitions.All[i];
                    Progress("ステージ " + (i + 1) + "：地形", 0.05f + i * 0.2f);
                    StageBuild sb = TerrainBuilder.Build(def, ctx.root);
                    ctx.stages.Add(sb);
                    Progress("ステージ " + (i + 1) + "：コース", 0.10f + i * 0.2f);
                    CourseBuilder.Build(sb, ctx);
                    SetPieceBuilder.Build(sb, ctx);
                    Progress("ステージ " + (i + 1) + "：飾り", 0.15f + i * 0.2f);
                    DressingBuilder.BuildStage(sb, ctx);
                }

                Progress("ロビー", 0.66f);
                LobbyBuilder.Build(ctx);

                Progress("乗り物", 0.8f);
                Mounts(ctx);
                CourseBuilder.BuildStarter(ctx);

                Progress("つなぐ", 0.9f);
                Wire(ctx);
                SceneDescriptor(ctx);
                GenUtil.FlushBehaviours();
            }
            catch (System.Exception e)
            {
                // エディタのコードなので try/catch してよい（U# の制約はランタイムだけ）。作りかけは残さない
                Debug.LogError("[HorseRace] ワールド生成中にエラー: " + e.Message + "\n" + e);
                if (ctx.root != null) Object.DestroyImmediate(ctx.root.gameObject);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("HorseRace/Build World",
                    "ワールド生成中にエラーが起きました:\n" + e.Message + "\n\n作りかけの " + GenUtil.ROOT_NAME + " は削除しました。詳しくは Console を見てください。", "OK");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[HorseRace] Build World が完了しました（" + GenUtil.Behaviours.Count + " 個の UdonSharpBehaviour をつなぎました）");
            Validator.Run();
        }

        static void Progress(string what, float t) => EditorUtility.DisplayProgressBar("HorseRace/Build World", what, t);

        // ------------------------------------------------------------------

        static void Lights(WorldContext ctx)
        {
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l.type != LightType.Directional || l.transform.IsChildOf(ctx.root)) continue;
                if (!l.gameObject.activeSelf) continue;
                l.gameObject.SetActive(false);
                Debug.Log("[HorseRace] 既存の Directional Light を無効にしました（生成した太陽と二重になるため）: " + l.name);
            }
            var sun = GenUtil.Child(ctx.root, "Sun", Vector3.zero);
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.8f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.55f, 0.50f);
        }

        static void Mounts(WorldContext ctx)
        {
            var parent = GenUtil.World(ctx.root, "Mounts", Vector3.zero).transform;
            for (int i = 0; i < RACE_MOUNTS; i++)
            {
                Transform spot = i < ctx.parking.Count ? ctx.parking[i] : ctx.lobbySpawn;
                ctx.raceMounts.Add(MountBuilder.Build(ctx, parent, "Mount_" + i, spot.position, spot.eulerAngles.y, i, i, false));
            }
            for (int i = 0; i < PRACTICE_MOUNTS; i++)
            {
                Transform home = ctx.practiceHomes[i];
                // 練習用：slot は -1（レースの枠ではない）、casterId は 8・9（全頭で重複させない）
                var m = MountBuilder.Build(ctx, parent, "PracticeMount_" + i, home.position, home.eulerAngles.y, -1, RACE_MOUNTS + i, true);
                ctx.practiceMounts.Add(m);
            }
        }

        // ------------------------------------------------------------------
        // つなぐ（PLAYTEST の「セットアップ」すべて）
        // ------------------------------------------------------------------

        public static void Wire(WorldContext ctx)
        {
            var go = GenUtil.Child(ctx.root, "RaceManager", Vector3.zero);
            var rm = GenUtil.AddU<RaceManager>(go);
            ctx.raceManager = rm;

            // ---- 乗り物・コース ----
            rm.mounts = ctx.raceMounts.ToArray();
            var starts = new Transform[StageDefinitions.All.Length * RaceManager.MAX_SLOTS];
            var firsts = new Checkpoint[ctx.stages.Count];
            var pens = new Transform[ctx.stages.Count];
            for (int s = 0; s < ctx.stages.Count; s++)
            {
                for (int i = 0; i < RaceManager.MAX_SLOTS; i++) starts[s * RaceManager.MAX_SLOTS + i] = ctx.stages[s].startPoints[i];
                firsts[s] = ctx.stages[s].firstCheckpoint;
                pens[s] = ctx.stages[s].finishPen;
            }
            rm.startPoints = starts;
            rm.firstCheckpoints = firsts;
            rm.finishPens = pens;
            rm.mountParking = ctx.parking.ToArray();
            rm.lobbySpawn = ctx.lobbySpawn;

            // ---- Task 9/10 のフック ----
            rm.starter = ctx.starter;
            var reset = new List<UdonSharpBehaviour>();
            foreach (var c in ctx.casters) reset.Add(c);
            foreach (var r in ctx.courseRelics) reset.Add(r);
            foreach (var r in ctx.spareRelics) reset.Add(r);
            foreach (var r in ctx.practiceRelics) reset.Add(r);
            rm.stageResetTargets = reset.ToArray();
            var refill = new List<UdonSharpBehaviour>();
            foreach (var r in ctx.revolvers) refill.Add(r);
            rm.refillTargets = refill.ToArray();
            rm.spectator = ctx.director;

            // ---- ロビー / UI ----
            rm.ledgers = ctx.ledgers.ToArray();
            rm.rankBoards = ctx.rankBoards.ToArray();
            rm.posterCards = ctx.posterCards.ToArray();
            rm.mountPickers = ctx.mountPickers.ToArray();
            rm.startBell = ctx.startBell;
            rm.practiceArea = ctx.practiceArea;

            // ---- 全員の raceManager（このビルドで作った UdonSharpBehaviour のうち raceManager フィールドを持つもの）----
            foreach (var b in GenUtil.Behaviours)
            {
                if (b == null || b == rm) continue;
                FieldInfo f = b.GetType().GetField("raceManager", BindingFlags.Public | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(RaceManager)) f.SetValue(b, rm);
            }

            // ---- 能力・遺物 ----
            RelicPickup[] pool = ctx.spareRelics.ToArray();
            foreach (var c in ctx.casters)
            {
                c.relicPool = pool;               // 全頭同じ並び
                c.spectator = null;               // RaceManager.spectator を使う
                if (c.practiceMount) c.practiceArea = ctx.practiceArea;
            }
            foreach (var r in ctx.spareRelics) r.pool = pool;

            // ---- 練習場 ----
            for (int i = 0; i < ctx.practicePosts.Count; i++)
                ctx.practicePosts[i].mount = i < ctx.practiceMounts.Count ? ctx.practiceMounts[i] : null;
            if (ctx.practiceArea != null) ctx.practiceArea.posts = ctx.practicePosts.ToArray();

            // ---- 演出 ----
            foreach (var nb in ctx.noticeBoards) if (nb.mode == 0) nb.director = ctx.director;
            foreach (var t in ctx.directorTags) t.director = ctx.director;
        }

        /// <summary>
        /// VRCSceneDescriptor は HorseRace_Generated の外（シーン直下の "VRCWorld"）に 1 つだけ置き、作り直しでも消さない
        /// （PipelineManager のワールド ID を保つため）。既にあればスポーンだけ更新する。
        /// </summary>
        static void SceneDescriptor(WorldContext ctx)
        {
            VRCSceneDescriptor desc = null;
            foreach (var d in Object.FindObjectsOfType<VRCSceneDescriptor>(true))
                if (!d.transform.IsChildOf(ctx.root)) { desc = d; break; }
            if (desc == null)
            {
                var go = new GameObject("VRCWorld");   // シーン直下（親なし）
                go.transform.position = ctx.lobbySpawn.position;
                desc = go.AddComponent<VRCSceneDescriptor>();
                Debug.Log("[HorseRace] シーン直下に VRCWorld（VRCSceneDescriptor）を作りました。以後の Build World では作り直しません");
            }
            else Debug.Log("[HorseRace] 既存の VRCSceneDescriptor（" + desc.name + "）のスポーン位置をロビーに設定しました");
            desc.spawns = new[] { ctx.lobbySpawn };
            desc.RespawnHeightY = -60f;
            EditorUtility.SetDirty(desc);
        }
    }

    /// <summary>
    /// UdonSharp 1.1：UdonSharpBehaviour のスクリプトごとに UdonSharpProgramAsset が要る（PLAYTEST Task 10 の「Program Asset を作り」）。
    /// 無いものはスクリプトの隣に作る。作ったときは U# のコンパイルを待つ必要があるので false を返して Build World を止める。
    /// </summary>
    public static class ProgramAssets
    {
        public static bool Ensure()
        {
            var have = new HashSet<MonoScript>();
            foreach (string g in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (pa != null && pa.sourceCsScript != null) have.Add(pa.sourceCsScript);
            }
            int created = 0;
            foreach (string g in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/HorseRace/Scripts" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null || have.Contains(ms)) continue;
                System.Type t = ms.GetClass();
                if (t == null || !typeof(UdonSharpBehaviour).IsAssignableFrom(t)) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = ms;
                AssetDatabase.CreateAsset(asset, path.Substring(0, path.Length - 3) + ".asset");
                created++;
            }
            if (created == 0) return true;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // U# 1.x の UdonSharpProgramAsset.CompileAllCsPrograms(forceCompile, editorBuild) でコンパイルを始める（記憶にある API 名。
            // 無い版では Refresh 後に U# が自動でコンパイルする）
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
            EditorUtility.DisplayDialog("HorseRace/Build World",
                "UdonSharp のプログラムアセットを " + created + " 個作り、コンパイルを始めました。\n\n"
                + "完了の確認：Unity 右下の読み込み表示が止まり、Console に UdonSharp のコンパイルエラー（赤）が無いこと"
                + "（Assets/HorseRace/Scripts の .asset を選ぶと Inspector にコンパイル結果が出ます）。\n"
                + "確認できたら、もう一度「HorseRace/Build World」を実行してください。", "OK");
            return false;
        }
    }
}
