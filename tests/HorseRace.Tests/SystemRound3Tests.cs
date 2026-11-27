using NUnit.Framework;
using UnityEngine;
using HorseRace;
using HorseRace.EditorTools;

namespace HorseRace.Tests
{
    /// <summary>ゲームシステムの第 3 回のレビュー（system-r3-01〜16）の純粋な計算。</summary>
    public class SystemRound3Tests
    {
        const float JUMP = 6f, G = 20f;   // MountController.jumpSpeed / GRAVITY

        // ---- r3-01：跳ねても深い砂は抜けられない ----
        [Test] public void BunnyHop_DoesNotBeatSand()
        {
            float grounded = MountMath.HopRunAverage(MountMath.MOUNT_HORSE, true, 400f, 1.25f, false, JUMP, G);
            float hopping = MountMath.HopRunAverage(MountMath.MOUNT_HORSE, true, 400f, 1.25f, true, JUMP, G);
            TestContext.WriteLine("sand 400m: grounded " + grounded + " m/s, hopping " + hopping + " m/s");
            Assert.LessOrEqual(hopping, grounded + 1e-3f);
            // ラクダの出番（system-r2-01）も残る：跳ねる馬より砂のラクダが速い
            float camel = MountMath.HopRunAverage(MountMath.MOUNT_CAMEL, true, 400f, 1.25f, false, JUMP, G);
            Assert.Greater(camel, hopping);
        }

        [Test] public void JumpCostsAndCoolsDown()
        {
            Assert.IsFalse(MountMath.CanJump(0.5f, 100f));
            Assert.IsFalse(MountMath.CanJump(2f, MountMath.JUMP_STAMINA - 0.1f));
            Assert.IsTrue(MountMath.CanJump(MountMath.JUMP_COOLDOWN, MountMath.JUMP_STAMINA));
            Assert.AreEqual(0f, MountMath.AirRefillMul(false, 1.6f));
            Assert.AreEqual(1.6f, MountMath.AirRefillMul(true, 1.6f));
            // 空中は回復しない（駆歩の回復 +8/s が止まる）、疾走なら減り続ける
            Assert.AreEqual(50f, MountMath.StepStamina(0, 50f, false, false, MountMath.GAIT_CANTER, 1f, 0f), 1e-4);
            Assert.Less(MountMath.StepStamina(0, 50f, true, false, MountMath.GAIT_SPRINT, 1f, 0f), 50f);
            // 着地で地上の目標まで、空中もカーブの上限 + 2
            Assert.AreEqual(14.76f, MountMath.LandingSpeed(18f, 14.76f), 1e-4);
            Assert.AreEqual(10f, MountMath.LandingSpeed(10f, 14.76f), 1e-4);
            Assert.AreEqual(MountMath.CornerSpeedCap(50f) + 2f, MountMath.AirSpeed(18f, 50f), 1e-4);
            Assert.AreEqual(18f, MountMath.AirSpeed(18f, 0f), 1e-4);
        }

        // ---- r3-02：坂で跳ねてもきれいな着地にならない ----
        [Test] public void HopOnSixDegreeSlope_NoBoost()
        {
            float drop = MountMath.HopDropOnSlope(18f, 6f, JUMP, G);
            float apex = JUMP * JUMP / (2f * G);
            TestContext.WriteLine("6° hop at 18 m/s: drop below takeoff " + drop + " m, from apex " + (drop + apex) + " m");
            Assert.GreaterOrEqual(drop + apex, 2f, "古い測り方（頂点から）なら加速が出てしまう坂");
            float h = MountMath.BoostHeight(true, 0f, apex, -drop);
            Assert.Less(h, MountMath.CLEAN_LANDING_MIN_M);
            Assert.IsFalse(MountMath.GrantsCleanLanding(h, 0.1f));
            // 崖から転がり落ちた 4m（跳んでいない）は今まで通り
            Assert.IsTrue(MountMath.GrantsCleanLanding(MountMath.BoostHeight(false, 0f, 4f, 0f), 0.1f));
            // 跳んで 3m の段を下りた（踏み切りより 3m 下）も加速
            Assert.IsTrue(MountMath.GrantsCleanLanding(MountMath.BoostHeight(true, 3f, 3.9f, 0f), 0.1f));
        }

