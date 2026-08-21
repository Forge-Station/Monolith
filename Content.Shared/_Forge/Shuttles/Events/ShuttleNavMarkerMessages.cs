using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Shuttles.Events;

[Serializable, NetSerializable]
public sealed class AddShuttleNavCoordinateMarkerMessage : BoundUserInterfaceMessage
{
    public float X;
    public float Y;

    public AddShuttleNavCoordinateMarkerMessage(float x, float y)
    {
        X = x;
        Y = y;
    }
}

[Serializable, NetSerializable]
public sealed class AddShuttleNavEntityMarkerMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;

    public AddShuttleNavEntityMarkerMessage(NetEntity target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class RemoveShuttleNavMarkerMessage : BoundUserInterfaceMessage
{
    public int Id;

    public RemoveShuttleNavMarkerMessage(int id)
    {
        Id = id;
    }
}
