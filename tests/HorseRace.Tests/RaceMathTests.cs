using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class RaceMathTests
    {
        [Test] public void HardLimit() { Assert.IsFalse(RaceMath.ShouldEndStage(1329.9, 1000, -1)); Assert.IsTrue(RaceMath.ShouldEndStage(1330, 1000, -1)); }
        [Test] public void AfterFirst() { Assert.IsFalse(RaceMath.ShouldEndStage(1259, 1000, 1200)); Assert.IsTrue(RaceMath.ShouldEndStage(1260, 1000, 1200)); }
        [Test] public void LargeServerTime() => Assert.IsTrue(RaceMath.ShouldEndStage(5_000_330.0, 5_000_000.0, -1));
        [Test] public void Order() => CollectionAssert.AreEqual(new[]{3,2,1,0}, RaceMath.ComputeOrder(new[]{5,9,9,7}, new[]{10f,50f,20f,0f}, new[]{-1.0,-1.0,-1.0,300.0}, new[]{true,true,true,true}));
        [Test] public void OrderSkipsInactive() => CollectionAssert.AreEqual(new[]{1}, RaceMath.ComputeOrder(new[]{3,1}, new[]{0f,0f}, new[]{-1.0,-1.0}, new[]{false,true}));
        [Test] public void OrderTiesStable() => CollectionAssert.AreEqual(new[]{0,1,2}, RaceMath.ComputeOrder(new[]{4,4,4}, new[]{5f,5f,5f}, new[]{-1.0,-1.0,-1.0}, new[]{true,true,true}));
        [Test] public void Points() { Assert.AreEqual(10, RaceMath.PointsForPlace(0)); Assert.AreEqual(1, RaceMath.PointsForPlace(7)); Assert.AreEqual(0, RaceMath.PointsForPlace(8)); }
        [Test] public void FinalStagePointsAreOneAndAHalf()
        {
            int[] want = { 15, 12, 9, 8, 6, 5, 3, 2 };
            for (int p = 0; p < 8; p++)
            {
                Assert.AreEqual(want[p], RaceMath.PointsForPlace(p, 2), "place " + p);
                Assert.AreEqual(RaceMath.PointsForPlace(p), RaceMath.PointsForPlace(p, 0));
                Assert.AreEqual(RaceMath.PointsForPlace(p), RaceMath.PointsForPlace(p, 1));
            }
            Assert.AreEqual(0, RaceMath.PointsForPlace(8, 2));
        }
        [Test] public void StartGrace()
        {
            Assert.IsTrue(RaceMath.InStartGrace(1000.0, 1000.0));
            Assert.IsTrue(RaceMath.InStartGrace(1007.9, 1000.0));
            Assert.IsFalse(RaceMath.InStartGrace(1008.0, 1000.0));
            Assert.AreEqual(3f, RaceMath.StartGraceLeft(1005.0, 1000.0), 1e-4);
            Assert.AreEqual(0f, RaceMath.StartGraceLeft(1100.0, 1000.0));
        }
        [Test] public void LobbyDeadline()
        {
            Assert.AreEqual(-1.0, RaceMath.LobbyDeadline(100, 1, 0, 5, -1));          // 1 人では時計は動かない
            Assert.AreEqual(160.0, RaceMath.LobbyDeadline(100, 2, 0, 5, -1));         // 2 人で 60 秒
            Assert.AreEqual(160.0, RaceMath.LobbyDeadline(110, 3, 1, 5, 160));        // 延ばさない
            Assert.AreEqual(135.0, RaceMath.LobbyDeadline(120, 3, 3, 5, 160));        // 全員乗った：15 秒
            Assert.AreEqual(115.0, RaceMath.LobbyDeadline(100, 4, 0, 4, -1));         // ワールドの全員がエントリー
            Assert.AreEqual(150.0, RaceMath.LobbyDeadline(140, 2, 2, 5, 150));        // 縮めるだけ
            Assert.AreEqual(-1.0, RaceMath.LobbyDeadline(140, 1, 1, 5, 150));         // 2 人未満に戻ったら消す
        }
        [Test] public void AnyEntrantCanStartNearDeadline()
        {
            Assert.IsTrue(RaceMath.CanRingStart(true, false, 100, -1));
            Assert.IsFalse(RaceMath.CanRingStart(false, true, 100, -1));
            Assert.IsFalse(RaceMath.CanRingStart(false, true, 100, 140));
            Assert.IsTrue(RaceMath.CanRingStart(false, true, 100, 129));
            Assert.IsFalse(RaceMath.CanRingStart(false, false, 100, 129));
        }
        [Test] public void OverallTieBreaksOnLastStage()
        {
            // 0: 10+6+8 = 24、1: 8+10+6 = 24 → 第 3 ステージで上の 0 が上（スロット順ではない）
            int[] totals = { 24, 24, 0, 0, 0, 0, 0, 0 };
            int[] place = new int[24];
            place[0] = 1; place[1] = 2;            // 第 1
            place[8 + 0] = 3; place[8 + 1] = 1;    // 第 2
            place[16 + 0] = 2; place[16 + 1] = 3;  // 第 3
            Assert.IsTrue(RaceMath.OverallAhead(0, 1, totals, place, 3));
            Assert.IsFalse(RaceMath.OverallAhead(1, 0, totals, place, 3));
            place[16 + 0] = 3; place[16 + 1] = 2;
            Assert.IsTrue(RaceMath.OverallAhead(1, 0, totals, place, 3));
            CollectionAssert.AreEqual(new[] { 1, 0 }, RaceMath.OverallOrder(totals, place, new[] { true, true, false, false, false, false, false, false }, 3));
        }
        [Test] public void OverallTieWithLateJoiner()
        {
            // 1 は第 2 ステージから（第 1 は 0 = 走っていない）。同点なら 2 人とも走った最後（第 3）で決める
            int[] totals = { 14, 14, 0, 0, 0, 0, 0, 0 };
            int[] place = new int[24];
            place[0] = 3; place[1] = 0;
            place[8 + 0] = 4; place[8 + 1] = 2;
            place[16 + 0] = 2; place[16 + 1] = 1;
            Assert.IsTrue(RaceMath.OverallAhead(1, 0, totals, place, 3));
            CollectionAssert.AreEqual(new[] { 1, 0 }, RaceMath.OverallOrder(totals, place, new[] { true, true, false, false, false, false, false, false }, 3));
            // 合計点が違えばそちらが先
            totals[0] = 15;
            Assert.IsTrue(RaceMath.OverallAhead(0, 1, totals, place, 3));
        }
        [Test] public void OverallTieFallsBackToWins()
        {
            int[] totals = { 10, 10, 0, 0 };
            int[] place = new int[12];
            place[0] = 1;           // 0 は第 1 だけ走って 1 着
            place[4 + 1] = 0;
            place[8 + 1] = 0;
            place[1] = 0; place[4 + 1] = 1;   // 1 は第 2 だけ 1 着 → 共通のステージなし・1 着の数も同じ → スロット順
            Assert.IsFalse(RaceMath.OverallAhead(1, 0, totals, place, 3));
            Assert.IsFalse(RaceMath.OverallAhead(0, 1, totals, place, 3));
            place[8 + 1] = 1;                 // 1 は 1 着が 2 回
            totals[1] = 10;
            Assert.IsTrue(RaceMath.OverallAhead(1, 0, totals, place, 3));
        }
        [Test] public void GridOrder()
        {
            int[] overall = { 2, 0, 5 };   // 2 が首位
            int[] lanes = RaceMath.GridOrder(overall, 1, 7, 8);
            Assert.AreEqual(2, lanes[2]);  // 首位はいちばん外（走る 3 人の中で）
            Assert.AreEqual(1, lanes[0]);
            Assert.AreEqual(0, lanes[5]);  // 最下位がレーン 0
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, lanes);   // 重ならない
            int[] s1 = RaceMath.GridOrder(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 0, 11, 8);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, s1);
            CollectionAssert.AreEqual(s1, RaceMath.GridOrder(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 0, 11, 8));   // 同じ seed なら同じ
            bool differs = false;
            for (int seed = 1; seed < 6 && !differs; seed++)
            {
                int[] o = RaceMath.GridOrder(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 0, seed, 8);
                for (int i = 0; i < 8; i++) if (o[i] != i) differs = true;
            }
            Assert.IsTrue(differs, "第 1 ステージはサイン順のままではない");
        }
        [Test] public void WrongWay()
        {
            Assert.IsTrue(RaceMath.WrongWayNow(-0.9f, 10f));
            Assert.IsFalse(RaceMath.WrongWayNow(-0.9f, 3f));
            Assert.IsFalse(RaceMath.WrongWayNow(0.2f, 10f));
        }
        [Test] public void LateJoin() { Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_LOBBY)); Assert.IsFalse(RaceMath.CanEnterStage(RaceMath.STATE_RACING)); Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_STAGE_RESULT)); }
        [Test] public void EntryModeLobbyAndBetweenStages()
        {
            Assert.AreEqual(RaceMath.ENTRY_NOW, RaceMath.EntryMode(RaceMath.STATE_LOBBY, 0, 3));
            Assert.AreEqual(RaceMath.ENTRY_NOW, RaceMath.EntryMode(RaceMath.STATE_STAGE_RESULT, 0, 3));
            Assert.AreEqual(RaceMath.ENTRY_NOW, RaceMath.EntryMode(RaceMath.STATE_STAGE_RESULT, 1, 3));
            Assert.AreEqual(RaceMath.ENTRY_REJECT, RaceMath.EntryMode(RaceMath.STATE_STAGE_RESULT, 2, 3));
        }
        [Test] public void EntryModeMidStageQueuesForNextStage()
        {
            Assert.AreEqual(RaceMath.ENTRY_QUEUE, RaceMath.EntryMode(RaceMath.STATE_COUNTDOWN, 0, 3));
            Assert.AreEqual(RaceMath.ENTRY_QUEUE, RaceMath.EntryMode(RaceMath.STATE_RACING, 0, 3));
            Assert.AreEqual(RaceMath.ENTRY_QUEUE, RaceMath.EntryMode(RaceMath.STATE_RACING, 1, 3));
            Assert.AreEqual(RaceMath.ENTRY_REJECT, RaceMath.EntryMode(RaceMath.STATE_RACING, 2, 3));
            Assert.AreEqual(RaceMath.ENTRY_REJECT, RaceMath.EntryMode(RaceMath.STATE_COUNTDOWN, 2, 3));
            Assert.AreEqual(RaceMath.ENTRY_REJECT, RaceMath.EntryMode(RaceMath.STATE_FINAL_RESULT, 2, 3));
        }
        [Test] public void PendingIndexReusesExistingThenFirstFree()
        {
            int[] pend = { -1, 7, -1, 9 };
            Assert.AreEqual(1, RaceMath.PendingIndexFor(pend, 7));
            Assert.AreEqual(3, RaceMath.PendingIndexFor(pend, 9));
            Assert.AreEqual(0, RaceMath.PendingIndexFor(pend, 4));
            Assert.AreEqual(-1, RaceMath.PendingIndexFor(new[] { 1, 2 }, 3));
            Assert.AreEqual(-1, RaceMath.PendingIndexFor(pend, -1));
        }
        [Test] public void AllFinished()
        {
            Assert.IsFalse(RaceMath.AllFinished(new[] { 100.0, -1.0 }, new[] { true, true }));
            Assert.IsTrue(RaceMath.AllFinished(new[] { 100.0, -1.0 }, new[] { true, false }));
            Assert.IsTrue(RaceMath.AllFinished(new[] { 100.0, 105.0 }, new[] { true, true }));
            Assert.IsFalse(RaceMath.AllFinished(new[] { -1.0, -1.0 }, new[] { false, false }));
        }
        [Test] public void RemainingHardLimit() { Assert.AreEqual(330f, RaceMath.RemainingSeconds(1000, 1000, -1), 1e-3f); Assert.AreEqual(138f, RaceMath.RemainingSeconds(1192, 1000, -1), 1e-3f); }
        [Test] public void RemainingAfterFirstFinish() { Assert.AreEqual(50f, RaceMath.RemainingSeconds(1210, 1000, 1200), 1e-3f); Assert.AreEqual(30f, RaceMath.RemainingSeconds(1300, 1000, 1290), 1e-3f); }
        [Test] public void RemainingClampsAtZero() { Assert.AreEqual(0f, RaceMath.RemainingSeconds(1400, 1000, -1)); Assert.AreEqual(0f, RaceMath.RemainingSeconds(1300, 1000, 1200)); }
        [Test] public void RemainingLargeServerTime() => Assert.AreEqual(30f, RaceMath.RemainingSeconds(5_000_300.0, 5_000_000.0, -1), 1e-3f);
        [Test] public void Clock() { Assert.AreEqual("3:12", RaceMath.FormatClock(192f)); Assert.AreEqual("0:05", RaceMath.FormatClock(4.2f)); Assert.AreEqual("5:30", RaceMath.FormatClock(330f)); Assert.AreEqual("0:00", RaceMath.FormatClock(-3f)); }
        [Test] public void Reset() { Assert.IsTrue(RaceMath.NeedsReset(-2.1f, 0, 0)); Assert.IsFalse(RaceMath.NeedsReset(0, 70, 2.9f)); Assert.IsTrue(RaceMath.NeedsReset(0, 70, 3f)); }
    }
}
