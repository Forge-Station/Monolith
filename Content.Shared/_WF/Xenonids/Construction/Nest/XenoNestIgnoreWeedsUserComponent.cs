using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.Nest;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoNestSystem))]
public sealed partial class XenoNestIgnoreWeedsUserComponent : Component;
