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
        public float radius;        // 足元の半径（m、拡大込み）。重なり・道からの距離の判定に使う
        public float height;        // 高さ（m、拡大込み）
        public bool collider;       // 当たり判定を付けるか（届く範囲の大きなものだけ）
        public int shape;           // DressingPlan.SHAPE_*
        public bool background;     // 背景専用（メサ・厩舎：見えない壁で囲む）
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
        public const int MAX_PER_STAGE = 1400;     // art-r1：谷底の近景・道の目印を足した（小物は静的バッチング＋トゥーンの GPU インスタンシング前提）
        public const int MIN_PER_STAGE = 450;
        public const int MAX_LOBBY = 150;          // 木の帯（LobbyLayout.TreeBelt）と柵は別に数える（ロビー＝町だけ、飾りは町と柵の間の 8m の帯だけなので 300 → 150）
        public const float LARGE_MIN_H = 1.6f;     // 「大きな飾り」の高さ。これ未満は小物（まとまりの中だけ、1 ステージの 25% まで）
        public const float SMALL_SHARE_MAX = 0.25f;
        public const float BAND_WINDOW_M = 100f;   // 道沿いの帯：コース 100m ごとに
        public const float BAND_DIST_M = 60f;      //   道から 60m 以内の大きな飾りを
        public const int ROADSIDE_LARGE_PER_100M = 6;   //   6 個以上
        public const float TREE_SCALE_MIN = 0.8f, TREE_SCALE_MAX = 1.4f;
        public const int OASIS_TREES = 25;
        // テーマごとの木の最少本数（テストで確認）
        public const int MIN_DEAD_TREES_CANYON = 90, MIN_DEAD_TREES_DUNES = 30, MIN_TREES_BORDER = 120, MIN_TREES_LOBBY = 20;
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
        public const float CANYON_FLOOR_STEP_M = 22f;    // 谷底の近景：弧長 22m ごと、左右交互
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

        public const int SHAPE_NONE = 0, SHAPE_BOX = 1, SHAPE_MESH = 2, SHAPE_TRUNK = 3;

        // ---- 素材のパス（Assets/HorseRace/Models からの相対）----
        public const string CLIFFS = "Packs/Cliffs/Cliffs.fbx", DROCKS = "Packs/DesertRocks/DesertRocks.fbx",
            RCOLL = "Packs/RockCollection/RockCollection.fbx", DPROPS = "Packs/DesertProps/DesertProps.fbx",
            CACTI = "Packs/Cacti/Cacti.fbx", CAMPFIRE = "Packs/Campfire/Campfire.fbx", LOGS = "Packs/WoodLogs/WoodLogs.fbx",
            HAY = "Packs/HayBales/HayBales.fbx", FARM = "Packs/FarmProps/FarmProps.fbx",
            ACACIA = "Props/AcaciaGrove/AcaciaGrove.fbx", CGROUP = "Props/CactusGroup/CactusGroup.fbx", DEADTREE = "Props/DeadTree/DeadTree.fbx",
            MESA = "Props/MesaRock/MesaRock.fbx", STABLE = "Props/Stable/Stable.fbx", TENT = "Props/Tent/Tent.fbx",
            MESAB = "ThirdParty/MesaRockB/MesaRockB.fbx", RSTYL = "ThirdParty/RockSetStylized/RockSetStylized.fbx",
            TOWNBLOCK = "Buildings/TownBlock.fbx", SALOON = "Buildings/SaloonFacade.fbx", RANCH = "Buildings/RanchCompound.fbx",
            SKELETON = "SkeletonHorse.fbx", TREES = "Packs/Trees/Trees.fbx", WTOWN = "Packs/WesternTown/WesternTown.fbx";
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
        static readonly string[] DpFence = Named("DesertProps", 6, 10, 3, 5);  // 06 = 柵、10 = 杭の列、03/05 = 杭
        static readonly string[] CactiParts = Parts("Cacti", 1, 9);
        static readonly string[] FarmCrates = Named("FarmProps", 11, 12, 19);
        static readonly string[] FarmBarrels = Named("FarmProps", 15, 22);
        static readonly string[] FarmSmall = Named("FarmProps", 9, 10, 13, 14, 16, 17, 18, 20, 21, 23, 24, 25, 26, 27, 28, 29, 30);
        static readonly string[] FarmFences = Named("FarmProps", 3, 4, 6, 8);
        const string FARM_LAMP = "FarmProps_02", FARM_TABLE = "FarmProps_05", FARM_BENCH = "FarmProps_07";
        static readonly string[] LogParts = Parts("WoodLogs", 1, 16);
        static readonly string[] HayParts = Parts("HayBales", 1, 7);
        static readonly string[] TreeParts = { "Trees_01", "Trees_02" };
        static readonly string[] DeadAndRocks = { "DesertRocks_10", "DesertRocks_11", "DesertRocks_12", "DesertRocks_13", "DesertRocks_14", "DesertRocks_15" };
        static readonly char[] GrayLetters = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H' };

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
                return new[] { CLIFFS, DROCKS, RCOLL, DPROPS, DEADTREE, ACACIA, CAMPFIRE, LOGS, HAY, FARM, TOWNBLOCK, SALOON };
            if (kind == HeightmapGen.KIND_DUNES)
                return new[] { CACTI, CGROUP, DPROPS, SKELETON, TENT, ACACIA, TREES, DEADTREE, CAMPFIRE, LOGS, HAY, FARM, RANCH, DROCKS, RCOLL };
            var m = new List<string> { MESA, MESAB, RSTYL, CGROUP, CACTI, STABLE, TOWNBLOCK, SALOON, FARM, HAY, LOGS, DEADTREE, DPROPS, TREES, ACACIA };
            foreach (char c in GrayLetters) m.Add(RockGray(c));
            return m.ToArray();
        }

        public static readonly string[] LobbyThemeAssets = { FARM, HAY, LOGS, DPROPS, CACTI, TREES, ACACIA };

        /// <summary>小物を置いてよい「まとまり」のグループ（野営地・オアシス・集落・骨の原・岩場など）。</summary>
        public static readonly HashSet<string> ClusterGroups = new HashSet<string>
        {
            "WagonCamp", "RuinedHomestead", "Oasis", "RanchWaypoint", "Settlement", "BoneField", "GoalTown", "RockField", "Rocks",
            "Scrub", "DuneGrass", "CactusForest", "Cactus", "TownEdge", "DeadGroveFloor", "DeadGroveRim", "DeadGroveFar", "SavannaGrove", "GrayRocks",
            "CanyonFloor", "GuideLine", "Railway", "Backlot", "FinishCamp", "CanyonRim", "MesaPlateau", "RimLandmark",
        };

        /// <summary>道のすぐ脇（ROAD_MIN、平らな道 ±12m の外）に意図して置くグループ。それ以外は ROAD_CLEAR より外。</summary>
        public static readonly HashSet<string> NearRoadGroups = new HashSet<string> { "Roadside", "GoalTown", "CanyonFloor", "GuideLine", "MesaGate", "MesaSaddle", "CornerSilhouette" };

        public static bool IsSmall(Placement p) => p.height < LARGE_MIN_H;

        /// <summary>道・除外の円との距離を調べるときの足元の半径。崖の壁は厚さの半分（長い辺は道に沿う）、メサの門は道側の裾。</summary>
        public static float FootprintRadius(Placement p)
        {
            if (p.group == "CliffWalls" || p.group == "CornerSilhouette" || p.group == "MesaSaddle" || (p.group == "MesaPlateau" && p.asset == CLIFFS))
                return p.radius * CLIFF_HALF_DEPTH;
            if (p.group == "MesaGate") return p.radius * MESA_GATE_FOOTPRINT;
            return p.radius;
        }

        public static bool IsTree(Placement p) => p.asset == TREES || p.asset == ACACIA || p.asset == DEADTREE;

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
        static void TrimSmall(List<Placement> list)
        {
            int small = 0;
            foreach (var p in list) if (IsSmall(p)) small++;
            for (int pass = 0; pass < 2 && small > SMALL_SHARE_MAX * list.Count; pass++)
                for (int i = list.Count - 1; i >= 0 && small > SMALL_SHARE_MAX * list.Count; i--)
                {
                    var p = list[i];
                    if (!IsSmall(p)) continue;
                    if (pass == 0 && ClusterGroups.Contains(p.group)) continue;
                    list.RemoveAt(i);
                    small--;
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

            public float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            public int Pick(int n) => rng.Next(n);
            public string Pick(string[] a) => a[rng.Next(a.Length)];

            public float Ground(float x, float z)
                => h == null ? 0f : WorldLayout.SampleHeight(h, sizeM, HeightmapGen.HEIGHT_M, x, z);

            public float RoadDist(Vector3 p) => course == null ? float.MaxValue : RoadClearance(def, course, cum, p);

            /// <summary>rBounds ≥ 0 なら範囲・置かない長方形・円はその半径で調べる（木の枝が柵や町の縁にかかってよいとき）。重なりは r で調べる。</summary>
            public bool Allowed(Vector3 p, float r, float minRoad, bool ignoreCircles, float rBounds = -1f)
            {
                float rr = r;
                if (rBounds >= 0f) r = rBounds;
                if (p.x - r < minX || p.x + r > maxX || p.z - r < minZ || p.z + r > maxZ) return false;
                if (RoadDist(p) - r < minRoad) return false;
                if (!ignoreCircles)
                    foreach (var c in circles)
                        if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + r) return false;
                if (capR > 0f && WorldLayout.DistToSegmentXZ(p, capA, capB) < capR + r) return false;
                if (hasRect && p.x + r > rectMin.x && p.x - r < rectMax.x && p.z + r > rectMin.y && p.z - r < rectMax.y) return false;
                r = rr;
                if (r >= 1.5f)
                    foreach (var q in list)
                        if (q.radius >= 1.5f && WorldLayout.DistXZ(p, q.position) < 0.75f * (r + q.radius)) return false;
                return true;
            }

            /// <summary>置けたら true。</summary>
            public bool Add(string group, string asset, string part, Vector3 p, float yaw, float scale, float sink,
                float minRoad = ROAD_CLEAR, bool trunk = false, bool background = false, float roll = 0f, bool ignoreCircles = false, float rBounds = -1f)
            {
                if (list.Count >= budget) return false;
                Size s = SizeOf(asset, part);
                float r = s.r * scale, hh = s.h * scale;
                if (!Allowed(p, r, minRoad, ignoreCircles, rBounds)) return false;
                float y = Ground(p.x, p.z);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    y = Mathf.Min(y, Ground(p.x + Mathf.Sin(a) * r * 0.7f, p.z + Mathf.Cos(a) * r * 0.7f));
                }
                bool reach = course == null || WorldLayout.DistToPolylineXZ(p, course) - r < REACHABLE_M;
                bool col = !background && reach && hh >= COLLIDER_MIN_H;
                list.Add(new Placement
                {
                    group = group, asset = asset, part = part, position = new Vector3(p.x, y - sink, p.z),
                    yawDeg = yaw, rollDeg = roll, scale = scale, radius = r, height = hh, collider = col,
                    shape = !col ? SHAPE_NONE : (trunk ? SHAPE_TRUNK : (r >= 6f ? SHAPE_MESH : SHAPE_BOX)),
                    background = background,
                });
                return true;
            }

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
            };
            foreach (var c in StageExclusions(def, courseLocal, goalArc)) w.circles.Add(c);
            if (shortcut != null)
            {
                w.capA = shortcut.a; w.capB = shortcut.b;
                w.capR = SetPieces.SHORTCUT_HALF_WIDTH + SetPieces.SHORTCUT_BLEND + 4f;
            }
            if (def.kind == HeightmapGen.KIND_MESA) GoalTown(w, goalArc);   // 町の建物を先に（道沿いの固定の場所）
            else FinishCamp(w, def, goalArc);                                 // 荒野・砂漠：ゴールの先の野営地と最後の直線の先の目印
            Landmark(w, def, goalArc);        // 遠くの目印（スタートから見える）
            CornerSilhouettes(w, def, goalArc);   // 急カーブの外側の崖（先を隠す）
            if (def.kind == HeightmapGen.KIND_MESA) { MesaGate(w, def, goalArc); MesaPlateau(w, def, goalArc); FinalApproachRail(w, def, goalArc); }
            if (def.kind == HeightmapGen.KIND_CANYON) { RimLandmarks(w, def, goalArc); CanyonRimFences(w, def, goalArc); }
            if (def.kind == HeightmapGen.KIND_DUNES) Railway(w, def, goalArc);
            GuideLine(w, def, goalArc);       // 道の目印（カーブの外側の杭・柵、直線の標柱）
            if (def.kind == HeightmapGen.KIND_CANYON) Canyon(w, def, goalArc);
            else if (def.kind == HeightmapGen.KIND_DUNES) Dunes(w, def, goalArc);
            else Border(w, def, goalArc);
            Roadside(w, goalArc);
            TrimSmall(w.list);
            return w.list;
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
            Vector3 spot = lineP - r0 * (CourseGeometry.GATE_HALF + 3f) + t0 * 2f;
            r.Add(new Vector4(spot.x, 0f, spot.z, 14f));
            float bridge = SetPieces.BridgeArc(def, goalArc);
            if (bridge >= 0f)
            {
                Vector3 b = WorldLayout.PointAtArc(course, bridge);
                r.Add(new Vector4(b.x, 0f, b.z, SetPieces.DETOUR_TO + 6f));
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
                Vector3 e = lineP + r0 * (sd * (CourseGeometry.GATE_HALF + 3f));
                r.Add(new Vector4(e.x, 0f, e.z, 5f));
            }
            // CP の黒板（BOARD_EVERY 個ごとの CP・最後の下りの CP・ゴール、道の右）
            float[] cps = WorldLayout.CheckpointArcs(goalArc, StageDefinitions.CP_SPACING_M);
            for (int k = 0; k < cps.Length; k++)
            {
                if (!CourseMarks.HasBoard(cps, k, goalArc)) continue;
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
            float total = CoursePath.Length(course);
            Vector3 pen = WorldLayout.PointAtArc(course, total - CourseGeometry.PEN_BACK);
            r.Add(new Vector4(pen.x, 0f, pen.z, CourseGeometry.PEN_RADIUS + 6f));
            if (def.kind == HeightmapGen.KIND_MESA)
            {
                Vector3 pp = PodiumLocal(course, goalArc);
                r.Add(new Vector4(pp.x, 0f, pp.z, 9f));
                Vector3 end = course[course.Length - 1];
                r.Add(new Vector4(end.x, 0f, end.z, 140f));   // ゴールの町（建物は GoalTown が固定の場所に置く）
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
                    w.Add("Roadside", FARM, FARM_LAMP, w.Side(a, s * (15f + w.Extra(a))), yaw, 1f, 0.05f, ROAD_MIN);
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
                    Grove(w, G, c, 28f, 5, 1f, 0f);
                }
            }
            // 谷底の近景（道から 14〜28m、22m ごとに左右交互）を最初に：走っていて速さを感じる中景の物
            CanyonFloor(w, goalArc);
            // 谷の縁に沿った崖の壁（左右交互に 50m ごと）。部品の広い面（ローカル ±X）を道へ向け、長い辺（ローカル Z）を道に沿わせる
            // （Blender で測った：Cliffs は低ポリの塊で、Z 方向が長く、±X の面が一番広い）。壁の裾を谷底の縁に置き、高さの 22% を斜面に埋める
            int ci = 0;
            for (float a = 40f; a < total; a += 50f)
            {
                int s = (ci % 2 == 0) ? 1 : -1;
                string part = CliffParts[ci++ % CliffParts.Length];
                float sc = w.Rand(1f, 1.4f);
                Size cs = SizeOf(CLIFFS, part);
                float r = cs.r * sc;
                float arc = a + w.Rand(-15f, 15f);
                float yaw = w.YawAt(arc) + (w.Pick(2) == 0 ? 0f : 180f) + w.Rand(-CLIFF_YAW_JITTER, CLIFF_YAW_JITTER);
                w.Add("CliffWalls", CLIFFS, part, w.Side(arc, s * (ROAD_CLEAR + w.Extra(arc) + r * CLIFF_HALF_DEPTH + w.Rand(0f, 8f))), yaw, sc,
                    CLIFF_SINK * cs.h * sc, rBounds: r * CLIFF_HALF_DEPTH);
            }
            // 枯れ木の林：谷底（道から 20〜60m）を 120m ごとに左右交互、縁（60〜120m）にも
            int side = 1;
            for (float a = 60f; a < total; a += 120f, side = -side)
            {
                Grove(w, "DeadGroveFloor", w.Side(a, side * w.Rand(28f, 45f)), 14f, 5, 0.9f, 0.1f);
                Grove(w, "DeadGroveRim", w.Side(a + 60f, -side * w.Rand(70f, 110f)), 18f, 4, 1f, 0f);
            }
            // 大岩
            foreach (var c in w.Centers(def.seed + 1, 8, 60f, 160f, 110f)) w.Add("BigRocks", DROCKS, w.Pick(RockBig), c, w.Rand(0f, 360f), w.Rand(1f, 1.3f), 1.5f);
            // 道沿いの帯（大きな岩・枯れ木・中くらいの崖）。狭い谷なので道から 32m まで（斜面・縁に乗らない）
            RoadsideBand(w, goalArc, x =>
            {
                int k = x.rng.Next(10);
                if (k < 4) return new[] { DEADTREE, null };
                if (k < 7) return new[] { DROCKS, x.Pick(RockMid) };
                if (k < 9) return new[] { DPROPS, x.Pick(DpRocks) };
                return new[] { CLIFFS, x.Pick(new[] { "Cliffs_08", "Cliffs_10", "Cliffs_13" }) };
            }, CANYON_BAND_MAX_LAT);
            // 岩場（まとまり 14 か所）
            foreach (var c in w.Centers(def.seed + 2, 14, 26f, 140f, 70f))
                for (int i = 0; i < 9; i++)
                {
                    int k = w.Pick(3);
                    if (k == 0) w.Add("RockField", DROCKS, w.Pick(i < 3 ? RockMid : RockSmall), w.Around(c, 22f), w.Rand(0f, 360f), w.Rand(0.8f, 1.3f), 0.3f);
                    else if (k == 1) w.Add("RockField", RCOLL, w.Pick(Pebbles), w.Around(c, 18f), w.Rand(0f, 360f), w.Rand(1.5f, 3f), 0.1f);
                    else w.Add("RockField", DPROPS, w.Pick(DpRocks), w.Around(c, 20f), w.Rand(0f, 360f), w.Rand(0.8f, 1.6f), 0.2f);
                }
            // 枯れ木をさらに（遠くの斜面）
            foreach (var c in w.Centers(def.seed + 5, 14, 60f, 200f, 70f)) Grove(w, "DeadGroveFar", c, 16f, 4, 1f, 0f);
            // 道ばたの小さなサボテンと草（まとまり）
            foreach (var c in w.Centers(def.seed + 4, 8, 20f, 60f, 60f))
                for (int i = 0; i < 4; i++)
                    w.Add("Scrub", DPROPS, i % 2 == 0 ? w.Pick(DpCacti) : w.Pick(DpGrass), w.Around(c, 8f), w.Rand(0f, 360f), 1f, 0.05f);
        }

        /// <summary>
        /// 谷底の近景：弧長 CANYON_FLOOR_STEP_M ごとに左右交互、道の脇（ROAD_MIN+1〜28m）に小さな岩・砂漠の岩・枯れ木を 1 つ、
        /// 1 つおきに小石の群れ（RockCollection 3〜5 個、拡大 2〜3）。CP の黒板の横（右側）は空ける（除外の円）。
        /// </summary>
        static void CanyonFloor(Work w, float goalArc)
        {
            const string G = "CanyonFloor";
            int i = 0;
            for (float a = 30f; a < goalArc - 10f; a += CANYON_FLOOR_STEP_M, i++)
            {
                float sd = (i % 2 == 0) ? 1f : -1f;
                int k = w.Pick(10);
                string asset, part; float sc; bool tree = false;
                if (k < 4) { asset = DROCKS; part = w.Pick(DeadAndRocks); sc = w.Rand(1.0f, 1.6f); }
                else if (k < 7) { asset = DPROPS; part = w.Pick(DpRocks); sc = w.Rand(0.9f, 1.4f); }
                else { asset = DEADTREE; part = null; sc = w.Rand(0.9f, 1.2f); tree = true; }
                float r = SizeOf(asset, part).r * sc;
                float arc = a + w.Rand(-4f, 4f);
                float lat = w.Rand(ROAD_MIN + 1f + r, CANYON_FLOOR_MAX_LAT) + w.Extra(arc);
                Vector3 p = w.Side(arc, sd * lat);
                w.Add(G, asset, part, p, w.Rand(0f, 360f), sc, tree ? 0.2f : 0.25f * SizeOf(asset, part).h * sc, ROAD_MIN, trunk: tree);
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

        /// <summary>
        /// 道の目印：曲率が GUIDE_CURVATURE を超えるカーブは、外側 GUIDE_LATERAL m に GUIDE_STEP_M ごとの杭の列（荒野・砂漠 = DesertProps_10）か
        /// 柵（境目 = FarmProps_03/04）を、カーブの終わりの GUIDE_AFTER_M m 先まで。直線は GUIDE_STRAIGHT_STEP_M ごとに左右交互の標柱（DesertProps_05）。
        /// </summary>
        static void GuideLine(Work w, StageDef def, float goalArc)
        {
            const string G = "GuideLine";
            float lastCorner = -1000f, lastPost = -1000f, cornerSide = 0f;
            int post = 0;
            for (float a = 20f; a < goalArc - 30f; a += GUIDE_STEP_M)
            {
                float k = Curvature(w.course, a);
                if (Mathf.Abs(k) > GUIDE_CURVATURE) { lastCorner = a; cornerSide = k > 0f ? 1f : -1f; }   // 左へ曲がる → 外側は右（+）
                if (a - lastCorner <= GUIDE_AFTER_M)
                {
                    // 荒野・砂漠：柵（DesertProps_06）と杭の列（_10）を交互（course-r1-09：急カーブの外側の線）
                    string part = def.kind == HeightmapGen.KIND_MESA ? (w.Pick(2) == 0 ? "FarmProps_03" : "FarmProps_04") : (w.Pick(2) == 0 ? "DesertProps_06" : "DesertProps_10");
                    string asset = def.kind == HeightmapGen.KIND_MESA ? FARM : DPROPS;
                    float r = SizeOf(asset, part).r;
                    float lat = Mathf.Max(GUIDE_LATERAL, ROAD_MIN + r + 0.05f) + (StageDefinitions.HalfWidthAt(def, a) - HeightmapGen.RUNWAY_M);
                    // 柵・杭の列の長い辺を道に沿わせる（FarmProps_03 は X が長い。FarmProps_04 は Z が長い）
                    float yaw = w.YawAt(a) + (part == "FarmProps_04" ? 0f : 90f);
                    w.Add(G, asset, part, w.Side(a, cornerSide * lat), yaw, 1f, 0.05f, ROAD_MIN);
                    lastPost = a;
                }
                else if (a - lastPost >= GUIDE_STRAIGHT_STEP_M)
                {
                    float sd = (post++ % 2 == 0) ? 1f : -1f;
                    float lat = Mathf.Max(GUIDE_LATERAL, ROAD_MIN + SizeOf(DPROPS, "DesertProps_05").r + 0.05f) + (StageDefinitions.HalfWidthAt(def, a) - HeightmapGen.RUNWAY_M);
                    if (w.Add(G, DPROPS, "DesertProps_05", w.Side(a, sd * lat), w.YawAt(a), 1f, 0.1f, ROAD_MIN)) lastPost = a;
                }
            }
        }

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
        {
            float s = StageDefinitions.START_ARC_M;
            Vector3 p0 = WorldLayout.PointAtArc(course, s), t0 = WorldLayout.TangentAtArc(course, s);
            Vector3 g = WorldLayout.PointAtArc(course, goalArc) - p0;
            float goalAng = Mathf.DeltaAngle(WorldLayout.YawDeg(t0), WorldLayout.YawDeg(new Vector3(g.x, 0f, g.z).normalized));
            float pref = Mathf.Clamp(goalAng, -30f, 30f);
            var circles = StageExclusions(def, course, goalArc);
            var angles = new List<float>();
            for (float a = -30f; a <= 30.01f; a += 5f) angles.Add(a);
            angles.Sort((x, y) => Mathf.Abs(x - pref).CompareTo(Mathf.Abs(y - pref)));
            foreach (float a in angles)
                for (float d = 700f; d >= 380f; d -= 40f)
                {
                    float yaw = (WorldLayout.YawDeg(t0) + a) * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(p0.x + Mathf.Sin(yaw) * d, 0f, p0.z + Mathf.Cos(yaw) * d);
                    if (p.x < 70f || p.z < 70f || p.x > StageDefinitions.SIZE - 70f || p.z > StageDefinitions.SIZE - 70f) continue;
                    if (WorldLayout.DistToPolylineXZ(p, course) < LANDMARK_MIN_ROAD_M) continue;
                    bool free = true;
                    foreach (var c in circles) if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + 10f) free = false;
                    if (!free) continue;
                    spot = p; angleDeg = a;
                    return true;
                }
            spot = Vector3.zero; angleDeg = 0f;
            return false;
        }

        /// <summary>遠くの目印（給水塔・高い建物）を 1 つ。DressingBuilder が煙の柱と灯りを足す。</summary>
        static void Landmark(Work w, StageDef def, float goalArc)
        {
            Vector3 p; float ang;
            if (!LandmarkSpot(def, w.course, goalArc, out p, out ang)) return;
            string asset, part; float sc;
            LandmarkAsset(def.kind, out asset, out part, out sc);
            Vector3 toStart = WorldLayout.PointAtArc(w.course, StageDefinitions.START_ARC_M) - p;
            w.Add("Landmark", asset, part, p, WorldLayout.YawDeg(new Vector3(toStart.x, 0f, toStart.z).normalized), sc, 0.3f, LANDMARK_MIN_ROAD_M - 10f,
                background: true, ignoreCircles: true);
        }

        /// <summary>砂漠の廃線：SetPieces.RailRuns の場所に線路（95.6m）を置き、線路に沿って除外の円を足す（他の飾りが線路に乗らない）。</summary>
        static void Railway(Work w, StageDef def, float goalArc)
        {
            foreach (var run in SetPieces.RailRuns(def, w.course, goalArc))
            {
                Vector3 a = run[0], b = run[1];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
                if (!w.Add("Railway", WTOWN, RAIL_TRACK, mid, WorldLayout.YawDeg(d), 1f, 0.15f, ROAD_CLEAR)) continue;
                w.list[w.list.Count - 1].collider = false;   // 低い線路は乗り越えられる（当たり判定なし）
                w.list[w.list.Count - 1].shape = SHAPE_NONE;
                for (int k = 0; k <= 16; k++)
                {
                    Vector3 q = Vector3.Lerp(a, b, k / 16f);
                    w.circles.Add(new Vector4(q.x, 0f, q.z, 3.5f));
                }
                // 線路の端に枕木の山（薪）と車止めがわりの岩
                w.Add("Railway", LOGS, w.Pick(LogParts), a + d * 3f + WorldLayout.RightOf(d) * 4.5f, w.Rand(0f, 360f), 1.6f, 0.02f, ROAD_CLEAR, ignoreCircles: true);
                w.Add("Railway", DROCKS, w.Pick(RockSmall), b - d * 2f + WorldLayout.RightOf(d) * 4.5f, w.Rand(0f, 360f), 1.2f, 0.3f, ROAD_CLEAR, ignoreCircles: true);
            }
        }

        /// <summary>
        /// 境目のメサの門：切り通し（SetPieces.PassArc）の両脇にメサ ×1.6 とメサ B ×1.6。遠く（300m 手前）から三角の切れ込みに見える。
        /// 道のすぐ脇なので見えない壁（外形の箱）ではなく、メッシュの当たり判定（道から遠い部分も正確）。
        /// </summary>
        static void MesaGate(Work w, StageDef def, float goalArc)
        {
            float pass = SetPieces.PassArc(def, goalArc);
            if (pass < 0f) return;
            const float SC = 1.6f;
            float yaw = w.YawAt(pass);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                string asset = sd < 0 ? MESA : MESAB;
                Size sz = SizeOf(asset, null);
                float r = sz.r * SC;
                float lat = SetPieces.PASS_WALL_FROM + r * 0.6f;
                w.Add("MesaGate", asset, null, w.Side(pass + sd * 6f, sd * lat), yaw + (sd < 0 ? 0f : 180f), SC, 0.18f * sz.h * SC, ROAD_MIN,
                    rBounds: r * MESA_GATE_FOOTPRINT);
            }
        }

        static void Dunes(Work w, StageDef def, float goalArc)
        {
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
                for (int i = 0; i < 6; i++) w.Add("BoneField", DPROPS, DpBones[i % DpBones.Length], w.Around(c, 10f), w.Rand(0f, 360f), w.Rand(0.9f, 1.3f), 0.05f);
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
            // 道沿いの並木（Trees とアカシアを交互、25m ごと、左右）
            for (float a = 20f; a < goalArc; a += 25f)
                for (int s = -1; s <= 1; s += 2)
                {
                    bool ac = ((int)(a / 25f) + (s > 0 ? 1 : 0)) % 3 == 0;
                    Tree(w, "TreeLine", ac ? ACACIA : TREES, ac ? null : TreePart(w), w.Side(a + w.Rand(-5f, 5f), s * w.Rand(22f, 32f)));
                }
            // サバンナの林（Trees＋アカシア、12 か所）
            foreach (var c in w.Centers(def.seed + 7, 12, 40f, 160f, 80f)) Grove(w, "SavannaGrove", c, 20f, 8, 0.1f, 0.4f);
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
                if (k < 7) return new[] { TREES, TreePart(x) };
                if (k < 8) return new[] { ACACIA, null };
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
        }

        public const float GOAL_STREET_FROM = -260f, GOAL_STREET_TO = 60f;   // ゴールからの弧長（course-r1-12：最後の 260m は町の通り）
        public const float GOAL_STREET_SIDE = 22f, GOAL_STREET_STEP = 20f, GOAL_HAY_SIDE = 13f;
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
                    Vector3 p = w.Side(a + sd * 4f, sd * (GOAL_STREET_SIDE + w.Extra(a)));
                    if (NearSmallCircle(w, p, SizeOf(WTOWN, StreetBuildings[0]).r)) continue;
                    if (WorldLayout.DistXZ(p, podium) < 18f) continue;
                    if (Mathf.Abs(off) < 10f) continue;   // ゴールゲートの両脇は空ける
                    int v = (k * 2 + (sd > 0 ? 1 : 0)) % 6;
                    float face = yaw + (sd < 0 ? 90f : -90f);
                    if (v == 0) w.Add("GoalTown", TOWNBLOCK, null, p, face, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                    else if (v == 3) w.Add("GoalTown", SALOON, null, p, face, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                    else w.Add("GoalTown", WTOWN, StreetBuildings[(k + (sd > 0 ? 3 : 0)) % StreetBuildings.Length], p, face, 1f, 0.2f, ROAD_MIN, ignoreCircles: true);
                }
                // 干し草の山（見物人の柵）：2 つ積み＋小さな束、左右交互
                if (Mathf.Abs(off) >= 12f)
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
            // 通りの奥の給水塔（コースの終点からさらに 45m 先、コースの延長線上）
            Vector3[] c = w.course;
            Vector3 end = c[c.Length - 1], dir = WorldLayout.TangentAtArc(c, CoursePath.Length(c) - 1f);
            Vector3 tp = end + dir * 45f;
            tp.x = Mathf.Clamp(tp.x, EDGE_MARGIN + 10f, w.sizeM - EDGE_MARGIN - 10f);
            tp.z = Mathf.Clamp(tp.z, EDGE_MARGIN + 10f, w.sizeM - EDGE_MARGIN - 10f);
            w.Add("GoalTown", RANCH, RANCH_TOWER, tp, WorldLayout.YawDeg(-dir), 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
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
            Vector3 c;
            if (FindBesideRange(w, goalArc + 40f, goalArc + 80f, 26f, 40f, 10f, out c)) WagonCamp(w, c, w.YawAt(goalArc), def.kind == HeightmapGen.KIND_DUNES);
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

        static bool FindBesideRange(Work w, float a0, float a1, float latMin, float latMax, float r, out Vector3 p)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                float a = Mathf.Lerp(a0, a1, (tries % 5) / 4f);
                float side = (tries / 5) % 2 == 0 ? 1f : -1f;
                p = w.Side(a, side * w.Rand(latMin, latMax));
                if (w.Allowed(p, r, ROAD_CLEAR, false)) return true;
            }
            p = Vector3.zero;
            return false;
        }

        /// <summary>急カーブの外側の崖（course-r1-02）：Cliffs_01〜05 ×0.6〜0.9 を頂点の外側に置き、先の道を隠す（三角の影の決まり）。</summary>
        static void CornerSilhouettes(Work w, StageDef def, float goalArc)
        {
            var corners = StageDefinitions.TightCorners(def, w.course);
            for (int i = 0; i < corners.Count; i++)
            {
                var c = corners[i];
                string part = "Cliffs_0" + (1 + i % 5);
                Size cs = SizeOf(CLIFFS, part);
                for (int tries = 0; tries < 6; tries++)
                {
                    float sc = 0.9f - tries * 0.06f;
                    float r = cs.r * sc;
                    float extra = StageDefinitions.HalfWidthAt(def, c.apexArc) - HeightmapGen.RUNWAY_M;
                    float lat = -c.side * (ROAD_CLEAR + 4f + extra + r * CLIFF_HALF_DEPTH + tries * 3f);
                    float yaw = w.YawAt(c.apexArc) + (i % 2 == 0 ? 0f : 180f);
                    if (w.Add("CornerSilhouette", CLIFFS, part, w.Side(c.apexArc, lat), yaw, sc, CLIFF_SINK * cs.h * sc, ROAD_MIN, rBounds: r * CLIFF_HALF_DEPTH))
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
                        if (w.Add("RimLandmark", RANCH, RANCH_TOWER, p, w.YawAt(a) + 180f, 1f, 0.3f, LANDMARK_MIN_ROAD_M - 10f, background: true)) break;
                    }
                    else if (w.Add("RimLandmark", CAMPFIRE, "Campfire_01", p, 0f, 1.2f, 0.05f, LANDMARK_MIN_ROAD_M - 10f))
                    {
                        Grove(w, "RimLandmark", p, 12f, 4, 1f, 0f);
                        break;
                    }
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
                        string part = k % 3 == 2 ? "DesertProps_10" : "DesertProps_06";
                        w.Add("CanyonRim", DPROPS, part, w.Side(a, sd * 34f), w.YawAt(a) + 90f, 1f, 0.05f);
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
                    string part = sd < 0 ? "Cliffs_01" : "Cliffs_03";
                    const float SC = 0.8f;
                    Size cs = SizeOf(CLIFFS, part);
                    float r = cs.r * SC;
                    float yaw = w.YawAt(saddle) + (sd < 0 ? 0f : 180f);
                    for (int tries = 0; tries < 4; tries++)
                        if (w.Add("MesaSaddle", CLIFFS, part, w.Side(saddle, sd * (StageDefinitions.CHOKE_HALF_M + 3f + r * CLIFF_HALF_DEPTH + tries * 2f)), yaw, SC,
                            CLIFF_SINK * cs.h * SC * 0.5f, ROAD_MIN, rBounds: r * CLIFF_HALF_DEPTH, ignoreCircles: true)) break;
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
                    string part = CliffParts[7 + i % 6];   // Cliffs_08〜13
                    Size cs = SizeOf(CLIFFS, part);
                    float sc = 0.7f;
                    float r = cs.r * sc;
                    w.Add("MesaPlateau", CLIFFS, part, w.Side(a, sd * (SetPieces.MESA_BAND_HALF + 3f + r * CLIFF_HALF_DEPTH)), w.YawAt(a) + (sd > 0 ? 180f : 0f), sc,
                        0.45f * cs.h * sc, rBounds: r * CLIFF_HALF_DEPTH);
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
            return w.list;
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

        /// <summary>遠景の山並みの素材（崖の板は使わない：遠くでも平らな箱に見えるため）。部品のあるものは部品名。</summary>
        static readonly string[][] BackdropAssets =
        {
            new[] { MESA, null }, new[] { MESAB, null }, new[] { DROCKS, "DesertRocks_01" }, new[] { RSTYL, null },
            new[] { MESAB, null }, new[] { DROCKS, "DesertRocks_02" }, new[] { MESA, null }, new[] { DROCKS, "DesertRocks_03" },
        };

        /// <summary>
        /// ワールド全体を囲む遠景の山並み（ワールド座標）。遊べる範囲の外接長方形の外側に 2 列（BACKDROP_OFFSET_M・BACKDROP_OFFSET2_M、
        /// 間隔 BACKDROP_SPACING_M・BACKDROP_SPACING2_M、前後左右に ±BACKDROP_JITTER_M ずらす）、メサ・メサ B・大岩・岩の群れを 5〜10 倍で置く
        /// （Collider なし、静的）。足元は外の地面（WorldBounds.FillerBaseHeight）の高さで、高さの 25% を埋める。
        /// 山の足元は遊べる範囲から BACKDROP_CLEAR_M m 以上離す。
        /// </summary>
        public static List<Placement> Backdrop(int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            Vector4 u = WorldBounds.Union();
            float w = u.z - u.x, d = u.w - u.y;
            float perim = 2f * (w + d);
            float[] offs = { WorldBounds.BACKDROP_OFFSET_M, WorldBounds.BACKDROP_OFFSET2_M };
            float[] gaps = { WorldBounds.BACKDROP_SPACING_M, WorldBounds.BACKDROP_SPACING2_M };
            int k = 0;
            for (int row = 0; row < 2; row++)
            {
                int n = Mathf.FloorToInt(perim / gaps[row]);
                for (int i = 0; i < n; i++, k++)
                {
                    float jitter = ((float)rng.NextDouble() * 2f - 1f) * WorldBounds.BACKDROP_JITTER_M;
                    // 2 列目は半分ずらす（互い違い）
                    float t = (i + (row == 1 ? 0.5f : 0f)) * perim / n + jitter;
                    t = (t % perim + perim) % perim;
                    string[] ap = BackdropAssets[(k + row) % BackdropAssets.Length];
                    float scale = 5f + (float)rng.NextDouble() * 5f;
                    Size sz = SizeOf(ap[0], ap[1]);
                    float r = sz.r * scale;
                    float outM = Mathf.Max(offs[row] + ((float)rng.NextDouble() * 2f - 1f) * WorldBounds.BACKDROP_JITTER_M,
                        r + WorldBounds.BACKDROP_CLEAR_M);
                    // 外接長方形の周上の点 → 外向きの法線方向へ outM
                    Vector3 p; Vector3 nrm;
                    if (t < w) { p = new Vector3(u.x + t, 0f, u.y); nrm = new Vector3(0f, 0f, -1f); }
                    else if (t < w + d) { p = new Vector3(u.z, 0f, u.y + (t - w)); nrm = new Vector3(1f, 0f, 0f); }
                    else if (t < 2f * w + d) { p = new Vector3(u.z - (t - w - d), 0f, u.w); nrm = new Vector3(0f, 0f, 1f); }
                    else { p = new Vector3(u.x, 0f, u.w - (t - 2f * w - d)); nrm = new Vector3(-1f, 0f, 0f); }
                    p = p + nrm * outM;
                    float h = sz.h * scale;
                    list.Add(new Placement
                    {
                        group = "Backdrop", asset = ap[0], part = ap[1],
                        position = new Vector3(p.x, WorldBounds.FILLER_Y + WorldBounds.FillerBaseHeight(p.x, p.z) - WorldBounds.BACKDROP_SINK * h, p.z),
                        yawDeg = (float)rng.NextDouble() * 360f, scale = scale, radius = r, height = h,
                        collider = false, shape = SHAPE_NONE, background = false,
                    });
                }
            }
            return list;
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
            return wave * t * t * (3f - 2f * t) + rise;
        }

        /// <summary>点と長方形の x/z 距離（中なら 0）。</summary>
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

        public static bool HasBoard(float[] cps, int k, float goalArc)
        {
            if (k == cps.Length - 1 || (k + 1) % CourseGeometry.BOARD_EVERY == 0) return true;
            return k == DashBoardCp(cps, goalArc);
        }

        /// <summary>最後の下りの始まり（StageDefinitions.DashArc）より手前で一番近い CP の添字。</summary>
        public static int DashBoardCp(float[] cps, float goalArc)
        {
            float dash = StageDefinitions.DashArc(goalArc);
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
                if (def.halfWidthM[k] < HeightmapGen.RUNWAY_M && def.widthArc[2 * k] - CHOKE_WARN_M > 40f) r.Add(def.widthArc[2 * k] - CHOKE_WARN_M);
            return r;
        }

        /// <summary>合図の飾りの位置（Terrain ローカル、y = 0）：全力の合図の街灯 2 本と札、絞りの手前の黒板。</summary>
        public static List<Vector3> MarkSpots(StageDef def, Vector3[] course, float goalArc)
        {
            var r = new List<Vector3>();
            float dash = StageDefinitions.DashArc(goalArc);
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
