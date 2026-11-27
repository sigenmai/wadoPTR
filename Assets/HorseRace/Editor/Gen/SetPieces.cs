using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// ステージの見どころ（spec 5.5）の純粋な幾何（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。
    ///   岩の橋（荒野、course-r1-07）：コースを横切る溝（深さ 12m）を高さマップに掘るが、道の中心 ±SPINE_HALF（7m）は掘らずに残す
    ///         ＝ 幅 14m の地形の背（岩の橋）。溝の両端（道から DETOUR_FROM 38m より外）も掘らず、平らにならした遠回り（約 40m 長い安全な道）にする。
    ///         作った板・手すりは無い（すべて地形）。溝の底には落とし穴（ResetVolume → 最後の CP へ戻す）。
    ///   近道（course-r1-06）：CP 2〜6 区間ぶんのコースの曲がりを弦で切る枝道。途中に高さ SHORTCUT_DROP の段（崖）があり、着地の硬直がある。
    ///         得点 = 本コースの長さ − 弦 − 18·着地の硬直 − 坂の分（0.06·弦）。SHORTCUT_MIN_SAVED_M（30m）未満は使わない。床は砂（SandZone）。
    ///         途中の CP には弦の上に「双子の CP」（同じ番号の Checkpoint）を置く：どちらの道でも CP を順番どおりに通る。
    ///   踏み切り（砂漠、course-r1-08）：道幅いっぱいの砂の盛り上がり（22m で +5m、13°）、中央 ±7m だけ 4m で 6m 落ちる縁、
    ///         その先 16m は砂の窪み（SandZone）。跳べば越えられ、横の浅い坂を転がる人は砂を通る。
    ///   台地（境目、course-r1-05）：道の高さの形で 32m 登る区間の両脇 35m の帯も同じ高さへ持ち上げる（縁から 15m で元の地形へ落ちる崖）。
    /// 座標は Terrain ローカル（m）。高さマップは [z,x]、0..1（× heightM）。
    /// </summary>
    public static class SetPieces
    {
        // 岩橋
        public const float TRENCH_HALF_WIDTH = 7f;     // コース方向の溝の半幅（底が平らな部分）
        public const float TRENCH_BLEND = 3f;          // 溝の縁の傾斜
        public const float TRENCH_HALF_LENGTH = 45f;   // コースを横切る方向の半分の長さ
        public const float TRENCH_END_TAPER = 12f;     // 溝の両端で浅くなる長さ
        public const float TRENCH_DEPTH = 12f;
        public const float BRIDGE_HALF_SPAN = 11f;     // 岩の背の勾配の検査から外す半分の長さ（溝の縁 10m より長い）
        public const float SPINE_HALF = 7f;            // 掘らずに残す地形の背（岩の橋）の半幅（絞り 14m）
        public const float SPINE_EDGE = 2f;            // 背の縁の切り立った斜面
        public const float DETOUR_FROM = 38f;          // ここから外は掘らない（溝の両端を回る遠回り）
        public const float DETOUR_TO = 54f;            // 遠回りの外の縁（平らにならす範囲）
        public const float DETOUR_ALONG = 30f;         // 遠回りのならしのコース方向の半分の長さ
        public const float DETOUR_BARRIER_M = 58f;     // 橋の周りの柵（遠回りを囲む）

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
        public const float PASS_HALF_CUT = 15f;        // 深さいっぱいの区間の半分（全長 30m）
        public const float PASS_RAMP = 26f;            // 前後の下り・上り（6m / 26m、最大 20° 未満）
        public const float PASS_ROAD_HALF = 13f;       // ここまで（道 ±12m ＋余白）は下げる
        public const float PASS_WALL_RISE = 22f;       // 両脇（道から 22〜40m）の盛り上げ
        public const float PASS_WALL_FROM = 22f, PASS_WALL_TO = 40f, PASS_WALL_FADE = 30f;

        // 砂漠の廃線（WesternTown_049_Track、長さ 95.6m）
        public const float RAIL_LENGTH = 95.6f;
        public const float RAIL_LATERAL = 35f;         // コースから（30〜40m）
        public const int RAIL_RUNS = 3;
        public const float RAIL_BED_HALF = 3.5f, RAIL_BED_BLEND = 4f;

        // 崖下りの近道
        public const float SHORTCUT_DROP = 6f;
        public const float MAX_DROP = 8f;
        public const float SHORTCUT_HALF_WIDTH = 4f;
        public const float SHORTCUT_BLEND = 4f;
        public const float SHORTCUT_RAMP_DEG = 12f;
        public const float RUNWAY_KEEP = 14f;          // 本コースの平らな道（±12m）＋余白は触らない
        public const float SHORTCUT_MIN_DEV = 24f;     // 弦がコースから最低これだけ離れる区間を選ぶ
        public const float SHORTCUT_MAX_DEV = 90f;     // 弦の真ん中が本コースからこれ以上離れない（柵は弦にも沿う）
        public const float SHORTCUT_MAX_CP_DEV = 50f;  // コース外リセット（CP の線分から 60m）より十分内側
        public const float SHORTCUT_MIN_SAVED_M = 30f; // 18 m/s で約 1.7 秒
        public const float SHORTCUT_MIN_RATIO = 1.12f, SHORTCUT_MAX_RATIO = 1.25f;  // 本コースの長さ ÷ 弦：全力でだけ得をする（並足では負ける）
        public const float SHORTCUT_SPEED = 18f, SHORTCUT_RAMP_COST = 0.06f;
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
                CarvePass(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, WorldLayout.PointAtArc(local, pass), WorldLayout.TangentAtArc(local, pass));
            foreach (var run in RailRuns(def, local, goalArc))
                FlattenBed(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, run[0], run[1]);
            return plan;
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
        /// 切り通しの高さの変化（m、地形に足す）。道（|lat| ≤ PASS_ROAD_HALF）は PASS_DEPTH 下げ（前後 PASS_RAMP でなめらかに）、
        /// 両脇（PASS_WALL_FROM〜PASS_WALL_TO）は PASS_WALL_RISE 盛り上げて、遠くから三角の切れ込みに見える壁にする。
        /// </summary>
        public static float PassDelta(float along, float lat)
        {
            float al = Mathf.Abs(along), la = Mathf.Abs(lat);
            float aw = 1f - Smooth01((al - PASS_HALF_CUT) / PASS_RAMP);
            float awWall = 1f - Smooth01((al - PASS_HALF_CUT - 10f) / 30f);
            float lw = Smooth01((la - PASS_ROAD_HALF) / (PASS_WALL_FROM - PASS_ROAD_HALF));
            float outer = 1f - Smooth01((la - PASS_WALL_TO) / PASS_WALL_FADE);
            return -PASS_DEPTH * aw * (1f - lw) + PASS_WALL_RISE * awWall * lw * outer;
        }

        public static void CarvePass(float[,] h, float sizeM, float heightM, Vector3 center, Vector3 tangent)
        {
            int n = h.GetLength(0);
            float cell = sizeM / (n - 1);
            Vector3 t = new Vector3(tangent.x, 0f, tangent.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float reach = PASS_HALF_CUT + 40f + PASS_WALL_TO + PASS_WALL_FADE;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach) / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.x + reach) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach) / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt((center.z + reach) / cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x * cell - center.x, dz = z * cell - center.z;
                    float d = PassDelta(dx * t.x + dz * t.z, dx * r.x + dz * r.z);
                    if (d != 0f) h[z, x] = Mathf.Clamp01(h[z, x] + d / heightM);
                }
        }

        /// <summary>
        /// 砂漠の廃線（コースと平行、横 RAIL_LATERAL m）：コースが RAIL_LENGTH m ほぼまっすぐな所を RAIL_RUNS か所（300m 以上離して）。
        /// 各 [a, b]（Terrain ローカル、y = 0）。コースの全点から 30m 以上・Terrain の端から 80m 以上離れる。砂漠以外は空。
        /// </summary>
        public static System.Collections.Generic.List<Vector3[]> RailRuns(StageDef def, Vector3[] local, float goalArc)
        {
            var runs = new System.Collections.Generic.List<Vector3[]>();
            if (def.kind != HeightmapGen.KIND_DUNES || local == null || local.Length < 2) return runs;
            var used = new System.Collections.Generic.List<float>();
            float half = RAIL_LENGTH * 0.5f;
            int side = 1;
            for (float a = 160f; a < goalArc - 160f && runs.Count < RAIL_RUNS; a += 10f)
            {
                bool far = true;
                foreach (float u in used) if (Mathf.Abs(u - a) < 300f) far = false;
                if (!far) continue;
                Vector3 p0 = WorldLayout.PointAtArc(local, a - half), p1 = WorldLayout.PointAtArc(local, a + half);
                // ほぼまっすぐ：途中の点が弦から 3m 以内
                bool straight = true;
                for (float k = a - half; k <= a + half && straight; k += 8f)
                    if (WorldLayout.DistToSegmentXZ(WorldLayout.PointAtArc(local, k), p0, p1) > 3f) straight = false;
                if (!straight) continue;
                for (int tryS = 0; tryS < 2; tryS++, side = -side)
                {
                    Vector3 t = new Vector3(p1.x - p0.x, 0f, p1.z - p0.z).normalized;
                    Vector3 r = WorldLayout.RightOf(t) * (side * RAIL_LATERAL);
                    Vector3 a0 = p0 + r, b0 = p1 + r;
                    bool ok = true;
                    for (int k = 0; k <= 12 && ok; k++)
                    {
                        Vector3 q = Vector3.Lerp(a0, b0, k / 12f);
                        if (q.x < 80f || q.z < 80f || q.x > StageDefinitions.SIZE - 80f || q.z > StageDefinitions.SIZE - 80f) ok = false;
                        else if (WorldLayout.DistToPolylineXZ(q, local) < 30f) ok = false;
                    }
                    if (!ok) continue;
                    runs.Add(new[] { new Vector3(a0.x, 0f, a0.z), new Vector3(b0.x, 0f, b0.z) });
                    used.Add(a);
                    side = -side;
                    break;
                }
            }
            return runs;
        }

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
            public float saved;         // 得点（m）= 本コースの長さ − 弦 − 18·着地の硬直 − 0.06·弦
            public float ratio;         // 本コースの長さ ÷ 弦
        }

        /// <summary>近道の得点（m）：本コースの長さ courseLen と弦 L、段の高さ drop から。</summary>
        public static float ShortcutScore(float courseLen, float L, float drop)
            => courseLen - L - SHORTCUT_SPEED * MountMath.LandingStun(drop) - SHORTCUT_RAMP_COST * L;

        /// <summary>
        /// 近道の計画。courseH は Terrain ローカルの点列（y = コースの高さ m）、cpArcs は CP の弧長。
        /// CP SHORTCUT_MIN_SPAN〜MAX_SPAN 区間ごとに弦（両端から END_INSET 内側）を引き、得点（ShortcutScore）が一番高い候補を選ぶ。
        /// 得点 SHORTCUT_MIN_SAVED_M 未満・長さの比が SHORTCUT_MIN_RATIO〜MAX_RATIO の外・CP の線分から SHORTCUT_MAX_CP_DEV より離れるものは使わない。
        /// avoid は使わない弧長の区間の組（[a0,a1, a2,a3, ...]：橋・踏み切りなど）。fromArc〜toArc の外とゴールの区間も使わない。無ければ null。
        /// </summary>
        public static ShortcutPlan PlanShortcut(Vector3[] courseH, float[] cpArcs, float[] avoid, float fromArc = 0f, float toArc = float.MaxValue)
        {
            if (courseH == null || cpArcs == null || cpArcs.Length < SHORTCUT_MIN_SPAN + 2) return null;
            ShortcutPlan best = null;
            float rampLen = SHORTCUT_DROP / Mathf.Tan(SHORTCUT_RAMP_DEG * Mathf.Deg2Rad);
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
                    if (ratio < SHORTCUT_MIN_RATIO || ratio > SHORTCUT_MAX_RATIO) continue;
                    float score = ShortcutScore(s1 - s0, L, SHORTCUT_DROP);
                    if (score < SHORTCUT_MIN_SAVED_M || (best != null && score <= best.saved)) continue;
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
                        maxDevFromCourse = dev, maxDevFromCpChord = devCp, saved = score, ratio = ratio,
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

        /// <summary>
        /// 遺物 4 つ：1) 最初の急カーブの外側（ふくらむ線、±10.5m）、2) 見どころの危ない線（荒野 = 岩の橋の背の上、砂漠 = 踏み切りの 14m 先の
        /// 2.5m の空中、境目 = 近道の崖の下の床）、3) 近道の上、4) 最後の下りの入口の内側（−10.5m）。近道が無いときは代わりにコースの外側の線。
        /// </summary>
        public static RelicSpot[] RelicSpots(StageDef def, Vector3[] local, float goalArc, ShortcutPlan plan)
        {
            var r = new RelicSpot[StageDefinitions.RELICS_PER_STAGE];
            var corners = StageDefinitions.TightCorners(def, local);
            float a1 = corners.Count > 0 ? corners[0].apexArc : goalArc * 0.2f;
            float s1 = corners.Count > 0 ? -corners[0].side * RELIC_WIDE_SIDE : RELIC_WIDE_SIDE;
            r[0] = OnCourse(local, a1, s1, 0f);
            float bridge = BridgeArc(def, goalArc), jump = JumpArc(def, goalArc);
            if (bridge >= 0f) r[1] = OnCourse(local, bridge, 0f, 0f);
            else if (jump >= 0f) r[1] = OnCourse(local, jump + JUMP_RELIC_AHEAD, 0f, JUMP_RELIC_UP);
            else if (plan != null) r[1] = OnChord(local, plan, Mathf.Lerp(plan.fDrop, 1f, 0.35f));
            else r[1] = OnCourse(local, goalArc * 0.45f, -RELIC_WIDE_SIDE, 0f);
            r[2] = plan != null ? OnChord(local, plan, Mathf.Lerp(plan.f0, plan.fDrop, bridge < 0f && jump < 0f ? 0.4f : 0.5f))
                : OnCourse(local, goalArc * 0.62f, RELIC_WIDE_SIDE, 0f);
            r[3] = OnCourse(local, StageDefinitions.DashArc(goalArc), -RELIC_WIDE_SIDE, 0f);
            return r;
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
