using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class DirectorMathTests
    {
        [Test] public void HoldsMinShot() => Assert.AreEqual(2, DirectorMath.ChooseTarget(2, 4.9f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
        [Test] public void ShowsHit() => Assert.AreEqual(1, DirectorMath.ChooseTarget(0, 6f, new[]{0,1,2}, new[]{0f,50f,50f}, 1, 1f));
        [Test] public void PrefersBattle() => Assert.AreEqual(2, DirectorMath.ChooseTarget(0, 6f, new[]{0,1,2}, new[]{0f,50f,3f}, -1, 99));
        [Test] public void DefaultsToLeader() => Assert.AreEqual(0, DirectorMath.ChooseTarget(1, 6f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
        [Test] public void ForcesChangeAtMax() => Assert.AreNotEqual(0, DirectorMath.ChooseTarget(0, 15f, new[]{0,1,2}, new[]{0f,50f,50f}, -1, 99));
        [Test] public void Empty() => Assert.AreEqual(-1, DirectorMath.ChooseTarget(0, 6f, new int[0], new float[0], -1, 99));
        [Test] public void HitSlotNotInOrderIgnored() => Assert.AreEqual(0, DirectorMath.ChooseTarget(1, 6f, new[]{0,1,2}, new[]{0f,50f,50f}, 7, 1f));
        [Test] public void ForcedChangeInBattleUsesFrontRider() => Assert.AreEqual(1, DirectorMath.ChooseTarget(2, 15f, new[]{0,1,2}, new[]{0f,50f,3f}, -1, 99));
        [Test] public void SingleHorseKeepsCurrentAtMax() => Assert.AreEqual(0, DirectorMath.ChooseTarget(0, 20f, new[]{0}, new[]{0f}, -1, 99));
        [Test] public void HighestBattleWins() => Assert.AreEqual(1, DirectorMath.ChooseTarget(0, 6f, new[]{0,1,2}, new[]{0f,2f,2f}, -1, 99));
        [Test] public void FinishPlaceOrdersByTime() => Assert.AreEqual(2, DirectorMath.FinishPlace(new[]{ 105.0, 103.0, -1.0 }, new[]{ true, true, true }, 0, 100.0));
        [Test] public void FinishPlaceZeroWhenNotFinished() => Assert.AreEqual(0, DirectorMath.FinishPlace(new[]{ 105.0, 103.0, -1.0 }, new[]{ true, true, true }, 2, 100.0));
        [Test] public void FinishPlaceIgnoresPreviousStage() => Assert.AreEqual(1, DirectorMath.FinishPlace(new[]{ 105.0, 50.0 }, new[]{ true, true }, 0, 100.0));
        [Test] public void FinishPlaceIgnoresInactive() => Assert.AreEqual(1, DirectorMath.FinishPlace(new[]{ 105.0, 103.0 }, new[]{ true, false }, 0, 100.0));
        [Test] public void FinishPlaceTieLowerSlotFirst() => Assert.AreEqual(2, DirectorMath.FinishPlace(new[]{ 103.0, 103.0 }, new[]{ true, true }, 1, 100.0));
        [Test] public void GapsXZUsesOrder()
        {
            float[] g = DirectorMath.GapsXZ(new[]{ 2, 0 }, new[]{ 0f, 0f, 3f }, new[]{ 0f, 0f, 4f });
            Assert.AreEqual(0f, g[0]);
            Assert.AreEqual(5f, g[1], 1e-4f);
        }
        [Test] public void SlotAtRankBounds()
        {
            Assert.AreEqual(5, DirectorMath.SlotAtRank(new[]{ 5, 1 }, 1));
            Assert.AreEqual(1, DirectorMath.SlotAtRank(new[]{ 5, 1 }, 2));
            Assert.AreEqual(-1, DirectorMath.SlotAtRank(new[]{ 5, 1 }, 3));
            Assert.AreEqual(-1, DirectorMath.SlotAtRank(new[]{ 5, 1 }, 0));
        }
    }
}
