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
        public Vector3[] courseLocal;    // Terrain ローカル（m、y は 0）
        public Vector3[] courseWorld;    // ワールド（y = 地面）
        public float goalArc;
        public Transform[] startPoints = new Transform[StageDefinitions.LANES];
        public Checkpoint firstCheckpoint;
        public readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
        public Transform finishPen;
        public Transform starterSpot;
        public GoalPresenter goal;
        public Vector3 goalTownCenter;
        public readonly List<SandZone> sandZones = new List<SandZone>();

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

        // 演出
        public SpectatorDirector director;
        public readonly List<NoticeBoardDisplay> noticeBoards = new List<NoticeBoardDisplay>();
        public readonly List<DirectorTag> directorTags = new List<DirectorTag>();
        public TownCrier crier;
        public Starter starter;
        public Podium podium;
        public AudioSource churchBell;
        public RenderTexture broadcastRT;
        public Material broadcastMaterial;
        public Material parchmentMaterial;

        public RaceManager raceManager;
    }
}
