using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[ByRefEvent]
public record struct XenoBurstPriorityEvent(NetUserId? BurstVictimUserId, NetUserId? InfectorUserId, NetEntity? Hive, NetEntity? SpawnedLarva = null);
