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
            GenUtil.Prim(PrimitiveType.Cube, "Arch", bridge.transform, new Vector3(0f, -3f, 0f),
                new Vector3(SetPieces.BRIDGE_WIDTH - 1f, 4f, len - 6f), Rock);
            float half = SetPieces.BRIDGE_WIDTH * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                var rail = GenUtil.Prim(PrimitiveType.Cube, s < 0 ? "RailL" : "RailR", bridge.transform,
                    new Vector3(s * (half + 0.3f), 0.7f, 0f), new Vector3(0.6f, 1.4f, len), Rock, true);
                rail.layer = WorldLayers.Block;
            }

            // 溝の縁の壁（道の外 → 橋へ追い込む）：溝の手前と向こうの縁に、手すりの端から溝の端まで 4m ごとの岩の壁
            Vector3 t = new Vector3(d.x, 0f, d.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            Vector3 center = WorldLayout.PointAtArc(c, sb.bridgeArc);
            float lipAlong = SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND + 0.6f;
            var wings = GenUtil.Child(bridge.transform, "TrenchWalls", Vector3.zero).transform;
            const float SEG = 4f;
            for (int e = -1; e <= 1; e += 2)
                for (int s = -1; s <= 1; s += 2)
                    for (float lat = half; lat < SetPieces.TRENCH_HALF_LENGTH + 2f; lat += SEG)
                    {
                        Vector3 w = sb.GroundWorld(center + t * (e * lipAlong) + r * (s * (lat + SEG * 0.5f)));
                        var wall = GenUtil.Prim(PrimitiveType.Cube, "Wall", wings, Vector3.zero, new Vector3(SEG + 0.4f, 2.2f, 0.9f), Rock, true);
                        wall.transform.position = w + Vector3.up * 0.8f;
                        wall.transform.rotation = Quaternion.LookRotation(t, Vector3.up);
                        wall.layer = WorldLayers.Block;
                    }

            // 溝の底の落とし穴：入った騎手を最後に通った CP へ戻す（地形の上なのでコース外判定が働かないことがあるため）
            var pit = GenUtil.Child(bridge.transform, "FallPit", new Vector3(0f, -SetPieces.TRENCH_DEPTH + 3f, 0f));
            var pitBox = pit.AddComponent<BoxCollider>();
            pitBox.isTrigger = true;
            pitBox.size = new Vector3(SetPieces.TRENCH_HALF_LENGTH * 2f + 10f, 8f, (SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND) * 2f);
            pit.layer = WorldLayers.TRIGGERS;
            GenUtil.AddU<ResetVolume>(pit);
            // 溝の両端と底に岩（掘った地形の切り口を見せない）
            var cover = GenUtil.Child(bridge.transform, "TrenchRocks", Vector3.zero).transform;
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                float endLat = SetPieces.TRENCH_HALF_LENGTH - SetPieces.TRENCH_END_TAPER * 0.5f;
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 p = sb.GroundWorld(center + r * (s2 * endLat) + t * (e * 4f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), e < 0 ? "DesertRocks_05" : "DesertRocks_04", cover,
                        p - Vector3.up * 2f, s2 * 30f + e * 70f, 1.6f, new Vector3(10f, 5f, 7f), Rock);
                    DressingBuilder.AddCollider(rock, false);
                    DressingBuilder.SetStatic(rock);
                }
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = sb.GroundWorld(center + r * (s2 * (16f + i * 6f)) + t * ((i % 2 == 0 ? 1f : -1f) * 3f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_0" + (6 + i % 4), cover,
                        p - Vector3.up * 0.8f, i * 53f, 1.1f, new Vector3(5f, 3f, 5f), Rock);
                    DressingBuilder.SetStatic(rock);
                }
            }
            DressingBuilder.SetStatic(bridge);
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
            // 段（崖下り）の通路の両脇の斜面と崖の面に岩（盛った地形の切り口を見せない）
            Vector3 rgt = WorldLayout.RightOf(dir);
            float L = p.length;
            for (float f = p.f0; f <= p.fDrop + 1e-4f; f += 7f / L)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 q = Vector3.Lerp(p.a, p.b, f);
                    float hgt = SetPieces.ShortcutHeight(p, Mathf.Min(f, p.fDrop - 0.001f));
                    Vector3 w = new Vector3(o0.x + q.x, o0.y + hgt, o0.z + q.z) + rgt * (s * (SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND * 0.6f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_1" + (1 + ((int)(f * 97f) % 4)), root,
                        w - Vector3.up * 1.4f, f * 400f + s * 90f, 1.5f, new Vector3(3f, 2f, 3f), Rock);
                    DressingBuilder.SetStatic(rock);
                }
            {
                Vector3 q = Vector3.Lerp(p.a, p.b, p.fDrop);
                float low = SetPieces.ShortcutHeight(p, p.fDrop + 0.001f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 w = new Vector3(o0.x + q.x, o0.y + low, o0.z + q.z) + rgt * (s * (SetPieces.SHORTCUT_HALF_WIDTH + 2.5f));
                    var face = GenUtil.InstantiatePart(GenUtil.Pack("Cliffs"), "Cliffs_13", root, w - Vector3.up * 1f, WorldLayout.YawDeg(dir) + s * 90f, 0.35f,
                        new Vector3(6f, 6f, 6f), Rock);
                    DressingBuilder.SetStatic(face);
                }
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
