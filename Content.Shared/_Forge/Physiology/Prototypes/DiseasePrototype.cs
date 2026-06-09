using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Physiology.Disease.Symptoms;
using Content.Shared._Forge.Physiology.Disease.Triggers;

namespace Content.Shared._Forge.Physiology.Disease;

[Prototype]
public sealed partial class DiseasePrototype : IPrototype
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
    public List<DiseaseStage> Stages { get; set; } = new();

    [DataField(serverOnly: true)]
    public List<DiseaseTrigger> Triggers { get; set; } = new();
}

[DataDefinition]
public sealed partial class DiseaseStage
{
    [DataField]
    public float MinSeverity { get; set; } = 0f;

    [DataField(serverOnly: true)]
    public List<DiseaseSymptom> Symptoms { get; set; } = new();
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
