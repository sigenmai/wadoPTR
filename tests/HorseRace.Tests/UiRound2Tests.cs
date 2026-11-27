using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using HorseRace;
using HorseRace.EditorTools;
using UnityEngine;

namespace HorseRace.Tests
{
    /// <summary>表示 round 2（ui-r2-01〜18）の文言・計算・読みやすさ・音の波形。</summary>
    public class UiRound2Tests
    {
        // ---- ui-r2-01：最終ステージの点 ----

        [Test] public void FinishTextFinalStageShowsBonusPoints()
        {
            int pts = RaceMath.PointsForPlace(0, RaceMath.FINAL_STAGE);
            Assert.AreEqual(15, pts);
            string t = HudMath.FinishText(1, pts, 83.4f, RaceMath.FINAL_STAGE);
            StringAssert.Contains("+15点", t);
            StringAssert.Contains("(×1.5)", t);
            StringAssert.Contains("1:23.4", t);
        }

        [Test] public void FinishTextOtherStagesHaveNoBonusMark()
        {
            for (int st = 0; st < RaceMath.FINAL_STAGE; st++)
            {
                string t = HudMath.FinishText(1, RaceMath.PointsForPlace(0, st), 60f, st);
                StringAssert.Contains("+10点", t);
                StringAssert.DoesNotContain("×1.5", t);
            }
            Assert.AreEqual(HudMath.FinishText(3, 6, 10f), HudMath.FinishText(3, 6, 10f, -1));
        }

        // ---- ui-r2-03 / r2-02：デスクトップの札と時計が画面の中 ----

        [Test] public void DesktopNeckCardInsideFrustum()
        {
            float k = UiStyle.DESKTOP_HUD_SCALE;
            Vector3 c = UiStyle.DesktopHudOffset;
            float hw = UiStyle.NECK_CARD_W * 0.5f * k;
            float top = UiStyle.NECK_CARD_H * 0.5f * k, bottom = UiStyle.NECK_PIPS_BOTTOM * k;
            foreach (var p in new[] { new Vector3(c.x - hw, c.y + top, c.z), new Vector3(c.x + hw, c.y + top, c.z),
                                      new Vector3(c.x - hw, c.y + bottom, c.z), new Vector3(c.x + hw, c.y + bottom, c.z) })
                Assert.IsTrue(UiStyle.InFrustum(p, 60f, 16f / 9f, 0.05f), "corner " + p);
            // 角の大きさ：横 15° 未満（旧 28°）
            float wDeg = 2f * Mathf.Atan(hw / c.z) * Mathf.Rad2Deg;
            Assert.Less(wDeg, 15f);
        }

        [Test] public void DesktopWatchInsideFrustumAndFacesEye()
        {
            float r = UiStyle.WATCH_DIAL_M * 0.5f * UiStyle.DESKTOP_WATCH_SCALE;
            Vector3 c = UiStyle.DesktopWatchOffset;
            foreach (var d in new[] { new Vector3(-r, -r, 0f), new Vector3(r, -r, 0f), new Vector3(-r, r, 0f), new Vector3(r, r, 0f) })
                Assert.IsTrue(UiStyle.InFrustum(c + d, 60f, 16f / 9f, 0.05f), "corner " + d);
            Assert.Greater(c.x, 0f, "首の札（左下）の反対の右下");
            Assert.AreEqual(0f, UiStyle.DesktopWatchEuler.y, "裏返さない（文字は -Z 側）");
            Assert.Greater(UiStyle.DesktopWatchEuler.x, 0f, "文字盤を目のほうへ起こす");
        }

        // ---- ui-r2-05 / r2-10：VR で読める大きさ ----

        [Test] public void MinVrPtMatchesOnePointThreeDegrees()
        {
            Assert.AreEqual(0.177f, UiStyle.MinVrPt(0.78f), 0.005f);
            Assert.AreEqual(0.136f, UiStyle.MinVrPt(0.6f), 0.005f);
        }

        [Test] public void NeckCardTextsNeverShrinkBelowVrMinimum()
        {
            Assert.AreEqual(UiStyle.NeckTextNames.Length, UiStyle.NeckTextMaxPt.Length);
            float min = UiStyle.MinVrPt(UiStyle.NECK_CARD_EYE_M);
            for (int i = 0; i < UiStyle.NeckTextMaxPt.Length; i++)
                Assert.GreaterOrEqual(UiStyle.MinPtFor(UiStyle.NeckTextMaxPt[i], UiStyle.NECK_CARD_EYE_M), min - 1e-5f, UiStyle.NeckTextNames[i]);
        }

