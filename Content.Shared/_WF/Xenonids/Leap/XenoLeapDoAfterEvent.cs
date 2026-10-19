using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Leap;

[Serializable, NetSerializable]
public sealed partial class XenoLeapDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    public XenoLeapDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}
