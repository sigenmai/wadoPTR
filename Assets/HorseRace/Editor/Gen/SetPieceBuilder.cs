using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5、簡易版）。CourseBuilder の後・DressingBuilder の前に呼ぶ。
    ///   荒野：岩橋（コースの下に溝を掘って Terrain に書き戻し、橋の板＝Ground、手すり＝Block を渡す）と
    ///         崖下りの近道（コースの曲がりを弦で切る枝道。途中に 6m の段。途中の CP 2 つの箱を近道まで広げる。CP は増やさない）
    ///   砂漠：コース上の砂丘越えのジャンプ台（Ground、約 10.6°）
    /// 幾何はすべて SetPieces（純粋・単体テスト済み）。本コースの勾配（≤25°）は変わらない（橋の上は板）。
    /// </summary>
    public static class SetPieceBuilder
    {
        static readonly Color Rock = new Color(0.62f, 0.44f, 0.32f);
        static readonly Color SandRamp = new Color(0.86f, 0.72f, 0.48f);
        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);

        public static void Build(StageBuild sb, WorldContext ctx)
        {
            StageDef def = sb.def;
            sb.bridgeArc = SetPieces.BridgeArc(def, sb.goalArc);
            sb.jumpArc = SetPieces.JumpArc(def, sb.goalArc);
            if (sb.bridgeArc < 0f && !def.cliffShortcut && sb.jumpArc < 0f) return;

            var root = GenUtil.Child(sb.root, "SetPieces", Vector3.zero).transform;
            root.position = Vector3.zero;

            if (sb.bridgeArc >= 0f || def.cliffShortcut)
            {
                // 地形の加工（掘る前の高さで近道を計画 → 溝 → 近道）を Terrain に書き戻す
                sb.shortcut = SetPieces.ApplyTerrain(def, sb.courseLocal, sb.heights, sb.goalArc);
                TerrainData td = sb.terrain.terrainData;
                td.SetHeights(0, 0, sb.heights);
                TerrainBuilder.PaintLayers(td, sb.heights, sb.sand);
            }
            if (sb.bridgeArc >= 0f) Bridge(sb, root);
            if (def.cliffShortcut)
            {
                if (sb.shortcut != null) Shortcut(sb, root);
                else GenUtil.Warn("ステージ" + (def.index + 1) + "：崖下りの近道を置ける区間が見つかりませんでした");
            }
            if (sb.jumpArc >= 0f) Jump(sb, root);
        }

        static void Bridge(StageBuild sb, Transform root)
        {
            Vector3[] c = sb.courseWorld;   // 掘る前のコースの高さ
            Vector3 p1 = WorldLayout.PointAtArc(c, sb.bridgeArc - SetPieces.BRIDGE_HALF_SPAN);
            Vector3 p2 = WorldLayout.PointAtArc(c, sb.bridgeArc + SetPieces.BRIDGE_HALF_SPAN);
            Vector3 d = p2 - p1;
            var bridge = new GameObject("RockBridge");
            bridge.transform.SetParent(root, false);
            bridge.transform.position = (p1 + p2) * 0.5f;
            bridge.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            float len = d.magnitude;
            // 板の上面 = コースの高さ（端で段差なし）
            var deck = GenUtil.Prim(PrimitiveType.Cube, "Deck", bridge.transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(SetPieces.BRIDGE_WIDTH, 1f, len), Rock, true);
            deck.layer = WorldLayers.Ground;
            var arch = GenUtil.Prim(PrimitiveType.Cube, "Arch", bridge.transform, new Vector3(0f, -3f, 0f),
                new Vector3(SetPieces.BRIDGE_WIDTH - 1f, 4f, len - 6f), Rock);
            for (int s = -1; s <= 1; s += 2)
            {
                var rail = GenUtil.Prim(PrimitiveType.Cube, s < 0 ? "RailL" : "RailR", bridge.transform,
                    new Vector3(s * (SetPieces.BRIDGE_WIDTH * 0.5f + 0.3f), 0.7f, 0f), new Vector3(0.6f, 1.4f, len), Rock, true);
                rail.layer = WorldLayers.Block;
            }
            sb.Exclude(bridge.transform.position, SetPieces.TRENCH_HALF_LENGTH + 10f);
        }

        static void Shortcut(StageBuild sb, Transform root)
        {
            SetPieces.ShortcutPlan p = sb.shortcut;
            // 途中の CP の箱を近道まで広げる（どちらの道でも順番どおりに CP を通る）
            for (int k = 0; k < p.crossCps.Length; k++)
            {
                int idx = p.crossCps[k];
                if (idx < 0 || idx >= sb.checkpoints.Count) continue;
                var box = sb.checkpoints[idx].GetComponent<BoxCollider>();
                if (box == null) continue;
                float o = p.crossOffsets[k];
                float lo = Mathf.Min(-CourseBuilder.CP_WIDTH * 0.5f, o - 8f), hi = Mathf.Max(CourseBuilder.CP_WIDTH * 0.5f, o + 8f);
                box.size = new Vector3(hi - lo, 24f, CourseBuilder.CP_DEPTH);
                box.center = new Vector3((lo + hi) * 0.5f, 9f, 0f);
            }
            // 入口の札と、段の縁の目印
            Vector3 o0 = sb.def.Origin;
            Vector3 dir = new Vector3(p.b.x - p.a.x, 0f, p.b.z - p.a.z).normalized;
            Vector3 entry = Vector3.Lerp(p.a, p.b, p.f0);
            Vector3 entryW = new Vector3(o0.x + entry.x, o0.y + SetPieces.ShortcutHeight(p, p.f0), o0.z + entry.z);
            var sign = GenUtil.World(root, "ShortcutSign", entryW + WorldLayout.RightOf(dir) * (SetPieces.SHORTCUT_HALF_WIDTH + 1.5f), WorldLayout.YawDeg(dir));
            GenUtil.Prim(PrimitiveType.Cube, "Post", sign.transform, new Vector3(0f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), new Color(0.3f, 0.2f, 0.12f));
            GenUtil.Prim(PrimitiveType.Cube, "Board", sign.transform, new Vector3(0f, 1.9f, 0f), new Vector3(1.6f, 0.5f, 0.06f), new Color(0.45f, 0.3f, 0.18f));
            GenUtil.Text(sign.transform, "Text", new Vector3(0f, 1.9f, -0.04f), Quaternion.identity, "近道（崖下り）", 1.6f, Paper, new Vector2(1.6f, 0.5f));
            Vector3 lip = Vector3.Lerp(p.a, p.b, p.fDrop);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 w = new Vector3(o0.x + lip.x, o0.y + SetPieces.ShortcutHeight(p, p.fDrop - 0.001f), o0.z + lip.z)
                    + WorldLayout.RightOf(dir) * (s * (SetPieces.SHORTCUT_HALF_WIDTH + 0.5f));
                GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_15", root, w, s * 40f, 1f, new Vector3(1.3f, 1.8f, 1.4f), Rock);
            }
            for (int k = 0; k <= 10; k++)
            {
                Vector3 q = Vector3.Lerp(p.a, p.b, k / 10f);
                sb.exclusions.Add(new Vector4(q.x, 0f, q.z, SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND + 6f));
            }
        }

        static void Jump(StageBuild sb, Transform root)
        {
            Vector3[] c = sb.courseWorld;
            Vector3 p0 = WorldLayout.PointAtArc(c, sb.jumpArc - SetPieces.JUMP_LENGTH * 0.5f);
            Vector3 p1 = WorldLayout.PointAtArc(c, sb.jumpArc + SetPieces.JUMP_LENGTH * 0.5f) + Vector3.up * SetPieces.JUMP_HEIGHT;
            Vector3 d = p1 - p0;
            var ramp = new GameObject("DuneJump");
            ramp.transform.SetParent(root, false);
            Quaternion rot = Quaternion.LookRotation(d.normalized, Vector3.up);
            ramp.transform.rotation = rot;
            const float T = 0.5f;
            ramp.transform.position = (p0 + p1) * 0.5f - rot * Vector3.up * (T * 0.5f);
            var deck = GenUtil.Prim(PrimitiveType.Cube, "Ramp", ramp.transform, Vector3.zero, new Vector3(SetPieces.JUMP_WIDTH, T, d.magnitude), SandRamp, true);
            deck.layer = WorldLayers.Ground;
            // 裏側（見た目だけ）
            var back = GenUtil.Prim(PrimitiveType.Cube, "Back", root, Vector3.zero, new Vector3(SetPieces.JUMP_WIDTH, SetPieces.JUMP_HEIGHT, 0.3f), SandRamp);
            back.transform.position = new Vector3(p1.x, p1.y - SetPieces.JUMP_HEIGHT * 0.5f, p1.z);
            back.transform.rotation = Quaternion.Euler(0f, WorldLayout.YawDeg(new Vector3(d.x, 0f, d.z).normalized), 0f);
            back.name = "DuneJumpBack";
            sb.Exclude(ramp.transform.position, 20f);
        }
    }
}
