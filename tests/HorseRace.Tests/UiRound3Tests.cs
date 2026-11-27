using System.IO;
using NUnit.Framework;
using HorseRace;
using HorseRace.EditorTools;
using UnityEngine;

namespace HorseRace.Tests
{
    /// <summary>表示 round 3（ui-r3-01〜16）の文言・計算・置き場所。</summary>
    public class UiRound3Tests
    {
        // ---- ui-r3-01：中継の結果のカット ----

        [Test] public void ResultCaptions()
        {
            Assert.AreEqual("第2幕 砂漠｜結果｜1着 太郎", HudMath.StageResultCaption(1, "太郎"));
            Assert.AreEqual("第1幕 荒野｜結果", HudMath.StageResultCaption(0, ""));
            Assert.AreEqual("総合結果｜優勝 花子  24点", HudMath.FinalResultCaption("花子", 24));
            Assert.AreEqual("総合結果", HudMath.FinalResultCaption("", 0));
        }

        [Test] public void PodiumOrbitStaysWithinTwelveDegrees()
        {
            float max = 0f;
            for (float t = 0f; t <= 20f; t += 0.1f) max = Mathf.Max(max, Mathf.Abs(HudMath.OrbitYaw(t, 12f, 20f)));
            Assert.AreEqual(12f, max, 0.05f);
            Assert.AreEqual(0f, HudMath.OrbitYaw(0f, 12f, 20f), 1e-4f);
            Assert.AreEqual(0f, HudMath.OrbitYaw(5f, 12f, 0f));
        }

        // ---- ui-r3-02：先頭のゴール ----

        [Test] public void LeaderFinishedTextAndDial()
        {
            Assert.AreEqual("先頭ゴール！ 残り1:00", HudMath.LeaderFinishedText());
            Assert.AreEqual(0f, HudMath.DialAngle(60f, 60f), 1e-4f);
            Assert.AreEqual(60f, HudMath.DialTotal(false, true));
            Assert.AreEqual(RaceMath.STAGE_HARD_LIMIT, HudMath.DialTotal(false, false));
            Assert.AreEqual(RaceMath.LOBBY_WAIT_SECONDS, HudMath.DialTotal(true, true));
            // 先頭のゴールの瞬間（残り 60 秒）は新しい 1 周の始まり：12 の近くへ飛ばない
            Assert.AreEqual(0f, HudMath.DialAngle(60f, HudMath.DialTotal(false, true)), 1e-4f);
            Assert.Less(HudMath.DialAngle(60f, HudMath.DialTotal(false, false)), -250f);   // 旧：330 秒の 1 周では 293° 先へ飛んだ
        }

        [Test] public void RemainingTurnsRedUnderAMinute()
        {
            StringAssert.Contains("#B81F14", HudMath.RemainingMarkup(60f));
            StringAssert.Contains("#B81F14", HudMath.RemainingMarkup(12f));
            StringAssert.DoesNotContain("#B81F14", HudMath.RemainingMarkup(61f));
            StringAssert.Contains("1:01", HudMath.RemainingMarkup(61f));
        }

        // ---- ui-r3-03：時間切れ ----

        [Test] public void TimeUpTexts()
        {
            Assert.AreEqual("時間切れ", HudMath.TimeUpText());
            Assert.AreEqual("0点（時間切れ）", HudMath.TimeUpLine());
        }

        // ---- ui-r3-04：デスクトップの能力の札 ----

        [Test] public void DesktopAbilityCardInsideFrustum()
        {
            float k = UiStyle.DESKTOP_HUD_SCALE;
            Vector3 c = UiStyle.DesktopAbilityOffset;
            float hw = UiStyle.POUCH_CARD_W * 0.5f, hh = UiStyle.POUCH_CARD_H * 0.5f;
            foreach (var corner in new[] { new Vector3(-hw, hh, 0f), new Vector3(hw, hh, 0f), new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f) })
                Assert.IsTrue(UiStyle.InFrustum(c + corner * k, 60f, 16f / 9f, 0.05f), "card corner " + corner);
            float g = UiStyle.POUCH_GEM_M * 0.5f;
            for (int i = 0; i < 3; i++)
                foreach (var d in new[] { new Vector3(-g, -g, 0f), new Vector3(g, g, 0f) })
                    Assert.IsTrue(UiStyle.InFrustum(c + (UiStyle.PouchGemLocal(i) + d) * k, 60f, 16f / 9f, 0.05f), "gem " + i);
            // 首の札（左下）の反対・同じ高さ
            Assert.Greater(c.x, 0f);
            Assert.AreEqual(-UiStyle.DesktopHudOffset.x, c.x, 1e-4f);
            Assert.AreEqual(UiStyle.DesktopHudOffset.y, c.y, 1e-4f);
        }

