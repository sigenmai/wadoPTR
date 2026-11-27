using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    public class LayoutOverridesTests
    {
        static List<LayoutItem> Plan() => new List<LayoutItem>
        {
            new LayoutItem { id = "S1/A/Rock|R_01#0", position = new Vector3(1, 0, 1), euler = new Vector3(0, 10, 0), scale = Vector3One(1) },
            new LayoutItem { id = "S1/A/Rock|R_02#1", position = new Vector3(5, 0, 5), euler = new Vector3(0, 20, 0), scale = Vector3One(1.2f) },
            new LayoutItem { id = "S1/B/Tree|#2", position = new Vector3(9, 1, 9), euler = new Vector3(0, 30, 85), scale = Vector3One(0.9f) },
        };

        static Vector3 Vector3One(float s) => new Vector3(s, s, s);

        [Test] public void ApplyMovesRotatesScalesAndDeletes()
        {
            var o = new List<LayoutOverride>
            {
                new LayoutOverride { id = "S1/A/Rock|R_01#0", position = new Vector3(2, 0, 3), euler = new Vector3(0, 45, 0), scale = Vector3One(2) },
                new LayoutOverride { id = "S1/B/Tree|#2", deleted = true },
                new LayoutOverride { id = "S9/Gone|#7", position = Vector3.zero },
            };
            var r = LayoutOverrides.Apply(Plan(), o, out var unknown, out int moved, out int deleted);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(1, moved); Assert.AreEqual(1, deleted);
            Assert.AreEqual(2f, r[0].position.x); Assert.AreEqual(45f, r[0].euler.y); Assert.AreEqual(2f, r[0].scale.x);
            Assert.AreEqual(5f, r[1].position.x);   // 手直しなし
            CollectionAssert.AreEqual(new[] { "S9/Gone|#7" }, unknown);
        }

        [Test] public void DiffThenApplyReproducesTheScene()
        {
            var plan = Plan();
            var scene = new List<LayoutItem>
            {
                new LayoutItem { id = plan[0].id, position = plan[0].position, euler = plan[0].euler, scale = plan[0].scale },          // 同じ
                new LayoutItem { id = plan[1].id, position = new Vector3(6, 0.5f, 4), euler = new Vector3(0, 380, 0), scale = Vector3One(1.5f) },   // 動かした
                // plan[2] は削除
                new LayoutItem { id = "Custom", position = Vector3.zero },
            };
            var d = LayoutOverrides.Diff(plan, scene);
            Assert.AreEqual(2, d.Count);
            Assert.IsTrue(d.Any(x => x.id == plan[2].id && x.deleted));
            var r = LayoutOverrides.Apply(plan, d, out var unknown, out _, out _);
            Assert.AreEqual(0, unknown.Count);
            Assert.AreEqual(2, r.Count);
            Assert.IsTrue(LayoutOverrides.SameTransform(r[1], scene[1].position, scene[1].euler, scene[1].scale));
        }

        [Test] public void SmallDifferencesAreIgnoredAndAnglesWrap()
        {
            var p = Plan()[0];
            Assert.IsTrue(LayoutOverrides.SameTransform(p, p.position + new Vector3(0.005f, 0, 0), new Vector3(0, 370f, 0), p.scale));
            Assert.IsFalse(LayoutOverrides.SameTransform(p, p.position + new Vector3(0.05f, 0, 0), p.euler, p.scale));
        }

        [Test] public void JsonRoundTrip()
        {
            var o = new List<LayoutOverride>
            {
                new LayoutOverride { id = "Lobby/TownEdge/FarmProps|FarmProps_03#12", position = new Vector3(1.25f, -0.5f, 1e-3f), euler = new Vector3(0, 359.9f, 0), scale = new Vector3(1, 1.6f, 1) },
                new LayoutOverride { id = "Weird \"name\" \\ path", deleted = true },
            };
            var back = LayoutOverrides.FromJson(LayoutOverrides.ToJson(o));
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(o[0].id, back[0].id);
            Assert.AreEqual(o[0].position.x, back[0].position.x);
            Assert.AreEqual(o[0].position.z, back[0].position.z);
            Assert.AreEqual(o[0].euler.y, back[0].euler.y);
            Assert.AreEqual(1.6f, back[0].scale.y);
            Assert.AreEqual(o[1].id, back[1].id);
            Assert.IsTrue(back[1].deleted);
            Assert.Throws<System.FormatException>(() => LayoutOverrides.FromJson("{\"items\": [ {\"deleted\": true} ]}"));
        }

        [Test] public void DuplicateIdsGetSuffix()
        {
            var taken = new HashSet<string> { "Lobby/A|#1", "Lobby/A|#1~2" };
            Assert.AreEqual("Lobby/A|#1~3", LayoutOverrides.UniqueId("Lobby/A|#1", taken));
            Assert.AreEqual("Lobby/B|#1", LayoutOverrides.UniqueId("Lobby/B|#1", taken));
        }

        [Test] public void BrokenIdSuffixDetected()
        {
            Assert.IsTrue(LayoutOverrides.LooksLikeBrokenId("Tent {S2/Oasis/Tent|#3} (1)"));   // Ctrl+D
            Assert.IsTrue(LayoutOverrides.LooksLikeBrokenId("Tent {S2/Oasis/Tent|#3}x"));
            Assert.IsFalse(LayoutOverrides.LooksLikeBrokenId("Tent {S2/Oasis/Tent|#3}"));
            Assert.IsFalse(LayoutOverrides.LooksLikeBrokenId("MyRock"));
        }

        [Test] public void SameOverridesIgnoresOrderAndTinyDifferences()
        {
            var a = new List<LayoutOverride>
            {
                new LayoutOverride { id = "x", position = new Vector3(1, 2, 3), euler = new Vector3(0, 10, 0), scale = Vector3One(1) },
                new LayoutOverride { id = "y", deleted = true },
            };
            var b = new List<LayoutOverride>
            {
                new LayoutOverride { id = "y", deleted = true },
                new LayoutOverride { id = "x", position = new Vector3(1.004f, 2, 3), euler = new Vector3(0, 370, 0), scale = Vector3One(1) },
            };
            Assert.IsTrue(LayoutOverrides.SameOverrides(a, b));
            b[1].position = new Vector3(2, 2, 3);
            Assert.IsFalse(LayoutOverrides.SameOverrides(a, b));
            Assert.IsFalse(LayoutOverrides.SameOverrides(a, new List<LayoutOverride> { a[0] }));
            Assert.IsTrue(LayoutOverrides.SameOverrides(null, new List<LayoutOverride>()));
        }

        [Test] public void NameCarriesId()
        {
            string id = "S2/Oasis/Tent|#3";
            Assert.AreEqual(id, LayoutOverrides.ParseId(LayoutOverrides.NameWithId("Tent", id)));
            Assert.IsNull(LayoutOverrides.ParseId("Tent"));
        }

        /// <summary>同じ入力なら id は同じ・重複しない（ステージの計画で確認）。</summary>
        [Test] public void IdsStableAndUniqueForSameInputs()
        {
            var s = StageDefinitions.All[1];
            var local = StageDefinitions.SampleLocal(s);
            float goal = StageDefinitions.GoalArc(s, local);
            var h = HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, s.kind, s.seed, StageDefinitions.Normalize(local));
            var a = LayoutOverrides.IdsFor("S2", DressingPlan.For(s, local, h, goal, null));
            var b = LayoutOverrides.IdsFor("S2", DressingPlan.For(s, local, h, goal, null));
            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(a.Count, a.Distinct().Count());
            var belt = LayoutOverrides.IdsFor("Belt", LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT, 3));
            Assert.AreEqual(belt.Count, belt.Distinct().Count());
            Assert.IsTrue(belt.All(x => x.StartsWith("Belt/TreeBelt/")));
        }
    }
}