        [Test] public void SeatCardTextsReadableInVr()
        {
            float min = UiStyle.MinVrPt(UiStyle.SEAT_CARD_EYE_M);
            foreach (float pt in new[] { 0.22f, 0.16f, 0.13f })
                Assert.GreaterOrEqual(UiStyle.MinPtFor(pt * UiStyle.SEAT_FONT_SCALE, UiStyle.SEAT_CARD_EYE_M), min - 1e-5f, pt.ToString());
            Assert.GreaterOrEqual(UiStyle.SEAT_KEYS_PT, min);
            Assert.Greater(UiStyle.SEAT_CARD_W, 0.32f);
            StringAssert.Contains("PgUp/PgDn", HudMath.SeatKeysHint());
        }

        // ---- ui-r2-09：紙と文字の明るさの比 ----

        [Test] public void InkOnPaperHasEnoughContrast()
        {
            foreach (var ink in UiStyle.PaperInks)
                Assert.GreaterOrEqual(UiStyle.Contrast(ink, UiStyle.PaperColor), 4.5f, ink.ToString());
            // 旧：明るい字（SignFace）を明るい紙に書いていた
            Assert.Less(UiStyle.Contrast(UiStyle.SignFace, UiStyle.PaperColor), 1.5f);
            Assert.AreEqual(21f, UiStyle.Contrast(Color.black, Color.white), 0.1f);
        }

        // ---- ui-r2-04：フォントに無い字を使わない ----

