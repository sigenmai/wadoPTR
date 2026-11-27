using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5、course-r1）。CourseBuilder の後・DressingBuilder の前に呼ぶ。地形の加工は SetPieces（純粋・単体テスト済み）。
    ///   荒野：岩の橋（溝を掘って中央 14m の地形の背を残す＝作った板は無い。溝の縁は Collider 付きの岩で守り、溝の両端は平らな遠回り）
    ///   近道（3 ステージとも）：弦の上に双子の CP、段の下の着地 12m だけ砂（SandZone）、入口に街灯 2 本（橙の灯り）と丸太の柱の手配書の札
    ///   砂漠：地形の踏み切り（+5m・13°、中央 ±7m だけ 6m の縁、その先は砂の窪み＝SandZone）。縁の両脇に干し草、手前に杭の列
    ///   境目：台地（地形の加工だけ。縁の崖・鞍部は DressingPlan.MesaPlateau）、切り通し（DressingPlan.MesaGate）
    ///   遺物 4 つ（SetPieces.RelicSpots：危ない線・遠回りの線）
    /// 見た目はすべて Models の素材（岩・崖・杭・街灯・丸太・焚き火・干し草・骨・手配書）。見えないのは Collider・トリガーだけ。
    /// </summary>
    public static class SetPieceBuilder
    {
        static int decorIndex;   // 見どころの飾りの岩の id 用（Build ごとに 0 から）
        static readonly Color Rock = new Color(0.62f, 0.44f, 0.32f);
        static readonly Color SandRamp = new Color(0.86f, 0.72f, 0.48f);
        static readonly Color Paper = new Color(0.93f, 0.86f, 0.70f);
        static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);
        static readonly Color Wood = new Color(0.45f, 0.30f, 0.18f);
        public static readonly Color LampWarm = new Color(1f, 0.75f, 0.45f);
        public static readonly Color SignalOrange = new Color(1f, 0.55f, 0.15f);
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

            // 地形の加工（台地 → 近道の計画 → 溝 → 近道 → 踏み切り → 切り通し → 廃線の路盤）を Terrain に書き戻す
            sb.shortcut = SetPieces.ApplyTerrain(def, sb.courseLocal, sb.heights, sb.goalArc);
            TerrainData td = sb.terrain.terrainData;
            td.SetHeights(0, 0, sb.heights);
            TerrainBuilder.PaintLayers(td, sb.heights, sb.sand, def.index);
            if (sb.bridgeArc >= 0f) Bridge(sb, root);
            if (def.hasShortcut)
            {
                if (sb.shortcut != null) Shortcut(sb, root);
                else GenUtil.Warn("ステージ" + (def.index + 1) + "：近道を置ける区間が見つかりませんでした");
            }
            if (sb.jumpArc >= 0f) Jump(sb, root);
            DeepSand(sb, root);
            RailBed(sb, root);
            Fork(sb, root);
            Relics(sb, ctx);
        }

        /// <summary>
        /// 深い砂の区間の目印（system-r2-01。道の上も砂で、馬は遅くラクダが速い）。入口の両脇に風で削れた岩（DesertRocks_04/05/06、道の外・当たり判定なし）、
        /// 入口を横切る砂の吹きだまり（パーティクル）、右の縁に立て札「深い砂」。出口の札は「固い地面」。モデルは作らない（既存の部品だけ）。
        /// </summary>
        static void DeepSand(StageBuild sb, Transform root)
        {
            float[] arcs = sb.def.sandTrailArcs;
            if (arcs == null) return;
            var g = GenUtil.Child(root, "DeepSand", Vector3.zero).transform;
            g.position = Vector3.zero;
            string[] rocks = { "DesertRocks_04", "DesertRocks_05", "DesertRocks_06" };
            for (int k = 0; 2 * k + 1 < arcs.Length; k++)
            {
                for (int end = 0; end < 2; end++)
                {
                    float a = arcs[2 * k + end];
                    Vector3 c = WorldLayout.PointAtArc(sb.courseWorld, a);
                    Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, a);
                    t = new Vector3(t.x, 0f, t.z).normalized;
                    Vector3 r = WorldLayout.RightOf(t);
                    float yaw = WorldLayout.YawDeg(t);
                    float hw = StageDefinitions.HalfWidthAt(sb.def, a);
                    string label = end == 0 ? "<b>深い砂</b>\n馬は重い・ラクダは速い" : "<b>固い地面</b>\nここから馬が速い";
                    SignPost(g, "DeepSandSign_" + k + "_" + end, sb.GroundWorld(c + r * (hw + 3f) - t * 8f), yaw, label,
                        end == 0 ? UiStyle.Gold : UiStyle.Teal, new Color(0f, 0f, 0f, 0f), 0f);
                    sb.Exclude(c, 10f);
                    if (end != 0) continue;
                    // 入口の両脇の風で削れた岩（道の縁の外、2 つずつ）
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        for (int j = 0; j < 2; j++)
                        {
                            string part = rocks[(k + j + (s2 > 0 ? 1 : 0)) % rocks.Length];
                            Vector3 rp = sb.GroundWorld(c + r * (s2 * (hw + 5f + j * 3.5f)) + t * (j * 6f - 3f)) - Vector3.up * 0.3f;
                            DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), part, g, rp, yaw + 35f * s2 + j * 50f, 1f,
                                new Vector3(3.2f, 2.2f + j, 2.6f), SandRamp));
                        }
                    // 入口を横切る砂の吹きだまり（風で道の上を流れる）
                    Vector3 dp = sb.GroundWorld(c + t * 6f);
                    GenUtil.DustBox(g, "SandDrift_" + k, dp - g.position + Vector3.up * 0.6f, new Vector3(hw * 2f + 10f, 0.8f, 14f), SandDust,
                        new Vector3(r.x * 2.2f, 0.2f, r.z * 2.2f), 9f);
                }
            }
        }

        /// <summary>
        /// 砂漠の踏切（course-r2-04）：深い砂の道の中の線路の路盤（幅 3m）は固い地面（FirmZone：SandZone の中でも砂にしない）。
        /// 入口の右に札「線路の上は固い」。線路の模型は DressingPlan.Railway。
        /// </summary>
        static void RailBed(StageBuild sb, Transform root)
        {
            var runs = SetPieces.RailRuns(sb.def, sb.courseLocal, sb.goalArc);
            if (runs.Count == 0) return;
            var g = GenUtil.Child(root, "RailBed", Vector3.zero).transform;
            g.position = Vector3.zero;
            for (int k = 0; k < runs.Count; k++)
            {
                Vector3 a = sb.Ground(runs[k][0].x, runs[k][0].z), b = sb.Ground(runs[k][1].x, runs[k][1].z);
                Vector3 d = b - a; d.y = 0f;
                var go = GenUtil.World(g, "FirmBed_" + k, (a + b) * 0.5f + Vector3.up * 1f, WorldLayout.YawDeg(d.normalized));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(SetPieces.RAIL_BED_HALF * 2f, 6f, d.magnitude);
                go.layer = WorldLayers.TRIGGERS;
                GenUtil.AddU<FirmZone>(go);
            }
            float a0 = SetPieces.OpeningArc(sb.def, sb.goalArc) - SetPieces.RAIL_LENGTH * SetPieces.RAIL_RUNS * 0.5f;
            Vector3 c = WorldLayout.PointAtArc(sb.courseWorld, a0);
            Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, a0);
            t = new Vector3(t.x, 0f, t.z).normalized;
            SignPost(g, "RailSign", sb.GroundWorld(c + WorldLayout.RightOf(t) * (StageDefinitions.HalfWidthAt(sb.def, a0) + 3f) - t * 10f), WorldLayout.YawDeg(t),
                "<b>線路の上は固い</b>\n細いが速い", UiStyle.Teal, new Color(0f, 0f, 0f, 0f), 0f);
        }

        /// <summary>
        /// 広場の二股（course-r2-13）：分かれ目の岩の列（DressingPlan.Fork）の右の広い線は砂（SandZone、後ろにつく・追い抜く所）、左の狭い線は固い。
        /// 入口の両脇に札：左 = 青緑「‹ 狭い・速い」、右 = 金「広い・砂 ›」。CP は同じ 1 本の列（箱は柵から柵まで）なので MountController は変わらない。
        /// </summary>
        static void Fork(StageBuild sb, Transform root)
        {
            var div = DressingPlan.ForkDivider(sb.def, sb.goalArc);
            if (div.Count == 0) return;
            var g = GenUtil.Child(root, "Fork", Vector3.zero).transform;
            g.position = Vector3.zero;
            for (int k = 0; k + 1 < div.Count; k += 2)
            {
                float a = div[k].x + DressingPlan.FORK_STEP_M;
                Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, a);
                t = new Vector3(t.x, 0f, t.z).normalized;
                Vector3 lane = WorldLayout.PointAtArc(sb.courseWorld, a) + WorldLayout.RightOf(t) * DressingPlan.ForkWideLaneLateral(sb.def, a);
                float width = StageDefinitions.HalfWidthAt(sb.def, a) - DressingPlan.FORK_LATERAL - DressingPlan.FORK_ROCK_R;
                SandBox(sb, g, "WideLaneSand_" + k, sb.GroundWorld(lane) + Vector3.up * 1f, WorldLayout.YawDeg(t), new Vector3(width, 8f, DressingPlan.FORK_STEP_M * 2f));
            }
            float a0 = div[0].x;
            Vector3 c = WorldLayout.PointAtArc(sb.courseWorld, a0);
            Vector3 t0 = WorldLayout.TangentAtArc(sb.courseWorld, a0);
            t0 = new Vector3(t0.x, 0f, t0.z).normalized;
            Vector3 r0 = WorldLayout.RightOf(t0);
            float hw = StageDefinitions.HalfWidthAt(sb.def, a0), yaw = WorldLayout.YawDeg(t0);
            SignPost(g, "ForkSignNarrow", sb.GroundWorld(c - r0 * (hw + 2.5f) - t0 * 12f), yaw, "<b>‹ 狭い・速い</b>\n固い地面", UiStyle.Teal, new Color(0f, 0f, 0f, 0f), 0f);
            SignPost(g, "ForkSignWide", sb.GroundWorld(c + r0 * (hw + 2.5f) - t0 * 12f), yaw, "<b>広い・砂 ›</b>\n追い抜くなら", UiStyle.Gold, new Color(0f, 0f, 0f, 0f), 0f);
        }

        /// <summary>遺物 4 つ（SetPieces.RelicSpots）。宝石は CourseBuilder.Relic。踏み切りの先の遺物は 2.5m の空中（跳んだ人だけ）。</summary>
        static void Relics(StageBuild sb, WorldContext ctx)
        {
            var relicRoot = GenUtil.Child(sb.root, "Relics", Vector3.zero).transform;
            relicRoot.position = Vector3.zero;
            sb.relicSpots = SetPieces.RelicSpots(sb.def, sb.courseLocal, sb.goalArc, sb.shortcut);
            for (int i = 0; i < sb.relicSpots.Length; i++)
            {
                var r = sb.relicSpots[i];
                Vector3 g = sb.Ground(r.local.x, r.local.z);
                ctx.courseRelics.Add(CourseBuilder.Relic(relicRoot, "Relic_" + (i + 1), g + Vector3.up * r.up, sb.def.index, false, g));
            }
        }

        /// <summary>丸太（WoodLogs の部品、長さ 1.5m の横倒し）を立てて高さ heightM の柱にする。足元 ground。</summary>
        public static GameObject UprightLog(Transform parent, string part, Vector3 ground, float yaw, float heightM, float thick)
        {
            var log = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), part, parent, ground + Vector3.up * (heightM * 0.5f), yaw, 1f,
                new Vector3(1.5f, 0.3f, 0.3f), new Color(0.30f, 0.20f, 0.12f));
            log.transform.rotation = Quaternion.Euler(0f, yaw, 90f);
            log.transform.localScale = new Vector3(heightM / 1.5f, thick, thick);
            DressingBuilder.SetStatic(log);
            return log;
        }

        /// <summary>
        /// 立て札（course-r1-04/06/15）：丸太の柱（WoodLogs_01、2.6m）に手配書の紙（WantedPosters_02）、看板の文字。読む人は yaw の方向を見ている。
        /// light ≠ clear なら柱の上に点の灯り。
        /// </summary>
        public static GameObject SignPost(Transform parent, string name, Vector3 ground, float yaw, string text, Color textColor, Color light, float lightRange, float scale = 1f)
        {
            var sign = GenUtil.World(parent, name, ground, yaw);
            // scale > 1（course-r3-07：急カーブの大きな札）：紙と文字を scale 倍、柱は紙の下の辺が 1.55m のまま上へ伸ばす
            float postH = 2.6f + 0.7f * (scale - 1f);
            UprightLog(sign.transform, "WoodLogs_01", ground, yaw, postH, 1.1f);
            GenUtil.PaperSheet(sign.transform, "Board", "WantedPosters_02", new Vector3(0f, 1.55f, -0.18f), null, 1.7f * scale, 0.7f * scale);
            GenUtil.Text(sign.transform, "Text", new Vector3(0f, 1.55f + 0.35f * scale, -0.22f), Quaternion.identity, text, 1.6f * scale, textColor, new Vector2(1.6f * scale, 0.6f * scale), TextStyle.Sign, true);
            if (light.a > 0f) GenUtil.PointLight(sign.transform, "SignLight", ground + Vector3.up * (postH + 0.3f), light, lightRange, 1.4f);
            return sign;
        }

        /// <summary>街灯 1 本（FarmProps_02）と灯り。</summary>
        public static GameObject Lamp(Transform parent, Vector3 ground, float yaw, Color light, float range, float scale = 1f)
        {
            var lamp = GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", parent, ground, yaw, scale, new Vector3(0.3f, 3f, 0.3f), Wood);
            DressingBuilder.SetStatic(lamp);
            GenUtil.PointLight(parent, "LampLight", ground + Vector3.up * (2.9f * scale), light, range, 1.6f);
            return lamp;
        }

        /// <summary>Is Trigger の箱に SandZone（馬を MountMath.HORSE_SAND_FACTOR 倍に）。中心・向き・大きさはワールド。</summary>
        static void SandBox(StageBuild sb, Transform parent, string name, Vector3 center, float yaw, Vector3 size)
        {
            var go = GenUtil.World(parent, name, center, yaw);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.layer = WorldLayers.TRIGGERS;
            sb.sandZones.Add(GenUtil.AddU<SandZone>(go));
        }

        /// <summary>
        /// 岩の橋（course-r1-07）：地形の背（中央 ±7m）が溝を渡る。溝の縁（背の両脇、横 9〜36m）は Collider 付きの岩（DesertRocks_04〜09、Block）で守り、
        /// 背の両脇の溝の中に大岩（DesertRocks_01〜03 ×0.32〜0.44、縦 1.5 倍）を溝へ向けて置く。溝の両端の外（38〜54m）は平らな遠回り（約 40m 長い安全な道）。
        /// 溝の底は落とし穴（ResetVolume）。橋の両端に街灯と焚き火の煙、溝の底に赤い灯り。
        /// </summary>
        static void Bridge(StageBuild sb, Transform root)
        {
            Vector3 center = sb.GroundWorld(WorldLayout.PointAtArc(sb.courseWorld, sb.bridgeArc));
            Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, sb.bridgeArc);
            t = new Vector3(t.x, 0f, t.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float yaw = WorldLayout.YawDeg(t);
            var bridge = GenUtil.World(root, "RockBridge", center, yaw);
            int st = sb.def.index + 1;

            // 溝の縁の岩（Collider、Block）：道から溝へ落ちない。遠回り（38m より外）と背（7m 以内）は空ける
            var guard = GenUtil.Child(bridge.transform, "LipGuard", Vector3.zero).transform;
            float lipAlong = SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND + 0.8f;
            int gi = 0;
            for (int e = -1; e <= 1; e += 2)
                for (int s = -1; s <= 1; s += 2)
                {
                    int li = 0;
                    for (float lat = SetPieces.SPINE_HALF + 2.5f; lat < SetPieces.DETOUR_FROM - 2f; lat += SetPieces.LIP_ROCK_STEP, gi++, li++)
                    {
                        if (!SetPieces.LipRockKept(lat, li)) continue;   // course-r3-04：背の近くは 1 つおき（溝の口が手前から見える）
                        Vector3 w = sb.GroundWorld(center + t * (e * lipAlong) + r * (s * lat));
                        string part = "DesertRocks_0" + (4 + gi % 6);
                        var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), part, guard, w - Vector3.up * 0.3f, yaw + 90f + gi * 37f, 0.9f,
                            new Vector3(4f, 1.6f, 2f), Rock);
                        DressingBuilder.AddCollider(rock, false);
                        GenUtil.SetLayerRecursive(rock, WorldLayers.Block);
                        DressingBuilder.SetStatic(rock);
                        GenUtil.Tag(rock, "S" + st + "/SetPieceDecor/lip#" + (decorIndex++));
                    }
                }
            // 背の両脇（溝の中）の崖：広い面を溝へ
            var spine = GenUtil.Child(bridge.transform, "SpineCliffs", Vector3.zero).transform;
            for (int s = -1; s <= 1; s += 2)
                for (int e = -1; e <= 1; e += 2)
                {
                    int k = (s + 1) + (e + 1) / 2;
                    // art-r2-08：崖の板（Cliffs）は近くで平らな箱に見えるので、形のある大岩（DesertRocks_01〜03）を縦に伸ばす
                    string part = "DesertRocks_0" + (1 + k % 3);
                    float sc = 0.32f + 0.04f * k;
                    Vector3 w = sb.GroundWorld(center + r * (s * (SetPieces.SPINE_HALF + 3f)) + t * (e * 3.5f));
                    var c = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), part, spine, w - Vector3.up * 1f, yaw + (s > 0 ? 0f : 180f) + k * 23f, sc, new Vector3(6f, 8f, 6f), Rock);
                    c.transform.localScale = new Vector3(sc, sc * 1.5f, sc);
                    DressingBuilder.SetStatic(c);
                    GenUtil.Tag(c, "S" + st + "/SetPieceDecor/spine#" + (decorIndex++));
                }
            // 溝の両端の岩（掘った地形の切り口を見せない）と遠回りの入口の杭
            var cover = GenUtil.Child(bridge.transform, "TrenchRocks", Vector3.zero).transform;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 p = sb.GroundWorld(center + r * (s * (SetPieces.DETOUR_FROM - 4f)) + t * (e * 3f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), e < 0 ? "DesertRocks_05" : "DesertRocks_04", cover,
                        p - Vector3.up * 2f, s * 30f + e * 70f, 1.3f, new Vector3(8f, 4f, 6f), Rock);
                    DressingBuilder.SetStatic(rock);
                    GenUtil.Tag(rock, "S" + st + "/SetPieceDecor/rock#" + (decorIndex++));
                }
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 sp = sb.GroundWorld(center + r * (s * (SetPieces.DETOUR_FROM + 2f)) + t * (e * (lipAlong + 3f)));
                    DressingBuilder.SetStatic(GenUtil.WoodPost(cover, sp, yaw, 2.6f, 0.22f));   // 立てた丸太の杭（art-r3-02）
                }
            }
            BridgeGate(sb, bridge.transform, center, t, r, yaw, lipAlong, st);
            // 溝の底の落とし穴：入った騎手を最後に通った CP へ戻す（背の下は地面の中なので触れない）
            var pit = GenUtil.Child(bridge.transform, "FallPit", new Vector3(0f, -SetPieces.TRENCH_DEPTH + 3f, 0f));
            var pitBox = pit.AddComponent<BoxCollider>();
            pitBox.isTrigger = true;
            pitBox.size = new Vector3(SetPieces.DETOUR_FROM * 2f, 8f, (SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND) * 2f);
            pit.layer = WorldLayers.TRIGGERS;
            GenUtil.AddU<ResetVolume>(pit);
            DressingBuilder.SetStatic(bridge);
            sb.Exclude(center, SetPieces.DETOUR_TO + 6f);
            BridgeSignals(sb, bridge.transform, center, t, r);
        }

        public const float BRIDGE_TOWER_H = 8.5f;     // art-r3-14：橋の両端の岩の塔は背より 7〜9m 高い
        public const float BRIDGE_LINTEL_H = 7.2f;    // 向こう端の横木の高さ（背から）

        /// <summary>
        /// 岩の橋の門（art-r3-14）：橋の両端の両脇に大岩（DesertRocks_01/02、縦 ×2.4）の塔を立て（背より 7〜9m 高い、150m 先から門に見える）、
        /// 向こう端の塔の間に丸太（WoodLogs_01、長さだけ伸ばした細い横木）を渡して、手配書の紙の幕に「岩の橋」。
        /// </summary>
        static void BridgeGate(StageBuild sb, Transform bridge, Vector3 center, Vector3 t, Vector3 r, float yaw, float lipAlong, int st)
        {
            var g = GenUtil.Child(bridge, "BridgeGate", Vector3.zero).transform;
            float along = lipAlong + 5f;
            float lat = 0f;
            for (int e = -1; e <= 1; e += 2)
                for (int s = -1; s <= 1; s += 2)
                {
                    string part = (e + s) % 4 == 0 ? "DesertRocks_02" : "DesertRocks_01";
                    float h0 = part == "DesertRocks_01" ? 12.8f : 12.4f, r0 = part == "DesertRocks_01" ? 16.7f : 11.7f;
                    float sc = BRIDGE_TOWER_H / (h0 * 2.4f);
                    lat = SetPieces.SPINE_HALF + 1.2f + r0 * sc * 0.8f;
                    Vector3 w = sb.GroundWorld(center + t * (e * along) + r * (s * lat));
                    var tower = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), part, g, w - Vector3.up * 0.8f, yaw + (s > 0 ? 0f : 180f) + e * 25f, sc,
                        new Vector3(4f, BRIDGE_TOWER_H, 4f), Rock);
                    tower.transform.localScale = new Vector3(sc, sc * 2.4f, sc);
                    DressingBuilder.AddCollider(tower, false);
                    GenUtil.SetLayerRecursive(tower, WorldLayers.Block);
                    DressingBuilder.SetStatic(tower);
                    GenUtil.Tag(tower, "S" + st + "/SetPieceDecor/tower#" + (decorIndex++));
                }
            // 向こう端（e = +1）の横木と幕
            Vector3 l = sb.GroundWorld(center + t * along - r * lat), rr = sb.GroundWorld(center + t * along + r * lat);
            float top = center.y + BRIDGE_LINTEL_H;
            const int PIECES = 4;
            float span = WorldLayout.DistXZ(l, rr), piece = span / PIECES;
            Vector3 bsc = CourseMarks.PostScale(piece + 0.05f, 0.6f);
            for (int k = 0; k < PIECES; k++)
            {
                Vector3 bp = Vector3.Lerp(l, rr, (k + 0.5f) / PIECES);
                var beam = GenUtil.InstantiatePart(GenUtil.Pack("WoodLogs"), "WoodLogs_01", g, new Vector3(bp.x, top, bp.z), yaw + k * 180f, 1f, new Vector3(1.5f, 0.3f, 0.3f), Wood);
                beam.transform.rotation = Quaternion.Euler(0f, yaw + k * 180f, 0f);
                beam.transform.localScale = bsc;
                DressingBuilder.SetStatic(beam);
            }
            Vector3 mid = new Vector3((l.x + rr.x) * 0.5f, top - 1.3f, (l.z + rr.z) * 0.5f);
            var sheet = GenUtil.World(g, "BridgeBanner", mid, yaw);
            GenUtil.PaperSheet(sheet.transform, "Paper", "WantedPosters_02", new Vector3(0f, 0f, -0.05f), null, 4.6f, 1.6f);
            GenUtil.Text(sheet.transform, "Text", new Vector3(0f, 0.05f, -0.12f), Quaternion.identity, "<b>岩の橋</b>", 7f, UiStyle.InkFace,
                new Vector2(4.2f, 1.3f), TextStyle.Ink, true);
        }

        /// <summary>岩の橋の合図：背の両端に街灯 2 本ずつと暖色の灯り、焚き火の煙（遠くから入口がわかる）。溝の底は赤い灯り（危険＝赤の色の決まり）。</summary>
        static void BridgeSignals(StageBuild sb, Transform bridge, Vector3 center, Vector3 t, Vector3 r)
        {
            var sig = GenUtil.Child(bridge, "Signals", Vector3.zero).transform;
            float len = (SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND) * 2f;
            for (int e = -1; e <= 1; e += 2)
            {
                for (int s = -1; s <= 1; s += 2)
                    Lamp(sig, sb.GroundWorld(center + t * (e * (len * 0.5f + 3f)) + r * (s * (SetPieces.SPINE_HALF + 1.5f))), WorldLayout.YawDeg(t) + (s > 0 ? -90f : 90f), LampWarm, 14f);
                Vector3 fp = sb.GroundWorld(center + t * (e * (len * 0.5f + 8f)) + r * (-(SetPieces.SPINE_HALF + 8f)));
                var fire = GenUtil.InstantiatePart(GenUtil.Pack("Campfire"), "Campfire_01", sig, fp, 0f, 1f, new Vector3(1.5f, 0.6f, 1.5f), Rock);
                DressingBuilder.SetStatic(fire);
                GenUtil.Particles(fire.transform, "Flames", new Vector3(0f, 0.4f, 0f), new Color(1f, 0.55f, 0.15f, 0.9f), 0.35f, 1.2f, 0.8f, true, 20f, 0.3f);
                GenUtil.SmokeColumn(fire.transform, "Smoke", new Vector3(0f, 1f, 0f), SmokeGrey, 18f, 1.6f);
            }
            for (int s = -1; s <= 1; s += 2)
                GenUtil.PointLight(sig, "TrenchDanger", center + r * (s * 22f) + Vector3.up * (-SetPieces.TRENCH_DEPTH + 2f), DangerRed, 24f, 1.2f);
            // course-r3-04：背の縁の街灯（危険 = 橙の灯り、片側 2 本）と、70m 手前の赤インクの札「落ちるな」（急カーブの札と同じ丸太の柱）
            float yaw = WorldLayout.YawDeg(t);
            foreach (var m in SetPieces.SpineEdgeLamps())
                Lamp(sig, sb.GroundWorld(center + t * m.x + r * m.y), yaw + (m.y > 0f ? -90f : 90f), SignalOrange, 10f);
            float wa = sb.bridgeArc - SetPieces.BRIDGE_WARN_BEFORE;
            Vector3 wt = WorldLayout.TangentAtArc(sb.courseWorld, wa);
            wt = new Vector3(wt.x, 0f, wt.z).normalized;
            Vector3 wp = WorldLayout.PointAtArc(sb.courseWorld, wa) + WorldLayout.RightOf(wt) * (StageDefinitions.HalfWidthAt(sb.def, wa) + CourseMarks.CORNER_BOARD_OUT_M);
            SignPost(sig, "FallWarning", sb.GroundWorld(wp), WorldLayout.YawDeg(wt), "<b>落ちるな ⇣</b>\n岩の橋は真ん中が安全", UiStyle.RedInk, new Color(0f, 0f, 0f, 0f), 0f);
        }

        /// <summary>
        /// 近道（course-r1-06）：途中の CP ごとに弦の上へ双子の CP（同じ番号・同じステージ、次は次の双子／出口の CP）、床は砂（SandZone）、
        /// 入口の両脇に街灯（橙の灯り 8m）、右に丸太の柱の手配書の札「→ 近道」。段（崖）の縁に小さな岩、通路の両脇と崖の面に岩。
        /// </summary>
        static void Shortcut(StageBuild sb, Transform root)
        {
            SetPieces.ShortcutPlan p = sb.shortcut;
            Vector3 o0 = sb.def.Origin;
            Vector3 dir = new Vector3(p.b.x - p.a.x, 0f, p.b.z - p.a.z).normalized;
            Vector3 rgt = WorldLayout.RightOf(dir);
            float yaw = WorldLayout.YawDeg(dir);
            var g = GenUtil.World(root, "Shortcut", sb.Ground(p.a.x, p.a.z), yaw).transform;
            // 双子の CP
            var twins = CourseBarrier.TwinCheckpoints(p);
            Checkpoint prevTwin = null;
            for (int k = 0; k < twins.Count; k++)
            {
                int idx = p.crossCps[k];
                if (idx < 0 || idx >= sb.checkpoints.Count) continue;
                Checkpoint main = sb.checkpoints[idx];
                Vector3 w = sb.Ground(twins[k][0].x, twins[k][0].z);
                var go = GenUtil.World(g, "TwinCP" + main.index, w, yaw);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(CourseBarrier.TWIN_HALF_WIDTH * 2f, CourseGeometry.CP_HEIGHT + 4f, CourseGeometry.CP_DEPTH);
                box.center = new Vector3(0f, (CourseGeometry.CP_HEIGHT + 4f) * 0.5f - 2f, 0f);
                go.layer = WorldLayers.TRIGGERS;
                var cp = GenUtil.AddU<Checkpoint>(go);
                cp.index = main.index;
                cp.stageIndex = main.stageIndex;
                cp.isGoal = false;
                cp.next = idx + 1 < sb.checkpoints.Count ? sb.checkpoints[idx + 1] : null;
                if (prevTwin != null) prevTwin.next = cp;
                prevTwin = cp;
                sb.twinCheckpoints.Add(cp);
            }
            // 段の下の着地の砂（course-r2-02：着地の LANDING_RUN m だけ。全面を砂にすると疾走でも本コースより遅い罠だった）
            float L = p.length;
            {
                float fm = (p.fDrop + 1f) * 0.5f;
                Vector3 m = Vector3.Lerp(p.a, p.b, fm);
                float hm = SetPieces.ShortcutHeight(p, fm);
                SandBox(sb, g, "LandingSand", new Vector3(o0.x + m.x, o0.y + hm + 1f, o0.z + m.z), yaw,
                    new Vector3((SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND) * 2f, 10f, (1f - p.fDrop) * L));
            }
            // 入口：街灯 2 本と札
            Vector3 entry = Vector3.Lerp(p.a, p.b, p.f0);
            Vector3 entryW = new Vector3(o0.x + entry.x, o0.y + SetPieces.ShortcutHeight(p, p.f0), o0.z + entry.z);
            for (int s = -1; s <= 1; s += 2)
                Lamp(g, sb.GroundWorld(entryW + rgt * (s * (SetPieces.SHORTCUT_HALF_WIDTH + 1.5f))), yaw + (s > 0 ? -90f : 90f), SignalOrange, 8f);
            Vector3 signW = sb.GroundWorld(entryW + rgt * (SetPieces.SHORTCUT_HALF_WIDTH + 3.5f) - dir * 2f);
            SignPost(g, "ShortcutSign", signW, yaw, "<b>→ 近道</b>\n全力で " + p.gainSeconds.ToString("0.0") + " 秒速い", UiStyle.Teal,
                new Color(0f, 0f, 0f, 0f), 0f);
            // 近道に入った合図（ui-r2-17）：札の横に青緑の火花。入口の双子の CP が出す（誰が入っても各自の画面で）
            var burstHolder = GenUtil.World(g, "ShortcutBurst", signW + Vector3.up * 2.5f);
            var burst = GenUtil.Particles(burstHolder.transform, "Burst", Vector3.zero, UiStyle.Teal, 0.25f, 6f, 1.2f, false, 0f, 0.6f);
            Checkpoint entryCp = null;
            foreach (var tc in sb.twinCheckpoints) if (tc != null && tc.transform.IsChildOf(g) && (entryCp == null || tc.index < entryCp.index)) entryCp = tc;
            if (entryCp != null)
            {
                entryCp.isShortcut = true;
                entryCp.shortcutBurst = burst;
            }
            Vector3 lip = Vector3.Lerp(p.a, p.b, p.fDrop);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 w = new Vector3(o0.x + lip.x, o0.y + SetPieces.ShortcutHeight(p, p.fDrop - 0.001f), o0.z + lip.z) + rgt * (s * (SetPieces.SHORTCUT_HALF_WIDTH + 0.5f));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_15", g, w, s * 40f, 1f, new Vector3(1.3f, 1.8f, 1.4f), Rock));
            }
            // 段（崖下り）の通路の両脇の斜面と崖の面に岩（盛った地形の切り口を見せない）
            for (float f = p.f0; f <= p.fDrop + 1e-4f; f += 7f / L)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 q = Vector3.Lerp(p.a, p.b, f);
                    float hgt = SetPieces.ShortcutHeight(p, Mathf.Min(f, p.fDrop - 0.001f));
                    Vector3 w = new Vector3(o0.x + q.x, o0.y + hgt, o0.z + q.z) + rgt * (s * (SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND * 0.6f));
                    var rock = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_1" + (1 + ((int)(f * 97f) % 4)), g,
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
                    var face = GenUtil.InstantiatePart(GenUtil.Pack("DesertRocks"), "DesertRocks_05", g, w - Vector3.up * 1f, yaw + s * 90f, 1.2f,
                        new Vector3(6f, 6f, 6f), Rock);   // art-r2-08：崖の板の代わりに形のある岩
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

        /// <summary>
        /// 踏み切り（course-r1-08）：地形だけ（SetPieces.CarveKicker）。縁の先の窪みに SandZone、縁の両脇 ±9m に干し草の山、坂の両脇に杭の列。
        /// </summary>
        static void Jump(StageBuild sb, Transform root)
        {
            Vector3 lip = sb.GroundWorld(WorldLayout.PointAtArc(sb.courseWorld, sb.jumpArc));
            Vector3 t = WorldLayout.TangentAtArc(sb.courseWorld, sb.jumpArc);
            t = new Vector3(t.x, 0f, t.z).normalized;
            var g = GenUtil.World(root, "DuneJump", lip, WorldLayout.YawDeg(t)).transform;
            float troughMid = (SetPieces.KICKER_LIP_M + SetPieces.KICKER_TROUGH_END) * 0.5f;
            Vector3 tm = sb.GroundWorld(lip + t * troughMid);
            SandBox(sb, g, "TroughSand", tm + Vector3.up * 1.5f, WorldLayout.YawDeg(t),
                new Vector3(SetPieces.KICKER_HALF_WIDTH * 2f, 8f, SetPieces.KICKER_TROUGH_END - SetPieces.KICKER_LIP_M + 2f));
            sb.Exclude(lip, 30f);
            JumpDressing(sb, g, lip, t);
        }

        /// <summary>
        /// ジャンプの合図（art-r1-08）：踏み切りの両脇（±(幅/2+1)）に杭の列 2 つと干し草の山、縁の下に青緑の灯り（加速の色）、
        /// 着地（縁の 15〜30m 先）に砂煙と、走る線の外に骨の馬と骨（ここに落ちる、の目印）、砂丘の稜線から砂が吹く。すべて当たり判定なし（見た目だけ）。
        /// </summary>
        static void JumpDressing(StageBuild sb, Transform root, Vector3 lip, Vector3 t)
        {
            var g = GenUtil.Child(root, "JumpDressing", Vector3.zero).transform;
            g.position = Vector3.zero;
            Vector3 r = WorldLayout.RightOf(t);
            float yaw = WorldLayout.YawDeg(t);
            // course-r3-13：坂の両脇（道の縁の外）に青緑（加速の色）の灯りの街灯の対を 5.5m ごと（前は骨のあばらに見える杭 DesertProps_10）。灯りは 1 本おき
            int li = 0;
            for (float a = -SetPieces.JUMP_LENGTH; a <= 0f; a += SetPieces.JUMP_LAMP_STEP_M, li++)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 lp = sb.GroundWorld(lip + t * a + r * (s * (SetPieces.KICKER_HALF_WIDTH + 1.5f)));
                    if (li % 2 == 0) Lamp(g, lp, yaw + (s > 0 ? -90f : 90f), BoostTeal, 7f);
                    else DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("FarmProps"), "FarmProps_02", g, lp, yaw + (s > 0 ? -90f : 90f), 1f, new Vector3(0.3f, 3f, 0.3f), Wood));
                }
            // 縁の両脇（道の縁の外）に青緑の文字の札「跳べ ↑」、坂の 10m 手前の右に「まん中で跳ぶと砂を越える」
            for (int s = -1; s <= 1; s += 2)
                SignPost(g, "JumpHere_" + (s > 0 ? "R" : "L"), sb.GroundWorld(lip + r * (s * (StageDefinitions.HalfWidthAt(sb.def, sb.jumpArc) + 1.5f)) - t * 1f), yaw, "<b>跳べ ↑</b>",
                    UiStyle.Teal, BoostTeal, 6f, 1.3f);
            {
                float wa = sb.jumpArc - SetPieces.JUMP_LENGTH - SetPieces.JUMP_SIGN_BEFORE_M;
                Vector3 wt = WorldLayout.TangentAtArc(sb.courseWorld, wa);
                wt = new Vector3(wt.x, 0f, wt.z).normalized;
                Vector3 wp = WorldLayout.PointAtArc(sb.courseWorld, wa) + WorldLayout.RightOf(wt) * (StageDefinitions.HalfWidthAt(sb.def, wa) + CourseMarks.CORNER_BOARD_OUT_M);
                SignPost(g, "JumpHint", sb.GroundWorld(wp), WorldLayout.YawDeg(wt), "<b>まん中で跳ぶと</b>\n砂を越える", UiStyle.Teal, new Color(0f, 0f, 0f, 0f), 0f, 1.2f);
            }
            // 縁の両脇 ±9m に干し草の山（中央の跳ぶ線と横の転がる線の境目）
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 hp = sb.GroundWorld(lip + r * (s * 9f) - t * 0.5f);
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hp, yaw, 1f, new Vector3(1.5f, 1.6f, 1.5f), SandRamp));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_01", g, hp + Vector3.up * 1.55f + t * 0.2f, yaw + 40f, 0.9f,
                    new Vector3(1.5f, 1.6f, 1.5f), SandRamp));
                DressingBuilder.SetStatic(GenUtil.InstantiatePart(GenUtil.Pack("HayBales"), "HayBales_0" + (2 + (s > 0 ? 1 : 0)), g, hp - t * 1.6f, yaw + 90f, 1f,
                    new Vector3(0.9f, 0.6f, 0.7f), SandRamp));
            }
            GenUtil.PointLight(g, "BoostLight", lip - t * 1.2f + Vector3.up * 0.6f, BoostTeal, 8f, 2.2f);
            // 着地の砂煙と、着地の先の骨（走る線の外）
            Vector3 land = sb.GroundWorld(lip + t * 18f);
            GenUtil.DustBox(g, "LandingSand", land - g.position + Vector3.up * 0.5f, new Vector3(14f, 0.5f, 10f), SandDust, new Vector3(0.8f, 0.3f, 0.4f), 6f);
            var skel = GenUtil.PlaceModel(GenUtil.SKELETON_FBX, g, "SkeletonHorse", sb.GroundWorld(lip + t * 22f + r * 18f) - Vector3.up * 0.15f, yaw + 30f, 1f,
                new Vector3(2f, 1.2f, 2.6f), SandRamp);
            foreach (var a in skel.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            skel.transform.rotation = Quaternion.Euler(0f, yaw + 30f, 85f);
            DressingBuilder.SetStatic(skel);
            string[] bones = { "DesertProps_07", "DesertProps_08", "DesertProps_08" };
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 bp = sb.GroundWorld(lip + t * (16f + i * 6f) + r * (-(16f + i)));
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
