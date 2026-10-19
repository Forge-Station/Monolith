using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Rest;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoRestSystem))]
public sealed partial class ActionBlockIfRestingComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocId Popup = "rmc-xeno-rest-cant";
}
