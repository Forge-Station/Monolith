using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[RegisterComponent, NetworkedComponent]
[Access(typeof(JoinXenoSystem))]
public sealed partial class JoinXenoCooldownIgnoreComponent : Component;
