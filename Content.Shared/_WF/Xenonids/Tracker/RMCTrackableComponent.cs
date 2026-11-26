using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Tracker;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCTrackableComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Intrinsic = true;
}
