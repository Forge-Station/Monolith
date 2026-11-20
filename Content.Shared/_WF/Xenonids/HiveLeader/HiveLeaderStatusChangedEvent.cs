namespace Content.Shared._WF.Xenonids.HiveLeader;

[ByRefEvent]
public readonly record struct HiveLeaderStatusChangedEvent(bool BecameLeader);
