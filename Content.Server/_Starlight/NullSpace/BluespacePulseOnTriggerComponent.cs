using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.NullSpace;

/// <summary>
/// Trigger a NullSpaceShuntEvent on Trigger.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class BluespacePulseOnTriggerComponent : Component
{
    [DataField]
    public float Radius = 10f;

    [DataField]
    public EntProtoId? Dome;

    [DataField]
    public EntityUid? CurrentDome;
}