        // ---- r3-06：正面の壁 ----
        [Test] public void WallHeadOnCostsAtMostTwelveMeters()
        {
            float loss = MountMath.WallHeadOnLossMeters(18f);
            TestContext.WriteLine("head-on at 18 m/s loses " + loss + " m");
            Assert.LessOrEqual(loss, 12f);
            Assert.Greater(loss, 0f);
            Assert.Greater(MountMath.WALL_HEAD_ON_KEEP, 0f);
            Assert.AreEqual(1f, MountMath.DeflectSide(3f, 9f));
            Assert.AreEqual(-1f, MountMath.DeflectSide(9f, 3f));
        }

        // ---- r3-07：キーボードで 60m のカーブを速さを落とさず ----
        [Test] public void KeyboardHoldsSpeedOnSixtyMetreCurve()
        {
            int mount = MountMath.MOUNT_HORSE;
            float target = MountMath.TOP_SPEED, speed = target, steer = 0f, yawRate = 0f, turned = 0f;
            const float dt = 0.01f;
            float minSpeed = speed;
            for (float t = 0f; t < 6f; t += dt)
            {
                steer = MountMath.SlewSteer(steer, 1f, dt);   // A を押しっぱなし
                float lim = MountMath.KeyboardSteerLimit(mount, speed, target);
                steer = Mathf.Clamp(steer, -lim, lim);
                float turnDeg = steer * MountMath.TurnRateDeg(mount, speed) * dt;
                yawRate = MountMath.SmoothYawRate(yawRate, turnDeg / dt, dt);
                float cap = Mathf.Min(target, MountMath.CornerSpeedCap(yawRate));
                speed = MountMath.Approach(speed, cap, dt);
                turned += turnDeg;
                minSpeed = Mathf.Min(minSpeed, speed);
            }
            float radius = speed / (yawRate * Mathf.Deg2Rad);
            TestContext.WriteLine("hold A: min speed " + minSpeed + ", radius " + radius + " m, turned " + turned + "°");
            Assert.GreaterOrEqual(minSpeed, 17.9f);
            Assert.LessOrEqual(radius, 60f, "60m のカーブは押しっぱなしで曲がれる");
            // 生の ±1（前の作り）なら落ちる
            Assert.Less(MountMath.CornerSpeedCap(MountMath.TurnRateDeg(mount, 18f)), 9f);
            // ゆっくり入れて速く戻す
            Assert.AreEqual(0.04f, MountMath.SlewSteer(0f, 1f, 0.01f), 1e-5);
            Assert.AreEqual(0.92f, MountMath.SlewSteer(1f, 0f, 0.01f), 1e-5);
            Assert.AreEqual(1f, MountMath.KeyboardSteerLimit(mount, 2f, 2f));
        }

        // ---- r3-08：手綱 ----
        [Test] public void ReinSteerModes()
        {
            Assert.AreEqual(0f, MountMath.ReinSteer(0.05f, 0.12f, MountMath.REIN_BOTH, 0f));
            Assert.AreEqual(1f, MountMath.ReinSteer(0f, 0.3f, MountMath.REIN_BOTH, 0f), 1e-5);
            Assert.AreEqual(-1f, MountMath.ReinSteer(0.3f, 0f, MountMath.REIN_BOTH, 0f), 1e-5);
            // 右手でリボルバーを構えて引いても曲がらない（左手の横のずれだけ）
            Assert.AreEqual(0f, MountMath.ReinSteer(0f, 0.3f, MountMath.REIN_LEFT_ONLY, 0.02f));
            Assert.AreEqual(1f, MountMath.ReinSteer(0f, 0.3f, MountMath.REIN_LEFT_ONLY, 0.25f), 1e-5);
            Assert.AreEqual(-0.5f, MountMath.ReinSteer(0f, 0f, MountMath.REIN_RIGHT_ONLY, -0.135f), 1e-4);
            Assert.IsFalse(MountMath.ReinBrake(0.2f, 1f, MountMath.REIN_LEFT_ONLY, 0.5f), "銃を持つ右手の引きはブレーキにしない");
            Assert.IsTrue(MountMath.ReinBrake(0.8f, 0f, MountMath.REIN_LEFT_ONLY, 0.5f));
            Assert.IsTrue(MountMath.ReinBrake(0.6f, 0.6f, MountMath.REIN_BOTH, 0.5f));
            Assert.IsTrue(MountMath.ReinMayRecenter(0.05f, false, 2f));
            Assert.IsFalse(MountMath.ReinMayRecenter(0.05f, false, 0.5f));
            Assert.IsFalse(MountMath.ReinMayRecenter(0.3f, false, 2f));
        }

