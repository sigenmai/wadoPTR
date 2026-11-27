using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>文字の見た目（素材ごと）。Plain = フォントのまま。</summary>
    public enum TextStyle { Plain, Ink, Chalk, Sign, Brass }

    /// <summary>
    /// 表示の決まり（純粋、単体テストあり）：文字の見た目 4 種の値（TMP の SDF マテリアルのプリセット）・文字の色・あふれ対策の下限・
    /// 紙の札の大きさ。Sea of Thieves 流に「同じ素材には同じ書体」：
    ///   Sign  = 空のカウント・ゴールの幕・近道の看板・表彰台の名札（紙の面、濃い茶の縁取り 0.22 と右下の影）
    ///   Chalk = 黒板（RankBoard）：白っぽいチョーク、少し太くやわらかく
    ///   Ink   = 台帳・電報・手配書の説明・鞍の札：焦げ茶のインク、太め
    ///   Brass = 首の札の数字・鞍の札の見出し：真鍮の金、濃い縁取り 0.18
    /// </summary>
    public static class UiStyle
    {
        /// <summary>自動の縮小の下限（上限 × 0.45）。</summary>
        public const float AUTO_MIN = 0.45f;

        public struct Preset
        {
            public float dilate;
            public float softness;
            public float outlineWidth;
            public Color outlineColor;
            public bool underlay;
            public Vector2 underlayOffset;
            public float underlaySoftness;
        }

        public static readonly Color ChalkFace = new Color(0.93f, 0.93f, 0.88f);
        public static readonly Color InkFace = new Color(0.23f, 0.15f, 0.08f);
        public static readonly Color SignFace = new Color(0.96f, 0.90f, 0.76f);
        public static readonly Color BrassFace = new Color(1f, 0.82f, 0.45f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.27f);
        public static readonly Color Amber = new Color(1f, 0.6f, 0.2f);
        public static readonly Color RedInk = new Color(0.72f, 0.12f, 0.08f);
        public static readonly Color Teal = new Color(0.2f, 0.75f, 0.7f);
        public static readonly Color DarkBrown = new Color(0.16f, 0.10f, 0.05f);

        public static Color FaceColor(TextStyle s)
        {
            switch (s)
            {
                case TextStyle.Chalk: return ChalkFace;
                case TextStyle.Ink: return InkFace;
                case TextStyle.Sign: return SignFace;
                case TextStyle.Brass: return BrassFace;
                default: return Color.white;
            }
        }

        public static Preset PresetFor(TextStyle s)
        {
            var p = new Preset { outlineColor = DarkBrown, underlayOffset = new Vector2(0f, 0f) };
            switch (s)
            {
                case TextStyle.Chalk:
                    p.dilate = 0.05f; p.softness = 0.12f;
                    break;
                case TextStyle.Ink:
                    p.dilate = 0.12f;
                    break;
                case TextStyle.Sign:
                    p.outlineWidth = 0.22f; p.underlay = true; p.underlayOffset = new Vector2(0.6f, -0.6f); p.underlaySoftness = 0.3f;
                    break;
                case TextStyle.Brass:
                    p.outlineWidth = 0.18f;
                    break;
            }
            return p;
        }

        // ---- 首の札（MinimalHud）の大きさ ----
        public const float NECK_CARD_W = 0.20f, NECK_CARD_H = 0.13f;
        public const float PIP_SIZE_M = 0.025f;
        /// <summary>能力の札の首の札からの横のずれ（m）。</summary>
        public const float POUCH_OFFSET_X = 0.09f;

        /// <summary>懐中時計の数字 k（1..12）の位置（文字盤のローカル、半径 r、12 が上）。</summary>
        public static Vector2 DialPoint(int k, float r)
        {
            float a = k * 30f * Mathf.PI / 180f;
            return new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
        }
    }
}
