using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.HiveLeader;

[Serializable, NetSerializable]
public sealed record HiveLeaderWatchEvent(NetEntity Leader);