        // ---- r3-09：蹄 ----
        [Test] public void HoofBeatsPerGait()
        {
            Assert.AreEqual(4, SfxMath.HoofBeats(MountMath.GAIT_WALK).Length);
            Assert.AreEqual(2, SfxMath.HoofBeats(MountMath.GAIT_TROT).Length);
            float[] c = SfxMath.HoofBeats(MountMath.GAIT_CANTER);
            Assert.AreEqual(3, c.Length);
            Assert.AreEqual(0.3f, c[1], 1e-5);
            Assert.AreEqual(0.45f, c[2], 1e-5);
            Assert.AreEqual(4, SfxMath.HoofBeats(MountMath.GAIT_SPRINT).Length);
            for (int g = 0; g < 4; g++)
            {
                float[] b = SfxMath.HoofBeats(g);
                for (int i = 0; i < b.Length; i++) Assert.That(b[i], Is.InRange(0f, 0.999f));
            }
            Assert.IsTrue(SfxMath.BeatCrossed(0.2f, 0.35f, 0.3f));
            Assert.IsFalse(SfxMath.BeatCrossed(0.31f, 0.35f, 0.3f));
            Assert.IsTrue(SfxMath.BeatCrossed(0.95f, 0.05f, 0f), "1 周して 0 を通った");
            Assert.AreEqual(0.9f, SfxMath.HoofPitch(0, 4), 1e-5);
            Assert.AreEqual(1.1f, SfxMath.HoofPitch(3, 4), 1e-5);
            Assert.AreEqual(0f, SfxMath.HoofDustRate(0, false, false));
            Assert.AreEqual(22f, SfxMath.HoofDustRate(3, false, false));
            Assert.AreEqual(22f * 1.6f, SfxMath.HoofDustRate(3, true, false), 1e-4);
            Assert.AreEqual(0f, SfxMath.HoofDustRate(3, true, true));
            float[] w = SfxMath.Generate(SfxMath.HOOF, SfxMath.SAMPLE_RATE);
            Assert.AreEqual(SfxMath.SampleCount(0.045f, SfxMath.SAMPLE_RATE), w.Length);
            Assert.AreEqual("Hoof", SfxMath.Name(SfxMath.HOOF));
        }

        // ---- r3-04：溢れ ----
        [Test] public void RelicOverflow_ConvertsToCooldownAndStamina()
        {
            Assert.AreEqual(0, AbilityMath.RelicOverflow(1, 2));
            Assert.AreEqual(1, AbilityMath.RelicOverflow(2, 2));
            Assert.AreEqual(2, AbilityMath.RelicOverflow(3, 2));
            Assert.AreEqual(0, AbilityMath.RelicOverflow(3, 0), "1 位は 0 のまま");
            float cd = AbilityMath.Cooldown(AbilityMath.ABILITY_LASSO);
            Assert.AreEqual(100f + cd - 0.5f * cd, AbilityMath.SurgeReadyAt(100f + cd, 100f, cd, 1), 1e-4);
            Assert.AreEqual(100f, AbilityMath.SurgeReadyAt(100f + cd, 100f, cd, 2), 1e-4);
            Assert.AreEqual(100f, AbilityMath.SurgeReadyAt(90f, 100f, cd, 1), 1e-4, "使える時刻を過去にしない");
            Assert.AreEqual(75f, AbilityMath.SurgeStamina(40f, 1), 1e-4);
            Assert.AreEqual(MountMath.STAMINA_MAX, AbilityMath.SurgeStamina(40f, 2), 1e-4);
        }

        // ---- r3-05：投げ縄 ----
        [Test] public void LassoMissCostsLess()
        {
            float full = AbilityMath.LassoCooldown(AbilityMath.CAST_HIT, 1f);
            float miss = AbilityMath.LassoCooldown(AbilityMath.CAST_MISS, 1f);
            float fizz = AbilityMath.LassoCooldown(AbilityMath.CAST_FIZZLE, 1f);
            Assert.AreEqual(AbilityMath.Cooldown(AbilityMath.ABILITY_LASSO), full, 1e-4);
            Assert.AreEqual(full * 0.35f, miss, 1e-4);
            Assert.AreEqual(1f, fizz, 1e-4);
            Assert.Less(fizz, miss);
            Assert.Less(miss, full);
            Assert.IsTrue(AbilityMath.InLassoCone(Vector3.forward, new Vector3(Mathf.Tan(9f * Mathf.Deg2Rad), 0f, 1f)));
            Assert.IsFalse(AbilityMath.InLassoCone(Vector3.forward, new Vector3(Mathf.Tan(11f * Mathf.Deg2Rad), 0f, 1f)));
            Assert.IsFalse(AbilityMath.InLassoCone(Vector3.forward, Vector3.back));
        }

