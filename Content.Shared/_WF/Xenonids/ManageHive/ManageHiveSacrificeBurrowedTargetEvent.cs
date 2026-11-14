using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ManageHive;

[ByRefEvent]
[Serializable, NetSerializable]
public sealed record ManageHiveSacrificeBurrowedTargetEvent(NetEntity Target);
