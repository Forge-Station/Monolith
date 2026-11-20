using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Finesse;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoFinesseSystem))]
public sealed partial class XenoMarkedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan TimeAdded;

    [DataField, AutoNetworkedField]
    public TimeSpan WearOffAt;

    [DataField, AutoNetworkedField]
    public bool IsCriticalTag = false;
}
