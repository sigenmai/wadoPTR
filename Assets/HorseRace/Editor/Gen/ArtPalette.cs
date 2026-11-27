using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 共通の色の決まり（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。GenUtil.Toonify・TerrainBuilder・Validator が使う。
    ///   ステージの主色（docs/research/reference-games.md のまとめの表）：荒野 = 赤錆、砂漠 = 砂金、境目 = 紫の夕景。
    ///   素材ごとの決まり：
    ///     ・大きな岩（Cliffs・DesertRocks_01〜03・MesaRock・MesaRockB）は平らにしすぎない（_Flatten 0.25・ぼかし 1.5）。
    ///       細部が消えて「のっぺりした板」に見えるのを防ぎ、テクスチャ付きのメサと同じ見え方にそろえる。Cliffs は赤錆に寄せる。
    ///     ・RockGray（青みがかった灰色）は暖色に寄せる。
    ///     ・町（WesternTown）の灰色・青の建材は古い木の色へ、緑の壁はセージ色へ寄せる（色だけで塗られた素材）。
    /// </summary>
    public static class ArtPalette
    {
        public const float DEFAULT_FLATTEN = 0.7f, DEFAULT_FLATTEN_MIP = 4f;
        public const float ROCK_FLATTEN = 0.25f, ROCK_FLATTEN_MIP = 1.5f;
        /// <summary>崖の板（Cliffs）はさらに平らにしない（art-r3-05：面の切り返しを見せる）。</summary>
        public const float CLIFF_FLATTEN = 0.1f;
        /// <summary>宝石（遺物）の明るさ（トゥーンの _Brightness = 1 + GEM_GLOW、art-r3-04：拾える物は一番鮮やかに）。</summary>
        public const float GEM_GLOW = 0.6f;

        public static readonly Color CliffTint = new Color(1.0f, 0.82f, 0.72f);
        public static readonly Color RockGrayTint = new Color(1.0f, 0.80f, 0.66f);
        // 静的な初期化は書いた順：RockGrayTint の後に置く
        /// <summary>岩のセット（RockSetStylized）の色：RockGray と同じ暖色に、土の色を少し掛ける（×0.9、art-r3-13）。</summary>
        public static readonly Color RockSetTint = new Color(RockGrayTint.r * 0.93f, RockGrayTint.g * 0.88f, RockGrayTint.b * 0.84f);
        public static readonly Color WeatheredWood = new Color(0.78f, 0.66f, 0.52f);
        public static readonly Color Sage = new Color(0.62f, 0.66f, 0.52f);

        // ---- 地形の色（TerrainBuilder の TerrainLayer）----
        public static readonly Color DirtCanyon = new Color(0.62f, 0.36f, 0.22f);    // 赤錆
        public static readonly Color DirtDunes = new Color(0.80f, 0.63f, 0.42f);     // art-r4-05：砂に近い明るい土（道の脇に見えても暗い帯にならない。前の 0.62/0.44/0.27 はアスファルトの帯に見えた）
        public static readonly Color DirtBorder = new Color(0.62f, 0.40f, 0.40f);    // 紫の夕景（art-r3-07：明るく）
        /// <summary>外の地面（art-r3-10）：3 ステージの土の平均に近い色（クリーム色の砂の板にしない）。</summary>
        public static readonly Color DirtOuter = new Color(0.64f, 0.43f, 0.31f);
        public static readonly Color RockLayer = new Color(0.55f, 0.45f, 0.38f);
        /// <summary>砂漠の砂（Sand(1)）。前は 3 ステージ共通の砂の色だった（art-r4-06：荒野・境目では黄色いしみに見えた）。</summary>
        public static readonly Color SandLayer = new Color(0.90f, 0.76f, 0.52f);
        /// <summary>art-r4-06：ステージの主色に合わせた砂（荒野 = 赤錆の砂、砂漠 = 砂金、境目 = 埃っぽいばら色）。</summary>
        public static readonly Color SandCanyon = new Color(0.80f, 0.54f, 0.38f);
        public static readonly Color SandBorder = new Color(0.76f, 0.58f, 0.54f);
        /// <summary>ステージ（0 荒野 / 1 砂漠 / 2 境目、それ以外 = ロビー・外の地面 = 荒野の砂）の砂の色。</summary>
        public static Color Sand(int stage) => stage == 1 ? SandLayer : (stage == 2 ? SandBorder : SandCanyon);
        /// <summary>地層の縞（峡谷の急斜面）：明るい帯と暗い帯。</summary>
        public static readonly Color StrataLight = new Color(0.74f, 0.46f, 0.30f);
        public static readonly Color StrataDark = new Color(0.52f, 0.30f, 0.20f);
        /// <summary>art-r4-04：境目の切り通しの壁の縞は明暗をはっきり（段の付いた岩の重なりに見える）。</summary>
        public static readonly Color StrataLightBorder = new Color(0.80f, 0.50f, 0.34f);
        public static readonly Color StrataDarkBorder = new Color(0.44f, 0.24f, 0.18f);
        public static Color StrataLightFor(int stage) => stage == 2 ? StrataLightBorder : StrataLight;
        public static Color StrataDarkFor(int stage) => stage == 2 ? StrataDarkBorder : StrataDark;

        // ---- 地面の粒（art-r4-02）：ToonTerrain のワールド座標の値ノイズで、道の外の色を少しだけ揺らす ----
        public const float GRAIN_SCALE_M = 3.5f, GRAIN_AMP = 0.07f, MOTTLE_SCALE_M = 45f, MOTTLE_AMP = 0.05f;
        public const float SCUFF_SCALE_M = 1.2f, SCUFF_AMP = 0.06f;

        /// <summary>ステージ（0 荒野 / 1 砂漠 / 2 境目、それ以外 = ロビー・外の地面）の土の色。</summary>
        public static Color Dirt(int stage) => stage == 1 ? DirtDunes : (stage == 2 ? DirtBorder : DirtCanyon);

        // ---- 道の踏み固めた土（art-r2-02）：ステージの土・砂と明るさで 0.15 / 0.10 以上離す（道が読める）----
        public static readonly Color TrailCanyon = new Color(0.82f, 0.63f, 0.46f);   // 明るい踏み固めた粘土（赤錆の土 0.405 → 0.66）
        public static readonly Color TrailDunes = new Color(0.84f, 0.70f, 0.50f);    // art-r4-05：砂 × 約 0.92 の明るさの踏み固めた砂（色の帯ではなく轍と路肩の小物で読む）
        /// <summary>轍の色 = 道の色 × RUT_SHADE（art-r3-06 → art-r4-02：0.85 → 0.75、轍で道が読める）。</summary>
        public const float RUT_SHADE = 0.75f;
        public static readonly Color TrailBorder = new Color(0.78f, 0.63f, 0.56f);   // 夕景の明るい道（紫の土 0.39 → 0.66、砂 0.77 とも 0.11 離す）

        /// <summary>ステージの道の色。</summary>
        public static Color Trail(int stage) => stage == 1 ? TrailDunes : (stage == 2 ? TrailBorder : TrailCanyon);

        // ---- 町の地面の板・岩山（art-r2-11）----
        /// <summary>町の模型の地面の板（WesternTown_001_Ground）：まわりの砂の地面に合わせた暗めの砂色（明るすぎるクリーム色にしない）。</summary>
        public static readonly Color TownGroundTint = new Color(0.80f, 0.60f, 0.44f);   // art-r3-09：踏み固めた土（TrailCanyon 寄り）で砂の土手へなじむ
        /// <summary>Buildings/*.fbx（画像付き）の青白い壁を古い木の色へ寄せる掛け色（art-r2-14）。</summary>
        public static readonly Color BuildingTextureTint = new Color(0.85f, 0.68f, 0.50f);   // art-r4-16：一番明るい成分を 0.85 まで（夕景の紫の地面の上で白く飛ばない）。art-r3-15 の (0.98,0.78,0.58) と同じ色合い
        /// <summary>画像付きの建物の掛け色の一番明るい成分の上限（art-r4-16）。</summary>
        public const float BUILDING_TEX_MAX = 0.85f;

        /// <summary>素材ごとのトゥーンの設定。</summary>
        public struct Style
        {
            public float flatten, flattenMip;
            public Color tint;          // 白 = 変えない（画像の色・元の色に掛ける）
            public float glow;          // 0 = ふつう。> 0 ならトゥーンの _Brightness = 1 + glow（遺物の宝石、art-r3-04）
            public string key;          // マテリアルのキャッシュのキーに足す（同じ元マテリアルでも設定ごとに別のマテリアル）
        }

        static bool Has(string s, string part) => s != null && s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>大きな岩（細部を残す）か。</summary>
        public static bool IsBigRock(string assetPath, string partName)
        {
            if (Has(assetPath, "Cliffs") || Has(assetPath, "MesaRock")) return true;
            if (Has(assetPath, "DesertRocks") && partName != null)
                return partName == "DesertRocks_01" || partName == "DesertRocks_02" || partName == "DesertRocks_03";
            return false;
        }

        /// <summary>岩の素材（色相の確認の対象）か。</summary>
        public static bool IsRockAsset(string assetPath)
            => Has(assetPath, "Rock") || Has(assetPath, "Cliffs") || Has(assetPath, "Mesa");

        public static Style StyleFor(string assetPath, string partName) => StyleFor(assetPath, partName, null);

        /// <summary>
        /// 素材の部品の種類（parts.json の kind：Ground / Hill / Rock / Building / Prop …）。名前の末尾 "_Ground" などから。無ければ null。
        /// split_parts.py は部品名を「パック_番号_種類」にするので、parts.json を読まなくても名前で分かる。
        /// </summary>
        public static string PartKind(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return null;
            int i = partName.LastIndexOf('_');
            if (i < 0 || i + 1 >= partName.Length) return null;
            string k = partName.Substring(i + 1);
            return char.IsLetter(k[0]) ? k : null;
        }

        /// <summary>素材ごとのトゥーンの設定。partKind（parts.json の kind）を渡すと、町の地面の板・岩山にも色の決まりを当てる。</summary>
        public static Style StyleFor(string assetPath, string partName, string partKind)
        {
            var s = new Style { flatten = DEFAULT_FLATTEN, flattenMip = DEFAULT_FLATTEN_MIP, tint = Color.white, key = "" };
            if (IsBigRock(assetPath, partName)) { s.flatten = ROCK_FLATTEN; s.flattenMip = ROCK_FLATTEN_MIP; s.key = "_Detail"; }
            if (Has(assetPath, "Cliffs")) { s.tint = CliffTint; s.flatten = CLIFF_FLATTEN; s.key += "_Rust"; }
            if (Has(assetPath, "RockGray")) { s.tint = RockGrayTint; s.key += "_Warm"; }
            // art-r3-13：岩のセット（灰白の岩）も暖色へ（遠くで白い「きのこ」の岩に見えた）
            if (Has(assetPath, "RockSetStylized")) { s.tint = RockSetTint; s.flatten = ROCK_FLATTEN; s.flattenMip = ROCK_FLATTEN_MIP; s.key += "_WarmSet"; }
            if (Has(assetPath, "Gems")) { s.glow = GEM_GLOW; s.key += "_Glow"; }
            if (Has(assetPath, "WesternTown"))
            {
                string kind = partKind ?? PartKind(partName);
                // art-r2-11：地面の板は砂の地面の色、岩山・岩は RockGray と同じ暖色（冷たい灰白の塊にしない）
                if (kind == "Ground") { s.tint = TownGroundTint; s.key += "_Sand"; }
                else if (kind == "Hill" || kind == "Rock")
                {
                    s.tint = RockGrayTint; s.flatten = ROCK_FLATTEN; s.flattenMip = ROCK_FLATTEN_MIP; s.key += "_Warm";
                }
            }
            return s;
        }

        /// <summary>Buildings/*.fbx・RanchCompound の部品（画像付きの建物）か。</summary>
        public static bool IsBuildingModel(string assetPath) => Has(assetPath, "Buildings/") || Has(assetPath, "RanchCompound");

        /// <summary>画像付きの素材に掛ける色（art-r2-14：建物の青白い壁 → 古い木）。それ以外は白。</summary>
        public static Color TextureTint(string assetPath) => IsBuildingModel(assetPath) ? BuildingTextureTint : Color.white;

        /// <summary>色だけで塗られた素材の色を共通の色に寄せる（町・建物の灰色・青 → 古い木、緑 → セージ）。それ以外はそのまま。</summary>
        public static Color RemapBaseColor(string assetPath, Color c)
        {
            if (!Has(assetPath, "WesternTown") && !IsBuildingModel(assetPath)) return c;
            float h, s, v;
            ToHSV(c, out h, out s, out v);
            if (v < 0.12f) return c;                                   // 黒に近い（窓・影）はそのまま
            if (v > 0.85f && s < 0.25f) return Lerp(c, WeatheredWood, 0.6f);          // art-r4-16：白に近いクリーム色の壁（夕景で白く飛ぶ）
            if (s < 0.12f && v > 0.25f) return Lerp(c, WeatheredWood, 0.65f);          // 灰色
            if (h >= 180f && h <= 260f && s >= 0.08f) return Lerp(c, WeatheredWood, 0.7f);   // 青
            if (h >= 75f && h < 170f && s >= 0.15f) return Lerp(c, Sage, 0.7f);        // 緑
            return c;
        }

        static Color Lerp(Color a, Color b, float t)
            => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1f);

        /// <summary>RGB → 色相（0〜360°）・彩度・明るさ（0〜1）。</summary>
        public static void ToHSV(Color c, out float h, out float s, out float v)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float d = mx - mn;
            v = mx;
            s = mx <= 1e-5f ? 0f : d / mx;
            if (d <= 1e-5f) { h = 0f; return; }
            if (mx == c.r) h = 60f * (((c.g - c.b) / d) % 6f);
            else if (mx == c.g) h = 60f * ((c.b - c.r) / d + 2f);
            else h = 60f * ((c.r - c.g) / d + 4f);
            if (h < 0f) h += 360f;
        }

        /// <summary>岩の色相として暖色（0〜60°）か。彩度がほぼ無い色（灰色）は暖色ではない（青みがかった灰色を見つけるため）。</summary>
        public static bool IsWarmRock(Color c)
        {
            float h, s, v;
            ToHSV(c, out h, out s, out v);
            if (s < 0.06f) return false;
            return h <= 60f || h >= 350f;
        }

        public static Color Multiply(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);
    }
}
