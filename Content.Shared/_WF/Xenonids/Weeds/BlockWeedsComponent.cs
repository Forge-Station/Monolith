using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Weeds;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoWeedsSystem))]
public sealed partial class BlockWeedsComponent : Component;
