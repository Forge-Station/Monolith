using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(LarvaQueueSystem))]
public sealed partial class CanBeLarvaQueuedComponent : Component;
