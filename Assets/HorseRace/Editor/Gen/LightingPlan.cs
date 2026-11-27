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
            // art-r4-10：空（Skybox/Procedural）。空の色は霧と分ける（青い天頂の下に暖かい霞）
            public Color skyTint;
            public float atmosphere, sunSize, exposure;
        }

        /// <summary>art-r5-14：昼の空（荒野・砂漠・ロビー）と夕景の空（境目）の天頂の色。</summary>
        public static readonly Color SkyClear = new Color(0.36f, 0.56f, 0.86f), SkyDusk = new Color(0.48f, 0.30f, 0.52f);
        /// <summary>art-r5-14：空の地平線の色（下見の 2 色のグラデーションの下の色・霧の色に近い）= 霧の色。</summary>
        public static Color SkyHorizon(int i) => ForStage(i).fogColor;

        /// <summary>天頂の色（空の色）と霧の明るさの差の最低（art-r4-10：空・霧・遠くの地面が同じ明るさの「霧の帯」にしない）。</summary>
        public const float SKY_FOG_CONTRAST_MIN = 0.15f;
        /// <summary>空の地面側の色 = 霧の色 × これ。</summary>
        public const float SKY_GROUND_FROM_FOG = 0.9f;
        /// <summary>雲の層（art-r4-10）：ステージの中心から CLOUD_OUT_M 先の輪に、高さ CLOUD_ALT_MIN〜MAX、大きさ CLOUD_SIZE_MIN〜MAX の柔らかい板 CLOUD_COUNT 枚。</summary>
        public const int CLOUD_COUNT = 14;
        public const float CLOUD_OUT_M = 1000f, CLOUD_ALT_MIN = 600f, CLOUD_ALT_MAX = 800f, CLOUD_SIZE_MIN = 300f, CLOUD_SIZE_MAX = 600f;

        /// <summary>ステージ i の空の色（tools/preview も同じ値）。</summary>
        public static Color SkyTint(int i) => ForStage(i).skyTint;

        /// <summary>
        /// 境目の最後の直線の向きの太陽の yaw（art-r4-10）：ゴールの 300m 手前〜ゴールの進む向きに夕日が正面に来るよう、
        /// 光の向き（Directional Light の前）を進む向きの反対にする。
        /// </summary>
        public static float DuskSunYaw()
        {
            var d = StageDefinitions.All[2];
            var local = StageDefinitions.SampleLocal(d);
            float goal = StageDefinitions.GoalArc(d, local);
            Vector3 a = WorldLayout.PointAtArc(local, goal - 300f), b = WorldLayout.PointAtArc(local, goal);
            Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z);
            if (t.sqrMagnitude < 1e-4f) return -60f;
            return WorldLayout.YawDeg(t.normalized) + 180f;
        }

        static float duskYaw = float.NaN;

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
                // art-r5-14：荒野・砂漠は彩度の高い青（0.36,0.56,0.86）・atmosphere 0.6（灰色がかった青の平らな空にしない。地面の赤錆・砂金と補色）
                skyTint = SkyClear, atmosphere = 0.6f, sunSize = 0.05f, exposure = 1.25f,
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
                l.skyTint = SkyClear; l.atmosphere = 0.6f;   // art-r5-14
            }
            else if (i == 2)
            {
                // 境目：地平線近くの夕日（16°）、橙の光、紫がかった霧と影
                if (float.IsNaN(duskYaw)) duskYaw = DuskSunYaw();
                l.sunEuler = new Vector3(16f, duskYaw, 0f);   // art-r4-10：夕日は最後の 300m の直線の正面
                l.sunColor = new Color(1f, 0.70f, 0.50f);
                l.sunIntensity = 1.35f;
                l.fogColor = new Color(0.86f, 0.60f, 0.52f);   // art-r5-14：地平線の暖かい帯（前の 0.70/0.56/0.64 は紫の壁）
                l.ambientSky = new Color(0.66f, 0.56f, 0.74f);
                l.ambientEquator = new Color(0.82f, 0.60f, 0.58f);
                l.ambientGround = new Color(0.74f, 0.58f, 0.68f);
                l.shadow = ShadowColorDusk;
                // art-r5-14：紫の天頂（0.48,0.30,0.52）・atmosphere 1.6・太陽 0.12 で、Skybox/Procedural が紫の天頂の下に橙の地平線の帯を描く
                l.skyTint = SkyDusk; l.atmosphere = 1.6f; l.sunSize = 0.12f;
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
