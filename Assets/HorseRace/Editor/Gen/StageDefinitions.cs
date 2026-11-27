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
        /// <summary>遺物 4 か所：公式コース長に対する割合と、コース中心からの横オフセット（m、+ が進行方向右）。</summary>
        public float[] relicFractions;
        public float[] relicSide;
        /// <summary>見どころ（spec 5.5）。負の値 = なし。割合は公式コース長に対して（実際は CP の中間に寄せる）。</summary>
        public float bridgeFraction = -1f;     // 荒野：掘った溝に架かる岩橋
        public bool cliffShortcut;             // 荒野：崖下りの近道（CP の連鎖には入らない枝道）
        public float jumpFraction = -1f;       // 砂漠：砂丘越えのジャンプ台
        public float passFraction = -1f;       // 境目：メサの切り通し（道を下げて両脇の地形を盛り、メサの門で挟む）

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

        static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

        public static readonly StageDef[] All =
        {
            new StageDef
            {
                index = 0, label = "荒野", kind = HeightmapGen.KIND_CANYON, seed = 11,
                center = new Vector3(-1200f, 0f, 0f),
                // 峡谷を S 字に 3 往復。行と行の間は 250m 以上離す（平らな道の影響範囲 90m が重ならない）
                controlLocal = new[]
                {
                    P(110, 120), P(380, 165), P(660, 110), P(870, 230), P(820, 440), P(560, 480),
                    P(300, 510), P(130, 660), P(240, 860), P(520, 850), P(760, 880), P(870, 900),
                },
                runoutM = 70f,
                relicFractions = new[] { 0.2f, 0.42f, 0.63f, 0.84f },
                relicSide = new[] { -4f, 14f, -4f, 14f },   // 線の上と線の外（±14m）を交互に：拾うには走る線を選ぶ（system-r1-04）
                bridgeFraction = 0.3f,
                cliffShortcut = true,
            },
            new StageDef
            {
                index = 1, label = "砂漠", kind = HeightmapGen.KIND_DUNES, seed = 23,
                center = new Vector3(0f, 0f, 0f),
                // 南北に 3 往復（砂丘のアップダウン）
                controlLocal = new[]
                {
                    P(130, 870), P(110, 600), P(170, 330), P(310, 130), P(470, 280), P(500, 540),
                    P(540, 820), P(700, 890), P(860, 720), P(840, 450), P(870, 210), P(880, 110),
                },
                runoutM = 70f,
                relicFractions = new[] { 0.18f, 0.4f, 0.62f, 0.85f },
                relicSide = new[] { 4f, -14f, 4f, -14f },
                jumpFraction = 0.5f,
            },
            new StageDef
            {
                index = 2, label = "境目", kind = HeightmapGen.KIND_MESA, seed = 37,
                center = new Vector3(1200f, 0f, 0f),
                // メサの間を抜けて北東の角のゴールの町へ。最後の 3 点は一直線（ゴール前 300m ＋流し 110m の下りの直線、
                // HeightmapGen.FINAL_DROP_M）。最後の 110m は町の通り（流し）
                controlLocal = new[]
                {
                    P(110, 110), P(360, 170), P(620, 120), P(870, 250), P(820, 470), P(560, 460),
                    P(300, 440), P(130, 610), P(230, 820), P(342, 766), P(479, 795), P(880, 880),
                },
                runoutM = 110f,
                relicFractions = new[] { 0.2f, 0.4f, 0.6f, 0.8f },
                relicSide = new[] { -4f, 14f, 4f, -14f },
                passFraction = 0.72f,
            },
        };

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
    }
}
