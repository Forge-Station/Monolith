using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.Nest;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoNestSystem))]
public sealed partial class XenoNestComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Surface;

    [DataField, AutoNetworkedField]
    public EntityUid? Nested;
}
