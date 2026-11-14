using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ManageHiveSystem))]
public sealed partial class HiveBoonKingComponent : Component;
