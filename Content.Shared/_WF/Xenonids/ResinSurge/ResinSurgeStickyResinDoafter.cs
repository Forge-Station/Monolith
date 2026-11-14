using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.ResinSurge;

[Serializable, NetSerializable]
public sealed partial class ResinSurgeStickyResinDoafter : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    public ResinSurgeStickyResinDoafter(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}
