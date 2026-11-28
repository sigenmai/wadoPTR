using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>コース構成 round 3（course-r3-01〜15、docs/research/review-log.md）。</summary>
    public class CourseRound3Tests
    {
        static ArtRound2Tests.Built Stage(int i) => ArtRound2Tests.Stage(i);
        static IEnumerable<int> Stages() { yield return 0; yield return 1; yield return 2; }

        // ---- course-r3-09：スタートの 200m は広く平ら（8 頭が横に広がってから最初の見どころ）----
        [TestCaseSource(nameof(Stages))]
        public void StartIsOpen(int i)
        {
            var def = StageDefinitions.All[i];
            float h0 = HeightmapGen.PathHeight(def, 0f) * HeightmapGen.HEIGHT_M;
            for (float a = 0f; a <= StageDefinitions.START_OPEN_M; a += 2f)
            {
                Assert.GreaterOrEqual(StageDefinitions.HalfWidthAt(def, a), StageDefinitions.START_OPEN_MIN_HALF_M, "arc " + a);
                float dh = HeightmapGen.PathHeight(def, a) * HeightmapGen.HEIGHT_M - h0;
                Assert.LessOrEqual(Mathf.Abs(dh), StageDefinitions.START_OPEN_MAX_RISE_M, "arc " + a);
            }
            // 8 頭（18.2m）が余裕で並ぶ。スタートの門は道幅いっぱい
            Assert.Greater(2f * StageDefinitions.START_HALF_M, StageDefinitions.LANES * StageDefinitions.LANE_SPACING_M + 8f);
            Assert.GreaterOrEqual(CourseGeometry.START_GATE_PIECES * DressingPlan.RAIL_LEN * 0.5f, StageDefinitions.START_HALF_M);
            Assert.Greater(CourseGeometry.START_SIDE, StageDefinitions.START_HALF_M);
        }

        // ---- course-r3-10：境目は前半から起伏（段・切り通しの高み）。どの坂も 7° 以下 ----
        [Test] public void MesaHasTerrainRhythmBeforeTheClimb()
        {
            var def = StageDefinitions.All[2];
            float climb = def.mesaClimbFraction * Stage(2).goal;
            int rises = 0;
            for (int k = 1; k < def.elevArc.Length; k++)
            {
                float run = def.elevArc[k] - def.elevArc[k - 1], rise = def.elevM[k] - def.elevM[k - 1];
                if (run > 1f && def.elevArc[k] <= climb) Assert.LessOrEqual(Mathf.Atan(Mathf.Abs(rise) / run) * Mathf.Rad2Deg, 7.1f, "key " + k);   // 台地の登り（1420〜1680、32m）は前のまま 7.02°。台地から下りる全力の段は対象外
                if (rise >= 5f && def.elevArc[k] < climb - 200f) rises++;
            }
            Assert.GreaterOrEqual(rises, 2, "台地の登りの前に 2 回以上の上り");
            float pass = SetPieces.PassArc(def, Stage(2).goal);
            Assert.GreaterOrEqual(StageDefinitions.ElevationAt(def, pass), 6f, "切り通しは高みを割る");
        }

        // ---- course-r3-01：ゴール・区間の門の横木は細い（引き伸ばした板にしない）----
        static void CheckBeams(List<CourseMarks.GatePart> parts, float span, float maxScale)
        {
            var beams = parts.Where(p => p.part == "WoodLogs_01" && Mathf.Abs(p.euler.z) < 1e-3f && Mathf.Abs(p.euler.x) < 1e-3f).ToList();
            Assert.Greater(beams.Count, 10);
            foreach (var b in beams)
                Assert.LessOrEqual(Mathf.Max(b.scale.x, Mathf.Max(b.scale.y, b.scale.z)), maxScale, "横木の丸太の拡大");
            // 1 段の長さ（両端の丸太の外の端）が span ±1m
            float top = beams.Max(b => b.position.y);
            var row = beams.Where(b => Mathf.Abs(b.position.y - top) < 0.05f).ToList();
            float len = CourseMarks.WOODLOG_01_LEN * row[0].scale.x;
            float ext = 0f;
            foreach (var a in row) foreach (var b in row) ext = Mathf.Max(ext, WorldLayout.DistXZ(a.position, b.position));
            Assert.AreEqual(span, ext + len, 1f, "横木の長さ");
            // 隣どうしは重なる（すき間なし）
            var xs = row.Select(b => b.position.x).OrderBy(x => x).ToList();
            for (int k = 1; k < xs.Count; k++) Assert.LessOrEqual(xs[k] - xs[k - 1], len);
        }

        [Test] public void GoalGateBeamsAreThinLogs()
        {
            var parts = CourseMarks.GoalGateParts(new Vector3(5f, 2f, 7f), new Vector3(0f, 0f, 1f), p => new Vector3(p.x, 2f, p.z));
            CheckBeams(parts, CourseGeometry.GATE_HALF * 2f + CourseMarks.GATE_POST_THICK, CourseMarks.GATE_POST_THICK);
            var beams = parts.Where(p => p.part == "WoodLogs_01" && Mathf.Abs(p.euler.z) < 1e-3f).ToList();
            Assert.AreEqual(2, beams.Select(b => Mathf.Round(b.position.y * 10f)).Distinct().Count(), "2 段（GATE_BEAM_GAP）");
            Assert.AreEqual(CourseMarks.GATE_BEAM_PIECES * CourseMarks.GATE_BEAM_TIERS, beams.Count);
            Assert.That(CourseMarks.GATE_BEAM_PIECES, Is.InRange(18, 28));
        }

        [Test] public void SectorGateBeamsAreThinLogs()
        {
            var b = Stage(1);
            var posts = CourseMarks.SectorGatePosts(b.def, b.local, b.goal);
            for (int k = 0; k + 1 < posts.Count; k += 2)
            {
                Vector3 l = posts[k], r = posts[k + 1];
                Vector3 d = (r - l).normalized * 0.55f;
                var parts = CourseMarks.SectorBeamPieces(l - d, r + d, 5.6f);
                CheckBeams(parts, WorldLayout.DistXZ(l, r) + 1.1f, 1.1f);
            }
        }

        // ---- course-r3-04：岩の橋の遺物は背の縁の際（危ない線）、溝は 60m 先から読める ----
        [Test] public void BridgeRelicSitsOnTheRiskyEdge()
        {
            var b = Stage(0);
            float bridge = SetPieces.BridgeArc(b.def, b.goal);
            var r = SetPieces.RelicSpots(b.def, b.local, b.goal, b.sc)[1];
            Assert.AreEqual(bridge, r.arc, 0.01f);
            Assert.GreaterOrEqual(Mathf.Abs(r.side), SetPieces.SPINE_HALF - 2f, "真ん中ではなく縁");
            Assert.Less(Mathf.Abs(r.side), SetPieces.SPINE_HALF, "背の上（落ちない）");
            Assert.GreaterOrEqual(SetPieces.TRENCH_HALF_WIDTH, 9f);
            // 背の近くの縁の岩は 1 つおき
            int kept = 0, all = 0;
            int li = 0;
            for (float lat = SetPieces.SPINE_HALF + 2.5f; lat < SetPieces.LIP_THIN_LAT; lat += SetPieces.LIP_ROCK_STEP, li++) { all++; if (SetPieces.LipRockKept(lat, li)) kept++; }
            Assert.LessOrEqual(kept, (all + 1) / 2);
            Assert.AreEqual(4, SetPieces.SpineEdgeLamps().Length);
            // art-r6-13：灯りは背の上ではなく両端の橋台（背の端より先）
            foreach (var m in SetPieces.SpineEdgeLamps()) Assert.Greater(Mathf.Abs(m.x), SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND);
        }

        // ---- course-r3-05：近い柵は途切れない（当たる壁は必ず見える）----
        [TestCaseSource(nameof(Stages))]
        public void NearBarrierIsContinuous(int i)
        {
            var b = Stage(i);
            var bp = CourseBarrier.For(b.def, b.local, b.goal, b.sc);
            var line = DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, bp);
            float gap = DressingPlan.NearFenceMaxGap(b.def, b.local, b.goal, bp, line);
            int panels = line.Count(p => p.part == "FarmProps_03"), posts = line.Count(p => p.part == DressingPlan.POST_PART);
            TestContext.WriteLine($"stage {i}: {line.Count} marks, {panels} fence panels, {posts} posts, max gap {gap:F2} m at {DressingPlan.WorstGapAt} arc {DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), DressingPlan.WorstGapAt, out float dd):F0} d {dd:F1}");
            Assert.LessOrEqual(gap, DressingPlan.FENCE_MAX_GAP_M);
            Assert.LessOrEqual(line.Count, DressingPlan.BARRIER_BUDGET);
            // art-r5-02：杭は柵 8 枚ごと、18° より急な斜面には立てない（白い歯の列にしない）
            Assert.Greater(posts, 0);
            Assert.LessOrEqual(posts, panels / DressingPlan.BARRIER_POST_EVERY + 60);
            Assert.IsFalse(line.Any(p => p.part == "DesertProps_05" || p.part == "FarmProps_04"));
            Assert.Greater(line.Count(p => p.asset == DressingPlan.RCOLL), 10, "遠い柵は岩の山");
        }

        // ---- course-r3-06：骨（DesertProps_05 の大腿骨）は骨のまとまりだけ。目印の杭は丸太 ----
        [TestCaseSource(nameof(Stages))]
        public void NoFemurMarkers(int i)
        {
            var b = Stage(i);
            var all = new List<Placement>(b.plan);
            all.AddRange(DressingPlan.BarrierLine(b.def, b.local, b.h, b.goal, CourseBarrier.For(b.def, b.local, b.goal, b.sc)));
            Assert.AreEqual(0, all.Count(p => p.part == "DesertProps_05" && !DressingPlan.BoneGroups.Contains(p.group)));
            Assert.Greater(b.plan.Count(p => p.group == "GuideLine" && p.part == DressingPlan.POST_PART && Mathf.Abs(p.height - DressingPlan.POST_H) < 0.01f), 5, "直線の標柱（札を打つ丸太）");
        }

        // ---- course-r3-07：急カーブの札は柵の線の上で騎手を向き、外側に干し草の壁 ----
        [TestCaseSource(nameof(Stages))]
        public void CornersReadFromTheSaddle(int i)
        {
            var b = Stage(i);
            var boards = CourseMarks.CornerBoards(b.def, b.local, b.goal);
            var big = boards.Where(x => x.chevrons <= 2).ToList();
            Assert.Greater(big.Count, 0);
            foreach (var x in big)
            {
                Assert.AreEqual(CourseMarks.CORNER_BOARD_BIG, x.scale, 1e-4f);
                float dc;
                float arc = DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), x.position, out dc);
                float fence = CourseBarrier.OffsetAtArc(b.def, b.local, b.goal, arc);
                Assert.LessOrEqual(dc, fence + 0.5f, "柵の内側");
                Assert.GreaterOrEqual(dc, Mathf.Min(fence - CourseMarks.CORNER_BOARD_FENCE_IN_M - 1.5f, StageDefinitions.HalfWidthAt(b.def, arc) + CourseMarks.CORNER_BOARD_OUT_M - 0.5f), "柵の線の近く");
            }
            foreach (var c in StageDefinitions.TightCorners(b.def, b.local))
            {
                var wall = CourseMarks.CornerWall(b.def, b.local, c);
                Assert.GreaterOrEqual(wall.Count, (int)((CourseMarks.CORNER_WALL_FROM_M + CourseMarks.CORNER_WALL_TO_M) / CourseMarks.CORNER_WALL_STEP_M));
                for (int k = 1; k < wall.Count; k++) Assert.LessOrEqual(WorldLayout.DistXZ(wall[k].position, wall[k - 1].position), CourseMarks.CORNER_WALL_STEP_M + 0.5f, "途切れない壁");
                foreach (var wb in wall)
                {
                    float dc;
                    float arc = DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), wb.position, out dc);
                    Assert.Greater(dc, StageDefinitions.HalfWidthAt(b.def, arc) + 0.5f, "道の外");
                }
            }
        }

        // ---- course-r3-08：ゴール前 200m の見物の直線 ----
        [TestCaseSource(nameof(Stages))]
        public void FinishChuteFramesTheLastStraight(int i)
        {
            var b = Stage(i);
            var parts = CourseMarks.FinishChute(b.def, b.local, b.goal);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Assert.LessOrEqual(CourseMarks.ChuteRailMaxGap(parts, sd), DressingPlan.FENCE_MAX_GAP_M, "レールのすき間 side " + sd);
                Assert.Greater(parts.Count(p => p.kind == CourseMarks.CHUTE_RAIL && p.side == sd) * DressingPlan.RAIL_LEN, 120f, "レールの長さ side " + sd);
            }
            Assert.GreaterOrEqual(parts.Count(p => p.kind == CourseMarks.CHUTE_LAMP), 8);
            Assert.GreaterOrEqual(parts.Count(p => p.kind == CourseMarks.CHUTE_FLAG), 8);   // art-r6-12：旗は 2 本に 1 本
            Assert.AreEqual(1, parts.Count(p => p.kind == CourseMarks.CHUTE_SIGN));
            if (b.def.kind != HeightmapGen.KIND_MESA) Assert.GreaterOrEqual(parts.Count(p => p.kind == CourseMarks.CHUTE_HAY), 20);
            foreach (var p in parts)
            {
                Assert.That(p.arc, Is.InRange(b.goal - CourseMarks.CHUTE_FROM_M - 1f, b.goal));
                float dc;
                float arc = DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), p.position, out dc);
                Assert.Greater(dc, StageDefinitions.HalfWidthAt(b.def, arc) + 0.5f, "道の外 " + p.kind);
            }
            // 見物の直線は置かない円の外：散らした飾りが直線をふさがない
            var circles = DressingPlan.StageExclusions(b.def, b.local, b.goal);
            Assert.IsFalse(circles.Any(c => c.w > 100f), "境目のゴールの町の円は 60m に縮めた");
        }

        // ---- course-r3-12：段の付いた八角の MesaRock は 250m より遠くだけ ----
        [TestCaseSource(nameof(Stages))]
        public void MesaRockOnlyFarAway(int i)
        {
            var b = Stage(i);
            foreach (var p in b.plan.Where(p => p.asset == DressingPlan.MESA))
                Assert.Greater(WorldLayout.DistToPolylineXZ(p.position, b.local), DressingPlan.MESA_ROCK_MIN_DIST_M, p.group);
        }

        // ---- course-r3-02：切り通しは手前から見える（背が無い）、門の岩・中の灯り・先の目印 ----
        [Test] public void PassIsVisibleOnApproach()
        {
            var b = Stage(2);
            float pass = SetPieces.PassArc(b.def, b.goal);
            float crest = SetPieces.MaxCrestAboveChord(SetPieces.PassApproachProfile(b.h, b.local, pass));
            TestContext.WriteLine($"crest before the pass: {crest:F2} m");
            Assert.LessOrEqual(crest, SetPieces.PASS_MAX_CREST_M);
            Assert.AreEqual(25f, SetPieces.PASS_HALF_CUT, 1e-4f);
            Assert.GreaterOrEqual(SetPieces.PASS_TAPER_M, 60f, "6m を 60m 以上かけて下げる（6° 未満）");
            Assert.AreEqual(1f, SetPieces.MaxCrestAboveChord(new[] { 0f, 1f, 0f }), 1e-4f);
            Assert.AreEqual(0f, SetPieces.MaxCrestAboveChord(new[] { 0f, -1f, 0f }), 1e-4f);
            int lamps = b.plan.Count(p => p.group == "PassLamp");
            Assert.GreaterOrEqual(lamps, 6, "切り通しの中の街灯");
            Assert.AreEqual(1, b.plan.Count(p => p.group == "PassBeacon" && p.part == DressingPlan.RANCH_TOWER), "出口の先の給水塔");
            Assert.AreEqual(1, b.plan.Count(p => p.group == "PassBeacon" && p.asset == DressingPlan.CAMPFIRE), "煙の柱");
            var tower = b.plan.Single(p => p.group == "PassBeacon" && p.part == DressingPlan.RANCH_TOWER);
            float dc;
            float ta = DressingPlan.NearestArc(b.local, DressingPlan.CourseArcs(b.local), tower.position, out dc);
            Vector3 ep = WorldLayout.PointAtArc(b.local, pass + SetPieces.PASS_HALF_CUT);
            Assert.That(WorldLayout.DistXZ(ep, tower.position), Is.InRange(DressingPlan.PASS_BEACON_FROM_M - 25f, DressingPlan.PASS_BEACON_TO_M + 25f));
            Assert.GreaterOrEqual(b.plan.Count(p => p.group == "MesaGate" && p.asset == DressingPlan.DROCKS && p.scaleY > 1.5f), 2, "門の岩");
        }

        // ---- course-r3-03：境目の壁と台地の縁に段の付いた岩（当たりは柵）----
        [Test] public void MesaWallsHaveStrata()
        {
            var b = Stage(2);
            var wall = b.plan.Where(p => p.group == "WallFace").ToList();
            TestContext.WriteLine($"wall face pieces: {wall.Count}");
            Assert.That(wall.Count, Is.InRange(80, 320));   // art-r4-04：6m ごと
            Assert.IsTrue(wall.All(p => !p.collider), "当たりは柵");
            Assert.AreEqual(0, wall.Count(p => p.asset.StartsWith("ThirdParty/RockGray/")), "art-r4-04：浮いて見えた RockGray はやめた");
            Assert.Greater(wall.Count(p => p.asset == DressingPlan.MESAB), 10);
            Assert.Greater(wall.Count(p => p.asset == DressingPlan.RSTYL), 5, "がれき");
            Assert.IsFalse(wall.Any(p => p.asset == DressingPlan.CLIFFS), "崖の板は近くに置かない（art-r3-05）");
            Assert.LessOrEqual(b.plan.Count, DressingPlan.MAX_PER_STAGE);
        }

        // ---- course-r3-14：スタート・ゴールの線は地形の塗り（道を横切る 2m の帯）----
        [TestCaseSource(nameof(Stages))]
        public void FinishAndStartLinesArePainted(int i)
        {
            var b = Stage(i);
            int res = StageDefinitions.ALPHAMAP_RES;
            var m = HeightmapGen.LineMask(res, b.def, b.local, b.goal);
            float W(Vector3 p) => m[Mathf.Clamp(Mathf.RoundToInt(p.z / StageDefinitions.SIZE * (res - 1)), 0, res - 1), Mathf.Clamp(Mathf.RoundToInt(p.x / StageDefinitions.SIZE * (res - 1)), 0, res - 1)];
            foreach (float a in new[] { StageDefinitions.START_ARC_M + 3f, b.goal })
            {
                Vector3 t = WorldLayout.TangentAtArc(b.local, a), r = WorldLayout.RightOf(t), c = WorldLayout.PointAtArc(b.local, a);
                Assert.Greater(W(c), 0.6f, "線の真ん中 arc " + a);
                Assert.Greater(W(c + r * (StageDefinitions.HalfWidthAt(b.def, a) - 2f)), 0.5f, "道幅いっぱい");
                Assert.AreEqual(0f, W(c + t * 8f), 1e-4f, "帯の外（コース方向）");
                Assert.AreEqual(0f, W(c + r * (StageDefinitions.HalfWidthAt(b.def, a) + 4f)), 1e-4f, "道の外");
            }
            // 帯は道のマスク（踏み固めた土）の上だけ：2 本 × 道幅 × 約 3m より広くない
            // （art-r4-12：岩の橋の背の明るい道 SPINE_LINE_W は別に数える）
            int on = 0, spine = 0;
            float bridge = SetPieces.BridgeArc(b.def, b.goal);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float v = m[z, x];
                    if (v <= 0.05f) continue;
                    if (bridge >= 0f && SetPieces.SPINE_LINE_W > 0f && Mathf.Abs(v - SetPieces.SPINE_LINE_W) < 0.02f) { spine++; continue; }
                    on++;
                }
            float cell = StageDefinitions.SIZE / (res - 1);
            Assert.Less(on * cell * cell, 2f * 2f * StageDefinitions.START_HALF_M * (HeightmapGen.LINE_BAND_M + 2f * HeightmapGen.LINE_BAND_FADE_M + 2f * cell));
            if (bridge >= 0f)
            {
                // art-r6-13：背は道の色のまま（線の明るさで塗らない）
                Assert.AreEqual(0, spine);
                Assert.AreEqual(0f, W(WorldLayout.PointAtArc(b.local, bridge)), 0.02f);
            }
        }
    }
}
