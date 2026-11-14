using Content.Shared._WF.Xenonids.Weeds;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.FloorResin;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoWeedsSystem))]
public sealed partial class ResinSpeedupModifierComponent : Component
{

    [DataField]
    public float HiveSpeedModifier;
}
