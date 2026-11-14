using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(HiveBoonSystem))]
public sealed partial class HiveKingVoteComponent : Component
{
    [DataField, AutoNetworkedField]
    public Dictionary<NetUserId, int> Votes = new();

    [DataField, AutoNetworkedField]
    public TimeSpan EndAt;
}
