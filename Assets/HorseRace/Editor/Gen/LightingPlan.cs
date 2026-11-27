using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 光と空気の数値（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。SceneAssembler（太陽・環境光・霧）、
    /// GenUtil（トゥーンの影色）、TerrainBuilder（地形のトゥーン）、Validator（暗すぎないかの確認）が同じ値を使う。
    ///   方針（docs/research/reference-games.md の Weird West / Far Far West）：影は「暗くする」のではなく「色を変える」。
    ///   2 段のトゥーンの影の段でも、峡谷の底の地面は輝度 MIN_SHADOW_LUMINANCE 以上（真っ黒な溝にしない）。
    /// トゥーンの影の段の色 = 素材の色 × 影色 × (環境光 + 太陽の色 × 強さ × SHADOW_LIGHT_FLOOR)（HorseToon.shader と同じ式）。
    /// </summary>
    public static class LightingPlan
    {
        // ---- 太陽：高め（62°）で東西に走る峡谷の行を照らし、壁の影を短く ----
        public static readonly Vector3 SUN_EULER = new Vector3(62f, -20f, 0f);
        public const float SUN_INTENSITY = 1.25f;
        public static readonly Color SunColor = new Color(1f, 0.93f, 0.8f);

        // ---- 環境光（Trilight）----
        public static readonly Color AmbientSky = new Color(0.78f, 0.70f, 0.62f);
        public static readonly Color AmbientEquator = new Color(0.80f, 0.62f, 0.48f);
        public static readonly Color AmbientGround = new Color(0.60f, 0.44f, 0.34f);

        // ---- トゥーンの影 ----
        /// <summary>影色（全ステージ共通）。暗い紫寄りの茶ではなく、明るい赤茶の影。</summary>
        public static readonly Color ShadowColor = new Color(0.70f, 0.52f, 0.54f);
        /// <summary>境目（ステージ 3）：紫の夕景の影（art-r2-10：影の段の地面が輝度 0.25 以上になるよう、前の (0.50,0.40,0.58) より明るく）。</summary>
        public static readonly Color ShadowColorDusk = new Color(0.62f, 0.52f, 0.70f);
        /// <summary>影の段にも足す太陽の割合（HorseToon.shader の lightCol * 0.5）。</summary>
        public const float SHADOW_LIGHT_FLOOR = 0.5f;
        /// <summary>峡谷の底の地面（影の段）の最低の輝度（Validator が確かめる）。</summary>
        public const float MIN_SHADOW_LUMINANCE = 0.25f;

        // ---- 霧（砂ぼこり）----
        public const float FOG_START_M = 350f, FOG_END_M = 1100f;
        public static readonly Color FogColor = new Color(0.86f, 0.72f, 0.56f);
        /// <summary>霧がこれ以上かかる距離の物は「見えない」とみなす（ロビーからステージが見えないかのテスト）。</summary>
        public const float FOG_HIDDEN_FRACTION = 0.85f;
        public static float FogHiddenDistance => FOG_START_M + (FOG_END_M - FOG_START_M) * FOG_HIDDEN_FRACTION;

        /// <summary>線形の霧の濃さ（0 = なし、1 = 霧の色だけ）。</summary>
        public static float FogFraction(float distM) => Mathf.Clamp01((distM - FOG_START_M) / (FOG_END_M - FOG_START_M));

        /// <summary>Rec.709 の輝度。</summary>
        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>トゥーンの影の段の色（HorseToon.shader / ToonTerrain.shader の shadowed の式）。</summary>
        public static Color ShadowedColor(Color albedo, Color shadow, Color ambient)
        {
            float k = SUN_INTENSITY * SHADOW_LIGHT_FLOOR;
            return new Color(albedo.r * shadow.r * (ambient.r + SunColor.r * k),
                albedo.g * shadow.g * (ambient.g + SunColor.g * k),
                albedo.b * shadow.b * (ambient.b + SunColor.b * k));
        }

        // ------------------------------------------------------------------
        // ステージごとの光（art-r2-10）：荒野 = 赤錆の昼、砂漠 = 砂金の午後、境目 = 紫の夕景
        // ------------------------------------------------------------------

        /// <summary>1 つのステージ（またはロビー）の太陽・霧・環境光・影の色。StageAtmosphere（Udon）が切り替える。</summary>
        public struct StageLight
        {
            public Vector3 sunEuler;
            public Color sunColor;
            public float sunIntensity;
            public Color fogColor;
            public float fogStart, fogEnd;
            public Color ambientSky, ambientEquator, ambientGround;
            public Color shadow;
        }

        public const int LOBBY_LIGHT = 3;

        /// <summary>ステージ i（0 荒野 / 1 砂漠 / 2 境目、それ以外 = ロビー＝荒野と同じ）の光。</summary>
        public static StageLight ForStage(int i)
        {
            var l = new StageLight
            {
                sunEuler = SUN_EULER, sunColor = SunColor, sunIntensity = SUN_INTENSITY,
                fogColor = FogColor, fogStart = FOG_START_M, fogEnd = FOG_END_M,
                ambientSky = AmbientSky, ambientEquator = AmbientEquator, ambientGround = AmbientGround,
                shadow = ShadowColor,
            };
            if (i == 1)
            {
                // 砂漠：少し低い午後の太陽（48°）、金色の霧、影は砂色に近い暖かい茶
                l.sunEuler = new Vector3(48f, -35f, 0f);
                l.sunColor = new Color(1f, 0.90f, 0.70f);
                l.sunIntensity = 1.3f;
                l.fogColor = new Color(0.92f, 0.80f, 0.60f);
                l.ambientSky = new Color(0.84f, 0.76f, 0.60f);
                l.ambientEquator = new Color(0.86f, 0.70f, 0.50f);
                l.ambientGround = new Color(0.66f, 0.50f, 0.36f);
                l.shadow = new Color(0.74f, 0.58f, 0.52f);
            }
            else if (i == 2)
            {
                // 境目：地平線近くの夕日（16°）、橙の光、紫がかった霧と影
                l.sunEuler = new Vector3(16f, -60f, 0f);
                l.sunColor = new Color(1f, 0.70f, 0.50f);
                l.sunIntensity = 1.35f;
                l.fogColor = new Color(0.70f, 0.56f, 0.64f);
                l.ambientSky = new Color(0.66f, 0.56f, 0.74f);
                l.ambientEquator = new Color(0.82f, 0.60f, 0.58f);
                l.ambientGround = new Color(0.74f, 0.58f, 0.68f);
                l.shadow = ShadowColorDusk;
            }
            return l;
        }

        /// <summary>影の段の色（ステージの太陽の色・強さで。ShadowedColor のステージ版）。</summary>
        public static Color ShadowedColor(Color albedo, StageLight l, Color ambient)
        {
            float k = l.sunIntensity * SHADOW_LIGHT_FLOOR;
            return new Color(albedo.r * l.shadow.r * (ambient.r + l.sunColor.r * k),
                albedo.g * l.shadow.g * (ambient.g + l.sunColor.g * k),
                albedo.b * l.shadow.b * (ambient.b + l.sunColor.b * k));
        }

        /// <summary>そのステージの地面（土と砂）の影の段の輝度のうち暗いほう（環境光は一番暗い地面側）。</summary>
        public static float MinFloorShadowLuminance(int stage, Color dirt, Color sand)
        {
            var l = ForStage(stage);
            return Mathf.Min(Luminance(ShadowedColor(dirt, l, l.ambientGround)), Luminance(ShadowedColor(sand, l, l.ambientGround)));
        }

        /// <summary>
        /// 峡谷の底の地面（赤茶の土・砂）の影の段の輝度のうち一番暗いもの。環境光は一番暗い地面側（AmbientGround）で数える（厳しめ）。
        /// </summary>
        public static float MinCanyonFloorShadowLuminance(Color dirt, Color sand)
        {
            float a = Luminance(ShadowedColor(dirt, ShadowColor, AmbientGround));
            float b = Luminance(ShadowedColor(sand, ShadowColor, AmbientGround));
            return Mathf.Min(a, b);
        }
    }
}
