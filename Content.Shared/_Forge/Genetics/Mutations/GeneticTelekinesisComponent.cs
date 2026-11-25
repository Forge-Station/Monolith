using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Tether-gun style telekinesis: grab an object, steer it with the mouse, optionally fling it.
/// Also extends interaction range for RMB verbs while the gene is active.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class GeneticTelekinesisComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 5f;

    /// <summary>
    /// Multiplier applied to normal hand throws (Ctrl+Q) while this gene is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ThrowSpeedMultiplier = 1.75f;

    [DataField, AutoNetworkedField]
    public float ThrowRangeBonus = 4f;

    [DataField, AutoNetworkedField]
    public float FlingForce = 30f;

    [DataField, AutoNetworkedField]
    public float FlingDistance = 15f;

    [DataField]
    public EntProtoId Action = "ActionGeneticTelekinesis";

    [DataField]
    public EntProtoId FlingAction = "ActionGeneticTelekinesisFling";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntityUid? FlingActionEntity;

    /// <summary>
    /// True if this system added TetherGun on startup (so we can remove it cleanly).
    /// </summary>
    [DataField]
    public bool AddedTetherGun;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextLosCheck;
}