        static string Root()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "HorseRace", "Scripts"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir);
            return dir;
        }

        /// <summary>IPA ゴシックにある字だけ：ASCII・かな・漢字・全角の記号と英数・CJK の句読点・決めた記号。</summary>
        static bool Allowed(char c)
        {
            int o = c;
            if (o >= 0x20 && o < 0x7F) return true;
            if (o >= 0x3000 && o <= 0x303F) return true;   // 、。「」〜 と全角空白
            if (o >= 0x3040 && o <= 0x30FF) return true;   // かな
            if (o >= 0x4E00 && o <= 0x9FFF) return true;   // 漢字
            if (o >= 0xFF01 && o <= 0xFF5E) return true;   // 全角の英数・記号（！（）：＝｜ と全角数字）
            return "■□↑↓←→◀▶★●◆✓│・×°―…".IndexOf(c) >= 0;
        }

        [Test] public void HudStringsUseOnlyFontGlyphs()
        {
            string root = Root();
            var files = new List<string>();
            foreach (string d in new[] { "UI", "Show", "Lobby" })
                files.AddRange(Directory.GetFiles(Path.Combine(root, "Assets", "HorseRace", "Scripts", d), "*.cs"));
            files.Add(Path.Combine(root, "Assets", "HorseRace", "Scripts", "Logic", "HudMath.cs"));
            var lit = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            int scanned = 0;
            var bad = new List<string>();
            foreach (string f in files)
            {
                foreach (string line in File.ReadAllLines(f))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("//")) continue;
                    foreach (Match m in lit.Matches(line))
                    {
                        scanned++;
                        foreach (char c in m.Groups[1].Value)
                            if (!Allowed(c)) bad.Add(Path.GetFileName(f) + ": U+" + ((int)c).ToString("X4") + " in \"" + m.Groups[1].Value + "\"");
                    }
                }
            }
            Assert.Greater(scanned, 100);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test] public void WrongWayAndMissedUseFontArrow()
        {
            Assert.IsTrue(Allowed('◀'));
            Assert.IsFalse(Allowed('↩'));
        }

        // ---- ui-r2-06：命中の知らせ ----

        [Test] public void HitTexts()
        {
            Assert.AreEqual("命中！ 太郎", HudMath.HitConfirmText("太郎"));
            Assert.AreEqual("命中！", HudMath.HitConfirmText(""));
            Assert.AreEqual("投げ縄に掛かった", HudMath.HitTakenText(AbilityMath.ABILITY_LASSO));
            Assert.AreEqual("砂塵で減速", HudMath.HitTakenText(AbilityMath.ABILITY_DUST));
            Assert.AreEqual("鉄球を受けた", HudMath.HitTakenText(AbilityMath.ABILITY_BALL));
            Assert.AreEqual("撃たれた", HudMath.HitTakenText(HudMath.HIT_REVOLVER));
            for (int a = 0; a < 8; a++) Assert.AreNotEqual(HudMath.HIT_REVOLVER, a);
        }

        // ---- ui-r2-07：操作の説明 ----

        [Test] public void ControlsMatchBindings()
        {
            string pc = HudMath.ControlsPC(), vr = HudMath.ControlsVR();
            StringAssert.Contains("Shift＝疾走", pc);       // MountController.ReadSprint
            StringAssert.Contains("E＝能力", pc);           // AbilityCaster.Update（KeyCode.E）
            StringAssert.Contains("右クリックで構え", pc);  // RevolverHolder（GetMouseButton(1)）
            StringAssert.Contains("左クリックで撃つ", pc);  // RevolverHolder（GetMouseButtonDown(0)）
            StringAssert.Contains("両手を振り下ろす＝疾走", vr);
            StringAssert.Contains("手綱を引く＝止まる", vr);
            StringAssert.Contains("トリガー＝能力", vr);
            string table = HudMath.ControlsTable();
            Assert.AreEqual(HudMath.CONTROLS_LINES - 1, table.Split('\n').Length - 1);
            StringAssert.Contains("<pos=52%>", table);
            Assert.AreEqual("E", HudMath.AbilityHint(false));
            Assert.AreEqual("トリガー", HudMath.AbilityHint(true));
            Assert.AreEqual(3, HudMath.ABILITY_HINT_CASTS);
        }

        // ---- ui-r2-08：ステージの合間 ----

        [Test] public void GapAndBetweenStages()
        {
            Assert.AreEqual("首位まで 4点", HudMath.GapText(10, 14));
            Assert.AreEqual("首位", HudMath.GapText(14, 14));
            Assert.AreEqual("首位", HudMath.GapText(20, 14));
            Assert.AreEqual("首位まで 4点  次：砂漠", HudMath.BetweenStagesLine(10, 14, 1, 3));
            Assert.AreEqual("首位", HudMath.BetweenStagesLine(14, 14, 3, 3));
            StringAssert.Contains("1.5倍", HudMath.BetweenStagesHeader(RaceMath.FINAL_STAGE, 3));
            Assert.AreEqual("総合", HudMath.BetweenStagesHeader(1, 3));
        }

        // ---- ui-r2-12：遺物の Lv ----

        [Test] public void LevelEffectUsesAbilityMath()
        {
            StringAssert.Contains(Mathf.RoundToInt(AbilityMath.DustRadius(2)) + "m", HudMath.LevelEffect(AbilityMath.ABILITY_DUST, 2));
            StringAssert.Contains(Mathf.RoundToInt(AbilityMath.LassoRange(3)) + "m", HudMath.LevelEffect(AbilityMath.ABILITY_LASSO, 3));
            StringAssert.Contains(AbilityMath.WallCount(2) + "枚", HudMath.LevelEffect(AbilityMath.ABILITY_WALL, 2));
            StringAssert.Contains(AbilityMath.BallCount(3) + "個", HudMath.LevelEffect(AbilityMath.ABILITY_BALL, 3));
            StringAssert.StartsWith("遺物 Lv2 ", HudMath.RelicNote(AbilityMath.ABILITY_DUST, 2));
        }

        // ---- ui-r2-13：町の大時計 ----

        [Test] public void TownClockTexts()
        {
            Assert.AreEqual(0, HudMath.ClockUrgency(42f));
            Assert.AreEqual(1, HudMath.ClockUrgency(29.9f));
            Assert.AreEqual(2, HudMath.ClockUrgency(9.9f));
            Assert.AreEqual(0, HudMath.ClockUrgency(-1f));
            StringAssert.Contains("0:42", HudMath.TownClockText(42f, 3, false, 0));
            StringAssert.Contains("<color=", HudMath.TownClockText(5f, 3, false, 0));
            StringAssert.Contains("あと 1 人で出走時計が動く", HudMath.TownClockText(-1f, 1, false, 0));
            StringAssert.Contains("あと 2 人", HudMath.TownClockText(-1f, 0, false, 0));
            string r = HudMath.TownClockText(-1f, 3, true, 1);
            StringAssert.Contains("レース中", r);
            StringAssert.Contains("第2幕 砂漠", r);
        }

        // ---- ui-r2-15：順位表の進み具合 ----

        [Test] public void StandingCell()
        {
            Assert.AreEqual("■■■□□□", HudMath.StandingCell(5, 10, false, 0f));
            Assert.AreEqual("ゴール 1:23.4", HudMath.StandingCell(10, 10, true, 83.45f));
            Assert.AreEqual("", HudMath.StandingCell(0, 0, false, 0f));
            StringAssert.Contains("<pos=62%>", HudMath.RaceRow("1位", "a", "b", "c"));
        }

        // ---- ui-r2-16：懐中時計の文字盤 ----

        [Test] public void DialMapsWholeStage()
        {
            float T = RaceMath.STAGE_HARD_LIMIT;
            Assert.AreEqual(0f, HudMath.DialAngle(T, T), 1e-4);
            Assert.AreEqual(-180f, HudMath.DialAngle(T * 0.5f, T), 1e-3);
            Assert.AreEqual(-360f, HudMath.DialAngle(0f, T), 1e-4);
            Assert.AreEqual(1, HudMath.NumeralState(1, T, T));
            Assert.AreEqual(2, HudMath.NumeralState(12, T, T));
            Assert.AreEqual(0, HudMath.NumeralState(1, T * 0.9f, T));   // 36° 過ぎた → 1（30°）は過ぎた
            Assert.AreEqual(1, HudMath.NumeralState(2, T * 0.9f, T));
            Assert.AreEqual(0, HudMath.NumeralState(6, T * 0.5f, T));
            Assert.AreEqual(-60f, HudMath.SecondHandAngle(10.7f), 1e-4);
            Assert.AreEqual(HudMath.SecondHandAngle(10.1f), HudMath.SecondHandAngle(10.9f));
        }

        // ---- ui-r2-18：酔い止め ----

        [Test] public void VignetteStrength()
        {
            Assert.AreEqual(0f, HudMath.VignetteStrength(18f, 200f, 0));
            Assert.AreEqual(0f, HudMath.VignetteStrength(5f, 10f, 2));
            Assert.AreEqual(0.85f, HudMath.VignetteStrength(18f, 0f, 2), 1e-4);
            Assert.AreEqual(0.5f, HudMath.VignetteStrength(0f, 150f, 1), 1e-4);
            Assert.Less(HudMath.VignetteStrength(13f, 0f, 1), HudMath.VignetteStrength(13f, 0f, 2));
            Assert.AreEqual("視界の縁：弱", HudMath.VignetteLabel(1));
        }

        // ---- ui-r2-11：仮の効果音 ----

        [Test] public void SfxClipsHaveLengthAndSafePeak()
        {
            var names = new HashSet<string>();
            for (int k = 0; k < SfxMath.KIND_COUNT; k++)
            {
                float[] w = SfxMath.Generate(k, SfxMath.SAMPLE_RATE);
                Assert.AreEqual(Mathf.RoundToInt(SfxMath.Seconds(k) * SfxMath.SAMPLE_RATE), w.Length, SfxMath.Name(k));
                Assert.Greater(w.Length, 0, SfxMath.Name(k));
                float peak = SfxMath.Peak(w);
                Assert.LessOrEqual(peak, 0.9f, SfxMath.Name(k));
                Assert.Greater(peak, 0.5f, SfxMath.Name(k) + " は無音");
                Assert.Less(Mathf.Abs(w[w.Length - 1]), 0.05f, SfxMath.Name(k) + " の最後がプチッと切れる");
                Assert.IsTrue(names.Add(SfxMath.Name(k)), "名前の重複");
            }
            Assert.AreEqual(2205, SfxMath.SampleCount(0.1f, 22050));
            Assert.AreEqual(0, SfxMath.SampleCount(0f, 22050));
            Assert.AreEqual(32767, SfxMath.ToPcm16(2f));
            Assert.AreEqual(-32767, SfxMath.ToPcm16(-1f));
        }

        [Test] public void SfxNoiseIsDeterministicAndBounded()
        {
            for (int i = 0; i < 2000; i++)
            {
                float n = SfxMath.Noise(i, 5);
                Assert.That(n, Is.InRange(-1f, 1f));
                Assert.AreEqual(n, SfxMath.Noise(i, 5));
            }
            Assert.AreEqual(0f, SfxMath.Envelope(-0.1f, 1f, 0.01f, 1f));
            Assert.AreEqual(0f, SfxMath.Envelope(1f, 1f, 0.01f, 1f), 1e-5);
        }
    }
}
