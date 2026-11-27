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

        static List<Placement> Lobby()
        {
            var ex = new List<Vector4> { new Vector4(0, 0, -2, 22), new Vector4(120, 0, -40, 80), new Vector4(-50, 0, -10, 26) };
            return DressingPlan.ForLobby(new Vector2(-41, 25), new Vector2(41, 189), ex, 5);
        }

        [Test] public void LobbyEdgesFilledOutsideTown()
        {
            var l = Lobby();
            TestContext.WriteLine($"lobby: {l.Count}");
            Assert.That(l.Count, Is.InRange(60, DressingPlan.MAX_LOBBY));
            foreach (var p in l)
            {
                bool inside = p.position.x > -41 && p.position.x < 41 && p.position.z > 25 && p.position.z < 189;
                Assert.IsFalse(inside);
                Assert.Greater(WorldLayout.DistXZ(p.position, new Vector3(0, 0, -2)), 22f);
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
