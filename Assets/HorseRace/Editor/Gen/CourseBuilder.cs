using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コース：スタート位置（8 頭の横一列）・スタートライン、CP（80m ごと、Is Trigger の BoxCollider。黒板 RankBoard は 3 つ目ごとの CP とゴール）、
    /// ゴールゲート（最後の CP = isGoal ＋ GoalPresenter）、ゴール後の囲い、遺物 4 個、スターターの立ち位置。
    /// コース外リセットは MountController が CP の線分からの距離で行う（Validate が線分からのずれ ≤ 60m を確認）。
    /// </summary>
    public static class CourseBuilder
    {
        public const float CP_WIDTH = 36f, CP_HEIGHT = 10f, CP_DEPTH = 6f;   // 18 m/s・10fps でも 1 フレーム 1.8m（体 2.6m＋奥行 6m）で必ず重なる
        public const float BOARD_SIDE = CourseGeometry.BOARD_SIDE;     // 黒板はコース中心から右へ 15m（平らな道 ±12m の外）
        public const float GATE_HALF = CourseGeometry.GATE_HALF;
        public const float PEN_RADIUS = CourseGeometry.PEN_RADIUS;
        public const float PEN_BACK = CourseGeometry.PEN_BACK;   // 囲いの中心 = 流しの終点の 14m 手前
        public const int BOARD_EVERY = CourseGeometry.BOARD_EVERY;        // 黒板は CP3・CP6・…とゴール

        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        static readonly Color DarkWood = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Chalk = new Color(0.12f, 0.22f, 0.16f);
        static readonly Color White = new Color(0.95f, 0.93f, 0.88f);
        static readonly Color Red = new Color(0.70f, 0.15f, 0.12f);
        static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);

        public static void Build(StageBuild sb, WorldContext ctx)
        {
            var root = GenUtil.Child(sb.root, "Course", Vector3.zero).transform;
            root.position = Vector3.zero;
            Vector3[] c = sb.courseWorld;
            int st = sb.def.index;

            // ---- スタート ----
            Vector3 t0 = WorldLayout.TangentAtArc(c, StageDefinitions.START_ARC_M);
            Vector3 r0 = WorldLayout.RightOf(t0);
            float yaw0 = WorldLayout.YawDeg(t0);
            Vector3 p0 = WorldLayout.PointAtArc(c, StageDefinitions.START_ARC_M);
            float[] lanes = WorldLayout.LaneOffsets(StageDefinitions.LANES, StageDefinitions.LANE_SPACING_M);
            var startRoot = GenUtil.World(root, "StartGrid", p0, yaw0).transform;
            for (int i = 0; i < lanes.Length; i++)
            {
                Vector3 p = sb.GroundWorld(p0 + r0 * lanes[i]) + Vector3.up * 0.05f;
                sb.startPoints[i] = GenUtil.World(startRoot, "Start_" + i, p, yaw0).transform;
            }
            Vector3 lineP = sb.GroundWorld(WorldLayout.PointAtArc(c, StageDefinitions.START_ARC_M + 3f));
            var line = GenUtil.World(root, "StartLine", lineP, yaw0);
            StartLineDressing(line.transform, sb, r0, t0, yaw0);
            // スターターはスタートラインの左脇（レーンの外）、コースのほうを向く
            Vector3 spot = sb.GroundWorld(lineP - r0 * (GATE_HALF + 3f) + t0 * 2f);
            sb.starterSpot = GenUtil.World(root, "StarterSpot", spot, WorldLayout.YawDeg(r0)).transform;
            sb.Exclude(spot, 14f);    // スターターの周りに岩を置かない

            // ---- CP ----
            float[] arcs = WorldLayout.CheckpointArcs(sb.goalArc, StageDefinitions.CP_SPACING_M);
            var cpRoot = GenUtil.Child(root, "Checkpoints", Vector3.zero).transform;
            cpRoot.position = Vector3.zero;
            Checkpoint prev = null;
            for (int k = 0; k < arcs.Length; k++)
            {
                bool goal = k == arcs.Length - 1;
                Vector3 t = WorldLayout.TangentAtArc(c, arcs[k]);
                Vector3 p = sb.GroundWorld(WorldLayout.PointAtArc(c, arcs[k]));
                var go = GenUtil.World(cpRoot, goal ? "CP" + (k + 1) + "_Goal" : "CP" + (k + 1), p, WorldLayout.YawDeg(t));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(CP_WIDTH, CP_HEIGHT, CP_DEPTH);
                box.center = new Vector3(0f, CP_HEIGHT * 0.5f - 1f, 0f);
                go.layer = WorldLayers.TRIGGERS;
                var cp = GenUtil.AddU<Checkpoint>(go);
                cp.index = k + 1;
                cp.stageIndex = st;
                cp.isGoal = goal;
                cp.next = null;
                if (prev != null) prev.next = cp;
                prev = cp;
                sb.checkpoints.Add(cp);
                if (k == 0) sb.firstCheckpoint = cp;
                // 黒板は 3 つ目ごとの CP とゴールだけ（RankBoard の書き直しの負荷を抑える）
                if (goal || (k + 1) % BOARD_EVERY == 0) ctx.rankBoards.Add(ChalkBoard(go.transform, sb, p, t, "Board"));
                if (goal) sb.goal = GoalGate(go.transform, sb, ctx);
            }

            // ---- ゴール後の囲い（流しの終点の手前。フィニッシュから 3 秒で着く距離より先） ----
            float total = CoursePath.Length(sb.courseLocal);
            float penArc = total - PEN_BACK;
            Vector3 penT = WorldLayout.TangentAtArc(c, penArc);
            Vector3 penP = sb.GroundWorld(WorldLayout.PointAtArc(c, penArc));
            sb.finishPen = GenUtil.World(root, "FinishPen", penP, WorldLayout.YawDeg(penT)).transform;
            Fence.Ring(sb.finishPen, PEN_RADIUS, sb);

            // ---- 遺物 ----
            var relicRoot = GenUtil.Child(root, "Relics", Vector3.zero).transform;
            relicRoot.position = Vector3.zero;
            for (int i = 0; i < sb.def.relicFractions.Length; i++)
            {
                float a = sb.def.relicFractions[i] * sb.goalArc;
                Vector3 t = WorldLayout.TangentAtArc(c, a);
                Vector3 p = sb.GroundWorld(WorldLayout.PointAtArc(c, a) + WorldLayout.RightOf(t) * sb.def.relicSide[i]);
                ctx.courseRelics.Add(Relic(relicRoot, "Relic_" + (i + 1), p, st, false));
            }
        }

        /// <summary>CP の黒板（RankBoard）。迫ってくる騎手のほうを向けて、コースの右脇に立てる。</summary>
        static RankBoard ChalkBoard(Transform cp, StageBuild sb, Vector3 p, Vector3 t, string name)
        {
            Vector3 bp = sb.GroundWorld(p + WorldLayout.RightOf(t) * BOARD_SIDE);
            var rb = ChalkBoardAt(cp, bp, WorldLayout.YawDeg(t), name, false);
            rb.stageIndex = sb.def.index;
            return rb;
        }

        /// <summary>スタートライン（art-r1-06）：両端の柱（DesertProps_05 ×1.2）と外側へ並ぶ杭の列（DesertProps_10）、レーンを横切る小石の線（当たり判定なし）。</summary>
        static void StartLineDressing(Transform line, StageBuild sb, Vector3 r0, Vector3 t0, float yaw0)
        {
            Vector3 c = line.position;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = sb.GroundWorld(c + r0 * (s * GATE_HALF));
                DressingBuilder.SetStatic(GenUtil.Stake(line, "DesertProps_05", post, yaw0, 1.2f));
                for (int k = 0; k < 2; k++)
                {
                    Vector3 sp = sb.GroundWorld(c + r0 * (s * (GATE_HALF + 2.4f + k * 3.4f)));
                    DressingBuilder.SetStatic(GenUtil.Stake(line, "DesertProps_10", sp, yaw0 + 90f, 1f));
                }
            }
            // レーンを横切る小石の線（平たい小石、当たり判定なし）
            for (float x = -GATE_HALF + 1f; x <= GATE_HALF - 1f + 1e-3f; x += 1.3f)
            {
                Vector3 p = sb.GroundWorld(c + r0 * x) - Vector3.up * 0.05f;
                var pebble = GenUtil.InstantiatePart(GenUtil.Pack("RockCollection"), ((int)Mathf.Abs(x * 10f)) % 2 == 0 ? "RockCollection_112" : "RockCollection_128",
                    line, p, x * 37f, 1.3f, new Vector3(0.6f, 0.2f, 0.6f), White);
                DressingBuilder.SetStatic(pebble);
            }
        }

        /// <summary>
        /// 黒板 1 枚（art-r1-06）：掲示板の模型（NoticeBoard ×0.75）の板の面に黒板の色を塗り、文字を板の少し手前に置く。
        /// 文字は -Z 側（yaw の方向を見ている人に読める）。模型が無ければ板の代わりに単色の板。
        /// </summary>
        public static RankBoard ChalkBoardAt(Transform parent, Vector3 worldPos, float yaw, string name, bool showLastRace)
        {
            var go = GenUtil.World(parent, name, worldPos, yaw);
            const float SC = 0.75f;
            var holder = GenUtil.PlaceModel(GenUtil.Prop("NoticeBoard"), go.transform, "Board", worldPos, yaw, SC, new Vector3(4.5f, 3.8f, 0.3f), DarkWood);
            var panel = GenUtil.FindDeep(holder.transform, "NoticeBoard_Panel");
            Vector3 textPos = new Vector3(0f, 1.9f, -0.04f);
            Vector2 textSize = new Vector2(2.3f, 1.5f);
            if (panel != null)
            {
                GenUtil.YawAlign(holder.transform, GenUtil.FirstNormalWorld(panel), -go.transform.forward);
                var pr = panel.GetComponent<Renderer>();
                if (pr != null)
                {
                    pr.sharedMaterial = GenUtil.ToonColor(Chalk);
                    Bounds b = pr.bounds;
                    Vector3 local = go.transform.InverseTransformPoint(b.center);
                    textPos = new Vector3(local.x, local.y, local.z - 0.04f);
                    textSize = new Vector2(3.2f * SC * 0.92f, 1.8f * SC * 0.9f);
                }
            }
            else GenUtil.Prim(PrimitiveType.Quad, "Slate", go.transform, new Vector3(0f, 1.9f, 0f), new Vector3(2.5f, 1.6f, 1f), Chalk)
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = GenUtil.Text(go.transform, "Chalk", textPos, Quaternion.identity, "出走前", 1.0f, White, textSize);
            var rb = GenUtil.AddU<RankBoard>(go);
            rb.boardText = text;
            rb.maxLines = 8;
            rb.showLastRace = showLastRace;
            return rb;
        }

        /// <summary>
        /// ゴールゲート（art-r1-06）：柱 = 杭（DesertProps_03 ×1.9 = 6.4m）、横木 = 丸太（WoodLogs_01 を横に引き伸ばして柱の間に渡す）、
        /// 幕 = 手配書の紙（引き伸ばし）に「ゴール」、柱の横に街灯 2 本と暖色の灯り、柱の足元に干し草と樽。GoalPresenter・紙吹雪・着順の札 8 枚。
        /// </summary>
        static GoalPresenter GoalGate(Transform cp, StageBuild sb, WorldContext ctx)
        {
            var gate = GenUtil.Child(cp, "GoalGate", Vector3.zero).transform;
            Vector3 c = gate.position;
            Vector3 right = gate.right, fwd = gate.forward;
            float yaw = gate.eulerAngles.y;
            var deco = GenUtil.Child(gate, "Dressing", Vector3.zero).transform;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = sb.GroundWorld(c + right * (s * GATE_HALF));
                DressingBuilder.SetStatic(GenUtil.Stake(deco, "DesertProps_03", post, yaw, 1.9f));
                Vector3 lamp = sb.GroundWorld(c + right * (s * (GATE_HALF + 1.4f)) - fwd * 0.6f);
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", deco, lamp, yaw + (s > 0 ? 90f : -90f), 1.25f,
                    new Vector3(0.3f, 3f, 0.3f), Wood));
                GenUtil.PointLight(deco, "GateLamp", lamp + Vector3.up * 3.6f, SetPieceBuilder.LampWarm, 10f, 1.5f);
                Vector3 foot = sb.GroundWorld(c + right * (s * (GATE_HALF + 1.2f)) + fwd * 1.6f);
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", deco, foot, yaw + s * 20f, 1f, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", deco, sb.GroundWorld(foot + right * (s * 1.6f)), yaw - s * 30f, 0.9f,
                    new Vector3(1.5f, 1.6f, 1.5f), Wood));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_15", deco, sb.GroundWorld(foot - fwd * 3f), 0f, 1.2f,
                    new Vector3(0.5f, 0.7f, 0.5f), Wood));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_22", deco, sb.GroundWorld(foot - fwd * 3.9f + right * (s * 0.8f)), 40f, 1.2f,
                    new Vector3(0.5f, 0.5f, 0.5f), Wood));
            }
            // 横木：丸太（長さ 1.5m）を X に引き伸ばして柱の間（2 × GATE_HALF + 1）に渡す
            var beam = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", deco, c + Vector3.up * 6.1f, yaw, 1f, new Vector3(1.5f, 0.3f, 1f), DarkWood);
            beam.transform.localScale = new Vector3((GATE_HALF * 2f + 1f) / 1.5f, 1.8f, 0.6f);
            DressingBuilder.SetStatic(beam);
            // 幕：手配書の紙を引き伸ばした板（表と裏）、文字は紙の手前
            GenUtil.PaperSheet(gate, "Banner", "WantedPosters_01", new Vector3(0f, 4.3f, -0.15f), null, 10f, 1.8f);
            var back = GenUtil.PaperSheet(gate, "BannerBack", "WantedPosters_01", new Vector3(0f, 4.3f, 0.15f), null, 10f, 1.8f);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            GenUtil.Text(gate, "BannerText", new Vector3(0f, 5.2f, -0.2f), Quaternion.identity, "ゴール", 8f, Red, new Vector2(10f, 1.4f));
            GenUtil.Text(gate, "BannerTextBack", new Vector3(0f, 5.2f, 0.2f), Quaternion.Euler(0f, 180f, 0f), "ゴール", 8f, Red, new Vector2(10f, 1.4f));

            var gp = GenUtil.AddU<GoalPresenter>(gate.gameObject);
            gp.stageIndex = sb.def.index;
            gp.confetti = GenUtil.Particles(gate, "Confetti", new Vector3(0f, 6.6f, 0f), new Color(1f, 0.85f, 0.4f), 0.15f, 6f, 3f, false, 0f, 2f);
            gp.bellAudio = ctx.churchBell;
            var fin = GenUtil.Child(gate, "FinishAudio", new Vector3(0f, 2f, 0f));
            gp.finishAudio = GenUtil.Audio(fin, false);
            var tags = new Transform[8];
            var texts = new TMPro.TextMeshPro[8];
            for (int i = 0; i < 8; i++)
            {
                // 着順の札：手配書の紙（WantedPosters_02）に着順。GoalPresenter が騎手の上へ動かす
                var tag = GenUtil.Child(gate, "PlaceTag_" + (i + 1), new Vector3(0f, 3f, 0f));
                GenUtil.PaperSheet(tag.transform, "Board", "WantedPosters_02", new Vector3(0f, -0.3f, 0f), null, 1.0f, 0.6f);
                texts[i] = GenUtil.Text(tag.transform, "Text", new Vector3(0f, 0f, -0.04f), Quaternion.identity, (i + 1) + "着", 2.5f, Ink, new Vector2(1f, 0.45f));
                tags[i] = tag.transform;
                tag.SetActive(false);
            }
            gp.placeTags = tags;
            gp.placeTagTexts = texts;
            return gp;
        }

        /// <summary>遺物（Packs/Gems の宝石 1 つ、Is Trigger の球 1.5m）。</summary>
        public static RelicPickup Relic(Transform parent, string name, Vector3 pos, int gemIndex, bool spare)
        {
            var go = GenUtil.World(parent, name, pos);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.5f;
            col.center = new Vector3(0f, 0.8f, 0f);
            go.layer = WorldLayers.TRIGGERS;
            // Gems_0N は色ちがいの宝石 8 個の組（tools/blender/split_parts.py で 1 個ずつに分けた）。_01 がいちばん大きい結晶
            string part = "Gems_0" + (GenUtil.Mod(gemIndex, 4) + 1) + "_01";
            var gem = GenUtil.InstantiatePart(GenUtil.Pack("Gems"), part, go.transform, pos, 0f, 1f,
                new Vector3(0.4f, 0.6f, 0.4f), new Color(0.3f, 0.8f, 0.9f));
            gem.name = "Visual";
            gem.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            // 宝石は大きさがまちまちなので最大 0.6m にそろえ、底を中心に寄せる
            Bounds b = GenUtil.RendererBounds(gem);
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (m > 0.01f) gem.transform.localScale = Vector3.one * (0.6f / m);
            b = GenUtil.RendererBounds(gem);
            Vector3 off = go.transform.position + new Vector3(0f, 0.8f, 0f) - b.center;
            foreach (Transform ch in gem.transform) ch.position += off;
            var rp = GenUtil.AddU<RelicPickup>(go);
            rp.isSpare = spare;
            rp.visual = gem;
            rp.groundMask = WorldLayers.GroundMask;
            var snd = GenUtil.Child(go.transform, "PickupSound", Vector3.up);
            rp.pickupSound = GenUtil.Audio(snd, false);
            return rp;
        }

        /// <summary>
        /// スターター（art-r1-06）：人の模型は無いので、街灯（FarmProps_02）につないだ馬（Horse.fbx、アニメーターを外して動かない）。
        /// 旗は街灯の柱に（赤く塗った紙を引き伸ばした旗、FlagPivot が振る）、拳銃は柱の腕（PistolArm が上げる）。上に呼び込みの文字。
        /// 3 ステージのスタート脇に移る。
        /// </summary>
        public static Starter BuildStarter(WorldContext ctx)
        {
            StageBuild s0 = ctx.stages[0];
            var go = GenUtil.World(ctx.root, "Starter", s0.starterSpot.position, s0.starterSpot.eulerAngles.y);
            var body = go.transform;
            Vector3 pos = go.transform.position;
            float yaw = go.transform.eulerAngles.y;
            var lamp = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", body, pos, yaw, 1.2f, new Vector3(0.3f, 3.6f, 0.3f), Wood);
            lamp.name = "LampPost";
            GenUtil.PointLight(body, "StarterLamp", pos + Vector3.up * 3.6f, SetPieceBuilder.LampWarm, 10f, 1.4f);
            // つないだ馬（柱の右、コースと平行に）
            var horse = GenUtil.PlaceModel(GenUtil.HORSE_FBX, body, "TiedHorse", pos + go.transform.right * 1.8f - go.transform.forward * 1.2f, yaw + 90f, 1f,
                new Vector3(0.8f, 1.6f, 2.4f), new Color(0.35f, 0.25f, 0.2f));
            foreach (var a in horse.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            var coat = AssetDatabase.LoadAssetAtPath<Material>(MountBuilder.CoatPaths[1]);
            if (coat != null) foreach (var r in horse.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = coat;
            // 柱の足元：干し草と樽
            GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_02", body, pos - go.transform.right * 0.9f + go.transform.forward * 0.6f, yaw + 20f, 1f,
                new Vector3(0.9f, 0.6f, 0.7f), Wood);
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_15", body, pos - go.transform.right * 0.8f - go.transform.forward * 0.7f, 0f, 1f,
                new Vector3(0.5f, 0.7f, 0.5f), Wood);

            // 旗：柱の 1.6m に、赤く塗った紙の旗（表裏）
            var flagPivot = GenUtil.Child(body, "FlagPivot", new Vector3(-0.15f, 1.6f, 0f)).transform;
            GenUtil.Stake(flagPivot, "DesertProps_05", flagPivot.position, yaw, 0.4f);
            var cloth = GenUtil.PaperSheet(flagPivot, "Cloth", "WantedPosters_01", new Vector3(0.35f, 0.75f, 0f), null, 0.6f, 0.4f);
            cloth.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            foreach (var r in cloth.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(Red);
            var clothBack = GenUtil.PaperSheet(flagPivot, "ClothBack", "WantedPosters_01", new Vector3(0.35f, 0.75f, 0f), null, 0.6f, 0.4f);
            clothBack.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            foreach (var r in clothBack.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(Red);

            // 拳銃の腕：柱の 2.4m（PistolArm が回して上げる）
            var arm = GenUtil.Child(body, "PistolArm", new Vector3(0.2f, 2.4f, 0f)).transform;
            var gun = GenUtil.InstantiateModel(GenUtil.Pack("Revolver"), arm, "Revolver");
            if (gun != null) { gun.transform.localPosition = new Vector3(0f, -0.2f, 0.05f); gun.transform.localScale = Vector3.one; }
            GenUtil.Text(body, "CallOut", new Vector3(0f, 4.6f, 0f), Quaternion.Euler(0f, 180f, 0f), "スタート係", 1.2f, Ink, new Vector2(3f, 0.6f));

            var st = GenUtil.AddU<Starter>(go);
            st.flagPivot = flagPivot;
            st.pistolArm = arm;
            st.audioSource = GenUtil.Audio(go, true);
            st.pistolSmoke = GenUtil.Particles(arm, "PistolSmoke", new Vector3(0f, -0.25f, 0.25f), new Color(0.85f, 0.85f, 0.85f, 0.7f), 0.3f, 1f, 1.5f, false, 0f, 0.1f);
            // 砂煙はスタートラインに沿って（スターターの右手 = コース側）3 か所
            var dust = new ParticleSystem[3];
            for (int i = 0; i < 3; i++)
                dust[i] = GenUtil.Particles(body, "StartDust_" + i, new Vector3(0f, 0.3f, 6f + i * 8f), new Color(0.85f, 0.72f, 0.5f, 0.8f), 1.2f, 2f, 2f, false, 0f, 2.5f);
            st.startDust = dust;
            st.skyCountText = GenUtil.Text(body, "SkyCount", new Vector3(-12f, 22f, 14f), Quaternion.Euler(0f, -90f, 0f), "", 60f,
                new Color(1f, 0.95f, 0.8f), new Vector2(30f, 20f));
            var spots = new Transform[ctx.stages.Count];
            for (int i = 0; i < spots.Length; i++) spots[i] = ctx.stages[i].starterSpot;
            st.stageSpots = spots;
            ctx.starter = st;
            return st;
        }
    }

    /// <summary>木の柵（Packs/FarmProps の直線の柵 FarmProps_03、幅 2.74m）。</summary>
    public static class Fence
    {
        public const string PART = "FarmProps_03";
        public const float PIECE = 2.74f;

        /// <summary>中心 center（ローカル原点）の周りに半径 r の柵の輪。乗り物と人は通れない（Block）。</summary>
        public static void Ring(Transform center, float r, StageBuild sb)
        {
            int n = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / PIECE));
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * 360f;
                Vector3 local = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, r);
                Vector3 w = center.TransformPoint(local);
                if (sb != null) w = sb.GroundWorld(w);
                Piece(center, w, center.eulerAngles.y + a);  // 部品の長手（ローカル X）が円の接線になる
            }
        }

        /// <summary>a から b へ柵を並べる。</summary>
        public static void Line(Transform parent, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.1f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / PIECE));
            float yaw = WorldLayout.YawDeg(d / len) - 90f;  // ローカル X を a→b に向ける
            for (int i = 0; i < n; i++) Piece(parent, Vector3.Lerp(a, b, (i + 0.5f) / n), yaw);
        }

        public static GameObject Piece(Transform parent, Vector3 world, float yaw)
        {
            var p = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), PART, parent, world, yaw, 1f,
                new Vector3(PIECE, 1f, 0.15f), new Color(0.45f, 0.30f, 0.18f));
            var box = p.AddComponent<BoxCollider>();
            // 柵の長手方向は部品のローカル X（FBX の幅 2.74m）
            box.size = new Vector3(PIECE, 1.4f, 0.3f);
            box.center = new Vector3(0f, 0.7f, 0f);
            GenUtil.SetLayerRecursive(p, WorldLayers.Block);
            return p;
        }
    }
}
