using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// メニュー「HorseRace/Validate」：生成したワールドを確認して、日本語のまとめを Console に出す。
    ///   ステージごと：コース全長 2.2〜2.8km、コース上の最大勾配 ≤ 25°、CP の数とつながり（ゴール 1 つ、next の連鎖）、
    ///                CP の線分からのコースのずれ ≤ 60m（コース外リセットの誤作動防止）、砂地の割合、SandZone、遺物 4 個
    ///   素材の欠け（Models/ の必要なファイル）、Build World 中の警告
    ///   つなぎ忘れ：生成したすべての UdonSharpBehaviour について、PLAYTEST で必須の参照が空でないか
    /// </summary>
    public static class Validator
    {
        // PLAYTEST の「セットアップ」で必須の参照（型名 → フィールド名）
        static readonly Dictionary<Type, string[]> Required = new Dictionary<Type, string[]>
        {
            { typeof(RaceManager), new[] { "mounts", "startPoints", "firstCheckpoints", "finishPens", "lobbySpawn", "starter", "stageResetTargets",
                "refillTargets", "spectator", "ledgers", "rankBoards", "posterCards", "mountPickers", "startBell", "practiceArea" } },
            { typeof(MountController), new[] { "station", "seatAnchor", "visual", "vrInput" } },
            { typeof(MountVisual), new[] { "horseModel", "camelModel", "skeletonModel", "horseAnimator", "camelAnimator", "skeletonAnimator",
                "horseController", "camelController", "skeletonController", "horseCoatRenderers", "coatMaterials", "breathAudio" } },
            { typeof(MountInputVR), new[] { "mount", "seatAnchor" } },
            { typeof(AbilityCaster), new[] { "mount", "raceManager", "pouch", "revolver", "dustCloud", "lasso", "rockWall", "balls", "relicPool" } },
            { typeof(CharmPouch), new[] { "pouchRenderer", "readyMaterial", "idleMaterial", "levelMarks" } },
            { typeof(RevolverHolder), new[] { "caster", "pickup", "cylinder", "hammer", "triggerPart", "bullets", "muzzle" } },
            { typeof(DustCloud), new[] { "visual", "particles" } },
            { typeof(LassoEffect), new[] { "line" } },
            { typeof(RockWall), new[] { "segments" } },
            { typeof(IronBall), new[] { "visual" } },
            { typeof(RelicPickup), new[] { "raceManager", "visual" } },
            { typeof(EntryLedger), new[] { "raceManager", "pageText" } },
            { typeof(MountPicker), new[] { "raceManager", "visual" } },
            { typeof(PosterCard), new[] { "raceManager", "posterVisual" } },
            { typeof(StartBell), new[] { "raceManager" } },
            { typeof(PracticeArea), new[] { "posts" } },
            { typeof(PracticeHorsePost), new[] { "raceManager", "mount", "home" } },
            { typeof(PracticeDummy), new[] { "pivot" } },
            { typeof(RankBoard), new[] { "raceManager", "boardText" } },
            { typeof(MinimalHud), new[] { "raceManager", "hudRoot", "rankText", "timeText" } },
            { typeof(PocketWatch), new[] { "raceManager", "watchRoot", "faceText" } },
            { typeof(SpectatorDirector), new[] { "raceManager", "broadcastCamera", "renderTexture", "tags" } },
            { typeof(DirectorTag), new[] { "director" } },
            { typeof(NoticeBoardDisplay), new[] { "raceManager", "panelRenderer", "broadcastMaterial", "parchmentMaterial", "boardText" } },
            { typeof(Starter), new[] { "raceManager", "flagPivot", "audioSource", "stageSpots" } },
            { typeof(GoalPresenter), new[] { "raceManager", "confetti", "bellAudio", "placeTags", "placeTagTexts" } },
            { typeof(Podium), new[] { "raceManager", "steps", "fireworks" } },
            { typeof(TownCrier), new[] { "raceManager", "boards" } },
        };

        static readonly string[] RequiredAssets =
        {
            GenUtil.HORSE_FBX, GenUtil.CAMEL_FBX, GenUtil.SKELETON_FBX,
            GenUtil.MODELS + "/Packs/WesternTown/WesternTown.fbx", GenUtil.MODELS + "/Packs/Cliffs/Cliffs.fbx",
            GenUtil.MODELS + "/Packs/DesertRocks/DesertRocks.fbx", GenUtil.MODELS + "/Packs/RockCollection/RockCollection.fbx",
            GenUtil.MODELS + "/Packs/DesertProps/DesertProps.fbx", GenUtil.MODELS + "/Packs/Cacti/Cacti.fbx",
            GenUtil.MODELS + "/Packs/Campfire/Campfire.fbx", GenUtil.MODELS + "/Packs/WoodLogs/WoodLogs.fbx",
            GenUtil.MODELS + "/Packs/HayBales/HayBales.fbx", GenUtil.MODELS + "/Packs/FarmProps/FarmProps.fbx",
            GenUtil.MODELS + "/Packs/Gems/Gems.fbx", GenUtil.MODELS + "/Packs/Revolver/Revolver.fbx",
            GenUtil.MODELS + "/Packs/WantedPosters/WantedPosters.fbx",
            GenUtil.WANTED_CARDS + "/Wanted_Horse.png", GenUtil.WANTED_CARDS + "/Wanted_Camel.png", GenUtil.WANTED_CARDS + "/Wanted_Dust.png",
            GenUtil.WANTED_CARDS + "/Wanted_Lasso.png", GenUtil.WANTED_CARDS + "/Wanted_Wall.png", GenUtil.WANTED_CARDS + "/Wanted_Ball.png",
            GenUtil.WANTED_CARDS + "/Wanted_Original.png",
            GenUtil.MODELS + "/Props/NoticeBoard/NoticeBoard.fbx", GenUtil.MODELS + "/Props/Podium/Podium.fbx",
            GenUtil.MODELS + "/Props/MesaRock/MesaRock.fbx", GenUtil.MODELS + "/Props/Stable/Stable.fbx",
            GenUtil.MODELS + "/Props/Tent/Tent.fbx", GenUtil.MODELS + "/Props/DeadTree/DeadTree.fbx",
            GenUtil.MODELS + "/Props/AcaciaGrove/AcaciaGrove.fbx", GenUtil.MODELS + "/Props/CactusGroup/CactusGroup.fbx",
            GenUtil.MODELS + "/ThirdParty/MesaRockB/MesaRockB.fbx", GenUtil.MODELS + "/ThirdParty/RockSetStylized/RockSetStylized.fbx",
            GenUtil.MODELS + "/Buildings/TownBlock.fbx", GenUtil.MODELS + "/Buildings/SaloonFacade.fbx", GenUtil.MODELS + "/Buildings/RanchCompound.fbx",
            MountBuilder.MOUNT_CONTROLLER, MountBuilder.CAMEL_CONTROLLER, MountBuilder.SKELETON_CONTROLLER,
        };

        [MenuItem("HorseRace/Validate")]
        public static void Run()
        {
            var warn = new List<string>();
            var info = new List<string>();
            GameObject root = null;
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name == GenUtil.ROOT_NAME) root = g;
            if (root == null)
            {
                Debug.LogWarning("[HorseRace] Validate: " + GenUtil.ROOT_NAME + " がありません。先に「HorseRace/Build World」を実行してください。");
                return;
            }

            CheckStages(root, warn, info);
            CheckAssets(warn);
            CheckWiring(root, warn, info);
            foreach (string m in GenUtil.Missing) if (!warn.Contains(m)) warn.Add("Build World の警告: " + m);

            var sb = new StringBuilder();
            sb.AppendLine("=== HorseRace Validate ===");
            foreach (string s in info) sb.AppendLine("・" + s);
            if (warn.Count == 0) sb.AppendLine("問題は見つかりませんでした。");
            else
            {
                sb.AppendLine("警告 " + warn.Count + " 件:");
                foreach (string s in warn) sb.AppendLine("  ! " + s);
            }
            if (warn.Count == 0) Debug.Log(sb.ToString());
            else Debug.LogWarning(sb.ToString());
        }

        // ------------------------------------------------------------------

        static void CheckStages(GameObject root, List<string> warn, List<string> info)
        {
            var allCps = root.GetComponentsInChildren<Checkpoint>(true);
            var allRelics = root.GetComponentsInChildren<RelicPickup>(true);
            foreach (StageDef def in StageDefinitions.All)
            {
                string tag = "ステージ" + (def.index + 1) + "（" + def.label + "）";
                Transform stageRoot = null;
                foreach (Transform t in root.transform)
                    if (t.name.StartsWith("Stage" + (def.index + 1) + "_")) stageRoot = t;
                if (stageRoot == null) { warn.Add(tag + " が生成されていません"); continue; }

                Vector3[] local = StageDefinitions.SampleLocal(def);
                float len = StageDefinitions.GoalArc(def, local);
                if (len < StageDefinitions.MIN_COURSE_M || len > StageDefinitions.MAX_COURSE_M)
                    warn.Add(tag + "：コース全長 " + len.ToString("F0") + "m が範囲外（2200〜2800m）");

                Terrain terrain = stageRoot.GetComponentInChildren<Terrain>(true);
                float slope = -1f, sand = -1f;
                if (terrain == null || terrain.terrainData == null) warn.Add(tag + "：Terrain がありません");
                else
                {
                    TerrainData td = terrain.terrainData;
                    int r = td.heightmapResolution;
                    float[,] h = td.GetHeights(0, 0, r, r);
                    slope = HeightmapGen.MaxSlopeDegAlong(h, td.size.x, td.size.y, StageDefinitions.Normalize(local));
                    if (slope > StageDefinitions.MAX_SLOPE_DEG) warn.Add(tag + "：コース上の最大勾配 " + slope.ToString("F1") + "° が 25° を超えています");
                    int ar = td.alphamapResolution;
                    if (td.alphamapLayers > TerrainBuilder.LAYER_SAND)
                    {
                        float[,,] a = td.GetAlphamaps(0, 0, ar, ar);
                        double s = 0;
                        for (int z = 0; z < ar; z++) for (int x = 0; x < ar; x++) s += a[z, x, TerrainBuilder.LAYER_SAND];
                        sand = (float)(s / (ar * ar));
                    }
                    if (terrain.gameObject.layer != WorldLayers.Ground && LayerMask.LayerToName(terrain.gameObject.layer) != "Ground")
                        warn.Add(tag + "：Terrain のレイヤーが Ground ではありません");
                }
                float sMin = def.kind == HeightmapGen.KIND_DUNES ? 0.6f : (def.kind == HeightmapGen.KIND_CANYON ? 0.05f : 0.1f);
                float sMax = def.kind == HeightmapGen.KIND_DUNES ? 1.0f : (def.kind == HeightmapGen.KIND_CANYON ? 0.4f : 0.5f);
                if (sand >= 0f && (sand < sMin || sand > sMax))
                    warn.Add(tag + "：砂地の割合 " + (sand * 100f).ToString("F0") + "% が想定（" + (sMin * 100f).ToString("F0") + "〜" + (sMax * 100f).ToString("F0") + "%）外");
                int zones = stageRoot.GetComponentsInChildren<SandZone>(true).Length;
                if (sand > 0.02f && zones == 0) warn.Add(tag + "：砂地があるのに SandZone がありません");

                // CP
                float[] arcs = WorldLayout.CheckpointArcs(len, StageDefinitions.CP_SPACING_M);
                var cps = new List<Checkpoint>();
                foreach (var c in allCps) if (c.stageIndex == def.index) cps.Add(c);
                cps.Sort((p, q) => p.index.CompareTo(q.index));
                int goals = 0;
                for (int i = 0; i < cps.Count; i++)
                {
                    if (cps[i].isGoal) goals++;
                    if (cps[i].index != i + 1) warn.Add(tag + "：CP の番号が連続していません（" + cps[i].name + " = " + cps[i].index + "）");
                    Checkpoint expectNext = i + 1 < cps.Count ? cps[i + 1] : null;
                    if (cps[i].next != expectNext) warn.Add(tag + "：CP" + cps[i].index + " の next が次の CP ではありません");
                    if (cps[i].GetComponent<Collider>() == null || !cps[i].GetComponent<Collider>().isTrigger)
                        warn.Add(tag + "：CP" + cps[i].index + " に Is Trigger の Collider がありません");
                }
                if (cps.Count != arcs.Length) warn.Add(tag + "：CP の数 " + cps.Count + "（想定 " + arcs.Length + "）");
                if (goals != 1 || (cps.Count > 0 && !cps[cps.Count - 1].isGoal)) warn.Add(tag + "：ゴールの CP が最後の 1 つだけになっていません");
                float dev = WorldLayout.MaxDeviationFromChords(local, StageDefinitions.START_ARC_M, arcs);
                if (dev > 60f) warn.Add(tag + "：CP の線分からコースが " + dev.ToString("F0") + "m 離れます（60m 超でリセットされる）");

                int relics = 0;
                foreach (var r in allRelics) if (!r.isSpare && r.transform.IsChildOf(stageRoot)) relics++;
                if (relics != StageDefinitions.RELICS_PER_STAGE) warn.Add(tag + "：遺物 " + relics + " 個（想定 4）");

                info.Add(tag + "：全長 " + len.ToString("F0") + "m（駆歩 10m/s で約 " + (len / 10f / 60f).ToString("F1") + " 分）、最大勾配 "
                    + (slope >= 0f ? slope.ToString("F1") + "°" : "-") + "、CP " + cps.Count + " 個、砂地 "
                    + (sand >= 0f ? (sand * 100f).ToString("F0") + "%" : "-") + "（SandZone " + zones + "）、遺物 " + relics + " 個、CP 線分からのずれ最大 " + dev.ToString("F0") + "m");
            }
        }

        static void CheckAssets(List<string> warn)
        {
            foreach (string p in RequiredAssets)
                if (AssetDatabase.LoadAssetAtPath<Object>(p) == null) warn.Add("素材がありません: " + p);
            foreach (string p in MountBuilder.CoatPaths)
                if (AssetDatabase.LoadAssetAtPath<Material>(p) == null) warn.Add("毛色マテリアルがありません: " + p + "（HorseRace/Create Horse Materials）");
            if (Shader.Find(GenUtil.TOON_SHADER) == null) warn.Add("シェーダー " + GenUtil.TOON_SHADER + " がありません");
        }

        static void CheckWiring(GameObject root, List<string> warn, List<string> info)
        {
            var all = root.GetComponentsInChildren<UdonSharpBehaviour>(true);
            int checkedFields = 0, managers = 0;
            foreach (var b in all)
            {
                if (b == null) continue;
                Type t = b.GetType();
                if (t == typeof(RaceManager)) managers++;
                string[] fields;
                if (!Required.TryGetValue(t, out fields)) continue;
                foreach (string name in fields)
                {
                    checkedFields++;
                    FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    if (f == null) { warn.Add(t.Name + " にフィールド " + name + " がありません（Validator の表を更新してください）"); continue; }
                    string problem = Problem(f.GetValue(b));
                    if (problem != null) warn.Add(Path(b.transform) + " の " + t.Name + "." + name + " が" + problem);
                }
                // 条件つきの必須
                var cp = b as Checkpoint;
                if (cp != null && !cp.isGoal && cp.next == null) warn.Add(Path(b.transform) + " の Checkpoint.next が空です（ゴール以外）");
                var ac = b as AbilityCaster;
                if (ac != null && ac.practiceMount && ac.practiceArea == null) warn.Add(Path(b.transform) + " の練習用 AbilityCaster.practiceArea が空です");
                var rp = b as RelicPickup;
                if (rp != null && rp.isSpare && Problem(rp.pool) != null) warn.Add(Path(b.transform) + " の予備の遺物の pool が空です");
                var nb = b as NoticeBoardDisplay;
                if (nb != null && nb.mode == 0 && nb.director == null) warn.Add(Path(b.transform) + " の中継の掲示板（Mode 0）に director がありません");
                var sd = b as SpectatorDirector;
                if (sd != null && sd.broadcastCamera != null)
                {
                    if (sd.broadcastCamera.targetTexture == null) warn.Add("中継カメラの Target Texture が空です");
                    if (sd.broadcastCamera.enabled) warn.Add("中継カメラが初期状態で有効になっています（無効にしてください）");
                    if (sd.broadcastCamera.GetComponent<AudioListener>() != null) warn.Add("中継カメラに Audio Listener が付いています");
                }
            }
            if (managers != 1) warn.Add("RaceManager が " + managers + " 個あります（1 個のはず）");
            var rm = root.GetComponentInChildren<RaceManager>(true);
            if (rm != null)
            {
                if (rm.mounts == null || rm.mounts.Length != RaceManager.MAX_SLOTS) warn.Add("RaceManager.mounts が 8 頭ではありません");
                else for (int i = 0; i < rm.mounts.Length; i++)
                        if (rm.mounts[i] != null && rm.mounts[i].slotIndex != i) warn.Add("Mount " + i + " の slotIndex が " + rm.mounts[i].slotIndex);
                if (rm.startPoints == null || rm.startPoints.Length != StageDefinitions.All.Length * RaceManager.MAX_SLOTS) warn.Add("RaceManager.startPoints が 24 個ではありません");
                int casters = root.GetComponentsInChildren<AbilityCaster>(true).Length;
                if (rm.stageResetTargets != null && rm.stageResetTargets.Length < casters) warn.Add("stageResetTargets に AbilityCaster が全部入っていません");
            }
            var ids = new HashSet<int>();
            foreach (var ac in root.GetComponentsInChildren<AbilityCaster>(true))
                if (!ids.Add(ac.casterId)) warn.Add("AbilityCaster の casterId " + ac.casterId + " が重複しています");
            info.Add("つなぎ確認：UdonSharpBehaviour " + all.Length + " 個、必須の参照 " + checkedFields + " 件を確認");
        }

        /// <summary>null / 空配列 / null を含む配列なら理由、問題なければ null。</summary>
        static string Problem(object v)
        {
            if (v == null) return "空です";
            var o = v as Object;
            if (v is Object && o == null) return "空です（参照切れ）";
            var arr = v as Array;
            if (arr != null)
            {
                if (arr.Length == 0) return "空の配列です";
                for (int i = 0; i < arr.Length; i++)
                {
                    object e = arr.GetValue(i);
                    if (e == null || (e is Object && (Object)e == null)) return " [" + i + "] が空です";
                }
            }
            return null;
        }

        static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null && t.parent.name != GenUtil.ROOT_NAME) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
