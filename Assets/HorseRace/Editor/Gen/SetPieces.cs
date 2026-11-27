using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5）の純粋な幾何（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    ///   岩の橋（荒野、course-r1-07）：コースを横切る溝（深さ 12m）を高さマップに掘るが、道の中心 ±SPINE_HALF（7m）は掘らずに残す
    ///         ＝ 幅 14m の地形の背（岩の橋）。溝の両端（道から DETOUR_FROM 38m より外）も掘らず、平らにならした遠回り（約 40m 長い安全な道）にする。
    ///         作った板・手すりは無い（すべて地形）。溝の底には落とし穴（ResetVolume → 最後の CP へ戻す）。
    ///   近道（course-r1-06）：CP 2〜6 区間ぶんのコースの曲がりを弦で切る枝道。途中に高さ SHORTCUT_DROP の段（崖）があり、着地の硬直がある。
    ///         損得は秒（course-r2-02、ShortcutGainSeconds）：疾走なら 1.5〜3.5 秒速く、駈歩では本コースの疾走より遅い。砂（SandZone）は段の下の着地の 12m だけ。
    ///         途中の CP には弦の上に「双子の CP」（同じ番号の Checkpoint）を置く：どちらの道でも CP を順番どおりに通る。
    ///   踏み切り（砂漠、course-r1-08）：道幅いっぱいの砂の盛り上がり（22m で +5m、13°）、中央 ±7m だけ 4m で 6m 落ちる縁、
    ///         その先 16m は砂の窪み（SandZone）。跳べば越えられ、横の浅い坂を転がる人は砂を通る。
    ///   台地（境目、course-r1-05）：道の高さの形で 32m 登る区間の両脇 35m の帯も同じ高さへ持ち上げる（縁から 15m で元の地形へ落ちる崖）。
    /// 座標は Terrain ローカル（m）。高さマップは [z,x]、0..1（× heightM）。
    /// </summary>
    public static class SetPieces
    {
        // 岩橋
        public const float TRENCH_HALF_WIDTH = 9f;     // コース方向の溝の半幅（底が平らな部分。course-r3-04：7 → 9m、60m 手前から溝の口が読める）
        public const float TRENCH_BLEND = 3f;          // 溝の縁の傾斜
        public const float TRENCH_HALF_LENGTH = 45f;   // コースを横切る方向の半分の長さ
        public const float TRENCH_END_TAPER = 12f;     // 溝の両端で浅くなる長さ
        public const float TRENCH_DEPTH = 12f;
        public const float BRIDGE_HALF_SPAN = 14f;     // 岩の背の勾配の検査から外す半分の長さ（溝の縁 12m より長い）
        public const float SPINE_HALF = 7f;            // 掘らずに残す地形の背（岩の橋）の半幅（絞り 14m）
        public const float SPINE_EDGE = 2f;            // 背の縁の切り立った斜面
        public const float DETOUR_FROM = 38f;          // ここから外は掘らない（溝の両端を回る遠回り）
        public const float DETOUR_TO = 54f;            // 遠回りの外の縁（平らにならす範囲）
        public const float DETOUR_ALONG = 30f;         // 遠回りのならしのコース方向の半分の長さ
        public const float DETOUR_BARRIER_M = 58f;     // 橋の周りの柵（遠回りを囲む）
        // ---- course-r3-04：溝の口を手前から見せる・危ない線の合図 ----
        public const float LIP_ROCK_STEP = 4.2f;       // 溝の縁の岩の間隔
        public const float LIP_THIN_LAT = 20f;         // 背から 20m 以内の縁の岩は 1 つおき（溝の口が近づく前から見える）
        public const float BRIDGE_WARN_BEFORE = 70f;   // 「落ちるな」の札（赤インク）は橋の 70m 手前

        /// <summary>溝の縁の岩を置くか（course-r3-04）：横 lat の k 番目。背から LIP_THIN_LAT 以内は 1 つおき。</summary>
        public static bool LipRockKept(float lat, int k) => lat >= LIP_THIN_LAT || k % 2 == 0;

        /// <summary>背の縁の街灯（course-r3-04、橋の中心からの (コース方向, 横)）：片側 2 本、縁から 0.4m 内側。</summary>
        public static Vector2[] SpineEdgeLamps()
        {
            float a = (TRENCH_HALF_WIDTH + TRENCH_BLEND) * 0.5f, lat = SPINE_HALF - 0.4f;
            return new[] { new Vector2(-a, -lat), new Vector2(a, -lat), new Vector2(-a, lat), new Vector2(a, lat) };
        }

        // 踏み切り（地形だけ。course-r1-08）
        public const float JUMP_LENGTH = 22f, JUMP_HEIGHT = 5f;   // 坂（13°）
        public const float KICKER_DROP_M = 6f, KICKER_LIP_M = 4f; // 縁で 6m を 4m で落ちる（中央 ±KICKER_CORE_M）
        public const float KICKER_CORE_M = 7f, KICKER_CORE_BLEND = 5f;
        public const float KICKER_SIDE_HEIGHT = 1.5f, KICKER_SIDE_BACK_M = 14f;   // 横は低く、ゆるい坂で窪みへ
        public const float KICKER_TROUGH_END = 20f, KICKER_TROUGH_EXIT = 6f;      // 縁から 4〜20m が砂の窪み、6m で道へ戻る
        public const float KICKER_HALF_WIDTH = 13f, KICKER_SHOULDER = 6f;
        public const float JUMP_WIDTH = KICKER_CORE_M * 2f;
        public const float JUMP_RELIC_AHEAD = 14f, JUMP_RELIC_UP = 2.5f;

        // 境目の台地（course-r1-05）
        public const float MESA_BAND_HALF = 35f;       // 道の中心から 35m（帯 70m）は道と同じ高さ
        public const float MESA_CLIFF_M = 15f;         // その外 15m で元の地形へ（崖）
        public const float MESA_RAMP_M = 260f;

        // 境目のメサの切り通し
        public const float PASS_DEPTH = 6f;            // 道を下げる深さ
        public const float PASS_HALF_CUT = 25f;        // 深さいっぱいの区間の半分（全長 50m。course-r3-02：15 → 25m）
        public const float PASS_RAMP = 60f;            // 前後の下り・上りの最短（course-r3-02：6m / 60m ≈ 5.7°。実際は PASS_TAPER_M でまっすぐ下げる）
        public const float PASS_ROAD_HALF = 13f;       // ここまで（道 ±12m ＋余白）は下げる
        public const float PASS_WALL_RISE = 22f;       // 両脇（道から 22〜40m）の盛り上げ
        public const float PASS_WALL_FROM = 22f, PASS_WALL_TO = 40f, PASS_WALL_FADE = 30f;
        // ---- art-r3-08：切り通しの手前の背（こぶ）をなくし、上の開きを 50m・壁を 2 段のベンチに ----
        public const float PASS_TAPER_M = 80f;         // 深さいっぱいの区間の前後 80m でまっすぐ下げる（道の高さはその間で単調）
        public const float PASS_PROFILE_BEFORE = 120f, PASS_PROFILE_AFTER = 120f;  // 道の高さを弦より上に出さない範囲（切り通しの中心から。下げ終わる 105m より外）
        public const float PASS_CREST_CHECK_M = 80f, PASS_MAX_CREST_M = 1f;        // course-r3-02：切り通しの 80m 手前から、弦より 1m 以上の背（こぶ）を作らない
        public const float PASS_BENCH1_FROM = 16f, PASS_BENCH1_H = 11f;            // 1 段目の壁（道の縁 13m の外 16〜20m で 11m 上る）
        public const float PASS_BENCH2_FROM = 25f, PASS_BENCH2_H = 11f;            // 2 段目（25〜29m でさらに 11m：上の開きは 50m）
        public const float PASS_BENCH_RISE_M = 4f;
        public const float PASS_CREST_FROM = 29f;      // 尾根（2 段目の上）の始まり：メサの門と焚き火はここから外
        public const float PASS_ROAD_BLEND = 4f;       // 道の帯（PASS_ROAD_HALF）の外 4m で下げ幅を 0 へ

        // 砂漠の踏切（course-r2-04）：序盤の道に沿って線路（WesternTown_049_Track、長さ 95.6m）を 2 本つなぎ、幅 3m の路盤だけ固い（道の上は深い砂）
        public const float RAIL_LENGTH = 95.6f;
        public const float RAIL_LATERAL = 7f;          // 道の中心から路盤の中心まで（道の右寄り、半幅 12m の内側）
        public const int RAIL_RUNS = 2;
        public const float RAIL_BED_HALF = 1.5f, RAIL_BED_BLEND = 2f;

        // 崖下りの近道
        public const float SHORTCUT_DROP = 8f;         // course-r2-02：着地で 0.16 秒の硬直・速さ 0.89 倍（本当の危険）
        public const float MAX_DROP = 10f;
        public const float SHORTCUT_HALF_WIDTH = 4f;
        public const float SHORTCUT_BLEND = 4f;
        public const float SHORTCUT_RAMP_DEG = 12f;
        public const float RUNWAY_KEEP = 14f;          // 本コースの平らな道（±12m）＋余白は触らない
        public const float SHORTCUT_MIN_DEV = 24f;     // 弦がコースから最低これだけ離れる区間を選ぶ
        public const float SHORTCUT_MAX_DEV = 90f;     // 弦の真ん中が本コースからこれ以上離れない（柵は弦にも沿う）
        public const float SHORTCUT_MAX_CP_DEV = 50f;  // コース外リセット（CP の線分から 60m）より十分内側
        // 近道の損得は秒で測る（course-r2-02）：本コースを疾走（18 m/s）で走るのと比べて、近道を疾走で 1.5 秒以上速く、駈歩（14 m/s）では遅い
        public const float SHORTCUT_MIN_GAIN_S = 1.5f, SHORTCUT_MAX_GAIN_S = 3.5f;
        public const float SHORTCUT_SPEED = MountMath.TOP_SPEED, SHORTCUT_CANTER_SPEED = 14f;
        public const float SHORTCUT_UPHILL_PER_DEG = 0.015f;   // MountMath.TargetSpeed の馬の上り
        public const float HAIRPIN_AVOID_M = 60f;      // ヘアピンの前後これだけは近道で切らない（曲がりの技を飛ばさない）
        public const int SHORTCUT_MIN_SPAN = 2, SHORTCUT_MAX_SPAN = 6;
        public const float LANDING_RUN = 12f;          // 段の下からコースに戻るまでの平らな長さ
        public const float END_INSET = 6f;             // CP から内側へ

        public static float JumpAngleDeg => Mathf.Atan(JUMP_HEIGHT / JUMP_LENGTH) * Mathf.Rad2Deg;

        /// <summary>target に一番近い「CP と CP の中間」の弧長（spacing*k + spacing/2、k ≥ 1、ゴールの 1.5 区間手前まで）。</summary>
        public static float MidCpArc(float target, float spacing, float goalArc)
        {
            int kMax = Mathf.Max(1, Mathf.FloorToInt((goalArc - spacing * 1.5f) / spacing));
            int k = Mathf.Clamp(Mathf.RoundToInt((target - spacing * 0.5f) / spacing), 1, kMax);
            return k * spacing + spacing * 0.5f;
        }

        /// <summary>弧長が [a0, a1] に入る点を除いた点列（橋の上は板なので地形の勾配の検査から外す）。</summary>
        public static Vector3[] ExcludeArcRange(Vector3[] pts, float a0, float a1)
        {
            var r = new System.Collections.Generic.List<Vector3>();
            float acc = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) acc += WorldLayout.DistXZ(pts[i - 1], pts[i]);
                if (acc < a0 || acc > a1) r.Add(pts[i]);
            }
            return r.ToArray();
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// 岩の橋の地形（h を書き換える）。center/tangent は橋の中心とコースの向き（x/z）。
        /// 1) 道の外 DETOUR_TO m まで・コース方向 ±DETOUR_ALONG m を道の高さにならす（溝の両端を回る遠回りの地面）。
        /// 2) コース方向 ±(TRENCH_HALF_WIDTH + TRENCH_BLEND)、横 SPINE_HALF〜DETOUR_FROM m を TRENCH_DEPTH 掘る。中央の背と両端は掘らない。
        /// </summary>
        public static void CarveTrench(float[,] h, float sizeM, float heightM, Vector3 center, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float road = WorldLayout.SampleHeight(h, sizeM, heightM, center.x, center.z) / heightM;
            float reach = DETOUR_TO + DETOUR_ALONG + 8f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - center.x, dz = z * cell - center.z;
                    float along = Mathf.Abs(dx * t.x + dz * t.z);
                    float lat = Mathf.Abs(dx * r.x + dz * r.z);
                    // 1) ならす
                    float fa = 1f - Smooth01((along - DETOUR_ALONG) / 8f);
                    float fl = 1f - Smooth01((lat - DETOUR_TO) / 8f);
                    float wf = fa * fl;
                    if (wf > 0f) h[z, x] = Mathf.Lerp(h[z, x], road, wf);
                    // 2) 掘る
                    float delta = TrenchDepthAt(along, lat);
                    if (delta > 0f) h[z, x] = Mathf.Max(0f, h[z, x] - delta / heightM);
                }
        }

        /// <summary>溝の深さ（m）。along/lat は橋の中心からのコース方向・横の距離（絶対値）。背（|lat| &lt; SPINE_HALF）と両端（&gt; DETOUR_FROM）は 0。</summary>
        public static float TrenchDepthAt(float along, float lat)
        {
            float wa = 1f - Smooth01((along - TRENCH_HALF_WIDTH) / TRENCH_BLEND);
            float wl = Smooth01((lat - SPINE_HALF) / SPINE_EDGE) * (1f - Smooth01((lat - (DETOUR_FROM - TRENCH_END_TAPER * 0.5f)) / (TRENCH_END_TAPER * 0.5f)));
            return TRENCH_DEPTH * wa * wl;
        }

        // ------------------------------------------------------------------
        // ステージ定義からの配置（Build World・Validate・テストで同じ結果）
        // ------------------------------------------------------------------

        public static float BridgeArc(StageDef def, float goalArc)
            => def.bridgeFraction < 0f ? -1f : MidCpArc(def.bridgeFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        public static float JumpArc(StageDef def, float goalArc)
            => def.jumpFraction < 0f ? -1f : MidCpArc(def.jumpFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        public static float PassArc(StageDef def, float goalArc)
            => def.passFraction < 0f ? -1f : MidCpArc(def.passFraction * goalArc, StageDefinitions.CP_SPACING_M, goalArc);

        /// <summary>点列の y に高さマップの高さ（m）を入れたコピー。</summary>
        public static Vector3[] WithHeights(Vector3[] local, float[,] h)
        {
            var r = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
                r[i] = new Vector3(local[i].x, WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, local[i].x, local[i].z), local[i].z);
            return r;
        }

        /// <summary>このステージの近道の計画（無ければ null）。heights は HeightmapGen の生成直後（台地を持ち上げた後、掘る前）の高さ。</summary>
        public static ShortcutPlan PlanFor(StageDef def, Vector3[] local, float[,] heights, float goalArc)
        {
            if (!def.hasShortcut) return null;
            float bridge = BridgeArc(def, goalArc), jump = JumpArc(def, goalArc);
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            var avoid = new System.Collections.Generic.List<float>();
            if (bridge >= 0f) { avoid.Add(bridge - 80f); avoid.Add(bridge + 80f); }
            if (jump >= 0f) { avoid.Add(jump - 80f); avoid.Add(jump + 60f); }
            foreach (var hp in StageDefinitions.Hairpins(def, local)) { avoid.Add(hp.fromArc - HAIRPIN_AVOID_M); avoid.Add(hp.toArc + HAIRPIN_AVOID_M); }
            avoid.Add(StageDefinitions.DashArc(def, goalArc) - 40f); avoid.Add(goalArc + 1000f);
            return PlanShortcut(WithHeights(local, heights), cps, avoid.ToArray(), def.shortcutFrom * goalArc, def.shortcutTo * goalArc);
        }

        /// <summary>見どころの地形の加工（台地・溝・近道・踏み切り・切り通し・廃線の路盤）を heights に適用する。</summary>
        public static ShortcutPlan ApplyTerrain(StageDef def, Vector3[] local, float[,] heights, float goalArc)
        {
            RaiseMesa(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, def, local, goalArc);
            ShortcutPlan plan = PlanFor(def, local, heights, goalArc);
            float bridge = BridgeArc(def, goalArc);
            if (bridge >= 0f)
                CarveTrench(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, bridge), WorldLayout.TangentAtArc(local, bridge));
            if (plan != null) ApplyShortcut(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, plan, local);
            float jump = JumpArc(def, goalArc);
            if (jump >= 0f)
                CarveKicker(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, jump), WorldLayout.TangentAtArc(local, jump));
            float pass = PassArc(def, goalArc);
            if (pass >= 0f)
                CarvePass(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, local, pass);
            if (def.kind == HeightmapGen.KIND_DUNES)
                FlattenStart(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, local);   // art-r3-11
            foreach (var run in RailRuns(def, local, goalArc))
                FlattenBed(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, run[0], run[1]);
            {
                // ゴールの先の尾根の切れ込み（course-r2-05：3 ステージとも。ゴールから外の世界が見える）
                Vector3 g = WorldLayout.PointAtArc(local, goalArc);
                float gh = WorldLayout.SampleHeight(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, g.x, g.z);
                CarveGoalNotch(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, g, WorldLayout.TangentAtArc(local, goalArc), gh);
            }
            return plan;
        }

        // ---- ゴールの先の尾根の切れ込み（art-r2-06 → course-r2-05：3 ステージとも）----
        public const float NOTCH_FROM_M = 30f;        // ゴールの先 30m から（低くするだけなので道・囲いの高さは変わらない）
        public const float NOTCH_RAMP_M = 30f;        // 切り込みの入り口のなめらかさ
        public const float NOTCH_HALF_M = 35f;        // 底の幅 70m（U 字）
        public const float NOTCH_FLOOR_M = 4f;        // 底はゴールの地面 + 4m
        public const float NOTCH_SIDE_SLOPE = 0.2f;   // 底の縁から外へ 1:5 で上る壁
        public const float NOTCH_EYE_M = 2.5f;        // 馬の上の目の高さ（GoalViewOpen の基準）
        public const float NOTCH_VIEW_M = 300f;       // ゴールからこの距離まで地形が目の高さ + 10m より低い

        /// <summary>切り込みの入り口の重み（0〜1）。along = ゴールの向きの距離。</summary>
        public static float NotchWeight(float along) => Smooth01((along - NOTCH_FROM_M) / NOTCH_RAMP_M);

        /// <summary>切り込みの面の高さ（m、ゴールの地面からの相対）：底 70m は +4m、その外は 1:5 で上る。lat = 横の距離。</summary>
        public static float NotchFloor(float lat) => NOTCH_FLOOR_M + Mathf.Max(0f, Mathf.Abs(lat) - NOTCH_HALF_M) * NOTCH_SIDE_SLOPE;

        /// <summary>
        /// ゴールの先の尾根を U 字に切る（course-r2-05）：ゴールの向きに、底 70m・ゴール + 4m、両脇は 1:5 の壁。
        /// 以前は最後の直線が外周の壁（60m の崖）に向かって終わり、一番大事な瞬間に地図の端を見ていた。
        /// 切れ込みの向こうに次のステージの目印（DressingPlan.NextStageLandmark）が見える。
        /// </summary>
        public static void CarveGoalNotch(float[,] h, float sizeM, float heightM, Vector3 goal, Vector3 tangent, float goalH)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x * cell - goal.x, dz = z * cell - goal.z;
                    float along = dx * t.x + dz * t.z, lat = dx * r.x + dz * r.z;
                    if (along < NOTCH_FROM_M) continue;
                    float w = NotchWeight(along);
                    float floor = (goalH + NotchFloor(lat)) / heightM;
                    if (h[z, x] > floor) h[z, x] = Mathf.Lerp(h[z, x], floor, w);
                }
        }

        /// <summary>台地の区間（登りの坂の始まり〜台地の端の 1 段目の下りの終わり、弧長 m）。台地が無ければ false。</summary>
        public static bool MesaRange(StageDef def, float goalArc, out float from, out float to)
        {
            from = to = -1f;
            if (def.mesaClimbFraction < 0f || def.mesaTopEndFraction < 0f) return false;
            from = def.mesaClimbFraction * goalArc - MESA_RAMP_M;
            to = def.mesaTopEndFraction * goalArc + 110f;
            return true;
        }

        /// <summary>
        /// 台地（course-r1-05）：MesaRange の区間で、道の中心から MESA_BAND_HALF m の帯を道の高さ（HeightmapGen.PathHeight）まで持ち上げ、
        /// その外 MESA_CLIFF_M m で元の地形へ落とす（下げはしない）。区間の両端（坂の下）は道の高さが元の高さに戻るので自然につながる。
        /// </summary>
        public static void RaiseMesa(float[,] h, float sizeM, float heightM, StageDef def, Vector3[] local, float goalArc)
        {
            float from, to;
            if (!MesaRange(def, goalArc, out from, out to)) return;
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            var pts = new System.Collections.Generic.List<Vector3>();
            var arcs = new System.Collections.Generic.List<float>();
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            // 区間の外の点も探す（区間の端の先の道の上を持ち上げない）。持ち上げるのは一番近い点が区間の中のときだけ
            for (float a = Mathf.Max(0f, from - 80f); a <= to + 80f; a += 3f)
            {
                Vector3 p = WorldLayout.PointAtArc(local, a);
                pts.Add(p); arcs.Add(a);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            float reach = MESA_BAND_HALF + MESA_CLIFF_M;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((maxX + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((maxZ + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x * cell, pz = z * cell, best = float.MaxValue, arc = 0f;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        float dx = pts[i].x - px, dz = pts[i].z - pz, dd = dx * dx + dz * dz;
                        if (dd < best) { best = dd; arc = arcs[i]; }
                    }
                    float d = Mathf.Sqrt(best);
                    if (d >= reach || arc < from || arc > to) continue;
                    float target = HeightmapGen.PathHeight(def, arc);
                    float w = 1f - Smooth01((d - MESA_BAND_HALF) / MESA_CLIFF_M);
                    float v = Mathf.Lerp(h[z, x], target, w);
                    if (v > h[z, x]) h[z, x] = Mathf.Clamp01(v);
                }
        }

        // ------------------------------------------------------------------
        // ジャンプの盛り上がり・切り通し・廃線
        // ------------------------------------------------------------------

        /// <summary>
        /// 踏み切りの高さの変化（m、地形に足す）。along = 縁からのコース方向の位置（+ が進む向き、縁 = 0）、lat = 横の距離。
        /// 坂：−JUMP_LENGTH〜0 で 0 → 縁の高さ（中央 JUMP_HEIGHT、横は KICKER_SIDE_HEIGHT）。縁の後ろ：中央は KICKER_LIP_M で −1m まで落ち、
        /// 横は KICKER_SIDE_BACK_M のゆるい坂。窪み（−1m）は KICKER_TROUGH_END まで、KICKER_TROUGH_EXIT で 0 へ。道の外は KICKER_SHOULDER で 0。
        /// </summary>
        public static float KickerDelta(float along, float lat)
        {
            float la = Mathf.Abs(lat);
            float lf = 1f - Smooth01((la - KICKER_HALF_WIDTH) / KICKER_SHOULDER);
            if (lf <= 0f) return 0f;
            float core = 1f - Smooth01((la - KICKER_CORE_M) / KICKER_CORE_BLEND);
            float crest = Mathf.Lerp(KICKER_SIDE_HEIGHT, JUMP_HEIGHT, core);
            float low = JUMP_HEIGHT - KICKER_DROP_M;   // −1m
            float back = Mathf.Lerp(KICKER_SIDE_BACK_M, KICKER_LIP_M, core);
            float d;
            if (along < -JUMP_LENGTH) d = 0f;
            else if (along <= 0f) d = crest * (along + JUMP_LENGTH) / JUMP_LENGTH;
            else if (along <= back) d = Mathf.Lerp(crest, low, along / back);
            else if (along <= KICKER_TROUGH_END) d = low;
            else if (along <= KICKER_TROUGH_END + KICKER_TROUGH_EXIT) d = Mathf.Lerp(low, 0f, (along - KICKER_TROUGH_END) / KICKER_TROUGH_EXIT);
            else d = 0f;
            return d * lf;
        }

        /// <summary>踏み切りを地形に作る（h を書き換える）。lip/tangent は縁の位置とコースの向き。</summary>
        public static void CarveKicker(float[,] h, float sizeM, float heightM, Vector3 lip, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float reach = JUMP_LENGTH + KICKER_TROUGH_END + KICKER_TROUGH_EXIT + KICKER_HALF_WIDTH + KICKER_SHOULDER;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((lip.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((lip.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((lip.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((lip.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - lip.x, dz = z * cell - lip.z;
                    float d = KickerDelta(dx * t.x + dz * t.z, dx * r.x + dz * r.z);
                    if (d != 0f) h[z, x] = Mathf.Clamp01(h[z, x] + d / heightM);
                }
        }

        /// <summary>
        /// 切り通しの壁の高さの変化（m、地形に足す、art-r3-08）：道の外に 2 段のベンチ（PASS_BENCH1/2、上の開きは 2 × PASS_CREST_FROM − 8 ≈ 50m）、
        /// その外の尾根を PASS_WALL_TO から PASS_WALL_FADE かけて元へ。前後は切り通しの中心から PASS_HALF_CUT + 10m の外 30m で 0。
        /// 道の下げ（CarvePass）は道の高さの線（PassRoadDelta）で別に行う。
        /// </summary>
        public static float PassDelta(float along, float lat)
        {
            float al = Mathf.Abs(along), la = Mathf.Abs(lat);
            float awWall = 1f - Smooth01((al - PASS_HALF_CUT - 10f) / 30f);
            float rise = PASS_BENCH1_H * Smooth01((la - PASS_BENCH1_FROM) / PASS_BENCH_RISE_M)
                       + PASS_BENCH2_H * Smooth01((la - PASS_BENCH2_FROM) / PASS_BENCH_RISE_M);
            float outer = 1f - Smooth01((la - PASS_WALL_TO) / PASS_WALL_FADE);
            return rise * awWall * outer;
        }

        /// <summary>
        /// 道の高さの列 h（等間隔）の「背」の高さ（m、course-r3-02）：両端を結ぶ弦より上に出る一番大きい高さ。0 以下 = 凹か平ら（手前から先が見える）。
        /// </summary>
        public static float MaxCrestAboveChord(float[] h)
        {
            int n = h.Length;
            if (n < 3) return 0f;
            float best = 0f;
            for (int i = 1; i < n - 1; i++) best = Mathf.Max(best, h[i] - (h[0] + (h[n - 1] - h[0]) * i / (n - 1f)));
            return best;
        }

        /// <summary>切り通しの手前 PASS_CREST_CHECK_M m の道の高さ（m、1m ごと、地形 h から）。</summary>
        public static float[] PassApproachProfile(float[,] h, Vector3[] local, float passArc)
        {
            int n = Mathf.RoundToInt(PASS_CREST_CHECK_M) + 1;
            var r = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = WorldLayout.PointAtArc(local, passArc - PASS_CREST_CHECK_M + i);
                r[i] = WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
            }
            return r;
        }

        /// <summary>切り通しの道の下げの割合（0〜1）：中心 ±PASS_HALF_CUT は 1、その前後 PASS_TAPER_M でまっすぐ 0 へ（art-r3-08）。</summary>
        public static float PassTaper(float along)
        {
            float al = Mathf.Abs(along);
            return 1f - Mathf.Clamp01((al - PASS_HALF_CUT) / PASS_TAPER_M);
        }

        /// <summary>
        /// 切り通しの道の高さの線（art-r3-08）：元の道の高さ orig（弧長 a ごと、中心 pass から PASS_PROFILE_BEFORE 手前〜PASS_PROFILE_AFTER 先）から、
        /// target = min(orig − PASS_DEPTH × PassTaper, 両端を結ぶ弦)。弦より上に出ない＝手前の背（こぶ）が消え、切り通しの中は PASS_DEPTH 以上低い。
        /// 返すのは下げ幅（≤ 0、m）。
        /// </summary>
        public static float[] PassRoadDelta(float[] orig, float step, float startAlong)
        {
            int n = orig.Length;
            var d = new float[n];
            if (n < 2) return d;
            float h0 = orig[0], h1 = orig[n - 1];
            for (int i = 0; i < n; i++)
            {
                float along = startAlong + i * step;
                float chord = h0 + (h1 - h0) * i / (n - 1f);
                float target = Mathf.Min(orig[i] - PASS_DEPTH * PassTaper(along), chord);
                d[i] = Mathf.Min(0f, target - orig[i]);
            }
            return d;
        }

        /// <summary>
        /// 切り通しを掘る（art-r3-08）：コースに沿って道の高さの線（PassRoadDelta）だけ下げ（道の帯 PASS_ROAD_HALF、外 PASS_ROAD_BLEND でなめらかに）、
        /// 両脇に 2 段のベンチの壁（PassDelta、横 = コースからの距離）を盛る。
        /// </summary>
        public static void CarvePass(float[,] h, float sizeM, float heightM, Vector3[] local, float passArc)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            const float STEP = 1f;
            float a0 = passArc - PASS_PROFILE_BEFORE, a1 = passArc + PASS_PROFILE_AFTER;
            int m = Mathf.RoundToInt((a1 - a0) / STEP) + 1;
            var orig = new float[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 p = WorldLayout.PointAtArc(local, a0 + i * STEP);
                orig[i] = WorldLayout.SampleHeight(h, sizeM, heightM, p.x, p.z);
            }
            float[] delta = PassRoadDelta(orig, STEP, -PASS_PROFILE_BEFORE);
            var pts = new Vector3[m];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < m; i++)
            {
                pts[i] = WorldLayout.PointAtArc(local, a0 + i * STEP);
                minX = Mathf.Min(minX, pts[i].x); maxX = Mathf.Max(maxX, pts[i].x);
                minZ = Mathf.Min(minZ, pts[i].z); maxZ = Mathf.Max(maxZ, pts[i].z);
            }
            // 各セル：切り通しの区間のコースで一番近い点（弧長 → 中心からの along、距離 → 横）。直線の座標ではなくコースに沿う（曲がっていても道に壁が乗らない）
            float reach = PASS_WALL_TO + PASS_WALL_FADE;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((maxX + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((maxZ + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x * cell, pz = z * cell;
                    float best = float.MaxValue; int bi = 0;
                    for (int i = 0; i < m; i++)
                    {
                        float dx = pts[i].x - px, dz = pts[i].z - pz, d2 = dx * dx + dz * dz;
                        if (d2 < best) { best = d2; bi = i; }
                    }
                    float lat = Mathf.Sqrt(best);
                    if (lat > reach) continue;
                    float along = a0 + bi * STEP - passArc;
                    float roadW = 1f - Smooth01((lat - PASS_ROAD_HALF) / PASS_ROAD_BLEND);
                    // 端の点が一番近いセル（区間の外へはみ出た側）は下げない
                    if (bi == 0 || bi == m - 1) roadW = 0f;
                    float d = delta[bi] * roadW + PassDelta(along, lat);
                    if (d != 0f) h[z, x] = Mathf.Clamp01(h[z, x] + d / heightM);
                }
        }

        // ---- art-r3-11：砂漠のスタートの直線を平らに（最初の砂丘の背を 150m より先へ）----
        public const float START_FLAT_M = 120f, START_FLAT_BLEND_M = 80f, START_FLAT_SIDE_M = 30f, START_FLAT_SIDE_BLEND_M = 25f;

        /// <summary>
        /// 道の最初の START_FLAT_M を、スタートの地面の高さにそろえる（その先 START_FLAT_BLEND_M でもとの高さへ、横は道の中心から 30m まで、外 25m でなめらかに）。
        /// スタートの目の前の砂丘の背が先の景色と目印を隠さない（art-r3-11）。
        /// </summary>
        public static void FlattenStart(float[,] h, float sizeM, float heightM, Vector3[] local)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            float s0 = StageDefinitions.START_ARC_M;
            Vector3 p0 = WorldLayout.PointAtArc(local, s0);
            float target = WorldLayout.SampleHeight(h, sizeM, heightM, p0.x, p0.z) / heightM;
            float end = s0 + START_FLAT_M + START_FLAT_BLEND_M;
            var cum = new float[local.Length];
            for (int i = 1; i < local.Length; i++) cum[i] = cum[i - 1] + WorldLayout.DistXZ(local[i - 1], local[i]);
            float reach = START_FLAT_SIDE_M + START_FLAT_SIDE_BLEND_M;
            int rr = Mathf.CeilToInt(reach / cell) + 1;
            var wmax = new float[n, n];
            for (float a = 0f; a <= end; a += 1f)
            {
                float along = a < s0 + START_FLAT_M ? 1f : 1f - Smooth01((a - s0 - START_FLAT_M) / START_FLAT_BLEND_M);
                if (along <= 0f) continue;
                Vector3 c = WorldLayout.PointAtArc(local, a);
                int cx = Mathf.RoundToInt(c.x / cell), cz = Mathf.RoundToInt(c.z / cell);
                for (int z = Mathf.Max(0, cz - rr); z <= Mathf.Min(n - 1, cz + rr); z++)
                    for (int x = Mathf.Max(0, cx - rr); x <= Mathf.Min(n - 1, cx + rr); x++)
                    {
                        float d = Mathf.Sqrt((x * cell - c.x) * (x * cell - c.x) + (z * cell - c.z) * (z * cell - c.z));
                        float wgt = along * (1f - Smooth01((d - START_FLAT_SIDE_M) / START_FLAT_SIDE_BLEND_M));
                        if (wgt > wmax[z, x]) wmax[z, x] = wgt;
                    }
            }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (wmax[z, x] > 0f) h[z, x] = Mathf.Lerp(h[z, x], target, wmax[z, x]);
        }

        /// <summary>
        /// 砂漠の踏切（course-r2-04）：序盤の見どころ（StageDef.openingFraction）を中心に、道に沿って線路 RAIL_RUNS 本（各 RAIL_LENGTH m）を端と端でつなぐ。
        /// 道の中心から右へ RAIL_LATERAL m（道の内側）。各 [a, b]（Terrain ローカル、y = 0）。砂漠以外・序盤の見どころが無いときは空。
        /// 路盤（幅 2·RAIL_BED_HALF）は固い地面（深い砂の中の速い細い線）。
        /// </summary>
        public static System.Collections.Generic.List<Vector3[]> RailRuns(StageDef def, Vector3[] local, float goalArc)
        {
            var runs = new System.Collections.Generic.List<Vector3[]>();
            if (def.kind != HeightmapGen.KIND_DUNES || def.openingFraction < 0f || local == null || local.Length < 2) return runs;
            float mid = OpeningArc(def, goalArc);
            float a = mid - RAIL_LENGTH * RAIL_RUNS * 0.5f;
            for (int k = 0; k < RAIL_RUNS; k++, a += RAIL_LENGTH)
            {
                Vector3 p0 = RailPoint(local, a), p1 = RailPoint(local, a + RAIL_LENGTH);
                // 弦の長さを線路の長さに合わせる（道がわずかに曲がっていても線路は 95.6m）
                Vector3 d = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z).normalized;
                runs.Add(new[] { p0, p0 + d * RAIL_LENGTH });
            }
            return runs;
        }

        static Vector3 RailPoint(Vector3[] local, float arc)
        {
            Vector3 p = WorldLayout.PointAtArc(local, arc) + WorldLayout.RightOf(WorldLayout.TangentAtArc(local, arc)) * RAIL_LATERAL;
            return new Vector3(p.x, 0f, p.z);
        }

        /// <summary>点 p が線路の路盤の上か（固い地面。深い砂の中でも馬が速い）。</summary>
        public static bool OnRailBed(System.Collections.Generic.List<Vector3[]> runs, Vector3 p)
        {
            foreach (var r in runs) if (WorldLayout.DistToSegmentXZ(p, r[0], r[1]) <= RAIL_BED_HALF) return true;
            return false;
        }

        /// <summary>序盤の見どころの中心の弧長（m）。無ければ −1。</summary>
        public static float OpeningArc(StageDef def, float goalArc) => def.openingFraction < 0f ? -1f : def.openingFraction * goalArc;

        /// <summary>廃線の路盤：a〜b の両端の高さを結ぶ直線に、幅 ±RAIL_BED_HALF をならす（RAIL_BED_BLEND で元へ）。</summary>
        public static void FlattenBed(float[,] h, float sizeM, float heightM, Vector3 a, Vector3 b)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            float ya = WorldLayout.SampleHeight(h, sizeM, heightM, a.x, a.z), yb = WorldLayout.SampleHeight(h, sizeM, heightM, b.x, b.z);
            Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float L = ab.magnitude;
            if (L < 1e-3f) return;
            Vector3 t = ab / L;
            float reach = RAIL_BED_HALF + RAIL_BED_BLEND;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - a.x, dz = z * cell - a.z;
                    float along = dx * t.x + dz * t.z;
                    if (along < -RAIL_BED_BLEND || along > L + RAIL_BED_BLEND) continue;
                    float lat = Mathf.Abs(dx * t.z - dz * t.x);
                    if (lat >= reach) continue;
                    float wl = lat <= RAIL_BED_HALF ? 1f : 1f - (lat - RAIL_BED_HALF) / RAIL_BED_BLEND;
                    float wa = along < 0f ? 1f + along / RAIL_BED_BLEND : (along > L ? 1f - (along - L) / RAIL_BED_BLEND : 1f);
                    float target = Mathf.Lerp(ya, yb, Mathf.Clamp01(along / L)) / heightM;
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, Mathf.Clamp01(wl * wa)));
                }
        }

        // ------------------------------------------------------------------
        // 崖下りの近道
        // ------------------------------------------------------------------


        public sealed class ShortcutPlan
        {
            public int cpInterval;      // CP[cpInterval] → CP[cpInterval+span]（0 始まりの配列添字）
            public int span;            // 渡る CP 区間の数
            public int[] crossCps;      // 途中の CP の配列添字。それぞれ弦の上に同じ番号の双子の CP を置く
            public float[] twinFractions;// 双子の CP の弦の上の位置（0..1、順番どおり）
            public float startArc, endArc;
            public Vector3 a, b;        // 弦の両端（Terrain ローカル、y = 地面の高さ m）
            public float length;
            public float f0;            // 本コースの平らな道を出る位置（0..1）
            public float rampEnd;       // 坂を上りきる位置
            public float fDrop;         // 段（崖）の位置
            public float drop;          // 段の高さ（m）
            public float maxDevFromCourse;
            public float maxDevFromCpChord;   // MountController のコース外判定（直前の CP → 次の CP の線分）からの最大距離
            public float saved;         // 得（m）= gainSeconds × 18（疾走で何 m 前に出るか）
            public float gainSeconds;   // 疾走で本コースより何秒速いか（ShortcutGainSeconds）
            public float ratio;         // 本コースの長さ ÷ 弦
        }

        /// <summary>
        /// 弦 L（m）を速さ speed で走る時間（秒）：平らな所＋上りの坂 rampLen（12°、×(1 − 12·0.015)）＋着地の後の砂 sandLen（×HORSE_SAND_FACTOR）＋着地の硬直。
        /// </summary>
        public static float ShortcutSeconds(float L, float rampLen, float drop, float sandLen, float speed)
        {
            float flat = Mathf.Max(0f, L - rampLen - sandLen);
            float ramp = speed * (1f - SHORTCUT_RAMP_DEG * SHORTCUT_UPHILL_PER_DEG);
            float sand = speed * MountMath.HORSE_SAND_FACTOR;
            return flat / speed + rampLen / ramp + sandLen / sand + MountMath.LandingStun(drop);
        }

        /// <summary>
        /// 近道の得（秒、course-r2-02）：本コース courseLen（m）を疾走で走る時間 − 近道を疾走で走る時間。
        /// 以前の得点（m）は砂と坂を数えておらず、表示の「得 37m」は実際には損だった。
        /// </summary>
        public static float ShortcutGainSeconds(float courseLen, float L, float rampLen, float drop, float sandLen)
            => courseLen / SHORTCUT_SPEED - ShortcutSeconds(L, rampLen, drop, sandLen, SHORTCUT_SPEED);

        /// <summary>同じ近道を駈歩で走ると、本コースを疾走で走るより遅いか（全力を残している人だけが得をする）。</summary>
        public static bool ShortcutLosesAtCanter(float courseLen, float L, float rampLen, float drop, float sandLen)
            => ShortcutSeconds(L, rampLen, drop, sandLen, SHORTCUT_CANTER_SPEED) > courseLen / SHORTCUT_SPEED;

        /// <summary>近道の上りの坂の長さ（m）。</summary>
        public static float ShortcutRampLen(float drop) => drop / Mathf.Tan(SHORTCUT_RAMP_DEG * Mathf.Deg2Rad);

        /// <summary>
        /// 近道の計画。courseH は Terrain ローカルの点列（y = コースの高さ m）、cpArcs は CP の弧長。
        /// CP SHORTCUT_MIN_SPAN〜MAX_SPAN 区間ごとに弦（両端から END_INSET 内側）を引き、疾走での得（秒）が一番大きい候補を選ぶ。
        /// 得が SHORTCUT_MIN_GAIN_S〜MAX_GAIN_S の外・駈歩でも得をする・CP の線分から SHORTCUT_MAX_CP_DEV より離れるものは使わない。
        /// avoid は使わない弧長の区間の組（[a0,a1, a2,a3, ...]：橋・踏み切りなど）。fromArc〜toArc の外とゴールの区間も使わない。無ければ null。
        /// </summary>
        public static ShortcutPlan PlanShortcut(Vector3[] courseH, float[] cpArcs, float[] avoid, float fromArc = 0f, float toArc = float.MaxValue)
        {
            if (courseH == null || cpArcs == null || cpArcs.Length < SHORTCUT_MIN_SPAN + 2) return null;
            ShortcutPlan best = null;
            float rampLen = ShortcutRampLen(SHORTCUT_DROP);
            for (int N = SHORTCUT_MIN_SPAN; N <= SHORTCUT_MAX_SPAN; N++)
                for (int i = 0; i + N + 1 < cpArcs.Length; i++)
                {
                    float s0 = cpArcs[i] + END_INSET, s1 = cpArcs[i + N] - END_INSET;
                    if (s0 < fromArc || s1 > toArc) continue;
                    bool hit = false;
                    if (avoid != null)
                        for (int k = 0; k + 1 < avoid.Length; k += 2)
                            if (s1 >= avoid[k] && s0 <= avoid[k + 1]) hit = true;
                    if (hit) continue;
                    Vector3 a = WorldLayout.PointAtArc(courseH, s0), b = WorldLayout.PointAtArc(courseH, s1);
                    float L = WorldLayout.DistXZ(a, b);
                    if (L < 40f) continue;
                    float ratio = (s1 - s0) / L;
                    float gain = ShortcutGainSeconds(s1 - s0, L, rampLen, SHORTCUT_DROP, LANDING_RUN);
                    if (gain < SHORTCUT_MIN_GAIN_S || gain > SHORTCUT_MAX_GAIN_S) continue;
                    if (!ShortcutLosesAtCanter(s1 - s0, L, rampLen, SHORTCUT_DROP, LANDING_RUN)) continue;
                    float score = gain * SHORTCUT_SPEED;
                    if (best != null && score <= best.saved) continue;
                    // 双子の CP：弦の平らな道を出た所（f0）から段の手前まで等間隔
                    int steps = Mathf.CeilToInt(L);
                    float dev = 0f, f0 = -1f, f1 = -1f;
                    for (int k = 0; k <= steps; k++)
                    {
                        float f = k / (float)steps;
                        float d = WorldLayout.DistToPolylineXZ(Vector3.Lerp(a, b, f), courseH);
                        if (d > dev) dev = d;
                        if (d >= RUNWAY_KEEP + 2f) { if (f0 < 0f) f0 = f; f1 = f; }
                    }
                    if (dev < SHORTCUT_MIN_DEV || dev > SHORTCUT_MAX_DEV || f0 < 0f) continue;
                    float fDrop = f1 - LANDING_RUN / L;
                    float rampEnd = f0 + rampLen / L;
                    if (fDrop - rampEnd < 4f / L) continue;
                    var twins = new float[N - 1];
                    for (int k = 1; k < N; k++) twins[k - 1] = Mathf.Lerp(f0, fDrop, k / (float)N);
                    // コース外判定（直前の CP → 次の CP の線分）からの距離：CP[i] → 双子 1 → … → CP[i+N]
                    var chain = new Vector3[N + 1];
                    chain[0] = WorldLayout.PointAtArc(courseH, cpArcs[i]);
                    for (int k = 1; k < N; k++) chain[k] = Vector3.Lerp(a, b, twins[k - 1]);
                    chain[N] = WorldLayout.PointAtArc(courseH, cpArcs[i + N]);
                    float devCp = 0f;
                    int seg = 0;
                    for (int k = 0; k <= steps; k++)
                    {
                        float f = k / (float)steps;
                        while (seg < N - 1 && f > twins[seg]) seg++;
                        float dc = WorldLayout.DistToSegmentXZ(Vector3.Lerp(a, b, f), chain[seg], chain[seg + 1]);
                        if (dc > devCp) devCp = dc;
                    }
                    if (devCp > SHORTCUT_MAX_CP_DEV) continue;
                    var cross = new int[N - 1];
                    for (int k = 1; k < N; k++) cross[k - 1] = i + k;
                    best = new ShortcutPlan
                    {
                        cpInterval = i, span = N, crossCps = cross, twinFractions = twins, startArc = s0, endArc = s1, a = a, b = b, length = L,
                        f0 = f0, rampEnd = rampEnd, fDrop = fDrop, drop = SHORTCUT_DROP,
                        maxDevFromCourse = dev, maxDevFromCpChord = devCp, saved = score, gainSeconds = gain, ratio = ratio,
                    };
                }
            return best;
        }

        /// <summary>近道の高さ（m）：両端の高さを結ぶ直線＋（坂で上がって）段の手前まで drop m 高い。</summary>
        public static float ShortcutHeight(ShortcutPlan p, float f)
        {
            float baseH = Mathf.Lerp(p.a.y, p.b.y, f);
            float bump;
            if (f <= p.f0 || f > p.fDrop) bump = 0f;
            else if (f < p.rampEnd) bump = p.drop * (f - p.f0) / Mathf.Max(1e-4f, p.rampEnd - p.f0);
            else bump = p.drop;
            return baseH + bump;
        }

        /// <summary>近道の通路を高さマップに作る。本コースから RUNWAY_KEEP m 以内のマスは触らない（本コースの勾配は変わらない）。</summary>
        public static void ApplyShortcut(float[,] h, float sizeM, float heightM, ShortcutPlan p, Vector3[] courseLocal)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 ab = new Vector3(p.b.x - p.a.x, 0f, p.b.z - p.a.z);
            float L = ab.magnitude;
            if (L < 1e-3f) return;
            Vector3 t = ab / L;
            float reach = SHORTCUT_HALF_WIDTH + SHORTCUT_BLEND;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.a.x, p.b.x) - reach) / cell));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(p.a.x, p.b.x) + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.a.z, p.b.z) - reach) / cell));
            int z1 = Mathf.Min(n - 1, Mathf.CeilToInt((Mathf.Max(p.a.z, p.b.z) + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - p.a.x, dz = z * cell - p.a.z;
                    float along = dx * t.x + dz * t.z;
                    if (along < 0f || along > L) continue;
                    float lat = Mathf.Abs(dx * t.z - dz * t.x);
                    if (lat >= reach) continue;
                    var cp = new Vector3(x * cell, 0f, z * cell);
                    float dc = WorldLayout.DistToPolylineXZ(cp, courseLocal);
                    float wr = Mathf.Clamp01((dc - RUNWAY_KEEP) / 2f);
                    if (wr <= 0f) continue;
                    float wl = lat <= SHORTCUT_HALF_WIDTH ? 1f : 1f - (lat - SHORTCUT_HALF_WIDTH) / SHORTCUT_BLEND;
                    float target = ShortcutHeight(p, along / L) / heightM;
                    h[z, x] = Mathf.Clamp01(Mathf.Lerp(h[z, x], target, wl * wr));
                }
        }

        // ------------------------------------------------------------------
        // 遺物の場所（course-r1-11：危ない線・遠回りの線に置き、先頭がまっすぐ走るだけでは全部拾えない）
        // ------------------------------------------------------------------

        public const float RELIC_WIDE_SIDE = 10.5f;

        public struct RelicSpot
        {
            public Vector3 local;   // Terrain ローカル（y = 0）
            public float up;        // 地面からの高さ（m、踏み切りの先の遺物は跳んだ人だけが取れる高さ）
            public float arc;       // 一番近いコースの弧長
            public float side;      // コースの中心からの横（m、+ = 右。近道の上は弦までの距離）
        }

        /// <summary>遺物どうしの弧長の最小の間隔（m、course-r2-09）。</summary>
        public const float RELIC_MIN_GAP_M = 350f;
        public const float PASS_RELIC_SIDE = 10.5f;   // 境目：切り通しの中の危ない脇の線（切り通しの壁の際）
        /// <summary>荒野の岩の橋の遺物（course-r3-04）：背の縁（SPINE_EDGE の落ち際）から 1.2m 内側。真ん中ではなく危ない線の上。</summary>
        public const float BRIDGE_RELIC_EDGE_IN = 1.2f;

        /// <summary>岩の橋の遺物の横（+ = 右）：次のカーブの内側の背の縁（内側の線は短いが、落ちる縁の際）。カーブが無ければ左。</summary>
        public static float BridgeRelicSide(Vector3[] local, float bridge, float goalArc)
        {
            int side = -1;
            foreach (var c in StageDefinitions.Corners(local, StageDefinitions.MEDIUM_CORNER_MAX_R, goalArc))
                if (c.fromArc > bridge) { side = c.side; break; }
            return side * (SPINE_HALF - BRIDGE_RELIC_EDGE_IN);
        }

        /// <summary>
        /// 遺物 4 つ（course-r2-09：350m 以上離し、コースの 4 分の 1 ごとに 1 つ）：
        /// 1) 序盤（弧長 300〜600m、最初の 4 分の 1）の最初のカーブの外側（ふくらむ線、±10.5m）。カーブが無ければ 450m の外側の線。
        /// 2) 見どころの危ない線（荒野 = 岩の橋の背の上、砂漠 = 踏み切りの 14m 先の 2.5m の空中、境目 = 切り通しの壁際 ±10.5m）。
        /// 3) 近道の上。4) 最後の下りの入口の内側（−10.5m）。近道が無いときは代わりにコースの外側の線。
        /// 間隔が足りないときは 2) を前後へずらす（危ない線のまま）。
        /// </summary>
        public static RelicSpot[] RelicSpots(StageDef def, Vector3[] local, float goalArc, ShortcutPlan plan)
        {
            var r = new RelicSpot[StageDefinitions.RELICS_PER_STAGE];
            // 1) 序盤のカーブの外側
            float a1 = 450f, s1 = RELIC_WIDE_SIDE;
            var corners = StageDefinitions.Corners(local, StageDefinitions.MEDIUM_CORNER_MAX_R, goalArc);
            float early = Mathf.Min(600f, goalArc * 0.25f - 20f);
            foreach (var c in corners)
                if (c.apexArc >= 300f && c.apexArc <= early) { a1 = c.apexArc; s1 = -c.side * RELIC_WIDE_SIDE; break; }
            r[0] = OnCourse(local, a1, s1, 0f);
            // 2) 見どころの危ない線
            float bridge = BridgeArc(def, goalArc), jump = JumpArc(def, goalArc), pass = PassArc(def, goalArc);
            if (bridge >= 0f) r[1] = OnCourse(local, bridge, BridgeRelicSide(local, bridge, goalArc), 0f);   // course-r3-04：背の縁の際
            else if (jump >= 0f) r[1] = OnCourse(local, jump + JUMP_RELIC_AHEAD, 0f, JUMP_RELIC_UP);
            else if (pass >= 0f) r[1] = OnCourse(local, pass, -PASS_RELIC_SIDE, 0f);
            else r[1] = OnCourse(local, goalArc * 0.45f, -RELIC_WIDE_SIDE, 0f);
            // 4) 最後の下りの入口の内側
            r[3] = OnCourse(local, StageDefinitions.DashArc(def, goalArc), -RELIC_WIDE_SIDE, 0f);
            // 3) 近道の上（無ければ 62% の外側の線）。弦の上で前後の遺物から RELIC_MIN_GAP_M 離れる位置を選ぶ
            if (plan != null)
            {
                float[] fs = { 0.5f, 0.65f, 0.35f, 0.8f, 0.2f, 0.95f, 0.05f };
                RelicSpot best = OnChord(local, plan, Mathf.Lerp(plan.f0, plan.fDrop, fs[0]));
                float bestGap = -1f;
                foreach (float f in fs)
                {
                    var c = OnChord(local, plan, Mathf.Lerp(plan.f0, plan.fDrop, f));
                    float gap = Mathf.Min(Mathf.Min(Mathf.Abs(c.arc - r[0].arc), Mathf.Abs(c.arc - r[1].arc)), Mathf.Abs(c.arc - r[3].arc));
                    if (gap >= RELIC_MIN_GAP_M) { best = c; break; }
                    if (gap > bestGap) { bestGap = gap; best = c; }
                }
                r[2] = best;
            }
            else r[2] = OnCourse(local, goalArc * 0.62f, RELIC_WIDE_SIDE, 0f);
            return r;
        }

        /// <summary>弧長の順に並べた遺物の間隔の最小（m）。</summary>
        public static float MinRelicGap(RelicSpot[] spots)
        {
            var a = new float[spots.Length];
            for (int i = 0; i < a.Length; i++) a[i] = spots[i].arc;
            System.Array.Sort(a);
            float best = float.MaxValue;
            for (int i = 1; i < a.Length; i++) best = Mathf.Min(best, a[i] - a[i - 1]);
            return best;
        }

        static RelicSpot OnCourse(Vector3[] local, float arc, float side, float up)
        {
            Vector3 t = WorldLayout.TangentAtArc(local, arc);
            Vector3 p = WorldLayout.PointAtArc(local, arc) + WorldLayout.RightOf(t) * side;
            return new RelicSpot { local = new Vector3(p.x, 0f, p.z), up = up, arc = arc, side = side };
        }

        static RelicSpot OnChord(Vector3[] local, ShortcutPlan plan, float f)
        {
            Vector3 p = Vector3.Lerp(plan.a, plan.b, f);
            p.y = 0f;
            float[] cum = DressingPlan.CourseArcs(local);
            float d;
            float arc = DressingPlan.NearestArc(local, cum, p, out d);
            return new RelicSpot { local = p, up = 0f, arc = arc, side = d };
        }

        /// <summary>弦に沿って高さマップを stepM ごとに読み、最大の下り段差（m、stepM 間の差）を返す。</summary>
        public static float MaxDropAlong(float[,] h, float sizeM, float heightM, Vector3 a, Vector3 b, float stepM)
        {
            float L = WorldLayout.DistXZ(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(L / stepM));
            float prev = WorldLayout.SampleHeight(h, sizeM, heightM, a.x, a.z), max = 0f;
            for (int k = 1; k <= steps; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
                float y = WorldLayout.SampleHeight(h, sizeM, heightM, p.x, p.z);
                if (prev - y > max) max = prev - y;
                prev = y;
            }
            return max;
        }
    }
}
