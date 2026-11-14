using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoConstructionSystem))]
public sealed partial class DeleteXenoResinOnHitComponent : Component;
