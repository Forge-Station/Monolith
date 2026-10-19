using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.ResinHole;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InResinHoleRangeComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> HoleList = new();
}