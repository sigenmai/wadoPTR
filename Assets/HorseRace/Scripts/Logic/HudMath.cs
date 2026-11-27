using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 表示（首の札・懐中時計・黒板・電報・手配書の説明）の文言と小さな計算。Static only（UdonSharp-safe：ジェネリック・LINQ なし）。
    /// ステージ名・ステージの色・名前の切り詰め・持久力の印の数・進み具合の棒・跳ねる拡大・表の列（TMP の pos タグ）。
    /// </summary>
    public class HudMath : UdonSharp.UdonSharpBehaviour
    {
        /// <summary>ステージ名の数（StageName の 0..STAGE_NAME_COUNT-1）。UdonSharp は static フィールドを持てないので文字列は StageName の中。</summary>
        public const int STAGE_NAME_COUNT = 3;

        public const int STAMINA_PIPS = 5;
        public const int PROGRESS_SEGMENTS = 10;
        public const int SHORT_NAME_MAX = 10;
        /// <summary>手首を顔に向けたと見なす内積（手の上向き・手から頭への向き）。</summary>
        public const float WRIST_DOT = 0.55f;
        /// <summary>跳ねる拡大の減衰（1/秒）。</summary>
        public const float PUNCH_DECAY = 14f;

        /// <summary>StageDefinitions.All[i].label と同じ（単体テストで確かめる）。範囲外は空。</summary>
        public static string StageName(int st)
        {
            if (st == 0) return "荒野";
            if (st == 1) return "砂漠";
            if (st == 2) return "境目";
            return "";
        }

        /// <summary>
        /// ステージの「深い砂」（道の上も砂、馬は ×0.82）の割合（%）。StageDefinitions の sandTrailArcs ÷ ゴールまでの長さ（単体テストで確かめる。system-r2-01）。
        /// </summary>
        public static int DeepSandPercent(int st)
        {
            if (st == 1) return DEEP_SAND_DUNES_PCT;
            if (st == 2) return DEEP_SAND_MESA_PCT;
            return 0;
        }

        public const int DEEP_SAND_DUNES_PCT = 31;
        public const int DEEP_SAND_MESA_PCT = 9;

        /// <summary>
        /// ステージの地形と有利な乗り物（ステージの合間の電報・囲いの手配書。system-r1-08 / r1-21 / r2-01）。
        /// 深い砂の割合が多いほどラクダ（固い地面は馬が約 8% 速く、深い砂はラクダが約 13% 速い）。最終ステージは得点 1.5 倍。
        /// </summary>
        public static string StageHint(int st)
        {
            if (st == 0) return "荒野 ― 深い砂なし・馬有利（岩場・崖の近道）";
            if (st == 1) return "砂漠 ― 深い砂 " + DeepSandPercent(1).ToString() + "%・ラクダ有利";
            if (st == 2) return "境目 ― 深い砂 " + DeepSandPercent(2).ToString() + "%・馬有利　最終ステージ 得点1.5倍";
            return "";
        }

        /// <summary>ステージの合間の電報の締め：「次：砂漠 ― 深い砂 31%・ラクダ有利…」。最後のステージの後は空。</summary>
        public static string NextStageLine(int stage, int stageCount)
        {
            int next = stage + 1;
            if (next >= stageCount) return "";
            return "次：" + StageHint(next) + "\n囲いの手配書で乗り物・能力を選び直せる";
        }

        /// <summary>総合結果の締め（system-r2-06）：同じ顔ぶれのまま 30 秒後にもう一戦。</summary>
        public static string RematchLine()
        {
            return "もう一戦？ " + Mathf.RoundToInt(RaceMath.LOBBY_READY_SECONDS * 2f).ToString() + " 秒後に出走（台帳で取り消し可）";
        }

        /// <summary>やり直しの長押しの札（system-r2-03）："やり直し… ■■■□□"。</summary>
        public static string ResetHoldText(float progress)
        {
            int n = Mathf.Clamp(Mathf.FloorToInt(progress * 5f + 0.0001f), 0, 5);
            string bar = "";
            for (int i = 0; i < 5; i++) bar += i < n ? "■" : "□";
            return "やり直し… " + bar;
        }

        /// <summary>"第1幕 荒野"（st は 0 始まり）。範囲外は "第4幕"。</summary>
        public static string StageLabel(int st)
        {
            string n = StageName(st);
            string s = "第" + (st + 1).ToString() + "幕";
            return n.Length > 0 ? s + " " + n : s;
        }

        /// <summary>ステージの色：荒野 = 赤錆、砂漠 = 砂金、境目 = 夕暮れの紫（参考調査の 1 ステージ 1 色）。</summary>
        public static Color StageColor(int st)
        {
            if (st == 1) return new Color(0.88f, 0.69f, 0.31f);
            if (st == 2) return new Color(0.54f, 0.42f, 0.66f);
            return new Color(0.71f, 0.31f, 0.18f);
        }

        /// <summary>TMP の色タグ用（"#B54F2E"）。</summary>
        public static string ColorHex(Color c)
        {
            return "#" + Hex2(c.r) + Hex2(c.g) + Hex2(c.b);
        }

        static string Hex2(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            string digits = "0123456789ABCDEF";
            return digits.Substring(n / 16, 1) + digits.Substring(n % 16, 1);
        }

        /// <summary>名前を max 文字までに切り詰める（超えたら max-1 文字＋「…」）。null は空。</summary>
        public static string ShortName(string name, int max)
        {
            if (name == null) return "";
            if (max < 2) max = 2;
            if (name.Length <= max) return name;
            return name.Substring(0, max - 1) + "…";
        }

        /// <summary>持久力（0..100）→ 点ける印の数 = ceil(持久力 / (100/印の数))。0..pips。</summary>
        public static int StaminaPips(float stamina, int pips)
        {
            if (pips <= 0) return 0;
            if (stamina <= 0f) return 0;
            int n = Mathf.CeilToInt(stamina / (100f / pips) - 0.0001f);
            return Mathf.Clamp(n, 0, pips);
        }

        /// <summary>印の列の見せ方：0 = 満タン（薄く小さく）、1 = ふつう、2 = 危ない（赤く脈打つ）。</summary>
        public static int StaminaMood(float stamina, bool exhausted, int pips)
        {
            if (exhausted) return 2;
            int lit = StaminaPips(stamina, pips);
            if (lit <= 1) return 2;
            if (stamina >= 99.5f) return 0;
            return 1;
        }

        /// <summary>"■■■■□□□□□□ ゴール"。cpReached = 通った CP の数（0..cpCount）。cpCount ≤ 0 は空。</summary>
        public static string ProgressBar(int cpReached, int cpCount, int segments)
        {
            if (cpCount <= 0 || segments <= 0) return "";
            int filled = Mathf.Clamp(cpReached * segments / cpCount, 0, segments);
            string s = "";
            for (int i = 0; i < segments; i++) s += i < filled ? "■" : "□";
            return s + " ゴール";
        }

        /// <summary>跳ねる拡大：t 秒後の倍率（from → 1、exp(-14t)）。</summary>
        public static float PunchScale(float t, float from)
        {
            if (t < 0f) t = 0f;
            return 1f + (from - 1f) * Mathf.Exp(-PUNCH_DECAY * t);
        }

        /// <summary>手首を顔に向けているか（handUp = 手の上向き、toHead = 手から頭への向き、どちらも正規化）。</summary>
        public static bool WristFacing(Vector3 handUp, Vector3 toHead)
        {
            return Vector3.Dot(handUp, toHead) > WRIST_DOT;
        }

        /// <summary>"<size=160%>3</size>位"（数字を大きく）。rank ≤ 0 は "―"。</summary>
        public static string RankMarkup(int rank)
        {
            if (rank <= 0) return "―";
            return "<size=160%>" + rank.ToString() + "</size>位";
        }

        /// <summary>"1:23.4"（10 分の 1 秒まで、切り捨て）。</summary>
        public static string FormatClockTenths(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int tenths = Mathf.FloorToInt(seconds * 10f + 0.0001f);
            int m = tenths / 600;
            int s = (tenths / 10) % 60;
            int d = tenths % 10;
            return m.ToString() + ":" + (s < 10 ? "0" : "") + s.ToString() + "." + d.ToString();
        }

        /// <summary>ゴールの札："3着  +6点\n1:23.4"。</summary>
        public static string FinishText(int place, int points, float seconds)
        {
            return FinishText(place, points, seconds, -1);
        }

        /// <summary>ゴールの札（ステージ付き、ui-r2-01）：最終ステージは点の横に小さく "(×1.5)"。points は RaceMath.PointsForPlace(place-1, stage) を渡す。</summary>
        public static string FinishText(int place, int points, float seconds, int stage)
        {
            string bonus = stage == RaceMath.FINAL_STAGE ? "<size=70%>(×1.5)</size>" : "";
            return place.ToString() + "着  +" + points.ToString() + "点" + bonus + "\n" + FormatClockTenths(seconds);
        }

        // ---- 命中の知らせ（ui-r2-06）----

        /// <summary>被弾の種類：リボルバー（能力の番号と重ならない値）。</summary>
        public const int HIT_REVOLVER = 100;

        /// <summary>撃った側の知らせ："命中！ 名前"。</summary>
        public static string HitConfirmText(string targetName)
        {
            if (targetName == null || targetName.Length == 0) return "命中！";
            return "命中！ " + targetName;
        }

        /// <summary>撃たれた側の知らせ（能力の番号か HIT_REVOLVER）。</summary>
        public static string HitTakenText(int kind)
        {
            if (kind == AbilityMath.ABILITY_LASSO) return "投げ縄に掛かった";
            if (kind == AbilityMath.ABILITY_DUST) return "砂塵で減速";
            if (kind == AbilityMath.ABILITY_BALL) return "鉄球を受けた";
            if (kind == HIT_REVOLVER) return "撃たれた";
            return "被弾";
        }

        // ---- 操作の説明（ui-r2-07）：キーは MountController（Shift・Space・WASD）・AbilityCaster（E）・RevolverHolder（右・左クリック）と同じ ----

        /// <summary>能力の札の「どのボタンで使うか」："E"（デスクトップ）/ "トリガー"（VR）。</summary>
        public static string AbilityHint(bool vr)
        {
            return vr ? "トリガー" : "E";
        }

        /// <summary>はじめの何回の発動まで能力の札にボタンを出すか。</summary>
        public const int ABILITY_HINT_CASTS = 3;

        /// <summary>操作の説明の行数（VR・PC とも）。</summary>
        public const int CONTROLS_LINES = 6;

        /// <summary>VR の操作 i 行目（0 = 見出し）。</summary>
        public static string ControlsLineVR(int i)
        {
            if (i == 0) return "<b>VR</b>";
            if (i == 1) return "左スティック＝曲がる";
            if (i == 2) return "両手を振り下ろす＝疾走";
            if (i == 3) return "手綱を引く＝止まる";
            if (i == 4) return "トリガー＝能力";
            if (i == 5) return "銃をつかんでトリガー＝撃つ";
            return "";
        }

        /// <summary>デスクトップの操作 i 行目（0 = 見出し）。</summary>
        public static string ControlsLinePC(int i)
        {
            if (i == 0) return "<b>PC</b>";
            if (i == 1) return "WASD＝走る・曲がる";
            if (i == 2) return "Shift＝疾走　Space＝跳ぶ";
            if (i == 3) return "E＝能力";
            if (i == 4) return "右クリックで構え・左クリックで撃つ";
            if (i == 5) return "PgUp/PgDn・Home/End・Ins/Del＝鞍";
            return "";
        }

        public static string ControlsVR()
        {
            string s = "";
            for (int i = 0; i < CONTROLS_LINES; i++) s += (i > 0 ? "\n" : "") + ControlsLineVR(i);
            return s;
        }

        public static string ControlsPC()
        {
            string s = "";
            for (int i = 0; i < CONTROLS_LINES; i++) s += (i > 0 ? "\n" : "") + ControlsLinePC(i);
            return s;
        }

        /// <summary>操作の看板の 2 列（左 VR・右 PC、TMP の pos タグ）。</summary>
        public static string ControlsTable()
        {
            string s = "";
            for (int i = 0; i < CONTROLS_LINES; i++)
                s += (i > 0 ? "\n" : "") + "<pos=0%>" + ControlsLineVR(i) + "<pos=52%>" + ControlsLinePC(i);
            return s;
        }

        /// <summary>鞍の札のキーの説明（デスクトップだけ、ui-r2-10）。</summary>
        public static string SeatKeysHint()
        {
            return "PgUp/PgDn 高さ  Home/End 前後  Ins/Del 傾き";
        }

        // ---- ステージの合間（ui-r2-08）----

        /// <summary>首位との差："首位まで 4点" / "首位"。</summary>
        public static string GapText(int myPoints, int leaderPoints)
        {
            int gap = leaderPoints - myPoints;
            if (gap <= 0) return "首位";
            return "首位まで " + gap.ToString() + "点";
        }

        /// <summary>首の札の合間の進み具合の行："首位まで 4点  次：砂漠"。最後のステージの後は差だけ。</summary>
        public static string BetweenStagesLine(int myPoints, int leaderPoints, int nextStage, int stageCount)
        {
            string s = GapText(myPoints, leaderPoints);
            if (nextStage < 0 || nextStage >= stageCount) return s;
            return s + "  次：" + StageName(nextStage);
        }

        /// <summary>首の札の合間の見出し："総合"、次が最終ステージなら "総合  次は得点1.5倍"。</summary>
        public static string BetweenStagesHeader(int nextStage, int stageCount)
        {
            if (nextStage == RaceMath.FINAL_STAGE && nextStage < stageCount) return "総合  次は得点1.5倍";
            return "総合";
        }

        // ---- 遺物（ui-r2-12）----

        /// <summary>Lv が上がって何が変わったか（AbilityMath の値から）："半径 6m・6秒" など。</summary>
        public static string LevelEffect(int ability, int level)
        {
            if (ability == AbilityMath.ABILITY_DUST)
                return "砂煙 半径" + Mathf.RoundToInt(AbilityMath.DustRadius(level)).ToString() + "m・" + Mathf.RoundToInt(AbilityMath.DustDuration(level)).ToString() + "秒";
            if (ability == AbilityMath.ABILITY_LASSO)
                return "縄 " + Mathf.RoundToInt(AbilityMath.LassoRange(level)).ToString() + "m・+" + AbilityMath.LassoPullBonus(level).ToString() + "m/s";
            if (ability == AbilityMath.ABILITY_WALL)
                return "岩壁 " + AbilityMath.WallCount(level).ToString() + "枚・幅" + AbilityMath.WallWidth(level).ToString() + "m";
            if (ability == AbilityMath.ABILITY_BALL)
                return "鉄球 " + AbilityMath.BallCount(level).ToString() + "個";
            return "";
        }

        /// <summary>首の札の知らせ："遺物 Lv2 砂煙 半径6m・6秒"。</summary>
        public static string RelicNote(int ability, int level)
        {
            string e = LevelEffect(ability, level);
            return "遺物 Lv" + level.ToString() + (e.Length > 0 ? " " + e : "");
        }

        // ---- 近道（ui-r2-17）----
        public static string ShortcutText()
        {
            return "近道！";
        }

        // ---- 町の大時計（ui-r2-13）----

        /// <summary>出走時計の急ぎ具合：0 = ふつう、1 = 30 秒未満（琥珀）、2 = 10 秒未満（赤）。left &lt; 0 は 0。</summary>
        public static int ClockUrgency(float left)
        {
            if (left < 0f) return 0;
            if (left < 10f) return 2;
            if (left < 30f) return 1;
            return 0;
        }

        /// <summary>町の大時計の文字。lobbyLeft = 出走までの秒数（なければ -1）、activeCount = サインした人数、racing = レース中、stage = 今のステージ。</summary>
        public static string TownClockText(float lobbyLeft, int activeCount, bool racing, int stage)
        {
            if (racing) return "レース中\n<size=60%>" + StageLabel(stage) + "</size>";
            if (lobbyLeft >= 0f)
            {
                int u = ClockUrgency(lobbyLeft);
                string clock = RaceMath.FormatClock(lobbyLeft);
                if (u == 2) clock = "<color=#B81F14>" + clock + "</color>";
                else if (u == 1) clock = "<color=#A8540A>" + clock + "</color>";   // 紙の上の琥珀（明るい琥珀は羊皮紙に溶ける）
                return "<size=60%>出走まで</size>\n" + clock;
            }
            int need = activeCount >= 2 ? 0 : 2 - activeCount;
            if (need <= 0) need = 1;
            return "<size=60%>台帳にサイン</size>\nあと " + need.ToString() + " 人で出走時計が動く";
        }

        // ---- 懐中時計（ui-r2-16）----

        /// <summary>針の角度（度、Z 軸まわり、負 = 時計回り）：ステージの全体 total を 1 周に。経過 0 で 0、残り 0 で -360。</summary>
        public static float DialAngle(float remaining, float total)
        {
            if (total <= 0f) return 0f;
            float f = Mathf.Clamp01(1f - remaining / total);
            return -360f * f;
        }

        /// <summary>
        /// 数字 k（1..12）の見せ方：0 = もう過ぎた（薄く）、1 = これから（インク）、2 = 最後の 2 つ（赤インク）。
        /// 数字 k の角度は k × 30°、針がそこを過ぎたら過ぎた扱い（12 = 終わり）。
        /// </summary>
        public static int NumeralState(int k, float remaining, float total)
        {
            float passed = -DialAngle(remaining, total);
            if (passed >= k * 30f - 0.001f) return 0;
            if (k >= 11) return 2;
            return 1;
        }

        /// <summary>秒針の角度：整数秒ごとに 6° 刻み（Mathf.Floor）。</summary>
        public static float SecondHandAngle(float remaining)
        {
            return -6f * Mathf.Floor(remaining);
        }

        // ---- VR の酔い止め（ui-r2-18）----

        /// <summary>視界の縁の強さの段：0 = なし、1 = 弱、2 = 強。</summary>
        public const int VIGNETTE_LEVELS = 3;

        public static string VignetteLabel(int level)
        {
            if (level == 1) return "視界の縁：弱";
            if (level == 2) return "視界の縁：強";
            return "視界の縁：なし";
        }

        /// <summary>
        /// 視界の縁の暗さ（0..1）。速さ（m/s）と曲がる速さ（度/秒）から：8 m/s 以下と 30°/s 以下は 0、18 m/s・120°/s で最大。
        /// level 1 = ×0.5、2 = ×0.85、0 = 0。
        /// </summary>
        public static float VignetteStrength(float speed, float yawRateDeg, int level)
        {
            if (level <= 0) return 0f;
            float a = Mathf.Clamp01((speed - 8f) / 10f);
            float b = Mathf.Clamp01((Mathf.Abs(yawRateDeg) - 30f) / 90f);
            float m = a > b ? a : b;
            float k = level == 1 ? 0.5f : 0.85f;
            return m * k;
        }

        /// <summary>表の 1 行（TMP の pos タグで列をそろえる）："&lt;pos=0%&gt;1着&lt;pos=15%&gt;名前&lt;pos=70%&gt;+10&lt;pos=85%&gt;計24"。</summary>
        public static string Row4(string a, string b, string c, string d)
        {
            return "<pos=0%>" + a + "<pos=15%>" + b + "<pos=70%>" + c + "<pos=85%>" + d;
        }

        /// <summary>レース中の順位表の行（ui-r2-15）：順位・名前・能力・進み具合（右の列を広く）。</summary>
        public static string RaceRow(string a, string b, string c, string d)
        {
            return "<pos=0%>" + a + "<pos=12%>" + b + "<pos=46%>" + c + "<pos=62%>" + d;
        }

        /// <summary>進み具合の四角だけ（"■■■□□□"）。cpCount ≤ 0 は空。</summary>
        public static string ProgressSquares(int cpReached, int cpCount, int segments)
        {
            if (cpCount <= 0 || segments <= 0) return "";
            int filled = Mathf.Clamp(cpReached * segments / cpCount, 0, segments);
            string s = "";
            for (int i = 0; i < segments; i++) s += i < filled ? "■" : "□";
            return s;
        }

        /// <summary>順位表の 4 列目（ui-r2-15）：ゴールしていれば "ゴール 1:23.4"、まだなら "■■■□□□"（6 つ）。</summary>
        public static string StandingCell(int cpReached, int cpCount, bool finished, float finishSeconds)
        {
            if (finished) return "ゴール " + FormatClockTenths(finishSeconds);
            return ProgressSquares(cpReached, cpCount, STANDING_SEGMENTS);
        }

        public const int STANDING_SEGMENTS = 6;

        /// <summary>2 列の行（順位と名前）。</summary>
        public static string Row2(string a, string b)
        {
            return "<pos=0%>" + a + "<pos=18%>" + b;
        }

        /// <summary>クールダウンの残り："あと 7"（切り上げ）。0 以下は空。</summary>
        public static string CooldownLabel(float remaining)
        {
            if (remaining <= 0f) return "";
            return "あと " + Mathf.CeilToInt(remaining - 0.0001f).ToString();
        }

        /// <summary>手配書の説明（効果とクールダウン、AbilityMath の値から）。</summary>
        public static string AbilityDescription(int ability)
        {
            string cd = " / CD " + Mathf.RoundToInt(AbilityMath.Cooldown(ability)).ToString() + "秒";
            if (ability == AbilityMath.ABILITY_DUST)
                return "砂塵：後ろに砂煙、追う馬を減速（×" + Pct(AbilityMath.DUST_SLOW_FACTOR) + "）" + cd;
            if (ability == AbilityMath.ABILITY_LASSO)
                return "投げ縄：前の馬に掛けて引き寄せ、自分は +" + Mathf.RoundToInt(AbilityMath.LASSO_SELF_BONUS_MPS).ToString() + "m/s" + cd;
            if (ability == AbilityMath.ABILITY_WALL)
                return "岩壁：" + Mathf.RoundToInt(AbilityMath.WALL_AHEAD).ToString() + "m 先に幅 " + Mathf.RoundToInt(AbilityMath.WALL_WIDTH).ToString() + "m の岩の壁（"
                    + Mathf.RoundToInt(AbilityMath.WALL_SECONDS).ToString() + "秒）" + cd;
            if (ability == AbilityMath.ABILITY_BALL)
                return "鉄球：転がる鉄球 " + AbilityMath.BALL_BOUNCES.ToString() + " 回はねる、当たると減速" + cd;
            return "？";
        }

        /// <summary>乗り物の説明。</summary>
        public static string MountDescription(int mount)
        {
            if (mount == MountMath.MOUNT_CAMEL) return "ラクダ：最高速は低いが砂地で落ちない";
            return "馬：最高速が高い。砂地では遅くなる";
        }

        static string Pct(float f)
        {
            return (Mathf.RoundToInt(f * 100f) / 100f).ToString();
        }
    }
}
