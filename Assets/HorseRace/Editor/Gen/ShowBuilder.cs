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
            ctx.churchBell = GenUtil.Audio(bell, true);
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

        /// <summary>たまり場の掲示板 3 枚（center の並び、北＝焚き火のほうを向く）と中継・木札・新聞売り。</summary>
        public static void BuildBoards(Transform parent, WorldContext ctx, Vector3 center)
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
            dir.followDistance = 9f;
            dir.followHeight = 4.5f;
            dir.lookHeight = 1.2f;
            ctx.director = dir;

            string[] titles = { "中継", "電報", "出走表" };
            NoticeBoardDisplay telegram = null;
            for (int mode = 0; mode < 3; mode++)
            {
                Vector3 p = center + new Vector3((mode - 1) * 6f, 0f, 0f);
                var nb = Board(root, "NoticeBoard_" + titles[mode], p, Vector3.forward, mode, ctx, out Transform front, out Vector3 panelCenter);
                if (mode == 1) telegram = nb;
                if (mode == 0)
                {
                    dir.captionText = GenUtil.Text(front, "Caption", new Vector3(0f, 1.05f, 0f), Quaternion.identity, "", 1.2f, Ink, new Vector2(3f, 0.3f));
                    dir.tags = Tags(front, dir, ctx);
                }
            }

            // 新聞売り（Capsule の人物）
            var crierGo = GenUtil.World(root, "TownCrier", center + new Vector3(12f, 0f, 4f), -90f);
            GenUtil.Prim(PrimitiveType.Capsule, "Body", crierGo.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.45f, 0.8f, 0.45f), new Color(0.3f, 0.3f, 0.45f));
            GenUtil.Prim(PrimitiveType.Sphere, "Head", crierGo.transform, new Vector3(0f, 1.75f, 0f), Vector3.one * 0.3f, new Color(0.9f, 0.75f, 0.6f));
            GenUtil.Prim(PrimitiveType.Cylinder, "Cap", crierGo.transform, new Vector3(0f, 1.92f, 0f), new Vector3(0.32f, 0.05f, 0.32f), DarkWood);
            var arm = GenUtil.Child(crierGo.transform, "PaperArm", new Vector3(0.28f, 1.35f, 0f)).transform;
            GenUtil.Prim(PrimitiveType.Cube, "Arm", arm, new Vector3(0f, -0.25f, 0f), new Vector3(0.1f, 0.5f, 0.1f), new Color(0.3f, 0.3f, 0.45f));
            GenUtil.Prim(PrimitiveType.Cube, "Paper", arm, new Vector3(0f, -0.5f, 0.12f), new Vector3(0.3f, 0.4f, 0.01f), Paper);
            var crier = GenUtil.AddU<TownCrier>(crierGo);
            crier.callAudio = GenUtil.Audio(crierGo, true);
            crier.boards = telegram != null ? new[] { telegram } : new NoticeBoardDisplay[0];
            crier.paperArm = arm;
            ctx.crier = crier;
        }

        /// <summary>掲示板 1 枚。パネルの法線（メッシュから読む）を facing に向け、文字はパネルの少し手前。</summary>
        static NoticeBoardDisplay Board(Transform parent, string name, Vector3 pos, Vector3 facing, int mode, WorldContext ctx,
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
                var q = GenUtil.Prim(PrimitiveType.Quad, "Panel", holder.transform, new Vector3(0f, 2.15f, -0.05f), new Vector3(3.2f, 1.8f, 1f), Paper);
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
            }

            var text = GenUtil.Text(front, "BoardText", new Vector3(0f, panelCenter.y - pos.y, 0f), Quaternion.identity, "",
                1.6f, Ink, new Vector2(3.0f, 1.7f));
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

        static DirectorTag[] Tags(Transform front, SpectatorDirector dir, WorldContext ctx)
        {
            var tags = new DirectorTag[9];
            for (int i = 0; i < 9; i++)
            {
                int rank = i < 8 ? i + 1 : 0;
                string label = rank == 0 ? "おまかせ" : rank + "位を追う";
                var go = GenUtil.Child(front, "Tag_" + (rank == 0 ? "Auto" : rank.ToString()), new Vector3(-2.0f + i * 0.5f, 0.75f, -0.05f));
                GenUtil.Prim(PrimitiveType.Cube, "Board", go.transform, Vector3.zero, new Vector3(0.45f, 0.25f, 0.04f), Wood);
                GenUtil.Prim(PrimitiveType.Cylinder, "String", go.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.01f, 0.08f, 0.01f), DarkWood);
                GenUtil.Text(go.transform, "Label", new Vector3(0f, 0f, -0.025f), Quaternion.identity, label, 0.7f, Paper, new Vector2(0.43f, 0.22f));
                var mark = GenUtil.Prim(PrimitiveType.Sphere, "SelectedMark", go.transform, new Vector3(0f, -0.18f, -0.02f), Vector3.one * 0.06f, new Color(0.8f, 0.15f, 0.1f));
                mark.SetActive(false);
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(0.45f, 0.25f, 0.08f);
                var tag = GenUtil.AddU<DirectorTag>(go);
                tag.director = dir;
                tag.followRank = rank;
                tag.selectedMark = mark;
                GenUtil.SetInteractText(tag, label);
                tags[i] = tag;
                ctx.directorTags.Add(tag);
            }
            return tags;
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
            for (int i = 0; i < 3; i++)
            {
                var tier = GenUtil.Prim(PrimitiveType.Cube, "Tier_" + label[i], holder.transform, new Vector3(x[i], baseTop + h[i] * 0.5f, 0f),
                    new Vector3(1.4f, h[i], 1.4f), new Color(0.55f, 0.38f, 0.22f), true);
                GenUtil.Text(holder.transform, "TierLabel_" + label[i], new Vector3(x[i], baseTop + h[i] * 0.5f, 0.71f), Quaternion.Euler(0f, 180f, 0f),
                    label[i], 4f, new Color(1f, 0.85f, 0.4f), new Vector2(1.2f, 0.6f));
                steps[i] = GenUtil.Child(holder.transform, "Step_" + label[i], new Vector3(x[i], baseTop + h[i] + 0.05f, 0f)).transform;
                tier.name = "Tier_" + label[i];
            }
            var fw = new ParticleSystem[4];
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 lp = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 8f) + Vector3.up * 1f;
                fw[i] = GenUtil.Particles(holder.transform, "Firework_" + (i + 1), lp, new Color(1f, 0.6f + 0.1f * i, 0.3f), 0.25f, 12f, 1.6f, false, 0f, 0.2f);
            }
            var pd = GenUtil.AddU<Podium>(holder);
            pd.steps = steps;
            pd.fireworks = fw;
            pd.fanfare = GenUtil.Audio(holder, true);
            return pd;
        }
    }
}
