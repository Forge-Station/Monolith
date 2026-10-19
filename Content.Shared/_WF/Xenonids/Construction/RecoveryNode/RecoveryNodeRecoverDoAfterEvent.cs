using Content.Shared.DoAfter;
using Robust.Shared.Serialization;
namespace Content.Shared._WF.Xenonids.Construction.RecoveryNode;

[Serializable, NetSerializable]
public sealed partial class RecoveryNodeRecoverDoAfterEvent : SimpleDoAfterEvent;
