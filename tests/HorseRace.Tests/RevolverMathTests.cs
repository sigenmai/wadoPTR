using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class RevolverMathTests
    {
        [Test] public void Ammo() { Assert.AreEqual(5, RevolverMath.AmmoAfterShot(6)); Assert.AreEqual(0, RevolverMath.AmmoAfterShot(0)); Assert.IsFalse(RevolverMath.CanShoot(0, RaceMath.STATE_RACING)); Assert.IsFalse(RevolverMath.CanShoot(6, RaceMath.STATE_COUNTDOWN)); Assert.IsTrue(RevolverMath.CanShoot(1, RaceMath.STATE_RACING)); }
        [Test] public void ThirdHitDrops() { Assert.IsFalse(RevolverMath.DropsRelic(0)); Assert.IsFalse(RevolverMath.DropsRelic(1)); Assert.IsTrue(RevolverMath.DropsRelic(2)); Assert.AreEqual(0, RevolverMath.RegisterHit(2)); Assert.AreEqual(2, RevolverMath.RegisterHit(1)); }
        [Test] public void TotalUsesCumulativeHintAndIgnoresStaleOnes()
        {
            Assert.AreEqual(1, RevolverMath.TotalAfterHit(0, 0));   // no hint: count locally
            Assert.AreEqual(1, RevolverMath.TotalAfterHit(0, 1));
            Assert.AreEqual(3, RevolverMath.TotalAfterHit(1, 3));   // hit 3 overtook hit 2
            Assert.AreEqual(3, RevolverMath.TotalAfterHit(3, 2));   // late hit 2 does not add a 4th
            Assert.AreEqual(4, RevolverMath.TotalAfterHit(3, 4));
        }
        [Test] public void DropDueOncePerThreeHits()
        {
            Assert.IsFalse(RevolverMath.DropDue(2, 0));
            Assert.IsTrue(RevolverMath.DropDue(3, 0));
            Assert.IsFalse(RevolverMath.DropDue(3, 1));
            Assert.IsFalse(RevolverMath.DropDue(5, 1));
            Assert.IsTrue(RevolverMath.DropDue(6, 1));
            Assert.AreEqual(2, RevolverMath.DropsFor(7));
        }
        [Test] public void OutOfOrderHitsDropExactlyOnce()
        {
            int total = 0, given = 0, drops = 0;
            int[] arrival = { 1, 3, 2 };
            for (int i = 0; i < arrival.Length; i++)
            {
                total = RevolverMath.TotalAfterHit(total, arrival[i]);
                if (RevolverMath.DropDue(total, given)) { given = RevolverMath.DropsFor(total); drops++; }
            }
            Assert.AreEqual(1, drops);
        }
        [Test] public void DropCooldownSuppressesSimultaneousThirdHits()
        {
            Assert.IsTrue(RevolverMath.DropAllowed(10f, -1000f));
            Assert.IsFalse(RevolverMath.DropAllowed(10.2f, 10f));
            Assert.IsTrue(RevolverMath.DropAllowed(10f + RevolverMath.DROP_COOLDOWN, 10f));
        }
        [Test] public void ShootingIsWorthIt()
        {
            // 当てたときの相手の損は、外したときの自分の損の 3 倍より大きい（30〜40% の命中率でも撃つ方が得）
            Assert.Greater(RevolverMath.HitCostMeters(14f), 3f * RevolverMath.MissCostMeters(14f));
            Assert.AreEqual(1.68f, RevolverMath.MissCostMeters(14f), 1e-3);
            Assert.AreEqual(7.84f, RevolverMath.HitCostMeters(14f), 1e-3);
            Assert.Greater(0.35f * RevolverMath.HitCostMeters(14f), 0.65f * RevolverMath.MissCostMeters(14f));
        }
        [Test] public void AimAssistWhileGalloping()
        {
            Assert.AreEqual(0.55f, RevolverMath.HitRadius(0.25f, true, 14f), 1e-4);
            Assert.AreEqual(0.25f, RevolverMath.HitRadius(0.25f, true, 8f), 1e-4);
            Assert.AreEqual(0.25f, RevolverMath.HitRadius(0.25f, false, 14f), 1e-4);
        }
        [Test] public void DropLevel() { Assert.AreEqual(2, RevolverMath.LevelAfterDrop(3)); Assert.AreEqual(1, RevolverMath.LevelAfterDrop(1)); Assert.IsFalse(RevolverMath.SpawnsDroppedRelic(1)); Assert.IsTrue(RevolverMath.SpawnsDroppedRelic(2)); }
    }
}
