using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Leap;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoLeapDestroyOnPassComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? SpawnPrototype;

    [DataField, AutoNetworkedField]
    public int Amount = 1;
}
