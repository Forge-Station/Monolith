using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._WF.Xenonids.Destroy;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoDestroyLeapingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityCoordinates? Target;

    [DataField, AutoNetworkedField]
    public TimeSpan? LeapMoveAt;

    [DataField, AutoNetworkedField]
    public TimeSpan? LeapEndAt;
}
