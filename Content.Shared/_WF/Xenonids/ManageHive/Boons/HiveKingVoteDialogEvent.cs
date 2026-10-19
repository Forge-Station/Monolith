using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

[Serializable, NetSerializable]
public sealed record HiveKingVoteDialogEvent(NetEntity Cocoon, NetEntity Voted);
