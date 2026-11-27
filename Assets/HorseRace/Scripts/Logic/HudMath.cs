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
            return place.ToString() + "着  +" + points.ToString() + "点\n" + FormatClockTenths(seconds);
        }

        /// <summary>表の 1 行（TMP の pos タグで列をそろえる）："&lt;pos=0%&gt;1着&lt;pos=15%&gt;名前&lt;pos=70%&gt;+10&lt;pos=85%&gt;計24"。</summary>
        public static string Row4(string a, string b, string c, string d)
        {
            return "<pos=0%>" + a + "<pos=15%>" + b + "<pos=70%>" + c + "<pos=85%>" + d;
        }

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
