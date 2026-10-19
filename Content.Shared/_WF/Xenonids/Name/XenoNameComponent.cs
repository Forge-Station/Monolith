using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Name;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedXenoNameSystem))]
public sealed partial class XenoNameComponent : Component
{
    [DataField, AutoNetworkedField]
    public string Rank = string.Empty;

    [DataField, AutoNetworkedField]
    public string Prefix = string.Empty;

    [DataField, AutoNetworkedField]
    public int Number;

    [DataField, AutoNetworkedField]
    public string Postfix = string.Empty;
}