        // ---- r3-10：蜃気楼 ----
        [Test] public void MirageMakesUntargetable()
        {
            Assert.AreEqual(20f, AbilityMath.Cooldown(AbilityMath.ABILITY_MIRAGE));
            Assert.AreEqual(2.5f, AbilityMath.MirageSeconds(1));
            Assert.AreEqual(4.5f, AbilityMath.MirageSeconds(3));
            Assert.AreEqual(1, AbilityMath.MirageDecoys(2));
            Assert.AreEqual(2, AbilityMath.MirageDecoys(3));
            float until = 10f + AbilityMath.MirageSeconds(1);
            Assert.IsFalse(AbilityMath.Targetable(until, 11f));
            Assert.IsTrue(AbilityMath.Targetable(until, until));
            Assert.IsTrue(AbilityMath.MirageVisible(10f, 10f, until));
            Assert.IsFalse(AbilityMath.MirageVisible(10f + 1f / 16f + 0.001f, 10f, until));
            Assert.IsTrue(AbilityMath.MirageVisible(20f, 10f, until));
            Assert.AreEqual(3f, AbilityMath.MirageDrift(0, 5f, 5f), 1e-4);
            Assert.AreEqual(-3f, AbilityMath.MirageDrift(1, 5f, 5f), 1e-4);
            Assert.IsTrue(AbilityMath.IsImplemented(AbilityMath.ABILITY_MIRAGE));
            Assert.IsTrue(AbilityMath.IsImplemented(AbilityMath.ABILITY_BOMB));
            Assert.IsFalse(AbilityMath.IsImplemented(AbilityMath.ABILITY_DINO));
            Assert.AreEqual(AbilityMath.ABILITY_DUST, AbilityMath.SanitizeAbility(AbilityMath.ABILITY_DINO));
            for (int i = 0; i < AbilityMath.ImplementedCount(); i++)
            {
                int id = AbilityMath.ImplementedAt(i);
                Assert.IsTrue(AbilityMath.IsImplemented(id));
                Assert.AreNotEqual("？", HudMath.AbilityDescription(id));
                Assert.AreNotEqual("？", RaceManagerLabel(id));
                Assert.Greater(HudMath.LevelEffect(id, 2).Length, 0);
            }
            Assert.AreEqual("蜃気楼", HudMath.AbilityCardCaption(AbilityMath.ABILITY_MIRAGE));
            Assert.AreEqual("Wanted_Original", HudMath.AbilityCardTexture(AbilityMath.ABILITY_MIRAGE));
            Assert.AreEqual("", HudMath.AbilityCardCaption(AbilityMath.ABILITY_DUST));
        }

        static string RaceManagerLabel(int id)
        {
            // RaceManager はテストに入っていないので、手配書の名前（AbilityCardCaption か説明の頭）で代わりに確かめる
            string d = HudMath.AbilityDescription(id);
            int k = d.IndexOf('：');
            return k > 0 ? d.Substring(0, k) : "？";
        }

        // ---- r3-11：火薬樽 ----
        [Test] public void BombRules()
        {
            Assert.AreEqual(20f, AbilityMath.Cooldown(AbilityMath.ABILITY_BOMB));
            Assert.AreEqual(1, AbilityMath.BombCount(1));
            Assert.AreEqual(2, AbilityMath.BombCount(3));
            Assert.AreEqual(4f, AbilityMath.BombRadius(1));
            Assert.AreEqual(6f, AbilityMath.BombRadius(3));
            Assert.IsFalse(AbilityMath.BombDetonates(1f, 10f));
            Assert.IsTrue(AbilityMath.BombDetonates(3f, 10f));
            Assert.IsTrue(AbilityMath.BombDetonates(0.5f, 2.9f));
            Assert.IsTrue(AbilityMath.InBlast(Vector3.zero, new Vector3(3f, 5f, 0f), 4f));
            Assert.IsFalse(AbilityMath.InBlast(Vector3.zero, new Vector3(3f, 0f, 3f), 4f));
        }

