using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Dialog;

[Serializable, NetSerializable]
public enum DialogType
{
    Options,
    Input,
    Confirm,
}
