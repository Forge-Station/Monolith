using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Stamina;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCStaminaDamageOnCollideComponent : Component
{
    [DataField]
    public double Damage;
}