        [Test] public void DesktopWatchDoesNotOverlapAbilityCard()
        {
            float r = UiStyle.WATCH_DIAL_M * 0.5f * UiStyle.DESKTOP_WATCH_SCALE;
            Vector3 w = UiStyle.DesktopWatchOffset;
            Vector3 a = UiStyle.DesktopAbilityOffset;
            float cardTop = a.y + UiStyle.POUCH_CARD_H * 0.5f * UiStyle.DESKTOP_HUD_SCALE;
            Assert.Greater(w.y - r, cardTop + 0.05f, "時計は能力の札より上");
            foreach (var d in new[] { new Vector3(-r, -r, 0f), new Vector3(r, r, 0f) })
                Assert.IsTrue(UiStyle.InFrustum(w + d, 60f, 16f / 9f, 0.05f));
        }

        // ---- ui-r3-05：VR の振動 ----

        [Test] public void HapticsAreClamped()
        {
            Assert.AreEqual(8, UiStyle.AllHaptics.Length);
            foreach (var h in UiStyle.AllHaptics)
            {
                Assert.LessOrEqual(h.x, 0.5f); Assert.Greater(h.x, 0f);
                Assert.LessOrEqual(h.y, 1f); Assert.Greater(h.y, 0f);
                Assert.Greater(h.z, 0f); Assert.LessOrEqual(h.z, 320f);
            }
            Assert.AreEqual(0.5f, HudMath.ClampHapticSeconds(2f));
            Assert.AreEqual(1f, HudMath.ClampHapticAmp(3f));
            Assert.AreEqual(0f, HudMath.ClampHapticAmp(-1f));
            // 被弾は重く（低い周波数・強い）、CP は軽く
            Assert.Greater(UiStyle.HapticHitTaken.y, UiStyle.HapticCheckpoint.y);
            Assert.Less(UiStyle.HapticHitTaken.z, UiStyle.HapticCheckpoint.z);
        }

        // ---- ui-r3-06：表彰台の順 ----

        [Test] public void PodiumRevealsAscendingWithHeldBeat()
        {
            Assert.Less(DirectorMath.PODIUM_DRUM_SECONDS, DirectorMath.PODIUM_REVEAL_3RD + 0.01f);
            Assert.Less(DirectorMath.PODIUM_REVEAL_3RD, DirectorMath.PODIUM_REVEAL_2ND);
            Assert.Less(DirectorMath.PODIUM_REVEAL_2ND, DirectorMath.PODIUM_REVEAL_1ST);
            Assert.GreaterOrEqual(DirectorMath.PODIUM_REVEAL_1ST - DirectorMath.PODIUM_REVEAL_2ND, DirectorMath.PODIUM_REVEAL_2ND - DirectorMath.PODIUM_REVEAL_3RD, "1 位の前は長めの間");
            Assert.Less(DirectorMath.PODIUM_REVEAL_1ST, 20f * 0.5f);   // 総合結果 20 秒の前半で
            Assert.AreEqual("<b><size=150%>総合結果</size></b>", HudMath.RESULTS_HEADING);
        }

        // ---- ui-r3-09：知らせの優先度 ----

        [Test] public void NotePriority()
        {
            Assert.IsTrue(HudMath.NoteWins(HudMath.NOTE_DANGER, HudMath.NOTE_INFO, 1f));
            Assert.IsTrue(HudMath.NoteWins(HudMath.NOTE_REWARD, HudMath.NOTE_REWARD, 1f));
            Assert.IsFalse(HudMath.NoteWins(HudMath.NOTE_INFO, HudMath.NOTE_DANGER, 1f), "近道で被弾の知らせを消さない");
            Assert.IsFalse(HudMath.NoteWins(HudMath.NOTE_REWARD, HudMath.NOTE_DANGER, 0.2f));
            Assert.IsTrue(HudMath.NoteWins(HudMath.NOTE_INFO, HudMath.NOTE_DANGER, 0f), "消えた知らせは上書きしてよい");
            Assert.IsTrue(HudMath.NoteWins(HudMath.NOTE_INFO, HudMath.NOTE_DANGER, -1f));
        }

        // ---- ui-r3-10：ゴールした後 ----

