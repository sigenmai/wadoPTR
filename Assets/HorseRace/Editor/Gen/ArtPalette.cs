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
        /// <summary>art-r6-10：DesertRocks_01〜09 の掛け色（素材のままの (240,80,16) のプラスチックの橙は、どの場面でも一番鮮やかで金のゴール・青緑のブーストより目立った）。</summary>
        /// 案の (0.86, 0.70, 0.62) は赤が強く、画像の平均 (0.59, 0.38, 0.27) に掛けると彩度が 0.55 → 0.68 に上がるので、青・緑を残して暗くする色にした。
        public static readonly Color DesertRocksTint = new Color(0.82f, 0.82f, 0.86f);
        /// <summary>art-r6-10：DesertRocks_Tex01〜08 の平均色（画像を測った値、Validator の MeanColor と同じ考え方）。</summary>
        public static readonly Color DesertRocksMean = new Color(0.59f, 0.38f, 0.27f);
        /// <summary>art-r6-10：小石（DesertRocks_10〜15）は軽く（0.95）。</summary>
        public static readonly Color DesertPebbleTint = new Color(0.95f, 0.95f, 0.95f);
        /// <summary>art-r6-10：岩のマテリアルの (平均色 × 掛け色) の彩度の上限（Validator が警告）。</summary>
        public const float ROCK_MAX_SATURATION = 0.65f;
        /// <summary>art-r6-01：メサの岩の (平均色 × 掛け色) の輝度の下限（真っ黒の板にしない）。</summary>
        public const float MESA_MIN_LUMINANCE = 0.25f;
        public static readonly Color RockGrayTint = new Color(1.0f, 0.80f, 0.66f);
        // 静的な初期化は書いた順：RockGrayTint の後に置く
        /// <summary>岩のセット（RockSetStylized）の色：RockGray と同じ暖色に、土の色を少し掛ける（×0.9、art-r3-13）。</summary>
        public static readonly Color RockSetTint = new Color(RockGrayTint.r * 0.93f, RockGrayTint.g * 0.88f, RockGrayTint.b * 0.84f);
        public static readonly Color WeatheredWood = new Color(0.78f, 0.66f, 0.52f);
        public static readonly Color Sage = new Color(0.62f, 0.66f, 0.52f);
        /// <summary>art-r5-06：白い壁を塗り替える 4 色の板壁（建物の名前の決まったハッシュで選ぶ）。青緑はブースト専用なので使わない。</summary>
        public static readonly Color FadedBarnRed = new Color(0.60f, 0.33f, 0.25f);
        public static readonly Color Adobe = new Color(0.80f, 0.60f, 0.44f);
        public static readonly Color Ochre = new Color(0.78f, 0.62f, 0.36f);
        public static readonly Color[] Sidings = { WeatheredWood, FadedBarnRed, Adobe, Ochre };
        /// <summary>art-r5-06：町・建物の塗り替えた色の明るさ（HSV の v）の上限（夕景の環境光でも白い漆喰に見せない）。</summary>
        public const float TOWN_MAX_V = 0.82f;
        public const float WHITE_WALL_V = 0.80f, WHITE_WALL_S = 0.25f, WHITE_WALL_LERP = 0.88f;
        /// <summary>
        /// art-r5-02：道の家具の杭（WoodLogs_01 を立てた柱：柵の継ぎ目・直線の標柱・近づく道）の色。地面より 1〜2 段暗い古い木
        /// （前は素材のままのクリーム色で、白い歯・骨に見えて本当の道しるべ＝茶色の柵より目立った）。
        /// </summary>
        public static readonly Color PostWood = new Color(0.50f, 0.36f, 0.25f);

        // ---- 地形の色（TerrainBuilder の TerrainLayer）----
        public static readonly Color DirtCanyon = new Color(0.62f, 0.36f, 0.22f);    // 赤錆
        public static readonly Color DirtDunes = new Color(0.80f, 0.63f, 0.42f);     // art-r4-05：砂に近い明るい土（道の脇に見えても暗い帯にならない。前の 0.62/0.44/0.27 はアスファルトの帯に見えた）
        public static readonly Color DirtBorder = new Color(0.62f, 0.40f, 0.40f);    // 紫の夕景（art-r3-07：明るく）
        /// <summary>外の地面（art-r3-10）：3 ステージの土の平均に近い色（クリーム色の砂の板にしない）。</summary>
        public static readonly Color DirtOuter = new Color(0.64f, 0.43f, 0.31f);
        public static readonly Color RockLayer = new Color(0.55f, 0.45f, 0.38f);
        /// <summary>砂漠の砂（Sand(1)）。前は 3 ステージ共通の砂の色だった（art-r4-06：荒野・境目では黄色いしみに見えた）。</summary>
        /// art-r6-11：0.90/0.76/0.52 → 0.84/0.68/0.46（道を明るくするのではなく砂を下げて差を保つ。夕日 × トゥーンの床で白く飛ばない）。
        public static readonly Color SandLayer = new Color(0.84f, 0.68f, 0.46f);
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
        /// <summary>art-r5-04：切り通しの壁の岩の段に掛ける色（明・暗・明）。StrataLightBorder / StrataDarkBorder の明暗の比を岩の色に掛ける。</summary>
        public static readonly Color WallTierLight = new Color(1.0f, 0.90f, 0.82f);
        public static readonly Color WallTierDark = new Color(0.66f, 0.52f, 0.48f);
        public static Color StrataLightFor(int stage) => stage == 2 ? StrataLightBorder : StrataLight;
        public static Color StrataDarkFor(int stage) => stage == 2 ? StrataDarkBorder : StrataDark;

        // ---- 地面の粒（art-r4-02）：ToonTerrain のワールド座標の値ノイズで、道の外の色を少しだけ揺らす ----
        /// art-r5-13：GRAIN_AMP 0.07 → 0.13、SCUFF_AMP 0.06 → 0.11（騎手の目の高さ 2.6m でも読める）。どちらも道の重み &lt; 0.5 の所だけ（道はきれいに）。
        public const float GRAIN_SCALE_M = 3.5f, GRAIN_AMP = 0.13f, MOTTLE_SCALE_M = 45f, MOTTLE_AMP = 0.05f;
        public const float SCUFF_SCALE_M = 1.2f, SCUFF_AMP = 0.11f;
        /// <summary>art-r6-07：道の脇（道の外 〜4m）の土の擦れと小石の点（1.5m、±0.06）。</summary>
        public const float NEAR_SCUFF_SCALE_M = 1.5f, NEAR_SCUFF_AMP = 0.06f;
        /// <summary>art-r6-11：砂のレイヤーのノイズ（0.03 → 0.015）と風の向きのさざ波（波長 1.6m・±0.04）。等方的なまだら（迷彩）にしない。</summary>
        public const float SAND_NOISE_AMOUNT = 0.015f, SAND_RIPPLE_WAVE_M = 1.6f, SAND_RIPPLE_AMP = 0.04f;
        public static readonly Vector2 SandWindDir = new Vector2(0.8f, 0.6f);
        /// <summary>art-r6-17：境目の土の色の高さの帯（6m ごと・±1.5m のゆらぎ・混ぜる割合 0.7）と細かい粒（±0.03）。</summary>
        public const float BORDER_BAND_M = 6f, BORDER_BAND_WOBBLE_M = 1.5f, BORDER_BAND_MIX = 0.7f, BORDER_GRAIN_AMP = 0.03f;

        /// <summary>ステージ（0 荒野 / 1 砂漠 / 2 境目、それ以外 = ロビー・外の地面）の土の色。</summary>
        public static Color Dirt(int stage) => stage == 1 ? DirtDunes : (stage == 2 ? DirtBorder : DirtCanyon);

        // ---- 道の踏み固めた土（art-r2-02）：ステージの土・砂と明るさで 0.15 / 0.10 以上離す（道が読める）----
        public static readonly Color TrailCanyon = new Color(0.82f, 0.63f, 0.46f);   // 明るい踏み固めた粘土（赤錆の土 0.405 → 0.66）
        public static readonly Color TrailDunes = new Color(0.88f, 0.78f, 0.58f);    // art-r6-11：0.95/0.86/0.68 は太陽 1.25 × トゥーンで (255,250,150) に飛んだ    // art-r5-07：砂より明るい踏み固めた砂（明るさで砂 + 0.08 以上、art-r4-05 の 0.84/0.70/0.50 は砂と同じ明るさで道が見えなかった）
        /// <summary>art-r5-07：砂漠の道は砂より明るさで TRAIL_DUNES_ABOVE_SAND 以上明るい（色相ではなく明るさの差で読む）。</summary>
        public const float TRAIL_DUNES_ABOVE_SAND = 0.08f;
        /// <summary>轍の色 = 道の色 × RUT_SHADE（art-r3-06 → art-r4-02：0.85 → 0.75 → art-r5-01：0.86、やわらかい 2 本の線。穴・油のしみに見せない）。</summary>
        /// art-r6-04：0.94（暗くするのは 0.06 まで。細い 2 本の線は濃くなくても読め、遠くで点・しみにならない）。
        public const float RUT_SHADE = 0.94f;
        /// <summary>art-r6-04：轍の暗さの上限（1 − RUT_SHADE）。</summary>
        public const float RUT_SHADE_MAX_DARKEN = 0.06f;
        public static readonly Color TrailBorder = new Color(0.78f, 0.63f, 0.56f);   // 夕景の明るい道（紫の土 0.39 → 0.66、砂 0.77 とも 0.11 離す）

        /// <summary>ステージの道の色。</summary>
        public static Color Trail(int stage) => stage == 1 ? TrailDunes : (stage == 2 ? TrailBorder : TrailCanyon);

        // ---- 町の地面の板・岩山（art-r2-11）----
        /// <summary>町の模型の地面の板（WesternTown_001_Ground）：まわりの砂の地面に合わせた暗めの砂色（明るすぎるクリーム色にしない）。</summary>
        public static readonly Color TownGroundTint = new Color(0.80f, 0.60f, 0.44f);   // art-r3-09：踏み固めた土（TrailCanyon 寄り）で砂の土手へなじむ
        /// <summary>Buildings/*.fbx（画像付き）の青白い壁を古い木の色へ寄せる掛け色（art-r2-14）。</summary>
        public static readonly Color BuildingTextureTint = new Color(0.80f, 0.62f, 0.46f);   // art-r5-06：0.85 → 0.80（夕景の環境光で白く見えた）。art-r4-16・art-r3-15 と同じ色合い
        /// <summary>画像付きの建物の掛け色の一番明るい成分の上限（art-r4-16 → art-r5-06：0.85 → 0.80）。</summary>
        public const float BUILDING_TEX_MAX = 0.80f;

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
            // art-r6-10：砂漠の岩（DesertRocks_01〜09）は彩度を落とす。小石（10〜15）は軽く
            if (Has(assetPath, "DesertRocks") && partName != null)
            {
                int num = PartNumber(partName);
                if (num >= 1 && num <= 9) { s.tint = DesertRocksTint; s.flatten = ROCK_FLATTEN; s.flattenMip = ROCK_FLATTEN_MIP; s.key += "_Muted"; }
                else if (num >= 10) { s.tint = DesertPebbleTint; s.key += "_Pebble"; }
            }
            // art-r3-13：岩のセット（灰白の岩）も暖色へ（遠くで白い「きのこ」の岩に見えた）
            if (Has(assetPath, "RockSetStylized")) { s.tint = RockSetTint; s.flatten = ROCK_FLATTEN; s.flattenMip = ROCK_FLATTEN_MIP; s.key += "_WarmSet"; }
            if (Has(assetPath, "Gems")) { s.glow = GEM_GLOW; s.key += "_Glow"; }
            // art-r5-02：立てた丸太の杭・横木（WoodLogs_01）は暗い古い木（クリーム色の白い歯にしない）
            if (Has(assetPath, "WoodLogs") && partName == DressingPlan.POST_PART) { s.tint = PostWood; s.key += "_Post"; }
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

        /// <summary>部品名の末尾の番号（"DesertRocks_07" → 7、"WesternTown_017_Prop" → 17）。無ければ −1。</summary>
        public static int PartNumber(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return -1;
            int i = partName.IndexOf('_');
            if (i < 0) return -1;
            int n = 0, k = i + 1, digits = 0;
            while (k < partName.Length && char.IsDigit(partName[k])) { n = n * 10 + (partName[k] - '0'); k++; digits++; }
            return digits == 0 ? -1 : n;
        }

        /// <summary>art-r6-10：色の彩度（HSV の s）。</summary>
        public static float Saturation(Color c) { float h, s, v; ToHSV(c, out h, out s, out v); return s; }

        /// <summary>Buildings/*.fbx・RanchCompound の部品（画像付きの建物）か。</summary>
        public static bool IsBuildingModel(string assetPath) => Has(assetPath, "Buildings/") || Has(assetPath, "RanchCompound");

        /// <summary>画像付きの素材に掛ける色（art-r2-14：建物の青白い壁 → 古い木）。それ以外は白。</summary>
        public static Color TextureTint(string assetPath) => IsBuildingModel(assetPath) ? BuildingTextureTint : Color.white;

        /// <summary>色だけで塗られた素材の色を共通の色に寄せる（町・建物の灰色・青 → 古い木、緑 → セージ）。それ以外はそのまま。</summary>
        public static Color RemapBaseColor(string assetPath, Color c) => RemapBaseColor(assetPath, c, assetPath);

        /// <summary>
        /// art-r5-06：objectName（建物の部品名・模型のパス）の決まったハッシュで、白い壁（v &gt; 0.80・s &lt; 0.25）を 4 色の板壁
        /// （古い木・色あせた納屋の赤・日干し煉瓦・黄土）のどれかへ 0.88 寄せる。暗い縁取り・窓はそのまま。結果の明るさは TOWN_MAX_V まで。
        /// </summary>
        public static Color RemapBaseColor(string assetPath, Color c, string objectName)
        {
            if (!Has(assetPath, "WesternTown") && !IsBuildingModel(assetPath)) return c;
            float h, s, v;
            ToHSV(c, out h, out s, out v);
            if (v < 0.12f) return c;                                   // 黒に近い（窓・影）はそのまま
            Color r = c;
            if (v > WHITE_WALL_V && s < WHITE_WALL_S) r = Lerp(c, SidingFor(objectName), WHITE_WALL_LERP);   // 白に近いクリーム色の壁（夕景で白く飛ぶ）
            else if (s < 0.12f && v > 0.25f) r = Lerp(c, WeatheredWood, 0.65f);          // 灰色
            else if (h >= 180f && h <= 260f && s >= 0.08f) r = Lerp(c, WeatheredWood, 0.7f);   // 青
            else if (h >= 75f && h < 170f && s >= 0.15f) r = Lerp(c, Sage, 0.7f);        // 緑
            return CapValue(r, TOWN_MAX_V);
        }

        /// <summary>art-r5-06：建物の名前の板壁の色（FNV-1a の決まったハッシュ。tools/preview の siding_for と同じ）。</summary>
        public static Color SidingFor(string objectName) => Sidings[(int)(StableHash(objectName) % (uint)Sidings.Length)];

        /// <summary>文字列の決まったハッシュ（FNV-1a 32bit。string.GetHashCode は実行ごとに変わるので使わない）。</summary>
        public static uint StableHash(string s)
        {
            uint hsh = 2166136261u;
            if (s != null)
                for (int i = 0; i < s.Length; i++) { hsh ^= s[i]; hsh *= 16777619u; }
            return hsh;
        }

        /// <summary>HSV の v（一番明るい成分）が max を超えたら、色相・彩度を保って暗くする。</summary>
        public static Color CapValue(Color c, float max)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (mx <= max || mx <= 1e-5f) return c;
            float k = max / mx;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
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
