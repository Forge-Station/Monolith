using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Movement;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCMovementSystem))]
public sealed partial class RMCMobCollisionMassComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Mass = 150;
}
