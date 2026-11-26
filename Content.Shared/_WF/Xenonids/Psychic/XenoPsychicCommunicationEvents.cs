using Content.Shared._WF.RMCCore.Dialog;
using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Psychic;

public sealed partial class XenoPsychicWhisperActionEvent : EntityTargetActionEvent;

public sealed partial class XenoPsychicRadianceActionEvent : InstantActionEvent;

public sealed partial class XenoGiveOrderActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed record XenoPsychicWhisperInputEvent(NetEntity Action, NetEntity Target, string Message = "")
    : DialogInputEvent(Message);

[Serializable, NetSerializable]
public sealed record XenoPsychicRadianceInputEvent(NetEntity Action, string Message = "")
    : DialogInputEvent(Message);

[Serializable, NetSerializable]
public sealed record XenoGiveOrderInputEvent(NetEntity Action, NetEntity Target, string Message = "")
    : DialogInputEvent(Message);
