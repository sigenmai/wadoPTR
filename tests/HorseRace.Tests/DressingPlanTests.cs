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
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
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
                float d = WorldLayout.DistToPolylineXZ(p.position, courses[i]) - p.radius;
                bool roadside = p.group == "Roadside" || p.group == "GoalTown";
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
                    if (p.group == "GoalTown" && townArea) continue;
                    if (p.group == "GoalTown" && c.w > 20f) continue;   // 岩橋の範囲は境目に無い
                    Assert.GreaterOrEqual(WorldLayout.DistXZ(p.position, new Vector3(c.x, 0, c.z)), c.w + p.radius - 0.01f, p.group + " " + p.asset);
                }
                var sc = shortcuts[i];
                if (sc != null)
                    Assert.Greater(WorldLayout.DistToSegmentXZ(p.position, sc.a, sc.b), SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND + p.radius, p.group);
                Assert.That(p.position.x - p.radius, Is.GreaterThanOrEqualTo(DressingPlan.EDGE_MARGIN - 0.01f));
                Assert.That(p.position.x + p.radius, Is.LessThanOrEqualTo(StageDefinitions.SIZE - DressingPlan.EDGE_MARGIN + 0.01f));
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
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
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
                if (p.asset == DressingPlan.MESA || p.asset == DressingPlan.MESAB || p.asset == DressingPlan.STABLE) Assert.IsTrue(p.background);
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
            TestContext.WriteLine("min per 100 m: " + counts.Min());
            for (int k = 0; k < windows; k++) Assert.GreaterOrEqual(counts[k], DressingPlan.ROADSIDE_LARGE_PER_100M, $"window {k * 100}-{k * 100 + 100} m");
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
                // 町の外形（+1m）の外、柵の中
                Assert.Greater(WorldBounds.DistToRect(p.position, t), 1f - 1e-3f, p.asset + " " + p.position);
                Assert.That(p.position.x, Is.InRange(f.x, f.z)); Assert.That(p.position.z, Is.InRange(f.y, f.w));
                // 南の辺（広場の前）には置かない
                Assert.IsFalse(p.position.z < t.y && Mathf.Abs(p.position.x) < 40f, "南の入口の前: " + p.asset + " " + p.position);
            }
            var counts = DressingPlan.CountByAsset(l);
            foreach (string a in DressingPlan.LobbyThemeAssets) Assert.IsTrue(counts.ContainsKey(a), a);
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
