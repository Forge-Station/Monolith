using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Parasite;

[Serializable, NetSerializable]
public sealed partial class AttachParasiteDoAfterEvent : SimpleDoAfterEvent;
