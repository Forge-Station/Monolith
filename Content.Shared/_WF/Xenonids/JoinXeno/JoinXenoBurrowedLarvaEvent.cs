using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[Serializable, NetSerializable]
public record JoinXenoBurrowedLarvaEvent(NetEntity Hive);
