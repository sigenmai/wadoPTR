using NUnit.Framework;
using HorseRace;
using HorseRace.EditorTools;
using UnityEngine;

namespace HorseRace.Tests
{
    /// <summary>表示の文言・計算（ui round 1）。</summary>
    public class HudMathTests
    {
        [Test]
        public void StageNamesMatchStageDefinitions()
        {
            Assert.AreEqual(StageDefinitions.All.Length, HudMath.STAGE_NAME_COUNT);
            for (int i = 0; i < StageDefinitions.All.Length; i++)
                Assert.AreEqual(StageDefinitions.All[i].label, HudMath.StageName(i), "stage " + i);
        }

        [Test]
        public void StageLabels()
        {
            Assert.AreEqual("第1幕 荒野", HudMath.StageLabel(0));
            Assert.AreEqual("第2幕 砂漠", HudMath.StageLabel(1));
            Assert.AreEqual("第3幕 境目", HudMath.StageLabel(2));
            Assert.AreEqual("第4幕", HudMath.StageLabel(3));
        }

        [Test]
        public void StageColorsAreTheResearchKeys()
        {
            AssertColor(new Color(0.71f, 0.31f, 0.18f), HudMath.StageColor(0));
            AssertColor(new Color(0.88f, 0.69f, 0.31f), HudMath.StageColor(1));
            AssertColor(new Color(0.54f, 0.42f, 0.66f), HudMath.StageColor(2));
            Assert.AreEqual("#B54F2E", HudMath.ColorHex(HudMath.StageColor(0)));
            Assert.AreEqual("#FFFFFF", HudMath.ColorHex(Color.white));
            Assert.AreEqual("#000000", HudMath.ColorHex(Color.black));
        }

        static void AssertColor(Color e, Color a)
        {
            Assert.AreEqual(e.r, a.r, 1e-4f); Assert.AreEqual(e.g, a.g, 1e-4f); Assert.AreEqual(e.b, a.b, 1e-4f);
        }

        [Test]
        public void ShortNameTruncatesToMax()
        {
            Assert.AreEqual("Ann", HudMath.ShortName("Ann", 10));
            Assert.AreEqual("0123456789", HudMath.ShortName("0123456789", 10));
            Assert.AreEqual("012345678…", HudMath.ShortName("0123456789A", 10));
            Assert.AreEqual(10, HudMath.ShortName("VeryLongDisplayName_2026", HudMath.SHORT_NAME_MAX).Length);
            Assert.AreEqual("", HudMath.ShortName(null, 10));
        }

        [Test]
        public void StaminaPipsAreCeilOfTwenties()
        {
            Assert.AreEqual(5, HudMath.StaminaPips(100f, 5));
            Assert.AreEqual(5, HudMath.StaminaPips(80.5f, 5));
            Assert.AreEqual(4, HudMath.StaminaPips(80f, 5));
            Assert.AreEqual(1, HudMath.StaminaPips(0.1f, 5));
            Assert.AreEqual(1, HudMath.StaminaPips(20f, 5));
            Assert.AreEqual(0, HudMath.StaminaPips(0f, 5));
            Assert.AreEqual(5, HudMath.StaminaPips(150f, 5));
        }

        [Test]
        public void StaminaMood()
        {
            Assert.AreEqual(0, HudMath.StaminaMood(100f, false, 5), "満タンは控えめ");
            Assert.AreEqual(1, HudMath.StaminaMood(60f, false, 5));
            Assert.AreEqual(2, HudMath.StaminaMood(20f, false, 5), "残り 1 つ以下は赤");
            Assert.AreEqual(2, HudMath.StaminaMood(90f, true, 5), "ばてたら赤");
        }

        [Test]
        public void ProgressBar()
        {
            Assert.AreEqual("□□□□□□□□□□ ゴール", HudMath.ProgressBar(0, 12, 10));
            Assert.AreEqual("■■■■■□□□□□ ゴール", HudMath.ProgressBar(6, 12, 10));
            Assert.AreEqual("■■■■■■■■■■ ゴール", HudMath.ProgressBar(12, 12, 10));
            Assert.AreEqual("■■■■■■■■■■ ゴール", HudMath.ProgressBar(20, 12, 10));
            Assert.AreEqual("", HudMath.ProgressBar(3, 0, 10));
        }

