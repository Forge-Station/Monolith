using Content.Shared._WF.RMCCore.Dialog;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ManageHive;

[Serializable, NetSerializable]
public sealed record ManageHiveDevolveMessageEvent(EntProtoId Choice, string Message = "") : DialogInputEvent(Message);
