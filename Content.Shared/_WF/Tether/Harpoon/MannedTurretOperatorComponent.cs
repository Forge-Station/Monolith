using Robust.Shared.GameStates;

namespace Content.Shared._WF.Tether.Harpoon;

/// <summary>
/// On a crew member buckled into a manned turret. The gun code reads it to send this operator's shoot input to
/// the turret instead of their hands.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MannedTurretOperatorComponent : Component
{
    /// <summary>The turret being manned. Its gun is the operator's gun.</summary>
    [AutoNetworkedField]
    public NetEntity Turret;

    /// <summary>Server only: the controls granted for the duration.</summary>
    public EntityUid? ReelInAction;
    public EntityUid? PayOutAction;
    public EntityUid? ReleaseAction;
}
