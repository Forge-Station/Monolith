using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Administration.RadarTeleport;

/// <summary>
/// An admin ghost clicked a spot on the mass scanner and asks to be moved there. Ignored for anyone else.
/// </summary>
[Serializable, NetSerializable]
public sealed class RadarTeleportRequestEvent : EntityEventArgs
{
    public MapCoordinates Target;

    public RadarTeleportRequestEvent(MapCoordinates target)
    {
        Target = target;
    }
}
