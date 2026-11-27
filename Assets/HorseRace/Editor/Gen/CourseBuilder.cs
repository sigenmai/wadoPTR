using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// コース：スタート位置（8 頭の横一列）・スタートの門（柵 9 枚、号砲で消える）、CP（80m ごと、Is Trigger の BoxCollider、幅は道幅に合わせる。
    /// 黒板 RankBoard は 3 つ目ごとの CP・最後の下りの直前の CP・ゴール・絞りの 120m 手前）、全力の合図（最後の下りの始まり）、
    /// ゴールゲート（最後の CP = isGoal ＋ GoalPresenter、丸太の門）、ゴール後の囲い、スターターの立ち位置。遺物は SetPieceBuilder（近道が決まった後）。
    /// コース外リセットは MountController が CP の線分からの距離で行う（Validate が線分からのずれ ≤ 60m を確認）。
    /// </summary>
    public static class CourseBuilder
    {
        public const float CP_WIDTH = CourseGeometry.CP_WIDTH, CP_HEIGHT = CourseGeometry.CP_HEIGHT, CP_DEPTH = CourseGeometry.CP_DEPTH;   // 1 か所（CourseGeometry）で決める
        public const float BOARD_SIDE = CourseGeometry.BOARD_SIDE;     // 黒板はコース中心から右へ 15m（平らな道 ±12m の外）
        public const float GATE_HALF = CourseGeometry.GATE_HALF;
        public const float START_SIDE = CourseGeometry.START_SIDE;
        public const float PEN_RADIUS = CourseGeometry.PEN_RADIUS;
        public const float PEN_BACK = CourseGeometry.PEN_BACK;   // 囲いの中心 = 流しの終点の 14m 手前
        public const int BOARD_EVERY = CourseGeometry.BOARD_EVERY;        // 黒板は CP3・CP6・…とゴール

        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        static readonly Color DarkWood = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Chalk = new Color(0.17f, 0.23f, 0.18f);   // 黒板の板（ui-r1-08）
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
            StartGate(line.transform, sb, r0, t0, yaw0);
            // スターターはスタートラインの左脇（レーンの外）、コースのほうを向く
            Vector3 spot = sb.GroundWorld(lineP - r0 * START_SIDE + t0 * 2f);
            sb.starterSpot = GenUtil.World(root, "StarterSpot", spot, WorldLayout.YawDeg(r0)).transform;
            sb.Exclude(spot, 14f);    // スターターの周りに岩を置かない
            // 空のカウント（ui-r1-09）：コースの中心線上、スタートラインの 30m 先・12m 上。騎手のほう（コースの進む向きを見る人）に読める
            Vector3 skyP = sb.GroundWorld(WorldLayout.PointAtArc(c, StageDefinitions.START_ARC_M + 3f + 30f)) + Vector3.up * 12f;
            sb.skyCountSpot = GenUtil.World(root, "SkyCountSpot", skyP, yaw0).transform;

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
                // 箱の幅は柵から柵まで（柵から CourseBarrier.CP_SIDE_CLEAR_M 内側：system-r2-02。柵沿いの外の線でも CP を通る）
                float half = CourseBarrier.CpHalfWidth(sb.def, sb.courseLocal, sb.goalArc, arcs[k]);
                box.size = new Vector3(half * 2f, CP_HEIGHT, CP_DEPTH);
                box.center = new Vector3(0f, CP_HEIGHT * 0.5f - 1f, 0f);
                go.layer = WorldLayers.TRIGGERS;
                var cp = GenUtil.AddU<Checkpoint>(go);
                cp.index = k + 1;
                cp.stageIndex = st;
                cp.isGoal = goal;
                cp.next = null;
                // 前の CP（最初はスタートライン）からこの CP までの中心の折れ線（10m ごと、system-r3-13）
                cp.pathIn = WorldLayout.ArcPolyline(c, k == 0 ? StageDefinitions.START_ARC_M + 3f : arcs[k - 1], arcs[k], PATH_SAMPLE_M);
                if (prev != null) prev.next = cp;
                prev = cp;
                sb.checkpoints.Add(cp);
                if (k == 0) sb.firstCheckpoint = cp;
                // 黒板は 3 つ目ごとの CP・最後の下りの直前の CP・ゴールだけ（RankBoard の書き直しの負荷を抑える）
                if (CourseMarks.HasBoard(sb.def, arcs, k, sb.goalArc)) ctx.rankBoards.Add(ChalkBoard(go.transform, sb, p, t, arcs[k], "Board"));
                if (goal) sb.goal = GoalGate(go.transform, sb, ctx);
            }

            // ---- ゴール後の囲い（流しの終点の手前。フィニッシュから 3 秒で着く距離より先） ----
            float total = CoursePath.Length(sb.courseLocal);
            float penArc = total - PEN_BACK;
            Vector3 penT = WorldLayout.TangentAtArc(c, penArc);
            Vector3 penP = sb.GroundWorld(WorldLayout.PointAtArc(c, penArc));
            sb.finishPen = GenUtil.World(root, "FinishPen", penP, WorldLayout.YawDeg(penT)).transform;
            Fence.Ring(sb.finishPen, PEN_RADIUS, sb);
            PenBoards(sb, ctx);

            // ---- 絞りの手前の黒板（course-r1-10）と全力の合図（course-r1-04） ----
            foreach (float a in CourseMarks.ChokeWarnArcs(sb.def))
            {
                Vector3 t = WorldLayout.TangentAtArc(c, a);
                Vector3 bp = sb.GroundWorld(WorldLayout.PointAtArc(c, a) + WorldLayout.RightOf(t) * CourseMarks.BoardSide(sb.def, a));
                var rb = ChalkBoardAt(root, bp, WorldLayout.YawDeg(t), "ChokeBoard", false);
                rb.stageIndex = st;
                ctx.rankBoards.Add(rb);
            }
            DashPoint(root, sb);
            CornerBoards(root, sb);
            SectorGates(root, sb);
            FinishChute(root, sb);
        }

        /// <summary>
        /// ゴール前の見物の直線（course-r3-08）：CourseMarks.FinishChute の部品（tools/preview も同じ）。レール・干し草は静的、街灯は暖色の灯り 10m、
        /// 旗は立てた丸太 4m の上に赤い手配書の紙（ゴールの幕と同じ赤のマテリアル、表と裏）、「あと 100m」の札。当たりは置かない（柵はゴールの 45m 手前で切れる）。
        /// </summary>
        static void FinishChute(Transform root, StageBuild sb)
        {
            var g = GenUtil.Child(root, "FinishChute", Vector3.zero).transform;
            g.position = Vector3.zero;
            var redMat = GenUtil.ToonColor(Red);
            int k = 0;
            foreach (var cp in CourseMarks.FinishChute(sb.def, sb.courseLocal, sb.goalArc))
            {
                Vector3 w = sb.Ground(cp.position.x, cp.position.z);
                if (cp.kind == CourseMarks.CHUTE_LAMP) { SetPieceBuilder.Lamp(g, w, cp.yaw, SetPieceBuilder.LampWarm, 10f); continue; }
                if (cp.kind == CourseMarks.CHUTE_SIGN)
                {
                    SetPieceBuilder.SignPost(g, "Last100", w, cp.yaw, "<b>あと 100m</b>", UiStyle.Gold, new Color(0f, 0f, 0f, 0f), 0f, 1.4f);
                    continue;
                }
                if (cp.kind == CourseMarks.CHUTE_FLAG)
                {
                    var flag = GenUtil.World(g, "ChuteFlag_" + (k++), w, cp.yaw).transform;
                    SetPieceBuilder.UprightLog(flag, "WoodLogs_01", w, cp.yaw, CourseMarks.CHUTE_POST_H, 1.1f);
                    for (int side = 0; side < 2; side++)
                    {
                        var sheet = GenUtil.PaperSheet(flag, side == 0 ? "Flag" : "FlagBack", "WantedPosters_02", new Vector3(cp.side * -0.55f, CourseMarks.CHUTE_POST_H - 1.25f, side == 0 ? -0.08f : 0.08f), null, 0.9f, 1.1f);
                        if (side == 1) sheet.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                        foreach (var rr in sheet.GetComponentsInChildren<Renderer>(true)) rr.sharedMaterial = redMat;
                    }
                    DressingBuilder.SetStatic(flag.gameObject);
                    continue;
                }
                var go = GenUtil.InstantiatePart(GenUtil.MODELS + "/" + cp.asset, cp.part, g, w - Vector3.up * 0.03f, cp.yaw, 1f, new Vector3(2.7f, 1f, 0.3f), Wood);
                go.transform.localScale = cp.scale;
                DressingBuilder.SetStatic(go);
            }
        }

        /// <summary>
        /// 急カーブの合図（course-r2-08）：頂点の 110・70・35m 手前の外側に札（丸太の柱に手配書の紙、赤い ››› ›› ›、曲がる向き）、
        /// 頂点の内側に干し草の山（2 段）、外側に街灯（橙の灯り 10m）。出口の先の目印（塔）は DressingPlan.GuideTowers。位置は CourseMarks と同じ。
        /// </summary>
        /// <summary>Checkpoint.pathIn の点の間隔（m）。</summary>
        public const float PATH_SAMPLE_M = 10f;

        static void CornerBoards(Transform root, StageBuild sb)
        {
            var g = GenUtil.Child(root, "CornerBoards", Vector3.zero).transform;
            g.position = Vector3.zero;
            Vector3 o = sb.def.Origin;
            int k = 0;
            foreach (var b in CourseMarks.CornerBoards(sb.def, sb.courseLocal, sb.goalArc))
            {
                Vector3 w = sb.Ground(b.position.x, b.position.z);
                SetPieceBuilder.SignPost(g, "CornerBoard_" + (k++), w, b.yaw, "<b><size=160%>" + CourseMarks.Chevrons(b.chevrons, b.side) + "</size></b>",
                    UiStyle.RedInk, new Color(0f, 0f, 0f, 0f), 0f, b.scale);   // 危険 = 赤（紙の上は赤インク）。70・35m 手前は柵の線の上に 1.8 倍（course-r3-07）
            }
            k = 0;
            foreach (var c in StageDefinitions.TightCorners(sb.def, sb.courseLocal))
            {
                Vector3[] m = CourseMarks.CornerApexMarks(sb.def, sb.courseLocal, c, sb.goalArc);
                Vector3 hay = sb.Ground(m[0].x, m[0].z), lamp = sb.Ground(m[1].x, m[1].z);
                float yaw = WorldLayout.YawDeg(WorldLayout.TangentAtArc(sb.courseLocal, c.apexArc));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hay, yaw, 1f, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hay + Vector3.up * 1.55f, yaw + 35f, 0.9f, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                SetPieceBuilder.Lamp(g, lamp, yaw + (c.side > 0 ? 90f : -90f), SetPieceBuilder.SignalOrange, 10f);
                // course-r3-07：外側の干し草の壁（2 段、当たりは柵だけ）
                const float S = CourseMarks.CORNER_WALL_SCALE;
                foreach (var wb in CourseMarks.CornerWall(sb.def, sb.courseLocal, c))
                {
                    Vector3 hp = sb.Ground(wb.position.x, wb.position.z);
                    DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), wb.part, g, hp - Vector3.up * 0.05f, wb.yaw, S, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                    DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), wb.part == "HayBales_01" ? "HayBales_02" : "HayBales_01", g, hp + Vector3.up * (1.5f * S),
                        wb.yaw + 90f, S * 0.95f, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                }
                k++;
            }
        }

        /// <summary>
        /// 区間の門（course-r2-15）：ゴールまでの 25・50・75% に、道の両脇 ±(半幅 + 1.5m) の丸太の柱（WoodLogs_01、6m）、柱の上に丸太 6 本の細い横木（art-r3-01：長さだけ伸ばす）、
        /// 柱の外に街灯 2 本、横木から手配書の紙（WantedPosters_02）に金の文字「残り 1.8 km」（60m 先から読める大きさ）。
        /// </summary>
        static void SectorGates(Transform root, StageBuild sb)
        {
            var g = GenUtil.Child(root, "SectorGates", Vector3.zero).transform;
            g.position = Vector3.zero;
            var arcs = CourseMarks.SectorGateArcs(sb.goalArc);
            var posts = CourseMarks.SectorGatePosts(sb.def, sb.courseLocal, sb.goalArc);
            for (int k = 0; k < arcs.Count; k++)
            {
                Vector3 t = WorldLayout.TangentAtArc(sb.courseLocal, arcs[k]);
                t = new Vector3(t.x, 0f, t.z).normalized;
                Vector3 rt = WorldLayout.RightOf(t);
                float yaw = WorldLayout.YawDeg(t);
                Vector3 l = sb.Ground(posts[2 * k].x, posts[2 * k].z), r = sb.Ground(posts[2 * k + 1].x, posts[2 * k + 1].z);
                var gate = GenUtil.World(g, "SectorGate_" + (k + 1), (l + r) * 0.5f, yaw).transform;
                const float H = 6f;
                SetPieceBuilder.UprightLog(gate, "WoodLogs_01", l, yaw, H, 1.1f);
                SetPieceBuilder.UprightLog(gate, "WoodLogs_01", r, yaw, H, 1.1f);
                float top = Mathf.Max(l.y, r.y) + H - 0.4f;
                // course-r3-01：横木は丸太の束 ×0.8 を引き伸ばさずにつなぐ（CourseMarks.BeamPieces、柱の外の端から外の端まで。tools/preview も同じ）
                Vector3 half = rt * 0.55f;
                foreach (var bp in CourseMarks.SectorBeamPieces(l - half, r + half, top))
                {
                    var beam = GenUtil.InstantiatePart(GenUtil.MODELS + "/" + bp.asset, bp.part, gate, bp.position, bp.euler.y, 1f, new Vector3(1.5f, 0.3f, 0.3f), DarkWood);
                    beam.transform.rotation = Quaternion.Euler(bp.euler);
                    beam.transform.localScale = bp.scale;
                    DressingBuilder.SetStatic(beam);
                }
                for (int sd = -1; sd <= 1; sd += 2)
                    SetPieceBuilder.Lamp(gate, sb.GroundWorld((sd < 0 ? l : r) + rt * (sd * 1.6f) - t * 0.8f), yaw + (sd > 0 ? -90f : 90f), SetPieceBuilder.LampWarm, 12f);
                // 残りの距離の紙（迫ってくる騎手のほうを向く）
                Vector3 mid = new Vector3((l.x + r.x) * 0.5f, top - 1.9f, (l.z + r.z) * 0.5f);
                var sheet = GenUtil.World(gate, "Remaining", mid, yaw);
                GenUtil.PaperSheet(sheet.transform, "Paper", "WantedPosters_02", new Vector3(0f, 0f, -0.05f), null, 4.2f, 1.5f);
                GenUtil.Text(sheet.transform, "Text", new Vector3(0f, 0.05f, -0.12f), Quaternion.identity, "<b>" + CourseMarks.RemainingText(sb.goalArc, arcs[k]) + "</b>",
                    6f, UiStyle.Gold, new Vector2(4f, 1.3f), TextStyle.Sign, true);
            }
        }

        /// <summary>
        /// 全力の合図（course-r1-04）：最後の下りの始まり（ゴールの 590m 手前）の両脇に街灯（橙の灯り 10m）、右に丸太の柱の札「ここから全力」。
        /// 位置は CourseMarks.MarkSpots と同じ。
        /// </summary>
        static void DashPoint(Transform root, StageBuild sb)
        {
            float dash = StageDefinitions.DashArc(sb.def, sb.goalArc);
            Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, dash);
            t = new Vector3(t.x, 0f, t.z).normalized;
            Vector3 rt = WorldLayout.RightOf(t), c = WorldLayout.PointAtArc(sb.courseWorld, dash);
            float hw = StageDefinitions.HalfWidthAt(sb.def, dash), yaw = WorldLayout.YawDeg(t);
            var g = GenUtil.World(root, "DashPoint", sb.GroundWorld(c), yaw).transform;
            for (int s = -1; s <= 1; s += 2)
                SetPieceBuilder.Lamp(g, sb.GroundWorld(c + rt * (s * (hw + CourseMarks.DASH_LAMP_OUT_M))), yaw + (s > 0 ? -90f : 90f), SetPieceBuilder.SignalOrange, 10f, 1.2f);
            SetPieceBuilder.SignPost(g, "DashSign", sb.GroundWorld(c + rt * (hw + CourseMarks.DASH_SIGN_OUT_M) + t * 3f), yaw, "<b>ここから全力</b>\n" + CourseMarks.FinishLine(sb.def), UiStyle.Gold,
                new Color(0f, 0f, 0f, 0f), 0f);
        }

        /// <summary>
        /// ゴール後の囲いの中の板（すべて既存の模型を置くだけ）。囲いの真ん中（ゴール後に移される所）から見て:
        ///  - 正面：中継の掲示板（NoticeBoard、NoticeBoardDisplay mode 0 = たまり場と同じ RenderTexture）と追う人を選ぶ木札（DirectorTag）。
        ///    ゴールした人は残りの走りをここで見る（system-r1-10）。
        ///  - 右前：黒板（RankBoard penBoard）に全員の順位・ゴール済み・残り時間。
        ///  - 左前（最終ステージ以外）：手配書の壁（馬・ラクダ・能力 4 枚の PosterCard）と台帳（EntryLedger）。
        ///    ステージの合間に次の地形に合わせて乗り物・能力を選び直す（system-r1-08）。
        /// </summary>
        static void PenBoards(StageBuild sb, WorldContext ctx)
        {
            Transform pen = sb.finishPen;
            Vector3 c = pen.position, fwd = pen.forward, right = pen.right;
            var root = GenUtil.Child(pen, "PenBoards", Vector3.zero).transform;
            int st = sb.def.index;

            Vector3 bp = sb.GroundWorld(c + fwd * 9f);
            ShowBuilder.Board(root, "PenBroadcast", bp, -fwd, NoticeBoardDisplay.MODE_BROADCAST, ctx, out Transform front, out Vector3 panelCenter);
            GenUtil.PaperSheet(front, "CaptionPaper", "WantedPosters_05", new Vector3(0f, 0.92f, 0.01f), null, 3.1f, 0.28f);
            GenUtil.Text(front, "Caption", new Vector3(0f, 1.06f, -0.01f), Quaternion.identity, "ゴールした人は残りの走りをここで見られる", 1.0f, UiStyle.InkFace,
                new Vector2(3f, 0.25f), TextStyle.Ink, true);
            ShowBuilder.Tags(front, null, ctx);

            Vector3 rp = sb.GroundWorld(c + fwd * 5f + right * 7.5f);
            Vector3 rdir = rp - c; rdir.y = 0f;
            var rb = ChalkBoardAt(root, rp, WorldLayout.YawDeg(rdir.normalized), "PenRankBoard", false);
            rb.stageIndex = st;
            rb.penBoard = true;
            rb.maxLines = 8;
            ctx.rankBoards.Add(rb);

            if (st + 1 >= StageDefinitions.All.Length) return;   // 最後のステージの後は乗り換えなし
            // 次のステージへの立て札（course-r2-05）：囲いの先、ゴールの先の切れ込みの向こうに次のステージがある
            SetPieceBuilder.SignPost(root, "NextStageSign", sb.GroundWorld(c + fwd * 12f + right * 4f), WorldLayout.YawDeg(fwd),
                "<b>次は " + StageDefinitions.All[st + 1].label + " →</b>", UiStyle.Gold, SetPieceBuilder.LampWarm, 8f);
            Vector3 wp = sb.GroundWorld(c + fwd * 5f - right * 7.5f);
            Vector3 wdir = wp - c; wdir.y = 0f;
            string next = HudMath.StageHint(st + 1);
            LobbyBuilder.PosterWallAt(root, ctx, wp, WorldLayout.YawDeg(wdir.normalized), "PenWantedWall",
                "<b><size=140%>次：" + next + "</size></b>\n手配書を取ると次のステージの乗り物・能力になる", 1.2f);
            // 台帳（サインし直し）：手配書の壁の手前の机（FarmProps_05）
            Vector3 dp = sb.GroundWorld(c + fwd * 2.5f - right * 4.5f);
            float dyaw = WorldLayout.YawDeg(wdir.normalized);
            var desk = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_05", root, dp, dyaw + 180f, 0.8f, new Vector3(2f, 0.8f, 1f), Wood);
            desk.name = "PenLedgerDesk";
            DressingBuilder.AddCollider(desk, false);
            float top = GenUtil.RendererBounds(desk).max.y;
            if (top < dp.y + 0.4f) top = dp.y + 0.8f;
            LobbyBuilder.LedgerBook(root, ctx, new Vector3(dp.x, top + 0.005f, dp.z), dyaw + 180f, "PenLedger", "台帳にサイン（次のステージの選択）");
        }

        /// <summary>CP の黒板（RankBoard）。迫ってくる騎手のほうを向けて、コースの右脇に立てる。</summary>
        static RankBoard ChalkBoard(Transform cp, StageBuild sb, Vector3 p, Vector3 t, float arc, string name)
        {
            Vector3 bp = sb.GroundWorld(p + WorldLayout.RightOf(t) * CourseMarks.BoardSide(sb.def, arc));
            var rb = ChalkBoardAt(cp, bp, WorldLayout.YawDeg(t), name, false);
            rb.stageIndex = sb.def.index;
            return rb;
        }

        public const int START_GATE_PIECES = CourseGeometry.START_GATE_PIECES;   // 柵 FarmProps_03（2.74m）× 12 ≈ 32.9m（広いスタートの道幅いっぱい、course-r3-09）

        /// <summary>
        /// スタートの門（course-r1-15）：スタートラインに柵 9 枚（FarmProps_03、Block の Collider）を道幅いっぱいに並べ、号砲で Starter が消す。
        /// 両端に街灯（暖色の灯り）と干し草の山。門は普段は消えていて、カウントダウンの間だけ出る。
        /// </summary>
        static void StartGate(Transform line, StageBuild sb, Vector3 r0, Vector3 t0, float yaw0)
        {
            Vector3 c = line.position;
            float half = START_GATE_PIECES * Fence.PIECE * 0.5f;
            var gate = GenUtil.Child(line, "StartGate", Vector3.zero);
            gate.transform.position = c;
            for (int i = 0; i < START_GATE_PIECES; i++)
            {
                Vector3 p = sb.GroundWorld(c + r0 * (-half + (i + 0.5f) * Fence.PIECE));
                Fence.Piece(gate.transform, p, yaw0);   // 柵の長手（ローカル X）を道の横向きに
            }
            sb.startGate = gate;
            gate.SetActive(false);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = sb.GroundWorld(c + r0 * (s * (half + 1.2f)));
                SetPieceBuilder.Lamp(line, post, yaw0 + (s > 0 ? -90f : 90f), SetPieceBuilder.LampWarm, 10f);
                for (int k = 0; k < 2; k++)
                {
                    Vector3 hp = sb.GroundWorld(c + r0 * (s * (half + 3f + k * 1.7f)) - t0 * 0.8f);
                    DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", line, hp, yaw0 + k * 25f, 1f, new Vector3(1.5f, 1.6f, 1.5f), Wood));
                    if (k == 0) DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", line, hp + Vector3.up * 1.55f, yaw0 + 40f, 0.9f,
                        new Vector3(1.5f, 1.6f, 1.5f), Wood));
                }
            }
        }

        /// <summary>
        /// 黒板 1 枚（art-r1-06）：掲示板の模型（NoticeBoard ×0.75）の板の面に黒板の色を塗り、文字を板の少し手前に置く。
        /// 文字は -Z 側（yaw の方向を見ている人に読める）。模型が無ければ板の代わりに単色の板。
        /// ui-r1-08/11：模型 ×0.8、板は黒板の色 (0.17,0.23,0.18)、文字はチョーク（UiStyle Chalk）で自動で縮む。
        /// コースの黒板（showLastRace = false）は走りながら読めるよう 2.2pt（上位 3 人＋自分の順位、RankBoard）、たまり場の黒板は 1.0pt の表。
        /// </summary>
        public static RankBoard ChalkBoardAt(Transform parent, Vector3 worldPos, float yaw, string name, bool showLastRace)
        {
            var go = GenUtil.World(parent, name, worldPos, yaw);
            const float SC = 0.8f;
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
            // 掲示板の模型が無いときは文字だけ（作った板は出さない）
            var text = GenUtil.Text(go.transform, "Chalk", textPos, Quaternion.identity, "出走前", showLastRace ? 1.0f : 2.2f, UiStyle.ChalkFace, textSize,
                TextStyle.Chalk, true);
            var rb = GenUtil.AddU<RankBoard>(go);
            rb.boardText = text;
            rb.maxLines = 8;
            rb.showLastRace = showLastRace;
            return rb;
        }

        /// <summary>
        /// ゴールゲート（course-r1-12）：柱 = 立てた丸太（WoodLogs_01、6.6m）、横木 = 丸太（WoodLogs_03 を引き伸ばして柱の間に渡す）、
        /// 幕 = 赤く塗った手配書の紙に「ゴール」、柱の横に街灯 2 本と暖色の灯り、柱の足元に干し草と樽。GoalPresenter・紙吹雪・着順の札 8 枚。
        /// </summary>
        static GoalPresenter GoalGate(Transform cp, StageBuild sb, WorldContext ctx)
        {
            var gate = GenUtil.Child(cp, "GoalGate", Vector3.zero).transform;
            Vector3 c = gate.position;
            Vector3 right = gate.right, fwd = gate.forward;
            float yaw = gate.eulerAngles.y;
            var deco = GenUtil.Child(gate, "Dressing", Vector3.zero).transform;
            // 柱・横木・杭・街灯・干し草・樽（art-r2-07）：置き方は CourseMarks.GoalGateParts（tools/preview も同じ）
            foreach (var gp0 in CourseMarks.GoalGateParts(c, fwd, p => sb.GroundWorld(p)))
            {
                if (gp0.light)
                {
                    SetPieceBuilder.Lamp(deco, gp0.position, gp0.euler.y, SetPieceBuilder.LampWarm, 10f, gp0.scale.x);
                    continue;
                }
                var go = GenUtil.InstantiatePart(GenUtil.MODELS + "/" + gp0.asset, gp0.part, deco, gp0.position, gp0.euler.y, 1f, new Vector3(1f, 1f, 1f), Wood);
                go.transform.rotation = Quaternion.Euler(gp0.euler.x, gp0.euler.y, gp0.euler.z);
                go.transform.localScale = gp0.scale;
                DressingBuilder.SetStatic(go);
            }
            // 幕（ui-r1-10 / course-r1-12）：手配書の紙（WantedPosters_02、1.4m）3 枚を下の横木から吊り、紙はパレットの赤に塗る。
            // 1 枚に 1 文字ずつ「ゴ」「ー」「ル」（金・縁取り、看板の文字）。表と裏
            string[] glyphs = { "ゴ", "ー", "ル" };
            var redMat = GenUtil.ToonColor(Red);
            for (int gi = 0; gi < 3; gi++)
            {
                float gx = (gi - 1) * 1.6f;
                for (int side = 0; side < 2; side++)
                {
                    float z = side == 0 ? -0.15f : 0.15f;
                    var sheet = GenUtil.PaperSheet(gate, "Banner_" + gi + (side == 0 ? "" : "_Back"), "WantedPosters_02", new Vector3(gx, CourseMarks.GATE_BANNER_Y, z), null, 1.4f, 1.6f);
                    if (side == 1) sheet.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    foreach (var rr in sheet.GetComponentsInChildren<Renderer>(true)) rr.sharedMaterial = redMat;
                    var gt = GenUtil.Text(gate, "BannerText_" + gi + (side == 0 ? "" : "_Back"), new Vector3(gx, CourseMarks.GATE_BANNER_Y + 0.25f, side == 0 ? -0.2f : 0.2f),
                        Quaternion.Euler(0f, side == 0 ? 0f : 180f, 0f), glyphs[gi], 9f, UiStyle.Gold, new Vector2(1.3f, 1.4f), TextStyle.Sign, true);
                    gt.fontStyle = TMPro.FontStyles.Bold;
                }
            }

            var gp = GenUtil.AddU<GoalPresenter>(gate.gameObject);
            gp.stageIndex = sb.def.index;
            gp.confetti = GenUtil.Particles(gate, "Confetti", new Vector3(0f, 6.6f, 0f), new Color(1f, 0.85f, 0.4f), 0.15f, 6f, 3f, false, 0f, 2f);
            gp.bellAudio = ctx.churchBell;
            // 1 着の札の金の火花（ui-r3-13、遺物の PickupBurst と同じ作り。GoalPresenter が札の位置へ動かして Emit 24）
            gp.winSparks = GenUtil.Particles(gate, "WinSparks", new Vector3(0f, 3f, 0f), new Color(1f, 0.82f, 0.35f), 0.12f, 4f, 0.8f, false, 0f, 0.3f);
            var fin = GenUtil.Child(gate, "FinishAudio", new Vector3(0f, 2f, 0f));
            gp.finishAudio = GenUtil.Audio(fin, false, false, SfxBuilder.Clip(SfxMath.STING));
            var tags = new Transform[8];
            var texts = new TMPro.TextMeshPro[8];
            for (int i = 0; i < 8; i++)
            {
                // 着順の札：手配書の紙（WantedPosters_04、1.1 × 0.75m：着順と名前の 2 行、ui-r2-14）。GoalPresenter が騎手の上へ動かす
                var tag = GenUtil.Child(gate, "PlaceTag_" + (i + 1), new Vector3(0f, 3f, 0f));
                GenUtil.PaperSheet(tag.transform, "Board", "WantedPosters_04", new Vector3(0f, -0.375f, 0f), null, 1.1f, 0.75f);
                texts[i] = GenUtil.Text(tag.transform, "Text", new Vector3(0f, 0f, -0.04f), Quaternion.identity, (i + 1) + "着", 2.5f, UiStyle.InkFace,
                    new Vector2(1.0f, 0.62f), TextStyle.Ink, true);
                texts[i].fontStyle = TMPro.FontStyles.Bold;
                tags[i] = tag.transform;
                tag.SetActive(false);
            }
            gp.placeTags = tags;
            gp.placeTagTexts = texts;
            return gp;
        }

        /// <summary>遺物（Packs/Gems の宝石 1 つ、Is Trigger の球 1.5m）。</summary>
        public static RelicPickup Relic(Transform parent, string name, Vector3 pos, int gemIndex, bool spare)
            => Relic(parent, name, pos, gemIndex, spare, pos);

        /// <summary>遺物の宝石の大きさ（art-r3-04：0.6m では 40m より先で見えなかった）。</summary>
        public const float RELIC_GEM_M = 1.1f;
        /// <summary>遺物の灯り（ブーストと同じ青緑、reference-games.md の決まり）。</summary>
        public static readonly Color RelicTeal = new Color(0.2f, 0.85f, 0.8f);

        /// <summary>
        /// 遺物（art-r3-04）：宝石 Gems_0N_01 を 1.1m に、宝石の 1m 上に青緑の灯り（7m）、宝石から 5m 立ちのぼる青緑の粒の柱（1 秒 6 粒、150m 先から見える）、
        /// 足元（ground、宙の遺物は地面）に小石 5 個の輪（拾う判定の外）。灯りと粒は宝石（visual）の子なので、拾われると一緒に消える。
        /// </summary>
        public static RelicPickup Relic(Transform parent, string name, Vector3 pos, int gemIndex, bool spare, Vector3 ground)
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
            // 宝石は大きさがまちまちなので最大 RELIC_GEM_M にそろえ、底を中心に寄せる
            Bounds b = GenUtil.RendererBounds(gem);
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (m > 0.01f) gem.transform.localScale = Vector3.one * (RELIC_GEM_M / m);
            b = GenUtil.RendererBounds(gem);
            Vector3 off = go.transform.position + new Vector3(0f, 0.8f, 0f) - b.center;
            foreach (Transform ch in gem.transform) ch.position += off;
            // 青緑の灯りと立ちのぼる粒（宝石の子：拾われると消える）
            GenUtil.PointLight(gem.transform, "RelicGlow", pos + Vector3.up * 1.0f, RelicTeal, 7f, 1.6f);
            var column = GenUtil.Particles(gem.transform, "RelicColumn", Vector3.zero, new Color(RelicTeal.r, RelicTeal.g, RelicTeal.b, 0.85f), 0.15f, 1.25f, 4f, true, 6f, 0.25f);
            column.transform.position = pos + Vector3.up * 0.9f;
            column.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // 円すいの向き（ローカル Z）を上へ
            var csh = column.shape;
            csh.shapeType = ParticleSystemShapeType.Cone;
            csh.angle = 2f;
            csh.radius = 0.25f;
            // 足元の小石の輪（拾う判定 1.5m の外、RockCollection）
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + 18f) * Mathf.Deg2Rad;
                Vector3 pp = ground + new Vector3(Mathf.Sin(a) * 2.0f, -0.05f, Mathf.Cos(a) * 2.0f);
                var peb = GenUtil.InstantiatePart(GenUtil.Pack("RockCollection"), "RockCollection_" + (8 * (1 + (GenUtil.Mod(gemIndex * 5 + i, 23)))).ToString("00"), go.transform, pp,
                    i * 71f, 2.2f, new Vector3(0.5f, 0.3f, 0.5f), new Color(0.55f, 0.42f, 0.32f));
                peb.name = "Pebble_" + i;
                DressingBuilder.SetStatic(peb);
            }
            var rp = GenUtil.AddU<RelicPickup>(go);
            rp.isSpare = spare;
            rp.visual = gem;
            rp.groundMask = WorldLayers.GroundMask;
            var snd = GenUtil.Child(go.transform, "PickupSound", Vector3.up);
            rp.pickupSound = GenUtil.Audio(snd, false, false, SfxBuilder.Clip(SfxMath.SPARKLE));
            // 拾ったときの火花（ui-r2-12）：宝石のまわりにはじける金の粒
            rp.burst = GenUtil.Particles(go.transform, "PickupBurst", new Vector3(0f, 0.8f, 0f), new Color(1f, 0.82f, 0.35f), 0.12f, 4f, 0.8f, false, 0f, 0.3f);
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
            SetPieceBuilder.UprightLog(flagPivot, "WoodLogs_04", flagPivot.position, yaw, 1.4f, 0.5f);   // 旗竿（丸太）
            var cloth = GenUtil.PaperSheet(flagPivot, "Cloth", "WantedPosters_02", new Vector3(0.35f, 0.75f, 0f), null, 0.6f, 0.4f);
            cloth.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            foreach (var r in cloth.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(Red);
            var clothBack = GenUtil.PaperSheet(flagPivot, "ClothBack", "WantedPosters_02", new Vector3(0.35f, 0.75f, 0f), null, 0.6f, 0.4f);
            clothBack.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            foreach (var r in clothBack.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = GenUtil.ToonColor(Red);

            // 拳銃の柱（ui-r1-20）：街灯の左に立てた丸太の杭（WoodLogs_01）。拳銃の腕は杭の 2.4m（PistolArm が回して上げる）
            var pistolPost = GenUtil.WoodPost(body, pos + go.transform.right * 0.6f, yaw, 3.35f, 0.22f);   // 立てた丸太（art-r3-02）
            pistolPost.name = "PistolPost";
            var arm = GenUtil.Child(body, "PistolArm", new Vector3(0.6f, 2.4f, -0.12f)).transform;
            var gun = GenUtil.InstantiateModel(GenUtil.Pack("Revolver"), arm, "Revolver");
            if (gun != null) { gun.transform.localPosition = new Vector3(0f, -0.2f, 0.05f); gun.transform.localScale = Vector3.one; }
            // 呼び込みの札：宙に浮かせず、柱の後ろの掲示板（NoticeBoard ×0.5）に。コースから読める（読む人はスターターのほうを見る = yaw + 180）
            LobbyBuilder.NoticeSign(body, "CallOut", pos - go.transform.forward * 2.3f, yaw + 180f, 0.5f, "<b><size=140%>スタート係</size></b>\n号砲 3 発で出走", 1.2f,
                UiStyle.InkFace, TextStyle.Ink, new Color(0.93f, 0.86f, 0.70f), false);

            var st = GenUtil.AddU<Starter>(go);
            st.flagPivot = flagPivot;
            st.pistolArm = arm;
            st.audioSource = GenUtil.Audio(go, true, false, SfxBuilder.Clip(SfxMath.SHOT));
            st.pistolSmoke = GenUtil.Particles(arm, "PistolSmoke", new Vector3(0f, -0.25f, 0.25f), new Color(0.85f, 0.85f, 0.85f, 0.7f), 0.3f, 1f, 1.5f, false, 0f, 0.1f);
            // 砂煙はスタートラインに沿って（スターターの右手 = コース側）3 か所
            var dust = new ParticleSystem[3];
            for (int i = 0; i < 3; i++)
                dust[i] = GenUtil.Particles(body, "StartDust_" + i, new Vector3(0f, 0.3f, 6f + i * 8f), new Color(0.85f, 0.72f, 0.5f, 0.8f), 1.2f, 2f, 2f, false, 0f, 2.5f);
            st.startDust = dust;
            // 空のカウント（ui-r1-09/11）：スターターの子ではなく、ステージごとのスタートの前方の中心線上（skySpots）へ動かす。看板の文字（縁取りと影）、表と裏
            Transform sky0 = s0.skyCountSpot != null ? s0.skyCountSpot : body;
            st.skyCountText = GenUtil.Text(ctx.root, "SkyCount", sky0.position, sky0.rotation, "", 60f, UiStyle.SignFace, new Vector2(30f, 12f), TextStyle.Sign, true);
            st.skyCountText.fontStyle = TMPro.FontStyles.Bold;
            st.skyCountBack = GenUtil.Text(ctx.root, "SkyCountBack", sky0.position, sky0.rotation * Quaternion.Euler(0f, 180f, 0f), "", 60f, UiStyle.SignFace,
                new Vector2(30f, 12f), TextStyle.Sign, true);
            st.skyCountBack.fontStyle = TMPro.FontStyles.Bold;
            var spots = new Transform[ctx.stages.Count];
            var skies = new Transform[ctx.stages.Count];
            for (int i = 0; i < spots.Length; i++) { spots[i] = ctx.stages[i].starterSpot; skies[i] = ctx.stages[i].skyCountSpot; }
            st.stageSpots = spots;
            st.skySpots = skies;
            // スタートの門（course-r1-15）：ステージごと。カウントダウンで出し、号砲で消す
            var gates = new GameObject[ctx.stages.Count];
            for (int i = 0; i < gates.Length; i++) gates[i] = ctx.stages[i].startGate;
            st.startGates = gates;
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
