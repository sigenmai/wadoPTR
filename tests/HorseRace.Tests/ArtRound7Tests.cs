using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>見た目・レベルアートの 7 巡目：斜面と接地（オーナーの指摘「アセットが地面の傾斜に対しておかしいところがある」、art-r7-01）。</summary>
    public class ArtRound7Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        static List<Placement> StageAll(ArtRound2Tests.Built b)
        {
            var all = new List<Placement>(b.plan);
            all.AddRange(DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc)));
            return all;
        }

        static void Near(Vector3 a, Vector3 b, float tol = 1e-3f, string msg = null)
        {
            Assert.AreEqual(b.x, a.x, tol, msg); Assert.AreEqual(b.y, a.y, tol, msg); Assert.AreEqual(b.z, a.z, tol, msg);
        }

        // ---- 回転（Unity の FromToRotation(up, n) × Euler(0, yaw, roll) と同じ）----

        [Test] public void RotationMatchesUnityConventions()
        {
            Near(GroundFit.Rotate(Vector3.forward, 90f, 0f, Vector3.up), Vector3.right);              // yaw 90：前（+Z）→ 右（+X）
            Near(GroundFit.Rotate(Vector3.right, 0f, 90f, Vector3.up), Vector3.up);                    // roll 90：ローカル X → 上（立てた丸太の杭）
            Vector3 n = new Vector3(0.3f, 0.9f, -0.2f).normalized;
            Near(GroundFit.Tilt(Vector3.up, n), n);                                                      // 真上 → n
            Vector3 axis = new Vector3(n.z, 0f, -n.x).normalized;                                        // 回転の軸（水平）は動かない
            Near(GroundFit.Tilt(axis, n), axis);
            Assert.AreEqual(Mathf.Acos(n.y) * Mathf.Rad2Deg, GroundFit.TiltDeg(n), 1e-3f);
            // 傾けても長さは変わらない
            Vector3 v = new Vector3(1.2f, -0.4f, 2f);
            Assert.AreEqual(v.magnitude, GroundFit.Tilt(v, n).magnitude, 1e-4f);
        }

        [Test] public void TiltIsFullThenPartialThenCapped()
        {
            Assert.AreEqual(20f, GroundFit.TiltFor(20f, 1f), 1e-4f);
            Assert.AreEqual(GroundFit.FULL_TILT_DEG, GroundFit.TiltFor(GroundFit.FULL_TILT_DEG, 1f), 1e-4f);
            Assert.AreEqual(GroundFit.FULL_TILT_DEG + 6f, GroundFit.TiltFor(GroundFit.FULL_TILT_DEG + 12f, 1f), 1e-4f);
            Assert.AreEqual(GroundFit.MAX_TILT_DEG, GroundFit.TiltFor(80f, 1f), 1e-4f);
            Assert.AreEqual(GroundFit.BIG_ROCK_MAX_TILT_DEG, GroundFit.TiltFor(60f, 9f), 1e-4f);
        }

        [Test] public void Classification()
        {
            Placement P(string a, string part, float roll = 0f, string group = "Rocks", bool bg = false, float sy = 1f)
                => new Placement { asset = a, part = part, rollDeg = roll, group = group, background = bg, scale = 1f, scaleY = sy, radius = 1f, height = 1f };
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.RCOLL, "RockCollection_104")));
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.HAY, "HayBales_02")));
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.DPROPS, "DesertProps_22")));          // 草
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.FARM, "FarmProps_15")));              // 樽
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.SKELETON, null, 85f)));               // 横倒しの骨の馬
            Assert.AreEqual(GroundFit.HUG, GroundFit.KindOf(P(DressingPlan.DROCKS, "DesertRocks_05", 0f, "RimLandmark", true)));   // 背景の岩も沿わせる
            Assert.AreEqual(GroundFit.LINEAR, GroundFit.KindOf(P(DressingPlan.FARM, "FarmProps_03")));           // 柵の板
            Assert.AreEqual(GroundFit.LINEAR, GroundFit.KindOf(P(DressingPlan.LOGS, "WoodLogs_01")));            // 寝かせた 1 本の丸太
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.LOGS, DressingPlan.POST_PART, 90f))); // 立てた丸太の杭
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.FARM, "FarmProps_02")));            // 街灯
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.DEADTREE, null)));
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.CACTI, "Cacti_03")));
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.DROCKS, "DesertRocks_02", 0f, "GrayRocks", false, 1.5f)));   // 縦に伸ばした岩は柱
            Assert.AreEqual(GroundFit.PLUMB, GroundFit.KindOf(P(DressingPlan.TOWNBLOCK, null)));
            Assert.AreEqual(GroundFit.SKIP, GroundFit.KindOf(P(DressingPlan.MESAB, null, 0f, "RimRing", true)));
            Assert.Greater(PartBounds.Count, 400);
            Assert.IsTrue(PartBounds.TryGet(DressingPlan.FARM, "FarmProps_03", out var fb));
            Assert.Greater(fb.hx, 4f * fb.hz);   // 柵の板の長い辺はローカル X
        }

        // ---- 合わせ方（20° の平らな斜面）----

        static float Plane(float x, float z) => 10f + x * Mathf.Tan(20f * Mathf.Deg2Rad);   // +X へ 20° 上る

        [Test] public void HugTiltsToSlopeAndTouches()
        {
            var p = new Placement { asset = DressingPlan.DROCKS, part = "DesertRocks_06", group = "Rocks", scale = 1f, radius = 2f, height = 2f, designSink = 0.1f, position = new Vector3(50f, 0f, 50f) };
            Assert.AreEqual(1, GroundFit.FitOne(p, Plane));
            Assert.AreEqual(20f, GroundFit.TiltDeg(p.up), 0.5f);
            Assert.Less(p.up.x, 0f);   // 下り（−X）へ傾く
            float off = GroundFit.ContactOffset(p, GroundFit.HUG, Plane);
            Assert.AreEqual(GroundFit.SinkFor(p), off, 0.02f);    // どこも浮かず、決まりの量だけ沈む
            var r = GroundFit.Audit(new List<Placement> { p }, Plane);
            Assert.AreEqual(0, r.Bad);
            // まっすぐのままなら「急な斜面にまっすぐ」と数える（20° ≥ 15°）
            var q = new Placement { asset = p.asset, part = p.part, group = "Rocks", scale = 1f, radius = 2f, height = 2f, position = p.position };
            q.position = new Vector3(q.position.x, Plane(q.position.x, q.position.z) - 0.8f, q.position.z);
            Assert.AreEqual(1, GroundFit.Audit(new List<Placement> { q }, Plane).plumbSteep);
        }

        [Test] public void FencePitchesAlongItsLengthOnly()
        {
            // 斜面を上る向き（長い辺 = ローカル X を +X へ：yaw 0）
            var f = new Placement { asset = DressingPlan.FARM, part = "FarmProps_03", group = "GuideLine", scale = 1f, radius = 1.4f, height = 1f, designSink = 0.05f, position = new Vector3(50f, 0f, 50f) };
            GroundFit.FitOne(f, Plane);
            Assert.AreEqual(20f, GroundFit.TiltDeg(f.up), 0.5f);
            Vector3 along = GroundFit.Rotate(f, Vector3.right);
            Assert.AreEqual(Mathf.Sin(20f * Mathf.Deg2Rad), along.y, 0.01f);   // 長い辺が斜面に沿って上る
            // 等高線に沿う柵（yaw 90：長い辺が Z）は傾かない（横の傾きは付けない）
            var g = new Placement { asset = DressingPlan.FARM, part = "FarmProps_03", group = "GuideLine", scale = 1f, radius = 1.4f, height = 1f, designSink = 0.05f, yawDeg = 90f, position = new Vector3(50f, 0f, 50f) };
            GroundFit.FitOne(g, Plane);
            Assert.Less(GroundFit.TiltDeg(g.up), 0.5f);
            Assert.AreEqual(0, GroundFit.Audit(new List<Placement> { f, g }, Plane).Bad);
            // 前の置き方（足元の円のいちばん低い所 − 差）では等高線の柵は埋もれていた
            var old = new Placement { asset = DressingPlan.FARM, part = "FarmProps_03", group = "GuideLine", scale = 1f, radius = 1.4f, height = 1f, yawDeg = 90f };
            float span = 2f * 1.4f * Mathf.Tan(20f * Mathf.Deg2Rad);
            old.position = new Vector3(50f, Plane(50f - 1.4f, 50f) - Mathf.Min(span + 0.1f, 0.6f), 50f);
            Assert.AreEqual(1, GroundFit.Audit(new List<Placement> { old }, Plane).buried);
        }

        [Test] public void PlumbStaysUprightAndDoesNotFloat()
        {
            var post = new Placement { asset = DressingPlan.LOGS, part = DressingPlan.POST_PART, rollDeg = 90f, group = "GuideLine", height = 2.6f, radius = 0.21f, groundR = 0.21f,
                scale = 1.79f, scaleY = 1f, scaleZ = 0.6f, position = new Vector3(50f, Plane(50f, 50f) + 1.3f + 0.5f, 50f) };   // 0.5m 浮いた杭
            GroundFit.FitOne(post, Plane);
            Assert.AreEqual(0f, GroundFit.TiltDeg(post.up), 1e-4f);
            Assert.GreaterOrEqual(GroundFit.ContactOffset(post, GroundFit.PLUMB, Plane), GroundFit.CONTACT_SINK_M - 1e-3f);
            var tree = new Placement { asset = DressingPlan.DEADTREE, group = "DeadGroveFloor", scale = 1f, radius = 3.8f, height = 3.8f, groundR = DressingPlan.TrunkRadius(3.8f), designSink = 0.05f,
                position = new Vector3(50f, Plane(50f, 50f) - 3f, 50f) };   // 3m 埋もれた木は上げる
            GroundFit.FitOne(tree, Plane);
            Assert.AreEqual(0f, GroundFit.TiltDeg(tree.up), 1e-4f);
            Assert.AreEqual(0, GroundFit.Audit(new List<Placement> { post, tree }, Plane).Bad);
        }

        [Test] public void OnAnEdgeMovesOffOrIsDropped()
        {
            // x < 50 は 6m 下の段（崖の縁）
            GroundFit.Ground step = (x, z) => x < 50f ? 4f : 10f;
            var rock = new Placement { asset = DressingPlan.DROCKS, part = "DesertRocks_10", group = "Rocks", scale = 1f, radius = 1f, height = 0.9f, position = new Vector3(50.3f, 0f, 20f) };
            var l = new List<Placement> { rock };
            GroundFit.Fit(l, step);
            Assert.AreEqual(1, l.Count);
            Assert.Greater(Mathf.Abs(rock.position.x - 50.3f), 0.2f);   // 縁から上の段か下の段の裾へずれた
            Assert.AreEqual(0, GroundFit.Audit(l, step).Bad);
            // 細い尾根（両側が落ちる）は直らない → 外す
            GroundFit.Ground ridge = (x, z) => Mathf.Abs(x - 50f) < 0.5f ? 10f : 2f;
            var r2 = new Placement { asset = DressingPlan.DROCKS, part = "DesertRocks_05", group = "Rocks", scale = 1f, radius = 3f, height = 3f, position = new Vector3(50f, 0f, 20f) };
            var l2 = new List<Placement> { r2 };
            GroundFit.Fit(l2, ridge);
            Assert.AreEqual(0, l2.Count);
        }

        // ---- 3 ステージとロビーの全部 ----

        [TestCaseSource(nameof(Stages))]
        public void StagesAreGrounded(int i)
        {
            var b = Stage(i);
            var r = GroundFit.Audit(StageAll(b), GroundFit.ForHeights(b.h, StageDefinitions.SIZE));
            TestContext.WriteLine(r.Summary("stage " + i));
            foreach (var s in r.worst) TestContext.WriteLine("  " + s);
            Assert.AreEqual(0, r.floating, "浮く");
            Assert.AreEqual(0, r.plumbSteep, "地面に沿う物が急な斜面にまっすぐ");
            Assert.AreEqual(0, r.onWall, "小さな物が 45° より急な壁に貼り付く");
            Assert.LessOrEqual(r.buried + r.poking, 12, "埋もれる・端が刺さる（尾根をまたぐ柵の板など、ごくわずか）");
            // 地面に沿う物の大半は傾いている（斜面のある所では）
            int hug = StageAll(b).Count(p => GroundFit.KindOf(p) == GroundFit.HUG), tilted = StageAll(b).Count(p => GroundFit.KindOf(p) == GroundFit.HUG && GroundFit.TiltDeg(p.up) > 1f);
            TestContext.WriteLine($"stage {i}: {tilted}/{hug} ground-hugging props tilted");
            Assert.Greater(tilted, 0);
        }

        /// <summary>計画の前後：合わせる前（GroundFit を切った計画）の数と、合わせた後の数（review-log の表）。</summary>
        [TestCaseSource(nameof(Stages))]
        public void AuditBeforeAfter(int i)
        {
            var b = Stage(i);
            GroundFit.Report before;
            lock (typeof(GroundFit))
            {
                GroundFit.Enabled = false;
                try
                {
                    var raw = DressingPlan.For(b.def, b.local, b.h, b.goal, b.sc);
                    raw.AddRange(DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc)));
                    before = GroundFit.Audit(raw, GroundFit.ForHeights(b.h, StageDefinitions.SIZE));
                }
                finally { GroundFit.Enabled = true; }
            }
            var after = GroundFit.Audit(StageAll(b), GroundFit.ForHeights(b.h, StageDefinitions.SIZE));
            TestContext.WriteLine("BEFORE " + before.Summary("stage " + i));
            foreach (var s in before.Top(15)) TestContext.WriteLine("  " + s);
            TestContext.WriteLine("AFTER  " + after.Summary("stage " + i));
            foreach (var s in after.Top(15)) TestContext.WriteLine("  " + s);
            Assert.Less(after.Bad, before.Bad);
        }

        static List<Placement> LobbyAll(System.Func<Vector3, float> ground)
        {
            var tn = LobbyLayout.TOWN_NOMINAL_RECT;
            var plan = DressingPlan.ForLobby(new Vector2(tn.x, tn.y), new Vector2(tn.z, tn.w), LobbyLayout.LobbyDressingExclusions(), 4242);
            LobbyLayout.SettleOnGround(plan, ground);
            var lots = LobbyLayout.LotFill();
            LobbyLayout.SettleOnGround(lots, ground);
            var belt = LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT, 3);
            LobbyLayout.SettleOnGround(belt, ground);
            var crown = LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT, 11);
            LobbyLayout.GroundCrown(crown, ground);
            LobbyLayout.FitToGround(crown, ground);
            var all = new List<Placement>(plan);
            all.AddRange(lots); all.AddRange(belt); all.AddRange(crown);
            return all;
        }

        [Test] public void LobbyIsGrounded()
        {
            var all = LobbyAll(LobbyLayout.SurfaceGround);
            var r = GroundFit.Audit(all, (x, z) => LobbyLayout.SurfaceGround(new Vector3(x, 0f, z)));
            TestContext.WriteLine(r.Summary("lobby"));
            foreach (var s in r.worst) TestContext.WriteLine("  " + s);
            Assert.AreEqual(0, r.floating);
            Assert.AreEqual(0, r.plumbSteep);
            Assert.AreEqual(0, r.onWall);
            Assert.LessOrEqual(r.buried + r.poking, 1, "板の縁を隠す岩（PlateEdge）は土手に半分埋める");
            // 前（町のまわりの飾りは y = 0、敷地は中心の地面 − 0.05、尾根の岩はまっすぐ）は町の土手・柵の外の上りで埋もれ・浮いていた
            var tn = LobbyLayout.TOWN_NOMINAL_RECT;
            var rawPlan = DressingPlan.ForLobby(new Vector2(tn.x, tn.y), new Vector2(tn.z, tn.w), LobbyLayout.LobbyDressingExclusions(), 4242);
            var rawLots = LobbyLayout.LotFill();
            foreach (var q in rawLots) q.position = new Vector3(q.position.x, LobbyLayout.ModelGround(q.position) - 0.05f, q.position.z);   // LobbyBuilder の前の置き方（地形 − 0.05）
            rawPlan.AddRange(rawLots);
            var rawCrown = LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT, 11);
            LobbyLayout.GroundCrown(rawCrown, LobbyLayout.SurfaceGround);   // 前：足元の 4 隅の低い所 − 差（まっすぐ）
            rawPlan.AddRange(rawCrown);
            var rawBelt = LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT, 3);
            foreach (var q in rawBelt) q.position = new Vector3(q.position.x, LobbyLayout.ModelGround(q.position) - 0.2f, q.position.z);
            rawPlan.AddRange(rawBelt);
            var rb = GroundFit.Audit(rawPlan, (x, z) => LobbyLayout.SurfaceGround(new Vector3(x, 0f, z)));
            TestContext.WriteLine("BEFORE " + rb.Summary("lobby"));
            foreach (var s in rb.Top(10)) TestContext.WriteLine("  " + s);
            Assert.Less(r.Bad, rb.Bad);
        }

        [Test] public void LobbyModelGroundMatchesBuilderShape()
        {
            Assert.AreEqual(-1f, LobbyLayout.ModelGround(new Vector3(0f, 0f, 100f)), 1e-4f);                            // 町の足元
            Assert.AreEqual(0f, LobbyLayout.ModelGround(new Vector3(0f, 0f, 20f)), 0.05f);                              // 南の入口の前（土手なし）
            float east = LobbyLayout.ModelGround(new Vector3(LobbyLayout.TOWN_NOMINAL_RECT.z + 4f, 0f, 100f));
            Assert.Greater(east, 0.2f);                                                                                  // 東の土手（4m 外で 0.3m）
            Assert.Less(east, LobbyLayout.BERM_HEIGHT_M + 0.05f);
        }

        // ---- art-r7-02：切り通しの壁の岩は壁に座る（オーナーの指摘「この写真　岩が変だとおもはないか？」）----

        /// <summary>道（x &lt; 10）の外、x = 10 から 1m ごとに 1.8m 上る 61° の壁（切り通しの実際の壁の形）。</summary>
        static float SteepWall(float x, float z) => Mathf.Max(0f, (x - 10f) * 1.8f);

        static Placement WallRock(float x, float y, float sy = 1.05f)
            => new Placement { group = "WallFace", asset = DressingPlan.DROCKS, part = "DesertRocks_03", position = new Vector3(x, y, 0f), yawDeg = 0f, scale = 0.7f, scaleY = sy,
                               radius = DressingPlan.SizeOf(DressingPlan.DROCKS, "DesertRocks_03").r * 0.7f, height = DressingPlan.SizeOf(DressingPlan.DROCKS, "DesertRocks_03").h * 0.7f * sy };

        [Test] public void WallSeatCheckCatchesCantileveredSlabs()
        {
            GroundFit.Ground g = SteepWall;
            Vector3 toRoad = new Vector3(-1f, 0f, 0f);
            // 前の置き方：中心を壁の途中に置き、底を中心の地面の高さに → 道側の底の下が空（棚に見えた）
            var slab = WallRock(18f, SteepWall(18f, 0f));
            Assert.IsFalse(GroundFit.WallSeated(slab, toRoad, g, out string why), "宙に張り出した岩");
            StringAssert.StartsWith("unsupported", why);
            Assert.Greater(GroundFit.WallSupportGap(slab, toRoad, g), 5f);
            Assert.Greater(GroundFit.WallOverhang(slab, toRoad, g), 3f);
            // 座らせる：道側の底のいちばん低い地面 − 0.3m に底
            var seated = WallRock(18f, 0f);
            DressingPlan.SeatOnWall(seated, toRoad, g, DressingPlan.WALL_SEAT_SINK_M);
            Assert.IsTrue(GroundFit.WallSeated(seated, toRoad, g, out why), why);
            Assert.LessOrEqual(GroundFit.WallSupportGap(seated, toRoad, g), 0f);
            Assert.LessOrEqual(GroundFit.WallOverhang(seated, toRoad, g), GroundFit.WALL_OVERHANG_TOL_M);
            GroundFit.WallExposure(seated, toRoad, g, out float front, out float depth);
            Assert.GreaterOrEqual(front, DressingPlan.WALL_SEAT_MIN_FRONT, "前の面が見える");
            Assert.Greater(depth, 2f, "薄いかけらではない");
            // 縦に伸ばした板は座っていても不可
            var tall = WallRock(18f, 0f, 1.6f);
            DressingPlan.SeatOnWall(tall, toRoad, g, DressingPlan.WALL_SEAT_SINK_M);
            Assert.IsFalse(GroundFit.WallSeated(tall, toRoad, g, out why));
            StringAssert.StartsWith("stretched", why);
            // 平らな棚の縁から道の上へ張り出す（底は棚の高さ、前の半分が縁の外）
            GroundFit.Ground ledge = (x, z) => x >= 20f ? 10f : 0f;
            var lip = WallRock(20f, 10f);
            Assert.Greater(GroundFit.WallOverhang(lip, toRoad, ledge), GroundFit.WALL_OVERHANG_TOL_M);
            Assert.IsFalse(GroundFit.WallSeated(lip, toRoad, ledge, out why));
        }

        [Test] public void PassWallRocksAreSeatedAndStepBack()
        {
            var b = Stage(2);
            GroundFit.Ground g = b.Ground;
            var rocks = b.plan.Where(DressingPlan.IsWallRock).ToList();
            float pass = SetPieces.PassArc(b.def, b.goal);
            var cum = DressingPlan.CourseArcs(b.local);
            int inCut = 0;
            var bad = new List<string>();
            foreach (var p in rocks)
            {
                Vector3 tr = DressingPlan.TowardRoad(b.local, p.position);
                if (!GroundFit.WallSeated(p, tr, g, out string why)) bad.Add(p.group + " " + (p.part ?? "MesaB") + " @" + p.position + " " + why);
                if (Mathf.Abs(DressingPlan.NearestArc(b.local, cum, p.position, out _) - pass) <= SetPieces.PASS_HALF_CUT + 25f) inCut++;
            }
            foreach (var m in bad.Take(10)) TestContext.WriteLine(m);
            Assert.AreEqual(0, bad.Count, "道側の底が浮く・壁から張り出す・縦に伸ばした壁の岩");
            Assert.Greater(inCut, 40, "切り通しの両側の壁の岩");
            // 上の段ほど壁の奥（道から遠い）：底の高さが 8m 上がるごとに前の面は道から遠くなる
            var cut = rocks.Where(p => p.group == "WallFace" && Mathf.Abs(DressingPlan.NearestArc(b.local, cum, p.position, out _) - pass) <= SetPieces.PASS_HALF_CUT + 25f).ToList();
            float hi = 0f, lo = 0f; int nh = 0, nl = 0;
            foreach (var p in cut)
            {
                float d;
                float a = DressingPlan.NearestArc(b.local, cum, p.position, out d);
                Vector3 c = WorldLayout.PointAtArc(b.local, a);
                float rel = p.position.y - b.Ground(c.x, c.z);
                if (rel > 12f) { hi += d; nh++; } else if (rel < 6f) { lo += d; nl++; }
            }
            Assert.Greater(nh, 5); Assert.Greater(nl, 5);
            Assert.Greater(hi / nh, lo / nl + 4f, "上の段は壁の奥へ下がる（道へ張り出さない）");
        }

        // ---- 部品の取り違え ----

        [TestCaseSource(nameof(Stages))]
        public void PartsBelongToTheirPack(int i)
        {
            foreach (var p in StageAll(Stage(i)))
            {
                if (p.part == null || !p.asset.StartsWith("Packs/")) continue;
                string pack = System.IO.Path.GetFileNameWithoutExtension(p.asset);
                Assert.IsTrue(p.part.StartsWith(pack + "_"), p.group + "：" + p.asset + " に " + p.part + "（Unity では部品が見つからず箱になる）");
            }
        }
    }
}
