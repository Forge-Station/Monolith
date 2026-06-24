using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.Body.Syndromes;

[Prototype]
public sealed partial class SyndromePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; set; } = string.Empty;

    [DataField]
    public Organ Organ { get; set; } = Organ.Body;

    [DataField]
    public bool Permanent { get; set; } = false;

    [DataField]
    public float Interval { get; set; } = 5f;

    [DataField]
    public float SeverityDecay { get; set; } = 0.5f;

    [DataField]
    public float FlareDelay { get; set; } = 0f;

    [DataField]
    public List<SyndromeStage> Stages { get; set; } = new();

    [DataField(serverOnly: true)]
    public List<SyndromeTrigger> Triggers { get; set; } = new();
}

[DataDefinition]
public sealed partial class SyndromeStage
{
    [DataField]
    public float MinSeverity { get; set; } = 0f;

    [DataField(serverOnly: true)]
    public List<SyndromeSymptom> Symptoms { get; set; } = new();
}

[Flags]
public enum Organ : ushort
{
    None = 0,
    Body = 1 << 0,
    Heart = 1 << 1,
    Liver = 1 << 2,
    Lung = 1 << 3,
    Brain = 1 << 4,
    Kidney = 1 << 5,
    Stomach = 1 << 6,
    Eyes = 1 << 7,
}
