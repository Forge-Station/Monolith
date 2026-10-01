using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10/WP3#7: Onyx's own Targeting stack is not vendored; use Shitmed's TargetBodyPart
using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Targeting;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TargetingSnapshotComponent : Component
{
    [DataField, AutoNetworkedField]
    public TargetBodyPart RequestedTarget = TargetBodyPart.Torso; // WOLFGATE(Wolfmed): D9: TargetBodyPart.Chest does not exist

    [DataField, AutoNetworkedField]
    public EntityUid? Shooter;
}