        [Test] public void FinishedTexts()
        {
            Assert.AreEqual("<size=160%>3</size>着", HudMath.FinishedRank(3));
            Assert.AreEqual("ゴール\n1:23.4", HudMath.FinishedTime(83.45f));
            Assert.AreEqual("<size=70%>囲いの掲示板で観戦</size>", HudMath.FinishedLine());
        }

        // ---- ui-r3-11：ロビーの手順 ----

        [Test] public void LobbySteps()
        {
            Assert.AreEqual(0, HudMath.LobbyNextStep(HudMath.LobbyStepMask(false, false, false, false)));
            Assert.AreEqual(1, HudMath.LobbyNextStep(HudMath.LobbyStepMask(true, false, false, false)));
            Assert.AreEqual(0, HudMath.LobbyNextStep(HudMath.LobbyStepMask(false, true, true, false)), "飛ばした手順に戻す");
            Assert.AreEqual(3, HudMath.LobbyNextStep(HudMath.LobbyStepMask(true, true, true, false)));
            Assert.AreEqual(HudMath.LOBBY_STEPS, HudMath.LobbyNextStep(HudMath.LobbyStepMask(true, true, true, true)));
            int m = HudMath.LobbyStepMask(true, false, false, false);
            StringAssert.Contains("✓", HudMath.LobbyStepText(0, m));
            StringAssert.Contains("#B81F14", HudMath.LobbyStepText(0, m));
            StringAssert.StartsWith("<b>", HudMath.LobbyStepText(1, m));
            Assert.AreEqual("三 台帳にサイン", HudMath.LobbyStepText(2, m));
            string table = HudMath.LobbyStepsTable();
            for (int i = 0; i < 4; i++) StringAssert.Contains(HudMath.LobbyStepLabel(i), table);
            Assert.AreEqual(4, LobbyLayout.StepSigns.Length);
        }

        [Test] public void StepSignsStandInFrontOfTheirStations()
        {
            Vector3[] stations = { LobbyLayout.WANTED_WALL, LobbyLayout.CORRAL_FIRST, LobbyLayout.LEDGER_DESK, LobbyLayout.START_BELL };
            for (int i = 0; i < 4; i++)
            {
                float d = WorldLayout.DistXZ(LobbyLayout.StepSigns[i], stations[i]);
                Assert.That(d, Is.InRange(2f, 5f), "札 " + (i + 1));
                // スポーンに近い側（手前）
                Assert.Less(WorldLayout.DistXZ(LobbyLayout.StepSigns[i], LobbyLayout.SPAWN), WorldLayout.DistXZ(stations[i], LobbyLayout.SPAWN) + 0.5f, "札 " + (i + 1));
            }
            Assert.Less(WorldLayout.DistXZ(LobbyLayout.STEPS_BOARD, LobbyLayout.SPAWN), 6f);
        }

        // ---- ui-r3-12：鉄球はプリミティブではない ----

        [Test] public void IronBallUsesRockAsset()
        {
            string root = Root();
            string mb = File.ReadAllText(Path.Combine(root, "Assets", "HorseRace", "Editor", "Gen", "MountBuilder.cs"));
            StringAssert.DoesNotContain("Prim(PrimitiveType.Sphere, \"Ball\"", mb);   // （かすみの内向き球は画面の効果で、見える物ではない）
            StringAssert.Contains("RockCollection", mb);
            StringAssert.Contains("UiStyle.BALL_ROCK_PART", mb);
            Assert.AreEqual("RockCollection_108", UiStyle.BALL_ROCK_PART);
            Assert.AreEqual(0.7f, UiStyle.BALL_VISUAL_M, 1e-4f);
        }

        // ---- ui-r3-13：着順の祝い方 ----

        [Test] public void ConfettiScalesWithPlace()
        {
            Assert.AreEqual(300, DirectorMath.ConfettiForPlace(1));
            Assert.AreEqual(120, DirectorMath.ConfettiForPlace(2));
            Assert.AreEqual(120, DirectorMath.ConfettiForPlace(3));
            Assert.AreEqual(30, DirectorMath.ConfettiForPlace(4));
            Assert.AreEqual(30, DirectorMath.ConfettiForPlace(8));
            Assert.AreEqual(0, DirectorMath.ConfettiForPlace(0));
            Assert.AreEqual(1.35f, DirectorMath.TagScaleForPlace(1), 1e-4f);
            Assert.AreEqual(1f, DirectorMath.TagScaleForPlace(2), 1e-4f);
        }

