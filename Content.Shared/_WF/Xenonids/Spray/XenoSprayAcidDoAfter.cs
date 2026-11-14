using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Spray;

[Serializable, NetSerializable]
public sealed partial class XenoSprayAcidDoAfter : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    public XenoSprayAcidDoAfter(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}
