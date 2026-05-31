using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportEngineComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Range = BoardingTeleportConstants.DefaultRange;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MaxTargetVelocity = BoardingTeleportConstants.DefaultMaxTargetVelocity;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MaxTargetAngularVelocity = BoardingTeleportConstants.DefaultMaxTargetAngularVelocity;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MinimumTargetMass;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float JumpCooldown = BoardingTeleportConstants.DefaultEngineJumpCooldown;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool BlockFriendlyTargets = true;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool ExperimentalPhaseShift;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool ExperimentalRiskBoost;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float VelocityToleranceMultiplier = 1f;

    /// <summary>
    /// Engine bluespace tier (1-4). Higher tiers bypass lower ship shield tiers.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int EngineTier;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkedConsole;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public TimeSpan NextJump = TimeSpan.Zero;
}
