using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Eviscerate;

[Serializable, NetSerializable]
public sealed partial class XenoEviscerateDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public int Rage;

    public XenoEviscerateDoAfterEvent(int rage)
    {
        Rage = rage;
    }
}
