using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.HiveLeader;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(HiveLeaderSystem))]
public sealed partial class HiveLeaderGranterComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> Leaders = new();

    [DataField, AutoNetworkedField]
    public int MaxLeaders = 4;

    [DataField, AutoNetworkedField]
    public EntProtoId PheromoneRelayId = "XenoLeaderPheromoneRelay";
}
