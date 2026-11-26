using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WF.Xenonids.Acid;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCAcidCollisionWakeOverrideComponent : Component
{
    [DataField]
    public bool PreviousEnabled = true;
}
