using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class DressingPlanTests
    {
        static readonly Dictionary<int, List<Placement>> cache = new Dictionary<int, List<Placement>>();
        static readonly Dictionary<int, Vector3[]> courses = new Dictionary<int, Vector3[]>();
        static readonly Dictionary<int, SetPieces.ShortcutPlan> shortcuts = new Dictionary<int, SetPieces.ShortcutPlan>();
        static readonly Dictionary<int, float> goals = new Dictionary<int, float>();

        static List<Placement> Plan(int i)
        {
            if (cache.TryGetValue(i, out var p)) return p;
            var s = StageDefinitions.All[i];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var sc = SetPieces.ApplyTerrain(s, local, h, goal);
            p = DressingPlan.For(s, local, h, goal, sc);
            cache[i] = p; courses[i] = local; shortcuts[i] = sc; goals[i] = goal;
            return p;
        }

        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        [TestCaseSource(nameof(Stages))]
        public void CountWithinBudget(int i)
        {
            int n = Plan(i).Count;
            TestContext.WriteLine($"stage {i}: {n} placements");
            Assert.That(n, Is.InRange(DressingPlan.MIN_PER_STAGE, DressingPlan.MAX_PER_STAGE));
        }

        [TestCaseSource(nameof(Stages))]
        public void NothingOnTheRoad(int i)
        {
            foreach (var p in Plan(i))
            {
                // 道幅を考えた距離（絞りでは道に寄せてよい、広場では遠ざける：course-r1-10）
                float d = DressingPlan.RoadClearance(StageDefinitions.All[i], courses[i], null, p.position) - DressingPlan.FootprintRadius(p);
                bool roadside = DressingPlan.NearRoadGroups.Contains(p.group);
                Assert.GreaterOrEqual(d, (roadside ? DressingPlan.ROAD_MIN : DressingPlan.ROAD_CLEAR) - 0.01f, p.group + " " + p.part + " " + p.asset);
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void ExclusionsRespected(int i)
        {
            var plan = Plan(i);
            var s = StageDefinitions.All[i];
            var circles = DressingPlan.StageExclusions(s, courses[i], goals[i]);
            foreach (var p in plan)
            {
                foreach (var c in circles)
                {
                    bool townArea = c.w >= 100f;   // ゴールの町の範囲は町の建物だけが入れる
                    if ((p.group == "GoalTown" || p.group == "Railway") && townArea) continue;   // 線路は町へ入る（course-r1-18）
                    if (p.group == "GoalTown" && c.w > 20f) continue;   // 岩橋の範囲は境目に無い
                    if (p.group == "Landmark") continue;   // 遠くの目印は除外の円の外を自分で選ぶ（LandmarkSpot）
                    Assert.GreaterOrEqual(WorldLayout.DistXZ(p.position, new Vector3(c.x, 0, c.z)), c.w + DressingPlan.FootprintRadius(p) - 0.01f, p.group + " " + p.asset);
                }
                var sc = shortcuts[i];
                if (sc != null)
                    Assert.Greater(WorldLayout.DistToSegmentXZ(p.position, sc.a, sc.b), SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND + p.radius, p.group);
                // 尾根のシルエット（art-r2-09）は外周の尾根の背に置き、Terrain の外（外の地面）へはみ出してよい（背景専用）
                float rr = p.group == "RimSilhouette" ? 0f : p.radius;
                Assert.That(p.position.x - rr, Is.GreaterThanOrEqualTo(DressingPlan.EDGE_MARGIN - 0.01f), p.group + " " + p.asset);
                Assert.That(p.position.x + rr, Is.LessThanOrEqualTo(StageDefinitions.SIZE - DressingPlan.EDGE_MARGIN + 0.01f));
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void ThemeAssetsAllUsed(int i)
        {
            var counts = DressingPlan.CountByAsset(Plan(i));
            TestContext.WriteLine(string.Join(", ", counts.Select(kv => Path.GetFileNameWithoutExtension(kv.Key) + "=" + kv.Value)));
            foreach (string a in DressingPlan.ThemeAssets(StageDefinitions.All[i].kind))
                Assert.IsTrue(counts.ContainsKey(a), "テーマの素材が置かれていない: " + a);
        }

        [TestCaseSource(nameof(Stages))]
        public void Deterministic(int i)
        {
            var s = StageDefinitions.All[i];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s, StageDefinitions.Normalize(local));
            var sc = SetPieces.ApplyTerrain(s, local, h, goal);
            var again = DressingPlan.For(s, local, h, goal, sc);
            var first = Plan(i);
            Assert.AreEqual(first.Count, again.Count);
            for (int k = 0; k < first.Count; k++)
            {
                Assert.AreEqual(first[k].asset, again[k].asset);
                Assert.AreEqual(first[k].part, again[k].part);
                Assert.AreEqual(first[k].position.x, again[k].position.x);
                Assert.AreEqual(first[k].position.z, again[k].position.z);
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void FarBackgroundHasNoCollidersAndMesasAreBackground(int i)
        {
            foreach (var p in Plan(i))
            {
                float d = WorldLayout.DistToPolylineXZ(p.position, courses[i]) - p.radius;
                if (d > DressingPlan.REACHABLE_M) Assert.IsFalse(p.collider, p.asset);
                // メサは背景（見えない壁）。ただし道のすぐ脇の切り通しの門・谷の壁・カーブの外・台地の縁（art-r2-08）はメッシュの当たり判定、
                // 水場の厩舎（art-r2-05）は外形の当たり判定
                bool nearRock = p.group == "MesaGate" || p.group == "CanyonWalls" || p.group == "CornerSilhouette" || p.group == "MesaPlateau";
                if ((p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB || p.asset == DressingPlan.STABLE) && !nearRock && p.group != "WaterStop") Assert.IsTrue(p.background, p.group);
                if (nearRock && p.collider && p.radius >= 6f) Assert.AreEqual(DressingPlan.SHAPE_MESH, p.shape, p.group);
                if (p.height < DressingPlan.COLLIDER_MIN_H) Assert.IsFalse(p.collider, p.asset + " " + p.part);
            }
        }

        static int Count(List<Placement> l, System.Func<Placement, bool> f) { int n = 0; foreach (var p in l) if (f(p)) n++; return n; }

        [Test] public void TreesPerTheme()
        {
            var c = Plan(0); var d = Plan(1); var b = Plan(2);
            int canyonDead = Count(c, p => p.asset == DressingPlan.DEADTREE);
            int dunesDead = Count(d, p => p.asset == DressingPlan.DEADTREE);
            int oasis = Count(d, p => p.group == "Oasis" && (p.asset == DressingPlan.TREES || p.asset == DressingPlan.ACACIA));
            int border = Count(b, p => p.asset == DressingPlan.TREES || p.asset == DressingPlan.ACACIA);
            int lobby = Count(Lobby(), p => p.asset == DressingPlan.TREES || p.asset == DressingPlan.ACACIA);
            TestContext.WriteLine($"canyon dead {canyonDead}, dunes dead {dunesDead}, oasis {oasis}, border {border}, lobby {lobby}");
            Assert.GreaterOrEqual(canyonDead, DressingPlan.MIN_DEAD_TREES_CANYON);
            Assert.GreaterOrEqual(dunesDead, DressingPlan.MIN_DEAD_TREES_DUNES);
            Assert.GreaterOrEqual(oasis, DressingPlan.OASIS_TREES);
            Assert.GreaterOrEqual(border, DressingPlan.MIN_TREES_BORDER);
            Assert.GreaterOrEqual(lobby, DressingPlan.MIN_TREES_LOBBY);
            Assert.Greater(Count(d, p => p.group == "RanchWaypoint" && DressingPlan.IsTree(p)), 0);
            Assert.Greater(Count(c, p => p.group == "WagonCamp" && p.asset == DressingPlan.ACACIA), 0);
            Assert.Greater(Count(b, p => p.group == "MesaFeet" && p.asset == DressingPlan.DEADTREE), 0);
        }

        [TestCaseSource(nameof(Stages))]
        public void TreesVariedAndTrunkCollidersOnlyNearRoad(int i)
        {
            float min = 9f, max = 0f;
            foreach (var p in Plan(i))
            {
                if (!DressingPlan.IsTree(p)) continue;
                Assert.That(p.scale, Is.InRange(DressingPlan.TREE_SCALE_MIN, DressingPlan.TREE_SCALE_MAX));
                min = Mathf.Min(min, p.scale); max = Mathf.Max(max, p.scale);
                float d = WorldLayout.DistToPolylineXZ(p.position, courses[i]) - p.radius;
                if (p.collider) { Assert.AreEqual(DressingPlan.SHAPE_TRUNK, p.shape); Assert.LessOrEqual(d, DressingPlan.REACHABLE_M); }
            }
            Assert.Greater(max - min, 0.3f);
        }

        /// <summary>道沿いの帯：コース 100m ごとに、道から 60m 以内の大きな飾りが 6 個以上。</summary>
        [TestCaseSource(nameof(Stages))]
        public void RoadsideBandDensity(int i)
        {
            var plan = Plan(i);
            var course = courses[i];
            var cum = DressingPlan.CourseArcs(course);
            int windows = Mathf.CeilToInt(goals[i] / DressingPlan.BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in plan)
            {
                if (DressingPlan.IsSmall(p)) continue;
                float a = DressingPlan.NearestArc(course, cum, p.position, out float d);
                if (d - p.radius > DressingPlan.BAND_DIST_M) continue;
                int k = Mathf.FloorToInt(a / DressingPlan.BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            TestContext.WriteLine("min per 100 m: " + counts.Min() + " at " + System.Array.IndexOf(counts, counts.Min()) * 100);
            for (int k = 0; k < windows; k++) Assert.GreaterOrEqual(counts[k], DressingPlan.ROADSIDE_LARGE_PER_100M, $"window {k * 100}-{k * 100 + 100} m");
        }

        /// <summary>峡谷の近景（art-r1-02）：コース 100m ごとに、道の中心から 30m 以内の飾りが 10 個以上（岩橋の溝の周りの区間は除く）。</summary>
        [Test] public void CanyonNearFieldDensity()
        {
            var plan = Plan(0);
            var course = courses[0];
            var cum = DressingPlan.CourseArcs(course);
            float bridge = SetPieces.BridgeArc(StageDefinitions.All[0], goals[0]);
            int windows = Mathf.CeilToInt(goals[0] / DressingPlan.BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in plan)
            {
                float a = DressingPlan.NearestArc(course, cum, p.position, out float d);
                if (DressingPlan.RoadClearance(StageDefinitions.All[0], course, cum, p.position) > DressingPlan.NEAR_FIELD_M) continue;
                int k = Mathf.FloorToInt(a / DressingPlan.BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            int min = int.MaxValue;
            for (int k = 0; k < windows; k++)
            {
                float c = (k + 0.5f) * DressingPlan.BAND_WINDOW_M;
                if (Mathf.Abs(c - bridge) < SetPieces.TRENCH_HALF_LENGTH + 20f) continue;   // 岩橋（溝は SetPieceBuilder が岩で飾る）
                if (c > goals[0] - 60f) continue;                                           // ゴール前（ゲートの足元）
                min = Mathf.Min(min, counts[k]);
                Assert.GreaterOrEqual(counts[k], DressingPlan.NEAR_PER_100M, $"window {k * 100}-{k * 100 + 100} m");
            }
            TestContext.WriteLine("near-field min per 100 m: " + min);
            Assert.Greater(Count(plan, p => p.group == "CanyonFloor"), 150);
        }

        /// <summary>
        /// 谷の壁（art-r2-08）：道の近くは崖の板（Cliffs）ではなく形のある大きな岩（メサ B・DesertRocks_01〜03・岩のセット）を縦に伸ばして並べ、
        /// 崖は道から CLIFF_MIN_ROAD_M 以上（足元の縁まで）で、どれも足元に岩のすそ（3〜5 個）。
        /// </summary>
        [Test] public void CanyonWallsAreRocksAndCliffsStayFar()
        {
            var plan = Plan(0);
            var walls = plan.Where(p => p.group == "CanyonWalls" || p.group == "CornerSilhouette").ToList();
            Assert.GreaterOrEqual(walls.Count, 20);
            Assert.IsTrue(walls.All(p => p.asset != DressingPlan.CLIFFS));
            Assert.Greater(walls.Count(p => p.scaleY > 1.15f), walls.Count / 2);
            for (int i = 0; i < 3; i++)
                foreach (var p in Plan(i).Where(q => q.asset == DressingPlan.CLIFFS))
                    Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(p.position, courses[i]) - DressingPlan.FootprintRadius(p), DressingPlan.CLIFF_MIN_ROAD_M - 0.01f, p.group + " " + p.part);
            var cliffs = plan.Where(p => p.asset == DressingPlan.CLIFFS).ToList();
            Assert.GreaterOrEqual(cliffs.Count, 5);
            foreach (var c in cliffs)
                Assert.GreaterOrEqual(plan.Count(p => p.group == "CliffSkirt" && WorldLayout.DistXZ(p.position, c.position) < c.radius * 0.8f), 3, c.part);
        }

        /// <summary>遠くの目印（art-r1-10）：どのステージもスタートで進む向き ±35° にあり、コースから 60m 以上離れ、計画に入っている。</summary>
        [TestCaseSource(nameof(Stages))]
        public void LandmarkAheadAtStart(int i)
        {
            var plan = Plan(i);
            var s = StageDefinitions.All[i];
            Assert.IsTrue(DressingPlan.LandmarkSpot(s, courses[i], goals[i], out Vector3 spot, out float ang));
            Vector3 p0 = WorldLayout.PointAtArc(courses[i], StageDefinitions.START_ARC_M), t0 = WorldLayout.TangentAtArc(courses[i], StageDefinitions.START_ARC_M);
            Vector3 dir = spot - p0;
            float a = Mathf.Abs(Mathf.DeltaAngle(WorldLayout.YawDeg(t0), WorldLayout.YawDeg(new Vector3(dir.x, 0f, dir.z).normalized)));
            TestContext.WriteLine($"stage {i}: landmark {spot} angle {a:F1}, dist {WorldLayout.DistXZ(spot, p0):F0}");
            Assert.LessOrEqual(a, DressingPlan.LANDMARK_MAX_ANGLE);
            Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(spot, courses[i]), DressingPlan.LANDMARK_MIN_ROAD_M - 0.01f);
            Assert.AreEqual(1, Count(plan, p => p.group == "Landmark"));
            var lm = plan.First(p => p.group == "Landmark");
            Assert.Less(WorldLayout.DistXZ(lm.position, spot), 0.01f);
            Assert.Greater(lm.height, 15f);
        }

        /// <summary>道の目印（art-r1-10）：急なカーブの外側に杭・柵の列が続き、直線には標柱が立つ。どれも平らな道の外。</summary>
        [TestCaseSource(nameof(Stages))]
        public void GuideLinesTraceCorners(int i)
        {
            var plan = Plan(i);
            var course = courses[i];
            var guides = plan.Where(p => p.group == "GuideLine").ToList();
            TestContext.WriteLine($"stage {i}: guide {guides.Count}");
            Assert.Greater(guides.Count, 40);
            int corners = 0, traced = 0;
            for (float a = 40f; a < goals[i] - 40f; a += 12f)
            {
                float k = DressingPlan.Curvature(course, a);
                if (Mathf.Abs(k) <= DressingPlan.GUIDE_CURVATURE * 1.2f) continue;
                corners++;
                Vector3 outside = WorldLayout.PointAtArc(course, a) + WorldLayout.RightOf(WorldLayout.TangentAtArc(course, a)) * (k > 0f ? 1f : -1f) * DressingPlan.GUIDE_LATERAL;
                if (guides.Any(g => WorldLayout.DistXZ(g.position, outside) < 9f)) traced++;
            }
            TestContext.WriteLine($"corners {corners}, traced {traced}");
            Assert.Greater(corners, 5);
            Assert.GreaterOrEqual(traced, corners * 0.7f);
            Assert.Greater(guides.Count(g => g.part == "DesertProps_05"), 5);   // 直線の標柱
        }

        /// <summary>砂漠の廃線（art-r1-10）：線路 3 本、コースから 30m 以上、線路の上に他の飾りが無い。</summary>
        [Test] public void DunesRailwayRuns()
        {
            var plan = Plan(1);
            var rails = plan.Where(p => p.group == "Railway" && p.part == DressingPlan.RAIL_TRACK).ToList();
            Assert.AreEqual(SetPieces.RAIL_RUNS, rails.Count);
            var runs = SetPieces.RailRuns(StageDefinitions.All[1], courses[1], goals[1]);
            foreach (var r in rails)
            {
                Assert.GreaterOrEqual(WorldLayout.DistToPolylineXZ(r.position, courses[1]), 30f);
                Assert.IsFalse(r.collider);
            }
            foreach (var p in plan)
            {
                if (p.group == "Railway") continue;
                foreach (var run in runs)
                    Assert.Greater(WorldLayout.DistToSegmentXZ(p.position, run[0], run[1]), 3f, p.group + " " + p.asset + " が線路の上");
            }
        }

        /// <summary>境目の切り通し・ゴールの町（art-r1-09）：メサの門が切り通しの両脇、ゴールの先 +15〜+110m の通りの両脇に建物、通りの奥に給水塔。</summary>
        [Test] public void BorderPassGateAndGoalStreet()
        {
            var plan = Plan(2);
            var s = StageDefinitions.All[2];
            var course = courses[2];
            float pass = SetPieces.PassArc(s, goals[2]);
            Assert.Greater(pass, 0f);
            var gate = plan.Where(p => p.group == "MesaGate").ToList();
            Assert.AreEqual(2, gate.Count);
            var cum = DressingPlan.CourseArcs(course);
            int left = 0, right = 0;
            foreach (var g in gate)
            {
                float a = DressingPlan.NearestArc(course, cum, g.position, out float d);
                Assert.Less(Mathf.Abs(a - pass), 40f);
                Vector3 rv = WorldLayout.RightOf(WorldLayout.TangentAtArc(course, a));
                Vector3 q = g.position - WorldLayout.PointAtArc(course, a);
                if (q.x * rv.x + q.z * rv.z > 0f) right++; else left++;
            }
            Assert.AreEqual(1, left); Assert.AreEqual(1, right);
            // 最後の 260m は町の大通り（course-r1-12）：ゴールの 260m 手前〜60m 先、両脇に建物
            int left2 = 0, right2 = 0, before = 0;
            foreach (var p in plan)
            {
                bool building = p.part != DressingPlan.RANCH_TOWER && (p.asset.StartsWith("Buildings/") || (p.part != null && p.part.EndsWith("_Building")));
                if (p.group != "GoalTown" || !building) continue;
                float a = DressingPlan.NearestArc(course, cum, p.position, out float d);
                Assert.That(a - goals[2], Is.InRange(DressingPlan.GOAL_STREET_FROM - 10f, DressingPlan.GOAL_STREET_TO + 10f));
                Vector3 rv = WorldLayout.RightOf(WorldLayout.TangentAtArc(course, a));
                Vector3 q = p.position - WorldLayout.PointAtArc(course, a);
                if (q.x * rv.x + q.z * rv.z > 0f) right2++; else left2++;
                if (a < goals[2] - 100f) before++;
            }
            TestContext.WriteLine($"street buildings left {left2} right {right2}, before goal-100 {before}");
            Assert.GreaterOrEqual(left2, 8); Assert.GreaterOrEqual(right2, 8);
            Assert.GreaterOrEqual(before, 8);
            Assert.Greater(Count(plan, p => p.group == "GoalTown" && p.asset == DressingPlan.HAY), 8);
            var tower = plan.Single(p => p.group == "GoalTown" && p.part == DressingPlan.RANCH_TOWER);
            Assert.Greater(WorldLayout.DistXZ(tower.position, course[course.Length - 1]), 20f);
        }

        [TestCaseSource(nameof(Stages))]
        public void SmallPropsCappedAndOnlyInClusters(int i)
        {
            var l = Plan(i);
            int small = Count(l, DressingPlan.IsSmall);
            TestContext.WriteLine($"small {small}/{l.Count}");
            Assert.LessOrEqual(small, DressingPlan.SMALL_SHARE_MAX * l.Count);
            foreach (var p in l)
                if (DressingPlan.IsSmall(p)) Assert.IsTrue(DressingPlan.ClusterGroups.Contains(p.group), p.group + " " + p.asset + " " + p.part);
        }

        static List<Placement> Lobby()
        {
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            return DressingPlan.ForLobby(new Vector2(t.x, t.y), new Vector2(t.z, t.w), LobbyLayout.LobbyDressingExclusions(), 5);
        }

        /// <summary>ロビーの飾りは町と柵の間の帯だけ：町の中（通り・広場・練習場）にも、南の入口の前にも置かない。</summary>
        [Test] public void LobbyEdgesFilledOutsideTown()
        {
            var l = Lobby();
            TestContext.WriteLine($"lobby: {l.Count}");
            Assert.That(l.Count, Is.InRange(50, DressingPlan.MAX_LOBBY));
            var t = LobbyLayout.TOWN_NOMINAL_RECT;
            var f = LobbyLayout.FENCE_RECT;
            foreach (var p in l)
            {
                if (p.group == "Backlot")
                {
                    // 町の裏（線路と東の通り道の間）。木は当たり判定なし
                    Assert.That(p.position.x, Is.InRange(22.2f, 23.0f));
                    if (DressingPlan.IsTree(p)) Assert.IsFalse(p.collider);
                    continue;
                }
                // 町の外形（+1m）の外、柵の中
                Assert.Greater(WorldBounds.DistToRect(p.position, t), 1f - 1e-3f, p.asset + " " + p.position);
                Assert.That(p.position.x, Is.InRange(f.x, f.z)); Assert.That(p.position.z, Is.InRange(f.y, f.w));
                // 南の辺（広場の前）には置かない
                Assert.IsFalse(p.position.z < t.y && Mathf.Abs(p.position.x) < 40f, "南の入口の前: " + p.asset + " " + p.position);
            }
            var counts = DressingPlan.CountByAsset(l);
            foreach (string a in DressingPlan.LobbyThemeAssets) Assert.IsTrue(counts.ContainsKey(a), a);
            // 町の裏の飾り：木 4 本以上と、柵・干し草・樽・薪
            Assert.GreaterOrEqual(Count(l, p => p.group == "Backlot" && p.asset == DressingPlan.TREES), 4);
            Assert.GreaterOrEqual(Count(l, p => p.group == "Backlot"), 12);
        }

        /// <summary>Assets/HorseRace/Models の FBX（Raptor は Phase 2 なので除く）はすべてどこかで使う。</summary>
        [Test] public void EveryPreparedModelIsUsed()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "HorseRace", "Models"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir, "リポジトリのルートが見つからない");
            string models = Path.Combine(dir, "Assets", "HorseRace", "Models");
            var used = new HashSet<string>(DressingPlan.UsedElsewhere);
            for (int i = 0; i < 3; i++) foreach (string a in DressingPlan.CountByAsset(Plan(i)).Keys) used.Add(a);
            foreach (string a in DressingPlan.CountByAsset(Lobby()).Keys) used.Add(a);
            var files = Directory.GetFiles(models, "*.fbx", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(models, f).Replace('\\', '/'))
                .Where(f => !f.StartsWith("Raptor/")).ToList();
            Assert.Greater(files.Count, 30);
            foreach (string f in files) Assert.IsTrue(used.Contains(f), "使われていない素材: " + f);
        }
    }
}
