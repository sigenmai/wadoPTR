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
        [Test] public void LateJoin() { Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_LOBBY)); Assert.IsFalse(RaceMath.CanEnterStage(RaceMath.STATE_RACING)); Assert.IsTrue(RaceMath.CanEnterStage(RaceMath.STATE_STAGE_RESULT)); }
        [Test] public void RemainingHardLimit() { Assert.AreEqual(330f, RaceMath.RemainingSeconds(1000, 1000, -1), 1e-3f); Assert.AreEqual(138f, RaceMath.RemainingSeconds(1192, 1000, -1), 1e-3f); }
        [Test] public void RemainingAfterFirstFinish() { Assert.AreEqual(50f, RaceMath.RemainingSeconds(1210, 1000, 1200), 1e-3f); Assert.AreEqual(30f, RaceMath.RemainingSeconds(1300, 1000, 1290), 1e-3f); }
        [Test] public void RemainingClampsAtZero() { Assert.AreEqual(0f, RaceMath.RemainingSeconds(1400, 1000, -1)); Assert.AreEqual(0f, RaceMath.RemainingSeconds(1300, 1000, 1200)); }
        [Test] public void RemainingLargeServerTime() => Assert.AreEqual(30f, RaceMath.RemainingSeconds(5_000_300.0, 5_000_000.0, -1), 1e-3f);
        [Test] public void Clock() { Assert.AreEqual("3:12", RaceMath.FormatClock(192f)); Assert.AreEqual("0:05", RaceMath.FormatClock(4.2f)); Assert.AreEqual("5:30", RaceMath.FormatClock(330f)); Assert.AreEqual("0:00", RaceMath.FormatClock(-3f)); }
        [Test] public void Reset() { Assert.IsTrue(RaceMath.NeedsReset(-2.1f, 0, 0)); Assert.IsFalse(RaceMath.NeedsReset(0, 70, 2.9f)); Assert.IsTrue(RaceMath.NeedsReset(0, 70, 3f)); }
    }
}
