using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent]
[Access(typeof(HiveBoonSystem))]
public sealed partial class HiveClusterComponent : Component;
