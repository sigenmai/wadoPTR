using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>
    /// 町（WesternTown.fbx）は tools/blender/split_parts.py で建物・小物ごとに別のオブジェクトに分けてある。
    /// その一覧（WesternTown.parts.json、Blender の座標：x 東西・y 南北・z 上）が、ロビーの配置の前提（LobbyLayoutTests の
    /// 測った外形・建物の箱）と合っているかを確かめる。
    /// </summary>
    public class TownPartsTests
    {
        const float STREET = 13.71f;   // 通りの高さ（LobbyLayoutTests の測り方と同じ）

        sealed class Part
        {
            public string name, kind;
            public float[] min, max;
        }

        static string Root()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "HorseRace", "Models"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir, "リポジトリのルートが見つからない");
            return dir;
        }

        static List<Part> Load(string rel)
        {
            string path = Path.Combine(Root(), "Assets", "HorseRace", "Models", rel);
            Assert.IsTrue(File.Exists(path), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = new List<Part>();
            foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
                r.Add(new Part
                {
                    name = it.GetProperty("name").GetString(),
                    kind = it.GetProperty("kind").GetString(),
                    min = it.GetProperty("bbox_min").EnumerateArray().Select(e => (float)e.GetDouble()).ToArray(),
                    max = it.GetProperty("bbox_max").EnumerateArray().Select(e => (float)e.GetDouble()).ToArray(),
                });
            return r;
        }

        /// <summary>部品の外形 → ロビー中心からの相対の長方形（minX, minZ, maxX, maxZ）。町の南端 z = 25、東西の中心 x = 0。</summary>
        static Vector4 LobbyRect(Part p, float ymin) => new Vector4(p.min[0], p.min[1] - ymin + 25f, p.max[0], p.max[1] - ymin + 25f);

        [Test] public void TownIsSplitIntoSeparateBuildings()
        {
            var parts = Load("Packs/WesternTown/WesternTown.parts.json");
            Assert.Greater(parts.Count, 100, "町が建物・小物ごとに分かれていない");
            Assert.AreEqual(parts.Count, parts.Select(p => p.name).Distinct().Count(), "部品名が重複している");
            Assert.AreEqual(1, parts.Count(p => p.kind == "Ground"));
            Assert.AreEqual(1, parts.Count(p => p.kind == "Hill"));
            Assert.GreaterOrEqual(parts.Count(p => p.kind == "Building"), 12);
            foreach (var p in parts) StringAssert.StartsWith("WesternTown_", p.name);

            // 分けても全体の外形は変わらない（LobbyLayout.TOWN_NOMINAL_RECT・柵の大きさの前提）
            float x0 = parts.Min(p => p.min[0]), x1 = parts.Max(p => p.max[0]);
            float y0 = parts.Min(p => p.min[1]), y1 = parts.Max(p => p.max[1]);
            var t = LobbyLayoutTests.TOWN_MEASURED;
            Assert.AreEqual(t.z - t.x, x1 - x0, 0.05f);
            Assert.AreEqual(t.w - t.y, y1 - y0, 0.05f);

            // 建物は 1 つずつの大きさ（隣の建物とつながっていない）：横幅 26m 以下
            foreach (var p in parts.Where(p => p.kind == "Building"))
                Assert.LessOrEqual(Mathf.Max(p.max[0] - p.min[0], p.max[1] - p.min[1]), 26f, p.name);
        }

        /// <summary>通りより 0.8m 以上高い建物・木はどれも、測った建物の箱（LobbyLayoutTests.TOWN_FOOTPRINTS）のどれかと重なり、遊びに使う場所とは重ならない。</summary>
        [Test] public void BuildingsMatchMeasuredFootprintsAndAvoidFunctionalSpots()
        {
            var parts = Load("Packs/WesternTown/WesternTown.parts.json");
            float ymin = parts.Min(p => p.min[1]);
            var tall = parts.Where(p => (p.kind == "Building" || p.kind == "Tree") && p.max[2] > STREET + 0.8f).ToList();
            Assert.Greater(tall.Count, 10);
            foreach (var p in tall)
            {
                Vector4 r = LobbyRect(p, ymin);
                bool covered = LobbyLayoutTests.TOWN_FOOTPRINTS.Any(b => r.x < b.z && b.x < r.z && r.y < b.w && b.y < r.w);
                Assert.IsTrue(covered, p.name + " " + r + " が測った建物の箱のどれとも重ならない");
                foreach (var kv in LobbyLayout.FunctionalSpots())
                {
                    var c = new Vector3(kv.Value.x, 0f, kv.Value.z);
                    if (c.z > LobbyLayout.TOWN_BACK_Z) continue;
                    Assert.Greater(WorldBounds.DistToRect(c, r), kv.Value.w, kv.Key + " が " + p.name + " " + r + " に重なる");
                }
            }
        }

        /// <summary>くっついて 1 つになっていた素材は、分けた後の部品の数（split_parts.py の一覧）。</summary>
        [TestCase("Buildings/RanchCompound.parts.json", 8)]
        [TestCase("Buildings/TownBlock.parts.json", 2)]
        [TestCase("Props/CactusGroup/CactusGroup.parts.json", 4)]
        [TestCase("Props/AcaciaGrove/AcaciaGrove.parts.json", 4)]
        [TestCase("ThirdParty/RockSetStylized/RockSetStylized.parts.json", 6)]
        public void FusedModelsAreSplit(string manifest, int minParts)
        {
            var parts = Load(manifest);
            Assert.GreaterOrEqual(parts.Count, minParts, manifest);
            Assert.AreEqual(parts.Count, parts.Select(p => p.name).Distinct().Count());
        }

        [Test] public void TownPartIds()
        {
            string id = LayoutOverrides.IdForPart("Lobby", "Town", "WesternTown", "WesternTown_012_Building", 11);
            Assert.AreEqual("Lobby/Town/WesternTown|WesternTown_012_Building#11", id);
            string name = LayoutOverrides.NameWithId("WesternTown_012_Building", id);
            Assert.AreEqual(id, LayoutOverrides.ParseId(name));
            Assert.AreEqual("WesternTown_012_Building", LayoutOverrides.StripId(name));
            Assert.AreEqual("Plain", LayoutOverrides.StripId("Plain"));
            // 計画の配置の id は今までと同じ形
            var p = new Placement { asset = "Packs/Cacti/Cacti.fbx", part = "Cacti_01", group = "Lobby" };
            Assert.AreEqual("S1/Lobby/Cacti|Cacti_01#3", LayoutOverrides.IdFor("S1", p, 3));
        }
    }
}
