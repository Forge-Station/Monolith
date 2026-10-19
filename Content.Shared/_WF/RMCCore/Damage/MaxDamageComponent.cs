using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Damage;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCDamageableSystem))]
public sealed partial class MaxDamageComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public FixedPoint2 Max;
}
