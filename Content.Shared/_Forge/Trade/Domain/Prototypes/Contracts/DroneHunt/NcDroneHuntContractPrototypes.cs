using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Trade;

[DataDefinition]
public sealed partial class NcDroneHuntGridEntry
{
    [DataField("path", required: true)]
    public ResPath Path { get; set; } = new("/Maps/_Mono/Shuttles/World/mpulsar.yml");

    [DataField("weight")]
    public int Weight { get; set; } = 1;
}

[DataDefinition]
public sealed partial class NcDroneHuntSpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; set; } = new();

    [DataField("grids", required: true)]
    public List<NcDroneHuntGridEntry> Grids { get; set; } = new();

    [DataField("minDistance")]
    public float MinDistance { get; set; }

    [DataField("maxDistance")]
    public float MaxDistance { get; set; }

    [DataField("safetyRadius")]
    public float SafetyRadius { get; set; }

    [DataField("placementAttempts")]
    public int PlacementAttempts { get; set; }
}

[DataDefinition]
public sealed partial class NcDroneHuntCompletionData
{
    [DataField("proof", required: true)]
    public string Proof { get; set; } = string.Empty;
}

[Prototype("ncDroneHuntContract")]
public sealed partial class NcDroneHuntContractPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("targetGroup", required: true)]
    public string TargetGroup { get; private set; } = string.Empty;

    [DataField("corePrototypes")]
    public List<string> CorePrototypes { get; private set; } = new();

    [DataField("spawn", required: true)]
    public NcDroneHuntSpawnData Spawn { get; private set; } = new();

    [DataField("completion", required: true)]
    public NcDroneHuntCompletionData Completion { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}
