using System;
using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>1 つの飾りの置き場所（Terrain ローカル m。ロビーはロビー中心からの相対）。</summary>
    public sealed class Placement
    {
        public string asset;        // Assets/HorseRace/Models からの相対パス（例 "Packs/Cacti/Cacti.fbx"）
        public string part;         // パックの部品名（子の名前）。モデル全体なら null
        public string group;        // 階層の整理用（"CliffWalls" "WagonCamp" など）
        public Vector3 position;    // y = 足元の一番低い地面 − 沈め量
        public float yawDeg;
        public float rollDeg;       // 横倒し（骨の馬など）
        public float scale = 1f;
        public float scaleY = 1f;   // 縦だけの追加の拡大（art-r2-08：岩を縦に伸ばして崖の代わりにする）。高さ height はこれ込み
        public float scaleZ = 1f;   // ローカル Z だけの追加の拡大（art-r3-02：立てた丸太の杭の断面を細くする）
        public float radius;        // 足元の半径（m、拡大込み）。重なり・道からの距離の判定に使う
        public float height;        // 高さ（m、拡大込み）
        public bool collider;       // 当たり判定を付けるか（届く範囲の大きなものだけ）
        public int shape;           // DressingPlan.SHAPE_*
        public bool background;     // 背景専用（メサ・厩舎：見えない壁で囲む）
        public bool lamp;           // art-r4-14：立てた丸太の柱の上に暖色の灯り（DressingBuilder が付ける。野の街灯の代わり）
        public float groundR = -1f; // art-r5-03：接地を調べた足元の半径（m、木・サボテンは幹）。< 0 = FootprintRadius
        public Color tint = Color.white;   // art-r5-04：この置き物だけに掛ける色（切り通しの段の明暗の縞）。白 = 素材の決まりのまま
    }

    /// <summary>
    /// 飾りの配置計画（UnityEditor に依存しない。tests/HorseRace.Tests で単体テストする）。DressingBuilder / LobbyBuilder が実体化する。
    /// 決まった乱数（ステージの seed）で、テーマごとの「まとまり」（崖の並び・岩場・枯れ木の林・幌馬車の野営地・廃屋・サボテンの林・
    /// 骨の原・オアシス・牧場・メサの輪・小さな集落・ゴールの町）を作る。
    /// 置かない場所：道（中心から ROAD_CLEAR m ＝平らな道 ±12m・CP の箱 ±18m・黒板 15m を含む）、岩橋の溝、ジャンプ台、
    /// 近道の通路、スターターの立ち位置、ゴール後の囲い、表彰台、Terrain の端。
    /// 予算：1 ステージ MAX_PER_STAGE 個まで（すべて静的、LOD なし）。小物（高さ 1.2m 未満）と道から REACHABLE_M より遠いものは Collider なし。
    /// </summary>
    public static class DressingPlan
    {
        public const int MAX_PER_STAGE = 2600;     // art-r4-02：路肩の小石（〜500 個、0.3〜1.1m、静的バッチング）を足した     // art-r3-03：途切れない柵（〜480 枚の低い柵、静的バッチング）を足した。art-r1：谷底の近景・道の目印を足した（小物は静的バッチング＋トゥーンの GPU インスタンシング前提）
        public const int MIN_PER_STAGE = 450;
        public const int MAX_LOBBY = 340;   // art-r3-09：大通りの飾りと地面の板の縁を足した（前は 150）。          // 木の帯（LobbyLayout.TreeBelt）と柵は別に数える（ロビー＝町だけ、飾りは町と柵の間の 8m の帯だけなので 300 → 150）
        public const float LARGE_MIN_H = 1.6f;     // 「大きな飾り」の高さ。これ未満は小物（まとまりの中だけ、1 ステージの 25% まで）
        public const float SMALL_SHARE_MAX = 0.25f;
        public const float BAND_WINDOW_M = 100f;   // 道沿いの帯：コース 100m ごとに
        public const float BAND_DIST_M = 60f;      //   道から 60m 以内の大きな飾りを
        public const int ROADSIDE_LARGE_PER_100M = 6;   //   6 個以上
        public const float TREE_SCALE_MIN = 0.8f, TREE_SCALE_MAX = 1.4f;
        public const int OASIS_TREES = 25;
        // テーマごとの木の最少本数（テストで確認）
        public const int MIN_DEAD_TREES_CANYON = 50, MIN_DEAD_TREES_DUNES = 30, MIN_TREES_BORDER = 50, MIN_TREES_LOBBY = 20;   // art-r2-04：枯れ木・緑の木は素材の 8% まで
        public const float ROAD_CLEAR = 18f;       // 散らす飾り：道の中心から（足元の半径込み）
        public const float ROAD_MIN = 13f;         // 道沿いに意図して置くもの（柵・町の建物）：平らな道 ±12m の外
        public const float REACHABLE_M = 60f;      // これより遠い飾りには Collider を付けない（コース外リセットで届かない）
        public const float COLLIDER_MIN_H = 1.2f;  // これより低い小物は Collider なし
        public const float EDGE_MARGIN = 10f;
        public const float CAMP_SPACING_M = 500f;  // 幌馬車の野営地の間隔（弧長）
        public const int ROADSIDE_EVERY_CP = 4;    // 道沿いの柵と標柱
        // ---- 近景（art-r1-02）：峡谷は道から 30m 以内に 100m ごと 10 個以上 ----
        public const float NEAR_FIELD_M = 30f;
        public const int NEAR_PER_100M = 10;
        public const float CANYON_FLOOR_STEP_M = 18f;    // 谷底の近景：弧長 22m ごと、左右交互
        public const float CANYON_FLOOR_MAX_LAT = 28f;
        public const float CANYON_BAND_MAX_LAT = 32f;    // 峡谷の道沿いの帯は道から 32m まで（狭い谷で斜面・縁に乗らない）
        // ---- 道の目印（art-r1-10）----
        public const float GUIDE_CURVATURE = 1f / 120f;  // これより急なカーブの外側に柵・杭の列
        public const float GUIDE_STEP_M = 6f, GUIDE_LATERAL = 14.5f, GUIDE_AFTER_M = 20f;
        public const float GUIDE_STRAIGHT_STEP_M = 40f;  // 直線は 40m ごとに左右交互の標柱
        public const float LANDMARK_MAX_ANGLE = 35f;     // スタートから見て進む向き ±35° に遠くの目印
        public const float LANDMARK_MIN_ROAD_M = 60f;
        // ---- 崖の壁（art-r1-03）：広い面（部品のローカル ±X）を道へ、長い辺（ローカル Z）を道に沿わせる ----
        public const float CLIFF_HALF_DEPTH = 0.67f;     // 部品の半分の厚さ ÷ 外形の半径（Blender で測った最大）
        public const float CLIFF_SINK = 0.22f;           // 高さに対する沈め量（斜面に埋める）
        public const float CLIFF_YAW_JITTER = 6f;
        public const float MESA_GATE_FOOTPRINT = 0.55f;  // メサの門：道側の裾の半径 ÷ 外形の半径（裾は斜面に埋まる）
        public const float STRETCHED_ROCK_FOOTPRINT = 0.85f;   // 縦に伸ばした岩：足元の半径 ÷ 外形の半径

        public const int SHAPE_NONE = 0, SHAPE_BOX = 1, SHAPE_MESH = 2, SHAPE_TRUNK = 3;

        // ---- 素材のパス（Assets/HorseRace/Models からの相対）----
        public const string CLIFFS = "Packs/Cliffs/Cliffs.fbx", DROCKS = "Packs/DesertRocks/DesertRocks.fbx",
            RCOLL = "Packs/RockCollection/RockCollection.fbx", DPROPS = "Packs/DesertProps/DesertProps.fbx",
            GEMS = "Packs/Gems/Gems.fbx", CAMEL = "Camel.fbx",
            CACTI = "Packs/Cacti/Cacti.fbx", CAMPFIRE = "Packs/Campfire/Campfire.fbx", LOGS = "Packs/WoodLogs/WoodLogs.fbx",
            HAY = "Packs/HayBales/HayBales.fbx", FARM = "Packs/FarmProps/FarmProps.fbx",
            ACACIA = "Props/AcaciaGrove/AcaciaGrove.fbx", CGROUP = "Props/CactusGroup/CactusGroup.fbx", DEADTREE = "Props/DeadTree/DeadTree.fbx",
            NOTICE = "Props/NoticeBoard/NoticeBoard.fbx",
            MESA = "Props/MesaRock/MesaRock.fbx", STABLE = "Props/Stable/Stable.fbx", TENT = "Props/Tent/Tent.fbx",
            MESAB = "ThirdParty/MesaRockB/MesaRockB.fbx", RSTYL = "ThirdParty/RockSetStylized/RockSetStylized.fbx",
            TOWNBLOCK = "Buildings/TownBlock.fbx", SALOON = "Buildings/SaloonFacade.fbx", RANCH = "Buildings/RanchCompound.fbx",
            SKELETON = "SkeletonHorse.fbx", HORSE = "Horse.fbx", TREES = "Packs/Trees/Trees.fbx", WTOWN = "Packs/WesternTown/WesternTown.fbx";
        public const string RANCH_TOWER = "RanchCompound_11_Building", TOWN_TALL_A = "WesternTown_084_Building", TOWN_TALL_B = "WesternTown_082_Building",
            RAIL_TRACK = "WesternTown_049_Track";
        public static string RockGray(char c) => "ThirdParty/RockGray/RockGray" + c + ".fbx";

        // ---- 部品の分類（Blender で一覧画像を描いて確認）----
        static readonly string[] CliffParts = Parts("Cliffs", 1, 13);
        static readonly string[] RockBig = { "DesertRocks_01", "DesertRocks_02", "DesertRocks_03" };
        static readonly string[] RockMid = Parts("DesertRocks", 4, 9);
        static readonly string[] RockSmall = Parts("DesertRocks", 10, 15);
        static readonly string[] Pebbles = Every8("RockCollection", 184);
        static readonly string[] DpRocks = Named("DesertProps", 1, 2, 4, 9, 11, 12, 13, 15, 16, 17, 18, 19, 21, 25, 26);
        static readonly string[] DpCacti = Named("DesertProps", 20, 24, 27, 28, 29, 30);
        static readonly string[] DpGrass = Named("DesertProps", 22, 23);
        static readonly string[] DpBones = Named("DesertProps", 7, 8, 14);   // 07 = 頭骨、08 = あばら、14 = 骨
        static readonly string[] CactiParts = Parts("Cacti", 1, 9);
        static readonly string[] FarmCrates = Named("FarmProps", 11, 12, 19);
        static readonly string[] FarmBarrels = Named("FarmProps", 15, 22);
        static readonly string[] FarmSmall = Named("FarmProps", 9, 10, 13, 14, 16, 17, 18, 20, 21, 23, 24, 25, 26, 27, 28, 29, 30);
        static readonly string[] FarmFences = Named("FarmProps", 3, 4, 6, 8);
        const string FARM_LAMP = "FarmProps_02", FARM_TABLE = "FarmProps_05", FARM_BENCH = "FarmProps_07";
        /// <summary>立てた丸太の杭の部品（art-r3-02：骨 DesertProps_03/05 を杭に使わない）。</summary>
        public const string POST_PART = "WoodLogs_01";
        public const float POST_H = 2.6f, POST_THICK = 0.3f;          // 直線の標柱（立て札の柱と同じ 2.6m）
        /// <summary>野の灯りの柱（art-r4-14）：立てた丸太 3.2m、上に灯り。</summary>
        public const float LAMP_POST_H = 3.2f, LAMP_POST_THICK = 0.26f;
        /// <summary>FarmProps_02（L 字の柱の街灯）を使ってよいまとまり（町だけ、art-r4-14）。</summary>
        public static readonly HashSet<string> FarmLampGroups = new HashSet<string> { "GoalTown", "StartTown", "StreetDressing", "Settlement", "MinersCamp", "WaterStop", "GoalOutpost", "PlazaApron" };
        public const float RAIL_POST_H = 1.7f, RAIL_POST_THICK = 0.24f; // 柵の継ぎ目の杭（柵より高く、大きな飾りに数える）
        static readonly string[] LogParts = Parts("WoodLogs", 1, 16);
        static readonly string[] HayParts = Parts("HayBales", 1, 7);
        static readonly string[] TreeParts = { "Trees_01", "Trees_02" };
        static readonly string[] DeadAndRocks = { "DesertRocks_10", "DesertRocks_11", "DesertRocks_12", "DesertRocks_13", "DesertRocks_14", "DesertRocks_15" };
        static readonly char[] GrayLetters = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H' };
        // ---- 町（WesternTown）の小物・岩（art-r2-05、Blender で部品の一覧を描いて確認）----
        static readonly string[] TownCrates = Town("Prop", 22, 112, 113, 116, 118);   // 木箱の山
        public const string TOWN_CART = "WesternTown_115_Prop", TOWN_BARRELS = "WesternTown_117_Prop", TOWN_SIGN = "WesternTown_038_Prop";
        static readonly string[] TownCacti = Town("Prop", 7, 8, 10, 15, 17, 18, 19, 20, 21, 23, 24, 25, 26, 32, 34, 35, 40, 41, 42, 43, 44, 53, 54, 55,
            58, 59, 60, 61, 62, 64, 65, 66, 69, 71, 75, 76, 77, 78, 85, 86, 89, 90, 91, 97, 98, 99, 105, 107, 108, 109, 110, 114, 119, 120, 121, 123, 124, 126);
        static readonly string[] TownRocksBig = Town("Rock", 3, 5, 6, 16, 27, 28, 33, 45, 46, 51, 68, 73, 74, 81, 92, 93, 95, 96, 103, 104, 125, 128, 133, 134, 136, 137);
        static readonly string[] TownRocksSmall = Town("Rock", 4, 9, 13, 14, 31, 50, 63, 72, 100, 101, 102, 106, 111, 122, 129, 130, 131, 132, 135);
        static readonly string[] TownPebbles = Town("Prop", 11, 12, 87, 88);
        static readonly string[] RanchBuildings = { "RanchCompound_07_Building", "RanchCompound_09_Building", "RanchCompound_10_Building" };
        static readonly string[] RstylParts = Parts2("RockSetStylized", 1, 7, "_Rock");
        static readonly string[] RstylBig = { "RockSetStylized_01_Rock", "RockSetStylized_02_Rock", "RockSetStylized_06_Rock", "RockSetStylized_07_Rock" };

        static string[] Town(string kind, params int[] n)
        {
            var r = new string[n.Length];
            for (int i = 0; i < n.Length; i++) r[i] = "WesternTown_" + n[i].ToString("000") + "_" + kind;
            return r;
        }

        static string[] Parts2(string pack, int a, int b, string suffix)
        {
            var r = new string[b - a + 1];
            for (int i = a; i <= b; i++) r[i - a] = pack + "_" + i.ToString("00") + suffix;
            return r;
        }

        /// <summary>宝石の部品（Gems_0g_0k、g = 1〜4 の組、k = 1〜8）。</summary>
        static string Gem(int g, int k) => "Gems_0" + (1 + GenMod(g, 4)) + "_0" + (1 + GenMod(k, 8));
        static int GenMod(int a, int n) => ((a % n) + n) % n;

        static string[] Parts(string pack, int a, int b)
        {
            var r = new string[b - a + 1];
            for (int i = a; i <= b; i++) r[i - a] = pack + "_" + i.ToString("00");
            return r;
        }

        static string[] Named(string pack, params int[] n)
        {
            var r = new string[n.Length];
            for (int i = 0; i < n.Length; i++) r[i] = pack + "_" + n[i].ToString("00");
            return r;
        }

        static string[] Every8(string pack, int max)
        {
            var r = new List<string>();
            for (int i = 8; i <= max; i += 8) r.Add(pack + "_" + i.ToString("00"));
            return r.ToArray();
        }

        // ---- 大きさ（Blender で測った外形：足元の半径 m・高さ m、拡大 1）----
        public struct Size
        {
            public float r, h;
            public Size(float r, float h) { this.r = r; this.h = h; }
        }

        public static string Key(string asset, string part) => asset + "|" + (part ?? "");

        static readonly Dictionary<string, Size> Catalog = new Dictionary<string, Size>
        {
            { Key("Buildings/RanchCompound.fbx", null), new Size(39.1f, 24.4f) },
            { Key("Buildings/SaloonFacade.fbx", null), new Size(8.5f, 10.9f) },
            { Key("Buildings/TownBlock.fbx", null), new Size(8.9f, 10.1f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_01"), new Size(1.1f, 4.1f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_02"), new Size(1.2f, 4.1f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_03"), new Size(1.0f, 4.1f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_04"), new Size(1.4f, 3.2f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_05"), new Size(1.2f, 3.2f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_06"), new Size(1.5f, 3.0f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_07"), new Size(1.0f, 2.9f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_08"), new Size(1.4f, 2.7f) },
            { Key("Packs/Cacti/Cacti.fbx", "Cacti_09"), new Size(1.0f, 2.0f) },
            { Key("Packs/Campfire/Campfire.fbx", "Campfire_01"), new Size(1.1f, 1.2f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_01"), new Size(31.2f, 26.1f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_02"), new Size(29.9f, 28.9f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_03"), new Size(30.0f, 33.4f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_04"), new Size(23.6f, 26.0f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_05"), new Size(20.3f, 24.3f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_06"), new Size(19.9f, 23.4f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_07"), new Size(18.4f, 23.8f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_08"), new Size(17.0f, 15.6f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_09"), new Size(17.5f, 19.4f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_10"), new Size(15.3f, 18.7f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_11"), new Size(15.0f, 21.0f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_12"), new Size(14.2f, 21.1f) },
            { Key("Packs/Cliffs/Cliffs.fbx", "Cliffs_13"), new Size(13.0f, 18.2f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_01"), new Size(2.9f, 2.9f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_02"), new Size(2.8f, 2.5f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_03"), new Size(0.5f, 3.4f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_04"), new Size(1.9f, 2.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_05"), new Size(0.4f, 3.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_06"), new Size(1.9f, 2.2f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_07"), new Size(1.8f, 1.7f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_08"), new Size(1.9f, 0.6f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_09"), new Size(1.3f, 2.5f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_10"), new Size(1.8f, 1.6f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_11"), new Size(1.0f, 2.5f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_12"), new Size(1.5f, 2.3f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_13"), new Size(0.8f, 2.3f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_14"), new Size(1.6f, 0.4f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_15"), new Size(1.0f, 2.3f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_16"), new Size(0.9f, 2.2f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_17"), new Size(0.8f, 2.2f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_18"), new Size(1.2f, 2.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_19"), new Size(0.7f, 2.0f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_20"), new Size(0.6f, 1.9f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_21"), new Size(0.8f, 1.9f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_22"), new Size(1.2f, 1.6f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_23"), new Size(1.2f, 1.5f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_24"), new Size(0.6f, 1.5f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_25"), new Size(0.6f, 1.4f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_26"), new Size(0.6f, 1.4f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_27"), new Size(0.8f, 1.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_28"), new Size(0.7f, 1.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_29"), new Size(0.3f, 1.1f) },
            { Key("Packs/DesertProps/DesertProps.fbx", "DesertProps_30"), new Size(0.4f, 1.1f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_01"), new Size(16.7f, 12.8f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_02"), new Size(11.7f, 12.4f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_03"), new Size(10.4f, 10.8f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_04"), new Size(6.6f, 2.9f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_05"), new Size(6.4f, 4.8f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_06"), new Size(4.6f, 4.3f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_07"), new Size(4.5f, 4.2f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_08"), new Size(4.5f, 1.6f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_09"), new Size(3.5f, 2.8f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_10"), new Size(1.9f, 3.2f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_11"), new Size(2.2f, 1.8f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_12"), new Size(2.0f, 1.0f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_13"), new Size(2.0f, 1.9f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_14"), new Size(1.9f, 2.0f) },
            { Key("Packs/DesertRocks/DesertRocks.fbx", "DesertRocks_15"), new Size(1.0f, 1.8f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_01"), new Size(6.6f, 0.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_02"), new Size(0.9f, 3.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_03"), new Size(1.4f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_04"), new Size(1.4f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_05"), new Size(1.6f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_06"), new Size(1.4f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_07"), new Size(1.5f, 0.7f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_08"), new Size(1.5f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_09"), new Size(0.8f, 0.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_10"), new Size(0.8f, 0.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_11"), new Size(0.9f, 1.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_12"), new Size(0.7f, 1.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_13"), new Size(0.7f, 0.1f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_14"), new Size(0.5f, 1.0f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_15"), new Size(0.5f, 0.7f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_16"), new Size(0.5f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_17"), new Size(0.5f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_18"), new Size(0.5f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_19"), new Size(0.5f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_20"), new Size(0.3f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_21"), new Size(0.3f, 0.6f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_22"), new Size(0.4f, 0.5f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_23"), new Size(0.4f, 0.5f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_24"), new Size(0.3f, 0.3f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_25"), new Size(0.3f, 0.3f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_26"), new Size(0.3f, 0.3f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_27"), new Size(0.3f, 0.5f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_28"), new Size(0.3f, 0.5f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_29"), new Size(0.2f, 0.5f) },
            { Key("Packs/FarmProps/FarmProps.fbx", "FarmProps_30"), new Size(0.2f, 0.4f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_01"), new Size(1.1f, 1.6f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_02"), new Size(0.6f, 0.6f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_03"), new Size(0.6f, 0.6f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_04"), new Size(0.6f, 0.6f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_05"), new Size(0.6f, 0.5f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_06"), new Size(0.6f, 0.5f) },
            { Key("Packs/HayBales/HayBales.fbx", "HayBales_07"), new Size(0.5f, 0.5f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_08"), new Size(1.1f, 2.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_104"), new Size(0.5f, 0.4f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_112"), new Size(0.5f, 0.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_120"), new Size(0.4f, 0.7f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_128"), new Size(0.5f, 0.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_136"), new Size(0.5f, 0.4f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_144"), new Size(0.4f, 0.7f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_152"), new Size(0.4f, 0.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_16"), new Size(1.1f, 1.9f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_160"), new Size(0.4f, 0.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_168"), new Size(0.4f, 0.5f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_176"), new Size(0.4f, 0.5f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_184"), new Size(0.4f, 0.6f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_24"), new Size(1.1f, 1.6f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_32"), new Size(1.1f, 1.3f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_40"), new Size(1.0f, 0.8f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_48"), new Size(1.0f, 0.8f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_56"), new Size(0.8f, 1.4f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_64"), new Size(0.5f, 1.0f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_72"), new Size(0.3f, 0.9f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_80"), new Size(0.3f, 0.9f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_88"), new Size(0.4f, 0.9f) },
            { Key("Packs/RockCollection/RockCollection.fbx", "RockCollection_96"), new Size(0.4f, 0.8f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_01"), new Size(0.9f, 0.3f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_02"), new Size(0.7f, 0.2f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_03"), new Size(0.6f, 0.3f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_04"), new Size(0.4f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_05"), new Size(0.4f, 0.0f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_06"), new Size(0.4f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_07"), new Size(0.4f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_08"), new Size(0.5f, 0.4f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_09"), new Size(0.3f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_10"), new Size(0.3f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_11"), new Size(0.3f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_12"), new Size(0.2f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_13"), new Size(0.3f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_14"), new Size(0.2f, 0.2f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_15"), new Size(0.2f, 0.1f) },
            { Key("Packs/WoodLogs/WoodLogs.fbx", "WoodLogs_16"), new Size(0.2f, 0.0f) },
            { Key("Props/AcaciaGrove/AcaciaGrove.fbx", null), new Size(8.5f, 6.5f) },
            { Key("Props/CactusGroup/CactusGroup.fbx", null), new Size(3.1f, 3.5f) },
            { Key("Props/DeadTree/DeadTree.fbx", null), new Size(3.8f, 3.8f) },
            { Key("Props/MesaRock/MesaRock.fbx", null), new Size(28.4f, 23.4f) },
            { Key("Props/Podium/Podium.fbx", null), new Size(3.5f, 0.7f) },
            { Key("Props/Stable/Stable.fbx", null), new Size(7.5f, 6.6f) },
            { Key("Props/Tent/Tent.fbx", null), new Size(5.5f, 2.9f) },
            { Key("ThirdParty/MesaRockB/MesaRockB.fbx", null), new Size(25.3f, 30.4f) },
            { Key("ThirdParty/RockGray/RockGrayA.fbx", null), new Size(2.0f, 2.0f) },
            { Key("ThirdParty/RockGray/RockGrayB.fbx", null), new Size(2.0f, 2.4f) },
            { Key("ThirdParty/RockGray/RockGrayC.fbx", null), new Size(2.0f, 1.6f) },
            { Key("ThirdParty/RockGray/RockGrayD.fbx", null), new Size(2.0f, 2.2f) },
            { Key("ThirdParty/RockGray/RockGrayE.fbx", null), new Size(1.7f, 2.7f) },
            { Key("ThirdParty/RockGray/RockGrayF.fbx", null), new Size(2.0f, 2.0f) },
            { Key("ThirdParty/RockGray/RockGrayG.fbx", null), new Size(2.0f, 2.2f) },
            { Key("ThirdParty/RockGray/RockGrayH.fbx", null), new Size(1.9f, 1.9f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", null), new Size(5.3f, 1.9f) },
            { Key("SkeletonHorse.fbx", null), new Size(1.3f, 2.0f) },
            { Key("Packs/Trees/Trees.fbx", "Trees_01"), new Size(3.9f, 9.0f) },
            { Key("Packs/Trees/Trees.fbx", "Trees_02"), new Size(3.4f, 8.5f) },
            // 1 つの FBX の中の部品（parts.json の外形から）
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_11_Building"), new Size(4.8f, 23.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_084_Building"), new Size(9.5f, 15.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_082_Building"), new Size(4.8f, 12.7f) },
            // 線路は長さ 95.6m だが幅 2.7m：足元は細い円とし、線路に沿った除外の円で他の飾りを避ける
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_049_Track"), new Size(2f, 1.3f) },
            // ゴールの町の通り（course-r1-12）：WesternTown の建物（parts.json の外形から）
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_029_Building"), new Size(6.0f, 9.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_030_Building"), new Size(6.7f, 6.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_036_Building"), new Size(6.1f, 7.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_039_Building"), new Size(6.9f, 11.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_047_Building"), new Size(7.2f, 14.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_048_Building"), new Size(7.7f, 6.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_056_Building"), new Size(6.3f, 5.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_057_Building"), new Size(6.5f, 13.0f) },
            // art-r2-05：町の小物・岩、牧場の建物、岩のセット、宝石の部品（parts.json の外形から）。ラクダは騎乗の模型と同じ大きさ
            { Key("Camel.fbx", null), new Size(1.4f, 2.3f) },
            { Key("Horse.fbx", null), new Size(1.3f, 2.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_003_Rock"), new Size(3.7f, 8.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_004_Rock"), new Size(1.6f, 3.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_005_Rock"), new Size(10.4f, 9.1f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_006_Rock"), new Size(5.0f, 7.7f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_007_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_008_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_009_Rock"), new Size(2.8f, 1.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_010_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_011_Prop"), new Size(0.6f, 0.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_012_Prop"), new Size(0.6f, 0.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_013_Rock"), new Size(2.5f, 2.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_014_Rock"), new Size(2.8f, 2.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_015_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_016_Rock"), new Size(3.7f, 7.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_017_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_018_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_019_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_020_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_021_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_022_Prop"), new Size(1.5f, 1.7f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_023_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_024_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_025_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_026_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_027_Rock"), new Size(3.2f, 6.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_028_Rock"), new Size(6.3f, 10.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_031_Rock"), new Size(2.8f, 1.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_032_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_033_Rock"), new Size(6.3f, 10.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_034_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_035_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_038_Prop"), new Size(1.0f, 0.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_040_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_041_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_042_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_043_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_044_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_045_Rock"), new Size(5.2f, 8.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_046_Rock"), new Size(5.6f, 10.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_050_Rock"), new Size(2.1f, 1.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_051_Rock"), new Size(3.8f, 7.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_053_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_054_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_055_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_058_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_059_Prop"), new Size(0.5f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_060_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_061_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_062_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_063_Rock"), new Size(2.5f, 1.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_064_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_065_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_066_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_068_Rock"), new Size(3.8f, 7.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_069_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_071_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_072_Rock"), new Size(0.8f, 3.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_073_Rock"), new Size(5.6f, 10.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_074_Rock"), new Size(3.2f, 6.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_075_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_076_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_077_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_078_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_081_Rock"), new Size(6.3f, 10.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_085_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_086_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_087_Prop"), new Size(1.2f, 1.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_088_Prop"), new Size(0.6f, 0.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_089_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_090_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_091_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_092_Rock"), new Size(6.3f, 10.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_093_Rock"), new Size(3.2f, 6.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_095_Rock"), new Size(5.2f, 8.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_096_Rock"), new Size(3.7f, 7.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_097_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_098_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_099_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_100_Rock"), new Size(1.6f, 1.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_101_Rock"), new Size(2.5f, 1.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_102_Rock"), new Size(1.6f, 1.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_103_Rock"), new Size(3.8f, 7.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_104_Rock"), new Size(3.2f, 9.1f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_105_Prop"), new Size(0.9f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_106_Rock"), new Size(3.6f, 3.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_107_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_108_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_109_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_110_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_111_Rock"), new Size(3.6f, 3.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_112_Prop"), new Size(0.9f, 0.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_113_Prop"), new Size(1.0f, 1.7f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_114_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_115_Prop"), new Size(2.3f, 1.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_116_Prop"), new Size(0.5f, 0.8f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_117_Prop"), new Size(1.8f, 2.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_118_Prop"), new Size(0.7f, 1.7f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_119_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_120_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_121_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_122_Rock"), new Size(1.6f, 1.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_123_Prop"), new Size(0.8f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_124_Prop"), new Size(1.0f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_125_Rock"), new Size(7.0f, 12.0f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_126_Prop"), new Size(0.6f, 2.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_128_Rock"), new Size(5.3f, 10.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_129_Rock"), new Size(5.3f, 4.9f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_130_Rock"), new Size(3.2f, 4.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_131_Rock"), new Size(3.2f, 4.4f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_132_Rock"), new Size(3.6f, 3.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_133_Rock"), new Size(3.7f, 7.3f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_134_Rock"), new Size(5.4f, 9.6f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_135_Rock"), new Size(2.0f, 1.5f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_136_Rock"), new Size(9.3f, 11.2f) },
            { Key("Packs/WesternTown/WesternTown.fbx", "WesternTown_137_Rock"), new Size(3.7f, 8.2f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_04_Prop"), new Size(5.8f, 2.8f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_05_Prop"), new Size(1.9f, 4.0f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_06_Prop"), new Size(1.9f, 4.0f) },
            { Key("Props/NoticeBoard/NoticeBoard.fbx", null), new Size(1.5f, 2.6f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_07_Building"), new Size(11.1f, 16.1f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_09_Building"), new Size(13.4f, 16.0f) },
            { Key("Buildings/RanchCompound.fbx", "RanchCompound_10_Building"), new Size(2.6f, 4.1f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_01_Rock"), new Size(1.9f, 1.9f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_02_Rock"), new Size(0.7f, 1.4f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_03_Rock"), new Size(0.7f, 0.3f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_04_Rock"), new Size(0.9f, 0.7f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_05_Rock"), new Size(0.3f, 0.4f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_06_Rock"), new Size(0.5f, 1.3f) },
            { Key("ThirdParty/RockSetStylized/RockSetStylized.fbx", "RockSetStylized_07_Rock"), new Size(0.7f, 1.5f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_01"), new Size(0.5f, 1.2f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_02"), new Size(0.2f, 1.1f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_03"), new Size(0.2f, 1.0f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_04"), new Size(0.2f, 0.9f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_05"), new Size(0.2f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_06"), new Size(0.2f, 0.6f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_07"), new Size(0.1f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_01_08"), new Size(0.1f, 0.4f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_01"), new Size(0.5f, 1.2f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_02"), new Size(0.2f, 1.1f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_03"), new Size(0.2f, 1.0f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_04"), new Size(0.2f, 0.9f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_05"), new Size(0.2f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_06"), new Size(0.2f, 0.6f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_07"), new Size(0.1f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_02_08"), new Size(0.1f, 0.4f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_01"), new Size(0.5f, 1.2f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_02"), new Size(0.2f, 1.1f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_03"), new Size(0.2f, 1.0f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_04"), new Size(0.2f, 0.9f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_05"), new Size(0.2f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_06"), new Size(0.2f, 0.6f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_07"), new Size(0.1f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_03_08"), new Size(0.1f, 0.4f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_01"), new Size(0.5f, 1.2f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_02"), new Size(0.2f, 1.1f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_03"), new Size(0.2f, 1.0f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_04"), new Size(0.2f, 0.9f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_05"), new Size(0.2f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_06"), new Size(0.2f, 0.6f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_07"), new Size(0.1f, 0.7f) },
            { Key("Packs/Gems/Gems.fbx", "Gems_04_08"), new Size(0.1f, 0.4f) },
        };

        public static Size SizeOf(string asset, string part)
        {
            Size s;
            return Catalog.TryGetValue(Key(asset, part), out s) ? s : new Size(1f, 1f);
        }

        /// <summary>テーマごとに必ず使う素材（FBX）。テストが「1 回以上置かれている」ことを確認する。</summary>
        public static string[] ThemeAssets(int kind)
        {
            if (kind == HeightmapGen.KIND_CANYON)
                return new[] { DROCKS, RCOLL, DPROPS, DEADTREE, ACACIA, CAMPFIRE, LOGS, HAY, FARM, TOWNBLOCK, SALOON, RSTYL, MESAB };   // 崖の板は 260m より遠い背景だけ（art-r3-05）
            if (kind == HeightmapGen.KIND_DUNES)
                return new[] { CACTI, CGROUP, DPROPS, SKELETON, TENT, ACACIA, TREES, DEADTREE, CAMPFIRE, LOGS, HAY, FARM, RANCH, DROCKS, RCOLL };
            // course-r3-12：段の付いた MesaRock は 250m より遠くだけ（境目の Terrain の中にはそんな所が無い：外の背景 Backdrop が使う）
            var m = new List<string> { MESAB, RSTYL, CGROUP, CACTI, STABLE, TOWNBLOCK, SALOON, FARM, HAY, LOGS, DEADTREE, DPROPS, TREES, ACACIA };
            foreach (char c in GrayLetters) m.Add(RockGray(c));
            return m.ToArray();
        }

        public static readonly string[] LobbyThemeAssets = { FARM, HAY, LOGS, DPROPS, CACTI, TREES, ACACIA };

        /// <summary>小物を置いてよい「まとまり」のグループ（野営地・オアシス・集落・骨の原・岩場など）。</summary>
        public static readonly HashSet<string> ClusterGroups = new HashSet<string>
        {
            "WagonCamp", "RuinedHomestead", "Oasis", "RanchWaypoint", "Settlement", "BoneField", "GoalTown", "RockField", "Rocks",
            "Scrub", "DuneGrass", "CactusForest", "Cactus", "TownEdge", "DeadGroveFloor", "DeadGroveRim", "DeadGroveFar", "SavannaGrove", "GrayRocks", "RoadIsland", "GuideTower", "Fork",
            "CanyonFloor", "GuideLine", "Railway", "Backlot", "FinishCamp", "CanyonRim", "MesaPlateau", "RimLandmark", "RanchYard", "StartTown",
            "MinersCamp", "Caravan", "WaterStop", "GoalOutpost", "RimRocks", "FarRocks", "CliffSkirt", "TreeLine",
            "MesaFloor", "RailHalt", "StreetDressing", "PlateEdge", "PassFloor", "MesaGate", "WallFace", "PassLamp", "PassBeacon",
            "Shoulder", "DuneWreck",
            "DuneFence",   // art-r5-07：砂丘の脇の低い柵（線の目印）
        };

        /// <summary>道のすぐ脇（ROAD_MIN、平らな道 ±12m の外）に意図して置くグループ。それ以外は ROAD_CLEAR より外。</summary>
        public static readonly HashSet<string> NearRoadGroups = new HashSet<string> { "Roadside", "GoalTown", "CanyonFloor", "GuideLine", "MesaGate", "MesaSaddle", "CornerSilhouette", "GoalOutpost",
            "Railway", "RanchYard", "StartTown", "RoadIsland", "NotchFrame", "BarrierLine", "Fork", "MesaFloor", "PassFloor", "StreetDressing", "RailHalt", "WallFace", "PassLamp",
            "DuneWreck", "Caravan", "DuneFence" };   // art-r4-18：砂漠の道の近くの荷車・ラクダの隊列

        public static bool IsSmall(Placement p) => p.height < LARGE_MIN_H;

        /// <summary>道・除外の円との距離を調べるときの足元の半径。崖の壁は厚さの半分（長い辺は道に沿う）、メサの門は道側の裾。</summary>
        public static float FootprintRadius(Placement p)
        {
            if (p.asset == CLIFFS) return p.radius * CLIFF_HALF_DEPTH;
            if (p.group == "MesaGate" || p.asset == MESAB || p.asset == MESA) return p.radius * MESA_GATE_FOOTPRINT;
            if (p.scaleY > 1.01f) return p.radius * STRETCHED_ROCK_FOOTPRINT;   // 縦に伸ばした岩（art-r2-08）は裾が斜面に埋まる
            return p.radius;
        }

        public static bool IsTree(Placement p) => p.asset == TREES || p.asset == ACACIA || p.asset == DEADTREE;

        // ---- art-r4-08：接地 ----
        public const float GROUND_REJECT_SPAN = 0.6f;   // 足元の高さの差がこの × 高さを超える斜面には置かない
        public const float GROUND_EXTRA_SINK = 0.1f;    // 高さの差 + 0.1m 沈める
        public const float GROUND_MAX_SINK = 0.6f;      // ただし高さの 60% まで（大岩が埋まって消えない）
        /// <summary>斜面に埋めるのが目的の岩のまとまり（崖の代わりの縦に伸ばした岩・壁の面・門・尾根の上のシルエット）。急な斜面でも置く。</summary>
        public static readonly HashSet<string> SlopeEmbedded = new HashSet<string>
        {
            "WallFace", "MesaGate", "CliffWalls", "CanyonWalls", "CliffBack", "CliffSkirt", "CornerSilhouette", "RimSilhouette", "RimLandmark", "MesaRing",
            "NotchFrame", "Landmark", "FinishLandmark", "BigRocks", "GrayRocks", "MesaSaddle", "CanyonRim", "PassFloor", "PassBeacon", "MesaPlateau", "SeamRock",
        };

        /// <summary>
        /// 斜面に置かない小物か（art-r4-08）：高さ GROUND_REJECT_MAX_H 未満の小物・岩のかけら・杭が、足元の高さの差 &gt; 0.6 × 高さの斜面に乗るとき。
        /// 線の目印（柵の線・道の目印）は途切れないことを優先して沈めるだけ（柵は土手に沿う）。背景・斜面に埋める岩も沈めるだけ。
        /// </summary>
        public static bool RejectSlope(string group, bool background, float height, float span)
        {
            if (background || SlopeEmbedded.Contains(group) || LineGroups.Contains(group) || group == "BarrierLine" || ShareExempt.Contains(group)
                || group == "MinersCamp" || group == "Caravan" || group == "WaterStop") return false;   // 形の決まった 1 か所のまとまりは沈めるだけ
            return height < GROUND_REJECT_MAX_H && span > GROUND_REJECT_SPAN * height;
        }
        public const float GROUND_REJECT_MAX_H = 3.2f;

        // ---- art-r5-03：浮かない（大きな物も）----
        /// <summary>斜面に埋めない・背景でない高さ GROUNDING_MAX_H 未満の物は、足元の差 &gt; GROUNDING_SPAN × 高さ、
        /// または足元のいちばん低い所が中心より GROUNDING_OVERHANG × 高さ低い（縁からはみ出す）なら置かない。</summary>
        public const float GROUNDING_SPAN = 0.45f, GROUNDING_OVERHANG = 0.3f, GROUNDING_MAX_H = 10f;
        /// <summary>Validator.CheckGrounding：足元のいちばん低い地面が置いた高さ（足元の底）よりこれ以上低いと浮いている。</summary>
        public const float GROUNDING_FLOAT_TOL_M = 0.25f;
        public const float TOWER_FLAT_R_M = 4f, TOWER_FLAT_SPAN_M = 0.6f, TOWER_RIM_OUT_M = 8f, TOWER_RIM_DROP_M = 2f;
        /// <summary>砂丘の上の塔（台座は砂に埋まる）：足元の差・外の落ち込みの上限をゆるめる。</summary>
        public const float TOWER_FLAT_SPAN_SAND_M = 1.2f, TOWER_RIM_DROP_SAND_M = 3.5f;

        /// <summary>
        /// art-r5-03：浮く置き方か。span = 足元 8 点の差、low = いちばん低い地面、center = 中心の地面。
        /// 斜面に埋める岩（SlopeEmbedded）・背景・高さ 10m 以上・途切れない線の目印（柵・路肩・柵の線）は沈めるだけ。
        /// </summary>
        public static bool RejectGrounding(string group, bool background, float height, float span, float low, float center)
        {
            if (background || height >= GROUNDING_MAX_H || SlopeEmbedded.Contains(group) || LineGroups.Contains(group) || group == "BarrierLine"
                || group == "DuneWreck") return false;   // 砂に半分埋めて傾けた荷車は埋めるのが目的
            return span > GROUNDING_SPAN * height || low < center - GROUNDING_OVERHANG * height;
        }

        /// <summary>柵の板（FarmProps_03/04/06/08）か：地面に沿って沈める（土手に沿う柵）。</summary>
        public static bool IsFencePart(string part) => part == "FarmProps_03" || part == "FarmProps_04" || part == "FarmProps_06" || part == "FarmProps_08";

        /// <summary>art-r5-03：塔の足元（4m で差 &lt; 0.6m、足元の 8m 外が 2m より低くない）。</summary>
        public static bool TowerSiteOk(float[,] h, float sizeM, Vector3 p, float fr) => TowerSiteOk(h, sizeM, p, fr, false);

        /// <summary>sand = 砂丘の上（砂に埋まる台座）：足元の差 TOWER_FLAT_SPAN_SAND_M・外の落ち込み TOWER_RIM_DROP_SAND_M まで。</summary>
        public static bool TowerSiteOk(float[,] h, float sizeM, Vector3 p, float fr, bool sand)
        {
            if (h == null) return true;
            float mn, mx, c;
            FootprintRange(h, sizeM, p, TOWER_FLAT_R_M, out mn, out mx, out c);
            if (mx - mn >= (sand ? TOWER_FLAT_SPAN_SAND_M : TOWER_FLAT_SPAN_M)) return false;
            float drop = sand ? TOWER_RIM_DROP_SAND_M : TOWER_RIM_DROP_M;
            float ro = Mathf.Max(fr, TOWER_FLAT_R_M) + TOWER_RIM_OUT_M;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                if (WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, p.x + Mathf.Sin(a) * ro, p.z + Mathf.Cos(a) * ro) < c - drop) return false;
            }
            return true;
        }

        /// <summary>接地を調べた足元の半径（Add が記録した値、無ければ FootprintRadius）。</summary>
        public static float GroundRadius(Placement p) => p.groundR >= 0f ? p.groundR : FootprintRadius(p);

        /// <summary>木の接地を調べる幹の半径（枝の広がりの半径 r から）。</summary>
        public static float TrunkRadius(float r) => Mathf.Min(r, 0.2f * r + 0.3f);

        /// <summary>塔の部品（風車・給水塔・鐘の塔）か。</summary>
        public static bool IsTowerPart(string part) => part == RANCH_TOWER || part == TOWN_TALL_A || part == TOWN_TALL_B;

        /// <summary>高さマップ h（sizeM 四方）の点 p の中心と半径 fr の 8 点の地面の最低・最高・中心。</summary>
        public static void FootprintRange(float[,] h, float sizeM, Vector3 p, float fr, out float mn, out float mx, out float center)
        {
            center = h == null ? 0f : WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, p.x, p.z);
            mn = center; mx = center;
            if (h == null) return;
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                float g = WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, p.x + Mathf.Sin(a) * fr, p.z + Mathf.Cos(a) * fr);
                mn = Mathf.Min(mn, g); mx = Mathf.Max(mx, g);
            }
        }

        /// <summary>
        /// art-r5-03（Validator.CheckGrounding と同じ）：置いた物の足元のいちばん低い地面が、足元の底（立てた杭は中心 − 高さ / 2）より
        /// どれだけ下か（m、+ = 浮いている）。斜面に埋める岩・背景は 0。
        /// </summary>
        public static float FloatGap(Placement p, float[,] h, float sizeM)
        {
            if (p.background || SlopeEmbedded.Contains(p.group) || h == null) return 0f;
            float mn, mx, c;
            FootprintRange(h, sizeM, p.position, GroundRadius(p), out mn, out mx, out c);
            float baseY = p.part == POST_PART && Mathf.Abs(p.rollDeg - 90f) < 1f ? p.position.y - p.height * 0.5f : p.position.y;
            return baseY - mn;
        }

        /// <summary>接地を調べる足元の半径（FootprintRadius と同じ決まりを Add の引数から）。</summary>
        public static float FootprintRadiusFor(string asset, string group, float r, float scaleY)
        {
            if (asset == CLIFFS) return r * CLIFF_HALF_DEPTH;
            if (group == "MesaGate" || asset == MESAB || asset == MESA) return r * MESA_GATE_FOOTPRINT;
            if (scaleY > 1.01f) return r * STRETCHED_ROCK_FOOTPRINT;
            return r;
        }

        // ---- 素材の偏りの上限（art-r2-04）----
        public const float KEY_SHARE_MAX = 0.08f;    // 1 つの素材（部品）はステージの飾りの 8% まで
        public const float PACK_SHARE_MAX = 0.25f;   // 1 つのパックは 25% まで（RockCollection・DesertProps・DesertRocks は部品ごとに数える）
        public const int SHARE_BASE = 600;           // 置いている途中の割合の分母の最小（最初の数個で上限にかからない）
        public const int MESA_LUSH_MAX = 60;         // 境目の緑の木（Trees・アカシア）の合計

        /// <summary>割合を数える単位：部品のあるパックは部品、モデル全体はモデル。</summary>
        public static string ShareKey(string asset, string part) => part == null ? asset : asset + "|" + part;

        /// <summary>パック全体でも 25% を数えるか（小石・砂漠の小物・砂漠の岩は部品ごとだけ）。</summary>
        public static bool PackCapped(string asset) => asset != RCOLL && asset != DPROPS && asset != DROCKS;

        /// <summary>
        /// 線の目印（art-r3-03：カーブの外側の途切れない柵）。同じ柵の板が続くのが目的なので、素材の割合（KEY/PACK_SHARE_MAX）と
        /// 小物の割合（SMALL_SHARE_MAX）の数に入れない（数えると柵が途中で間引かれて途切れる）。
        /// </summary>
        public static readonly HashSet<string> LineGroups = new HashSet<string> { "GuideLine", "Shoulder", "DuneFence" };   // art-r4-02：路肩の小石も途切れさせない

        /// <summary>割合の上限をかけない（1 か所だけの、形の決まったまとまり）。数には入る。</summary>
        public static readonly HashSet<string> ShareExempt = new HashSet<string>
        {
            "Landmark", "FinishLandmark", "GoalTown", "GoalOutpost", "MesaGate", "RailHalt", "PassFloor", "StreetDressing", "PlateEdge", "MesaSaddle", "Railway", "RimLandmark", "GiantSkeleton",
            "GuideLine", "CanyonRim", "RanchYard", "StartTown", "RoadIsland", "GuideTower", "NotchFrame", "Fork", "WallFace", "PassLamp", "PassBeacon",
            "RimSilhouette",   // art-r5-03：地平線のシルエットは割合で間引かない（大岩・メサの数が増えても地平線を空けない）
        };

        /// <summary>
        /// 最後に割合をそろえる：素材が KEY_SHARE_MAX・パックが PACK_SHARE_MAX を超えていたら、上限をかけないまとまり以外から
        /// 後に置いたものを外す（外すと分母も減るので、超えなくなるまで繰り返す）。
        /// </summary>
        static void EnforceShares(Work w)
        {
            for (int guard = 0; guard < 4000; guard++)
            {
                int n = 0;
                foreach (var q in w.list) if (!LineGroups.Contains(q.group)) n++;
                string worstKey = null; bool worstPack = false; float worst = 0f;
                foreach (var kv in w.keyCount)
                {
                    float over = kv.Value - KEY_SHARE_MAX * n;
                    if (over > worst) { worst = over; worstKey = kv.Key; worstPack = false; }
                }
                foreach (var kv in w.packCount)
                {
                    if (!PackCapped(kv.Key)) continue;
                    float over = kv.Value - PACK_SHARE_MAX * n;
                    if (over > worst) { worst = over; worstKey = kv.Key; worstPack = true; }
                }
                if (worstKey == null) return;
                bool removed = false;
                for (int i = w.list.Count - 1; i >= 0; i--)
                {
                    var p = w.list[i];
                    if (ShareExempt.Contains(p.group)) continue;
                    if (worstPack ? p.asset != worstKey : ShareKey(p.asset, p.part) != worstKey) continue;
                    w.Count(p, -1);
                    w.list.RemoveAt(i);
                    removed = true;
                    break;
                }
                if (!removed) return;
            }
        }

        /// <summary>素材（部品）ごとの数と、ステージの飾りに占める一番大きい割合（テスト・Validator 用）。</summary>
        public static float MaxShare(List<Placement> list, bool pack, out string which)
        {
            var c = new Dictionary<string, int>();
            int total = 0;
            foreach (var p in list)
            {
                if (LineGroups.Contains(p.group)) continue;   // 線の目印は数えない（art-r3-03）
                total++;
                if (pack && !PackCapped(p.asset)) continue;
                string k = pack ? p.asset : ShareKey(p.asset, p.part);
                int v; c.TryGetValue(k, out v); c[k] = v + 1;
            }
            which = null; float best = 0f;
            foreach (var kv in c)
            {
                float f = kv.Value / (float)Mathf.Max(1, total);
                if (f > best) { best = f; which = kv.Key; }
            }
            return best;
        }

        /// <summary>コース点列の累積弧長。</summary>
        public static float[] CourseArcs(Vector3[] course)
        {
            var a = new float[course.Length];
            for (int i = 1; i < course.Length; i++) a[i] = a[i - 1] + WorldLayout.DistXZ(course[i - 1], course[i]);
            return a;
        }

        /// <summary>p に一番近いコース点の弧長と距離（x/z）。</summary>
        public static float NearestArc(Vector3[] course, float[] cum, Vector3 p, out float dist)
        {
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < course.Length; i++)
            {
                float dx = course[i].x - p.x, dz = course[i].z - p.z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; bi = i; }
            }
            dist = Mathf.Sqrt(best);
            return cum[bi];
        }

        /// <summary>
        /// 道からの「幅を考えた」距離（m）：コースの中心からの距離 − (その場所の平らな道の半幅 − 12m)。
        /// 道幅 12m ではそのままの距離、絞り（7m）では 5m 長く、広場（18m）では 6m 短く数える（飾りの道からの距離の決まりを道幅に合わせる）。
        /// </summary>
        public static float RoadClearance(StageDef def, Vector3[] course, float[] cum, Vector3 p)
        {
            if (def == null || def.widthArc == null) return WorldLayout.DistToPolylineXZ(p, course);
            if (cum == null) cum = CourseArcs(course);
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < course.Length; i += 2)
            {
                float dx = course[i].x - p.x, dz = course[i].z - p.z, d = dx * dx + dz * dz;
                if (d < best) { best = d; bi = i; }
            }
            float exact = WorldLayout.DistToPolylineXZ(p, course);
            return exact - (StageDefinitions.HalfWidthAt(def, cum[bi]) - HeightmapGen.RUNWAY_M);
        }

        /// <summary>小物が全体の SMALL_SHARE_MAX を超えたら、まとまりの外の小物 → 後から置いたまとまりの小物の順に外す。</summary>
        static void TrimSmall(Work w)
        {
            var list = w.list;
            int small = 0, counted = 0;
            foreach (var p in list) if (!LineGroups.Contains(p.group)) { counted++; if (IsSmall(p)) small++; }
            for (int pass = 0; pass < 2 && small > SMALL_SHARE_MAX * counted; pass++)
                for (int i = list.Count - 1; i >= 0 && small > SMALL_SHARE_MAX * counted; i--)
                {
                    var p = list[i];
                    if (!IsSmall(p) || LineGroups.Contains(p.group)) continue;
                    if (pass == 0 && ClusterGroups.Contains(p.group)) continue;
                    w.Count(p, -1);
                    list.RemoveAt(i);
                    small--; counted--;
                }
        }

        /// <summary>この計画の外で置かれる素材（遺物・リボルバー・手配書・町・掲示板・表彰台・乗り物・ロビーの厩舎・焚き火）。</summary>
        public static readonly string[] UsedElsewhere =
        {
            "Packs/Gems/Gems.fbx", "Packs/Revolver/Revolver.fbx", "Packs/WantedPosters/WantedPosters.fbx", "Packs/WesternTown/WesternTown.fbx",
            "Props/NoticeBoard/NoticeBoard.fbx", "Props/Podium/Podium.fbx", "Horse.fbx", "Camel.fbx", "SkeletonHorse.fbx",
            STABLE, CAMPFIRE,
        };

        // ------------------------------------------------------------------
        // 計画の作業場
        // ------------------------------------------------------------------

        sealed class Work
        {
            public List<Placement> list = new List<Placement>();
            public Vector3[] course;          // null = ロビー（道なし）
            public StageDef def;              // 道幅（course-r1-10）。null = 幅 12m のまま
            public float[] cum;
            public float[,] h;
            public float sizeM;
            public float minX, maxX, minZ, maxZ;
            public List<Vector4> circles = new List<Vector4>();   // x, _, z, 半径
            public Vector3 capA, capB;                           // 近道の通路
            public float capR = -1f;
            public Vector2 rectMin, rectMax;                    // 置かない長方形（ロビーの町）
            public bool hasRect;
            public System.Random rng;
            public int budget;
            // ---- 1 つの素材に偏らない（art-r2-04）：ステージだけ ----
            public bool capShares;
            public int lushMax = int.MaxValue;              // 境目：緑の木（Trees・アカシア）の上限
            public int lush;
            public Dictionary<string, int> keyCount = new Dictionary<string, int>();
            public Dictionary<string, int> packCount = new Dictionary<string, int>();
            public int lineN;                               // 線の目印（LineGroups）の数：割合の分母に入れない

            /// <summary>この素材をもう 1 つ置くと、素材ごと（KEY_SHARE_MAX）・パックごと（PACK_SHARE_MAX）の割合を超えるか。分母は max(今の数 + 1, SHARE_BASE)。</summary>
            public bool OverShare(string group, string asset, string part)
            {
                if (lushMax < int.MaxValue && (asset == TREES || asset == ACACIA) && lush >= lushMax) return true;
                if (!capShares || ShareExempt.Contains(group)) return false;
                float n = Mathf.Max(list.Count - lineN + 1, SHARE_BASE);
                int k;
                keyCount.TryGetValue(ShareKey(asset, part), out k);
                if (k + 1 > KEY_SHARE_MAX * n) return true;
                if (PackCapped(asset))
                {
                    packCount.TryGetValue(asset, out k);
                    if (k + 1 > PACK_SHARE_MAX * n) return true;
                }
                return false;
            }

            public void Count(Placement p, int d)
            {
                if (LineGroups.Contains(p.group)) { lineN += d; return; }   // 線の目印は割合に数えない（art-r3-03）
                string key = ShareKey(p.asset, p.part);
                int k;
                keyCount.TryGetValue(key, out k); keyCount[key] = k + d;
                packCount.TryGetValue(p.asset, out k); packCount[p.asset] = k + d;
                if (p.asset == TREES || p.asset == ACACIA) lush += d;
            }

            public float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            public int Pick(int n) => rng.Next(n);
            public string Pick(string[] a) => a[rng.Next(a.Length)];

            public float Ground(float x, float z)
                => h == null ? 0f : WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, x, z);

            public float RoadDist(Vector3 p) => course == null ? float.MaxValue : RoadClearance(def, course, cum, p);

            /// <summary>rBounds ≥ 0 なら範囲・置かない長方形・円はその半径で調べる（木の枝が柵や町の縁にかかってよいとき）。重なりは r で調べる。</summary>
            public bool Allowed(Vector3 p, float r, float minRoad, bool ignoreCircles, float rBounds = -1f, float rOverlap = -1f)
            {
                float rr = r;
                if (rBounds >= 0f) r = rBounds;
                if (p.x - r < minX || p.x + r > maxX || p.z - r < minZ || p.z + r > maxZ) return false;
                if (RoadDist(p) - r < minRoad) return false;
                if (!ignoreCircles)
                    foreach (var c in circles)
                        if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + r) return false;
                if (capR > 0f && WorldLayout.DistToSegmentXZ(p, capA, capB) < capR + Mathf.Max(r, rr)) return false;   // art-r4-04：大岩の外形が近道にかからない
                if (hasRect && p.x + r > rectMin.x && p.x - r < rectMax.x && p.z + r > rectMin.y && p.z - r < rectMax.y) return false;
                r = rOverlap >= 0f ? rOverlap : rr;
                if (r >= 1.5f)
                    foreach (var q in list)
                        if (q.radius >= 1.5f && WorldLayout.DistXZ(p, q.position) < 0.75f * (r + q.radius)) return false;
                return true;
            }

            /// <summary>置けたら true。</summary>
            public bool Add(string group, string asset, string part, Vector3 p, float yaw, float scale, float sink,
                float minRoad = ROAD_CLEAR, bool trunk = false, bool background = false, float roll = 0f, bool ignoreCircles = false, float rBounds = -1f,
                float scaleY = 1f, float rOverlap = -1f, bool fixedSpot = false)
            {
                if (list.Count >= budget) return false;
                // course-r3-12：段の付いた八角の MesaRock は霧で平らになる 250m より遠くだけ。近くは形のある MesaRockB（同じくらいの外形）に替える
                if (asset == MESA && course != null && WorldLayout.DistToPolylineXZ(p, course) <= MESA_ROCK_MIN_DIST_M) asset = MESAB;
                if (OverShare(group, asset, part)) return false;
                Size s = SizeOf(asset, part);
                float r = s.r * scale, hh = s.h * scale * scaleY;
                // art-r4-08：接地。足元の中心と足元の半径の 8 点（art-r5-03）のいちばん低い所へ、高さの差 + 0.1m 以上沈める。
                // 急な斜面（差 > 0.6 × 高さ）の小物・柵は置かない（斜面から突き出す）。斜面に埋める岩のまとまり（SlopeEmbedded）と背景は置く
                float fr = FootprintRadiusFor(asset, group, r, scaleY);
                if (trunk || asset == TREES || asset == ACACIA || asset == DEADTREE) fr = TrunkRadius(r);   // art-r5-03：木は幹だけが地面に触れる
                float y = 0f, span = 0f, y0;
                // art-r5-03：浮く場所なら少しずらして（2・4m、塔は 16m まで）平らな足元を探す。線の目印・形の決まった物はずらさない
                bool tower = IsTowerPart(part) && h != null;
                bool nudge = h != null && !background && !fixedSpot && !LineGroups.Contains(group) && group != "BarrierLine" && part != RAIL_TRACK;
                int rings = fixedSpot ? 0 : (tower ? 4 : (nudge ? 2 : 0));
                bool found = false;
                Vector3 p0 = p;
                for (int ring = 0; ring <= rings && !found; ring++)
                    for (int k = 0; k < (ring == 0 ? 1 : 8) && !found; k++)
                    {
                        float off = ring * (tower ? 3f : 2f), ang = (k * 45f + ring * 22.5f) * Mathf.Deg2Rad;
                        Vector3 q = new Vector3(p0.x + Mathf.Sin(ang) * off, 0f, p0.z + Mathf.Cos(ang) * off);
                        if (!Allowed(q, r, minRoad, ignoreCircles, rBounds, rOverlap)) continue;
                        FootprintGround(q, fr, out y, out span, out y0);
                        if (RejectSlope(group, background, hh, span)) continue;
                        // art-r5-03：斜面・縁で浮く物を置かない（谷側が宙に浮く・縁から台座がはみ出す）。線路は平らにした路盤の上
                        if (part != RAIL_TRACK && !IsFencePart(part) && RejectGrounding(group, background, hh, span, y, y0)) continue;
                        if (tower && !DressingPlan.TowerSiteOk(h, sizeM, q, fr, def != null && def.kind == HeightmapGen.KIND_DUNES)) continue;
                        if ((asset == CAMEL || group == "Caravan") && h != null && SlopeAt(this, q) > CARAVAN_MAX_SLOPE_DEG) continue;   // 砂丘の面に浮くラクダ
                        p = q; found = true;
                    }
                if (!found) return false;
                sink = Mathf.Max(sink, Mathf.Min(span + GROUND_EXTRA_SINK, GROUND_MAX_SINK * hh));
                bool reach = course == null || WorldLayout.DistToPolylineXZ(p, course) - r < REACHABLE_M;
                bool col = !background && reach && hh >= COLLIDER_MIN_H;
                list.Add(new Placement
                {
                    group = group, asset = asset, part = part, position = new Vector3(p.x, y - sink, p.z),
                    yawDeg = yaw, rollDeg = roll, scale = scale, scaleY = scaleY, radius = r, height = hh, collider = col,
                    shape = !col ? SHAPE_NONE : (trunk ? SHAPE_TRUNK : (r >= 6f ? SHAPE_MESH : SHAPE_BOX)),
                    background = background, groundR = fr,
                });
                Count(list[list.Count - 1], 1);
                return true;
            }

            /// <summary>
            /// 立てた丸太の杭（art-r3-02）：WoodLogs_01 を横倒し（roll 90°）にして高さ heightM・太さ thickM の柱にする
            /// （SetPieceBuilder.UprightLog と同じ置き方、断面は CourseMarks.PostScale で正方形に近く）。骨の杭 DesertProps_03/05 の代わり。
            /// </summary>
            public bool AddPost(string group, Vector3 p, float yaw, float heightM, float thickM, float minRoad = ROAD_MIN, bool ignoreCircles = false, float maxSlope = POST_MAX_SLOPE_DEG)
            {
                if (list.Count >= budget) return false;
                float r = Mathf.Max(0.2f, thickM * 0.7f);
                if (!Allowed(p, r, minRoad, ignoreCircles)) return false;
                // art-r5-02：急な斜面（> POST_MAX_SLOPE_DEG）には杭を立てない（切り通しの 40° の壁に杭の列が登っていた）
                if (h != null && SlopeAt(this, p) > maxSlope) return false;
                float y, span;
                FootprintGround(p, r, out y, out span);   // art-r4-08：いちばん低い所へ
                Vector3 sc = CourseMarks.PostScale(heightM, thickM);
                bool reach = course == null || WorldLayout.DistToPolylineXZ(p, course) - r < REACHABLE_M;
                bool col = reach && heightM >= COLLIDER_MIN_H;
                list.Add(new Placement
                {
                    group = group, asset = LOGS, part = POST_PART, position = new Vector3(p.x, y + heightM * 0.5f - 0.15f, p.z),
                    yawDeg = yaw, rollDeg = 90f, scale = sc.x, scaleY = sc.y / sc.x, scaleZ = sc.z / sc.x, radius = r, height = heightM,
                    collider = col, shape = col ? SHAPE_BOX : SHAPE_NONE, groundR = r,
                });
                Count(list[list.Count - 1], 1);
                return true;
            }

            /// <summary>
            /// 野の灯り（art-r4-14）：立てた丸太の柱（LAMP_POST_H）の上に暖色の灯り（Placement.lamp、DressingBuilder が付ける）。
            /// L 字の柱に吊るした街灯 FarmProps_02 は野原に並ぶと絞首台に見えるので、町（GoalTown・StartTown・StreetDressing・ロビー）だけに使う。
            /// </summary>
            public bool AddLampPost(string group, Vector3 p, float yaw, float minRoad = ROAD_MIN, bool ignoreCircles = false)
            {
                if (!AddPost(group, p, yaw, LAMP_POST_H, LAMP_POST_THICK, minRoad, ignoreCircles, 90f)) return false;   // 灯りは道の縁の目印：傾きでは間引かない
                list[list.Count - 1].lamp = true;
                return true;
            }

            /// <summary>足元（中心と半径 fr の 8 点、art-r5-03：前は 0.7 × fr の 4 点で縁を見落とした）のいちばん低い地面と、高い所との差。</summary>
            public void FootprintGround(Vector3 p, float fr, out float low, out float span)
            {
                float c;
                FootprintGround(p, fr, out low, out span, out c);
            }

            /// <summary>center = 足元の中心の地面の高さ。</summary>
            public void FootprintGround(Vector3 p, float fr, out float low, out float span, out float center)
            {
                float mn, mx;
                FootprintRange(h, sizeM, p, fr, out mn, out mx, out center);
                low = mn; span = mx - mn;
            }

            /// <summary>
            /// art-r5-03：塔（風車・給水塔・鐘の塔）は足元 4m の地面の差 &lt; TOWER_FLAT_SPAN_M、かつ足元の 8m 外の地面が TOWER_RIM_DROP_M より低くない
            /// （縁に立って台座が宙にはみ出さない）。
            /// </summary>

            public Vector3 Around(Vector3 c, float radius)
            {
                float a = Rand(0f, Mathf.PI * 2f), d = radius * Mathf.Sqrt((float)rng.NextDouble());
                return new Vector3(c.x + Mathf.Sin(a) * d, 0f, c.z + Mathf.Cos(a) * d);
            }

            /// <summary>コースの弧長 arc の横 lateral m（+ = 右）。</summary>
            public Vector3 Side(float arc, float lateral)
            {
                Vector3 t = WorldLayout.TangentAtArc(course, arc);
                Vector3 p = WorldLayout.PointAtArc(course, arc);
                Vector3 r = WorldLayout.RightOf(t);
                return new Vector3(p.x + r.x * lateral, 0f, p.z + r.z * lateral);
            }

            public float YawAt(float arc) => WorldLayout.YawDeg(WorldLayout.TangentAtArc(course, arc));

            /// <summary>その弧長での平らな道の半幅 − 12m（道沿いの飾りの横の距離に足す）。</summary>
            public float Extra(float arc) => def == null ? 0f : StageDefinitions.HalfWidthAt(def, arc) - HeightmapGen.RUNWAY_M;

            /// <summary>まとまりの中心：コースから minD〜maxD m、中心どうし spacing m 以上、除外範囲の外。</summary>
            public List<Vector3> Centers(int seed, int n, float minD, float maxD, float spacing)
            {
                var pts = WorldLayout.ScatterNearCourse(seed, n * 3, sizeM, 40f, course, minD, maxD, spacing);
                var r = new List<Vector3>();
                foreach (var p in pts)
                {
                    if (r.Count >= n) break;
                    if (Allowed(p, 4f, ROAD_CLEAR, false)) r.Add(p);
                }
                return r;
            }
        }

        // ------------------------------------------------------------------
        // ステージ
        // ------------------------------------------------------------------

        /// <summary>
        /// ステージの飾りの計画。courseLocal は Terrain ローカルのコース点列、heights は見どころを掘った後の高さ、
        /// shortcut は崖下りの近道（無ければ null）。決まった乱数なので同じ入力なら同じ結果。
        /// </summary>
        public static List<Placement> For(StageDef def, Vector3[] courseLocal, float[,] heights, float goalArc, SetPieces.ShortcutPlan shortcut)
        {
            var w = new Work
            {
                course = courseLocal, def = def, cum = CourseArcs(courseLocal), h = heights, sizeM = StageDefinitions.SIZE, budget = MAX_PER_STAGE,
                minX = EDGE_MARGIN, minZ = EDGE_MARGIN, maxX = StageDefinitions.SIZE - EDGE_MARGIN, maxZ = StageDefinitions.SIZE - EDGE_MARGIN,
                rng = new System.Random(def.seed * 7919 + 17),
                capShares = true, lushMax = def.kind == HeightmapGen.KIND_MESA ? MESA_LUSH_MAX : int.MaxValue,
            };
            foreach (var c in StageExclusions(def, courseLocal, goalArc)) w.circles.Add(c);
            if (shortcut != null)
            {
                w.capA = shortcut.a; w.capB = shortcut.b;
                w.capR = SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND + 4f;
            }
            if (def.kind == HeightmapGen.KIND_MESA) GoalTown(w, goalArc);   // 町の建物を先に（道沿いの固定の場所）
            else { GoalOutpost(w, def, goalArc); FinishCamp(w, def, goalArc); }   // 荒野・砂漠：ゴールの先の出張所（art-r2-06）と最後の直線の先の目印
            Opening(w, def, goalArc);         // 序盤の見どころ（course-r2-04）
            NotchFrame(w, def, goalArc);      // ゴールの先の切れ込みの額縁と次のステージの目印（course-r2-05）
            Landmark(w, def, goalArc);        // 遠くの目印（スタートから見える）
            if (def.kind == HeightmapGen.KIND_DUNES) GiantSkeletons(w, def, goalArc);   // 砂丘の上の巨大な骨（art-r2-16）
            CornerSilhouettes(w, def, goalArc);   // 急カーブの外側の崖（先を隠す）
            if (def.kind == HeightmapGen.KIND_MESA)
            {
                MesaGate(w, def, goalArc); MesaPlateau(w, def, goalArc); FinalApproachRail(w, def, goalArc);
                foreach (var p in w.list) if (p.group == "WallFace") { p.collider = false; p.shape = SHAPE_NONE; }   // course-r3-03：当たりは柵が受け持つ
            }
            if (def.kind == HeightmapGen.KIND_CANYON) { RimLandmarks(w, def, goalArc); CanyonCrestCaps(w, def, goalArc); CanyonRimFences(w, def, goalArc); }
            if (def.kind == HeightmapGen.KIND_DUNES) Railway(w, def, goalArc);
            RoadIslands(w, def, goalArc);     // S 字の真ん中の岩の島（course-r2-03）
            Fork(w, def, goalArc);            // 広場の二股（course-r2-13）
            GuideTowers(w, def, goalArc);     // 先へ誘う塔（course-r2-11、急カーブの出口の「ここを見ろ」= course-r2-08）
            GuideLine(w, def, goalArc);       // 道の目印（カーブの外側の杭・柵、直線の標柱）
            Shoulder(w, def, goalArc);        // 路肩の小石・草（art-r4-02：平らな色の面を割る）
            if (def.kind == HeightmapGen.KIND_DUNES) DuneFence(w, def, goalArc);   // art-r5-07：砂丘の面の脇の低い柵
            Vignettes(w, def, goalArc);       // 道から 30〜60m の人の大きさの見どころ（art-r2-05）
            if (def.kind == HeightmapGen.KIND_CANYON) Canyon(w, def, goalArc);
            else if (def.kind == HeightmapGen.KIND_DUNES) Dunes(w, def, goalArc);
            else Border(w, def, goalArc);
            Roadside(w, goalArc);
            RimSilhouettes(w, def, goalArc);  // 外周の尾根の上のシルエット（art-r2-09）
            RimRing(w, def, goalArc);         // 空いた外周の輪の背景（course-r2-10）
            ThinFarPebbles(w);                // art-r5-15：道から 60m より遠い小さな岩を半分に
            CornerClusters(w, def, goalArc);  // art-r5-15：その分で急カーブの外側に 6〜10m の岩のまとまり
            for (int pass = 0; pass < 4; pass++) { EnforceShares(w); TrimSmall(w); }
            return w.list;
        }

        // ------------------------------------------------------------------
        // コース構成 round 2（course-r2-03/04/05/06/08/10/11）
        // ------------------------------------------------------------------

        /// <summary>
        /// 序盤の見どころ（course-r2-04）。荒野 = 牧場の庭（門の絞りの両脇に RanchCompound_07/09、庭に RanchCompound_04〜06 と柵、出口に風車の塔）、
        /// 境目 = 町の通り（最初の 250m の両脇 ±16m に建物、掲示板・干し草、町を出る所に Cliffs_01 ×0.6 の門）。砂漠は踏切（Railway）。
        /// </summary>
        static void Opening(Work w, StageDef def, float goalArc)
        {
            if (def.kind == HeightmapGen.KIND_DUNES) RailHalt(w, def);   // 砂漠のスタートの停車場（art-r3-11）
            float a0 = SetPieces.OpeningArc(def, goalArc);
            if (a0 < 0f) return;
            if (def.kind == HeightmapGen.KIND_CANYON) RanchYard(w, def, a0);
            else if (def.kind == HeightmapGen.KIND_MESA) StartTown(w, def, goalArc);
        }

        // ---- art-r3-11：砂漠のスタートの停車場 ----
        public const float RAIL_HALT_FROM_M = 20f, RAIL_HALT_TO_M = 45f;   // スタートラインからの横の距離

        /// <summary>
        /// 砂漠のスタートの停車場（art-r3-11）：スタートラインの両脇 20〜45m に、左 = 線路（WesternTown_049_Track、道に平行）と給水塔（WesternTown_084 ×1.2）、
        /// 干し草の山と木箱、右 = テント 2 張りと街灯（FarmProps_02、暖色の灯り）につないだラクダ、干し草。最初の 1 枚目からステージの「場所」が分かる。
        /// </summary>
        static void RailHalt(Work w, StageDef def)
        {
            const string G = "RailHalt";
            float s = StageDefinitions.START_ARC_M;
            float yaw = w.YawAt(s + 20f);
            float hw = StageDefinitions.HalfWidthAt(def, s);
            float ex = hw - HeightmapGen.RUNWAY_M;
            // 左：線路（低い・当たり判定なし）と給水塔、干し草と木箱
            if (w.Add(G, WTOWN, RAIL_TRACK, w.Side(s + 30f, -(RAIL_HALT_FROM_M + 4f + ex)), yaw, 1f, 0.15f, ROAD_MIN, ignoreCircles: true, rBounds: 2f, rOverlap: 0.5f))
            {
                w.list[w.list.Count - 1].collider = false;
                w.list[w.list.Count - 1].shape = SHAPE_NONE;
            }
            // art-r5-03：給水塔は平らな足元を探す（砂丘のうねりの縁に立てない）
            bool tower = false;
            for (int ti = 0; ti < 32 && !tower; ti++)
            {
                const float side = -1f;
                int tj = ti % 32;
                tower = w.Add(G, WTOWN, TOWN_TALL_A, w.Side(s + 36f + (tj % 8) * 7f, side * (RAIL_HALT_TO_M - 4f + ex + (tj / 8) * 7f)), yaw + 90f, 1.2f, 0.3f, ROAD_MIN, ignoreCircles: true, rOverlap: 0.3f);
            }
            for (int i = 0; i < 3; i++)
                w.Add(G, HAY, i == 0 ? "HayBales_01" : HayParts[1 + i], w.Side(s + 30f + i * 1.8f, -(RAIL_HALT_FROM_M + ex + (i % 2) * 1.2f)), yaw + i * 35f, 1f, 0.05f, ROAD_MIN);
            for (int i = 0; i < 3; i++)
                w.Add(G, FARM, FarmCrates[i % FarmCrates.Length], w.Side(s + 38f + i * 1.4f, -(RAIL_HALT_FROM_M + 1.5f + ex)), yaw + i * 20f, 1f, 0.02f, ROAD_MIN);
            w.Add(G, WTOWN, TOWN_BARRELS, w.Side(s + 43f, -(RAIL_HALT_FROM_M + 2f + ex)), yaw, 1f, 0.02f, ROAD_MIN);
            // 右：テント 2 張り・街灯につないだラクダ・干し草
            w.Add(G, TENT, null, w.Side(s + 24f, RAIL_HALT_FROM_M + 8f + ex), yaw - 90f, 1f, 0.1f, ROAD_MIN, ignoreCircles: true, rOverlap: 0.5f);
            w.Add(G, TENT, null, w.Side(s + 40f, RAIL_HALT_TO_M - 9f + ex), yaw - 110f, 1f, 0.1f, ROAD_MIN, ignoreCircles: true, rOverlap: 0.5f);
            w.AddLampPost(G, w.Side(s + 14f, RAIL_HALT_FROM_M + ex), yaw, ROAD_MIN, ignoreCircles: true);   // art-r4-14
            w.Add(G, CAMEL, null, w.Side(s + 15.5f, RAIL_HALT_FROM_M + 2.4f + ex), yaw + 80f, 1f, 0.02f, ROAD_MIN, ignoreCircles: true);
            w.Add(G, HAY, "HayBales_01", w.Side(s + 32f, RAIL_HALT_FROM_M + 1f + ex), yaw + 15f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
            w.Add(G, HAY, HayParts[4], w.Side(s + 33.5f, RAIL_HALT_FROM_M + 2.2f + ex), yaw - 20f, 1f, 0.02f, ROAD_MIN, ignoreCircles: true);
        }

        public const float RANCH_YARD_HALF_M = 90f;          // 庭は門の前後 ±90m
        public const float RANCH_GATE_GAP_M = 2f;            // 門の建物と道の縁のすき間
        public const string RANCH_PROP_A = "RanchCompound_04_Prop", RANCH_PROP_B = "RanchCompound_05_Prop", RANCH_PROP_C = "RanchCompound_06_Prop";

        static void RanchYard(Work w, StageDef def, float gate)
        {
            const string G = "RanchYard";
            float hw = StageDefinitions.HalfWidthAt(def, gate), yaw = w.YawAt(gate);
            // 門：道の両脇に母屋と納屋（道の縁から 2m）
            string[] gateParts = { "RanchCompound_07_Building", "RanchCompound_09_Building" };
            for (int sd = -1; sd <= 1; sd += 2)
            {
                string part = gateParts[sd < 0 ? 0 : 1];
                float r = SizeOf(RANCH, part).r;
                w.Add(G, RANCH, part, w.Side(gate, sd * (hw + RANCH_GATE_GAP_M + r)), yaw + (sd < 0 ? 90f : -90f), 1f, 0.3f, ROAD_MIN, rOverlap: 0.5f);
            }
            // 庭の柵（道の縁 + 3m、6m ごと、門の前後 25m は空ける）と小屋・水桶
            int k = 0;
            for (float a = gate - RANCH_YARD_HALF_M; a <= gate + RANCH_YARD_HALF_M; a += GUIDE_STEP_M, k++)
            {
                if (Mathf.Abs(a - gate) < 25f) continue;
                for (int sd = -1; sd <= 1; sd += 2)
                    w.Add(G, FARM, k % 2 == 0 ? "FarmProps_03" : "FarmProps_04", w.Side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + 3f)), w.YawAt(a), 1f, 0.02f, ROAD_MIN);
            }
            string[] props = { RANCH_PROP_A, RANCH_PROP_B, RANCH_PROP_C, RANCH_PROP_B };
            for (int i = 0; i < props.Length; i++)
            {
                float a = gate + (i < 2 ? -1f : 1f) * (40f + 22f * (i % 2));
                float sd = i % 2 == 0 ? 1f : -1f;
                float r = SizeOf(RANCH, props[i]).r;
                w.Add(G, RANCH, props[i], w.Side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + 8f + r)), w.YawAt(a) + 90f * i, 1f, 0.1f, ROAD_MIN);
            }
            // 出口の目印（art-r4-15：風車の塔は荒野の遠くの目印 Landmark の 1 本だけ。庭の出口は干し草の山と荷車）
            float ex = gate + RANCH_YARD_HALF_M + 40f;
            for (int t = 0; t < 10; t++)
                if (w.Add(G, WTOWN, TOWN_CART, w.Side(ex + t * 10f, (t % 2 == 0 ? 1f : -1f) * (StageDefinitions.HalfWidthAt(def, ex) + 6f + t * 2f)), w.YawAt(ex) + 30f, 1f, 0.05f, ROAD_MIN))
                {
                    Vector3 hp = w.list[w.list.Count - 1].position;
                    for (int i = 0; i < 3; i++) w.Add(G, HAY, i == 0 ? "HayBales_01" : HayParts[i + 1], new Vector3(hp.x + 2.5f + i * 1.1f, 0f, hp.z + (i % 2) * 1.2f), i * 40f, 1f, 0.05f, ROAD_MIN);
                    break;
                }
        }

        public const float START_TOWN_LEN_M = 250f, START_TOWN_SIDE_M = 16f, START_TOWN_STEP_M = 22f;

        /// <summary>境目のスタートの町（course-r2-04）：最初の 250m の両脇 ±16m（建物の正面）に建物、掲示板と干し草、町の出口に崖の門。</summary>
        static void StartTown(Work w, StageDef def, float goalArc)
        {
            const string G = "StartTown";
            int k = 0;
            for (float a = 40f; a <= START_TOWN_LEN_M; a += START_TOWN_STEP_M, k++)
            {
                float yaw = w.YawAt(a);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    int v = (k * 2 + (sd > 0 ? 1 : 0)) % 6;
                    string asset = v == 0 ? TOWNBLOCK : (v == 3 ? SALOON : WTOWN);
                    string part = asset == WTOWN ? StreetBuildings[(k + (sd > 0 ? 3 : 0)) % StreetBuildings.Length] : null;
                    float r = SizeOf(asset, part).r;
                    Vector3 p = w.Side(a, sd * (START_TOWN_SIDE_M + w.Extra(a) + r * 0.8f));
                    float face = yaw + (sd < 0 ? 90f : -90f);
                    w.FootprintGround(p, r, out float _, out float span);
                    if (span > TOWN_MAX_SPAN_M) p = w.Side(a, sd * (START_TOWN_SIDE_M + w.Extra(a) + r * 0.6f));   // art-r4-16：斜面なら道へ寄せて、それでも急なら建てない
                    w.FootprintGround(p, r, out float _, out span);
                    if (span > TOWN_MAX_SPAN_M) continue;
                    w.Add(G, asset, part, p, FacadeYaw(asset, part, face), 1f, asset == WTOWN ? 0.2f : 0.3f, ROAD_MIN, rOverlap: 0.5f);
                }
                // course-r3-09：スタートの 200m は半幅 16m（道の縁の外に置く）
                if (k % 2 == 1) w.Add(G, HAY, "HayBales_01", w.Side(a + 9f, (k % 4 == 1 ? 1f : -1f) * (StageDefinitions.HalfWidthAt(def, a + 9f) + 1.5f)), yaw, 1f, 0.05f, ROAD_MIN);
                if (k % 3 == 2) w.Add(G, FARM, FARM_LAMP, w.Side(a + 11f, (k % 2 == 0 ? 1f : -1f) * (StageDefinitions.HalfWidthAt(def, a + 11f) + 1.5f)), yaw, 1f, 0.05f, ROAD_MIN);
            }
            w.Add(G, NOTICE, null, w.Side(70f, -(StageDefinitions.HalfWidthAt(def, 70f) + 2.5f)), w.YawAt(70f) + 90f, 1f, 0.05f, ROAD_MIN);
            // 町の出口の岩の門：道の両脇に縦に伸ばした大岩（道のすぐ脇の崖の板は平らな箱に見えるので形のある岩：art-r2-08）
            float ex = START_TOWN_LEN_M + 30f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                string part = sd < 0 ? "DesertRocks_01" : "DesertRocks_02";
                const float sc = 0.6f;
                float r = SizeOf(DROCKS, part).r * sc;
                for (int t = 0; t < 4; t++)
                    if (w.Add(G, DROCKS, part, w.Side(ex + t * 6f, sd * (HeightmapGen.RUNWAY_M + 6f + r * 0.8f + t * 3f)), w.YawAt(ex) + (sd < 0 ? 0f : 180f), sc,
                        1f, ROAD_MIN, rOverlap: 0.5f, scaleY: 1.4f))
                        break;
            }
        }

        // ---- course-r2-03：S 字の真ん中の岩の島 ----
        public const float ISLAND_MIN_R = 70f, ISLAND_MAX_R = 220f;    // この半径の曲がり（急カーブでも直線でもない S 字）
        public const float ISLAND_LATERAL = 4f;        // 島の中心は道の中心から内側へ 4m（内側 = 短いが狭い線、外側 = 広い線）
        public const float ISLAND_ROCK_R = 2.6f;       // 島の岩の足元の半径
        public const float ISLAND_SPACING_M = 300f;

        /// <summary>岩の島の弧長と横（+ = 右）：全力の合図より手前、スタートから 300m 以上、CP・見どころ・急カーブ・絞りから離れた S 字の曲がりの頂点。</summary>
        public static List<Vector2> IslandSpots(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector2>();
            float dash = StageDefinitions.DashArc(def, goalArc);
            var avoid = new List<float>();
            avoid.Add(SetPieces.BridgeArc(def, goalArc)); avoid.Add(SetPieces.JumpArc(def, goalArc)); avoid.Add(SetPieces.PassArc(def, goalArc));
            avoid.Add(SetPieces.OpeningArc(def, goalArc));
            var corners = StageDefinitions.Corners(course, StageDefinitions.MEDIUM_CORNER_MAX_R, goalArc);
            float last = -1000f;
            for (int i = 0; i < course.Length; i++)
            {
                float a = i * StageDefinitions.SAMPLE_SPACING_M;
                if (a < 300f || a > dash - 60f || a - last < ISLAND_SPACING_M) continue;
                float rad = StageDefinitions.RadiusAt(course, i);
                if (rad < ISLAND_MIN_R || rad > ISLAND_MAX_R) continue;
                // 曲がりの頂点（前後 10m より半径が小さい）
                if (rad > StageDefinitions.RadiusAt(course, Mathf.Max(0, i - 5)) || rad > StageDefinitions.RadiusAt(course, Mathf.Min(course.Length - 1, i + 5))) continue;
                bool ok = StageDefinitions.HalfWidthAt(def, a) >= HeightmapGen.RUNWAY_M - 0.01f && StageDefinitions.HalfWidthAt(def, a) <= HeightmapGen.RUNWAY_M + 0.01f;
                foreach (float v in avoid) if (v >= 0f && Mathf.Abs(v - a) < 140f) ok = false;
                foreach (var c in corners) if (a > c.fromArc - 60f && a < c.toArc + 60f) ok = false;
                foreach (float cp in WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M)) if (Mathf.Abs(cp - a) < 12f) ok = false;
                if (def.sandTrailArcs != null && StageDefinitions.InDeepSand(def, a)) ok = false;
                if (!ok) continue;
                int side = StageDefinitions.TurnSide(course, i);
                r.Add(new Vector2(a, side * ISLAND_LATERAL));   // 内側（右へ曲がるなら右）
                last = a;
            }
            return r;
        }

        static void RoadIslands(Work w, StageDef def, float goalArc)
        {
            const string G = "RoadIsland";
            foreach (var s in IslandSpots(def, w.course, goalArc))
            {
                Vector3 c = w.Side(s.x, s.y);
                float yaw = w.YawAt(s.x);
                // art-r5-09：島はどのステージも形のある大岩（境目の小さく縮めたメサ B は道の真ん中の切り株に見えた）、中心は道の中心から 4m（ISLAND_LATERAL）
                {
                    string part = RockBig[(int)(s.x / 7f) % RockBig.Length];
                    float sc = ISLAND_ROCK_R / SizeOf(DROCKS, part).r;
                    w.Add(G, DROCKS, part, c, yaw + s.x, sc, 0.1f, -1000f, ignoreCircles: true, rOverlap: 0.5f, fixedSpot: true);
                }
                // 島の前後に干し草の対（art-r5-09：島を額縁にして、道の分かれ目として読ませる）
                for (int e = -1; e <= 1; e += 2)
                    w.Add(G, HAY, "HayBales_01", w.Side(s.x + e * 3.8f, s.y), yaw + e * 40f, 1f, 0.05f, -1000f, ignoreCircles: true, rOverlap: 0.2f, fixedSpot: true);
            }
            // 島は乗り越えられない岩（Block の当たり）
            foreach (var p in w.list)
                if (p.group == G && p.height >= COLLIDER_MIN_H) { p.collider = true; p.shape = SHAPE_BOX; }
        }

        // ---- art-r4-02：路肩（道の縁の外 1〜5m の小石・草） ----
        public const float SHOULDER_STEP_M = 5f, SHOULDER_LAT_MIN = 1f, SHOULDER_LAT_MAX = 9f;   // art-r5-13：5 → 9m（2 本目の帯 6〜9m）
        /// <summary>art-r5-13：2 本目の帯（道の縁の外 6〜9m、4m ごと）を置く区間（切り通し・急カーブ・近道の入口）の草と小石。</summary>
        public const float SHOULDER_BAND2_MIN = 6f, SHOULDER_BAND2_STEP_M = 4f;
        /// <summary>art-r5-13：近景の密度（100m ごとの道から 30m 以内の数）を上げる区間（境目の切り通し、荒野・砂漠の急カーブ）。</summary>
        public const int NEAR_PER_100M_DENSE = 16;
        /// <summary>art-r5-07：砂漠の道の縁の点線（4.5〜5m、2 つおき）。</summary>
        public const float DUNE_EDGE_DOT_MIN = 4.5f, DUNE_EDGE_DOT_MAX = 5f;
        public const float SHOULDER_PEBBLE_MIN = 0.6f, SHOULDER_PEBBLE_MAX = 1.0f;
        static readonly string[] ShoulderPebbles = Every8From("RockCollection", 104, 184);
        static readonly string[] ShoulderGrass = { "DesertProps_22", "DesertProps_23" };

        static string[] Every8From(string pack, int from, int max)
        {
            var r = new List<string>();
            for (int i = from; i <= max; i += 8) r.Add(pack + "_" + i.ToString("00"));
            return r.ToArray();
        }

        /// <summary>路肩を空ける弧長（スタート・ゴールの門、岩の橋、踏み切り、近道の入口）か。</summary>
        static bool ShoulderSkip(StageDef def, float goalArc, float a)
        {
            if (a < StageDefinitions.START_ARC_M + 12f || a > goalArc - 6f) return true;
            float br = SetPieces.BridgeArc(def, goalArc);
            if (br >= 0f && Mathf.Abs(a - br) < SetPieces.TRENCH_HALF_WIDTH + SetPieces.TRENCH_BLEND + 6f) return true;
            float jp = SetPieces.JumpArc(def, goalArc);
            if (jp >= 0f && a > jp - SetPieces.JUMP_LENGTH - 6f && a < jp + 30f) return true;
            return false;
        }

        /// <summary>
        /// 路肩（art-r4-02）：5m ごとに左右交互、道の縁の外 1〜5m に、3 つのうち 2 つは小石の塊（RockCollection_104〜184 ×0.6〜1.0）、
        /// 3 つ目は草（DesertProps_22/23 ×0.7）か小さなサボテン（Cacti_09 ×0.5）。当たりなし（高さ 1.2m 未満）。
        /// 平らな色の地面に小さな物の粒を散らし、道の縁を読ませる（Sable・Walkabout の「平らな色＋小さな物」）。割合の数には入れない（LineGroups）。
        /// </summary>
        static void Shoulder(Work w, StageDef def, float goalArc)
        {
            const string G = "Shoulder";
            int k = 0;
            for (float a = 0f; a < goalArc; a += SHOULDER_STEP_M, k++)
            {
                if (ShoulderSkip(def, goalArc, a)) continue;
                float hw = StageDefinitions.HalfWidthAt(def, a);
                bool ok = false;
                for (int tries = 0; tries < 4 && !ok; tries++)   // 札・門の円や斜面に当たったら反対側・別の横の距離で
                {
                float sd = (k + tries) % 2 == 0 ? 1f : -1f;
                float lat = hw + w.Rand(SHOULDER_LAT_MIN, SHOULDER_BAND2_MIN - 1f);
                Vector3 p = w.Side(a + w.Rand(-1.5f, 1.5f), sd * lat);
                if (k % 3 != 2) ok = w.Add(G, RCOLL, w.Pick(ShoulderPebbles), p, w.Rand(0f, 360f), w.Rand(SHOULDER_PEBBLE_MIN, SHOULDER_PEBBLE_MAX), 0.05f,
                    HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f);
                else if (def.kind == HeightmapGen.KIND_CANYON || k % 2 == 0)
                    ok = w.Add(G, DPROPS, w.Pick(ShoulderGrass), p, w.Rand(0f, 360f), 0.7f, 0.05f, HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f);
                else ok = w.Add(G, CACTI, "Cacti_09", p, w.Rand(0f, 360f), 0.5f, 0.05f, HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f);
                }
                if (ok) { var q = w.list[w.list.Count - 1]; q.collider = false; q.shape = SHAPE_NONE; }
                // art-r5-07：砂漠は両側の道の縁に点線（2 つおきに小石の塊、4.5〜5m：砂と同じ明るさの道の縁を読ませる）
                if (def.kind == HeightmapGen.KIND_DUNES && k % 2 == 0)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                        if (w.Add(G, RCOLL, ShoulderPebbles[(k / 2 + s2 + 2) % ShoulderPebbles.Length], w.Side(a, s2 * (hw + w.Rand(DUNE_EDGE_DOT_MIN, DUNE_EDGE_DOT_MAX))), w.Rand(0f, 360f),
                                SHOULDER_PEBBLE_MAX, 0.05f, HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f))
                        { var q = w.list[w.list.Count - 1]; q.collider = false; q.shape = SHAPE_NONE; }
            }
            // art-r5-13：2 本目の帯（6〜9m、4m ごと、左右）：切り通し・急カーブ・近道の入口。騎手の目の高さの手前 10m に細かい物を
            var corners = StageDefinitions.TightCorners(def, w.course);
            float pass = SetPieces.PassArc(def, goalArc);
            float sca = ShortcutEntryArc(w);
            int j = 0;
            for (float a = 0f; a < goalArc; a += SHOULDER_BAND2_STEP_M, j++)
            {
                if (ShoulderSkip(def, goalArc, a) || !DenseNearArc(def, corners, pass, sca, a)) continue;
                float hw = StageDefinitions.HalfWidthAt(def, a);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 p = w.Side(a + w.Rand(-1.2f, 1.2f), sd * (hw + w.Rand(SHOULDER_BAND2_MIN, SHOULDER_LAT_MAX)));
                    bool ok = (j + sd) % 2 == 0
                        ? w.Add(G, DPROPS, ShoulderGrass[(j / 2) % ShoulderGrass.Length], p, w.Rand(0f, 360f), w.Rand(0.6f, 0.9f), 0.05f, HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f)
                        : w.Add(G, RCOLL, w.Pick(ShoulderPebbles), p, w.Rand(0f, 360f), w.Rand(SHOULDER_PEBBLE_MIN, SHOULDER_PEBBLE_MAX), 0.05f, HeightmapGen.RUNWAY_M + 0.3f, rOverlap: 0f);
                    if (ok) { var q = w.list[w.list.Count - 1]; q.collider = false; q.shape = SHAPE_NONE; }
                }
            }
        }

        // ---- art-r5-07：砂丘の脇の低い柵 ----
        /// <summary>道の縁から DUNE_FENCE_OUT_M の所に、道の縁の外 DUNE_FENCE_PROBE_M の地面が道より DUNE_FENCE_RISE_M 以上高い（砂丘の面が道に迫る）区間だけ。</summary>
        public const float DUNE_FENCE_OUT_M = 4f, DUNE_FENCE_PROBE_M = 9f, DUNE_FENCE_RISE_M = 1.5f;

        /// <summary>
        /// 砂漠の低い柵（art-r5-07）：道の脇に砂丘の面が迫る直線の区間（カーブの柵 GuideFenceRuns の外）に、道の縁 + 3m の線で FarmProps_03 を端と端でつなぐ。
        /// 前は柵の線が砂丘を登っていき、騎手から道の縁が分からなかった（明るい道の色と合わせて、道の縁を線で読ませる）。
        /// </summary>
        static void DuneFence(Work w, StageDef def, float goalArc)
        {
            const string G = "DuneFence";
            var runs = GuideFenceRuns(def, w.course, goalArc);
            int piece = 0;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                bool have = false;
                Vector3 joint = Vector3.zero;
                for (float a = StageDefinitions.START_ARC_M + 40f; a < goalArc - 40f; a += 1f)
                {
                    bool skip = ShoulderSkip(def, goalArc, a);
                    foreach (var run in runs) if (a >= run.fromArc - 10f && a <= run.toArc + 10f) skip = true;
                    float hw = StageDefinitions.HalfWidthAt(def, a);
                    Vector3 c = WorldLayout.PointAtArc(w.course, a), pr = w.Side(a, sd * (hw + DUNE_FENCE_PROBE_M));
                    bool rise = w.Ground(pr.x, pr.z) - w.Ground(c.x, c.z) >= DUNE_FENCE_RISE_M;
                    Vector3 q = w.Side(a, sd * (hw + DUNE_FENCE_OUT_M));
                    if (skip || !rise) { have = false; continue; }
                    if (!have) { joint = q; have = true; continue; }
                    if (WorldLayout.DistXZ(joint, q) < RAIL_LEN) continue;
                    Vector3 d = new Vector3(q.x - joint.x, 0f, q.z - joint.z).normalized;
                    Vector3 j2 = joint + d * RAIL_LEN;
                    if (w.Add(G, FARM, "FarmProps_03", (joint + j2) * 0.5f, WorldLayout.YawDeg(d) + 90f, 1f, 0.05f, ROAD_MIN, rOverlap: 0f, fixedSpot: true))
                    { var p = w.list[w.list.Count - 1]; p.collider = false; p.shape = SHAPE_NONE; piece++; }
                    joint = j2;
                }
            }
        }

        /// <summary>art-r5-13：近景を濃くする弧長か（境目の切り通し ±(PASS_HALF_CUT + 25m)、急カーブの前後 20m、近道の入口 ±40m）。</summary>
        public static bool DenseNearArc(StageDef def, List<StageDefinitions.Corner> corners, float pass, float shortcutArc, float a)
        {
            if (pass >= 0f && Mathf.Abs(a - pass) < SetPieces.PASS_HALF_CUT + 25f) return true;
            if (shortcutArc >= 0f && Mathf.Abs(a - shortcutArc) < 40f) return true;
            foreach (var c in corners) if (a >= c.fromArc - 20f && a <= c.toArc + 20f) return true;
            return false;
        }

        /// <summary>近道の入口の弧長（無ければ −1）：近道の通路の始まり（capA）にいちばん近いコースの弧長。</summary>
        static float ShortcutEntryArc(Work w)
        {
            if (w.capR <= 0f) return -1f;
            float d;
            return NearestArc(w.course, w.cum, w.capA, out d);
        }

        // ---- course-r2-13：広場の二股（同じ長さの 2 本の線） ----
        public const float FORK_HALF_LEN_M = 40f;      // 分かれ目の岩の列の長さの半分
        public const float FORK_LATERAL = -7f;         // 岩の列の中心線からの横（左寄り）：左 = 狭くて固い速い線、右 = 広いが砂（追い抜き・後ろにつく所）
        public const float FORK_STEP_M = 5f;
        public const float FORK_ROCK_R = 1.6f;

        /// <summary>二股の分かれ目の岩の列の点（弧長, 横）。広場（半幅 WIDE_HALF_M）の中だけ。無ければ空。</summary>
        public static List<Vector2> ForkDivider(StageDef def, float goalArc)
        {
            var r = new List<Vector2>();
            if (def.forkFraction < 0f) return r;
            float c = def.forkFraction * goalArc;
            for (float a = c - FORK_HALF_LEN_M; a <= c + FORK_HALF_LEN_M + 0.01f; a += FORK_STEP_M) r.Add(new Vector2(a, FORK_LATERAL));
            return r;
        }

        /// <summary>二股の右の線（広いが砂）の中心の横（m）：岩の列と右の道の縁の真ん中。</summary>
        public static float ForkWideLaneLateral(StageDef def, float arc) => 0.5f * (FORK_LATERAL + FORK_ROCK_R + StageDefinitions.HalfWidthAt(def, arc));

        static void Fork(Work w, StageDef def, float goalArc)
        {
            const string G = "Fork";
            int k = 0;
            foreach (var d in ForkDivider(def, goalArc))
            {
                string part = "DesertRocks_0" + (6 + k % 2);
                float sc = FORK_ROCK_R / SizeOf(DROCKS, part).r;
                if (w.Add(G, DROCKS, part, w.Side(d.x, d.y), w.YawAt(d.x) + k * 41f, sc, 0.1f, -1000f, ignoreCircles: true, rOverlap: 0.2f))
                {
                    var p = w.list[w.list.Count - 1];
                    if (p.height >= COLLIDER_MIN_H) { p.collider = true; p.shape = SHAPE_BOX; }
                }
                k++;
            }
        }

        // ---- course-r2-11：先へ誘う給水塔（風車の塔） ----
        public const float TOWER_MIN_AHEAD = 70f, TOWER_MAX_AHEAD = 200f, TOWER_MAX_ANGLE = 15f;
        public const float TOWER_SPACING_M = 180f;     // 塔どうし（同じ見通しに 2 本並ばない）
        public const float TOWER_ROAD_M = 45f;         // 柵の外（道の中心から）
        public const float STRAIGHT_FOR_TOWER_M = 200f;

        /// <summary>塔を向ける区間の出口（弧長）：200m を超える直線の終わりと、急カーブの出口。</summary>
        public static List<float> TowerExits(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<float>();
            float run = 0f;
            for (int i = 0; i < course.Length; i++)
            {
                float a = i * StageDefinitions.SAMPLE_SPACING_M;
                if (a > goalArc) break;
                if (StageDefinitions.RadiusAt(course, i) > StageDefinitions.STRAIGHT_R_M) run += StageDefinitions.SAMPLE_SPACING_M;
                else { if (run > STRAIGHT_FOR_TOWER_M) r.Add(a); run = 0f; }
            }
            foreach (var c in StageDefinitions.TightCorners(def, course)) r.Add(c.toArc);
            r.Sort();
            return r;
        }

        // ---- art-r4-15：先へ誘う目印は 1 ステージ 1 つの形（同じ風車の塔をどのステージにも並べない） ----
        public const float TOWER_MAX_SLOPE_DEG = 10f;    // 塔は 10° 未満の地面だけ（斜面に傾いて立たない）
        public const float CAIRN_SMOKE_M = 25f;          // 境目：焚き火の山の煙の高さ
        public const int GUIDE_PER_STAGE = 1;

        /// <summary>そのステージの先へ誘う目印の素材（art-r4-15）：荒野 = 鐘の塔（WesternTown_082）、砂漠 = 給水塔（WesternTown_084）、境目 = 焚き火の山（煙の柱 25m）。</summary>
        public static void GuideAsset(int kind, out string asset, out string part, out float scale)
        {
            if (kind == HeightmapGen.KIND_DUNES) { asset = WTOWN; part = TOWN_TALL_A; scale = 0.95f; }   // 高さ 14m ≒ 町の建物（10.9m）× 1.3
            else if (kind == HeightmapGen.KIND_MESA) { asset = CAMPFIRE; part = "Campfire_01"; scale = 2.2f; }
            else { asset = WTOWN; part = TOWN_TALL_B; scale = 1.1f; }   // 14m。荒野の風車は遠くの目印（Landmark）の 1 本だけ。先へ誘うのは鐘の塔
        }

        /// <summary>地面の傾き（°、4m 離れた 4 点）。</summary>
        static float SlopeAt(Work w, Vector3 p)
        {
            const float e = 4f;
            float gx = (w.Ground(p.x + e, p.z) - w.Ground(p.x - e, p.z)) / (2f * e), gz = (w.Ground(p.x, p.z + e) - w.Ground(p.x, p.z - e)) / (2f * e);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 先へ誘う目印（course-r2-11 → art-r4-15）：1 ステージに 1 つだけ。直線の終わり・急カーブの出口のうち、出ていく向き ±15° の 70〜260m 先、
        /// 柵の外の 10° 未満の地面に、ステージごとに違う形（GuideAsset）。足元に焚き火（煙の柱）。境目は焚き火と薪の山（煙の柱は 25m、DressingBuilder）。
        /// 同じ風車の塔が 3 ステージのどの絵にも入って壁紙になっていた（art-r4-15）。
        /// </summary>
        static void GuideTowers(Work w, StageDef def, float goalArc)
        {
            const string G = "GuideTower";
            var towers = new List<Vector3>();
            foreach (var p in w.list) if (p.part == RANCH_TOWER || p.part == TOWN_TALL_A || p.part == TOWN_TALL_B) towers.Add(p.position);
            string asset, part; float scale;
            GuideAsset(def.kind, out asset, out part, out scale);
            float[] angs = { 0f, 7.5f, -7.5f, 15f, -15f };
            int placed = 0;
            var exits = TowerExits(def, w.course, goalArc);
            // 境目は切り通しの出口を先に（切り通しを抜けた先に煙の柱）
            float pass = SetPieces.PassArc(def, goalArc);
            if (pass >= 0f) exits.Insert(0, pass + SetPieces.PASS_HALF_CUT);
            foreach (float a in exits)
            {
                if (placed >= GUIDE_PER_STAGE) break;
                Vector3 t = WorldLayout.TangentAtArc(w.course, a + 10f), o = WorldLayout.PointAtArc(w.course, a);
                bool done = false;
                for (float d = TOWER_MIN_AHEAD + 50f; d <= TOWER_MAX_AHEAD + 60f && !done; d = d >= TOWER_MAX_AHEAD ? d + 999f : d + 20f)
                    foreach (float ang in angs)
                    {
                        float cr = Mathf.Cos(ang * Mathf.Deg2Rad), sr = Mathf.Sin(ang * Mathf.Deg2Rad);
                        Vector3 dir = new Vector3(t.x * cr + t.z * sr, 0f, -t.x * sr + t.z * cr);   // y 軸まわりに ang 度（右回り）
                        Vector3 p = o + dir * d;
                        p.y = 0f;
                        if (WorldLayout.DistToPolylineXZ(p, w.course) < TOWER_ROAD_M) continue;
                        if (SlopeAt(w, p) >= TOWER_MAX_SLOPE_DEG) continue;
                        bool far = true;
                        foreach (var q in towers) if (WorldLayout.DistXZ(p, q) < TOWER_SPACING_M) { far = false; break; }
                        if (!far) continue;
                        if (!w.Add(G, asset, part, p, WorldLayout.YawDeg(t) + 180f, scale, 0.3f, TOWER_ROAD_M - 10f, background: true, ignoreCircles: def.kind == HeightmapGen.KIND_MESA)) continue;
                        towers.Add(p);
                        if (def.kind == HeightmapGen.KIND_MESA)
                        {
                            // 焚き火の山：薪の束を三角に積む（WoodLogs）
                            for (int k = 0; k < 6; k++)
                                w.Add(G, LOGS, LogParts[2 + k % 6], p + new Vector3(Mathf.Sin(k * 1.05f) * 2.4f, 0f, Mathf.Cos(k * 1.05f) * 2.4f), k * 60f, 1.6f, 0.05f,
                                    TOWER_ROAD_M - 15f, ignoreCircles: true);
                        }
                        else w.Add(G, CAMPFIRE, "Campfire_01", p + WorldLayout.RightOf(t) * 6f, 0f, 1.2f, 0.05f, TOWER_ROAD_M - 15f, ignoreCircles: true);
                        placed++;
                        done = true;
                        break;
                    }
            }
        }

        // ---- course-r2-05：ゴールの先の切れ込みの額縁 ----
        /// <summary>切れ込み（SetPieces.CarveGoalNotch）の両脇に形のある大きな岩（art-r3-05：前は Cliffs_04/05）を立て、向こうに次のステージの目印（砂漠 = 給水塔、境目 = メサ B ×1.5）。</summary>
        static void NotchFrame(Work w, StageDef def, float goalArc)
        {
            const string G = "NotchFrame";
            Vector3 g = WorldLayout.PointAtArc(w.course, goalArc), t = WorldLayout.TangentAtArc(w.course, goalArc), r = WorldLayout.RightOf(t);
            float yaw = WorldLayout.YawDeg(t);
            for (int sd = -1; sd <= 1 && def.kind != HeightmapGen.KIND_CANYON; sd += 2)   // 荒野の切れ込みは外周の尾根そのもの（崖は谷の決まりで岩に）
            {
                // 切れ込みの両脇の額縁：道から 260m 以内なので崖の板ではなく形のある大きな岩（art-r3-05、砂漠は町の大きな岩・DesertRocks）
                for (int k = 0; k < 8; k++)
                {
                    float along = 120f + k * 15f, lat = sd * (SetPieces.NOTCH_HALF_M + 10f + 6f + k * 3f);
                    Vector3 p = g + t * along + r * lat;
                    p.y = 0f;
                    if (WorldLayout.DistToPolylineXZ(p, w.course) < ROAD_CLEAR + 8f) continue;
                    if (AddNearRock(w, G, p, 14f, 20f, yaw + (sd < 0 ? 0f : 180f) + w.Rand(-20f, 20f), ROAD_MIN, true))
                        break;
                }
            }
            if (def.index >= StageDefinitions.All.Length - 1) return;   // 最後のステージの先は無い
            int next = StageDefinitions.All[def.index + 1].kind;
            bool placed = false;
            for (float sc = 1.5f; sc >= 0.75f && !placed; sc -= 0.25f)
                for (float along = 260f; along >= 140f && !placed; along -= 15f)
                {
                    Vector3 p = g + t * along;
                    p.y = 0f;
                    placed = next == HeightmapGen.KIND_DUNES   // art-r4-15：次の砂漠の目印は給水塔（風車の塔は荒野の遠くの目印の 1 本だけ）
                        ? w.Add(G, WTOWN, TOWN_TALL_A, p, yaw + 180f, sc, 0.3f, ROAD_MIN, background: true, ignoreCircles: true)
                        : w.Add(G, MESAB, null, p, yaw, sc, 0.2f * SizeOf(MESAB, null).h * sc, ROAD_MIN, background: true, ignoreCircles: true);
                }
        }

        // ---- course-r2-10：空いた外周の輪の背景 ----
        public const float RIM_RING_INSET_M = 110f, RIM_RING_STEP_M = 90f, RIM_RING_ROAD_M = 60f;

        /// <summary>コースが内へ寄って空いた外周の輪（外周から 110m 内側の線）にメサ・メサ B（道から 260m 以上なら Cliffs_06/07、砂漠は大きな岩）を並べる（遠くに必ず何かがある）。</summary>
        static void RimRing(Work w, StageDef def, float goalArc)
        {
            const string G = "RimLandmark";
            float per = RimPathLength(RIM_RING_INSET_M, w.sizeM);
            int k = 0;
            for (float s = 30f; s < per; s += RIM_RING_STEP_M, k++)
            {
                Vector3 p = RimPathPoint(s, RIM_RING_INSET_M, w.sizeM);
                if (w.RoadDist(p) < RIM_RING_ROAD_M + 20f) continue;
                // 崖の板（Cliffs_06/07）は道から CLIFF_FAR_M 以上・尾根の上でない所だけ（art-r3-05）。ほかはメサ・メサ B、砂漠は大きな岩
                string part = k % 2 == 0 ? "Cliffs_06" : "Cliffs_07";
                float csc = w.Rand(0.8f, 1.1f);
                bool cliff = k % 3 != 2 && def.kind != HeightmapGen.KIND_CANYON && CliffSpotOk(w, p, SizeOf(CLIFFS, part).r * csc * CLIFF_HALF_DEPTH);
                if (cliff)
                {
                    float yaw = WorldLayout.YawDeg(new Vector3(w.sizeM * 0.5f - p.x, 0f, w.sizeM * 0.5f - p.z).normalized) + 90f;
                    w.Add(G, CLIFFS, part, p, yaw, csc, CLIFF_SINK * SizeOf(CLIFFS, part).h, RIM_RING_ROAD_M, background: true, rOverlap: 0.5f);
                }
                else if (def.kind == HeightmapGen.KIND_DUNES)
                    AddNearRock(w, G, p, 16f, 26f, w.Rand(0f, 360f), RIM_RING_ROAD_M, true);
                else
                    w.Add(G, k % 2 == 0 ? MESAB : MESA, null, p, w.Rand(0f, 360f), w.Rand(0.7f, 1f), 0.2f * SizeOf(MESAB, null).h, RIM_RING_ROAD_M, background: true, rOverlap: 0.5f);
            }
        }

        // ---- course-r2-06 / course-r3-05：柵の線の目印（見えない壁に形を与える）----
        public const float BARRIER_NEAR_M = 30f;        // 中心線からこの距離以内の柵は途切れない柵（FarmProps_03 を端と端でつなぐ）
        public const float BARRIER_STEP_CORNER = 3f, BARRIER_STEP_PASS = 6f, BARRIER_STEP_FAR = 14f;
        public const float BARRIER_TIGHT_EXTRA_M = 10f;  // 道の縁から 10m 以内の柵（急カーブ・絞り・ヘアピン）の急カーブの外側は岩
        public const float BARRIER_CORNER_ROCK_SCALE = 0.45f;
        public const float BARRIER_INSET_M = 0.8f;      // 柵の線から道の側へ（壁の手前に見える）
        public const int BARRIER_POST_EVERY = 8;        // art-r5-02：柵 8 枚ごとに立てた丸太の杭（前は 4 枚：白い歯の列に見えた）
        /// <summary>art-r5-02：杭（AddPost）はこの傾き（°）より急な地面には立てない。</summary>
        public const float POST_MAX_SLOPE_DEG = 18f;
        public const float BARRIER_NEAR_PAD_M = 3f;     // 近い区間（NearBarrierSegments）の端も柵になるよう少し外まで柵にする
        public const int BARRIER_BUDGET = 6000;
        /// <summary>近い柵の目に見える柵の板どうしの最大のすき間（m、course-r3-05：当たる壁は必ず見える）。</summary>
        public const float FENCE_MAX_GAP_M = 0.6f;
        public const int BARRIER_FENCE = 0, BARRIER_CORNER = 1, BARRIER_PASS = 2, BARRIER_FAR = 3;
        static readonly string[] FarClusters = { "RockCollection_16", "RockCollection_56", "RockCollection_88" };

        /// <summary>
        /// 柵の線の点 p の目印の種類（course-r3-05）：中心線から BARRIER_NEAR_M 以内 = 途切れない柵（BARRIER_FENCE）。ただし急カーブの外側で道の縁から
        /// BARRIER_TIGHT_EXTRA_M 以内 = 岩（BARRIER_CORNER）、切り通しの底 = 小さな岩（BARRIER_PASS）。それより遠い = 岩の山（BARRIER_FAR）。
        /// </summary>
        public static int BarrierKind(StageDef def, Vector3[] course, float[] cum, List<StageDefinitions.Corner> corners, float passArc, Vector3 p, out float arc, out float dc)
        {
            arc = NearestArc(course, cum, p, out dc);
            float hw = StageDefinitions.HalfWidthAt(def, arc);
            if (passArc >= 0f && Mathf.Abs(arc - passArc) < PASS_FLOOR_HALF_M + 10f) return BARRIER_PASS;
            if (dc <= hw + BARRIER_TIGHT_EXTRA_M)
            {
                Vector3 cp = WorldLayout.PointAtArc(course, arc);
                foreach (var c in corners)
                    if (arc >= c.fromArc - CourseBarrier.CORNER_PAD_M && arc <= c.toArc + CourseBarrier.CORNER_PAD_M)
                    {
                        Vector3 rt = WorldLayout.RightOf(WorldLayout.TangentAtArc(course, arc));
                        float lat = (p.x - cp.x) * rt.x + (p.z - cp.z) * rt.z;
                        if (lat * c.side < 0f) return BARRIER_CORNER;   // 右へ曲がるなら左が外側
                    }
            }
            return dc <= BARRIER_NEAR_M + BARRIER_NEAR_PAD_M ? BARRIER_FENCE : BARRIER_FAR;
        }

        /// <summary>
        /// 柵の線の目印（course-r2-06 / course-r3-05）：CourseBarrier の線を 0.5m ごとに歩き、
        ///   近い所（BARRIER_FENCE）は FarmProps_03 を長い辺（ローカル X）を線に沿わせて端と端でつなぐ途切れない柵（1 枚 2.74m、区間の終わりは短く切った 1 枚）、
        ///   4 枚ごとに立てた丸太の杭。急カーブの外側（BARRIER_CORNER）は 3m ごとに DesertRocks_04〜09 ×0.45、切り通しの底は 6m ごとに小さな岩、
        ///   遠い所は 14m ごとに岩の山（RockCollection_16/56/88）。前は 6〜14m ごとに 1 枚ずつで、線の 55〜80% が空いていた（見えない壁にぶつかった）。
        /// 別の計画（DressingPlan.For の割合・予算に数えない）として返す。柵は置かない円（札・灯り）も越えてつなぐ（札は柵の手前）。すべて当たりなし・静的。
        /// </summary>
        public static List<Placement> BarrierLine(StageDef def, Vector3[] course, float[,] heights, float goalArc, CourseBarrier.Plan plan)
        {
            var w = new Work
            {
                course = course, def = def, cum = CourseArcs(course), h = heights, sizeM = StageDefinitions.SIZE, budget = BARRIER_BUDGET,
                minX = EDGE_MARGIN, minZ = EDGE_MARGIN, maxX = StageDefinitions.SIZE - EDGE_MARGIN, maxZ = StageDefinitions.SIZE - EDGE_MARGIN,
                rng = new System.Random(def.seed * 31 + 5),
            };
            foreach (var c in StageExclusions(def, course, goalArc)) if (c.w < 20f) w.circles.Add(c);
            var corners = StageDefinitions.TightCorners(def, course);
            float passArc = SetPieces.PassArc(def, goalArc);
            const string G = "BarrierLine";
            const float STEP = 0.5f;
            int k = 0;
            for (int ri = 0; ri < plan.runs.Count; ri++)
            {
                Vector3[] run = plan.runs[ri];
                int n = plan.closed[ri] ? run.Length : run.Length - 1;
                bool haveJoint = false;
                Vector3 joint = Vector3.zero, prevQ = Vector3.zero;
                int pieces = 0;
                float nextMark = 0f, acc = 0f;
                int lastKind = -1;
                for (int si = 0; si < n; si++)
                {
                    Vector3 a = run[si], b = run[(si + 1) % run.Length];
                    float len = WorldLayout.DistXZ(a, b);
                    if (len < 0.05f) continue;
                    Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z) / len;
                    float yaw = WorldLayout.YawDeg(d);
                    for (float u = (si == 0 ? 0f : STEP - ((acc) % STEP)); u <= len + 1e-4f; u += STEP)
                    {
                        Vector3 p = a + d * u;
                        p.y = 0f;
                        float arc, dc;
                        int kind = BarrierKind(def, course, w.cum, corners, passArc, p, out arc, out dc);
                        Vector3 q = BarrierInset(p, d, WorldLayout.PointAtArc(course, arc), plan);
                        float along = acc + u;
                        if (kind != lastKind)
                        {
                            if (lastKind == BARRIER_FENCE && haveJoint) FenceTail(w, G, joint, prevQ);
                            haveJoint = false;
                            nextMark = along;
                            lastKind = kind;
                        }
                        if (kind == BARRIER_FENCE)
                        {
                            if (!haveJoint) { joint = q; haveJoint = true; pieces = 0; }
                            else
                            {
                                float dq = WorldLayout.DistXZ(joint, q);
                                if (dq >= RAIL_LEN)
                                {
                                    float dp = WorldLayout.DistXZ(joint, prevQ);
                                    float f = Mathf.Clamp01((RAIL_LEN - dp) / Mathf.Max(1e-4f, dq - dp));
                                    Vector3 j = Vector3.Lerp(prevQ, q, f);
                                    j.y = 0f;
                                    FencePanel(w, G, joint, j, 1f);
                                    if (pieces % BARRIER_POST_EVERY == 0)
                                        w.AddPost(G, joint, WorldLayout.YawDeg((j - joint).normalized), RAIL_POST_H, RAIL_POST_THICK, -1000f, ignoreCircles: true);
                                    pieces++;
                                    joint = j;
                                }
                            }
                        }
                        else if (along >= nextMark)
                        {
                            if (kind == BARRIER_PASS)   // 切り通しの底：杭ではなく小さな岩（art-r3-08）
                                w.Add(G, DROCKS, RockSmall[k % RockSmall.Length], q, yaw + k * 53f, 0.7f, 0.3f, -1000f, rOverlap: 0.2f);
                            else if (kind == BARRIER_CORNER)
                                w.Add(G, DROCKS, "DesertRocks_0" + (4 + k % 6), q, yaw + k * 53f, BARRIER_CORNER_ROCK_SCALE, 0.3f, -1000f, rOverlap: 0.2f, ignoreCircles: true);   // 干し草の壁（course-r3-07）の後ろの柵の線
                            else
                                w.Add(G, RCOLL, FarClusters[k % FarClusters.Length], q, yaw + k * 71f, 1.6f, 0.2f, -1000f, rOverlap: 0.2f);   // 遠い柵：岩の山
                            k++;
                            nextMark = along + (kind == BARRIER_CORNER ? BARRIER_STEP_CORNER : (kind == BARRIER_PASS ? BARRIER_STEP_PASS : BARRIER_STEP_FAR));
                        }
                        prevQ = q;
                    }
                    acc += len;
                }
                if (lastKind == BARRIER_FENCE && haveJoint) FenceTail(w, G, joint, prevQ);
            }
            foreach (var p in w.list) { p.collider = false; p.shape = SHAPE_NONE; }   // 当たりは柵（BoxCollider）だけ
            return w.list;
        }

        /// <summary>柵の線の点 p（線の向き d）を、線に直角に道の側（一番近いコースの点 cp の側）へ BARRIER_INSET_M 寄せた点（y = 0）。</summary>
        public static Vector3 BarrierInset(Vector3 p, Vector3 d, Vector3 cp, CourseBarrier.Plan plan = null)
        {
            Vector3 nrm = WorldLayout.RightOf(new Vector3(d.x, 0f, d.z).normalized);
            if (plan != null)
            {
                // 通ってよい側（Field が負）へ：柵が折り返して 2 本が近い所でも、それぞれの柵の道の側
                Vector3 a = new Vector3(p.x + nrm.x * BARRIER_INSET_M, 0f, p.z + nrm.z * BARRIER_INSET_M), b = new Vector3(p.x - nrm.x * BARRIER_INSET_M, 0f, p.z - nrm.z * BARRIER_INSET_M);
                if (plan.Field(b) < plan.Field(a)) nrm = -nrm;
            }
            else if ((cp.x - p.x) * nrm.x + (cp.z - p.z) * nrm.z < 0f) nrm = -nrm;
            return new Vector3(p.x + nrm.x * BARRIER_INSET_M, 0f, p.z + nrm.z * BARRIER_INSET_M);
        }

        /// <summary>柵の板 1 枚（FarmProps_03、長い辺 = ローカル X を a→b に沿わせる）。frac &lt; 1 は長さだけ縮めた最後の 1 枚。置かない円は越える。</summary>
        static void FencePanel(Work w, string g, Vector3 a, Vector3 b, float frac)
        {
            Vector3 c = (a + b) * 0.5f;
            Vector3 dd = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float yaw = WorldLayout.YawDeg(dd.normalized) + 90f;   // FarmProps_03 の長い辺（ローカル X）を線の向きへ（RailPiece と同じ）
            if (w.Add(g, FARM, "FarmProps_03", c, yaw, frac, 0.02f, -1000f, ignoreCircles: true, rOverlap: 0.2f, scaleY: 1f / frac) && frac < 0.999f)
                w.list[w.list.Count - 1].scaleZ = 1f / frac;   // 長さ（X）だけ縮める
        }

        /// <summary>区間の終わりの短い 1 枚（残りが FENCE_MAX_GAP_M より長いとき）。</summary>
        static void FenceTail(Work w, string g, Vector3 joint, Vector3 end)
        {
            float rem = WorldLayout.DistXZ(joint, end);
            if (rem < FENCE_MAX_GAP_M) return;
            FencePanel(w, g, joint, end, Mathf.Clamp(rem / RAIL_LEN, 0.2f, 1f));
        }

        /// <summary>
        /// 近い柵（BARRIER_FENCE）の目に見える柵の板の最大のすき間（m、course-r3-05）：柵の線（CourseBarrier の各 run）を 0.5m ごとに歩き、
        /// BarrierLine と同じく道の側へ BARRIER_INSET_M 寄せた点から一番近い柵の板（長い辺の線分）までの距離の最大。
        /// 目印の種類の変わり目の前後 2m と、柵の線が 25° より急に折れる角の柵 1 枚の長さ以内（板の弦が角・線の細いとげを切る）は数えない。
        /// 札・灯りの置かない円の中も柵はつなぐので例外なし。テストと Validator 用。
        /// </summary>
        /// <summary>NearFenceMaxGap の一番大きいすき間の場所（テストの記録用）。</summary>
        public static Vector3 WorstGapAt;
        public static float NearFenceMaxGap(StageDef def, Vector3[] course, float goalArc, CourseBarrier.Plan plan, List<Placement> line)
        {
            var cum = CourseArcs(course);
            var corners = StageDefinitions.TightCorners(def, course);
            float passArc = SetPieces.PassArc(def, goalArc);
            var segA = new List<Vector3>();
            var segB = new List<Vector3>();
            foreach (var p in line)
            {
                if (p.part != "FarmProps_03") continue;
                float half = RAIL_LEN * 0.5f * p.scale;
                float a = (p.yawDeg - 90f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 c = new Vector3(p.position.x, 0f, p.position.z);
                segA.Add(c - dir * half); segB.Add(c + dir * half);
            }
            float worst = 0f;
            for (int ri = 0; ri < plan.runs.Count; ri++)
            {
                Vector3[] run = plan.runs[ri];
                int n = plan.closed[ri] ? run.Length : run.Length - 1;
                for (int si = 0; si < n; si++)
                {
                    Vector3 a = run[si], b = run[(si + 1) % run.Length];
                    float len = WorldLayout.DistXZ(a, b);
                    if (len < 0.05f) continue;
                    bool sharpA = SharpVertex(run, si, plan.closed[ri]), sharpB = SharpVertex(run, (si + 1) % run.Length, plan.closed[ri]);
                    for (float u = 0.25f; u < len; u += 0.5f)
                    {
                        if ((sharpA && u < RAIL_LEN) || (sharpB && len - u < RAIL_LEN)) continue;
                        Vector3 p = Vector3.Lerp(a, b, u / len);
                        p.y = 0f;
                        float arc, dc;
                        if (BarrierKind(def, course, cum, corners, passArc, p, out arc, out dc) != BARRIER_FENCE) continue;
                        bool edge = false;
                        for (int e = -1; e <= 1 && !edge; e += 2)
                        {
                            Vector3 q2 = Vector3.Lerp(a, b, Mathf.Clamp01((u + e * 2f) / len));
                            float a2, d2;
                            if (BarrierKind(def, course, cum, corners, passArc, q2, out a2, out d2) != BARRIER_FENCE) edge = true;
                        }
                        if (edge) continue;
                        Vector3 q = BarrierInset(p, (b - a) / len, WorldLayout.PointAtArc(course, arc), plan);
                        float best = float.MaxValue;
                        for (int i = 0; i < segA.Count; i++)
                        {
                            float dx = segA[i].x - q.x, dz = segA[i].z - q.z;
                            if (dx * dx + dz * dz > 25f) continue;
                            best = Mathf.Min(best, WorldLayout.DistToSegmentXZ(q, segA[i], segB[i]));
                        }
                        if (best > worst) { worst = best; WorstGapAt = q; }
                    }
                }
            }
            return worst;
        }

        static bool SharpVertex(Vector3[] run, int i, bool closed)
        {
            int n = run.Length;
            if (!closed && (i <= 0 || i >= n - 1)) return true;
            Vector3 a = run[(i - 1 + n) % n], b = run[i], c = run[(i + 1) % n];
            Vector3 d0 = new Vector3(b.x - a.x, 0f, b.z - a.z), d1 = new Vector3(c.x - b.x, 0f, c.z - b.z);
            if (d0.sqrMagnitude < 1e-6f || d1.sqrMagnitude < 1e-6f) return true;
            float cos = (d0.x * d1.x + d0.z * d1.z) / (d0.magnitude * d1.magnitude);
            return cos < Mathf.Cos(25f * Mathf.Deg2Rad);
        }

        /// <summary>柵の区間のうち、中心線から BARRIER_NEAR_M 以内の区間（目印を 6m ごとに置く）。テストと Validator 用。</summary>
        public static List<Vector3[]> NearBarrierSegments(Vector3[] course, CourseBarrier.Plan plan)
        {
            var r = new List<Vector3[]>();
            foreach (var s in plan.Segments())
                if (WorldLayout.DistToPolylineXZ((s[0] + s[1]) * 0.5f, course) <= BARRIER_NEAR_M) r.Add(s);
            return r;
        }

        /// <summary>
        /// 置かない円（Terrain ローカル x/z と半径）：スターターの立ち位置、岩橋の溝、ジャンプ台、ゴール後の囲い、表彰台、
        /// （境目）ゴールの町のまわり。CourseBuilder / SetPieceBuilder / DressingBuilder の置き方と同じ式で求める。
        /// </summary>
        public static List<Vector4> StageExclusions(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector4>();
            float s = StageDefinitions.START_ARC_M;
            Vector3 t0 = WorldLayout.TangentAtArc(course, s);
            Vector3 lineP = WorldLayout.PointAtArc(course, s + 3f);
            Vector3 r0 = WorldLayout.RightOf(t0);
            Vector3 spot = lineP - r0 * CourseGeometry.START_SIDE + t0 * 2f;
            r.Add(new Vector4(spot.x, 0f, spot.z, 14f));
            float bridge = SetPieces.BridgeArc(def, goalArc);
            if (bridge >= 0f)
            {
                Vector3 b = WorldLayout.PointAtArc(course, bridge);
                r.Add(new Vector4(b.x, 0f, b.z, SetPieces.DETOUR_TO + 6f));
                // course-r3-04：「落ちるな」の札（橋の 70m 手前の右）
                float wa = bridge - SetPieces.BRIDGE_WARN_BEFORE;
                Vector3 wp = WorldLayout.PointAtArc(course, wa) + WorldLayout.RightOf(WorldLayout.TangentAtArc(course, wa)) * (StageDefinitions.HalfWidthAt(def, wa) + CourseMarks.CORNER_BOARD_OUT_M);
                r.Add(new Vector4(wp.x, 0f, wp.z, 3f));
            }
            float jump = SetPieces.JumpArc(def, goalArc);
            if (jump >= 0f)
                for (float k = -SetPieces.JUMP_LENGTH; k <= SetPieces.KICKER_TROUGH_END + SetPieces.KICKER_TROUGH_EXIT; k += 12f)
                {
                    Vector3 j = WorldLayout.PointAtArc(course, jump + k);
                    r.Add(new Vector4(j.x, 0f, j.z, SetPieces.KICKER_HALF_WIDTH + 8f));
                }
            // 全力の合図・絞りの手前の黒板・スタートの門の灯り（CourseMarks）
            foreach (var m in CourseMarks.MarkSpots(def, course, goalArc)) r.Add(new Vector4(m.x, 0f, m.z, 4f));
            // スタートラインの両端の柱と杭の列
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 e = lineP + r0 * (sd * CourseGeometry.START_SIDE);
                r.Add(new Vector4(e.x, 0f, e.z, 5f));
            }
            // CP の黒板（BOARD_EVERY 個ごとの CP・最後の下りの CP・ゴール、道の右）
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            for (int k = 0; k < cps.Length; k++)
            {
                if (!CourseMarks.HasBoard(def, cps, k, goalArc)) continue;
                Vector3 bt = WorldLayout.TangentAtArc(course, cps[k]);
                Vector3 bp = WorldLayout.PointAtArc(course, cps[k]) + WorldLayout.RightOf(bt) * CourseMarks.BoardSide(def, cps[k]);
                r.Add(new Vector4(bp.x, 0f, bp.z, 3.5f));
            }
            // ゴールゲートの柱の足元（干し草・樽・街灯）
            {
                Vector3 gt = WorldLayout.TangentAtArc(course, goalArc);
                Vector3 gp = WorldLayout.PointAtArc(course, goalArc);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 e = gp + WorldLayout.RightOf(gt) * (sd * (CourseGeometry.GATE_HALF + 1.5f));
                    r.Add(new Vector4(e.x, 0f, e.z, 4f));
                }
            }
            // course-r2-08：急カーブの札・頂点の干し草と街灯。course-r2-15：区間の門の柱。course-r2-03：岩の島
            foreach (var cb in CourseMarks.CornerBoards(def, course, goalArc)) r.Add(new Vector4(cb.position.x, 0f, cb.position.z, 3f));
            foreach (var c in StageDefinitions.TightCorners(def, course))
                {
                    foreach (var m in CourseMarks.CornerApexMarks(def, course, c, goalArc)) r.Add(new Vector4(m.x, 0f, m.z, 3f));
                    foreach (var wb in CourseMarks.CornerWall(def, course, c)) r.Add(new Vector4(wb.position.x, 0f, wb.position.z, 1.6f));   // course-r3-07：外側の干し草の壁
                }
            foreach (var gp in CourseMarks.SectorGatePosts(def, course, goalArc)) r.Add(new Vector4(gp.x, 0f, gp.z, 4f));
            foreach (var fd in ForkDivider(def, goalArc))
            {
                Vector3 fp = WorldLayout.PointAtArc(course, fd.x) + WorldLayout.RightOf(WorldLayout.TangentAtArc(course, fd.x)) * fd.y;
                r.Add(new Vector4(fp.x, 0f, fp.z, FORK_ROCK_R + 2f));
            }
            foreach (var isl in IslandSpots(def, course, goalArc))
            {
                Vector3 ip = WorldLayout.PointAtArc(course, isl.x) + WorldLayout.RightOf(WorldLayout.TangentAtArc(course, isl.x)) * isl.y;
                r.Add(new Vector4(ip.x, 0f, ip.z, ISLAND_ROCK_R + 4f));
            }
            float total = CoursePath.Length(course);
            Vector3 pen = WorldLayout.PointAtArc(course, total - CourseGeometry.PEN_BACK);
            r.Add(new Vector4(pen.x, 0f, pen.z, CourseGeometry.PEN_RADIUS + 6f));
            // course-r3-08：ゴール前の見物の直線（レール・干し草・街灯・旗・札）
            foreach (var cp in CourseMarks.FinishChute(def, course, goalArc))
                if (cp.kind != CourseMarks.CHUTE_RAIL || (int)(cp.arc / 6f) % 2 == 0)
                    r.Add(new Vector4(cp.position.x, 0f, cp.position.z, cp.kind == CourseMarks.CHUTE_HAY ? 1.6f : 1.4f));
            if (def.kind == HeightmapGen.KIND_MESA)
            {
                Vector3 pp = PodiumLocal(course, goalArc);
                r.Add(new Vector4(pp.x, 0f, pp.z, 9f));
                // ゴールの町（建物は GoalTown が固定の場所に置く）：course-r3-08 で囲いを中心に 60m（前はコースの終点から 140m で、見物の直線まで空けていた）
                r.Add(new Vector4(pen.x, 0f, pen.z, GOAL_TOWN_CLEAR_M));
            }
            return r;
        }

        /// <summary>表彰台の位置（ゴールから 45m 先、通りの右 15m）。DressingBuilder と同じ。</summary>
        public static Vector3 PodiumLocal(Vector3[] course, float goalArc)
        {
            float pa = goalArc + 45f;
            Vector3 pt = WorldLayout.TangentAtArc(course, pa);
            Vector3 pr = WorldLayout.RightOf(pt);
            return WorldLayout.PointAtArc(course, pa) + pr * 15f;
        }

        /// <summary>道沿いの街灯と目印の岩：CP ROADSIDE_EVERY_CP 個ごと、両脇（小物の柵は置かない）。</summary>
        static void Roadside(Work w, float goalArc)
        {
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            for (int k = 1; k < cps.Length - 1; k++)
            {
                if ((k + 1) % ROADSIDE_EVERY_CP != 0) continue;
                float a = cps[k] + 12f;
                float yaw = w.YawAt(a);
                for (int s = -1; s <= 1; s += 2)
                {
                    w.AddLampPost("Roadside", w.Side(a, s * (15f + w.Extra(a))), yaw);   // art-r4-14：野の灯りは丸太の柱
                    w.Add("Roadside", DROCKS, w.Pick(RockMid), w.Side(a + 6f, s * (17f + w.Extra(a + 6f) + SizeOf(DROCKS, "DesertRocks_05").r)), w.Rand(0f, 360f), 1.1f, 0.3f, ROAD_MIN);
                }
            }
        }

        static void WagonCamp(Work w, Vector3 c, float yaw, bool acacias)
        {
            const string G = "WagonCamp";
            if (!w.Add(G, CAMPFIRE, "Campfire_01", c, yaw, 1f, 0.05f)) return;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                w.Add(G, LOGS, i == 0 ? "WoodLogs_01" : w.Pick(new[] { "WoodLogs_02", "WoodLogs_03", "WoodLogs_08" }),
                    new Vector3(c.x + Mathf.Sin(a) * 3.2f, 0f, c.z + Mathf.Cos(a) * 3.2f), yaw + i * 90f + 90f, 1.4f, 0.05f);
            }
            w.Add(G, HAY, "HayBales_01", w.Around(c, 9f), w.Rand(0f, 360f), 1f, 0.05f);
            for (int i = 0; i < 2; i++) w.Add(G, HAY, w.Pick(HayParts), w.Around(c, 8f), w.Rand(0f, 360f), 1f, 0.02f);
            for (int i = 0; i < 2; i++) w.Add(G, FARM, w.Pick(FarmCrates), w.Around(c, 8f), w.Rand(0f, 360f), 1f, 0.02f);
            w.Add(G, FARM, w.Pick(FarmBarrels), w.Around(c, 7f), w.Rand(0f, 360f), 1f, 0.02f);
            w.Add(G, FARM, FARM_TABLE, w.Around(c, 7f), w.Rand(0f, 360f), 1f, 0.02f);
            for (int i = 0; i < 3; i++) w.Add(G, FARM, w.Pick(FarmSmall), w.Around(c, 8f), w.Rand(0f, 360f), 1f, 0.02f);
            for (int i = 0; i < 2; i++) w.Add(G, LOGS, w.Pick(LogParts), w.Around(c, 6f), w.Rand(0f, 360f), 1f, 0.02f);
            if (acacias) for (int i = 0; i < 2; i++) Tree(w, G, ACACIA, null, w.Around(c, 16f));
        }

        /// <summary>道の横 lateralMin〜lateralMax m に、arc から少しずつずらして置ける場所を探す。</summary>
        static bool FindBeside(Work w, float arc, float lateralMin, float lateralMax, float r, out Vector3 p)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                float side = (tries % 2 == 0) ? 1f : -1f;
                float a = arc + (tries / 2) * 15f * ((tries / 2) % 2 == 0 ? 1f : -1f);
                p = w.Side(a, side * w.Rand(lateralMin, lateralMax));
                if (w.Allowed(p, r, ROAD_CLEAR, false)) return true;
            }
            p = Vector3.zero;
            return false;
        }

        /// <summary>木 1 本（拡大 0.8〜1.4、向きは乱数、幹だけの Collider は道から 60m 以内のときだけ）。</summary>
        static bool Tree(Work w, string group, string asset, string part, Vector3 p, float minRoad = ROAD_CLEAR)
            => w.Add(group, asset, part, p, w.Rand(0f, 360f), w.Rand(TREE_SCALE_MIN, TREE_SCALE_MAX), 0.2f, minRoad, trunk: true);

        static string TreePart(Work w) => w.Pick(TreeParts);

        /// <summary>木の林：center の半径 radius に count 本を試す（重なりは Allowed が避ける）。置けた本数を返す。</summary>
        static int Grove(Work w, string group, Vector3 c, float radius, int count, float deadShare, float acaciaShare)
        {
            int n = 0;
            for (int i = 0; i < count * 3 && n < count; i++)
            {
                float u = (float)w.rng.NextDouble();
                bool ok = u < deadShare ? Tree(w, group, DEADTREE, null, w.Around(c, radius))
                    : (u < deadShare + acaciaShare ? Tree(w, group, ACACIA, null, w.Around(c, radius))
                    : Tree(w, group, TREES, TreePart(w), w.Around(c, radius)));
                if (ok) n++;
            }
            return n;
        }

        /// <summary>
        /// 道沿いの帯：コースの 100m ごとに、道から 60m 以内の「大きな飾り」（高さ LARGE_MIN_H 以上）が ROADSIDE_LARGE_PER_100M 個以上に
        /// なるまで、道から 18〜55m に大きな飾り（pick が返す素材）を足す。
        /// </summary>
        static void RoadsideBand(Work w, float goalArc, System.Func<Work, string[]> pick, float maxLat = BAND_DIST_M - 5f)
        {
            float[] cum = CourseArcs(w.course);
            int windows = Mathf.CeilToInt(goalArc / BAND_WINDOW_M);
            var counts = new int[windows];
            foreach (var p in w.list)
            {
                if (IsSmall(p)) continue;
                float d;
                float a = NearestArc(w.course, cum, p.position, out d);
                if (d - p.radius > BAND_DIST_M) continue;
                int k = Mathf.FloorToInt(a / BAND_WINDOW_M);
                if (k >= 0 && k < windows) counts[k]++;
            }
            for (int k = 0; k < windows; k++)
            {
                float a0 = k * BAND_WINDOW_M, a1 = Mathf.Min(goalArc, a0 + BAND_WINDOW_M);
                for (int tries = 0; tries < 240 && counts[k] < ROADSIDE_LARGE_PER_100M + 2; tries++)
                {
                    // 狭い帯（maxLat）で足りなければ（岩橋の周りなど）後半は広い帯でも探す
                    if (tries >= 120 && counts[k] >= ROADSIDE_LARGE_PER_100M) break;
                    float latMax = tries < 120 ? maxLat : BAND_DIST_M - 5f;
                    string[] ap = pick(w);
                    Size sz = SizeOf(ap[0], ap[1]);
                    float sc = ap[0] == TREES || ap[0] == ACACIA || ap[0] == DEADTREE ? w.Rand(TREE_SCALE_MIN, TREE_SCALE_MAX) : w.Rand(0.9f, 1.3f);
                    if (sz.h * sc < LARGE_MIN_H) continue;   // 帯には大きな飾りだけ
                    float r = sz.r * sc;
                    float arc = w.Rand(a0 + 2f, a1 - 2f);
                    float lat = w.Rand(ROAD_CLEAR + r, Mathf.Max(ROAD_CLEAR + r + 1f, latMax)) + w.Extra(arc);
                    Vector3 p = w.Side(arc, (w.rng.Next(2) == 0 ? -1f : 1f) * lat);
                    bool tree = ap[0] == TREES || ap[0] == ACACIA || ap[0] == DEADTREE || ap[0] == CACTI || ap[0] == CGROUP;
                    if (w.Add("RoadsideBand", ap[0], ap[1], p, w.Rand(0f, 360f), sc, tree ? 0.2f : 0.4f, trunk: tree)) counts[k]++;
                }
            }
        }

        /// <summary>崖（Cliffs）は道からこれ以上（足元の縁まで）離す。近くでは平らな箱に見える（art-r2-08）。</summary>
        public const float CLIFF_MIN_ROAD_M = 90f;
        /// <summary>崖の板（Cliffs）は道から 260m 以上（霧で柔らかくなる距離）だけ（art-r3-05）。それより近い所と尾根の上は形のある岩（NearRock）。</summary>
        public const float CLIFF_FAR_M = 260f;
        /// <summary>尾根の上：その点の地面がまわり 30m の平均より 6m 以上高い（空を背にした箱のシルエットになる）。</summary>
        public const float RIDGE_CREST_M = 6f, RIDGE_RADIUS_M = 30f;

        /// <summary>点 p がまわり RIDGE_RADIUS_M の平均の地面より RIDGE_CREST_M 以上高いか（尾根の上）。</summary>
        static bool OnCrest(Work w, Vector3 p)
        {
            if (w.h == null) return false;
            float sum = 0f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                sum += w.Ground(p.x + Mathf.Sin(a) * RIDGE_RADIUS_M, p.z + Mathf.Cos(a) * RIDGE_RADIUS_M);
            }
            return w.Ground(p.x, p.z) - sum / 8f > RIDGE_CREST_M;
        }

        /// <summary>崖の板を置いてよい所か（art-r3-05）：砂漠でなく、足元の縁が道から CLIFF_FAR_M 以上、尾根の上でない。</summary>
        static bool CliffSpotOk(Work w, Vector3 p, float footR)
        {
            if (w.def != null && w.def.kind == HeightmapGen.KIND_DUNES) return false;
            if (w.course != null && WorldLayout.DistToPolylineXZ(p, w.course) - footR < CLIFF_FAR_M) return false;
            return !OnCrest(w, p);
        }

        /// <summary>崖の代わりの大きな岩（art-r3-05）：砂漠は町の大きな岩・DesertRocks_01〜03、ほかは NearRock（メサ B・DesertRocks_01〜03・岩のセット）。</summary>
        static bool AddNearRock(Work w, string group, Vector3 p, float hMin, float hMax, float yaw, float minRoad, bool background)
        {
            string asset, part; float sc, sy;
            if (w.def != null && w.def.kind == HeightmapGen.KIND_DUNES)
            {
                if (w.Pick(2) == 0) { asset = WTOWN; part = w.Pick(TownRocksBig); }
                else { asset = DROCKS; part = w.Pick(RockBig); }
                sy = w.Rand(1.0f, 1.4f);
                sc = w.Rand(hMin, hMax) / Mathf.Max(0.1f, SizeOf(asset, part).h * sy);
            }
            else NearRock(w, hMin, hMax, out asset, out part, out sc, out sy);
            float r = SizeOf(asset, part).r * sc;
            float foot = asset == MESAB ? r * MESA_GATE_FOOTPRINT : r * STRETCHED_ROCK_FOOTPRINT;
            return w.Add(group, asset, part, p, yaw, sc, CLIFF_SINK * SizeOf(asset, part).h * sc * sy, minRoad, background: background, rBounds: foot, scaleY: sy);
        }

        /// <summary>
        /// 道の近く（CLIFF_MIN_ROAD_M 以内）の大きな岩（art-r2-08）：メサ B・DesertRocks_01〜03・岩のセットの大きな部品から 1 つ。
        /// 拡大は高さ hMin〜hMax m になるように、縦だけ 1.2〜1.8 倍（崖の代わりに背の高い岩）。
        /// </summary>
        static void NearRock(Work w, float hMin, float hMax, out string asset, out string part, out float scale, out float scaleY)
        {
            int k = w.Pick(3);
            if (k == 0) { asset = MESAB; part = null; }
            else if (k == 1) { asset = DROCKS; part = w.Pick(RockBig); }
            else { asset = RSTYL; part = w.Pick(RstylBig); }
            scaleY = w.Rand(1.2f, 1.8f);
            float h0 = SizeOf(asset, part).h * scaleY;
            scale = w.Rand(hMin, hMax) / Mathf.Max(0.1f, h0);
        }

        /// <summary>崖 1 枚の足元に DesertRocks_04〜09 を 3〜5 個（高さの 30% を沈める）：箱の底の縁を隠す（art-r2-08）。</summary>
        static void CliffSkirt(Work w, Placement cliff)
        {
            int n = 3 + w.Pick(3);
            float rr = cliff.radius * CLIFF_HALF_DEPTH;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n * 360f + w.Rand(-25f, 25f)) * Mathf.Deg2Rad;
                float d = rr * w.Rand(0.8f, 1.05f);
                string part = w.Pick(RockMid);
                float sc = w.Rand(1.0f, 1.6f);
                var p = new Vector3(cliff.position.x + Mathf.Sin(a) * d, 0f, cliff.position.z + Mathf.Cos(a) * d);
                w.Add("CliffSkirt", DROCKS, part, p, w.Rand(0f, 360f), sc, 0.3f * SizeOf(DROCKS, part).h * sc, CLIFF_MIN_ROAD_M - 30f, ignoreCircles: true, rOverlap: 0f);
            }
        }

        static void Canyon(Work w, StageDef def, float goalArc)
        {
            float total = CoursePath.Length(w.course);
            // 一つしかない見どころから先に：幌馬車の野営地（約 500m ごと、アカシア付き）と廃屋
            for (float a = CAMP_SPACING_M * 0.5f; a < goalArc; a += CAMP_SPACING_M)
            {
                Vector3 c;
                if (FindBeside(w, a, 26f, 34f, 10f, out c)) WagonCamp(w, c, w.YawAt(a), true);
            }
            {
                Vector3 c;
                if (FindBeside(w, goalArc * 0.62f, 95f, 120f, 20f, out c))
                {
                    const string G = "RuinedHomestead";
                    float yaw = w.Rand(0f, 360f);
                    w.Add(G, TOWNBLOCK, null, c, yaw, 1f, 0.5f);
                    float rad = yaw * Mathf.Deg2Rad;
                    w.Add(G, SALOON, null, new Vector3(c.x + Mathf.Cos(rad) * 15f, 0f, c.z - Mathf.Sin(rad) * 15f), yaw + 15f, 1f, 0.5f);
                    for (int i = 0; i < 6; i++) w.Add(G, FARM, w.Pick(FarmFences), w.Around(c, 22f), w.Rand(0f, 360f), 1f, 0.05f);
                    for (int i = 0; i < 4; i++) w.Add(G, FARM, w.Pick(FarmCrates), w.Around(c, 16f), w.Rand(0f, 360f), 1f, 0.05f);
                    w.Add(G, WTOWN, TOWN_CART, w.Around(c, 12f), w.Rand(0f, 360f), 1f, 0.05f);
                    Grove(w, G, c, 28f, 5, 1f, 0f);
                }
            }
            // 谷底の近景（道から 14〜28m、22m ごとに左右交互）を最初に：走っていて速さを感じる中景の物
            CanyonFloor(w, goalArc);
            // 谷の縁の壁（左右交互に 50m ごと）：道の近くは崖（Cliffs）ではなく、形と模様のある大きな岩（メサ B・DesertRocks_01〜03・岩のセット）を
            // 縦に 1.2〜1.8 倍・向き ±25° でばらして並べる（art-r2-08：近くの Cliffs は平らな箱に見える）。高さの 22% を斜面に埋める
            int ci = 0;
            for (float a = 40f; a < total; a += 50f, ci++)
            {
                int s = (ci % 2 == 0) ? 1 : -1;
                string asset, part; float sc, sy;
                NearRock(w, 14f, 26f, out asset, out part, out sc, out sy);
                float r = SizeOf(asset, part).r * sc;
                float foot = asset == MESAB ? r * MESA_GATE_FOOTPRINT : r * STRETCHED_ROCK_FOOTPRINT;
                float arc = a + w.Rand(-15f, 15f);
                float yaw = w.YawAt(arc) + w.Rand(-25f, 25f) + (w.Pick(2) == 0 ? 0f : 180f);
                w.Add("CanyonWalls", asset, part, w.Side(arc, s * (ROAD_CLEAR + w.Extra(arc) + foot + w.Rand(0f, 8f))), yaw, sc,
                    CLIFF_SINK * SizeOf(asset, part).h * sc * sy, rBounds: foot, scaleY: sy);
            }
            // 崖（Cliffs）は道から CLIFF_FAR_M（260m）より遠く、尾根の上でない所だけ（art-r3-05：近くと空を背にした所では平らな橙の箱に見える）。
            // それより近い上の斜面（道から CLIFF_MIN_ROAD_M〜）は形のある大きな岩（NearRock）。左右交互に 70m ごと、崖の足元に岩のすそ
            ci = 0;
            for (float a = 60f; a < total; a += 70f, ci++)
            {
                int s = (ci % 2 == 0) ? -1 : 1;
                string part = CliffParts[ci % CliffParts.Length];
                float sc = w.Rand(1f, 1.4f);
                Size cs = SizeOf(CLIFFS, part);
                float r = cs.r * sc;
                for (int tries = 0; tries < 3; tries++)
                {
                    float arc = a + w.Rand(-15f, 15f);
                    float yaw = w.YawAt(arc) + (w.Pick(2) == 0 ? 0f : 180f) + w.Rand(-CLIFF_YAW_JITTER, CLIFF_YAW_JITTER);
                    Vector3 p = w.Side(arc, s * (CLIFF_MIN_ROAD_M + 2f + w.Extra(arc) + r * CLIFF_HALF_DEPTH + w.Rand(0f, 20f)));
                    if (!CliffSpotOk(w, p, r * CLIFF_HALF_DEPTH))
                    {
                        if (AddNearRock(w, "CliffBack", p, 16f, 28f, yaw + w.Rand(-25f, 25f), CLIFF_MIN_ROAD_M - 20f, true)) break;
                        continue;
                    }
                    if (w.Add("CliffBack", CLIFFS, part, p, yaw, sc, CLIFF_SINK * cs.h * sc, CLIFF_MIN_ROAD_M + 6f, rBounds: r * CLIFF_HALF_DEPTH))
                    {
                        CliffSkirt(w, w.list[w.list.Count - 1]);
                        break;
                    }
                }
            }
            // 枯れ木の林：谷底（道から 28〜45m）を 120m ごとに左右交互（art-r2-04：枯れ木は半分にし、縁は岩場に）
            int side = 1;
            for (float a = 60f; a < total; a += 120f, side = -side)
            {
                Grove(w, "DeadGroveFloor", w.Side(a, side * w.Rand(28f, 45f)), 14f, 3, 0.9f, 0.1f);
                TownRockPile(w, "RimRocks", w.Side(a + 60f, -side * w.Rand(70f, 110f)), 16f, 5);
            }
            // 大岩
            foreach (var c in w.Centers(def.seed + 1, 8, 60f, 160f, 110f)) w.Add("BigRocks", DROCKS, w.Pick(RockBig), c, w.Rand(0f, 360f), w.Rand(1f, 1.3f), 1.5f);
            // 道沿いの帯（大きな岩・枯れ木・町の岩・岩のセット）。狭い谷なので道から 32m まで（斜面・縁に乗らない）
            RoadsideBand(w, goalArc, x =>
            {
                int k = x.rng.Next(10);
                if (k < 2) return new[] { DEADTREE, null };
                if (k < 5) return new[] { DROCKS, x.Pick(RockMid) };
                if (k < 7) return new[] { DPROPS, x.Pick(DpRocks) };
                if (k < 9) return new[] { WTOWN, x.Pick(TownRocksBig) };
                return new[] { RSTYL, x.Pick(RstylBig) };
            }, CANYON_BAND_MAX_LAT);
            // 岩場（まとまり 14 か所）
            foreach (var c in w.Centers(def.seed + 2, 14, 26f, 140f, 70f))
                for (int i = 0; i < 9; i++)
                {
                    int k = w.Pick(4);
                    if (k == 0) w.Add("RockField", DROCKS, w.Pick(i < 3 ? RockMid : RockSmall), w.Around(c, 22f), w.Rand(0f, 360f), w.Rand(0.8f, 1.3f), 0.3f);
                    else if (k == 1) w.Add("RockField", RCOLL, w.Pick(Pebbles), w.Around(c, 18f), w.Rand(0f, 360f), w.Rand(1.5f, 3f), 0.1f);
                    else if (k == 2) w.Add("RockField", WTOWN, w.Pick(TownRocksSmall), w.Around(c, 18f), w.Rand(0f, 360f), w.Rand(0.8f, 1.3f), 0.2f);
                    else w.Add("RockField", DPROPS, w.Pick(DpRocks), w.Around(c, 20f), w.Rand(0f, 360f), w.Rand(0.8f, 1.6f), 0.2f);
                }
            // 遠くの斜面：町の岩の山（枯れ木は 1 本だけ）
            foreach (var c in w.Centers(def.seed + 5, 14, 60f, 200f, 70f)) TownRockPile(w, "FarRocks", c, 16f, 4);
            // 道ばたの小さなサボテンと草（まとまり）
            foreach (var c in w.Centers(def.seed + 4, 8, 20f, 60f, 60f))
                for (int i = 0; i < 4; i++)
                    w.Add("Scrub", DPROPS, i % 2 == 0 ? w.Pick(DpCacti) : w.Pick(DpGrass), w.Around(c, 8f), w.Rand(0f, 360f), 1f, 0.05f);
        }

        /// <summary>町（WesternTown）の岩の山：大きな岩 1 つ、小さな岩・砂漠の岩、枯れ木 1 本（art-r2-04/05）。</summary>
        static void TownRockPile(Work w, string group, Vector3 c, float radius, int n)
        {
            string big = w.Pick(TownRocksBig);
            float bs = w.Rand(0.9f, 1.4f);
            w.Add(group, WTOWN, big, c, w.Rand(0f, 360f), bs, 0.25f * SizeOf(WTOWN, big).h * bs);
            for (int i = 1; i < n; i++)
            {
                if (i % 2 == 1) w.Add(group, WTOWN, w.Pick(TownRocksSmall), w.Around(c, radius), w.Rand(0f, 360f), w.Rand(0.9f, 1.5f), 0.3f);
                else w.Add(group, DROCKS, w.Pick(i == 2 ? RockMid : DeadAndRocks), w.Around(c, radius), w.Rand(0f, 360f), w.Rand(0.9f, 1.3f), 0.4f);
            }
            Tree(w, group, DEADTREE, null, w.Around(c, radius));
        }

        /// <summary>近景の 1 つの部品は 100m ごとに 2 回まで（art-r3-14：同じ形の岩が並ぶと判を押したように見える）。</summary>
        public const int FLOOR_PART_PER_100M = 2;

        /// <summary>
        /// 谷底・台地の近景（art-r1-02 / art-r3-07 / art-r3-14）：弧長 CANYON_FLOOR_STEP_M ごとに左右交互、道の脇（ROAD_MIN+1〜28m）に 1 つ、
        /// 1 つおきに小石の群れ（RockCollection 3〜5 個、拡大 2〜3）。CP の黒板の横（右側）は空ける（除外の円）。
        ///   荒野：小さな岩・砂漠の岩・町の岩・岩のセット・枯れ木・農場のがらくた。
        ///   境目（MesaFloor）：砂漠の草（DesertProps_22/23）・小さなサボテン・小さな岩・町の小石・岩のセット。
        /// 1 つの部品は 100m ごとに FLOOR_PART_PER_100M 回まで（選び直す）。
        /// </summary>
        static void CanyonFloor(Work w, float goalArc)
        {
            bool mesa = w.def != null && w.def.kind == HeightmapGen.KIND_MESA;
            string G = mesa ? "MesaFloor" : "CanyonFloor";
            var used = new Dictionary<string, int>();
            int i = 0;
            for (float a = 30f; a < goalArc - 10f; a += CANYON_FLOOR_STEP_M, i++)
            {
                float sd = (i % 2 == 0) ? 1f : -1f;
                int win = Mathf.FloorToInt(a / 100f);
                string asset = null, part = null; float sc = 1f; bool tree = false;
                for (int tries = 0; tries < 6; tries++)
                {
                    int k = w.Pick(20);
                    tree = false;
                    if (mesa)
                    {
                        if (k < 5) { asset = DPROPS; part = w.Pick(DpGrass); sc = w.Rand(1.0f, 1.5f); }
                        else if (k < 8) { asset = DPROPS; part = w.Pick(DpCacti); sc = w.Rand(0.9f, 1.3f); }
                        else if (k < 12) { asset = DROCKS; part = w.Pick(RockSmall); sc = w.Rand(1.0f, 1.6f); }
                        else if (k < 15) { asset = WTOWN; part = w.Pick(TownPebbles); sc = w.Rand(1.0f, 1.5f); }
                        else if (k < 19) { asset = RSTYL; part = w.Pick(RstylParts); sc = w.Rand(1.2f, 2.0f); }
                        else { asset = DEADTREE; part = null; sc = w.Rand(0.8f, 1.1f); tree = true; }
                    }
                    else
                    {
                        if (k < 5) { asset = DROCKS; part = w.Pick(DeadAndRocks); sc = w.Rand(1.0f, 1.6f); }
                        else if (k < 9) { asset = DPROPS; part = w.Pick(DpRocks); sc = w.Rand(0.9f, 1.4f); }
                        else if (k < 12) { asset = WTOWN; part = w.Pick(TownRocksSmall); sc = w.Rand(0.8f, 1.2f); }
                        else if (k < 16) { asset = RSTYL; part = w.Pick(RstylParts); sc = w.Rand(1.2f, 2.2f); }
                        else if (k < 18) { asset = k == 16 ? FARM : LOGS; part = k == 16 ? w.Pick(FarmCrates) : w.Pick(LogParts); sc = 1.2f; }
                        else if (k < 19) { asset = RCOLL; part = w.Pick(Pebbles); sc = w.Rand(2.5f, 3.5f); }
                        else { asset = DEADTREE; part = null; sc = w.Rand(0.9f, 1.2f); tree = true; }
                    }
                    int n0;
                    used.TryGetValue(win + "|" + ShareKey(asset, part), out n0);
                    if (n0 < FLOOR_PART_PER_100M) break;
                }
                string key = win + "|" + ShareKey(asset, part);
                int n1;
                used.TryGetValue(key, out n1);
                if (n1 >= FLOOR_PART_PER_100M) continue;
                float r = SizeOf(asset, part).r * sc;
                float arc = a + w.Rand(-4f, 4f);
                float lat = w.Rand(ROAD_MIN + 1f + r, CANYON_FLOOR_MAX_LAT) + w.Extra(arc);
                Vector3 p = w.Side(arc, sd * lat);
                if (w.Add(G, asset, part, p, w.Rand(0f, 360f), sc, tree ? 0.2f : 0.25f * SizeOf(asset, part).h * sc, ROAD_MIN, trunk: tree))
                    used[key] = n1 + 1;
                if (i % 2 == 0)
                {
                    int n = 3 + w.Pick(3);
                    Vector3 c = w.Side(arc + w.Rand(4f, 9f), sd * (w.Rand(ROAD_MIN + 3f, CANYON_FLOOR_MAX_LAT - 2f) + w.Extra(arc)));
                    for (int j = 0; j < n; j++)
                        w.Add(G, RCOLL, w.Pick(Pebbles), w.Around(c, 3.5f), w.Rand(0f, 360f), w.Rand(2f, 3f), 0.1f, ROAD_MIN);
                }
            }
        }

        // ------------------------------------------------------------------
        // 道の目印・遠くの目印（art-r1-10）
        // ------------------------------------------------------------------

        /// <summary>コースの弧長 a での曲率（1/m、+ = 左へ曲がる、- = 右へ）。前後 6m の向きの差から。</summary>
        public static float Curvature(Vector3[] course, float a)
        {
            Vector3 t0 = WorldLayout.TangentAtArc(course, a - 6f), t1 = WorldLayout.TangentAtArc(course, a + 6f);
            float cross = t0.z * t1.x - t0.x * t1.z;   // + = 右回り（y 上・x 右・z 前の左手系で右へ曲がる）
            float dot = t0.x * t1.x + t0.z * t1.z;
            float ang = Mathf.Atan2(cross, dot);
            return -ang / 12f;
        }

        // ---- art-r3-03：カーブの外側の途切れない柵 ----
        public const float GUIDE_RUN_PAD_M = 25f;        // カーブの前後に柵を延ばす
        public const float GUIDE_MERGE_M = 20f;          // 同じ向きのカーブがこれより近ければ 1 本の柵に
        public const float RAIL_LEN = 2.74f;             // FarmProps_03（X が長い）・_04（Z が長い）の柵 1 枚の長さ（Blender で 2.743m）
        public const int RAIL_POST_EVERY = 2;            // 継ぎ目 2 つごとに杭（5.5m）
        public const int GUIDE_RAIL_MAX = 480;           // 1 ステージの柵の枚数の上限（約 1.3km）

        /// <summary>カーブの外側の柵の 1 本：継ぎ目の点（Terrain ローカル、y = 0）。柵 1 枚 = 隣り合う継ぎ目の間。side = 外側（+1 = 右）。</summary>
        public sealed class GuideRun
        {
            public List<Vector3> joints = new List<Vector3>();
            public float fromArc, toArc, lateral;
            public int side;
        }

        /// <summary>
        /// カーブの外側の柵の計画（art-r3-03）：曲率が GUIDE_CURVATURE を超える区間（同じ向きで GUIDE_MERGE_M 以内はつなぐ）を前後 GUIDE_RUN_PAD_M 延ばし、
        /// 外側の一定の横（区間の最大の半幅 + GUIDE_LATERAL − 12m）の線に沿って柵 1 枚の長さ（RAIL_LEN）ごとに継ぎ目を置く（なめらかな平行線）。
        /// 道（どの区間でも）から ROAD_MIN + 1m より近い継ぎ目で柵を切る（ヘアピンで戻ってくる道をふさがない）。急なカーブ順に GUIDE_RAIL_MAX 枚まで。
        /// </summary>
        public static List<GuideRun> GuideFenceRuns(StageDef def, Vector3[] course, float goalArc)
        {
            var cum = CourseArcs(course);
            float a0 = 20f, a1 = goalArc - 30f;
            // 1) 急なカーブの区間
            var spans = new List<Vector4>();   // from, to, side, 最大の曲率
            for (float a = a0; a <= a1; a += 2f)
            {
                float k = Curvature(course, a);
                if (Mathf.Abs(k) <= GUIDE_CURVATURE) continue;
                int side = k > 0f ? 1 : -1;   // 左へ曲がる → 外側は右（+）
                if (spans.Count > 0 && (int)spans[spans.Count - 1].z == side && a - spans[spans.Count - 1].y <= GUIDE_MERGE_M)
                {
                    var v = spans[spans.Count - 1];
                    spans[spans.Count - 1] = new Vector4(v.x, a, v.z, Mathf.Max(v.w, Mathf.Abs(k)));
                }
                else spans.Add(new Vector4(a, a, side, Mathf.Abs(k)));
            }
            // 2) 前後に延ばし、重なった同じ向きの区間をつなぐ
            var runs = new List<Vector4>();
            foreach (var sp in spans)
            {
                var v = new Vector4(Mathf.Max(a0, sp.x - GUIDE_RUN_PAD_M), Mathf.Min(a1, sp.y + GUIDE_RUN_PAD_M), sp.z, sp.w);
                if (runs.Count > 0 && (int)runs[runs.Count - 1].z == (int)v.z && v.x <= runs[runs.Count - 1].y)
                {
                    var u = runs[runs.Count - 1];
                    runs[runs.Count - 1] = new Vector4(u.x, Mathf.Max(u.y, v.y), u.z, Mathf.Max(u.w, v.w));
                }
                else runs.Add(v);
            }
            // 急なカーブから順に（上限の枚数に入るように）
            var order = new List<int>();
            for (int i = 0; i < runs.Count; i++) order.Add(i);
            order.Sort((x, y) => runs[y].w.CompareTo(runs[x].w));
            var keep = new List<GuideRun>();
            int total = 0;
            foreach (int ri in order)
            {
                var rv = runs[ri];
                int side = (int)rv.z;
                float hw = 0f;
                for (float a = rv.x; a <= rv.y; a += 2f) hw = Mathf.Max(hw, StageDefinitions.HalfWidthAt(def, a));
                float lat = Mathf.Max(GUIDE_LATERAL, ROAD_MIN + 1.5f) + (hw - HeightmapGen.RUNWAY_M);
                GuideRun cur = null;
                Vector3 prev = Vector3.zero;
                bool havePrev = false;
                for (float a = rv.x; a <= rv.y + 0.01f; a += 0.5f)
                {
                    Vector3 t = WorldLayout.TangentAtArc(course, a), c = WorldLayout.PointAtArc(course, a), rt = WorldLayout.RightOf(t);
                    Vector3 q = new Vector3(c.x + rt.x * side * lat, 0f, c.z + rt.z * side * lat);
                    if (RoadClearance(def, course, cum, q) < ROAD_MIN + 1f)
                    {
                        if (cur != null && cur.joints.Count >= 3) { keep.Add(cur); total += cur.joints.Count - 1; }
                        cur = null; havePrev = false;
                        continue;
                    }
                    if (cur == null)
                    {
                        cur = new GuideRun { fromArc = a, toArc = a, lateral = lat, side = side };
                        cur.joints.Add(q);
                        prev = q; havePrev = true;
                        continue;
                    }
                    Vector3 last = cur.joints[cur.joints.Count - 1];
                    float dq = WorldLayout.DistXZ(last, q);
                    if (dq >= RAIL_LEN && havePrev)
                    {
                        // 前の点と今の点の間で、最後の継ぎ目からちょうど RAIL_LEN の点
                        float dp = WorldLayout.DistXZ(last, prev);
                        float f = Mathf.Clamp01((RAIL_LEN - dp) / Mathf.Max(1e-4f, dq - dp));
                        Vector3 j = Vector3.Lerp(prev, q, f);
                        j.y = 0f;
                        cur.joints.Add(j);
                        cur.toArc = a;
                    }
                    prev = q;
                }
                if (cur != null && cur.joints.Count >= 3) { keep.Add(cur); total += cur.joints.Count - 1; }
                if (total >= GUIDE_RAIL_MAX) break;
            }
            keep.Sort((x, y) => x.fromArc.CompareTo(y.fromArc));
            // 上限を超えた分は最後の柵の端から切る
            int over = total - GUIDE_RAIL_MAX;
            for (int i = keep.Count - 1; i >= 0 && over > 0; i--)
            {
                var g = keep[i];
                int cut = Mathf.Min(over, g.joints.Count - 1);
                if (g.joints.Count - cut < 3) cut = g.joints.Count - 1;
                g.joints.RemoveRange(g.joints.Count - cut, cut);
                over -= cut;
                if (g.joints.Count < 3) keep.RemoveAt(i);
            }
            return keep;
        }

        /// <summary>柵の 1 枚：k 枚目（0 から）の部品と向き。FarmProps_03（X が長い）と _04（Z が長い）は同じ柵：長い辺を継ぎ目の向きにそろえる。</summary>
        public static void RailPiece(Vector3 a, Vector3 b, int k, out string part, out Vector3 center, out float yaw)
        {
            Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            center = (a + b) * 0.5f;
            part = (k & 1) == 0 ? "FarmProps_03" : "FarmProps_04";
            float dy = WorldLayout.YawDeg(d);
            yaw = part == "FarmProps_04" ? dy : dy + 90f;
        }

        /// <summary>
        /// 道の目印（art-r3-03）：急なカーブの外側に途切れない柵（GuideFenceRuns、FarmProps_03/04 を端と端でつなぎ、継ぎ目 3 つごとに立てた丸太の杭）。
        /// 直線は GUIDE_STRAIGHT_STEP_M ごとに左右交互の標柱（立てた丸太の杭 2.6m、art-r3-02：骨の DesertProps_05 は使わない）、1 本おきに街灯。
        /// </summary>
        static void GuideLine(Work w, StageDef def, float goalArc)
        {
            const string G = "GuideLine";
            var runs = GuideFenceRuns(def, w.course, goalArc);
            int piece = 0;
            foreach (var run in runs)
            {
                for (int i = 0; i + 1 < run.joints.Count; i++)
                {
                    string part; Vector3 c; float yaw;
                    RailPiece(run.joints[i], run.joints[i + 1], piece++, out part, out c, out yaw);
                    w.Add(G, FARM, part, c, yaw, 1f, 0.05f, ROAD_MIN, rOverlap: 0f);
                }
                for (int i = 0; i < run.joints.Count; i += RAIL_POST_EVERY)
                {
                    if (InPassCut(w, def, goalArc, run.joints[i])) continue;   // 切り通しの中は杭を立てない（art-r3-08）
                    Vector3 d = run.joints[Mathf.Min(i + 1, run.joints.Count - 1)] - run.joints[Mathf.Max(0, i - 1)];
                    w.AddPost(G, run.joints[i], WorldLayout.YawDeg(new Vector3(d.x, 0f, d.z).normalized), RAIL_POST_H, RAIL_POST_THICK);
                }
            }
            // 直線の標柱：柵の区間の外だけ
            float lastPost = -1000f;
            int post = 0;
            for (float a = 20f; a < goalArc - 30f; a += GUIDE_STEP_M)
            {
                bool inRun = false;
                foreach (var run in runs) if (a >= run.fromArc - 10f && a <= run.toArc + 10f) inRun = true;
                if (inRun) { lastPost = a; continue; }
                if (a - lastPost < GUIDE_STRAIGHT_STEP_M) continue;
                float passA = SetPieces.PassArc(def, goalArc);
                if (passA >= 0f && Mathf.Abs(a - passA) < PASS_FLOOR_HALF_M + 10f) continue;   // 切り通しの底は岩で縁取る（art-r3-08）
                bool lamp = post % 2 == 1;
                float sd = (post++ % 2 == 0) ? 1f : -1f;
                float lat = Mathf.Max(GUIDE_LATERAL, ROAD_MIN + 1f) + (StageDefinitions.HalfWidthAt(def, a) - HeightmapGen.RUNWAY_M);
                bool ok = lamp ? w.AddLampPost(G, w.Side(a, sd * lat), w.YawAt(a))   // art-r4-14
                               : w.AddPost(G, w.Side(a, sd * lat), w.YawAt(a), POST_H, POST_THICK);
                if (ok) lastPost = a;
            }
        }

        /// <summary>点 p が切り通しの底（中心の前後 PASS_FLOOR_HALF_M + 10m）か。</summary>
        static bool InPassCut(Work w, StageDef def, float goalArc, Vector3 p)
        {
            float pass = SetPieces.PassArc(def, goalArc);
            if (pass < 0f) return false;
            float d;
            return Mathf.Abs(NearestArc(w.course, w.cum, p, out d) - pass) < PASS_FLOOR_HALF_M + 10f;
        }

        /// <summary>
        /// 柵の線の並び（art-r2-03 / art-r3-02）：FarmProps_03・FarmProps_04・立てた丸太の杭（WoodLogs_01）の順に繰り返す。
        /// _03 と _04 は同じ柵（長い辺の軸がちがうだけ）なので、向きをそろえれば同じ線に見える。骨（DesertProps_03/05/06/10）は使わない。
        /// </summary>
        public static string FencePiece(int k)
        {
            int m = ((k % 3) + 3) % 3;
            return m == 0 ? "FarmProps_03" : (m == 1 ? "FarmProps_04" : POST_PART);
        }

        /// <summary>骨（あばら・頭骨 DesertProps_06/07/08）を置いてよいまとまり（art-r2-03）。1 ステージ BONE_MAX 個まで。</summary>
        public static readonly HashSet<string> BoneGroups = new HashSet<string> { "BoneField", "GiantSkeleton" };
        public const int BONE_MAX = 12;
        /// <summary>骨の部品か（art-r3-02：DesertProps_03/05 は杭ではなく大きな大腿骨。杭・柱には使わない）。</summary>
        public static bool IsBonePart(string part)
            => part == "DesertProps_03" || part == "DesertProps_05" || part == "DesertProps_06" || part == "DesertProps_07" || part == "DesertProps_08";

        /// <summary>ステージごとの遠くの目印の素材（部品）と拡大。</summary>
        public static void LandmarkAsset(int kind, out string asset, out string part, out float scale)
        {
            if (kind == HeightmapGen.KIND_DUNES) { asset = WTOWN; part = TOWN_TALL_A; scale = 1.4f; }
            else if (kind == HeightmapGen.KIND_MESA) { asset = WTOWN; part = TOWN_TALL_B; scale = 1.6f; }
            else { asset = RANCH; part = RANCH_TOWER; scale = 1.4f; }
        }

        /// <summary>
        /// 遠くの目印の場所（Terrain ローカル）：スタートの位置から進む向き ±30° の中で、ゴールの方向に一番近い向き、遠い順（700〜380m）に、
        /// Terrain の外周の尾根の内側（端から 70m）・コースから LANDMARK_MIN_ROAD_M 以上・除外の円の外の最初の点。angleDeg = 進む向きとの角度。
        /// </summary>
        public static bool LandmarkSpot(StageDef def, Vector3[] course, float goalArc, out Vector3 spot, out float angleDeg)
            => LandmarkSpot(def, course, goalArc, null, out spot, out angleDeg);

        /// <summary>スタートの目の高さ（地面 + 2.6m、馬の上）。</summary>
        public const float START_EYE_M = 2.6f;

        /// <summary>
        /// heights を渡すと、スタートの目の高さ（START_EYE_M）から目印のてっぺん（高さの 85%）が地形に隠れない場所だけを選ぶ（art-r3-11：
        /// 砂漠のスタートの先の砂丘の背が給水塔を隠していた）。見える場所が無ければ heights なしと同じ場所。
        /// </summary>
        public static bool LandmarkSpot(StageDef def, Vector3[] course, float goalArc, float[,] heights, out Vector3 spot, out float angleDeg)
        {
            float s = StageDefinitions.START_ARC_M;
            Vector3 p0 = WorldLayout.PointAtArc(course, s), t0 = WorldLayout.TangentAtArc(course, s);
            Vector3 g = WorldLayout.PointAtArc(course, goalArc) - p0;
            float goalAng = Mathf.DeltaAngle(WorldLayout.YawDeg(t0), WorldLayout.YawDeg(new Vector3(g.x, 0f, g.z).normalized));
            float pref = Mathf.Clamp(goalAng, -30f, 30f);
            var circles = StageExclusions(def, course, goalArc);
            var angles = new List<float>();
            for (float a = -35f; a <= 35.01f; a += 5f) angles.Add(a);
            angles.Sort((x, y) => Mathf.Abs(x - pref).CompareTo(Mathf.Abs(y - pref)));
            string asset, part; float sc;
            LandmarkAsset(def.kind, out asset, out part, out sc);
            float top = SizeOf(asset, part).h * sc * 0.85f;
            bool found = false, foundFlat = false;
            Vector3 first = Vector3.zero, flat = Vector3.zero; float firstAng = 0f, flatAng = 0f;
            float fr = FootprintRadiusFor(asset, "Landmark", SizeOf(asset, part).r * sc, 1f);
            foreach (float a in angles)
                for (float d = 700f; d >= 260f; d -= 20f)
                {
                    float yaw = (WorldLayout.YawDeg(t0) + a) * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(p0.x + Mathf.Sin(yaw) * d, 0f, p0.z + Mathf.Cos(yaw) * d);
                    if (p.x < 70f || p.z < 70f || p.x > StageDefinitions.SIZE - 70f || p.z > StageDefinitions.SIZE - 70f) continue;
                    if (WorldLayout.DistToPolylineXZ(p, course) < LANDMARK_MIN_ROAD_M) continue;
                    bool free = true;
                    foreach (var c in circles) if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + 10f) free = false;
                    if (!free) continue;
                    if (!found) { found = true; first = p; firstAng = a; }
                    if (heights == null) { spot = p; angleDeg = a; return true; }
                    // art-r5-03：塔は平らで縁でない地面だけ（風車の台座が尾根の縁から宙にはみ出していた）
                    if (!TowerSiteOk(heights, StageDefinitions.SIZE, p, fr, def.kind == HeightmapGen.KIND_DUNES)) continue;
                    if (!foundFlat) { foundFlat = true; flat = p; flatAng = a; }
                    if (LandmarkVisible(heights, p0, p, top)) { spot = p; angleDeg = a; return true; }
                }
            if (foundFlat) { spot = flat; angleDeg = flatAng; return true; }
            spot = first; angleDeg = firstAng;
            return found;
        }

        /// <summary>スタートの点 start（y 無視）の目の高さから、点 p の地面 + topM のてっぺんが地形（0.5m の余裕）に隠れないか。</summary>
        public static bool LandmarkVisible(float[,] h, Vector3 start, Vector3 p, float topM)
        {
            float H = HeightmapGen.HEIGHT_M, S = StageDefinitions.SIZE;
            float y0 = WorldLayout.SampleHeight(h, S, H, start.x, start.z) + START_EYE_M;
            float y1 = WorldLayout.SampleHeight(h, S, H, p.x, p.z) + topM;
            float d = WorldLayout.DistXZ(start, p);
            for (float s = 6f; s < d - 20f; s += 4f)
            {
                float f = s / d;
                float x = start.x + (p.x - start.x) * f, z = start.z + (p.z - start.z) * f;
                if (WorldLayout.SampleHeight(h, S, H, x, z) > y0 + (y1 - y0) * f + 0.5f) return false;
            }
            return true;
        }

        /// <summary>遠くの目印（給水塔・高い建物）を 1 つ。DressingBuilder が煙の柱と灯りを足す。</summary>
        static void Landmark(Work w, StageDef def, float goalArc)
        {
            Vector3 p; float ang;
            if (!LandmarkSpot(def, w.course, goalArc, w.h, out p, out ang)) return;
            string asset, part; float sc;
            LandmarkAsset(def.kind, out asset, out part, out sc);
            Vector3 toStart = WorldLayout.PointAtArc(w.course, StageDefinitions.START_ARC_M) - p;
            w.Add("Landmark", asset, part, p, WorldLayout.YawDeg(new Vector3(toStart.x, 0f, toStart.z).normalized), sc, 0.3f, 0f,
                background: true, ignoreCircles: true, fixedSpot: true, rOverlap: 0f);   // LandmarkSpot が平らで見える場所を選んだ
        }

        /// <summary>
        /// 砂漠の踏切（course-r2-04）：SetPieces.RailRuns の場所（序盤の道の内側の固い路盤）に線路（95.6m）を 2 本つなぎ、線路に沿って除外の円を足す
        /// （他の飾りが線路に乗らない）。道の外の両脇に枕木の山（WoodLogs）、左の縁に杭の列（DesertProps_10）。
        /// </summary>
        static void Railway(Work w, StageDef def, float goalArc)
        {
            var runs = SetPieces.RailRuns(def, w.course, goalArc);
            if (runs.Count > 0)
            {
                float open = SetPieces.OpeningArc(def, goalArc), half = SetPieces.RAIL_LENGTH * SetPieces.RAIL_RUNS * 0.5f;
                for (float a = open - half; a <= open + half; a += 12f)
                    w.Add("Railway", DPROPS, "DesertProps_10", w.Side(a, -(StageDefinitions.HalfWidthAt(def, a) + 2.5f)), w.YawAt(a), 1f, 0.02f, ROAD_MIN, ignoreCircles: true);
            }
            foreach (var run in runs)
            {
                Vector3 a = run[0], b = run[1];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                if (!w.Add("Railway", WTOWN, RAIL_TRACK, mid, WorldLayout.YawDeg(d), 1f, 0.15f, -1000f, ignoreCircles: true, rBounds: 2f, rOverlap: 0.5f)) continue;
                w.list[w.list.Count - 1].collider = false;   // 低い線路は乗り越えられる（当たり判定なし）
                w.list[w.list.Count - 1].shape = SHAPE_NONE;
                for (int k = 0; k <= 16; k++)
                {
                    Vector3 q = Vector3.Lerp(a, b, k / 16f);
                    w.circles.Add(new Vector4(q.x, 0f, q.z, 3.5f));
                }
                // 線路の端に枕木の山（薪）と車止めがわりの岩
                Vector3 outR = WorldLayout.RightOf(d) * (HeightmapGen.RUNWAY_M - SetPieces.RAIL_LATERAL + 5f);
                w.Add("Railway", LOGS, w.Pick(LogParts), a + d * 8f + outR, w.Rand(0f, 360f), 1.6f, 0.02f, ROAD_MIN, ignoreCircles: true);
                w.Add("Railway", LOGS, w.Pick(LogParts), mid + outR, w.Rand(0f, 360f), 1.6f, 0.02f, ROAD_MIN, ignoreCircles: true);
                w.Add("Railway", DROCKS, w.Pick(RockSmall), b - d * 4f + outR, w.Rand(0f, 360f), 1.2f, 0.3f, ROAD_MIN, ignoreCircles: true);
            }
        }

        public const float MESA_GATE_SCALE = 2.4f;     // art-r3-08：300m 先から見える門
        public const float PASS_FLOOR_HALF_M = 45f;    // 切り通しの中（前後 ±45m）の道ばたの岩

        /// <summary>
        /// 境目のメサの門（art-r3-08）：切り通し（SetPieces.PassArc）の両脇の尾根の上にメサ ×2.4 とメサ B ×2.4、その手前の尾根に焚き火 2 つ
        /// （DressingBuilder が煙の柱と暖色の灯り 18m を足す＝300m 先の目印）。切り通しの底の道ばたは小さな岩（DesertRocks_10〜15）で縁取る（杭は立てない）。
        /// 道のすぐ脇なので見えない壁（外形の箱）ではなく、メッシュの当たり判定（道から遠い部分も正確）。
        /// </summary>
        static void MesaGate(Work w, StageDef def, float goalArc)
        {
            float pass = SetPieces.PassArc(def, goalArc);
            if (pass < 0f) return;
            float yaw = w.YawAt(pass);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                string asset = sd < 0 ? MESA : MESAB;
                Size sz = SizeOf(asset, null);
                float r = sz.r * MESA_GATE_SCALE;
                float lat = SetPieces.PASS_CREST_FROM + 6f + r * MESA_GATE_FOOTPRINT;
                w.Add("MesaGate", asset, null, w.Side(pass + sd * 6f, sd * lat), yaw + (sd < 0 ? 0f : 180f), MESA_GATE_SCALE, 0.18f * sz.h * MESA_GATE_SCALE, ROAD_MIN,
                    rBounds: r * MESA_GATE_FOOTPRINT, rOverlap: r * 0.4f);
                // 尾根の上（メサの手前）の焚き火（煙の柱の目印）と薪
                Vector3 f = w.Side(pass - 10f, sd * (SetPieces.PASS_CREST_FROM + 2.5f));
                w.Add("MesaGate", CAMPFIRE, "Campfire_01", f, 0f, 1.2f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add("MesaGate", LOGS, LogParts[2 + (sd > 0 ? 1 : 0)], new Vector3(f.x + 1.8f, 0f, f.z + 0.6f), yaw + 30f, 1.3f, 0.02f, ROAD_MIN, ignoreCircles: true);
            }
            PassPortal(w, def, pass);
            PassWallFaces(w, def, pass);
            // 切り通しの底の道ばた：小さな岩（6m ごと、左右）
            int k = 0;
            for (float a = pass - PASS_FLOOR_HALF_M; a <= pass + PASS_FLOOR_HALF_M; a += 6f, k++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    string part = k % 2 == 0 ? RockSmall[(k + (sd > 0 ? 3 : 0)) % RockSmall.Length] : DeadAndRocks[(k * 2 + (sd > 0 ? 1 : 0)) % DeadAndRocks.Length];
                    float sc = w.Rand(0.6f, 0.9f);
                    float lat = StageDefinitions.HalfWidthAt(def, a) + 1.1f + SizeOf(DROCKS, part).r * sc + (k % 3) * 0.4f;
                    w.Add("PassFloor", DROCKS, part, w.Side(a + w.Rand(-1.5f, 1.5f), sd * lat), w.Rand(0f, 360f), sc, 0.25f * SizeOf(DROCKS, part).h * sc, ROAD_MIN);
                }
        }

        // ---- course-r3-02：切り通しを手前から読ませる（門の岩・中の灯り・先の目印） ----
        public const float PASS_PORTAL_BEFORE_M = 10f, PASS_PORTAL_TURN_DEG = 20f;
        public const float PASS_LAMP_STEP_M = 15f, PASS_LAMP_OUT_M = 2f;
        public const float PASS_BEACON_FROM_M = 120f, PASS_BEACON_TO_M = 180f;

        /// <summary>
        /// 切り通しの合図（course-r3-02）：(a) 切り通しの口の 10m 手前、壁の足元（PASS_WALL_FROM）の両脇に縦に伸ばした大岩（DesertRocks_01/02 ×0.55、縦 ×1.6）を
        /// 外へ 20° 開いて門にする（崖の板 Cliffs は近くで平らな板に見えるので使わない：art-r3-05）、(b) 切り通しの中の両脇（道の縁 + 2m）に 15m ごとの街灯
        /// （暖色の灯り 12m、DressingBuilder が付ける：group PassLamp）、(c) 切り通しの出口から道の延長線の 120〜180m 先に給水塔（RanchCompound_11 ×1.2）と焚き火の煙
        /// （260m 手前から切れ込みの真ん中に見える目印、group PassBeacon）。
        /// </summary>
        static void PassPortal(Work w, StageDef def, float pass)
        {
            float mouth = pass - SetPieces.PASS_HALF_CUT - PASS_PORTAL_BEFORE_M;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                string part = sd < 0 ? "DesertRocks_01" : "DesertRocks_02";
                const float SC = 0.55f, SY = 1.6f;
                Size cs = SizeOf(DROCKS, part);
                float r = cs.r * SC;
                for (int tries = 0; tries < 6; tries++)
                {
                    float lat = Mathf.Max(SetPieces.PASS_WALL_FROM, StageDefinitions.HalfWidthAt(def, mouth) + ROAD_MIN - HeightmapGen.RUNWAY_M + r * STRETCHED_ROCK_FOOTPRINT + 1f) + tries * 2.5f;
                    if (w.Add("MesaGate", DROCKS, part, w.Side(mouth - tries * 3f, sd * lat), w.YawAt(mouth) + sd * PASS_PORTAL_TURN_DEG + (sd < 0 ? 0f : 180f), SC,
                        CLIFF_SINK * cs.h * SC * SY * 0.5f, ROAD_MIN, rBounds: r * STRETCHED_ROCK_FOOTPRINT, scaleY: SY, ignoreCircles: true)) break;
                }
            }
            // (b) 切り通しの中の街灯
            for (float a = pass - SetPieces.PASS_HALF_CUT - 10f; a <= pass + SetPieces.PASS_HALF_CUT + 10f; a += PASS_LAMP_STEP_M)
                for (int sd = -1; sd <= 1; sd += 2)
                    w.AddLampPost("PassLamp", w.Side(a + (sd > 0 ? 7.5f : 0f), sd * (StageDefinitions.HalfWidthAt(def, a) + PASS_LAMP_OUT_M)), w.YawAt(a) + (sd > 0 ? -90f : 90f));   // art-r4-04/14
            // art-r5-04：入口（口）の両脇に灯りの柱の対（暖色の灯り。切り通しの入口を額縁にする）
            {
                float ma = pass - SetPieces.PASS_HALF_CUT - 18f;
                for (int sd = -1; sd <= 1; sd += 2)
                    w.AddLampPost("PassLamp", w.Side(ma, sd * (StageDefinitions.HalfWidthAt(def, ma) + PASS_LAMP_OUT_M)), w.YawAt(ma) + (sd > 0 ? -90f : 90f));
            }
            // (c) 出口の先の目印（道の延長線の上、道から離れた所）
            float exit = pass + SetPieces.PASS_HALF_CUT;
            Vector3 ep = WorldLayout.PointAtArc(w.course, exit), et = WorldLayout.TangentAtArc(w.course, exit);
            et = new Vector3(et.x, 0f, et.z).normalized;
            for (int tries = 0; tries < 64; tries++)   // art-r5-03：塔は平らな足元まで広く探す
            {
                float d = Mathf.Lerp(PASS_BEACON_FROM_M - 15f, PASS_BEACON_TO_M + 5f, (tries % 8) / 7f);
                float off = ((tries / 8 + 1) / 2) * 7f * ((tries / 8) % 2 == 0 ? 1f : -1f);
                Vector3 p = ep + et * d + WorldLayout.RightOf(et) * off;
                p.y = 0f;
                if (w.Add("PassBeacon", RANCH, RANCH_TOWER, p, WorldLayout.YawDeg(-et), 1.2f, 0.3f, ROAD_CLEAR + 4f, background: true))
                {
                    Vector3 f = p - et * 7f + WorldLayout.RightOf(et) * 4f;
                    w.Add("PassBeacon", CAMPFIRE, "Campfire_01", f, 0f, 1.2f, 0.05f, ROAD_CLEAR, ignoreCircles: true);
                    break;
                }
            }
        }

        // ---- course-r3-03 → art-r4-04：境目の壁の面（段の付いた岩の積み重ね） ----
        public const float WALL_FACE_STEP_M = 6f;        // art-r4-04：12 → 6m（岩が途切れず段になる）
        public const float WALL_FACE_UPPER_AT = 0.6f;    // 中の段は壁の 60% の横（高さもおよそ 60%）
        public const float WALL_SCREE_MIN_EDGE_M = 4f;   // がれきは道の縁から 4m 以上
        public const float WALL_LOW_SCALE_MIN = 0.5f, WALL_LOW_SCALE_MAX = 0.7f, WALL_LOW_SCALE_Y = 1.6f, WALL_LOW_SINK = 0.3f;
        public const float WALL_MID_SCALE_MIN = 0.8f, WALL_MID_SCALE_MAX = 1.1f, WALL_MID_YAW_JITTER = 8f;
        public const float WALL_CREST_STEP_M = 18f, WALL_CREST_SCALE = 1.2f;
        static readonly string[] ScreeParts = { "RockSetStylized_01_Rock", "RockSetStylized_02_Rock", "RockSetStylized_06_Rock" };

        /// <summary>
        /// 壁の面の 1 か所（course-r3-03 → art-r4-04）：弧長 a・側 sd の壁（横 lat0〜lat1）に
        ///   下の段：縦に伸ばした大岩（DesertRocks_01〜03 ×0.5〜0.7、縦 ×1.6、広い面を道へ、高さの 30% を埋める）、
        ///   中の段：壁の 60% の横に、どの場所にもメサ B（×0.8〜1.1、向き = 道の向き ±8° で面が続く棚に）、
        ///   頂：WALL_CREST_STEP_M ごとに壁の上の縁へメサ B ×1.2（尾根の線を三角に切る。MesaRock は 250m 以内に置かない course-r3-12 の決まり）、
        ///   1 か所おきに壁の足元（道の縁 + 4m 以上）にがれき（RockSetStylized_01/02/06）。
        /// 浮いて見えた RockGray ×2.5 はやめた。当たりは付けない（柵が受け持つ：group WallFace）。
        /// </summary>
        static void WallFace(Work w, StageDef def, float a, int sd, float lat0, float lat1, int k, bool everySpot = true)
        {
            const string G = "WallFace";
            float roadYaw = w.YawAt(a);
            float yaw = roadYaw + (sd < 0 ? 90f : -90f);   // 広い面（ローカル ±X）を道へ（art-r1-03 と同じ）
            string bp = RockBig[k % RockBig.Length];
            float bsc = WALL_LOW_SCALE_MIN + (WALL_LOW_SCALE_MAX - WALL_LOW_SCALE_MIN) * ((k * 7) % 5) / 4f;
            Size bs = SizeOf(DROCKS, bp);
            float br = bs.r * bsc;
            float bl = Mathf.Max(lat0, StageDefinitions.HalfWidthAt(def, a) + ROAD_MIN - HeightmapGen.RUNWAY_M + br * STRETCHED_ROCK_FOOTPRINT + 0.5f);
            w.Add(G, DROCKS, bp, w.Side(a, sd * bl), yaw + ((k % 3) - 1) * 6f, bsc, WALL_LOW_SINK * bs.h * bsc * WALL_LOW_SCALE_Y, ROAD_MIN,
                rBounds: br * STRETCHED_ROCK_FOOTPRINT, rOverlap: 0f, scaleY: WALL_LOW_SCALE_Y);
            // 中の段：どの場所にも岩の棚。切り通し（everySpot）はメサ B と縦に伸ばした大岩 DesertRocks_01〜03 ×1.0 を交互、台地の縁は 1 か所おきにメサ B
            // （案はどの場所もメサ B だが、メサ B が 1 つの素材の上限 8% を超え、尾根のシルエットのメサが置けなくなるため交互にした）
            if (everySpot && k % 4 >= 2)
            {
                string mp = RockBig[(k + 2) % RockBig.Length];
                Size ds = SizeOf(DROCKS, mp);
                float dr = ds.r * 1.0f, ml = lat0 + (lat1 - lat0) * WALL_FACE_UPPER_AT;
                w.Add(G, DROCKS, mp, w.Side(a + 3f, sd * Mathf.Max(ml, StageDefinitions.HalfWidthAt(def, a) + ROAD_MIN - HeightmapGen.RUNWAY_M + dr * STRETCHED_ROCK_FOOTPRINT + 0.5f)),
                    yaw + w.Rand(-WALL_MID_YAW_JITTER, WALL_MID_YAW_JITTER), 1.0f, CLIFF_SINK * ds.h * 1.2f, ROAD_MIN, rBounds: dr * STRETCHED_ROCK_FOOTPRINT, rOverlap: 0f, scaleY: 1.2f);
            }
            else if (everySpot || k % 4 == 0)
            {
            float msc = WALL_MID_SCALE_MIN + (WALL_MID_SCALE_MAX - WALL_MID_SCALE_MIN) * ((k * 3) % 4) / 3f;
            Size ms = SizeOf(MESAB, null);
            float mr = ms.r * msc;
            float ul = lat0 + (lat1 - lat0) * WALL_FACE_UPPER_AT;
            w.Add(G, MESAB, null, w.Side(a + 3f, sd * Mathf.Max(ul, StageDefinitions.HalfWidthAt(def, a) + ROAD_MIN - HeightmapGen.RUNWAY_M + mr * MESA_GATE_FOOTPRINT + 0.5f)),
                roadYaw + (sd < 0 ? 0f : 180f) + w.Rand(-WALL_MID_YAW_JITTER, WALL_MID_YAW_JITTER), msc, CLIFF_SINK * ms.h * msc, ROAD_MIN,
                rBounds: mr * MESA_GATE_FOOTPRINT, rOverlap: 0f);
            }
            if (k % 2 == 1)
            {
                string sp = ScreeParts[k / 2 % ScreeParts.Length];
                float edge = StageDefinitions.HalfWidthAt(def, a) + WALL_SCREE_MIN_EDGE_M + SizeOf(RSTYL, sp).r * 1.3f;
                w.Add(G, RSTYL, sp, w.Side(a - 3f, sd * edge), w.Rand(0f, 360f), 1.3f, 0.1f, ROAD_MIN);
            }
        }

        /// <summary>
        /// 壁の頂（art-r4-04）：WALL_CREST_STEP_M ごと、壁の上の縁（lat1）に縦に伸ばした大岩（DesertRocks_01〜03 ×WALL_CREST_SCALE × 0.5、縦 ×1.5）。
        /// 案の MesaRock は道から 250m 以内に置かない決まり（course-r3-12）なので、尾根の線を三角に切る大岩にした。
        /// </summary>
        static void WallCrest(Work w, StageDef def, float a, int sd, float lat1, int k)
        {
            string part = RockBig[(k + 1) % RockBig.Length];
            float sc = WALL_CREST_SCALE * 0.5f;
            Size cs = SizeOf(DROCKS, part);
            float r = cs.r * sc;
            w.Add("WallFace", DROCKS, part, w.Side(a, sd * (lat1 + r * STRETCHED_ROCK_FOOTPRINT * 0.3f)), w.YawAt(a) + (sd < 0 ? 90f : -90f) + w.Rand(-20f, 20f), sc,
                CLIFF_SINK * cs.h * sc * 1.5f, ROAD_MIN, rBounds: r * STRETCHED_ROCK_FOOTPRINT, rOverlap: 0f, background: true, scaleY: 1.5f);
        }

        // ---- art-r5-04：切り通しの壁は 3 段の大きな岩の重なり ----
        public const float WALL_TIER_STEP_M = 9f;
        /// <summary>段の高さ（道からの m）：下の段・+7m・+14m。</summary>
        public static readonly float[] WALL_TIER_H = { 0.5f, 7f, 14f };
        /// <summary>壁の面から道の側へ引き出す量（× 外形の半径）：岩の厚さのおよそ 45% が壁から出る（前の CLIFF_SINK 0.22 では 60° の壁にほとんど埋まり、薄いかけらに見えた）。</summary>
        public const float WALL_TIER_PULL = 0.3f;
        public const float WALL_TIER_MESAB_MIN = 0.42f, WALL_TIER_MESAB_MAX = 0.55f;   // 中の段：メサ B（外形 25m・高さ 30m）× 0.42〜0.55 = 高さ 13〜17m
        public const float WALL_TIER_ROCK_MIN = 0.6f, WALL_TIER_ROCK_MAX = 0.85f;      // 下・上の段：大岩 DesertRocks_01〜03

        /// <summary>
        /// 境目の切り通しの両脇の壁（course-r3-03 → art-r4-04 → art-r5-04）：WALL_TIER_STEP_M ごとに、両側の壁に 3 段（道 + 0.5m・+7m・+14m の高さの所）。
        /// 下と上の段は大岩（DesertRocks_01〜03、縦に少し伸ばす）、中の段はメサ B。広い面を道へ（art-r1-03）、壁から厚さの 45% を道の側へ出す。
        /// 色は段ごとに明・暗・明（ArtPalette.WallTierLight / WallTierDark：StrataLightBorder / StrataDarkBorder の比）で、なめらかな地形の面ではなく段の付いた岩の壁に見せる。
        /// Cliffs（崖の板）は道から 260m 以内に置かない決まり（art-r3-05：近くでは平らな箱に見える）なので使わない。1 か所おきに壁の足元にがれき（RockSetStylized）、頂は WallCrest。
        /// </summary>
        static void PassWallFaces(Work w, StageDef def, float pass)
        {
            const string G = "WallFace";
            int k = 0;
            float a0 = pass - SetPieces.PASS_HALF_CUT - 25f, a1 = pass + SetPieces.PASS_HALF_CUT + 25f;
            for (float a = a0; a <= a1; a += WALL_TIER_STEP_M, k++)
            {
                Vector3 c = WorldLayout.PointAtArc(w.course, a);
                float roadY = w.Ground(c.x, c.z), hw = StageDefinitions.HalfWidthAt(def, a), roadYaw = w.YawAt(a);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    int kk = k * 2 + (sd > 0 ? 1 : 0);
                    for (int tier = 0; tier < WALL_TIER_H.Length; tier++)
                    {
                        float lat = WallLatAt(w, a, sd, roadY + WALL_TIER_H[tier], hw + 1f, SetPieces.PASS_WALL_TO + SetPieces.PASS_WALL_FADE);
                        if (lat < 0f) continue;   // 切り通しの端：壁がその高さまで無い
                        bool mesa = tier == 1;
                        string asset = mesa ? MESAB : DROCKS, part = mesa ? null : RockBig[(kk + tier) % RockBig.Length];
                        float t01 = ((kk * 7 + tier * 3) % 5) / 4f;
                        float sc = mesa ? Mathf.Lerp(WALL_TIER_MESAB_MIN, WALL_TIER_MESAB_MAX, t01) : Mathf.Lerp(WALL_TIER_ROCK_MIN, WALL_TIER_ROCK_MAX, t01);
                        float sy = mesa ? 1f : (tier == 0 ? 1.3f : 1.2f);
                        Size sz = SizeOf(asset, part);
                        float r = sz.r * sc, foot = r * (mesa ? MESA_GATE_FOOTPRINT : STRETCHED_ROCK_FOOTPRINT);
                        float minLat = hw + ROAD_MIN - HeightmapGen.RUNWAY_M + foot + 0.5f;
                        float place = Mathf.Max(minLat, lat - WALL_TIER_PULL * r + foot * 0.5f);
                        float yaw = mesa ? roadYaw + (sd < 0 ? 0f : 180f) : roadYaw + (sd < 0 ? 90f : -90f);
                        if (!w.Add(G, asset, part, w.Side(a + (tier - 1) * 2.5f, sd * place), yaw + w.Rand(-WALL_MID_YAW_JITTER, WALL_MID_YAW_JITTER), sc,
                                0.12f * sz.h * sc * sy, ROAD_MIN, rBounds: foot, rOverlap: 0f, scaleY: sy, fixedSpot: true))
                            continue;
                        w.list[w.list.Count - 1].tint = mesa ? ArtPalette.WallTierDark : ArtPalette.WallTierLight;
                    }
                    if (kk % 2 == 1)
                    {
                        string sp = ScreeParts[kk / 2 % ScreeParts.Length];
                        float edge = hw + WALL_SCREE_MIN_EDGE_M + SizeOf(RSTYL, sp).r * 1.3f;
                        w.Add(G, RSTYL, sp, w.Side(a - 3f, sd * edge), w.Rand(0f, 360f), 1.3f, 0.1f, ROAD_MIN);
                    }
                }
            }
            int cc = 0;
            for (float a = a0 + 4f; a <= a1; a += WALL_CREST_STEP_M, cc++)
                for (int sd = -1; sd <= 1; sd += 2)
                    WallCrest(w, def, a, sd, SetPieces.PASS_WALL_TO, cc * 2 + (sd > 0 ? 1 : 0));
        }

        /// <summary>弧長 a・側 sd で、地面が y 以上になる最初の横の距離（m、latMin〜latMax を 1m ごと）。届かなければ −1。</summary>
        static float WallLatAt(Work w, float a, int sd, float y, float latMin, float latMax)
        {
            for (float lat = latMin; lat <= latMax; lat += 1f)
            {
                Vector3 q = w.Side(a, sd * lat);
                if (w.Ground(q.x, q.z) >= y) return lat;
            }
            return -1f;
        }

        // ---- art-r4-04：切り通しの壁の灯り（日の当たる側の段を光で分ける） ----
        public const int PASS_WALL_LIGHTS_PER_SIDE = 2;
        /// <summary>art-r5-04：日の当たる側の壁だけに 2 つ（切り通しの 1/3 と 2/3）、太陽の色・範囲 25m・強さ 1.5（壁の段を照らす。前は両側に 45m・0.5）。</summary>
        public const float PASS_WALL_LIGHT_RANGE = 25f, PASS_WALL_LIGHT_INTENSITY = 1.5f, PASS_WALL_LIGHT_AT = 0.5f;

        /// <summary>
        /// 切り通しの壁の灯り（art-r4-04）：両側の壁にそれぞれ 2 つ、壁の真ん中（横 = 壁の 50%）の切り通しの中心 ±PASS_HALF_CUT/2 に。
        /// 返す点は Terrain ローカル (x, z) と、y = 壁の高さの割合（DressingBuilder が道の高さ + 割合 × 壁の高さに置く）。境目以外は空。
        /// </summary>
        public static List<Vector3> PassWallLights(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector3>();
            float pass = SetPieces.PassArc(def, goalArc);
            if (pass < 0f) return r;
            float lat = SetPieces.PASS_WALL_FROM + (SetPieces.PASS_WALL_TO - SetPieces.PASS_WALL_FROM) * 0.5f;
            int sun = SunFacingWallSide(def, course, pass);
            for (int i = 0; i < PASS_WALL_LIGHTS_PER_SIDE; i++)
            {
                float a = pass - SetPieces.PASS_HALF_CUT + 2f * SetPieces.PASS_HALF_CUT * (i + 1) / (PASS_WALL_LIGHTS_PER_SIDE + 1f);   // 1/3・2/3
                Vector3 t = WorldLayout.TangentAtArc(course, a);
                // 壁の手前（道の側）に寄せて、日の当たる面を照らす
                Vector3 p = WorldLayout.PointAtArc(course, a) + WorldLayout.RightOf(t) * (sun * (lat - 6f));
                r.Add(new Vector3(p.x, PASS_WALL_LIGHT_AT, p.z));
            }
            return r;
        }

        /// <summary>切り通しの日の当たる側の壁（+1 = 右、−1 = 左）：壁の面の道へ向く向きが太陽の方を向く側。</summary>
        public static int SunFacingWallSide(StageDef def, Vector3[] course, float pass)
        {
            Vector3 e = LightingPlan.ForStage(def.index).sunEuler;
            // Quaternion.Euler(x, y, 0) * forward = (sin y · cos x, −sin x, cos y · cos x)。太陽へ向かう向きはその逆
            float px = e.x * Mathf.Deg2Rad, py = e.y * Mathf.Deg2Rad;
            Vector3 toSun = new Vector3(-Mathf.Sin(py) * Mathf.Cos(px), Mathf.Sin(px), -Mathf.Cos(py) * Mathf.Cos(px));
            Vector3 right = WorldLayout.RightOf(WorldLayout.TangentAtArc(course, pass));
            // 右の壁の面は道（−右）を向く
            return (-right.x * toSun.x - right.z * toSun.z) > 0f ? 1 : -1;
        }

        // ---- art-r4-18：砂漠の道の近くの暮らしの跡 ----
        public const float DUNE_WRECK_STEP_M = 300f, DUNE_WRECK_LAT_MIN = 12f, DUNE_WRECK_LAT_MAX = 20f, DUNE_WRECK_SINK = 0.4f, DUNE_WRECK_TILT = 18f;
        public const float CARAVAN_ROAD_MAX_M = 25f;
        /// <summary>art-r5-03：ラクダ・隊列はこの傾き（°）より急な砂丘の面には置かない。</summary>
        public const float CARAVAN_MAX_SLOPE_DEG = 12f;
        public const int CARAVAN_CAMELS = 3;

        /// <summary>
        /// 砂漠の近景（art-r4-18）：300m ごとに道の縁から 12〜20m に半分埋まった荷車（WesternTown_115、高さの 40% を埋め 18° 傾ける）と骨、
        /// コースの 35%（オアシスの横）に道から 25m 以内を行くラクダの隊列（Camel.fbx 3 頭、アニメーターなし）。
        /// </summary>
        static void DuneNearLife(Work w, StageDef def, float goalArc)
        {
            const string G = "DuneWreck";
            int k = 0;
            for (float a = 250f; a < goalArc - 150f; a += DUNE_WRECK_STEP_M, k++)
            {
                if (ShoulderSkip(def, goalArc, a)) continue;
                for (int tries = 0; tries < 4; tries++)
                {
                    float sd = ((k + tries) % 2 == 0) ? 1f : -1f;
                    float lat = StageDefinitions.HalfWidthAt(def, a) + w.Rand(DUNE_WRECK_LAT_MIN, DUNE_WRECK_LAT_MAX);
                    Vector3 p = w.Side(a + tries * 4f, sd * lat);
                    Size cs = SizeOf(WTOWN, TOWN_CART);
                    if (!w.Add(G, WTOWN, TOWN_CART, p, w.YawAt(a) + w.Rand(-60f, 60f), 1f, DUNE_WRECK_SINK * cs.h, ROAD_MIN, roll: DUNE_WRECK_TILT * (tries % 2 == 0 ? 1f : -1f), fixedSpot: true))
                        continue;
                    // art-r5-03：骨は骨のまとまりだけ（art-r3-06）。荷車の脇は草と小石
                    w.Add(G, DPROPS, DpGrass[k % DpGrass.Length], p + new Vector3(2.2f, 0f, 1.4f), w.Rand(0f, 360f), 1.1f, 0.05f, ROAD_MIN);
                    w.Add(G, RCOLL, Pebbles[k % Pebbles.Length], p + new Vector3(-1.8f, 0f, -1.6f), w.Rand(0f, 360f), 1f, 0.05f, ROAD_MIN);
                    break;
                }
            }
            // ラクダの隊列：オアシスの横（35%）、道の縁から 8〜20m（中心から 25m 以内）を道に沿って
            float ca = goalArc * 0.35f - 30f;
            for (int i = 0; i < CARAVAN_CAMELS; i++)
            {
                float a = ca + i * 6f;
                float lat = Mathf.Clamp(StageDefinitions.HalfWidthAt(def, a) + 8f, 19.5f, CARAVAN_ROAD_MAX_M - 3f);
                for (int tries = 0; tries < 12; tries++)   // art-r5-03：平らな所まで道に沿って探す（砂丘の面に浮かせない）
                    if (w.Add("Caravan", CAMEL, null, w.Side(a + tries * 5f, -Mathf.Min(CARAVAN_ROAD_MAX_M - 1f, lat + (tries % 3) * 1.2f)), w.YawAt(a) + w.Rand(-6f, 6f), 1f, 0.02f, ROAD_MIN, fixedSpot: true)) break;
            }
        }

        static void Dunes(Work w, StageDef def, float goalArc)
        {
            DuneNearLife(w, def, goalArc);
            // オアシス（コースの 35%、右へ 45m 前後）：テント・野営地・木の林（Trees＋アカシア 25 本以上）
            {
                Vector3 c;
                if (FindBeside(w, goalArc * 0.35f, 45f, 58f, 16f, out c))
                {
                    const string G = "Oasis";
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (i * 120f) * Mathf.Deg2Rad;
                        w.Add(G, TENT, null, new Vector3(c.x + Mathf.Sin(a) * 11f, 0f, c.z + Mathf.Cos(a) * 11f), i * 120f + 180f, 1f, 0.1f);
                    }
                    WagonCamp(w, c, 0f, false);
                    int trees = 0;
                    for (int ring = 0; ring < 4 && trees < OASIS_TREES + 4; ring++)
                        trees += Grove(w, G, c, 22f + ring * 10f, 12, 0f, 0.45f);
                    for (int i = 0; i < 6; i++) w.Add(G, DPROPS, "DesertProps_23", w.Around(c, 16f), w.Rand(0f, 360f), 1f, 0.05f);
                }
            }
            // 牧場（中継地点、コースの 75%、道から 60〜80m）と木
            {
                Vector3 c;
                float rr = SizeOf(RANCH, null).r;
                if (FindBeside(w, goalArc * 0.75f, ROAD_CLEAR + rr + 4f, ROAD_CLEAR + rr + 25f, rr, out c))
                {
                    const string G = "RanchWaypoint";
                    w.Add(G, RANCH, null, c, w.YawAt(goalArc * 0.75f) + 90f, 1f, 0.3f);
                    for (int i = 0; i < 8; i++) w.Add(G, i % 2 == 0 ? HAY : FARM, i % 2 == 0 ? w.Pick(HayParts) : w.Pick(FarmBarrels), w.Around(c, rr + 12f), w.Rand(0f, 360f), 1f, 0.05f);
                    Grove(w, G, c, rr + 22f, 8, 0f, 0.5f);
                }
            }
            // 幌馬車の野営地（2 か所）
            for (int k = 0; k < 2; k++)
            {
                Vector3 c;
                if (FindBeside(w, goalArc * (0.15f + 0.4f * k), 26f, 34f, 10f, out c)) WagonCamp(w, c, w.YawAt(goalArc * (0.15f + 0.4f * k)), true);
            }
            // 骨の原（4 か所、骨の馬が横たわる）
            foreach (var c in w.Centers(def.seed + 3, 4, 24f, 70f, 150f))
            {
                w.Add("BoneField", SKELETON, null, c, w.Rand(0f, 360f), 1f, 0.15f, roll: 85f);
                // 頭骨 1・あばら 1 と骨 4（あばら・頭骨はステージで BONE_MAX まで、art-r2-03）
                for (int i = 0; i < 6; i++) w.Add("BoneField", DPROPS, i == 0 ? "DesertProps_07" : (i == 3 ? "DesertProps_08" : "DesertProps_14"), w.Around(c, 10f), w.Rand(0f, 360f), w.Rand(0.9f, 1.3f), 0.05f);
                Tree(w, "BoneField", DEADTREE, null, w.Around(c, 14f));
            }
            // サボテンの林（10 か所）
            foreach (var c in w.Centers(def.seed + 1, 10, 24f, 150f, 80f))
            {
                w.Add("CactusForest", CGROUP, null, c, w.Rand(0f, 360f), w.Rand(0.9f, 1.3f), 0.2f, trunk: true);
                for (int i = 0; i < 10; i++)
                {
                    if (i % 3 == 2) w.Add("CactusForest", DPROPS, w.Pick(DpCacti), w.Around(c, 16f), w.Rand(0f, 360f), w.Rand(0.9f, 1.4f), 0.1f, trunk: true);
                    else w.Add("CactusForest", CACTI, w.Pick(CactiParts), w.Around(c, 18f), w.Rand(0f, 360f), w.Rand(0.8f, 1.3f), 0.1f, trunk: true);
                }
            }
            // 枯れ木（散らばる、30 本以上）
            foreach (var c in w.Centers(def.seed + 6, 24, 22f, 200f, 50f)) { Tree(w, "DeadTrees", DEADTREE, null, c); Tree(w, "DeadTrees", DEADTREE, null, w.Around(c, 10f)); }
            // 道沿いの帯（サボテン・サボテンの群れ・枯れ木・岩）
            RoadsideBand(w, goalArc, x =>
            {
                int k = x.rng.Next(10);
                if (k < 5) return new[] { CACTI, x.Pick(CactiParts) };
                if (k < 7) return new[] { CGROUP, null };
                if (k < 9) return new[] { DEADTREE, null };
                return new[] { DROCKS, x.Pick(RockMid) };
            });
            // 砂丘の草（まとまり）
            foreach (var c in w.Centers(def.seed + 2, 12, 20f, 120f, 50f))
                for (int i = 0; i < 4; i++) w.Add("DuneGrass", DPROPS, w.Pick(DpGrass), w.Around(c, 10f), w.Rand(0f, 360f), w.Rand(0.8f, 1.2f), 0.05f);
            // 砂に埋もれた岩（まとまり）
            foreach (var c in w.Centers(def.seed + 4, 8, 26f, 140f, 90f))
                for (int i = 0; i < 5; i++)
                {
                    if (i == 0) w.Add("Rocks", DROCKS, w.Pick(RockMid), w.Around(c, 12f), w.Rand(0f, 360f), 1f, 0.8f);
                    else w.Add("Rocks", RCOLL, w.Pick(Pebbles), w.Around(c, 12f), w.Rand(0f, 360f), w.Rand(1.5f, 2.5f), 0.1f);
                }
        }

        static void Border(Work w, StageDef def, float goalArc)
        {
            // 台地の近景（art-r3-07）：道から 30m 以内に草・小さなサボテン・小石・岩（谷底の近景と同じ決まり）
            CanyonFloor(w, goalArc);
            // ゴールの手前の小さな集落（ゴールの 350m 手前、道の横 30〜40m）
            {
                Vector3 c;
                if (FindBeside(w, goalArc - 350f, 32f, 40f, 16f, out c))
                {
                    const string G = "Settlement";
                    float yaw = w.YawAt(goalArc - 350f);
                    w.Add(G, SALOON, null, c, yaw + 90f, 1f, 0.4f);
                    float rad = yaw * Mathf.Deg2Rad;
                    w.Add(G, TOWNBLOCK, null, new Vector3(c.x + Mathf.Sin(rad) * 18f, 0f, c.z + Mathf.Cos(rad) * 18f), yaw + 90f, 1f, 0.4f);
                    for (int i = 0; i < 4; i++) w.Add(G, FARM, w.Pick(FarmFences), w.Around(c, 18f), w.Rand(0f, 360f), 1f, 0.05f);
                    for (int i = 0; i < 3; i++) w.Add(G, FARM, w.Pick(FarmCrates), w.Around(c, 14f), w.Rand(0f, 360f), 1f, 0.05f);
                    for (int i = 0; i < 2; i++) w.Add(G, FARM, w.Pick(FarmBarrels), w.Around(c, 14f), w.Rand(0f, 360f), 1f, 0.05f);
                    w.Add(G, FARM, FARM_LAMP, w.Around(c, 10f), 0f, 1f, 0.05f);
                    for (int i = 0; i < 3; i++) w.Add(G, HAY, w.Pick(HayParts), w.Around(c, 16f), w.Rand(0f, 360f), 1f, 0.05f);
                    for (int i = 0; i < 3; i++) w.Add(G, LOGS, w.Pick(LogParts), w.Around(c, 14f), w.Rand(0f, 360f), 1f, 0.02f);
                    Grove(w, G, c, 30f, 6, 0f, 0.5f);
                }
            }
            // メサの輪（背景、道から 110〜300m、見えない壁）と足元の枯れ木
            foreach (var c in w.Centers(def.seed + 1, 14, 110f, 300f, 90f))
            {
                bool b = w.Pick(2) == 1;
                float sc = w.Rand(1.4f, 2.2f);
                if (!w.Add("MesaRing", b ? MESAB : MESA, null, c, w.Rand(0f, 360f), sc, 3f, background: true)) continue;
                float mr = SizeOf(b ? MESAB : MESA, null).r * sc;
                for (int i = 0; i < 3; i++)
                {
                    float a = w.Rand(0f, Mathf.PI * 2f), d = mr + w.Rand(6f, 16f);
                    Tree(w, "MesaFeet", DEADTREE, null, new Vector3(c.x + Mathf.Sin(a) * d, 0f, c.z + Mathf.Cos(a) * d));
                }
            }
            // 厩舎（背景、遠く）
            {
                Vector3 c;
                if (FindBeside(w, goalArc * 0.3f, 150f, 200f, 10f, out c))
                    w.Add("FarStable", STABLE, null, c, w.Rand(0f, 360f), 1f, 0.1f, background: true);
            }
            // 道沿いの並び（25m ごと、左右）：夕景の乾いた土地（art-r2-04）＝枯れ木・サボテンの群れ・灰色の岩・岩のセット・町のサボテン、
            // 緑の木（Trees・アカシア）は 4 つに 1 つ（ステージで MESA_LUSH_MAX 本まで）
            int li = 0;
            for (float a = 20f; a < goalArc; a += 25f)
                for (int s = -1; s <= 1; s += 2, li++)
                {
                    Vector3 p = w.Side(a + w.Rand(-5f, 5f), s * w.Rand(22f, 32f));
                    switch (li % 8)
                    {
                        case 0: Tree(w, "TreeLine", TREES, TreePart(w), p); break;
                        case 1: case 5: Tree(w, "TreeLine", DEADTREE, null, p); break;
                        case 2: w.Add("TreeLine", CGROUP, null, p, w.Rand(0f, 360f), w.Rand(0.9f, 1.2f), 0.2f, trunk: true); break;
                        case 3: w.Add("TreeLine", RockGray(GrayLetters[li % GrayLetters.Length]), null, p, w.Rand(0f, 360f), w.Rand(1.4f, 2.2f), 0.3f); break;
                        case 4: Tree(w, "TreeLine", ACACIA, null, p); break;
                        case 6: w.Add("TreeLine", RSTYL, w.Pick(RstylBig), p, w.Rand(0f, 360f), w.Rand(2.5f, 3.5f), 0.3f, scaleY: w.Rand(1.2f, 1.6f)); break;
                        default: w.Add("TreeLine", WTOWN, w.Pick(TownCacti), p, w.Rand(0f, 360f), w.Rand(0.9f, 1.2f), 0.1f, trunk: true); break;
                    }
                }
            // 夕景の林（枯れ木が多く、緑は少し、12 か所）
            foreach (var c in w.Centers(def.seed + 7, 12, 40f, 160f, 80f)) Grove(w, "SavannaGrove", c, 20f, 6, 0.6f, 0.2f);
            // 灰色の岩場（10 か所）
            int gi = 0;
            foreach (var c in w.Centers(def.seed + 2, 10, 24f, 150f, 70f))
                for (int i = 0; i < 9; i++)
                {
                    if (i == 0) w.Add("GrayRocks", RSTYL, null, w.Around(c, 14f), w.Rand(0f, 360f), w.Rand(1.2f, 1.8f), 0.2f);
                    else w.Add("GrayRocks", RockGray(GrayLetters[gi++ % GrayLetters.Length]), null, w.Around(c, 18f), w.Rand(0f, 360f), w.Rand(1.2f, 2.4f), 0.2f);
                }
            // サボテンの群れ
            foreach (var c in w.Centers(def.seed + 3, 8, 22f, 120f, 80f))
            {
                w.Add("Cactus", CGROUP, null, c, w.Rand(0f, 360f), w.Rand(0.9f, 1.2f), 0.2f, trunk: true);
                for (int i = 0; i < 3; i++) w.Add("Cactus", CACTI, w.Pick(CactiParts), w.Around(c, 12f), w.Rand(0f, 360f), 1f, 0.1f, trunk: true);
                w.Add("Cactus", DPROPS, w.Pick(DpCacti), w.Around(c, 10f), w.Rand(0f, 360f), 1f, 0.1f, trunk: true);
            }
            // 道沿いの帯（灰色の岩・木・サボテンの群れ）
            RoadsideBand(w, goalArc, x =>
            {
                int k = x.rng.Next(10);
                if (k < 4) return new[] { RockGray(GrayLetters[x.rng.Next(GrayLetters.Length)]), null };
                if (k < 5) return new[] { TREES, TreePart(x) };
                if (k < 7) return new[] { DEADTREE, null };
                if (k < 8) return new[] { WTOWN, x.Pick(TownCacti) };
                return new[] { CGROUP, null };
            });
            // 枯れ木と草（まとまり）
            foreach (var c in w.Centers(def.seed + 4, 6, 22f, 100f, 70f))
            {
                Tree(w, "Scrub", DEADTREE, null, w.Around(c, 8f));
                for (int i = 0; i < 4; i++) w.Add("Scrub", DPROPS, w.Pick(DpGrass), w.Around(c, 12f), w.Rand(0f, 360f), 1f, 0.05f);
                w.Add("Scrub", DPROPS, w.Pick(DpRocks), w.Around(c, 12f), w.Rand(0f, 360f), 1f, 0.2f);
            }
            // 大岩
            foreach (var c in w.Centers(def.seed + 5, 4, 60f, 150f, 120f)) w.Add("BigRocks", DROCKS, w.Pick(RockBig), c, w.Rand(0f, 360f), 1.1f, 1.5f);
            // 岩のセットの岩場（8 か所）：部品ごとにばらして置く（art-r2-04）
            foreach (var c in w.Centers(def.seed + 8, 8, 30f, 150f, 70f))
                for (int i = 0; i < 6; i++)
                    w.Add("GrayRocks", RSTYL, RstylParts[i % RstylParts.Length], w.Around(c, 12f), w.Rand(0f, 360f), w.Rand(2f, 3.2f), 0.2f, scaleY: w.Rand(1f, 1.4f));
        }

        public const float GOAL_TOWN_CLEAR_M = 60f;   // course-r3-08：ゴールの町の置かない円（囲いの中心から）
        public const float GOAL_STREET_FROM = -200f, GOAL_STREET_TO = 60f;   // course-r3-08：通りはゴールの 200m 手前から（見物の直線と同じ）   // ゴールからの弧長（course-r1-12：最後の 260m は町の通り）
        public const float GOAL_STREET_SIDE = HeightmapGen.RUNWAY_M + 7f, GOAL_STREET_STEP = 20f, GOAL_HAY_SIDE = 13f;   // course-r3-08：建物は道の縁 + 7m（前は 22m）
        public const float GOAL_BOARDWALK_SIDE = 16f;   // art-r3-15：建物の前の板張りの縁
        // ---- 通りの飾り（art-r3-09 / art-r3-15）：板張りの縁に 7m ごと、街灯 16m ごと、荷車・水桶 30m ごと ----
        public const float STREET_PROP_STEP_M = 7f, STREET_LAMP_STEP_M = 16f, STREET_CART_STEP_M = 30f;
        static readonly string[] StreetTrough = { TOWN_CART, TOWN_BARRELS };

        /// <summary>
        /// 通りの飾り 1 か所（n = 7m ごとの番号）：樽・木箱・干し草・町の木箱の山を順に、16m ごと（n が 16/7 の倍数に近い所）に街灯、30m ごとに荷車か樽の山。
        /// face = 通りを向く向き。置けた数を返す。
        /// </summary>
        static int StreetProp(Work w, Vector3 p, float face, int n, float minRoad, bool ignoreCircles)
        {
            const string G = "StreetDressing";
            int placed = 0;
            float d = n * STREET_PROP_STEP_M;
            Vector3 along = new Vector3(Mathf.Sin((face + 90f) * Mathf.Deg2Rad), 0f, Mathf.Cos((face + 90f) * Mathf.Deg2Rad));
            switch (n % 4)
            {
                case 0: if (w.Add(G, FARM, FarmBarrels[n / 4 % FarmBarrels.Length], p, w.Rand(0f, 360f), 1.1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++;
                        if (w.Add(G, FARM, FarmBarrels[(n / 4 + 1) % FarmBarrels.Length], p + along * 0.9f, w.Rand(0f, 360f), 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++; break;
                case 1: if (w.Add(G, FARM, FarmCrates[n % FarmCrates.Length], p, face + w.Rand(-15f, 15f), 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++; break;
                case 2: if (w.Add(G, HAY, HayParts[1 + n % 6], p, face + w.Rand(-30f, 30f), 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++; break;
                default: if (w.Add(G, WTOWN, TownCrates[n % TownCrates.Length], p, face + w.Rand(-10f, 10f), 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++; break;
            }
            if (Mathf.Repeat(d, STREET_LAMP_STEP_M) < STREET_PROP_STEP_M - 0.01f && w.Add(G, FARM, FARM_LAMP, p - along * 2.2f, face, 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles)) placed++;
            if (Mathf.Repeat(d, STREET_CART_STEP_M) < STREET_PROP_STEP_M - 0.01f && n > 0)
            {
                Vector3 inward = new Vector3(Mathf.Sin(face * Mathf.Deg2Rad), 0f, Mathf.Cos(face * Mathf.Deg2Rad));
                if (w.Add(G, WTOWN, StreetTrough[n % 2], p + inward * 0.3f + along * 3.4f, face + 90f, 1f, 0.02f, minRoad, ignoreCircles: ignoreCircles, rOverlap: 0.4f)) placed++;
            }
            return placed;
        }
        // ---- art-r4-16：町の建物の正面を道へ向ける・斜面に張り出さない ----
        /// <summary>町の建物の足元（中心と 0.7 × 半径の 4 点）の高さの差の上限（m）。超える所には建てない（斜面に張り出して見える）。</summary>
        public const float TOWN_MAX_SPAN_M = 1.2f;
        /// <summary>
        /// WesternTown の通りの建物の正面の向き（部品のローカル）：+1 = ローカル +X、−1 = ローカル −X（町の模型の通りの西側の建物は東 = +X を向く。
        /// parts.json の重心の x の符号の逆）。Buildings/*.fbx（TownBlock・SaloonFacade）はローカル +Z（0）。
        /// </summary>
        public static int FacadeSide(string asset, string part)
        {
            if (asset != WTOWN || part == null) return 0;
            switch (part)
            {
                case "WesternTown_029_Building": case "WesternTown_036_Building": case "WesternTown_047_Building": case "WesternTown_056_Building": case "WesternTown_070_Building": return 1;
                case "WesternTown_030_Building": case "WesternTown_039_Building": case "WesternTown_048_Building": case "WesternTown_057_Building": case "WesternTown_067_Building": return -1;
            }
            return 0;
        }

        /// <summary>正面（FacadeSide）がワールドの向き faceDeg を向く yaw（art-r4-16：前は WesternTown の建物の横・裏の平らな壁が道を向いていた）。</summary>
        public static float FacadeYaw(string asset, string part, float faceDeg)
        {
            int s = FacadeSide(asset, part);
            return s > 0 ? faceDeg - 90f : (s < 0 ? faceDeg + 90f : faceDeg);
        }

        static readonly string[] StreetBuildings =
        {
            "WesternTown_029_Building", "WesternTown_030_Building", "WesternTown_036_Building", "WesternTown_039_Building",
            "WesternTown_047_Building", "WesternTown_048_Building", "WesternTown_056_Building", "WesternTown_057_Building",
        };

        /// <summary>
        /// ゴールの町（境目）：ゴールの 260m 手前から 60m 先まで、通りの両脇 ±22m に建物（TownBlock・SaloonFacade・WesternTown の建物を交互に、
        /// 通りのほうを向く）、±13m に干し草の山（見物人の柵）、街灯。最後の 260m は町の大通りを走る。通りの奥に給水塔（遠くの目印）。
        /// 表彰台（ゴールの 45m 先・右 15m）とゴールゲートの足元は空ける。
        /// </summary>
        static void GoalTown(Work w, float goalArc)
        {
            Vector3 podium = PodiumLocal(w.course, goalArc);
            int k = 0;
            for (float off = GOAL_STREET_FROM; off <= GOAL_STREET_TO; off += GOAL_STREET_STEP, k++)
            {
                float a = goalArc + off;
                float yaw = w.YawAt(a);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    int v0 = (k * 2 + (sd > 0 ? 1 : 0)) % 6;
                    string bAsset = v0 == 0 ? TOWNBLOCK : (v0 == 3 ? SALOON : WTOWN);
                    float br = SizeOf(bAsset, bAsset == WTOWN ? StreetBuildings[(k + (sd > 0 ? 3 : 0)) % StreetBuildings.Length] : null).r;
                    // course-r3-08：建物の正面が道の縁 + 7m（中心は奥行きの半分ぶん外）
                    Vector3 p = w.Side(a + sd * 4f, sd * (GOAL_STREET_SIDE + w.Extra(a) + br * 0.5f));
                    if (NearSmallCircle(w, p, SizeOf(WTOWN, StreetBuildings[0]).r)) continue;
                    if (WorldLayout.DistXZ(p, podium) < 18f) continue;
                    if (Mathf.Abs(off) < 10f) continue;   // ゴールゲートの両脇は空ける
                    int v = (k * 2 + (sd > 0 ? 1 : 0)) % 6;
                    float face = yaw + (sd < 0 ? 90f : -90f);
                    string sbp = StreetBuildings[(k + (sd > 0 ? 3 : 0)) % StreetBuildings.Length];
                    // art-r4-16：足元の高さの差が 1.2m を超える所（斜面に張り出す）は建てない
                    w.FootprintGround(p, br, out float _, out float span);
                    if (span > TOWN_MAX_SPAN_M) continue;
                    if (v == 0) w.Add("GoalTown", TOWNBLOCK, null, p, face, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                    else if (v == 3) w.Add("GoalTown", SALOON, null, p, face, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                    else w.Add("GoalTown", WTOWN, sbp, p, FacadeYaw(WTOWN, sbp, face), 1f, 0.2f, ROAD_MIN, ignoreCircles: true);
                }
                // 干し草の山（見物人の柵）：2 つ積み＋小さな束、左右交互。ゴールの手前は見物の直線（CourseMarks.FinishChute）のレールと旗が受け持つ
                if (off >= 12f)
                {
                    float sd2 = k % 2 == 0 ? 1f : -1f;
                    Vector3 hp = w.Side(a + 8f, sd2 * (GOAL_HAY_SIDE + w.Extra(a)));
                    if (WorldLayout.DistXZ(hp, podium) >= 12f && !NearSmallCircle(w, hp, 2f))
                    {
                        w.Add("GoalTown", HAY, "HayBales_01", hp, yaw, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                        w.Add("GoalTown", HAY, HayParts[1 + k % 6], w.Side(a + 10.5f, sd2 * (GOAL_HAY_SIDE + 0.6f + w.Extra(a))), yaw + 30f, 1f, 0.02f, ROAD_MIN, ignoreCircles: true);
                    }
                    Vector3 lp = w.Side(a + 2f, -sd2 * (14f + w.Extra(a)));
                    if (k % 3 == 1 && !NearSmallCircle(w, lp, 1f)) w.Add("GoalTown", FARM, FARM_LAMP, lp, yaw, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                }
            }
            // 通りの両脇の板張りの縁（±16m、art-r3-15）：樽・木箱・干し草 7m ごと、街灯 16m ごと、荷車 30m ごと
            for (int sd = -1; sd <= 1; sd += 2)
            {
                int n = 0;
                for (float off = GOAL_STREET_FROM + 6f; off <= GOAL_STREET_TO - 6f; off += STREET_PROP_STEP_M, n++)
                {
                    if (Mathf.Abs(off) < 12f) continue;   // ゴールゲートの両脇
                    float a = goalArc + off;
                    Vector3 p = w.Side(a, sd * (GOAL_BOARDWALK_SIDE + w.Extra(a)));
                    if (WorldLayout.DistXZ(p, podium) < 12f || NearSmallCircle(w, p, 1.5f)) continue;
                    StreetProp(w, p, w.YawAt(a) + (sd < 0 ? 90f : -90f), n, ROAD_MIN, true);
                }
            }
            // 通りの奥の給水塔（コースの終点からさらに 45m 先、コースの延長線上）
            Vector3[] c = w.course;
            Vector3 end = c[c.Length - 1], dir = WorldLayout.TangentAtArc(c, CoursePath.Length(c) - 1f);
            Vector3 tp = end + dir * 45f;
            tp.x = Mathf.Clamp(tp.x, EDGE_MARGIN + 10f, w.sizeM - EDGE_MARGIN - 10f);
            tp.z = Mathf.Clamp(tp.z, EDGE_MARGIN + 10f, w.sizeM - EDGE_MARGIN - 10f);
            w.Add("GoalTown", WTOWN, TOWN_TALL_A, tp, WorldLayout.YawDeg(-dir), 1f, 0.3f, ROAD_MIN, ignoreCircles: true);   // art-r4-15：境目の風車は切り通しの先の 1 本だけ
        }

        // ------------------------------------------------------------------
        // ゴールの先の出張所（art-r2-06）・見どころ（art-r2-05）・尾根のシルエット（art-r2-09）・巨大な骨（art-r2-16）
        // ------------------------------------------------------------------

        public const float OUTPOST_TOWER_ALONG = 125f;   // 荒野：給水塔はゴールの先 125m（尾根の切れ込みの中）、ゴールの向きの線上
        public const float OUTPOST_TOWER_SCALE = 1.4f;
        public const float OUTPOST_DUNES_ALONG = 102f;   // 砂漠：高い建物（WesternTown_084）はゴールの先 102m

        /// <summary>ゴールの位置・向き・右（Terrain ローカル）から、ゴールの先 along m・横 lat m の点。</summary>
        static Vector3 GoalFrame(Work w, float goalArc, float along, float lat)
        {
            Vector3 g = WorldLayout.PointAtArc(w.course, goalArc), t = WorldLayout.TangentAtArc(w.course, goalArc);
            Vector3 r = WorldLayout.RightOf(t);
            Vector3 p = g + t * along + r * lat;
            return new Vector3(p.x, 0f, p.z);
        }

        /// <summary>
        /// ゴールの先の出張所（art-r2-06、荒野・砂漠）：最後の直線から見てゴールの門の奥に「着く場所」を作る。ゴールの向きの線上と ±25〜32m に、
        ///   荒野：牧場の建物（RanchCompound_07/09/10）、線上に給水塔（RanchCompound_11 ×1.4、尾根の切れ込み SetPieces.CarveGoalNotch の中）、
        ///         街灯（暖色の灯り）2 本、焚き火の煙、木箱と荷車。
        ///   砂漠：高い建物（WesternTown_084）を線上に、通りの建物 2 棟、テント 3 張り、ラクダ 2 頭、干し草。
        /// </summary>
        static void GoalOutpost(Work w, StageDef def, float goalArc)
        {
            const string G = "GoalOutpost";
            float yaw = w.YawAt(goalArc);
            if (def.kind == HeightmapGen.KIND_CANYON)
            {
                w.Add(G, WTOWN, TOWN_TALL_B, GoalFrame(w, goalArc, OUTPOST_TOWER_ALONG, 0f), yaw + 180f, OUTPOST_TOWER_SCALE, 0.3f, ROAD_MIN, background: true, ignoreCircles: true);   // art-r4-15
                w.Add(G, RANCH, RanchBuildings[0], GoalFrame(w, goalArc, 96f, 32f), yaw - 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, RANCH, RanchBuildings[1], GoalFrame(w, goalArc, 100f, -34f), yaw + 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, RANCH, RanchBuildings[2], GoalFrame(w, goalArc, 82f, 22f), yaw + 180f, 1f, 0.1f, ROAD_MIN, ignoreCircles: true);
                for (int sd = -1; sd <= 1; sd += 2) w.Add(G, FARM, FARM_LAMP, GoalFrame(w, goalArc, 84f, sd * 12f), yaw, 1.2f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, CAMPFIRE, "Campfire_01", GoalFrame(w, goalArc, 90f, -16f), 0f, 1.2f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, WTOWN, TOWN_CART, GoalFrame(w, goalArc, 88f, 16f), yaw + 30f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, WTOWN, TownCrates[0], GoalFrame(w, goalArc, 92f, -24f), yaw + 10f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, WTOWN, TOWN_BARRELS, GoalFrame(w, goalArc, 105f, 20f), yaw - 20f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, HAY, "HayBales_01", GoalFrame(w, goalArc, 86f, -9f), yaw, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
            }
            else
            {
                w.Add(G, WTOWN, TOWN_TALL_A, GoalFrame(w, goalArc, OUTPOST_DUNES_ALONG, 0f), yaw, 1f, 0.6f, ROAD_MIN, background: true, ignoreCircles: true);
                w.Add(G, WTOWN, StreetBuildings[0], GoalFrame(w, goalArc, 88f, 28f), yaw - 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                w.Add(G, WTOWN, StreetBuildings[4], GoalFrame(w, goalArc, 92f, -30f), yaw + 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                for (int i = 0; i < 3; i++)
                    w.Add(G, TENT, null, GoalFrame(w, goalArc, 74f + i * 9f, (i % 2 == 0 ? 1f : -1f) * 20f), yaw + 60f * i, 1f, 0.1f, ROAD_MIN, ignoreCircles: true);
                for (int i = 0; i < 2; i++)
                    w.Add(G, CAMEL, null, GoalFrame(w, goalArc, 80f + i * 6f, -14f - i * 3f), yaw + 70f + 40f * i, 1f, 0.02f, ROAD_MIN, ignoreCircles: true);
                for (int i = 0; i < 3; i++)
                    w.Add(G, HAY, HayParts[i], GoalFrame(w, goalArc, 78f + i * 1.4f, 13f + i), yaw + 30f * i, 1f, 0.03f, ROAD_MIN, ignoreCircles: true);
            }
        }

        /// <summary>出張所の目印（荒野 = 給水塔、砂漠 = 高い建物）の素材・ゴールの先の距離・拡大。</summary>
        public static void GoalOutpostLandmark(int kind, out string asset, out string part, out float along, out float scale)
        {
            if (kind == HeightmapGen.KIND_DUNES) { asset = WTOWN; part = TOWN_TALL_A; along = OUTPOST_DUNES_ALONG; scale = 1f; }
            else { asset = WTOWN; part = TOWN_TALL_B; along = OUTPOST_TOWER_ALONG; scale = OUTPOST_TOWER_SCALE; }   // art-r4-15：鐘の塔（風車は Landmark の 1 本だけ）
        }

        // ---- 道沿いの見どころ（art-r2-05）：道から VIGNETTE_LAT_MIN〜MAX m、VIGNETTE_STEP_MIN〜MAX m ごと ----
        public const float VIGNETTE_LAT_MIN = 30f, VIGNETTE_LAT_MAX = 60f;
        public const float VIGNETTE_STEP_MIN = 350f, VIGNETTE_STEP_MAX = 450f;
        /// <summary>art-r5-03：見どころの広がり（半径 9m）の地面の差の上限（m）。</summary>
        public const float VIGNETTE_MAX_SPAN_M = 1.6f;

        /// <summary>ステージごとの見どころのまとまりの名前（荒野 = 鉱夫の野営、砂漠 = 隊商、境目 = 水場）。</summary>
        public static string VignetteGroup(int kind) => kind == HeightmapGen.KIND_CANYON ? "MinersCamp" : (kind == HeightmapGen.KIND_DUNES ? "Caravan" : "WaterStop");

        static void Vignettes(Work w, StageDef def, float goalArc)
        {
            string G = VignetteGroup(def.kind);
            int k = 0;
            for (float a = 200f; a < goalArc - 150f; a += w.Rand(VIGNETTE_STEP_MIN, VIGNETTE_STEP_MAX), k++)
            {
                Vector3 c;
                if (!FindBesideRange(w, a - 30f, a + 30f, VIGNETTE_LAT_MIN + 8f, VIGNETTE_LAT_MAX - 8f, 9f, out c, VIGNETTE_MAX_SPAN_M)
                    && !FindBesideRange(w, a - 60f, a + 60f, VIGNETTE_LAT_MIN + 4f, VIGNETTE_LAT_MAX + 4f, 9f, out c, VIGNETTE_MAX_SPAN_M * 1.6f)
                    && !(def.kind == HeightmapGen.KIND_CANYON && FindBesideRange(w, a - 120f, a + 120f, ROAD_CLEAR + 6f, VIGNETTE_LAT_MAX, 7f, out c, VIGNETTE_MAX_SPAN_M * 2f))) continue;   // 狭い谷：谷底の平らな所
                Vector3 toRoad = WorldLayout.PointAtArc(w.course, a) - c;
                float face = WorldLayout.YawDeg(new Vector3(toRoad.x, 0f, toRoad.z).normalized);
                if (def.kind == HeightmapGen.KIND_CANYON) MinersCamp(w, G, c, face, k);
                else if (def.kind == HeightmapGen.KIND_DUNES) Caravan(w, G, c, face, k);
                else WaterStop(w, G, c, face, k);
            }
        }

        static Vector3 Rel(Vector3 c, float face, float right, float fwd)
        {
            float a = face * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), r = new Vector3(f.z, 0f, -f.x);
            return new Vector3(c.x + f.x * fwd + r.x * right, 0f, c.z + f.z * fwd + r.z * right);
        }

        /// <summary>鉱夫の野営：テント 2 張り、焚き火（煙）、薪の山、荷車と樽からこぼれる宝石、街灯（暖色の灯り）。</summary>
        static void MinersCamp(Work w, string G, Vector3 c, float face, int k)
        {
            w.Add(G, CAMPFIRE, "Campfire_01", c, 0f, 1f, 0.05f);
            w.Add(G, TENT, null, Rel(c, face, -7f, -5f), face + 30f, 1f, 0.1f);
            w.Add(G, TENT, null, Rel(c, face, 6f, -7f), face - 25f, 1f, 0.1f);
            w.Add(G, LOGS, "WoodLogs_08", Rel(c, face, 3f, 2.5f), face + 90f, 1.4f, 0.05f);
            Vector3 cart = Rel(c, face, -6f, 4f);
            w.Add(G, WTOWN, k % 2 == 0 ? TOWN_CART : TOWN_BARRELS, cart, face + 40f, 1f, 0.05f);
            for (int i = 0; i < 5; i++)
                w.Add(G, GEMS, Gem(k + i / 3, 2 + i), new Vector3(cart.x + w.Rand(-1.8f, 1.8f), 0f, cart.z + w.Rand(1.6f, 2.8f)), w.Rand(0f, 360f), w.Rand(0.8f, 1.2f), 0.05f, ignoreCircles: true);
            w.Add(G, FARM, FARM_LAMP, Rel(c, face, 2.5f, 6f), face, 1.1f, 0.05f);
            w.Add(G, WTOWN, TownCrates[k % TownCrates.Length], Rel(c, face, 8f, 2f), face + 15f, 1f, 0.05f);
            w.Add(G, WTOWN, TownPebbles[k % TownPebbles.Length], Rel(c, face, -9f, 1f), face, 1.3f, 0.1f);
        }

        /// <summary>隊商：ラクダ 2〜3 頭（Animator を外した置き物）、テント 2 張り、干し草、木箱と樽の山。</summary>
        static void Caravan(Work w, string G, Vector3 c, float face, int k)
        {
            int camels = 2 + k % 2;
            for (int i = 0; i < camels; i++)
                w.Add(G, CAMEL, null, Rel(c, face, -5f + i * 3.2f, 3f + (i % 2) * 1.5f), face + 80f + i * 25f, 1f, 0.02f);
            w.Add(G, TENT, null, Rel(c, face, -8f, -6f), face + 20f, 1f, 0.1f);
            w.Add(G, TENT, null, Rel(c, face, 7f, -6f), face - 35f, 1f, 0.1f);
            for (int i = 0; i < 3; i++) w.Add(G, HAY, HayParts[i], Rel(c, face, 4f + i * 1.3f, 1f + (i % 2)), face + i * 40f, 1f, 0.03f);
            w.Add(G, WTOWN, TownCrates[(k + 1) % TownCrates.Length], Rel(c, face, 1f, -3f), face + 10f, 1f, 0.05f);
            w.Add(G, WTOWN, TOWN_BARRELS, Rel(c, face, -2f, -3.5f), face - 30f, 1f, 0.05f);
            w.Add(G, WTOWN, TownCacti[(k * 7) % TownCacti.Length], Rel(c, face, 11f, 2f), w.Rand(0f, 360f), 1f, 0.1f, trunk: true);
        }

        /// <summary>水場：厩舎、牧場の小屋（RanchCompound_10）、柵、樽・木箱、干し草、町のサボテン。</summary>
        static void WaterStop(Work w, string G, Vector3 c, float face, int k)
        {
            w.Add(G, STABLE, null, Rel(c, face, 0f, -6f), face + 180f, 1f, 0.1f);
            w.Add(G, RANCH, RanchBuildings[2], Rel(c, face, 10f, -2f), face + 90f, 1f, 0.1f);
            for (int i = 0; i < 4; i++) w.Add(G, FARM, FarmFences[i % FarmFences.Length], Rel(c, face, -10f + i * 2.8f, 4f), face + 90f, 1f, 0.05f);
            w.Add(G, WTOWN, TOWN_BARRELS, Rel(c, face, -9f, -1f), face, 1f, 0.05f);
            w.Add(G, WTOWN, TownCrates[(k + 2) % TownCrates.Length], Rel(c, face, 7f, 4f), face + 25f, 1f, 0.05f);
            w.Add(G, HAY, "HayBales_01", Rel(c, face, -6f, 6f), face, 1f, 0.05f);
            w.Add(G, HAY, HayParts[1 + k % 5], Rel(c, face, -4.8f, 6.5f), face + 40f, 1f, 0.03f);
            w.Add(G, WTOWN, TownCacti[(k * 5 + 3) % TownCacti.Length], Rel(c, face, 13f, 5f), w.Rand(0f, 360f), 1f, 0.1f, trunk: true);
            w.Add(G, FARM, FARM_LAMP, Rel(c, face, 3f, 5f), face, 1.1f, 0.05f);
        }

        // ---- 巨大な骨（art-r2-16）----
        public const float GIANT_SKELETON_SCALE = 7f, GIANT_SKELETON_SINK = 0.35f;
        public const float GIANT_SKELETON_HERO_SCALE = 12f;   // art-r4-18：踏み切りの前の主役

        /// <summary>
        /// 砂漠の巨大な骨の場所：スタートの位置から進む向き ±30°・250〜400m で、コースから 80m 以上・端から 70m 以上の点のうち一番高い所（砂丘の上）。
        /// </summary>
        public static bool GiantSkeletonSpot(StageDef def, Vector3[] course, float[,] h, out Vector3 spot)
        {
            float s = StageDefinitions.START_ARC_M;
            Vector3 p0 = WorldLayout.PointAtArc(course, s), t0 = WorldLayout.TangentAtArc(course, s);
            float best = float.MinValue; spot = Vector3.zero;
            for (float a = -30f; a <= 30.01f; a += 5f)
                for (float d = 250f; d <= 400.01f; d += 25f)
                {
                    float yaw = (WorldLayout.YawDeg(t0) + a) * Mathf.Deg2Rad;
                    var p = new Vector3(p0.x + Mathf.Sin(yaw) * d, 0f, p0.z + Mathf.Cos(yaw) * d);
                    if (p.x < 70f || p.z < 70f || p.x > StageDefinitions.SIZE - 70f || p.z > StageDefinitions.SIZE - 70f) continue;
                    if (WorldLayout.DistToPolylineXZ(p, course) < 80f) continue;
                    float g = h == null ? 0f : WorldLayout.SampleHeight(h, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, p.x, p.z);
                    if (g > best) { best = g; spot = p; }
                }
            return best > float.MinValue;
        }

        static void GiantSkeletons(Work w, StageDef def, float goalArc)
        {
            const string G = "GiantSkeleton";
            float sk = SizeOf(SKELETON, null).h * GIANT_SKELETON_SCALE;
            Vector3 p;
            if (GiantSkeletonSpot(def, w.course, w.h, out p))
                w.Add(G, SKELETON, null, p, w.Rand(0f, 360f), GIANT_SKELETON_SCALE, GIANT_SKELETON_SINK * sk, LANDMARK_MIN_ROAD_M - 10f, background: true, roll: 85f, ignoreCircles: true);
            // 2 頭目：踏み切りへ向かう坂の手前の横、高い所
            float jump = SetPieces.JumpArc(def, goalArc);
            if (jump < 0f) return;
            float bestG = float.MinValue; Vector3 bestP = Vector3.zero;
            for (int i = 0; i < 12; i++)
            {
                Vector3 q = w.Side(jump - 160f + i * 10f, (i % 2 == 0 ? 1f : -1f) * (70f + (i % 4) * 10f));
                if (!w.Allowed(q, 9f, LANDMARK_MIN_ROAD_M - 10f, true)) continue;
                float g = w.Ground(q.x, q.z);
                if (g > bestG) { bestG = g; bestP = q; }
            }
            // art-r4-18：砂漠の主役のシルエット＝踏み切りへ向かう坂から見える砂丘の上の巨大な骨（×12、半分埋める）
            if (bestG > float.MinValue)
                w.Add(G, SKELETON, null, bestP, w.Rand(0f, 360f), GIANT_SKELETON_HERO_SCALE, 0.5f * SizeOf(SKELETON, null).h * GIANT_SKELETON_HERO_SCALE, LANDMARK_MIN_ROAD_M - 10f,
                    background: true, roll: 85f, ignoreCircles: true);
        }

        // ---- 尾根のシルエット（art-r2-09）----
        public const float RIM_SIL_STEP_MIN = 70f, RIM_SIL_STEP_MAX = 120f;
        public const float RIM_SIL_INSET_M = 28f;    // 角を丸めた外周から内側へ（尾根の背）

        /// <summary>角を丸めた正方形（HeightmapGen.RimDistance と同じ角の丸み）の縁から inset m 内側の周の長さ s の点。</summary>
        public static Vector3 RimPathPoint(float s, float inset, float sizeM)
        {
            float R = Mathf.Min(HeightmapGen.RIM_CORNER_RADIUS_M, sizeM * 0.5f), rho = Mathf.Max(1f, R - inset);
            float L = sizeM - 2f * R, A = rho * Mathf.PI * 0.5f, per = 4f * (L + A);
            s = ((s % per) + per) % per;
            // 下の辺 → 右下の角 → 右の辺 → 右上の角 → 上の辺 → 左上の角 → 左の辺 → 左下の角
            Vector2[] cs = { new Vector2(sizeM - R, R), new Vector2(sizeM - R, sizeM - R), new Vector2(R, sizeM - R), new Vector2(R, R) };
            for (int i = 0; i < 4; i++)
            {
                float a0 = -90f + i * 90f;   // 辺の外向きの角度（下の辺 = -90°）
                if (s < L)
                {
                    Vector2 c0 = cs[(i + 3) % 4];
                    float ang = a0 * Mathf.Deg2Rad;
                    float nx = Mathf.Cos(ang), ny = Mathf.Sin(ang), tx = -ny, ty = nx;
                    return new Vector3(c0.x + nx * rho + tx * s, 0f, c0.y + ny * rho + ty * s);
                }
                s -= L;
                if (s < A)
                {
                    float ang = (a0 + s / A * 90f) * Mathf.Deg2Rad;
                    return new Vector3(cs[i].x + Mathf.Cos(ang) * rho, 0f, cs[i].y + Mathf.Sin(ang) * rho);
                }
                s -= A;
            }
            return new Vector3(sizeM * 0.5f, 0f, inset);
        }

        public static float RimPathLength(float inset, float sizeM)
        {
            float R = Mathf.Min(HeightmapGen.RIM_CORNER_RADIUS_M, sizeM * 0.5f), rho = Mathf.Max(1f, R - inset);
            return 4f * (sizeM - 2f * R + rho * Mathf.PI * 0.5f);
        }

        /// <summary>
        /// 外周の尾根の背に 70〜120m ごとのシルエット（art-r2-09）：荒野 = メサ・メサ B ×1.5〜3、砂漠 = 骨の馬 ×5〜6（横倒し）と DesertRocks_01〜03 ×1.5〜2、
        /// 境目 = メサ ×2〜3。高さの 25% を埋める。足元の縁はコースから 30m 以上（背景専用、当たり判定なし）。
        /// </summary>
        static void RimSilhouettes(Work w, StageDef def, float goalArc)
        {
            const string G = "RimSilhouette";
            float per = RimPathLength(RIM_SIL_INSET_M, w.sizeM);
            int k = 0;
            for (float s = w.Rand(0f, 60f); s < per; s += w.Rand(RIM_SIL_STEP_MIN, RIM_SIL_STEP_MAX), k++)
            {
                Vector3 p = RimPathPoint(s, RIM_SIL_INSET_M, w.sizeM);
                string asset, part = null; float sc, roll = 0f;
                if (def.kind == HeightmapGen.KIND_CANYON) { asset = k % 2 == 0 ? MESA : MESAB; sc = w.Rand(1.5f, 3f); }
                else if (def.kind == HeightmapGen.KIND_DUNES)
                {
                    if (k % 3 == 1) { asset = SKELETON; sc = w.Rand(5f, 6f); roll = 85f; }
                    else { asset = DROCKS; part = RockBig[k % RockBig.Length]; sc = w.Rand(1.5f, 2f); }
                }
                else if (k % 2 == 0) { asset = MESA; sc = w.Rand(2f, 3f); }
                else { asset = DROCKS; part = RockBig[k % RockBig.Length]; sc = w.Rand(1.6f, 2.2f); }   // art-r4-04：メサ B が素材の 8% を超えないよう大岩と交互
                for (int tries = 0; tries < 3; tries++, sc *= 0.8f)
                {
                    Size sz = SizeOf(asset, part);
                    float r = sz.r * sc, foot = asset == MESA || asset == MESAB ? r * MESA_GATE_FOOTPRINT : r * 0.8f;
                    if (w.RoadDist(p) - foot < 30f) continue;
                    bool free = true;
                    foreach (var c in w.circles) if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + foot) { free = false; break; }
                    if (!free) continue;
                    if (w.Add(G, asset, part, p, w.Rand(0f, 360f), sc, 0.25f * sz.h * sc, 0f, background: true, roll: roll, ignoreCircles: false, rBounds: 0f, rOverlap: 0f))
                    {
                        var q = w.list[w.list.Count - 1];
                        q.collider = false; q.shape = SHAPE_NONE;
                        break;
                    }
                }
            }
        }

        /// <summary>半径 20m 未満の置かない円（ゲートの足元・黒板・合図）に p（半径 r）がかかるか。</summary>
        static bool NearSmallCircle(Work w, Vector3 p, float r)
        {
            foreach (var c in w.circles)
                if (c.w < 20f && WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + r) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // コースの形の目印（course-r1-02/12/13/18）
        // ------------------------------------------------------------------

        /// <summary>荒野・砂漠のゴール（course-r1-12）：ゴールの 40〜80m 先の横に幌馬車の野営地（焚き火の煙）、最後の直線の延長線上の遠くにメサ ×1.5。</summary>
        static void FinishCamp(Work w, StageDef def, float goalArc)
        {
            Vector3 lm;
            if (FinishLandmarkSpot(def, w.course, goalArc, out lm))
                w.Add("FinishLandmark", MESA, null, lm, w.Rand(0f, 360f), FINISH_LANDMARK_SCALE, 0.2f * SizeOf(MESA, null).h * FINISH_LANDMARK_SCALE,
                    LANDMARK_MIN_ROAD_M - 10f, background: true, ignoreCircles: true);
        }

        public const float FINISH_LANDMARK_SCALE = 1.5f;

        /// <summary>最後の直線の延長線上（直線の向き）で、ゴールから 300〜500m（Terrain に入らなければ近い順に 60m まで）、横に少しずらして Terrain の中の空いた点。</summary>
        public static bool FinishLandmarkSpot(StageDef def, Vector3[] course, float goalArc, out Vector3 spot)
        {
            Vector3 g = WorldLayout.PointAtArc(course, goalArc), t = WorldLayout.TangentAtArc(course, goalArc - 60f);
            Vector3 r = WorldLayout.RightOf(t);
            float rad = SizeOf(MESA, null).r * FINISH_LANDMARK_SCALE;
            float[] lats = { 0f, 60f, -60f, 110f, -110f };
            for (float d = 500f; d >= 60f; d -= 20f)
                foreach (float lat in lats)
                {
                    Vector3 p = g + t * d + r * lat;
                    if (p.x < 60f || p.z < 60f || p.x > StageDefinitions.SIZE - 60f || p.z > StageDefinitions.SIZE - 60f) continue;
                    if (WorldLayout.DistToPolylineXZ(p, course) < rad * 0.5f + LANDMARK_MIN_ROAD_M) continue;
                    spot = new Vector3(p.x, 0f, p.z);
                    return true;
                }
            spot = Vector3.zero;
            return false;
        }

        static bool FindBesideRange(Work w, float a0, float a1, float latMin, float latMax, float r, out Vector3 p, float maxSpan = -1f)
        {
            int n = maxSpan > 0f ? 80 : 30;
            for (int tries = 0; tries < n; tries++)
            {
                float a = Mathf.Lerp(a0, a1, (tries % 5) / 4f);
                float side = (tries / 5) % 2 == 0 ? 1f : -1f;
                p = w.Side(a, side * w.Rand(latMin, latMax));
                if (!w.Allowed(p, r, ROAD_CLEAR, false)) continue;
                if (maxSpan > 0f && w.h != null)
                {
                    // art-r5-03：まとまりの広がり全体がほぼ平ら（テント・ラクダ・焚き火が斜面に浮かない）
                    float mn, mx, c;
                    FootprintRange(w.h, w.sizeM, p, r, out mn, out mx, out c);
                    if (mx - mn > maxSpan) continue;
                    FootprintRange(w.h, w.sizeM, p, r * 0.5f, out mn, out mx, out c);
                    if (mx - mn > maxSpan * 0.5f) continue;
                }
                return true;
            }
            p = Vector3.zero;
            return false;
        }

        // ---- art-r5-15：遠くの小さな岩の「ごま塩」を減らし、カーブの外側に大きなシルエット ----
        public const float FAR_PEBBLE_ROAD_M = 60f, FAR_PEBBLE_MAX_H = 1.6f;
        public const float CORNER_CLUSTER_H_MIN = 6f, CORNER_CLUSTER_H_MAX = 10f, CORNER_CLUSTER_LAT_MIN = 40f, CORNER_CLUSTER_LAT_MAX = 90f;

        /// <summary>小さな岩（高さ FAR_PEBBLE_MAX_H 未満の岩の部品）か。</summary>
        public static bool IsSmallRock(Placement p) => p.height < FAR_PEBBLE_MAX_H && (p.asset == RCOLL || p.asset == DROCKS || p.asset == RSTYL || ArtPalette.IsRockAsset(p.asset));

        /// <summary>道から FAR_PEBBLE_ROAD_M より遠い小さな岩を 1 つおきに外す（線の目印・形の決まったまとまりは残す）。平原の均一な点々（ごま塩）を減らす。</summary>
        static void ThinFarPebbles(Work w)
        {
            int k = 0;
            for (int i = w.list.Count - 1; i >= 0; i--)
            {
                var p = w.list[i];
                if (!IsSmallRock(p) || LineGroups.Contains(p.group) || ShareExempt.Contains(p.group) || p.group == "BarrierLine") continue;
                if (WorldLayout.DistToPolylineXZ(p.position, w.course) < FAR_PEBBLE_ROAD_M) continue;
                if (k++ % 2 == 0) { w.Count(p, -1); w.list.RemoveAt(i); }
            }
        }

        /// <summary>
        /// 急カーブの外側の岩のまとまり（art-r5-15、BotW の「三角」の決まり）：頂点の外側 40〜90m に 3 つ、高さ 6〜10m のメサ B・大岩・岩のセットの大きな部品。
        /// 道の先を隠して見せる中くらいの距離（40〜150m）のシルエット。崖の板（Cliffs）は道から 260m 以内に置かない決まり（art-r3-05）なので使わない。
        /// </summary>
        static void CornerClusters(Work w, StageDef def, float goalArc)
        {
            const string G = "CornerSilhouette";
            int k = 0;
            foreach (var c in StageDefinitions.TightCorners(def, w.course))
            {
                if (c.apexArc < 120f || c.apexArc > goalArc - 80f) continue;
                Vector3 rt = WorldLayout.RightOf(WorldLayout.TangentAtArc(w.course, c.apexArc));
                float outSide = -c.side;   // 右へ曲がる（side > 0）なら外側は左
                for (int i = 0; i < 3; i++, k++)
                {
                    float hgt = Mathf.Lerp(CORNER_CLUSTER_H_MIN, CORNER_CLUSTER_H_MAX, ((k * 7) % 5) / 4f);
                    string asset, part;
                    if (i == 0) { asset = MESAB; part = null; }
                    else if (i == 1) { asset = DROCKS; part = RockBig[k % RockBig.Length]; }
                    else { asset = RSTYL; part = RstylBig[k % RstylBig.Length]; }
                    float sc = hgt / SizeOf(asset, part).h;
                    for (int tries = 0; tries < 6; tries++)
                    {
                        float along = (i - 1) * 18f + w.Rand(-6f, 6f);
                        float lat = Mathf.Lerp(CORNER_CLUSTER_LAT_MIN, CORNER_CLUSTER_LAT_MAX, (i * 0.35f + tries * 0.12f) % 1f);
                        Vector3 p = WorldLayout.PointAtArc(w.course, c.apexArc + along) + rt * (outSide * lat);
                        p.y = 0f;
                        if (w.Add(G, asset, part, p, w.Rand(0f, 360f), sc, 0.15f * hgt, ROAD_CLEAR)) break;
                    }
                }
            }
        }

        public const float CORNER_SIL_EDGE_M = 30f;
        /// <summary>Props/MesaRock（段の付いた八角の岩）を置いてよいコースからの最小距離（course-r3-12）。</summary>
        public const float MESA_ROCK_MIN_DIST_M = 250f;

        /// <summary>急カーブの外側の崖（course-r1-02）：Cliffs_01〜05 ×0.6〜0.9 を頂点の外側に置き、先の道を隠す（三角の影の決まり）。</summary>
        static void CornerSilhouettes(Work w, StageDef def, float goalArc)
        {
            var corners = StageDefinitions.TightCorners(def, w.course);
            for (int i = 0; i < corners.Count; i++)
            {
                var c = corners[i];
                // art-r2-08：道のすぐ外なので崖（Cliffs）ではなくメサ B（縦に伸ばす）。裾は斜面に埋まる
                Size cs = SizeOf(MESAB, null);
                float sy = 1.3f + 0.1f * (i % 4);
                for (int tries = 0; tries < 6; tries++)
                {
                    float sc = 0.7f - tries * 0.05f;
                    float r = cs.r * sc;
                    // course-r3-07：外側の道の縁から 30〜45m（頂点の 40m 手前の 22mm の画角に入る）
                    float lat = -c.side * (StageDefinitions.HalfWidthAt(def, c.apexArc) + CORNER_SIL_EDGE_M + tries * 3f);
                    float yaw = w.YawAt(c.apexArc) + i * 67f;
                    if (w.Add("CornerSilhouette", MESAB, null, w.Side(c.apexArc, lat), yaw, sc, CLIFF_SINK * cs.h * sc * sy, ROAD_MIN, rBounds: r * MESA_GATE_FOOTPRINT, scaleY: sy))
                        break;
                }
            }
        }

        /// <summary>
        /// 荒野の急カーブの出口の目印（course-r1-13）：次の直線の上の縁（道から 70〜95m）に給水塔（RanchCompound_11）か、
        /// 枯れ木の林と焚き火（煙の柱）を交互に。曲がった先で行き先が読める。
        /// </summary>
        static void RimLandmarks(Work w, StageDef def, float goalArc)
        {
            var corners = StageDefinitions.TightCorners(def, w.course);
            for (int i = 0; i < corners.Count; i++)
            {
                float a = Mathf.Min(goalArc - 20f, corners[i].toArc + 120f);
                for (int tries = 0; tries < 8; tries++)
                {
                    float sd = (tries % 2 == 0 ? 1f : -1f) * -corners[i].side;
                    Vector3 p = w.Side(a + (tries / 2) * 25f, sd * (70f + (tries / 2) * 8f));
                    if (i % 2 == 0)
                    {
                        // art-r4-15/17：風車の塔の代わりにメサ B の頂（尾根の上の三角のシルエット）
                        if (w.Add("RimLandmark", MESAB, null, p, w.YawAt(a) + w.Rand(-20f, 20f), 0.9f, 0.3f * SizeOf(MESAB, null).h * 0.9f, LANDMARK_MIN_ROAD_M - 10f, background: true)) break;
                    }
                    else if (w.Add("RimLandmark", CAMPFIRE, "Campfire_01", p, 0f, 1.2f, 0.05f, LANDMARK_MIN_ROAD_M - 10f))
                    {
                        Grove(w, "RimLandmark", p, 12f, 4, 1f, 0f);
                        break;
                    }
                }
            }
        }

        // ---- art-r4-17：峡谷の壁の頂の岩（段の付いた頂の線を隠す） ----
        public const float CREST_CAP_STEP_M = 40f, CREST_CAP_LAT_MIN = 34f, CREST_CAP_LAT_MAX = 90f;

        /// <summary>
        /// 峡谷の壁の頂（art-r4-17）：40m ごとに左右交互、道から 34〜90m の横の線でいちばん高い所（壁の頂）に、岩のセットの大きな岩（高さ 9m）と縦に伸ばした大岩を交互に 35% 埋める。
        /// 高さマップの段（のこぎりの歯）に見える頂の線を、三角の岩のシルエットで切る。group RimLandmark（斜面に埋める岩）。
        /// </summary>
        static void CanyonCrestCaps(Work w, StageDef def, float goalArc)
        {
            int k = 0;
            for (float a = 120f; a < goalArc - 60f; a += CREST_CAP_STEP_M, k++)
            {
                float sd = k % 2 == 0 ? 1f : -1f, best = float.MinValue, bestLat = -1f;
                for (float lat = CREST_CAP_LAT_MIN; lat <= CREST_CAP_LAT_MAX; lat += 4f)
                {
                    Vector3 q = w.Side(a, sd * (lat + w.Extra(a)));
                    if (q.x < EDGE_MARGIN + 20f || q.z < EDGE_MARGIN + 20f || q.x > w.sizeM - EDGE_MARGIN - 20f || q.z > w.sizeM - EDGE_MARGIN - 20f) break;
                    float g = w.Ground(q.x, q.z);
                    if (g > best) { best = g; bestLat = lat; }
                }
                if (bestLat < 0f) continue;
                Vector3 p = w.Side(a, sd * (bestLat + w.Extra(a)));
                if (k % 2 == 0)
                {
                    // メサ B は尾根のシルエット（RimSilhouettes）が使うので、頂は岩のセットの大きな岩（メサ B が素材の 8% を超えない）
                    string rp = RstylBig[k / 2 % RstylBig.Length];
                    float sc = 9f / Mathf.Max(0.5f, SizeOf(RSTYL, rp).h);
                    w.Add("RimLandmark", RSTYL, rp, p, w.Rand(0f, 360f), sc, 0.35f * SizeOf(RSTYL, rp).h * sc, ROAD_CLEAR, background: true);
                }
                else
                {
                    string part = RockBig[k % RockBig.Length];
                    const float SC = 0.45f, SY = 1.5f;
                    w.Add("RimLandmark", DROCKS, part, p, w.YawAt(a) + (sd < 0 ? 90f : -90f), SC, 0.35f * SizeOf(DROCKS, part).h * SC * SY, ROAD_CLEAR, background: true, scaleY: SY);
                }
            }
        }

        /// <summary>荒野の縁の柵（course-r1-18）：岩の橋の前の直線と最後の直線で、道の上の段の縁（道から 34m）に柵と杭の列（DesertProps_06/10）を 6m ごと。</summary>
        static void CanyonRimFences(Work w, StageDef def, float goalArc)
        {
            float bridge = SetPieces.BridgeArc(def, goalArc);
            var runs = new List<Vector2>();
            if (bridge >= 0f) runs.Add(new Vector2(bridge - 150f, bridge - 50f));
            runs.Add(new Vector2(goalArc - 640f, goalArc - 420f));
            int k = 0;
            foreach (var run in runs)
                for (float a = run.x; a <= run.y; a += GUIDE_STEP_M, k++)
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        string part = FencePiece(k);
                        w.Add("CanyonRim", part.StartsWith("FarmProps") ? FARM : DPROPS, part, w.Side(a, sd * 34f), w.YawAt(a) + (part == "FarmProps_04" ? 0f : 90f), 1f, 0.05f);
                    }
        }

        /// <summary>
        /// 境目の台地（course-r1-05）：台地の縁（道から MESA_BAND_HALF+3m）に Cliffs_08〜13 と DesertRocks_01〜03 を交互に 45m ごと、
        /// 鞍部（道幅 14m の絞り）の両脇に Cliffs_01 と Cliffs_03 ×0.8（道へ広い面を向ける）、300m 先の背景にメサ 2 つ。
        /// </summary>
        static void MesaPlateau(Work w, StageDef def, float goalArc)
        {
            float from, to;
            if (!SetPieces.MesaRange(def, goalArc, out from, out to)) return;
            float top0 = def.mesaClimbFraction * goalArc, top1 = def.mesaTopEndFraction * goalArc;
            // course-r3-03：登りの間、70m の帯の両縁の崖に段の付いた岩の積み重ね（台地へ登る「卓」の縁が読める）
            {
                int wk = 0;
                for (float a = top0 - SetPieces.MESA_RAMP_M; a <= top0; a += WALL_FACE_STEP_M, wk++)
                    for (int sd = -1; sd <= 1; sd += 2)
                        WallFace(w, def, a, sd, SetPieces.MESA_BAND_HALF, SetPieces.MESA_BAND_HALF + SetPieces.MESA_CLIFF_M, wk * 2 + (sd > 0 ? 1 : 0), false);
            }
            // 鞍部：一番狭い区間の真ん中
            float saddle = -1f;
            for (int k = 0; k < def.halfWidthM.Length; k++)
            {
                float m = 0.5f * (def.widthArc[2 * k] + def.widthArc[2 * k + 1]);
                if (def.halfWidthM[k] < HeightmapGen.RUNWAY_M && m >= top0 && m <= top1) { saddle = m; break; }
            }
            if (saddle >= 0f)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    // art-r2-08：崖の板ではなく DesertRocks_01 / 03 を縦に 1.5 倍
                    string part = sd < 0 ? "DesertRocks_01" : "DesertRocks_03";
                    const float SC = 0.8f, SY = 1.5f;
                    Size cs = SizeOf(DROCKS, part);
                    float r = cs.r * SC;
                    float yaw = w.YawAt(saddle) + (sd < 0 ? 20f : 200f);
                    // course-r2-08：ヘアピンの手前の札（置かない円）を避けて、鞍部の前後へずらしてもよい
                    float[] alongs = { 0f, -14f, 14f, -26f, 26f };
                    bool ok = false;
                    for (int tries = 0; tries < 10 && !ok; tries++)
                        ok = w.Add("MesaSaddle", DROCKS, part, w.Side(saddle + alongs[tries % 5], sd * (StageDefinitions.CHOKE_HALF_M + 3f + r * STRETCHED_ROCK_FOOTPRINT + (tries / 5) * 3f)), yaw, SC,
                            CLIFF_SINK * cs.h * SC * SY * 0.5f, ROAD_MIN, rBounds: r * STRETCHED_ROCK_FOOTPRINT, scaleY: SY);
                }
            int i = 0;
            for (float a = top0; a <= top1; a += 45f, i++)
            {
                if (saddle >= 0f && Mathf.Abs(a - saddle) < 40f) continue;
                float sd = i % 2 == 0 ? 1f : -1f;
                if (i % 3 == 2)
                {
                    string part = RockBig[i % RockBig.Length];
                    float sc = 0.8f;
                    float r = SizeOf(DROCKS, part).r * sc;
                    w.Add("MesaPlateau", DROCKS, part, w.Side(a, sd * (SetPieces.MESA_BAND_HALF + 3f + r * 0.5f)), w.Rand(0f, 360f), sc, 0.25f * SizeOf(DROCKS, part).h * sc);
                }
                else
                {
                    // art-r2-08：台地の縁はメサ B・岩のセット（縦に伸ばす）。崖の板（Cliffs）は道の近くに置かない
                    string asset, part; float sc, sy;
                    NearRock(w, 10f, 18f, out asset, out part, out sc, out sy);
                    float r = SizeOf(asset, part).r * sc;
                    float foot = asset == MESAB ? r * MESA_GATE_FOOTPRINT : r * STRETCHED_ROCK_FOOTPRINT;
                    w.Add("MesaPlateau", asset, part, w.Side(a, sd * (SetPieces.MESA_BAND_HALF + 3f + foot)), w.YawAt(a) + w.Rand(-25f, 25f), sc,
                        0.45f * SizeOf(asset, part).h * sc * sy, rBounds: foot, scaleY: sy);
                }
            }
            // 背景のメサ（鞍部から 300m、左右）
            if (saddle >= 0f)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 p = w.Side(saddle, sd * 300f);
                    p.x = Mathf.Clamp(p.x, 90f, w.sizeM - 90f); p.z = Mathf.Clamp(p.z, 90f, w.sizeM - 90f);
                    w.Add("MesaRing", sd < 0 ? MESA : MESAB, null, p, w.Rand(0f, 360f), 1.6f, 3f, background: true);
                }
        }

        /// <summary>境目の最後の直線（course-r1-18）：直線の始まりからゴールの町まで、左 44m（柵の外）に線路（WesternTown_049_Track）を並べる。</summary>
        static void FinalApproachRail(Work w, StageDef def, float goalArc)
        {
            var corners = StageDefinitions.TightCorners(def, w.course);
            float start = corners.Count > 0 ? corners[corners.Count - 1].toArc + 20f : goalArc - 400f;
            for (float a = start + RAIL_HALF; a + RAIL_HALF <= goalArc + GOAL_STREET_TO; a += SetPieces.RAIL_LENGTH)
            {
                Vector3 p = w.Side(a, -FINAL_RAIL_SIDE);
                if (!w.Add("Railway", WTOWN, RAIL_TRACK, p, w.YawAt(a), 1f, 0.15f, ROAD_CLEAR, ignoreCircles: true)) continue;
                w.list[w.list.Count - 1].collider = false;
                w.list[w.list.Count - 1].shape = SHAPE_NONE;
                for (int k = 0; k <= 12; k++)
                {
                    Vector3 q = w.Side(a - RAIL_HALF + k * SetPieces.RAIL_LENGTH / 12f, -FINAL_RAIL_SIDE);
                    w.circles.Add(new Vector4(q.x, 0f, q.z, 3.5f));
                }
            }
        }

        const float RAIL_HALF = SetPieces.RAIL_LENGTH * 0.5f;
        public const float FINAL_RAIL_SIDE = 44f;

        // ------------------------------------------------------------------
        // ロビー
        // ------------------------------------------------------------------

        /// <summary>
        /// ロビーの町のまわりの飾り（位置はロビー中心からの相対 m、地面 y = 0）。townMin/townMax は町の外形（x/z）、
        /// exclusions は置かない円（x, _, z, 半径）。ロビー全体＝町（柵は町の外形 + 8m）なので、飾りは町と柵の間の 8m の帯だけ：
        ///   東・西・北の辺の 3〜5m 外に 12m ごとの小さな山（木箱・樽・干し草・薪・柵・草）。南の辺（広場の前）には置かない。
        ///   木：北の辺（岩山の裏）に 9m ごと、東・西の辺は町の奥の壁（LobbyLayout.TOWN_BACK_Z）より北に 12m ごと。
        /// 町の中（通り・広場・練習場）には何も置かない。
        /// </summary>
        public static List<Placement> ForLobby(Vector2 townMin, Vector2 townMax, List<Vector4> exclusions, int seed)
        {
            var w = new Work
            {
                course = null, h = null, sizeM = 0f, budget = MAX_LOBBY,
                // ロビーの柵（LobbyLayout.FENCE_RECT）の 0.5m 内側まで（木の枝は柵にかかってよい）
                minX = LobbyLayout.FENCE_RECT.x + 0.5f, maxX = LobbyLayout.FENCE_RECT.z - 0.5f,
                minZ = LobbyLayout.FENCE_RECT.y + 0.5f, maxZ = LobbyLayout.FENCE_RECT.w - 0.5f,
                rng = new System.Random(seed), hasRect = true,
                rectMin = new Vector2(townMin.x - 1f, townMin.y - 1f), rectMax = new Vector2(townMax.x + 1f, townMax.y + 1f),
            };
            if (exclusions != null) w.circles.AddRange(exclusions);
            float wdt = townMax.x - townMin.x, dep = townMax.y - townMin.y;
            float perim = 2f * (wdt + dep);
            int n = Mathf.FloorToInt(perim / 12f);
            for (int i = 0; i < n; i++)
            {
                float d = i * perim / n;
                if (d < wdt) continue;   // 南の辺（広場の前）は空ける
                Vector3 p; float yaw;
                PerimeterPoint(townMin, townMax, d, w.Rand(3f, 5f), out p, out yaw);
                int kind = i % 5;
                string G = "TownEdge";
                if (kind == 0)
                {
                    w.Add(G, FARM, w.Pick(FarmFences), p, yaw, 1f, 0f);
                    w.Add(G, FARM, w.Pick(FarmFences), Offset(p, yaw, 2.8f), yaw, 1f, 0f);
                }
                else if (kind == 1)
                {
                    w.Add(G, FARM, w.Pick(FarmCrates), p, w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, FARM, w.Pick(FarmBarrels), w.Around(p, 2f), w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, FARM, w.Pick(FarmSmall), w.Around(p, 2.5f), w.Rand(0f, 360f), 1f, 0f);
                }
                else if (kind == 2)
                {
                    w.Add(G, HAY, "HayBales_01", p, w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, HAY, w.Pick(HayParts), w.Around(p, 2.5f), w.Rand(0f, 360f), 1f, 0f);
                }
                else if (kind == 3)
                {
                    w.Add(G, LOGS, w.Pick(LogParts), p, w.Rand(0f, 360f), 1.2f, 0f);
                    w.Add(G, LOGS, w.Pick(LogParts), w.Around(p, 2f), w.Rand(0f, 360f), 1.2f, 0f);
                    w.Add(G, FARM, w.Pick(new[] { "FarmProps_16", "FarmProps_17", "FarmProps_18" }), w.Around(p, 2f), w.Rand(0f, 360f), 1f, 0f);
                }
                else
                {
                    w.Add(G, DPROPS, w.Pick(DpGrass), p, w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, CACTI, w.Pick(CactiParts), w.Around(p, 2.5f), w.Rand(0f, 360f), 0.8f, 0f, trunk: true);
                    w.Add(G, DPROPS, w.Pick(DpCacti), w.Around(p, 2.5f), w.Rand(0f, 360f), 1f, 0f, trunk: true);
                }
            }
            // 木：町と柵の間の帯の真ん中（町の縁から 4〜5m）に、北の辺は 9m ごと、東・西の辺は 14m ごと（南の入口の前は円で除く）。
            // 帯は 8m しかないので、枝は柵・町の縁にかかってよい（範囲は幹のまわり 1.2m で調べる）。
            int k = 0;
            for (float x = townMin.x + 4f; x < townMax.x - 2f; x += 9f, k++)
                LobbyTree(w, new Vector3(x + w.Rand(-2f, 2f), 0f, townMax.y + w.Rand(4f, 5f)), k);
            for (float z = townMin.y + 8f; z < townMax.y; z += 14f, k++)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                    LobbyTree(w, new Vector3(s2 < 0 ? townMin.x - w.Rand(4f, 5f) : townMax.x + w.Rand(4f, 5f), 0f, z + w.Rand(-2f, 2f)), k + s2);
            Backlot(w);
            PlazaApron(w, townMin, townMax);   // art-r4-07：スポーンの手前の両脇（先に置く：通りの飾りより優先）
            StreetDressing(w, townMin, townMax);
            PlateEdge(w, townMin, townMax);
            return w.list;
        }

        // ---- art-r4-07：広場の手前（スポーンの視線の両脇） ----
        public const float APRON_LANE_HALF_M = 2.5f, APRON_X_MIN = 5f, APRON_X_MAX = 12f, APRON_Z_MIN = 32f, APRON_Z_MAX = 48f;

        /// <summary>スポーンからの視線（向き SPAWN_YAW）の z での x（ロビー中心からの相対）。</summary>
        public static float SpawnAxisX(float z) => LobbyLayout.SPAWN.x + Mathf.Tan(LobbyLayout.SPAWN_YAW * Mathf.Deg2Rad) * (z - LobbyLayout.SPAWN.z);

        /// <summary>
        /// 広場の手前（art-r4-07）：スポーンの視線の両脇 x = 視線 ±3.6〜8m（スポーンから見た 60° の扇の中）、z = 34〜49m に、丸太を板のように並べた板張りの縁（WoodLogs_01 を平らに）、
        /// つなぎ柵（FarmProps_03 ×0.6 と立てた丸太の杭）とつないだ馬（Horse.fbx、アニメーターなし）、樽の山（WesternTown_117）、荷車（WesternTown_115）、木箱、
        /// 街灯（FarmProps_02、暖色の灯り）。視線の 5m の通り道は空ける。遊びに使う物のまわりは空ける。
        /// </summary>
        static void PlazaApron(Work w, Vector2 townMin, Vector2 townMax)
        {
            const string G = "PlazaApron";
            bool rect = w.hasRect;
            w.hasRect = false;
            // スポーンから見た 60° の扇（4〜20m）に入るよう、視線から 3〜8m（遠いほど広く）に置く
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float X(float off, float z) => SpawnAxisX(z) + sd * off;
                // 板張りの縁：丸太の束を平らに 4 枚（2.9m × 2m × 0.26m）
                for (int i = 0; i < 4; i++)
                {
                    float z = 37f + i * 2.9f;
                    var p = new Vector3(X(7.5f, z), 0f, z);
                    if (ClearOfSpots(p, 1.5f)) w.Add(G, LOGS, POST_PART, p, 90f, 2f, 0.02f, ROAD_CLEAR, ignoreCircles: true, scaleY: 0.4f, rOverlap: 0f);
                }
                // つなぎ柵と馬
                {
                    float z = 40f;
                    var rp = new Vector3(X(5f, z), 0f, z);
                    if (ClearOfSpots(rp, 1.5f))
                    {
                        w.Add(G, FARM, "FarmProps_03", rp, 90f, 0.6f, 0.02f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                        w.AddPost(G, rp + new Vector3(0f, 0f, -0.9f), 0f, 1.2f, 0.16f, ROAD_CLEAR, true);
                        w.AddPost(G, rp + new Vector3(0f, 0f, 0.9f), 0f, 1.2f, 0.16f, ROAD_CLEAR, true);
                        w.Add(G, HORSE, null, rp + new Vector3(sd * 1.6f, 0f, 0.3f), sd > 0 ? -90f : 90f, 1f, 0.02f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                    }
                }
                // 樽の山・荷車（西）／木箱（東）・木箱と樽・街灯・干し草
                var bp = new Vector3(X(6.5f, 46.5f), 0f, 46.5f);
                if (ClearOfSpots(bp, 1.8f)) w.Add(G, WTOWN, TOWN_BARRELS, bp, sd * 30f, 1f, 0.05f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                var cp = new Vector3(X(5.5f, 34.5f), 0f, 34.5f);
                if (ClearOfSpots(cp, 1.2f))
                    w.Add(G, sd < 0 ? WTOWN : FARM, sd < 0 ? TOWN_CART : FarmCrates[0], cp, sd < 0 ? 20f : 15f, 1f, 0.05f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                var kp = new Vector3(X(4f, 43.5f), 0f, 43.5f);
                if (ClearOfSpots(kp, 1f))
                {
                    w.Add(G, WTOWN, TownCrates[(sd + 1) % TownCrates.Length], kp, sd * 12f, 1f, 0.05f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                    w.Add(G, FARM, FarmBarrels[0], kp + new Vector3(sd * 1.2f, 0f, 1.1f), 0f, 1f, 0.05f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                }
                var lp = new Vector3(X(3.6f, 48.5f), 0f, 48.5f);
                if (ClearOfSpots(lp, 0.8f)) w.Add(G, FARM, FARM_LAMP, lp, sd > 0 ? -90f : 90f, 1f, 0.02f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
                var hp = new Vector3(X(8f, 48f), 0f, 48f);
                if (ClearOfSpots(hp, 1f)) w.Add(G, HAY, "HayBales_02", hp, sd * 20f, 1.4f, 0.02f, ROAD_CLEAR, ignoreCircles: true, rOverlap: 0f);
            }
            w.hasRect = rect;
        }

        /// <summary>遊びに使う物（LobbyLayout.FunctionalSpots）・練習場から r + 0.6m 以上離れているか。</summary>
        static bool ClearOfSpots(Vector3 p, float r)
        {
            foreach (var kv in LobbyLayout.FunctionalSpots())
                if (WorldLayout.DistXZ(p, new Vector3(kv.Value.x, 0f, kv.Value.z)) < kv.Value.w + r + 0.6f) return false;
            Vector4 pr = LobbyLayout.PRACTICE_RECT;
            return !(p.x > pr.x - 1f - r && p.x < pr.z + 1f + r && p.z > pr.y - 1f - r && p.z < pr.w + 1f + r);
        }

        /// <summary>町の大通りの板張りの縁の線（町の南端 z = 25・中心 x = 0 のとき）：西 x、東 x、南の z、北の z（Blender で測った建物 029〜070 の正面）。</summary>
        public static readonly Vector4 BOARDWALKS = new Vector4(-4.4f, 5.6f, 49f, 112f);

        /// <summary>大通りの板張りの縁の 2 本の線（ロビー中心からの相対、町の外形の実測でずらす）。</summary>
        public static Vector3[][] BoardwalkLines(Vector2 townMin, Vector2 townMax)
        {
            float dx = (townMin.x + townMax.x) * 0.5f, dz = townMin.y - LobbyLayout.TOWN_NOMINAL_RECT.y;
            Vector4 b = BOARDWALKS;
            return new[]
            {
                new[] { new Vector3(b.x + dx, 0f, b.z + dz), new Vector3(b.x + dx, 0f, b.w + dz) },
                new[] { new Vector3(b.y + dx, 0f, b.z + dz), new Vector3(b.y + dx, 0f, b.w + dz) },
            };
        }

        /// <summary>
        /// 町の大通り（art-r3-09）：板張りの縁の両側に樽・木箱・干し草・町の木箱 7m ごと、街灯 16m ごと（LobbyBuilder が暖色の灯り）、荷車・樽の山 30m ごと。
        /// 通りの真ん中は町の小石と草を 5m ごと（踏み固めた土に見せる）。遊びに使う物（FunctionalSpots）のまわりは空ける。
        /// </summary>
        static void StreetDressing(Work w, Vector2 townMin, Vector2 townMax)
        {
            bool rect = w.hasRect;
            w.hasRect = false;
            var lines = BoardwalkLines(townMin, townMax);
            for (int li = 0; li < 2; li++)
            {
                Vector3 a = lines[li][0], b = lines[li][1];
                float len = WorldLayout.DistXZ(a, b);
                float face = li == 0 ? 90f : -90f;   // 西の縁は東（通り）を向く
                int n = 0;
                for (float d = 1.5f; d <= len; d += STREET_PROP_STEP_M, n++)
                {
                    Vector3 p = Vector3.Lerp(a, b, d / len);
                    if (!ClearOfSpots(p, 1.2f)) continue;
                    StreetProp(w, p, face, n + li, ROAD_CLEAR, true);
                }
            }
            // 通りの真ん中：町の小石と草
            float cx = (lines[0][0].x + lines[1][0].x) * 0.5f;
            int k = 0;
            for (float z = LobbyLayout.SPAWN.z + 4f; z <= lines[0][1].z; z += 5f, k++)
            {
                // art-r4-07：スポーンからの視線（幅 5m の通り道）を空け、東寄りに置く（前は干し草・岩が通りの真ん中にあった）
                Vector3 p = new Vector3(SpawnAxisX(z) + APRON_LANE_HALF_M + w.Rand(0.6f, 2f), 0f, z + w.Rand(-1.5f, 1.5f));
                if (!ClearOfSpots(p, 0.8f)) continue;
                if (k % 2 == 0) w.Add("StreetDressing", WTOWN, TownPebbles[k / 2 % TownPebbles.Length], p, w.Rand(0f, 360f), w.Rand(1f, 1.4f), 0.05f, ROAD_CLEAR, ignoreCircles: true);
                else w.Add("StreetDressing", DPROPS, DpGrass[k % 2], p, w.Rand(0f, 360f), w.Rand(0.7f, 1f), 0.05f, ROAD_CLEAR, ignoreCircles: true);
            }
            w.hasRect = rect;
        }

        /// <summary>町の地面の板（WesternTown_001_Ground）の外形（町の南端 z = 25・中心 x = 0 のとき、Blender で測った値）。</summary>
        public static readonly Vector4 TOWN_PLATE = new Vector4(-36.6f, 27.3f, 37.5f, 189.2f);
        public const float PLATE_EDGE_STEP_M = 4f;

        /// <summary>
        /// 町の地面の板の縁（art-r3-09）：外形のまわりを 4m ごとに、町の小さな岩と砂漠の草（DesertProps_22/23）を半分埋めて、板の硬い縁を崩す
        /// （LobbyLayout.PlateSand の砂の縁と合わせる）。遊びに使う物・練習場のまわりは空ける。
        /// </summary>
        static void PlateEdge(Work w, Vector2 townMin, Vector2 townMax)
        {
            bool rect = w.hasRect;
            w.hasRect = false;
            float dx = (townMin.x + townMax.x) * 0.5f, dz = townMin.y - LobbyLayout.TOWN_NOMINAL_RECT.y;
            var mn = new Vector2(TOWN_PLATE.x + dx, TOWN_PLATE.y + dz);
            var mx = new Vector2(TOWN_PLATE.z + dx, TOWN_PLATE.w + dz);
            float per = 2f * ((mx.x - mn.x) + (mx.y - mn.y));
            int k = 0;
            for (float d = 2f; d < per; d += PLATE_EDGE_STEP_M, k++)
            {
                Vector3 p; float yaw;
                PerimeterPoint(mn, mx, d, w.Rand(-0.8f, 0.8f), out p, out yaw);
                if (!ClearOfSpots(p, 1f)) continue;
                if (k % 2 == 0)
                {
                    string part = TownRocksSmall[k / 2 % TownRocksSmall.Length];
                    float sc = w.Rand(0.45f, 0.75f);
                    w.Add("PlateEdge", WTOWN, part, p, w.Rand(0f, 360f), sc, 0.5f * SizeOf(WTOWN, part).h * sc, ROAD_CLEAR, ignoreCircles: true);
                }
                else w.Add("PlateEdge", DPROPS, DpGrass[(k / 2) % 2], p, w.Rand(0f, 360f), w.Rand(0.8f, 1.2f), 0.15f, ROAD_CLEAR, ignoreCircles: true);
            }
            w.hasRect = rect;
        }

        /// <summary>
        /// 町の裏（art-r1-16）：東の建物の列の裏と線路の間（LobbyLayout.BacklotSpots、6m ごと）に、柵・干し草・薪・樽・木箱と木 4 本。
        /// 練習場へ行く道から建物の平らな裏が見えないように。町の中なので町の外形の除外は外し、通り道をふさがない小さな物だけ（木は当たり判定なし）。
        /// </summary>
        static void Backlot(Work w)
        {
            const string G = "Backlot";
            bool rect = w.hasRect;
            w.hasRect = false;
            var spots = LobbyLayout.BacklotSpots();
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 p = spots[i];
                switch (i % 4)
                {
                    case 0:
                        w.Add(G, FARM, "FarmProps_04", p, 0f, 1f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        break;
                    case 1:
                        w.Add(G, HAY, w.Pick(new[] { "HayBales_02", "HayBales_03", "HayBales_04" }), p, 0f, 1f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        w.Add(G, HAY, "HayBales_05", p + new Vector3(0f, 0f, 1.1f), 90f, 1f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        break;
                    case 2:
                        w.Add(G, FARM, w.Pick(FarmBarrels), p, w.Rand(0f, 360f), 1f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        w.Add(G, LOGS, w.Pick(new[] { "WoodLogs_04", "WoodLogs_06", "WoodLogs_07" }), p + new Vector3(0f, 0f, 1.2f), 0f, 1.2f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        break;
                    default:
                        w.Add(G, FARM, "FarmProps_14", p, w.Rand(0f, 360f), 1f, 0f, ROAD_CLEAR, ignoreCircles: true);
                        break;
                }
                // 木（4 本）：幹は帯の中、枝は線路と建物の裏にかぶさって平らな壁を隠す
                if (i % 2 == 0 && w.Add(G, TREES, TreePart(w), p + new Vector3(0f, 0f, 3f), w.Rand(0f, 360f), w.Rand(TREE_SCALE_MIN, TREE_SCALE_MAX), 0.2f,
                        ROAD_CLEAR, trunk: true, ignoreCircles: true, rBounds: 0.3f))
                {
                    w.list[w.list.Count - 1].collider = false;
                    w.list[w.list.Count - 1].shape = SHAPE_NONE;
                }
            }
            w.hasRect = rect;
        }

        static void LobbyTree(Work w, Vector3 p, int k)
        {
            bool ac = ((k % 3) + 3) % 3 == 0;
            w.Add("TreeRows", ac ? ACACIA : TREES, ac ? null : TreePart(w), p, w.Rand(0f, 360f), w.Rand(TREE_SCALE_MIN, TREE_SCALE_MAX), 0.2f,
                ROAD_CLEAR, trunk: true, rBounds: 1.2f);
        }

        static Vector3 Offset(Vector3 p, float yaw, float d)
        {
            float a = (yaw + 90f) * Mathf.Deg2Rad;
            return new Vector3(p.x + Mathf.Sin(a) * d, 0f, p.z + Mathf.Cos(a) * d);
        }

        /// <summary>長方形の外周を距離 d 進んだ点を、外側へ out m ずらす。yaw は外周に沿った向き（部品のローカル X が外周に沿う）。</summary>
        static void PerimeterPoint(Vector2 mn, Vector2 mx, float d, float outM, out Vector3 p, out float yaw)
        {
            float wdt = mx.x - mn.x, dep = mx.y - mn.y;
            if (d < wdt) { p = new Vector3(mn.x + d, 0f, mn.y - outM); yaw = 0f; return; }
            d -= wdt;
            if (d < dep) { p = new Vector3(mx.x + outM, 0f, mn.y + d); yaw = 90f; return; }
            d -= dep;
            if (d < wdt) { p = new Vector3(mx.x - d, 0f, mx.y + outM); yaw = 0f; return; }
            d -= wdt;
            p = new Vector3(mn.x - outM, 0f, mx.y - Mathf.Min(d, dep)); yaw = 90f;
        }

        // ------------------------------------------------------------------
        // 背景の山並み
        // ------------------------------------------------------------------

        /// <summary>
        /// 遠景の山並みの素材（art-r3-10 → art-r4-09）：メサ・メサ B（2 倍の重み）・大岩・岩のセットの大きな部品。
        /// 画像の無い崖の板（Cliffs_02/06/09）は拡大すると平らな橙の箱になり、地層の模様のあるメサ・大岩の横で仮置きに見えたのでやめた。
        /// </summary>
        static readonly string[][] BackdropAssets =
        {
            new[] { MESA, null }, new[] { MESAB, null }, new[] { DROCKS, "DesertRocks_01" }, new[] { RSTYL, "RockSetStylized_01_Rock" },
            new[] { MESAB, null }, new[] { MESAB, null }, new[] { DROCKS, "DesertRocks_02" }, new[] { MESA, null },
            new[] { MESA, null }, new[] { RSTYL, "RockSetStylized_06_Rock" }, new[] { DROCKS, "DesertRocks_03" }, new[] { MESAB, null },
        };
        /// <summary>
        /// ステージの縁の継ぎ目の輪（art-r4-01）の素材と拡大：メサ B・メサ（0.9〜1.4 倍 = 高さ 25〜45m、外周の尾根と同じ高さ）、
        /// 大岩 DesertRocks_01〜03 と岩のセット RockSetStylized_06（2.5〜4.5 倍）。崖の板は使わない。
        /// </summary>
        static readonly string[][] SeamAssets =
        {
            new[] { MESAB, null }, new[] { MESA, null }, new[] { DROCKS, "DesertRocks_01" }, new[] { DROCKS, "DesertRocks_02" },
            new[] { DROCKS, "DesertRocks_03" }, new[] { MESAB, null }, new[] { MESA, null }, new[] { DROCKS, "DesertRocks_02" },
        };   // art-r5-05：小さめの岩のセット（RockSetStylized）は外した（縁をなぞる均一な輪に見えた）
        public const float SEAM_SCALE_MIN = 2.5f, SEAM_SCALE_MAX = 4.5f, SEAM_MESA_SCALE_MIN = 0.9f, SEAM_MESA_SCALE_MAX = 1.4f;
        /// <summary>継ぎ目の岩の埋める割合（art-r4-01）。</summary>
        public const float SEAM_SINK = 0.35f;

        // ---- art-r4-09：崖の板は一番外の輪だけ ----
        public const float CLIFF_RING_OFFSET_M = 860f, CLIFF_RING_STEP_M = 650f, CLIFF_RING_SCALE = 3f;
        public const float CLIFF_MIN_SPAWN_M = 900f, CLIFF_MIN_FOG = 0.7f;

        /// <summary>崖の板を置いてよいか（art-r4-09）：どのコースの点・ロビーのスポーンからも CLIFF_MIN_SPAWN_M より遠く、霧が CLIFF_MIN_FOG 以上かかる。</summary>
        public static bool CliffFarEnough(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var d in StageDefinitions.All)
            {
                var local = StageDefinitions.SampleLocal(d);
                Vector3 o = d.Origin;
                for (int i = 0; i < local.Length; i += 5)
                    best = Mathf.Min(best, WorldLayout.DistXZ(p, local[i] + o));
            }
            best = Mathf.Min(best, WorldLayout.DistXZ(p, WorldBounds.LOBBY_CENTER + LobbyLayout.SPAWN));
            return best >= CLIFF_MIN_SPAWN_M && LightingPlan.FogFraction(best) >= CLIFF_MIN_FOG;
        }

        public static float SeamScale(string asset, float u)
            => asset == MESA || asset == MESAB ? Mathf.Lerp(SEAM_MESA_SCALE_MIN, SEAM_MESA_SCALE_MAX, u) : Mathf.Lerp(SEAM_SCALE_MIN, SEAM_SCALE_MAX, u);
        public const float BACKDROP_BASE_SCALE = 4.5f;                 // 拡大 = 4.5 × 0.7〜2.2
        public const float BACKDROP_SCALE_MIN = 0.7f, BACKDROP_SCALE_MAX = 2.2f;
        public const float BACKDROP_GAP_MIN = 40f, BACKDROP_GAP_MAX = 120f;   // まとまりの間のすき間のばらつき

        static float R01(System.Random rng) => (float)rng.NextDouble();

        /// <summary>外接長方形 u の周上の点（t = 周の距離）と外向きの法線。</summary>
        static void RectPerimeter(Vector4 u, float t, out Vector3 p, out Vector3 nrm)
        {
            float w = u.z - u.x, d = u.w - u.y, perim = 2f * (w + d);
            t = (t % perim + perim) % perim;
            if (t < w) { p = new Vector3(u.x + t, 0f, u.y); nrm = new Vector3(0f, 0f, -1f); }
            else if (t < w + d) { p = new Vector3(u.z, 0f, u.y + (t - w)); nrm = new Vector3(1f, 0f, 0f); }
            else if (t < 2f * w + d) { p = new Vector3(u.z - (t - w - d), 0f, u.w); nrm = new Vector3(0f, 0f, 1f); }
            else { p = new Vector3(u.x, 0f, u.w - (t - 2f * w - d)); nrm = new Vector3(-1f, 0f, 0f); }
        }

        // ---- art-r5-05：継ぎ目のまとまり ----
        public const float SEAM_CLUSTER_GAP_MIN = 150f, SEAM_CLUSTER_GAP_MAX = 320f, SEAM_CORNER_SCALE_MIN = 2f, SEAM_CORNER_SCALE_MAX = 2.6f;

        static bool SeamClear(List<Placement> list, Vector4[] rects, int si, Vector3 p, float rad)
        {
            for (int ri = 0; ri < rects.Length; ri++) if (ri != si && WorldBounds.DistToRect(p, rects[ri]) < rad + 10f) return false;
            foreach (var q in list) if (q.group == "BackdropNear" && WorldLayout.DistXZ(p, q.position) < 0.45f * (rad + q.radius)) return false;
            return true;
        }

        static void AddSeamPiece(List<Placement> list, string asset, string part, Vector3 p, float scale, float yaw)
        {
            var piece = BackdropPiece("BackdropNear", asset, part, p, scale, yaw);
            piece.position = new Vector3(p.x, piece.position.y + (WorldBounds.BACKDROP_SINK - SEAM_SINK) * piece.height, p.z);
            list.Add(piece);
        }

        static Placement BackdropPiece(string group, string asset, string part, Vector3 p, float scale, float yaw)
        {
            Size sz = SizeOf(asset, part);
            float h = sz.h * scale;
            return new Placement
            {
                group = group, asset = asset, part = part,
                position = new Vector3(p.x, WorldBounds.FILLER_Y + WorldBounds.FillerBaseHeight(p.x, p.z) - WorldBounds.BACKDROP_SINK * h, p.z),
                yawDeg = yaw, scale = scale, radius = sz.r * scale, height = h,
                collider = false, shape = SHAPE_NONE, background = false,
            };
        }

        /// <summary>
        /// ワールド全体を囲む遠景の山並み（ワールド座標、art-r3-10）。遊べる範囲の外接長方形の外側に 2 列（BACKDROP_OFFSET_M・BACKDROP_OFFSET2_M）、
        /// 2〜4 個のまとまり（メサ・メサ B・大岩・岩のセット・崖の板）を、まとまりの間のすき間を 40〜120m ばらして置く（等間隔の判押しにしない）。
        /// 拡大 4.5 × 0.7〜2.2、向きはばらばら（Collider なし、静的）。足元は外の地面（WorldBounds.FillerBaseHeight）の高さで、高さの 25% を埋める。
        /// 山の足元は遊べる範囲から BACKDROP_CLEAR_M m 以上離す。3 列目（ステージのすぐ外）も 2〜3 個のまとまりをばらした間隔で。
        /// </summary>
        public static List<Placement> Backdrop(int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            Vector4 u = WorldBounds.Union();
            float perim = 2f * ((u.z - u.x) + (u.w - u.y));
            var rects = WorldBounds.PlayableRects();
            float[] offs = { WorldBounds.BACKDROP_OFFSET_M, WorldBounds.BACKDROP_OFFSET2_M };
            float[] gaps = { WorldBounds.BACKDROP_SPACING_M, WorldBounds.BACKDROP_SPACING2_M };
            int k = 0;
            for (int row = 0; row < 2; row++)
            {
                float t = row * gaps[row] * 0.5f;
                while (t < perim + row * gaps[row] * 0.5f - gaps[row] * 0.8f)
                {
                    Vector3 c0, nrm;
                    RectPerimeter(u, t, out c0, out nrm);
                    int n = 2 + rng.Next(3);
                    float clusterW = 0f;
                    Vector3 tan = new Vector3(-nrm.z, 0f, nrm.x);
                    float along = 0f;
                    for (int i = 0; i < n; i++, k++)
                    {
                        string[] ap = BackdropAssets[(k * 7 + rng.Next(BackdropAssets.Length)) % BackdropAssets.Length];
                        float scale = BACKDROP_BASE_SCALE * (BACKDROP_SCALE_MIN + R01(rng) * (BACKDROP_SCALE_MAX - BACKDROP_SCALE_MIN));
                        float r = SizeOf(ap[0], ap[1]).r * scale;
                        float outM = Mathf.Max(offs[row] + (R01(rng) * 2f - 1f) * WorldBounds.BACKDROP_JITTER_M, r + WorldBounds.BACKDROP_CLEAR_M);
                        Vector3 p = c0 + tan * along + nrm * outM;
                        // 遊べる範囲から離す（角の近くでは外へ押し出す）
                        for (int it = 0; it < 8; it++)
                        {
                            bool ok = true;
                            foreach (var q in rects) if (WorldBounds.DistToRect(p, q) < r + WorldBounds.BACKDROP_CLEAR_M) ok = false;
                            if (ok) break;
                            p += nrm * 40f;
                        }
                        Vector4 f = WorldBounds.FillerRect();
                        if (p.x - r <= f.x || p.x + r >= f.z || p.z - r <= f.y || p.z + r >= f.w) continue;
                        list.Add(BackdropPiece("Backdrop", ap[0], ap[1], p, scale, R01(rng) * 360f));
                        along += r * (0.9f + R01(rng) * 0.6f);
                        clusterW = along;
                    }
                    t += clusterW + gaps[row] * 1.1f + BACKDROP_GAP_MIN + R01(rng) * (BACKDROP_GAP_MAX - BACKDROP_GAP_MIN) * 3f;
                }
            }
            // 一番外の崖の板（art-r4-09）：画像の無い崖の板（Cliffs_02/06/09）は霧が 70% 以上かかる、スポーン・コースから 900m より遠い所だけ
            // （外接長方形から CLIFF_RING_OFFSET_M、まとまりの間 CLIFF_RING_STEP_M）。赤錆の掛け色と _Flatten は ArtPalette が付ける
            {
                string[] cliffParts = { "Cliffs_02", "Cliffs_06", "Cliffs_09" };
                int ci = 0;
                for (float t = R01(rng) * CLIFF_RING_STEP_M; t < perim; t += CLIFF_RING_STEP_M * (0.8f + 0.4f * R01(rng)), ci++)
                {
                    Vector3 c0, nrm;
                    RectPerimeter(u, t, out c0, out nrm);
                    string part = cliffParts[ci % cliffParts.Length];
                    float scale = CLIFF_RING_SCALE * (0.85f + 0.3f * R01(rng));
                    float r = SizeOf(CLIFFS, part).r * scale;
                    Vector3 p = c0 + nrm * CLIFF_RING_OFFSET_M;
                    Vector4 f = WorldBounds.FillerRect();
                    if (p.x - r <= f.x || p.x + r >= f.z || p.z - r <= f.y || p.z + r >= f.w) continue;
                    if (!CliffFarEnough(p)) continue;
                    list.Add(BackdropPiece("BackdropFar", CLIFFS, part, p, scale, R01(rng) * 360f));
                }
            }
            // 3 列目＝継ぎ目のまとまり（art-r2-09 → art-r4-01 → art-r5-05）：縁に沿って 150〜320m の不規則な間隔で 3〜6 個の大きな岩・メサのまとまりを縁をまたいで、
            // 四隅には大きなメサ（×2〜2.6）。前の 56〜80m ごとの同じ大きさの岩の輪は四角い板の縁をかえってなぞっていた。
            // 色の継ぎ目は地面の色の帯（HeightmapGen.GroundTint）がぼかす。高さは SceneAssembler / tools/preview が縁の地面に置き直し、高さの 35% を埋める
            for (int si = 0; si < StageDefinitions.All.Length; si++)
            {
                Vector4 r = rects[si];
                float rw = r.z - r.x, rd = r.w - r.y, per = 2f * (rw + rd);
                float[] corners = { 0f, rw, rw + rd, 2f * rw + rd };
                // 四隅：外へ斜めの大きなメサ・メサ B
                for (int ci = 0; ci < 4; ci++)
                {
                    Vector3 c0, n0, n1;
                    RectPerimeter(r, corners[ci] + 1f, out c0, out n0);
                    RectPerimeter(r, corners[ci] - 1f, out c0, out n1);
                    RectPerimeter(r, corners[ci], out c0, out n0);
                    Vector3 diag = (n0 + n1).normalized;
                    for (int i = 0; i < 2; i++, k++)
                    {
                        string asset = (ci + i) % 2 == 0 ? MESAB : MESA;
                        float scale = Mathf.Lerp(SEAM_CORNER_SCALE_MIN, SEAM_CORNER_SCALE_MAX, R01(rng));
                        float rad = SizeOf(asset, null).r * scale, foot = rad * MESA_GATE_FOOTPRINT;
                        Vector3 side = i == 0 ? Vector3.zero : new Vector3(-diag.z, 0f, diag.x) * (foot * (R01(rng) < 0.5f ? 1.1f : -1.1f));
                        Vector3 p = c0 + diag * (foot - 15f) + side;
                        if (!SeamClear(list, rects, si, p, rad)) continue;
                        AddSeamPiece(list, asset, null, p, scale, R01(rng) * 360f);
                    }
                }
                // 縁に沿ったまとまり（隅から 120m の内は隅のメサに任せる）
                for (float t0 = 60f + R01(rng) * 120f; t0 < per; t0 += Mathf.Lerp(SEAM_CLUSTER_GAP_MIN, SEAM_CLUSTER_GAP_MAX, R01(rng)))
                {
                    bool nearCorner = false;
                    foreach (float cc in corners) if (Mathf.Abs(Mathf.DeltaAngle(t0 / per * 360f, cc / per * 360f)) * per / 360f < 120f) nearCorner = true;
                    if (nearCorner) continue;
                    Vector3 c0, nrm;
                    RectPerimeter(r, t0, out c0, out nrm);
                    Vector3 tan = new Vector3(-nrm.z, 0f, nrm.x);
                    int n = 3 + rng.Next(4);
                    float along = -20f;
                    for (int i = 0; i < n; i++, k++)
                    {
                        string[] ap = SeamAssets[(k + i * 3 + rng.Next(SeamAssets.Length)) % SeamAssets.Length];
                        float scale = SeamScale(ap[0], R01(rng));
                        float rad = SizeOf(ap[0], ap[1]).r * scale;
                        // 縁をまたぐ：中心は縁の内 15m 〜 外 35m（まとまりの中で前後に揺らす）
                        Vector3 p = c0 + tan * along + nrm * (rad * 0.4f - 15f + R01(rng) * 50f);
                        along += rad * (0.7f + R01(rng) * 0.6f);
                        if (!SeamClear(list, rects, si, p, rad)) continue;
                        AddSeamPiece(list, ap[0], ap[1], p, scale, R01(rng) * 360f);
                    }
                }
            }
            return list;
        }

        /// <summary>まとまりの並びの不規則さ（art-r3-10）：group の飾りを中心 center のまわりの角度で並べ、隣どうしの角度のすき間の変動係数（標準偏差 ÷ 平均）。</summary>
        public static float SpacingVariation(List<Placement> list, string group, Vector3 center)
        {
            var ang = new List<float>();
            foreach (var p in list) if (p.group == group) ang.Add(Mathf.Atan2(p.position.x - center.x, p.position.z - center.z));
            if (ang.Count < 3) return 0f;
            ang.Sort();
            var gaps = new List<float>();
            for (int i = 0; i < ang.Count; i++) gaps.Add(i + 1 < ang.Count ? ang[i + 1] - ang[i] : ang[0] + Mathf.PI * 2f - ang[i]);
            float mean = 0f; foreach (float g in gaps) mean += g; mean /= gaps.Count;
            float v = 0f; foreach (float g in gaps) v += (g - mean) * (g - mean); v /= gaps.Count;
            return mean <= 1e-6f ? 0f : Mathf.Sqrt(v) / mean;
        }

        /// <summary>素材（FBX）ごとの個数。</summary>
        public static Dictionary<string, int> CountByAsset(List<Placement> list)
        {
            var r = new Dictionary<string, int>();
            foreach (var p in list)
            {
                int n;
                r.TryGetValue(p.asset, out n);
                r[p.asset] = n + 1;
            }
            return r;
        }
    }

    /// <summary>
    /// ワールド全体の範囲（UnityEditor に依存しない）。遊べる範囲 = 3 ステージの Terrain とロビーの地面。
    /// その外側に背景の山並み（DressingPlan.Backdrop）、全体の下に地面の板（FillerRect、y = FILLER_Y）。
    /// 長方形は Vector4(minX, minZ, maxX, maxZ)。
    /// </summary>
    public static class WorldBounds
    {
        /// <summary>
        /// ロビー（町）の中心。ステージ 2 の南の端（z = -500）から十分離し、町のどこから見てもステージの地形が尾根の上に見えるときは
        /// 霧がほぼかかる距離（LightingPlan.FogHiddenDistance）より遠い（LobbyLayoutTests.StagesHiddenFromLobby）。
        /// </summary>
        public static readonly Vector3 LOBBY_CENTER = new Vector3(0f, 0f, -1580f);
        /// <summary>ロビーの地面（Terrain）のワールド座標の長方形 = LOBBY_CENTER + LobbyLayout.GROUND_RECT（柵 + 38m）。</summary>
        public static Vector4 LobbyRect()
        {
            Vector4 g = LobbyLayout.GROUND_RECT;
            return new Vector4(LOBBY_CENTER.x + g.x, LOBBY_CENTER.z + g.y, LOBBY_CENTER.x + g.z, LOBBY_CENTER.z + g.w);
        }
        public const float BACKDROP_OFFSET_M = 320f;       // 遊べる範囲の外側、この距離から山並み（1 列目）
        public const float BACKDROP_SPACING_M = 170f;
        public const float BACKDROP_OFFSET2_M = 520f;      // 2 列目（互い違い）
        public const float BACKDROP_SPACING2_M = 230f;
        public const float BACKDROP_JITTER_M = 60f;
        public const float BACKDROP_SINK = 0.25f;          // 高さに対する埋める割合
        public const float BACKDROP_CLEAR_M = 80f;         // 山の足元と遊べる範囲の間はこれ以上空ける
        public const float BACKDROP_SPACING3_M = 80f;      // 3 列目＝継ぎ目の輪（ステージの縁、art-r2-09 → art-r4-01：220 → 80m）
        public const float BACKDROP_CLEAR3_M = -15f;       // art-r4-01：60 → −15m（足元の内側の端が縁の 15m 内側まで＝縁をまたぐ）
        public const float FILLER_MARGIN_M = 1000f;        // 外の地面は遊べる範囲よりこれだけ広く（2 列目の山並みの外まで）
        public const float FILLER_Y = -3f;                 // ロビーの地面（最低 -2m）より下

        public static Vector4[] PlayableRects()
        {
            var r = new List<Vector4>();
            foreach (var s in StageDefinitions.All)
            {
                Vector3 o = s.Origin;
                r.Add(new Vector4(o.x, o.z, o.x + StageDefinitions.SIZE, o.z + StageDefinitions.SIZE));
            }
            r.Add(LobbyRect());
            return r.ToArray();
        }

        public static Vector4 Union()
        {
            var rs = PlayableRects();
            var u = rs[0];
            foreach (var r in rs) u = new Vector4(Mathf.Min(u.x, r.x), Mathf.Min(u.y, r.y), Mathf.Max(u.z, r.z), Mathf.Max(u.w, r.w));
            return u;
        }

        public static Vector4 FillerRect()
        {
            var u = Union();
            return new Vector4(u.x - FILLER_MARGIN_M, u.y - FILLER_MARGIN_M, u.z + FILLER_MARGIN_M, u.w + FILLER_MARGIN_M);
        }

        /// <summary>
        /// 外の地面（Terrain、FillerRect）の基本の高さ（FILLER_Y からの m）：遊べる範囲のすぐ外は平ら、離れると ±6m のうねり、
        /// 外接長方形から 120〜520m で山並みへ向かって 20〜60m 上る。遊べる範囲の縁との継ぎ目は TerrainBuilder が縁の高さに合わせる。
        /// </summary>
        public static float FillerBaseHeight(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            float dNear = float.MaxValue;
            foreach (var r in PlayableRects()) dNear = Mathf.Min(dNear, DistToRect(p, r));
            float dU = DistToRect(p, Union());
            float wave = 6f * (2f * HeightmapGen.Noise01(x / 180f, z / 180f, 5, 31) - 1f);
            float t = Mathf.Clamp01((dNear - 20f) / 100f);
            float u = Mathf.Clamp01((dU - 120f) / 400f);
            float rise = u * u * (3f - 2f * u) * (20f + 40f * HeightmapGen.Noise01(x / 400f, z / 400f, 5, 32));
            // art-r3-10：低い周波数のこぶ（8〜20m）：外の地面の尾根が水平の帯にならない
            float hump = (8f + 12f * HeightmapGen.Noise01(x / 650f, z / 650f, 5, 33)) * Mathf.Clamp01(2f * HeightmapGen.Noise01(x / 320f, z / 320f, 5, 34) - 0.6f);
            float st = t * t * (3f - 2f * t);
            return wave * st + hump * st + rise;
        }

        // ---- 外の地面の高さ（art-r2-01）：TerrainBuilder.BuildFiller と tools/preview が同じ式を使う ----
        public const int FILLER_RES = 513;
        public const float FILLER_DEPTH_M = 40f;       // FILLER_Y からこれだけ下まで（ステージ・ロビーの下に埋める高さ）
        public const float FILLER_RANGE_M = 360f;
        public const float FILLER_BLEND_M = 250f;      // 遊べる範囲の縁から、この距離で基本の高さへ戻る（art-r4-01：150 → 250m、縁の尾根から外へなだらかに下る）
        public const float FILLER_CORRIDOR_M = 120f;   // art-r4-01：2 つの長方形がどちらもこれ以内の「間の通路」は低い方の縁の高さ + 0〜8m のうねりへ上げる
        public const float FILLER_CORRIDOR_NOISE_M = 8f;
        public const float FILLER_UNDER_M = 1.6f;      // 遊べる範囲の縁の 1 マス以内：縁に沿った一番低い地面からこれだけ下（art-r4-01：3 → 1.6m、縁の段を見せない）
        public const float FILLER_SEAM_M = 0.6f;       // その外：縁の地面からこれだけ下（継ぎ目の段・すき間を見せない）

        /// <summary>点と長方形の符号付きの x/z 距離（外 = +、中 = −（一番近い辺までの距離））。</summary>
        public static float SignedDistToRect(Vector3 p, Vector4 r)
        {
            float d = DistToRect(p, r);
            if (d > 0f) return d;
            return -Mathf.Min(Mathf.Min(p.x - r.x, r.z - p.x), Mathf.Min(p.z - r.y, r.w - p.z));
        }

        /// <summary>
        /// 外の地面（Terrain、セル cellX × cellZ m）の頂点 (wx, wz) のワールドの高さ（art-r2-01）。ground(i, x, z) = 長方形 i（rects）の地面のワールドの高さ。
        ///   ・遊べる範囲の 1 マスより奥：FILLER_Y − FILLER_DEPTH_M（完全に埋める。頂点の間の補間でもステージの地面より上に出ない）。
        ///   ・縁の 1 マス以内（中・外とも）：縁の点 q（長方形の 2m 内側）のまわり ±1 マス 3×3 の一番低い地面 − FILLER_UNDER_M。
        ///     頂点 1 つで縁の高さを取ると、マスの中で地面が 3m 以上下がる所（峡谷の壁・砂丘の崩れ面・切り通し・溝）で外の地面が上に出る。
        ///   ・その外：同じ 3×3 の一番低い地面 − FILLER_SEAM_M から、FILLER_BLEND_M かけて基本の高さ（FillerBaseHeight）へ。
        /// </summary>
        public static float FillerHeight(float wx, float wz, float cellX, float cellZ, Vector4[] rects, System.Func<int, float, float, float> ground)
        {
            float y = FILLER_Y + FillerBaseHeight(wx, wz);
            int best = -1; float bd = float.MaxValue;
            var wp = new Vector3(wx, 0f, wz);
            for (int i = 0; i < rects.Length; i++)
            {
                float d = SignedDistToRect(wp, rects[i]);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0 || bd >= FILLER_BLEND_M || ground == null) return y;
            float cell = Mathf.Max(cellX, cellZ);
            if (bd < -cell) return FILLER_Y - FILLER_DEPTH_M;
            float low = EdgeLow(rects[best], best, wx, wz, cellX, cellZ, ground);
            // art-r4-01：遊べる範囲の内側の頂点は 1.6m 下（ステージの地面の下に隠す）、外側の頂点は縁の高さ − 0.6m（見える継ぎ目に段を作らない）
            float under = bd < 0f ? FILLER_UNDER_M : FILLER_SEAM_M;
            float target = low - under;
            if (bd <= cell) return target;
            // art-r4-01：2 つの長方形の間の通路（どちらも FILLER_CORRIDOR_M 以内）は、低い方の縁の高さ + うねりまで上げる（平らな空の通路にしない）
            for (int j = 0; j < rects.Length; j++)
            {
                if (j == best || bd >= FILLER_CORRIDOR_M) continue;
                float dj = SignedDistToRect(wp, rects[j]);
                if (dj <= 0f || dj >= FILLER_CORRIDOR_M) continue;
                float lowJ = EdgeLow(rects[j], j, wx, wz, cellX, cellZ, ground);
                float corridor = Mathf.Min(low, lowJ) - FILLER_SEAM_M + FILLER_CORRIDOR_NOISE_M * HeightmapGen.Noise01(wx / 140f, wz / 140f, 5, 35);
                y = Mathf.Max(y, corridor);
            }
            float u = Mathf.Clamp01(bd / FILLER_BLEND_M);
            return Mathf.Lerp(target, y, u * u * (3f - 2f * u));
        }

        /// <summary>
        /// 長方形 r（添字 i）の縁の地面の高さ（art-r2-01 → art-r4-01）：(wx, wz) にいちばん近い縁の点（0.5m 内側）と、縁に沿って ±半マス・±1/4 マスの
        /// いちばん低い所。前は内側へも 1 マス見ていたので、外周の尾根の頂（縁）より 10〜18m 低い内側の斜面を拾い、外の地面が縁から崖のように落ちていた。
        /// </summary>
        static float EdgeLow(Vector4 r, int i, float wx, float wz, float cellX, float cellZ, System.Func<int, float, float, float> ground)
        {
            float qx = Mathf.Clamp(wx, r.x + 0.5f, r.z - 0.5f), qz = Mathf.Clamp(wz, r.y + 0.5f, r.w - 0.5f);
            // 縁に沿う向き：x の辺（上下）にいるなら x、左右の辺なら z
            bool alongX = Mathf.Min(qz - r.y, r.w - qz) <= Mathf.Min(qx - r.x, r.z - qx);
            float low = float.MaxValue;
            for (int k = -2; k <= 2; k++)
            {
                float sx = alongX ? Mathf.Clamp(qx + k * cellX * 0.25f, r.x + 0.5f, r.z - 0.5f) : qx;
                float sz = alongX ? qz : Mathf.Clamp(qz + k * cellZ * 0.25f, r.y + 0.5f, r.w - 0.5f);
                low = Mathf.Min(low, ground(i, sx, sz));
            }
            return low;
        }

        /// <summary>外の地面の高さの格子（[z, x]、n × n、ワールドの高さ m）。FillerRect 全体。</summary>
        public static float[,] FillerGrid(int n, Vector4[] rects, System.Func<int, float, float, float> ground)
        {
            Vector4 f = FillerRect();
            float cellX = (f.z - f.x) / (n - 1), cellZ = (f.w - f.y) / (n - 1);
            var h = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    h[z, x] = FillerHeight(f.x + x * cellX, f.y + z * cellZ, cellX, cellZ, rects, ground);
            return h;
        }

        /// <summary>FillerGrid の (wx, wz) での双線形補間（Terrain の描画と同じ）。</summary>
        public static float SampleFiller(float[,] grid, float wx, float wz)
        {
            Vector4 f = FillerRect();
            int n = grid.GetLength(0);
            float u = (wx - f.x) / (f.z - f.x) * (n - 1), v = (wz - f.y) / (f.w - f.y) * (n - 1);
            int x0 = Mathf.Clamp((int)Mathf.Floor(u), 0, n - 2), z0 = Mathf.Clamp((int)Mathf.Floor(v), 0, n - 2);
            float fx = Mathf.Clamp01(u - x0), fz = Mathf.Clamp01(v - z0);
            float a = grid[z0, x0] + (grid[z0, x0 + 1] - grid[z0, x0]) * fx;
            float b = grid[z0 + 1, x0] + (grid[z0 + 1, x0 + 1] - grid[z0 + 1, x0]) * fx;
            return a + (b - a) * fz;
        }

        /// <summary>点と長方形の x/z 距離（中なら 0）。</summary>
        // ---- art-r5-05：ステージの色は外へにじむ（四角い色の板の縁を見せない）----
        /// <summary>ステージの土の色の重みが縁の内 SEAM_IN_M から外 SEAM_OUT_M へ下がる。縁の位置はノイズで ±（SEAM_NOISE_FINE_M/2 + SEAM_NOISE_COARSE_M/2）揺らす。</summary>
        public const float SEAM_IN_M = 160f, SEAM_OUT_M = 350f, SEAM_NOISE_FINE_M = 60f, SEAM_NOISE_COARSE_M = 220f;
        public const float SEAM_NOISE_FINE_SCALE = 90f, SEAM_NOISE_COARSE_SCALE = 300f;

        /// <summary>ステージ si の土の色の重み（0〜1）：ノイズでゆがめた縁からの距離 d' = d + 60·(N(x/90) − 0.5) + 220·(N(x/300) − 0.5) の smoothstep。</summary>
        public static float StageColorWeight(int si, float wx, float wz)
        {
            Vector3 o = StageDefinitions.All[si].Origin;
            var r = new Vector4(o.x, o.z, o.x + StageDefinitions.SIZE, o.z + StageDefinitions.SIZE);
            var p = new Vector3(wx, 0f, wz);
            float d = SignedDistToRect(p, r);
            if (d > SEAM_OUT_M + SEAM_NOISE_COARSE_M) return 0f;
            float dw = d + SEAM_NOISE_FINE_M * (HeightmapGen.Noise01(wx / SEAM_NOISE_FINE_SCALE, wz / SEAM_NOISE_FINE_SCALE, 0, 77) - 0.5f)
                         + SEAM_NOISE_COARSE_M * (HeightmapGen.Noise01(wx / SEAM_NOISE_COARSE_SCALE, wz / SEAM_NOISE_COARSE_SCALE, 0, 78) - 0.5f);
            float t = Mathf.Clamp01((dw + SEAM_IN_M) / (SEAM_IN_M + SEAM_OUT_M));
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// ワールドの点の地面の土の色（art-r5-05）：近いステージの土（ArtPalette.Dirt(i)）を重み StageColorWeight で、残りは外の地面の色 DirtOuter。
        /// ステージの Terrain と外の地面が同じ式で塗る（ToonTerrain の _TintTex）ので、縁の両側の色が続き、色の境目はノイズで曲がる。
        /// </summary>
        public static Color GroundTint(float wx, float wz)
        {
            float sum = 0f, r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < StageDefinitions.All.Length; i++)
            {
                float w = StageColorWeight(i, wx, wz);
                if (w <= 0f) continue;
                Color c = ArtPalette.Dirt(i);
                r += c.r * w; g += c.g * w; b += c.b * w; sum += w;
            }
            Color o = ArtPalette.DirtOuter;
            if (sum < 1f) { float k = 1f - sum; r += o.r * k; g += o.g * k; b += o.b * k; sum = 1f; }
            return new Color(r / sum, g / sum, b / sum, 1f);
        }

        public static float DistToRect(Vector3 p, Vector4 r)
        {
            float dx = Mathf.Max(0f, Mathf.Max(r.x - p.x, p.x - r.z));
            float dz = Mathf.Max(0f, Mathf.Max(r.y - p.z, p.z - r.w));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>コースの置き物の寸法（CourseBuilder と DressingPlan で共有。UnityEditor に依存しない）。</summary>
    public static class CourseGeometry
    {
        public const float GATE_HALF = 13f;
        /// <summary>スタートの門の柵の枚数（course-r3-09：半幅 16m の道いっぱい、FarmProps_03 2.74m × 12 ≈ 32.9m）。</summary>
        public const int START_GATE_PIECES = 12;
        /// <summary>スタートラインの両端（スターターの立ち位置・端の柱）の中心からの横の距離：広いスタート（半幅 16m）の外 3m。</summary>
        public const float START_SIDE = StageDefinitions.START_HALF_M + 3f;
        public const float PEN_RADIUS = 12f;
        public const float PEN_BACK = 14f;
        public const float BOARD_SIDE = 15f;     // CP の黒板はコース中心から右へ 15m（平らな道 ±12m の外）
        public const int BOARD_EVERY = 3;        // 黒板は CP3・CP6・…とゴール
        /// <summary>CP の箱（道幅 12m のとき）。CourseBuilder と CourseBarrier の両方がこれを使う（course-r1-14：1 か所で決める）。</summary>
        public const float CP_WIDTH = 36f, CP_HEIGHT = 10f, CP_DEPTH = 6f;   // 18 m/s・10fps でも 1 フレーム 1.8m（体 2.6m＋奥行 6m）で必ず重なる
    }

    /// <summary>
    /// コースの合図の位置（course-r1-04/10/15/16）。CourseBuilder（実体）と DressingPlan（置かない円）が同じ式で使う。
    ///   黒板：BOARD_EVERY 個ごとの CP・ゴール・「最後の下りの始まり（ゴールの 590m 手前）」に一番近い CP（全力の前に順位を読む）。
    ///   絞りの手前の黒板：道幅が狭くなる区間の CHOKE_WARN_M 手前、道の右。
    ///   全力の合図：最後の下りの始まりの両脇 ±(半幅 + 2m) に街灯（暖色の灯り）、右に「ここから全力」の札。
    /// </summary>
    public static class CourseMarks
    {
        public const float CHOKE_WARN_M = 120f;
        public const float DASH_LAMP_OUT_M = 2f, DASH_SIGN_OUT_M = 4.5f;

        public static bool HasBoard(StageDef def, float[] cps, int k, float goalArc)
        {
            if (k == cps.Length - 1 || (k + 1) % CourseGeometry.BOARD_EVERY == 0) return true;
            return k == DashBoardCp(def, cps, goalArc);
        }

        /// <summary>最後の下りの始まり（StageDefinitions.DashArc）より手前で一番近い CP の添字。</summary>
        public static int DashBoardCp(StageDef def, float[] cps, float goalArc)
        {
            float dash = StageDefinitions.DashArc(def, goalArc);
            int best = -1;
            for (int k = 0; k < cps.Length - 1; k++) if (cps[k] <= dash) best = k;
            return best;
        }

        /// <summary>黒板の横の距離（道の右、平らな道の縁の 3m 外、最低 BOARD_SIDE）。</summary>
        public static float BoardSide(StageDef def, float arc) => Mathf.Max(CourseGeometry.BOARD_SIDE, StageDefinitions.HalfWidthAt(def, arc) + 3f);

        /// <summary>絞りの区間の手前 CHOKE_WARN_M の弧長。</summary>
        public static List<float> ChokeWarnArcs(StageDef def)
        {
            var r = new List<float>();
            if (def == null || def.widthArc == null || def.halfWidthM == null) return r;
            for (int k = 0; k < def.halfWidthM.Length && 2 * k + 1 < def.widthArc.Length; k++)
                if (def.halfWidthM[k] <= StageDefinitions.CHOKE_HALF_M && def.widthArc[2 * k] - CHOKE_WARN_M > 40f) r.Add(def.widthArc[2 * k] - CHOKE_WARN_M);
            return r;
        }

        // ---- ゴールゲート（art-r2-07）：CourseBuilder.GoalGate と tools/preview が同じ数字で置く ----
        public const float GATE_POST_H = 7f, GATE_POST_THICK = 1.4f;
        public const float GATE_BEAM_Y = 6.5f, GATE_BEAM_GAP = 0.9f, GATE_BEAM_THICK = 0.6f;   // art-r3-01：太さ 0.6m（丸太の束を細く。0.8 でも下見で板に見えた）
        public const float GATE_LAMP_SCALE = 1.25f;
        // WoodLogs_01 の外形（Blender で測った値、Unity のローカル：X 長さ 1.45・Y 高さ 0.32・Z 奥行き 0.99。丸太を並べた束）
        public const float WOODLOG_01_LEN = 1.45f, WOODLOG_01_H = 0.32f, WOODLOG_01_W = 0.99f;
        /// <summary>WoodLogs_01 の直径（束の高さ）：art-r3-01 の指示の WOODLOG_01_DIAM。</summary>
        public const float WOODLOG_01_DIAM = WOODLOG_01_H;
        /// <summary>
        /// 横木の丸太の拡大（course-r3-01）：引き伸ばさず、元の丸太の束 WoodLogs_01 を同じ倍率 ×0.9 で何本もつなぐ（太さ ≈ 0.29m × 0.89m、柱の 1.4m より細い）。
        /// 前は 6 本を長さだけ（または 3 本を一様に ×6.1）伸ばし、ゴールの上 1/3 をふさぐ板に見えた。区間の門は ×0.8。
        /// </summary>
        public const float GATE_BEAM_SCALE = 0.9f, SECTOR_BEAM_SCALE = 0.8f;
        /// <summary>隣の丸太と重ねる割合（継ぎ目を隠す、course-r3-01）：1 本の間隔 = 長さ × 0.92。</summary>
        public const float BEAM_OVERLAP = 0.92f;
        public const int GATE_BEAM_TIERS = 2;
        /// <summary>ゴールの横木 1 段の本数（柱の外の端から外の端まで、course-r3-01）。</summary>
        public static int GATE_BEAM_PIECES => BeamCount(CourseGeometry.GATE_HALF * 2f + GATE_POST_THICK, GATE_BEAM_SCALE);

        /// <summary>長さ span を、拡大 scale の WoodLogs_01 を 8% 重ねてつなぐ本数。</summary>
        public static int BeamCount(float span, float scale) => Mathf.Max(1, Mathf.CeilToInt(span / (WOODLOG_01_LEN * scale * BEAM_OVERLAP)));

        /// <summary>
        /// 横木 1 段（course-r3-01）：l から r まで（足元の点、y は無視）、高さ y（束の中心）に、WoodLogs_01 を一様な拡大 scale のまま BeamCount 本、
        /// 端から端まで等間隔に並べる（両端の丸太の外の端が l・r にそろう）。向きは 1 本おきに 180° 回す（同じ束の繰り返しに見せない）。
        /// CourseBuilder（ゴール・区間の門）と tools/preview が同じ部品を置く。
        /// </summary>
        public static List<GatePart> BeamPieces(Vector3 l, Vector3 r, float y, float scale)
        {
            var list = new List<GatePart>();
            Vector3 d = new Vector3(r.x - l.x, 0f, r.z - l.z);
            float span = d.magnitude;
            if (span < 0.01f) return list;
            d /= span;
            float yaw = WorldLayout.YawDeg(new Vector3(-d.z, 0f, d.x));   // 進む向き（ローカル X = 右 = d）
            float len = WOODLOG_01_LEN * scale;
            int n = BeamCount(span, scale);
            float step = n > 1 ? (span - len) / (n - 1) : 0f;
            for (int k = 0; k < n; k++)
            {
                float x = n > 1 ? len * 0.5f + step * k : span * 0.5f;
                Vector3 c = new Vector3(l.x + d.x * x, y - WOODLOG_01_H * scale * 0.5f, l.z + d.z * x);   // 束の Y の原点は底
                list.Add(new GatePart { asset = "Packs/WoodLogs/WoodLogs.fbx", part = "WoodLogs_01", position = c, euler = new Vector3(0f, yaw + (k % 2) * 180f, 0f),
                    scale = Vector3.one * scale });
            }
            return list;
        }
        /// <summary>横木の下限の高さ（テスト GoalGateBeamIsThin）。</summary>
        public const float GATE_BEAM_MIN_Y = 5.5f;
        /// <summary>幕（手配書の紙）の中心の高さ：下の横木から吊るす。</summary>
        public const float GATE_BANNER_Y = GATE_BEAM_Y - GATE_BEAM_GAP - 0.85f;

        /// <summary>
        /// WoodLogs_01 を長さ lengthM・断面 thickM × thickM の丸太にする拡大（ローカル X・Y・Z、art-r3-01/02）。
        /// 立てるときは roll 90°（ローカル X が上）。前の横木は同じ倍率（×6.1）で太さも 6 倍になり、平らな板に見えた。
        /// </summary>
        public static Vector3 PostScale(float lengthM, float thickM)
            => new Vector3(lengthM / WOODLOG_01_LEN, thickM / WOODLOG_01_H, thickM / WOODLOG_01_W);

        /// <summary>ゴールゲートの模型 1 つ：素材（Models からの相対）・部品・位置・回転（オイラー °）・拡大。light = 街灯の灯りを付ける。</summary>
        public struct GatePart
        {
            public string asset, part;
            public Vector3 position, euler, scale;
            public bool light;
        }

        /// <summary>
        /// ゴールゲートの模型（art-r2-07 / art-r3-01）：柱 = 立てた丸太 WoodLogs_01（高さ 7m・太さ 1.4m）、柱の外に樽の山（FarmProps_15/22、
        /// 骨の杭 DesertProps_03 は使わない：art-r3-02）、横木 = 丸太 WoodLogs_01 を 6 本つないで 2 段（GATE_BEAM_Y と 0.9m 下、太さ 0.6m の細い枠）、
        /// 柱の外に街灯 ×1.25、足元に干し草と樽。c = ゴールの中心の地面、t = 進む向き、ground(p) = p の真下の地面の点。
        /// </summary>
        public static List<GatePart> GoalGateParts(Vector3 c, Vector3 t, System.Func<Vector3, Vector3> ground)
        {
            t = new Vector3(t.x, 0f, t.z).normalized;
            Vector3 r = WorldLayout.RightOf(t);
            float yaw = WorldLayout.YawDeg(t);
            var l = new List<GatePart>();
            const string LOGS = "Packs/WoodLogs/WoodLogs.fbx", FARM = "Packs/FarmProps/FarmProps.fbx", HAY = "Packs/HayBales/HayBales.fbx";
            float G = CourseGeometry.GATE_HALF;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = ground(c + r * (s * G));
                l.Add(new GatePart { asset = LOGS, part = "WoodLogs_01", position = post + Vector3.up * (GATE_POST_H * 0.5f - 0.3f), euler = new Vector3(0f, yaw, 90f),
                    scale = PostScale(GATE_POST_H, GATE_POST_THICK) });
                // 柱の外の樽の山（大樽 2 段＋小樽）：柱の足元を太く見せる（前は骨の杭 ×1.9）
                Vector3 bp = ground(c + r * (s * (G + 1.05f)));
                l.Add(new GatePart { asset = FARM, part = "FarmProps_15", position = bp, euler = new Vector3(0f, yaw + 15f * s, 0f), scale = Vector3.one * 1.3f });
                l.Add(new GatePart { asset = FARM, part = "FarmProps_15", position = bp + Vector3.up * 0.95f, euler = new Vector3(0f, yaw - 25f * s, 0f), scale = Vector3.one * 1.15f });
                l.Add(new GatePart { asset = FARM, part = "FarmProps_22", position = ground(c + r * (s * (G + 1.1f)) + t * 0.95f), euler = new Vector3(0f, yaw + 60f, 0f), scale = Vector3.one * 1.3f });
                l.Add(new GatePart { asset = FARM, part = "FarmProps_02", position = ground(c + r * (s * (G + 1.4f + 0.6f)) - t * 0.6f), euler = new Vector3(0f, yaw + (s > 0 ? 90f : -90f), 0f),
                    scale = Vector3.one * GATE_LAMP_SCALE, light = true });
                Vector3 foot = c + r * (s * (G + 1.2f)) + t * 1.6f;
                l.Add(new GatePart { asset = HAY, part = "HayBales_01", position = ground(foot + t * 0.6f), euler = new Vector3(0f, yaw + s * 20f, 0f), scale = Vector3.one });
                l.Add(new GatePart { asset = HAY, part = "HayBales_01", position = ground(foot + r * (s * 1.6f)), euler = new Vector3(0f, yaw - s * 30f, 0f), scale = Vector3.one * 0.9f });
                l.Add(new GatePart { asset = FARM, part = "FarmProps_15", position = ground(foot - t * 3f), euler = Vector3.zero, scale = Vector3.one * 1.2f });
                l.Add(new GatePart { asset = FARM, part = "FarmProps_22", position = ground(foot - t * 3.9f + r * (s * 0.8f)), euler = new Vector3(0f, 40f, 0f), scale = Vector3.one * 1.2f });
            }
            // 横木（course-r3-01）：丸太の束 WoodLogs_01 を一様に ×GATE_BEAM_SCALE のまま約 23 本つなぎ（BeamPieces）、GATE_BEAM_Y と GATE_BEAM_GAP 下の 2 段。
            // 空が透けて見える細い枠（前は引き伸ばした丸太で、ゴールの上 1/3 をふさぐ板と黒い影の帯になった）
            float span = G * 2f + GATE_POST_THICK;
            for (int tier = 0; tier < GATE_BEAM_TIERS; tier++)
                l.AddRange(BeamPieces(c - r * (span * 0.5f), c + r * (span * 0.5f), c.y + GATE_BEAM_Y - tier * GATE_BEAM_GAP, GATE_BEAM_SCALE));
            return l;
        }

        /// <summary>全力の合図の札の 2 行目（course-r2-07：ステージごとの締めくくり）。</summary>
        public static string FinishLine(StageDef def)
        {
            if (def.finishKind == StageDefinitions.FINISH_CREST) return "下りの先の丘を越えるとゴール";
            if (def.finishKind == StageDefinitions.FINISH_TOWN) return "台地を下りて町の通り・最後に右";
            return "峡谷を 2 段下りて平地をゴールへ";
        }

        // ---- course-r2-08 / course-r3-07：急カーブの手前の矢印の札・外側の干し草の壁 ----
        public static readonly float[] CORNER_BOARD_BEFORE = { 110f, 70f, 35f };
        public const float CORNER_BOARD_OUT_M = 2.5f;     // 110m 手前の札：外側の道の縁から（柵の内側）
        public const float CORNER_BOARD_FENCE_IN_M = 1.4f; // 70・35m 手前の札：外側の柵の線の手前（course-r3-07：柵の線の上で騎手のほうを向く）
        public const float CORNER_BOARD_TURN_DEG = 25f;   // 札を道の側へ振る角度
        public const float CORNER_BOARD_BIG = 1.8f;       // 70・35m 手前の札の紙の大きさ（倍）
        public const float CORNER_HAY_IN_M = 1.5f, CORNER_LAMP_OUT_M = 2.5f;
        // 外側の干し草の壁（course-r3-07）：頂点の 30m 手前〜25m 先、外側の柵の手前に 2 段の干し草（HayBales_01/02 ×1.3、2.2m ごと）
        public const float CORNER_WALL_BEFORE_M = 30f, CORNER_WALL_AFTER_M = 25f, CORNER_WALL_STEP_M = 2.2f, CORNER_WALL_SCALE = 1.3f;
        public const float CORNER_WALL_EDGE_M = 1.7f;     // 道の縁から干し草の中心まで（柵は道の縁 + 4m）

        /// <summary>急カーブの札 1 枚：位置（Terrain ローカル、y = 0）、向き（読む人が見る向き）、矢印の数（3 = 110m 手前、2、1 = 35m 手前）、曲がる向き（+1 = 右）、紙の大きさ（倍）。</summary>
        public struct CornerBoard
        {
            public Vector3 position;
            public float yaw, scale;
            public int chevrons, side;
        }

        /// <summary>
        /// 急カーブごと：頂点の 110・70・35m 手前の外側に札（›››・››・›）。110m は道の縁の外 2.5m・道に沿う向き。70・35m は外側の柵の線の上
        /// （CourseBarrier.OffsetAtArc − CORNER_BOARD_FENCE_IN_M）に 1.8 倍の紙で、道の側へ 25° 振って迫ってくる騎手のほうを向く（course-r3-07）。
        /// </summary>
        public static List<CornerBoard> CornerBoards(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<CornerBoard>();
            foreach (var c in StageDefinitions.TightCorners(def, course))
                for (int k = 0; k < CORNER_BOARD_BEFORE.Length; k++)
                {
                    float a = c.apexArc - CORNER_BOARD_BEFORE[k];
                    if (a < StageDefinitions.START_ARC_M + 20f) continue;
                    Vector3 t = WorldLayout.TangentAtArc(course, a);
                    float hw = StageDefinitions.HalfWidthAt(def, a);
                    bool big = k > 0;
                    float lat = big ? Mathf.Max(hw + CORNER_BOARD_OUT_M, CourseBarrier.OffsetAtArc(def, course, goalArc, a) - CORNER_BOARD_FENCE_IN_M) : hw + CORNER_BOARD_OUT_M;
                    Vector3 p = WorldLayout.PointAtArc(course, a) + WorldLayout.RightOf(t) * (-c.side * lat);
                    // 外側（右へ曲がるなら左）に立つ札の面を道の側へ：左に立つなら左へ回す（面の向き = −前）
                    float yaw = WorldLayout.YawDeg(t) + (big ? -c.side * CORNER_BOARD_TURN_DEG : 0f);
                    r.Add(new CornerBoard { position = new Vector3(p.x, 0f, p.z), yaw = yaw, chevrons = CORNER_BOARD_BEFORE.Length - k, side = c.side, scale = big ? CORNER_BOARD_BIG : 1f });
                }
            return r;
        }

        /// <summary>矢印の文字（右へ曲がるなら ›、左なら ‹）。</summary>
        public static string Chevrons(int n, int side)
        {
            string one = side > 0 ? "›" : "‹";
            string s = "";
            for (int i = 0; i < n; i++) s += one;
            return s;
        }

        /// <summary>頂点の内側の干し草の山と外側の街灯（Terrain ローカル）。[0] = 干し草、[1] = 街灯（course-r3-07：干し草の壁の後ろ、柵の外 1m）。</summary>
        public static Vector3[] CornerApexMarks(StageDef def, Vector3[] course, StageDefinitions.Corner c, float goalArc)
        {
            Vector3 t = WorldLayout.TangentAtArc(course, c.apexArc), rt = WorldLayout.RightOf(t), p = WorldLayout.PointAtArc(course, c.apexArc);
            float hw = StageDefinitions.HalfWidthAt(def, c.apexArc);
            float lampLat = Mathf.Max(hw + CORNER_LAMP_OUT_M, CourseBarrier.OffsetAtArc(def, course, goalArc, c.apexArc) + 1f);
            Vector3 hay = p + rt * (c.side * (hw + CORNER_HAY_IN_M)), lamp = p + rt * (-c.side * lampLat);
            return new[] { new Vector3(hay.x, 0f, hay.z), new Vector3(lamp.x, 0f, lamp.z) };
        }

        /// <summary>
        /// 外側の干し草の壁の 1 つ（art-r4-11）：四角い干し草（HayBales_02/03、長い辺 0.92m）を長い辺を道に沿わせて拡大 scale、足元は Terrain ローカル y = 0。
        /// cap = 上に丸い干し草（HayBales_01 ×CORNER_WALL_CAP_SCALE）を載せる（3 つに 1 つ）。
        /// </summary>
        public struct WallBale
        {
            public Vector3 position;
            public float yaw, scale;
            public string part;
            public bool cap;
        }

        // art-r4-11：四角い干し草を端と端でつなぐ低い壁（丸い干し草の格子は「蜂の巣の畑」に見えた）
        public const float HAY_BLOCK_LEN = 0.92f, HAY_BLOCK_H = 0.55f;       // HayBales_02/03 の長い辺・高さ（Blender で測った外形）
        public const float CORNER_WALL_BLOCK_SCALE = CORNER_WALL_STEP_M / HAY_BLOCK_LEN;   // 2.2m の干し草（高さ 1.3m）
        public const float CORNER_WALL_YAW_JITTER = 6f, CORNER_WALL_SCALE_JITTER = 0.05f, CORNER_WALL_CAP_SCALE = 0.8f;
        public const float CORNER_WALL_FROM_M = 15f, CORNER_WALL_TO_M = 25f;   // 頂点の 15m 手前〜25m 先
        public const float CORNER_CHEVRON_STEP_M = 8f;

        /// <summary>
        /// 急カーブの外側の干し草の壁（course-r3-07 → art-r4-11）：頂点の CORNER_WALL_FROM_M 手前から CORNER_WALL_TO_M 先まで、道の縁 + CORNER_WALL_EDGE_M に
        /// 2.2m ごとの四角い干し草（HayBales_02/03 ×2.4 ±5%、長い辺を外側の線に沿わせ ±6° だけ振る）を端と端でつなぎ、3 つに 1 つ丸い干し草を載せる。
        /// 当たりは柵（見えない壁）だけ。「ここへ行くな」の 1 本の続いた壁。
        /// </summary>
        public static List<WallBale> CornerWall(StageDef def, Vector3[] course, StageDefinitions.Corner c)
        {
            var r = new List<WallBale>();
            int k = 0;
            Vector3 last = Vector3.zero;
            for (float a = c.apexArc - CORNER_WALL_FROM_M; a <= c.apexArc + CORNER_WALL_TO_M + 1e-3f; a += 0.25f)
            {
                Vector3 t = WorldLayout.TangentAtArc(course, a);
                Vector3 p = WorldLayout.PointAtArc(course, a) + WorldLayout.RightOf(t) * (-c.side * (StageDefinitions.HalfWidthAt(def, a) + CORNER_WALL_EDGE_M));
                p.y = 0f;
                if (k > 0 && WorldLayout.DistXZ(p, last) < CORNER_WALL_STEP_M) continue;   // 外側の線の上で 2.2m ごと（カーブの外は弧長より長い）
                // 決まった揺らぎ（乱数を使わない）：向き ±6°、拡大 ±5%
                float jy = CORNER_WALL_YAW_JITTER * Mathf.Sin(k * 2.39f), js = 1f + CORNER_WALL_SCALE_JITTER * Mathf.Sin(k * 1.71f + 0.5f);
                r.Add(new WallBale { position = p, yaw = WorldLayout.YawDeg(t) + 90f + jy, scale = CORNER_WALL_BLOCK_SCALE * js,
                    part = k % 2 == 0 ? "HayBales_02" : "HayBales_03", cap = k % 3 == 1 });
                last = p;
                k++;
            }
            return r;
        }

        /// <summary>壁の上の赤い矢印の札 1 枚（art-r4-11）：足元 = 壁の上（Terrain ローカル、y = 壁の高さ）、向き（迫ってくる騎手が見る向き）。</summary>
        public struct WallChevron
        {
            public Vector3 position;
            public float yaw;
            public int side;
        }

        /// <summary>
        /// 干し草の壁の上の赤い矢印の札（art-r4-11）：CORNER_CHEVRON_STEP_M ごとに、壁の干し草の上（高さ = 干し草の高さ）に短い丸太の柱と手配書の紙の札
        /// （急カーブの札と同じ赤インクの ›）。道の側へ 25° 振って騎手を向く。
        /// </summary>
        public static List<WallChevron> CornerWallChevrons(StageDef def, Vector3[] course, StageDefinitions.Corner c)
        {
            var r = new List<WallChevron>();
            var wall = CornerWall(def, course, c);
            float run = 0f;
            for (int i = 0; i < wall.Count; i++)
            {
                if (i > 0) run += WorldLayout.DistXZ(wall[i].position, wall[i - 1].position);
                if (i > 0 && run < CORNER_CHEVRON_STEP_M - 0.01f) continue;
                if (wall[i].cap) continue;   // 丸い干し草の上には立てない
                run = 0f;
                Vector3 p = wall[i].position;
                p.y = HAY_BLOCK_H * wall[i].scale;
                r.Add(new WallChevron { position = p, yaw = wall[i].yaw - 90f - c.side * CORNER_BOARD_TURN_DEG, side = c.side });
            }
            return r;
        }

        // ---- course-r3-08：ゴール前の 200m の見物の直線（ランニングレール・見物人の干し草・街灯・赤い旗） ----
        public const float CHUTE_FROM_M = 200f, CHUTE_GATE_GAP_M = 3f;   // ゴールの 200m 手前から、ゴールゲートの 3m 手前まで
        public const float CHUTE_RAIL_OUT_M = 1f, CHUTE_POST_OUT_M = 1.6f, CHUTE_LAMP_OUT_M = 2.3f, CHUTE_HAY_OUT_M = 3.5f;   // 道の縁から
        public const float CHUTE_HAY_STEP_M = 12f, CHUTE_LAMP_STEP_M = 25f, CHUTE_FLAG_STEP_M = 15f, CHUTE_POST_H = 4f, CHUTE_POST_THICK = 0.28f;
        public const float CHUTE_SIGN_BEFORE_M = 100f;
        public const int CHUTE_RAIL = 0, CHUTE_HAY = 1, CHUTE_LAMP = 2, CHUTE_FLAG = 3, CHUTE_SIGN = 4;

        /// <summary>見物の直線の部品 1 つ：種類（CHUTE_*）・素材・部品・足元（Terrain ローカル y = 0）・向き・拡大（ローカル）・弧長・どちら側か。</summary>
        public struct ChutePart
        {
            public int kind, side;
            public string asset, part;
            public Vector3 position, scale;
            public float yaw, arc;
        }

        /// <summary>
        /// ゴール前の見物の直線（course-r3-08、3 ステージとも）：ゴールの CHUTE_FROM_M 手前からゴールゲートの手前まで、
        ///   両脇の道の縁 + 1m に途切れない柵 FarmProps_03（ランニングレール、端と端でつなぐ）、
        ///   12m ごとに見物人の干し草の山（HayBales_01 + _03、道の縁 + 3.5m。境目は町の板張りの縁が見物人なので置かない）、
        ///   25m ごとに街灯（暖色の灯り 10m）、15m ごとに立てた丸太 4m に赤い手配書の紙（ゴールの幕と同じ赤）の旗、ゴールの 100m 手前に「あと 100m」の札（右）。
        /// 急カーブの外側の干し草の壁・札・CP の黒板・全力の合図と重なる所はそちらを優先して空ける。CourseBuilder と tools/preview が同じ部品を置く。
        /// </summary>
        public static List<ChutePart> FinishChute(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<ChutePart>();
            const string FARM = "Packs/FarmProps/FarmProps.fbx", HAY = "Packs/HayBales/HayBales.fbx", LOGS = "Packs/WoodLogs/WoodLogs.fbx";
            float a0 = goalArc - CHUTE_FROM_M, a1 = goalArc - CHUTE_GATE_GAP_M;
            // 先に置かれている合図（重ねない）
            var busy = new List<Vector4>();
            var corners = StageDefinitions.TightCorners(def, course);
            foreach (var cb in CornerBoards(def, course, goalArc)) busy.Add(new Vector4(cb.position.x, 0f, cb.position.z, 2.5f));
            var walls = new List<Vector3>();
            foreach (var c in corners)
            {
                foreach (var m in CornerApexMarks(def, course, c, goalArc)) busy.Add(new Vector4(m.x, 0f, m.z, 2.5f));
                foreach (var wb in CornerWall(def, course, c)) walls.Add(wb.position);
            }
            foreach (var m in MarkSpots(def, course, goalArc)) busy.Add(new Vector4(m.x, 0f, m.z, 3f));
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            for (int k = 0; k < cps.Length; k++)
            {
                if (!HasBoard(def, cps, k, goalArc)) continue;
                Vector3 bt = WorldLayout.TangentAtArc(course, cps[k]);
                Vector3 bp = WorldLayout.PointAtArc(course, cps[k]) + WorldLayout.RightOf(bt) * BoardSide(def, cps[k]);
                busy.Add(new Vector4(bp.x, 0f, bp.z, 1.5f));
            }
            System.Func<Vector3, float, bool> free = (p, rad) =>
            {
                foreach (var b in busy) if (WorldLayout.DistXZ(p, new Vector3(b.x, 0f, b.z)) < b.w + rad) return false;
                foreach (var q in walls) if (WorldLayout.DistXZ(p, q) < 2.6f + rad) return false;
                return true;
            };
            System.Func<float, float, Vector3> side = (a, lat) =>
            {
                Vector3 t = WorldLayout.TangentAtArc(course, a), c = WorldLayout.PointAtArc(course, a), rt = WorldLayout.RightOf(t);
                return new Vector3(c.x + rt.x * lat, 0f, c.z + rt.z * lat);
            };
            // 1) ランニングレール（両脇、端と端でつなぐ）
            for (int sd = -1; sd <= 1; sd += 2)
            {
                bool have = false;
                Vector3 joint = Vector3.zero, prev = Vector3.zero;
                for (float a = a0; a <= a1 + 1e-3f; a += 0.5f)
                {
                    Vector3 q = side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + CHUTE_RAIL_OUT_M));
                    if (!free(q, 0.3f)) { have = false; continue; }   // 急カーブの干し草の壁がレールの代わり
                    if (!have) { joint = q; prev = q; have = true; continue; }
                    float dq = WorldLayout.DistXZ(joint, q);
                    if (dq >= DressingPlan.RAIL_LEN)
                    {
                        float dp = WorldLayout.DistXZ(joint, prev);
                        float f = Mathf.Clamp01((DressingPlan.RAIL_LEN - dp) / Mathf.Max(1e-4f, dq - dp));
                        Vector3 j = Vector3.Lerp(prev, q, f);
                        j.y = 0f;
                        Vector3 d = new Vector3(j.x - joint.x, 0f, j.z - joint.z).normalized;
                        r.Add(new ChutePart { kind = CHUTE_RAIL, side = sd, asset = FARM, part = "FarmProps_03", position = (joint + j) * 0.5f,
                            yaw = WorldLayout.YawDeg(d) + 90f, scale = Vector3.one, arc = a });
                        joint = j;
                    }
                    prev = q;
                }
            }
            // 2) 見物人の干し草（境目は町の板張りの縁が見物人）
            if (def.kind != HeightmapGen.KIND_MESA)
            {
                int k = 0;
                for (float a = a0 + 6f; a <= goalArc - 15f; a += CHUTE_HAY_STEP_M, k++)
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        Vector3 q = side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + CHUTE_HAY_OUT_M));
                        if (!free(q, 1.2f)) continue;
                        float yaw = WorldLayout.YawDeg(WorldLayout.TangentAtArc(course, a)) + (k % 3) * 25f;
                        r.Add(new ChutePart { kind = CHUTE_HAY, side = sd, asset = HAY, part = "HayBales_01", position = q, yaw = yaw, scale = Vector3.one, arc = a });
                        Vector3 q2 = side(a + 1.6f, sd * (StageDefinitions.HalfWidthAt(def, a) + CHUTE_HAY_OUT_M + 0.4f));
                        r.Add(new ChutePart { kind = CHUTE_HAY, side = sd, asset = HAY, part = "HayBales_03", position = q2, yaw = yaw + 40f, scale = Vector3.one * 0.9f, arc = a + 1.6f });
                    }
            }
            // 3) 街灯（25m ごと、両脇。「あと 100m」の札と重ならないよう 12.5m ずらす）
            for (float a = a0 + 12.5f; a <= a1; a += CHUTE_LAMP_STEP_M)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 q = side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + CHUTE_LAMP_OUT_M));
                    if (!free(q, 0.6f)) continue;
                    float yaw = WorldLayout.YawDeg(WorldLayout.TangentAtArc(course, a)) + (sd > 0 ? -90f : 90f);
                    r.Add(new ChutePart { kind = CHUTE_LAMP, side = sd, asset = FARM, part = "FarmProps_02", position = q, yaw = yaw, scale = Vector3.one, arc = a });
                }
            // 4) 赤い旗（15m ごと、両脇。丸太 4m と赤い紙）
            for (float a = a0 + 5f; a <= a1; a += CHUTE_FLAG_STEP_M)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 q = side(a, sd * (StageDefinitions.HalfWidthAt(def, a) + CHUTE_POST_OUT_M));
                    if (!free(q, 0.6f)) continue;
                    r.Add(new ChutePart { kind = CHUTE_FLAG, side = sd, asset = LOGS, part = "WoodLogs_01", position = q, yaw = WorldLayout.YawDeg(WorldLayout.TangentAtArc(course, a)),
                        scale = PostScale(CHUTE_POST_H, CHUTE_POST_THICK), arc = a });
                }
            // 5) 「あと 100m」の札（右、レールと干し草の間）
            {
                float a = goalArc - CHUTE_SIGN_BEFORE_M;
                Vector3 q = side(a, StageDefinitions.HalfWidthAt(def, a) + CHUTE_LAMP_OUT_M + 0.2f);
                r.Add(new ChutePart { kind = CHUTE_SIGN, side = 1, asset = LOGS, part = "WoodLogs_01", position = q, yaw = WorldLayout.YawDeg(WorldLayout.TangentAtArc(course, a)),
                    scale = Vector3.one, arc = a });
            }
            return r;
        }

        /// <summary>見物の直線のレールの最大のすき間（m、course-r3-08 のテスト用）：両脇それぞれ、弧長の順につないだ板の端と端の距離の最大（急カーブの干し草の壁の所は除く）。</summary>
        public static float ChuteRailMaxGap(List<ChutePart> parts, int side)
        {
            var rails = new List<ChutePart>();
            foreach (var p in parts) if (p.kind == CHUTE_RAIL && p.side == side) rails.Add(p);
            float worst = 0f;
            for (int i = 1; i < rails.Count; i++)
            {
                float a0 = (rails[i - 1].yaw - 90f) * Mathf.Deg2Rad, a1 = (rails[i].yaw - 90f) * Mathf.Deg2Rad;
                Vector3 e0 = rails[i - 1].position + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * (DressingPlan.RAIL_LEN * 0.5f);
                Vector3 s1 = rails[i].position - new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * (DressingPlan.RAIL_LEN * 0.5f);
                float g = WorldLayout.DistXZ(e0, s1);
                if (g < 6f) worst = Mathf.Max(worst, g);   // それより長い切れ目は干し草の壁・札（意図した空き）
            }
            return worst;
        }

        // ---- course-r2-15：区間の門（残りの距離） ----
        public static readonly float[] SECTOR_FRACTIONS = { 0.25f, 0.5f, 0.75f };
        public const float SECTOR_POST_OUT_M = 1.5f;

        /// <summary>区間の門の弧長（ゴールまでの 25・50・75%）。CP の箱と重ならないよう CP から 10m 以上ずらす。</summary>
        public static List<float> SectorGateArcs(float goalArc)
        {
            var r = new List<float>();
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            foreach (float f in SECTOR_FRACTIONS)
            {
                float a = f * goalArc;
                foreach (float c in cps) if (Mathf.Abs(c - a) < 10f) a = c + (a >= c ? 10f : -10f);
                r.Add(a);
            }
            return r;
        }

        /// <summary>「残り 1.8 km」（ゴールまでの残りを 0.1 km に丸める）。</summary>
        public static string RemainingText(float goalArc, float arc)
        {
            float km = Mathf.Max(0f, goalArc - arc) / 1000f;
            return "残り " + km.ToString("0.0") + " km";
        }

        /// <summary>区間の門の柱の高さ（m）と、横木（柱の上の端の 0.4m 下）。</summary>
        public const float SECTOR_POST_H = 6f;
        /// <summary>art-r5-15：区間の門の柱の太さの倍率（ゴールの門の太い柱にそろえる）。</summary>
        public const float SECTOR_POST_THICK_K = 1.4f;

        /// <summary>区間の門の横木（course-r3-01）：l・r = 柱の外の端（足元）、top = 横木の中心の高さ。丸太の束 ×SECTOR_BEAM_SCALE をつなぐ。</summary>
        public static List<GatePart> SectorBeamPieces(Vector3 l, Vector3 r, float top) => BeamPieces(l, r, top, SECTOR_BEAM_SCALE);

        /// <summary>区間の門の柱の足元（Terrain ローカル、[2k] = 左、[2k+1] = 右）。</summary>
        public static List<Vector3> SectorGatePosts(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector3>();
            foreach (float a in SectorGateArcs(goalArc))
            {
                Vector3 t = WorldLayout.TangentAtArc(course, a), rt = WorldLayout.RightOf(t), p = WorldLayout.PointAtArc(course, a);
                float hw = StageDefinitions.HalfWidthAt(def, a) + SECTOR_POST_OUT_M;
                Vector3 l = p - rt * hw, q = p + rt * hw;
                r.Add(new Vector3(l.x, 0f, l.z)); r.Add(new Vector3(q.x, 0f, q.z));
            }
            return r;
        }

        /// <summary>合図の飾りの位置（Terrain ローカル、y = 0）：全力の合図の街灯 2 本と札、絞りの手前の黒板。</summary>
        public static List<Vector3> MarkSpots(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector3>();
            float dash = StageDefinitions.DashArc(def, goalArc);
            Vector3 t = WorldLayout.TangentAtArc(course, dash), rt = WorldLayout.RightOf(t), c = WorldLayout.PointAtArc(course, dash);
            float hw = StageDefinitions.HalfWidthAt(def, dash);
            r.Add(c + rt * (hw + DASH_LAMP_OUT_M)); r.Add(c - rt * (hw + DASH_LAMP_OUT_M)); r.Add(c + rt * (hw + DASH_SIGN_OUT_M) + t * 3f);
            foreach (float a in ChokeWarnArcs(def))
                r.Add(WorldLayout.PointAtArc(course, a) + WorldLayout.RightOf(WorldLayout.TangentAtArc(course, a)) * BoardSide(def, a));
            for (int i = 0; i < r.Count; i++) r[i] = new Vector3(r[i].x, 0f, r[i].z);
            return r;
        }
    }
}
