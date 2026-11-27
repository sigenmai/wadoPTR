using System.Collections.Generic;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>1 ステージぶんの生成結果。</summary>
    public sealed class StageBuild
    {
        public StageDef def;
        public Transform root;
        public Terrain terrain;
        public float[,] heights;
        public float[,] sand;
        /// <summary>道の踏み固めた土の重み（alphamap の解像度）。</summary>
        public float[,] trail;
        /// <summary>遺物の場所（SetPieces.RelicSpots、SetPieceBuilder が近道を決めた後に入る）。</summary>
        public SetPieces.RelicSpot[] relicSpots;
        public Vector3[] courseLocal;    // Terrain ローカル（m、y は 0）
        public Vector3[] courseWorld;    // ワールド（y = 地面）
        public float goalArc;
        public Transform[] startPoints = new Transform[StageDefinitions.LANES];
        public Checkpoint firstCheckpoint;
        public readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        /// <summary>近道の上の双子の CP（同じ番号。本コースの CP の連鎖には入らない）。</summary>
        public readonly List<Checkpoint> twinCheckpoints = new List<Checkpoint>();
        /// <summary>スタートの門（柵、Block）。号砲で消える（Starter）。</summary>
        public GameObject startGate;
        public Transform finishPen;
        public Transform starterSpot;
        /// <summary>空のカウントの位置（スタートラインの 30m 先・12m 上の中心線上、騎手のほうを向く）。</summary>
        public Transform skyCountSpot;
        public GoalPresenter goal;
        public Vector3 goalTownCenter;
        public readonly List<SandZone> sandZones = new List<SandZone>();
        /// <summary>飾りを置かない場所（Terrain ローカル x/z と半径 m）：スターター・橋・近道など。</summary>
        public readonly List<Vector4> exclusions = new List<Vector4>();
        public SetPieces.ShortcutPlan shortcut;
        public float bridgeArc = -1f, jumpArc = -1f;

        public void Exclude(Vector3 world, float radius)
        {
            Vector3 o = def.Origin;
            exclusions.Add(new Vector4(world.x - o.x, 0f, world.z - o.z, radius));
        }

        /// <summary>Terrain ローカル (lx, lz) → ワールド（地面の高さ）。</summary>
        public Vector3 Ground(float lx, float lz)
        {
            Vector3 o = def.Origin;
            return new Vector3(o.x + lx, o.y + WorldLayout.SampleHeight(heights, StageDefinitions.SIZE, HeightmapGen.HEIGHT_M, lx, lz), o.z + lz);
        }

        public Vector3 GroundWorld(Vector3 world) => Ground(world.x - def.Origin.x, world.z - def.Origin.z);
    }

    /// <summary>Build World 全体で集める参照。SceneAssembler が最後に RaceManager などへつなぐ。</summary>
    public sealed class WorldContext
    {
        public Transform root;
        public readonly List<StageBuild> stages = new List<StageBuild>();

        // 乗り物
        public readonly List<MountController> raceMounts = new List<MountController>();
        public readonly List<MountController> practiceMounts = new List<MountController>();
        public readonly List<AbilityCaster> casters = new List<AbilityCaster>();
        public readonly List<RevolverHolder> revolvers = new List<RevolverHolder>();
        public readonly List<Transform> parking = new List<Transform>();

        // 遺物
        public readonly List<RelicPickup> courseRelics = new List<RelicPickup>();
        public readonly List<RelicPickup> spareRelics = new List<RelicPickup>();
        public readonly List<RelicPickup> practiceRelics = new List<RelicPickup>();

        // ロビー
        public Transform lobbyRoot;
        public Terrain lobbyTerrain;
        public Transform lobbySpawn;
        public Vector3 lobbyCenter;
        public readonly List<EntryLedger> ledgers = new List<EntryLedger>();
        public readonly List<RankBoard> rankBoards = new List<RankBoard>();
        public readonly List<PosterCard> posterCards = new List<PosterCard>();
        public readonly List<MountPicker> mountPickers = new List<MountPicker>();
        public StartBell startBell;
        public PracticeArea practiceArea;
        public readonly List<PracticeHorsePost> practicePosts = new List<PracticeHorsePost>();
        public readonly List<Transform> practiceHomes = new List<Transform>();
        public readonly List<PracticeDummy> dummies = new List<PracticeDummy>();
        public MinimalHud hud;
        public PocketWatch watch;
        /// <summary>練習場の往復タイム（ui-r3-07：首の札が計測中の秒数・自己ベストを読む）。</summary>
        public PracticeLap practiceLap;

        // 演出
        public SpectatorDirector director;
        public readonly List<NoticeBoardDisplay> noticeBoards = new List<NoticeBoardDisplay>();
        public readonly List<DirectorTag> directorTags = new List<DirectorTag>();
        /// <summary>予想の札（system-r2-15、電報の掲示板の下、value = スロット）と札の上の紙の文字。</summary>
        public readonly List<PosterCard> pickCards = new List<PosterCard>();
        public TMPro.TextMeshPro pickStatus;
        /// <summary>サルーンの掲示板「あなたの戦績」（system-r3-15）。</summary>
        public TMPro.TextMeshPro recordText;
        public TownCrier crier;
        public Starter starter;
        public Podium podium;
        public AudioSource churchBell;
        public RenderTexture broadcastRT;
        public Material broadcastMaterial;
        public Material parchmentMaterial;

        public RaceManager raceManager;
        public Light sun;                     // 生成した太陽（StageAtmosphere が向き・色を入れ替える）
        /// <summary>Build World が無効にした既存の Directional Light（失敗したら戻す）。</summary>
        public readonly List<GameObject> disabledLights = new List<GameObject>();
    }
}
