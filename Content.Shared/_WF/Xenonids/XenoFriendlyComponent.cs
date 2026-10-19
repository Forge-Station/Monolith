using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoSystem))]
public sealed partial class XenoFriendlyComponent : Component;
