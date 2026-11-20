using Content.Shared._WF.Xenonids.Parasite;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(LarvaQueueSystem), typeof(SharedXenoParasiteSystem))]
public sealed partial class LarvaQueuedComponent : Component;
