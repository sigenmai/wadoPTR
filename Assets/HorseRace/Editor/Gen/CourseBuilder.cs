using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コース：スタート位置（8 頭の横一列）・スタートライン、CP（80m ごと、Is Trigger の BoxCollider ＋黒板 RankBoard）、
    /// ゴールゲート（最後の CP = isGoal ＋ GoalPresenter）、ゴール後の囲い、遺物 4 個、スターターの立ち位置。
    /// コース外リセットは MountController が CP の線分からの距離で行う（Validate が線分からのずれ ≤ 60m を確認）。
    /// </summary>
    public static class CourseBuilder
    {
        public const float CP_WIDTH = 36f, CP_HEIGHT = 10f, CP_DEPTH = 4f;
        public const float BOARD_SIDE = 15f;     // 黒板はコース中心から右へ 15m（平らな道 ±12m の外）
        public const float GATE_HALF = 13f;
        public const float PEN_RADIUS = 12f;
        public const float PEN_BACK = 14f;       // 囲いの中心 = 流しの終点の 14m 手前

        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        static readonly Color DarkWood = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Chalk = new Color(0.12f, 0.22f, 0.16f);
        static readonly Color White = new Color(0.95f, 0.93f, 0.88f);
        static readonly Color Red = new Color(0.70f, 0.15f, 0.12f);

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
            GenUtil.Prim(PrimitiveType.Cube, "Chalk", line.transform, new Vector3(0f, 0.02f, 0f), new Vector3(24f, 0.04f, 0.4f), White);
            GenUtil.Prim(PrimitiveType.Cylinder, "PostL", line.transform, new Vector3(-GATE_HALF, 1.5f, 0f), new Vector3(0.25f, 1.5f, 0.25f), Wood);
            GenUtil.Prim(PrimitiveType.Cylinder, "PostR", line.transform, new Vector3(GATE_HALF, 1.5f, 0f), new Vector3(0.25f, 1.5f, 0.25f), Wood);
            // スターターはスタートラインの左脇（レーンの外）、コースのほうを向く
            Vector3 spot = sb.GroundWorld(lineP - r0 * (GATE_HALF + 3f) + t0 * 2f);
            sb.starterSpot = GenUtil.World(root, "StarterSpot", spot, WorldLayout.YawDeg(r0)).transform;

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
                ctx.rankBoards.Add(ChalkBoard(go.transform, sb, p, t, "Board"));
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
            return ChalkBoardAt(cp, bp, WorldLayout.YawDeg(t), name, false);
        }

        /// <summary>黒板 1 枚。文字は板の -Z 側（yaw の方向を見ている人に読める）。</summary>
        public static RankBoard ChalkBoardAt(Transform parent, Vector3 worldPos, float yaw, string name, bool showLastRace)
        {
            var go = GenUtil.World(parent, name, worldPos, yaw);
            GenUtil.Prim(PrimitiveType.Cube, "PostL", go.transform, new Vector3(-1.3f, 1.1f, 0.05f), new Vector3(0.12f, 2.2f, 0.12f), Wood);
            GenUtil.Prim(PrimitiveType.Cube, "PostR", go.transform, new Vector3(1.3f, 1.1f, 0.05f), new Vector3(0.12f, 2.2f, 0.12f), Wood);
            GenUtil.Prim(PrimitiveType.Cube, "Slate", go.transform, new Vector3(0f, 1.9f, 0f), new Vector3(2.5f, 1.6f, 0.06f), Chalk);
            GenUtil.Prim(PrimitiveType.Cube, "Frame", go.transform, new Vector3(0f, 1.9f, 0.02f), new Vector3(2.7f, 1.8f, 0.04f), DarkWood);
            var text = GenUtil.Text(go.transform, "Chalk", new Vector3(0f, 1.9f, -0.04f), Quaternion.identity, "出走前", 1.2f, White, new Vector2(2.3f, 1.5f));
            var rb = GenUtil.AddU<RankBoard>(go);
            rb.boardText = text;
            rb.maxLines = 8;
            rb.showLastRace = showLastRace;
            return rb;
        }

        /// <summary>ゴールゲート（柱 2 本＋横木＋幕）と GoalPresenter。紙吹雪・着順の木札 8 枚。</summary>
        static GoalPresenter GoalGate(Transform cp, StageBuild sb, WorldContext ctx)
        {
            var gate = GenUtil.Child(cp, "GoalGate", Vector3.zero).transform;
            GenUtil.Prim(PrimitiveType.Cylinder, "PostL", gate, new Vector3(-GATE_HALF, 3f, 0f), new Vector3(0.5f, 3f, 0.5f), Wood);
            GenUtil.Prim(PrimitiveType.Cylinder, "PostR", gate, new Vector3(GATE_HALF, 3f, 0f), new Vector3(0.5f, 3f, 0.5f), Wood);
            GenUtil.Prim(PrimitiveType.Cube, "Beam", gate, new Vector3(0f, 6.2f, 0f), new Vector3(GATE_HALF * 2f + 1f, 0.5f, 0.5f), DarkWood);
            GenUtil.Prim(PrimitiveType.Cube, "Banner", gate, new Vector3(0f, 5.2f, 0f), new Vector3(10f, 1.4f, 0.08f), Red);
            GenUtil.Text(gate, "BannerText", new Vector3(0f, 5.2f, -0.06f), Quaternion.identity, "ゴール", 8f, White, new Vector2(10f, 1.4f));
            GenUtil.Text(gate, "BannerTextBack", new Vector3(0f, 5.2f, 0.06f), Quaternion.Euler(0f, 180f, 0f), "ゴール", 8f, White, new Vector2(10f, 1.4f));

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
                var tag = GenUtil.Child(gate, "PlaceTag_" + (i + 1), new Vector3(0f, 3f, 0f));
                GenUtil.Prim(PrimitiveType.Cube, "Board", tag.transform, Vector3.zero, new Vector3(1.0f, 0.45f, 0.05f), Wood);
                texts[i] = GenUtil.Text(tag.transform, "Text", new Vector3(0f, 0f, -0.04f), Quaternion.identity, (i + 1) + "着", 2.5f, White, new Vector2(1f, 0.45f));
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
            string part = "Gems_0" + (gemIndex % 4 + 1);
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

        /// <summary>スターター（Capsule の人物＋旗＋拳銃の腕）。3 ステージのスタート脇に移る。</summary>
        public static Starter BuildStarter(WorldContext ctx)
        {
            StageBuild s0 = ctx.stages[0];
            var go = GenUtil.World(ctx.root, "Starter", s0.starterSpot.position, s0.starterSpot.eulerAngles.y);
            var body = go.transform;
            GenUtil.Prim(PrimitiveType.Capsule, "Body", body, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.9f, 0.5f), new Color(0.35f, 0.25f, 0.2f));
            GenUtil.Prim(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.95f, 0f), Vector3.one * 0.32f, new Color(0.9f, 0.75f, 0.6f));
            GenUtil.Prim(PrimitiveType.Cylinder, "HatBrim", body, new Vector3(0f, 2.1f, 0f), new Vector3(0.6f, 0.02f, 0.6f), DarkWood);
            GenUtil.Prim(PrimitiveType.Cylinder, "HatTop", body, new Vector3(0f, 2.22f, 0f), new Vector3(0.3f, 0.12f, 0.3f), DarkWood);

            var flagPivot = GenUtil.Child(body, "FlagPivot", new Vector3(-0.3f, 1.4f, 0f)).transform;
            GenUtil.Prim(PrimitiveType.Cylinder, "Pole", flagPivot, new Vector3(0f, 0.6f, 0f), new Vector3(0.04f, 0.6f, 0.04f), Wood);
            GenUtil.Prim(PrimitiveType.Quad, "Cloth", flagPivot, new Vector3(0.3f, 1.0f, 0f), new Vector3(0.6f, 0.4f, 1f), Red);
            GenUtil.Prim(PrimitiveType.Quad, "ClothBack", flagPivot, new Vector3(0.3f, 1.0f, 0f), new Vector3(0.6f, 0.4f, 1f), Red)
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var arm = GenUtil.Child(body, "PistolArm", new Vector3(0.3f, 1.5f, 0f)).transform;
            GenUtil.Prim(PrimitiveType.Cube, "Arm", arm, new Vector3(0f, -0.3f, 0f), new Vector3(0.12f, 0.6f, 0.12f), new Color(0.35f, 0.25f, 0.2f));
            var gun = GenUtil.InstantiateModel(GenUtil.Pack("Revolver"), arm, "Revolver");
            if (gun != null) { gun.transform.localPosition = new Vector3(0f, -0.62f, 0.05f); gun.transform.localScale = Vector3.one; }

            var st = GenUtil.AddU<Starter>(go);
            st.flagPivot = flagPivot;
            st.pistolArm = arm;
            st.audioSource = GenUtil.Audio(go, true);
            st.pistolSmoke = GenUtil.Particles(arm, "PistolSmoke", new Vector3(0f, -0.7f, 0.2f), new Color(0.85f, 0.85f, 0.85f, 0.7f), 0.3f, 1f, 1.5f, false, 0f, 0.1f);
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
