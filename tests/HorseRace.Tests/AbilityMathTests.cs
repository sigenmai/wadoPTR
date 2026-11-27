using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class AbilityMathTests
    {
        [Test] public void Level_Caps() { Assert.AreEqual(2, AbilityMath.LevelAfterRelic(1)); Assert.AreEqual(3, AbilityMath.LevelAfterRelic(3)); }
        [Test] public void Cooldowns() { Assert.AreEqual(18f, AbilityMath.Cooldown(0)); Assert.AreEqual(15f, AbilityMath.Cooldown(3)); Assert.AreEqual(20f, AbilityMath.Cooldown(99)); }
        [Test] public void Scaling() { Assert.AreEqual(8f, AbilityMath.DustRadius(3)); Assert.AreEqual(35f, AbilityMath.LassoRange(2)); Assert.AreEqual(3, AbilityMath.WallCount(3)); Assert.AreEqual(1, AbilityMath.BallCount(1)); Assert.AreEqual(4f, AbilityMath.DustDuration(1)); Assert.AreEqual(8f, AbilityMath.DustRadius(9)); Assert.AreEqual(4f, AbilityMath.DustRadius(0)); }
        [Test] public void Relic_FirstClaimWins() { int h = AbilityMath.RelicArbiter(-1, 42); Assert.AreEqual(42, h); Assert.AreEqual(42, AbilityMath.RelicArbiter(h, 7)); }
        [Test] public void SecondaryConstants()
        {
            Assert.AreEqual(0.8f, AbilityMath.DUST_SLOW_FACTOR);
            Assert.AreEqual(4f, AbilityMath.LASSO_SELF_BONUS_MPS); Assert.AreEqual(2f, AbilityMath.LASSO_SELF_SECONDS);
            Assert.AreEqual(0.7f, AbilityMath.LASSO_TARGET_FACTOR); Assert.AreEqual(1f, AbilityMath.LASSO_TARGET_SECONDS);
            Assert.AreEqual(6f, AbilityMath.WALL_WIDTH); Assert.AreEqual(20f, AbilityMath.WALL_AHEAD); Assert.AreEqual(6f, AbilityMath.WALL_SECONDS);
            Assert.AreEqual(0.6f, AbilityMath.BALL_HIT_FACTOR); Assert.AreEqual(1.2f, AbilityMath.BALL_HIT_SECONDS);
            Assert.AreEqual(3, AbilityMath.BALL_BOUNCES); Assert.AreEqual(0.5f, AbilityMath.BALL_CURVE_DEG_PER_M_PER_ROLL_DEG);
            Assert.AreEqual(8, SpeedMod.SLOTS);
        }
    }
}
