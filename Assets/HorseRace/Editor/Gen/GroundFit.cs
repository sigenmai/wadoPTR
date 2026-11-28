using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// art-r7-01：斜面と接地（オーナーの指摘「アセットが地面の傾斜に対しておかしいところがある」）。UnityEditor に依存しない。
    /// 置き物を 3 種類に分けて地面に合わせ（Fit）、合っているかを調べる（Audit）：
    ///   HUG（地面に沿う：岩・小石・草・骨・干し草・木箱・樽・寝かせた丸太の山・焚き火）：足元の地面の傾き（面の当てはめ）へ傾ける。
    ///       28° までは全部、その先は半分（最大 40°、高さ 4m 以上の大岩は 25° まで）。傾けた底の面のいちばん地面に近い所で接し、決まりの量だけ沈める。
    ///   PLUMB（まっすぐ立つ：杭・灯り・木・サボテン・建物・看板・塔・メサ・崖の板）：傾けない。底の面（向きのある長方形、木・サボテンは幹）の
    ///       いちばん低い所で接して決まりの量だけ沈める（メサ・崖の板・縦に伸ばした岩・杭・建物・塔は浮いていれば下げるだけ）。
    ///   置き直しても見えないほど埋もれる物（崖の縁・細い尾根）は上り側へ少しずらし、それでもだめなら計画から外す。
    ///   LINEAR（線のもの：柵の板・寝かせた 1 本の丸太・板）：長い辺に沿って両端の地面の差だけ縦に傾ける（最大 35°、横には傾けない）。
    /// 足元は部品のメッシュの外形（PartBounds、無ければ SizeOf の円）を向き・拡大込みで回した底の面の点（角・辺の中・中心）。
    /// 回転は Unity と同じ：FromToRotation(Vector3.up, p.up) × Euler(0, yaw, roll)。
    /// </summary>
    public static class GroundFit
    {
        public const int SKIP = 0, HUG = 1, PLUMB = 2, LINEAR = 3;
        public const float FULL_TILT_DEG = 28f, MAX_TILT_DEG = 40f, PARTIAL_TILT_K = 0.5f;
        public const float BIG_ROCK_H = 4f, BIG_ROCK_MAX_TILT_DEG = 25f;
        public const float LINEAR_MAX_PITCH_DEG = 35f;
        public const float CONTACT_SINK_M = 0.05f;          // まっすぐ立てる物：浮いた分 + これだけ下げる
        public const float HUG_MIN_SINK_M = 0.1f;           // 沿わせる物・線の物：接する所から最低これだけ沈める（前の GROUND_EXTRA_SINK と同じ）
        public const float MAX_SINK_FRAC = 0.6f;            // 決まりの沈め量の上限（高さの 60%）
        public const float FLOAT_TOL_M = DressingPlan.GROUNDING_FLOAT_TOL_M;
        public const float STEEP_DEG = 15f, PLUMB_TILT_K = 0.5f, PLUMB_SLOPE_CAP_DEG = 50f;   // 検査：15° より急な地面で、傾きが地面の傾きの半分未満なら「急な斜面にまっすぐ立つ」
        public const float BURIED_FRAC = 0.85f;             // 検査：底の面のどこでも高さの 85% より深く埋まる → 見えない
        public const float POKE_FRAC = 0.8f;                // 検査：線の物の端が厚さの 80% より深く埋まる → 斜面に刺さる
        public const float ROCK_FOOT = 0.6f, BOX_FOOT = 0.9f, LINE_FOOT = 0.95f;   // 底の面の大きさ（外形に対する割合：岩は丸い）
        public const float SLOPE_PROBE_MIN_M = 0.75f;

        public delegate float Ground(float x, float z);

        // ------------------------------------------------------------------
        // 分類
        // ------------------------------------------------------------------

        public static bool IsRockAsset(string a)
            => a == DressingPlan.DROCKS || a == DressingPlan.RCOLL || a == DressingPlan.RSTYL || a.StartsWith("ThirdParty/RockGray/");

        static int Num(string part)
        {
            int i = part.LastIndexOf('_');
            int n;
            return i >= 0 && int.TryParse(part.Substring(i + 1), out n) ? n : -1;
        }

        static bool IsCactus(Placement p)
        {
            if (p.asset == DressingPlan.CACTI || p.asset == DressingPlan.CGROUP) return true;
            if (p.asset == DressingPlan.DPROPS && p.part != null) { int n = Num(p.part); return n == 20 || n == 24 || n >= 27 && n <= 30; }
            return p.asset == DressingPlan.WTOWN && p.part != null && DressingPlan.IsTownCactus(p.part);
        }

        static bool IsPost(Placement p) => p.part == DressingPlan.POST_PART && Mathf.Abs(p.rollDeg - 90f) < 1f;

        public static int KindOf(Placement p)
        {
            if (p.keepY || p.asset == null) return SKIP;
            string a = p.asset, part = p.part ?? "";
            // 背景：メサ・崖の板・建物は遠くの形のまま。背景の岩（尾根の頂・外周の輪の岩）は沿わせる（art-r7-01：急な壁に埋めた大岩の先だけが宙に出ていた）
            if (p.background && !(IsRockAsset(a) || part.EndsWith("_Rock"))) return SKIP;
            if (a == DressingPlan.GEMS || a.Contains("WantedPosters")) return SKIP;
            if (part.EndsWith("_Hill") || part.EndsWith("_Ground")) return SKIP;
            if (a == DressingPlan.CLIFFS || a == DressingPlan.MESA || a == DressingPlan.MESAB) return PLUMB;
            if (p.group == "CornerWall") return PLUMB;   // 急カーブの外の干し草の壁（2 段に積む）：壁はまっすぐ、浮かないよう下げるだけ
            if (IsPost(p)) return PLUMB;
            if (p.rollDeg != 0f && a != DressingPlan.SKELETON) return PLUMB;   // 横倒しの物（骨の馬だけは沿わせる）
            if (DressingPlan.IsFencePart(part) || part == DressingPlan.RAIL_TRACK) return LINEAR;
            if (a == DressingPlan.LOGS) return Elongation(p) >= 1.4f ? LINEAR : HUG;
            if (IsCactus(p) || DressingPlan.IsTree(p)) return PLUMB;
            if (IsRockAsset(a)) return p.scaleY > 1.01f ? PLUMB : HUG;   // 縦に伸ばした岩（崖の代わり）は柱：傾けない
            if (a == DressingPlan.DPROPS || a == DressingPlan.HAY || a == DressingPlan.CAMPFIRE || a == DressingPlan.SKELETON) return HUG;
            if (a == DressingPlan.FARM) return part == "FarmProps_02" ? PLUMB : HUG;   // 街灯はまっすぐ。木箱・樽・机・小物は沿わせる
            if (a == DressingPlan.WTOWN)
            {
                if (part.EndsWith("_Rock")) return HUG;
                if (part.EndsWith("_Prop") && part != DressingPlan.TOWN_SIGN) return HUG;
                return PLUMB;
            }
            return PLUMB;
        }

        /// <summary>外形の長い辺 ÷ 短い辺（拡大込み、水平）。</summary>
        public static float Elongation(Placement p)
        {
            Vector3 mn, mx;
            LocalBox(p, out mn, out mx);
            float ex = mx.x, ez = mx.z;
            return Mathf.Max(ex, ez) / Mathf.Max(1e-3f, Mathf.Min(ex, ez));
        }

        // ------------------------------------------------------------------
        // 形と回転
        // ------------------------------------------------------------------

        /// <summary>拡大込みのローカルの外形（x・z は ±、y は底〜上）。PartBounds に無ければ SizeOf の円に内接する正方形。</summary>
        public static bool LocalBox(Placement p, out Vector3 mn, out Vector3 mx)
        {
            PartBounds.Box b;
            float s = Mathf.Max(0.01f, p.scale);
            if (p.asset != null && PartBounds.TryGet(p.asset, p.part, out b))
            {
                mn = new Vector3(-b.hx * s, b.y0 * s * p.scaleY, -b.hz * s * p.scaleZ);
                mx = new Vector3(b.hx * s, b.y1 * s * p.scaleY, b.hz * s * p.scaleZ);
                return true;
            }
            float r = Mathf.Max(0.05f, p.radius * 0.75f);
            mn = new Vector3(-r, 0f, -r);
            mx = new Vector3(r, Mathf.Max(0.05f, p.height), r);
            return false;
        }

        /// <summary>Unity の Quaternion.FromToRotation(up, p.up) × Euler(0, yaw, roll) でローカルの向き v を回す。</summary>
        public static Vector3 Rotate(Placement p, Vector3 v) => Rotate(v, p.yawDeg, p.rollDeg, p.up);

        public static Vector3 Rotate(Vector3 v, float yawDeg, float rollDeg, Vector3 up)
        {
            if (rollDeg != 0f)
            {
                float c = Mathf.Cos(rollDeg * Mathf.Deg2Rad), s = Mathf.Sin(rollDeg * Mathf.Deg2Rad);
                v = new Vector3(v.x * c - v.y * s, v.x * s + v.y * c, v.z);
            }
            if (yawDeg != 0f)
            {
                float c = Mathf.Cos(yawDeg * Mathf.Deg2Rad), s = Mathf.Sin(yawDeg * Mathf.Deg2Rad);
                v = new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
            }
            return Tilt(v, up);
        }

        /// <summary>真上を up へ回す最小の回転（Quaternion.FromToRotation(Vector3.up, up)）で v を回す。</summary>
        public static Vector3 Tilt(Vector3 v, Vector3 up)
        {
            float hl = Mathf.Sqrt(up.x * up.x + up.z * up.z);
            if (hl < 1e-6f) return up.y >= 0f ? v : new Vector3(v.x, -v.y, -v.z);
            float m = up.magnitude, cos = up.y / m, sin = hl / m;
            Vector3 k = new Vector3(up.z / hl, 0f, -up.x / hl);   // cross(Y, up) の向き
            Vector3 kxv = new Vector3(k.y * v.z - k.z * v.y, k.z * v.x - k.x * v.z, k.x * v.y - k.y * v.x);
            float kd = Vector3.Dot(k, v);
            return v * cos + kxv * sin + k * (kd * (1f - cos));
        }

        /// <summary>傾きの角度（°、真上から）。</summary>
        public static float TiltDeg(Vector3 up) => Mathf.Acos(Mathf.Clamp(up.y / Mathf.Max(1e-6f, up.magnitude), -1f, 1f)) * Mathf.Rad2Deg;

        /// <summary>地面の傾き θ（°）に対して沿わせる角度：FULL_TILT_DEG までは全部、その先は半分、MAX_TILT_DEG まで（大岩は BIG_ROCK_MAX_TILT_DEG）。</summary>
        public static float TiltFor(float slopeDeg, float height)
        {
            float t = slopeDeg <= FULL_TILT_DEG ? slopeDeg : FULL_TILT_DEG + (slopeDeg - FULL_TILT_DEG) * PARTIAL_TILT_K;
            return Mathf.Min(t, height >= BIG_ROCK_H ? BIG_ROCK_MAX_TILT_DEG : MAX_TILT_DEG);
        }

        static bool UsesTrunk(Placement p) => DressingPlan.IsTree(p) || IsCactus(p);

        static float FootShrink(Placement p, int kind)
        {
            if (kind == LINEAR) return LINE_FOOT;
            if (IsRockAsset(p.asset) || (p.part != null && p.part.EndsWith("_Rock")) || p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB
                || p.asset == DressingPlan.CLIFFS || p.asset == DressingPlan.DPROPS) return ROCK_FOOT;
            return BOX_FOOT;
        }

        /// <summary>
        /// 足元の点（ワールド、位置 p.position 込み）：回した外形の 6 面のうちいちばん低い面の、角・辺の中・中心（長い辺は 1.5m ごと）。
        /// 木・サボテンは幹（GroundRadius）の円の 8 点と中心。
        /// </summary>
        public static List<Vector3> Footprint(Placement p, int kind)
        {
            var pts = new List<Vector3>();
            if (kind == PLUMB && UsesTrunk(p))
            {
                float r = DressingPlan.GroundRadius(p);
                Vector3 baseC = p.position;
                pts.Add(baseC);
                for (int i = 0; i < 8; i++)
                {
                    float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                    pts.Add(baseC + Tilt(new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r), p.up));
                }
                return pts;
            }
            Vector3 mn, mx;
            LocalBox(p, out mn, out mx);
            float shrink = FootShrink(p, kind);
            // 下を向く面（回した外向きの法線がいちばん下向き）
            int bestAxis = 1; float bestSign = -1f, bestY = float.MaxValue;
            for (int ax = 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 nrm = ax == 0 ? new Vector3(sg, 0f, 0f) : (ax == 1 ? new Vector3(0f, sg, 0f) : new Vector3(0f, 0f, sg));
                    float y = Rotate(p, nrm).y;
                    if (y < bestY - 1e-4f) { bestY = y; bestAxis = ax; bestSign = sg; }
                }
            int ja = (bestAxis + 1) % 3, ka = (bestAxis + 2) % 3;
            Vector3 mid = (mn + mx) * 0.5f;
            float hj = (Get(mx, ja) - Get(mn, ja)) * 0.5f * shrink, hk = (Get(mx, ka) - Get(mn, ka)) * 0.5f * shrink;
            int nj = Mathf.Clamp(Mathf.CeilToInt(2f * hj / 1.5f) + 1, 3, 9), nk = Mathf.Clamp(Mathf.CeilToInt(2f * hk / 1.5f) + 1, 3, 9);
            for (int a = 0; a < nj; a++)
                for (int b = 0; b < nk; b++)
                {
                    if (a != 0 && a != nj - 1 && b != 0 && b != nk - 1 && !(a == nj / 2 && b == nk / 2)) continue;   // 縁と中心だけ
                    Vector3 l = mid;
                    Set(ref l, bestAxis, bestSign < 0 ? Get(mn, bestAxis) : Get(mx, bestAxis));
                    Set(ref l, ja, Get(mid, ja) + hj * (2f * a / (nj - 1) - 1f));
                    Set(ref l, ka, Get(mid, ka) + hk * (2f * b / (nk - 1) - 1f));
                    pts.Add(p.position + Rotate(p, l));
                }
            return pts;
        }

        static float Get(Vector3 v, int i) => i == 0 ? v.x : (i == 1 ? v.y : v.z);
        static void Set(ref Vector3 v, int i, float f) { if (i == 0) v.x = f; else if (i == 1) v.y = f; else v.z = f; }

        /// <summary>足元の地面の傾き（面の当てはめ：半径 R の 8 点）。downhill = 下り向き（水平、単位）。戻り値 = 傾き（°）。</summary>
        public static float SlopeUnder(Ground g, Vector3 c, float R, out Vector3 downhill)
        {
            R = Mathf.Max(R, SLOPE_PROBE_MIN_M);
            float bx = 0f, bz = 0f, sxx = 0f, szz = 0f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                float dx = Mathf.Sin(a) * R, dz = Mathf.Cos(a) * R;
                float y = g(c.x + dx, c.z + dz);
                bx += y * dx; bz += y * dz; sxx += dx * dx; szz += dz * dz;
            }
            bx /= sxx; bz /= szz;
            float m = Mathf.Sqrt(bx * bx + bz * bz);
            downhill = m > 1e-6f ? new Vector3(-bx / m, 0f, -bz / m) : Vector3.zero;
            return Mathf.Atan(m) * Mathf.Rad2Deg;
        }

        /// <summary>足元の広がり（地面の傾きを調べる半径）：外形の水平の大きさの平均（木・サボテンは幹）。</summary>
        public static float ProbeRadius(Placement p, int kind)
        {
            if (kind == PLUMB && UsesTrunk(p)) return DressingPlan.GroundRadius(p);
            Vector3 mn, mx;
            LocalBox(p, out mn, out mx);
            Vector3 e = mx - mn;
            float a = Mathf.Abs(Mathf.DeltaAngle(0f, p.rollDeg)) > 45f ? e.y : e.x;   // 横倒し：高さの向きが水平になる
            return Mathf.Sqrt(Mathf.Abs(a * e.z)) * 0.5f * FootShrink(p, kind);
        }

        // ------------------------------------------------------------------
        // 合わせる
        // ------------------------------------------------------------------

        /// <summary>
        /// 地面に置いた置き物（y は地面の高さに合わせ済み）を斜面に合わせる。HUG・LINEAR は向き（up）と高さを置き直す
        /// （接する所から designSink、最低 HUG_MIN_SINK_M、高さの 60% まで沈める）。PLUMB は浮いていれば下げるだけ。
        /// 戻り値 = 置き直した数。
        /// </summary>
        /// <summary>false = 合わせない（検査の前後の比べ用。テストだけが切り替える）。</summary>
        public static bool Enabled = true;

        public static int Fit(List<Placement> list, Ground g) => Fit(list, g, true);

        /// <summary>allowDrop = 置き直しても埋もれる物（崖の縁・細い尾根の上）を計画から外す。</summary>
        public static int Fit(List<Placement> list, Ground g, bool allowDrop) => Fit(list, g, allowDrop, null);

        /// <summary>moveOk(p, 元の足元, ずらした足元) = ずらしてよいか（道に近づけない等。null = どこでも）。</summary>
        public static int Fit(List<Placement> list, Ground g, bool allowDrop, System.Func<Placement, Vector3, Vector3, bool> moveOk)
        {
            if (!Enabled) return 0;
            int n = 0;
            var drop = new HashSet<Placement>();
            foreach (var p in list)
            {
                int r = FitOne(p, g, true, moveOk);
                if (r != 0) n++;
                if (r < 0 && allowDrop) drop.Add(p);
            }
            if (drop.Count > 0) list.RemoveAll(drop.Contains);
            return n;
        }

        /// <summary>
        /// 1 つ置き直す。戻り値 1 = 置き直した、0 = そのまま、−1 = 埋もれて見えない（崖の縁・細い尾根：上り側へ外形の 0.5〜1.5 倍ずらしても直らない）。
        /// 線の物・杭は動かさない（線が途切れる）。
        /// </summary>
        public static int FitOne(Placement p, Ground g) => FitOne(p, g, true);

        public static int FitOne(Placement p, Ground g, bool allowMove) => FitOne(p, g, allowMove, null);

        public static int FitOne(Placement p, Ground g, bool allowMove, System.Func<Placement, Vector3, Vector3, bool> moveOk)
        {
            int kind = KindOf(p);
            if (kind == SKIP) return 0;
            if (FitExempt.Contains(p.group ?? "")) return Buried(p, kind, g) ? -1 : 0;   // 置き直さない。見えないほど埋もれていれば外す
            bool movable = kind != LINEAR && !IsPost(p) && !LowerOnly(p) && !DressingPlan.LineGroups.Contains(p.group ?? "");
            Vector3 p0 = p.position;
            Place(p, kind, g);
            if (!Buried(p, kind, g) && !OnWall(p, kind, g)) return 1;   // 小さな物は急な壁に貼り付けない（壁の途中で宙に浮いて見える）
            if (!movable || !allowMove) return (kind == LINEAR && p.part != DressingPlan.RAIL_TRACK) || IsPost(p) ? 1 : -1;
            Vector3 down;
            float pr = ProbeRadius(p, kind);
            SlopeUnder(g, p0, pr, out down);
            if (down.sqrMagnitude > 0.5f && OnWall(p, kind, g))
            {
                // 壁の上の小物は壁の裾（下り側）へ 3m まで下ろす（切り通しの底の道ばたの岩）
                for (float d = WALL_SLIDE_STEP_M; d <= WALL_SLIDE_MAX_M + 1e-3f; d += WALL_SLIDE_STEP_M)
                {
                    p.position = new Vector3(p0.x + down.x * d, p0.y, p0.z + down.z * d);
                    if (moveOk != null && !moveOk(p, p0, p.position)) break;
                    Place(p, kind, g);
                    if (!Buried(p, kind, g) && !OnWall(p, kind, g)) return 1;
                }
            }
            if (down.sqrMagnitude > 0.5f)
            {
                float unit = Mathf.Max(1f, 2f * pr);
                for (int i = 1; i <= 3; i++)
                {
                    p.position = new Vector3(p0.x - down.x * unit * 0.5f * i, p0.y, p0.z - down.z * unit * 0.5f * i);
                    if (moveOk != null && !moveOk(p, p0, p.position)) break;
                    Place(p, kind, g);
                    if (!Buried(p, kind, g) && !OnWall(p, kind, g)) return 1;
                }
            }
            p.position = p0;
            Place(p, kind, g);
            return (kind == LINEAR && p.part != DressingPlan.RAIL_TRACK) || IsPost(p) ? 1 : -1;   // 柵の板・杭は外さない（線が途切れる）
        }

        /// <summary>
        /// 置き直さないまとまり：切り通しの壁の段の岩（art-r6-02/04：壁の面から引き出す量・沈め量を絵で合わせた。壁に埋めるのが目的）。
        /// 検査はする（浮く・足元のどこも埋もれる）。
        /// </summary>
        public static readonly HashSet<string> FitExempt = new HashSet<string> { "WallFace" };

        /// <summary>まっすぐな物のうち、高さを置き直さず（浮いていれば下げるだけ）にする物：メサ・崖の板・縦に伸ばした岩（斜面に埋めるのが目的）、杭（AddPost が決めた高さ）。</summary>
        static bool LowerOnly(Placement p)
            => p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB || p.asset == DressingPlan.CLIFFS || p.scaleY > 1.01f || IsPost(p)
               || DressingPlan.IsTowerPart(p.part) || (p.asset != null && p.asset.StartsWith("Buildings/"));

        static void Place(Placement p, int kind, Ground g)
        {
            if (kind == PLUMB)
            {
                p.up = Vector3.up;
                float off = ContactOffset(p, kind, g);
                if (LowerOnly(p))
                {
                    if (off < -CONTACT_SINK_M) p.position = new Vector3(p.position.x, p.position.y + off - CONTACT_SINK_M, p.position.z);
                    return;
                }
                float sk = SinkFor(p);
                p.position = new Vector3(p.position.x, p.position.y + off - sk, p.position.z);
                return;
            }
            if (kind == LINEAR)
            {
                Vector3 mn, mx;
                LocalBox(p, out mn, out mx);
                bool alongX = mx.x >= mx.z;
                float half = (alongX ? mx.x : mx.z) * LINE_FOOT;
                Vector3 d = Rotate(alongX ? Vector3.right : Vector3.forward, p.yawDeg, 0f, Vector3.up);
                d = new Vector3(d.x, 0f, d.z).normalized;
                float g1 = g(p.position.x - d.x * half, p.position.z - d.z * half), g2 = g(p.position.x + d.x * half, p.position.z + d.z * half);
                float al = Mathf.Clamp(Mathf.Atan2(g2 - g1, 2f * half) * Mathf.Rad2Deg, -LINEAR_MAX_PITCH_DEG, LINEAR_MAX_PITCH_DEG) * Mathf.Deg2Rad;
                p.up = new Vector3(-d.x * Mathf.Sin(al), Mathf.Cos(al), -d.z * Mathf.Sin(al));
            }
            else
            {
                Vector3 down;
                float slope = SlopeUnder(g, p.position, ProbeRadius(p, kind), out down);
                float t = TiltFor(slope, p.height) * Mathf.Deg2Rad;
                p.up = new Vector3(down.x * Mathf.Sin(t), Mathf.Cos(t), down.z * Mathf.Sin(t));
            }
            float sink = SinkFor(p);
            float o = ContactOffset(p, kind, g);
            p.position = new Vector3(p.position.x, p.position.y + o - sink, p.position.z);
        }

        /// <summary>接する所から沈める量：決まりの量（designSink）、最低 HUG_MIN_SINK_M（小さな物は高さの 30%）、高さの 60% まで。</summary>
        public static float SinkFor(Placement p)
        {
            float h = BoxHeight(p);
            return Mathf.Min(Mathf.Max(p.designSink, Mathf.Min(HUG_MIN_SINK_M, 0.3f * h)), MAX_SINK_FRAC * h);
        }

        /// <summary>高さ（外形の底〜上、杭は高さ）。</summary>
        public static float BoxHeight(Placement p)
        {
            if (IsPost(p)) return p.height;
            Vector3 mn, mx;
            LocalBox(p, out mn, out mx);
            return Mathf.Max(0.05f, mx.y - mn.y);
        }

        /// <summary>見えないほど埋もれるか：足元のどこも高さの BURIED_FRAC より深い、または中心で（傾けた軸に沿って）BURIED_FRAC より深い。</summary>
        public const float RAIL_MAX_BURY_M = 1.5f;
        /// <summary>小さな物（高さ WALL_SMALL_H 未満の岩・小石・草・骨・干し草）を置かない壁の傾き。沿わせても 40° までなので、残りの傾きで壁に貼り付いて浮いて見える。</summary>
        public const float WALL_DEG = 45f, WALL_SMALL_H = 2f, WALL_SLIDE_STEP_M = 0.5f, WALL_SLIDE_MAX_M = 3f;

        /// <summary>地面に沿う小さな物が急な壁（WALL_DEG より急）の上にあるか（背景・切り通しの壁の段の岩・柵の線の目印は除く）。</summary>
        public static bool OnWall(Placement p, int kind, Ground g)
        {
            if (kind != HUG || p.background || FitExempt.Contains(p.group ?? "") || p.height >= WALL_SMALL_H
                || p.group == "BarrierLine" || p.group == "MesaGate" || p.group == "PassBeacon") return false;   // 柵の線の目印の岩（切り通しの壁ぎわ）は線を途切れさせない。切り通しの尾根の焚き火（門の目印）は尾根に置くのが目的
            Vector3 down;
            return SlopeUnder(g, p.position, ProbeRadius(p, kind), out down) > WALL_DEG;
        }

        public static bool Buried(Placement p, int kind, Ground g)
        {
            if (IsPost(p)) return false;
            if (p.part == DressingPlan.RAIL_TRACK)   // 96m の線路：路盤の無い起伏では途中が埋もれる（境目の最後の直線の脇）
            {
                float dm = 0f;
                foreach (var q in Footprint(p, kind)) dm = Mathf.Max(dm, g(q.x, q.z) - q.y);
                return dm > RAIL_MAX_BURY_M;
            }
            float hgt = BoxHeight(p);
            // 斜面に埋めるのが目的の岩・背景・下げるだけの物は中心が深くてよい（壁から出た面が見える）。足元のどこも深いときだけ
            bool embedded = p.background || DressingPlan.SlopeEmbedded.Contains(p.group ?? "") || LowerOnly(p);
            float depthC = (g(p.position.x, p.position.z) - p.position.y) * Mathf.Max(0.05f, p.up.normalized.y);
            if (!embedded && depthC > BURIED_FRAC * hgt) return true;
            float depthMin = float.MaxValue;
            foreach (var q in Footprint(p, kind)) depthMin = Mathf.Min(depthMin, g(q.x, q.z) - q.y);
            return depthMin > BURIED_FRAC * hgt;
        }

        // ---- 見どころ・ゴール・スタートの手で置く部品（SetPieceBuilder・CourseBuilder・下見の Extras が同じ置き方）----

        /// <summary>
        /// 地面に置く部品 1 つ：足元 at（x・z）の地面へ下ろし、種類どおりに合わせる（沿わせる・長さに沿う・浮かないよう下げる）。動かさない・外さない。
        /// 戻り値の position（足元）と up（FromToRotation(Vector3.up, up) × Euler(0, yaw, roll)）で置く。
        /// </summary>
        public static Placement Part(string asset, string part, Vector3 at, float yaw, float scale, float sink, Ground g, string group = "SetPiece", float roll = 0f)
        {
            var sz = DressingPlan.SizeOf(asset, part);
            var p = new Placement
            {
                group = group, asset = asset, part = part, position = new Vector3(at.x, 0f, at.z), yawDeg = yaw, rollDeg = roll, scale = scale,
                radius = sz.r * scale, height = sz.h * scale, designSink = sink,
            };
            Settle(p, g);
            FitOne(p, g, false);
            return p;
        }

        /// <summary>
        /// 縁に置くまっすぐな岩（溝の縁の岩：落ちない壁）：中心の地面 − sink に立て、底の面が FLOAT_TOL_M より浮くなら away の向きへ
        /// 0.25m ずつ maxM までずらす（縁から張り出さない）。戻り値の position が足元。
        /// </summary>
        public static Placement SlideOffEdge(string asset, string part, Vector3 at, Vector3 away, float maxM, float yaw, float scale, float sink, Ground g, string group = "SetPiece")
        {
            var sz = DressingPlan.SizeOf(asset, part);
            var p = new Placement
            {
                group = group, asset = asset, part = part, yawDeg = yaw, scale = scale, radius = sz.r * scale, height = sz.h * scale, designSink = sink,
            };
            away = new Vector3(away.x, 0f, away.z).normalized;
            for (float d = 0f; d <= maxM + 1e-3f; d += 0.25f)
            {
                p.position = new Vector3(at.x + away.x * d, 0f, at.z + away.z * d);
                Settle(p, g);
                if (ContactOffset(p, PLUMB, g) >= -FLOAT_TOL_M) break;
            }
            return p;
        }

        /// <summary>中心の地面へ下ろす（y = 地面 − designSink、立てた杭は中心の高さ）。向きはまっすぐに戻す。Fit の前に使う（ロビーの手で並べた物）。</summary>
        public static void Settle(Placement p, Ground g)
        {
            if (p.keepY) return;
            float y = g(p.position.x, p.position.z) - p.designSink + (IsPost(p) ? p.height * 0.5f : 0f);
            p.position = new Vector3(p.position.x, y, p.position.z);
            p.up = Vector3.up;
        }

        /// <summary>足元の点のうち地面にいちばん近い（浮いている）所で接するには y をいくら動かすか（+ = 上げられる、− = 浮いている）。</summary>
        public static float ContactOffset(Placement p, int kind, Ground g)
        {
            float off = float.MaxValue;
            foreach (var q in Footprint(p, kind)) off = Mathf.Min(off, g(q.x, q.z) - q.y);
            return off;
        }

        // ------------------------------------------------------------------
        // 調べる
        // ------------------------------------------------------------------

        public sealed class Report
        {
            public int total, checkedN, floating, buried, poking, plumbSteep, onWall;
            public float worstFloat, worstPlumbDeg;
            public readonly List<string> worst = new List<string>();
            public readonly Dictionary<string, int> byKey = new Dictionary<string, int>();   // "種類 グループ 部品" → 数

            public int Bad => floating + buried + poking + plumbSteep + onWall;

            public void Add(Report o)
            {
                total += o.total; checkedN += o.checkedN; floating += o.floating; buried += o.buried; poking += o.poking; plumbSteep += o.plumbSteep; onWall += o.onWall;
                worstFloat = Mathf.Max(worstFloat, o.worstFloat); worstPlumbDeg = Mathf.Max(worstPlumbDeg, o.worstPlumbDeg);
                worst.AddRange(o.worst);
                foreach (var kv in o.byKey) { int k; byKey.TryGetValue(kv.Key, out k); byKey[kv.Key] = k + kv.Value; }
            }

            public string Summary(string scope)
                => scope + "：" + checkedN + "/" + total + " 個を調べた　浮く " + floating + "（最大 " + worstFloat.ToString("F2") + "m）・埋もれる " + buried
                   + "・端が刺さる " + poking + "・急な斜面にまっすぐ " + plumbSteep + "（最大 " + worstPlumbDeg.ToString("F0") + "°）・壁に小物 " + onWall;

            /// <summary>種類ごとに多い順の上位 n 件（"float GuideLine FarmProps_03 × 12"）。</summary>
            public List<string> Top(int n)
            {
                var l = new List<KeyValuePair<string, int>>(byKey);
                l.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
                var r = new List<string>();
                for (int i = 0; i < l.Count && i < n; i++) r.Add(l[i].Key + " × " + l[i].Value);
                return r;
            }
        }

        static void Count(Report r, string what, Placement p, string detail)
        {
            string k = what + " " + (p.group ?? "-") + " " + (p.part ?? System.IO.Path.GetFileNameWithoutExtension(p.asset));
            int c; r.byKey.TryGetValue(k, out c); r.byKey[k] = c + 1;
            if (r.worst.Count < 400) r.worst.Add(k + " @(" + p.position.x.ToString("F1") + ", " + p.position.y.ToString("F1") + ", " + p.position.z.ToString("F1") + ") " + detail
                + (p.rollDeg != 0f ? " roll " + p.rollDeg.ToString("F0") : "") + " s " + p.scale.ToString("F2") + (p.scaleY != 1f || p.scaleZ != 1f ? "×" + p.scaleY.ToString("F2") + "×" + p.scaleZ.ToString("F2") : ""));
        }

        /// <summary>
        /// 置き物の接地を調べる（a. 浮く：足元の点のどこかが地面より FLOAT_TOL_M 以上上、b. 埋もれる：足元のどこも高さの 85% より深い・
        /// 線の物の端が厚さの 80% より深く刺さる、c. 地面に沿う物が 15° より急な地面に、傾きの半分未満の傾きで立つ）。背景・わざと埋めた物は数えない。
        /// </summary>
        public static Report Audit(List<Placement> list, Ground g)
        {
            var r = new Report();
            foreach (var p in list)
            {
                r.total++;
                int kind = KindOf(p);
                if (kind == SKIP) continue;
                r.checkedN++;
                var pts = Footprint(p, kind);
                float gapMax = float.MinValue, depthMin = float.MaxValue;
                foreach (var q in pts)
                {
                    float gy = g(q.x, q.z);
                    gapMax = Mathf.Max(gapMax, q.y - gy);
                    depthMin = Mathf.Min(depthMin, gy - q.y);
                }
                if (gapMax > FLOAT_TOL_M)
                {
                    r.floating++; r.worstFloat = Mathf.Max(r.worstFloat, gapMax);
                    Count(r, "float", p, gapMax.ToString("F2") + "m");
                }
                Vector3 mn, mx;
                LocalBox(p, out mn, out mx);
                float hgt = BoxHeight(p);
                // 埋もれる：足元のどこも高さの 85% より深い、または中心で（傾けた軸に沿って）高さの 85% より深い（急な斜面から岩の先だけが出る）
                if (Buried(p, kind, g))
                {
                    r.buried++;
                    float depthC = g(p.position.x, p.position.z) - p.position.y;
                    Count(r, "buried", p, (Mathf.Max(depthMin, depthC) / hgt * 100f).ToString("F0") + "%");
                }
                bool exempt = FitExempt.Contains(p.group ?? "");   // 壁に埋めた段の岩：端・傾きは調べない（浮く・埋もれるだけ）
                if (!exempt && (kind == LINEAR || (kind == HUG && Elongation(p) >= 2f)))
                {
                    bool alongX = mx.x >= mx.z;
                    float half = (alongX ? mx.x : mx.z) * LINE_FOOT;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 l = new Vector3(alongX ? s * half : 0f, mn.y, alongX ? 0f : s * half);
                        Vector3 e = p.position + Rotate(p, l);
                        float dep = g(e.x, e.z) - e.y;
                        if (dep > POKE_FRAC * hgt) { r.poking++; Count(r, "poke", p, (dep / hgt * 100f).ToString("F0") + "%"); break; }
                    }
                }
                if (OnWall(p, kind, g)) { r.onWall++; Count(r, "wall", p, "> " + WALL_DEG.ToString("F0") + "°"); }
                if (kind == HUG && !exempt)
                {
                    Vector3 down;
                    float slope = SlopeUnder(g, p.position, ProbeRadius(p, kind), out down);
                    float tilt = TiltDeg(p.up);
                    if (slope >= STEEP_DEG && tilt < PLUMB_TILT_K * Mathf.Min(slope, PLUMB_SLOPE_CAP_DEG) - 0.5f)
                    {
                        r.plumbSteep++; r.worstPlumbDeg = Mathf.Max(r.worstPlumbDeg, slope);
                        Count(r, "plumb", p, slope.ToString("F0") + "° tilt " + tilt.ToString("F0") + "°");
                    }
                }
            }
            return r;
        }

        /// <summary>ステージの高さマップ（Terrain ローカル）の地面。</summary>
        public static Ground ForHeights(float[,] h, float sizeM)
            => (x, z) => WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, x, z);
    }
}
