using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Devour;

[Serializable, NetSerializable]
public sealed partial class XenoDevourDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return this;
    }
}
