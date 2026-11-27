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
        public const int MAX_PER_STAGE = 800;      // Task 11d：木を増やした（静的バッチング＋トゥーンの GPU インスタンシング前提）
        public const int MIN_PER_STAGE = 450;
        public const int MAX_LOBBY = 300;          // 木の帯（LobbyLayout.TreeBelt）と柵は別に数える（ロビーを町くらいに縮めたので 450 → 300）
        public const float LARGE_MIN_H = 1.6f;     // 「大きな飾り」の高さ。これ未満は小物（まとまりの中だけ、1 ステージの 25% まで）
        public const float SMALL_SHARE_MAX = 0.25f;
        public const float BAND_WINDOW_M = 100f;   // 道沿いの帯：コース 100m ごとに
        public const float BAND_DIST_M = 60f;      //   道から 60m 以内の大きな飾りを
        public const int ROADSIDE_LARGE_PER_100M = 6;   //   6 個以上
        public const float TREE_SCALE_MIN = 0.8f, TREE_SCALE_MAX = 1.4f;
        public const int OASIS_TREES = 25;
        // テーマごとの木の最少本数（テストで確認）
        public const int MIN_DEAD_TREES_CANYON = 90, MIN_DEAD_TREES_DUNES = 30, MIN_TREES_BORDER = 120, MIN_TREES_LOBBY = 30;
        public const float ROAD_CLEAR = 18f;       // 散らす飾り：道の中心から（足元の半径込み）
        public const float ROAD_MIN = 13f;         // 道沿いに意図して置くもの（柵・町の建物）：平らな道 ±12m の外
        public const float REACHABLE_M = 60f;      // これより遠い飾りには Collider を付けない（コース外リセットで届かない）
        public const float COLLIDER_MIN_H = 1.2f;  // これより低い小物は Collider なし
        public const float EDGE_MARGIN = 10f;
        public const float CAMP_SPACING_M = 500f;  // 幌馬車の野営地の間隔（弧長）
        public const int ROADSIDE_EVERY_CP = 4;    // 道沿いの柵と標柱

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
            SKELETON = "SkeletonHorse.fbx", TREES = "Packs/Trees/Trees.fbx";
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
        };

        public static bool IsSmall(Placement p) => p.height < LARGE_MIN_H;

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

            public float RoadDist(Vector3 p) => course == null ? float.MaxValue : WorldLayout.DistToPolylineXZ(p, course);

            public bool Allowed(Vector3 p, float r, float minRoad, bool ignoreCircles)
            {
                if (p.x - r < minX || p.x + r > maxX || p.z - r < minZ || p.z + r > maxZ) return false;
                if (RoadDist(p) - r < minRoad) return false;
                if (!ignoreCircles)
                    foreach (var c in circles)
                        if (WorldLayout.DistXZ(p, new Vector3(c.x, 0f, c.z)) < c.w + r) return false;
                if (capR > 0f && WorldLayout.DistToSegmentXZ(p, capA, capB) < capR + r) return false;
                if (hasRect && p.x + r > rectMin.x && p.x - r < rectMax.x && p.z + r > rectMin.y && p.z - r < rectMax.y) return false;
                if (r >= 1.5f)
                    foreach (var q in list)
                        if (q.radius >= 1.5f && WorldLayout.DistXZ(p, q.position) < 0.75f * (r + q.radius)) return false;
                return true;
            }

            /// <summary>置けたら true。</summary>
            public bool Add(string group, string asset, string part, Vector3 p, float yaw, float scale, float sink,
                float minRoad = ROAD_CLEAR, bool trunk = false, bool background = false, float roll = 0f, bool ignoreCircles = false)
            {
                if (list.Count >= budget) return false;
                Size s = SizeOf(asset, part);
                float r = s.r * scale, hh = s.h * scale;
                if (!Allowed(p, r, minRoad, ignoreCircles)) return false;
                float y = Ground(p.x, p.z);
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    y = Mathf.Min(y, Ground(p.x + Mathf.Sin(a) * r * 0.7f, p.z + Mathf.Cos(a) * r * 0.7f));
                }
                bool reach = course == null || RoadDist(p) - r < REACHABLE_M;
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
                course = courseLocal, h = heights, sizeM = StageDefinitions.SIZE, budget = MAX_PER_STAGE,
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
                r.Add(new Vector4(b.x, 0f, b.z, SetPieces.TRENCH_HALF_LENGTH + 10f));
            }
            float jump = SetPieces.JumpArc(def, goalArc);
            if (jump >= 0f)
            {
                Vector3 j = WorldLayout.PointAtArc(course, jump);
                r.Add(new Vector4(j.x, 0f, j.z, 22f));
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
                    w.Add("Roadside", FARM, FARM_LAMP, w.Side(a, s * 15f), yaw, 1f, 0.05f, ROAD_MIN);
                    w.Add("Roadside", DROCKS, w.Pick(RockMid), w.Side(a + 6f, s * (17f + SizeOf(DROCKS, "DesertRocks_05").r)), w.Rand(0f, 360f), 1.1f, 0.3f, ROAD_MIN);
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
        static void RoadsideBand(Work w, float goalArc, System.Func<Work, string[]> pick)
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
                for (int tries = 0; tries < 120 && counts[k] < ROADSIDE_LARGE_PER_100M + 2; tries++)
                {
                    string[] ap = pick(w);
                    Size sz = SizeOf(ap[0], ap[1]);
                    float sc = ap[0] == TREES || ap[0] == ACACIA || ap[0] == DEADTREE ? w.Rand(TREE_SCALE_MIN, TREE_SCALE_MAX) : w.Rand(0.9f, 1.3f);
                    if (sz.h * sc < LARGE_MIN_H) continue;   // 帯には大きな飾りだけ
                    float r = sz.r * sc;
                    float lat = w.Rand(ROAD_CLEAR + r, Mathf.Max(ROAD_CLEAR + r + 1f, BAND_DIST_M - 5f));
                    float arc = w.Rand(a0 + 2f, a1 - 2f);
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
            // 谷の縁に沿った崖の壁（左右交互に 50m ごと）
            int ci = 0;
            for (float a = 40f; a < total; a += 50f)
            {
                int s = (ci % 2 == 0) ? 1 : -1;
                string part = CliffParts[ci++ % CliffParts.Length];
                float sc = w.Rand(1f, 1.4f);
                float r = SizeOf(CLIFFS, part).r * sc;
                w.Add("CliffWalls", CLIFFS, part, w.Side(a + w.Rand(-15f, 15f), s * (ROAD_CLEAR + r + w.Rand(4f, 22f))), w.Rand(0f, 360f), sc, 2f);
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
            // 道沿いの帯（大きな岩・枯れ木・中くらいの崖）
            RoadsideBand(w, goalArc, x =>
            {
                int k = x.rng.Next(10);
                if (k < 4) return new[] { DEADTREE, null };
                if (k < 7) return new[] { DROCKS, x.Pick(RockMid) };
                if (k < 9) return new[] { DPROPS, x.Pick(DpRocks) };
                return new[] { CLIFFS, x.Pick(new[] { "Cliffs_08", "Cliffs_10", "Cliffs_13" }) };
            });
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

        /// <summary>ゴールの町：流し（ゴール後の通り）の両脇に建物 3 棟ずつ（通りのほうを向く）、通りの先に牧場（入れば）。</summary>
        static void GoalTown(Work w, float goalArc)
        {
            string[] left = { TOWNBLOCK, SALOON, TOWNBLOCK };
            string[] right = { SALOON, TOWNBLOCK, TOWNBLOCK };
            for (int i = 0; i < 3; i++)
            {
                float a = goalArc - 30f + i * 22f;
                float yaw = w.YawAt(a);
                w.Add("GoalTown", left[i], null, w.Side(a, -22f), yaw + 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                w.Add("GoalTown", right[i], null, w.Side(a, 22f), yaw - 90f, 1f, 0.3f, ROAD_MIN, ignoreCircles: true);
                w.Add("GoalTown", FARM, FARM_LAMP, w.Side(a + 11f, i % 2 == 0 ? 14f : -14f), yaw, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add("GoalTown", FARM, w.Pick(FarmBarrels), w.Side(a + 6f, 15f), 0f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
                w.Add("GoalTown", FARM, w.Pick(FarmCrates), w.Side(a + 4f, -15f), 0f, 1f, 0.05f, ROAD_MIN, ignoreCircles: true);
            }
        }

        // ------------------------------------------------------------------
        // ロビー
        // ------------------------------------------------------------------

        /// <summary>
        /// ロビーの町のまわりの飾り（位置はロビー中心からの相対 m、地面 y = 0）。townMin/townMax は町の外形（x/z）、
        /// exclusions は広場の設備など置かない円（x, _, z, 半径）。町の外周 4〜8m 外を一周、9m ごとに小さな山（木箱・樽・干し草・薪・柵・草）。
        /// </summary>
        public static List<Placement> ForLobby(Vector2 townMin, Vector2 townMax, List<Vector4> exclusions, int seed)
        {
            var w = new Work
            {
                course = null, h = null, sizeM = 0f, budget = MAX_LOBBY,
                // ロビーの柵（LobbyLayout.FENCE_RECT）の 3m 内側
                minX = LobbyLayout.FENCE_RECT.x + 3f, maxX = LobbyLayout.FENCE_RECT.z - 3f,
                minZ = LobbyLayout.FENCE_RECT.y + 3f, maxZ = LobbyLayout.FENCE_RECT.w - 3f,
                rng = new System.Random(seed), hasRect = true,
                rectMin = new Vector2(townMin.x - 1f, townMin.y - 1f), rectMax = new Vector2(townMax.x + 1f, townMax.y + 1f),
            };
            if (exclusions != null) w.circles.AddRange(exclusions);
            float perim = 2f * ((townMax.x - townMin.x) + (townMax.y - townMin.y));
            int n = Mathf.FloorToInt(perim / 9f);
            for (int i = 0; i < n; i++)
            {
                float d = i * perim / n;
                Vector3 p; float yaw;
                PerimeterPoint(townMin, townMax, d, w.Rand(4f, 8f), out p, out yaw);
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
                    w.Add(G, FARM, w.Pick(FarmBarrels), w.Around(p, 2.5f), w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, FARM, w.Pick(FarmSmall), w.Around(p, 3f), w.Rand(0f, 360f), 1f, 0f);
                }
                else if (kind == 2)
                {
                    w.Add(G, HAY, "HayBales_01", p, w.Rand(0f, 360f), 1f, 0f);
                    w.Add(G, HAY, w.Pick(HayParts), w.Around(p, 3f), w.Rand(0f, 360f), 1f, 0f);
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
                    w.Add(G, CACTI, w.Pick(CactiParts), w.Around(p, 3f), w.Rand(0f, 360f), 0.8f, 0f, trunk: true);
                    w.Add(G, DPROPS, w.Pick(DpCacti), w.Around(p, 3f), w.Rand(0f, 360f), 1f, 0f, trunk: true);
                }
            }
            // 木の並木：町の外周の 13〜15m 外を 10m ごと、ロビーの東西の柵の内側（柵の 12m 内側）にも
            for (int i = 0; i < Mathf.FloorToInt(perim / 10f); i++)
            {
                Vector3 p; float yaw;
                PerimeterPoint(townMin, townMax, i * 10f, w.Rand(13f, 15f), out p, out yaw);
                if (i % 3 == 0) Tree(w, "TreeRows", ACACIA, null, p, 0f);
                else Tree(w, "TreeRows", TREES, TreePart(w), p, 0f);
            }
            for (float z = w.minZ + 10f; z < w.maxZ - 10f; z += 14f)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    bool ac = ((int)(z / 14f)) % 2 != 0;
                    Tree(w, "TreeRows", ac ? ACACIA : TREES, ac ? null : TreePart(w), new Vector3(s2 < 0 ? w.minX + 9f : w.maxX - 9f, 0f, z + w.Rand(-3f, 3f)), 0f);
                }
            // 町の横の空き地（並木と柵ぎわの列の間が 30m 以上ある側だけ）：真ん中に 18m ごとの疎らな列（練習場・広場は除外の円で避ける）
            for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                float avenue = s2 < 0 ? townMin.x - 15f : townMax.x + 15f, wall = s2 < 0 ? w.minX + 9f : w.maxX - 9f;
                if (Mathf.Abs(wall - avenue) < 30f) continue;
                float x = (avenue + wall) * 0.5f;
                for (float z = townMin.y; z < w.maxZ - 10f; z += 18f)
                    Tree(w, "TreeRows", TREES, TreePart(w), new Vector3(x + w.Rand(-4f, 4f), 0f, z + w.Rand(-3f, 3f)), 0f);
            }
            return w.list;
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

        static readonly string[] BackdropCliffs = { "Cliffs_01", "Cliffs_02", "Cliffs_03", "Cliffs_04", "Cliffs_05" };

        /// <summary>
        /// ワールド全体を囲む遠景の山並み（ワールド座標）。遊べる範囲の外接長方形の外側 BACKDROP_OFFSET_M m 以上に、
        /// BACKDROP_SPACING_M m ごとにメサ・崖を 4〜8 倍で置く（Collider なし、静的）。山の足元は遊べる範囲から BACKDROP_CLEAR_M m 以上離す。
        /// </summary>
        public static List<Placement> Backdrop(int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<Placement>();
            Vector4 u = WorldBounds.Union();
            float w = u.z - u.x, d = u.w - u.y;
            float perim = 2f * (w + d);
            int n = Mathf.FloorToInt(perim / WorldBounds.BACKDROP_SPACING_M);
            for (int i = 0; i < n; i++)
            {
                float t = (i + (float)rng.NextDouble() * 0.5f) * perim / n;
                int k = i % 3;
                string asset = k == 0 ? MESA : (k == 1 ? MESAB : CLIFFS);
                string part = k == 2 ? BackdropCliffs[rng.Next(BackdropCliffs.Length)] : null;
                float scale = 4f + (float)rng.NextDouble() * 4f;
                Size sz = SizeOf(asset, part);
                float r = sz.r * scale;
                float outM = Mathf.Max(WorldBounds.BACKDROP_OFFSET_M, r + WorldBounds.BACKDROP_CLEAR_M) + (float)rng.NextDouble() * 60f;
                // 外接長方形の周上の点 → 外向きの法線方向へ outM
                Vector3 p; Vector3 nrm;
                if (t < w) { p = new Vector3(u.x + t, 0f, u.y); nrm = new Vector3(0f, 0f, -1f); }
                else if (t < w + d) { p = new Vector3(u.z, 0f, u.y + (t - w)); nrm = new Vector3(1f, 0f, 0f); }
                else if (t < 2f * w + d) { p = new Vector3(u.z - (t - w - d), 0f, u.w); nrm = new Vector3(0f, 0f, 1f); }
                else { p = new Vector3(u.x, 0f, u.w - (t - 2f * w - d)); nrm = new Vector3(-1f, 0f, 0f); }
                p = p + nrm * outM;
                list.Add(new Placement
                {
                    group = "Backdrop", asset = asset, part = part, position = new Vector3(p.x, WorldBounds.FILLER_Y - 2f * scale, p.z),
                    yawDeg = (float)rng.NextDouble() * 360f, scale = scale, radius = r, height = sz.h * scale,
                    collider = false, shape = SHAPE_NONE, background = false,
                });
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
        public static readonly Vector3 LOBBY_CENTER = new Vector3(0f, 0f, -820f);
        /// <summary>ロビーの地面（Terrain）のワールド座標の長方形 = LOBBY_CENTER + LobbyLayout.GROUND_RECT（柵 + 38m）。</summary>
        public static Vector4 LobbyRect()
        {
            Vector4 g = LobbyLayout.GROUND_RECT;
            return new Vector4(LOBBY_CENTER.x + g.x, LOBBY_CENTER.z + g.y, LOBBY_CENTER.x + g.z, LOBBY_CENTER.z + g.w);
        }
        public const float BACKDROP_OFFSET_M = 320f;       // 遊べる範囲の外側、この距離から山並み
        public const float BACKDROP_SPACING_M = 170f;
        public const float BACKDROP_CLEAR_M = 80f;         // 山の足元と遊べる範囲の間はこれ以上空ける
        public const float FILLER_MARGIN_M = 800f;         // 地面の板は遊べる範囲よりこれだけ広く（山並みの外まで）
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
    }
}
