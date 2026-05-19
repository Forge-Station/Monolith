using Robust.Shared.GameStates;

namespace Content.Shared._Forge.BoardingTeleport.Components;

/// <summary>
/// Bluespace drive for a boarding teleport console. Holds acquisition range and lock tolerances.
/// Auto-links to a console on the same grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportEngineComponent : Component
{
    /// <summary>
    /// Maximum distance from the operator grid at which a target can be locked.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float Range = BoardingTeleportConstants.DefaultRange;

    /// <summary>
    /// Target linear speed above which a bluespace lock is rejected.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MaxTargetVelocity = BoardingTeleportConstants.DefaultMaxTargetVelocity;

    /// <summary>
    /// Target angular speed above which a bluespace lock is rejected.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MaxTargetAngularVelocity = BoardingTeleportConstants.DefaultMaxTargetAngularVelocity;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float MinimumTargetMass;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkedConsole;
}
