using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Shields;

[RegisterComponent, NetworkedComponent]
public sealed partial class KingShieldComponent : Component
{
    [DataField]
    public float MaxDamagePercent = 0.1f;
}
