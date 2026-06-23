using Robust.Shared.Prototypes;


namespace Content.Shared._Forge.Trade;


[DataDefinition]
public sealed partial class NcArtifactStudySpawnData
{
    [DataField("point", required: true)]
    public ContractPointSelectorPrototype Point { get; private set; } = new();
}

[Prototype("ncArtifactStudyContract")]
public sealed partial class NcArtifactStudyContractPrototype : IPrototype
{
    [DataField("name", required: true)]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("repeatable")]
    public bool Repeatable { get; private set; } = true;

    [DataField("icon")]
    public string Icon { get; private set; } = string.Empty;

    [DataField("artifact", required: true)]
    public string Artifact { get; private set; } = string.Empty;

    [DataField("spawn", required: true)]
    public NcArtifactStudySpawnData Spawn { get; private set; } = new();

    [DataField("reward", required: true)]
    public List<NcSupplyRewardEntry> Reward { get; private set; } = new();

    /// <summary>Optional extension conditions evaluated by registered server-side handlers.</summary>
    [DataField("conditions")]
    public List<ContractConditionDef> Conditions { get; private set; } = new();

    [IdDataField] public string ID { get; private set; } = default!;
}
