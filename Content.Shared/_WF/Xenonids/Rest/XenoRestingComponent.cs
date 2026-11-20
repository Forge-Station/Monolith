using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Rest;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoRestSystem))]
public sealed partial class XenoRestingComponent : Component;
