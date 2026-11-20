using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Construction.Nest;

[Serializable, NetSerializable]
public sealed partial class XenoNestDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public bool AllDirs;
}
