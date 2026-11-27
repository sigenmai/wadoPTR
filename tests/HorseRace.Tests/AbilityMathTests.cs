using NUnit.Framework;
using HorseRace;

namespace HorseRace.Tests
{
    public class AbilityMathTests
    {
        [Test] public void Level_Caps() { Assert.AreEqual(2, AbilityMath.LevelAfterRelic(1)); Assert.AreEqual(3, AbilityMath.LevelAfterRelic(3)); }
        [Test] public void Cooldowns() { Assert.AreEqual(18f, AbilityMath.Cooldown(0)); Assert.AreEqual(15f, AbilityMath.Cooldown(3)); Assert.AreEqual(20f, AbilityMath.Cooldown(99)); }
        [Test] public void Scaling() { Assert.AreEqual(8f, AbilityMath.DustRadius(3)); Assert.AreEqual(45f, AbilityMath.LassoRange(2)); Assert.AreEqual(3, AbilityMath.WallCount(3)); Assert.AreEqual(1, AbilityMath.BallCount(1)); Assert.AreEqual(3f, AbilityMath.DustDuration(1)); Assert.AreEqual(5f, AbilityMath.DustDuration(3)); Assert.AreEqual(8f, AbilityMath.DustRadius(9)); Assert.AreEqual(4f, AbilityMath.DustRadius(0)); }
        [Test] public void RelicGain_ByRank()
        {
            Assert.AreEqual(1, AbilityMath.RelicGain(1, 1));   // 1 人：+1
            Assert.AreEqual(0, AbilityMath.RelicGain(1, 2));   // 先頭は拾えない
            Assert.AreEqual(2, AbilityMath.RelicGain(2, 2));
            Assert.AreEqual(0, AbilityMath.RelicGain(1, 8));
            for (int r = 2; r <= 4; r++) Assert.AreEqual(1, AbilityMath.RelicGain(r, 8), "rank " + r);
            for (int r = 5; r <= 8; r++) Assert.AreEqual(2, AbilityMath.RelicGain(r, 8), "rank " + r);
            Assert.AreEqual(1, AbilityMath.RelicGain(0, 8));   // 順位がまだ無い
            Assert.AreEqual(3, AbilityMath.LevelAfterRelicGain(2, 2));
            Assert.AreEqual(3, AbilityMath.LevelAfterRelicGain(1, 2));
            Assert.AreEqual(2, AbilityMath.LevelAfterRelicGain(2, 0));
        }
        [Test] public void Relic_FirstClaimWins() { int h = AbilityMath.RelicArbiter(-1, 42); Assert.AreEqual(42, h); Assert.AreEqual(42, AbilityMath.RelicArbiter(h, 7)); }
        [Test] public void LassoPullBonus_ScalesWithLevel()
        {
            Assert.AreEqual(5f, AbilityMath.LassoPullBonus(1)); Assert.AreEqual(6.5f, AbilityMath.LassoPullBonus(2)); Assert.AreEqual(8f, AbilityMath.LassoPullBonus(3));
            Assert.AreEqual(AbilityMath.LASSO_SELF_BONUS_MPS, AbilityMath.LassoPullBonus(1));
            Assert.AreEqual(5f, AbilityMath.LassoPullBonus(0)); Assert.AreEqual(8f, AbilityMath.LassoPullBonus(9));
        }
        [Test] public void WallWidth_ScalesWithLevel()
        {
            Assert.AreEqual(6f, AbilityMath.WallWidth(1)); Assert.AreEqual(7.5f, AbilityMath.WallWidth(2)); Assert.AreEqual(9f, AbilityMath.WallWidth(3));
            Assert.AreEqual(AbilityMath.WALL_WIDTH, AbilityMath.WallWidth(1));
            Assert.AreEqual(6f, AbilityMath.WallWidth(-1)); Assert.AreEqual(9f, AbilityMath.WallWidth(4));
        }
        [Test] public void SecondaryConstants()
        {
            Assert.AreEqual(0.85f, AbilityMath.DUST_SLOW_FACTOR);
            Assert.AreEqual(5f, AbilityMath.LASSO_SELF_BONUS_MPS); Assert.AreEqual(2f, AbilityMath.LASSO_SELF_SECONDS);
            Assert.AreEqual(0.7f, AbilityMath.LASSO_TARGET_FACTOR); Assert.AreEqual(1f, AbilityMath.LASSO_TARGET_SECONDS);
            Assert.AreEqual(6f, AbilityMath.WALL_WIDTH); Assert.AreEqual(28f, AbilityMath.WALL_AHEAD); Assert.AreEqual(6f, AbilityMath.WALL_SECONDS);
            Assert.AreEqual(0.6f, AbilityMath.BALL_HIT_FACTOR); Assert.AreEqual(1.2f, AbilityMath.BALL_HIT_SECONDS);
            Assert.AreEqual(3, AbilityMath.BALL_BOUNCES); Assert.AreEqual(0.5f, AbilityMath.BALL_CURVE_DEG_PER_M_PER_ROLL_DEG);
            Assert.AreEqual(8, SpeedMod.SLOTS);
        }
    }
}
