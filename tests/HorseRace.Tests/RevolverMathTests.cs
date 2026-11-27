using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class RevolverMathTests
    {
        [Test] public void Ammo() { Assert.AreEqual(5, RevolverMath.AmmoAfterShot(6)); Assert.AreEqual(0, RevolverMath.AmmoAfterShot(0)); Assert.IsFalse(RevolverMath.CanShoot(0, RaceMath.STATE_RACING)); Assert.IsFalse(RevolverMath.CanShoot(6, RaceMath.STATE_COUNTDOWN)); Assert.IsTrue(RevolverMath.CanShoot(1, RaceMath.STATE_RACING)); }
        [Test] public void ThirdHitDrops() { Assert.IsFalse(RevolverMath.DropsRelic(0)); Assert.IsFalse(RevolverMath.DropsRelic(1)); Assert.IsTrue(RevolverMath.DropsRelic(2)); Assert.AreEqual(0, RevolverMath.RegisterHit(2)); Assert.AreEqual(2, RevolverMath.RegisterHit(1)); }
        [Test] public void DropLevel() { Assert.AreEqual(2, RevolverMath.LevelAfterDrop(3)); Assert.AreEqual(1, RevolverMath.LevelAfterDrop(1)); Assert.IsFalse(RevolverMath.SpawnsDroppedRelic(1)); Assert.IsTrue(RevolverMath.SpawnsDroppedRelic(2)); }
    }
}
