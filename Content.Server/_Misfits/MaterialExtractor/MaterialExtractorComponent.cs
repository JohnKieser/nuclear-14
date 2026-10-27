using Content.Shared.Chemistry.Reagent;
using Content.Shared._Misfits.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Misfits.MaterialExtractor;

[RegisterComponent]
public sealed partial class MaterialExtractorComponent : Component
{
    // Balance fields. Keep gameplay tuning on the entity prototype, not in the system.
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
    [DataField] public int OperatorRadius = 4;
    [DataField] public int PulseIntervalSeconds = 2;
    [DataField] public int FirstWaveMinSeconds = 30;
    [DataField] public int FirstWaveMaxSeconds = 30;
    [DataField] public int WaveMinSeconds = 30;
    [DataField] public int WaveMaxSeconds = 30;
    [DataField] public int WaveWarningSeconds;
    [DataField] public int WaveMinMobCount = 1;
    [DataField] public int WaveMaxMobCount = 3;
    [DataField] public float WaveSpawnMinDistance = 16f;
    [DataField] public float WaveSpawnMaxDistance = 28f;
    [DataField] public int WaveSpawnAttempts = 40;
    [DataField] public float WaveSpawnPvsBuffer = 3f;
    [DataField] public float BossWaveChance = 0.12f;
    [DataField] public int BossWaveMinNumber = 4;
    [DataField] public float EncounterPlayerRadius = 20f;
    [DataField] public float NightStart = 0.75f;
    [DataField] public float NightEnd = 0.965f;

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
    [DataField] public float PoorDepositChance = 0.25f;
    [DataField] public float RichDepositChance = 0.15f;
    [DataField] public float PoorYieldMultiplier = 0.7f;
    [DataField] public float RichYieldMultiplier = 1.4f;
    [DataField] public float LowFuelWarningFraction = 0.25f;
    [DataField] public string FuelSolution = "tank";
    [DataField] public ProtoId<ReagentPrototype> FuelReagent = "WeldingFuel";
    [ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<MobWaveThemePrototype> CurWaveTheme = "MobWave_WildLife";
    public TimeSpan NextPulse;
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextWave;
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan DamagePauseUntil;
    public bool BeaconOn;
    public bool WarningSent;
    public bool LowFuelWarningIssued;
    [ViewVariables(VVAccess.ReadOnly)]
    public bool WasRunning;
    public bool WaveActive;
    [ViewVariables(VVAccess.ReadOnly)]
    public int WavesStartedTotal = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public int ConsecutiveWaves = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public readonly HashSet<EntityUid> ActiveAttackers = [];
    public float YieldMultiplier = 1f;
    public string DepositQuality = "FAIR";
    [ViewVariables(VVAccess.ReadOnly)]
    public float SearchPercent = 0;
    [ViewVariables(VVAccess.ReadOnly)]
    public int Participants = 0;
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
