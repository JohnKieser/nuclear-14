using System.Collections.Frozen;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;


namespace Content.Server._Misfits.MaterialExtractor;

[RegisterComponent]
public sealed partial class MaterialExtractorComponent : Component
{


    [DataField] public Dictionary<string, int> OutputWeights = new()
    {
        ["N14IronOre1"] = 20,
        ["N14CopperOre1"] = 18,
        ["N14LeadOre1"] = 14,
        ["SulfurOre1"] = 12,
        ["N14Sand1"] = 12,
        ["Salt1"] = 10,
        ["N14ZincOre1"] = 6,
        ["N14BauxiteOre1"] = 5,
        ["FertilizerOre1"] = 3,
    };

    public Queue<string> LootQueue = new();

    [DataField]
    public TimeSpan PulseIntervalSeconds = new(0);

    [DataField]
    public int SubWavePerWave = 5;

    [DataField]
    public TimeSpan SubWaveMinTime = new(0);
    [DataField]
    public TimeSpan SubWaveMaxTime = new(0);
    [DataField]
    public TimeSpan WaveMinTime = new(0);
    [DataField]
    public TimeSpan WaveMaxTime = new(0);
    [DataField]
    public TimeSpan SubWaveWarningSeconds =  new(900);

    [DataField]
    public int WaveMinMobCount = 1;
    [DataField]
    public int MaxMobWaveCount = 200;
    [DataField]
    public float WaveSpawnMinDistance = 16f;
    [DataField]
    public float WaveSpawnMaxDistance = 28f;

    [DataField]
    public int WaveSpawnAttempts = 40;
    [DataField]
    public float WaveSpawnPvsBuffer = 3f;
    [DataField]
    public float BossWaveChance = 0.12f;
    [DataField]
    public int BossWaveMinNumber = 4;

    [DataField]
    public float EncounterPlayerRadius = 20f;
    // this is day/night cycle in terms of precentage
    // ex) NightStart = 0.75f just means we say night starts when day is 75% complete
    [DataField]
    public float NightStart = 0.75f;
    [DataField]
    public float NightEnd = 0.965f;

    [DataField(required: true),ViewVariables(VVAccess.ReadOnly)]
    public List<ProtoId<MobWaveThemePrototype>> WaveThemes = new();


    [DataField] public Dictionary<string, int> WaveMobWeights = new()
    {
        ["N14MobMaterialExtractorMoleratWave"] = 24,
        ["N14MobMaterialExtractorGeckoWave"] = 18,
        ["N14MobMaterialExtractorMirelurkWave"] = 12,
        ["N14MobMaterialExtractorRadhogWave"] = 10,
        ["N14MobMaterialExtractorFireGeckoWave"] = 8,
        ["N14MobMaterialExtractorNightstalkerCubWave"] = 8,
        ["N14MobMaterialExtractorGiantAntWave"] = 7,
        ["N14MobMaterialExtractorNightstalkerWave"] = 4,
        ["N14MobMaterialExtractorRadMirelurkWave"] = 3,
        ["N14MobMaterialExtractorGiantFireAntWave"] = 3,
        ["N14MobMaterialExtractorRadscorpionWave"] = 3,
    };
    [DataField]
    public float PoorDepositChance = 0.25f;
    [DataField]
    public float RichDepositChance = 0.15f;
    [DataField]
    public float PoorYieldMultiplier = 0.7f;
    [DataField]
    public float RichYieldMultiplier = 1.4f;
    [DataField]
    public float LowFuelWarningThreshold = 0.25f;
    [DataField]
    public string FuelSolution = "tank";
    [DataField]
    public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";

    public FixedPoint2 HealthThreshhold = 200;

    [ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<MobWaveThemePrototype> WaveThemeID = "MobWave_WildLife";

    public MobWaveThemePrototype WaveTheme = null!;

    public FrozenDictionary<EntityUid,List<MapCoordinates>> SpawnChunks = FrozenDictionary<EntityUid, List<MapCoordinates>>.Empty;

    public HashSet<EntityUid> SpawnChunksTotal = new();
    public HashSet<EntityUid> ChunksInPVS = new();
    public HashSet<EntityUid> ChunksOutsidePVS = new();
    public TimeSpan NextPulse = new(0);
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextWave = new(0);
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextSubWave = new(0);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextWaveTransitionWarning = new(0);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextSubWaveWarning = new(0);

    public bool BeaconOn;
    public bool WarningSent;
    public bool LowFuelWarningIssued;

    [ViewVariables(VVAccess.ReadOnly)]
    public int WavesTotal = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public int SubWavesTotal = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public int SubWavesInARow = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public int WavesInARow = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public readonly HashSet<EntityUid> ActiveAttackers = [];

    [ViewVariables(VVAccess.ReadOnly)]
    public int Difficulty = 1;
    [ViewVariables(VVAccess.ReadOnly)]
    public int DifficultySubWaveRNG = 1;
    /// <summary>
    /// Not actual statisical variance as far as I know
    /// just for rng to choose what to multiply difficulty by from min to max
    /// </summary>
    [DataField]
    public float SubWaveMinVariance = 0.90f;
    [DataField]
    public float SubWaveMaxVariance = 1.10f;

    public float YieldMultiplier = 1f;
    public string DepositQuality = "FAIR";
    [ViewVariables(VVAccess.ReadOnly)]
    public int Participants = 0;

    public ExtractorState State = ExtractorState.Init;
}
[Flags]
public enum ExtractorState
{
    Deleting = 0,
    Init = 1 << 0,
    Broken = 1 << 1,
    Inactive = 1 << 2,
    WaveShutdown = 1 << 3,
    Active = 1 << 4,
    AwaitingWaveEnd = 1 << 5,
    OnWaveTransition = 1 << 6,
    QueuedTask = 1 << 7,

    ExtractorOff = Init | Broken | Inactive,
    ExtractorOn = Active | AwaitingWaveEnd | OnWaveTransition | QueuedTask,
    ExtractorBlockUI = Init | Broken,
    ExtractorMobWaveStopped = AwaitingWaveEnd | OnWaveTransition,
    ExtractorBlockUserShutdown = ExtractorState.Active | ExtractorState.AwaitingWaveEnd | ExtractorState.ExtractorOff
}

/// <summary>
/// One faction-safe extractor encounter. A wave selects exactly one theme, then
/// draws every member from that theme so unrelated factions never overlap.
/// </summary>
[Prototype("MobWaveTheme")]
[DataDefinition]
public sealed partial class MobWaveThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;
    [DataField(required: true)] public string Name = string.Empty;
    [DataField(required: true)] public Dictionary<EntProtoId, float> Mobs = new();
    [DataField] public Dictionary<EntProtoId, float> Bosses = new();

    [DataField] public int Weight = 1;
    [DataField] public int MinCount = 1;
    [DataField] public int MaxCount = 3;
    [DataField] public bool NightOnly = false;
    [DataField] public ExpeditionMobFamily Family = ExpeditionMobFamily.Wildlife;
}
