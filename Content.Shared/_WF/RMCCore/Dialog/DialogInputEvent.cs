using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Dialog;

[Serializable, NetSerializable]
public abstract record DialogInputEvent(string Message);