        [Test]
        public void PunchScaleDecaysToOne()
        {
            Assert.AreEqual(1.6f, HudMath.PunchScale(0f, 1.6f), 1e-5f);
            Assert.Less(HudMath.PunchScale(0.1f, 1.6f), 1.2f);
            Assert.AreEqual(1f, HudMath.PunchScale(1f, 1.6f), 1e-4f);
            Assert.AreEqual(1.6f, HudMath.PunchScale(-1f, 1.6f), 1e-5f);
        }

        [Test]
        public void WristFacingThreshold()
        {
            Assert.IsTrue(HudMath.WristFacing(new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f)));
            Assert.IsFalse(HudMath.WristFacing(new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)));
            Assert.IsFalse(HudMath.WristFacing(new Vector3(0.5f, 0.5f, 0.707f), new Vector3(0f, 1f, 0f)), "0.5 < 0.55");
        }

        [Test]
        public void RankAndFinishText()
        {
            Assert.AreEqual("<size=160%>3</size>位", HudMath.RankMarkup(3));
            Assert.AreEqual("―", HudMath.RankMarkup(0));
            Assert.AreEqual("1:23.4", HudMath.FormatClockTenths(83.47f));
            Assert.AreEqual("0:05.0", HudMath.FormatClockTenths(5f));
            Assert.AreEqual("3着  +6点\n3:20.0", HudMath.FinishText(3, RaceMath.PointsForPlace(2), 200f));
        }

        [Test]
        public void RowsUsePosTags()
        {
            Assert.AreEqual("<pos=0%>1着<pos=15%>name<pos=70%>+10<pos=85%>計24", HudMath.Row4("1着", "name", "+10", "計24"));
            StringAssert.StartsWith("<pos=0%>1位<pos=", HudMath.Row2("1位", "x"));
        }

        [Test]
        public void CooldownLabel()
        {
            Assert.AreEqual("あと 7", HudMath.CooldownLabel(6.2f));
            Assert.AreEqual("あと 1", HudMath.CooldownLabel(0.01f));
            Assert.AreEqual("", HudMath.CooldownLabel(0f));
        }

        [Test]
        public void DescriptionsQuoteAbilityMath()
        {
            for (int a = AbilityMath.ABILITY_DUST; a <= AbilityMath.ABILITY_BALL; a++)
            {
                string d = HudMath.AbilityDescription(a);
                StringAssert.Contains("CD " + Mathf.RoundToInt(AbilityMath.Cooldown(a)) + "秒", d);
            }
            StringAssert.StartsWith("砂塵：", HudMath.AbilityDescription(AbilityMath.ABILITY_DUST));
            StringAssert.Contains(((int)AbilityMath.WALL_AHEAD).ToString() + "m", HudMath.AbilityDescription(AbilityMath.ABILITY_WALL));
            StringAssert.StartsWith("ラクダ：", HudMath.MountDescription(MountMath.MOUNT_CAMEL));
            StringAssert.StartsWith("馬：", HudMath.MountDescription(MountMath.MOUNT_HORSE));
        }

        [Test]
        public void TextStylePresets()
        {
            var chalk = UiStyle.PresetFor(TextStyle.Chalk);
            Assert.AreEqual(0.05f, chalk.dilate, 1e-6f);
            Assert.AreEqual(0.12f, chalk.softness, 1e-6f);
            Assert.AreEqual(0.12f, UiStyle.PresetFor(TextStyle.Ink).dilate, 1e-6f);
            var sign = UiStyle.PresetFor(TextStyle.Sign);
            Assert.AreEqual(0.22f, sign.outlineWidth, 1e-6f);
            Assert.IsTrue(sign.underlay);
            Assert.AreEqual(0.6f, sign.underlayOffset.x, 1e-6f);
            Assert.AreEqual(-0.6f, sign.underlayOffset.y, 1e-6f);
            Assert.AreEqual(0.18f, UiStyle.PresetFor(TextStyle.Brass).outlineWidth, 1e-6f);
            Assert.AreEqual(0.45f, UiStyle.AUTO_MIN, 1e-6f);
        }

        [Test]
        public void DialNumeralsOnCircle()
        {
            Vector2 twelve = UiStyle.DialPoint(12, 0.05f);
            Assert.AreEqual(0f, twelve.x, 1e-5f);
            Assert.AreEqual(0.05f, twelve.y, 1e-5f);
            Vector2 three = UiStyle.DialPoint(3, 0.05f);
            Assert.AreEqual(0.05f, three.x, 1e-5f);
            Assert.AreEqual(0f, three.y, 1e-5f);
        }
    }
}
