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
                "refillTargets", "spectator", "ledgers", "rankBoards", "posterCards", "mountPickers", "startBell", "practiceArea", "hud", "raceOrder", "pickCards", "pickStatusText", "recordText" } },
            { typeof(RaceOrder), new[] { "raceManager" } },
            { typeof(MountController), new[] { "station", "seatAnchor", "visual", "vrInput", "seatFit" } },
            { typeof(SeatFit), new[] { "mount", "seatAnchor", "saddleY", "saddleZ", "panelRoot", "buttons", "readout" } },
            { typeof(SeatAdjustButton), new[] { "seatFit" } },
            { typeof(MountVisual), new[] { "horseModel", "camelModel", "skeletonModel", "horseAnimator", "camelAnimator", "skeletonAnimator",
                "horseController", "camelController", "skeletonController", "horseCoatRenderers", "coatMaterials", "breathAudio", "hooves", "hoofDust", "flickerRenderers" } },
            { typeof(MountInputVR), new[] { "mount", "seatAnchor" } },
            { typeof(AbilityCaster), new[] { "mount", "raceManager", "pouch", "revolver", "dustCloud", "lasso", "rockWall", "balls", "relicPool", "aimMarker", "aimInRange", "aimOutRange", "fizzleSound", "doubles", "kegs" } },
            { typeof(MirageDouble), new[] { "caster", "visual", "body", "hitBox", "popDust" } },
            { typeof(PowderKeg), new[] { "visual", "fuse", "blast" } },
            { typeof(CharmPouch), new[] { "cardRoot", "cardRenderers", "readyMaterials", "idleMaterials", "cooldownText", "levelMarks" } },
            { typeof(RevolverHolder), new[] { "caster", "pickup", "cylinder", "hammer", "triggerPart", "bullets", "muzzle" } },
            { typeof(DustCloud), new[] { "visual", "particles" } },
            { typeof(LassoEffect), new[] { "line" } },
            { typeof(RockWall), new[] { "segments", "caster", "crumbleDust" } },
            { typeof(IronBall), new[] { "visual" } },
            { typeof(RelicPickup), new[] { "raceManager", "visual" } },
            { typeof(EntryLedger), new[] { "raceManager", "pageText", "choiceText" } },
            { typeof(MountPicker), new[] { "raceManager", "visual" } },
            { typeof(PosterCard), new[] { "raceManager", "posterVisual", "label" } },
            { typeof(StartBell), new[] { "raceManager" } },
            { typeof(PracticeArea), new[] { "posts" } },
            { typeof(PracticeHorsePost), new[] { "raceManager", "mount", "home" } },
            { typeof(PracticeDummy), new[] { "pivot" } },
            { typeof(RankBoard), new[] { "raceManager", "boardText" } },
            { typeof(MinimalHud), new[] { "raceManager", "hudRoot", "rankText", "timeText", "headerText", "progressText", "staminaPips" } },
            { typeof(PocketWatch), new[] { "raceManager", "watchRoot", "faceText" } },
            { typeof(SpectatorDirector), new[] { "raceManager", "broadcastCamera", "renderTexture", "tags", "goalShots", "podiumShot", "podiumLook" } },
            { typeof(DirectorTag), new[] { "director" } },
            { typeof(NoticeBoardDisplay), new[] { "raceManager", "panelRenderer", "broadcastMaterial", "parchmentMaterial", "boardText" } },
            { typeof(Starter), new[] { "raceManager", "flagPivot", "audioSource", "stageSpots", "skyCountText", "skySpots" } },
            { typeof(GoalPresenter), new[] { "raceManager", "confetti", "bellAudio", "placeTags", "placeTagTexts" } },
            { typeof(Podium), new[] { "raceManager", "steps", "fireworks", "namePlates", "plateRoots", "drumClip", "goldBurst", "resultsBoard" } },
            { typeof(StepSigns), new[] { "raceManager", "stepTexts", "stepLights", "startPoint" } },
            { typeof(PracticeLap), new[] { "boardText", "hud" } },
            { typeof(Checkpoint), new[] { "raceManager" } },
            { typeof(TownCrier), new[] { "raceManager", "boards" } },
            { typeof(StageAtmosphere), new[] { "raceManager", "sun", "fogColor" } },
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
            GenUtil.Third("RockGray", "RockGrayA"), GenUtil.Third("RockGray", "RockGrayB"), GenUtil.Third("RockGray", "RockGrayC"),
            GenUtil.Third("RockGray", "RockGrayD"), GenUtil.Third("RockGray", "RockGrayE"), GenUtil.Third("RockGray", "RockGrayF"),
            GenUtil.Third("RockGray", "RockGrayG"), GenUtil.Third("RockGray", "RockGrayH"),
            GenUtil.FONT_TTF,
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
            CheckLayers(root, warn);
            CheckHidden(root, warn, info);
            CheckCustom(root, warn, info);
            CheckPrimitives(root, warn, info);
            Transform lobbyDr = GenUtil.FindDeep(root.transform, "TownDressing");
            int lobbyN = 0;
            if (lobbyDr != null) foreach (Transform g in lobbyDr) { if (g.name.StartsWith("WallPoster")) continue; foreach (Transform c in g) lobbyN++; }
            if (lobbyN > DressingPlan.MAX_LOBBY) warn.Add("ロビーの飾りが " + lobbyN + " 個（上限 " + DressingPlan.MAX_LOBBY + "）");
            Transform fenceT = GenUtil.FindDeep(root.transform, "LobbyFence");
            Transform beltT = GenUtil.FindDeep(root.transform, "LobbyTreeBelt");
            int fenceN = fenceT != null ? fenceT.childCount : 0, beltN = 0;
            if (beltT != null) foreach (Transform g in beltT) beltN += g.childCount;
            int expectFence = LobbyLayout.FenceSegments(LobbyLayout.FENCE_RECT, LobbyLayout.Gates, LobbyLayout.FENCE_PIECE_M).Count;
            if (fenceN < expectFence) warn.Add("ロビーの柵が " + fenceN + " 区間しかありません（" + expectFence + " 区間以上で一周）。切れ目から外が見えます");
            // 実際の町（WesternTown）が柵の中に 8m 以上の余白で入っているか
            Transform townT = GenUtil.FindDeep(root.transform, "WesternTown");
            if (fenceT != null && townT != null && fenceN > 0)
            {
                Bounds fb = GenUtil.RendererBounds(fenceT.gameObject), tb = GenUtil.RendererBounds(townT.gameObject);
                float m = Mathf.Min(Mathf.Min(tb.min.x - fb.min.x, fb.max.x - tb.max.x), Mathf.Min(tb.min.z - fb.min.z, fb.max.z - tb.max.z));
                if (m < LobbyLayout.TOWN_MARGIN_M - 0.5f) warn.Add("町（WesternTown）と柵の余白が " + m.ToString("F1") + "m しかありません（8m 以上）");
                else info.Add("ロビー：町と柵の余白 最小 " + m.ToString("F0") + "m");
            }
            int minBelt = LobbyLayout.MinBelt(LobbyLayout.FENCE_RECT);
            if (beltN < minBelt) warn.Add("ロビーの柵の外の木の帯が " + beltN + " 本しかありません（" + minBelt + " 本以上）");
            info.Add("ロビー：町のまわりの飾り " + lobbyN + " 個（上限 " + DressingPlan.MAX_LOBBY + "）、柵 " + fenceN + " 区間（出入口 " + LobbyLayout.Gates.Length
                + "）、柵の外の木の帯 " + beltN + " 本（別枠、上限 " + LobbyLayout.MAX_BELT + "）");
            CheckWiring(root, warn, info);
            CheckAudio(root, warn, info);
            CheckLook(warn, info);
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
                    // 踏み切りの縁（6m の段）は地形の勾配から外す
                    float jumpA = SetPieces.JumpArc(def, len);
                    Vector3[] slopePts = jumpA < 0f ? local : SetPieces.ExcludeArcRange(local, jumpA - 2f, jumpA + 20f);
                    slope = HeightmapGen.MaxSlopeDegAlong(h, td.size.x, td.size.y, StageDefinitions.Normalize(slopePts));
                    CheckSetPieces(def, tag, stageRoot, local, len, h, allCps, warn, info);
                    CheckRim(tag, h, td.size.x, td.size.y, warn, info);
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
                foreach (var c in allCps) if (c.stageIndex == def.index && !IsTwin(c)) cps.Add(c);
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

                int dressing = CountDressing(GenUtil.FindDeep(stageRoot, "Dressing"));
                if (dressing > DressingPlan.MAX_PER_STAGE) warn.Add(tag + "：飾りが " + dressing + " 個（上限 " + DressingPlan.MAX_PER_STAGE + "）");
                if (dressing < DressingPlan.MIN_PER_STAGE / 2) warn.Add(tag + "：飾りが少なすぎます（" + dressing + " 個。素材が読み込めていない可能性）");
                info.Add(tag + "：飾り " + dressing + " 個（上限 " + DressingPlan.MAX_PER_STAGE + "、すべて Static）");

                int relics = 0;
                foreach (var r in allRelics) if (!r.isSpare && r.transform.IsChildOf(stageRoot)) relics++;
                if (relics != StageDefinitions.RELICS_PER_STAGE) warn.Add(tag + "：遺物 " + relics + " 個（想定 4）");
                CheckBarrier(tag, stageRoot, cps, allRelics, warn, info);

                info.Add(tag + "：全長 " + len.ToString("F0") + "m（駆歩 10m/s で約 " + (len / 10f / 60f).ToString("F1") + " 分）、最大勾配 "
                    + (slope >= 0f ? slope.ToString("F1") + "°" : "-") + "、CP " + cps.Count + " 個、砂地 "
                    + (sand >= 0f ? (sand * 100f).ToString("F0") + "%" : "-") + "（SandZone " + zones + "）、遺物 " + relics + " 個、CP 線分からのずれ最大 " + dev.ToString("F0") + "m");
            }
        }

        /// <summary>
        /// コースの見えない柵（BarrierBuilder）：区間の数、Block レイヤー、CP の箱・遺物・スタート位置・ゴール後の囲いから
        /// CourseBarrier.CP_CLEAR_M（4m）以上離れているか。
        /// </summary>
        static void CheckBarrier(string tag, Transform stageRoot, List<Checkpoint> cps, RelicPickup[] allRelics, List<string> warn, List<string> info)
        {
            // CP の箱の大きさは CourseGeometry の 1 か所で決まる（course-r1-14）。幅は道幅に合わせて CP ごとに変わる
            Transform root = GenUtil.FindDeep(stageRoot, BarrierBuilder.ROOT);
            Transform walls = root != null ? GenUtil.FindDeep(root, "Walls") : null;
            if (walls == null || walls.childCount == 0) { warn.Add(tag + "：コースの見えない柵（" + BarrierBuilder.ROOT + "）がありません"); return; }
            var segs = new List<Vector3[]>();
            bool badLayer = false;
            foreach (Transform w in walls)
            {
                var box = w.GetComponent<BoxCollider>();
                if (box == null) continue;
                if (w.gameObject.layer != WorldLayers.Block) badLayer = true;
                float half = box.size.z * 0.5f;
                segs.Add(new[] { w.TransformPoint(new Vector3(0f, 0f, -half)), w.TransformPoint(new Vector3(0f, 0f, half)) });
            }
            if (badLayer) warn.Add(tag + "：コースの柵が Block レイヤーではありません（乗り物が止まりません）");

            float minCp = float.MaxValue;
            string worst = "";
            var withTwins = new List<Checkpoint>(cps);
            foreach (var tw in stageRoot.GetComponentsInChildren<Checkpoint>(true)) if (IsTwin(tw)) withTwins.Add(tw);
            foreach (var cp in withTwins)
            {
                var box = cp.GetComponent<BoxCollider>();
                if (box == null) continue;
                Vector3 c = box.center, z = box.size * 0.5f;
                Transform t = cp.transform;
                var quad = new[]
                {
                    t.TransformPoint(new Vector3(c.x - z.x, 0f, c.z - z.z)), t.TransformPoint(new Vector3(c.x + z.x, 0f, c.z - z.z)),
                    t.TransformPoint(new Vector3(c.x + z.x, 0f, c.z + z.z)), t.TransformPoint(new Vector3(c.x - z.x, 0f, c.z + z.z)),
                };
                float d = CourseBarrier.DistToQuad(segs, quad);
                if (d < minCp) { minCp = d; worst = "CP" + cp.index; }
            }
            var points = new List<KeyValuePair<string, Vector3>>();
            foreach (var r in allRelics) if (!r.isSpare && r.transform.IsChildOf(stageRoot)) points.Add(new KeyValuePair<string, Vector3>("遺物 " + r.name, r.transform.position));
            Transform grid = GenUtil.FindDeep(stageRoot, "StartGrid");
            if (grid != null) foreach (Transform s in grid) points.Add(new KeyValuePair<string, Vector3>("スタート位置", s.position));
            Transform pen = GenUtil.FindDeep(stageRoot, "FinishPen");
            if (pen != null) points.Add(new KeyValuePair<string, Vector3>("ゴール後の囲い", pen.position));
            float minPt = float.MaxValue;
            foreach (var kv in points)
            {
                float d = CourseBarrier.DistToPoint(segs, kv.Value);
                if (d < minPt) minPt = d;
                if (d < CourseBarrier.CP_CLEAR_M) warn.Add(tag + "：コースの柵が" + kv.Key + "から " + d.ToString("F1") + "m しか離れていません（" + CourseBarrier.CP_CLEAR_M + "m 以上）");
            }
            if (minCp < CourseBarrier.CP_CLEAR_M) warn.Add(tag + "：コースの柵が " + worst + " の箱から " + minCp.ToString("F1") + "m しか離れていません（" + CourseBarrier.CP_CLEAR_M + "m 以上）");
            info.Add(tag + "：コースの見えない柵 " + segs.Count + " 区間（道の縁から " + CourseBarrier.SIDE_M + "m・急カーブ " + CourseBarrier.CORNER_SIDE_M + "m、近づくと見える）、CP の箱まで最小 "
                + (minCp < float.MaxValue ? minCp.ToString("F1") : "-") + "m、遺物・スタート・囲いまで最小 " + (minPt < float.MaxValue ? minPt.ToString("F1") : "-") + "m");
        }

        /// <summary>見どころ：岩橋（板が Ground、溝が 8m 以上深い）、崖下りの近道（段 3〜8m、コースに戻る、CP の線分から 60m 以内、
        /// 途中の CP の箱が近道に届く）、ジャンプ台（25° 以下、Ground）。</summary>
        static void CheckSetPieces(StageDef def, string tag, Transform stageRoot, Vector3[] local, float len, float[,] h,
            Checkpoint[] allCps, List<string> warn, List<string> info)
        {
            float bridge = SetPieces.BridgeArc(def, len);
            if (bridge >= 0f)
            {
                Transform rb = GenUtil.FindDeep(stageRoot, "RockBridge");
                if (rb == null) warn.Add(tag + "：岩の橋（RockBridge）がありません");
                else
                {
                    // 地形の背（中央）と溝の底（横 20m）の差（course-r1-07：作った板は無い）
                    Vector3 c = WorldLayout.PointAtArc(local, bridge), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(local, bridge));
                    float H(Vector3 q) => WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, q.x, q.z);
                    float depth = H(c) - Mathf.Max(H(c + r * 20f), H(c - r * 20f));
                    if (depth < 8f) warn.Add(tag + "：岩の橋の両脇の溝が浅すぎます（" + depth.ToString("F1") + "m）");
                    float detour = Mathf.Max(Mathf.Abs(H(c + r * 46f) - H(c)), Mathf.Abs(H(c - r * 46f) - H(c)));
                    if (detour > 3f) warn.Add(tag + "：溝の両端の遠回りが道の高さと " + detour.ToString("F1") + "m ずれています");
                    Transform pit = GenUtil.FindDeep(rb, "FallPit");
                    if (pit == null || pit.GetComponent<ResetVolume>() == null || pit.gameObject.layer != WorldLayers.TRIGGERS)
                        warn.Add(tag + "：溝の底の落とし穴（FallPit / ResetVolume、Ignore Raycast）がありません");
                    Transform guard = GenUtil.FindDeep(rb, "LipGuard");
                    if (guard == null || guard.childCount == 0) warn.Add(tag + "：溝の縁の岩（LipGuard）がありません");
                    foreach (var prim in rb.GetComponentsInChildren<MeshFilter>(true))
                        if (prim.sharedMesh != null && (prim.sharedMesh.name == "Cube" || prim.sharedMesh.name == "Cylinder"))
                            warn.Add(tag + "：岩の橋にプリミティブの見た目（" + prim.name + "）があります");
                    info.Add(tag + "：岩の橋（背の両脇の溝 " + depth.ToString("F1") + "m、遠回りの高さの差 " + detour.ToString("F1") + "m）");
                }
            }
            if (def.hasShortcut)
            {
                // 生成直後の高さ（HeightmapGen は決定的）に台地を足して Build World と同じ計画を作り、シーンの地形で段差を測る
                float[,] raw = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, def, StageDefinitions.Normalize(local));
                SetPieces.RaiseMesa(raw, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, def, local, len);
                SetPieces.ShortcutPlan p = SetPieces.PlanFor(def, local, raw, len);
                if (p == null) warn.Add(tag + "：近道を置ける区間がありません");
                else
                {
                    float drop = SetPieces.MaxDropAlong(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.a, p.b, 4f);
                    if (drop > SetPieces.MAX_DROP + 0.5f) warn.Add(tag + "：近道の段が高すぎます（" + drop.ToString("F1") + "m > 8m）");
                    if (drop < 3f) warn.Add(tag + "：近道の段が低すぎて着地の硬直が起きません（" + drop.ToString("F1") + "m）");
                    float rejoin = WorldLayout.DistToPolylineXZ(p.b, local);
                    if (rejoin > 60f) warn.Add(tag + "：近道の出口がコースから " + rejoin.ToString("F0") + "m 離れています");
                    if (p.maxDevFromCpChord > 60f) warn.Add(tag + "：近道が CP の線分から " + p.maxDevFromCpChord.ToString("F0") + "m 離れます（コース外リセット）");
                    int twins = 0;
                    foreach (var c in allCps) if (c.stageIndex == def.index && IsTwin(c)) twins++;
                    if (twins != p.twinFractions.Length) warn.Add(tag + "：近道の双子の CP が " + twins + " 個（想定 " + p.twinFractions.Length + "）");
                    info.Add(tag + "：近道（CP" + (p.cpInterval + 1) + "〜CP" + (p.cpInterval + 1 + p.span) + "、疾走で " + p.gainSeconds.ToString("F1") + " 秒得、長さの比 "
                        + p.ratio.ToString("F2") + "、段 " + drop.ToString("F1") + "m、双子の CP " + twins + "）");
                }
            }
            float jumpArc = SetPieces.JumpArc(def, len);
            if (jumpArc >= 0f)
            {
                Transform jump = GenUtil.FindDeep(stageRoot, "DuneJump");
                if (jump == null) warn.Add(tag + "：踏み切り（DuneJump）がありません");
                else
                {
                    Vector3 lip = WorldLayout.PointAtArc(local, jumpArc - 0.5f), before = WorldLayout.PointAtArc(local, jumpArc - SetPieces.JUMP_LENGTH - 4f);
                    float rise = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, lip.x, lip.z)
                        - WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, before.x, before.z);
                    if (rise < 2.5f) warn.Add(tag + "：踏み切りの縁が低すぎます（" + rise.ToString("F1") + "m）");
                    if (GenUtil.FindDeep(jump, "TroughSand") == null) warn.Add(tag + "：踏み切りの先の砂の窪み（SandZone）がありません");
                    info.Add(tag + "：踏み切り（地形、縁 +" + rise.ToString("F1") + "m・" + SetPieces.JumpAngleDeg.ToString("F1") + "°）");
                }
            }
            float pass = SetPieces.PassArc(def, len);
            if (pass >= 0f)
            {
                // 切り通し：掘る前の高さ（HeightmapGen は決定的）と比べて道が下がり、両脇が盛り上がっているか
                float[,] raw = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, def, StageDefinitions.Normalize(local));
                Vector3 c = WorldLayout.PointAtArc(local, pass), r = WorldLayout.RightOf(WorldLayout.TangentAtArc(local, pass));
                float H(float[,] a, Vector3 q) => WorldLayout.SampleHeight(a, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, q.x, q.z);
                float cut = H(raw, c) - H(h, c), wall = Mathf.Min(H(h, c + r * 30f) - H(raw, c + r * 30f), H(h, c - r * 30f) - H(raw, c - r * 30f));
                if (cut < SetPieces.PASS_DEPTH * 0.6f || wall < 10f)
                    warn.Add(tag + "：メサの切り通しが浅い（道 −" + cut.ToString("F1") + "m、両脇 +" + wall.ToString("F1") + "m）");
                else info.Add(tag + "：メサの切り通し（道 −" + cut.ToString("F1") + "m、両脇 +" + wall.ToString("F1") + "m）");
            }
        }

        static bool IsTwin(Checkpoint c) => c != null && c.name.StartsWith("TwinCP");

        /// <summary>レイヤーとマスク：マスクが 0 でない、体の Collider が Mount、大きなトリガーが Ignore Raycast、地形が Ground。</summary>
        /// <summary>ui-r2-11：clip の無い AudioSource は鳴らない（PlayOneShot で clip を渡す MinimalHud.finishAudio・GoalPresenter.bellAudio も clip を持たせる）。</summary>
        static void CheckAudio(GameObject root, List<string> warn, List<string> info)
        {
            int total = 0, silent = 0;
            string first = "";
            foreach (var a in root.GetComponentsInChildren<AudioSource>(true))
            {
                total++;
                if (a.clip != null) continue;
                silent++;
                if (first.Length == 0) first = a.gameObject.name;
            }
            if (silent > 0) warn.Add("音の無い AudioSource が " + silent + " 個（例：" + first + "）。SfxBuilder.Clip で仮の音を付けてください");
            else info.Add("音：AudioSource " + total + " 個すべてに clip あり（仮の合成音は Generated/Audio）");
        }

        static void CheckLayers(GameObject root, List<string> warn)
        {
            int ground = LayerMask.NameToLayer("Ground"), mount = LayerMask.NameToLayer("Mount");
            if (ground < 0 || mount < 0 || LayerMask.NameToLayer("Rider") < 0 || LayerMask.NameToLayer("Block") < 0)
                warn.Add("レイヤー Ground / Mount / Rider / Block のどれかが TagManager にありません");
            foreach (var t in root.GetComponentsInChildren<Terrain>(true))
                if (t.gameObject.layer != ground) warn.Add("Terrain " + t.name + "（" + t.transform.parent.name + "）が Ground レイヤーではありません");
            foreach (var m in root.GetComponentsInChildren<MountController>(true))
            {
                if (m.gameObject.layer != mount) warn.Add(m.name + " が Mount レイヤーではありません");
                var body = m.GetComponent<BoxCollider>();
                if (body == null || body.isTrigger) warn.Add(m.name + " に体の Collider（Is Trigger でない BoxCollider）がありません");
                if (m.groundMask.value == 0 || m.obstacleMask.value == 0) warn.Add(m.name + " の groundMask / obstacleMask が 0 です");
            }
            foreach (var c in root.GetComponentsInChildren<AbilityCaster>(true))
                if (c.targetMask.value == 0 || c.blockMask.value == 0 || c.groundMask.value == 0) warn.Add(c.name + " の AbilityCaster のマスクが 0 です");
            foreach (var b in root.GetComponentsInChildren<IronBall>(true)) if (b.groundMask.value == 0) warn.Add(b.name + " の groundMask が 0 です");
            foreach (var w in root.GetComponentsInChildren<RockWall>(true)) if (w.groundMask.value == 0) warn.Add(w.name + " の groundMask が 0 です");
            foreach (var l in root.GetComponentsInChildren<LassoEffect>(true)) if (l.mountMask.value == 0) warn.Add(l.name + " の mountMask が 0 です");
            foreach (var r in root.GetComponentsInChildren<RelicPickup>(true))
            {
                if (r.groundMask.value == 0) warn.Add(r.name + " の groundMask が 0 です");
                if (r.gameObject.layer != WorldLayers.TRIGGERS) warn.Add(r.name + "（遺物のトリガー）が Ignore Raycast レイヤーではありません");
            }
            foreach (var c in root.GetComponentsInChildren<Checkpoint>(true))
                if (c.gameObject.layer != WorldLayers.TRIGGERS) { warn.Add("CP のトリガーが Ignore Raycast レイヤーではありません（" + c.name + " ほか）"); break; }
            foreach (var z in root.GetComponentsInChildren<SandZone>(true))
                if (z.gameObject.layer != WorldLayers.TRIGGERS) { warn.Add("SandZone が Ignore Raycast レイヤーではありません（" + z.name + " ほか）"); break; }
            foreach (var a in root.GetComponentsInChildren<PracticeArea>(true))
                if (a.gameObject.layer != WorldLayers.TRIGGERS) warn.Add("練習場のトリガーが Ignore Raycast レイヤーではありません");
            if (!GenUtil.FontAvailable()) warn.Add("日本語の TMP フォントがありません（" + GenUtil.FONT_TTF + "）");
        }

        /// <summary>外周の尾根：4 辺それぞれ、辺の平均の高さが 120m 内側の平均より 15m 以上高い。</summary>
        static void CheckRim(string tag, float[,] h, float sizeM, float heightM, List<string> warn, List<string> info)
        {
            int n = h.GetLength(0);
            int inner = Mathf.RoundToInt(120f / (sizeM / (n - 1)));
            string[] names = { "南", "北", "西", "東" };
            float minRise = float.MaxValue;
            for (int e = 0; e < 4; e++)
            {
                double edge = 0, ins = 0;
                for (int k = 0; k < n; k++)
                {
                    int ez = e == 0 ? 0 : (e == 1 ? n - 1 : k), ex = e == 2 ? 0 : (e == 3 ? n - 1 : k);
                    int iz = e == 0 ? inner : (e == 1 ? n - 1 - inner : k), ix = e == 2 ? inner : (e == 3 ? n - 1 - inner : k);
                    edge += h[ez, ex]; ins += h[iz, ix];
                }
                float rise = (float)((edge - ins) / n) * heightM;
                minRise = Mathf.Min(minRise, rise);
                if (rise < 15f) warn.Add(tag + "：外周の尾根（" + names[e] + "）が低すぎます（内側より " + rise.ToString("F0") + "m）。地形の端が見えるおそれ");
            }
            info.Add(tag + "：外周の尾根（内側より最低 " + minRise.ToString("F0") + "m 高い）");
        }

        /// <summary>見えてはいけない場所：霧、中継・参照カメラの遠さ、見えない壁、遠景の山並み、地面の板の範囲、中継カメラの範囲。</summary>
        static readonly string[] PrimitiveMeshNames = { "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad" };

        /// <summary>
        /// 「モデルは作るな」（ui-r3-12）：見えている（有効な Renderer の）Unity のプリミティブ（Cube / Sphere / Capsule / Cylinder / Plane / Quad の組み込みメッシュ）を
        /// 一覧にして警告する。素材が見つからないときの代わり（名前が *_Fallback / FallbackPrimitive）は除く（素材の欠けは別に警告される）。
        /// </summary>
        static void CheckPrimitives(GameObject root, List<string> warn, List<string> info)
        {
            int n = 0;
            var names = new List<string>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                string go = mf.gameObject.name;
                if (go.EndsWith("_Fallback") || go.Contains("FallbackPrimitive")) continue;
                if (Array.IndexOf(PrimitiveMeshNames, mf.sharedMesh.name) < 0) continue;
                string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (path != "Library/unity default resources") continue;
                n++;
                if (names.Count < 12) names.Add(go);
            }
            if (n > 0) warn.Add("見えているプリミティブが " + n + " 個あります（素材の部品に置き換える）: " + string.Join(", ", names.ToArray()) + (n > names.Count ? " …" : ""));
            else info.Add("見えているプリミティブ：なし");
        }

        static void CheckHidden(GameObject root, List<string> warn, List<string> info)
        {
            if (!RenderSettings.fog) warn.Add("霧（RenderSettings.fog）が無効です");
            else if (RenderSettings.fogMode == FogMode.Linear && (RenderSettings.fogEndDistance > 1300f || RenderSettings.fogStartDistance >= RenderSettings.fogEndDistance))
                warn.Add("霧の距離が想定外です（開始 " + RenderSettings.fogStartDistance + "m、終わり " + RenderSettings.fogEndDistance + "m）");
            var sd = root.GetComponentInChildren<SpectatorDirector>(true);
            if (sd != null)
            {
                if (sd.broadcastCamera != null && sd.broadcastCamera.farClipPlane > 1200f) warn.Add("中継カメラの Far Clip が 1200m を超えています");
                if (sd.stageBoundsMin == null || sd.stageBoundsMin.Length < StageDefinitions.All.Length) warn.Add("中継カメラの範囲（stageBoundsMin/Max）が設定されていません");
            }
            foreach (StageDef def in StageDefinitions.All)
            {
                Transform stageRoot = null;
                foreach (Transform t in root.transform) if (t.name.StartsWith("Stage" + (def.index + 1) + "_")) stageRoot = t;
                if (stageRoot != null && GenUtil.FindDeep(stageRoot, "StageBoundary") == null) warn.Add("ステージ" + (def.index + 1) + " の見えない壁（StageBoundary）がありません");
            }
            if (GenUtil.FindDeep(root.transform, "LobbyBorder") == null) warn.Add("ロビーの見えない壁（LobbyBorder）がありません");
            if (GenUtil.FindDeep(root.transform, "TownWalls") == null) warn.Add("町のまわりの見えない壁（TownWalls）がありません");
            Transform bd = GenUtil.FindDeep(root.transform, "Backdrop");
            int backdrop = 0;
            if (bd != null) foreach (Transform g in bd) if (g.name == "Backdrop") backdrop += g.childCount;
            if (backdrop < 40) warn.Add("遠景の山並みが " + backdrop + " 個しかありません（40 個以上）");
            Transform filler = bd != null ? GenUtil.FindDeep(bd, "OuterGround") : null;
            var ft = filler != null ? filler.GetComponent<Terrain>() : null;
            if (ft == null || ft.terrainData == null) warn.Add("外の地面（OuterGround の Terrain）がありません。ステージの間に空白が見えます");
            else
            {
                Vector3 o = ft.transform.position, sz = ft.terrainData.size;
                foreach (var rect in WorldBounds.PlayableRects())
                    if (o.x > rect.x || o.x + sz.x < rect.z || o.z > rect.y || o.z + sz.z < rect.w)
                    { warn.Add("外の地面が遊べる範囲を覆っていません（空白が見えるおそれ）"); break; }
            }
            info.Add("見えない場所の対処：霧 " + (RenderSettings.fog ? RenderSettings.fogStartDistance.ToString("F0") + "〜" + RenderSettings.fogEndDistance.ToString("F0") + "m" : "なし")
                + "、遠景の山並み " + backdrop + " 個");
        }

        /// <summary>
        /// 見た目の決まり（art-r1-01 / art-r1-13）：
        ///   ・峡谷の底の地面の影の段（素材の色 × 影色 × (環境光 + 太陽 × 0.5)）の輝度が LightingPlan.MIN_SHADOW_LUMINANCE 以上（真っ黒な溝にしない）。
        ///     実際の環境光（RenderSettings）と太陽（RenderSettings.sun）で数える。
        ///   ・岩のトゥーンのマテリアルのうち、平均の色相が暖色（0〜60°）の外のもの（青い灰色の岩など）を一覧にする（情報）。
        /// </summary>
        static void CheckLook(List<string> warn, List<string> info)
        {
            Color amb = RenderSettings.ambientGroundColor;
            float sunK = RenderSettings.sun != null ? RenderSettings.sun.intensity * LightingPlan.SHADOW_LIGHT_FLOOR : LightingPlan.SUN_INTENSITY * LightingPlan.SHADOW_LIGHT_FLOOR;
            Color sun = RenderSettings.sun != null ? RenderSettings.sun.color : LightingPlan.SunColor;
            float worst = float.MaxValue;
            foreach (Color albedo in new[] { ArtPalette.DirtCanyon, ArtPalette.SandLayer })
            {
                Color sh = LightingPlan.ShadowColor;
                var c = new Color(albedo.r * sh.r * (amb.r + sun.r * sunK), albedo.g * sh.g * (amb.g + sun.g * sunK), albedo.b * sh.b * (amb.b + sun.b * sunK));
                worst = Mathf.Min(worst, LightingPlan.Luminance(c));
            }
            if (worst < LightingPlan.MIN_SHADOW_LUMINANCE)
                warn.Add("峡谷の底の影が暗すぎます（影の段の輝度 " + worst.ToString("F2") + "、" + LightingPlan.MIN_SHADOW_LUMINANCE + " 以上）。環境光・太陽・影色を確認してください");
            else info.Add("光：峡谷の底の影の段の輝度 " + worst.ToString("F2") + "（" + LightingPlan.MIN_SHADOW_LUMINANCE + " 以上）");

            var cool = new List<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { GenUtil.GEN_MATERIALS }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!ArtPalette.IsRockAsset(name)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || m.shader.name != GenUtil.TOON_SHADER) continue;
                Color mean = MeanColor(m.GetTexture("_MainTex") as Texture2D);
                Color tint = m.GetColor("_Tint");
                Color c = ArtPalette.Multiply(mean, tint);
                if (!ArtPalette.IsWarmRock(c))
                {
                    float h, sat, v;
                    ArtPalette.ToHSV(c, out h, out sat, out v);
                    cool.Add(name + "（色相 " + h.ToString("F0") + "°・彩度 " + sat.ToString("F2") + "）");
                }
            }
            if (cool.Count > 0) info.Add("色：暖色（0〜60°）の外の岩のマテリアル " + cool.Count + " 個: " + string.Join("、", cool.ToArray()));
            else info.Add("色：岩のマテリアルはすべて暖色（0〜60°）");
            CheckCliffDistance(warn, info);
        }

        /// <summary>
        /// 崖の板（Cliffs）は道（コースの中心線）から DressingPlan.CLIFF_FAR_M（260m）以上（足元の縁 = 外形の半径 × 0.67 まで）、砂漠には置かない（art-r3-05）。
        /// 近くの Cliffs は平らな箱に見える（art-r2-08）。シーンの「Cliffs_NN」の部品をすべて調べる。
        /// </summary>
        static void CheckCliffDistance(List<string> warn, List<string> info)
        {
            var courses = new List<Vector3[]>();
            foreach (var def in StageDefinitions.All)
            {
                var local = StageDefinitions.SampleLocal(def);
                var w = new Vector3[local.Length];
                for (int i = 0; i < local.Length; i++) w[i] = local[i] + def.Origin;
                courses.Add(w);
            }
            int n = 0, near = 0;
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (!t.name.StartsWith("Cliffs_")) continue;
                n++;
                Bounds b = GenUtil.RendererBounds(t.gameObject);
                float foot = Mathf.Max(b.extents.x, b.extents.z) * DressingPlan.CLIFF_HALF_DEPTH;
                for (int s = 0; s < courses.Count; s++)
                {
                    Vector3 o = StageDefinitions.All[s].Origin;
                    Vector3 p = t.position;
                    if (p.x < o.x || p.z < o.z || p.x > o.x + StageDefinitions.SIZE || p.z > o.z + StageDefinitions.SIZE) continue;
                    float d = WorldLayout.DistToPolylineXZ(p, courses[s]) - foot;
                    bool dunes = StageDefinitions.All[s].kind == HeightmapGen.KIND_DUNES;
                    if (d < DressingPlan.CLIFF_FAR_M - 0.5f || dunes)
                    {
                        near++;
                        if (near <= 5) warn.Add("崖の板が道に近すぎます（" + t.name + "、道から " + d.ToString("F0") + "m、" + DressingPlan.CLIFF_FAR_M + "m 以上・砂漠には置かない：art-r3-05）");
                    }
                }
            }
            if (near == 0) info.Add("見た目：崖の板 " + n + " 枚はどれも道から " + DressingPlan.CLIFF_FAR_M + "m 以上（砂漠には無し）");
        }

        /// <summary>テクスチャの平均の色（小さな RenderTexture に縮めて読む。読めない設定のテクスチャでもよい）。無ければ白。</summary>
        static Color MeanColor(Texture2D tex)
        {
            if (tex == null) return Color.white;
            var rt = RenderTexture.GetTemporary(4, 4, 0);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] px = small.GetPixels();
            Object.DestroyImmediate(small);
            float r = 0f, gg = 0f, b = 0f;
            foreach (var c in px) { r += c.r; gg += c.g; b += c.b; }
            int n = Mathf.Max(1, px.Length);
            return new Color(r / n, gg / n, b / n);
        }

        /// <summary>
        /// 手直しと HorseRace_Custom：手直しの件数、id 付きの飾りの数。Custom の物が道（コース中心から 12m 以内）に乗っていないか、
        /// CP・遺物・スタート位置・ゴール後の囲い・スターターの立ち位置をふさいでいないか。
        /// </summary>
        static void CheckCustom(GameObject root, List<string> warn, List<string> info)
        {
            int tagged = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (LayoutOverrides.ParseId(t.name) != null) tagged++;
            int overrides = 0;
            if (System.IO.File.Exists(LayoutOverrides.FILE))
            {
                try { overrides = LayoutOverrides.FromJson(System.IO.File.ReadAllText(LayoutOverrides.FILE)).Count; }
                catch (System.Exception e) { warn.Add(LayoutOverrides.FILE + " を読めません（" + e.Message + "）"); }
            }
            GameObject custom = null;
            foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == LayoutTools.CUSTOM_ROOT) custom = g;
            int n = custom != null ? custom.transform.childCount : 0;
            info.Add("配置の手直し：id 付きの飾り " + tagged + " 個、手直し " + overrides + " 件（" + LayoutOverrides.FILE + "）、" + LayoutTools.CUSTOM_ROOT + " に " + n + " 個");
            if (custom == null || n == 0) return;

            var courses = new List<Vector3[]>();
            foreach (StageDef def in StageDefinitions.All)
            {
                Vector3[] local = StageDefinitions.SampleLocal(def);
                var w = new Vector3[local.Length];
                for (int i = 0; i < local.Length; i++) w[i] = def.Origin + local[i];
                courses.Add(w);
            }
            var critical = new List<KeyValuePair<string, Vector3>>();
            foreach (var c in root.GetComponentsInChildren<Checkpoint>(true)) critical.Add(new KeyValuePair<string, Vector3>("CP" + c.index + "（ステージ" + (c.stageIndex + 1) + "）", c.transform.position));
            foreach (var r in root.GetComponentsInChildren<RelicPickup>(true)) if (!r.isSpare) critical.Add(new KeyValuePair<string, Vector3>("遺物 " + r.name, r.transform.position));
            var rm = root.GetComponentInChildren<RaceManager>(true);
            if (rm != null)
            {
                if (rm.startPoints != null) foreach (var t in rm.startPoints) if (t != null) critical.Add(new KeyValuePair<string, Vector3>("スタート位置", t.position));
                if (rm.finishPens != null) foreach (var t in rm.finishPens) if (t != null) critical.Add(new KeyValuePair<string, Vector3>("ゴール後の囲い", t.position));
            }
            var st = root.GetComponentInChildren<Starter>(true);
            if (st != null && st.stageSpots != null) foreach (var t in st.stageSpots) if (t != null) critical.Add(new KeyValuePair<string, Vector3>("スターターの立ち位置", t.position));

            foreach (Transform c in custom.transform)
            {
                Bounds b = GenUtil.RendererBounds(c.gameObject);
                Vector3 p = b.size.sqrMagnitude > 0f ? b.center : c.position;
                float r = Mathf.Max(b.extents.x, b.extents.z);
                foreach (var course in courses)
                    if (WorldLayout.DistToPolylineXZ(p, course) - r < 12f)
                    { warn.Add(LayoutTools.CUSTOM_ROOT + "/" + c.name + " が道（コース中心から 12m 以内）に乗っています"); break; }
                foreach (var kv in critical)
                    if (WorldLayout.DistXZ(p, kv.Value) - r < 6f && Mathf.Abs(p.y - kv.Value.y) < 20f)
                    { warn.Add(LayoutTools.CUSTOM_ROOT + "/" + c.name + " が" + kv.Key + "をふさいでいます"); break; }
            }
        }

        /// <summary>飾りの個数（Dressing の下のグループの子。見えない壁・表彰台は数えない）。</summary>
        static int CountDressing(Transform dressing)
        {
            if (dressing == null) return 0;
            int n = 0;
            foreach (Transform g in dressing)
            {
                if (g.name == "Podium") continue;
                foreach (Transform c in g) if (!c.name.EndsWith("_InvisibleWalls")) n++;
            }
            return n;
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
                var rw = b as RockWall;
                if (rw != null && (rw.obstacleMask.value == 0 || rw.mountMask.value == 0)) warn.Add(Path(b.transform) + " の RockWall の obstacleMask / mountMask が 0 です（道をふさぎきらない判定が効かない）");
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
