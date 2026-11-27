using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5、簡易版）。CourseBuilder の後・DressingBuilder の前に呼ぶ。
    ///   荒野：岩橋（コースの下に溝を掘って Terrain に書き戻し、橋の板＝Ground、手すり＝Block を渡す）と
    ///         崖下りの近道（コースの曲がりを弦で切る枝道。途中に 6m の段。途中の CP 2 つの箱を近道まで広げる。CP は増やさない）
    ///   砂漠：コース上の砂丘越えのジャンプ台（地形の盛り上がり＋見えない板の Collider、約 10.6°）。踏み切りの杭の列と干し草、縁の下の青緑の灯り
    ///         （加速＝青緑の色の決まり）、着地の砂煙、着地の先に骨の馬
    ///   境目：メサの切り通し（地形の加工だけ。両脇のメサの門は DressingPlan.MesaGate）
    /// 見た目はすべて Models の素材（岩・杭・街灯・焚き火・干し草・骨）。プリミティブの板は見えない Collider としてだけ使う。
    /// 幾何はすべて SetPieces（純粋・単体テスト済み）。本コースの勾配（≤25°）は変わらない（橋の上は板）。
    /// </summary>
    public static class SetPieceBuilder
    {
        static int decorIndex;   // 見どころの飾りの岩の id 用（Build ごとに 0 から）
        static readonly Color Rock = new Color(0.62f, 0.44f, 0.32f);
        static readonly Color SandRamp = new Color(0.86f, 0.72f, 0.48f);
        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);
        static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);
        public static readonly Color LampWarm = new Color(1f, 0.75f, 0.45f);
        public static readonly Color DangerRed = new Color(1f, 0.25f, 0.15f);
        public static readonly Color BoostTeal = new Color(0.2f, 0.9f, 0.85f);
        public static readonly Color SmokeGrey = new Color(0.32f, 0.30f, 0.29f, 0.55f);
        public static readonly Color SandDust = new Color(0.90f, 0.78f, 0.58f, 0.35f);

        public static void Build(StageBuild sb, WorldContext ctx)
        {
            StageDef def = sb.def;
            decorIndex = 0;
            sb.bridgeArc = SetPieces.BridgeArc(def, sb.goalArc);
            sb.jumpArc = SetPieces.JumpArc(def, sb.goalArc);
            var root = GenUtil.Child(sb.root, "SetPieces", Vector3.zero).transform;
            root.position = Vector3.zero;

            // 地形の加工（掘る前の高さで近道を計画 → 溝 → 近道 → ジャンプの盛り上がり → 切り通し → 廃線の路盤）を Terrain に書き戻す
            sb.shortcut = SetPieces.ApplyTerrain(def, sb.courseLocal, sb.heights, sb.goalArc);
            TerrainData td = sb.terrain.terrainData;
            td.SetHeights(0, 0, sb.heights);
            TerrainBuilder.PaintLayers(td, sb.heights, sb.sand, def.index);
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
            // 板の上面 = コースの高さ（端で段差なし）。板と手すりは見えない Collider（見た目は下の岩）
            var deck = GenUtil.Prim(PrimitiveType.Cube, "Deck", bridge.transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(SetPieces.BRIDGE_WIDTH, 1f, len), Rock, true);
            deck.layer = WorldLayers.Ground;
            GenUtil.HideRenderers(deck);
            float half = SetPieces.BRIDGE_WIDTH * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                var rail = GenUtil.Prim(PrimitiveType.Cube, s < 0 ? "RailL" : "RailR", bridge.transform,
                    new Vector3(s * (half + 0.3f), 0.7f, 0f), new Vector3(0.6f, 1.4f, len), Rock, true);
                rail.layer = WorldLayers.Block;
                GenUtil.HideRenderers(rail);
            }
            BridgeRocks(sb, bridge.transform, len, half);

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
                        GenUtil.HideRenderers(wall);
                        // 見た目は岩（DesertRocks_04 と _08 を交互、長い辺 = ローカル X を壁に沿わせる）。当たり判定は上の見えない箱
                        bool alt = Mathf.RoundToInt(lat / SEG) % 2 == 0;
                        var wr = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), alt ? "DesertRocks_04" : "DesertRocks_08", wings,
                            w - Vector3.up * 0.25f, WorldLayout.YawDeg(t) + (s > 0 ? 0f : 180f), alt ? 0.6f : 0.85f, new Vector3(4f, 1.5f, 1f), Rock);
                        DressingBuilder.SetStatic(wr);
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
                    GenUtil.Tag(rock, "S" + (sb.def.index + 1) + "/SetPieceDecor/rock#" + (decorIndex++));
                }
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = sb.GroundWorld(center + r * (s2 * (16f + i * 6f)) + t * ((i % 2 == 0 ? 1f : -1f) * 3f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_0" + (6 + i % 4), cover,
                        p - Vector3.up * 0.8f, i * 53f, 1.1f, new Vector3(5f, 3f, 5f), Rock);
                    DressingBuilder.SetStatic(rock);
                    GenUtil.Tag(rock, "S" + (sb.def.index + 1) + "/SetPieceDecor/rock#" + (decorIndex++));
                }
            }
            DressingBuilder.SetStatic(bridge);
            sb.Exclude(bridge.transform.position, SetPieces.TRENCH_HALF_LENGTH + 10f);
            BridgeSignals(sb, bridge.transform, center, t, r, len, half);
        }

        /// <summary>
        /// 岩橋の見た目（art-r1-07）：両端の溝の中に大岩（DesertRocks_01・_02 ×1.3〜1.45、長い辺を橋の向きに、40% 埋めて上面は板の下）＝自然の石の橋の支え、
        /// 板の面は平たい岩（DesertRocks_08）を 4 列 × 5 枚、手すりは平たい岩を縁に 7m ごと。
        /// </summary>
        static void BridgeRocks(StageBuild sb, Transform bridge, float len, float half)
        {
            float yawD = bridge.eulerAngles.y;
            Vector3 fwd = bridge.forward, right = bridge.right;
            float deckY = bridge.position.y;
            var rocks = GenUtil.Child(bridge, "BridgeRocks", Vector3.zero).transform;
            // 支え（両端の溝の中）
            for (int e = -1; e <= 1; e += 2)
                for (int k = 0; k < 2; k++)
                {
                    string part = k == 0 ? "DesertRocks_01" : "DesertRocks_02";
                    float sc = k == 0 ? 1.3f : 1.45f;
                    float H = (k == 0 ? 12.8f : 12.4f) * sc;
                    Vector3 p = bridge.position + fwd * (e * (SetPieces.TRENCH_HALF_WIDTH + 1f)) + right * ((k == 0 ? -1f : 1f) * 6f);
                    float ground = sb.GroundWorld(p).y;
                    float y = Mathf.Min(ground - 0.4f * H, deckY - 0.6f - H);
                    var a = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), part, rocks, new Vector3(p.x, y, p.z), yawD - 90f + (e > 0 ? 180f : 0f), sc,
                        new Vector3(20f, 10f, 15f), Rock);
                    DressingBuilder.SetStatic(a);
                    GenUtil.Tag(a, "S" + (sb.def.index + 1) + "/SetPieceDecor/arch#" + (decorIndex++));
                }
            // 板の面（平たい岩、上面が板の上面に届くように）
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 4; j++)
                {
                    float along = -len * 0.5f + 2.3f + i * (len - 4.6f) / 4f;
                    float lat = -half + 3.25f + j * (SetPieces.BRIDGE_WIDTH - 6.5f) / 3f;
                    Vector3 p = bridge.position + fwd * along + right * lat;
                    var slab = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_08", rocks, new Vector3(p.x, deckY - 1.5f, p.z),
                        yawD - 90f + ((i + j) % 2 == 0 ? 0f : 180f), 1f, new Vector3(7f, 1.5f, 5f), Rock);
                    DressingBuilder.SetStatic(slab);
                }
            // 手すり（縁に 7m ごと、長い辺を橋の向きに）
            for (int s = -1; s <= 1; s += 2)
                for (float along = -len * 0.5f + 3.5f; along <= len * 0.5f - 2f; along += 7f)
                {
                    Vector3 p = bridge.position + fwd * along + right * (s * (half + 0.3f));
                    var rail = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_08", rocks, new Vector3(p.x, deckY - 0.35f, p.z),
                        yawD - 90f + (s > 0 ? 180f : 0f), 0.9f, new Vector3(6.5f, 1.4f, 1f), Rock);
                    DressingBuilder.SetStatic(rail);
                }
        }

        /// <summary>岩橋の合図：両端に街灯 2 本と暖色の灯り、焚き火の煙（遠くから入口がわかる）。溝の底は暗い赤の灯り（危険＝赤の色の決まり）。</summary>
        static void BridgeSignals(StageBuild sb, Transform bridge, Vector3 center, Vector3 t, Vector3 r, float len, float half)
        {
            var sig = GenUtil.Child(bridge, "Signals", Vector3.zero).transform;
            for (int e = -1; e <= 1; e += 2)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 p = sb.GroundWorld(center + t * (e * (len * 0.5f + 3f)) + r * (s * (half + 1.5f)));
                    var lamp = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", sig, p, WorldLayout.YawDeg(t) + (s > 0 ? -90f : 90f), 1f,
                        new Vector3(0.3f, 3f, 0.3f), Rock);
                    DressingBuilder.SetStatic(lamp);
                    GenUtil.PointLight(sig, "LampLight", p + Vector3.up * 2.9f, LampWarm, 14f, 1.8f);
                }
                Vector3 fp = sb.GroundWorld(center + t * (e * (len * 0.5f + 6f)) + r * (-(half + 5f)));
                var fire = GenUtil.InstantiatePart(GenUtil.Pack("Campfire"), "Campfire_01", sig, fp, 0f, 1f, new Vector3(1.5f, 0.6f, 1.5f), Rock);
                DressingBuilder.SetStatic(fire);
                GenUtil.Particles(fire.transform, "Flames", new Vector3(0f, 0.4f, 0f), new Color(1f, 0.55f, 0.15f, 0.9f), 0.35f, 1.2f, 0.8f, true, 20f, 0.3f);
                GenUtil.SmokeColumn(fire.transform, "Smoke", new Vector3(0f, 1f, 0f), SmokeGrey, 18f, 1.6f);
            }
            Vector3 bottom = center + Vector3.up * (-SetPieces.TRENCH_DEPTH + 2f);
            GenUtil.PointLight(sig, "TrenchDanger", bottom, DangerRed, 30f, 1.2f);
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
            GenUtil.Stake(sign.transform, "DesertProps_05", sign.transform.position, WorldLayout.YawDeg(dir), 0.75f);
            GenUtil.PaperSheet(sign.transform, "Board", "WantedPosters_01", new Vector3(0f, 1.6f, -0.12f), null, 1.6f, 0.6f);
            GenUtil.Text(sign.transform, "Text", new Vector3(0f, 1.9f, -0.15f), Quaternion.identity, "近道（崖下り）", 1.6f, Ink, new Vector2(1.6f, 0.5f));
            GenUtil.PointLight(sign.transform, "SignLight", sign.transform.position + Vector3.up * 2.6f, LampWarm, 8f, 1.2f);
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
                    GenUtil.Tag(rock, "S" + (sb.def.index + 1) + "/SetPieceDecor/rock#" + (decorIndex++));
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
                    GenUtil.Tag(face, "S" + (sb.def.index + 1) + "/SetPieceDecor/face#" + (decorIndex++));
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
            // 板は見えない Collider（正確に跳ばせる）。見た目は地形の砂丘の盛り上がり（SetPieces.RaiseKicker）
            var deck = GenUtil.Prim(PrimitiveType.Cube, "Ramp", ramp.transform, Vector3.zero, new Vector3(SetPieces.JUMP_WIDTH, T, d.magnitude), SandRamp, true);
            deck.layer = WorldLayers.Ground;
            GenUtil.HideRenderers(deck);
            sb.Exclude(ramp.transform.position, 20f);
            JumpDressing(sb, root, p0, p1);
        }

        /// <summary>
        /// ジャンプの合図（art-r1-08）：踏み切りの両脇（±(幅/2+1)）に杭の列 2 つと干し草の山、縁の下に青緑の灯り（加速の色）、
        /// 着地（縁の 15〜30m 先）に砂煙と、走る線の外に骨の馬と骨（ここに落ちる、の目印）、砂丘の稜線から砂が吹く。すべて当たり判定なし（見た目だけ）。
        /// </summary>
        static void JumpDressing(StageBuild sb, Transform root, Vector3 p0, Vector3 p1)
        {
            var g = GenUtil.Child(root, "JumpDressing", Vector3.zero).transform;
            g.position = Vector3.zero;
            Vector3 t = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float yaw = WorldLayout.YawDeg(t);
            Vector3 lip = new Vector3(p1.x, p1.y, p1.z);
            float side = SetPieces.JUMP_WIDTH * 0.5f + 1f;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int k = 0; k < 2; k++)
                {
                    Vector3 sp = sb.GroundWorld(p0 + t * (k * 4f - 2f) + r * (s * side));
                    DressingBuilder.SetStatic(GenUtil.Stake(g, "DesertProps_10", sp, yaw + 90f, 1f));
                }
                Vector3 hp = sb.GroundWorld(lip + r * (s * (side + 0.6f)));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hp, yaw, 1f, new Vector3(1.5f, 1.6f, 1.5f), SandRamp));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hp + Vector3.up * 1.55f + t * 0.2f, yaw + 40f, 0.9f,
                    new Vector3(1.5f, 1.6f, 1.5f), SandRamp));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_0" + (2 + (s > 0 ? 1 : 0)), g, hp - t * 1.6f, yaw + 90f, 1f,
                    new Vector3(0.9f, 0.6f, 0.7f), SandRamp));
            }
            GenUtil.PointLight(g, "BoostLight", lip - t * 1.2f + Vector3.up * 0.4f, BoostTeal, 8f, 2.2f);
            // 着地の砂煙と、着地の先の骨（走る線の外）
            Vector3 land = sb.GroundWorld(lip + t * 18f);
            GenUtil.DustBox(g, "LandingSand", land - g.position + Vector3.up * 0.5f, new Vector3(14f, 0.5f, 10f), SandDust, new Vector3(0.8f, 0.3f, 0.4f), 6f);
            var skel = GenUtil.PlaceModel(GenUtil.SKELETON_FBX, g, "SkeletonHorse", sb.GroundWorld(lip + t * 22f + r * 11f) - Vector3.up * 0.15f, yaw + 30f, 1f,
                new Vector3(2f, 1.2f, 2.6f), SandRamp);
            foreach (var a in skel.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            skel.transform.rotation = Quaternion.Euler(0f, yaw + 30f, 85f);
            DressingBuilder.SetStatic(skel);
            string[] bones = { "DesertProps_07", "DesertProps_08", "DesertProps_08" };
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 bp = sb.GroundWorld(lip + t * (16f + i * 6f) + r * (-(10.5f + i)));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("DesertProps"), bones[i], g, bp - Vector3.up * 0.05f, yaw + i * 70f, 1.1f,
                    new Vector3(1.5f, 0.6f, 1.5f), Paper));
            }
            // 砂丘の稜線から吹く砂（踏み切りの両脇の遠く）
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 cp = sb.GroundWorld(lip + r * (s * 30f) - t * 10f);
                GenUtil.DustBox(g, "CrestSand_" + (s < 0 ? "L" : "R"), cp - g.position + Vector3.up * 1.5f, new Vector3(20f, 1f, 6f), SandDust,
                    new Vector3(r.x * 2.5f * -s, 0.4f, r.z * 2.5f * -s), 5f);
            }
        }
    }
}
