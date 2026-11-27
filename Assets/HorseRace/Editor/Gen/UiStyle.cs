using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>文字の見た目（素材ごと）。Plain = フォントのまま。</summary>
    public enum TextStyle { Plain, Ink, Chalk, Sign, Brass }

    /// <summary>
    /// 表示の決まり（純粋、単体テストあり）：文字の見た目 4 種の値（TMP の SDF マテリアルのプリセット）・文字の色・あふれ対策の下限・
    /// 紙の札の大きさ。Sea of Thieves 流に「同じ素材には同じ書体」：
    ///   Sign  = 空のカウント・ゴールの幕・近道の看板（空・暗い板の前、濃い茶の縁取り 0.22 と右下の影）
    ///   Chalk = 黒板（RankBoard）：白っぽいチョーク、少し太くやわらかく
    ///   Ink   = 台帳・電報・手配書の説明・鞍の札・首の札・表彰台の名札・町の大時計：焦げ茶のインク、太め（紙の上はいつもこれ：ui-r2-05/09）
    ///   Brass = 真鍮の面の上だけ（能力の札のクールダウン・鞍のボタン）：真鍮の金、濃い縁取り 0.18
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

        // ---- 読める大きさ（ui-r2-05 / r2-10）----

        /// <summary>TMP の 1pt の文字の高さ（m、ワールドの TextMeshPro・拡大 1）。</summary>
        public const float TMP_M_PER_PT = 0.1f;
        /// <summary>VR で読める最小の文字の角度（度、Meta の目安 1.3〜1.5°）。</summary>
        public const float MIN_VR_DEG = 1.3f;
        /// <summary>首の札の文字までの距離（m、鞍の上の目から首の札）。</summary>
        public const float NECK_CARD_EYE_M = 0.78f;
        /// <summary>鞍の札（座席の調整）までの距離（m）。</summary>
        public const float SEAT_CARD_EYE_M = 0.6f;

        /// <summary>距離 distanceM で MIN_VR_DEG の高さになる文字の大きさ（pt）。</summary>
        public static float MinVrPt(float distanceM)
        {
            return 2f * distanceM * Mathf.Tan(MIN_VR_DEG * 0.5f * Mathf.Deg2Rad) / TMP_M_PER_PT;
        }

        /// <summary>自動の縮小の下限：max × AUTO_MIN と、その距離で読める最小のうち大きいほう（上限は超えない）。</summary>
        public static float MinPtFor(float maxPt, float distanceM)
        {
            float m = Mathf.Max(maxPt * AUTO_MIN, MinVrPt(distanceM));
            return Mathf.Min(m, maxPt);
        }

        /// <summary>首の札の文字（名前・上限 pt）。LobbyBuilder.BuildNeckCard が使い、テストで下限を確かめる。</summary>
        public static readonly string[] NeckTextNames = { "Header", "RankText", "TimeText", "ProgressText" };
        public static readonly float[] NeckTextMaxPt = { 0.25f, 0.9f, 0.45f, 0.22f };

        /// <summary>鞍の札の文字（ui-r2-10：0.32×0.24 → 0.40×0.30m、文字は ×1.4）。</summary>
        public const float SEAT_CARD_W = 0.40f, SEAT_CARD_H = 0.30f;
        public const float SEAT_FONT_SCALE = 1.4f;
        public const float SEAT_KEYS_PT = 0.16f;

        // ---- デスクトップの首の札（ui-r2-03）----
        public const float DESKTOP_HUD_SCALE = 0.55f;
        public static readonly Vector3 DesktopHudOffset = new Vector3(-0.40f, -0.19f, 0.55f);
        /// <summary>首の札の下の持久力の印の下端（札の中心から、拡大 1、m）。</summary>
        public const float NECK_PIPS_BOTTOM = -(NECK_CARD_H * 0.5f + 0.02f + 0.0125f + PIP_SIZE_M * 0.5f);

        // ---- デスクトップの懐中時計（ui-r2-02）----
        public const float DESKTOP_WATCH_SCALE = 0.6f;
        public static readonly Vector3 DesktopWatchOffset = new Vector3(0.30f, -0.17f, 0.5f);
        public static readonly Vector3 DesktopWatchEuler = new Vector3(22f, 0f, 0f);
        public const float WATCH_DIAL_M = 0.13f;

        /// <summary>頭のローカルの点 p（+Z が前）が縦の視野 vFovDeg・横縦比 aspect の画面の中か（margin = 端からの余白の割合）。</summary>
        public static bool InFrustum(Vector3 p, float vFovDeg, float aspect, float margin)
        {
            if (p.z <= 0.01f) return false;
            float halfH = p.z * Mathf.Tan(vFovDeg * 0.5f * Mathf.Deg2Rad) * (1f - margin);
            float halfW = halfH * aspect;
            return Mathf.Abs(p.x) <= halfW && Mathf.Abs(p.y) <= halfH;
        }

        // ---- 文字と紙の明るさの比（ui-r2-09）----

        /// <summary>WCAG の相対輝度（sRGB の色から）。</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        static float Lin(float v)
        {
            return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>明るさの比（1..21、順序は問わない）。紙の上の文字は 4.5 以上。</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            float hi = Mathf.Max(la, lb), lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        /// <summary>手配書の紙の色（GenUtil.PaperSheet の塗り）。</summary>
        public static readonly Color PaperColor = new Color(0.93f, 0.86f, 0.70f);
        /// <summary>紙の上で使う文字の色（表彰台の名札・段の番号・首の札・台帳）。テストで紙との比 ≥ 4.5 を確かめる。</summary>
        public static readonly Color[] PaperInks = { InkFace, RedInk, DarkBrown };

        /// <summary>懐中時計の数字 k（1..12）の位置（文字盤のローカル、半径 r、12 が上）。</summary>
        public static Vector2 DialPoint(int k, float r)
        {
            float a = k * 30f * Mathf.PI / 180f;
            return new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
        }
    }
}
