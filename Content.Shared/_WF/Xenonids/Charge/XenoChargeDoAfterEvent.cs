using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Charge;

[Serializable, NetSerializable]
public sealed partial class XenoChargeDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    public XenoChargeDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}
