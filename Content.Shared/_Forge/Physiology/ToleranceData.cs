using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.Physiology.Tolerance;

[DataDefinition]
public sealed partial class ToleranceData
{
    [DataField]
    public float Value { get; set; } = 0f;

    [DataField]
    public TimeSpan LastChanged { get; set; } = TimeSpan.Zero;

    [DataField]
    public float DecayRate { get; set; } = 0.001f;
}
