using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Stomp;

[Serializable, NetSerializable]
public sealed partial class XenoStompDoAfterEvent : SimpleDoAfterEvent;