        // ---- r3-12：集中攻撃 ----
        [Test] public void PileOnGuardAfterThreeHitsInSixSeconds()
        {
            float[] t = { -1f, -1f, -1f, -1f };
            Assert.IsFalse(AbilityMath.PileOnGuard(t, 10f));
            t[0] = 5f; t[1] = 8f;
            Assert.IsFalse(AbilityMath.PileOnGuard(t, 10f));
            t[2] = 10f;
            Assert.IsTrue(AbilityMath.PileOnGuard(t, 10f));
            Assert.IsFalse(AbilityMath.PileOnGuard(t, 11.5f), "6 秒より前の 1 発は数えない");
            Assert.IsFalse(AbilityMath.PileOnGuard(null, 0f));
        }

        // ---- r3-13：道のりで順位 ----
        [Test] public void RemainingAlongBeatsStraightLineAtFork()
        {
            // CP の間のヘアピン（北へ 100m、東へ 20m、南へ 100m で次の CP）。A はまだ行きの序盤、B は帰りの半分
            Vector3[] path = { new Vector3(0, 0, 0), new Vector3(0, 0, 100), new Vector3(20, 0, 100), new Vector3(20, 0, 0) };
            Vector3 a = new Vector3(0, 0, 10);
            Vector3 b = new Vector3(20, 0, 50);
            float straightA = Vector3.Distance(a, path[3]), straightB = Vector3.Distance(b, path[3]);
            float alongA = RaceMath.RemainingAlong(path, a), alongB = RaceMath.RemainingAlong(path, b);
            TestContext.WriteLine("straight A " + straightA + " B " + straightB + " / along A " + alongA + " B " + alongB);
            Assert.Less(straightA, straightB, "直線の距離だと A が前に見える");
            Assert.Less(alongB, alongA, "道のりでは B が前");
            Assert.AreEqual(210f, alongA, 1e-3);
            Assert.AreEqual(50f, alongB, 1e-3);
            Assert.AreEqual(0f, RaceMath.RemainingAlong(path, new Vector3(20, 0, -20)), 1e-3);
            Assert.AreEqual(220f, RaceMath.RemainingAlong(path, Vector3.zero), 1e-3);
            Assert.AreEqual(5f, RaceMath.RemainingAlong(new[] { new Vector3(3, 0, 4) }, Vector3.zero), 1e-4);
            Assert.AreEqual(0f, RaceMath.RemainingAlong(null, Vector3.zero));
        }

        [Test] public void CheckpointPathSamplesTheCourse()
        {
            Vector3[] course = { new Vector3(0, 0, 0), new Vector3(0, 0, 100), new Vector3(50, 0, 100) };
            Vector3[] path = WorldLayout.ArcPolyline(course, 20f, 125f, 10f);
            Assert.AreEqual(12, path.Length);   // 20, 30 … 120, 125
            Assert.AreEqual(20f, path[0].z, 1e-4);
            Assert.AreEqual(25f, path[path.Length - 1].x, 1e-4);
            // 終点（CP）から測った道のりは弧長の差と同じ
            Assert.AreEqual(105f, RaceMath.RemainingAlong(path, path[0]), 1e-3);
            Assert.AreEqual(25f, RaceMath.RemainingAlong(path, new Vector3(0, 0, 100)), 1e-3);
        }

        // ---- r3-14 ----
        [Test] public void HardLimitFollowsStageLength()
        {
            Assert.AreEqual(300f, RaceMath.STAGE_HARD_LIMIT);
            Assert.AreEqual(60f, RaceMath.AFTER_FIRST_FINISH);
        }

        // ---- r3-15 ----
        [Test] public void RecordPageAndBests()
        {
            string page = HudMath.RecordPage(5, 2, 3, 201.4f, 0f, 0f);
            StringAssert.Contains("あなたの戦績", page);
            StringAssert.Contains("5回", page);
            StringAssert.Contains("―", page);
            Assert.IsTrue(HudMath.IsNewBest(0f, 210f));
            Assert.IsTrue(HudMath.IsNewBest(210f, 205f));
            Assert.IsFalse(HudMath.IsNewBest(205f, 210f));
            Assert.IsFalse(HudMath.IsNewBest(205f, 0f));
        }
    }
}
