using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 観戦・演出（PLAYTEST Task 10 の配置を自動化）。LobbyBuilder / DressingBuilder から呼ばれる。
    ///   中継の RenderTexture（Generated/BroadcastRT.renderTexture、960×540、Depth 24、AA なし、Mipmap なし）、
    ///   M_Broadcast（Unlit/Texture = RT）・M_Parchment（Unlit/Texture = 手配書の紙）※表示用なのでトゥーン対象外、
    ///   掲示板 NoticeBoard.fbx ×3（Mode 0 中継 / 1 電報 / 2 エントリー一覧）、SpectatorDirector（無効のカメラ 1 台）、
    ///   木札 DirectorTag ×9（1〜8 位、0 = おまかせ）、新聞売り TownCrier、表彰台 Podium、教会の鐘（2D の AudioSource）。
    /// </summary>
    public static class ShowBuilder
    {
        public const string RT_PATH = GenUtil.GENERATED + "/BroadcastRT.renderTexture";
        public const string BROADCAST_MAT = GenUtil.GEN_MATERIALS + "/M_Broadcast.mat";
        public const string PARCHMENT_MAT = GenUtil.GEN_MATERIALS + "/M_Parchment.mat";

        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        static readonly Color DarkWood = new Color(0.30f, 0.20f, 0.12f);
        static readonly Color Ink = new Color(0.25f, 0.15f, 0.08f);
        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);

        public static void CreateAssets(WorldContext ctx)
        {
            GenUtil.EnsureFolder(GenUtil.GEN_MATERIALS);
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RT_PATH);
            if (rt == null)
            {
                rt = new RenderTexture(960, 540, 24);
                rt.name = "BroadcastRT";
                rt.antiAliasing = 1;
                rt.useMipMap = false;
                rt.autoGenerateMips = false;
                AssetDatabase.CreateAsset(rt, RT_PATH);
            }
            else
            {
                rt.Release();
                rt.width = 960; rt.height = 540; rt.depth = 24;
                rt.antiAliasing = 1; rt.useMipMap = false; rt.autoGenerateMips = false;
                EditorUtility.SetDirty(rt);
            }
            ctx.broadcastRT = rt;
            ctx.broadcastMaterial = Unlit(BROADCAST_MAT, rt);
            var paper = AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_PAPER);
            if (paper == null) GenUtil.Warn("羊皮紙のテクスチャが見つかりません: " + GenUtil.WANTED_PAPER);
            ctx.parchmentMaterial = Unlit(PARCHMENT_MAT, paper);

            // 教会の鐘（2D、全員に聞こえる。3 ステージ共用）
            var bell = GenUtil.Child(ctx.root, "ChurchBell", Vector3.zero);
            ctx.churchBell = GenUtil.Audio(bell, true, false, SfxBuilder.Clip(SfxMath.BELL));
        }

        static Material Unlit(string path, Texture tex)
        {
            var sh = Shader.Find("Unlit/Texture");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            if (sh != null) m.shader = sh;
            m.mainTexture = tex;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>たまり場の掲示板 3 枚（center を中心に facing と直角に 6m ずつ並べ、facing の向き＝焚き火のほうを向く）と中継・木札・新聞売り（crierPos、crierYaw）。</summary>
        public static void BuildBoards(Transform parent, WorldContext ctx, Vector3 center, Vector3 facing, Vector3 crierPos, float crierYaw)
        {
            var root = GenUtil.World(parent, "NoticeBoards", center).transform;

            // 中継（SpectatorDirector とカメラ）
            var dirGo = GenUtil.World(root, "SpectatorDirector", center + Vector3.up * 3f);
            var camGo = GenUtil.Child(dirGo.transform, "BroadcastCamera", Vector3.zero);
            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = ctx.broadcastRT;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = SceneAssembler.BROADCAST_FAR_CLIP;   // 霧の終わり（1100m）まで。1200m 以下
            cam.enabled = false;
            var dir = GenUtil.AddU<SpectatorDirector>(dirGo);
            dir.broadcastCamera = cam;
            dir.renderTexture = ctx.broadcastRT;
            dir.followDistance = 11f;
            dir.followHeight = 4.5f;
            dir.lookHeight = 1.2f;
            ctx.director = dir;

            string[] titles = { "中継", "電報", "出走表" };
            NoticeBoardDisplay telegram = null;
            Transform telegramFront = null;
            float telegramPanelY = 2f;
            for (int mode = 0; mode < 3; mode++)
            {
                Vector3 side = new Vector3(facing.z, 0f, -facing.x);   // 並ぶ向き（facing と直角）
                Vector3 p = center + side * ((mode - 1) * 6f);
                var nb = Board(root, "NoticeBoard_" + titles[mode], p, facing, mode, ctx, out Transform front, out Vector3 panelCenter);
                if (mode == 1) { telegram = nb; telegramFront = front; telegramPanelY = panelCenter.y - p.y; }
                if (mode == 0)
                {
                    // 中継の字幕：板の下の紙の帯（WantedPosters_05）に「第1幕 荒野｜3位 ○○ を追跡｜残り 2:10」
                    GenUtil.PaperSheet(front, "CaptionPaper", "WantedPosters_05", new Vector3(0f, 0.92f, 0.01f), null, 3.1f, 0.28f);
                    dir.captionText = GenUtil.Text(front, "Caption", new Vector3(0f, 1.06f, -0.01f), Quaternion.identity, "", 1.2f, UiStyle.InkFace,
                        new Vector2(3f, 0.25f), TextStyle.Ink, true);
                    dir.tags = Tags(front, dir, ctx);
                }
            }

            // 予想の札（system-r2-15）：電報の掲示板の下に、走る人の名前の札 8 枚
            if (telegramFront != null) PickCards(telegramFront, ctx);

            // 新聞売りの屋台（art-r1-12：人の模型は無いので、木箱を積んだ屋台と街灯。号外の紙は街灯の腕に吊り、呼び込みのときに振り上げる）
            var crierGo = GenUtil.World(root, "TownCrier", crierPos, crierYaw);
            Vector3 cp = crierGo.transform.position, cr = crierGo.transform.right, cf = crierGo.transform.forward;
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_11", crierGo.transform, cp, crierYaw, 1f, new Vector3(1.2f, 1.1f, 1.2f), Wood).name = "Stall";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_12", crierGo.transform, cp + Vector3.up * 1.1f + cr * 0.1f, crierYaw + 15f, 0.8f,
                new Vector3(1f, 1f, 1f), Wood).name = "StallTop";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_15", crierGo.transform, cp + cr * 1.1f, 0f, 1f, new Vector3(0.5f, 0.7f, 0.5f), Wood).name = "Barrel";
            GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", crierGo.transform, cp - cr * 0.9f - cf * 0.3f, crierYaw + 90f, 0.8f,
                new Vector3(0.3f, 2.5f, 0.3f), Wood).name = "Lamp";
            for (int i = 0; i < 3; i++)   // 売り物の新聞の束（紙を重ねて置く）
                GenUtil.PaperSheet(crierGo.transform, "Bundle_" + i, "WantedPosters_01", new Vector3(-0.3f + i * 0.3f, 1.12f, -0.62f), null, 0.28f, 0.36f);
            var arm = GenUtil.Child(crierGo.transform, "PaperArm", new Vector3(-0.9f, 1.9f, -0.3f)).transform;
            GenUtil.PaperSheet(arm, "Paper", "WantedPosters_01", new Vector3(0f, -0.5f, -0.05f), null, 0.32f, 0.44f);
            var crier = GenUtil.AddU<TownCrier>(crierGo);
            // 電報の紙（ui-r1-20）：屋台の上（paperFrom）から電報の掲示板の右の飾りの位置（paperTo）へ、呼び声のときに弧を描いて滑って貼られる
            if (telegramFront != null)
            {
                var from = GenUtil.Child(crierGo.transform, "TelegramFrom", new Vector3(0f, 1.25f, -0.4f)).transform;
                from.rotation = telegramFront.rotation;
                var to = GenUtil.Child(telegramFront, "TelegramTo", new Vector3(1.93f, telegramPanelY - 0.35f, -0.05f)).transform;
                var paperGo = GenUtil.World(root, "TelegramPaper", from.position);
                paperGo.transform.rotation = from.rotation;
                GenUtil.PaperSheet(paperGo.transform, "Sheet", "WantedPosters_03", new Vector3(0f, 0f, 0f), null, 0.46f, 0.62f);
                GenUtil.Text(paperGo.transform, "Text", new Vector3(0f, 0.36f, -0.01f), Quaternion.identity, "<b>電報</b>\n着順は板に", 0.6f, UiStyle.InkFace,
                    new Vector2(0.4f, 0.4f), TextStyle.Ink, true);
                paperGo.transform.SetPositionAndRotation(to.position, to.rotation);   // 最初は貼ってある
                crier.telegramPaper = paperGo.transform;
                crier.paperFrom = from;
                crier.paperTo = to;
            }
            crier.callAudio = GenUtil.Audio(crierGo, true, false, SfxBuilder.Clip(SfxMath.CHIME));
            // 観客の予想の結果（ui-r3-16）：当たり = 鈴、はずれ = 鈍い音（同じ 2D の声から）
            crier.pickHitClip = SfxBuilder.Clip(SfxMath.CHIME);
            crier.pickMissClip = SfxBuilder.Clip(SfxMath.THUD);
            crier.boards = telegram != null ? new[] { telegram } : new NoticeBoardDisplay[0];
            crier.paperArm = arm;
            ctx.crier = crier;
        }

        /// <summary>掲示板 1 枚。パネルの法線（メッシュから読む）を facing に向け、文字はパネルの少し手前。</summary>
        internal static NoticeBoardDisplay Board(Transform parent, string name, Vector3 pos, Vector3 facing, int mode, WorldContext ctx,
            out Transform front, out Vector3 panelCenter)
        {
            var holder = GenUtil.PlaceModel(GenUtil.Prop("NoticeBoard"), parent, name, pos, 0f, 1f, new Vector3(4.5f, 3.8f, 0.3f), Wood);
            var panel = GenUtil.FindDeep(holder.transform, "NoticeBoard_Panel");
            Vector3 normal = facing;
            if (panel != null)
            {
                normal = GenUtil.FirstNormalWorld(panel);
                GenUtil.YawAlign(holder.transform, normal, facing);
                normal = GenUtil.FirstNormalWorld(panel);
            }
            else GenUtil.Warn("NoticeBoard.fbx に NoticeBoard_Panel が無いため、板の代わりに Quad を使います");

            Renderer pr;
            if (panel != null)
            {
                pr = panel.GetComponent<Renderer>();
                panelCenter = pr.bounds.center;
            }
            else
            {
                var q = GenUtil.Prim(PrimitiveType.Quad, "Panel_Fallback", holder.transform, new Vector3(0f, 2.15f, -0.05f), new Vector3(3.2f, 1.8f, 1f), Paper);
                q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                pr = q.GetComponent<Renderer>();
                panelCenter = pr.bounds.center;
                normal = facing;
            }
            pr.sharedMaterial = ctx.parchmentMaterial;
            DressingBuilder.AddCollider(holder, false);

            // 「手前」の基準：位置 = 足元、+Z = 見ている人から板へ（TMP は +Z を見る人に読める）
            var f = new GameObject("Front");
            f.transform.SetParent(holder.transform, false);
            f.transform.position = new Vector3(panelCenter.x, pos.y, panelCenter.z) + normal * 0.03f;
            f.transform.rotation = Quaternion.LookRotation(-new Vector3(normal.x, 0f, normal.z).normalized, Vector3.up);
            front = f.transform;

            // 枠の左右に手配書（飾り）
            for (int side = -1; side <= 1; side += 2)
            {
                var ph = GenUtil.Child(front, side < 0 ? "PosterL" : "PosterR", new Vector3(side * 1.93f, panelCenter.y - pos.y - 0.35f, -0.02f));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GenUtil.WANTED_CARDS + "/" + (side < 0 ? "Wanted_Original" : (mode == 0 ? "Wanted_Horse" : (mode == 1 ? "Wanted_Camel" : "Wanted_Dust"))) + ".png");
                LobbyBuilder.Poster(ph.transform, "WantedPosters_01", tex, "Poster");
                GenUtil.Tag(ph, "Lobby/BoardPoster/WantedPosters|WantedPosters_01#" + mode + (side < 0 ? "L" : "R"));
            }

            var text = GenUtil.Text(front, "BoardText", new Vector3(0f, panelCenter.y - pos.y, 0f), Quaternion.identity, "",
                1.6f, UiStyle.InkFace, new Vector2(3.0f, 1.7f), TextStyle.Ink, true);
            var nb = GenUtil.AddU<NoticeBoardDisplay>(holder);
            nb.mode = mode;
            nb.panelRenderer = pr;
            nb.broadcastMaterial = ctx.broadcastMaterial;
            nb.parchmentMaterial = ctx.parchmentMaterial;
            nb.boardText = text;
            nb.maxLines = 8;
            if (mode == 0) nb.director = ctx.director;
            ctx.noticeBoards.Add(nb);
            return nb;
        }

        /// <summary>
        /// 中継の木札（ui-r1-17）：板の下に手配書の紙（WantedPosters_04、0.42 × 0.3m）を 9 枚ピンで留め、「1位」〜「8位」「おまかせ」（0.9pt）。
        /// 選んでいる札には宝石のピン（Gems_03_02）。
        /// </summary>
        internal static DirectorTag[] Tags(Transform front, SpectatorDirector dir, WorldContext ctx)
        {
            var tags = new DirectorTag[9];
            for (int i = 0; i < 9; i++)
            {
                int rank = i < 8 ? i + 1 : 0;
                string label = rank == 0 ? "おまかせ" : rank + "位";
                string hint = rank == 0 ? "中継：おまかせ" : "中継：" + rank + "位を追う";
                var go = GenUtil.Child(front, "Tag_" + (rank == 0 ? "Auto" : rank.ToString()), new Vector3(-2.0f + i * 0.5f, 0.6f, -0.05f));
                GenUtil.PaperSheet(go.transform, "Paper", "WantedPosters_04", new Vector3(0f, -0.15f, 0.01f), null, 0.42f, 0.3f);
                var labelText = GenUtil.Text(go.transform, "Label", new Vector3(0f, 0f, -0.01f), Quaternion.identity, label, 0.9f, UiStyle.InkFace, new Vector2(0.38f, 0.24f), TextStyle.Ink, true);
                labelText.fontStyle = TMPro.FontStyles.Bold;
                labelText.fontSizeMin = 0.6f;   // 「3位 名前」でも 0.6pt より小さくしない（ui-r2-14）
                var mark = GenUtil.InstantiatePart(GenUtil.Pack("Gems"), "Gems_03_02", go.transform, go.transform.position, 0f, 1f,
                    new Vector3(0.05f, 0.07f, 0.05f), new Color(0.8f, 0.15f, 0.1f));
                mark.name = "SelectedMark";
                GenUtil.FitMax(mark, 0.07f);
                mark.transform.localPosition = new Vector3(0f, 0.11f, -0.03f);
                mark.SetActive(false);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(0.42f, 0.3f, 0.08f);
                var tag = GenUtil.AddU<DirectorTag>(go);
                tag.director = dir;
                tag.followRank = rank;
                tag.selectedMark = mark;
                tag.label = labelText;
                tag.baseLabel = label;
                GenUtil.SetInteractText(tag, hint);
                tags[i] = tag;
                ctx.directorTags.Add(tag);
            }
            return tags;
        }

        /// <summary>
        /// 予想の札（system-r2-15）：電報の板の下に手配書の紙（WantedPosters_04、0.44 × 0.32m）を 8 枚（value = スロット 0..7）。
        /// 札の文字は RaceManager が乗り手の名前・乗り物・能力に書き換え、枠が空いている札は隠す。選んだ札は別の紙（WantedPosters_05）と宝石のピン。
        /// 札の上の紙の帯（WantedPosters_05）に「予想（1着 3点・3着まで 1点） 通算 N点／あなたの予想：…」。モデルは作らない（既存の紙と宝石）。
        /// </summary>
        internal static void PickCards(Transform front, WorldContext ctx)
        {
            GenUtil.PaperSheet(front, "PickPaper", "WantedPosters_05", new Vector3(0f, 0.86f, 0.01f), null, 3.1f, 0.3f);
            ctx.pickStatus = GenUtil.Text(front, "PickStatus", new Vector3(0f, 1.01f, -0.01f), Quaternion.identity, "予想", 1.0f, UiStyle.InkFace,
                new Vector2(3f, 0.27f), TextStyle.Ink, true);
            for (int i = 0; i < RaceManager.MAX_SLOTS; i++)
            {
                var go = GenUtil.Child(front, "Pick_" + (i + 1), new Vector3(-1.75f + i * 0.5f, 0.55f, -0.05f));
                var paper = GenUtil.Child(go.transform, "Poster", Vector3.zero);
                GenUtil.PaperSheet(paper.transform, "Paper", "WantedPosters_04", new Vector3(0f, -0.16f, 0.01f), null, 0.44f, 0.32f);
                var taken = GenUtil.Child(go.transform, "Taken", Vector3.zero);
                GenUtil.PaperSheet(taken.transform, "Paper", "WantedPosters_05", new Vector3(0f, -0.16f, 0.01f), null, 0.44f, 0.32f);
                var mark = GenUtil.InstantiatePart(GenUtil.Pack("Gems"), "Gems_03_02", taken.transform, go.transform.position, 0f, 1f,
                    new Vector3(0.05f, 0.07f, 0.05f), new Color(0.95f, 0.75f, 0.2f));
                mark.name = "PickedMark";
                GenUtil.FitMax(mark, 0.07f);
                mark.transform.localPosition = new Vector3(0f, 0.12f, -0.03f);
                taken.SetActive(false);
                var label = GenUtil.Text(go.transform, "Label", new Vector3(0f, 0f, -0.012f), Quaternion.identity, "", 0.8f, UiStyle.InkFace,
                    new Vector2(0.4f, 0.27f), TextStyle.Ink, true);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(0.44f, 0.32f, 0.08f);
                var pc = GenUtil.AddU<PosterCard>(go);
                pc.kind = PosterCard.KIND_PICK;
                pc.value = i;
                pc.posterVisual = paper;
                pc.takenVisual = taken;
                pc.label = label;
                pc.takeSound = GenUtil.Audio(go, false, false, SfxBuilder.Clip(SfxMath.RUSTLE));
                GenUtil.SetInteractText(pc, "予想：この人が勝つ");
                go.SetActive(false);   // 枠に人がいる札だけ RaceManager が出す
                ctx.pickCards.Add(pc);
            }
        }

        /// <summary>表彰台の後ろの「総合結果」の掲示板（NoticeBoard ×0.8、紙の板・看板の文字）。観客は表彰台の正面（+Z）から見る。</summary>
        /// <summary>見出し（Podium.resultsHeading と同じ）。4〜8 位は Podium がこの下に小さく書く（ui-r3-06）。</summary>
        public const string RESULTS_HEADING = HudMath.RESULTS_HEADING;

        static TMPro.TextMeshPro NoticeSignForPodium(Transform holder, Vector3 pos, float yawFacingAudience)
        {
            Vector3 back = holder.TransformPoint(new Vector3(0f, 0f, -3.2f));
            back.y = pos.y;
            var t = LobbyBuilder.NoticeSign(holder, "ResultsBanner", back, yawFacingAudience + 180f, 0.8f, RESULTS_HEADING, 2.4f,
                UiStyle.Gold, TextStyle.Sign, new Color(0.36f, 0.24f, 0.15f));
            return t;
        }

        /// <summary>
        /// 表彰台（Props/Podium は低い円台なので、その上に木の段を 3 つ積む：1 位 = 中央・一番高い、2 位 = 左、3 位 = 右）。
        /// 段の上に立ち位置（0 = 1 位、1 = 2 位、2 = 3 位）、花火 4 つ。台の当たり判定は MeshCollider（形のまま）、段は BoxCollider。
        /// </summary>
        public static Podium BuildPodium(Transform parent, Vector3 pos, float yawFacingAudience)
        {
            var holder = GenUtil.PlaceModel(GenUtil.Prop("Podium"), parent, "Podium", pos - Vector3.up * 0.05f, yawFacingAudience, 1f,
                new Vector3(5f, 0.65f, 5f), new Color(0.8f, 0.7f, 0.5f));
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
            Bounds b = GenUtil.RendererBounds(holder);
            float baseTop = b.max.y - holder.transform.position.y;
            if (baseTop < 0.05f) baseTop = 0.65f;
            float[] x = { 0f, -1.5f, 1.5f };
            float[] h = { 1.0f, 0.7f, 0.45f };
            string[] label = { "1", "2", "3" };
            var steps = new Transform[3];
            var plates = new TMPro.TextMeshPro[3];
            var plateRoots = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                // 段（ui-r1-13）：当たり判定は見えない箱、見た目は干し草（HayBales_01〜03）を段の大きさ 1.4 × h × 1.4 に引き伸ばしたもの
                var tier = GenUtil.Prim(PrimitiveType.Cube, "Tier_" + label[i], holder.transform, new Vector3(x[i], baseTop + h[i] * 0.5f, 0f),
                    new Vector3(1.4f, h[i], 1.4f), new Color(0.55f, 0.38f, 0.22f), true);
                GenUtil.HideRenderers(tier);
                var hay = GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_0" + (i + 1), holder.transform, holder.transform.position, 0f, 1f,
                    new Vector3(1.4f, h[i], 1.4f), new Color(0.85f, 0.75f, 0.4f));
                hay.transform.SetParent(holder.transform, false);
                hay.transform.localPosition = new Vector3(x[i], baseTop, 0f);
                hay.transform.localRotation = Quaternion.identity;
                GenUtil.FitSize(hay, new Vector3(1.4f, h[i], 1.4f));
                // 段の番号（ui-r2-09）：干し草（黄色）に金の字は溶けるので、段の正面に手配書の紙（WantedPosters_05、0.6 × 0.45m）を留めて焦げ茶のインク
                var tierPatch = GenUtil.Child(holder.transform, "TierPatch_" + label[i], new Vector3(x[i], baseTop + h[i] * 0.5f, 0.71f));
                tierPatch.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                GenUtil.PaperSheet(tierPatch.transform, "Paper", "WantedPosters_05", new Vector3(0f, -0.225f, 0.005f), null, 0.6f, 0.45f);
                GenUtil.Text(tierPatch.transform, "TierLabel_" + label[i], new Vector3(0f, 0f, -0.01f), Quaternion.identity,
                    label[i], 3.2f, UiStyle.DarkBrown, new Vector2(0.5f, 0.4f), TextStyle.Ink, true).fontStyle = TMPro.FontStyles.Bold;
                steps[i] = GenUtil.Child(holder.transform, "Step_" + label[i], new Vector3(x[i], baseTop + h[i] + 0.05f, 0f)).transform;
                tier.name = "Tier_" + label[i];
                // 名札：段の上 2.6m に手配書の紙（WantedPosters_04、1.4 × 0.5m）、看板の文字。観客側（+Z）から読める
                var plate = GenUtil.Child(holder.transform, "NamePlate_" + label[i], new Vector3(x[i], baseTop + h[i] + 2.6f, -0.6f));
                plate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                GenUtil.PaperSheet(plate.transform, "Paper", "WantedPosters_04", new Vector3(0f, -0.25f, 0.01f), null, 1.4f, 0.5f);
                // 紙の上は濃いインク（ui-r2-09：UiStyle.Contrast ≥ 4.5、1 位は Podium が赤インク）
                plates[i] = GenUtil.Text(plate.transform, "Name", new Vector3(0f, 0f, -0.02f), Quaternion.identity, "", 1.2f, UiStyle.InkFace,
                    new Vector2(1.32f, 0.44f), TextStyle.Ink, true);
                plates[i].fontStyle = TMPro.FontStyles.Bold;
                plateRoots[i] = plate;
                // 名札を吊る杭（立てた丸太 WoodLogs_01、art-r3-02：骨の杭は使わない）：段の後ろから立てる（宙に浮かせない）
                GenUtil.WoodPost(plate.transform, holder.transform.TransformPoint(new Vector3(x[i], baseTop + h[i], -0.66f)), 0f, 2.96f, 0.22f).name = "PlatePost";
            }
            // 「総合結果」の掲示板（表彰台の後ろ、観客側から読める）。4〜8 位は見出しの下に小さく（ui-r3-06）
            var board = NoticeSignForPodium(holder.transform, pos, yawFacingAudience);
            var fw = new ParticleSystem[4];
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.71f, 0.31f, 0.18f), 0f), new GradientColorKey(new Color(0.88f, 0.69f, 0.31f), 0.5f),
                new GradientColorKey(new Color(0.54f, 0.42f, 0.66f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 lp = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 8f) + Vector3.up * 1f;
                fw[i] = GenUtil.Particles(holder.transform, "Firework_" + (i + 1), lp, new Color(1f, 0.6f + 0.1f * i, 0.3f), 0.25f, 12f, 1.6f, false, 0f, 0.2f);
                // 3 ステージの色（赤錆・砂金・紫）からランダム（ui-r1-13）
                var main = fw[i].main;
                var grad = new ParticleSystem.MinMaxGradient(g);
                grad.mode = ParticleSystemGradientMode.RandomColor;
                main.startColor = grad;
            }
            var pd = GenUtil.AddU<Podium>(holder);
            pd.namePlates = plates;
            pd.plateRoots = plateRoots;
            pd.steps = steps;
            pd.fireworks = fw;
            pd.fanfare = GenUtil.Audio(holder, true, false, SfxBuilder.Clip(SfxMath.STING));
            // 発表の順（ui-r3-06）：太鼓（時計の音を 0.12 秒ごと）・名札をめくる音・1 位の鐘・最後のファンファーレ（新しい音の素材は作らない）
            pd.drumClip = SfxBuilder.Clip(SfxMath.TICK);
            pd.revealClip = SfxBuilder.Clip(SfxMath.PEN);
            pd.bellClip = SfxBuilder.Clip(SfxMath.BELL);
            pd.stingClip = SfxBuilder.Clip(SfxMath.STING);
            pd.resultsBoard = board;
            pd.resultsHeading = RESULTS_HEADING;
            // 1 位の段の上の金の紙吹雪（ゴールの紙吹雪と同じ作り、Emit 300）
            pd.goldBurst = GenUtil.Particles(holder.transform, "GoldBurst", steps[0].localPosition + Vector3.up * 3.2f, new Color(1f, 0.82f, 0.35f), 0.15f, 6f, 3f, false, 0f, 1.5f);
            // 中継の表彰台のカット（ui-r3-01）：正面 9m・高さ 3.2m から 1 位の段を見る。SceneAssembler が SpectatorDirector に入れる
            var shot = GenUtil.Child(holder.transform, "PodiumShot", new Vector3(0f, 3.2f, 9f));
            Vector3 look = steps[0].position + Vector3.up * 1.2f - shot.transform.position;
            if (look.sqrMagnitude > 0.001f) shot.transform.rotation = Quaternion.LookRotation(look);
            return pd;
        }
    }
}
