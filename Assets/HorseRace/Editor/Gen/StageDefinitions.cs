using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 3 ステージの定義（エディタ専用・純粋データ。UnityEditor に依存しないので単体テストできる）。
    /// 座標はすべて「Terrain ローカル（m）」：Terrain の角 = (0,0,0)、x/z は 0..1000。
    /// ワールド座標 = Origin + ローカル。Terrain の中心がステージの中心 (-1200,0,0)/(0,0,0)/(1200,0,0)。
    /// 制御点の最後の runoutM はゴール後の流し（ゴール後の囲いを置く平らな道。ゴールの町もここに並ぶ）で、
    /// 公式のコース長（スタート〜ゴール）には含めない。
    /// 徒歩用の近道は Phase 2。
    /// </summary>
    public sealed class StageDef
    {
        public int index;
        public string label;
        public int kind;
        public int seed;
        public Vector3 center;
        public Vector3[] controlLocal;
        public float runoutM;
        /// <summary>
        /// 道の高さの形（course-r1-03/04）：弧長 elevArc[k]（m、スタートから）での高さ elevM[k]（m、ステージの基準の高さから）。間は直線補間、
        /// 範囲の外は端の値。HeightmapGen.PathHeight がこれに小さなうねりを足す。最後の下り（DASH_DROP）は ゴールの 350m 手前で終わる。
        /// </summary>
        public float[] elevArc, elevM;
        /// <summary>
        /// 道幅（course-r1-10）：区間 [widthArc[2k], widthArc[2k+1]]（m）の平らな道の半幅 halfWidthM[k]（m）。区間の外は RUNWAY_M（12m）。
        /// 7m = 絞り（3〜4 頭）、18m = 追い抜きの広場。区間の端は WIDTH_BLEND_M でなめらかに変わる。
        /// </summary>
        public float[] widthArc, halfWidthM;
        /// <summary>見どころ（spec 5.5）。負の値 = なし。割合は公式コース長に対して（実際は CP の中間に寄せる）。</summary>
        public float bridgeFraction = -1f;     // 荒野：溝の上に残した地形の背（岩の橋）と、溝の両端を回る遠回り
        public bool hasShortcut;               // 近道（CP の連鎖には入らない枝道、途中に 6m の段）
        public float shortcutFrom = 0f, shortcutTo = 1f;   // 近道を探す範囲（公式コース長に対する割合）
        public float jumpFraction = -1f;       // 砂漠：地形の踏み切り（+5m の坂と 6m の段、その先に砂の窪み）
        public float passFraction = -1f;       // 境目：メサの切り通し（道を下げて両脇の地形を盛り、メサの門で挟む）
        public float mesaClimbFraction = -1f;  // 境目：台地の上（mesaClimbFraction〜mesaTopEndFraction、この間は道幅 70m の帯ごと持ち上がる）
        public float mesaTopEndFraction = -1f;
        /// <summary>
        /// 深い砂（system-r2-01）：区間 [sandTrailArcs[2k], sandTrailArcs[2k+1]]（m）は道の上も砂（Terrain の砂の塗り・SandZone）。
        /// ここだけ馬は ×MountMath.HORSE_SAND_FACTOR、ラクダが速い。区間の入口に風で削れた岩・砂の吹きだまり・「深い砂」の札。null = なし。
        /// </summary>
        public float[] sandTrailArcs;
        /// <summary>
        /// ゴール前の形（course-r2-07）：全力の合図（DashArc）から dashLenM m で dashDropM m 下り、その先 dashEndBeforeGoalM m でゴール。
        /// finishKind はステージごとの締めくくり（FINISH_GORGE = 峡谷の 2 段の下りから広い平地、FINISH_CREST = 下りの後の見えない丘、FINISH_TOWN = 台地から下りて町の通りの曲がり）。
        /// </summary>
        public float dashLenM = 240f, dashDropM = 22f, dashEndBeforeGoalM = 350f;
        public int finishKind;
        /// <summary>序盤の見どころ（course-r2-04）の中心の弧長の割合（公式コース長に対して）。負 = なし。</summary>
        public float openingFraction = -1f;
        /// <summary>同じ長さの二股（course-r2-13）の中心の弧長の割合。負 = なし。</summary>
        public float forkFraction = -1f;

        public Vector3 Origin => new Vector3(center.x - StageDefinitions.HALF, center.y, center.z - StageDefinitions.HALF);
    }

    public static class StageDefinitions
    {
        public const float SIZE = HeightmapGen.TERRAIN_SIZE_M;   // 1000
        public const float HALF = SIZE * 0.5f;
        public const int HEIGHTMAP_RES = 513;
        public const int ALPHAMAP_RES = 512;
        /// <summary>コースのサンプリング間隔（m）。地形の平らな道・CP・検証すべてこの点列を使う。</summary>
        public const float SAMPLE_SPACING_M = 2f;
        public const float CP_SPACING_M = 80f;
        public const int LANES = 8;
        public const float LANE_SPACING_M = 2.6f;
        public const float START_ARC_M = 6f;        // スタート位置（8 頭の横一列）の弧長
        public const float MIN_COURSE_M = 2200f, MAX_COURSE_M = 2800f;
        public const float MAX_SLOPE_DEG = 25f;
        public const int RELICS_PER_STAGE = 4;
        // 道幅
        public const float CHOKE_HALF_M = 7f, WIDE_HALF_M = 18f, WIDTH_BLEND_M = 15f;
        /// <summary>スタートの広い区間（course-r3-09）：弧長 0〜START_OPEN_M は半幅 START_HALF_M（8 頭 × 2.6m = 18.2m が余裕で広がる）、高さもほぼ平ら。</summary>
        public const float START_HALF_M = 16f, START_OPEN_M = 200f, START_OPEN_MIN_HALF_M = 14f, START_OPEN_MAX_RISE_M = 2f;
        // ゴール前の締めくくり（course-r2-07、StageDef.finishKind）
        public const int FINISH_GORGE = 0, FINISH_CREST = 1, FINISH_TOWN = 2;
        // コースの外周からの距離（course-r2-10）：最後の 120m（ゴールの先の切れ込みへ向かう所）以外は 180m 以上内側
        public const float RIM_KEEP_M = 180f, RIM_KEEP_EXEMPT_M = 120f;
        // 直線の上限（course-r2-03）：半径 250m より大きい区間が 320m を超えない（全力の合図の後は除く）
        public const float STRAIGHT_R_M = 250f, MAX_STRAIGHT_M = 320f;

        static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        public static readonly StageDef[] All =
        {
            new StageDef
            {
                index = 0, label = "荒野", kind = HeightmapGen.KIND_CANYON, seed = 11,
                center = new Vector3(-1200f, 0f, 0f),
                // 峡谷を下る短い技巧のコース（course-r2-01/03/10/12、約 2210m）：北の縁の内側を西へ、牧場の庭（序盤の見どころ）を抜けて S 字、
                // 急カーブ 1（左）・2（左）で谷を下り、東へ戻ってヘアピン（右 176°、半径 18m：疾走のままでは曲がれない）、南西へ下って急カーブ 4（左）、
                // 南の 200m の線を東へ。全力の合図から峡谷の 2 段の下り（24m）を抜けて広い平地 255m、ゴールの先の尾根の切れ込みから東の砂漠が見える
                controlLocal = new[]
                {
                    P(808.0f, 818.0f), P(772.9f, 818.0f), P(737.7f, 818.0f), P(702.6f, 818.0f), P(667.4f, 818.0f), P(632.3f, 818.0f), P(597.1f, 818.0f), P(562.0f, 818.0f),
                    P(552.0f, 817.2f), P(542.3f, 814.7f), P(533.1f, 810.6f), P(524.7f, 805.0f), P(517.4f, 798.1f), P(500.7f, 779.6f), P(493.4f, 772.7f), P(485.0f, 767.1f),
                    P(475.8f, 763.1f), P(466.1f, 760.6f), P(456.1f, 759.7f), P(422.1f, 759.7f), P(388.1f, 759.7f), P(354.1f, 759.7f), P(320.1f, 759.7f), P(286.1f, 759.7f),
                    P(280.2f, 759.2f), P(274.5f, 757.7f), P(269.1f, 755.2f), P(264.2f, 751.8f), P(260.0f, 747.6f), P(256.6f, 742.7f), P(254.1f, 737.3f), P(252.6f, 731.6f),
                    P(252.1f, 725.7f), P(252.1f, 695.7f), P(250.9f, 677.8f), P(247.5f, 660.3f), P(241.9f, 643.3f), P(234.4f, 624.7f), P(228.8f, 607.7f), P(225.4f, 590.2f),
                    P(224.2f, 572.3f), P(224.2f, 562.3f), P(224.7f, 556.2f), P(226.3f, 550.3f), P(228.9f, 544.8f), P(232.4f, 539.8f), P(236.7f, 535.5f), P(241.7f, 532.0f),
                    P(247.2f, 529.4f), P(253.1f, 527.8f), P(259.2f, 527.3f), P(289.2f, 527.3f), P(319.2f, 527.3f), P(340.1f, 528.7f), P(360.6f, 533.1f), P(380.2f, 540.3f),
                    P(416.8f, 556.5f), P(436.4f, 563.7f), P(456.9f, 568.0f), P(477.8f, 569.5f), P(511.9f, 569.5f), P(546.1f, 569.5f), P(580.3f, 569.5f), P(614.4f, 569.5f),
                    P(648.6f, 569.5f), P(682.8f, 569.5f), P(685.8f, 569.2f), P(688.8f, 568.5f), P(691.6f, 567.2f), P(694.1f, 565.5f), P(696.3f, 563.3f), P(698.2f, 560.8f),
                    P(699.5f, 558.1f), P(700.4f, 555.2f), P(700.8f, 552.1f), P(700.6f, 549.1f), P(699.9f, 546.1f), P(698.8f, 543.2f), P(697.1f, 540.6f), P(695.1f, 538.3f),
                    P(692.7f, 536.5f), P(690.0f, 535.0f), P(687.1f, 534.0f), P(684.0f, 533.5f), P(669.1f, 532.5f), P(651.6f, 529.8f), P(634.8f, 524.5f), P(619.1f, 516.4f),
                    P(604.9f, 506.0f), P(592.5f, 493.5f), P(570.0f, 466.6f), P(547.5f, 439.8f), P(525.0f, 413.0f), P(502.5f, 386.2f), P(493.6f, 376.6f), P(484.0f, 367.7f),
                    P(461.0f, 348.4f), P(438.0f, 329.2f), P(423.2f, 314.4f), P(411.2f, 297.2f), P(402.4f, 278.3f), P(397.0f, 258.1f), P(395.2f, 237.2f), P(395.2f, 227.2f),
                    P(395.7f, 221.0f), P(397.3f, 214.9f), P(400.0f, 209.2f), P(403.6f, 204.1f), P(408.0f, 199.7f), P(413.2f, 196.1f), P(418.9f, 193.4f), P(424.9f, 191.8f),
                    P(431.2f, 191.2f), P(471.2f, 191.2f), P(496.7f, 192.9f), P(521.8f, 197.8f), P(546.1f, 205.8f), P(583.2f, 220.8f), P(607.5f, 228.8f), P(632.6f, 233.7f),
                    P(658.1f, 235.3f), P(693.1f, 235.3f), P(728.1f, 235.3f), P(763.1f, 235.3f), P(798.1f, 235.3f), P(833.1f, 235.3f), P(868.1f, 235.3f), P(903.1f, 235.3f),
                },
                runoutM = 70f,
                // 3 段の下り（40m）：急カーブ 1 の手前、北の谷の後、最後の 2 段（全力の合図から 150m で 24m、9°）
                elevArc = new[] { 0f, 250f, 380f, 900f, 960f, 1808f, 1868f, 1898f, 1958f },
                elevM = new[] { 40f, 40f, 30f, 30f, 24f, 24f, 12f, 12f, 0f },
                // スタートの 200m は半幅 16m（course-r3-09：8 頭が横に広がる）。絞り：牧場の門（230〜300、S 字の入口）・岩の橋（1360〜1440）・峡谷の出口（全力の合図から 70m）。
                // ヘアピンの頂点 ±25m は半幅 9m。広場：急カーブ 1 の後・最後の平地
                widthArc = new[] { 0f, 200f, 230f, 300f, 1360f, 1440f, 1808f, 1878f, 1251f, 1301f, 610f, 710f, 1980f, 2150f },
                halfWidthM = new[] { START_HALF_M, 7f, 7f, 7f, 9f, 18f, 18f },
                bridgeFraction = 1400f / 2213f,
                hasShortcut = true, shortcutFrom = 0.22f, shortcutTo = 0.5f,
                dashLenM = 150f, dashDropM = 24f, dashEndBeforeGoalM = 255f, finishKind = FINISH_GORGE,
                openingFraction = 265f / 2213f,   // 牧場の門（course-r3-09：スタートの広い 200m の先へ）
                forkFraction = 660f / 2213f,     // 急カーブ 1 の後の広場
            },
            new StageDef
            {
                index = 1, label = "砂漠", kind = HeightmapGen.KIND_DUNES, seed = 23,
                center = new Vector3(0f, 0f, 0f),
                // 長く速いコース（course-r2-03/10/12、約 2590m）：南の線を西へ（序盤の廃線の踏切：固い路盤の細い線と深い砂）、西の線を北へ、
                // 北の線を東へ（シケインと踏み切り）、北東でヘアピン（右 176°）、南西へ下って中くらいの U ターン（左）、東へ。
                // 稜線を 6 回越え、全力の合図から 12m 下って、ゴールの 180m 手前の見えない丘（8m）を越えるとゴールの門
                controlLocal = new[]
                {
                    P(805.0f, 215.0f), P(765.0f, 215.0f), P(744.1f, 213.5f), P(723.7f, 209.2f), P(694.8f, 200.9f), P(674.3f, 196.6f), P(653.5f, 195.1f), P(615.5f, 195.1f),
                    P(577.5f, 195.1f), P(539.5f, 195.1f), P(501.5f, 195.1f), P(463.5f, 195.1f), P(441.2f, 196.7f), P(419.4f, 201.3f), P(390.5f, 209.6f), P(368.7f, 214.2f),
                    P(346.4f, 215.8f), P(306.4f, 215.8f), P(299.5f, 216.4f), P(292.7f, 218.2f), P(286.4f, 221.1f), P(280.7f, 225.1f), P(275.8f, 230.1f), P(271.8f, 235.8f),
                    P(268.8f, 242.1f), P(267.0f, 248.8f), P(266.4f, 255.8f), P(266.4f, 285.8f), P(266.4f, 315.8f), P(265.1f, 335.3f), P(261.0f, 354.4f), P(254.3f, 372.7f),
                    P(238.1f, 409.3f), P(231.4f, 427.6f), P(227.3f, 446.7f), P(226.0f, 466.2f), P(226.0f, 503.7f), P(226.0f, 541.2f), P(227.3f, 560.7f), P(231.4f, 579.8f),
                    P(238.1f, 598.1f), P(250.3f, 625.6f), P(256.9f, 643.9f), P(261.0f, 663.0f), P(262.4f, 682.5f), P(262.4f, 712.5f), P(262.9f, 719.1f), P(264.7f, 725.5f),
                    P(267.5f, 731.5f), P(271.3f, 736.9f), P(275.9f, 741.6f), P(281.4f, 745.4f), P(287.4f, 748.2f), P(293.8f, 749.9f), P(300.4f, 750.5f), P(330.4f, 750.5f),
                    P(360.4f, 750.5f), P(370.8f, 751.4f), P(380.9f, 754.1f), P(390.4f, 758.5f), P(398.9f, 764.5f), P(406.3f, 771.9f), P(419.2f, 787.2f), P(426.6f, 794.6f),
                    P(435.1f, 800.6f), P(444.6f, 805.1f), P(454.7f, 807.8f), P(465.1f, 808.7f), P(505.1f, 808.7f), P(524.3f, 807.5f), P(543.1f, 803.8f), P(561.3f, 797.8f),
                    P(579.9f, 790.3f), P(598.1f, 784.2f), P(616.9f, 780.6f), P(636.1f, 779.3f), P(673.6f, 779.3f), P(711.1f, 779.3f), P(748.6f, 779.3f), P(786.1f, 779.3f),
                    P(789.1f, 779.1f), P(792.1f, 778.3f), P(794.9f, 777.0f), P(797.4f, 775.3f), P(799.6f, 773.2f), P(801.4f, 770.7f), P(802.8f, 768.0f), P(803.7f, 765.0f),
                    P(804.1f, 762.0f), P(803.9f, 758.9f), P(803.2f, 755.9f), P(802.1f, 753.1f), P(800.4f, 750.5f), P(798.4f, 748.2f), P(796.0f, 746.3f), P(793.3f, 744.8f),
                    P(790.4f, 743.9f), P(787.3f, 743.4f), P(772.4f, 742.3f), P(753.4f, 739.3f), P(735.3f, 733.1f), P(718.5f, 723.8f), P(691.6f, 705.6f), P(664.6f, 687.5f),
                    P(637.7f, 669.3f), P(610.7f, 651.1f), P(593.2f, 637.4f), P(577.7f, 621.3f), P(564.5f, 603.3f), P(543.3f, 569.3f), P(530.2f, 551.3f), P(514.7f, 535.2f),
                    P(497.1f, 521.5f), P(472.2f, 504.7f), P(447.4f, 487.9f), P(439.0f, 481.1f), P(431.9f, 473.0f), P(426.3f, 463.7f), P(422.4f, 453.6f), P(420.4f, 443.0f),
                    P(420.2f, 432.2f), P(421.9f, 421.5f), P(425.4f, 411.3f), P(430.6f, 401.8f), P(437.4f, 393.4f), P(445.6f, 386.4f), P(454.9f, 380.8f), P(465.0f, 376.9f),
                    P(475.6f, 374.9f), P(486.4f, 374.7f), P(511.3f, 376.4f), P(536.2f, 378.2f), P(563.9f, 382.0f), P(590.7f, 389.7f), P(618.9f, 400.0f), P(647.1f, 410.3f),
                    P(673.9f, 417.9f), P(701.5f, 421.8f), P(737.4f, 424.3f), P(773.4f, 426.8f), P(809.3f, 429.4f), P(845.2f, 431.9f), P(881.1f, 434.4f),
                },
                runoutM = 70f,
                // 稜線 6 つ（10〜13m、上り・下り 60〜80m）。U ターンは 12m の砂丘の上、全力の合図から 100m で下り、ゴールの 180m 手前に 8m の丘
                // course-r3-09：最初の稜線はスタートの広い 200m の先（230〜390、2 つ目の稜線 600〜720 はそのまま：上りを 12° 以下に保つ）
                elevArc = new[] { 0f, 230f, 310f, 390f, 600f, 660f, 720f, 780f, 840f, 900f, 960f, 1720f, 1800f, 1880f, 1900f, 1980f, 2040f, 2104f, 2258f, 2358f, 2365f, 2411f, 2471f },
                elevM = new[] { 0f, 0f, 11f, 0f, 0f, 11f, 0f, 12f, 0f, 12f, 0f, 0f, 12f, 0f, 0f, 11f, 0f, 12f, 12f, 0f, 0f, 8f, 0f },
                // 絞り：踏み切りの手前（1324〜1400）と U ターンの砂丘へ上る坂（2040〜2104）。ヘアピンの頂点 ±25m は半幅 9m。広場：シケイン・最後の下り
                widthArc = new[] { 0f, 200f, 1324f, 1400f, 2040f, 2104f, 1627f, 1677f, 1150f, 1290f, 2258f, 2358f },
                halfWidthM = new[] { START_HALF_M, 7f, 7f, 9f, 18f, 18f },
                jumpFraction = 1400f / 2591f,
                sandTrailArcs = new[] { 150f, 350f, 600f, 960f, 1700f, 1950f },   // 踏切の線（固いのは路盤だけ）、西の稜線越え、ヘアピンの後の南西へ下る線
                hasShortcut = true, shortcutFrom = 0.27f, shortcutTo = 0.47f,
                dashLenM = 100f, dashDropM = 12f, dashEndBeforeGoalM = 233f, finishKind = FINISH_CREST,
                openingFraction = 250f / 2591f,
                forkFraction = 1220f / 2591f,
            },
            new StageDef
            {
                index = 2, label = "境目", kind = HeightmapGen.KIND_MESA, seed = 37,
                center = new Vector3(1200f, 0f, 0f),
                // 最後のステージ（course-r2-05/10/12、約 2360m）：南東の町の通りから北へ出て（両脇の建物、2 つの崖の門）、北の線を西へ、
                // 西で南へ（メサの切り通し）、東へ 7° の坂で 32m の台地に登り、台地の上でヘアピン（右 176°）、南西へ台地から下りて
                // 町の大通り：ゴールの 150m 手前で右へ 70° 曲がり、南西（来た台地とメサの方）を向いてゴール
                controlLocal = new[]
                {
                    P(800.0f, 205.0f), P(800.0f, 240.7f), P(800.0f, 276.4f), P(800.0f, 312.1f), P(800.0f, 347.9f), P(800.0f, 383.6f), P(800.0f, 419.3f), P(800.0f, 455.0f),
                    P(798.2f, 478.5f), P(792.7f, 501.4f), P(780.3f, 539.4f), P(774.8f, 562.3f), P(773.0f, 585.7f), P(773.0f, 625.7f), P(773.0f, 665.7f), P(773.0f, 705.7f),
                    P(773.0f, 745.7f), P(772.4f, 752.0f), P(770.8f, 758.1f), P(768.1f, 763.7f), P(764.5f, 768.9f), P(760.1f, 773.3f), P(755.0f, 776.9f), P(749.3f, 779.6f),
                    P(743.2f, 781.2f), P(737.0f, 781.7f), P(707.0f, 781.7f), P(677.0f, 781.7f), P(666.5f, 780.8f), P(656.4f, 778.1f), P(647.0f, 773.7f), P(638.4f, 767.7f),
                    P(631.0f, 760.3f), P(618.1f, 745.0f), P(610.7f, 737.6f), P(602.2f, 731.6f), P(592.7f, 727.2f), P(582.6f, 724.5f), P(572.2f, 723.6f), P(538.8f, 723.6f),
                    P(505.5f, 723.6f), P(472.2f, 723.6f), P(452.6f, 725.0f), P(433.4f, 729.5f), P(415.2f, 736.7f), P(379.2f, 754.3f), P(361.0f, 761.5f), P(341.8f, 765.9f),
                    P(322.2f, 767.4f), P(282.2f, 767.4f), P(242.2f, 767.4f), P(236.0f, 766.9f), P(229.9f, 765.2f), P(224.2f, 762.6f), P(219.1f, 759.0f), P(214.7f, 754.5f),
                    P(211.1f, 749.4f), P(208.4f, 743.7f), P(206.8f, 737.7f), P(206.2f, 731.4f), P(206.2f, 691.4f), P(207.5f, 671.8f), P(211.4f, 652.6f), P(219.1f, 623.6f),
                    P(223.0f, 604.4f), P(224.2f, 584.8f), P(224.2f, 559.8f), P(224.8f, 553.5f), P(226.4f, 547.5f), P(229.1f, 541.8f), P(232.7f, 536.6f), P(237.1f, 532.2f),
                    P(242.2f, 528.6f), P(247.9f, 526.0f), P(254.0f, 524.3f), P(260.2f, 523.8f), P(300.2f, 523.8f), P(340.2f, 523.8f), P(380.2f, 523.8f), P(420.2f, 523.8f),
                    P(460.2f, 523.8f), P(479.7f, 525.1f), P(498.8f, 529.2f), P(517.2f, 535.9f), P(535.4f, 544.0f), P(553.8f, 550.7f), P(572.9f, 554.8f), P(592.4f, 556.1f),
                    P(602.4f, 556.1f), P(605.4f, 555.9f), P(608.4f, 555.1f), P(611.2f, 553.8f), P(613.7f, 552.1f), P(616.0f, 550.0f), P(617.8f, 547.5f), P(619.1f, 544.7f),
                    P(620.0f, 541.8f), P(620.4f, 538.8f), P(620.2f, 535.7f), P(619.6f, 532.7f), P(618.4f, 529.9f), P(616.7f, 527.3f), P(614.7f, 525.0f), P(612.3f, 523.1f),
                    P(609.6f, 521.6f), P(606.7f, 520.6f), P(603.6f, 520.2f), P(588.7f, 519.1f), P(571.6f, 516.6f), P(555.1f, 511.4f), P(539.7f, 503.7f), P(525.6f, 493.7f),
                    P(513.3f, 481.6f), P(503.1f, 467.7f), P(487.2f, 442.2f), P(471.3f, 416.8f), P(455.4f, 391.4f), P(448.8f, 379.0f), P(444.1f, 365.9f), P(441.2f, 352.3f),
                    P(440.2f, 338.4f), P(440.2f, 308.4f), P(440.2f, 278.4f), P(439.3f, 267.9f), P(436.6f, 257.8f), P(432.1f, 248.4f), P(426.1f, 239.8f), P(418.8f, 232.4f),
                    P(410.2f, 226.4f), P(400.7f, 222.0f), P(365.8f, 209.3f), P(330.9f, 196.6f), P(296.0f, 183.9f), P(261.1f, 171.2f), P(226.2f, 158.5f), P(191.3f, 145.8f),
                    P(156.4f, 133.1f),
                },
                runoutM = 110f,
                // course-r3-10：最初のカーブの後に 6m の段（250〜600）、切り通しが割る 8m の高み（760〜1240、切り通しは 1000）、
                // 登り 1420〜1680（32m、7°）、台地 〜1840、台地の縁の 1 段目（10m）、全力の合図から 200m で 22m 下りて町へ。どの坂も 7° 以下
                elevArc = new[] { 0f, 250f, 330f, 520f, 600f, 760f, 960f, 1040f, 1240f, 1420f, 1680f, 1840f, 1900f, 1915f, 2115f },
                elevM = new[] { 0f, 0f, 6f, 6f, 0f, 0f, 8f, 8f, 0f, 0f, 32f, 32f, 22f, 22f, 0f },
                // 絞り：台地の上の鞍部（1675〜1735）と台地の縁の門（1840〜1905）。ヘアピンの頂点 ±25m は半幅 9m。広場：シケイン・町の大通りの曲がり
                widthArc = new[] { 0f, 200f, 1675f, 1735f, 1840f, 1905f, 1749f, 1799f, 640f, 760f, 2120f, 2230f },
                halfWidthM = new[] { START_HALF_M, 7f, 7f, 9f, 18f, 18f },
                passFraction = 1000f / 2360f,   // 台地へ登る手前のメサの門（切り通し）
                mesaClimbFraction = 1680f / 2360f, mesaTopEndFraction = 1840f / 2360f,
                sandTrailArcs = new[] { 740f, 950f },   // 北の線の吹きだまり（シケインの後〜切り通しの手前）
                hasShortcut = true, shortcutFrom = 0.5f, shortcutTo = 0.67f,
                dashLenM = 200f, dashDropM = 22f, dashEndBeforeGoalM = 245f, finishKind = FINISH_TOWN,
                openingFraction = 125f / 2360f,
                forkFraction = 700f / 2360f,     // シケインの広場
            },
        };

        /// <summary>道の高さの形（m、ステージの基準から）。キーが無ければ 0。</summary>
        public static float ElevationAt(StageDef s, float arc) => Keyed(s.elevArc, s.elevM, arc);

        public static float Keyed(float[] keys, float[] vals, float x)
        {
            if (keys == null || vals == null || keys.Length == 0) return 0f;
            if (x <= keys[0]) return vals[0];
            for (int k = 1; k < keys.Length; k++)
                if (x <= keys[k])
                {
                    float span = keys[k] - keys[k - 1];
                    return span <= 1e-4f ? vals[k] : Mathf.Lerp(vals[k - 1], vals[k], (x - keys[k - 1]) / span);
                }
            return vals[vals.Length - 1];
        }

        /// <summary>平らな道の半幅（m）。区間の中は halfWidthM、区間の端から WIDTH_BLEND_M で RUNWAY_M へ戻る。</summary>
        public static float HalfWidthAt(StageDef s, float arc)
        {
            float hw = HeightmapGen.RUNWAY_M;
            if (s == null || s.widthArc == null || s.halfWidthM == null) return hw;
            float best = hw, bestW = 0f;
            for (int k = 0; k < s.halfWidthM.Length && 2 * k + 1 < s.widthArc.Length; k++)
            {
                float a0 = s.widthArc[2 * k], a1 = s.widthArc[2 * k + 1];
                float d = arc < a0 ? a0 - arc : (arc > a1 ? arc - a1 : 0f);
                float w = 1f - Mathf.Clamp01(d / WIDTH_BLEND_M);
                if (w > bestW) { bestW = w; best = Mathf.Lerp(HeightmapGen.RUNWAY_M, s.halfWidthM[k], w); }
            }
            return best;
        }

        /// <summary>弧長 arc が深い砂の区間の中か（system-r2-01）。</summary>
        public static bool InDeepSand(StageDef s, float arc)
        {
            if (s == null || s.sandTrailArcs == null) return false;
            for (int k = 0; 2 * k + 1 < s.sandTrailArcs.Length; k++)
                if (arc >= s.sandTrailArcs[2 * k] && arc <= s.sandTrailArcs[2 * k + 1]) return true;
            return false;
        }

        /// <summary>深い砂の区間の合計がスタートからゴールまでに占める割合（0..1）。</summary>
        public static float DeepSandShare(StageDef s, float goalArc)
        {
            if (s == null || s.sandTrailArcs == null || goalArc <= 0f) return 0f;
            float sum = 0f;
            for (int k = 0; 2 * k + 1 < s.sandTrailArcs.Length; k++)
                sum += Mathf.Max(0f, Mathf.Min(s.sandTrailArcs[2 * k + 1], goalArc) - Mathf.Max(s.sandTrailArcs[2 * k], 0f));
            return Mathf.Clamp01(sum / goalArc);
        }

        /// <summary>最後の下りの始まり（全力の合図の位置）の弧長（course-r2-07：ステージごと）。</summary>
        public static float DashArc(StageDef s, float goalArc) => goalArc - s.dashEndBeforeGoalM - s.dashLenM;

        /// <summary>最後の下りの終わり（ここからゴールまで平ら、または丘）の弧長。</summary>
        public static float DashEndArc(StageDef s, float goalArc) => goalArc - s.dashEndBeforeGoalM;

        /// <summary>Terrain ローカル（m）の等間隔サンプル（流しを含む）。</summary>
        public static Vector3[] SampleLocal(StageDef s) => CoursePath.Sample(s.controlLocal, SAMPLE_SPACING_M);

        /// <summary>HeightmapGen 用に 0..1 へ正規化（x/z）。</summary>
        public static Vector3[] Normalize(Vector3[] local)
        {
            var r = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) r[i] = new Vector3(local[i].x / SIZE, 0f, local[i].z / SIZE);
            return r;
        }

        /// <summary>公式のコース長（スタート〜ゴール、m）。</summary>
        public static float GoalArc(StageDef s, Vector3[] sampledLocal) => CoursePath.Length(sampledLocal) - s.runoutM;
        // ------------------------------------------------------------------
        // コースの形の検査（course-r1-01/02）
        // ------------------------------------------------------------------

        /// <summary>カーブ半径を測る窓の半分（サンプル数）。2m 間隔で ±3 点 = 12m の窓（約 10m）。</summary>
        public const int CORNER_WINDOW_HALF = 3;
        public const float TIGHT_CORNER_MAX_R = 45f;      // これ未満 = 急カーブ（駈歩に落とすか外へふくらむ）
        public const float TIGHT_CORNER_MIN_R = 16f;      // これ未満は無し（ヘアピンは 18m 前後：疾走のままでは曲がれない。course-r2-01）
        public const float HAIRPIN_MIN_TURN_DEG = 150f;   // 急カーブのうち、これ以上向きを変えるものがヘアピン
        public const float MEDIUM_CORNER_MAX_R = 75f;     // 45〜75m = 中くらいのカーブ
        public const int MIN_TIGHT_CORNERS = 3;
        public const float MIN_STAGE_SEPARATION_M = 120f; // 2 つのステージの形の平均の差

        /// <summary>サンプル i の前後 half 点を通る円の半径（m、まっすぐなら float.MaxValue）。</summary>
        public static float RadiusAt(Vector3[] pts, int i, int half = CORNER_WINDOW_HALF)
        {
            if (pts == null || i - half < 0 || i + half >= pts.Length) return float.MaxValue;
            Vector3 a = pts[i - half], b = pts[i], c = pts[i + half];
            float ab = WorldLayout.DistXZ(a, b), bc = WorldLayout.DistXZ(b, c), ca = WorldLayout.DistXZ(c, a);
            float cross = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x));
            if (cross < 1e-5f) return float.MaxValue;
            return ab * bc * ca / (2f * cross);
        }

        /// <summary>点列（スタート〜ゴール、2m 間隔）の最小のカーブ半径（m）。</summary>
        public static float MinCornerRadius(Vector3[] sampled) => MinCornerRadius(sampled, 0f, float.MaxValue);

        public static float MinCornerRadius(Vector3[] sampled, float fromArc, float toArc)
        {
            float best = float.MaxValue;
            for (int i = 0; i < sampled.Length; i++)
            {
                float a = i * SAMPLE_SPACING_M;
                if (a < fromArc || a > toArc) continue;
                float r = RadiusAt(sampled, i);
                if (r < best) best = r;
            }
            return best;
        }

        /// <summary>カーブ 1 つ（半径が maxR 未満の連続した区間）：頂点の弧長・最小半径・曲がる向き（+1 = 右、-1 = 左）・入口と出口の弧長。</summary>
        public struct Corner
        {
            public float apexArc, radius, fromArc, toArc;
            public int side;
        }

        /// <summary>半径 maxR 未満の区間をカーブとしてまとめる（gapM 以内の切れ目はつなぐ）。toArc より先は見ない。</summary>
        public static System.Collections.Generic.List<Corner> Corners(Vector3[] sampled, float maxR, float toArc, float gapM = 16f)
        {
            var list = new System.Collections.Generic.List<Corner>();
            bool open = false;
            Corner cur = new Corner();
            float lastIn = -1000f;
            for (int i = 0; i < sampled.Length; i++)
            {
                float a = i * SAMPLE_SPACING_M;
                if (a > toArc) break;
                float r = RadiusAt(sampled, i);
                if (r < maxR)
                {
                    if (!open || a - lastIn > gapM)
                    {
                        if (open) list.Add(cur);
                        cur = new Corner { apexArc = a, radius = r, fromArc = a, toArc = a, side = TurnSide(sampled, i) };
                        open = true;
                    }
                    cur.toArc = a;
                    if (r < cur.radius) { cur.radius = r; cur.apexArc = a; cur.side = TurnSide(sampled, i); }
                    lastIn = a;
                }
            }
            if (open) list.Add(cur);
            return list;
        }

        /// <summary>サンプル i で右へ曲がっていれば +1、左なら -1（y 上・x 右・z 前）。</summary>
        public static int TurnSide(Vector3[] pts, int i, int half = CORNER_WINDOW_HALF)
        {
            int i0 = Mathf.Max(0, i - half), i1 = Mathf.Min(pts.Length - 1, i + half);
            Vector3 a = pts[i0], b = pts[i], c = pts[i1];
            float cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
            return cross < 0f ? 1 : -1;
        }

        /// <summary>急カーブ（TIGHT_CORNER_MAX_R 未満）の一覧（スタート〜ゴール）。</summary>
        public static System.Collections.Generic.List<Corner> TightCorners(StageDef s, Vector3[] sampled)
            => Corners(sampled, TIGHT_CORNER_MAX_R, GoalArc(s, sampled));

        /// <summary>カーブ c の前後 padM m を含めた向きの変化（度、+ = 右）。</summary>
        public static float TurnDeg(Vector3[] sampled, Corner c, float padM = 20f)
        {
            int i0 = Mathf.Clamp(Mathf.FloorToInt((c.fromArc - padM) / SAMPLE_SPACING_M), 2, sampled.Length - 3);
            int i1 = Mathf.Clamp(Mathf.CeilToInt((c.toArc + padM) / SAMPLE_SPACING_M), 2, sampled.Length - 3);
            float total = 0f, prev = HeadingDeg(sampled, i0);
            for (int i = i0 + 1; i <= i1; i++)
            {
                float h = HeadingDeg(sampled, i);
                total += Mathf.DeltaAngle(prev, h);
                prev = h;
            }
            return total;
        }

        static float HeadingDeg(Vector3[] p, int i)
        {
            Vector3 a = p[Mathf.Max(0, i - 2)], b = p[Mathf.Min(p.Length - 1, i + 2)];
            return Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
        }

        /// <summary>ヘアピン（急カーブのうち |向きの変化| ≥ HAIRPIN_MIN_TURN_DEG）の一覧（course-r2-01）。</summary>
        public static System.Collections.Generic.List<Corner> Hairpins(StageDef s, Vector3[] sampled)
        {
            var r = new System.Collections.Generic.List<Corner>();
            foreach (var c in TightCorners(s, sampled))
                if (Mathf.Abs(TurnDeg(sampled, c)) >= HAIRPIN_MIN_TURN_DEG) r.Add(c);
            return r;
        }

        /// <summary>
        /// 柵の内側で取れる一番大きな一定半径の線（m）。turnDeg 曲がるカーブ（中心線の半径 rCenter）で、柵が中心線から offset m のとき、
        /// 入口と出口で外の柵に、頂点で内の柵に触れる円：R = (ro − ri·cos(θ/2)) / (1 − cos(θ/2))、ro = rCenter + offset、ri = rCenter − offset。
        /// 90° の急カーブ（R35、柵 16m）は約 130m（疾走のまま）、176° のヘアピン（R18、柵 13m）は約 32m。
        /// </summary>
        public static float BestLineRadius(float turnDeg, float rCenter, float offset)
        {
            float half = Mathf.Clamp(Mathf.Abs(turnDeg), 1f, 180f) * 0.5f * Mathf.Deg2Rad;
            float ro = rCenter + offset, ri = Mathf.Max(0f, rCenter - offset);
            float c = Mathf.Cos(half);
            return (ro - ri * c) / Mathf.Max(1e-3f, 1f - c);
        }

        /// <summary>全力の合図より手前で、半径 STRAIGHT_R_M より大きい区間が続く最長の長さ（m、course-r2-03）。</summary>
        public static float MaxStraightM(StageDef s, Vector3[] sampled)
        {
            float dash = DashArc(s, GoalArc(s, sampled)), run = 0f, best = 0f;
            for (int i = 0; i < sampled.Length; i++)
            {
                if (i * SAMPLE_SPACING_M > dash) break;
                if (RadiusAt(sampled, i) > STRAIGHT_R_M) { run += SAMPLE_SPACING_M; if (run > best) best = run; }
                else run = 0f;
            }
            return best;
        }

        /// <summary>コースの点の外周からの最小距離（m、course-r2-10）。ゴールの RIM_KEEP_EXEMPT_M 手前から先（切れ込みへ向かう所）は除く。</summary>
        public static float MinRimClearance(StageDef s, Vector3[] sampled)
        {
            float goal = GoalArc(s, sampled), best = float.MaxValue;
            for (int i = 0; i < sampled.Length; i++)
            {
                if (i * SAMPLE_SPACING_M > goal - RIM_KEEP_EXEMPT_M) break;
                Vector3 p = sampled[i];
                best = Mathf.Min(best, Mathf.Min(Mathf.Min(p.x, p.z), Mathf.Min(SIZE - p.x, SIZE - p.z)));
            }
            return best;
        }

        /// <summary>2 つのコースを弧長の割合で n 点に打ち直し、同じ割合の点どうしの距離の平均（m）。形がちがうほど大きい。</summary>
        public static float MeanPathDistance(Vector3[] a, Vector3[] b, int n = 200)
        {
            float la = CoursePath.Length(a), lb = CoursePath.Length(b), sum = 0f;
            for (int k = 0; k < n; k++)
            {
                float f = k / (float)(n - 1);
                sum += WorldLayout.DistXZ(WorldLayout.PointAtArc(a, f * la), WorldLayout.PointAtArc(b, f * lb));
            }
            return sum / n;
        }
    }
}
