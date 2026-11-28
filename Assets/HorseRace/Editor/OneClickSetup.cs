using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ワンボタンセットアップ：TMP の素材 → シーン作成・保存 → UdonSharp のプログラムアセット（コンパイル待ち）
    /// → Build World → シーン保存 を、待ちを挟みながら自動で最後まで進める。
    ///
    /// 始め方は 2 つ：
    ///   - メニュー「HorseRace/★ ワンボタンで全部やる」
    ///   - HorseRace_Install.bat が置く目印ファイル Assets/HorseRace/.oneclick（Unity は「.」始まりを取り込まない）。
    ///     スクリプトの読み込み後にこれを見つけると、聞かずに自動で始める（bat のダブルクリックだけで完成させるため）
    /// 目印もなく、まだ一度も聞いていないプロジェクトでは、最初の 1 回だけ「作りますか？」と聞く。
    ///
    /// コンパイルやパッケージの読み込みで中断されるので、進行中かどうかは SessionState に持ち、
    /// スクリプトの再読み込み（[InitializeOnLoad]）のあとで続きから再開する。
    /// </summary>
    [InitializeOnLoad]
    public static class OneClickSetup
    {
        public const string SCENE_PATH = "Assets/Scenes/HorseRace.unity";
        const string PENDING = "HorseRace.OneClick.Step";     // 0 = 進行中でない、1 以上 = 試した回数 + 1
        const int MAX_STEPS = 10;                              // 何度やっても進まないときに止める
        const double SETTLE_SECONDS = 4.0;                     // コンパイル・読み込みが止まってから待つ秒数
        static double resumeAt;

        static string MarkerPath => Path.Combine(Application.dataPath, "HorseRace/.oneclick");
        static string OfferedKey => "HorseRace.OneClick.Offered." + Application.dataPath;

        static OneClickSetup()
        {
            EditorApplication.delayCall += OnLoad;
        }

        static void OnLoad()
        {
            if (Application.isBatchMode) return;
            if (SessionState.GetInt(PENDING, 0) > 0) { Schedule(); return; }   // 途中で再読み込みされた：続きから

            if (File.Exists(MarkerPath))
            {
                File.Delete(MarkerPath);
                EditorPrefs.SetBool(OfferedKey, true);
                Debug.Log("[HorseRace] インストーラーの目印を見つけたので、ワンボタンセットアップを自動で始めます。");
                Run();
                return;
            }
            if (EditorPrefs.GetBool(OfferedKey, false)) return;
            EditorPrefs.SetBool(OfferedKey, true);
            if (WorldInScene()) return;
            if (EditorUtility.DisplayDialog("荒野横断ホースレース",
                    "ワールドを自動で組み立てますか？\n\n"
                    + "TextMeshPro の素材の読み込み・シーンの作成・UdonSharp のコンパイル・ワールド生成までを、"
                    + "待ち時間を挟みながら最後まで自動で進めます（数分かかります）。",
                    "作る", "あとで（メニュー HorseRace/★ ワンボタンで全部やる）"))
                Run();
        }

        [MenuItem("HorseRace/★ ワンボタンで全部やる", false, 0)]
        public static void Run()
        {
            SessionState.SetInt(PENDING, 1);
            Step();
        }

        static void Schedule()
        {
            resumeAt = EditorApplication.timeSinceStartup + SETTLE_SECONDS;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                resumeAt = EditorApplication.timeSinceStartup + SETTLE_SECONDS;   // 落ち着くまで待ち直す
                return;
            }
            if (EditorApplication.timeSinceStartup < resumeAt) return;
            EditorApplication.update -= Tick;
            Step();
        }

        static void Finish() => SessionState.EraseInt(PENDING);

        static void Fail(string msg)
        {
            Finish();
            EditorUtility.ClearProgressBar();
            Debug.LogError("[HorseRace] ワンボタンセットアップ: " + msg);
            EditorUtility.DisplayDialog("HorseRace/★ ワンボタンで全部やる", msg
                + "\n\nConsole（Window → General → Console）の赤い行をそのまま Claude に送ってください。", "OK");
        }

        static void Step()
        {
            int n = SessionState.GetInt(PENDING, 0);
            if (n <= 0) return;
            if (n > MAX_STEPS) { Fail("何度試しても先へ進めませんでした（コンパイルエラーが残っている可能性があります）。"); return; }
            SessionState.SetInt(PENDING, n + 1);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Finish();
                EditorUtility.DisplayDialog("HorseRace", "Play モードを止めてから、もう一度メニュー「HorseRace/★ ワンボタンで全部やる」を押してください。", "OK");
                return;
            }

            // 1. TextMeshPro の素材（TMP Settings）。読み込みはあとで再読み込みが走るので、待ってから続きへ
            if (!TmpReady())
            {
                if (!ImportTmp()) { Fail("TextMeshPro の素材（TMP Essential Resources）が見つかりませんでした。メニュー「Window → TextMeshPro → Import TMP Essential Resources」を押してから、もう一度実行してください。"); return; }
                Debug.Log("[HorseRace] (1/4) TextMeshPro の素材を読み込みました。読み込みが終わるのを待っています…");
                Schedule();
                return;
            }

            // 2. シーン
            if (!EnsureScene()) { Finish(); Debug.Log("[HorseRace] シーンの保存がキャンセルされたので止めました。"); return; }

            // 3. UdonSharp のプログラムアセット。作ったときはコンパイル待ち
            if (!ProgramAssets.Ensure(false))
            {
                if (ProgramAssets.LastError != null) { Fail(ProgramAssets.LastError); return; }
                Debug.Log("[HorseRace] (3/4) UdonSharp のプログラムアセットを作りました。コンパイルが終わるのを待っています…");
                Schedule();
                return;
            }

            // 4. ワールド生成（最後に Validate も走る）
            Debug.Log("[HorseRace] (4/4) Build World を実行します。");
            Finish();
            SceneAssembler.BuildWorld();
            if (!WorldInScene()) { Fail("Build World が最後まで進みませんでした。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("荒野横断ホースレース", "ワールドができました（シーン " + scene.path + " に保存済み）。\n\n"
                + "Console の Validate のまとめを確認してください。\n"
                + "次は「VRChat SDK → Show Control Panel → Builder」で Number of Clients を 2 にして「Build & Test」。", "OK");
        }

        static bool WorldInScene()
        {
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == GenUtil.ROOT_NAME) return true;
            return false;
        }

        static bool TmpReady() => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        static bool ImportTmp()
        {
            // Unity 2022.3（VRChat）は com.unity.textmeshpro、Unity 6 以降は com.unity.ugui に同梱
            string[] candidates =
            {
                "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage",
                "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage",
            };
            foreach (string c in candidates)
            {
                string full = Path.GetFullPath(c);
                if (!File.Exists(full)) continue;
                AssetDatabase.ImportPackage(full, false);
                return true;
            }
            return false;
        }

        /// 今のシーンにワールドがあればそのまま。無ければ Assets/Scenes/HorseRace.unity を開く（無ければ作って保存）
        static bool EnsureScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == SCENE_PATH || WorldInScene()) return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (File.Exists(Path.GetFullPath(SCENE_PATH)))
            {
                EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
                Debug.Log("[HorseRace] (2/4) シーン " + SCENE_PATH + " を開きました。");
                return true;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(s, SCENE_PATH);
            Debug.Log("[HorseRace] (2/4) シーン " + SCENE_PATH + " を作って保存しました。");
            return true;
        }
    }
}
