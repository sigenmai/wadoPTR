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
    ///   2. 毛色マテリアル（HorseMaterialBuilder）と Mount.controller（MountAnimatorBuilder）、騎乗ポーズ Station_Ride.controller（RidePoseBuilder）を作る
    ///   3. レイヤー Ground / Mount / Rider / Block を用意（WorldLayers）
    ///   4. ステージ ×3（Terrain・砂地・コース・見えない柵・飾り）、ロビー（町・受付・手配書・柵の馬・鐘・練習場・たまり場・掲示板・中継）
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
            Scene scene = SceneManager.GetActiveScene();
            var ctx = new WorldContext();
            try
            {
                if (!LayoutTools.GuardUnsavedEdits()) return;   // 手で直して保存していない配置があれば聞く
                if (!ProgramAssets.Ensure()) return;
                GenUtil.Reset();
                GenUtil.ResetFontCache();
                SfxBuilder.ResetCache();
                TerrainBuilder.ResetCache();

                Progress("毛色マテリアル・アニメーター", 0.01f);
                HorseMaterialBuilder.Create();
                MountAnimatorBuilder.Build();
                RidePoseBuilder.Build();   // VRCStation 用の騎乗ポーズ（MountBuilder が全頭の Station に入れる）
                if (!WorldLayers.Ensure()) return;

                foreach (GameObject g in scene.GetRootGameObjects())
                    if (g.name == GenUtil.ROOT_NAME) Object.DestroyImmediate(g);
                LayoutTools.EnsureCustomRoot();   // HorseRace_Custom（利用者の物）は消さない・触らない
                ctx.root = new GameObject(GenUtil.ROOT_NAME).transform;

                Progress("光・中継の素材", 0.02f);
                Lights(ctx);
                Atmosphere();
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
                    BarrierBuilder.Build(sb);   // コースの見えない柵（溝・近道を掘った後の地形に合わせる）
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
                Backdrop(ctx);
                StageCameraBounds(ctx);
                SceneDescriptor(ctx);
                // 飾りの手直し：計算どおりの姿勢を基準に保存してから、LayoutOverrides.json を当てる
                LayoutTools.WriteBaseline();
                LayoutTools.ApplyOverrides();
                GenUtil.FlushBehaviours();
            }
            catch (System.Exception e)
            {
                // エディタのコードなので try/catch してよい（U# の制約はランタイムだけ）。作りかけは残さない
                Debug.LogError("[HorseRace] ワールド生成中にエラー: " + e.Message + "\n" + e);
                if (ctx.root != null) Object.DestroyImmediate(ctx.root.gameObject);
                foreach (var l in ctx.disabledLights) if (l != null) l.SetActive(true);   // 無効にした既存のライトを戻す
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
                ctx.disabledLights.Add(l.gameObject);
                Debug.Log("[HorseRace] 既存の Directional Light を無効にしました（生成した太陽と二重になるため）: " + l.name);
            }
            // 太陽は高め（62°）：東西に走る峡谷の行の底まで照らし、壁の影を短くする（art-r1-01、LightingPlan）
            var sun = GenUtil.Child(ctx.root, "Sun", Vector3.zero);
            sun.transform.rotation = Quaternion.Euler(LightingPlan.SUN_EULER.x, LightingPlan.SUN_EULER.y, LightingPlan.SUN_EULER.z);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = LightingPlan.SunColor;
            light.intensity = LightingPlan.SUN_INTENSITY;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.55f, 0.50f);
            ctx.sun = light;
        }

        /// <summary>
        /// ステージごとの空気（art-r2-10）：太陽の上に StageAtmosphere を付け、LightingPlan.ForStage(0〜3) の値を書き込む
        /// （3 = ロビー）。実行時は RaceManager の state / stage を見て霧・環境光・太陽・影の色を入れ替える。
        /// </summary>
        static void StageAtmosphere(WorldContext ctx)
        {
            if (ctx.sun == null) return;
            var at = GenUtil.AddU<StageAtmosphere>(ctx.sun.gameObject);
            at.raceManager = ctx.raceManager;
            at.sun = ctx.sun;
            const int N = LightingPlan.LOBBY_LIGHT + 1;
            at.sunEuler = new Vector3[N]; at.sunColor = new Color[N]; at.sunIntensity = new float[N];
            at.fogColor = new Color[N]; at.fogStart = new float[N]; at.fogEnd = new float[N];
            at.ambientSky = new Color[N]; at.ambientEquator = new Color[N]; at.ambientGround = new Color[N]; at.shadowColor = new Color[N];
            for (int i = 0; i < N; i++)
            {
                LightingPlan.StageLight l = LightingPlan.ForStage(i);
                at.sunEuler[i] = l.sunEuler; at.sunColor[i] = l.sunColor; at.sunIntensity[i] = l.sunIntensity;
                at.fogColor[i] = l.fogColor; at.fogStart[i] = l.fogStart; at.fogEnd[i] = l.fogEnd;
                at.ambientSky[i] = l.ambientSky; at.ambientEquator[i] = l.ambientEquator; at.ambientGround[i] = l.ambientGround;
                at.shadowColor[i] = l.shadow;
            }
            // art-r4-10：ステージごとの空（荒野 = 青い天頂、砂漠 = 明るい青、境目 = 紫の夕空・大きな夕日）
            at.skyboxes = new Material[N];
            for (int i = 0; i < N; i++) at.skyboxes[i] = SkyMaterial(i);
            if (at.skyboxes[LightingPlan.LOBBY_LIGHT] != null) RenderSettings.skybox = at.skyboxes[LightingPlan.LOBBY_LIGHT];
            CloudDecks(ctx);
        }

        /// <summary>ステージ i（3 = ロビー）の空のマテリアル（Generated/Materials/M_Sky_i.mat、Skybox/Procedural）。地面側の色 = 霧 × 0.9。</summary>
        static Material SkyMaterial(int i)
        {
            var sh = Shader.Find("Skybox/Procedural");
            if (sh == null) return null;
            LightingPlan.StageLight l = LightingPlan.ForStage(i);
            string path = GenUtil.GEN_MATERIALS + "/M_Sky_" + i + ".mat";
            GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_SkyTint", l.skyTint);
            float gk = LightingPlan.SKY_GROUND_FROM_FOG;
            m.SetColor("_GroundColor", new Color(l.fogColor.r * gk, l.fogColor.g * gk, l.fogColor.b * gk, 1f));
            m.SetFloat("_AtmosphereThickness", l.atmosphere);
            m.SetFloat("_SunSize", l.sunSize);
            m.SetFloat("_Exposure", l.exposure);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 雲の層（art-r4-10）：ステージ・ロビーごとに 1 つのパーティクル（動かない大きな柔らかい板 14 枚、ステージの中心から 1000m 先の輪、
        /// 高さ 600〜800m、大きさ 300〜600m、ごくゆっくり流れる）。地平線の上に「空の手前の物」を置いて空の奥行きを出す。
        /// </summary>
        static void CloudDecks(WorldContext ctx)
        {
            var root = GenUtil.Child(ctx.root, "CloudDecks", Vector3.zero).transform;
            root.position = Vector3.zero;
            var centers = new System.Collections.Generic.List<Vector3>();
            foreach (var d in StageDefinitions.All) centers.Add(d.center);
            centers.Add(WorldBounds.LOBBY_CENTER);
            for (int k = 0; k < centers.Count; k++)
            {
                var ps = GenUtil.Particles(root, "CloudDeck_" + k, centers[k] + Vector3.up * LightingPlan.CLOUD_ALT_MIN, new Color(1f, 0.97f, 0.92f, 0.55f),
                    LightingPlan.CLOUD_SIZE_MIN, 0f, 100000f, true, 0f, LightingPlan.CLOUD_OUT_M);
                var main = ps.main;
                main.maxParticles = LightingPlan.CLOUD_COUNT;
                main.startSize = new ParticleSystem.MinMaxCurve(LightingPlan.CLOUD_SIZE_MIN, LightingPlan.CLOUD_SIZE_MAX);
                main.startSpeed = 0.4f;   // ごくゆっくり
                var em = ps.emission;
                em.rateOverTime = LightingPlan.CLOUD_COUNT;   // 最初の 1 秒で 14 枚（上限）出て、寿命が長いのでそのまま残る
                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Circle;   // ステージのまわりの輪（水平）
                sh.radius = LightingPlan.CLOUD_OUT_M;
                sh.radiusThickness = 0.2f;
                sh.rotation = new Vector3(-90f, 0f, 0f);
                sh.position = new Vector3(0f, 0f, 0f);
                var pos = ps.transform.position;
                ps.transform.position = new Vector3(pos.x, centers[k].y + 0.5f * (LightingPlan.CLOUD_ALT_MIN + LightingPlan.CLOUD_ALT_MAX), pos.z);
            }
        }

        // ---- 見えてはいけない場所の対処（Task 11c）----
        public const float FOG_START_M = LightingPlan.FOG_START_M, FOG_END_M = LightingPlan.FOG_END_M;
        public const float BROADCAST_FAR_CLIP = 1100f;     // 中継カメラ（1200m 以下）
        public const float PLAYER_FAR_CLIP = 1500f;        // 参照カメラ（VRChat のプレイヤーの視界の遠さ）
        public static readonly Color FogColor = LightingPlan.FogColor;
        public const string SKY_MAT = GenUtil.GEN_MATERIALS + "/M_DustySky.mat";

        /// <summary>砂ぼこりの霧（線形 350〜1100m、暖かい砂色）と、同じ色味の空・環境光。遠くの空白や背景の切れ目を霧で隠す。</summary>
        static void Atmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FOG_START_M;
            RenderSettings.fogEndDistance = FOG_END_M;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            // 環境光は明るい暖色（影の段が黒くつぶれない：LightingPlan.MinCanyonFloorShadowLuminance）
            RenderSettings.ambientSkyColor = LightingPlan.AmbientSky;
            RenderSettings.ambientEquatorColor = LightingPlan.AmbientEquator;
            RenderSettings.ambientGroundColor = LightingPlan.AmbientGround;
            var sh = Shader.Find("Skybox/Procedural");
            if (sh != null)
            {
                GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
                var m = AssetDatabase.LoadAssetAtPath<Material>(SKY_MAT);
                if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, SKY_MAT); }
                m.shader = sh;
                m.SetColor("_SkyTint", new Color(0.80f, 0.62f, 0.45f));
                m.SetColor("_GroundColor", FogColor);
                m.SetFloat("_AtmosphereThickness", 1.6f);
                m.SetFloat("_Exposure", 1.1f);
                EditorUtility.SetDirty(m);
                RenderSettings.skybox = m;
            }
            else GenUtil.Warn("Skybox/Procedural シェーダーが見つかりません（空は既定のまま）");
        }

        /// <summary>
        /// 遠景の山並み（DressingPlan.Backdrop、2 列）と、ワールド全体の下の外の地面（低解像度の Terrain：ステージの間・外側の空白を埋め、
        /// 山並みへ向かって上る。ステージ・ロビーの縁の高さに合わせて継ぎ目を見せない：TerrainBuilder.BuildFiller）。
        /// </summary>
        static void Backdrop(WorldContext ctx)
        {
            var root = GenUtil.Child(ctx.root, "Backdrop", Vector3.zero).transform;
            root.position = Vector3.zero;
            var plan = DressingPlan.Backdrop(7);
            Vector4 f = WorldBounds.FillerRect();
            Terrain filler = TerrainBuilder.BuildFiller(ctx, root);
            // ステージのすぐ外の岩（art-r2-09）は外の地面（縁の高さへ寄せた後）の上に置き直す
            foreach (var p in plan)
                if (p.group == "BackdropNear" && filler != null)
                    p.position = new Vector3(p.position.x, filler.SampleHeight(new Vector3(p.position.x, 0f, p.position.z)) + filler.transform.position.y
                        - WorldBounds.BACKDROP_SINK * p.height, p.position.z);
            DressingBuilder.Instantiate(plan, root, Vector3.zero, "Backdrop");
            // ロビーの見晴らしの切れ目の目印（art-r2-15）：外の地面の高さに合わせて置き、給水塔の上に煙の柱
            var vista = LobbyLayout.VistaHeroes(5);
            Vector3 lc = WorldBounds.LOBBY_CENTER;
            foreach (var p in vista)
            {
                float wx = lc.x + p.position.x, wz = lc.z + p.position.z;
                float g = filler != null ? filler.SampleHeight(new Vector3(wx, 0f, wz)) + filler.transform.position.y : lc.y;
                p.position = new Vector3(p.position.x, g - lc.y + p.position.y, p.position.z);
            }
            var vroot = GenUtil.Child(root, "LobbyVista", Vector3.zero).transform;
            vroot.position = Vector3.zero;
            DressingBuilder.Instantiate(vista, vroot, lc, "LobbyVista");
            foreach (var p in vista)
                if (p.part == DressingPlan.RANCH_TOWER)
                    GenUtil.SmokeColumn(vroot, "VistaSmoke", lc + p.position + Vector3.up * (p.height + 1f), new Color(0.30f, 0.27f, 0.26f, 0.6f), 45f, 4f);
            Debug.Log("[HorseRace] 遠景の山並み " + plan.Count + " 個（2 列）、外の地面（Terrain）" + (f.z - f.x).ToString("F0") + "×" + (f.w - f.y).ToString("F0") + "m");
        }

        /// <summary>中継カメラの範囲：各ステージの見えない壁の少し内側。</summary>
        static void StageCameraBounds(WorldContext ctx)
        {
            if (ctx.director == null) return;
            int n = ctx.stages.Count;
            var mn = new Vector3[n];
            var mx = new Vector3[n];
            float inset = TerrainBuilder.WALL_INSET_M + 5f;
            for (int i = 0; i < n; i++)
            {
                Vector3 o = ctx.stages[i].def.Origin;
                mn[i] = new Vector3(o.x + inset, 0f, o.z + inset);
                mx[i] = new Vector3(o.x + StageDefinitions.SIZE - inset, 0f, o.z + StageDefinitions.SIZE - inset);
            }
            ctx.director.stageBoundsMin = mn;
            ctx.director.stageBoundsMax = mx;
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
            // 順位だけを運ぶ小さな同期（system-r2-13）。raceManager は下の「全員の raceManager」でつなぐ
            var orderGo = GenUtil.Child(go.transform, "RaceOrder", Vector3.zero);
            rm.raceOrder = GenUtil.AddU<RaceOrder>(orderGo);
            StageAtmosphere(ctx);

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
            rm.hud = ctx.hud;
            rm.practiceArea = ctx.practiceArea;
            rm.pickCards = ctx.pickCards.ToArray();
            rm.pickStatusText = ctx.pickStatus;
            rm.recordText = ctx.recordText;   // system-r3-15
            // 観客の予想の結果（ui-r3-16）：新聞売りの声で鈴／鈍い音、予想の紙が跳ねる
            rm.crier = ctx.crier;
            if (ctx.crier != null) ctx.crier.pickPaper = ctx.pickStatus;
            // 練習の札（ui-r3-07）：練習用の馬に乗っている間も首の札、往復のタイム
            if (ctx.hud != null)
            {
                ctx.hud.practiceArea = ctx.practiceArea;
                ctx.hud.practiceLap = ctx.practiceLap;
            }
            if (ctx.practiceLap != null) ctx.practiceLap.hud = ctx.hud;

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
            // 酔い止め（ui-r2-18）は練習用の馬に乗っている間も効かせる
            foreach (var b in GenUtil.Behaviours) if (b is ComfortVignette cv) cv.practiceArea = ctx.practiceArea;

            // ---- 演出 ----
            ResultShots(ctx, pens);
            foreach (var nb in ctx.noticeBoards) if (nb.mode == 0) nb.director = ctx.director;
            foreach (var t in ctx.directorTags) t.director = ctx.director;
            // 囲いの掲示板の木札も含めて、選んでいる札の印を光らせる（system-r1-10）
            if (ctx.director != null) ctx.director.tags = ctx.directorTags.ToArray();
        }

        /// <summary>
        /// 中継の結果のカット（ui-r3-01）：ステージごとにゴールの門の 9m 後ろ・4m 上から囲いを見る空の Transform（goalShots）と、
        /// 表彰台の正面のカット（ShowBuilder.BuildPodium の PodiumShot）・回る中心（Step_1）。
        /// </summary>
        static void ResultShots(WorldContext ctx, Transform[] pens)
        {
            if (ctx.director == null) return;
            var shots = new Transform[ctx.stages.Count];
            for (int s = 0; s < ctx.stages.Count; s++)
            {
                var sb = ctx.stages[s];
                if (sb.goal == null) continue;
                Transform gate = sb.goal.transform;
                Vector3 back = new Vector3(gate.forward.x, 0f, gate.forward.z);
                if (back.sqrMagnitude < 1e-4f) back = Vector3.forward;
                back = back.normalized;
                Vector3 p = gate.position - back * 9f + Vector3.up * 4f;
                var go = GenUtil.World(sb.root != null ? sb.root : ctx.root, "GoalShot", p);
                Vector3 target = pens != null && s < pens.Length && pens[s] != null ? pens[s].position : gate.position + back * 20f;
                Vector3 look = target + Vector3.up * 1f - p;
                if (look.sqrMagnitude > 1e-3f) go.transform.rotation = Quaternion.LookRotation(look);
                shots[s] = go.transform;
            }
            ctx.director.goalShots = shots;
            if (ctx.podium != null)
            {
                ctx.director.podiumShot = GenUtil.FindDeep(ctx.podium.transform, "PodiumShot");
                ctx.director.podiumLook = GenUtil.FindDeep(ctx.podium.transform, "Step_1");
            }
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
            // 参照カメラ：プレイヤーの視界の遠さ（霧の向こうまで描かない）。利用者が別に設定していればそのまま
            if (desc.ReferenceCamera == null || desc.ReferenceCamera.transform.IsChildOf(ctx.root))
            {
                var rc = GenUtil.Child(ctx.root, "ReferenceCamera", ctx.lobbySpawn.position + Vector3.up * 1.6f);
                var cam = rc.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = PLAYER_FAR_CLIP;
                cam.enabled = false;
                desc.ReferenceCamera = rc;
            }
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
        /// showDialog=false はワンボタンセットアップ用（ダイアログを出さず、コンパイルを待って自動で再開する）
        public static bool Ensure(bool showDialog = true)
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
            // UdonSharp 1.x の UdonSharpProgramAsset.CompileAllCsPrograms(forceCompile, editorBuild) でコンパイルを始める。
            // API 名は記憶によるもので未検証。この名前が無い U# の版ではこの行がエディタのコンパイルエラーになるので、その場合は
            // この 1 行を消せばよい（Refresh の後、U# はプログラムアセットを自動でコンパイルする）。
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
            if (showDialog) EditorUtility.DisplayDialog("HorseRace/Build World",
                "UdonSharp のプログラムアセットを " + created + " 個作り、コンパイルを始めました。\n\n"
                + "完了の確認：Unity 右下の読み込み表示が止まり、Console に UdonSharp のコンパイルエラー（赤）が無いこと"
                + "（Assets/HorseRace/Scripts の .asset を選ぶと Inspector にコンパイル結果が出ます）。\n"
                + "確認できたら、もう一度「HorseRace/Build World」を実行してください。", "OK");
            return false;
        }
    }
}