        // ---- ui-r3-14：何人中と前との差 ----

        [Test] public void RankOutOfCountAndGap()
        {
            Assert.AreEqual("<size=160%>3</size><size=60%>/8</size>位", HudMath.RankMarkup(3, 8));
            Assert.AreEqual("<size=160%>1</size>位", HudMath.RankMarkup(1, 1));
            Assert.AreEqual("―", HudMath.RankMarkup(0, 8));
            Assert.AreEqual("<size=70%>前まで 12m</size>", HudMath.GapAheadText(12.2f));
            Assert.AreEqual("", HudMath.GapAheadText(61f));
            Assert.AreEqual("", HudMath.GapAheadText(-1f));
            StringAssert.Contains("#FFC745", HudMath.GapAheadText(5f));   // 風よけの距離は金
            StringAssert.DoesNotContain("#FFC745", HudMath.GapAheadText(9f));
            Assert.AreEqual(7f, HudMath.GapMeters(3, 20f, 3, 13f, 50f), 1e-4f);   // 同じ CP：距離の差
            Assert.AreEqual(30f, HudMath.GapMeters(3, 20f, 4, 90f, 30f), 1e-4f);   // 違う CP：直線
            Assert.AreEqual(-1f, HudMath.GapMeters(3, 20f, -1, 0f, 10f));          // 前はゴール済み
            Assert.AreEqual(0f, HudMath.GapMeters(3, 10f, 3, 12f, 1f));            // 並んでいる
        }

        // ---- ui-r3-15：フォントに無い字の名前 ----

        [Test] public void SafeNameReplacesMissingGlyphs()
        {
            Assert.AreEqual("Ann", HudMath.SafeName("Ann", 0));
            Assert.AreEqual("太郎", HudMath.SafeName("太郎", 0));
            Assert.AreEqual("ライダー3", HudMath.SafeName("김철수", 2), "ハングルだけ");
            Assert.AreEqual("ライダー1", HudMath.SafeName("\U0001F40E\U0001F525", 0), "絵文字だけ（サロゲートペア）");
            Assert.AreEqual("Bob・本", HudMath.SafeName("Bob\U0001F40E\U0001F525본本", 1), "混ざった名前：続く「・」は 1 つ");
            Assert.AreEqual("Zoë Ωмир", HudMath.SafeName("Zoë Ωмир", 0), "Latin-1・ギリシャ・キリル");
            Assert.AreEqual("a・b", HudMath.SafeName("a<b", 0), "TMP のタグにしない");
            Assert.AreEqual("ライダー", HudMath.SafeName(null, -1));
            Assert.AreEqual("ライダー5", HudMath.SafeName("", 4));
            Assert.AreEqual("Bob・本", HudMath.DisplayName("Bob\U0001F40E본本", 10));
        }

        // ---- ui-r3-16・r3-07：観客の予想・練習 ----

        [Test] public void PickCaptionAndPracticeLines()
        {
            Assert.AreEqual("予想 的中！ 太郎 1着  +3点", HudMath.PickCaption("的中！ 太郎 1着  +3点"));
            Assert.AreEqual("南の門→北の門→南の門", HudMath.PracticeRouteLine(0));
            StringAssert.StartsWith("<color=#FFC745>南の門</color>→北の門", HudMath.PracticeRouteLine(1));
            StringAssert.Contains("<color=#FFC745>北の門</color>", HudMath.PracticeRouteLine(2));
            Assert.AreEqual("自己ベスト\n1:02.3", HudMath.PracticeBestText(62.3f));
            Assert.AreEqual("自己ベスト\n―", HudMath.PracticeBestText(-1f));
        }

        // ---- ui-r3-08：狙いの印 ----

        [Test] public void AimMarkerConstants()
        {
            Assert.AreEqual(0.1f, AbilityMath.AIM_PROBE_INTERVAL, 1e-4f);   // 10 Hz
            Assert.AreEqual(2.9f, AbilityMath.AIM_MARKER_HEIGHT, 1e-4f);
            Assert.AreEqual(180f, AbilityMath.AIM_MARKER_SPIN_DEG, 1e-4f);
            Assert.Greater(AbilityMath.BALL_AIM_RANGE, AbilityMath.LassoRange(1));
            Assert.Greater(AbilityMath.AIM_PROBE_REACH, 1f);
        }

        static string Root()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "HorseRace", "Scripts"))) dir = Path.GetDirectoryName(dir);
            Assert.IsNotNull(dir);
            return dir;
        }
    }
}
